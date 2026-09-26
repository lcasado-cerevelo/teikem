using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Teikem.Api.Auth;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;

namespace Teikem.Api.Controllers;

/// <summary>
/// Lote 5 (P9) — 'Planificar el día' (L262): agrupa por zona de despacho las órdenes confirmadas sin ruta de la fecha, en la
/// ruta abierta de la zona o en una nueva con el chofer estándar. Idempotente y serializada por compañía. No optimiza, no
/// despacha, no asigna vehículo ni cambia el estatus de las órdenes. Módulo LTL_GROUND (núcleo); permiso trips.plan.
/// El TenantId sale del principal; la solicitud solo lleva la fecha, las zonas y CreateEmptyTrips.
/// </summary>
[ApiController]
[Route("api/v1/trips")]
[Authorize]
[RequireModule(ModuleKeys.LtlGround)]
public sealed class TripPlanningController(TripDayPlanningService planning) : ControllerBase
{
    /// <summary>
    /// Planifica el día: {planDate, dispatchZoneIds? (sin lista = todas las zonas activas; máximo 50), createEmptyTrips?}.
    /// Devuelve por zona la ruta usada o creada, las órdenes asignadas y omitidas y sus avisos.
    /// </summary>
    [HttpPost("plan-day"), RequirePermission(PermissionCatalog.TripsPlan)]
    public Task<PlanDayResultDto> PlanDay([FromBody] PlanDayRequest req, CancellationToken ct) => planning.PlanDayAsync(req, ct);
}
