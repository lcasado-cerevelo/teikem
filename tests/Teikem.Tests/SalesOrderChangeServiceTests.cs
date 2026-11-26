using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Catalogs;
using Teikem.Domain.Common;
using Teikem.Domain.Constants;
using Teikem.Domain.Orders;
using Teikem.Domain.Sales;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Notifications;
using Teikem.Infrastructure.Orders;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>Lote 249 — eventos de seguridad anotados en memoria (la autorización de crédito de una modificación deja «credit_override»).</summary>
internal sealed class RecordingSecurityWriter : ISecurityEventWriter
{
    public List<(string Type, string Outcome, string Detail)> Events { get; } = new();

    public Task WriteAsync(string eventType, string outcome, int? userId = null, int? tenantId = null, object? detail = null, CancellationToken ct = default)
    {
        Events.Add((eventType, outcome, System.Text.Json.JsonSerializer.Serialize(detail)));
        return Task.CompletedTask;
    }
}

/// <summary>
/// Lote 249 (P5) — lo común de las pruebas del servicio de modificaciones: el servicio armado sobre el mundo del motor (SalesChangeWorld, con el publicador que
/// anota), la autorización del cliente de siempre, los catálogos del lote (si faltan) y lecturas de la modificación y de su historial de estatus.
/// </summary>
internal static class ChangeKit
{
    public const string AuthorizedBy = "María López";
    public const string Reason = "El cliente pidió el cambio por teléfono.";

    /// <summary>El servicio sobre SalesChangeWorld (sin PickGroupService ni almacén de archivos: estas pruebas no los usan).</summary>
    public static SalesOrderChangeService Service(SalesChangeWorld x, ISecurityEventWriter? security = null, StoredFileService? files = null)
        => new(x.F.Db, x.F.Tenant, x.Svc, x.Batches, null!, x.F.Get<StatusService>(), x.F.Get<PermissionService>(), files!, x.F.Get<INumberSequenceService>(),
            x.F.Lookups, TenantClock.Default, security ?? new RecordingSecurityWriter(), x.Publisher);

    /// <summary>El mundo del motor con los catálogos del lote sembrados.</summary>
    public static async Task<SalesChangeWorld> WorldAsync(decimal stock = 20m, bool tax = false)
    {
        var x = await SalesChangeWorld.CreateAsync(stock, tax);
        await x.SeedChangeCatalogsAsync();
        return x;
    }

    /// <summary>Siembra el EntityType, el canal PHONE y el dominio de estatus de la modificación si faltan (mismos ids que SalesChangeWorld.SeedChangeCatalogsAsync).</summary>
    public static async Task SeedAsync(WmsFixture f)
    {
        if (await f.Db.StatusCodes.AsNoTracking().AnyAsync(s => s.Entity == SalesChangeCodes.StatusDomain)) return;
        var pipe = f.LookupId(LookupDomains.StageKind, StageKinds.Pipeline);
        var term = f.LookupId(LookupDomains.StageKind, StageKinds.Terminal);
        f.Db.LookupCodes.AddRange(
            new LookupCode { LookupCodeId = 9001, Entity = LookupDomains.EntityType, InternalCode = SalesChangeCodes.EntityType, LabelJson = "{\"es\":\"Modificación\"}" },
            new LookupCode { LookupCodeId = 9002, Entity = SalesChangeCodes.ChannelDomain, InternalCode = SalesChangeChannels.Phone, LabelJson = "{\"es\":\"Por teléfono\",\"en\":\"By phone\"}" });
        var sort = 1;
        foreach (var (code, kind, initial) in new[] { (SalesOrderChangeStatuses.PendingApproval, pipe, true), (SalesOrderChangeStatuses.Applied, term, false),
                     (SalesOrderChangeStatuses.Rejected, term, false), (SalesOrderChangeStatuses.Withdrawn, term, false) })
            f.Db.StatusCodes.Add(new StatusCode { StatusCodeId = 9100 + sort, Entity = SalesChangeCodes.StatusDomain, InternalCode = code, LabelJson = $"{{\"es\":\"{code}\"}}",
                StageKindLookupId = kind, SortOrder = sort++, IsInitial = initial, IsActive = true });
        await f.Db.SaveChangesAsync();
        f.Lookups.Load(await f.Db.LookupCodes.AsNoTracking().ToListAsync());
        f.Db.ChangeTracker.Clear();
    }

