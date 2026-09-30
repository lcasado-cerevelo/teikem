namespace Teikem.Domain.Migration;

/// <summary>Origen del cupo estimado de una posición migrada del WMS MSWM (columna Origen del CSV de cupos).</summary>
public static class BinCapacityOrigins
{
    /// <summary>La posición tiene historial propio en el WMS: cupo = su máximo histórico redondeado hacia arriba a la decena.</summary>
    public const string History = "HISTORIAL";
    /// <summary>Sin historial: mediana de los cupos con historial de su mismo pasillo.</summary>
    public const string Aisle = "PASILLO";
    /// <summary>Sin historial ni pasillo con historial: mediana de los cupos con historial de su zona.</summary>
    public const string Zone = "ZONA";
    /// <summary>Último recurso: mediana de los cupos con historial de todo el almacén.</summary>
    public const string Warehouse = "ALMACEN";

    /// <summary>Orden de presentación en el resumen del reporte.</summary>
    public static readonly IReadOnlyList<string> All = new[] { History, Aisle, Zone, Warehouse };
}

/// <summary>Posición a estimar: su código de Teikem, su zona y su pasillo (null si el id del WMS no sigue 'NN-L-NN').</summary>
public sealed record BinCapacityInput(string Code, string ZoneCode, string? Aisle);

/// <summary>
/// Cupo estimado de una posición: HistoricalMax = el mayor total histórico &gt; 0 de la posición en el WMS (null si no tiene
/// historial); Capacity = cupo máximo en unidades que se le asigna; Origin = BinCapacityOrigins.
/// </summary>
public sealed record BinCapacityEstimate(string Code, string ZoneCode, string? Aisle, decimal? HistoricalMax, int Capacity, string Origin);

/// <summary>Qué hace el importador con el cupo estimado de una posición (columna Resultado del CSV de cupos).</summary>
public enum BinCapacityAction
{
    /// <summary>Posición nueva: nace con el cupo estimado.</summary>
    AssignOnCreate,
    /// <summary>Posición existente sin cupo en modo --update: se le llena.</summary>
    FillExisting,
    /// <summary>Posición existente que ya tiene cupo (capturado a mano o de una carga anterior): nunca se pisa.</summary>
    KeepExisting,
    /// <summary>Posición existente sin cupo y sin --update: no se toca (sin --update el importador solo agrega lo que falta).</summary>
    SkipWithoutUpdate,
}

/// <summary>
/// Lote 11 (cupo de posiciones de Depot) — estimación PURA del cupo máximo (WarehouseBin.MaxCapacityQty, unidades de producto)
/// de las posiciones que vienen del WMS MSWM, a partir de su historial de existencias. Sin EF ni IO: el importador le pasa las
/// posiciones del plan y el máximo histórico por código de posición.
/// - Máximo histórico de una posición: el mayor total &gt; 0 entre todas las "fotos" del WMS (inventario actual, inventario
///   anterior, conteos cíclicos, historial de conteo y acomodos sumados por día).
/// - Cupo con historial = máximo histórico redondeado hacia arriba a la decena (mínimo 10) → origen HISTORIAL.
/// - Sin historial: mediana de los cupos HISTORIAL de su pasillo (PASILLO); si el pasillo no tiene ninguno, la de su zona
///   (ZONA); si tampoco, la del almacén (ALMACEN). Las medianas se redondean hacia arriba a la decena. Los cupos heredados no
///   alimentan otras medianas (solo los de HISTORIAL). Una mediana de pasillo o de zona solo se usa si tiene al menos
///   MinSamples cupos con historial; con menos se pasa al nivel siguiente (con dos datos, 30 y 264.600, la "mediana" sería
///   132.320: no representa a nadie).
/// - Pasillo = BinParts.Aisle de LegacyImportRules.ParseBinCode (el mismo criterio con que el importador deriva pasillo/nivel/
///   posición del LocationId): '01-a-24' y '01-A-07' son el pasillo '01'. Una posición sin pasillo (PISO, R1…) salta a su zona.
/// </summary>
public static class BinCapacityRules
{
    /// <summary>Datos con historial que necesita una mediana de pasillo o de zona para usarse.</summary>
    public const int MinSamples = 5;

    /// <summary>Paso de redondeo y cupo mínimo.</summary>
    public const int Step = 10;

    /// <summary>Tope: el mayor múltiplo de 10 que cabe en INT (WarehouseBin.MaxCapacityQty).</summary>
    public const int MaxCapacity = int.MaxValue / Step * Step;

    /// <summary>Cantidad → cupo: hacia arriba a la decena, mínimo 10, tope MaxCapacity (1 → 10, 10 → 10, 11 → 20, 1212 → 1220).</summary>
    public static int RoundUpToStep(decimal quantity)
    {
        if (quantity <= Step) return Step;
        var rounded = Math.Ceiling(quantity / Step) * Step;
        return rounded >= MaxCapacity ? MaxCapacity : (int)rounded;
    }

