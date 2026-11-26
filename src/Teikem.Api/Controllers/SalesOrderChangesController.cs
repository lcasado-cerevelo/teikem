using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Teikem.Api.Auth;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;

namespace Teikem.Api.Controllers;

/// <summary>
/// Lote 249 (Lote E) — «Modificar la orden de venta» (módulo SALES_ORDERS): opciones de edición, vista previa, registrar (y aplicar o dejar por aprobar), historial,
/// segunda aprobación (aprobar / rechazar con sales.modify.approve), retirar, subir la evidencia de la autorización y la orden interna de adición. sales.price,
/// sales.tax, warehouse.reopen-pack y orders.credit_override los exige el servicio según el caso. Lo de otra compañía responde 404.
/// </summary>
[ApiController]
[Route("api/v1/sales-orders/{publicId:guid}")]
[Authorize]
[RequireModule(ModuleKeys.SalesOrders)]
public sealed class SalesOrderChangesController(SalesOrderChangeService changes) : ControllerBase
{
    /// <summary>La etapa de la orden, las recolecciones con su etapa, la posición por línea, las facturas, los ajustes, la modificación pendiente y los permisos del usuario.</summary>
    [HttpGet("edit-options"), RequirePermission(PermissionCatalog.SalesView)]
    public Task<SalesOrderEditOptionsDto> EditOptions(Guid publicId, CancellationToken ct) => changes.EditOptionsAsync(publicId, ct);

    /// <summary>Qué pasaría con la modificación (no escribe nada): antes, después, diferencia, efectos, crédito, aprobación, adjunto y bloqueos.</summary>
    [HttpPost("changes/preview"), RequirePermission(PermissionCatalog.SalesModify)]
    public Task<SalesOrderChangePreviewDto> Preview(Guid publicId, [FromBody] SalesOrderChangeRequest? req, CancellationToken ct) => changes.PreviewAsync(publicId, req, ct);

    /// <summary>Registra la modificación: sin segunda aprobación se aplica en la misma llamada; con ella queda pendiente sin tocar nada.</summary>
    [HttpPost("changes"), RequirePermission(PermissionCatalog.SalesModify)]
    public Task<SalesOrderChangeDto> Create(Guid publicId, [FromBody] SalesOrderChangeRequest? req, CancellationToken ct) => changes.CreateAsync(publicId, req, ct);

    /// <summary>El historial de modificaciones de la orden (la más reciente primero).</summary>
    [HttpGet("changes"), RequirePermission(PermissionCatalog.SalesView)]
    public Task<IReadOnlyList<SalesOrderChangeListItemDto>> List(Guid publicId, CancellationToken ct) => changes.ListAsync(publicId, ct);

    /// <summary>Una modificación con su foto de antes y después, la diferencia y los efectos.</summary>
    [HttpGet("changes/{changePublicId:guid}"), RequirePermission(PermissionCatalog.SalesView)]
    public Task<SalesOrderChangeDto> Get(Guid publicId, Guid changePublicId, CancellationToken ct) => changes.GetAsync(publicId, changePublicId, ct);

    /// <summary>Segunda aprobación: la da otra persona que quien registró (403). 409 si la orden cambió desde el registro.</summary>
    [HttpPost("changes/{changePublicId:guid}/approve"), RequirePermission(PermissionCatalog.SalesModifyApprove)]
    public Task<SalesOrderChangeDto> Approve(Guid publicId, Guid changePublicId, [FromBody] SalesOrderChangeDecisionRequest? req, CancellationToken ct)
        => changes.ApproveAsync(publicId, changePublicId, req, ct);

    /// <summary>Rechaza la modificación pendiente (nota obligatoria). No se aplica nada.</summary>
    [HttpPost("changes/{changePublicId:guid}/reject"), RequirePermission(PermissionCatalog.SalesModifyApprove)]
    public Task<SalesOrderChangeDto> Reject(Guid publicId, Guid changePublicId, [FromBody] SalesOrderChangeDecisionRequest? req, CancellationToken ct)
        => changes.RejectAsync(publicId, changePublicId, req, ct);

    /// <summary>Retira la modificación pendiente (quien la registró o quien puede modificar órdenes). No se aplica nada.</summary>
    [HttpPost("changes/{changePublicId:guid}/withdraw"), RequirePermission(PermissionCatalog.SalesModify)]
    public Task<SalesOrderChangeDto> Withdraw(Guid publicId, Guid changePublicId, [FromBody] SalesOrderChangeDecisionRequest? req, CancellationToken ct)
        => changes.WithdrawAsync(publicId, changePublicId, req, ct);

    /// <summary>
    /// Sube la evidencia de la autorización del cliente (imagen o PDF, hasta Files:MaxMegabytes) como documento de la orden. Se refiere después en la modificación
    /// por su PublicId (attachmentFilePublicId); un archivo respalda una sola modificación.
    /// </summary>
    [HttpPost("change-attachments"), Consumes("multipart/form-data"), RequirePermission(PermissionCatalog.SalesModify)]
    [RequestSizeLimit(32 * 1024 * 1024)]
    public async Task<StoredFileDto> UploadAttachment(Guid publicId, IFormFile? file, [FromServices] StoredFileService files, CancellationToken ct)
    {
        if (file is null || file.Length == 0) throw new Teikem.Infrastructure.Exceptions.ValidationException("file", SalesOrderChangeService.AttachmentFileRequired);
        if (file.Length > files.MaxBytes) throw new Teikem.Infrastructure.Exceptions.ValidationException("file", Teikem.Domain.Files.StoredFileRules.TooLarge((int)(files.MaxBytes / 1024 / 1024)));
        using var buffer = new MemoryStream((int)file.Length);
        await file.CopyToAsync(buffer, ct);
        return await changes.UploadAttachmentAsync(publicId, file.FileName, file.ContentType, buffer.ToArray(), ct);
    }

    /// <summary>La orden interna de adición (orden aparte que cuelga de la raíz): se crea y se solicita en una sola transacción, o al aprobar si toca segunda aprobación.</summary>
    [HttpPost("additions"), RequirePermission(PermissionCatalog.SalesModify)]
    public Task<SalesOrderChangeDto> CreateAddition(Guid publicId, [FromBody] SalesOrderAdditionRequest? req, CancellationToken ct) => changes.CreateAdditionAsync(publicId, req, ct);
}
