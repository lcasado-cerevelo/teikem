using Teikem.Domain.Catalogs;
using Teikem.Domain.Common;

namespace Teikem.Domain.Trips;

/// <summary>
/// Lote 5 — Versión del plan de una ruta (Trip). IsActive = versión vigente (a lo sumo una por Trip: UX_Route_Trip_Active);
/// Version única por Trip (UQ_Route_Version). Estatus RouteStatus: DRAFT → OPTIMIZED → ACTIVE (congelada al despachar);
/// ARCHIVED terminal. Sin TenantId: se alcanza SIEMPRE por un Trip ya filtrado. StopCount y los totales los escribe
/// RouteWriter.RecomputeAsync. Solo la escribe RouteWriter.
/// </summary>
[AuditEntity(Constants.EntityTypes.Route)]
public class Route : ISoftDeletable, IHasStatus
{
    public int RouteId { get; set; }
    public int TripId { get; set; }
    public int Version { get; set; } = 1;
    public bool IsActive { get; set; } = true;
    public int StatusCodeId { get; set; }
    [NotAudited] public decimal? TotalDistanceKm { get; set; }
    [NotAudited] public int? TotalDurationMin { get; set; }
    [NotAudited] public int StopCount { get; set; }
    [NotAudited] public DateTime CreatedAtUtc { get; set; }
    [NotAudited] public byte[]? RowVersion { get; set; }

    public Trip? Trip { get; set; }
    public StatusCode? Status { get; set; }
    public ICollection<RouteStop> Stops { get; set; } = new List<RouteStop>();
}

/// <summary>
/// Lote 5 — Parada de una versión de ruta: la parada DELIVERY pendiente (OrderStop) de una orden de la ruta, en su
/// secuencia. Nace PENDING sin fila de historial; los tiempos y distancias planificados los escribe RouteWriter
/// (técnicos: no se auditan). Sin TenantId: se alcanza por su Route → Trip. Solo la escribe RouteWriter.
/// </summary>
[AuditEntity(Constants.EntityTypes.RouteStop)]
public class RouteStop : IHasStatus
{
    public int RouteStopId { get; set; }
    public int RouteId { get; set; }
    public int OrderStopId { get; set; }
    public int Sequence { get; set; }
    [NotAudited] public DateTime? PlannedArrivalUtc { get; set; }
    [NotAudited] public DateTime? PlannedDepartureUtc { get; set; }
    [NotAudited] public decimal? DistanceFromPrevKm { get; set; }
    [NotAudited] public int? DurationFromPrevMin { get; set; }
    public int StatusCodeId { get; set; }
    public DateTime? ActualArrivalUtc { get; set; }
    public DateTime? ActualDepartureUtc { get; set; }

    public Route? Route { get; set; }
    public StatusCode? Status { get; set; }
}
