namespace Teikem.Domain.Tenancy;

/// <summary>
/// 2026-10-12 — duración de la sesión en MINUTOS (la pantalla la pide en horas con decimales: 24 = 1 día, 0.25 = 15 minutos). Las columnas SessionMinutes / DeviceSessionMinutes
/// (NULL = no configuradas) mandan sobre las antiguas SessionDays / DeviceSessionDays; mientras estén en NULL vale lo de siempre (días × 1440). Al guardar minutos también se
/// actualizan los días (redondeando hacia arriba) para lo que aún lea esas columnas.
/// </summary>
public static class SessionRules
{
    public const int MinMinutes = 1;
    public const int MaxMinutes = 525600;   // 365 días
    public const string MinutesRange = "La duración debe estar entre 1 minuto y 365 días (8760 horas).";

    /// <summary>Minutos efectivos: los configurados o, si no, los días de siempre × 1440.</summary>
    public static int Minutes(int? configuredMinutes, int days) => configuredMinutes is int m && m > 0 ? m : Math.Max(1, days) * 1440;

    public static bool IsValid(int minutes) => minutes is >= MinMinutes and <= MaxMinutes;

    /// <summary>Días equivalentes (hacia arriba, 1 a 365) para las columnas antiguas.</summary>
    public static int DaysFor(int minutes) => Math.Clamp((int)Math.Ceiling(minutes / 1440.0), 1, 365);
}
