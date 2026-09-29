using Teikem.Domain.Migration;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 10 / P1: reglas puras de la migración desde QuickBooks Desktop y el WMS MSWM, con casos tomados de los datos reales
/// de Advance Depot y Advance Solutions y los mensajes exactos del reporte.
/// </summary>
public class LegacyImportRulesTests
{
    // ---------------------------------------------------------------- SKU

    [Theory]
    [InlineData("1040 P", "1040P")]
    [InlineData("NECH 1001", "NECH1001")]
    [InlineData("TAPE CLEAR", "TAPECLEAR")]
    [InlineData("171-ac-426-b", "171-AC-426-B")]
    [InlineData("  00050-7  ", "00050-7")]
    [InlineData("A\tB  C", "ABC")]
    public void NormalizeSku_removes_all_whitespace_and_uppercases(string raw, string expected)
        => Assert.Equal(expected, LegacyImportRules.NormalizeSku(raw));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NormalizeSku_empty_is_null(string? raw) => Assert.Null(LegacyImportRules.NormalizeSku(raw));

    [Fact]
    public void SkuChanged_detects_spaces_and_case_but_not_outer_trim()
    {
        Assert.True(LegacyImportRules.SkuChanged("1040 P", "1040P"));
        Assert.True(LegacyImportRules.SkuChanged("171-ac-426-b", "171-AC-426-B"));
        Assert.False(LegacyImportRules.SkuChanged("12525556", "12525556"));
        Assert.False(LegacyImportRules.SkuChanged(" 979 ", "979"));
        Assert.False(LegacyImportRules.SkuChanged(null, null));
    }

    [Fact]
    public void SkuKey_crosses_sources_without_case_or_spaces()
    {
        Assert.Equal(LegacyImportRules.SkuKey("nech 1001"), LegacyImportRules.SkuKey("NECH1001"));
        Assert.Equal("TAPECLEAR", LegacyImportRules.SkuKey(" Tape Clear "));
        Assert.Equal("", LegacyImportRules.SkuKey(""));
    }

    // ---------------------------------------------------------------- dirección de QuickBooks

    [Fact]
    public void Address_city_with_comma_and_state()
    {
        var a = LegacyImportRules.ParseQuickBooksAddress(new[] { "Farmacia Guayama", "Calle Ashford 12", "Guayama, PR 00784", null, null });
        Assert.Equal("Farmacia Guayama", a.Name);
        Assert.Equal("Calle Ashford 12", a.Line1);
        Assert.Null(a.Line2);
        Assert.Equal("Guayama", a.City);
        Assert.Equal("PR", a.State);
        Assert.Equal("00784", a.PostalCode);
        Assert.Null(a.Phone);
        Assert.True(a.CityParsed);
        Assert.False(a.IsEmpty);
    }

    [Theory]
    [InlineData("Cayey, PR  00736", "Cayey", "00736")]
    [InlineData("Añasco PR, 00610", "Añasco", "00610")]
    [InlineData("Carolina 00983", "Carolina", "00983")]
    [InlineData("TOA BAJA , PR 00949", "TOA BAJA", "00949")]
    [InlineData("Bayamón, PR 00961", "Bayamón", "00961")]
    [InlineData("San Juan, P.R. 00907-1234", "San Juan", "00907-1234")]
    [InlineData("Ponce, Puerto Rico 00731", "Ponce", "00731")]
    [InlineData("caguas, pr", "caguas", null)]
    public void Address_city_line_variants(string cityLine, string city, string? zip)
    {
        var a = LegacyImportRules.ParseQuickBooksAddress(new[] { "Cliente", "Calle 1", cityLine });
        Assert.Equal(city, a.City);
        Assert.Equal("PR", a.State);
        Assert.Equal(zip, a.PostalCode);
        Assert.Equal("Calle 1", a.Line1);
    }

    [Fact]
    public void Address_with_dotted_phone_line()
    {
        var a = LegacyImportRules.ParseQuickBooksAddress(new[]
            { "Farmacia Bayamón", "Urb. Santa Juanita", "Ave. Main 45", "Bayamón, PR 00961", "787.686.6464" });
        Assert.Equal("Urb. Santa Juanita", a.Line1);
        Assert.Equal("Ave. Main 45", a.Line2);
        Assert.Equal("Bayamón", a.City);
        Assert.Equal("00961", a.PostalCode);
        Assert.Equal("787.686.6464", a.Phone);
    }

    [Fact]
    public void Address_with_two_phones_takes_the_first()
    {
        var a = LegacyImportRules.ParseQuickBooksAddress(new[]
            { "Hospital X", "Carr. 2 Km 10", "Bayamón, PR 00961", "787.787.7733/787.743.1273" });
        Assert.Equal("787.787.7733", a.Phone);
        Assert.Equal("Carr. 2 Km 10", a.Line1);
        Assert.Null(a.Line2);
    }

