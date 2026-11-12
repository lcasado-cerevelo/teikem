using System.Globalization;
using System.Text;

namespace Teikem.Domain.Sales;

/// <summary>Promedio, mínimo y máximo mensual de un producto (unidades), con el mejor mes y cuántos meses completos se usaron.</summary>
public sealed record MonthlyStats(decimal? Average, decimal? Min, decimal? Max, DateOnly? BestMonth, int MonthsUsed);

/// <summary>
/// Venta semanal de las últimas 8 semanas completas: semanas usadas (desde la primera semana de la ventana con venta), semanas con venta, promedio, desviación
/// (poblacional) y la confianza del cálculo (<see cref="SalesAnalysisRules.ConfidenceNone"/>…).
/// </summary>
public sealed record WeeklyStats(int WeeksUsed, int WeeksWithSales, decimal? Average, decimal? StdDev, string Confidence);

/// <summary>Una pieza de un kit para el prorrateo: producto, cuántas lleva cada kit y su precio de lista (el peso).</summary>
public sealed record SalesKitPart(int ProductId, decimal QuantityPerKit, decimal? ListPrice);

/// <summary>Lo que le toca a una pieza de un kit vendido: producto, unidades y monto prorrateado.</summary>
public sealed record SalesKitShare(int ProductId, decimal Quantity, decimal Amount);

/// <summary>Conciliación del mes: total de rutas + sin representante + envío y otros = total del mes (Difference = lo que no cuadra; debería ser 0).</summary>
public sealed record SalesReconciliation(decimal RoutesTotal, decimal Unassigned, decimal ShippingAndOther, decimal MonthTotal, decimal Difference);

/// <summary>Una fila de compra del historial vista por la regla de duplicados (Item Receipt y su Bill).</summary>
public sealed record PurchaseDupRow(string Type, string? Number, string Supplier, string ItemKey, decimal Quantity);

/// <summary>
/// Lote 241 — reglas PURAS del análisis de ventas (sin BD; las prueba xunit). Las definiciones salen de la hoja «Notas» de Movimiento_de_productos.xlsx y de la hoja
/// «Reorder point» de Solutions_Items (decisiones de Luis):
/// - ABC: A = los productos que juntos hacen el 80 % de la venta en $ (el producto es A si lo acumulado ANTES de él es menor que 80 %), B = el siguiente 15 %, C = el resto;
///   sin venta positiva = «-».
/// - Estado: Sin ventas | Parado (más de 45 días sin vender) | Nuevo (empezó a vender en el mes anterior al último mes completo o después) | Subiendo / Bajando (el último
///   mes completo contra el promedio de los dos anteriores, cambio de 25 % o más) | Estable.
/// - Promedio / mínimo / máximo mensual: unidades por mes de los meses COMPLETOS desde el primer mes con ventas (los meses sin venta cuentan 0).
/// - Promedio semanal: las últimas 8 semanas completas (lunes a domingo) antes de la semana del día de corte; se usan las semanas desde la primera de la ventana con venta.
/// - Cobertura en semanas = disponible ÷ promedio semanal; en meses = semanas ÷ <see cref="WeeksPerMonth"/>.
/// - Tendencia: el último mes completo contra el promedio de los dos anteriores; solo si vendió en los tres.
/// - Punto de reorden sugerido = ⌈(venta semanal / 7) × entrega + z × (desviación semanal / √7) × √entrega⌉.
/// </summary>
public static class SalesAnalysisRules
{
    public const int DefaultLeadTimeDays = 5;
    public const decimal DefaultServiceLevelZ = 1.645m;
    public const int MaxLeadTimeDays = 365;
    public const decimal MaxServiceLevelZ = 4m;
    public const int StaleDays = 45;
    public const decimal TrendThreshold = 0.25m;
    public const int WeeksWindow = 8;
    public const decimal AbcA = 0.80m;
    public const decimal AbcB = 0.95m;
    /// <summary>Semanas por mes para pasar la cobertura a meses y proyectar el saldo (365.25 / 12 / 7).</summary>
    public const decimal WeeksPerMonth = 365.25m / 12m / 7m;

    public const int NameMaxLength = 100;
    public const int BrandMaxLength = 100;
    public const int MaxBulk = 2000;
    public const int MaxPageSize = 500;
    public const int MaxMonthsRange = 36;

