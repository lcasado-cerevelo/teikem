using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Constants;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Services;
using Teikem.Infrastructure.Wms;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// 2026-10-08 — Daño declarado en la línea del recibo (checkbox "vinieron unidades dañadas" de la captura): al confirmar, lo recibido entra completo y
/// las unidades dañadas salen de donde aterrizaron hacia cuarentena (o la posición indicada), o se desechan; la tarea de acomodo solo cuenta lo bueno.
/// </summary>
public class ReceiptDamageTests
{
    private sealed record World(WmsFixture F, Warehouse W, WarehouseBin Staging, WarehouseBin Reserve, WarehouseBin Quarantine, Product P);

    private static async Task<World> CreateAsync(bool withQuarantine = true)
    {
        var f = await WmsFixture.CreateAsync(s =>
        {
            s.AddSingleton<IStatusTransitionEffect, WarehouseTaskStatusEffect>();
            s.AddSingleton<IWarehouseTaskHandler, PutawayTaskHandler>();
            s.AddSingleton<IPurchaseOrderReceiving, ReceivingFakePurchaseOrders>();
            s.AddSingleton<IReceiptConfirmationParticipant, ReceivingFakeCrossDock>();
            s.AddSingleton<AsnService>();
            s.AddSingleton<ReceiptService>();
            s.AddReceiptDamageDeps();
        });
        var w = await f.AddWarehouseAsync("W1");
        var stg = await f.AddBinAsync(await f.AddZoneAsync(w, "STG", ZoneTypes.Staging), "STG-01");
        var rsv = await f.AddBinAsync(await f.AddZoneAsync(w, "RSV", ZoneTypes.Reserve), "R-01");
        var q = withQuarantine ? await f.AddBinAsync(await f.AddZoneAsync(w, "CUA", ZoneTypes.Quarantine), "Q-01") : null!;
        var p = await f.AddProductAsync("SKU-1");
        return new World(f, w, stg, rsv, q, p);
    }

    private static ReceiptLineRequest Line(World x, decimal qty, decimal? damaged = null, string? cause = DamageCauses.TransitAccident, string? note = "caja mojada",
        int? damageBin = null, bool? discard = null, int? target = null)
        => new(x.P.PublicId, qty, TargetBinId: target, DamagedQty: damaged, DamageCause: cause, DamageNote: note, DamageBinId: damageBin, DamageDiscard: discard);

    private static Task<ReceiptDetailDto> ConfirmedAsync(World x, ReceiptLineRequest line, string? mode = null)
        => x.F.Get<ReceiptService>().CreateAsync(new ReceiptCreateRequest(WarehousePublicId: x.W.PublicId, Type: ReceiptTypes.Blind,
            StagingBinId: x.Staging.WarehouseBinId, Lines: new[] { line }, Confirm: true, ReceivingMode: mode), default);

    private static async Task<decimal> OnHandAsync(World x, WarehouseBin b) => await x.F.OnHandAsync(x.P.ProductId, b.WarehouseBinId);

    private static async Task<DamageReport> ReportAsync(World x)
        => await x.F.Db.Set<DamageReport>().AsNoTracking().SingleAsync();

