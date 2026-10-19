using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Teikem.Infrastructure.Services;

namespace Teikem.Api.Controllers;

/// <summary>
/// Lote 127 — el almacén de archivos: servir y quitar un archivo por su id público. El permiso lo decide el dueño del archivo (el comprobante de entrega: orders.view para ver, orders.deliver para quitar);
/// la compañía sale del principal (filtro global). Nunca se sirve con un enlace al disco. Subir se hace desde el dueño (p. ej. POST /orders/{id}/delivery/files).
/// </summary>
[ApiController]
[Route("api/v1/files")]
[Authorize]
public sealed class FilesController(StoredFileService files) : ControllerBase
{
    /// <summary>El contenido del archivo, en línea (una imagen se ve en el navegador; un PDF se abre). Cache privada corta.</summary>
    [HttpGet("{publicId:guid}")]
    public async Task<IActionResult> Get(Guid publicId, CancellationToken ct)
    {
        var f = await files.GetContentAsync(publicId, ct);
        Response.Headers.CacheControl = "private, max-age=300";
        Response.Headers.ContentDisposition = $"inline; filename=\"{f.FileName}\"";
        return File(f.Bytes, f.ContentType);
    }

    /// <summary>Quita el archivo en lógico (el contenido no se borra del almacén).</summary>
    [HttpDelete("{publicId:guid}")]
    public async Task<IActionResult> Remove(Guid publicId, CancellationToken ct)
    {
        await files.RemoveAsync(publicId, ct);
        return NoContent();
    }
}
