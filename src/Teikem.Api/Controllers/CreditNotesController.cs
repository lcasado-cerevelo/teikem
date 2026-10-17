using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Teikem.Api.Auth;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;

namespace Teikem.Api.Controllers;

/// <summary>
/// Lote 79 (P6) — notas de crédito (módulo BILLING). Ver con billing.view; crear, cambiar, aplicar y anular con billing.credit; aprobar exige además reautenticación reciente (AAL2).
/// Una nota: borrador → aprobada → aplicada (o anulada). Aprobarla no mueve inventario; lo que no se aplica a facturas queda como saldo a favor del cliente.
/// </summary>
[ApiController]
[Route("api/v1/billing/credit-notes")]
[Authorize]
[RequireModule(ModuleKeys.Billing)]
public sealed class CreditNotesController(CreditNoteService credits) : ControllerBase
{
    [HttpGet, RequirePermission(PermissionCatalog.BillingView)]
    public Task<PagedResult<CreditNoteListItemDto>> List([FromQuery] string? status, [FromQuery] Guid? clientPublicId, [FromQuery] string? q, [FromQuery] int skip, [FromQuery] int take, CancellationToken ct)
        => credits.ListAsync(status, clientPublicId, q, skip, take, ct);

    [HttpGet("{publicId:guid}"), RequirePermission(PermissionCatalog.BillingView)]
    public Task<CreditNoteDto> Get(Guid publicId, CancellationToken ct) => credits.GetAsync(publicId, ct);

    /// <summary>Crea la nota en borrador: sobre una factura (cada línea acredita una de la factura, sin pasar de lo facturado) o libre (con cliente, para una factura externa o un ajuste).</summary>
    [HttpPost, RequirePermission(PermissionCatalog.BillingCredit)]
    public Task<CreditNoteDto> Create([FromBody] CreditNoteRequest req, CancellationToken ct) => credits.CreateAsync(req, ct);

    /// <summary>Cambia el motivo, la nota y las líneas (reemplaza todas) de una nota en borrador.</summary>
    [HttpPut("{publicId:guid}"), RequirePermission(PermissionCatalog.BillingCredit)]
    public Task<CreditNoteDto> Update(Guid publicId, [FromBody] CreditNoteRequest req, CancellationToken ct) => credits.UpdateAsync(publicId, req, ct);

    /// <summary>Aprueba la nota (vuelve a comprobar el tope de lo acreditado). Exige billing.credit y reautenticación reciente (AAL2).</summary>
    [HttpPost("{publicId:guid}/approve"), RequirePermission(PermissionCatalog.BillingCredit), RequireAal2]
    public Task<CreditNoteDto> Approve(Guid publicId, CancellationToken ct) => credits.ApproveAsync(publicId, ct);

    /// <summary>Aplica parte o todo el saldo de una nota aprobada a una factura abierta del mismo cliente; si la factura queda en cero pasa a Pagada.</summary>
    [HttpPost("{publicId:guid}/apply"), RequirePermission(PermissionCatalog.BillingCredit)]
    public Task<CreditNoteDto> Apply(Guid publicId, [FromBody] CreditApplyRequest req, CancellationToken ct) => credits.ApplyAsync(publicId, req, ct);

    /// <summary>Anula una nota en borrador o aprobada sin aplicaciones (con motivo).</summary>
    [HttpPost("{publicId:guid}/void"), RequirePermission(PermissionCatalog.BillingCredit)]
    public Task<CreditNoteDto> Void(Guid publicId, [FromBody] CreditVoidRequest req, CancellationToken ct) => credits.VoidAsync(publicId, req?.Reason, ct);
}
