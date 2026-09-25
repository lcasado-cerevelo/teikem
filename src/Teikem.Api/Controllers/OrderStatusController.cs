using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Teikem.Api.Auth;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;

namespace Teikem.Api.Controllers;

/// <summary>Cuerpo de POST /orders/{id}/confirm: RowVersion opcional (409 si cambió) y overrideCredit (ajuste C: exige orders.credit_override).</summary>
public sealed record OrderConfirmBody(string? RowVersion = null, bool OverrideCredit = false);

/// <summary>
/// Lote 3 (P4): vista previa de cotización y crédito, confirmar, reprecio, cancelar y estatus laterales de una orden.
/// Módulo LTL_GROUND (núcleo). El controlador interno siempre pasa OrderScope.Any; el portal (Lote 8) pasará el ClientId
/// del principal. Los avances de pipeline (PICKUP…DELIVERED) no se exponen aquí (DECISIÓN 11).
/// </summary>
[ApiController]
[Route("api/v1/orders/{publicId:guid}")]
[Authorize]
[RequireModule(ModuleKeys.LtlGround)]
public sealed class OrderStatusController(OrderStatusService service) : ControllerBase
{
    /// <summary>Cotización y chequeo de crédito sin persistir (líneas, despacho, COD, total, contrato, límite/pendiente/disponible/excede).</summary>
    [HttpGet("quote"), RequirePermission(PermissionCatalog.OrdersView)]
    public Task<OrderQuotePreviewDto> PreviewQuote(Guid publicId, CancellationToken ct)
        => service.PreviewQuoteAsync(publicId, OrderScope.Any, ct);

    /// <summary>Confirmar: cotiza, congela y verifica crédito al salir de la etapa inicial. 422 credit_exceeded; overrideCredit=true exige orders.credit_override.</summary>
    [HttpPost("confirm"), RequirePermission(PermissionCatalog.OrdersEdit)]
    public Task<OrderDetailDto> Confirm(Guid publicId, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] OrderConfirmBody? body, CancellationToken ct)
        => service.ConfirmAsync(publicId, new OrderConfirmRequest(body?.RowVersion), OrderScope.Any, body?.OverrideCredit ?? false, ct);

    /// <summary>Re-cotiza una orden ya cotizada (capacidad REPRICE: por defecto solo en CONFIRMED).</summary>
    [HttpPost("reprice"), RequirePermission(PermissionCatalog.OrdersEdit)]
    public Task<OrderDetailDto> Reprice(Guid publicId, CancellationToken ct)
        => service.RepriceAsync(publicId, OrderScope.Any, ct);

    /// <summary>Cancelar con bitácora (capacidad CANCEL; respeta las entradas laterales del tenant).</summary>
    [HttpPost("cancel"), RequirePermission(PermissionCatalog.OrdersCancel)]
    public Task<OrderDetailDto> Cancel(Guid publicId, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] OrderCancelRequest? req, CancellationToken ct)
        => service.CancelAsync(publicId, req, OrderScope.Any, ct);

    /// <summary>Estatus laterales (ON_HOLD, PARTIAL, FAILED) y regreso al pipeline. CANCELLED y la confirmación tienen acción propia.</summary>
    [HttpPost("status"), RequirePermission(PermissionCatalog.OrdersEdit)]
    public Task<OrderDetailDto> TransitionStatus(Guid publicId, [FromBody] StatusChangeRequest req, CancellationToken ct)
        => service.TransitionAsync(publicId, req, OrderScope.Any, ct);
}
