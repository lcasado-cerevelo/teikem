using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Teikem.Api.Auth;
using Teikem.Api.Controllers;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Constants;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Analytics;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Services;
using Teikem.Infrastructure.Wms;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// 2026-10-08 — Daños sobre el ledger REAL: un daño del almacén va a cuarentena (transferencia) o se desecha (ajuste negativo DAMAGE); uno de un
/// recibo entra a cuarentena (ajuste positivo DAMAGE) o se desecha de una vez sin tocar el inventario; lo que está en cuarentena se desecha después
/// o se recupera a una posición de guardado.
/// </summary>
public class DamageServiceTests
{
    private sealed record World(WmsFixture F, Warehouse W, WarehouseBin Pick, WarehouseBin Quarantine, WarehouseBin Reserve, WarehouseBin Staging, Product P);

    private static async Task<World> SeedAsync()
    {
        var f = await WmsFixture.CreateAsync(s =>
        {
            s.AddSingleton<ITenantClock>(TenantClock.Default);
            s.AddSingleton<InventoryReadService>();
            s.AddSingleton<InventoryAdjustmentService>();
            s.AddSingleton<DamageService>();
        });
        var w = await f.AddWarehouseAsync("ALM-01");
        var pick = await f.AddBinAsync(await f.AddZoneAsync(w, "PCK", ZoneTypes.Picking), "A-01");
        var quarantine = await f.AddBinAsync(await f.AddZoneAsync(w, "CUA", ZoneTypes.Quarantine), "Q-01");
        var reserve = await f.AddBinAsync(await f.AddZoneAsync(w, "RSV", ZoneTypes.Reserve), "R-01");
        var staging = await f.AddBinAsync(await f.AddZoneAsync(w, "STG", ZoneTypes.Staging), "S-01");
        var p = await f.AddProductAsync("SKU-1");
        await f.PostAsync(new InventoryPosting(InventoryTxnTypes.Receipt, p.ProductId, 10m, ToWarehouseId: w.WarehouseId, ToBinId: pick.WarehouseBinId));
        return new World(f, w, pick, quarantine, reserve, staging, p);
    }

    private static DamageReportRequest Req(World w, string origin, string disposition, decimal qty = 4m, int? fromBin = null, Guid? receipt = null, string cause = DamageCauses.WarehouseAccident,
        int? quarantineBin = null, Product? product = null)
        => new(origin, w.W.PublicId, (product ?? w.P).PublicId, null, null, fromBin, qty, cause, disposition, quarantineBin, receipt, "se cayó la tarima");

    private static Task<decimal> OnHandAsync(World w, WarehouseBin b) => w.F.OnHandAsync(w.P.ProductId, b.WarehouseBinId);

    [Fact]
    public async Task Warehouse_damage_to_quarantine_transfers_it_and_keeps_the_report()
    {
        var w = await SeedAsync();
        var dto = await w.F.Get<DamageService>().ReportAsync(Req(w, DamageOrigins.Warehouse, DamageDispositions.Quarantine, fromBin: w.Pick.WarehouseBinId), default);

        Assert.Equal(DamageStatuses.Quarantined, dto.StatusCode);
        Assert.Equal("DAN-00001", dto.Code);
        Assert.Equal("A-01", dto.FromBinCode);
        Assert.Equal("Q-01", dto.QuarantineBinCode);   // la única posición de cuarentena del almacén, sin indicarla
        Assert.Equal(6m, await OnHandAsync(w, w.Pick));
        Assert.Equal(4m, await OnHandAsync(w, w.Quarantine));
        var txn = (await w.F.TransactionsAsync()).Last();
        Assert.Equal(InventoryTxnTypes.Transfer, await TxnTypeAsync(w, txn));
        Assert.Contains("DAN-00001", txn.Notes);
        Assert.Equal(new[] { DamageStatuses.Reported, DamageStatuses.Quarantined }, await w.F.HistoryCodesAsync(EntityTypes.DamageReport, dto.Id));
    }

