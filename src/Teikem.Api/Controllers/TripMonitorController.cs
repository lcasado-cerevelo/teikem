using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Teikem.Api.Auth;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;

namespace Teikem.Api.Controllers;

/// <summary>
/// Lote 5 (P7) — monitoreo de rutas: las despachadas o en curso del día (y las completadas, salvo includeCompleted=false),
/// con progreso, próxima ETA, pines aproximados, máximo de paradas y último ping (con el respaldo del chofer desde la salida).
/// Los totales se calculan sobre la fecha y la zona; la búsqueda libre solo filtra la lista. Módulo LTL_GROUND; trips.view.
/// El TenantId sale del principal. El mapa lo dibuja el front con GET /api/v1/trips/{id}.
/// </summary>
[ApiController]
[Route("api/v1/trips")]
[Authorize]
[RequireModule(ModuleKeys.LtlGround)]
public sealed class TripMonitorController(TripMonitorService monitor) : ControllerBase
{
    /// <summary>Monitor del día (sin fecha = hoy UTC), opcionalmente por zona, con búsqueda libre sobre los textos visibles.</summary>
    [HttpGet("monitor"), RequirePermission(PermissionCatalog.TripsView)]
    public Task<MonitorDto> Get([FromQuery] DateOnly? date, [FromQuery] int? dispatchZoneId, [FromQuery] string? search,
        [FromQuery] bool includeCompleted = true, CancellationToken ct = default)
        => monitor.ListAsync(new MonitorQuery(date, dispatchZoneId, search, includeCompleted), ct);
}
