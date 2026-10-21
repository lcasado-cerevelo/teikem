using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Teikem.Api.Auth;
using Teikem.Domain.Common;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;

namespace Teikem.Api.Controllers;

/// <summary>
/// Lote 111 — faltantes y reclamaciones: Por verificar → Perdido → Reclamada → Resuelta. Módulo LTL_GROUND; lecturas con claims.view, mutaciones con claims.manage. La compañía sale del principal.
/// </summary>
[ApiController]
[Route("api/v1/claims")]
[Authorize]
[RequireModule(ModuleKeys.LtlGround)]
public sealed class ClaimsController(ClaimService claims) : ControllerBase
{
    /// <summary>Órdenes con piezas «Por verificar» o «Perdido» sin reclamación (búsqueda por número de orden, conduce o pieza).</summary>
    [HttpGet("to-verify"), RequirePermission(PermissionCatalog.ClaimsView)]
    public Task<PagedResult<MissingOrderDto>> ToVerify([FromQuery] string? search, [FromQuery] int skip, [FromQuery] int take, CancellationToken ct) => claims.ToVerifyAsync(search, skip, take, ct);

    /// <summary>«Por verificar» → «Perdido» (sin números: todas las «Por verificar» de la orden).</summary>
    [HttpPost("lost"), RequirePermission(PermissionCatalog.ClaimsManage)]
    public Task<MissingOrderDto> MarkLost([FromBody] MarkLostRequest req, CancellationToken ct) => claims.MarkLostAsync(req, ct);

    /// <summary>Crea la reclamación RCL-##### con las piezas perdidas de la orden («Perdido» → «Reclamada»).</summary>
    [HttpPost, RequirePermission(PermissionCatalog.ClaimsManage)]
    public Task<ClaimDto> Create([FromBody] ClaimCreateRequest req, CancellationToken ct) => claims.CreateAsync(req, ct);

    [HttpGet, RequirePermission(PermissionCatalog.ClaimsView)]
    public Task<PagedResult<ClaimListItemDto>> List([FromQuery] string? status, [FromQuery] string? search, [FromQuery] int skip, [FromQuery] int take, CancellationToken ct) => claims.ListAsync(status, search, skip, take, ct);

    [HttpGet("{publicId:guid}"), RequirePermission(PermissionCatalog.ClaimsView)]
    public Task<ClaimDto> Get(Guid publicId, CancellationToken ct) => claims.GetAsync(publicId, ct);

    /// <summary>Resuelve la reclamación: PAID (con monto), REJECTED o CLOSED.</summary>
    [HttpPost("{publicId:guid}/resolve"), RequirePermission(PermissionCatalog.ClaimsManage)]
    public Task<ClaimDto> Resolve(Guid publicId, [FromBody] ClaimResolveRequest req, CancellationToken ct) => claims.ResolveAsync(publicId, req, ct);
}