    [Fact]
    public async Task Warehouse_damage_can_be_discarded_right_away_with_a_damage_adjustment()
    {
        var w = await SeedAsync();
        var dto = await w.F.Get<DamageService>().ReportAsync(Req(w, DamageOrigins.Warehouse, DamageDispositions.Discard, fromBin: w.Pick.WarehouseBinId), default);

        Assert.Equal(DamageStatuses.Discarded, dto.StatusCode);
        Assert.Null(dto.QuarantineBinCode);
        Assert.NotNull(dto.ResolvedAtUtc);
        Assert.Equal(6m, await OnHandAsync(w, w.Pick));
        var txn = (await w.F.TransactionsAsync()).Last();
        Assert.Equal(InventoryTxnTypes.Adjustment, await TxnTypeAsync(w, txn));
        Assert.Equal(-4m, txn.Quantity);
        Assert.Equal(w.F.LookupId(LookupDomains.AdjustmentReason, AdjustmentReasons.Damage), txn.ReasonLookupId);
    }

    [Fact]
    public async Task Receipt_damage_to_quarantine_enters_the_quarantine_bin_and_links_the_receipt()
    {
        var w = await SeedAsync();
        var receipt = await w.F.AddReceiptAsync(w.W, ReceiptStatuses.Received);
        var before = (await w.F.TransactionsAsync()).Count;
        var dto = await w.F.Get<DamageService>().ReportAsync(Req(w, DamageOrigins.Receipt, DamageDispositions.Quarantine, qty: 3m, receipt: receipt.PublicId, cause: DamageCauses.ArrivedDamaged), default);

        Assert.Equal(DamageOrigins.Receipt, dto.OriginCode);
        Assert.Equal(receipt.Number, dto.ReceiptNumber);
        Assert.Null(dto.FromBinCode);
        Assert.Equal(3m, await OnHandAsync(w, w.Quarantine));
        Assert.Equal(10m, await OnHandAsync(w, w.Pick));   // lo bueno no se toca
        Assert.Equal(before + 1, (await w.F.TransactionsAsync()).Count);
    }

    [Fact]
    public async Task Receipt_damage_discarded_at_once_never_enters_inventory()
    {
        var w = await SeedAsync();
        var receipt = await w.F.AddReceiptAsync(w.W, ReceiptStatuses.Received);
        var before = (await w.F.TransactionsAsync()).Count;
        var dto = await w.F.Get<DamageService>().ReportAsync(Req(w, DamageOrigins.Receipt, DamageDispositions.Discard, qty: 5m, receipt: receipt.PublicId, cause: DamageCauses.TransitAccident), default);

        Assert.Equal(DamageStatuses.Discarded, dto.StatusCode);
        Assert.Equal(before, (await w.F.TransactionsAsync()).Count);   // ningún movimiento: nunca entró
        Assert.Equal(10m, await OnHandAsync(w, w.Pick));
        Assert.Equal(DamageCauses.TransitAccident, dto.CauseCode);
    }

    [Fact]
    public async Task Quarantined_damage_can_be_discarded_later()
    {
        var w = await SeedAsync();
        var svc = w.F.Get<DamageService>();
        var d = await svc.ReportAsync(Req(w, DamageOrigins.Warehouse, DamageDispositions.Quarantine, fromBin: w.Pick.WarehouseBinId), default);

        var done = await svc.DiscardAsync(d.Id, new DamageResolveRequest(Notes: "se tiró"), default);

        Assert.Equal(DamageStatuses.Discarded, done.StatusCode);
        Assert.Equal("se tiró", done.ResolutionNotes);
        Assert.Equal(0m, await OnHandAsync(w, w.Quarantine));
        Assert.Equal(6m, await OnHandAsync(w, w.Pick));
        // ya está resuelto: no se vuelve a resolver
        var again = await Assert.ThrowsAsync<StatusRuleException>(() => svc.DiscardAsync(d.Id, null, default));
        Assert.Equal(DamageRules.ResolveOnlyQuarantined, again.Message);
    }

