using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Teikem.Api.Auth;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;

namespace Teikem.Api.Controllers;

/// <summary>Módulo 0B: catálogo de módulos y encendido por tenant (el menú se arma leyendo esto).</summary>
[ApiController]
[Route("api/v1/modules")]
[Authorize]
public sealed class ModulesController(ModuleService modules) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<ModuleDto>> Catalog(CancellationToken ct) => modules.GetCatalogAsync(ct);

    /// <summary>Encender/apagar: encender exige la dependencia; apagar apaga en cascada; los núcleo no se apagan.</summary>
    [HttpPut("{moduleKey}"), RequirePermission(PermissionCatalog.AdminTenant), RequireAal2]
    public Task<IReadOnlyList<ModuleDto>> Set(string moduleKey, [FromBody] TenantModuleRequest req, CancellationToken ct) => modules.SetEnabledAsync(moduleKey, req.IsEnabled, ct);
}

/// <summary>Módulo 0B: ajustes de la compañía (defaults de captura, calendario, MFA/sesión, marca) y feriados.</summary>
[ApiController]
[Route("api/v1/tenant")]
[Authorize]
public sealed class TenantController(TenantService tenants) : ControllerBase
{
    [HttpGet("settings")]
    public Task<TenantSettingsDto> Settings(CancellationToken ct) => tenants.GetSettingsAsync(ct);

    [HttpPut("settings"), RequirePermission(PermissionCatalog.AdminTenant)]
    public Task<TenantSettingsDto> Update([FromBody] TenantSettingsUpdateRequest req, CancellationToken ct) => tenants.UpdateSettingsAsync(req, ct);

    [HttpGet("holidays")]
    public Task<IReadOnlyList<TenantHolidayDto>> Holidays([FromQuery] int? year, CancellationToken ct) => tenants.GetHolidaysAsync(year, ct);

    [HttpPost("holidays"), RequirePermission(PermissionCatalog.AdminTenant)]
    public Task<TenantHolidayDto> AddHoliday([FromBody] TenantHolidayRequest req, CancellationToken ct) => tenants.AddHolidayAsync(req, ct);

    [HttpDelete("holidays/{id:int}"), RequirePermission(PermissionCatalog.AdminTenant)]
    public async Task<IActionResult> RemoveHoliday(int id, CancellationToken ct) { await tenants.RemoveHolidayAsync(id, ct); return NoContent(); }

    /// <summary>Calendario laboral: últimos N días hábiles (sin fines de semana ni feriados) para tendencias/SLA.</summary>
    [HttpGet("work-days")]
    public Task<IReadOnlyList<DateOnly>> WorkDays([FromQuery] int n = 7, CancellationToken ct = default) => tenants.LastWorkDaysAsync(Math.Clamp(n, 1, 60), ct);
}

/// <summary>Plataforma (solo administradores de Teikem): listar y aprovisionar compañías.</summary>
[ApiController]
[Route("api/v1/platform/tenants")]
[Authorize(Policy = Policies.PlatformAdmin)]
public sealed class PlatformController(ProvisioningService provisioning) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<TenantSummaryDto>> List(CancellationToken ct) => provisioning.GetTenantsAsync(ct);

    /// <summary>Crea tenant + módulos (núcleo + pedidos) + roles clonados + admin + vistas/indicadores/gráficos por default.</summary>
    [HttpPost, RequireAal2]
    public Task<TenantProvisionResult> Provision([FromBody] TenantProvisionRequest req, CancellationToken ct) => provisioning.ProvisionAsync(req, ct);
}
