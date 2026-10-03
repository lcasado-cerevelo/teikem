using Teikem.Domain.Tenancy;
using Xunit;
using R = Teikem.Domain.Tenancy.TenantFormatRules;

namespace Teikem.Tests;

/// <summary>
/// Región y formatos de la compañía (2026-10) — reglas puras (TenantFormatRules): dos regiones (PR, US) con su juego completo de
/// valores por defecto, los defaults de la entidad iguales a los de dbo.Tenant (Puerto Rico), cada CHECK de la tabla con su
/// mensaje exacto del 400, la combinación de separadores, la zona reconocida por la plataforma (IANA o Windows) y la regla de
/// región: región distinta sin campos = valores de la región; con campos = mandan los explícitos sobre los de la región.
/// </summary>
public class TenantFormatRulesTests
{
    private static readonly TenantFormat Pr = R.Defaults(R.PuertoRico);
    private static readonly TenantFormat Us = R.Defaults(R.UnitedStates);

    [Fact]
    public void Only_two_regions_with_their_defaults()
    {
        Assert.Equal(new[] { "PR", "US" }, R.Regions);
        Assert.Equal(new TenantFormat("PR", "America/Puerto_Rico", "USD", "$", "B", 2, "MDY", "/", 12, 0, ",", ".", "+1", "(###) ###-####"), Pr);
        Assert.Equal(new TenantFormat("US", "America/New_York", "USD", "$", "B", 2, "MDY", "/", 12, 0, ",", ".", "+1", "(###) ###-####"), Us);
        Assert.Null(R.Validate(Pr));
        Assert.Null(R.Validate(Us));
        // El orden de fecha de Puerto Rico es un default POR CONFIRMAR: vive en una sola constante.
        Assert.Equal(R.PuertoRicoDateOrder, Pr.DateOrder);
        Assert.Equal("MDY", R.PuertoRicoDateOrder);
        Assert.Throws<ArgumentOutOfRangeException>(() => R.Defaults("MX"));
    }

    [Fact]
    public void A_new_tenant_entity_has_the_puerto_rico_defaults_like_the_table()
    {
        var t = new Tenant();
        Assert.Equal(Pr, R.Read(t));
        Assert.True(R.MatchesRegionDefaults(R.Read(t)));
    }

    [Fact]
    public void Write_and_read_round_trip_every_column()
    {
        var f = new TenantFormat("US", "Pacific/Honolulu", "EUR", "€", "A", 3, "YMD", "-", 24, 1, " ", ",", "+34", "### ## ## ##");
        var t = new Tenant();
        R.Write(t, f);
        Assert.Equal(f, R.Read(t));
        Assert.False(R.MatchesRegionDefaults(f));
    }

    // ------------------------------------------------------------------ regla de región

    [Fact]
    public void Another_region_without_fields_loads_its_full_set()
    {
        var customized = Pr with { TimeFormat = 24, DateOrder = "DMY", ThousandsSeparator = ".", DecimalSeparator = "," };
        var (f, field, error) = R.Apply(customized, new TenantFormatChanges(RegionCode: "US"));
        Assert.Null(error);
        Assert.Null(field);
        Assert.Equal(Us, f);
    }

    [Fact]
    public void Another_region_with_fields_takes_the_region_and_the_explicit_fields_win()
    {
        var (f, _, error) = R.Apply(Pr with { TimeFormat = 24 }, new TenantFormatChanges(RegionCode: "US", DateOrder: "DMY"));
        Assert.Null(error);
        Assert.Equal(Us with { DateOrder = "DMY" }, f);   // hora 12 de la región (no la 24 de antes) y el orden explícito
        Assert.False(R.MatchesRegionDefaults(f!));
    }

    [Fact]
    public void Same_region_keeps_the_current_values_and_fields_apply_over_them()
    {
        var customized = Pr with { TimeFormat = 24 };
        Assert.Equal(customized, R.Apply(customized, new TenantFormatChanges(RegionCode: "PR")).Format);
        Assert.Equal(customized with { WeekStartDay = 1 }, R.Apply(customized, new TenantFormatChanges(WeekStartDay: 1)).Format);
        Assert.Equal(customized with { WeekStartDay = 1 }, R.Apply(customized, new TenantFormatChanges(RegionCode: "pr", WeekStartDay: 1)).Format);
    }

    [Fact]
    public void Changes_know_whether_they_carry_format_fields()
    {
        Assert.True(new TenantFormatChanges().IsEmpty);
        Assert.False(new TenantFormatChanges(RegionCode: "US").HasFormatFields);
        Assert.False(new TenantFormatChanges(RegionCode: "US").IsEmpty);
        Assert.True(new TenantFormatChanges(PhoneMask: "#").HasFormatFields);
        Assert.True(new TenantFormatChanges(WeekStartDay: 0).HasFormatFields);
    }

