using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Teikem.Api.Auth;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;

namespace Teikem.Api.Controllers;

/// <summary>
/// Lote 4 (P7): asignar o reasignar el chofer de una entrega especial ya creada. Vive con las órdenes (LTL_GROUND,
/// trips.dispatch); el servicio exige además el módulo CATALOG encendido porque usa choferes y tarifas (403 module_disabled).
/// Confirma si la orden sigue en la etapa inicial, avanza hasta IN_TRANSIT y crea el viaje pagado al chofer con el monto
/// congelado. overrideCredit exige orders.credit_override.
/// </summary>
[ApiController]
[Route("api/v1/orders/{publicId:guid}/driver")]
[Authorize]
[RequireModule(ModuleKeys.LtlGround)]
public sealed class SpecialDeliveryController(SpecialDeliveryDispatchService dispatch) : ControllerBase
{
    [HttpPost, RequirePermission(PermissionCatalog.TripsDispatch)]
    public Task<OrderDetailDto> Assign(Guid publicId, [FromBody] SpecialDeliveryAssignRequest req, CancellationToken ct)
        => dispatch.AssignAsync(publicId, req, OrderScope.Any, ct);
}
