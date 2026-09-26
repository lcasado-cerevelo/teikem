using Teikem.Domain.Wms;
using Xunit;

namespace Teikem.Tests;

/// <summary>Lote 6 / P1: reglas puras de Almacenes y ubicaciones (códigos, posición compuesta, baja, almacén por defecto, muelle manual).</summary>
public class WarehouseRulesTests
{
    // ---------------------------------------------------------------- NormalizeCode

    [Theory]
    [InlineData("  alm-01 ", "ALM-01")]
    [InlineData("stg", "STG")]
    [InlineData("zona_a", "ZONA_A")]
    public void NormalizeCode_trims_and_uppercases(string raw, string expected)
        => Assert.Equal((expected, (string?)null), WarehouseRules.NormalizeCode(raw));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NormalizeCode_empty_is_required(string? raw)
        => Assert.Equal(((string?)null, "El código es obligatorio."), WarehouseRules.NormalizeCode(raw));

    [Theory]
    [InlineData("ALM 01")]
    [InlineData("ALM/01")]
    [InlineData("ALMÁ")]
    [InlineData("A.B")]
    public void NormalizeCode_rejects_other_characters(string raw)
        => Assert.Equal(((string?)null, "El código solo admite letras, números, guion y guion bajo (máximo 30)."), WarehouseRules.NormalizeCode(raw));

    [Fact]
    public void NormalizeCode_max_30()
    {
        Assert.Equal(new string('A', 30), WarehouseRules.NormalizeCode(new string('a', 30)).Code);
        Assert.Equal(WarehouseRules.CodeInvalidMessage, WarehouseRules.NormalizeCode(new string('A', 31)).Error);
    }

    // ---------------------------------------------------------------- ComposeBinCode / ResolveBinCode

    [Fact]
    public void ComposeBinCode_joins_the_parts_in_order()
        => Assert.Equal(("A01-R02-N3-P04", (string?)null), WarehouseRules.ComposeBinCode("a01", " R02", "n3 ", "P04"));

    [Fact]
    public void ComposeBinCode_skips_empty_parts()
        => Assert.Equal(("A01-P04", (string?)null), WarehouseRules.ComposeBinCode("A01", null, "  ", "P04"));

    [Fact]
    public void ComposeBinCode_without_parts_asks_for_code()
        => Assert.Equal(((string?)null, "Indique el código de la posición o su pasillo/rack/nivel/posición."),
            WarehouseRules.ComposeBinCode(null, "", " ", null));

    [Fact]
    public void ComposeBinCode_rejects_long_or_invalid_parts()
    {
        Assert.Equal(WarehouseRules.BinPartInvalidMessage, WarehouseRules.ComposeBinCode(new string('A', 21), null, null, null).Error);
        Assert.Equal(WarehouseRules.BinPartInvalidMessage, WarehouseRules.ComposeBinCode("A 1", null, null, null).Error);
    }

    [Fact]
    public void ComposeBinCode_max_40()
    {
        var p = new string('A', 10);
        // 4 × 10 + 3 guiones = 43 > 40
        Assert.Equal(WarehouseRules.BinCodeInvalidMessage, WarehouseRules.ComposeBinCode(p, p, p, p).Error);
        // 3 × 12 + 2 guiones = 38
        var q = new string('B', 12);
        Assert.Equal($"{q}-{q}-{q}", WarehouseRules.ComposeBinCode(q, q, q, null).Code);
    }

    [Fact]
    public void ResolveBinCode_prefers_the_explicit_code()
    {
        Assert.Equal(("STG-01", (string?)null), WarehouseRules.ResolveBinCode(" stg-01 ", "A01", null, null, null));
        Assert.Equal(("A01-R01-N1-P01", (string?)null), WarehouseRules.ResolveBinCode(null, "A01", "R01", "N1", "P01"));
        Assert.Equal(WarehouseRules.BinCodeRequiredMessage, WarehouseRules.ResolveBinCode("  ", null, null, null, null).Error);
        Assert.Equal(WarehouseRules.BinCodeInvalidMessage, WarehouseRules.ResolveBinCode(new string('X', 41), null, null, null, null).Error);
        Assert.Equal(WarehouseRules.BinCodeInvalidMessage, WarehouseRules.ResolveBinCode("Q 01", null, null, null, null).Error);
        // Las partes se validan aunque llegue el código explícito (se guardan en sus columnas).
        Assert.Equal(WarehouseRules.BinPartInvalidMessage, WarehouseRules.ResolveBinCode("Q-01", "A/1", null, null, null).Error);
    }

    // ---------------------------------------------------------------- capacidad de peso

