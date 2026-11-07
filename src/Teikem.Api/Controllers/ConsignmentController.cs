using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Teikem.Api.Auth;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;

namespace Teikem.Api.Controllers;

/// <summary>
/// Lote 224 (P6 del pedido de vendedores) — Contabilidad → Facturación → pestaña Consignación (módulo BILLING). Ver con billing.view o billing.consignment.pay; registrar pagos con
/// billing.consignment.pay; anular un pago con billing.consignment.pay y reautenticación reciente (AAL2). Lo consignado cuenta como vendido solo por estos pagos, en su fecha.
/// </summary>
[ApiController]
[Route("api/v1/billing/consignment")]
[Authorize]
[RequireModule(ModuleKeys.Billing)]
public sealed class ConsignmentController(ConsignmentService consignment) : ControllerBase
{
    private const string Read = PermissionCatalog.BillingView + "|" + PermissionCatalog.BillingConsignmentPay;

    /// <summary>
    /// Facturas en consignación (dejado, devuelto, pagado, saldo), las más recientes primero. balance = OPEN (con saldo, por omisión), PAID (pagadas por completo) o ALL;
    /// q busca en el número de factura, de orden y en el cliente.
    /// </summary>
    [HttpGet("invoices"), RequirePermission(Read)]
    public Task<PagedResult<ConsignmentInvoiceListItemDto>> Invoices([FromQuery] string? balance, [FromQuery] Guid? clientPublicId, [FromQuery] string? q,
        [FromQuery] int skip, [FromQuery] int take, CancellationToken ct)
        => consignment.ListAsync(balance, clientPublicId, q, skip, take, ct);

    /// <summary>La factura con sus líneas en consignación y sus pagos de consignación (también los anulados). 404 si no tiene nada en consignación.</summary>
    [HttpGet("invoices/{publicId:guid}"), RequirePermission(Read)]
    public Task<ConsignmentInvoiceDto> Invoice(Guid publicId, CancellationToken ct) => consignment.GetInvoiceAsync(publicId, ct);

    /// <summary>Reporte «Consignado por cliente»: qué se le dejó, qué devolvió, qué pagó y qué queda.</summary>
    [HttpGet("by-client"), RequirePermission(Read)]
    public Task<ConsignmentByClientDto> ByClient(CancellationToken ct) => consignment.ByClientAsync(ct);

    /// <summary>
    /// Registra un pago de consignación (parcial o total; no pasa del saldo en consignación). Si la factura tiene saldo por cobrar, entra también como pago de la factura (y la salda
    /// al cubrirla). 422 si la factura no tiene nada en consignación, ya está pagada su consignación o no está emitida, vencida o pagada.
    /// </summary>
    [HttpPost("invoices/{publicId:guid}/payments"), RequirePermission(PermissionCatalog.BillingConsignmentPay)]
    public Task<ConsignmentInvoiceDto> RecordPayment(Guid publicId, [FromBody] ConsignmentPaymentRequest req, CancellationToken ct) => consignment.RecordAsync(publicId, req, ct);

    /// <summary>Anula un pago de consignación con motivo (deja de contar como vendido) y su pago de la factura. Exige AAL2. No aplica si la factura ya quedó pagada por completo.</summary>
    [HttpPost("payments/{publicId:guid}/void"), RequirePermission(PermissionCatalog.BillingConsignmentPay), RequireAal2]
    public Task<ConsignmentInvoiceDto> VoidPayment(Guid publicId, [FromBody] ConsignmentPaymentVoidRequest req, CancellationToken ct)
        => consignment.VoidAsync(publicId, req?.Reason, ct);
}
