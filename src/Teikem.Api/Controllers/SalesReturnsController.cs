using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Teikem.Api.Auth;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;

namespace Teikem.Api.Controllers;

/// <summary>Cuerpo opcional de cancelar una devolución: el comentario queda en el historial de estatus.</summary>
public sealed record SalesReturnCancelRequest(string? Comment = null);

/// <summary>
/// Lote 81 (P6) — devoluciones de venta (módulo SALES_ORDERS). Ver con sales.view; pedir y cancelar con sales.return; autorizar, rechazar y cambiar la ventana con sales.return.authorize; recibir con
/// warehouse.receive (reutiliza el recibo tipo RETURN y el flujo de daños); acreditar (notas de crédito en borrador) con billing.credit. Flujo: solicitada → autorizada → recibida → acreditada.
/// </summary>
[ApiController]
[Route("api/v1/sales-returns")]
[Authorize]
[RequireModule(ModuleKeys.SalesOrders)]
public sealed class SalesReturnsController(SalesReturnService returns) : ControllerBase
{
    /// <summary>Ventana de devolución de la compañía: días después del último despacho (0 = sin ventana).</summary>
    [HttpGet("settings"), RequirePermission(PermissionCatalog.SalesView)]
    public Task<SalesReturnSettingsDto> Settings(CancellationToken ct) => returns.GetSettingsAsync(ct);

    [HttpPut("settings"), RequirePermission(PermissionCatalog.SalesReturnAuthorize)]
    public Task<SalesReturnSettingsDto> UpdateSettings([FromBody] SalesReturnSettingsRequest req, CancellationToken ct) => returns.UpdateSettingsAsync(req, ct);

    [HttpGet, RequirePermission(PermissionCatalog.SalesView)]
    public Task<PagedResult<SalesReturnListItemDto>> List([FromQuery] string? status, [FromQuery] Guid? clientPublicId, [FromQuery] Guid? orderPublicId, [FromQuery] string? q, [FromQuery] int skip,
        [FromQuery] int take, CancellationToken ct)
        => returns.ListAsync(status, clientPublicId, orderPublicId, q, skip, take, ct);

    [HttpGet("{publicId:guid}"), RequirePermission(PermissionCatalog.SalesView)]
    public Task<SalesReturnDto> Get(Guid publicId, CancellationToken ct) => returns.GetAsync(publicId, ct);

    /// <summary>Lo que se puede devolver de una orden de venta (por línea: despachado, devuelto, pendiente y por devolver) y si la ventana de devolución ya pasó.</summary>
    [HttpGet("returnable"), RequirePermission(PermissionCatalog.SalesView)]
    public Task<ReturnableDto> Returnable([FromQuery] Guid salesOrderPublicId, CancellationToken ct) => returns.ReturnableAsync(salesOrderPublicId, ct);

    [HttpPost, RequirePermission(PermissionCatalog.SalesReturn)]
    public Task<SalesReturnDto> Create([FromBody] SalesReturnRequest req, CancellationToken ct) => returns.CreateAsync(req, ct);

    [HttpPost("{publicId:guid}/authorize"), RequirePermission(PermissionCatalog.SalesReturnAuthorize)]
    public Task<SalesReturnDto> Authorize(Guid publicId, [FromBody] SalesReturnAuthorizeRequest? req, CancellationToken ct) => returns.AuthorizeAsync(publicId, req?.Note, ct);

    [HttpPost("{publicId:guid}/reject"), RequirePermission(PermissionCatalog.SalesReturnAuthorize)]
    public Task<SalesReturnDto> Reject(Guid publicId, [FromBody] SalesReturnRejectRequest req, CancellationToken ct) => returns.RejectAsync(publicId, req?.Reason, ct);

    [HttpPost("{publicId:guid}/cancel"), RequirePermission(PermissionCatalog.SalesReturn)]
    public Task<SalesReturnDto> Cancel(Guid publicId, [FromBody] SalesReturnCancelRequest? req, CancellationToken ct) => returns.CancelAsync(publicId, req?.Comment, ct);

    /// <summary>Recibe lo que llegó (en partes): crea y confirma un recibo tipo RETURN con lo bueno y lo dañado por línea; lo recibido cuenta como devuelto en la orden de venta.</summary>
    [HttpPost("{publicId:guid}/receive"), RequirePermission(PermissionCatalog.WarehouseReceive)]
    public Task<SalesReturnDto> Receive(Guid publicId, [FromBody] SalesReturnReceiveRequest req, CancellationToken ct) => returns.ReceiveAsync(publicId, req, ct);

    /// <summary>Crea las notas de crédito en borrador de lo recibido que sí se facturó y pasa la devolución a acreditada.</summary>
    [HttpPost("{publicId:guid}/credit"), RequirePermission(PermissionCatalog.BillingCredit)]
    public Task<SalesReturnDto> Credit(Guid publicId, CancellationToken ct) => returns.CreditAsync(publicId, ct);
}
