using Teikem.Domain.Wms;

namespace Teikem.Domain.Fleet;

/// <summary>
/// Avisos de flota (2026-10-21, Lote 168): reglas PURAS de qué se avisa de los documentos de vehículos (registro, seguro, inspección, permiso) y de choferes
/// (licencias y certificaciones). Documento vencido = su fecha de vencimiento es ANTERIOR a hoy; por vencer = vence hoy o dentro de los próximos N días (ajuste de la
/// compañía). Un documento SIN fecha de vencimiento no avisa nunca. La clave de no repetición es la misma de los avisos de inventario (semana ISO + huella del conjunto).
/// </summary>
public static class FleetAlertRules
{
    public const int DefaultDocWarnDays = 30;
    public const int MaxDocWarnDays = 365;

    public const string DocWarnDaysRange = "Los días de aviso de documentos de la flota deben estar entre 1 y 365.";

    public static bool ValidDocWarnDays(int days) => days is >= 1 and <= MaxDocWarnDays;

    public static bool IsExpired(DateOnly? expiry, DateOnly today) => expiry is DateOnly e && InventoryAlertRules.IsExpired(e, today);

    public static bool IsExpiring(DateOnly? expiry, DateOnly today, int warnDays) => expiry is DateOnly e && InventoryAlertRules.IsExpiring(e, today, warnDays);

    /// <summary>Días que faltan para vencer (negativo = ya venció).</summary>
    public static int DaysToExpiry(DateOnly expiry, DateOnly today) => InventoryAlertRules.DaysToExpiry(expiry, today);
}
