using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Constants;
using Teikem.Domain.Orders;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Orders;
using Teikem.Infrastructure.Persistence.Scripts;
using Teikem.Infrastructure.Services;
using Teikem.Infrastructure.Wms;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// 2026-10-11 — Despacho manual (DMA-#####): salida de inventario SIN entrega sobre la misma recolección (PickBatch con
/// ManualIssueReasonId). Reglas puras (motivo obligatorio, nota ≤ 500, número DMA con su propio contador, no empacable, nota del
/// Kárdex, filtro kind), permiso warehouse.issue (catálogo, plantillas y espejo en logistica-db-update.sql con propagación a los
/// roles ya clonados) y el servicio sobre InMemory (WmsFixture): ISSUE con nota 'DMA-… · motivo', 409 sin efecto parcial,
/// 422 al empacar, eliminar con reversa y warehouse.issue, lista por tipo.
/// </summary>
public sealed class ManualIssueTests
{
    // ================================================================ reglas puras

    [Fact]
    public void Reason_is_required_and_normalized()
    {
        Assert.Equal((null, "Indique el motivo del despacho manual."), PickBatchRules.NormalizeReason(null));
        Assert.Equal((null, PickBatchRules.ManualReasonRequired), PickBatchRules.NormalizeReason("   "));
        Assert.Equal(("SAMPLE", null), PickBatchRules.NormalizeReason(" sample "));
        Assert.Equal("El motivo XYZ no existe o está inactivo.", PickBatchRules.ManualReasonUnknown("XYZ"));
    }

    [Fact]
    public void Note_is_optional_and_at_most_500()
    {
        Assert.Equal((null, null), PickBatchRules.NormalizeNote(null));
        Assert.Equal((null, null), PickBatchRules.NormalizeNote("  "));
        Assert.Equal(("muestra para la feria", null), PickBatchRules.NormalizeNote("  muestra para la feria "));
        Assert.Equal((new string('x', 500), null), PickBatchRules.NormalizeNote(new string('x', 500)));
        Assert.Equal((null, "La nota admite como máximo 500 caracteres."), PickBatchRules.NormalizeNote(new string('x', 501)));
    }

    [Fact]
    public void Number_is_dma_with_its_own_counter()
    {
        Assert.Equal("MANUALISSUE", NumberKinds.ManualIssue);
        Assert.True(NumberingRules.IsKnownKind(NumberKinds.ManualIssue));
        Assert.Equal("DMA-#####", WmsNumbering.PatternFor(NumberKinds.ManualIssue));
        Assert.Equal("DMA-00012", WmsNumbering.Format(NumberKinds.ManualIssue, 12));
        Assert.NotEqual(NumberKinds.PackBatch, NumberKinds.ManualIssue);
        Assert.True(KardexRules.IsManualIssueNumber("DMA-00012"));
        Assert.False(KardexRules.IsManualIssueNumber("EMP-00012"));
    }

    [Fact]
    public void A_manual_issue_is_never_packable()
    {
        Assert.True(PickBatchRules.CanPack(PickBatchStatuses.Collected, true, isManual: false));
        Assert.False(PickBatchRules.CanPack(PickBatchStatuses.Collected, true, isManual: true));
        Assert.Equal("El despacho manual DMA-00003 no se empaca: es una salida de inventario sin entrega.", PickBatchRules.ManualNotPackable("DMA-00003"));
        // Se elimina como una recolección COLLECTED (con reversa).
        Assert.True(PickBatchRules.CanDelete(PickBatchStatuses.Collected, false, false));
        Assert.True(PickBatchRules.IsManual(5));
        Assert.False(PickBatchRules.IsManual(null));
    }

