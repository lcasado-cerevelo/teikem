using Teikem.Domain.Wms;
using Xunit;

namespace Teikem.Tests;

/// <summary>Lote 6 / P2: reglas puras del maestro de productos y de las categorías (ProductRules), con los mensajes exactos del manual.</summary>
public class ProductRulesTests
{
    // ---------------------------------------------------------------- SKU, nombre y código de barras

    [Fact]
    public void NormalizeSku_trims_and_uppercases()
        => Assert.Equal(("GLU-100", (string?)null), ProductRules.NormalizeSku("  glu-100 "));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NormalizeSku_empty_is_required(string? raw)
        => Assert.Equal(((string?)null, "El SKU es obligatorio."), ProductRules.NormalizeSku(raw));

    [Fact]
    public void NormalizeSku_rejects_long_and_inner_spaces()
    {
        Assert.Equal("El SKU no puede exceder 60 caracteres.", ProductRules.NormalizeSku(new string('a', 61)).Error);
        Assert.Equal(new string('A', 60), ProductRules.NormalizeSku(new string('a', 60)).Sku);
        Assert.Equal("El SKU no admite espacios ni caracteres de control.", ProductRules.NormalizeSku("AB C").Error);
    }

    [Fact]
    public void NormalizeName_and_barcode()
    {
        Assert.Equal("El nombre del producto es obligatorio.", ProductRules.NormalizeName(" ").Error);
        Assert.Equal("Medidor", ProductRules.NormalizeName(" Medidor ").Name);
        Assert.Equal(((string?)null, (string?)null), ProductRules.NormalizeBarcode("  "));
        Assert.Equal("0123", ProductRules.NormalizeBarcode(" 0123 ").Barcode);
        Assert.Equal("El código de barras no puede exceder 60 caracteres.", ProductRules.NormalizeBarcode(new string('1', 61)).Error);
    }

    // ---------------------------------------------------------------- dinero y medidas

    [Fact]
    public void Money_accepts_null_zero_and_four_decimals()
    {
        Assert.Null(ProductRules.Money(null, "costo"));
        Assert.Null(ProductRules.Money(0m, "costo"));
        Assert.Null(ProductRules.Money(12.3456m, "costo"));
    }

    [Fact]
    public void Money_messages_are_exact()
    {
        Assert.Equal("El costo y el precio no pueden ser negativos.", ProductRules.Money(-0.01m, "costo"));
        Assert.Equal("El costo admite como máximo 4 decimales.", ProductRules.Money(1.23456m, "costo"));
        Assert.Equal("El precio admite como máximo 4 decimales.", ProductRules.Money(1.23456m, "precio"));
        Assert.Equal(ProductRules.MoneyTooLarge, ProductRules.Money(100_000_000_000_000m, "precio"));
    }

    [Fact]
    public void ValidateMeasures_checks_sign_and_precision()
    {
        Assert.Empty(ProductRules.ValidateMeasures(1.125m, 0.0125m));
        Assert.Contains(("weightKg", "El peso y el volumen no pueden ser negativos."), ProductRules.ValidateMeasures(-1m, null));
        Assert.Contains(("volumeM3", ProductRules.VolumeInvalid), ProductRules.ValidateMeasures(null, 0.12345m));
        Assert.Contains(("weightKg", ProductRules.WeightInvalid), ProductRules.ValidateMeasures(1.2345m, null));
    }

    // ---------------------------------------------------------------- mínimos

    [Fact]
    public void Minimums_valid_with_picking_bin()
        => Assert.Empty(ProductRules.ValidateMinimums(50m, 5m, 8m, preferredBinIsPicking: true));

    [Fact]
    public void Minimums_max_below_min_message()
        => Assert.Contains(("maxPickQty", "El máximo de la posición de picking debe ser mayor o igual al mínimo."),
            ProductRules.ValidateMinimums(null, 5m, 4m, preferredBinIsPicking: true));

    [Fact]
    public void Minimums_pick_min_requires_picking_bin()
    {
        Assert.Contains(("minPickQty", "El mínimo de picking requiere una posición preferida en una zona PICKING."),
            ProductRules.ValidateMinimums(null, 5m, null, preferredBinIsPicking: false));
        // Sin mínimo de picking no se exige posición preferida (MinQty es del disponible total).
        Assert.Empty(ProductRules.ValidateMinimums(50m, null, 8m, preferredBinIsPicking: false));
    }

    [Fact]
    public void Minimums_negative_and_decimals()
    {
        Assert.Contains(("minQty", ProductRules.MinNegative), ProductRules.ValidateMinimums(-1m, null, null, false));
        Assert.Contains(("maxPickQty", ProductRules.MinDecimals), ProductRules.ValidateMinimums(null, null, 1.2345m, false));
    }

    [Fact]
    public void Available_BelowMin_and_Value()
    {
        Assert.Equal(7m, ProductRules.Available(10m, 3m));
        Assert.True(ProductRules.IsBelowMin(50m, 13m, isActive: true));
        Assert.False(ProductRules.IsBelowMin(50m, 13m, isActive: false));
        Assert.False(ProductRules.IsBelowMin(null, 0m, isActive: true));
        Assert.False(ProductRules.IsBelowMin(5m, 5m, isActive: true));
        Assert.Equal(98.7648m, ProductRules.Value(8m, 12.3456m));
        Assert.Equal(0.0002m, ProductRules.Value(0.001m, 0.15m)); // 0.00015 → AwayFromZero
        Assert.Null(ProductRules.Value(8m, null));
    }