    [Fact]
    public async Task Damaged_units_go_to_quarantine_when_the_receipt_is_confirmed_and_only_the_good_ones_are_put_away()
    {
        var x = await CreateAsync();
        await using var f = x.F;
        var receipt = await ConfirmedAsync(x, Line(x, 10m, damaged: 3m));

        Assert.Equal(7m, await OnHandAsync(x, x.Staging));
        Assert.Equal(3m, await OnHandAsync(x, x.Quarantine));
        var line = Assert.Single(receipt.Lines);
        Assert.Equal(3m, line.DamagedQty);
        Assert.Equal(DamageCauses.TransitAccident, line.DamageCauseCode);
        Assert.Equal("caja mojada", line.DamageNote);
        Assert.NotNull(line.DamageReportId);
        Assert.StartsWith("DAN-", line.DamageReportCode);

        var report = await ReportAsync(x);
        Assert.Equal(3m, report.Quantity);
        Assert.Equal(x.Quarantine.WarehouseBinId, report.QuarantineBinId);
        Assert.Equal(x.Staging.WarehouseBinId, report.FromBinId);
        Assert.Equal(receipt.Header.Id, report.ReceiptHeaderId);
        Assert.Equal(new[] { DamageStatuses.Reported, DamageStatuses.Quarantined }, await f.HistoryCodesAsync(EntityTypes.DamageReport, report.DamageReportId));

        var putawayId = f.LookupId(LookupDomains.WarehouseTaskType, WarehouseTaskTypes.Putaway);
        var task = Assert.Single(await f.Db.WarehouseTasks.AsNoTracking().Where(t => t.TaskTypeLookupId == putawayId).ToListAsync());
        Assert.Equal(7m, task.Quantity);
    }

    [Fact]
    public async Task Discarding_right_away_takes_the_units_out_with_a_damage_adjustment()
    {
        var x = await CreateAsync();
        await using var f = x.F;
        var receipt = await ConfirmedAsync(x, Line(x, 10m, damaged: 4m, discard: true));

        Assert.Equal(6m, await OnHandAsync(x, x.Staging));
        Assert.Equal(0m, await OnHandAsync(x, x.Quarantine));
        var report = await ReportAsync(x);
        Assert.Equal(4m, report.Quantity);
        Assert.Null(report.QuarantineBinId);
        Assert.NotNull(report.ResolvedAtUtc);
        Assert.Equal(new[] { DamageStatuses.Reported, DamageStatuses.Discarded }, await f.HistoryCodesAsync(EntityTypes.DamageReport, report.DamageReportId));
        Assert.True(Assert.Single(receipt.Lines).DamageDiscard);
    }

    [Fact]
    public async Task Without_a_quarantine_bin_the_damaged_units_stay_where_they_landed_but_are_not_put_away()
    {
        var x = await CreateAsync(withQuarantine: false);
        await using var f = x.F;
        await ConfirmedAsync(x, Line(x, 10m, damaged: 2m));

        Assert.Equal(10m, await OnHandAsync(x, x.Staging));
        var report = await ReportAsync(x);
        Assert.Equal(x.Staging.WarehouseBinId, report.QuarantineBinId);
        // quedó en una posición de recepción, mezclada con lo bueno: se reserva para que no se despache
        Assert.True(report.IsReserved);
        Assert.Equal(2m, (await x.F.BalanceAsync(x.P.ProductId, x.Staging.WarehouseBinId))!.QtyReserved);
        var putawayId = f.LookupId(LookupDomains.WarehouseTaskType, WarehouseTaskTypes.Putaway);
        Assert.Equal(8m, Assert.Single(await f.Db.WarehouseTasks.AsNoTracking().Where(t => t.TaskTypeLookupId == putawayId).ToListAsync()).Quantity);
    }

    [Fact]
    public async Task The_damage_bin_can_be_any_active_bin_of_the_warehouse()
    {
        var x = await CreateAsync();
        await using var f = x.F;
        await ConfirmedAsync(x, Line(x, 10m, damaged: 1m, damageBin: x.Reserve.WarehouseBinId));

        Assert.Equal(1m, await OnHandAsync(x, x.Reserve));
        Assert.Equal(0m, await OnHandAsync(x, x.Quarantine));
        Assert.Equal(x.Reserve.WarehouseBinId, (await ReportAsync(x)).QuarantineBinId);
    }

    [Fact]
    public async Task Direct_receipt_moves_the_damaged_units_out_of_the_target_bin()
    {
        var x = await CreateAsync();
        await using var f = x.F;
        var receipt = await ConfirmedAsync(x, Line(x, 10m, damaged: 5m, target: x.Reserve.WarehouseBinId), ReceivingModes.Direct);

        Assert.Equal(5m, await OnHandAsync(x, x.Reserve));
        Assert.Equal(5m, await OnHandAsync(x, x.Quarantine));
        Assert.Equal(ReceiptStatuses.Putaway, receipt.Header.StatusCode);
        Assert.Equal(x.Reserve.WarehouseBinId, (await ReportAsync(x)).FromBinId);
    }