    // ---------------------------------------------------------------- códigos de estado, ABC y confianza
    public const string StatusNoSales = "NO_SALES";
    public const string StatusStale = "STALE";
    public const string StatusNew = "NEW";
    public const string StatusRising = "RISING";
    public const string StatusFalling = "FALLING";
    public const string StatusStable = "STABLE";

    public const string AbcNone = "-";

    public const string ConfidenceNone = "NONE";
    public const string ConfidenceLow = "LOW";
    public const string ConfidenceSporadic = "SPORADIC";
    public const string ConfidenceHigh = "HIGH";

    // ---------------------------------------------------------------- etiquetas de grupos (en las respuestas del API)
    public const string NoRepName = "Sin representante";
    public const string NoRouteName = "Sin ruta";
    public const string NoBrandLine = "Sin marca";
    public const string DiscountsLine = "Descuentos sin línea";
    public const string UnmatchedLine = "Producto sin emparejar";

    // ---------------------------------------------------------------- mensajes exactos (el manual y la FAQ los citan)
    public const string BodyRequired = "El cuerpo de la solicitud es obligatorio.";
    public const string RouteNotFound = "Ruta";
    public const string RepNotFound = "Representante";
    public const string LineNotFound = "Línea";
    public const string ClientNotFound = "Cliente";
    public const string ProductNotFound = "Producto";
    public const string RouteNameRequired = "Escriba el nombre de la ruta.";
    public const string RepNameRequired = "Escriba el nombre del representante.";
    public const string LineNameRequired = "Escriba el nombre de la línea.";
    public static readonly string NameTooLong = $"El nombre no puede pasar de {NameMaxLength} caracteres.";
    public const string RouteNameTaken = "Ya hay una ruta activa con ese nombre.";
    public const string RepNameTaken = "Ya hay un representante activo con ese nombre.";
    public const string LineNameTaken = "Ya hay una línea activa con ese nombre.";
    public const string RouteInactive = "La ruta está desactivada: actívela o escoja otra.";
    public const string RepInactive = "El representante está desactivado: actívelo o escoja otro.";
    public const string LineInactive = "La línea está desactivada: actívela antes de cambiar sus marcas.";
    public const string UserNotInCompany = "El usuario no pertenece a esta compañía.";
    public const string RouteHasReps = "La ruta tiene representantes activos: páselos a otra ruta antes de desactivarla.";
    public const string RepHasClients = "El representante tiene clientes asignados: páselos a otro representante antes de desactivarlo.";
    public const string MonthInvalid = "El mes debe tener la forma AAAA-MM (por ejemplo 2026-10).";
    public const string MonthRangeInvalid = "El mes final no puede ser anterior al inicial.";
    public static readonly string MonthRangeTooLong = $"El rango no puede pasar de {MaxMonthsRange} meses.";
    public const string QuotaInvalid = "La cuota debe ser cero o mayor, con hasta 2 decimales.";
    public static readonly string TooMany = $"No se pueden mandar más de {MaxBulk} renglones en una sola llamada.";
    public const string ClientsRequired = "Indique los clientes (clientIds).";
    public const string BrandRequired = "Escriba la marca.";
    public static readonly string BrandTooLong = $"La marca no puede pasar de {BrandMaxLength} caracteres.";
    public static readonly string LeadTimeInvalid = $"Los días de entrega deben estar entre 1 y {MaxLeadTimeDays}.";
    public static readonly string ServiceLevelInvalid = $"El nivel de servicio (z) debe ser mayor que 0 y hasta {MaxServiceLevelZ.ToString(CultureInfo.InvariantCulture)}.";
    public const string AliasKindInvalid = "El tipo debe ser CLIENT o PRODUCT.";
    public const string AliasRawRequired = "Indique el nombre tal como viene en el historial (raw).";
    public const string AliasTargetRequired = "Indique el cliente o el producto de Teikem al que se enlaza.";
    public const string FileRequired = "Suba al menos uno de los dos archivos (ventas o compras).";
    public const string FileTooLarge = "El archivo no puede pasar de 20 MB.";
    public const string PageInvalid = "La página debe ser 1 o mayor.";
    public static readonly string PageSizeInvalid = $"El tamaño de página debe estar entre 1 y {MaxPageSize}.";

