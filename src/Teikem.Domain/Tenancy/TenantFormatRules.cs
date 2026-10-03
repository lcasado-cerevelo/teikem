using System.Text.RegularExpressions;

namespace Teikem.Domain.Tenancy;

/// <summary>
/// Región y formatos de la compañía (pedido de Luis, 2026-10): el juego completo de valores con que se muestran fechas, horas,
/// dinero, números y teléfonos, y la zona horaria con que se cuenta "hoy". Mismo orden y tipos que las columnas de dbo.Tenant.
/// </summary>
public sealed record TenantFormat(
    string RegionCode, string TimeZoneId, string CurrencyCode, string CurrencySymbol, string CurrencySymbolPosition, byte CurrencyDecimals,
    string DateOrder, string DateSeparator, byte TimeFormat, byte WeekStartDay, string ThousandsSeparator, string DecimalSeparator,
    string PhoneCountryCode, string PhoneMask);

/// <summary>
/// Cambio parcial de región y formatos: null = sin cambio (igual que el resto de PUT /tenant/settings). Lo usan los ajustes de la
/// compañía y el aprovisionamiento.
/// </summary>
public sealed record TenantFormatChanges(
    string? RegionCode = null, string? TimeZoneId = null, string? CurrencyCode = null, string? CurrencySymbol = null,
    string? CurrencySymbolPosition = null, byte? CurrencyDecimals = null, string? DateOrder = null, string? DateSeparator = null,
    byte? TimeFormat = null, byte? WeekStartDay = null, string? ThousandsSeparator = null, string? DecimalSeparator = null,
    string? PhoneCountryCode = null, string? PhoneMask = null)
{
    /// <summary>¿Trae algún campo de formato (cualquiera menos la región)?</summary>
    public bool HasFormatFields =>
        TimeZoneId is not null || CurrencyCode is not null || CurrencySymbol is not null || CurrencySymbolPosition is not null
        || CurrencyDecimals is not null || DateOrder is not null || DateSeparator is not null || TimeFormat is not null
        || WeekStartDay is not null || ThousandsSeparator is not null || DecimalSeparator is not null || PhoneCountryCode is not null
        || PhoneMask is not null;

    /// <summary>¿No trae nada (ni región ni formatos)?</summary>
    public bool IsEmpty => RegionCode is null && !HasFormatFields;
}

/// <summary>
/// Reglas puras de región y formatos de la compañía (espejo de los CHECK de dbo.Tenant en Diseño/logistica-db-estructura.sql).
/// - Solo hay dos regiones, Puerto Rico (PR) y Estados Unidos (US) (decisión de Luis); cada una trae un juego completo de valores
///   por defecto (<see cref="Defaults"/>) y cada campo se puede cambiar por separado.
/// - Cambio de región (<see cref="Apply"/>): si la región pedida es distinta de la actual, se parte de los valores por defecto
///   de la nueva región y los campos que vengan explícitos mandan sobre esos valores (sin campos = la región completa). Sin
///   cambio de región, los campos explícitos se aplican sobre los valores actuales.
/// - La zona horaria debe conocerla la plataforma (<see cref="TimeZoneInfo.TryFindSystemTimeZoneById"/>); un id de Windows
///   (p. ej. "SA Western Standard Time") se acepta y se guarda con su nombre IANA, como ya tolera LocalDay.
/// Mensajes públicos: los usan el servicio (400 ValidationException), las pruebas y el manual (FAQ).
/// </summary>
public static class TenantFormatRules
{
    // ------------------------------------------------------------------ regiones
    public const string PuertoRico = "PR";
    public const string UnitedStates = "US";
    /// <summary>Región de una compañía nueva si no se indica otra.</summary>
    public const string DefaultRegion = PuertoRico;
    public static readonly IReadOnlyList<string> Regions = [PuertoRico, UnitedStates];

    public const string PuertoRicoTimeZone = "America/Puerto_Rico";
    public const string UnitedStatesTimeZone = "America/New_York";

    /// <summary>
    /// Orden de fecha por defecto de Puerto Rico. POR CONFIRMAR con el dueño (bitácora del documento maestro): se escogió MM/DD/AAAA
    /// por la práctica comercial local; si se decide DD/MM/AAAA basta cambiar esta constante (y el default de la columna
    /// dbo.Tenant.DateOrder en el script de estructura, que hoy reproduce este valor).
    /// </summary>
    public const string PuertoRicoDateOrder = DateOrders.MonthDayYear;