    [Theory]
    [InlineData(11, DamageCauses.TransitAccident, "x", "damagedQty", DamageRules.LineDamagedTooMuch)]
    [InlineData(2, null, "x", "damageCause", DamageRules.CauseRequired)]
    [InlineData(2, DamageCauses.Other, "  ", "damageNote", DamageRules.LineOtherNeedsNote)]
    [InlineData(2, "NOPE", "x", "damageCause", null)]
    public async Task Invalid_damage_in_a_line_is_rejected_with_the_field_error(int damaged, string? cause, string? note, string field, string? message)
    {
        var x = await CreateAsync();
        await using var f = x.F;
        var ex = await Assert.ThrowsAsync<ValidationException>(() => f.Get<ReceiptService>().CreateAsync(new ReceiptCreateRequest(WarehousePublicId: x.W.PublicId,
            Type: ReceiptTypes.Blind, Lines: new[] { Line(x, 10m, damaged, cause, note) }), default));
        var key = Assert.Single(ex.Errors.Keys, k => k.EndsWith(field, StringComparison.Ordinal));
        if (message is not null) Assert.Equal(message, ex.Errors[key][0]);
    }

    [Fact]
    public async Task Serial_products_cannot_declare_damage()
    {
        var x = await CreateAsync();
        await using var f = x.F;
        var serial = await f.AddProductAsync("SER-1", TrackingTypes.Serial);
        var ex = await Assert.ThrowsAsync<ValidationException>(() => f.Get<ReceiptService>().CreateAsync(new ReceiptCreateRequest(WarehousePublicId: x.W.PublicId,
            Type: ReceiptTypes.Blind, Lines: new[] { new ReceiptLineRequest(serial.PublicId, 2m, SerialNumbers: new[] { "S1", "S2" }, DamagedQty: 1m,
                DamageCause: DamageCauses.ArrivedDamaged) }), default));
        Assert.Contains(DamageRules.SerialNotSupported, ex.Errors.Values.SelectMany(v => v));
    }

    [Fact]
    public async Task Damage_can_be_edited_on_an_open_receipt_and_cleared()
    {
        var x = await CreateAsync();
        await using var f = x.F;
        var receipts = f.Get<ReceiptService>();
        var open = await receipts.CreateAsync(new ReceiptCreateRequest(WarehousePublicId: x.W.PublicId, Type: ReceiptTypes.Blind, StagingBinId: x.Staging.WarehouseBinId,
            Lines: new[] { Line(x, 10m) }), default);
        var line = Assert.Single(open.Lines);
        Assert.Equal(0m, line.DamagedQty);

        var edited = await receipts.UpdateLineAsync(open.Header.PublicId, line.Id,
            new ReceiptLineUpdateRequest(DamagedQty: 2m, DamageCause: DamageCauses.ArrivedDamaged, DamageNote: "vino así"), default);
        Assert.Equal(2m, Assert.Single(edited.Lines).DamagedQty);

        // bajar lo recibido por debajo de lo dañado no se puede
        var ex = await Assert.ThrowsAsync<ValidationException>(() => receipts.UpdateLineAsync(open.Header.PublicId, line.Id, new ReceiptLineUpdateRequest(ReceivedQty: 1m), default));
        Assert.Contains(DamageRules.LineDamagedTooMuch, ex.Errors.Values.SelectMany(v => v));

        var cleared = await receipts.UpdateLineAsync(open.Header.PublicId, line.Id, new ReceiptLineUpdateRequest(ClearDamage: true), default);
        Assert.Equal(0m, Assert.Single(cleared.Lines).DamagedQty);
        Assert.Null(Assert.Single(cleared.Lines).DamageCauseCode);
    }
}
