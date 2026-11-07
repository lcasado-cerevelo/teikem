using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Clients;
using Teikem.Domain.Common;
using Teikem.Domain.Constants;
using Teikem.Domain.Files;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 222 (plan §1.3) — documentos del cliente: la foto del registro de comerciante, el certificado de revendedor u otro. El contenido va al almacén de archivos (StoredFile clase
/// DOCUMENT, dueño CLIENT: se ve por GET /files/{id} con clients.read); la fila ClientDocument dice el tipo. Subir o quitar exige clients.update, o clients.create_basic mientras
/// el cliente es PROSPECTO (el vendedor sube las fotos del cliente nuevo que dio de alta). Quitar = IsActive = 0 (el contenido no se borra).
/// </summary>
public sealed class ClientDocumentService(TeikemDbContext db, ITenantContext tenant, ILookupCache lookups, StoredFileService files, PermissionService permissions)
{
    public async Task<IReadOnlyList<ClientDocumentDto>> ListAsync(Guid clientPublicId, CancellationToken ct)
    {
        var client = await db.Clients.AsNoTracking().FirstOrDefaultAsync(c => c.PublicId == clientPublicId, ct) ?? throw new NotFoundException("Cliente");
        var rows = await (from d in db.Set<ClientDocument>().AsNoTracking()
                          join f in db.Set<StoredFile>().AsNoTracking() on d.StoredFileId equals f.StoredFileId
                          where d.ClientId == client.ClientId && d.IsActive && f.IsActive
                          orderby d.ClientDocumentId
                          select new { Doc = d, File = f }).ToListAsync(ct);
        var userIds = rows.Where(r => r.Doc.CreatedBy is not null).Select(r => r.Doc.CreatedBy!.Value).Distinct().ToList();
        var names = userIds.Count == 0 ? new Dictionary<int, string>()
            : await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName ?? u.Email ?? string.Empty, ct);
        var result = new List<ClientDocumentDto>(rows.Count);
        foreach (var r in rows)
        {
            var type = await lookups.GetAsync(r.Doc.DocumentTypeLookupId, ct);
            result.Add(new ClientDocumentDto(r.Doc.PublicId, type?.InternalCode ?? string.Empty, type is null ? string.Empty : MultilingualText.Resolve(type.LabelJson, tenant.Lang),
                r.File.PublicId, r.File.FileName, r.File.ContentType, r.File.SizeBytes, r.Doc.Notes, r.Doc.CreatedAtUtc,
                r.Doc.CreatedBy is int u ? names.GetValueOrDefault(u) : null));
        }
        return result;
    }

    /// <summary>Sube un documento (JPG, PNG, WEBP, GIF o PDF; el tipo real se comprueba por el contenido). 400 con el mensaje del almacén si el archivo no sirve.</summary>
    public async Task<ClientDocumentDto> UploadAsync(Guid clientPublicId, string? documentType, string? notes, string? fileName, string? contentType, ReadOnlyMemory<byte> content,
        CancellationToken ct)
    {
        var client = await db.Clients.AsNoTracking().FirstOrDefaultAsync(c => c.PublicId == clientPublicId, ct) ?? throw new NotFoundException("Cliente");
        await EnsureCanWriteAsync(client, ct);
        var code = (documentType ?? string.Empty).Trim().ToUpperInvariant();
        if (!ClientDocumentCodes.Types.Contains(code)) throw new ValidationException("documentType", ClientProspectRules.DocumentTypeInvalid);
        var typeId = await lookups.TryGetIdAsync(ClientDocumentCodes.TypeDomain, code, ct) ?? throw new ValidationException("documentType", ClientProspectRules.DocumentTypeInvalid);
        var n = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        if (n is { Length: > ClientProspectRules.MaxNotesLength }) throw new ValidationException("notes", ClientProspectRules.DocumentNotesTooLong);

        var stored = await files.AttachAsync(EntityTypes.Client, client.ClientId, StoredFileCodes.KindDocument, fileName, contentType, content, ct, enforceOwnerWrite: false);
        var fileId = await db.Set<StoredFile>().AsNoTracking().Where(f => f.PublicId == stored.PublicId).Select(f => f.StoredFileId).FirstAsync(ct);
        var doc = new ClientDocument
        {
            PublicId = Guid.NewGuid(), TenantId = ((TenantContext)tenant).RequireTenantId(), ClientId = client.ClientId, StoredFileId = fileId, DocumentTypeLookupId = typeId,
            Notes = n, IsActive = true, CreatedAtUtc = DateTime.UtcNow, CreatedBy = tenant.UserId,
        };
        db.Set<ClientDocument>().Add(doc);
        await db.SaveChangesAsync(ct);
        return (await ListAsync(clientPublicId, ct)).First(d => d.PublicId == doc.PublicId);
    }

    /// <summary>Quita el documento en lógico (y su archivo; el contenido no se borra del almacén).</summary>
    public async Task RemoveAsync(Guid clientPublicId, Guid documentPublicId, CancellationToken ct)
    {
        var client = await db.Clients.AsNoTracking().FirstOrDefaultAsync(c => c.PublicId == clientPublicId, ct) ?? throw new NotFoundException("Cliente");
        await EnsureCanWriteAsync(client, ct);
        var doc = await db.Set<ClientDocument>().AsTracking().FirstOrDefaultAsync(d => d.PublicId == documentPublicId && d.ClientId == client.ClientId && d.IsActive, ct)
                  ?? throw new NotFoundException("Documento", documentPublicId);
        doc.IsActive = false;
        var file = await db.Set<StoredFile>().AsTracking().FirstOrDefaultAsync(f => f.StoredFileId == doc.StoredFileId, ct);
        if (file is not null) file.IsActive = false;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>clients.update siempre; clients.create_basic solo mientras el cliente es PROSPECTO.</summary>
    private async Task EnsureCanWriteAsync(Client client, CancellationToken ct)
    {
        if (await permissions.HasPermissionAsync(PermissionCatalog.ClientsUpdate, ct)) return;
        var status = await db.StatusCodes.AsNoTracking().Where(s => s.StatusCodeId == client.StatusCodeId).Select(s => s.InternalCode).FirstOrDefaultAsync(ct);
        if (status == ClientStatuses.Prospect && await permissions.HasPermissionAsync(PermissionCatalog.ClientsCreateBasic, ct)) return;
        throw new ForbiddenException(ClientProspectRules.DocumentNeedsPermission);
    }
}