    /// <summary>Una modificación con la autorización de siempre.</summary>
    public static SalesOrderChangeRequest Req(params SalesOrderChangeLineOp[] ops)
        => new(ops, AuthorizedBy, "Compras", null, SalesChangeChannels.Phone, null, Reason);

    public static SalesOrderChangeLineOp Qty(int lineId, decimal qty) => new(ChangeOpKinds.Qty, lineId, Quantity: qty);
    public static SalesOrderChangeLineOp Add(Product p, decimal qty) => new(ChangeOpKinds.Add, ProductPublicId: p.PublicId, Quantity: qty);

    public static SalesOrderAdditionRequest Addition(params (Product P, decimal Qty)[] lines)
        => new(lines.Select(l => new SalesOrderLineRequest(l.P.PublicId, l.Qty)).ToList(), null, AuthorizedBy, null, null, SalesChangeChannels.Phone, null, Reason);

    /// <summary>Los estatus por los que pasó la modificación, en orden (de su historial).</summary>
    public static async Task<List<string>> HistoryAsync(WmsFixture f, Guid changePublicId)
    {
        var id = await f.Db.Set<SalesOrderChange>().AsNoTracking().Where(c => c.PublicId == changePublicId).Select(c => c.SalesOrderChangeId).SingleAsync();
        var typeId = await f.Db.LookupCodes.AsNoTracking().Where(l => l.Entity == LookupDomains.EntityType && l.InternalCode == SalesChangeCodes.EntityType)
            .Select(l => l.LookupCodeId).SingleAsync();
        var codes = await f.Db.StatusCodes.AsNoTracking().Where(s => s.Entity == SalesChangeCodes.StatusDomain).ToDictionaryAsync(s => s.StatusCodeId, s => s.InternalCode);
        return (await f.Db.EntityStatusHistories.AsNoTracking().Where(h => h.EntityTypeLookupId == typeId && h.EntityId == id).OrderBy(h => h.EntityStatusHistoryId)
            .Select(h => h.ToStatusCodeId).ToListAsync()).Select(s => codes[s]).ToList();
    }

    public static Task<SalesOrderChange> RowAsync(WmsFixture f, Guid changePublicId)
        => f.Db.Set<SalesOrderChange>().AsNoTracking().SingleAsync(c => c.PublicId == changePublicId);

    /// <summary>El modo de segunda aprobación y el adjunto obligatorio de la compañía.</summary>
    public static async Task SetTenantAsync(WmsFixture f, string mode, decimal? amount = null, bool attachment = false)
    {
        var t = await f.Db.Tenants.AsTracking().SingleAsync(x => x.TenantId == WmsFixture.TenantId);
        (t.SalesChangeApprovalMode, t.SalesChangeApprovalAmount, t.SalesChangeAttachmentRequired) = (mode, amount, attachment);
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
    }

    public static async Task SetClientAsync(WmsFixture f, int clientId, Action<Teikem.Domain.Clients.Client> set)
    {
        var c = await f.Db.Clients.AsTracking().SingleAsync(x => x.ClientId == clientId);
        set(c);
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
    }
}

