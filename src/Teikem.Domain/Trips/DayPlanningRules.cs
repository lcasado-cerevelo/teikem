namespace Teikem.Domain.Trips;

/// <summary>
/// Orden candidata a 'Planificar el día', en términos de dominio. La arma TripDayPlanningService a partir del
/// UnassignedCandidate del pool (TripOrderService.PoolAsync), que vive en Infrastructure y el dominio no puede referenciar.
/// ZoneId null = sin zona o con zona ambigua (ZoneAmbiguous distingue los dos casos).
/// IneligibleReason: el motivo de TripRules.CheckEligibility (p. ej. sin la capacidad ASSIGN_TRIP), o null si es asignable.
/// </summary>
public sealed record DayPlanningCandidate(
    int TransportOrderId,
    string OrderNumber,
    int? ZoneId,
    bool ZoneAmbiguous,
    DateTime? RequestedDate,
    string? IneligibleReason);

/// <summary>Orden que 'Planificar el día' deja sin asignar en una zona, con su código de motivo y el texto visible.</summary>
public sealed record DayPlanningSkip(int TransportOrderId, string OrderNumber, string ReasonCode, string Reason);

/// <summary>
/// Resultado de una zona: órdenes a asignar (en orden de fecha solicitada y número), omitidas con su motivo, si la zona ya
/// tiene ruta abierta y si hay que crear una ruta nueva para ella.
/// </summary>
public sealed record DayPlanningZone(
    int ZoneId,
    bool HasOpenTrip,
    bool CreateTrip,
    IReadOnlyList<int> Assigned,
    IReadOnlyList<DayPlanningSkip> Skipped);

/// <summary>Reparto completo: una entrada por zona destino (id ascendente) y las órdenes sin zona o con zona ambigua.</summary>
public sealed record DayPlanningAllocation(IReadOnlyList<DayPlanningZone> Zones, int OrdersWithoutZone)
{
    public int OrdersAssigned => Zones.Sum(z => z.Assigned.Count);
    public int OrdersSkipped => Zones.Sum(z => z.Skipped.Count);
    public int TripsToCreate => Zones.Count(z => z.CreateTrip);
}

/// <summary>Solicitud validada: fecha del plan y zonas pedidas sin repetir (null = todas las zonas activas del tenant).</summary>
public sealed record DayPlanningValidation(string? Field, string? Error, DateOnly? PlanDate, IReadOnlyList<int>? ZoneIds)
{
    public bool IsValid => Error is null;
}

/// <summary>
/// Lote 5 (P9) — reglas puras de 'Planificar el día' (L262: agrupar órdenes confirmadas por fecha/zona en trips).
/// - Cada zona destino usa su ruta abierta (DRAFT/PLANNED, activa, de la fecha; la de menor id) o, si no tiene y hay órdenes
///   asignables (o se pidió CreateEmptyTrips), se crea una nueva.
/// - Las órdenes asignables de la zona se agregan en orden de fecha solicitada (sin fecha al final) y número de orden.
/// - Una orden no elegible (IneligibleReason) o que llevaría la ruta por encima del tope técnico de 300 paradas se omite
///   con su motivo; las demás siguen.
/// - Las órdenes sin zona o con zona ambigua no se tocan: solo se cuentan en OrdersWithoutZone.
/// - Es determinista: la misma entrada produce la misma salida. La idempotencia la da el pool (solo órdenes sin ruta vigente)
///   más la reutilización de la ruta abierta.
/// Los mensajes son constantes públicas porque el manual y la FAQ los citan tal cual.
/// </summary>
public static class DayPlanningRules
{
    /// <summary>Máximo de zonas por solicitud de 'Planificar el día'.</summary>
    public const int MaxZones = 50;

    public const string MaxZonesMessage = "Máximo 50 zonas por planificación.";

    /// <summary>Código del motivo de omisión por el tope técnico de paradas de una ruta.</summary>
    public const string CapacityHardCapCode = "CAPACITY_HARD_CAP";
    public const string CapacityHardCapMessage = "La ruta llegó al máximo de 300 paradas; la orden queda sin asignar.";

    /// <summary>Código del motivo de omisión cuando la orden no es elegible (el texto es el motivo de TripRules.CheckEligibility).</summary>
    public const string NotEligibleCode = "NOT_ELIGIBLE";

    /// <summary>Texto del aviso por orden omitida en el resultado de una zona: 'Orden {número}: {motivo}'.</summary>
    public static string SkippedIssueMessage(string orderNumber, string reason) => $"Orden {orderNumber}: {reason}";

    // ---------------------------------------------------------------- validación