    [Fact]
    public void Values_are_normalized()
    {
        var (f, _, error) = R.Apply(Pr, new TenantFormatChanges(RegionCode: " us ", CurrencyCode: "eur", CurrencySymbolPosition: "a", DateOrder: "ymd",
            CurrencySymbol: " € ", PhoneCountryCode: " +34 ", PhoneMask: " ### ### ### "));
        Assert.Null(error);
        Assert.Equal(("US", "EUR", "A", "YMD", "€", "+34", "### ### ###"),
            (f!.RegionCode, f.CurrencyCode, f.CurrencySymbolPosition, f.DateOrder, f.CurrencySymbol, f.PhoneCountryCode, f.PhoneMask));
    }

    [Fact]
    public void A_space_is_a_valid_thousands_separator_and_is_not_trimmed()
    {
        var (f, _, error) = R.Apply(Pr, new TenantFormatChanges(ThousandsSeparator: " ", DecimalSeparator: ","));
        Assert.Null(error);
        Assert.Equal((" ", ","), (f!.ThousandsSeparator, f.DecimalSeparator));
    }

    // ------------------------------------------------------------------ cada CHECK con su mensaje

    public static TheoryData<TenantFormatChanges, string, string> Invalid => new()
    {
        { new TenantFormatChanges(RegionCode: "MX"), "regionCode", "Región desconocida: 'MX'. Use PR (Puerto Rico) o US (Estados Unidos)." },
        { new TenantFormatChanges(TimeZoneId: "  "), "timeZoneId", "Indique la zona horaria de la compañía, por ejemplo America/Puerto_Rico." },
        { new TenantFormatChanges(TimeZoneId: "Mars/Olympus_Mons"), "timeZoneId",
            "La zona horaria 'Mars/Olympus_Mons' no la reconoce la plataforma. Use un nombre IANA, por ejemplo America/Puerto_Rico o America/New_York." },
        { new TenantFormatChanges(CurrencyCode: "US"), "currencyCode", "El código de moneda debe ser de 3 letras mayúsculas (ISO 4217), por ejemplo USD." },
        { new TenantFormatChanges(CurrencyCode: "U$D"), "currencyCode", "El código de moneda debe ser de 3 letras mayúsculas (ISO 4217), por ejemplo USD." },
        { new TenantFormatChanges(CurrencySymbol: ""), "currencySymbol", "El símbolo de moneda es obligatorio y de 1 a 3 caracteres, por ejemplo $." },
        { new TenantFormatChanges(CurrencySymbol: "US$$"), "currencySymbol", "El símbolo de moneda es obligatorio y de 1 a 3 caracteres, por ejemplo $." },
        { new TenantFormatChanges(CurrencySymbolPosition: "X"), "currencySymbolPosition", "La posición del símbolo de moneda debe ser B (antes del monto) o A (después)." },
        { new TenantFormatChanges(CurrencyDecimals: 1), "currencyDecimals", "Los decimales de la moneda deben ser 0, 2 o 3." },
        { new TenantFormatChanges(CurrencyDecimals: 4), "currencyDecimals", "Los decimales de la moneda deben ser 0, 2 o 3." },
        { new TenantFormatChanges(DateOrder: "DYM"), "dateOrder", "El orden de la fecha debe ser MDY (mes/día/año), DMY (día/mes/año) o YMD (año/mes/día)." },
        { new TenantFormatChanges(DateSeparator: " "), "dateSeparator", "El separador de fecha debe ser '/', '-' o '.'." },
        { new TenantFormatChanges(DateSeparator: "//"), "dateSeparator", "El separador de fecha debe ser '/', '-' o '.'." },
        { new TenantFormatChanges(TimeFormat: 13), "timeFormat", "El formato de hora debe ser 12 o 24." },
        { new TenantFormatChanges(WeekStartDay: 6), "weekStartDay", "El primer día de la semana debe ser 0 (domingo) o 1 (lunes)." },
        { new TenantFormatChanges(ThousandsSeparator: "'"), "thousandsSeparator", "El separador de miles debe ser coma (','), punto ('.') o espacio (' ')." },
        { new TenantFormatChanges(ThousandsSeparator: ""), "thousandsSeparator", "El separador de miles debe ser coma (','), punto ('.') o espacio (' ')." },
        { new TenantFormatChanges(DecimalSeparator: " "), "decimalSeparator", "El separador decimal debe ser punto ('.') o coma (',')." },
        { new TenantFormatChanges(PhoneCountryCode: "1"), "phoneCountryCode", "El código de país del teléfono debe ser '+' seguido de 1 a 4 dígitos, por ejemplo +1." },
        { new TenantFormatChanges(PhoneCountryCode: "+12345"), "phoneCountryCode", "El código de país del teléfono debe ser '+' seguido de 1 a 4 dígitos, por ejemplo +1." },
        { new TenantFormatChanges(PhoneMask: "(xxx) xxx-xxxx"), "phoneMask",
            "La máscara de teléfono debe tener de 1 a 30 caracteres sin acentos y al menos un '#' (cada # es un dígito), por ejemplo (###) ###-####." },
        { new TenantFormatChanges(PhoneMask: new string('#', 31)), "phoneMask",
            "La máscara de teléfono debe tener de 1 a 30 caracteres sin acentos y al menos un '#' (cada # es un dígito), por ejemplo (###) ###-####." },
        { new TenantFormatChanges(PhoneMask: "### ñ ###"), "phoneMask",
            "La máscara de teléfono debe tener de 1 a 30 caracteres sin acentos y al menos un '#' (cada # es un dígito), por ejemplo (###) ###-####." },
    };