    // ------------------------------------------------------------------ valores permitidos (mismos que los CHECK)
    public static class SymbolPositions
    {
        /// <summary>Símbolo antes del monto ($1,234.50).</summary>
        public const string Before = "B";
        /// <summary>Símbolo después del monto (1.234,50 €).</summary>
        public const string After = "A";
        public static readonly IReadOnlyList<string> All = [Before, After];
    }

    public static class DateOrders
    {
        public const string MonthDayYear = "MDY";
        public const string DayMonthYear = "DMY";
        public const string YearMonthDay = "YMD";
        public static readonly IReadOnlyList<string> All = [MonthDayYear, DayMonthYear, YearMonthDay];
    }

    public static readonly IReadOnlyList<byte> CurrencyDecimalsAllowed = [0, 2, 3];
    public static readonly IReadOnlyList<string> DateSeparators = ["/", "-", "."];
    public static readonly IReadOnlyList<byte> TimeFormats = [12, 24];
    /// <summary>0 = domingo, 1 = lunes.</summary>
    public static readonly IReadOnlyList<byte> WeekStartDays = [0, 1];
    /// <summary>Coma, punto o espacio.</summary>
    public static readonly IReadOnlyList<string> ThousandsSeparators = [",", ".", " "];
    public static readonly IReadOnlyList<string> DecimalSeparators = [".", ","];

    /// <summary>Largos de columna (dbo.Tenant).</summary>
    public const int TimeZoneIdMaxLength = 64;
    public const int CurrencySymbolMaxLength = 3;
    public const int PhoneMaskMaxLength = 30;
    /// <summary>Carácter de la máscara de teléfono que representa un dígito.</summary>
    public const char PhoneMaskDigit = '#';

    // ------------------------------------------------------------------ mensajes (400)
    public static string UnknownRegion(string value) => $"Región desconocida: '{value}'. Use PR (Puerto Rico) o US (Estados Unidos).";
    public const string TimeZoneRequired = "Indique la zona horaria de la compañía, por ejemplo America/Puerto_Rico.";
    public static string UnknownTimeZone(string value)
        => $"La zona horaria '{value}' no la reconoce la plataforma. Use un nombre IANA, por ejemplo America/Puerto_Rico o America/New_York.";
    public const string CurrencyCodeInvalid = "El código de moneda debe ser de 3 letras mayúsculas (ISO 4217), por ejemplo USD.";
    public const string CurrencySymbolInvalid = "El símbolo de moneda es obligatorio y de 1 a 3 caracteres, por ejemplo $.";
    public const string CurrencySymbolPositionInvalid = "La posición del símbolo de moneda debe ser B (antes del monto) o A (después).";
    public const string CurrencyDecimalsInvalid = "Los decimales de la moneda deben ser 0, 2 o 3.";
    public const string DateOrderInvalid = "El orden de la fecha debe ser MDY (mes/día/año), DMY (día/mes/año) o YMD (año/mes/día).";
    public const string DateSeparatorInvalid = "El separador de fecha debe ser '/', '-' o '.'.";
    public const string TimeFormatInvalid = "El formato de hora debe ser 12 o 24.";
    public const string WeekStartDayInvalid = "El primer día de la semana debe ser 0 (domingo) o 1 (lunes).";
    public const string ThousandsSeparatorInvalid = "El separador de miles debe ser coma (','), punto ('.') o espacio (' ').";
    public const string DecimalSeparatorInvalid = "El separador decimal debe ser punto ('.') o coma (',').";
    public const string SameSeparators = "El separador de miles y el decimal no pueden ser el mismo";
    public const string PhoneCountryCodeInvalid = "El código de país del teléfono debe ser '+' seguido de 1 a 4 dígitos, por ejemplo +1.";
    public const string PhoneMaskInvalid
        = "La máscara de teléfono debe tener de 1 a 30 caracteres sin acentos y al menos un '#' (cada # es un dígito), por ejemplo (###) ###-####.";