    [Fact]
    public void Movement_note_and_kardex_reference()
    {
        // Decisión del dueño: 'DMA-00012 · {motivo}' (la nota libre queda en la ficha, no en el Kárdex).
        Assert.Equal("DMA-00012 · Muestra", PickBatchRules.ManualIssueMovementNote("DMA-00012", "Muestra"));
        Assert.Equal("DMA-00012 · Uso interno", PickBatchRules.ManualIssueMovementNote("DMA-00012", " Uso interno "));
        Assert.Equal("DMA-00012", PickBatchRules.ManualIssueMovementNote("DMA-00012", ""));
        var longLabel = PickBatchRules.ManualIssueMovementNote("DMA-00012", new string('n', 400));
        Assert.Equal(300, longLabel.Length);
        Assert.StartsWith("DMA-00012 · nnn", longLabel);

        Assert.Equal("Despacho manual DMA-00012", KardexRules.RefLabel(EntityTypes.PickBatch, 7, "DMA-00012"));
        Assert.Equal("Manual issue DMA-00012", KardexRules.RefLabel(EntityTypes.PickBatch, 7, "DMA-00012", "en"));
        Assert.Equal("Recolección EMP-00001", KardexRules.RefLabel(EntityTypes.PickBatch, 7, "EMP-00001"));
    }

    [Fact]
    public void Kind_filter_is_manual_pack_or_all()
    {
        Assert.Equal(("ALL", null), PickBatchRules.NormalizeKind(null));
        Assert.Equal(("MANUAL", null), PickBatchRules.NormalizeKind(" manual "));
        Assert.Equal(("PACK", null), PickBatchRules.NormalizeKind("Pack"));
        Assert.Equal(("ALL", "El tipo debe ser MANUAL, PACK o ALL."), PickBatchRules.NormalizeKind("OTRO"));
    }

    [Fact]
    public void Search_finds_the_reason_and_the_note()
    {
        var row = new PickBatchListRow(1, "DMA-00001", null, null, null, null, new[] { "SKU-1" }, "Muestra", "para la feria");
        Assert.True(PickBatchRules.MatchesSearch(row, "muestra"));
        Assert.True(PickBatchRules.MatchesSearch(row, "FERIA"));
        Assert.True(PickBatchRules.MatchesSearch(row, "DMA-0"));
        Assert.False(PickBatchRules.MatchesSearch(row, "EMP"));
    }

    [Fact]
    public void Reason_catalog_has_the_five_system_codes()
    {
        Assert.Equal("ManualIssueReason", LookupDomains.ManualIssueReason);
        Assert.Equal(new[] { "SAMPLE", "INTERNAL_USE", "CUSTOMER_PICKUP", "SALE", "OTHER" }, ManualIssueReasons.All);
    }

    // ================================================================ permiso y espejo SQL

    private static string UpdateSql() => File.ReadAllText(Path.Combine(DatabaseInitializer.ResolveRepoRoot(null), "Diseño", "logistica-db-update.sql"));

    [Fact]
    public void Permission_warehouse_issue_is_in_the_catalog_and_the_warehouse_templates()
    {
        Assert.Equal("warehouse.issue", PermissionCatalog.WarehouseIssue);
        var p = Assert.Single(PermissionCatalog.All, x => x.Code == "warehouse.issue");
        Assert.Equal(("WAREHOUSE", "Despacho manual (salida sin entrega)"), (p.Category, p.LabelEs));
        var t = PermissionCatalog.RoleTemplates;
        Assert.Contains(PermissionCatalog.WarehouseIssue, t["WarehouseOperator"]);
        Assert.Contains(PermissionCatalog.WarehouseIssue, t["TenantAdmin"]);
        foreach (var role in new[] { "Dispatcher", "Billing", "Driver", "ReadOnly" })
            Assert.DoesNotContain(PermissionCatalog.WarehouseIssue, t[role]);
        // El rol clonado lo recibe del PermissionSeeder solo si es nuevo en su corrida (por eso el SQL lo propaga también).
        Assert.Equal(new[] { "warehouse.issue" }, PermissionCatalog.CodesToPropagate("WarehouseOperator",
            new HashSet<string> { "warehouse.issue" }, new HashSet<string>()));
    }

