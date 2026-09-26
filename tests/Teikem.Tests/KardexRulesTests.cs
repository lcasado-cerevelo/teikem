using Teikem.Domain.Constants;
using Teikem.Domain.Wms;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 6 / P3: reglas puras del Kárdex (KardexRules). La cantidad del ledger se guarda CON signo (D3, L331); SignedQuantity
/// aplica la perspectiva del filtro de ubicación. Además: columna Posición, origen legible, chips, rango UTC, subcategorías,
/// genealogía (entradas/salidas y destinos) y reconstrucción del saldo desde el ledger.
/// </summary>
public class KardexRulesTests
{
    private const int W1 = 1, W2 = 2, B1 = 11, B2 = 12, B3 = 21;

    private static KardexLocationFilter Wh(params int[] ids) => new(ids.ToHashSet(), null);
    private static KardexLocationFilter Bins(params int[] ids) => new(null, ids.ToHashSet());

    // ---------------------------------------------------------------- SignedQuantity sin filtro

    [Theory]
    [InlineData(InventoryTxnTypes.Receipt, 10, 10)]
    [InlineData(InventoryTxnTypes.Issue, -3, -3)]
    [InlineData(InventoryTxnTypes.CrossDock, -4, -4)]
    [InlineData(InventoryTxnTypes.Adjustment, 2, 2)]
    [InlineData(InventoryTxnTypes.Adjustment, -1, -1)]
    [InlineData(InventoryTxnTypes.Transfer, 5, 0)]
    public void SignedQuantity_without_filter_is_the_ledger_quantity_except_transfer(string type, int stored, int expected)
    {
        Assert.Equal(expected, KardexRules.SignedQuantity(stored, type, W1, B1, W1, B2));
        Assert.Equal(expected, KardexRules.SignedQuantity(stored, type, W1, B1, W1, B2, KardexLocationFilter.None));
        Assert.Equal(expected, KardexRules.SignedQuantity(stored, type, W1, B1, W1, B2, new KardexLocationFilter(new HashSet<int>(), null)));
    }

    [Fact]
    public void SignedQuantity_transfer_type_is_case_insensitive()
        => Assert.Equal(0m, KardexRules.SignedQuantity(5m, "transfer", W1, B1, W2, B3));

    // ---------------------------------------------------------------- SignedQuantity con filtro

    [Fact]
    public void SignedQuantity_with_bin_filter_exit_is_negative_entry_positive_internal_zero()
    {
        // TRANSFER B1 → B2 (+5 en el ledger).
        Assert.Equal(-5m, KardexRules.SignedQuantity(5m, InventoryTxnTypes.Transfer, W1, B1, W1, B2, Bins(B1)));
        Assert.Equal(5m, KardexRules.SignedQuantity(5m, InventoryTxnTypes.Transfer, W1, B1, W1, B2, Bins(B2)));
        Assert.Equal(0m, KardexRules.SignedQuantity(5m, InventoryTxnTypes.Transfer, W1, B1, W1, B2, Bins(B1, B2)));
        Assert.Equal(0m, KardexRules.SignedQuantity(5m, InventoryTxnTypes.Transfer, W1, B1, W1, B2, Bins(B3)));
    }

    [Fact]
    public void SignedQuantity_with_filter_uses_magnitude_for_signed_ledger_rows()
    {
        // ISSUE −3 desde B1: salida del filtro → −3; RECEIPT +10 a B1 → +10; ADJUSTMENT −2 desde B1 → −2.
        Assert.Equal(-3m, KardexRules.SignedQuantity(-3m, InventoryTxnTypes.Issue, W1, B1, null, null, Bins(B1)));
        Assert.Equal(10m, KardexRules.SignedQuantity(10m, InventoryTxnTypes.Receipt, null, null, W1, B1, Bins(B1)));
        Assert.Equal(-2m, KardexRules.SignedQuantity(-2m, InventoryTxnTypes.Adjustment, W1, B1, null, null, Bins(B1)));
        Assert.Equal(0m, KardexRules.SignedQuantity(-2m, InventoryTxnTypes.Adjustment, W1, B1, null, null, Bins(B2)));
    }