    /// <summary>Nombres de campo del error (los del JSON del request).</summary>
    public static class Fields
    {
        public const string RegionCode = "regionCode";
        public const string TimeZoneId = "timeZoneId";
        public const string CurrencyCode = "currencyCode";
        public const string CurrencySymbol = "currencySymbol";
        public const string CurrencySymbolPosition = "currencySymbolPosition";
        public const string CurrencyDecimals = "currencyDecimals";
        public const string DateOrder = "dateOrder";
        public const string DateSeparator = "dateSeparator";
        public const string TimeFormat = "timeFormat";
        public const string WeekStartDay = "weekStartDay";
        public const string ThousandsSeparator = "thousandsSeparator";
        public const string DecimalSeparator = "decimalSeparator";
        public const string PhoneCountryCode = "phoneCountryCode";
        public const string PhoneMask = "phoneMask";
    }

    private static readonly Regex CurrencyCodePattern = new("^[A-Z]{3}$", RegexOptions.CultureInvariant);
    private static readonly Regex PhoneCountryCodePattern = new(@"^\+[0-9]{1,4}$", RegexOptions.CultureInvariant);

    // ------------------------------------------------------------------ regiones

    public static bool IsRegion(string? regionCode) => regionCode is not null && Regions.Contains(regionCode);

    /// <summary>
    /// Valores por defecto de la región (PR o US, ya normalizada). Solo cambian la zona horaria y, si el dueño lo decide, el orden
    /// de la fecha de Puerto Rico (<see cref="PuertoRicoDateOrder"/>); lo demás es igual: USD, '$' antes, 2 decimales, '/', 12 h,
    /// semana desde el domingo, miles ',', decimal '.', +1 y (###) ###-####. Región desconocida → ArgumentOutOfRangeException
    /// (el servicio la valida antes con <see cref="NormalizeRegion"/>).
    /// </summary>
    public static TenantFormat Defaults(string regionCode) => regionCode switch
    {
        PuertoRico => new TenantFormat(PuertoRico, PuertoRicoTimeZone, "USD", "$", SymbolPositions.Before, 2,
            PuertoRicoDateOrder, "/", 12, 0, ",", ".", "+1", "(###) ###-####"),
        UnitedStates => new TenantFormat(UnitedStates, UnitedStatesTimeZone, "USD", "$", SymbolPositions.Before, 2,
            DateOrders.MonthDayYear, "/", 12, 0, ",", ".", "+1", "(###) ###-####"),
        _ => throw new ArgumentOutOfRangeException(nameof(regionCode), regionCode, UnknownRegion(regionCode)),
    };

    /// <summary>¿Los valores son exactamente los de su región? (false = la pantalla la muestra como "Personalizada").</summary>
    public static bool MatchesRegionDefaults(TenantFormat format)
        => IsRegion(format.RegionCode) && format == Defaults(format.RegionCode);

    /// <summary>Región sin espacios y en mayúsculas; desconocida → mensaje del 400.</summary>
    public static (string? Region, string? Error) NormalizeRegion(string value)
    {
        var v = value.Trim().ToUpperInvariant();
        return IsRegion(v) ? (v, null) : (null, UnknownRegion(value.Trim()));
    }

    // ------------------------------------------------------------------ zona horaria

    /// <summary>
    /// Zona horaria reconocida por la plataforma, guardada con su nombre IANA. Acepta el id de Windows equivalente (en Windows y
    /// Linux .NET los traduce, como LocalDay.ResolveZone) y lo traduce al IANA de la región de la compañía cuando existe
    /// ("SA Western Standard Time" en PR → America/Puerto_Rico; sin región, el IANA principal de esa zona de Windows).
    /// Vacía → <see cref="TimeZoneRequired"/>; desconocida o demasiado larga → <see cref="UnknownTimeZone"/>.
    /// </summary>
    public static (string? ZoneId, string? Error) NormalizeTimeZone(string value, string? regionCode = null)
    {
        var v = value.Trim();
        if (v.Length == 0) return (null, TimeZoneRequired);
        if (v.Length > TimeZoneIdMaxLength || !TimeZoneInfo.TryFindSystemTimeZoneById(v, out var tz)) return (null, UnknownTimeZone(v));
        if (tz.HasIanaId && !IsWindowsId(v)) return (IanaCasing(v, tz), null);
        // Id de Windows: se guarda su nombre IANA si la plataforma lo sabe; si no, el id tal cual (la plataforma lo reconoce).
        string? iana = null;
        if (regionCode is not null && TimeZoneInfo.TryConvertWindowsIdToIanaId(v, regionCode, out var regional)) iana = regional;
        else if (TimeZoneInfo.TryConvertWindowsIdToIanaId(v, out var primary)) iana = primary;
        return iana is not null && iana.Length <= TimeZoneIdMaxLength ? (iana, null) : (v, null);
    }