// Lote 249 (P5): registrar una modificación (autorización, foto, totales, contadores, historial y aviso), permisos de precio e IVU manual, vista previa sin escribir,
// opciones de edición, la reversa mínima desde el servicio, la ruta vieja PATCH lines como adaptador y los destinatarios del aviso.
public sealed class SalesOrderChangeServiceTests
{
    [Fact]
    public async Task An_applied_change_keeps_the_authorization_the_snapshots_the_totals_the_counters_the_history_and_the_notice()
    {
        await using var x = await ChangeKit.WorldAsync();
        var o = await x.RequestedAsync((x.P1, 3m), (x.P2, 2m));   // 3 × 10 + 2 × 8 = 46
        var svc = ChangeKit.Service(x);

        var dto = await svc.CreateAsync(o.PublicId, ChangeKit.Req(ChangeKit.Qty(o.Lines[0].Id, 5m), ChangeKit.Add(x.P3, 1m)), default);

        var i = dto.Item;
        Assert.Equal((SalesOrderChangeStatuses.Applied, 1, SalesChangeKinds.Modify, false), (i.Status, i.ChangeNo, i.Kind, i.IsPending));
        Assert.Equal((ChangeKit.AuthorizedBy, "Compras", SalesChangeChannels.Phone, "Por teléfono", ChangeKit.Reason),
            (i.AuthorizedByName, i.AuthorizedByTitle, i.Channel, i.ChannelLabel, i.Reason));
        Assert.Equal((46m, 71m, 25m), (i.TotalBefore, i.TotalAfter, i.TotalDelta));   // 5 × 10 + 2 × 8 + 1 × 5
        Assert.Equal((1, 0, 1, 0), (dto.LinesAdded, dto.LinesRemoved, dto.LinesChanged, dto.ReversedBatchCount));
        Assert.False(i.RequiresApproval);
        Assert.NotNull(dto.AppliedAtUtc);
        Assert.Contains("Agregó: MED-3 ×1", i.Summary);
        Assert.Contains("MED-1 (cantidad 3 → 5)", i.Summary);
        Assert.Equal((46m, 71m), (dto.Before!.Total, dto.After!.Total));
        Assert.Single(dto.Diff!.Added);
        Assert.Equal("Sin cambios en el almacén.", dto.Effects!.WarehouseImpact);
        var row = await ChangeKit.RowAsync(x.F, i.PublicId);
        Assert.Equal(64, row.BaseFingerprint.Length);
        Assert.Contains("\"ops\"", row.RequestJson);
        Assert.Equal(new[] { SalesOrderChangeStatuses.PendingApproval, SalesOrderChangeStatuses.Applied }, await ChangeKit.HistoryAsync(x.F, i.PublicId));
        Assert.Equal(SalesChangeCodes.EntityType, typeof(SalesOrderChange).GetCustomAttribute<AuditEntityAttribute>()!.EntityTypeCode);

        var order = await x.Svc.GetAsync(o.PublicId, default);
        Assert.Equal((5m, 5m, 3), (order.Lines[0].QtyOrdered, order.Lines[0].QtyReserved, order.Lines.Count));
        Assert.Equal(1, order.ChangeCount);
        var notice = Assert.Single(x.Publisher.Published, p => p.Event == SalesChangeEventCodes.OrderChanged);
        Assert.Equal($"CHG-{i.PublicId:N}", notice.Key);
        Assert.Equal(("1", ChangeKit.AuthorizedBy + " (Compras)", "Sin cambios en el almacén."), (notice.Values["changeNo"], notice.Values["authorizedBy"], notice.Values["warehouseImpact"]));
        await x.AssertCoherentAsync(o.PublicId);

        // el aviso va por omisión al vendedor y al encargado del almacén; a quien registró solo si se aplicó al aprobar
        var def = new SalesChangeEventProvider().Events.Single(e => e.Code == SalesChangeEventCodes.OrderChanged);
        Assert.Contains(def.ContextRecipients!, r => r.Key == SalesChangeContextRecipients.Seller && r.IsDefault);
        Assert.Contains(def.ContextRecipients!, r => r.Key == SalesChangeContextRecipients.Assignee && r.IsDefault);
    }

