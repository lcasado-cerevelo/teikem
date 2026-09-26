namespace Teikem.Infrastructure.Trips;

// Lote 5 (P0) — tipos de servicio que NO son contratos HTTP (no viven en Contracts): los usan TripService, TripOrderService y
// TripDayPlanningService entre sí. Firmas fijas: 'Planificar el día' (P9) se programa contra ellas.
//   - TripService.PrepareNumberingAsync(ct)                                  → contador TRIP en autocommit, antes de la transacción
//   - TripService.CreateTrackedAsync(TripCreateSpec, ct)                      → Trip tracked y guardado, dentro de la transacción
//   - TripOrderService.PoolAsync(UnassignedPoolFilter, ct)                    → órdenes asignables sin ruta vigente, con zona

/// <summary>
/// Alta de una ruta dentro de la transacción del llamador. Ids internos ya resueltos bajo el filtro de tenant.
/// UseDefaultDriver: con zona y sin chofer, toma el chofer por defecto de la zona. PlannedStartUtc null = 12:00 UTC de PlanDate.
/// </summary>
public sealed record TripCreateSpec(DateOnly PlanDate, int? DispatchZoneId, int? DriverId, int? VehicleId, DateTime? PlannedStartUtc, bool UseDefaultDriver);

/// <summary>
/// Filtro del pool 'sin asignar': RequestedUpTo = fecha solicitada ≤ ese día o nula; ZoneIds null = cualquier zona (lista vacía
/// = ninguna); IncludeNoZone agrega las órdenes sin zona o con zona ambigua.
/// </summary>
public sealed record UnassignedPoolFilter(DateOnly? RequestedUpTo, IReadOnlyCollection<int>? ZoneIds, bool IncludeNoZone);

/// <summary>
/// Orden del pool: zona resuelta (ZoneId/ZoneCode null si no hay o es ambigua) y el motivo por el que no se puede asignar
/// (IneligibleReason, p. ej. sin la capacidad ASSIGN_TRIP), o null si es asignable.
/// </summary>
public sealed record UnassignedCandidate(
    int TransportOrderId,
    Guid PublicId,
    string OrderNumber,
    int? ZoneId,
    string? ZoneCode,
    bool ZoneAmbiguous,
    string StatusCode,
    DateTime? RequestedDate,
    string? IneligibleReason);