    [Fact]
    public void SignedQuantity_with_warehouse_filter_between_warehouses()
    {
        Assert.Equal(-7m, KardexRules.SignedQuantity(7m, InventoryTxnTypes.Transfer, W1, B1, W2, B3, Wh(W1)));
        Assert.Equal(7m, KardexRules.SignedQuantity(7m, InventoryTxnTypes.Transfer, W1, B1, W2, B3, Wh(W2)));
        Assert.Equal(0m, KardexRules.SignedQuantity(7m, InventoryTxnTypes.Transfer, W1, B1, W2, B3, Wh(W1, W2)));
        // Dentro del mismo almacén: interna.
        Assert.Equal(0m, KardexRules.SignedQuantity(7m, InventoryTxnTypes.Transfer, W1, B1, W1, B2, Wh(W1)));
    }

    [Fact]
    public void Filter_with_warehouse_and_bin_requires_both()
    {
        var f = new KardexLocationFilter(new HashSet<int> { W1 }, new HashSet<int> { B1 });
        Assert.True(f.Matches(W1, B1));
        Assert.False(f.Matches(W2, B1));
        Assert.False(f.Matches(W1, B2));
        Assert.False(f.Matches(null, B1));
        Assert.False(Bins(B1).Matches(W1, null));
        Assert.True(KardexLocationFilter.None.IsEmpty);
    }

    // ---------------------------------------------------------------- Posición, origen, chips

    [Fact]
    public void Position_formats_one_side_both_sides_or_dash()
    {
        Assert.Equal("ALM-01/A01-R01-N1-P01", KardexRules.Position(null, null, "ALM-01", "A01-R01-N1-P01"));
        Assert.Equal("ALM-01/A01-R01-N1-P01", KardexRules.Position("ALM-01", "A01-R01-N1-P01", null, null));
        Assert.Equal("ALM-01/A → ALM-02/B", KardexRules.Position("ALM-01", "A", "ALM-02", "B"));
        Assert.Equal("ALM-01", KardexRules.Position("ALM-01", null, null, null));
        Assert.Equal("—", KardexRules.Position(null, null, null, null));
    }

    [Fact]
    public void RefLabel_by_entity_type_with_fallback()
    {
        Assert.Equal("Recibo REC-00001", KardexRules.RefLabel(EntityTypes.Receipt, 5, "REC-00001"));
        Assert.Equal("Recolección EMP-00007", KardexRules.RefLabel(EntityTypes.PickBatch, 9, "EMP-00007"));
        Assert.Equal("Conteo CC-00002", KardexRules.RefLabel(EntityTypes.CycleCount, 2, "CC-00002"));
        Assert.Equal("Orden de compra PO-00003", KardexRules.RefLabel(EntityTypes.PurchaseOrder, 3, "PO-00003"));
        Assert.Equal("Cruce de muelle XD-00001", KardexRules.RefLabel(EntityTypes.CrossDockAllocation, 4, "XD-00001"));
        Assert.Equal("Tarea #12", KardexRules.RefLabel(EntityTypes.WarehouseTask, 12));
        // Sin número conocido: 'TIPO·id'.
        Assert.Equal("RECEIPT·5", KardexRules.RefLabel(EntityTypes.Receipt, 5));
        Assert.Equal("FOO·8", KardexRules.RefLabel("foo", 8));
        // Tipo sin prefijo con número: solo el número.
        Assert.Equal("X-1", KardexRules.RefLabel("FOO", 8, "X-1"));
        Assert.Null(KardexRules.RefLabel(null, 8));
    }

    [Theory]
    [InlineData(InventoryTxnTypes.Receipt, "Recepción")]
    [InlineData(InventoryTxnTypes.Issue, "Despacho")]
    [InlineData(InventoryTxnTypes.Transfer, "Transferencia")]
    [InlineData(InventoryTxnTypes.Adjustment, "Ajuste")]
    [InlineData(InventoryTxnTypes.CrossDock, "Cruce de muelle")]
    [InlineData("receipt", "Recepción")]
    [InlineData("OTHER", "OTHER")]
    public void TypeChip_labels(string code, string label) => Assert.Equal(label, KardexRules.TypeChip(code));

    // ---------------------------------------------------------------- fechas, páginas, categorías