    /// <summary>¿Es un id de Windows con equivalente IANA? (en Windows, FindSystemTimeZoneById lo devuelve con HasIanaId = false).</summary>
    private static bool IsWindowsId(string id) => TimeZoneInfo.TryConvertWindowsIdToIanaId(id, out _);

    /// <summary>En Windows el id IANA se encuentra sin distinguir mayúsculas; se conserva el del sistema si coincide.</summary>
    private static string IanaCasing(string requested, TimeZoneInfo tz)
        => string.Equals(tz.Id, requested, StringComparison.OrdinalIgnoreCase) ? tz.Id : requested;

    // ------------------------------------------------------------------ aplicar y validar

    /// <summary>
    /// Aplica un cambio parcial sobre los valores actuales y valida el resultado. Regla de región: región distinta de la actual →
    /// se parte de sus valores por defecto y los campos explícitos mandan; sin cambio de región → los campos explícitos sobre los
    /// actuales. Devuelve el primer error (campo del JSON y mensaje del 400) o el formato final normalizado.
    /// </summary>
    public static (TenantFormat? Format, string? Field, string? Error) Apply(TenantFormat current, TenantFormatChanges changes)
    {
        var baseFormat = current;
        if (changes.RegionCode is not null)
        {
            var (region, regionError) = NormalizeRegion(changes.RegionCode);
            if (region is null) return (null, Fields.RegionCode, regionError);
            if (region != current.RegionCode) baseFormat = Defaults(region);
        }

        var f = baseFormat;
        if (changes.TimeZoneId is not null)
        {
            var (zone, zoneError) = NormalizeTimeZone(changes.TimeZoneId, f.RegionCode);
            if (zone is null) return (null, Fields.TimeZoneId, zoneError);
            f = f with { TimeZoneId = zone };
        }
        if (changes.CurrencyCode is not null) f = f with { CurrencyCode = changes.CurrencyCode.Trim().ToUpperInvariant() };
        if (changes.CurrencySymbol is not null) f = f with { CurrencySymbol = changes.CurrencySymbol.Trim() };
        if (changes.CurrencySymbolPosition is not null) f = f with { CurrencySymbolPosition = changes.CurrencySymbolPosition.Trim().ToUpperInvariant() };
        if (changes.CurrencyDecimals is byte decimals) f = f with { CurrencyDecimals = decimals };
        if (changes.DateOrder is not null) f = f with { DateOrder = changes.DateOrder.Trim().ToUpperInvariant() };
        if (changes.DateSeparator is not null) f = f with { DateSeparator = changes.DateSeparator };
        if (changes.TimeFormat is byte timeFormat) f = f with { TimeFormat = timeFormat };
        if (changes.WeekStartDay is byte weekStart) f = f with { WeekStartDay = weekStart };
        // Los separadores no se recortan: el espacio es un separador de miles válido.
        if (changes.ThousandsSeparator is not null) f = f with { ThousandsSeparator = changes.ThousandsSeparator };
        if (changes.DecimalSeparator is not null) f = f with { DecimalSeparator = changes.DecimalSeparator };
        if (changes.PhoneCountryCode is not null) f = f with { PhoneCountryCode = changes.PhoneCountryCode.Trim() };
        if (changes.PhoneMask is not null) f = f with { PhoneMask = changes.PhoneMask.Trim() };

        var error = Validate(f);
        return error is { } e ? (null, e.Field, e.Message) : (f, null, null);
    }

