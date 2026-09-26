using Teikem.Domain.Catalogs;
using Teikem.Domain.Common;
using Teikem.Domain.Fleet;

namespace Teikem.Domain.Trips;

/// <summary>
/// Lote 5 — Ruta del día ('Trip' en el SQL). En pantalla, 'Ruta' es el Trip más su versión vigente del plan (Route).
/// - Code 'AAAA-####' (NumberSequence TRIP por tenant): inmutable, único por compañía (UQ_Trip_Code) y nunca reutilizado.
/// - Estatus TripStatus vía StatusService: DRAFT → PLANNED → DISPATCHED → IN_PROGRESS → COMPLETED; CANCELLED ('Eliminar
///   ruta', solo desde DRAFT/PLANNED) deja IsActive = 0.
/// - DispatchZoneId, VehicleId y DriverId llevan FKs compuestas (Id, TenantId) en el SQL (segunda barrera multi-tenant).
/// - OriginWarehouseId se mapea pero no se escribe en este lote.
/// - PlannedEndUtc y los totales los escribe RouteWriter.RecomputeAsync (técnicos: no se auditan).
/// Los controladores no importan este namespace (choque con Microsoft.AspNetCore.Routing.Route): usan alias.
/// </summary>
[AuditEntity(Constants.EntityTypes.Trip)]
public class Trip : ITenantScoped, ISoftDeletable, IHasStatus, IAuditStamped
{
    public int TripId { get; set; }
    [NotAudited] public Guid PublicId { get; set; }
    public int TenantId { get; set; }
    public string Code { get; set; } = string.Empty;
    public DateOnly PlanDate { get; set; }
    public int? DispatchZoneId { get; set; }
    public int? OriginWarehouseId { get; set; }
    public int? VehicleId { get; set; }
    public int? DriverId { get; set; }
    public int StatusCodeId { get; set; }
    public DateTime? PlannedStartUtc { get; set; }
    [NotAudited] public DateTime? PlannedEndUtc { get; set; }
    public DateTime? ActualStartUtc { get; set; }
    public DateTime? ActualEndUtc { get; set; }
    [NotAudited] public decimal? TotalDistanceKm { get; set; }
    [NotAudited] public int? TotalDurationMin { get; set; }
    public bool IsActive { get; set; } = true;
    [NotAudited] public DateTime CreatedAtUtc { get; set; }
    public int? CreatedBy { get; set; }
    [NotAudited] public DateTime? UpdatedAtUtc { get; set; }
    public int? UpdatedBy { get; set; }
    [NotAudited] public byte[]? RowVersion { get; set; }

    public StatusCode? Status { get; set; }
    public DispatchZone? DispatchZone { get; set; }
    public Driver? Driver { get; set; }
    public Vehicle? Vehicle { get; set; }
    public ICollection<TripOrder> Orders { get; set; } = new List<TripOrder>();
    public ICollection<Route> Routes { get; set; } = new List<Route>();
}

/// <summary>
/// Lote 5 — Orden asignada a una ruta. Una orden solo puede estar en UNA ruta vigente (IsCurrent = 1; UX_TripOrder_Current);
/// IsCurrent pasa a 0 al completar la ruta. Liberarla de una ruta abierta es un DELETE físico (L272), auditado bajo TRIP.
/// Solo la escribe RouteWriter.
/// </summary>
[AuditEntity(Constants.EntityTypes.Trip)]
public class TripOrder : ITenantScoped
{
    public int TripOrderId { get; set; }
    public int TenantId { get; set; }
    public int TripId { get; set; }
    public int TransportOrderId { get; set; }
    public int? SortHint { get; set; }
    public bool IsCurrent { get; set; } = true;
    [NotAudited] public DateTime AssignedAtUtc { get; set; }
    public int? AssignedBy { get; set; }

    public Trip? Trip { get; set; }
}
