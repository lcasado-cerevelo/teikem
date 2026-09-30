using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Teikem.Api.Auth;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;

namespace Teikem.Api.Controllers;

/// <summary>
/// Lote 14 (D6) — panel "Necesita tu atención" del Pulso: exige pulse.attention y nada más en la política (sin módulo): cada
/// tipo de aviso decide su módulo y su permiso en el servicio (descuadres de inventario: WMS_LOTSERIAL + inventory.view). Lo que
/// el usuario no puede ver no suma. Sin pendientes: total 0 y listas vacías ("Todo en orden" en la web).
/// </summary>
[ApiController]
[Route("api/v1/analytics/attention")]
[Authorize]
[RequirePermission(PermissionCatalog.PulseAttention)]
public sealed class AttentionController(AttentionFeedService feed) : ControllerBase
{
    /// <summary>
    /// Los 5 pendientes más antiguos (items, con ruta y parámetros de "Revisar"), el total para "Ver todos (N)" y el total y la
    /// ruta por tipo (groups).
    /// </summary>
    [HttpGet]
    public Task<AttentionDto> Get(CancellationToken ct) => feed.GetAsync(ct);
}