    [Fact]
    public void Address_third_and_later_middle_lines_go_to_line2()
    {
        var a = LegacyImportRules.ParseQuickBooksAddress(new[] { "Cliente", "Edif. A", "Suite 3", "Piso 2", "Caguas, PR 00725" });
        Assert.Equal("Edif. A", a.Line1);
        Assert.Equal("Suite 3 / Piso 2", a.Line2);
    }

    [Fact]
    public void Address_without_city_line_uses_placeholder()
    {
        var a = LegacyImportRules.ParseQuickBooksAddress(new[] { "Cliente Sin Ciudad", "Calle Luna 5" });
        Assert.Equal("SIN CIUDAD", a.City);
        Assert.Equal("PR", a.State);
        Assert.Null(a.PostalCode);
        Assert.Equal("Calle Luna 5", a.Line1);
        Assert.False(a.CityParsed);
    }

    [Fact]
    public void Address_with_only_name_repeats_name_in_line1()
    {
        var a = LegacyImportRules.ParseQuickBooksAddress(new[] { "  AxisCare  ", "", null, "Toa Baja, PR 00949", "787-249-8344" });
        Assert.Equal("AxisCare", a.Name);
        Assert.Equal("AxisCare", a.Line1);
        Assert.Equal("Toa Baja", a.City);
        Assert.Equal("787-249-8344", a.Phone);
    }

    [Fact]
    public void Address_real_axiscare_ship_to()
    {
        var a = LegacyImportRules.ParseQuickBooksAddress(new[] { "AxisCare", "Pepsi Ind. Pk.", "PR#2 Km 19.5", "Toa Baja PR 00949", null });
        Assert.Equal("Pepsi Ind. Pk.", a.Line1);
        Assert.Equal("PR#2 Km 19.5", a.Line2);
        Assert.Equal("Toa Baja", a.City);
        Assert.Equal("00949", a.PostalCode);
    }

    [Fact]
    public void Address_empty_lines_is_empty()
    {
        var a = LegacyImportRules.ParseQuickBooksAddress(new string?[] { null, " ", "", null, null });
        Assert.True(a.IsEmpty);
        Assert.Null(a.Line1);
        Assert.Equal("SIN CIUDAD", a.City);
        Assert.Equal("PR", a.State);
    }

    [Fact]
    public void Address_short_number_is_not_a_phone()
    {
        var a = LegacyImportRules.ParseQuickBooksAddress(new[] { "Cliente", "12345", "Caguas, PR 00725" });
        Assert.Null(a.Phone);
        Assert.Equal("12345", a.Line1);
    }

    // ---------------------------------------------------------------- término de pago