    /// <summary>
    /// Mediana de los cupos (promedio de los dos centrales si son pares) redondeada hacia arriba a la decena. Vacío → null.
    /// </summary>
    public static int? Median(IEnumerable<int> capacities)
    {
        var sorted = (capacities ?? Array.Empty<int>()).OrderBy(c => c).ToList();
        if (sorted.Count == 0) return null;
        var mid = sorted.Count / 2;
        var median = sorted.Count % 2 == 1 ? sorted[mid] : ((decimal)sorted[mid - 1] + sorted[mid]) / 2m;
        return RoundUpToStep(median);
    }

    /// <summary>
    /// Máximo histórico por código de posición de Teikem: los ids del WMS se pasan por LegacyImportRules.ParseBinCode (así
    /// '01-a-24' y '01-A-24' son la misma posición) y se toma el mayor total &gt; 0. Los totales ≤ 0 no cuentan.
    /// </summary>
    public static IReadOnlyDictionary<string, decimal> HistoricalMaxByCode(IEnumerable<(string LocationId, decimal Quantity)> photoTotals)
    {
        var max = new Dictionary<string, decimal>(StringComparer.Ordinal);
        foreach (var (locationId, quantity) in photoTotals ?? Array.Empty<(string, decimal)>())
        {
            if (quantity <= 0 || string.IsNullOrWhiteSpace(locationId)) continue;
            var code = LegacyImportRules.ParseBinCode(locationId).Code;
            if (!max.TryGetValue(code, out var current) || quantity > current) max[code] = quantity;
        }
        return max;
    }

    /// <summary>
    /// Cupo de cada posición (en el orden recibido). Si ninguna posición del almacén tiene historial no hay de dónde estimar:
    /// devuelve una lista vacía (el importador lo informa y deja las posiciones sin cupo).
    /// </summary>
    public static IReadOnlyList<BinCapacityEstimate> Estimate(IReadOnlyList<BinCapacityInput> bins, IReadOnlyDictionary<string, decimal> historicalMax, int minSamples = MinSamples)
    {
        ArgumentNullException.ThrowIfNull(bins);
        ArgumentNullException.ThrowIfNull(historicalMax);

        // Cupos con historial propio: son la base de todas las medianas.
        var own = new Dictionary<string, (decimal Max, int Capacity)>(StringComparer.Ordinal);
        foreach (var b in bins)
            if (historicalMax.TryGetValue(b.Code, out var m) && m > 0) own[b.Code] = (m, RoundUpToStep(m));
        if (own.Count == 0) return Array.Empty<BinCapacityEstimate>();

        var withHistory = bins.Where(b => own.ContainsKey(b.Code)).ToList();
        var byAisle = withHistory.Where(b => b.Aisle is not null).GroupBy(b => b.Aisle!, StringComparer.Ordinal)
            .Where(g => g.Count() >= minSamples)
            .ToDictionary(g => g.Key, g => Median(g.Select(b => own[b.Code].Capacity))!.Value, StringComparer.Ordinal);
        var byZone = withHistory.GroupBy(b => b.ZoneCode, StringComparer.Ordinal)
            .Where(g => g.Count() >= minSamples)
            .ToDictionary(g => g.Key, g => Median(g.Select(b => own[b.Code].Capacity))!.Value, StringComparer.Ordinal);
        var warehouse = Median(withHistory.Select(b => own[b.Code].Capacity))!.Value;

        var result = new List<BinCapacityEstimate>(bins.Count);
        foreach (var b in bins)
        {
            if (own.TryGetValue(b.Code, out var h))
                result.Add(new BinCapacityEstimate(b.Code, b.ZoneCode, b.Aisle, h.Max, h.Capacity, BinCapacityOrigins.History));
            else if (b.Aisle is not null && byAisle.TryGetValue(b.Aisle, out var a))
                result.Add(new BinCapacityEstimate(b.Code, b.ZoneCode, b.Aisle, null, a, BinCapacityOrigins.Aisle));
            else if (byZone.TryGetValue(b.ZoneCode, out var z))
                result.Add(new BinCapacityEstimate(b.Code, b.ZoneCode, b.Aisle, null, z, BinCapacityOrigins.Zone));
            else
                result.Add(new BinCapacityEstimate(b.Code, b.ZoneCode, b.Aisle, null, warehouse, BinCapacityOrigins.Warehouse));
        }
        return result;
    }

    /// <summary>
    /// Qué hacer con el cupo estimado de una posición: nueva → nace con él; existente con cupo → se conserva SIEMPRE (nunca se
    /// pisa un cupo capturado a mano); existente sin cupo → se llena solo con --update.
    /// </summary>
    public static BinCapacityAction Decide(bool binExists, int? currentCapacity, bool update)
        => !binExists ? BinCapacityAction.AssignOnCreate
            : currentCapacity is not null ? BinCapacityAction.KeepExisting
            : update ? BinCapacityAction.FillExisting
            : BinCapacityAction.SkipWithoutUpdate;
}