    [Fact]
    public void Update_sql_mirrors_the_structure_catalog_and_permission_and_propagates_to_cloned_roles()
    {
        var sql = UpdateSql();
        var at = sql.IndexOf("2026-10-11 — Despacho manual", StringComparison.Ordinal);
        Assert.True(at > 0, "Falta la sección 2026-10-11 del despacho manual en logistica-db-update.sql.");
        var section = sql[at..];
        Assert.Contains("IF COL_LENGTH('dbo.PickBatch', 'ManualIssueReasonId') IS NULL", section);
        Assert.Contains("IF COL_LENGTH('dbo.PickBatch', 'Note') IS NULL", section);
        Assert.Contains("Note NVARCHAR(500) NULL", section);
        Assert.Contains("FK_PickBatch_ManualIssueReason", section);
        Assert.Contains("definition NOT LIKE '%MANUALISSUE%'", section);
        Assert.Contains("'RENTALRETURN','MANUALISSUE'", section);
        foreach (var code in ManualIssueReasons.All) Assert.Contains($"(N'{code}',", section);
        Assert.Contains("N'ManualIssueReason'", section);
        Assert.Contains("IF NOT EXISTS (SELECT 1 FROM dbo.Permission WHERE Code = 'warehouse.issue')", section);
        // Plantillas Y roles ya clonados (sin filtrar por TenantId), solo agregando.
        Assert.Contains("JOIN dbo.Permission p ON p.Code = 'warehouse.issue'\nWHERE r.Name IN ('WarehouseOperator', 'TenantAdmin')\n  AND NOT EXISTS",
            section.Replace("\r\n", "\n"));
        Assert.DoesNotContain("DELETE", section, StringComparison.OrdinalIgnoreCase);
    }

    // ================================================================ servicio (InMemory)

    private static Task<WmsFixture> FixtureAsync() => WmsFixture.CreateAsync(s => s.AddSingleton(sp => new PickBatchService(
        sp.GetRequiredService<Teikem.Infrastructure.Persistence.TeikemDbContext>(), sp.GetRequiredService<ITenantContext>(),
        sp.GetRequiredService<ILookupCache>(), sp.GetRequiredService<StatusService>(), sp.GetRequiredService<INumberSequenceService>(),
        sp.GetRequiredService<PermissionService>(), sp.GetRequiredService<InventoryLedger>(), null!)));

    private sealed record World(WmsFixture F, Warehouse W, WarehouseBin Bin, Product P);

    private static async Task<World> WorldAsync(decimal onHand = 5m, int? ownerClientId = null)
    {
        var f = await FixtureAsync();
        var w = await f.AddWarehouseAsync("W1");
        var bin = await f.AddBinAsync(await f.AddZoneAsync(w, "PCK", ZoneTypes.Picking), "P-01");
        var p = await f.AddProductAsync("PN", ownerClientId: ownerClientId, purchaseCost: 2m);
        await f.PostAsync(new InventoryPosting(InventoryTxnTypes.Receipt, p.ProductId, onHand, ToWarehouseId: w.WarehouseId, ToBinId: bin.WarehouseBinId));
        return new World(f, w, bin, p);
    }

    private static ManualIssueCreateRequest Req(World x, decimal qty, string? reason = ManualIssueReasons.Sample, string? note = null)
        => new(x.W.PublicId, new[] { new PickBatchLineRequest(x.P.PublicId, qty) }, reason, note);

