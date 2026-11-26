using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Teikem.Api.Auth;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;

namespace Teikem.Api.Controllers;

/// <summary>
/// Lote 248 — exenciones de IVU desde la ficha del cliente (módulo TAX, permiso tax.manage). La captura única crea el documento que la respalda (archivo nuevo) y la
/// exención en una sola transacción, o aplica la exención con un documento ya subido. Respeta Idempotency-Key (el middleware cubre multipart).
/// </summary>
[ApiController]
[Route("api/v1/clients/{publicId:guid}/tax-exemptions")]
[Authorize]
[RequireModule(ModuleKeys.Tax)]
public sealed class ClientTaxExemptionsController(ClientTaxExemptionCaptureService capture) : ControllerBase
{
    /// <summary>Las exenciones del cliente (la más reciente primero), con el documento que las respalda o la marca «falta el documento».</summary>
    [HttpGet, RequirePermission(PermissionCatalog.TaxManage)]
    public Task<IReadOnlyList<TaxExemptionDto>> List(Guid publicId, CancellationToken ct) => capture.ListForClientAsync(publicId, ct);

    /// <summary>
    /// Captura única: archivo nuevo (file + documentType, documentNumber, validFrom y validTo) O documento ya subido (clientDocumentPublicId), nunca los dos.
    /// scope ALL o COMPONENTS con componentCodes; exemptionType vacío = la etiqueta del tipo de documento.
    /// </summary>
    [HttpPost, Consumes("multipart/form-data"), RequirePermission(PermissionCatalog.TaxManage)]
    [RequestSizeLimit(32 * 1024 * 1024)]
    public async Task<ClientTaxExemptionCaptureDto> Capture(Guid publicId, IFormFile? file, [FromForm] Guid? clientDocumentPublicId, [FromForm] string? documentType,
        [FromForm] string? documentNumber, [FromForm] DateOnly? validFrom, [FromForm] DateOnly? validTo, [FromForm] string? scope, [FromForm] List<string>? componentCodes,
        [FromForm] string? notes, [FromForm] string? exemptionType, CancellationToken ct)
    {
        byte[]? bytes = null;
        if (file is not null)
        {
            await using var ms = new MemoryStream();
            await file.CopyToAsync(ms, ct);
            bytes = ms.ToArray();
        }
        var req = new ClientTaxExemptionCaptureRequest(clientDocumentPublicId, documentType, documentNumber, validFrom, validTo, scope,
            componentCodes is { Count: > 0 } ? componentCodes : null, notes, exemptionType);
        return await capture.CaptureAsync(publicId, req, file?.FileName, file?.ContentType, bytes, ct);
    }
}