    [Fact]
    public void ValidateMaxWeight_rules()
    {
        Assert.Null(WarehouseRules.ValidateMaxWeight(null));
        Assert.Null(WarehouseRules.ValidateMaxWeight(1500.5m));
        Assert.Equal("La capacidad de peso debe ser mayor que cero.", WarehouseRules.ValidateMaxWeight(0m));
        Assert.Equal(WarehouseRules.MaxWeight, WarehouseRules.ValidateMaxWeight(-1m));
        Assert.Equal(WarehouseRules.MaxWeightPrecision, WarehouseRules.ValidateMaxWeight(1.2345m));
        Assert.Equal(WarehouseRules.MaxWeightPrecision, WarehouseRules.ValidateMaxWeight(1_000_000_000m));
    }

    // ---------------------------------------------------------------- almacén por defecto

    [Fact]
    public void DefaultWarehouse_only_with_exactly_one_active()
    {
        Assert.Equal(7, WarehouseRules.DefaultWarehouse(new[] { 7 }));
        Assert.Equal(7, WarehouseRules.DefaultWarehouse(new[] { 7, 7 }));
        Assert.Null(WarehouseRules.DefaultWarehouse(Array.Empty<int>()));
        Assert.Null(WarehouseRules.DefaultWarehouse(new[] { 7, 8 }));
    }

    // ---------------------------------------------------------------- muelle manual

    [Theory]
    [InlineData("FREE", true)]
    [InlineData("occupied", true)]
    [InlineData(" MAINTENANCE ", true)]
    [InlineData("CLOSED", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void ManualDockStatuses_are_free_occupied_and_maintenance(string? code, bool expected)
        => Assert.Equal(expected, WarehouseRules.IsManualDockStatus(code));

    [Fact]
    public void ManualDockStatuses_has_exactly_three()
        => Assert.Equal(3, WarehouseRules.ManualDockStatuses.Count);

    // ---------------------------------------------------------------- baja del almacén

    [Fact]
    public void DeactivationBlockers_empty_when_nothing_is_open()
        => Assert.Empty(WarehouseRules.DeactivationBlockers(new WarehouseUsage(0, 0, 0, 0, 0, 0, 0, 0)));

    [Fact]
    public void DeactivationBlockers_lists_each_kind()
    {
        var e = WarehouseRules.DeactivationBlockers(new WarehouseUsage(5.5m, 1, 2, 1, 3, 1, 1, 2));
        Assert.Equal(new[] { "inventory", "receipts", "cycleCounts", "tasks", "pickBatches", "crossDockPlans", "appointments" }, e.Keys.ToArray());
        Assert.Equal("Inventario en mano 5.5, reservado 1.", e["inventory"][0]);
        Assert.Equal("Recibos abiertos: 2.", e["receipts"][0]);
    }

    [Fact]
    public void DeactivationBlockers_reserved_alone_blocks()
        => Assert.True(WarehouseRules.DeactivationBlockers(new WarehouseUsage(0, 2, 0, 0, 0, 0, 0, 0)).ContainsKey("inventory"));

    // ---------------------------------------------------------------- mensajes exactos

    [Fact]
    public void Messages_are_exact()
    {
        Assert.Equal("Ya existe un almacén con ese código.", WarehouseRules.DuplicateWarehouseMessage);
        Assert.Equal("Ya existe una zona con ese código en el almacén.", WarehouseRules.DuplicateZoneMessage);
        Assert.Equal("Ya existe una posición con ese código en el almacén.", WarehouseRules.DuplicateBinMessage);
        Assert.Equal("Ya existe un muelle con ese código en el almacén.", WarehouseRules.DuplicateDockMessage);
        Assert.Equal("El almacén ALM-01 tiene inventario o documentos abiertos; no se puede dar de baja.", WarehouseRules.WarehouseNotEmpty("ALM-01"));
        Assert.Equal("La posición STG-01 tiene inventario; no se puede desactivar.", WarehouseRules.BinNotEmpty("STG-01"));
        Assert.Equal("La zona tiene posiciones activas; desactívelas primero.", WarehouseRules.ZoneHasActiveBins);
        Assert.Equal("El muelle tiene citas agendadas o en curso.", WarehouseRules.DockHasAppointments);
        Assert.Equal("El código del almacén no se puede cambiar.", WarehouseRules.WarehouseCodeImmutableMessage);
        Assert.Equal("Tipo de zona desconocido: 'FOO'.", WarehouseRules.UnknownZoneType("FOO"));
        Assert.Equal("Tipo de muelle desconocido: 'X'.", WarehouseRules.UnknownDockType("X"));
        Assert.Equal("PR", WarehouseRules.DefaultCountry);
    }
}
