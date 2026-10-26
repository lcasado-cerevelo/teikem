namespace Teikem.Domain.Tenancy;

/// <summary>
/// 2026-10-23 (Luis) — duración de la sesión del chofer en la app. Cada compañía puede fijar la suya (<c>Tenant.DriverSessionMinutes</c>, tope de la aplicación: <c>DriverApp:MaxSessionMinutes</c>);
/// sin valor vale la sesión general. Si el chofer trabaja en varias compañías manda la MÁS PERMISIVA (decisión de Luis), pero el MFA de cada compañía se cumple siempre (ver AuthService).
/// </summary>
public static class DriverSessionRules
{
    /// <summary>Tope por omisión de la sesión del chofer: 30 días.</summary>
    public const int DefaultMaxMinutes = 30 * 1440;

    /// <summary>La sesión de UNA compañía: el valor del chofer (con el tope) o, si es más largo, la sesión general. Nunca menor que la general.</summary>
    public static int CompanyMinutes(int? driverMinutes, int? generalMinutes, int maxMinutes)
    {
        var general = SessionRules.Minutes(generalMinutes);
        var driver = driverMinutes is int d && d > 0 ? Math.Min(d, Math.Max(maxMinutes, 1)) : 0;
        return Math.Max(general, driver);
    }

    /// <summary>La sesión del chofer entre varias compañías: la más permisiva. Sin compañías, la general mínima de un día.</summary>
    public static int Minutes(IEnumerable<(int? DriverMinutes, int? GeneralMinutes)> companies, int maxMinutes)
    {
        var all = companies.Select(c => CompanyMinutes(c.DriverMinutes, c.GeneralMinutes, maxMinutes)).ToList();
        return all.Count == 0 ? 1440 : all.Max();
    }
}

/// <summary>Mensajes del acceso del chofer a la app.</summary>
public static class DriverAuthMessages
{
    public const string UseTheAppMessage = "Los choferes entran con la app del chofer.";
    public const string NotADriverSessionMessage = "Esta sesión no es de la app móvil.";
    /// <summary>Lote 213: la app móvil deja entrar a quien tiene mobile.use (o es chofer activo) en alguna compañía.</summary>
    public const string NoMobileAccessMessage = "Su usuario no tiene acceso a la app móvil en ninguna compañía.";
    public const string MfaRequiredForCompany = "mfa_required";
}
