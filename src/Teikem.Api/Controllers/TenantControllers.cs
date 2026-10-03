using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc;
using Teikem.Api.Auth;
using Teikem.Domain.Constants;
using Teikem.Domain.Tenancy;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
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

    /// <summary>Región y formatos (2026-10): regiones con sus valores por defecto y valores permitidos de cada campo (solo sesión).</summary>
    [HttpGet("format-options")]
    public TenantFormatOptionsDto FormatOptions() => tenants.GetFormatOptions();

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

/// <summary>
/// Antes de leer el cuerpo de la subida de un logo: si la petición ya declara más de lo que cabe (512 KB + holgura del multipart)
/// responde 413 sin leerla, y limita el cuerpo sin longitud declarada (chunked) a ese máximo.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class BrandLogoUploadLimitAttribute : Attribute, IResourceFilter
{
    /// <summary>Holgura para el sobre multipart (límites, cabeceras de la parte).</summary>
    public const int EnvelopeBytes = 16 * 1024;
    public const int MaxRequestBytes = BrandLogoRules.MaxBytes + EnvelopeBytes;

    public void OnResourceExecuting(ResourceExecutingContext context)
    {
        var req = context.HttpContext.Request;
        if (req.ContentLength is > MaxRequestBytes) throw new PayloadTooLargeException(BrandLogoRules.TooLargeMessage);
        var feature = context.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (feature is { IsReadOnly: false }) feature.MaxRequestBodySize = MaxRequestBytes;
    }

    public void OnResourceExecuted(ResourceExecutedContext context) { }
}

/// <summary>
/// Logos de la marca de la compañía (Ajustes → Marca). Cuatro ranuras: lockup, lockup-inverted, mark, mark-inverted. Escribir exige
/// admin.tenant; leer solo sesión (es la marca de la interfaz). Subida: multipart/form-data con el campo 'file' (SVG/PNG/JPG/WebP,
/// hasta 512 KB) → 400 (vacío, imagen dañada, SVG con contenido activo), 413 (tamaño) y 415 (formato). Lectura con ETag y
/// Last-Modified (304), nosniff y una CSP restrictiva: el archivo no ejecuta nada ni abierto directamente.
/// </summary>
[ApiController]
[Route("api/v1/tenant/brand/logos")]
[Authorize]
public sealed class BrandLogosController(BrandLogoService logos) : ControllerBase
{
    /// <summary>Los logos que tiene la compañía (sin el archivo).</summary>
    [HttpGet]
    public Task<IReadOnlyList<BrandLogoDto>> List(CancellationToken ct) => logos.ListAsync(ct);

    /// <summary>El archivo de la ranura (404 si no hay logo en ella).</summary>
    [HttpGet("{slot}")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK, "image/svg+xml", "image/png", "image/jpeg", "image/webp")]
    public async Task<IActionResult> Get(string slot, CancellationToken ct)
    {
        var f = await logos.GetAsync(slot, ct);
        Response.Headers.CacheControl = "private, no-cache";
        Response.Headers.XContentTypeOptions = "nosniff";
        Response.Headers.ContentSecurityPolicy = "default-src 'none'; style-src 'unsafe-inline'; img-src data:; sandbox";
        Response.Headers["Cross-Origin-Resource-Policy"] = "same-site";
        Response.Headers.ContentDisposition = $"inline; filename=\"logo-{slot}.{BrandLogoRules.Extension(f.ContentType)}\"";
        return File(f.Content, f.ContentType, new DateTimeOffset(DateTime.SpecifyKind(f.LastModifiedUtc, DateTimeKind.Utc)),
            new Microsoft.Net.Http.Headers.EntityTagHeaderValue($"\"{f.ETag}\""));
    }

    /// <summary>Sube o reemplaza el logo de la ranura (admin.tenant).</summary>
    [HttpPut("{slot}"), RequirePermission(PermissionCatalog.AdminTenant), BrandLogoUploadLimit]
    public async Task<BrandLogoDto> Put(string slot, IFormFile? file, CancellationToken ct)
    {
        if (file is null)
        {
            if (!Request.HasFormContentType) throw new UnsupportedMediaException("Envíe el logo como multipart/form-data en el campo 'file'.");
            throw new ValidationException("file", BrandLogoRules.FileRequiredMessage);
        }
        if (file.Length > BrandLogoRules.MaxBytes) throw new PayloadTooLargeException(BrandLogoRules.TooLargeMessage);
        using var ms = new MemoryStream((int)file.Length);
        await using (var s = file.OpenReadStream()) await s.CopyToAsync(ms, ct);
        return await logos.SaveAsync(slot, ms.ToArray(), file.ContentType, ct);
    }

    /// <summary>Quita el logo de la ranura (admin.tenant); la interfaz vuelve al de Teikem.</summary>
    [HttpDelete("{slot}"), RequirePermission(PermissionCatalog.AdminTenant)]
    public async Task<IActionResult> Remove(string slot, CancellationToken ct) { await logos.RemoveAsync(slot, ct); return NoContent(); }
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