    [Fact]
    public async Task A_price_change_needs_sales_price_and_then_takes_todays_origin()
    {
        await using var x = await ChangeKit.WorldAsync();
        var o = await x.RequestedAsync((x.P1, 3m));
        var svc = ChangeKit.Service(x);
        var price = new SalesOrderChangeLineOp(ChangeOpKinds.Price, o.Lines[0].Id, UnitPrice: 7m);

        x.F.SetPermissions(PermissionCatalog.SalesModify, PermissionCatalog.SalesView);
        var denied = await Assert.ThrowsAsync<ForbiddenException>(() => svc.CreateAsync(o.PublicId, ChangeKit.Req(price), default));
        Assert.Equal(SalesRules.PriceNeedsPermission, denied.Message);
        Assert.Empty(await x.F.Db.Set<SalesOrderChange>().AsNoTracking().ToListAsync());

        x.F.Tenant.IsPlatformAdmin = true;
        var dto = await svc.CreateAsync(o.PublicId, ChangeKit.Req(price), default);
        Assert.True(dto.HasPriceChange);
        var line = (await x.Svc.GetAsync(o.PublicId, default)).Lines[0];
        Assert.Equal((7m, 10m, true), (line.UnitPrice, line.ResolvedUnitPrice, line.IsManualPrice));
    }

    [Fact]
    public async Task Manual_tax_needs_sales_tax_and_a_reason()
    {
        // el 400 del motivo y el 403 de sales.tax se revisan antes de aplicar (sin importar el módulo de impuestos)
        await using var x = await ChangeKit.WorldAsync();
        var o = await x.RequestedAsync((x.P1, 3m));
        var svc = ChangeKit.Service(x);

        var noNote = await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(o.PublicId,
            ChangeKit.Req(new SalesOrderChangeLineOp(ChangeOpKinds.Tax, o.Lines[0].Id, TaxPct: 1m)), default));
        Assert.Equal(SalesChangeRules.TaxNoteRequired, noNote.Message);

