using Teikem.Domain.Constants;

namespace Teikem.Domain.Wms;

/// <summary>
/// Lote 6 — Reglas puras de números de serie. Normalización (trim, máximo 80, sin duplicados sin distinguir mayúsculas, 500
/// por línea) y estatus destino según el movimiento (D16).
/// </summary>
public static class SerialRules
{
    public const int MaxLength = 80;
    public const int MaxPerLine = 500;

    public const string Empty = "Los números de serie no pueden estar vacíos.";
    public static readonly string TooLong = $"Un número de serie admite como máximo {MaxLength} caracteres.";
    public static readonly string TooMany = $"Una línea admite como máximo {MaxPerLine} números de serie.";
    public static string Duplicated(string serial) => $"El número de serie {serial} está repetido.";

    /// <summary>'La serie {s} no está disponible en {bin}.' (409): no está AVAILABLE o no está en esa posición.</summary>
    public static string NotAvailable(string serial, string bin) => $"La serie {serial} no está disponible en {bin}.";

    /// <summary>'La serie {s} ya está en inventario.' (409): una serie en inventario no se recibe de nuevo (D34).</summary>
    public static string AlreadyInStock(string serial) => $"La serie {serial} ya está en inventario.";

    /// <summary>'La serie {s} fue dada de baja; no vuelve al inventario.' (409, D34).</summary>
    public static string Scrapped(string serial) => $"La serie {serial} fue dada de baja; no vuelve al inventario.";

    /// <summary>
    /// Normaliza la lista capturada: recorta, valida vacíos, largo, duplicados (sin distinguir mayúsculas) y tope por línea.
    /// Devuelve (series, null) o (vacío, mensaje de error). Conserva el orden de captura.
    /// </summary>
    public static (IReadOnlyList<string> Serials, string? Error) Normalize(IEnumerable<string?>? raw)
    {
        var list = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in raw ?? Enumerable.Empty<string?>())
        {
            var s = r?.Trim();
            if (string.IsNullOrEmpty(s)) return (Array.Empty<string>(), Empty);
            if (s.Length > MaxLength) return (Array.Empty<string>(), TooLong);
            if (!seen.Add(s)) return (Array.Empty<string>(), Duplicated(s));
            list.Add(s);
            if (list.Count > MaxPerLine) return (Array.Empty<string>(), TooMany);
        }
        return (list, null);
    }

    /// <summary>
    /// Estatus de la serie después del movimiento: entrada (+) → AVAILABLE; salida por ISSUE o CROSSDOCK → SHIPPED; salida por
    /// ADJUSTMENT → SCRAPPED (baja); TRANSFER → sin cambio (null).
    /// </summary>
    public static string? TargetStatus(string txnType, int sign)
    {
        if (txnType == InventoryTxnTypes.Transfer) return null;
        if (sign > 0) return SerialStatuses.Available;
        return txnType switch
        {
            InventoryTxnTypes.Issue or InventoryTxnTypes.CrossDock => SerialStatuses.Shipped,
            InventoryTxnTypes.Adjustment => SerialStatuses.Scrapped,
            _ => null,
        };
    }
}