    [Fact]
    public void UtcRange_to_is_exclusive_next_day()
    {
        var (from, to) = KardexRules.UtcRange(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 26));
        Assert.Equal(new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc), from);
        Assert.Equal(new DateTime(2026, 9, 27, 0, 0, 0, DateTimeKind.Utc), to);
        Assert.Equal(DateTimeKind.Utc, from!.Value.Kind);
        Assert.Equal(((DateTime?)null, (DateTime?)null), KardexRules.UtcRange(null, null));
        Assert.True(KardexRules.IsRangeInverted(new DateOnly(2026, 9, 2), new DateOnly(2026, 9, 1)));
        Assert.False(KardexRules.IsRangeInverted(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 1)));
        Assert.Equal("La fecha 'desde' no puede ser posterior a la fecha 'hasta'.", KardexRules.RangeInverted);
    }

    [Fact]
    public void Page_is_clamped()
    {
        Assert.Equal((0, 200), KardexRules.Page(-5, 0, 200));
        Assert.Equal((10, 200), KardexRules.Page(10, 5000, 200));
        Assert.Equal((0, 50), KardexRules.Page(0, 50, 200));
    }

    [Fact]
    public void WithDescendants_includes_all_levels_and_survives_cycles()
    {
        var parents = new Dictionary<int, int?> { [1] = null, [2] = 1, [3] = 2, [4] = null, [5] = 6, [6] = 5 };
        Assert.Equal(new[] { 1, 2, 3 }, KardexRules.WithDescendants(new[] { 1 }, parents).OrderBy(i => i));
        Assert.Equal(new[] { 2, 3 }, KardexRules.WithDescendants(new[] { 2 }, parents).OrderBy(i => i));
        Assert.Equal(new[] { 5, 6 }, KardexRules.WithDescendants(new[] { 5 }, parents).OrderBy(i => i));
    }

    [Fact]
    public void DaysToExpiry_and_owner_label()
    {
        var today = new DateOnly(2026, 9, 26);
        Assert.Equal(4, KardexRules.DaysToExpiry(new DateOnly(2026, 9, 30), today));
        Assert.Equal(-1, KardexRules.DaysToExpiry(new DateOnly(2026, 9, 25), today));
        Assert.Null(KardexRules.DaysToExpiry(null, today));
        Assert.Equal("Propio", KardexRules.OwnerLabel(null));
        Assert.Equal("Farmacia A", KardexRules.OwnerLabel("Farmacia A"));
    }

    // ---------------------------------------------------------------- genealogía

    [Fact]
    public void InOut_ignores_transfers()
    {
        var rows = new[]
        {
            (InventoryTxnTypes.Receipt, 10m), (InventoryTxnTypes.Transfer, 4m), (InventoryTxnTypes.Issue, -3m),
            (InventoryTxnTypes.Adjustment, -1m), (InventoryTxnTypes.Adjustment, 2m),
        };
        Assert.Equal((12m, 4m), KardexRules.InOut(rows));
    }

    [Fact]
    public void DestinationQty_is_net_and_reversal_neutralizes()
    {
        Assert.Equal(3m, KardexRules.DestinationQty(new[] { (InventoryTxnTypes.Issue, -3m) }));
        Assert.Equal(0m, KardexRules.DestinationQty(new[] { (InventoryTxnTypes.Issue, -3m), (InventoryTxnTypes.Adjustment, 3m) }));
        Assert.Equal(4m, KardexRules.DestinationQty(new[] { (InventoryTxnTypes.CrossDock, -4m), (InventoryTxnTypes.Transfer, 9m) }));
        Assert.True(KardexRules.IsOutbound(InventoryTxnTypes.Issue));
        Assert.True(KardexRules.IsOutbound(InventoryTxnTypes.CrossDock));
        Assert.False(KardexRules.IsOutbound(InventoryTxnTypes.Adjustment));
    }

    // ---------------------------------------------------------------- conciliación

    [Fact]
    public void Rebuild_adds_to_side_and_subtracts_from_side_per_key()
    {
        // RECEIPT +10 a (P1, W1, B1, lote NULL); ISSUE −3 desde ahí; TRANSFER +2 de B1 a B2; ADJUSTMENT −1 desde B2;
        // RECEIPT +4 a (P1, W1, B1, lote 7): el lote distingue la clave.
        var k1 = new LedgerKey(1, W1, B1, null);
        var k2 = new LedgerKey(1, W1, B2, null);
        var k3 = new LedgerKey(1, W1, B1, 7);
        var to = new[] { (k1, 10m), (k2, 2m), (k3, 4m) };
        var from = new[] { (k1, -3m), (k1, 2m), (k2, -1m) };
        var map = KardexRules.Rebuild(to, from);
        Assert.Equal(5m, map[k1]);
        Assert.Equal(1m, map[k2]);
        Assert.Equal(4m, map[k3]);
        Assert.Equal(3, map.Count);
    }
}