    [Theory]
    [MemberData(nameof(Invalid))]
    public void Each_check_rejects_with_its_exact_message(TenantFormatChanges changes, string field, string message)
    {
        var (f, actualField, error) = R.Apply(Pr, changes);
        Assert.Null(f);
        Assert.Equal(field, actualField);
        Assert.Equal(message, error);
    }

    [Theory]
    [InlineData(".", null)]    // miles '.' contra el decimal '.' guardado
    [InlineData(".", ".")]     // los dos en el mismo pedido
    [InlineData(null, ",")]    // decimal ',' contra los miles ',' guardados
    public void Thousands_and_decimal_separators_can_never_be_the_same(string? thousands, string? dec)
    {
        var (f, field, error) = R.Apply(Pr, new TenantFormatChanges(ThousandsSeparator: thousands, DecimalSeparator: dec));
        Assert.Null(f);
        Assert.Equal("decimalSeparator", field);
        Assert.Equal("El separador de miles y el decimal no pueden ser el mismo", error);
        Assert.Equal(R.SameSeparators, error);
    }

    [Fact]
    public void Swapping_both_separators_at_once_is_valid()
    {
        var (f, _, error) = R.Apply(Pr, new TenantFormatChanges(ThousandsSeparator: ".", DecimalSeparator: ","));
        Assert.Null(error);
        Assert.Equal((".", ","), (f!.ThousandsSeparator, f.DecimalSeparator));
    }

    [Theory]
    [InlineData((byte)0)]
    [InlineData((byte)2)]
    [InlineData((byte)3)]
    public void Currency_decimals_allowed(byte decimals) => Assert.Null(R.Apply(Pr, new TenantFormatChanges(CurrencyDecimals: decimals)).Error);

    [Theory]
    [InlineData("#")]
    [InlineData("###-####")]
    [InlineData("+# (###) ###-####")]
    public void Phone_masks_need_at_least_one_digit_placeholder(string mask) => Assert.True(R.IsValidPhoneMask(mask));

    // ------------------------------------------------------------------ zona horaria

    [Theory]
    [InlineData("America/Puerto_Rico")]
    [InlineData("America/New_York")]
    [InlineData("Pacific/Honolulu")]
    [InlineData("Europe/Madrid")]
    public void Iana_zones_known_by_the_platform_are_kept(string zone)
    {
        var (f, _, error) = R.Apply(Pr, new TenantFormatChanges(TimeZoneId: zone));
        Assert.Null(error);
        Assert.Equal(zone, f!.TimeZoneId);
    }

    [Fact]
    public void A_windows_zone_id_is_accepted_and_stored_with_the_iana_name_of_the_region()
    {
        // Como LocalDay: en Linux y en Windows .NET reconoce ambos ids; se guarda el IANA de la región de la compañía.
        Assert.Equal(("America/Puerto_Rico", (string?)null), R.NormalizeTimeZone("SA Western Standard Time", "PR"));
        Assert.Equal(("America/New_York", (string?)null), R.NormalizeTimeZone("Eastern Standard Time", "US"));
        var (f, _, error) = R.Apply(Us, new TenantFormatChanges(TimeZoneId: "Hawaiian Standard Time"));
        Assert.Null(error);
        Assert.Equal("Pacific/Honolulu", f!.TimeZoneId);
    }

    [Fact]
    public void A_zone_id_longer_than_the_column_is_unknown()
    {
        var (_, field, error) = R.Apply(Pr, new TenantFormatChanges(TimeZoneId: new string('A', 65)));
        Assert.Equal("timeZoneId", field);
        Assert.StartsWith("La zona horaria '", error);
    }
}
