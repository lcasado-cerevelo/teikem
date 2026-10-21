using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Teikem.Api.Auth;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;

namespace Teikem.Api.Controllers;

/// <summary>
/// Lote 50 (P2.2) — Sistema → Trabajos programados: los trabajos automáticos de la compañía (limpieza del historial, revisión diaria
/// de inventario…). Módulo SYSTEM (núcleo, siempre encendido); todo con jobs.manage. Rutas por clave del trabajo; nunca DELETE.
/// </summary>
[ApiController]
[Route("api/v1/jobs")]
[Authorize]
[RequireModule(ModuleKeys.System)]
// Lote 185: además del permiso de datos de cada acción, la pantalla exige su permiso de acceso del menú ("jobs.view"); quitarlo de un rol quita también los datos (Y entre ambos).
[RequirePermission("jobs.view")]
public sealed class JobsController(ScheduledJobService jobs) : ControllerBase
{
    /// <summary>Los trabajos de la compañía con su horario, último resultado y próxima corrida.</summary>
    [HttpGet, RequirePermission(PermissionCatalog.JobsManage)]
    public Task<IReadOnlyList<ScheduledJobDto>> List(CancellationToken ct) => jobs.ListAsync(ct);

    [HttpGet("{key}"), RequirePermission(PermissionCatalog.JobsManage)]
    public Task<ScheduledJobDto> Get(string key, CancellationToken ct) => jobs.GetAsync(key, ct);

    /// <summary>Interruptor y/o horario (null = sin cambio). 400 con el mensaje exacto si el horario no es válido; 409 si cambió (rowVersion).</summary>
    [HttpPatch("{key}"), RequirePermission(PermissionCatalog.JobsManage)]
    public Task<ScheduledJobDto> Update(string key, [FromBody] ScheduledJobRequest req, CancellationToken ct) => jobs.UpdateAsync(key, req, ct);

    /// <summary>Pide una corrida ya (aunque el trabajo esté apagado); el sistema la toma en unos segundos. 409 si ya se está ejecutando.</summary>
    [HttpPost("{key}/run"), RequirePermission(PermissionCatalog.JobsManage)]
    public Task<ScheduledJobDto> RunNow(string key, CancellationToken ct) => jobs.RunNowAsync(key, ct);

    /// <summary>Historial de corridas (las más recientes primero; hasta 100).</summary>
    [HttpGet("{key}/runs"), RequirePermission(PermissionCatalog.JobsManage)]
    public Task<IReadOnlyList<ScheduledJobRunDto>> Runs(string key, [FromQuery] int take, CancellationToken ct) => jobs.RunsAsync(key, take, ct);
}