    /// <summary>
    /// Valida la solicitud: fecha obligatoria entre ayer y +60 días (TripPlanningRules.ValidatePlanDate) y, si vienen zonas,
    /// sin repetir y como máximo 50. Zonas null o vacías = todas las zonas activas del tenant (ZoneIds null).
    /// </summary>
    public static DayPlanningValidation ValidateRequest(DateOnly? planDate, IReadOnlyCollection<int>? dispatchZoneIds, DateOnly today)
    {
        if (TripPlanningRules.ValidatePlanDate(planDate, today) is string dateError)
            return new DayPlanningValidation("planDate", dateError, null, null);

        if (dispatchZoneIds is null || dispatchZoneIds.Count == 0)
            return new DayPlanningValidation(null, null, planDate, null);

        var distinct = dispatchZoneIds.Distinct().ToList();
        if (distinct.Count > MaxZones)
            return new DayPlanningValidation("dispatchZoneIds", MaxZonesMessage, planDate, null);

        return new DayPlanningValidation(null, null, planDate, distinct);
    }

    // ---------------------------------------------------------------- reparto

    /// <summary>
    /// Reparte el pool por zona destino.
    /// - openTrips: zona → paradas vigentes de su ruta abierta (solo zonas que ya tienen ruta abierta).
    /// - targetZones: zonas a planificar; las órdenes de otras zonas se ignoran (no cuentan en ningún total).
    /// - createEmptyTrips: crea la ruta de la zona aunque no tenga órdenes asignables (destino del escaneo Outbound).
    /// Devuelve una entrada por zona destino, en id ascendente.
    /// </summary>
    public static DayPlanningAllocation Allocate(
        IReadOnlyList<DayPlanningCandidate> candidates,
        IReadOnlyDictionary<int, int> openTrips,
        IReadOnlyCollection<int> targetZones,
        bool createEmptyTrips)
    {
        ArgumentNullException.ThrowIfNull(openTrips);
        var targets = new HashSet<int>(targetZones ?? Array.Empty<int>());
        var byZone = targets.ToDictionary(z => z, _ => new List<DayPlanningCandidate>());
        var withoutZone = 0;
        var seen = new HashSet<int>();

        foreach (var c in candidates ?? Array.Empty<DayPlanningCandidate>())
        {
            if (c is null || !seen.Add(c.TransportOrderId)) continue;   // defensa: una orden cuenta una sola vez
            if (c.ZoneId is not int zoneId) { withoutZone++; continue; }
            if (byZone.TryGetValue(zoneId, out var list)) list.Add(c);
        }

        var zones = new List<DayPlanningZone>(byZone.Count);
        foreach (var zoneId in byZone.Keys.OrderBy(z => z))
        {
            var hasOpen = openTrips.TryGetValue(zoneId, out var currentStops);
            var stops = Math.Max(0, hasOpen ? currentStops : 0);
            var assigned = new List<int>();
            var skipped = new List<DayPlanningSkip>();

            foreach (var c in Order(byZone[zoneId]))
            {
                if (!string.IsNullOrWhiteSpace(c.IneligibleReason))
                    skipped.Add(new DayPlanningSkip(c.TransportOrderId, c.OrderNumber, NotEligibleCode, c.IneligibleReason!));
                else if (stops + assigned.Count >= TripRules.MaxStopsHardCap)
                    skipped.Add(new DayPlanningSkip(c.TransportOrderId, c.OrderNumber, CapacityHardCapCode, CapacityHardCapMessage));
                else
                    assigned.Add(c.TransportOrderId);
            }

            var createTrip = !hasOpen && (assigned.Count > 0 || createEmptyTrips);
            zones.Add(new DayPlanningZone(zoneId, hasOpen, createTrip, assigned, skipped));
        }

        return new DayPlanningAllocation(zones, withoutZone);
    }

    /// <summary>
    /// Quita del reparto las órdenes que otra operación asignó a una ruta entre la lectura del pool y su bloqueo (ya no están
    /// 'sin asignar': no cuentan como asignadas ni como omitidas). Una zona sin ruta abierta que se queda sin órdenes solo
    /// conserva CreateTrip si se pidió CreateEmptyTrips.
    /// </summary>
    public static DayPlanningAllocation RemoveTaken(DayPlanningAllocation allocation, IReadOnlyCollection<int> taken, bool createEmptyTrips)
    {
        ArgumentNullException.ThrowIfNull(allocation);
        if (taken is null || taken.Count == 0) return allocation;
        var set = new HashSet<int>(taken);
        var zones = allocation.Zones.Select(z =>
        {
            var assigned = z.Assigned.Where(id => !set.Contains(id)).ToList();
            var skipped = z.Skipped.Where(s => !set.Contains(s.TransportOrderId)).ToList();
            var createTrip = !z.HasOpenTrip && (assigned.Count > 0 || createEmptyTrips);
            return new DayPlanningZone(z.ZoneId, z.HasOpenTrip, createTrip, assigned, skipped);
        }).ToList();
        return new DayPlanningAllocation(zones, allocation.OrdersWithoutZone);
    }

    /// <summary>Fecha solicitada (sin fecha al final), número de orden (ordinal) y id: orden total y determinista.</summary>
    private static IEnumerable<DayPlanningCandidate> Order(IEnumerable<DayPlanningCandidate> list)
        => list
            .OrderBy(c => c.RequestedDate is null)
            .ThenBy(c => c.RequestedDate)
            .ThenBy(c => c.OrderNumber, StringComparer.Ordinal)
            .ThenBy(c => c.TransportOrderId);
}