        x.F.SetPermissions(PermissionCatalog.SalesModify, PermissionCatalog.SalesView);
        await Assert.ThrowsAsync<ForbiddenException>(() => svc.CreateAsync(o.PublicId,
            ChangeKit.Req(new SalesOrderChangeLineOp(ChangeOpKinds.Tax, o.Lines[0].Id, TaxPct: 1m, Note: "Exento por acuerdo")), default));
        Assert.Empty(await x.F.Db.Set<SalesOrderChange>().AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task The_preview_writes_nothing_and_shows_the_projection()
    {
        await using var x = await ChangeKit.WorldAsync();
        var o = await x.RequestedAsync((x.P1, 3m), (x.P2, 2m));
        var svc = ChangeKit.Service(x);

        var p = await svc.PreviewAsync(o.PublicId, ChangeKit.Req(ChangeKit.Qty(o.Lines[0].Id, 5m)), default);

        Assert.Empty(p.Blockers);
        Assert.Equal((46m, 66m, 20m), (p.TotalBefore, p.TotalAfter, p.TotalDelta));
        Assert.Equal("qtyOrdered", Assert.Single(Assert.Single(p.Diff.Changed).Fields).Field);
        Assert.False(p.RequiresApproval);
        Assert.False(p.CreditExceeded);
        Assert.Empty(await x.F.Db.Set<SalesOrderChange>().AsNoTracking().ToListAsync());
        var after = await x.Svc.GetAsync(o.PublicId, default);
        Assert.Equal((3m, 3m), (after.Lines[0].QtyOrdered, after.Lines[0].QtyReserved));
        Assert.Equal((20m, 3m), await x.BalanceOfAsync(x.P1));

        // un bloqueo sale en la vista previa con su texto exacto (no se lanza)
        var blocked = await svc.PreviewAsync(o.PublicId, ChangeKit.Req(new SalesOrderChangeLineOp(ChangeOpKinds.Remove, o.Lines[0].Id),
            new SalesOrderChangeLineOp(ChangeOpKinds.Remove, o.Lines[1].Id)), default);
        Assert.Contains(SalesChangeRules.CannotRemoveAll, blocked.Blockers);
    }

    [Fact]
    public async Task Edit_options_bring_the_line_positions_the_batches_and_the_permissions()
    {
        await using var x = await ChangeKit.WorldAsync();
        var o = await x.RequestedAsync((x.P1, 10m));
        var pick = await x.DispatchAsync(o.PublicId, o.Lines[0].Id, 4m);
        var svc = ChangeKit.Service(x);

        var opts = await svc.EditOptionsAsync(o.PublicId, default);

        Assert.Equal((SalesOrderStatuses.Partial, (string?)null, false, true), (opts.Status, opts.OrderBlocker, opts.MustUseAddition, opts.CanAddition));
        var line = Assert.Single(opts.Lines);
        Assert.Equal((4m, 0m, 0m, 0m, 0m), (line.InCollected, line.InPacked, line.Out, line.MinQuantity, line.MinQuantityWithReopen));
        Assert.Equal(("MED-1", x.P1.PublicId), (line.Sku, line.ProductPublicId));
        var batch = Assert.Single(opts.Batches);
        Assert.Equal((pick.PickBatchNumber, ChangeBatchStages.Reversible), (batch.Number, batch.Stage));
        Assert.Equal(new SalesChangePermissionsDto(true, true, true, true, true, true), opts.Permissions);
        Assert.Equal(SalesChangeApprovalModes.None, opts.Settings.ApprovalMode);
        Assert.Null(opts.Pending);
    }

    [Fact]
    public async Task Lowering_below_what_was_picked_undoes_only_the_newest_needed_pick()
    {
        await using var x = await ChangeKit.WorldAsync();
        var o = await x.RequestedAsync((x.P1, 10m));
        var line = o.Lines[0].Id;
        var a = await x.DispatchAsync(o.PublicId, line, 4m);
        var b = await x.DispatchAsync(o.PublicId, line, 2m);
        var svc = ChangeKit.Service(x);

        var dto = await svc.CreateAsync(o.PublicId, ChangeKit.Req(ChangeKit.Qty(line, 5m)), default);

        Assert.Equal(1, dto.ReversedBatchCount);
        Assert.Equal(new[] { b.PickBatchNumber }, dto.Effects!.ReversedBatches);
        Assert.Contains(b.PickBatchNumber, dto.Effects.WarehouseImpact);
        var after = await x.Svc.GetAsync(o.PublicId, default);
        Assert.Equal(("PARTIAL", 5m, 4m, 1m), (after.Status, after.Lines[0].QtyOrdered, after.Lines[0].QtyShipped, after.Lines[0].QtyReserved));
        var statuses = after.Dispatches!.ToDictionary(d => d.Number, d => d.Status);
        Assert.Equal(("COLLECTED", "CANCELLED"), (statuses[a.PickBatchNumber], statuses[b.PickBatchNumber]));
        await x.AssertCoherentAsync(o.PublicId);
    }

    [Fact]
    public async Task The_old_patch_line_route_is_an_adapter_that_needs_the_authorization_and_sales_modify()
    {
        await using var x = await ChangeKit.WorldAsync();
        var o = await x.RequestedAsync((x.P1, 3m));
        var svc = ChangeKit.Service(x);

        var noAuth = await Assert.ThrowsAsync<ValidationException>(() => svc.ModifyLineAsync(o.PublicId, o.Lines[0].Id, new SalesOrderLineModifyRequest(Quantity: 4m), default));
        Assert.Equal(SalesChangeRules.AuthorizedByNameRequired, noAuth.Message);
        var noReason = await Assert.ThrowsAsync<ValidationException>(() => svc.ModifyLineAsync(o.PublicId, o.Lines[0].Id,
            new SalesOrderLineModifyRequest(Quantity: 4m, AuthorizedByName: "Ana", AuthorizationChannel: "PHONE"), default));
        Assert.Equal(SalesChangeRules.ReasonRequired, noReason.Message);

        x.F.SetPermissions(PermissionCatalog.SalesCreate, PermissionCatalog.SalesView);
        await Assert.ThrowsAsync<ForbiddenException>(() => svc.ModifyLineAsync(o.PublicId, o.Lines[0].Id,
            new SalesOrderLineModifyRequest(4m, null, null, "Ana", null, "PHONE", null, "Pidió una más"), default));

        x.F.Tenant.IsPlatformAdmin = true;
        var order = await svc.ModifyLineAsync(o.PublicId, o.Lines[0].Id, new SalesOrderLineModifyRequest(4m, null, null, "Ana", null, "PHONE", null, "Pidió una más"), default);
        Assert.Equal((4m, 4m), (order.Lines[0].QtyOrdered, order.Lines[0].QtyReserved));
        var change = Assert.Single(await x.F.Db.Set<SalesOrderChange>().AsNoTracking().ToListAsync());
        Assert.Equal(("Ana", "Pidió una más"), (change.AuthorizedByName, change.Reason));
    }

    [Fact]
    public async Task The_notice_content_resolves_its_context_recipients_and_has_a_sample()
    {
        await using var x = await ChangeKit.WorldAsync();
        var o = await x.RequestedAsync((x.P1, 3m));
        var dto = await ChangeKit.Service(x).CreateAsync(o.PublicId, ChangeKit.Req(ChangeKit.Qty(o.Lines[0].Id, 4m)), default);
        var content = new SalesChangeNoticeContent(x.F.Services.GetRequiredService<IServiceScopeFactory>());
        var values = x.Publisher.Published.Single(p => p.Event == SalesChangeEventCodes.OrderChanged).Values
            .ToDictionary(kv => kv.Key, kv => kv.Value ?? string.Empty);

        Assert.Equal(SalesChangeEventCodes.All, content.EventCodes);
        var seller = await content.RecipientsAsync(WmsFixture.TenantId, SalesChangeEventCodes.OrderChanged, SalesChangeContextRecipients.Seller, values, default);
        Assert.Equal(SalesChangeNoticeContent.NoSellerReason, seller.EmptyReason);
        var assignee = await content.RecipientsAsync(WmsFixture.TenantId, SalesChangeEventCodes.OrderChanged, SalesChangeContextRecipients.Assignee, values, default);
        Assert.Equal(SalesChangeNoticeContent.NoAssigneeReason, assignee.EmptyReason);
        var registrant = await content.RecipientsAsync(WmsFixture.TenantId, SalesChangeEventCodes.OrderChanged, SalesChangeContextRecipients.Registrant, values, default);
        Assert.Equal(SalesChangeNoticeContent.AppliedOnRegisterReason, registrant.EmptyReason);
        // de otra compañía: nada (sin decir por qué)
        var other = await content.RecipientsAsync(WmsFixture.OtherTenantId, SalesChangeEventCodes.OrderChanged, SalesChangeContextRecipients.Seller, values, default);
        Assert.Equal((0, (string?)null), (other.People.Count, other.EmptyReason));
        Assert.Null(await content.BuildAsync(new NotificationContentRequest(WmsFixture.TenantId, SalesChangeEventCodes.OrderChanged,
            new Dictionary<string, string>(), NotificationAudience.Staff, "es"), default));

        // la muestra trae todas las variables de los cuatro eventos
        var events = new SalesChangeEventProvider().Events.ToList();
        foreach (var e in events)
        {
            var sample = content.Sample(e.Code, NotificationAudience.Staff, "es");
            Assert.All(e.Variables, v => Assert.True(sample.Values.ContainsKey(v.Name), $"{e.Code}: falta {v.Name}"));
        }
        Assert.NotNull(dto);
    }
}

// Lote 249 (P5): la modificación sobre un empaque (reabrir en la misma llamada, con su permiso) y sobre una orden facturada (solo adición), con el mundo de almacén del
// lote (ReopenWorld: OrderService para dar de baja el envío y PickGroupService).
public sealed class SalesOrderChangePackedAndBilledTests
{
    private static SalesOrderChangeService Service(ReopenWorld w)
        => new(w.Db, w.F.Tenant, w.X.Sales, w.Batches, w.Groups, w.F.Get<StatusService>(), w.F.Get<PermissionService>(), w.F.Get<StoredFileService>(),
            w.F.Get<INumberSequenceService>(), w.F.Lookups, TenantClock.Default, new RecordingSecurityWriter(), w.F.Get<INotificationPublisher>());

