using System.Globalization;
using System.Text;

namespace Teikem.Domain.Trips;

/// <summary>
/// Lote 5 (P3) — motor HEURISTIC (puro y determinista; sin BD ni llamadas externas). Es lo que corre detrás de
/// IRouteOptimizer en este lote; VROOM / OR-Tools lo reemplazarán sin tocar el servicio.
///
/// 1) Orden total de las paradas: zona, fin de ventana, inicio de ventana, código postal, pueblo normalizado (sin acentos
///    ni mayúsculas), número de orden y OrderStopId. En cada criterio los nulos van al final, así que dos corridas con la
///    misma entrada dan exactamente la misma secuencia (sin depender del orden de llegada).
/// 2) Asignación greedy en ese orden contra la capacidad del vehículo: paradas, peso y volumen. Una capacidad NULL no tiene
///    límite; el peso o volumen NULL de una orden cuenta 0. La parada que no cabe queda sin asignar con el PRIMER motivo que
///    falle (paradas → peso → volumen) y se sigue con la siguiente (una más liviana posterior sí puede entrar).
/// 3) Sin geometría: Polyline = null.
/// </summary>
public static class HeuristicRoutePlanner
{
    /// <summary>Planifica la ruta. La entrada no se modifica; el resultado pasa RouteOptimizationRules.ValidateResult.</summary>
    public static RouteOptimizationResult Plan(RouteOptimizationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var ordered = Order(request.Stops ?? Array.Empty<PlanStopInput>());
        var capacity = request.Capacity;

        var assigned = new List<int>(ordered.Count);
        var unassigned = new List<UnassignedPlanStop>();
        var weight = 0m;
        var volume = 0m;

        foreach (var stop in ordered)
        {
            var reason = FirstCapacityFailure(capacity, assigned.Count, weight, volume, stop);
            if (reason is not null)
            {
                unassigned.Add(new UnassignedPlanStop(stop.OrderStopId, reason));
                continue;
            }
            assigned.Add(stop.OrderStopId);
            weight += stop.WeightKg ?? 0m;
            volume += stop.VolumeM3 ?? 0m;
        }

        return new RouteOptimizationResult(assigned, unassigned, null);
    }

    /// <summary>Orden total determinista (ver cabecera). Público para las pruebas y para mostrar el criterio en el manual.</summary>
    public static IReadOnlyList<PlanStopInput> Order(IEnumerable<PlanStopInput> stops)
        => stops
            .OrderBy(s => IsBlank(s.ZoneCode)).ThenBy(s => s.ZoneCode?.Trim().ToUpperInvariant(), StringComparer.Ordinal)
            .ThenBy(s => s.WindowEndUtc is null).ThenBy(s => s.WindowEndUtc)
            .ThenBy(s => s.WindowStartUtc is null).ThenBy(s => s.WindowStartUtc)
            .ThenBy(s => IsBlank(s.PostalCode)).ThenBy(s => s.PostalCode?.Trim(), StringComparer.Ordinal)
            .ThenBy(s => IsBlank(s.City)).ThenBy(s => NormalizeCity(s.City), StringComparer.Ordinal)
            .ThenBy(s => s.OrderNumber ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(s => s.OrderStopId)
            .ToList();

    /// <summary>
    /// Primer motivo por el que la parada no cabe con lo ya asignado (CAPACITY_STOPS → CAPACITY_WEIGHT → CAPACITY_VOLUME), o
    /// null si cabe. Capacidad NULL = sin límite; peso/volumen NULL de la orden = 0.
    /// </summary>
    public static string? FirstCapacityFailure(VehicleCapacity? capacity, int assignedCount, decimal assignedWeightKg, decimal assignedVolumeM3, PlanStopInput stop)
    {
        if (capacity is null) return null;
        if (capacity.MaxStops is int maxStops && assignedCount + 1 > maxStops) return UnassignedReasons.CapacityStops;
        if (capacity.MaxWeightKg is decimal maxWeight && assignedWeightKg + (stop.WeightKg ?? 0m) > maxWeight) return UnassignedReasons.CapacityWeight;
        if (capacity.MaxVolumeM3 is decimal maxVolume && assignedVolumeM3 + (stop.VolumeM3 ?? 0m) > maxVolume) return UnassignedReasons.CapacityVolume;
        return null;
    }

    /// <summary>Pueblo para ordenar: sin acentos, mayúsculas invariantes y espacios colapsados ('  Bayamón ' → 'BAYAMON').</summary>
    public static string NormalizeCity(string? city)
    {
        if (string.IsNullOrWhiteSpace(city)) return string.Empty;
        var decomposed = city.Trim().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        var lastSpace = false;
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsWhiteSpace(c))
            {
                if (!lastSpace) sb.Append(' ');
                lastSpace = true;
                continue;
            }
            sb.Append(char.ToUpperInvariant(c));
            lastSpace = false;
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    private static bool IsBlank(string? s) => string.IsNullOrWhiteSpace(s);
}
