using Teikem.Domain.Constants;
using Teikem.Domain.Wms;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 16 — reglas puras del recibo directo a posición (ReceivingModeRules) y el cupo por unidades y la cuarentena del
/// acomodo dirigido (PutawayRules): modo del recibo (D1, D2, D9), destino necesario y permitido (D5), espacio libre y aviso
/// de cupo (D4), posición de asiento con cruce de muelle (D11), mensajes exactos del plan (sección 3).
/// </summary>
public class ReceivingModeRulesTests
{
    [Fact]
    public void Parse_mode_accepts_both_codes_without_case_and_rejects_others_with_the_exact_message()
    {
        Assert.Equal((null, null), ReceivingModeRules.ParseMode(null));
        Assert.Equal((null, null), ReceivingModeRules.ParseMode("  "));
        Assert.Equal((ReceivingModes.Direct, null), ReceivingModeRules.ParseMode(" direct "));
        Assert.Equal((ReceivingModes.Putaway, null), ReceivingModeRules.ParseMode("PUTAWAY"));
        var (mode, error) = ReceivingModeRules.ParseMode("HALF");
        Assert.Null(mode);
        Assert.Equal("Modo de recepción desconocido: 'HALF'. Use PUTAWAY o DIRECT.", error);
        Assert.Equal(new[] { "PUTAWAY", "DIRECT" }, ReceivingModes.All);
    }

    [Fact]
    public void Effective_mode_is_the_receipt_copy_then_the_warehouse_then_putaway()
    {
        Assert.Equal(ReceivingModes.Putaway, ReceivingModeRules.Effective(null, null));
        Assert.Equal(ReceivingModes.Direct, ReceivingModeRules.Effective(null, ReceivingModes.Direct));
        Assert.Equal(ReceivingModes.Putaway, ReceivingModeRules.Effective(ReceivingModes.Putaway, ReceivingModes.Direct));   // D2: el recibo conserva su modo
        Assert.Equal(ReceivingModes.Direct, ReceivingModeRules.Effective(ReceivingModes.Direct, ReceivingModes.Putaway));
        Assert.True(ReceivingModeRules.IsDirect("direct"));
        Assert.False(ReceivingModeRules.IsDirect(null));
    }

    [Fact]
    public void Create_mode_takes_the_request_then_the_warehouse_and_old_app_receipts_fall_back_to_putaway()
    {
        Assert.Equal(ReceivingModes.Direct, ReceivingModeRules.CreateMode(null, ReceivingModes.Direct, confirm: false, anyTarget: false));
        Assert.Equal(ReceivingModes.Putaway, ReceivingModeRules.CreateMode(ReceivingModes.Putaway, ReceivingModes.Direct, false, false));
        Assert.Equal(ReceivingModes.Direct, ReceivingModeRules.CreateMode(ReceivingModes.Direct, null, true, false));
        // D9: la app anterior manda confirm = true sin modo y sin destinos → con acomodo (tareas), no se pierde.
        Assert.Equal(ReceivingModes.Putaway, ReceivingModeRules.CreateMode(null, ReceivingModes.Direct, confirm: true, anyTarget: false));
        Assert.Equal(ReceivingModes.Direct, ReceivingModeRules.CreateMode(null, ReceivingModes.Direct, confirm: true, anyTarget: true));
        Assert.Equal(ReceivingModes.Putaway, ReceivingModeRules.CreateMode(null, null, true, true));
    }

    [Fact]
    public void A_line_needs_a_target_when_it_receives_something_except_a_lot_product_without_lot()
    {
        Assert.True(ReceivingModeRules.NeedsTarget(TrackingTypes.None, 1m, false));
        Assert.False(ReceivingModeRules.NeedsTarget(TrackingTypes.None, 0m, false));
        Assert.True(ReceivingModeRules.NeedsTarget(TrackingTypes.Lot, 3m, true));
        Assert.False(ReceivingModeRules.NeedsTarget(TrackingTypes.Lot, 3m, false));
        Assert.True(ReceivingModeRules.NeedsTarget(TrackingTypes.Serial, 2m, false));
    }

    [Fact]
    public void Target_zone_rejects_receiving_and_cross_dock_and_allows_quarantine()
    {
        Assert.Equal("La posición STG-01 está en una zona STAGING; la posición destino debe ser de guardado.",
            ReceivingModeRules.ValidateTargetZone("STG-01", ZoneTypes.Staging));
        Assert.Equal("La posición X-01 está en una zona CROSSDOCK; la posición destino debe ser de guardado.",
            ReceivingModeRules.ValidateTargetZone("X-01", ZoneTypes.CrossDock));
        foreach (var z in new[] { ZoneTypes.Reserve, ZoneTypes.Picking, ZoneTypes.Quarantine, ZoneTypes.Refrigerated, null })
            Assert.Null(ReceivingModeRules.ValidateTargetZone("R-01", z));
    }