    public static string BrandTaken(string brand, string line) => $"La marca «{brand}» ya está en la línea «{line}»: quítela de ahí primero.";
    public static string NotAReport(string file) =>
        $"El archivo {file} no parece un reporte «Sales by Item Detail» o «Purchases by Item Detail» de QuickBooks (faltan las columnas Type, Date, Qty y Amount).";

    // ---------------------------------------------------------------- meses

    public static DateOnly MonthStart(DateOnly d) => new(d.Year, d.Month, 1);

    /// <summary>Lee «AAAA-MM» (también «AAAA-MM-DD»: se toma su mes). Vacío o mal escrito = null.</summary>
    public static DateOnly? ParseMonth(string? raw)
    {
        var s = raw?.Trim();
        if (string.IsNullOrEmpty(s)) return null;
        if (DateOnly.TryParseExact(s, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var m)) return m;
        if (DateOnly.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) return MonthStart(d);
        return null;
    }

    public static string MonthKey(DateOnly month) => month.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    /// <summary>Los meses de [from, to] (primer día de cada uno), en orden.</summary>
    public static IReadOnlyList<DateOnly> Months(DateOnly from, DateOnly to)
    {
        var list = new List<DateOnly>();
        for (var m = MonthStart(from); m <= MonthStart(to); m = m.AddMonths(1)) list.Add(m);
        return list;
    }

    /// <summary>Último mes COMPLETO respecto del día de corte: el mes anterior al del día de corte (el mes del corte todavía puede recibir ventas).</summary>
    public static DateOnly LastCompleteMonth(DateOnly asOf) => MonthStart(asOf).AddMonths(-1);

    // ---------------------------------------------------------------- nombres y clases

