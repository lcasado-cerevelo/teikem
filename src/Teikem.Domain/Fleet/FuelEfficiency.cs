using System.Globalization;

namespace Teikem.Domain.Fleet;

/// <summary>Lectura mínima de una carga de combustible para calcular eficiencia y validar el odómetro (sin BD).</summary>
public sealed record FuelReading(int Id, DateTime FillDateUtc, decimal? OdometerKm, decimal Liters, decimal TotalCost);

/// <summary>
/// Eficiencia de una carga: distancia desde la carga anterior con odómetro, km/L (2 decimales) y costo/km (4 decimales).
/// ConsumedLiters/ConsumedCost = combustible atribuido a esa distancia: los litros y el costo de esta carga MÁS los de las
/// cargas sin odómetro intermedias (tanque lleno). Los cinco son null si no hay carga anterior con odómetro, si falta el
/// odómetro o si la distancia es ≤ 0.
/// </summary>
public sealed record FuelEfficiencyRow(int Id, DateTime FillDateUtc, decimal? OdometerKm, decimal Liters, decimal TotalCost,
    decimal? DistanceKm, decimal? KmPerLiter, decimal? CostPerKm, decimal? ConsumedLiters, decimal? ConsumedCost);

/// <summary>
/// Resumen de un conjunto de cargas: número de cargas, litros y costo totales, y la eficiencia equivalente
/// (Σ distancia / Σ litros consumidos y Σ costo consumido / Σ distancia de las cargas CON distancia).
/// </summary>
public sealed record FuelEfficiencySummary(int Fills, decimal TotalLiters, decimal TotalCost, decimal? DistanceKm, decimal? KmPerLiter, decimal? CostPerKm);

/// <summary>Resultado de <see cref="FuelEfficiency.Compute"/>: filas en orden cronológico y el resumen de la serie completa.</summary>
public sealed record FuelEfficiencyResult(IReadOnlyList<FuelEfficiencyRow> Rows, FuelEfficiencySummary Summary)
{
    /// <summary>Filas por id de carga (para completar DTOs de una página).</summary>
    public IReadOnlyDictionary<int, FuelEfficiencyRow> ById { get; } = Rows.ToDictionary(r => r.Id);
}

/// <summary>
/// Lote 4 (P5) — bitácora de combustible: km/L y costo/km se calculan al leer, no se guardan (decisión del plan).
/// - Supuesto de tanque lleno, lecturas consecutivas de odómetro: el combustible consumido en el tramo entre dos lecturas es
///   el de la carga que cierra el tramo MÁS el de las cargas sin odómetro intermedias. km/L = distancia / esos litros;
///   costo/km = ese costo / distancia.
/// - La serie se ordena por fecha y, a igual fecha, por id (orden de captura).
/// - Una carga sin odómetro no tiene eficiencia propia y no interrumpe la serie: la siguiente se mide contra la última CON
///   odómetro y absorbe sus litros y su costo. Si el tramo no es válido (distancia ≤ 0) lo acumulado se descarta.
/// - El odómetro debe ser monótono por fecha dentro del mismo vehículo (ValidateOdometer → 400).
/// </summary>
public static class FuelEfficiency
{
    public const int KmPerLiterDecimals = 2;
    public const int CostPerKmDecimals = 4;

    /// <summary>Calcula la eficiencia de cada carga de UNA serie (un vehículo, solo cargas activas) y su resumen.</summary>
    public static FuelEfficiencyResult Compute(IEnumerable<FuelReading> readings)
    {
        var ordered = Order(readings);
        var rows = new List<FuelEfficiencyRow>(ordered.Count);
        decimal? previousOdometer = null;
        // Litros y costo de las cargas SIN odómetro posteriores a la última lectura (se atribuyen al tramo siguiente).
        decimal pendingLiters = 0m, pendingCost = 0m;

        foreach (var r in ordered)
        {
            decimal? distance = null, kmPerLiter = null, costPerKm = null, consumedLiters = null, consumedCost = null;
            if (r.OdometerKm is decimal current)
            {
                if (previousOdometer is decimal prev)
                {
                    var d = current - prev;
                    if (d > 0)
                    {
                        var liters = pendingLiters + r.Liters;
                        var cost = pendingCost + r.TotalCost;
                        distance = d;
                        consumedLiters = liters;
                        consumedCost = cost;
                        kmPerLiter = liters > 0 ? Round(d / liters, KmPerLiterDecimals) : null;
                        costPerKm = Round(cost / d, CostPerKmDecimals);
                    }
                }
                previousOdometer = current;
                pendingLiters = 0m;
                pendingCost = 0m;
            }
            else if (previousOdometer is not null)
            {
                pendingLiters += r.Liters;
                pendingCost += r.TotalCost;
            }
            rows.Add(new FuelEfficiencyRow(r.Id, r.FillDateUtc, r.OdometerKm, r.Liters, r.TotalCost, distance, kmPerLiter, costPerKm,
                consumedLiters, consumedCost));
        }

        return new FuelEfficiencyResult(rows, Summarize(rows));
    }

