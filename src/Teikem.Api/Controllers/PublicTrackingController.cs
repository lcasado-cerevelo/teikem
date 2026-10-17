using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Teikem.Api.Auth;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;

namespace Teikem.Api.Controllers;

/// <summary>
/// Lote 103 — «¿Cómo va mi orden?». Consulta PÚBLICA (sin sesión) por el número de conduce de una compañía: devuelve solo el avance (etapa de las que tiene el camino normal de la compañía, estado y fecha del último
/// movimiento), sin nombres, direcciones, teléfonos ni montos. 404 con el mismo mensaje para compañía sin seguimiento, número inexistente o mal escrito. 20 por minuto por IP (429 después).
/// </summary>
[ApiController]
[Route("api/v1/public/track")]
public sealed class PublicTrackingController(PublicTrackingService tracking) : ControllerBase
{
    /// <summary>El avance del paquete con ese conduce en la compañía de ese identificador (<c>?lang=es|en</c> opcional).</summary>
    [HttpGet("{slug}/{waybill}"), AllowAnonymous, EnableRateLimiting(DeviceRateLimits.Track)]
    public Task<PublicTrackDto> Track(string slug, string waybill, [FromQuery] string? lang, CancellationToken ct) => tracking.TrackAsync(slug, waybill, lang, ct);
}

/// <summary>Lote 103 — la compañía enciende o apaga el seguimiento público poniendo o quitando su identificador (Ajustes de la compañía). Ver: cualquier usuario de la compañía; cambiar: admin.tenant.</summary>
[ApiController]
[Route("api/v1/tenant/tracking")]
[Authorize]
public sealed class TrackingSettingsController(PublicTrackingService tracking) : ControllerBase
{
    [HttpGet]
    public Task<TrackingSettingsDto> Get(CancellationToken ct) => tracking.GetSettingsAsync(ct);

    /// <summary>Slug vacío = apagar. 400 si no es válido o está reservado; 409 si ya lo usa otra compañía.</summary>
    [HttpPut, RequirePermission(PermissionCatalog.AdminTenant)]
    public Task<TrackingSettingsDto> Put([FromBody] TrackingSettingsRequest req, CancellationToken ct) => tracking.SetSettingsAsync(req, ct);
}
