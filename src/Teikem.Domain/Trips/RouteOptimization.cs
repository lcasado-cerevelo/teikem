namespace Teikem.Domain.Trips;

/// <summary>
/// Parada que entra al optimizador (sin nombres de personas: la solicitud se guarda en OptimizationRun.RequestJson).
/// Peso y volumen son los de la orden (NULL cuenta 0 contra la capacidad); Lat/Lng null si la parada no tiene coordenada.
/// </summary>
public sealed record PlanStopInput(
    int OrderStopId,
    string OrderNumber,
    string? ZoneCode,
    string? PostalCode,
    string City,
    DateTime? WindowStartUtc,
    DateTime? WindowEndUtc,
    int ServiceMinutes,
    decimal? WeightKg,
    decimal? VolumeM3,
    double? Lat,
    double? Lng);

/// <summary>Capacidad del vehículo de la ruta: NULL = sin límite (también cuando la ruta no tiene vehículo).</summary>
public sealed record VehicleCapacity(int? MaxStops, decimal? MaxWeightKg, decimal? MaxVolumeM3)
{
    public static readonly VehicleCapacity Unlimited = new(null, null, null);
}

/// <summary>Solicitud al optimizador: hora de salida planificada, capacidad del vehículo y paradas de la versión vigente.</summary>
public sealed record RouteOptimizationRequest(DateTime? StartUtc, VehicleCapacity? Capacity, IReadOnlyList<PlanStopInput> Stops);

/// <summary>Parada que el optimizador dejó fuera, con su motivo (UnassignedReasons).</summary>
public sealed record UnassignedPlanStop(int OrderStopId, string ReasonCode);

/// <summary>Resultado del optimizador: paradas asignadas en secuencia, las que no cupieron y la geometría (null en HEURISTIC).</summary>
public sealed record RouteOptimizationResult(IReadOnlyList<int> OrderedStopIds, IReadOnlyList<UnassignedPlanStop> Unassigned, string? Polyline);

/// <summary>Motivos por los que una parada no entra en la ruta optimizada (se muestran en la ficha mientras siga sin ruta).</summary>
public static class UnassignedReasons
{
    public const string CapacityStops = "CAPACITY_STOPS";
    public const string CapacityWeight = "CAPACITY_WEIGHT";
    public const string CapacityVolume = "CAPACITY_VOLUME";

    public const string CapacityStopsMessage = "Excede el máximo de paradas del vehículo.";
    public const string CapacityWeightMessage = "Excede la capacidad de peso del vehículo.";
    public const string CapacityVolumeMessage = "Excede la capacidad de volumen del vehículo.";

    /// <summary>Etiqueta del motivo; un código desconocido (motor futuro) se devuelve tal cual.</summary>
    public static string Message(string? code) => code?.Trim().ToUpperInvariant() switch
    {
        CapacityStops => CapacityStopsMessage,
        CapacityWeight => CapacityWeightMessage,
        CapacityVolume => CapacityVolumeMessage,
        _ => code ?? string.Empty,
    };
}

/// <summary>
/// Lote 5 (P0) — validación de la respuesta de CUALQUIER motor antes de escribirla: solo ids de la entrada, sin repetidos
/// (ni dentro de un grupo ni entre grupos) y asignados ∪ sin asignar = entrada. Devuelve null si es válida o el motivo.
/// </summary>
public static class RouteOptimizationRules
{
    public static string? ValidateResult(RouteOptimizationRequest request, RouteOptimizationResult? result)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (result is null) return "el motor no devolvió resultado.";
        var input = new HashSet<int>((request.Stops ?? Array.Empty<PlanStopInput>()).Select(s => s.OrderStopId));
        var seen = new HashSet<int>();

        foreach (var id in result.OrderedStopIds ?? Array.Empty<int>())
        {
            if (!input.Contains(id)) return $"la parada {id} no pertenece a la solicitud.";
            if (!seen.Add(id)) return $"la parada {id} aparece más de una vez.";
        }
        foreach (var u in result.Unassigned ?? Array.Empty<UnassignedPlanStop>())
        {
            if (u is null) return "hay una parada sin asignar vacía.";
            if (!input.Contains(u.OrderStopId)) return $"la parada {u.OrderStopId} no pertenece a la solicitud.";
            if (!seen.Add(u.OrderStopId)) return $"la parada {u.OrderStopId} aparece más de una vez.";
            if (string.IsNullOrWhiteSpace(u.ReasonCode)) return $"la parada {u.OrderStopId} no tiene motivo.";
        }
        var missing = input.Where(id => !seen.Contains(id)).OrderBy(id => id).ToList();
        if (missing.Count > 0) return $"faltan paradas en la respuesta: {string.Join(", ", missing)}.";
        return null;
    }
}