    /// <summary>
    /// Resumen de un subconjunto de filas ya calculadas (p. ej. las cargas de un período): Fills/TotalLiters/TotalCost sobre
    /// todas; km/L = Σ distancia / Σ litros consumidos y costo/km = Σ costo consumido / Σ distancia, solo de las cargas con
    /// distancia (lo consumido incluye las cargas sin odómetro intermedias, ver <see cref="FuelEfficiencyRow"/>).
    /// </summary>
    public static FuelEfficiencySummary Summarize(IEnumerable<FuelEfficiencyRow> rows)
    {
        var list = rows as IReadOnlyCollection<FuelEfficiencyRow> ?? rows.ToList();
        var withDistance = list.Where(r => r.DistanceKm is > 0).ToList();

        decimal? distance = null, kmPerLiter = null, costPerKm = null;
        if (withDistance.Count > 0)
        {
            var d = withDistance.Sum(r => r.DistanceKm!.Value);
            var liters = withDistance.Sum(r => r.ConsumedLiters ?? r.Liters);
            var cost = withDistance.Sum(r => r.ConsumedCost ?? r.TotalCost);
            distance = d;
            kmPerLiter = liters > 0 ? Round(d / liters, KmPerLiterDecimals) : null;
            costPerKm = Round(cost / d, CostPerKmDecimals);
        }

        return new FuelEfficiencySummary(list.Count, list.Sum(r => r.Liters), list.Sum(r => r.TotalCost), distance, kmPerLiter, costPerKm);
    }

    /// <summary>
    /// Odómetro monótono por fecha: la lectura no puede ser menor que la de una carga anterior ni mayor que la de una
    /// posterior del MISMO vehículo (readings = cargas activas de ese vehículo). A igual fecha manda el id: una carga nueva
    /// (excludeId null) va después de todas las de su misma fecha; al editar se excluye la propia fila y se usa su id.
    /// Lecturas iguales son válidas. Sin odómetro → válido (null).
    /// </summary>
    public static string? ValidateOdometer(IEnumerable<FuelReading> readings, DateTime fillDateUtc, decimal? odometerKm, int? excludeId)
    {
        if (odometerKm is not decimal km) return null;
        var selfId = excludeId ?? int.MaxValue;

        FuelReading? previousMax = null, nextMin = null;
        foreach (var r in readings)
        {
            if (r.OdometerKm is not decimal other) continue;
            if (excludeId is int ex && r.Id == ex) continue;

            var isBefore = r.FillDateUtc < fillDateUtc || (r.FillDateUtc == fillDateUtc && r.Id < selfId);
            if (isBefore)
            {
                if (previousMax is null || other > previousMax.OdometerKm!.Value
                    || (other == previousMax.OdometerKm!.Value && IsLater(r, previousMax)))
                    previousMax = r;
            }
            else
            {
                if (nextMin is null || other < nextMin.OdometerKm!.Value
                    || (other == nextMin.OdometerKm!.Value && IsLater(nextMin, r)))
                    nextMin = r;
            }
        }

        if (previousMax is not null && km < previousMax.OdometerKm!.Value)
            return $"La lectura de odómetro ({FormatKm(km)} km) es menor que la de una carga anterior del mismo vehículo " +
                   $"({FormatKm(previousMax.OdometerKm!.Value)} km el {FormatDate(previousMax.FillDateUtc)}).";
        if (nextMin is not null && km > nextMin.OdometerKm!.Value)
            return $"La lectura de odómetro ({FormatKm(km)} km) es mayor que la de una carga posterior del mismo vehículo " +
                   $"({FormatKm(nextMin.OdometerKm!.Value)} km el {FormatDate(nextMin.FillDateUtc)}).";
        return null;
    }

    /// <summary>Kilómetros con separador de miles y sin decimales sobrantes (5100.0 → '5,100'; 5100.5 → '5,100.5').</summary>
    public static string FormatKm(decimal km) => km.ToString("#,##0.#", CultureInfo.InvariantCulture);

    private static string FormatDate(DateTime utc) => utc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static List<FuelReading> Order(IEnumerable<FuelReading> readings)
        => readings.OrderBy(r => r.FillDateUtc).ThenBy(r => r.Id).ToList();

    private static bool IsLater(FuelReading a, FuelReading b)
        => a.FillDateUtc > b.FillDateUtc || (a.FillDateUtc == b.FillDateUtc && a.Id > b.Id);

    private static decimal Round(decimal value, int decimals) => Math.Round(value, decimals, MidpointRounding.AwayFromZero);
}
