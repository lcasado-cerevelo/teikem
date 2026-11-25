namespace Teikem.Infrastructure.Tax;

/// <summary>Una línea de un documento tal como se guardó: su tasa (copia), el porcentaje realmente cobrado, el impuesto guardado y si es exenta.</summary>
public sealed record TaxBreakdownLine(int? TaxRateId, decimal TaxRatePct, decimal TaxAmount, bool IsTaxExempt);

/// <summary>Qué representa un renglón del desglose.</summary>
public enum TaxBreakdownKind
{
    /// <summary>Un componente de la tasa (p. ej. «IVU estatal»); Name = el nombre del componente.</summary>
    Component,
    /// <summary>Una tasa sin componentes (o una línea sin tasa): un solo renglón «IVU».</summary>
    Plain,
    /// <summary>Exención parcial que no se pudo deducir del porcentaje cobrado (ambigua o inexistente): «IVU (sin desglose)».</summary>
    Unsplit,
    /// <summary>El IVU del envío cuando la orden usa varias tasas: «IVU del envío».</summary>
    Shipping,
}

/// <summary>
/// Un renglón del desglose. Percent = el porcentaje del componente (null si no aplica o si difiere entre tasas). IsExempt = el componente
/// quedó exento en TODAS las líneas donde aparece (se muestra «exento», Amount = 0).
/// </summary>
public sealed record TaxBreakdownRow(TaxBreakdownKind Kind, string Name, decimal? Percent, decimal Amount, bool IsExempt = false);

/// <summary>
/// Lote 243 — desglose del impuesto YA GUARDADO de un documento (la orden de venta en el correo) entre los componentes de su tasa
/// (TaxRateComponent: IVU estatal y municipal). Lógica pura sobre <see cref="TaxRules.Allocate"/>:
/// - Agrupa las líneas que llevan impuesto por (TaxRateId, TaxRatePct).
/// - TaxRatePct == Percent de la tasa: el impuesto del grupo se reparte entre todos los componentes.
/// - TaxRatePct &lt; Percent: exención parcial; se busca el subconjunto ÚNICO de componentes cuya suma es la diferencia (esos quedan
///   exentos en el grupo). Ambiguo o inexistente → renglón «IVU (sin desglose)».
/// - Tasa sin componentes (o línea sin tasa) → renglón «IVU».
/// - El IVU del envío se suma al grupo SOLO si toda la orden usa una sola tasa; si hay varias (o ninguna), va en su renglón «IVU del envío».
/// - Cada grupo se reparte a 6 decimales, se suma por componente (por nombre) y se redondea a los decimales de la moneda con RESIDUO
///   MAYOR A NIVEL DE ORDEN: la suma mostrada es exactamente el impuesto de las líneas más el del envío.
/// </summary>
public static class TaxBreakdownRules
{
    public const int AllocationDecimals = 6;

    public const string PlainLabelEs = "IVU";
    public const string PlainLabelEn = "Sales tax";
    public const string UnsplitLabelEs = "IVU (sin desglose)";
    public const string UnsplitLabelEn = "Sales tax (not itemized)";
    public const string ShippingLabelEs = "IVU del envío";
    public const string ShippingLabelEn = "Shipping tax";

    /// <summary>Etiqueta del renglón en el idioma pedido (el componente lleva su propio nombre).</summary>
    public static string Label(TaxBreakdownRow row, string? lang)
    {
        var en = string.Equals(lang, "en", StringComparison.OrdinalIgnoreCase);
        return row.Kind switch
        {
            TaxBreakdownKind.Plain => en ? PlainLabelEn : PlainLabelEs,
            TaxBreakdownKind.Unsplit => en ? UnsplitLabelEn : UnsplitLabelEs,
            TaxBreakdownKind.Shipping => en ? ShippingLabelEn : ShippingLabelEs,
            _ => row.Name,
        };
    }