    [Fact]
    public async Task Quarantined_damage_can_be_recovered_to_a_storage_bin_but_not_to_quarantine_or_staging()
    {
        var w = await SeedAsync();
        var svc = w.F.Get<DamageService>();
        var d = await svc.ReportAsync(Req(w, DamageOrigins.Warehouse, DamageDispositions.Quarantine, fromBin: w.Pick.WarehouseBinId), default);

        await Assert.ThrowsAsync<ValidationException>(() => svc.RecoverAsync(d.Id, new DamageResolveRequest(), default));
        var staging = await Assert.ThrowsAsync<ValidationException>(() => svc.RecoverAsync(d.Id, new DamageResolveRequest(w.Staging.WarehouseBinId), default));
        Assert.Contains("S-01", staging.Message);
        await Assert.ThrowsAsync<ValidationException>(() => svc.RecoverAsync(d.Id, new DamageResolveRequest(w.Quarantine.WarehouseBinId), default));

        var done = await svc.RecoverAsync(d.Id, new DamageResolveRequest(w.Reserve.WarehouseBinId), default);
        Assert.Equal(DamageStatuses.Recovered, done.StatusCode);
        Assert.Equal(4m, await OnHandAsync(w, w.Reserve));
        Assert.Equal(0m, await OnHandAsync(w, w.Quarantine));
    }

    [Fact]
    public async Task Validation_errors_are_reported_by_field_and_nothing_moves()
    {
        var w = await SeedAsync();
        var svc = w.F.Get<DamageService>();
        var bad = await Assert.ThrowsAsync<ValidationException>(() => svc.ReportAsync(
            new DamageReportRequest("RECEIPT", w.W.PublicId, null, null, null, null, 0m, null, "OTRO"), default));
        Assert.Contains("quantity", bad.Errors.Keys);
        Assert.Contains("cause", bad.Errors.Keys);
        Assert.Contains("disposition", bad.Errors.Keys);
        Assert.Contains("productPublicId", bad.Errors.Keys);
        Assert.Contains("receiptPublicId", bad.Errors.Keys);

        var noBin = await Assert.ThrowsAsync<ValidationException>(() => svc.ReportAsync(Req(w, DamageOrigins.Warehouse, DamageDispositions.Quarantine), default));
        Assert.Equal(DamageRules.FromBinRequired, noBin.Errors["fromBinId"][0]);
        Assert.Equal(10m, await OnHandAsync(w, w.Pick));
    }

    [Fact]
    public async Task Without_a_quarantine_bin_or_enough_stock_it_fails()
    {
        var w = await SeedAsync();
        var svc = w.F.Get<DamageService>();
        // (en SQL Server el reporte y el movimiento van en una transacción y se revierten juntos; el InMemory de pruebas no revierte)
        await Assert.ThrowsAsync<InsufficientStockException>(() => svc.ReportAsync(Req(w, DamageOrigins.Warehouse, DamageDispositions.Discard, qty: 99m, fromBin: w.Pick.WarehouseBinId), default));
        Assert.Equal(10m, await OnHandAsync(w, w.Pick));

        // un almacén sin zona de cuarentena
        var other = await w.F.AddWarehouseAsync("ALM-02");
        var bin = await w.F.AddBinAsync(await w.F.AddZoneAsync(other, "PCK2", ZoneTypes.Picking), "B-01");
        await w.F.PostAsync(new InventoryPosting(InventoryTxnTypes.Receipt, w.P.ProductId, 5m, ToWarehouseId: other.WarehouseId, ToBinId: bin.WarehouseBinId));
        var req = new DamageReportRequest(DamageOrigins.Warehouse, other.PublicId, w.P.PublicId, null, null, bin.WarehouseBinId, 1m, DamageCauses.Other, DamageDispositions.Quarantine);
        var ex = await Assert.ThrowsAsync<StatusRuleException>(() => svc.ReportAsync(req, default));
        Assert.Equal(DamageRules.NoQuarantineBin, ex.Message);
    }

