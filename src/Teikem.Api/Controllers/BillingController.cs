using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Teikem.Api.Auth;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;

namespace Teikem.Api.Controllers;

/// <summary>Cuerpo opcional de cancelar una corrida: el motivo queda en el historial de estatus.</summary>
public sealed record BillingRunCancelRequest(string? Comment = null);

/// <summary>
/// Lote 72 (P5) — facturación (módulo BILLING). Lectura con billing.view; generar, revisar, cancelar y ajustar con billing.generate; aprobar la corrida y anular una factura con billing.approve
/// y reautenticación reciente (AAL2). Una corrida: generar → revisar → aprobar (las facturas se emiten) → exportar. Las facturas no se borran: se anulan.
/// </summary>
[ApiController]
[Route("api/v1/billing")]
[Authorize]
[RequireModule(ModuleKeys.Billing)]
public sealed class BillingController(BillingService billing) : ControllerBase
{
    /// <summary>Base de facturación del producto vendido (al despachar o al entregar) y días de vencimiento de las facturas.</summary>
    [HttpGet("settings"), RequirePermission(PermissionCatalog.BillingView)]
    public Task<BillingSettingsDto> Settings(CancellationToken ct) => billing.GetSettingsAsync(ct);

    [HttpPut("settings"), RequirePermission(PermissionCatalog.BillingApprove)]
    public Task<BillingSettingsDto> UpdateSettings([FromBody] BillingSettingsRequest req, CancellationToken ct) => billing.UpdateSettingsAsync(req, ct);

    /// <summary>Corridas (la más reciente primero); status = GENERATED, REVIEWED, APPROVED, EXPORTED o CANCELLED.</summary>
    [HttpGet("runs"), RequirePermission(PermissionCatalog.BillingView)]
    public Task<IReadOnlyList<BillingRunListItemDto>> Runs([FromQuery] string? status, CancellationToken ct) => billing.ListRunsAsync(status, ct);

    [HttpGet("runs/{id:int}"), RequirePermission(PermissionCatalog.BillingView)]
    public Task<BillingRunDto> Run(int id, CancellationToken ct) => billing.GetRunAsync(id, ct);

    /// <summary>
    /// Genera la corrida: junta lo despachado y no facturado de las órdenes de venta hasta la fecha final (o solo dentro del período con onlyInPeriod) y arma una factura en borrador por cliente.
    /// 422 si no hay nada por facturar.
    /// </summary>
    [HttpPost("runs"), RequirePermission(PermissionCatalog.BillingGenerate)]
    public Task<BillingRunDto> Generate([FromBody] BillingRunRequest req, CancellationToken ct) => billing.GenerateAsync(req, ct);

    [HttpPost("runs/{id:int}/review"), RequirePermission(PermissionCatalog.BillingGenerate)]
    public Task<BillingRunDto> Review(int id, CancellationToken ct) => billing.ReviewAsync(id, ct);

    /// <summary>Aprueba la corrida revisada: sus facturas se emiten. Exige billing.approve y reautenticación reciente (AAL2).</summary>
    [HttpPost("runs/{id:int}/approve"), RequirePermission(PermissionCatalog.BillingApprove), RequireAal2]
    public Task<BillingRunDto> Approve(int id, CancellationToken ct) => billing.ApproveAsync(id, ct);

    /// <summary>Cancela una corrida generada o revisada: sus facturas se anulan y lo reclamado vuelve a quedar por facturar.</summary>
    [HttpPost("runs/{id:int}/cancel"), RequirePermission(PermissionCatalog.BillingGenerate)]
    public Task<BillingRunDto> Cancel(int id, [FromBody] BillingRunCancelRequest? req, CancellationToken ct) => billing.CancelAsync(id, req?.Comment, ct);

    [HttpGet("invoices"), RequirePermission(PermissionCatalog.BillingView)]
    public Task<PagedResult<InvoiceListItemDto>> Invoices([FromQuery] string? status, [FromQuery] Guid? clientPublicId, [FromQuery] int? runId, [FromQuery] string? q,
        [FromQuery] int skip, [FromQuery] int take, CancellationToken ct)
        => billing.ListInvoicesAsync(status, clientPublicId, runId, q, skip, take, ct);

    [HttpGet("invoices/{publicId:guid}"), RequirePermission(PermissionCatalog.BillingView)]
    public Task<InvoiceDto> Invoice(Guid publicId, CancellationToken ct) => billing.GetInvoiceAsync(publicId, ct);

    /// <summary>Ajusta el monto unitario de una línea (con motivo) mientras la corrida está generada o revisada; deja el calculado y recalcula el impuesto de la línea y los totales.</summary>
    [HttpPatch("invoices/{publicId:guid}/lines/{lineId:int}"), RequirePermission(PermissionCatalog.BillingGenerate)]
    public Task<InvoiceDto> AdjustLine(Guid publicId, int lineId, [FromBody] InvoiceLineAdjustRequest req, CancellationToken ct) => billing.AdjustLineAsync(publicId, lineId, req, ct);

    /// <summary>Anula una factura emitida o vencida sin pagos y libera lo que había facturado. Exige billing.approve y AAL2.</summary>
    [HttpPost("invoices/{publicId:guid}/void"), RequirePermission(PermissionCatalog.BillingApprove), RequireAal2]
    public Task<InvoiceDto> Void(Guid publicId, [FromBody] InvoiceVoidRequest? req, CancellationToken ct) => billing.VoidAsync(publicId, req?.Comment, ct);
}