    /// <param name="lines">Las líneas del documento (las exentas o sin impuesto no aportan).</param>
    /// <param name="shippingTax">El IVU del envío (0 = sin envío o sin IVU).</param>
    /// <param name="rates">Tasas por TaxRateId con sus componentes en orden; una tasa que falta se trata como sin componentes.</param>
    /// <param name="currencyDecimals">Decimales de la moneda (0, 2 o 3).</param>
    public static IReadOnlyList<TaxBreakdownRow> Compute(IReadOnlyList<TaxBreakdownLine> lines, decimal shippingTax,
        IReadOnlyDictionary<int, TaxRateInfo> rates, int currencyDecimals)
    {
        var decimals = Math.Clamp(currencyDecimals, 0, 4);
        var groups = lines.Where(l => l.TaxAmount != 0m)
            .GroupBy(l => (l.TaxRateId, l.TaxRatePct))
            .Select(g => (Key: g.Key, Tax: g.Sum(l => l.TaxAmount)))
            .ToList();
        var shippingInGroup = groups.Count == 1 && shippingTax != 0m;
        if (shippingInGroup) groups[0] = (groups[0].Key, groups[0].Tax + shippingTax);

        // acumuladores en orden de aparición: (Kind, Name) → monto a 6 decimales, porcentaje y exención
        var buckets = new List<Bucket>();
        Bucket BucketOf(TaxBreakdownKind kind, string name, decimal? percent)
        {
            var b = buckets.FirstOrDefault(x => x.Kind == kind && string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
            if (b is null)
            {
                b = new Bucket(kind, name) { Percent = percent };
                buckets.Add(b);
            }
            else if (b.Percent != percent) b.Percent = null;
            return b;
        }

        foreach (var (key, tax) in groups)
        {
            var rate = key.TaxRateId is int id ? rates.GetValueOrDefault(id) : null;
            if (rate is null || rate.Components.Count == 0 || rate.Percent <= 0m)
            {
                var plain = BucketOf(TaxBreakdownKind.Plain, PlainLabelEs, null);
                plain.Raw += tax;
                plain.AllExempt = false;
                continue;
            }
            IReadOnlySet<string>? exempt = null;
            if (key.TaxRatePct != rate.Percent)
            {
                exempt = key.TaxRatePct < rate.Percent ? UniqueExemptSubset(rate, rate.Percent - key.TaxRatePct) : null;
                if (exempt is null)
                {
                    var unsplit = BucketOf(TaxBreakdownKind.Unsplit, UnsplitLabelEs, null);
                    unsplit.Raw += tax;
                    unsplit.AllExempt = false;
                    continue;
                }
            }
            foreach (var part in TaxRules.Allocate(tax, rate, AllocationDecimals, exempt))
            {
                var b = BucketOf(TaxBreakdownKind.Component, part.Name, part.Percent);
                b.Raw += part.Amount;
                if (!part.IsExempt) b.AllExempt = false;
            }
        }
        if (!shippingInGroup && shippingTax != 0m)
        {
            var s = BucketOf(TaxBreakdownKind.Shipping, ShippingLabelEs, null);
            s.Raw += shippingTax;
            s.AllExempt = false;
        }

        var target = lines.Where(l => l.TaxAmount != 0m).Sum(l => l.TaxAmount) + shippingTax;
        var amounts = LargestRemainder(buckets.Select(b => b.Raw).ToList(), target, decimals);
        return buckets.Select((b, i) => new TaxBreakdownRow(b.Kind, b.Name, b.Percent, b.AllExempt ? 0m : amounts[i], b.AllExempt)).ToList();
    }

    /// <summary>
    /// El subconjunto ÚNICO de componentes (de porcentaje mayor que cero) cuya suma de porcentajes es <paramref name="difference"/>, como
    /// códigos; null si no hay ninguno o si hay más de uno (ambiguo). Una tasa tiene pocos componentes (hasta 10): se prueban todos.
    /// </summary>
    public static IReadOnlySet<string>? UniqueExemptSubset(TaxRateInfo rate, decimal difference)
    {
        var comps = rate.Components.Where(c => c.Percent > 0m).ToList();
        if (comps.Count == 0 || comps.Count > 16 || difference <= 0m) return null;
        HashSet<string>? found = null;
        var total = 1 << comps.Count;
        for (var mask = 1; mask < total; mask++)
        {
            decimal sum = 0m;
            for (var i = 0; i < comps.Count; i++)
                if ((mask & (1 << i)) != 0) sum += comps[i].Percent;
            if (sum != difference) continue;
            if (found is not null) return null;   // ambiguo
            found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < comps.Count; i++)
                if ((mask & (1 << i)) != 0) found.Add(comps[i].Code);
        }
        return found;
    }

    /// <summary>
    /// Redondea cada monto a <paramref name="decimals"/> con residuo mayor: todos hacia abajo y las unidades que faltan para llegar a
    /// <paramref name="target"/> van, una por una, a los de mayor residuo (empate: el primero). La suma es exactamente el objetivo.
    /// </summary>
    public static IReadOnlyList<decimal> LargestRemainder(IReadOnlyList<decimal> raw, decimal target, int decimals)
    {
        if (raw.Count == 0) return Array.Empty<decimal>();
        var unit = Unit(decimals);
        var floors = raw.Select(r => Math.Floor(r / unit) * unit).ToArray();
        var left = target - floors.Sum();
        var order = raw.Select((r, i) => (Index: i, Remainder: r - floors[i])).OrderByDescending(x => x.Remainder).ThenBy(x => x.Index).Select(x => x.Index).ToList();
        var k = 0;
        while (left >= unit && k < order.Count)
        {
            floors[order[k++]] += unit;
            left -= unit;
        }
        if (left != 0m) floors[order[0]] += left;   // objetivo con más decimales que la moneda (o negativo): lo absorbe el de mayor residuo
        return floors;
    }

    private static decimal Unit(int decimals)
    {
        var u = 1m;
        for (var i = 0; i < decimals; i++) u /= 10m;
        return u;
    }

    private sealed class Bucket(TaxBreakdownKind kind, string name)
    {
        public TaxBreakdownKind Kind { get; } = kind;
        public string Name { get; } = name;
        public decimal? Percent { get; set; }
        public decimal Raw { get; set; }
        public bool AllExempt { get; set; } = true;
    }
}