    [Fact]
    public async Task A_packed_line_needs_reopen_pack_its_permission_and_then_reopens_and_undoes_in_the_same_call()
    {
        await using var w = await ReopenWorld.CreateAsync();
        await ChangeKit.SeedAsync(w.F);
        var order = await w.OrderAsync(10m);
        var batch = await w.DispatchAsync(order, 10m);
        var shipment = await w.PackAsync(batch.PublicId);
        var svc = Service(w);
        var req = ChangeKit.Req(ChangeKit.Qty(order.Lines[0].Id, 6m));

        var needs = await Assert.ThrowsAsync<StatusRuleException>(() => svc.CreateAsync(order.PublicId, req, default));
        Assert.Equal(SalesChangeRules.LineNeedsReopen("MED", batch.Number), needs.Message);

        w.F.SetPermissions(PermissionCatalog.SalesModify, PermissionCatalog.SalesView);
        var perm = await Assert.ThrowsAsync<ForbiddenException>(() => svc.CreateAsync(order.PublicId, req with { ReopenPack = true }, default));
        Assert.Equal(PickBatchRules.ReopenPermission, perm.Message);
        Assert.Equal(PickBatchStatuses.Packed, w.Code((await w.BatchAsync(batch.PublicId)).StatusCodeId));

        w.F.Tenant.IsPlatformAdmin = true;
        var dto = await svc.CreateAsync(order.PublicId, req with { ReopenPack = true }, default);

        Assert.Equal((true, 1), (dto.ReopenedPack, dto.ReversedBatchCount));
        Assert.Equal(new[] { batch.Number }, dto.Effects!.ReopenedPacks);
        Assert.Equal(new[] { batch.Number }, dto.Effects.ReversedBatches);
        var row = await w.BatchAsync(batch.PublicId);
        Assert.Equal((PickBatchStatuses.Cancelled, false), (w.Code(row.StatusCodeId), row.IsActive));
        Assert.False((await w.Db.TransportOrders.AsNoTracking().SingleAsync(o => o.TransportOrderId == shipment.TransportOrderId)).IsActive);
        var after = await w.X.Sales.GetAsync(order.PublicId, default);
        Assert.Equal((SalesOrderStatuses.Requested, 6m, 0m, 6m), (after.Status, after.Lines[0].QtyOrdered, after.Lines[0].QtyShipped, after.Lines[0].QtyReserved));
        Assert.Single(await w.OutboxAsync(SalesChangeEventCodes.OrderChanged));
        Assert.NotEmpty(await w.OutboxAsync(SalesChangeEventCodes.PackReopened));
    }