    [Fact]
    public async Task Serial_products_are_not_reported_here()
    {
        var w = await SeedAsync();
        var serial = await w.F.AddProductAsync("SER-1", TrackingTypes.Serial);
        var ex = await Assert.ThrowsAsync<StatusRuleException>(() => w.F.Get<DamageService>().ReportAsync(Req(w, DamageOrigins.Warehouse, DamageDispositions.Discard, fromBin: w.Pick.WarehouseBinId, product: serial), default));
        Assert.Equal(DamageRules.SerialNotSupported, ex.Message);
    }

    [Fact]
    public async Task The_list_filters_by_status_origin_and_search()
    {
        var w = await SeedAsync();
        var svc = w.F.Get<DamageService>();
        var receipt = await w.F.AddReceiptAsync(w.W, ReceiptStatuses.Received);
        await svc.ReportAsync(Req(w, DamageOrigins.Warehouse, DamageDispositions.Quarantine, qty: 1m, fromBin: w.Pick.WarehouseBinId), default);
        await svc.ReportAsync(Req(w, DamageOrigins.Receipt, DamageDispositions.Discard, qty: 2m, receipt: receipt.PublicId), default);

        Assert.Equal(2, (await svc.ListAsync(null, null, null, 0, 50, default)).Total);
        Assert.Equal(1, (await svc.ListAsync("QUARANTINED", null, null, 0, 50, default)).Total);
        Assert.Equal(1, (await svc.ListAsync(null, "RECEIPT", null, 0, 50, default)).Total);
        Assert.Equal(1, (await svc.ListAsync(null, null, "DAN-00002", 0, 50, default)).Total);
        Assert.Equal(2, (await svc.ListAsync(null, null, "SKU-1", 0, 50, default)).Total);
        Assert.Equal("DAN-00002", (await svc.ListAsync(null, null, null, 0, 50, default)).Items[0].Code);   // el más reciente primero
    }

    [Fact]
    public async Task The_data_source_exposes_the_reports_for_views_and_charts()
    {
        var w = await SeedAsync();
        await w.F.Get<DamageService>().ReportAsync(Req(w, DamageOrigins.Warehouse, DamageDispositions.Quarantine, qty: 2m, fromBin: w.Pick.WarehouseBinId), default);
        var source = new DamageDataSource(w.F.Db, w.F.Tenant, w.F.Lookups);

        Assert.Equal(EntityTypes.DamageReport, source.Key);
        Assert.Equal("ReportedAtUtc", source.DateField);
        var row = Assert.Single(await source.LoadAsync(new DataQuery(), default));
        Assert.Equal("DAN-00001", row["Code"]);
        Assert.Equal("SKU-1", row["Sku"]);
        Assert.Equal(2m, row["Quantity"]);
        Assert.Equal("QUARANTINED", row["StatusCode"]);
        Assert.Equal("WAREHOUSE_ACCIDENT", row["CauseCode"]);
        Assert.All(source.Fields, f => Assert.True(row.ContainsKey(f.Key), f.Key));
    }

    [Fact]
    public void Every_damage_endpoint_asks_for_warehouse_damage_under_the_wms_module()
    {
        var type = typeof(DamageReportsController);
        Assert.Equal(ModuleKeys.WmsLotSerial, type.GetCustomAttribute<RequireModuleAttribute>()!.ModuleKey);
        var actions = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).ToList();
        Assert.Equal(5, actions.Count);   // List, Get, Report, Discard, Recover
        foreach (var a in actions)
        {
            var policy = a.GetCustomAttributes<AuthorizeAttribute>().Single().Policy;
            Assert.Equal(RequirePermissionAttribute.Prefix + PermissionCatalog.WarehouseDamage, policy);
        }
    }

    private static async Task<string> TxnTypeAsync(World w, InventoryTransaction txn)
        => (await w.F.Lookups.GetAsync(txn.TxnTypeLookupId))!.InternalCode;
}
