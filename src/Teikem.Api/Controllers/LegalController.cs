using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Teikem.Api.Auth;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Legal;

namespace Teikem.Api.Controllers;

/// <summary>
/// Lote 99: documento legal único («Política de privacidad y términos de uso»). El texto es público (lo lee quien aún no entra); el estado y la aceptación son del usuario con sesión.
/// Mientras falte aceptar la versión vigente, el resto del API responde 403 'legal_acceptance_required' (LegalAcceptanceMiddleware).
/// </summary>
[ApiController]
[Route("api/v1/legal")]
public sealed class LegalController(LegalService legal, ITenantContext tenant) : ControllerBase
{
    /// <summary>Texto vigente en el idioma pedido (?lang=es|en; por omisión el del encabezado). Sin sesión.</summary>
    [HttpGet("current"), AllowAnonymous]
    public LegalDocumentDto Current([FromQuery] string? lang) => legal.GetCurrent(lang ?? tenant.Lang);

    /// <summary>Si el usuario ya aceptó la versión vigente y si se le exige aceptar.</summary>
    [HttpGet("status"), Authorize]
    public Task<LegalStatusDto> Status(CancellationToken ct) => legal.StatusAsync(ct);

    /// <summary>Quién aceptó y quién no la versión vigente entre los usuarios de la compañía (pendientes primero). Filtros: status=accepted|pending, q. Pide admin.audit.</summary>
    [HttpGet("acceptances"), Authorize, RequirePermission(PermissionCatalog.AdminAudit)]
    public Task<PagedResult<LegalAcceptanceRowDto>> Acceptances([FromQuery] string? status, [FromQuery] string? q, [FromQuery] int skip, [FromQuery] int take, CancellationToken ct) => legal.ListAsync(status, q, skip, take, ct);

    /// <summary>Acepta la versión vigente (409 si la versión enviada ya no es la vigente).</summary>
    [HttpPost("accept"), Authorize]
    public Task<LegalStatusDto> Accept([FromBody] LegalAcceptRequest req, CancellationToken ct) => legal.AcceptAsync(req, ct);
}
