using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Constants;
using Teikem.Domain.ExitNotices;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Notifications;
using Teikem.Infrastructure.Orders;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Services;
using Teikem.Infrastructure.Wms;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 176 — recolección maestra: varios documentos del MISMO cliente y almacén se recolectan juntos (cada uno con su despacho de siempre) y la maestra los reúne bajo un número.
/// Reglas puras y servicio sobre InMemory (avisos de salida; el empaque con el envío real se prueba en el recorrido contra SQL Server).
/// </summary>
public sealed class PickGroupTests
{
    [Fact]
    public void Selection_needs_two_to_twenty_distinct_known_documents()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        Assert.Equal("documents", PickGroupRules.ValidateSelection(new[] { ((string?)"SALES_ORDER", (Guid?)a) })!.Value.Field);
        Assert.Equal(PickGroupRules.DocumentsRequired, PickGroupRules.ValidateSelection(Array.Empty<(string?, Guid?)>())!.Value.Message);
        Assert.Equal(PickGroupRules.DocumentRepeated, PickGroupRules.ValidateSelection(new[] { ((string?)"sales_order", (Guid?)a), ("SALES_ORDER", a) })!.Value.Message);
        Assert.Equal(PickGroupRules.UnknownKind("ORDER"), PickGroupRules.ValidateSelection(new[] { ((string?)"EXIT_NOTICE", (Guid?)a), ("ORDER", b) })!.Value.Message);
        Assert.Equal("documents[1]", PickGroupRules.ValidateSelection(new[] { ((string?)"EXIT_NOTICE", (Guid?)a), (null, b) })!.Value.Field);
        Assert.Null(PickGroupRules.ValidateSelection(new[] { ((string?)"EXIT_NOTICE", (Guid?)a), ("SALES_ORDER", b) }));
        var many = Enumerable.Range(0, 21).Select(_ => ((string?)"EXIT_NOTICE", (Guid?)Guid.NewGuid())).ToList();
        Assert.Equal(PickGroupRules.TooManyDocuments, PickGroupRules.ValidateSelection(many)!.Value.Message);
    }

    [Fact]
    public void Status_is_derived_from_the_envelope_and_the_active_flag()
    {
        Assert.Equal("COLLECTED", PickGroupRules.Status(true, null));
        Assert.Equal("PACKED", PickGroupRules.Status(true, 7));
        Assert.Equal("CANCELLED", PickGroupRules.Status(false, 7));
    }

    private sealed record World(WmsFixture F, Warehouse W, Teikem.Domain.Clients.Client Owner, Teikem.Domain.Clients.Client Other, Product P, ExitNoticeService Exits, PickGroupService Groups, PickBatchService Batches);

    private static async Task<World> WorldAsync()
    {
        var f = await WmsFixture.CreateAsync(s =>
        {
            s.AddSingleton<ITenantClock>(TenantClock.Default);
            s.AddSingleton<INotificationEventProvider, ExitNoticeEventProvider>();
            s.AddSingleton<NotificationEventCatalog>();
            s.AddSingleton<INotificationPublisher, NotificationPublisher>();
            s.AddSingleton<ExitNoticeService>();
            s.AddSingleton<SyncService>();
            s.AddSingleton(sp => new PickBatchService(sp.GetRequiredService<TeikemDbContext>(), sp.GetRequiredService<ITenantContext>(), sp.GetRequiredService<ILookupCache>(),
                sp.GetRequiredService<StatusService>(), sp.GetRequiredService<INumberSequenceService>(), sp.GetRequiredService<PermissionService>(), sp.GetRequiredService<InventoryLedger>(),
                null!, null, sp.GetRequiredService<ExitNoticeService>()));
            s.AddSingleton(sp => new PickGroupService(sp.GetRequiredService<TeikemDbContext>(), sp.GetRequiredService<ITenantContext>(), sp.GetRequiredService<StatusService>(),
                sp.GetRequiredService<INumberSequenceService>(), sp.GetRequiredService<PermissionService>(), sp.GetRequiredService<ModuleService>(), null!, null!,
                sp.GetRequiredService<ExitNoticeService>(), sp.GetRequiredService<PickBatchService>()));
        });
        var w = await f.AddWarehouseAsync("W1");
        var pick = await f.AddBinAsync(await f.AddZoneAsync(w, "PZ", ZoneTypes.Picking), "P-01");
        var pick2 = await f.AddBinAsync(await f.AddZoneAsync(w, "PZ2", ZoneTypes.Picking), "P-02");
        var owner = await f.AddClientAsync("ASICS");
        var other = await f.AddClientAsync("NIKE");
        var p = await f.AddProductAsync("MED", ownerClientId: owner.ClientId);
        var po = await f.AddProductAsync("NK", ownerClientId: other.ClientId);
        await f.PostAsync(new InventoryPosting(InventoryTxnTypes.Receipt, p.ProductId, 6m, ToWarehouseId: w.WarehouseId, ToBinId: pick.WarehouseBinId));
        await f.PostAsync(new InventoryPosting(InventoryTxnTypes.Receipt, p.ProductId, 4m, ToWarehouseId: w.WarehouseId, ToBinId: pick2.WarehouseBinId));
        await f.PostAsync(new InventoryPosting(InventoryTxnTypes.Receipt, po.ProductId, 5m, ToWarehouseId: w.WarehouseId, ToBinId: pick.WarehouseBinId));
        return new World(f, w, owner, other, p, f.Get<ExitNoticeService>(), f.Get<PickGroupService>(), f.Get<PickBatchService>());
    }

    private static async Task<ExitNoticeDto> NoticeAsync(World x, Teikem.Domain.Clients.Client client, Product p, decimal qty)
        => await x.Exits.CreateAsync(new ExitNoticeRequest(x.W.PublicId, client.PublicId, Reference: "R", Destination: "D", Lines: new[] { new ExitNoticeLineRequest(p.PublicId, qty) }), default);

    private static decimal OnHand(World x, Product p) => x.F.Db.StockBalances.AsNoTracking().Where(b => b.ProductId == p.ProductId).ToList().Sum(b => b.QtyOnHand);

    private static PickGroupCreateRequest Req(params ExitNoticeDto[] notices)
        => new(notices.Select(n => new PickGroupDocumentRequest(PickGroupRules.KindExitNotice, n.PublicId)).ToList());

    [Fact]
    public async Task Collecting_two_notices_together_gives_one_master_with_all_the_movements_and_each_notice_keeps_its_own_dispatch()
    {
        var x = await WorldAsync();
        await using var _ = x.F;
        var a = await NoticeAsync(x, x.Owner, x.P, 4m);
        var b = await NoticeAsync(x, x.Owner, x.P, 5m);   // 6 en una posición + 4 en otra: el segundo sale de las dos

        var g = await x.Groups.CreateAsync(Req(a, b), default);

        Assert.Equal("COLLECTED", g.StatusCode);
        Assert.StartsWith("EMP-", g.Number);
        Assert.True(g.CanPack);
        Assert.True(g.CanDelete);
        Assert.Equal(("ASICS", 9m), (g.ClientName.Replace("Cliente ", ""), g.TotalQty));
        Assert.Equal(2, g.Documents.Count);
        Assert.Equal(new[] { a.Number, b.Number }, g.Documents.Select(d => d.Number));
        Assert.Equal(new[] { 4m, 5m }, g.Documents.Select(d => d.Quantity));
        // todos los movimientos, con su posición y el documento al que pertenecen
        Assert.Equal(9m, g.Movements.Sum(m => m.Quantity));
        Assert.Contains(g.Movements, m => m.DocumentNumber == b.Number && m.BinCode == "P-02");
        Assert.All(g.Movements, m => Assert.False(string.IsNullOrEmpty(m.BinCode)));
        Assert.Equal(1m, OnHand(x, x.P));
        // cada recolección sigue siendo de su aviso, pero ya no se empaca ni se borra suelta si está empacada
        var members = await x.F.Db.Set<PickBatch>().AsNoTracking().Where(m => m.PickGroupId != null).ToListAsync();
        Assert.Equal(2, members.Count);
        var dto = await x.Batches.GetAsync(members[0].PublicId, default);
        Assert.False(dto.CanPack);
        Assert.Equal(g.Number, dto.GroupNumber);
        var ex = await Assert.ThrowsAsync<StatusRuleException>(() => x.Batches.PackAsync(members[0].PublicId, new PickBatchPackRequest(null), default));
        Assert.Equal(PickGroupRules.InGroup(members[0].Number, g.Number), ex.Message);

        var page = await x.Groups.ListAsync(new PickGroupQuery(Search: a.Number), default);
        Assert.Equal(g.PublicId, Assert.Single(page.Items).PublicId);
        Assert.Empty(page.Items[0].Movements);
        Assert.Empty((await x.Groups.ListAsync(new PickGroupQuery(Status: "PACKED"), default)).Items);
    }

    [Fact]
    public async Task Documents_of_different_clients_or_fewer_than_two_are_rejected_without_collecting()
    {
        var x = await WorldAsync();
        await using var _ = x.F;
        var a = await NoticeAsync(x, x.Owner, x.P, 2m);
        var n = await NoticeAsync(x, x.Other, (await x.F.Db.Set<Product>().AsNoTracking().FirstAsync(p => p.Sku == "NK")), 2m);

        var mixed = await Assert.ThrowsAsync<ValidationException>(() => x.Groups.CreateAsync(Req(a, n), default));
        Assert.Contains("clientes distintos", mixed.Errors!["documents[1]"][0]);
        var one = await Assert.ThrowsAsync<ValidationException>(() => x.Groups.CreateAsync(Req(a), default));
        Assert.Equal(PickGroupRules.DocumentsRequired, one.Errors!["documents"][0]);
        var missing = await Assert.ThrowsAsync<NotFoundException>(() => x.Groups.CreateAsync(new PickGroupCreateRequest(new[]
        {
            new PickGroupDocumentRequest(PickGroupRules.KindExitNotice, a.PublicId), new PickGroupDocumentRequest(PickGroupRules.KindExitNotice, Guid.NewGuid()),
        }), default));
        Assert.NotNull(missing);
        Assert.Empty(await x.F.Db.Set<PickGroup>().ToListAsync());
        Assert.Empty(await x.F.Db.Set<PickBatch>().ToListAsync());
    }

    [Fact]
    public async Task Deleting_the_master_undoes_every_dispatch_and_the_notices_get_their_reservation_back()
    {
        var x = await WorldAsync();
        await using var _ = x.F;
        var a = await NoticeAsync(x, x.Owner, x.P, 3m);
        var b = await NoticeAsync(x, x.Owner, x.P, 3m);
        var g = await x.Groups.CreateAsync(Req(a, b), default);
        Assert.Equal(4m, OnHand(x, x.P));

        await x.Groups.DeleteAsync(g.PublicId, new PickBatchDeleteRequest("Me equivoqué"), default);

        Assert.Equal(10m, OnHand(x, x.P));
        var after = await x.Groups.GetAsync(g.PublicId, default);
        Assert.Equal(("CANCELLED", false, false), (after.StatusCode, after.IsActive, after.CanPack));
        Assert.All(await x.F.Db.Set<PickBatch>().AsNoTracking().ToListAsync(), m => Assert.False(m.IsActive));
        var notice = await x.Exits.GetAsync(a.PublicId, default);
        Assert.Equal(ExitNoticeStatuses.Requested, notice.StatusCode);
        Assert.Equal(3m, notice.Lines.Single().QtyReserved);
        await Assert.ThrowsAsync<NotFoundException>(() => x.Groups.DeleteAsync(g.PublicId, null, default));
    }
}
