using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Constants;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 16 — posición destino sugerida (D3, D4, D6): GET target-suggestions (con cupo, lo reservado por otras líneas y las
/// que no caben al final), "Usar posiciones sugeridas" (solo donde cabe, acumulando, sin sugerencia cuando no hay espacio),
/// espacio libre del destino en la línea (TargetFreeQty) y la cuarentena primero en devoluciones.
/// </summary>
public sealed class ReceiptTargetSuggestionTests
{
    /// <summary>W1 del fixture en modo directo: R-01 (201) con cupo 5, R-02 (202) sin cupo, Q-01 (203, cuarentena) y X-01 (204, cruce).</summary>
    private static async Task<ReceivingFixture> FixtureAsync(int? r2Capacity = null)
    {
        var f = await ReceivingFixture.CreateAsync();
        f.Db.Set<WarehouseZone>().AddRange(
            new WarehouseZone { WarehouseZoneId = 13, WarehouseId = f.WarehouseId, Code = "QUA", Name = "Cuarentena", ZoneTypeLookupId = f.LookupId(LookupDomains.ZoneType, ZoneTypes.Quarantine), IsActive = true },
            new WarehouseZone { WarehouseZoneId = 14, WarehouseId = f.WarehouseId, Code = "XD", Name = "Cruce", ZoneTypeLookupId = f.LookupId(LookupDomains.ZoneType, ZoneTypes.CrossDock), IsActive = true });
        f.Db.Set<WarehouseBin>().AddRange(
            new WarehouseBin { WarehouseBinId = 202, WarehouseZoneId = 12, WarehouseId = f.WarehouseId, Code = "R-02", IsActive = true, MaxCapacityQty = r2Capacity },
            new WarehouseBin { WarehouseBinId = 203, WarehouseZoneId = 13, WarehouseId = f.WarehouseId, Code = "Q-01", IsActive = true },
            new WarehouseBin { WarehouseBinId = 204, WarehouseZoneId = 14, WarehouseId = f.WarehouseId, Code = "X-01", IsActive = true });
        await f.Db.SaveChangesAsync();
        (await f.Db.Set<WarehouseBin>().SingleAsync(b => b.WarehouseBinId == f.ReserveBinId)).MaxCapacityQty = 5;
        (await f.Db.Set<Warehouse>().SingleAsync(w => w.WarehouseId == f.WarehouseId)).ReceivingModeLookupId =
            f.LookupId(LookupDomains.ReceivingMode, ReceivingModes.Direct);
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        return f;
    }

    private static ReceiptCreateRequest Blind(string type, params (Guid Product, decimal Qty, int? Target)[] lines)
        => new(Type: type, Lines: lines.Select(l => new ReceiptLineRequest(l.Product, l.Qty, TargetBinId: l.Target)).ToList());

    [Fact]
    public async Task Suggestions_list_the_bins_that_fit_first_and_mark_the_ones_over_capacity()
    {
        await using var f = await FixtureAsync();
        var receipts = f.Get<ReceiptService>();
        var created = await receipts.CreateAsync(Blind(ReceiptTypes.Blind, (f.ProductNonePublicId, 8m, null)), default);
        var line = Assert.Single(created.Lines);

        var s = await receipts.SuggestTargetsAsync(created.Header.PublicId, line.Id, null, default);
        Assert.Equal(new[] { ("R-02", true), ("R-01", false) }, s.Select(x => (x.BinCode, x.Fits)));   // ni STG, ni X-01, ni cuarentena
        var r1 = s.Single(x => x.BinCode == "R-01");
        Assert.Equal((5, 0m, 0m, 5m), (r1.MaxCapacityQty!.Value, r1.QtyOnHand, r1.ClaimedQty, r1.FreeQty!.Value));
        Assert.Null(s[0].FreeQty);
        Assert.Equal(PutawayRules.ReserveEmpty, s[0].ReasonCode);
        Assert.Single(await receipts.SuggestTargetsAsync(created.Header.PublicId, line.Id, 1, default));

        // Destino con cupo: el espacio libre se ve en la línea (5) y la cantidad lo excede (solo aviso, D4: confirma igual).
        var withTarget = await receipts.UpdateLineAsync(created.Header.PublicId, line.Id, new ReceiptLineUpdateRequest(TargetBinId: f.ReserveBinId), default);
        Assert.Equal(5m, Assert.Single(withTarget.Lines).TargetFreeQty);
        var confirmed = await receipts.ConfirmAsync(created.Header.PublicId, null, default);
        Assert.Equal(ReceiptStatuses.Putaway, confirmed.Header.StatusCode);
        Assert.Null(Assert.Single(confirmed.Lines).TargetFreeQty);   // cerrado: ya no se calcula

        // Línea de otro recibo → 404.
        var other = await receipts.CreateAsync(Blind(ReceiptTypes.Blind, (f.ProductNonePublicId, 1m, null)), default);
        await Assert.ThrowsAsync<NotFoundException>(() => receipts.SuggestTargetsAsync(other.Header.PublicId, line.Id, null, default));
    }

    [Fact]
    public async Task Other_lines_of_the_receipt_count_as_claimed_and_returns_prefer_quarantine()
    {
        await using var f = await FixtureAsync();
        var receipts = f.Get<ReceiptService>();
        var created = await receipts.CreateAsync(Blind(ReceiptTypes.Blind, (f.ProductNonePublicId, 3m, f.ReserveBinId), (f.ProductNonePublicId, 3m, null)), default);
        var second = created.Lines[1];
        var s = await receipts.SuggestTargetsAsync(created.Header.PublicId, second.Id, null, default);
        var r1 = s.Single(x => x.BinCode == "R-01");
        Assert.Equal((3m, 2m, false), (r1.ClaimedQty, r1.FreeQty!.Value, r1.Fits));   // 3 de la otra línea + 3 > 5
        Assert.Equal("R-02", s[0].BinCode);
        // El espacio libre de la línea no descuenta su propia cantidad: cupo 5 − 0 en mano − 0 de otras líneas.
        Assert.Equal(5m, created.Lines[0].TargetFreeQty);

        var ret = await receipts.CreateAsync(Blind(ReceiptTypes.Return, (f.ProductNonePublicId, 1m, null)), default);
        var rs = await receipts.SuggestTargetsAsync(ret.Header.PublicId, ret.Lines[0].Id, null, default);
        Assert.Equal(("Q-01", PutawayRules.QuarantineReturn, "Cuarentena (devolución)"), (rs[0].BinCode, rs[0].ReasonCode, rs[0].Reason));
    }

    [Fact]
    public async Task Use_suggested_fills_only_lines_without_target_where_they_fit_accumulating()
    {
        await using var f = await FixtureAsync();
        var receipts = f.Get<ReceiptService>();
        var created = await receipts.CreateAsync(Blind(ReceiptTypes.Blind, (f.ProductNonePublicId, 8m, null), (f.ProductNonePublicId, 2m, null),
            (f.ProductNonePublicId, 0m, null)), default);

        var result = await receipts.ApplySuggestedTargetsAsync(created.Header.PublicId, new ReceiptApplySuggestionsRequest(created.RowVersion), default);
        Assert.Equal((2, 0), (result.Assigned, result.WithoutSuggestion));
        var lines = result.Receipt.Lines;
        Assert.Equal("R-02", lines[0].TargetBinCode);   // 8 no cabe en R-01 (cupo 5)
        Assert.Equal("R-02", lines[1].TargetBinCode);   // mismo producto: consolida con la línea anterior (D3)
        Assert.Null(lines[2].TargetBinId);              // no recibe nada: no necesita destino
        Assert.Null(lines[1].TargetFreeQty);            // R-02 sin cupo

        // Idempotente: una segunda vez no toca las que ya tienen destino.
        var again = await receipts.ApplySuggestedTargetsAsync(created.Header.PublicId, null, default);
        Assert.Equal((0, 0), (again.Assigned, again.WithoutSuggestion));

        var confirmed = await receipts.ConfirmAsync(created.Header.PublicId, null, default);
        Assert.Equal(ReceiptStatuses.Putaway, confirmed.Header.StatusCode);
        Assert.Empty(confirmed.PutawayTasks);
    }

    [Fact]
    public async Task Use_suggested_spreads_different_products_and_shows_free_space()
    {
        await using var f = await FixtureAsync();
        var receipts = f.Get<ReceiptService>();
        var created = await receipts.CreateAsync(Blind(ReceiptTypes.Blind, (f.ProductNonePublicId, 8m, null), (f.ProductClientPublicId, 2m, null)), default);
        var result = await receipts.ApplySuggestedTargetsAsync(created.Header.PublicId, null, default);
        Assert.Equal((2, 0), (result.Assigned, result.WithoutSuggestion));
        Assert.Equal("R-02", result.Receipt.Lines[0].TargetBinCode);
        Assert.Equal("R-01", result.Receipt.Lines[1].TargetBinCode);   // otro producto: R-02 ya no está vacía; R-01 vacía con espacio
        Assert.Equal(5m, result.Receipt.Lines[1].TargetFreeQty);       // sin descontar su propia cantidad (2 ≤ 5: sin aviso)
    }

    [Fact]
    public async Task Use_suggested_reports_lines_without_room()
    {
        await using var f = await FixtureAsync(r2Capacity: 1);
        var receipts = f.Get<ReceiptService>();
        var created = await receipts.CreateAsync(Blind(ReceiptTypes.Blind, (f.ProductNonePublicId, 8m, null)), default);
        var result = await receipts.ApplySuggestedTargetsAsync(created.Header.PublicId, null, default);
        Assert.Equal((0, 1), (result.Assigned, result.WithoutSuggestion));
        Assert.Null(Assert.Single(result.Receipt.Lines).TargetBinId);

        // Confirmado: 422 del recibo cerrado.
        await receipts.UpdateLineAsync(created.Header.PublicId, created.Lines[0].Id, new ReceiptLineUpdateRequest(TargetBinId: f.ReserveBinId), default);
        await receipts.ConfirmAsync(created.Header.PublicId, null, default);
        await Assert.ThrowsAsync<StatusRuleException>(() => receipts.ApplySuggestedTargetsAsync(created.Header.PublicId, null, default));
    }
}
