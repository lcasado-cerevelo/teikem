using Teikem.Domain.Catalogs;
using Teikem.Domain.Common;

namespace Teikem.Domain.Trips;

/// <summary>
/// Lote 5 — Corrida de optimización de una ruta (bitácora: sin [AuditEntity]; su historial va a EntityStatusHistory bajo
/// OPTIMIZATION_RUN). Estatus OptimizationRunStatus: PENDING → OK | ERROR. OptimizationRunId es INT (el SQL pasó de BIGINT
/// a INT en el Lote 5). RouteId apunta a la versión creada (OK) o a la que se intentó reemplazar (ERROR). RequestJson no lleva
/// nombres de personas; ResponseJson se lee con RoutePlanJson (tolerante).
/// </summary>
public class OptimizationRun : ITenantScoped, IHasStatus
{
    public int OptimizationRunId { get; set; }
    public int TenantId { get; set; }
    public int? TripId { get; set; }
    public int? RouteId { get; set; }
    public int EngineLookupId { get; set; }
    public int StatusCodeId { get; set; }
    public string RequestJson { get; set; } = string.Empty;
    public string? ResponseJson { get; set; }
    public string? ErrorMessage { get; set; }
    public decimal? TotalDistanceKm { get; set; }
    public int? TotalDurationMin { get; set; }
    public int? UnassignedCount { get; set; }
    public DateTime StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public int? CreatedBy { get; set; }

    public LookupCode? Engine { get; set; }
    public StatusCode? Status { get; set; }
}
