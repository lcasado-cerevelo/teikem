using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Teikem.Api.Auth;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;

namespace Teikem.Api.Controllers;

/// <summary>
/// Lote 2 — Clientes: lista, alta compuesta (cliente + contrato inicial), ficha, perfil, numeración por cliente,
/// contactos (personas), estatus y baja lógica. Vive bajo el módulo CATALOG; los permisos son clients.read/create/update.
/// Teléfonos/correos del cliente van por /api/v1/contacts/CLIENT/{id} (Lote 1) y los de cada persona por CLIENT_CONTACT.
/// </summary>
[ApiController]
[Route("api/v1/clients")]
[Authorize]
[RequireModule(ModuleKeys.Catalog)]
public sealed class ClientsController(ClientService clients) : ControllerBase
{
    [HttpGet, RequirePermission(PermissionCatalog.ClientsRead)]
    public Task<IReadOnlyList<ClientListItemDto>> List([FromQuery] string? search, [FromQuery] bool includeInactive, CancellationToken ct)
        => clients.GetListAsync(search, includeInactive, ct, CustomFieldFilters.FromQuery(Request.Query));   // Lote 130: cf.clave

    /// <summary>Alta compuesta del modal '+ Nuevo cliente'. Devuelve la ficha completa (con currentContract si se creó contrato).</summary>
    [HttpPost, RequirePermission(PermissionCatalog.ClientsCreate)]
    public Task<ClientDetailDto> Create([FromBody] ClientCreateRequest req, CancellationToken ct) => clients.CreateAsync(req, ct);

    /// <summary>Vista previa en vivo de un patrón de numeración: ?pattern=AX-%23%23%23%23%23&amp;seq=1 → AX-00001.</summary>
    [HttpGet("number-format/preview"), RequirePermission(PermissionCatalog.ClientsRead)]
    public NumberPreviewDto PreviewNumber([FromQuery] string? pattern, [FromQuery] long seq = 1) => ClientService.PreviewNumber(pattern, seq);

    [HttpGet("{publicId:guid}"), RequirePermission(PermissionCatalog.ClientsRead)]
    public Task<ClientDetailDto> Get(Guid publicId, CancellationToken ct) => clients.GetAsync(publicId, ct);

    /// <summary>Perfil (sin Name: la identidad del cliente no se edita). Acepta rowVersion opcional (409 si cambió).</summary>
    [HttpPatch("{publicId:guid}/profile"), RequirePermission(PermissionCatalog.ClientsUpdate)]
    public Task<ClientDetailDto> UpdateProfile(Guid publicId, [FromBody] ClientProfileUpdateRequest req, CancellationToken ct)
        => clients.UpdateProfileAsync(publicId, req, ct);

    /// <summary>Numeración por cliente: quién asigna el número de orden/factura y los patrones (cadena vacía = patrón por defecto).</summary>
    [HttpPatch("{publicId:guid}/number-settings"), RequirePermission(PermissionCatalog.ClientsUpdate)]
    public Task<ClientDetailDto> UpdateNumberSettings(Guid publicId, [FromBody] ClientNumberSettingsRequest req, CancellationToken ct)
        => clients.UpdateNumberSettingsAsync(publicId, req, ct);

    [HttpGet("{publicId:guid}/contacts"), RequirePermission(PermissionCatalog.ClientsRead)]
    public Task<IReadOnlyList<ClientContactDto>> Contacts(Guid publicId, [FromQuery] bool includeInactive, CancellationToken ct)
        => clients.GetContactsAsync(publicId, includeInactive, ct);

    [HttpPost("{publicId:guid}/contacts"), RequirePermission(PermissionCatalog.ClientsUpdate)]
    public Task<ClientContactDto> AddContact(Guid publicId, [FromBody] ClientContactCreateRequest req, CancellationToken ct)
        => clients.AddContactAsync(publicId, req, ct);

    [HttpPatch("{publicId:guid}/contacts/{id:int}"), RequirePermission(PermissionCatalog.ClientsUpdate)]
    public Task<ClientContactDto> UpdateContact(Guid publicId, int id, [FromBody] ClientContactUpdateRequest req, CancellationToken ct)
        => clients.UpdateContactAsync(publicId, id, req, ct);

    /// <summary>Cambio de estatus (ClientStatus) vía StatusService: valida etapa, escribe historial y dispara efectos.</summary>
    [HttpPost("{publicId:guid}/status"), RequirePermission(PermissionCatalog.ClientsUpdate)]
    public Task<ClientDetailDto> TransitionStatus(Guid publicId, [FromBody] StatusChangeRequest req, CancellationToken ct)
        => clients.TransitionStatusAsync(publicId, req, ct);

