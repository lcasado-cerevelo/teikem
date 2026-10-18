using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Teikem.Api.Auth;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;

namespace Teikem.Api.Controllers;

/// <summary>
/// Lote 112 — devolver una orden (permiso orders.return, módulo LTL_GROUND): la orden pasa a «Devuelta» y se crea la orden de vuelta con las piezas que se devuelven. La compañía sale del principal.
/// </summary>
[ApiController]
[Route("api/v1/orders/{publicId:guid}/return")]
[Authorize]
[RequireModule(ModuleKeys.LtlGround)]
public sealed class OrderReturnController(ReturnOrderService returns) : ControllerBase
{
    [HttpPost, RequirePermission(PermissionCatalog.OrdersReturn)]
    public Task<ReturnOrderResultDto> Return(Guid publicId, [FromBody] ReturnOrderRequest? req, CancellationToken ct) => returns.ReturnAsync(publicId, req, ct);
}
