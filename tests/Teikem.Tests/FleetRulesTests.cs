using Teikem.Domain.Constants;
using Teikem.Domain.Fleet;
using Xunit;

namespace Teikem.Tests;

/// <summary>Lote 4 / P0: reglas puras compartidas de Flota (FleetRules) y la regla 'documento vigente por tipo' (FleetDocuments).</summary>
public class FleetRulesTests
{
    private static readonly DateOnly Today = new(2026, 9, 26);

    // ---------------------------------------------------------------- códigos y búsqueda

    [Fact]
    public void NormalizeCode_trims_and_uppercases()
        => Assert.Equal(("V-001", (string?)null), FleetRules.NormalizeCode("  v-001 ", 30, "req"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NormalizeCode_empty_returns_the_required_message(string? raw)
        => Assert.Equal(((string?)null, "El código del vehículo es obligatorio."), FleetRules.NormalizeCode(raw, 30, "El código del vehículo es obligatorio."));

    [Fact]
    public void NormalizeCode_too_long_returns_the_length_message()
    {
        Assert.Equal(((string?)null, "El código no puede exceder 30 caracteres."), FleetRules.NormalizeCode(new string('A', 31), 30, "req"));
        Assert.Equal(new string('A', 30), FleetRules.NormalizeCode(new string('a', 30), 30, "req").Code);
    }

    [Theory]
    [InlineData("diesel", true)]
    [InlineData("DIÉSEL", true)]
    [InlineData("1ftfw", true)]       // parte del VIN en minúsculas
    [InlineData("van diesel", true)]  // cada palabra en algún campo
    [InlineData("truck", false)]
    [InlineData("", true)]
    [InlineData(null, true)]
    public void MatchesSearch_ignores_accents_and_case(string? query, bool expected)
        => Assert.Equal(expected, FleetRules.MatchesSearch(query, "V-001", "ABC123", "Diésel", "DIESEL", "Van", "1FTFW1E50NFA00001", null));

    [Fact]
    public void MatchesSearch_without_fields_does_not_match_a_query()
        => Assert.False(FleetRules.MatchesSearch("x", null, ""));

    [Fact]
    public void EffectiveMaxStops_uses_own_value_or_tenant_default()
    {
        Assert.Equal(25, FleetRules.EffectiveMaxStops(null, 25));
        Assert.Equal(18, FleetRules.EffectiveMaxStops(18, 25));
        Assert.Null(FleetRules.EffectiveMaxStops(null, null));
    }

    // ---------------------------------------------------------------- fechas y vencimientos

    [Fact]
    public void ValidateDocumentDates_rejects_expiry_before_issue()
    {
        Assert.Equal("La fecha de vencimiento no puede ser anterior a la de emisión.", FleetRules.ValidateDocumentDates(Today, Today.AddDays(-1)));
        Assert.Null(FleetRules.ValidateDocumentDates(Today, Today));
        Assert.Null(FleetRules.ValidateDocumentDates(null, Today));
        Assert.Null(FleetRules.ValidateDocumentDates(Today, null));
    }

    [Fact]
    public void ExpiryState_borders()
    {
        Assert.Equal(ExpiryStates.Expired, FleetRules.ExpiryState(Today.AddDays(-1), Today));
        Assert.Equal(ExpiryStates.Expiring, FleetRules.ExpiryState(Today, Today));
        Assert.Equal(0, FleetRules.DaysTo(Today, Today));
        Assert.Equal(ExpiryStates.Expiring, FleetRules.ExpiryState(Today.AddDays(30), Today));
        Assert.Equal(ExpiryStates.Ok, FleetRules.ExpiryState(Today.AddDays(31), Today));
        Assert.Equal(ExpiryStates.NoExpiry, FleetRules.ExpiryState(null, Today));
        Assert.Equal(ExpiryStates.Ok, FleetRules.ExpiryState(Today.AddDays(8), Today, withinDays: 7));
        Assert.Equal(-1, FleetRules.DaysTo(Today.AddDays(-1), Today));
    }

    // ---------------------------------------------------------------- precisión DECIMAL y odómetro

    [Fact]
    public void DecimalError_detects_scale_and_magnitude()
    {
        var scale = FleetRules.DecimalError(1.23456m, 18, 4);
        Assert.Equal("El valor admite como máximo 4 decimales y debe ser menor que 100,000,000,000,000.", scale);
        Assert.Contains("4 decimales", scale);
        Assert.NotNull(FleetRules.DecimalError(100_000_000_000_000m, 18, 4));   // 1e14
        Assert.Null(FleetRules.DecimalError(99_999_999_999_999.9999m, 18, 4));
        Assert.NotNull(FleetRules.DecimalError(10_000_000m, 10, 3));
        Assert.Equal("El valor admite como máximo 3 decimales y debe ser menor que 10,000,000.", FleetRules.DecimalError(10_000_000m, 10, 3));
        Assert.Null(FleetRules.DecimalError(9_999_999.999m, 10, 3));
        Assert.Null(FleetRules.DecimalError(null, 12, 1));
        Assert.Null(FleetRules.DecimalError(1.2000m, 12, 1));                  // ceros a la derecha no cuentan
        Assert.NotNull(FleetRules.DecimalError(-100_000_000_000m, 12, 1));     // magnitud en valor absoluto
    }

    [Fact]
    public void RaiseOdometer_is_monotonic()
    {
        Assert.Equal(5m, FleetRules.RaiseOdometer(null, 5m));
        Assert.Equal(6000m, FleetRules.RaiseOdometer(6000m, 5500m));
        Assert.Equal(6500m, FleetRules.RaiseOdometer(6000m, 6500m));
        Assert.Equal(6000m, FleetRules.RaiseOdometer(6000m, null));
        Assert.Null(FleetRules.RaiseOdometer(null, null));
    }

    // ---------------------------------------------------------------- documento vigente por tipo

    private static FleetDocumentRow Row(string key, string kind, string type, DateOnly? expiry, int ownerId = 1, string ownerKind = FleetOwnerKinds.Vehicle)
        => new(key, ownerKind, ownerId, Guid.Empty, "C", "N", true, false, kind, 1, type, null, null, expiry);

    private static bool Superseded(IReadOnlyList<FleetDocumentRow> rows, string key) => rows.Single(r => r.RowKey == key).IsSuperseded;

    [Fact]
    public void Expired_registration_renewed_by_a_later_one_is_superseded()
    {
        var rows = FleetDocuments.MarkSuperseded(new[]
        {
            Row("VD-1", "REGISTRATION", "REGISTRATION", Today.AddDays(-1)),
            Row("VD-2", "REGISTRATION", "REGISTRATION", Today.AddDays(365)),
        });
        Assert.True(Superseded(rows, "VD-1"));
        Assert.False(Superseded(rows, "VD-2"));
        Assert.Equal(new[] { "VD-1", "VD-2" }, rows.Select(r => r.RowKey)); // conserva el orden
    }

    [Fact]
    public void Different_types_or_owners_do_not_supersede_each_other()
    {
        var types = FleetDocuments.MarkSuperseded(new[]
        {
            Row("VD-1", "REGISTRATION", "REGISTRATION", Today.AddDays(-1)),
            Row("VD-2", "INSURANCE", "INSURANCE", Today.AddDays(365)),
        });
        Assert.All(types, r => Assert.False(r.IsSuperseded));

        var owners = FleetDocuments.MarkSuperseded(new[]
        {
            Row("VD-1", "REGISTRATION", "REGISTRATION", Today.AddDays(-1), ownerId: 1),
            Row("VD-2", "REGISTRATION", "REGISTRATION", Today.AddDays(365), ownerId: 2),
        });
        Assert.All(owners, r => Assert.False(r.IsSuperseded));
    }

    [Fact]
    public void Documents_without_expiry_never_supersede_nor_are_superseded()
    {
        var rows = FleetDocuments.MarkSuperseded(new[]
        {
            Row("VD-1", "PERMIT", "PERMIT", Today.AddDays(-10)),
            Row("VD-2", "PERMIT", "PERMIT", null),
        });
        Assert.All(rows, r => Assert.False(r.IsSuperseded));
    }

    [Fact]
    public void Same_expiry_does_not_supersede()
    {
        var rows = FleetDocuments.MarkSuperseded(new[]
        {
            Row("VD-1", "INSURANCE", "INSURANCE", Today.AddDays(10)),
            Row("VD-2", "INSURANCE", "INSURANCE", Today.AddDays(10)),
        });
        Assert.All(rows, r => Assert.False(r.IsSuperseded));
    }

    [Fact]
    public void Licenses_of_different_classes_are_independent()
    {
        var rows = FleetDocuments.MarkSuperseded(new[]
        {
            Row("DL-1", FleetDocumentKinds.License, "CDL_A", Today.AddDays(-5), ownerKind: FleetOwnerKinds.Driver),
            Row("DL-2", FleetDocumentKinds.License, "CDL_B", Today.AddDays(300), ownerKind: FleetOwnerKinds.Driver),
            Row("DL-3", FleetDocumentKinds.License, "CDL_A", Today.AddDays(200), ownerKind: FleetOwnerKinds.Driver),
        });
        Assert.True(Superseded(rows, "DL-1"));
        Assert.False(Superseded(rows, "DL-2"));
        Assert.False(Superseded(rows, "DL-3"));
    }

    [Fact]
    public void A_license_and_a_certification_with_the_same_type_code_are_independent()
    {
        var rows = FleetDocuments.MarkSuperseded(new[]
        {
            Row("DL-1", FleetDocumentKinds.License, "REEFER", Today.AddDays(-5), ownerKind: FleetOwnerKinds.Driver),
            Row("DC-1", FleetDocumentKinds.Certification, "REEFER", Today.AddDays(300), ownerKind: FleetOwnerKinds.Driver),
        });
        Assert.All(rows, r => Assert.False(r.IsSuperseded));
    }
}