    [Fact]
    public async Task A_billed_order_only_takes_an_addition()
    {
        await using var w = await ReopenWorld.CreateAsync();
        await ChangeKit.SeedAsync(w.F);
        var order = await w.OrderAsync(10m);
        await w.DispatchAsync(order, 4m);
        await w.InvoiceLineAsync(order.Lines[0].Id, 4m);
        var svc = Service(w);

        var billed = await Assert.ThrowsAsync<StatusRuleException>(() => svc.CreateAsync(order.PublicId, ChangeKit.Req(ChangeKit.Qty(order.Lines[0].Id, 12m)), default));
        Assert.Equal(SalesChangeRules.Billed, billed.Message);
        var opts = await svc.EditOptionsAsync(order.PublicId, default);
        Assert.Equal((SalesChangeRules.Billed, true), (opts.OrderBlocker, opts.MustUseAddition));
        Assert.Equal("FAC-00012", Assert.Single(opts.Invoices).Number);

        var addition = await svc.CreateAdditionAsync(order.PublicId, ChangeKit.Addition((w.Product, 2m)), default);
        Assert.Equal((SalesOrderChangeStatuses.Applied, SalesChangeKinds.Addition), (addition.Item.Status, addition.Item.Kind));
        Assert.Equal(SalesOrderStatuses.Requested, (await w.X.Sales.GetAsync(addition.Item.AdditionPublicId!.Value, default)).Status);
    }
}