    /// <summary>
    /// Valida un juego completo (cada CHECK de dbo.Tenant y la combinación de separadores). La zona solo se comprueba aquí si es
    /// desconocida para la plataforma. Devuelve el primer error en el orden de la pantalla, o null.
    /// </summary>
    public static (string Field, string Message)? Validate(TenantFormat f)
    {
        if (!IsRegion(f.RegionCode)) return (Fields.RegionCode, UnknownRegion(f.RegionCode));
        if (string.IsNullOrWhiteSpace(f.TimeZoneId)) return (Fields.TimeZoneId, TimeZoneRequired);
        if (f.TimeZoneId.Length > TimeZoneIdMaxLength || !TimeZoneInfo.TryFindSystemTimeZoneById(f.TimeZoneId, out _))
            return (Fields.TimeZoneId, UnknownTimeZone(f.TimeZoneId));
        if (!CurrencyCodePattern.IsMatch(f.CurrencyCode)) return (Fields.CurrencyCode, CurrencyCodeInvalid);
        if (f.CurrencySymbol.Length is 0 or > CurrencySymbolMaxLength || string.IsNullOrWhiteSpace(f.CurrencySymbol))
            return (Fields.CurrencySymbol, CurrencySymbolInvalid);
        if (!SymbolPositions.All.Contains(f.CurrencySymbolPosition)) return (Fields.CurrencySymbolPosition, CurrencySymbolPositionInvalid);
        if (!CurrencyDecimalsAllowed.Contains(f.CurrencyDecimals)) return (Fields.CurrencyDecimals, CurrencyDecimalsInvalid);
        if (!DateOrders.All.Contains(f.DateOrder)) return (Fields.DateOrder, DateOrderInvalid);
        if (!DateSeparators.Contains(f.DateSeparator)) return (Fields.DateSeparator, DateSeparatorInvalid);
        if (!TimeFormats.Contains(f.TimeFormat)) return (Fields.TimeFormat, TimeFormatInvalid);
        if (!WeekStartDays.Contains(f.WeekStartDay)) return (Fields.WeekStartDay, WeekStartDayInvalid);
        if (!ThousandsSeparators.Contains(f.ThousandsSeparator)) return (Fields.ThousandsSeparator, ThousandsSeparatorInvalid);
        if (!DecimalSeparators.Contains(f.DecimalSeparator)) return (Fields.DecimalSeparator, DecimalSeparatorInvalid);
        if (f.ThousandsSeparator == f.DecimalSeparator) return (Fields.DecimalSeparator, SameSeparators);
        if (!PhoneCountryCodePattern.IsMatch(f.PhoneCountryCode)) return (Fields.PhoneCountryCode, PhoneCountryCodeInvalid);
        if (!IsValidPhoneMask(f.PhoneMask)) return (Fields.PhoneMask, PhoneMaskInvalid);
        return null;
    }

    /// <summary>De 1 a 30 caracteres ASCII visibles o espacio (la columna es VARCHAR) y al menos un '#'.</summary>
    public static bool IsValidPhoneMask(string? mask)
        => !string.IsNullOrEmpty(mask) && mask.Length <= PhoneMaskMaxLength && mask.Contains(PhoneMaskDigit)
           && mask.All(c => c is >= ' ' and <= '~');

    // ------------------------------------------------------------------ entidad

    /// <summary>Formato actual de la compañía.</summary>
    public static TenantFormat Read(Tenant t) => new(
        t.RegionCode, t.TimeZoneId, t.CurrencyCode, t.CurrencySymbol, t.CurrencySymbolPosition, t.CurrencyDecimals,
        t.DateOrder, t.DateSeparator, t.TimeFormat, t.WeekStartDay, t.ThousandsSeparator, t.DecimalSeparator,
        t.PhoneCountryCode, t.PhoneMask);

    /// <summary>Copia un formato ya validado a la compañía (la auditoría registra solo las columnas que cambian).</summary>
    public static void Write(Tenant t, TenantFormat f)
    {
        t.RegionCode = f.RegionCode;
        t.TimeZoneId = f.TimeZoneId;
        t.CurrencyCode = f.CurrencyCode;
        t.CurrencySymbol = f.CurrencySymbol;
        t.CurrencySymbolPosition = f.CurrencySymbolPosition;
        t.CurrencyDecimals = f.CurrencyDecimals;
        t.DateOrder = f.DateOrder;
        t.DateSeparator = f.DateSeparator;
        t.TimeFormat = f.TimeFormat;
        t.WeekStartDay = f.WeekStartDay;
        t.ThousandsSeparator = f.ThousandsSeparator;
        t.DecimalSeparator = f.DecimalSeparator;
        t.PhoneCountryCode = f.PhoneCountryCode;
        t.PhoneMask = f.PhoneMask;
    }
}