    [Theory]
    [InlineData("CHEQUE", "CHEQUE")]
    [InlineData("cheque", "CHEQUE")]
    [InlineData("Cash", "CASH")]
    [InlineData("ACH", "ACH")]
    [InlineData("COD", "COD")]
    [InlineData("Due on receipt", "COD")]
    [InlineData("DUE ON RECEIPT", "COD")]
    [InlineData("Net 15", "NET15")]
    [InlineData("net 20", "NET20")]
    [InlineData("Net 30", "NET30")]
    [InlineData("NET30", "NET30")]
    [InlineData("Net 45", "NET45")]
    [InlineData("Net  60", "NET60")]
    [InlineData("Consignment", "CONSIGNMENT")]
    [InlineData("PK BY REP", "PK_BY_REP")]
    [InlineData("pk by rep", "PK_BY_REP")]
    public void MapPaymentTerm_maps_quickbooks_terms(string qb, string expected)
        => Assert.Equal(expected, LegacyImportRules.MapPaymentTerm(qb));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("Net 90")]
    [InlineData("2% 10 Net 30")]
    public void MapPaymentTerm_empty_or_unknown_is_null(string? qb) => Assert.Null(LegacyImportRules.MapPaymentTerm(qb));

    [Fact]
    public void UnknownPaymentTerm_message()
        => Assert.Equal("Término de pago de QuickBooks sin equivalente: 'Net 90'.", LegacyImportRules.UnknownPaymentTerm("Net 90"));

    // ---------------------------------------------------------------- posiciones del WMS

    [Theory]
    [InlineData("01-A-01", "01-A-01", "01", "A", "01")]
    [InlineData("01-a-24", "01-A-24", "01", "A", "24")]
    [InlineData(" 12-c-07 ", "12-C-07", "12", "C", "07")]
    public void ParseBinCode_rack_pattern(string id, string code, string aisle, string level, string position)
    {
        var b = LegacyImportRules.ParseBinCode(id);
        Assert.Equal(new BinParts(code, aisle, level, position), b);
    }

    [Theory]
    [InlineData("PISO", "PISO")]
    [InlineData("CARTONES", "CARTONES")]
    [InlineData("MATTRESS PISO DEPOT", "MATTRESS-PISO-DEPOT")]
    [InlineData("15006 MATTRESS  PISO", "15006-MATTRESS-PISO")]
    [InlineData("R1", "R1")]
    [InlineData("r1", "R1")]
    [InlineData("0101", "0101")]
    [InlineData("1-A-01", "1-A-01")]
    public void ParseBinCode_other_ids_have_no_parts(string id, string code)
    {
        var b = LegacyImportRules.ParseBinCode(id);
        Assert.Equal(code, b.Code);
        Assert.Null(b.Aisle);
        Assert.Null(b.Level);
        Assert.Null(b.Position);
    }

    // ---------------------------------------------------------------- zonas

    private static readonly ZoneRule[] DepotZones =
    {
        new("PCK", "Picking", "PICKING", "Picking Location", null),
        new("RSV", "Reserva", "RESERVE", "REGULAR", null),
        new("ALM", "Advance Logistics", "RESERVE", "ADVANCE LOGISTICS M", null),
        new("PISO", "Piso", "RESERVE", null, new[] { "PISO", "FLOOR", "CARTONES", "MATTRESS PISO DEPOT", "15006 MATTRESS PISO" }),
        new("STG", "Recepción", "STAGING", null, new[] { "R1" }),
        new("SHP", "Embarque", "STAGING", null, new[] { "S1" }),
    };

    [Theory]
    [InlineData("01-A-01", "Picking Location 01", "PCK")]
    [InlineData("01-a-24", "picking location", "PCK")]
    [InlineData("05-B-10", "REGULAR RACK", "RSV")]
    [InlineData("0101", "Advance Logistics Main", "ALM")]
    [InlineData("PISO", "§", "PISO")]
    [InlineData("cartones", null, "PISO")]
    [InlineData("MATTRESS PISO DEPOT", "Picking Location", "PISO")]   // el id gana a la descripción
    [InlineData("R1", "Receiving", "STG")]
    [InlineData("s1", "", "SHP")]
    public void ResolveZone_by_id_then_by_description(string id, string? description, string expected)
        => Assert.Equal(expected, LegacyImportRules.ResolveZone(id, description, 1, DepotZones));

    [Theory]
    [InlineData("W1", "§")]
    [InlineData("Z1", null)]
    [InlineData("99-Z-99", "Otra cosa")]
    [InlineData("01-A-01", "Location Picking")]   // debe EMPEZAR con el texto
    public void ResolveZone_without_match_is_null(string id, string? description)
        => Assert.Null(LegacyImportRules.ResolveZone(id, description, 1, DepotZones));

    [Fact]
    public void ResolveZone_no_rules_is_null()
        => Assert.Null(LegacyImportRules.ResolveZone("PISO", "Picking", 1, Array.Empty<ZoneRule>()));

    // ---------------------------------------------------------------- categorías y tipos

    [Theory]
    [InlineData(null, "AxisCare")]
    [InlineData("", "AxisCare")]
    [InlineData("   ", "AxisCare")]
    [InlineData("CARTONES", "CARTONES")]
    [InlineData("cartones", "CARTONES")]
    public void DepotCategory_maps_empty_and_cartones(string? qb, string expected)
        => Assert.Equal(expected, LegacyImportRules.DepotCategory(qb));

    [Theory]
    [InlineData("SOLUTIONS")]
    [InlineData("NATIONAL GUARD")]
    public void DepotCategory_other_is_null(string qb) => Assert.Null(LegacyImportRules.DepotCategory(qb));

    [Fact]
    public void DepotDefaultCategory_is_AxisCare() => Assert.Equal("AxisCare", LegacyImportRules.DepotDefaultCategory);

    [Fact]
    public void UnknownCategory_message()
        => Assert.Equal("Categoría de QuickBooks sin regla: 'NATIONAL GUARD'.", LegacyImportRules.UnknownCategory("NATIONAL GUARD"));

    [Theory]
    [InlineData("Inventory Part", true)]
    [InlineData("inventory part", true)]
    [InlineData(" Inventory Assembly ", true)]
    [InlineData("Service", false)]
    [InlineData("Non-inventory Part", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsInventoryType_cases(string? type, bool expected)
        => Assert.Equal(expected, LegacyImportRules.IsInventoryType(type));

    // ---------------------------------------------------------------- números

    [Theory]
    [InlineData("1,045", "1045")]
    [InlineData("-208", "-208")]
    [InlineData("0.74", "0.74")]
    [InlineData(" 12,500.50 ", "12500.50")]
    [InlineData("0", "0")]
    public void ParseQuickBooksNumber_invariant(string raw, string expected)
        => Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), LegacyImportRules.ParseQuickBooksNumber(raw));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("10.5%")]
    [InlineData("abc")]
    public void ParseQuickBooksNumber_empty_percent_or_text_is_null(string? raw)
        => Assert.Null(LegacyImportRules.ParseQuickBooksNumber(raw));

    // ---------------------------------------------------------------- correos y teléfonos

    [Fact]
    public void SplitEmails_by_semicolon_trims_and_dedupes()
    {
        var emails = LegacyImportRules.SplitEmails("compras@farmacia.test; ventas@farmacia.test;;COMPRAS@farmacia.test ; no-es-correo");
        Assert.Equal(new[] { "compras@farmacia.test", "ventas@farmacia.test" }, emails);
    }

    [Fact]
    public void SplitEmails_comma_and_space_separators()
        => Assert.Equal(new[] { "a@x.test", "b@x.test", "c@x.test" }, LegacyImportRules.SplitEmails("a@x.test, b@x.test c@x.test"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ;  ")]
    public void SplitEmails_empty(string? raw) => Assert.Empty(LegacyImportRules.SplitEmails(raw));

    [Theory]
    [InlineData(" 787-249-8344 ", "787-249-8344")]
    [InlineData("(787)  686   6464", "(787) 686 6464")]
    [InlineData("7872498", "7872498")]
    public void NormalizePhone_trims_and_collapses(string raw, string expected)
        => Assert.Equal(expected, LegacyImportRules.NormalizePhone(raw));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("787-249")]
    [InlineData("ext. 12")]
    public void NormalizePhone_less_than_seven_digits_is_null(string? raw)
        => Assert.Null(LegacyImportRules.NormalizePhone(raw));

    [Fact]
    public void NormalizePhone_max_40_characters()
    {
        var phone = LegacyImportRules.NormalizePhone("787-249-8344 ext 1234 / 787-743-1273 / 787-555-0000");
        Assert.NotNull(phone);
        Assert.True(phone!.Length <= 40);
        Assert.StartsWith("787-249-8344", phone);
    }

    // ---------------------------------------------------------------- claves y WMS

    [Fact]
    public void QuickBooksCodeKey_is_trimmed_name_as_is()
    {
        Assert.Equal("Farmacia Central Drug", LegacyImportRules.QuickBooksCodeKey("  Farmacia Central Drug "));
        Assert.Equal("AxisCare", LegacyImportRules.QuickBooksCodeKey("AxisCare"));
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("  ", true)]
    [InlineData("§", true)]
    [InlineData(" § ", true)]
    [InlineData("PISO", false)]
    public void IsWmsEmpty_cases(string? value, bool expected)
        => Assert.Equal(expected, LegacyImportRules.IsWmsEmpty(value));

    // ---------------------------------------------------------------- mensajes exactos del reporte

    [Fact]
    public void Report_messages_exact_text()
    {
        Assert.Equal("SKU '1040 P' normalizado a '1040P'.", LegacyImportRules.SkuNormalized("1040 P", "1040P"));
        Assert.Equal("El ítem 979 no tiene descripción; se usa el SKU como nombre.", LegacyImportRules.NameFromSku("979"));
        Assert.Equal("El ítem 24-081028 tiene existencia negativa (-208); no se carga saldo inicial.",
            LegacyImportRules.NegativeQuantity("24-081028", -208m));
        Assert.Equal("El SKU 886-PS4 tiene 385 unidades en el WMS y no está en QuickBooks; se crea en la categoría por defecto.",
            LegacyImportRules.UnknownWmsSku("886-PS4", 385.000m));
        Assert.Equal("La posición W1 del WMS no tiene zona destino; se omite.", LegacyImportRules.LocationSkipped("W1"));
        Assert.Equal("Registro de prueba descartado: 'prueba2'.", LegacyImportRules.TestRecordSkipped("prueba2"));
        Assert.Equal("Dirección de 'Farmacia X' sin línea de ciudad; se usa 'SIN CIUDAD'.", LegacyImportRules.AddressNotParsed("Farmacia X"));
    }

    [Fact]
    public void NegativeQuantity_formats_decimals_invariant()
        => Assert.Equal("El ítem X tiene existencia negativa (-1.5); no se carga saldo inicial.",
            LegacyImportRules.NegativeQuantity("X", -1.500m));
}