    [Fact]
    public async Task Manual_issue_takes_stock_out_with_its_own_number_reason_note_and_kardex_note()
    {
        var x = await WorldAsync();
        await using var _ = x.F;
        var svc = x.F.Get<PickBatchService>();
        var before = (await x.F.TransactionsAsync()).Count;

        var dto = await svc.ManualIssueAsync(Req(x, 2m, " sample ", "para la feria"), default);

        Assert.Equal("DMA-00001", dto.Number);
        Assert.True(dto.IsManual);
        Assert.Equal(PickBatchStatuses.Collected, dto.StatusCode);
        Assert.Equal(ManualIssueReasons.Sample, dto.ReasonCode);
        Assert.Equal("SAMPLE", dto.ReasonLabel);                    // el fixture siembra {"es":"SAMPLE"}
        Assert.Equal("para la feria", dto.Note);
        Assert.False(dto.CanPack);
        Assert.True(dto.CanDelete);
        Assert.Null(dto.OrderPublicId);
        Assert.Null(dto.OwnerClientName);                           // inventario propio
        Assert.Equal(4m, dto.TotalCost);                            // 2 × 2 (costo congelado)
        Assert.Equal(3m, await x.F.OnHandAsync(x.P.ProductId, x.Bin.WarehouseBinId));

        var issue = Assert.Single((await x.F.TransactionsAsync()).Skip(before));
        Assert.Equal(x.F.LookupId(LookupDomains.InventoryTxnType, InventoryTxnTypes.Issue), issue.TxnTypeLookupId);
        Assert.Equal(-2m, issue.Quantity);
        Assert.Equal(x.F.LookupId(LookupDomains.EntityType, EntityTypes.PickBatch), issue.RefEntityLookupId);
        Assert.Equal(dto.Id, issue.RefId);
        Assert.Equal("DMA-00001 · SAMPLE", issue.Notes);

        var row = await x.F.Db.Set<PickBatch>().AsNoTracking().SingleAsync(b => b.PickBatchId == dto.Id);
        Assert.Equal(x.F.LookupId(LookupDomains.ManualIssueReason, ManualIssueReasons.Sample), row.ManualIssueReasonId);

        // El contador DMA es propio: una recolección normal sigue con EMP-00001 y el siguiente manual es DMA-00002.
        var emp = await svc.CollectAsync(new PickBatchCreateRequest(x.W.PublicId, new[] { new PickBatchLineRequest(x.P.PublicId, 1m) }), default);
        Assert.Equal("EMP-00001", emp.Number);
        Assert.False(emp.IsManual);
        Assert.Null(emp.ReasonCode);
        Assert.Equal("DMA-00002", (await svc.ManualIssueAsync(Req(x, 1m, ManualIssueReasons.InternalUse), default)).Number);
    }

    [Fact]
    public async Task Missing_or_unknown_reason_and_long_note_are_400_without_effects()
    {
        var x = await WorldAsync();
        await using var _ = x.F;
        var svc = x.F.Get<PickBatchService>();
        var before = (await x.F.TransactionsAsync()).Count;

        var missing = await Assert.ThrowsAsync<ValidationException>(() => svc.ManualIssueAsync(Req(x, 1m, reason: null), default));
        Assert.Equal("Indique el motivo del despacho manual.", Assert.Single(missing.Errors!["reasonCode"]));
        Assert.Equal("Indique el motivo del despacho manual.", missing.Message);

        var both = await Assert.ThrowsAsync<ValidationException>(() => svc.ManualIssueAsync(Req(x, 1m, reason: "", note: new string('x', 501)), default));
        Assert.Equal(PickBatchRules.ManualReasonRequired, Assert.Single(both.Errors!["reasonCode"]));
        Assert.Equal(PickBatchRules.ManualNoteTooLong, Assert.Single(both.Errors!["note"]));

        var unknown = await Assert.ThrowsAsync<ValidationException>(() => svc.ManualIssueAsync(Req(x, 1m, reason: "regalo"), default));
        Assert.Equal("El motivo REGALO no existe o está inactivo.", Assert.Single(unknown.Errors!["reasonCode"]));

        // Un motivo desactivado ya no se acepta.
        var other = await x.F.Db.LookupCodes.SingleAsync(l => l.Entity == LookupDomains.ManualIssueReason && l.InternalCode == ManualIssueReasons.Other);
        other.IsActive = false;
        await x.F.Db.SaveChangesAsync();
        x.F.Db.ChangeTracker.Clear();
        var inactive = await Assert.ThrowsAsync<ValidationException>(() => svc.ManualIssueAsync(Req(x, 1m, reason: "OTHER"), default));
        Assert.Equal(PickBatchRules.ManualReasonUnknown("OTHER"), Assert.Single(inactive.Errors!["reasonCode"]));

        Assert.Empty(await x.F.Db.Set<PickBatch>().AsNoTracking().ToListAsync());
        Assert.Equal(before, (await x.F.TransactionsAsync()).Count);
        Assert.Equal(5m, await x.F.OnHandAsync(x.P.ProductId, x.Bin.WarehouseBinId));
    }