    /// <summary>Baja lógica (IsActive = 0). Nunca DELETE.</summary>
    [HttpPost("{publicId:guid}/deactivate"), RequirePermission(PermissionCatalog.ClientsUpdate)]
    public async Task<IActionResult> Deactivate(Guid publicId, CancellationToken ct) { await clients.SetActiveAsync(publicId, false, ct); return NoContent(); }

    [HttpPost("{publicId:guid}/reactivate"), RequirePermission(PermissionCatalog.ClientsUpdate)]
    public async Task<IActionResult> Reactivate(Guid publicId, CancellationToken ct) { await clients.SetActiveAsync(publicId, true, ct); return NoContent(); }

    // ---------------------------------------------------------------- Lote 222 (pedido de vendedores): cliente prospecto y documentos

    /// <summary>Alta mínima del cliente nuevo desde un pedido: nace PROSPECTO y avisa a quien aprueba. 400 con el campo y el mensaje exacto si falta algo.</summary>
    [HttpPost("basic"), RequirePermission(PermissionCatalog.ClientsCreateBasic + "|" + PermissionCatalog.ClientsCreate)]
    public Task<ClientDetailDto> CreateBasic([FromBody] ClientBasicCreateRequest? req, [FromServices] ClientProspectService prospects, CancellationToken ct)
        => prospects.CreateBasicAsync(req, ct);

    /// <summary>Aprueba el prospecto (→ ACTIVO) con su límite de crédito y término; sus órdenes ya se pueden despachar. 422 si no es prospecto.</summary>
    [HttpPost("{publicId:guid}/approve"), RequirePermission(PermissionCatalog.SalesApproveClient)]
    public Task<ClientDetailDto> Approve(Guid publicId, [FromBody] ClientApproveRequest? req, [FromServices] ClientProspectService prospects, CancellationToken ct)
        => prospects.ApproveAsync(publicId, req, ct);

    /// <summary>Rechaza el prospecto (→ SUSPENDIDO) y cancela sus órdenes abiertas. El comentario es obligatorio (400); 422 si no es prospecto.</summary>
    [HttpPost("{publicId:guid}/reject"), RequirePermission(PermissionCatalog.SalesApproveClient)]
    public Task<ClientRejectResultDto> Reject(Guid publicId, [FromBody] ClientRejectRequest? req, [FromServices] ClientProspectService prospects, CancellationToken ct)
        => prospects.RejectAsync(publicId, req, ct);

    /// <summary>Documentos del cliente (registro de comerciante, certificado de revendedor, otro). El archivo se ve por GET /files/{filePublicId}.</summary>
    [HttpGet("{publicId:guid}/documents"), RequirePermission(PermissionCatalog.ClientsRead)]
    public Task<IReadOnlyList<ClientDocumentDto>> Documents(Guid publicId, [FromServices] ClientDocumentService documents, CancellationToken ct) => documents.ListAsync(publicId, ct);

    /// <summary>Sube un documento (JPG, PNG, WEBP, GIF o PDF). documentType: MERCHANT_REGISTRATION, RESELLER_CERTIFICATE u OTHER. Exige clients.update (o clients.create_basic si es prospecto).</summary>
    [HttpPost("{publicId:guid}/documents"), Consumes("multipart/form-data"), RequirePermission(PermissionCatalog.ClientsUpdate + "|" + PermissionCatalog.ClientsCreateBasic)]
    [RequestSizeLimit(32 * 1024 * 1024)]
    public async Task<ClientDocumentDto> UploadDocument(Guid publicId, IFormFile? file, [FromForm] string? documentType, [FromForm] string? notes,
        [FromServices] ClientDocumentService documents, CancellationToken ct)
    {
        byte[] bytes = Array.Empty<byte>();
        if (file is not null && file.Length > 0)
        {
            await using var ms = new MemoryStream();
            await file.CopyToAsync(ms, ct);
            bytes = ms.ToArray();
        }
        return await documents.UploadAsync(publicId, documentType, notes, file?.FileName, file?.ContentType, bytes, ct);
    }

    /// <summary>Quita el documento (en lógico).</summary>
    [HttpDelete("{publicId:guid}/documents/{documentPublicId:guid}"), RequirePermission(PermissionCatalog.ClientsUpdate + "|" + PermissionCatalog.ClientsCreateBasic)]
    public async Task<IActionResult> RemoveDocument(Guid publicId, Guid documentPublicId, [FromServices] ClientDocumentService documents, CancellationToken ct)
    {
        await documents.RemoveAsync(publicId, documentPublicId, ct);
        return NoContent();
    }
}
