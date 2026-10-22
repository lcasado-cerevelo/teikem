using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Teikem.Api.Auth;
using Teikem.Domain.Tenancy;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;

namespace Teikem.Api.Controllers;

/// <summary>
/// 2026-10-22 — marca pública para la pantalla de entrada (sin sesión): `GET /api/v1/public/brand?tenant=` da el nombre y los logos de la compañía; `GET /api/v1/public/brand/logo/{slot}?tenant=`
/// el archivo. `host` = la dirección de la página (si la web no se sirve desde el mismo sitio que el API); `tenant` = identificador público de la última compañía usada en esa computadora (opcional); sin él, o si no existe, manda el dominio y luego el subdominio de la dirección con la que se abrió la página.
/// Un identificador o una dirección sin compañía responde 200 con `source: none` (nada que adivinar); el archivo sin logo, 404. Solo salen nombre y logo.
/// </summary>
[ApiController]
[Route("api/v1/public/brand")]
[AllowAnonymous]
[EnableRateLimiting(DeviceRateLimits.PublicBrand)]
public sealed class PublicBrandController(PublicBrandService brand) : ControllerBase
{
    /// <summary>La dirección con la que se abrió la página: la que manda la web (puede servirse desde otro sitio que el API) o, si no, la de esta petición.</summary>
    private string? HostOf(string? host) => !string.IsNullOrWhiteSpace(host) ? host : Request.Host.HasValue ? Request.Host.Host : null;

    [HttpGet]
    public Task<PublicBrandDto> Get([FromQuery] Guid? tenant, [FromQuery] string? host, CancellationToken ct) => brand.GetAsync(tenant, HostOf(host), ct);

    [HttpGet("logo/{slot}")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK, "image/svg+xml", "image/png", "image/jpeg", "image/webp")]
    public async Task<IActionResult> GetLogo(string slot, [FromQuery] Guid? tenant, [FromQuery] string? host, CancellationToken ct)
    {
        var f = await brand.GetLogoAsync(slot, tenant, HostOf(host), ct);
        Response.Headers.CacheControl = "public, max-age=300";
        Response.Headers.XContentTypeOptions = "nosniff";
        Response.Headers.ContentSecurityPolicy = "default-src 'none'; style-src 'unsafe-inline'; img-src data:; sandbox";
        Response.Headers["Cross-Origin-Resource-Policy"] = "same-site";
        Response.Headers.ContentDisposition = $"inline; filename=\"logo-{slot}.{BrandLogoRules.Extension(f.ContentType)}\"";
        return File(f.Content, f.ContentType, new DateTimeOffset(DateTime.SpecifyKind(f.LastModifiedUtc, DateTimeKind.Utc)),
            new Microsoft.Net.Http.Headers.EntityTagHeaderValue($"\"{f.ETag}\""));
    }
}