    [Fact]
    public void DaysToExpiry_and_owner_label()
    {
        var today = new DateOnly(2026, 9, 26);
        Assert.Equal(4, ProductRules.DaysToExpiry(new DateOnly(2026, 9, 30), today));
        Assert.Equal(-1, ProductRules.DaysToExpiry(new DateOnly(2026, 9, 25), today));
        Assert.Null(ProductRules.DaysToExpiry(null, today));
        Assert.Equal("Propio", ProductRules.OwnerLabel(null));
        Assert.Equal("Farmacia A", ProductRules.OwnerLabel("Farmacia A"));
    }

    // ---------------------------------------------------------------- inmutables y desactivación

    [Fact]
    public void Immutable_only_with_movements_in_fixed_order()
    {
        Assert.Null(ProductRules.ImmutableChange(false, true, true, true));
        Assert.Null(ProductRules.ImmutableChange(true, false, false, false));
        Assert.Equal("el tipo de seguimiento", ProductRules.ImmutableChange(true, true, true, false));
        Assert.Equal("la unidad de medida base", ProductRules.ImmutableChange(true, false, true, true));
        Assert.Equal("el dueño", ProductRules.ImmutableChange(true, false, false, true));
        Assert.Equal("No se puede cambiar el tipo de seguimiento de un producto que ya tiene movimientos.",
            ProductRules.Immutable(ProductRules.FieldTracking));
    }

    [Fact]
    public void Deactivate_messages_are_exact()
    {
        Assert.Equal("El producto PN-1 tiene inventario en mano (12.5); no se puede desactivar.", ProductRules.DeactivateWithStock("PN-1", 12.500m));
        Assert.Equal("El producto PN-1 tiene inventario en mano (3); no se puede desactivar.", ProductRules.DeactivateWithStock("PN-1", 3m));
        Assert.Equal("El producto PN-1 está en recibos abiertos o tareas pendientes; ciérrelos antes de desactivarlo.",
            ProductRules.DeactivateOpenDocs("PN-1"));
        Assert.Equal("Ya existe un producto con ese SKU para ese dueño.", ProductRules.SkuTaken);
        Assert.Equal("Ya existe un producto activo con ese código de barras.", ProductRules.BarcodeTaken);
        Assert.Equal("La posición preferida debe pertenecer al almacén preferido.", ProductRules.PreferredBinMismatch);
    }

    // ---------------------------------------------------------------- categorías

    // 1 Medicamentos → 2 Diabetes → 3 Medidores → 4 Tiras → 5 Marca X ; 6 Equipos (raíz)
    private static readonly Dictionary<int, int?> Tree = new() { [1] = null, [2] = 1, [3] = 2, [4] = 3, [5] = 4, [6] = null };
    private static readonly Dictionary<int, string> Names = new()
    { [1] = "Medicamentos", [2] = "Diabetes", [3] = "Medidores", [4] = "Tiras", [5] = "Marca X", [6] = "Equipos" };

    [Fact]
    public void CreatesCycle_detects_self_and_descendant_parent()
    {
        Assert.True(ProductRules.CreatesCycle(2, 2, Tree));
        Assert.True(ProductRules.CreatesCycle(2, 4, Tree));
        Assert.False(ProductRules.CreatesCycle(2, 6, Tree));
        Assert.False(ProductRules.CreatesCycle(2, null, Tree));
        Assert.Equal("Una categoría no puede ser su propia ascendente.", ProductRules.CategoryCycle);
    }

    [Fact]
    public void CreatesCycle_tolerates_corrupt_map()
        => Assert.True(ProductRules.CreatesCycle(9, 7, new Dictionary<int, int?> { [7] = 8, [8] = 7, [9] = null }));

    [Fact]
    public void Level_and_subtree_height()
    {
        Assert.Equal(1, ProductRules.Level(null, Tree));
        Assert.Equal(5, ProductRules.Level(4, Tree));
        Assert.Equal(5, ProductRules.SubtreeHeight(1, Tree));
        Assert.Equal(1, ProductRules.SubtreeHeight(6, Tree));
    }

    [Fact]
    public void ExceedsDepth_for_new_and_moved_categories()
    {
        Assert.False(ProductRules.ExceedsDepth(null, 4, Tree)); // nueva en el nivel 5
        Assert.True(ProductRules.ExceedsDepth(null, 5, Tree));  // nueva en el nivel 6
        Assert.True(ProductRules.ExceedsDepth(1, 6, Tree));     // subárbol de 5 niveles bajo una raíz → 6
        Assert.False(ProductRules.ExceedsDepth(2, 6, Tree));    // subárbol de 4 niveles bajo una raíz → 5
        Assert.Equal("Las categorías admiten como máximo 5 niveles.", ProductRules.CategoryDepth);
    }

    [Fact]
    public void CategoryPath_and_descendants()
    {
        Assert.Equal("Medicamentos / Diabetes / Medidores", ProductRules.CategoryPath(3, Tree, Names));
        Assert.Equal("Equipos", ProductRules.CategoryPath(6, Tree, Names));
        Assert.Equal(new HashSet<int> { 3, 4, 5 }, ProductRules.WithDescendants(new[] { 3 }, Tree).ToHashSet());
        Assert.Equal(new HashSet<int> { 6 }, ProductRules.WithDescendants(new[] { 6 }, Tree).ToHashSet());
    }

    [Fact]
    public void CategoryName_normalization_and_messages()
    {
        Assert.Equal("El nombre de la categoría es obligatorio.", ProductRules.NormalizeCategoryName(null).Error);
        Assert.Equal("Tiras", ProductRules.NormalizeCategoryName(" Tiras ").Name);
        Assert.Equal("Ya existe una categoría con ese nombre en ese nivel.", ProductRules.CategoryNameTaken);
        Assert.Equal("La categoría tiene productos activos.", ProductRules.CategoryHasProducts);
    }
}