    [Fact]
    public async Task Insufficient_stock_is_409_without_partial_effect_and_does_not_consume_the_number()
    {
        var x = await WorldAsync(onHand: 3m);
        await using var _ = x.F;
        var svc = x.F.Get<PickBatchService>();
        var before = (await x.F.TransactionsAsync()).Count;

        var ex = await Assert.ThrowsAnyAsync<TeikemException>(() => svc.ManualIssueAsync(Req(x, 4m), default));
        Assert.Equal(409, ex.StatusCode);
        Assert.Equal("insufficient_stock", ex.Code);
        Assert.Equal("Inventario insuficiente de PN en W1: disponible 3, solicitado 4.", ex.Message);
        Assert.Empty(await x.F.Db.Set<PickBatch>().AsNoTracking().ToListAsync());
        Assert.Equal(before, (await x.F.TransactionsAsync()).Count);
        Assert.Equal(3m, await x.F.OnHandAsync(x.P.ProductId, x.Bin.WarehouseBinId));

        Assert.Equal("DMA-00001", (await svc.ManualIssueAsync(Req(x, 3m), default)).Number);
    }

    [Fact]
    public async Task Two_owners_is_400_with_the_manual_message()
    {
        var x = await WorldAsync();
        await using var _ = x.F;
        var client = await x.F.AddClientAsync("C1");
        var p3 = await x.F.AddProductAsync("P3", ownerClientId: client.ClientId);
        await x.F.PostAsync(new InventoryPosting(InventoryTxnTypes.Receipt, p3.ProductId, 5m, ToWarehouseId: x.W.WarehouseId, ToBinId: x.Bin.WarehouseBinId));

        var ex = await Assert.ThrowsAsync<ValidationException>(() => x.F.Get<PickBatchService>().ManualIssueAsync(new ManualIssueCreateRequest(x.W.PublicId,
            new[] { new PickBatchLineRequest(x.P.PublicId, 1m), new PickBatchLineRequest(p3.PublicId, 1m) }, ManualIssueReasons.Sale), default));
        Assert.Equal("Un despacho manual solo puede tener productos de un mismo dueño.", Assert.Single(ex.Errors!["lines"]));
    }

    [Fact]
    public async Task Client_inventory_shows_its_owner()
    {
        var x = await WorldAsync();
        await using var _ = x.F;
        var client = await x.F.AddClientAsync("C9");
        var p3 = await x.F.AddProductAsync("P3", ownerClientId: client.ClientId);
        await x.F.PostAsync(new InventoryPosting(InventoryTxnTypes.Receipt, p3.ProductId, 5m, ToWarehouseId: x.W.WarehouseId, ToBinId: x.Bin.WarehouseBinId));

        var dto = await x.F.Get<PickBatchService>().ManualIssueAsync(new ManualIssueCreateRequest(x.W.PublicId,
            new[] { new PickBatchLineRequest(p3.PublicId, 2m) }, ManualIssueReasons.CustomerPickup), default);
        Assert.Equal("Cliente C9", dto.OwnerClientName);
        Assert.Null(dto.ClientName);   // sin orden
    }

    [Fact]
    public async Task Packing_a_manual_issue_is_422_before_asking_for_the_order()
    {
        var x = await WorldAsync();
        await using var _ = x.F;
        var svc = x.F.Get<PickBatchService>();
        var dto = await svc.ManualIssueAsync(Req(x, 1m), default);
        x.F.SetPermissions(PermissionCatalog.InventoryView, PermissionCatalog.WarehousePick);   // sin orders.create: igual 422

        var ex = await Assert.ThrowsAsync<StatusRuleException>(() => svc.PackAsync(dto.PublicId, new PickBatchPackRequest(null), default));
        Assert.Equal("El despacho manual DMA-00001 no se empaca: es una salida de inventario sin entrega.", ex.Message);
        var after = await svc.GetAsync(dto.PublicId, default);
        Assert.Equal(PickBatchStatuses.Collected, after.StatusCode);
        Assert.Null(after.OrderPublicId);
    }

