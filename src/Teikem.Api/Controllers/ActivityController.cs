using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Teikem.Api.Auth;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;

namespace Teikem.Api.Controllers;

/// <summary>
/// Lote 7A (P1) — panel "Actividad reciente" del Pulso: eventos de negocio por módulo (maestro, módulo 12). Mismo patrón que
/// /api/v1/analytics/pulse: módulo ANALYTICS y analytics.view; además el servicio exige el permiso del módulo pedido
/// (Almacén: inventory.view) y responde 403 'No tiene permiso para ver la actividad del módulo {module}.' si no lo tiene.
/// </summary>
[ApiController]
[Route("api/v1/analytics/activity")]
[Authorize]
[RequireModule(ModuleKeys.Analytics)]
[RequirePermission(PermissionCatalog.AnalyticsView)]
public sealed class ActivityController(ActivityFeedService feed) : ControllerBase
{
    /// <summary>
    /// Eventos del módulo (sin module: el primer módulo visible) en la ventana 24h (default), 48h o today; onlyMandatory
    /// deja solo los obligatorios; skip/take paginan (take ≤ 50, default 50). visibleModules = pestañas que el usuario ve.
    /// </summary>
    [HttpGet]
    public Task<ActivityPageDto> Get([FromQuery] string? module, [FromQuery] string? window, [FromQuery] bool onlyMandatory = false,
        [FromQuery] int skip = 0, [FromQuery] int take = 50, CancellationToken ct = default)
        => feed.GetAsync(new ActivityQuery(module, string.IsNullOrWhiteSpace(window) ? "24h" : window, onlyMandatory, skip, take), ct);
}
