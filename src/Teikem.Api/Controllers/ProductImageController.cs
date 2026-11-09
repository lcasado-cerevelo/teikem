using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Teikem.Api.Auth;
using Teikem.Domain.Constants;
using Teikem.Domain.Files;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;

namespace Teikem.Api.Controllers;

/// <summary>
/// Lote 229 — la foto del producto (vale para productos, kits y kits de oferta). Verla con inventory.view o sales.view; ponerla o quitarla con el permiso con que se
/// edita el producto (inventory.manage). Solo JPG, PNG o WEBP hasta Files:MaxMegabytes; si es más grande de 1200 px se reduce. El contenido se baja por
/// GET /files/{imagePublicId} (con sales.view basta).
/// </summary>
[ApiController]
[Route("api/v1/products/{publicId:guid}/image")]
[Authorize]
[RequireModule(ModuleKeys.WmsLotSerial)]
public sealed class ProductImageController(ProductImageService images, StoredFileService files) : ControllerBase
{
    /// <summary>La foto propia (imagePublicId; null = no tiene) y la que se muestra (effectiveImagePublicId: si es un kit sin foto, la de sus piezas).</summary>
    [HttpGet, RequirePermission(PermissionCatalog.InventoryView + "|" + PermissionCatalog.SalesView)]
    public Task<ProductImageDto> Get(Guid publicId, CancellationToken ct) => images.GetAsync(publicId, ct);

    /// <summary>Pone o cambia la foto. 400 si no es JPG/PNG/WEBP o pasa del tope; 404 producto.</summary>
    [HttpPost, Consumes("multipart/form-data"), RequirePermission(PermissionCatalog.InventoryManage)]
    [RequestSizeLimit(32 * 1024 * 1024)]
    public async Task<ProductImageDto> Set(Guid publicId, IFormFile? file, CancellationToken ct)
    {
        var bytes = await ReadAsync(file, files, ct);
        return await images.SetAsync(publicId, file!.FileName, bytes, ct);
    }

    /// <summary>Quita la foto (la desliga; el archivo no se borra).</summary>
    [HttpDelete, RequirePermission(PermissionCatalog.InventoryManage)]
    public Task<ProductImageDto> Clear(Guid publicId, CancellationToken ct) => images.ClearAsync(publicId, ct);

    /// <summary>Lee el archivo subido (vacío = 400 «Adjunte la foto.»; más grande que el tope = 400 con el mensaje del almacén).</summary>
    internal static async Task<byte[]> ReadAsync(IFormFile? file, StoredFileService files, CancellationToken ct)
    {
        if (file is null || file.Length == 0) throw new Teikem.Infrastructure.Exceptions.ValidationException("file", ProductImageRules.ImageRequired);
        if (file.Length > files.MaxBytes) throw new Teikem.Infrastructure.Exceptions.ValidationException("file", StoredFileRules.TooLarge(files.MaxBytes / 1024 / 1024));
        using var buffer = new MemoryStream((int)file.Length);
        await file.CopyToAsync(buffer, ct);
        return buffer.ToArray();
    }
}
