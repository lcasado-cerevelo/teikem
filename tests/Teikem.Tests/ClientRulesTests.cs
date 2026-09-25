using Teikem.Domain.Clients;
using Xunit;

namespace Teikem.Tests;

/// <summary>Lote 2 / P1: reglas puras de numeración por cliente y código de cliente (sin BD).</summary>
public class NumberFormatTests
{
    [Fact]
    public void Resolve_pads_with_zeros_to_the_number_of_hashes()
    {
        Assert.Equal("AX-00001", NumberFormat.Resolve("AX-#####", 1));
        Assert.Equal("AX-12345", NumberFormat.Resolve("AX-#####", 12345));
    }

    [Fact]
    public void Resolve_overflow_never_truncates()
    {
        Assert.Equal("AX-123", NumberFormat.Resolve("AX-##", 123));
        Assert.Equal("AX-123-Z", NumberFormat.Resolve("AX-##-Z", 123));
    }

    [Fact]
    public void Resolve_replaces_at_with_series_letter()
    {
        Assert.Equal("AA-007", NumberFormat.Resolve("@@-###", 7));
        Assert.Equal("BB-007", NumberFormat.Resolve("@@-###", 7, 'B'));
    }

    [Fact]
    public void Validate_rejects_pattern_without_hash_empty_too_long_and_bad_chars()
    {
        Assert.NotNull(NumberFormat.Validate("ORD-"));
        Assert.NotNull(NumberFormat.Validate("AX"));
        Assert.NotNull(NumberFormat.Validate(""));
        Assert.NotNull(NumberFormat.Validate(null));
        Assert.NotNull(NumberFormat.Validate(new string('#', 41)));
        Assert.NotNull(NumberFormat.Validate("AX ###"));
        Assert.NotNull(NumberFormat.Validate("AX*###"));
        Assert.NotNull(NumberFormat.Validate("AÑ-###"));
        Assert.False(NumberFormat.IsValid("AX"));
    }

    [Fact]
    public void Validate_accepts_typical_patterns()
    {
        Assert.Null(NumberFormat.Validate("FAC-####"));
        Assert.Null(NumberFormat.Validate("ORD/2026-#####"));
        Assert.Null(NumberFormat.Validate("@@_#.#"));
        Assert.Null(NumberFormat.Validate(new string('#', 40)));
        Assert.True(NumberFormat.IsValid("FAC-####"));
    }

    [Fact]
    public void Defaults_exist_and_validate()
    {
        Assert.Equal("ORD-#####", NumberFormat.Defaults.Order);
        Assert.Equal("FAC-#####", NumberFormat.Defaults.Invoice);
        Assert.Equal("PQT-#####", NumberFormat.Defaults.Package);
        Assert.Null(NumberFormat.Validate(NumberFormat.Defaults.Order));
        Assert.Null(NumberFormat.Validate(NumberFormat.Defaults.Invoice));
        Assert.Null(NumberFormat.Validate(NumberFormat.Defaults.Package));
        Assert.Equal("ORD-00001", NumberFormat.Resolve(NumberFormat.Defaults.Order, 1));
    }

    [Fact]
    public void Resolve_rejects_negative_sequence()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => NumberFormat.Resolve("AX-###", -1));
    }
}

public class ClientCodeTests
{
    [Fact]
    public void FromName_uppercases_and_strips_diacritics()
    {
        Assert.Equal("FARMACIA-LAS-MARIAS", ClientCode.FromName("Farmacia Las Marías"));
    }

    [Fact]
    public void FromName_replaces_symbols_with_dash_and_collapses()
    {
        Assert.Equal("BEAUTY-CODE-INC", ClientCode.FromName("Beauty  Code, Inc."));
        Assert.Equal("ACME-2026", ClientCode.FromName("  ¡¡Acme (2026)!!  "));
    }

    [Fact]
    public void FromName_truncates_to_20_without_trailing_dash()
    {
        // "DISTRIBUIDORA-NACIONAL" → 20 chars sería "DISTRIBUIDORA-NACION"
        Assert.Equal("DISTRIBUIDORA-NACION", ClientCode.FromName("Distribuidora Nacional del Caribe"));
        // Corte justo en un guion: "SUPERMERCADOS-ECONO" (19) + "-" → se quita el guion final
        var code = ClientCode.FromName("Supermercados Econo Xpress");
        Assert.True(code.Length <= ClientCode.MaxLength);
        Assert.False(code.EndsWith('-'));
        Assert.Equal("SUPERMERCADOS-ECONO", code);
    }

    [Fact]
    public void FromName_rejects_empty_or_symbol_only_names()
    {
        Assert.Throws<ArgumentException>(() => ClientCode.FromName(""));
        Assert.Throws<ArgumentException>(() => ClientCode.FromName("   "));
        Assert.Throws<ArgumentException>(() => ClientCode.FromName(null));
        Assert.Throws<ArgumentException>(() => ClientCode.FromName("¡¡¡ --- !!!"));
    }

    [Fact]
    public void WithSuffix_keeps_max_length()
    {
        Assert.Equal("ACME-2", ClientCode.WithSuffix("ACME", 2));
        var longBase = new string('A', 20);
        var s = ClientCode.WithSuffix(longBase, 12);
        Assert.True(s.Length <= ClientCode.MaxLength);
        Assert.EndsWith("-12", s);
    }
}