    /// <summary>
    /// Clave de un nombre para emparejar (clientes del historial con los de Teikem): sin acentos, mayúsculas invariantes, sin puntos ni comas, espacios juntos y
    /// recortado («Farmacia Central Drug.» = «FARMACIA CENTRAL DRUG»).
    /// </summary>
    public static string NameKey(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;
        var decomposed = name.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        var space = false;
        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            var c = ch is '.' or ',' ? ' ' : char.ToUpperInvariant(ch);
            if (char.IsWhiteSpace(c)) { if (!space && sb.Length > 0) sb.Append(' '); space = true; continue; }
            space = false;
            sb.Append(c);
        }
        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Clase de una línea de venta del historial: sección de inventario = PRODUCT; sección o ítem de descuento = DISCOUNT; ítem de envío (shipping, delivery, envío,
    /// flete) = SHIPPING; un ítem que emparejó con un producto = PRODUCT; servicios, «Parts» y otros cargos sin producto = OTHER; lo demás (sección desconocida) = PRODUCT
    /// (si no empareja se reporta para enlazarlo a mano).
    /// </summary>
    public static string ClassOf(string? section, string item, bool productMatched)
    {
        var s = NameKey(section);
        var text = NameKey(item);
        if (s.StartsWith("INVENTORY", StringComparison.Ordinal) || s.StartsWith("ASSEMBL", StringComparison.Ordinal)) return SalesAnalysisCodes.ClassProduct;
        if (s.StartsWith("DISCOUNT", StringComparison.Ordinal) || text.Contains("DISCOUNT", StringComparison.Ordinal) || text.Contains("DESCUENTO", StringComparison.Ordinal))
            return SalesAnalysisCodes.ClassDiscount;
        if (text.Contains("SHIPPING", StringComparison.Ordinal) || text.Contains("DELIVERY", StringComparison.Ordinal) || text.Contains("ENVIO", StringComparison.Ordinal)
            || text.Contains("FLETE", StringComparison.Ordinal) || text.Contains("FREIGHT", StringComparison.Ordinal))
            return SalesAnalysisCodes.ClassShipping;
        if (productMatched) return SalesAnalysisCodes.ClassProduct;
        if (s.StartsWith("SERVICE", StringComparison.Ordinal) || s.StartsWith("PARTS", StringComparison.Ordinal) || s.StartsWith("NON-INVENTORY", StringComparison.Ordinal)
            || s.StartsWith("NON INVENTORY", StringComparison.Ordinal) || s.StartsWith("OTHER", StringComparison.Ordinal) || s.StartsWith("SALES TAX", StringComparison.Ordinal)
            || s.StartsWith("PAYMENT", StringComparison.Ordinal) || s.StartsWith("SUBTOTAL", StringComparison.Ordinal))
            return SalesAnalysisCodes.ClassOther;
        return SalesAnalysisCodes.ClassProduct;
    }

    /// <summary>¿La clase suma a la venta del representante? Producto y descuento (la venta neta); envío y otros van aparte en la conciliación.</summary>
    public static bool CountsForRep(string lineClass) => lineClass is SalesAnalysisCodes.ClassProduct or SalesAnalysisCodes.ClassDiscount;

    // ---------------------------------------------------------------- ABC

    /// <summary>
    /// Clasificación ABC por venta en $: se ordena de mayor a menor (empate: la clave en orden ordinal de su texto) y cada producto toma A si lo acumulado ANTES de él
    /// es menor que 80 % del total positivo, B si es menor que 95 %, si no C. Venta cero o negativa = «-».
    /// </summary>
    public static IReadOnlyDictionary<TKey, string> Abc<TKey>(IEnumerable<(TKey Key, decimal Amount)> items) where TKey : notnull
    {
        var list = items.ToList();
        var result = new Dictionary<TKey, string>();
        var positive = list.Where(i => i.Amount > 0m).OrderByDescending(i => i.Amount).ThenBy(i => i.Key.ToString(), StringComparer.Ordinal).ToList();
        var total = positive.Sum(i => i.Amount);
        var before = 0m;
        foreach (var (key, amount) in positive)
        {
            var share = total == 0m ? 1m : before / total;
            result[key] = share < AbcA ? "A" : share < AbcB ? "B" : "C";
            before += amount;
        }
        foreach (var (key, _) in list) result.TryAdd(key, AbcNone);
        return result;
    }

    // ---------------------------------------------------------------- mensual

    /// <summary>
    /// Promedio, mínimo y máximo mensual de las unidades: meses completos desde el primer mes con ventas (≥ rangeStart) hasta lastCompleteMonth; los meses sin venta
    /// cuentan 0. Mejor mes = el de más unidades (empate: el primero). Sin meses completos = todo null.
    /// </summary>
    public static MonthlyStats MonthStats(IReadOnlyDictionary<DateOnly, decimal> unitsByMonth, DateOnly rangeStart, DateOnly lastCompleteMonth)
    {
        var first = unitsByMonth.Where(kv => kv.Value != 0m && kv.Key >= MonthStart(rangeStart)).Select(kv => (DateOnly?)kv.Key).OrderBy(k => k).FirstOrDefault();
        if (first is not DateOnly f || f > lastCompleteMonth) return new MonthlyStats(null, null, null, null, 0);
        var months = Months(f, lastCompleteMonth);
        var values = months.Select(m => (Month: m, Units: unitsByMonth.TryGetValue(m, out var u) ? u : 0m)).ToList();
        var best = values.OrderByDescending(v => v.Units).ThenBy(v => v.Month).First();
        return new MonthlyStats(Math.Round(values.Average(v => v.Units), 2, MidpointRounding.AwayFromZero), values.Min(v => v.Units), values.Max(v => v.Units), best.Month, values.Count);
    }

    /// <summary>Tendencia: el mes contra el promedio de los dos anteriores (0.25 = +25 %); null si en alguno de los tres no vendió.</summary>
    public static decimal? Trend(decimal month, decimal previous, decimal beforePrevious)
    {
        if (month <= 0m || previous <= 0m || beforePrevious <= 0m) return null;
        return Math.Round(month / ((previous + beforePrevious) / 2m) - 1m, 4, MidpointRounding.AwayFromZero);
    }

    // ---------------------------------------------------------------- semanal

    /// <summary>Lunes de la semana del día.</summary>
    public static DateOnly WeekStart(DateOnly d) => d.AddDays(-(((int)d.DayOfWeek + 6) % 7));

    /// <summary>Las últimas <paramref name="weeks"/> semanas completas (lunes a domingo) antes de la semana del día de corte: [desde, hasta exclusivo).</summary>
    public static (DateOnly From, DateOnly ToExclusive) WeekWindow(DateOnly asOf, int weeks = WeeksWindow)
    {
        var end = WeekStart(asOf);
        return (end.AddDays(-7 * weeks), end);
    }

    /// <summary>
    /// Venta semanal de la ventana (más antigua primero): se usan las semanas desde la primera con venta; promedio y desviación poblacional de esas semanas.
    /// Confianza: sin venta en la ventana = NONE; menos de 3 semanas usadas = LOW (poca historia); la mitad o menos de las usadas con venta = SPORADIC; si no HIGH.
    /// </summary>
    public static WeeklyStats WeekStats(IReadOnlyList<decimal> weekly)
    {
        var first = -1;
        for (var i = 0; i < weekly.Count; i++) if (weekly[i] != 0m) { first = i; break; }
        if (first < 0) return new WeeklyStats(0, 0, null, null, ConfidenceNone);
        var used = weekly.Skip(first).ToList();
        var avg = used.Average();
        var variance = used.Sum(v => (double)((v - avg) * (v - avg))) / used.Count;
        var std = (decimal)Math.Sqrt(variance);
        var withSales = used.Count(v => v > 0m);
        var confidence = used.Count < 3 ? ConfidenceLow : withSales * 2 <= used.Count ? ConfidenceSporadic : ConfidenceHigh;
        return new WeeklyStats(used.Count, withSales, Math.Round(avg, 2, MidpointRounding.AwayFromZero), Math.Round(std, 2, MidpointRounding.AwayFromZero), confidence);
    }

    /// <summary>Semanas de cobertura = disponible ÷ promedio semanal (1 decimal); null sin promedio positivo. Disponible negativo cuenta 0.</summary>
    public static decimal? CoverageWeeks(decimal available, decimal? weeklyAverage)
        => weeklyAverage is decimal w && w > 0m ? Math.Round(Math.Max(0m, available) / w, 1, MidpointRounding.AwayFromZero) : null;

    /// <summary>Cobertura en meses (derivada de las semanas).</summary>
    public static decimal? CoverageMonths(decimal available, decimal? weeklyAverage)
        => weeklyAverage is decimal w && w > 0m ? Math.Round(Math.Max(0m, available) / (w * WeeksPerMonth), 1, MidpointRounding.AwayFromZero) : null;

    /// <summary>Saldo proyectado tras <paramref name="months"/> meses si la venta sigue al promedio semanal (nunca negativo; sin promedio = el disponible).</summary>
    public static decimal ProjectedBalance(decimal available, decimal? weeklyAverage, int months)
        => Math.Max(0m, Math.Round(available - (weeklyAverage ?? 0m) * WeeksPerMonth * months, 0, MidpointRounding.AwayFromZero));

    /// <summary>Punto de reorden sugerido = ⌈(semanal / 7) × entrega + z × (desviación / √7) × √entrega⌉; null sin promedio semanal.</summary>
    public static int? SuggestedReorderPoint(decimal? weeklyAverage, decimal? weeklyStdDev, int leadTimeDays, decimal z)
    {
        if (weeklyAverage is not decimal avg) return null;
        var daily = (double)avg / 7d;
        var safety = (double)z * ((double)(weeklyStdDev ?? 0m) / Math.Sqrt(7d)) * Math.Sqrt(leadTimeDays);
        // un pelo de tolerancia para que 12.0000000001 no suba a 13 por el redondeo de doble precisión
        return (int)Math.Ceiling(Math.Round(daily * leadTimeDays + safety, 6, MidpointRounding.AwayFromZero));
    }

    // ---------------------------------------------------------------- estado

    /// <summary>
    /// Estado del producto, en este orden: sin ventas (nunca vendió en el rango) → parado (más de 45 días desde la última venta hasta el día de corte) → nuevo (su primer
    /// mes con venta es el anterior al último mes completo o después) → subiendo / bajando (tendencia de ±25 % o más) → estable.
    /// </summary>
    public static string ProductStatus(DateOnly? lastSale, DateOnly? firstSaleMonth, DateOnly asOf, decimal? trend)
    {
        if (lastSale is not DateOnly last) return StatusNoSales;
        if (asOf.DayNumber - last.DayNumber > StaleDays) return StatusStale;
        if (firstSaleMonth is DateOnly f && f >= LastCompleteMonth(asOf).AddMonths(-1)) return StatusNew;
        if (trend is decimal t && t >= TrendThreshold) return StatusRising;
        if (trend is decimal t2 && t2 <= -TrendThreshold) return StatusFalling;
        return StatusStable;
    }

    // ---------------------------------------------------------------- cuotas y comparaciones

    /// <summary>Cuota vigente de un mes: la del mes o, si no tiene, la del mes anterior más reciente; null si no hay ninguna antes.</summary>
    public static decimal? EffectiveQuota(IEnumerable<(DateOnly Month, decimal Amount)> quotas, DateOnly month)
    {
        var m = MonthStart(month);
        var found = quotas.Where(q => MonthStart(q.Month) <= m).OrderByDescending(q => q.Month).Select(q => (decimal?)q.Amount).FirstOrDefault();
        return found;
    }

    /// <summary>Cambio en proporción (0.10 = +10 %); null si lo anterior es 0.</summary>
    public static decimal? ChangePct(decimal previous, decimal current) => previous == 0m ? null : Math.Round((current - previous) / Math.Abs(previous), 4, MidpointRounding.AwayFromZero);

    /// <summary>Cumplimiento de la cuota en proporción (1 = 100 %); null sin cuota positiva.</summary>
    public static decimal? Compliance(decimal sales, decimal? quota) => quota is decimal q && q > 0m ? Math.Round(sales / q, 4, MidpointRounding.AwayFromZero) : null;

    /// <summary>Proporción de una parte en el total (null si el total es 0).</summary>
    public static decimal? Share(decimal part, decimal total) => total == 0m ? null : Math.Round(part / total, 4, MidpointRounding.AwayFromZero);

    public static SalesReconciliation Reconcile(decimal routesTotal, decimal unassigned, decimal shippingAndOther, decimal monthTotal)
        => new(routesTotal, unassigned, shippingAndOther, monthTotal, Math.Round(monthTotal - (routesTotal + unassigned + shippingAndOther), 2, MidpointRounding.AwayFromZero));

    // ---------------------------------------------------------------- kits

    /// <summary>
    /// Reparte una línea de kit (cantidad de kits y su monto) entre sus piezas con la MISMA regla del archivo IIF de QuickBooks (lote 225): pesos = cantidad por kit ×
    /// precio de lista, cierre al centavo por mayor residuo, pesos en cero = a partes iguales. Unidades de cada pieza = kits × cantidad por kit.
    /// </summary>
    public static IReadOnlyList<SalesKitShare> SplitKit(decimal kits, decimal amount, IReadOnlyList<SalesKitPart> parts)
    {
        var shares = QuickBooksRules.Prorate(amount, parts.Select(p => p.QuantityPerKit * (p.ListPrice ?? 0m)).ToList());
        return parts.Select((p, i) => new SalesKitShare(p.ProductId, kits * p.QuantityPerKit, shares[i])).ToList();
    }

    // ---------------------------------------------------------------- compras

    /// <summary>
    /// Duplicados de compras: en QuickBooks un «Item Receipt» que luego se factura pasa a ser «Bill» (la misma transacción); si el reporte llegara a traer las dos
    /// (mismo suplidor, número, ítem y cantidad), se cuenta solo el Item Receipt (la fecha en que llegó la mercancía) y se descarta el Bill. Empareja uno a uno.
    /// Devuelve los índices de los Bill descartados.
    /// </summary>
    public static IReadOnlySet<int> DuplicatedBills(IReadOnlyList<PurchaseDupRow> rows)
    {
        static string Key(PurchaseDupRow r) => string.Join("|", NameKey(r.Supplier), NameKey(r.Number), r.ItemKey, r.Quantity.ToString(CultureInfo.InvariantCulture));
        var receipts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var r in rows.Where(r => IsItemReceipt(r.Type)))
            receipts[Key(r)] = receipts.GetValueOrDefault(Key(r)) + 1;
        var drop = new HashSet<int>();
        for (var i = 0; i < rows.Count; i++)
        {
            if (!string.Equals(rows[i].Type?.Trim(), "Bill", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(rows[i].Number)) continue;
            var k = Key(rows[i]);
            if (receipts.TryGetValue(k, out var n) && n > 0) { receipts[k] = n - 1; drop.Add(i); }
        }
        return drop;
    }

    public static bool IsItemReceipt(string? type) => string.Equals(type?.Trim(), "Item Receipt", StringComparison.OrdinalIgnoreCase);
}
