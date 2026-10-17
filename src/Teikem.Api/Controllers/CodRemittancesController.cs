using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Teikem.Api.Auth;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;

namespace Teikem.Api.Controllers;

/// <summary>
/// Lote 86 (P8) — remesa COD al cliente (módulo COD): bruto cobrado, comisión y neto a remitir. Ver con cod.view; todo lo demás con cod.remit. Flujo: abierta → cuadrada → aprobada (reautenticación AAL2) →
/// remitida; una abierta o cuadrada se cancela. Teikem calcula y exporta; no emite el pago.
/// </summary>
[ApiController]
[Route("api/v1/cod/remittances")]
[Authorize]
[RequireModule(ModuleKeys.Cod)]
public sealed class CodRemittancesController(CodRemittanceService remittances) : ControllerBase
{
    [HttpGet, RequirePermission(PermissionCatalog.CodView)]
    public Task<PagedResult<CodRemittanceListItemDto>> List([FromQuery] string? status, [FromQuery] Guid? clientPublicId, [FromQuery] string? q, [FromQuery] int skip, [FromQuery] int take, CancellationToken ct)
        => remittances.ListAsync(status, clientPublicId, q, skip, take, ct);

    [HttpGet("{publicId:guid}"), RequirePermission(PermissionCatalog.CodView)]
    public Task<CodRemittanceDto> Get(Guid publicId, CancellationToken ct) => remittances.GetAsync(publicId, ct);

    /// <summary>Lo que entraría en la remesa (cobros cuadrados del cliente en el período) y la comisión que se aplicaría; no crea nada.</summary>
    [HttpGet("candidates"), RequirePermission(PermissionCatalog.CodRemit)]
    public Task<CodRemittanceCandidatesDto> Candidates([FromQuery] Guid? clientPublicId, [FromQuery] DateOnly? periodStart, [FromQuery] DateOnly? periodEnd, [FromQuery] decimal? commissionPct, CancellationToken ct)
        => remittances.CandidatesAsync(clientPublicId, periodStart, periodEnd, commissionPct, ct);

    [HttpPost, RequirePermission(PermissionCatalog.CodRemit)]
    public Task<CodRemittanceDto> Create([FromBody] CodRemittanceRequest req, CancellationToken ct) => remittances.CreateAsync(req, ct);

    [HttpPost("{publicId:guid}/review"), RequirePermission(PermissionCatalog.CodRemit)]
    public Task<CodRemittanceDto> Review(Guid publicId, CancellationToken ct) => remittances.ReviewAsync(publicId, ct);

    /// <summary>Aprobar exige reautenticación (AAL2), como aprobar una corrida de facturación.</summary>
    [HttpPost("{publicId:guid}/approve"), RequirePermission(PermissionCatalog.CodRemit), RequireAal2]
    public Task<CodRemittanceDto> Approve(Guid publicId, CancellationToken ct) => remittances.ApproveAsync(publicId, ct);

    [HttpPost("{publicId:guid}/remit"), RequirePermission(PermissionCatalog.CodRemit)]
    public Task<CodRemittanceDto> Remit(Guid publicId, [FromBody] CodRemitRequest? req, CancellationToken ct) => remittances.RemitAsync(publicId, req, ct);

    [HttpPost("{publicId:guid}/cancel"), RequirePermission(PermissionCatalog.CodRemit)]
    public Task<CodRemittanceDto> Cancel(Guid publicId, CancellationToken ct) => remittances.CancelAsync(publicId, ct);

    /// <summary>El archivo de la remesa (una fila por cobro) con una plantilla del alcance COD_REMITTANCE; sin plantilla, la primera activa.</summary>
    [HttpGet("{publicId:guid}/export"), RequirePermission(PermissionCatalog.CodRemit)]
    public Task<ExportFileDto> Export(Guid publicId, [FromQuery] Guid? templatePublicId, CancellationToken ct) => remittances.ExportAsync(publicId, templatePublicId, ct);
}