    [Fact]
    public void Free_quantity_and_capacity_warning()
    {
        Assert.Null(ReceivingModeRules.FreeQty(null, 10m, 5m));
        Assert.Equal(5m, ReceivingModeRules.FreeQty(5, 0m, 0m));
        Assert.Equal(2m, ReceivingModeRules.FreeQty(10, 6m, 2m));
        Assert.Equal(0m, ReceivingModeRules.FreeQty(5, 4m, 3m));   // nunca negativo
        Assert.True(ReceivingModeRules.Exceeds(5m, 8m));
        Assert.False(ReceivingModeRules.Exceeds(5m, 5m));
        Assert.False(ReceivingModeRules.Exceeds(null, 1000m));      // sin cupo nunca excede
    }

    [Fact]
    public void Posting_bin_is_the_target_in_direct_mode_unless_the_line_goes_to_cross_dock()
    {
        Assert.Equal(20, ReceivingModeRules.PostingBin(ReceivingModes.Direct, hasCrossDock: false, targetBinId: 20, stagingBinId: 10));
        Assert.Equal(10, ReceivingModeRules.PostingBin(ReceivingModes.Direct, hasCrossDock: true, targetBinId: 20, stagingBinId: 10));   // D11
        Assert.Equal(10, ReceivingModeRules.PostingBin(ReceivingModes.Putaway, false, 20, 10));
    }

    [Fact]
    public void Messages_are_the_exact_ones_of_the_plan()
    {
        Assert.Equal("Indique la posición destino de PN: el recibo entra directo a posición.", ReceivingModeRules.TargetRequired("PN"));
        Assert.Equal("La posición ZZ no existe en el almacén del recibo.", ReceivingModeRules.TargetCodeNotFound("ZZ"));
        Assert.Equal("Indique la posición destino por id o por código, no ambos.", ReceivingModeRules.TargetIdAndCode);
        Assert.Equal("Ya se capturó PN con destino R-01; en un recibo con aviso u orden de compra cada línea entra a una sola posición.",
            ReceivingModeRules.SameProductOtherTarget("PN", "R-01"));
        Assert.Equal("La posición destino R-01 está desactivada.", ReceivingModeRules.TargetBinInactive("R-01"));
        Assert.Equal("La zona de la posición destino R-01 está inactiva.", ReceivingModeRules.TargetZoneInactive("R-01"));
    }

    // ---------------------------------------------------------------- PutawayRules (cupo por unidades y cuarentena)

    private static PutawayCandidate C(int id, string code, string zoneType, bool empty = true, decimal productQty = 0m, int? cap = null, decimal currentQty = 0m)
        => new(id, code, 1, zoneType[..3], zoneType, true, null, 0m, empty, productQty, 0m, false, cap, currentQty);

    [Fact]
    public void Unit_capacity_skips_bins_where_the_quantity_does_not_fit_also_for_putaway_tasks()
    {
        var candidates = new[] { C(1, "R-01", ZoneTypes.Reserve, cap: 5), C(2, "R-02", ZoneTypes.Reserve), C(3, "R-03", ZoneTypes.Reserve, empty: false, cap: 10, currentQty: 4m) };
        var r = PutawayRules.Rank(candidates, null, 8m, null, RotationClasses.Slow, null, 5);
        Assert.Equal(new[] { 2 }, r.Select(x => x.BinId));                        // R-01 (cupo 5) y R-03 (4 + 8 > 10) no caben
        Assert.All(r, x => Assert.True(x.Fits));
        var small = PutawayRules.Rank(candidates, null, 5m, null, RotationClasses.Slow, null, 5);
        Assert.Equal(new[] { 1, 2, 3 }, small.Select(x => x.BinId));             // 5 cabe justo en R-01 y en R-03 (4 + 5 ≤ 10)
    }

    [Fact]
    public void Over_capacity_bins_can_be_listed_last_with_fits_false()
    {
        var candidates = new[] { C(1, "R-01", ZoneTypes.Reserve, cap: 5), C(2, "R-02", ZoneTypes.Reserve) };
        var r = PutawayRules.Rank(candidates, null, 8m, null, RotationClasses.Slow, null, 5, includeOverCapacity: true);
        Assert.Equal(new[] { (2, true), (1, false) }, r.Select(x => (x.BinId, x.Fits)));
        Assert.Equal(5, r[1].MaxCapacityQty);
    }

    [Fact]
    public void Returns_put_quarantine_first_and_quarantine_is_never_suggested_otherwise()
    {
        var candidates = new[] { C(1, "R-01", ZoneTypes.Reserve), C(2, "Q-01", ZoneTypes.Quarantine), C(3, "STG", ZoneTypes.Staging), C(4, "X-01", ZoneTypes.CrossDock) };
        var ret = PutawayRules.Rank(candidates, null, 1m, null, RotationClasses.Slow, null, 5, preferQuarantine: true);
        Assert.Equal(new[] { 2, 1 }, ret.Select(x => x.BinId));
        Assert.Equal(PutawayRules.QuarantineReturn, ret[0].ReasonCode);
        Assert.Equal("Cuarentena (devolución)", ret[0].Reason);
        var normal = PutawayRules.Rank(candidates, null, 1m, null, RotationClasses.Slow, null, 5);
        Assert.Equal(new[] { 1 }, normal.Select(x => x.BinId));
    }
}
