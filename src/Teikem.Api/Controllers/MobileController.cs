using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Teikem.Api.Auth;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;

namespace Teikem.Api.Controllers;

/// <summary>
/// Lote 213 — la app móvil para el personal (la misma app del chofer, que crece). Todo con mobile.use y sin módulo: cada pantalla de la app pide sus propios datos a los endpoints de siempre
/// (atención, pulso, órdenes, mensajes…) con los permisos de siempre; aquí solo va lo propio del teléfono.
/// </summary>
[ApiController]
[Route("api/v1/mobile")]
[Authorize]
[RequirePermission(PermissionCatalog.MobileUse)]
public sealed class MobileController(DriverPushService push, MobileOrderService orders, DriverAppService app, StreetAssignService street) : ControllerBase
{
    // ---------------------------------------------------------------- Lote 214: órdenes

    /// <summary>Buscar órdenes activas por número, conduce, empaque, factura o número de paquete (`q`): primero la coincidencia exacta del escáner (`matchedBy`), si no, búsqueda libre (hasta 30). 400 sin texto.</summary>
    [HttpGet("orders")]
    [RequirePermission(PermissionCatalog.OrdersView)]
    public Task<MobileOrderSearchDto> SearchOrders([FromQuery] string? q, CancellationToken ct) => orders.SearchAsync(q, ct);

    /// <summary>La ficha de la orden para la calle: contacto, piezas, cobro pendiente, ruta y chofer, comprobante e intentos, y qué acción cabe. 404 si no existe.</summary>
    [HttpGet("orders/{publicId:guid}")]
    [RequirePermission(PermissionCatalog.OrdersView)]
    public Task<MobileOrderDto> Order(Guid publicId, CancellationToken ct) => orders.GetAsync(publicId, ct);

    /// <summary>Los motivos de «No entregado» (lista «Razón de fallo»).</summary>
    [HttpGet("failure-reasons")]
    [RequirePermission(PermissionCatalog.OrdersDeliver)]
    public Task<IReadOnlyList<DriverFailureReasonDto>> FailureReasons(CancellationToken ct) => app.ListFailureReasonsAsync(ct);

    /// <summary>Entrega cualquier orden con la misma evidencia del chofer (quién recibió, firma, fotos, GPS, cobros); queda a nombre de quien lo hace. Idempotente. 400 datos; 404 orden; 409 ya entregada por otra vía; 422 estatus o tipo.</summary>
    [HttpPost("orders/{publicId:guid}/deliver")]
    [RequirePermission(PermissionCatalog.OrdersDeliver)]
    public Task<DriverShipmentResultDto> Deliver(Guid publicId, [FromBody] DriverDeliverRequest? req, CancellationToken ct) => app.DeliverAnyAsync(publicId, req, ct);

    /// <summary>No entrega (o no recoge) cualquier orden con un motivo de «Razón de fallo». Idempotente. 400 motivo/nota/foto; 404 orden; 422 estatus.</summary>
    [HttpPost("orders/{publicId:guid}/fail")]
    [RequirePermission(PermissionCatalog.OrdersDeliver)]
    public Task<DriverShipmentResultDto> Fail(Guid publicId, [FromBody] DriverFailRequest? req, CancellationToken ct) => app.FailAnyAsync(publicId, req, ct);

    /// <summary>Recoge cualquier orden pendiente de recoger (pasa a Recogida con quién entregó, fotos y GPS). Idempotente. 400 datos; 404 orden; 422 ya no está pendiente o la etapa no existe.</summary>
    [HttpPost("orders/{publicId:guid}/pickup")]
    [RequirePermission(PermissionCatalog.OrdersDeliver)]
    public Task<DriverShipmentResultDto> Pickup(Guid publicId, [FromBody] DriverPickupRequest? req, CancellationToken ct) => app.PickupAnyAsync(publicId, req, ct);

    // ---------------------------------------------------------------- Lote 215: asignar en la calle (trips.streetassign + módulo de transporte)

    /// <summary>Dónde se puede mandar la orden: las rutas abiertas de hoy del mismo tipo (más cercanas al domicilio primero, con la última ubicación del chofer si la mandó), y los choferes y vehículos para una ruta rápida. 404 si la orden no existe.</summary>
    [HttpGet("street/orders/{publicId:guid}/targets")]
    [RequirePermission(PermissionCatalog.TripsStreetAssign)]
    [RequireModule(ModuleKeys.LtlGround)]
    public Task<StreetTargetsDto> StreetTargets(Guid publicId, CancellationToken ct) => street.TargetsAsync(publicId, ct);

    /// <summary>Manda la orden a una ruta abierta (planeada, despachada o en curso); si estaba en otra, la pasa. Al chofer se le deja un aviso. 404 orden/ruta; 409 ya está en esa ruta; 422 tipo distinto, estatus que no se mueve, ruta cerrada o no elegible.</summary>
    [HttpPost("street/assign")]
    [RequirePermission(PermissionCatalog.TripsStreetAssign)]
    [RequireModule(ModuleKeys.LtlGround)]
    public Task<StreetResultDto> StreetAssign([FromBody] StreetAssignRequest? req, CancellationToken ct) => street.AssignAsync(req, ct);

    /// <summary>Saca la orden de su ruta (vuelve a «Sin asignar» si era de entrega) y avisa al chofer. 422 si no está en ninguna ruta o su estatus ya no se mueve.</summary>
    [HttpPost("street/unassign")]
    [RequirePermission(PermissionCatalog.TripsStreetAssign)]
    [RequireModule(ModuleKeys.LtlGround)]
    public Task<StreetResultDto> StreetUnassign([FromBody] StreetUnassignRequest? req, CancellationToken ct) => street.UnassignAsync(req, ct);

    /// <summary>Ruta rápida: crea y despacha una ruta de hoy para el chofer y vehículo elegidos con las órdenes dadas (todas del mismo tipo). Si falla, la ruta se cancela.</summary>
    [HttpPost("street/quick-route")]
    [RequirePermission(PermissionCatalog.TripsStreetAssign)]
    [RequireModule(ModuleKeys.LtlGround)]
    public Task<StreetResultDto> StreetQuickRoute([FromBody] StreetQuickRouteRequest? req, CancellationToken ct) => street.QuickRouteAsync(req, ct);

    // ---------------------------------------------------------------- Lote 213: teléfono

    /// <summary>Registra el teléfono para los avisos emergentes (`{platform: ANDROID|IOS, pushToken, appVersion}`). Idempotente. Un chofer queda en su ficha; el personal, en su usuario.</summary>
    [HttpPost("device")]
    public async Task<IActionResult> RegisterDevice([FromBody] DriverDeviceRegisterRequest? req, CancellationToken ct)
    {
        await push.RegisterDeviceAsync(req, ct);
        return NoContent();
    }
}
