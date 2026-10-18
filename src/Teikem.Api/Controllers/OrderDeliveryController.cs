using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Teikem.Api.Auth;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;

namespace Teikem.Api.Controllers;

/// <summary>
/// Lote 118 — registrar la entrega de una orden desde la web (permiso orders.deliver, módulo LTL_GROUND): la orden pasa a «Entregada» y queda el comprobante, con la firma automática del usuario que lo registra.
/// Consultar el comprobante exige poder ver órdenes. La compañía sale del principal.
/// </summary>
[ApiController]
[Route("api/v1/orders/{publicId:guid}/delivery")]
[Authorize]
[RequireModule(ModuleKeys.LtlGround)]
public sealed class OrderDeliveryController(DeliveryRegistrationService delivery) : ControllerBase
{
    [HttpPost, RequirePermission(PermissionCatalog.OrdersDeliver)]
    public Task<DeliveryProofDto> Register(Guid publicId, [FromBody] DeliveryRegisterRequest? req, CancellationToken ct) => delivery.RegisterAsync(publicId, req, ct);

    /// <summary>El comprobante de la orden, o 204 si todavía no tiene (la entrega se registró por otra vía o no se ha entregado).</summary>
    [HttpGet, RequirePermission(PermissionCatalog.OrdersView)]
    public async Task<ActionResult<DeliveryProofDto>> Get(Guid publicId, CancellationToken ct)
        => await delivery.FindByOrderAsync(publicId, ct) is { } proof ? proof : NoContent();
}
