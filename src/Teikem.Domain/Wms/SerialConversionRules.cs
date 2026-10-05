using System.Globalization;
using Teikem.Domain.Constants;

namespace Teikem.Domain.Wms;

/// <summary>Existencia de una posición que se convierte a serie: posición, su código y las unidades en mano.</summary>
public sealed record SerialConversionStock(int BinId, string BinCode, decimal OnHand);

/// <summary>
/// Lote 26 (Rentas R0) — reglas puras de la conversión de un producto sin seguimiento (NONE) a seguimiento por serie. La
/// conversión es por posición: en cada posición con existencia se capturan tantas series como unidades en mano; el servicio
/// (ProductSerialConversionService) saca el saldo sin serie con un ADJUSTMENT − y mete cada serie con un ADJUSTMENT +, ambos
/// con el motivo de sistema TRACKING_CONVERSION, y cambia el seguimiento a SERIAL en la misma transacción. Es la ÚNICA vía para
/// cambiar el seguimiento de un producto con movimientos (D25 lo bloquea en la edición).
/// Los mensajes son públicos porque el manual y la FAQ los citan.
/// </summary>
public static class SerialConversionRules
{
    /// <summary>
    /// 400 — en la posición {bin} hay {required} unidades en mano y se capturaron {captured} series (deben coincidir). Una
    /// posición sin existencia con series capturadas da el mismo mensaje con 0 unidades.
    /// </summary>
    public static string CaptureCount(int required, string bin, int captured)
        => $"Capture {required} número(s) de serie para {bin} (hay {captured}).";

    /// <summary>409 — alguna posición del producto tiene unidades reservadas (recolección, cruce de muelle u otra reserva).</summary>
    public static string ReservedUnits(string sku) => $"El producto {sku} tiene unidades reservadas; libérelas antes de convertirlo.";

    /// <summary>422 — el producto ya se controla por serie.</summary>
    public static string AlreadySerial(string sku) => $"El producto {sku} ya se controla por serie.";

    /// <summary>422 — solo se convierten productos sin seguimiento (NONE); uno por lote no.</summary>
    public static string NotUntracked(string sku) => $"Solo se convierten a serie productos sin seguimiento; {sku} se controla por lote.";

    /// <summary>422 — una posición tiene una existencia fraccionaria: no se le puede dar una serie a cada unidad.</summary>
    public static string FractionalStock(string sku, string bin, decimal qty)
        => $"La existencia de {sku} en {bin} es {Format(qty)}; ajústela a unidades enteras antes de convertirlo.";

    /// <summary>422 — existencia sin posición (solo en el almacén) o con lote: no hay a qué posición asignar las series.</summary>
    public static string StockNotInBin(string sku, string where)
        => $"La existencia de {sku} en {where} no está en una posición sin lote; muévala o ajústela antes de convertirlo.";

    /// <summary>
    /// 409 — documentos abiertos que moverían el producto sin series (recibos abiertos, tareas pendientes, recolecciones que
    /// aún se pueden eliminar o conteos abiertos): el mismo criterio que la baja del producto.
    /// </summary>
    public static string OpenDocuments(string sku)
        => $"El producto {sku} tiene recibos, tareas, recolecciones o conteos abiertos; termínelos antes de convertirlo.";

    /// <summary>Nota por defecto de los movimientos de la conversión (cuando el usuario no escribe una).</summary>
    public const string DefaultNotes = "Conversión a serie";

    /// <summary>¿La cantidad es de unidades enteras? (una serie por unidad).</summary>
    public static bool IsWholeUnits(decimal qty) => qty == decimal.Truncate(qty);

    /// <summary>
    /// Primer descuadre entre la existencia por posición y las series capturadas (orden por código de posición), o NULL si
    /// cuadra todo: cada posición con existencia necesita exactamente tantas series como unidades en mano y una posición sin
    /// existencia no admite series. captured = series capturadas por posición; binCodes = código de cada posición capturada.
    /// </summary>
    public static string? FirstCountMismatch(IReadOnlyList<SerialConversionStock> stock, IReadOnlyDictionary<int, int> captured,
        IReadOnlyDictionary<int, string> binCodes)
    {
        var rows = stock.Select(s => (s.BinId, Code: s.BinCode, Required: (int)s.OnHand)).ToList();
        foreach (var (binId, count) in captured)
            if (count > 0 && rows.All(r => r.BinId != binId))
                rows.Add((binId, binCodes.GetValueOrDefault(binId) ?? binId.ToString(CultureInfo.InvariantCulture), 0));
        foreach (var r in rows.OrderBy(r => r.Code, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.BinId))
        {
            var got = captured.GetValueOrDefault(r.BinId);
            if (got != r.Required) return CaptureCount(r.Required, r.Code, got);
        }
        return null;
    }

    /// <summary>
    /// Validación del seguimiento de origen: NONE → null (se convierte); SERIAL → AlreadySerial; cualquier otro (LOT) → NotUntracked.
    /// </summary>
    public static string? CheckSourceTracking(string? trackingCode, string sku)
    {
        var code = (trackingCode ?? TrackingTypes.None).ToUpperInvariant();
        if (code == TrackingTypes.None) return null;
        return code == TrackingTypes.Serial ? AlreadySerial(sku) : NotUntracked(sku);
    }

    private static string Format(decimal q) => q.ToString("0.###", CultureInfo.InvariantCulture);
}
