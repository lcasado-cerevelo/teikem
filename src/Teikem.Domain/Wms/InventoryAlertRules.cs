using System.Security.Cryptography;
using System.Text;

namespace Teikem.Domain.Wms;

/// <summary>
/// Avisos de inventario (2026-10-20, Lote 141): reglas PURAS de qué se avisa. Lote por vencer / vencido, saldo bajo (bajo el mínimo del producto), sin existencia
/// y sin movimiento. Los umbrales (días de anticipación del vencimiento y días sin movimiento) son ajustes de la compañía. Aquí no hay base de datos: las decisiones
/// reciben fechas y cantidades y devuelven sí/no, y la clave de no repetición que evita avisar lo mismo cada día.
/// </summary>
public static class InventoryAlertRules
{
    public const int DefaultExpiryWarnDays = 30;
    public const int MaxExpiryWarnDays = 365;
    public const int DefaultNoMovementDays = 90;
    public const int MaxNoMovementDays = 3650;
    /// <summary>Cuántos renglones lleva el texto de un aviso (el resto se resume en «y N más»).</summary>
    public const int MaxItemsInText = 10;

    public const string ExpiryWarnDaysRange = "Los días de aviso de vencimiento deben estar entre 1 y 365.";
    public const string NoMovementDaysRange = "Los días sin movimiento deben estar entre 1 y 3650.";

    public static bool ValidExpiryWarnDays(int days) => days is >= 1 and <= MaxExpiryWarnDays;
    public static bool ValidNoMovementDays(int days) => days is >= 1 and <= MaxNoMovementDays;

    /// <summary>Vencido = su fecha de vencimiento es ANTERIOR a hoy (el día de vencimiento todavía se puede usar).</summary>
    public static bool IsExpired(DateOnly expiry, DateOnly today) => expiry < today;

    /// <summary>Por vencer = vence hoy o dentro de los próximos <paramref name="warnDays"/> días (y todavía no venció).</summary>
    public static bool IsExpiring(DateOnly expiry, DateOnly today, int warnDays) => expiry >= today && expiry <= today.AddDays(warnDays);

    /// <summary>Días que faltan para vencer (negativo = ya venció).</summary>
    public static int DaysToExpiry(DateOnly expiry, DateOnly today) => expiry.DayNumber - today.DayNumber;

    /// <summary>Saldo bajo = producto activo con mínimo y disponible TOTAL menor que el mínimo (el mismo cálculo de la lista de productos).</summary>
    public static bool IsLow(decimal? minQty, decimal available, bool isActive) => ProductRules.IsBelowMin(minQty, available, isActive);

    /// <summary>Sin existencia = el producto SÍ tuvo movimientos alguna vez (no es uno recién creado) y hoy no queda nada en mano.</summary>
    public static bool IsOutOfStock(bool hadMovements, decimal onHand) => hadMovements && onHand <= 0m;

    /// <summary>Sin movimiento = hay existencia pero el último movimiento del producto tiene <paramref name="days"/> días o más.</summary>
    public static bool IsStale(DateTime? lastMovementUtc, DateTime nowUtc, int days, decimal onHand)
        => onHand > 0m && lastMovementUtc is DateTime last && (nowUtc - last).TotalDays >= days;

    /// <summary>
    /// Clave de no repetición del aviso: evento + semana ISO + huella de LO QUE se avisa. El mismo conjunto no se repite en la misma semana; si cambia (entra un lote nuevo al
    /// plazo, otro producto baja del mínimo) sale un aviso nuevo, y mientras el problema siga vivo hay un recordatorio semanal. Hasta 100 caracteres.
    /// </summary>
    public static string DedupKey(string prefix, DateTime nowUtc, IEnumerable<string> ids)
    {
        var sorted = ids.OrderBy(x => x, StringComparer.Ordinal).ToArray();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('|', sorted)))).ToLowerInvariant()[..16];
        var week = System.Globalization.ISOWeek.GetWeekOfYear(nowUtc);
        var year = System.Globalization.ISOWeek.GetYear(nowUtc);
        return $"{prefix}-{year}W{week:00}-{hash}";
    }

    /// <summary>Texto de los renglones de un aviso: hasta <see cref="MaxItemsInText"/> y «… y N más».</summary>
    public static string ItemsText(IReadOnlyList<string> items)
    {
        if (items.Count <= MaxItemsInText) return string.Join("\n", items);
        return string.Join("\n", items.Take(MaxItemsInText)) + $"\n… +{items.Count - MaxItemsInText}";
    }
}
