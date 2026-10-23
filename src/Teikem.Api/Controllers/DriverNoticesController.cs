using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Teikem.Api.Auth;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;

namespace Teikem.Api.Controllers;

/// <summary>La oficina escribe un aviso a un chofer (aparece en la bandeja de avisos de su app). Permiso trips.dispatch; módulo LTL_GROUND.</summary>
[ApiController]
[Route("api/v1/driver-notices")]
[Authorize]
[RequireModule(ModuleKeys.LtlGround)]
public sealed class DriverNoticesController(DriverNoticeService notices) : ControllerBase
{
    /// <summary>Cuerpo `{driverPublicId, title, body}`. 400 título faltante o de más de 120 caracteres, texto de más de 1000; 404 chofer.</summary>
    [HttpPost, RequirePermission(PermissionCatalog.TripsDispatch)]
    public Task<DriverNoticeDto> Send([FromBody] DriverNoticeSendRequest? req, CancellationToken ct) => notices.SendAsync(req, ct);
}
