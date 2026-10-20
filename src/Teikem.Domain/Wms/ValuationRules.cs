namespace Teikem.Domain.Wms;

/// <summary>Método de valuación del inventario de la compañía (Tenant.ValuationMethod): costo promedio o por capas (lo primero que entró, FIFO).</summary>
public static class ValuationMethods
{
    public const string Average = "AVERAGE";
    public const string Layers = "LAYERS";
    public static readonly string[] All = { Average, Layers };
}

/// <summary>Una capa de costo (línea de recibo confirmada): cuántas unidades entraron y a qué costo unitario (null = sin costo congelado). Se ordenan de la más vieja a la más nueva.</summary>
public sealed record CostLayer(decimal Qty, decimal? UnitCost);

/// <summary>Valor de lo que hay en mano de un producto: total, costo unitario resultante y unidades que no se pudieron valuar (ni capa ni costo del producto).</summary>
public sealed record ValuationResult(decimal Value, decimal? UnitCost, decimal UnvaluedQty);

/// <summary>
/// Lote 148 — valuación del inventario sobre las capas de costo del Lote 147. Dos métodos, por compañía:
/// - <b>AVERAGE</b>: costo promedio ponderado de todas las capas recibidas del producto; el valor es lo que hay en mano × ese promedio. Una salida cuesta el promedio.
/// - <b>LAYERS</b> (lo primero que entró, FIFO): lo que hay en mano se atribuye a las capas MÁS NUEVAS (lo viejo se fue primero); el valor es la suma de esas capas. Una salida cuesta la capa
///   más vieja que aún queda.
/// Una capa sin costo congelado (o lo que hay en mano más allá de todas las capas, p. ej. inventario anterior al Lote 147) se valúa al costo del producto; sin él, queda sin valuar.
/// </summary>
public static class ValuationRules
{
    public const string Invalid = "El método de valuación debe ser AVERAGE (costo promedio) o LAYERS (por capas).";

    public static string? Parse(string? value)
    {
        var v = value?.Trim().ToUpperInvariant();
        return v is not null && ValuationMethods.All.Contains(v) ? v : null;
    }

    private static decimal? Effective(CostLayer l, decimal? productCost) => l.UnitCost ?? productCost;

    /// <summary>Promedio ponderado de las capas (con el costo del producto en las que no traen); el costo del producto si no hay capas; null si no hay nada.</summary>
    public static decimal? Average(IReadOnlyList<CostLayer> layers, decimal? productCost)
    {
        decimal qty = 0m, amount = 0m;
        foreach (var l in layers)
        {
            if (l.Qty <= 0m || Effective(l, productCost) is not decimal c) continue;
            qty += l.Qty;
            amount += l.Qty * c;
        }
        return qty > 0m ? decimal.Round(amount / qty, 4, MidpointRounding.AwayFromZero) : productCost;
    }

    public static ValuationResult Value(string? method, decimal onHand, IReadOnlyList<CostLayer> layersOldestFirst, decimal? productCost)
    {
        if (onHand <= 0m) return new ValuationResult(0m, null, 0m);
        if (Parse(method) == ValuationMethods.Layers)
        {
            decimal left = onHand, value = 0m, unvalued = 0m;
            for (var i = layersOldestFirst.Count - 1; i >= 0 && left > 0m; i--)
            {
                var take = Math.Min(layersOldestFirst[i].Qty, left);
                if (take <= 0m) continue;
                left -= take;
                if (Effective(layersOldestFirst[i], productCost) is decimal c) value += take * c;
                else unvalued += take;
            }
            if (left > 0m)
            {
                if (productCost is decimal pc) value += left * pc;
                else unvalued += left;
            }
            value = decimal.Round(value, 4, MidpointRounding.AwayFromZero);
            return new ValuationResult(value, unvalued >= onHand ? null : decimal.Round(value / (onHand - unvalued), 4, MidpointRounding.AwayFromZero), unvalued);
        }
        var avg = Average(layersOldestFirst, productCost);
        return avg is decimal a
            ? new ValuationResult(decimal.Round(onHand * a, 4, MidpointRounding.AwayFromZero), a, 0m)
            : new ValuationResult(0m, null, onHand);
    }

    /// <summary>
    /// Costo unitario de una salida cuando hay <paramref name="onHandBefore"/> en mano ANTES de sacar: el promedio (AVERAGE) o la capa más vieja que aún queda (LAYERS). Sin capas, el costo del
    /// producto. null si no hay costo en ningún lado.
    /// </summary>
    public static decimal? IssueUnitCost(string? method, decimal onHandBefore, IReadOnlyList<CostLayer> layersOldestFirst, decimal? productCost)
    {
        if (Parse(method) != ValuationMethods.Layers) return Average(layersOldestFirst, productCost);
        if (layersOldestFirst.Count == 0 || onHandBefore <= 0m) return productCost;
        decimal left = onHandBefore;
        decimal? oldest = null;
        for (var i = layersOldestFirst.Count - 1; i >= 0 && left > 0m; i--)
        {
            var take = Math.Min(layersOldestFirst[i].Qty, left);
            if (take <= 0m) continue;
            left -= take;
            oldest = Effective(layersOldestFirst[i], productCost);
        }
        return left > 0m ? productCost : oldest;   // si sobra existencia sin capa, lo más viejo es inventario anterior: costo del producto
    }
}
