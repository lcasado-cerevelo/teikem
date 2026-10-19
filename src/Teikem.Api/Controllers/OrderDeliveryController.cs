using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Teikem.Api.Auth;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;

namespace Teikem.Api.Controllers;

/// <summary>
/// Lote 118 — registrar la entrega de una orden desde la web (permiso orders.deliver, módulo LTL_GROUND): la orden pasa a «Entregada» y queda el comprobante, con la firma automática del usuario que lo registra.
/// Consultar el comprobante exige poder ver órdenes. La compañía sale del principal.
/// </summary>
[ApiController]
[Route("api/v1/orders/{publicId:guid}/delivery")]
[Authorize]
[RequireModule(ModuleKeys.LtlGround)]
public sealed class OrderDeliveryController(DeliveryRegistrationService delivery, StoredFileService files) : ControllerBase
{
    /// <summary>Lote 127: adjunta al comprobante la imagen de la firma (kind SIGNATURE) o una foto de la entrega (DELIVERY_PHOTO); JPG, PNG, WEBP, GIF o PDF, hasta Files:MaxMegabytes.</summary>
    [HttpPost("files"), Consumes("multipart/form-data"), RequirePermission(PermissionCatalog.OrdersDeliver)]
    [RequestSizeLimit(32 * 1024 * 1024)]
    public async Task<StoredFileDto> AttachFile(Guid publicId, [FromForm] string? kind, IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length == 0) throw new Teikem.Infrastructure.Exceptions.ValidationException("file", Teikem.Domain.Files.StoredFileRules.FileRequired);
        if (file.Length > files.MaxBytes) throw new Teikem.Infrastructure.Exceptions.ValidationException("file", Teikem.Domain.Files.StoredFileRules.TooLarge(files.MaxBytes / 1024 / 1024));
        var proofId = await delivery.RequireProofIdAsync(publicId, ct);
        using var buffer = new MemoryStream((int)file.Length);
        await file.CopyToAsync(buffer, ct);
        return await files.AttachAsync(Teikem.Domain.Orders.DeliveryProofCodes.EntityType, proofId, kind ?? Teikem.Domain.Files.StoredFileCodes.KindDeliveryPhoto, file.FileName, file.ContentType, buffer.ToArray(), ct);
    }

    [HttpPost, RequirePermission(PermissionCatalog.OrdersDeliver)]
    public Task<DeliveryProofDto> Register(Guid publicId, [FromBody] DeliveryRegisterRequest? req, CancellationToken ct) => delivery.RegisterAsync(publicId, req, ct);

    /// <summary>El comprobante de la orden, o 204 si todavía no tiene (la entrega se registró por otra vía o no se ha entregado).</summary>
    [HttpGet, RequirePermission(PermissionCatalog.OrdersView)]
    public async Task<ActionResult<DeliveryProofDto>> Get(Guid publicId, CancellationToken ct)
        => await delivery.FindByOrderAsync(publicId, ct) is { } proof ? proof : NoContent();
}