    [Fact]
    public async Task Deleting_a_manual_issue_reverses_the_stock_and_requires_warehouse_issue()
    {
        var x = await WorldAsync();
        await using var _ = x.F;
        var svc = x.F.Get<PickBatchService>();
        var dto = await svc.ManualIssueAsync(Req(x, 2m), default);
        Assert.Equal(3m, await x.F.OnHandAsync(x.P.ProductId, x.Bin.WarehouseBinId));

        // Con warehouse.pick pero sin warehouse.issue (desde /pick-batches) → 403 y nada cambia.
        x.F.SetPermissions(PermissionCatalog.InventoryView, PermissionCatalog.WarehousePick);
        var denied = await Assert.ThrowsAsync<ForbiddenException>(() => svc.DeleteAsync(dto.PublicId, new PickBatchDeleteRequest(), default));
        Assert.Equal("Falta el permiso 'warehouse.issue'.", denied.Message);
        Assert.Equal(3m, await x.F.OnHandAsync(x.P.ProductId, x.Bin.WarehouseBinId));

        // Con warehouse.issue (sin orders.cancel): reversa a la posición original y CANCELLED.
        x.F.SetPermissions(PermissionCatalog.InventoryView, PermissionCatalog.WarehouseIssue);
        await svc.DeleteManualIssueAsync(dto.PublicId, new PickBatchDeleteRequest("me equivoqué"), default);
        Assert.Equal(5m, await x.F.OnHandAsync(x.P.ProductId, x.Bin.WarehouseBinId));
        x.F.Db.ChangeTracker.Clear();
        var gone = await svc.GetManualIssueAsync(dto.PublicId, default);
        Assert.Equal(PickBatchStatuses.Cancelled, gone.StatusCode);
        Assert.False(gone.IsActive);
        Assert.NotNull(Assert.Single(gone.Lines).ReversalTxnId);
        var reversal = (await x.F.TransactionsAsync()).Last();
        Assert.Equal(x.F.LookupId(LookupDomains.AdjustmentReason, AdjustmentReasons.PickBatchReversal), reversal.ReasonLookupId);

        // Ya eliminado: 404 con la etiqueta del despacho manual (no 'Recolección no encontrada.').
        var again = await Assert.ThrowsAsync<NotFoundException>(() => svc.DeleteManualIssueAsync(dto.PublicId, null, default));
        Assert.Equal("Despacho manual no encontrado.", again.Message);
    }

    [Fact]
    public async Task Manual_routes_do_not_reach_pick_batches_and_list_filters_by_kind()
    {
        var x = await WorldAsync();
        await using var _ = x.F;
        var svc = x.F.Get<PickBatchService>();
        var emp = await svc.CollectAsync(new PickBatchCreateRequest(x.W.PublicId, new[] { new PickBatchLineRequest(x.P.PublicId, 1m) }), default);
        var dma = await svc.ManualIssueAsync(Req(x, 1m, note: "feria"), default);

        var notFound = await Assert.ThrowsAsync<NotFoundException>(() => svc.GetManualIssueAsync(emp.PublicId, default));
        Assert.Equal("Despacho manual no encontrado.", notFound.Message);
        await Assert.ThrowsAsync<NotFoundException>(() => svc.DeleteManualIssueAsync(emp.PublicId, null, default));
        Assert.Equal(dma.Number, (await svc.GetManualIssueAsync(dma.PublicId, default)).Number);

        async Task<string[]> Numbers(string? kind, string? search = null)
            => (await svc.ListAsync(new PickBatchQuery(Kind: kind, Search: search), default)).Items.Select(i => i.Number).OrderBy(n => n).ToArray();
        Assert.Equal(new[] { "DMA-00001", "EMP-00001" }, await Numbers(null));
        Assert.Equal(new[] { "DMA-00001", "EMP-00001" }, await Numbers("ALL"));
        Assert.Equal(new[] { "DMA-00001" }, await Numbers("manual"));
        Assert.Equal(new[] { "EMP-00001" }, await Numbers("PACK"));
        Assert.Equal(new[] { "DMA-00001" }, await Numbers(null, "feria"));      // la búsqueda encuentra la nota
        Assert.Equal(new[] { "DMA-00001" }, await Numbers(null, "sample"));     // y el motivo
        var bad = await Assert.ThrowsAsync<ValidationException>(() => svc.ListAsync(new PickBatchQuery(Kind: "X"), default));
        Assert.Equal(PickBatchRules.KindInvalid, Assert.Single(bad.Errors!["kind"]));
    }
}
