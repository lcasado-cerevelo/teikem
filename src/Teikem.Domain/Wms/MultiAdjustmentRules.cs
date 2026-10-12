using System.Globalization;

namespace Teikem.Domain.Wms;

/// <summary>
/// Reglas puras del ajuste múltiple AJM-###### (2026-10-12; docs/ajuste-multiple-diseno.md). Sin BD.
/// - «Hay ahora» vacío = la línea no se toca; igual a lo que hay en el sistema = tampoco genera movimiento; solo las líneas con diferencia
///   crean movimientos y quedan en el documento.
/// - La diferencia se calcula en el servidor contra el saldo bloqueado; si lo que vio la pantalla ya no es lo que hay → 409 con las líneas.
/// Los mensajes son públicos porque el manual y la FAQ los citan.
/// </summary>
public static class MultiAdjustmentRules
{
    public const int MaxNotesLength = AdjustmentRules.MaxNotesLength;

    public const string LinesRequired = "Escriba la cantidad que hay ahora en al menos una posición.";
    public const string NothingToAdjust = "Ninguna línea tiene diferencia con el sistema: no hay nada que ajustar.";
    public const string CountedNegative = "Lo que hay ahora no puede ser negativo.";
    public const string SystemQtyRequired = "Falta la cantidad que el sistema tenía al abrir la pantalla.";
    public const string SerialNotSupported = "Los productos con serie no se ajustan aquí: use el ajuste normal.";
    public const string LotRequired = "Este producto lleva lote: indique el lote de la línea.";
    public const string LotNotAllowed = "Este producto no lleva lote.";
    public const string Duplicated = "Esa combinación de producto, posición y lote está repetida.";

    public static string Stale(string sku, string bin, decimal current)
        => $"La existencia de {sku} en {bin} cambió mientras contaba: el sistema tiene {Fmt(current)}. Revise y confirme de nuevo.";

    public static string BelowReserved(string sku, string bin, decimal reserved)
        => $"{sku} en {bin} tiene {Fmt(reserved)} reservado: la existencia no puede quedar por debajo.";

    public static string Fmt(decimal v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>Diferencia con signo (contado − sistema); 0 = la línea no genera movimiento.</summary>
    public static decimal Difference(decimal counted, decimal system) => counted - system;

    /// <summary>Resumen del documento: líneas, productos distintos y unidades que suben y bajan (en positivo).</summary>
    public static (int Lines, int Products, decimal Up, decimal Down) Summarize(IEnumerable<(int ProductId, decimal Difference)> lines)
    {
        var list = lines.Where(l => l.Difference != 0).ToList();
        return (list.Count, list.Select(l => l.ProductId).Distinct().Count(),
            list.Where(l => l.Difference > 0).Sum(l => l.Difference), list.Where(l => l.Difference < 0).Sum(l => -l.Difference));
    }
}
