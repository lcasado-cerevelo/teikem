namespace Teikem.Domain.Tenancy;

/// <summary>
/// Duración de la sesión en MINUTOS (la pantalla la pide en horas con decimales: 24 = 1 día, 0.25 = 15 minutos). Las columnas SessionMinutes / DeviceSessionMinutes de la compañía
/// (NULL = no configuradas) valen, mientras estén en NULL, el valor por omisión de 30 días.
/// </summary>
public static class SessionRules
{
    public const int MinMinutes = 1;
    public const int MaxMinutes = 525600;   // 365 días
    /// <summary>Duración por omisión de una sesión sin configurar: 30 días.</summary>
    public const int DefaultMinutes = 30 * 1440;
    public const string MinutesRange = "La duración debe estar entre 1 minuto y 365 días (8760 horas).";

    /// <summary>Minutos efectivos: los configurados o, si no, los 30 días por omisión.</summary>
    public static int Minutes(int? configuredMinutes) => configuredMinutes is int m && m > 0 ? m : DefaultMinutes;

    public static bool IsValid(int minutes) => minutes is >= MinMinutes and <= MaxMinutes;
}
