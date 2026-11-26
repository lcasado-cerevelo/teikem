using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Clients;
using Teikem.Domain.Common;
using Teikem.Domain.Constants;
using Teikem.Domain.Files;
using Teikem.Domain.Tax;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Clients;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 222 (plan §1.3) — documentos del cliente: la foto del registro de comerciante, el certificado de revendedor u otro. El contenido va al almacén de archivos (StoredFile clase
/// DOCUMENT, dueño CLIENT: se ve por GET /files/{id} con clients.read); la fila ClientDocument dice el tipo. Subir o quitar exige clients.update, o clients.create_basic mientras
/// el cliente es PROSPECTO (el vendedor sube las fotos del cliente nuevo que dio de alta). Quitar = IsActive = 0 (el contenido no se borra).
/// <para>
/// Lote 248 — número y vigencia: el tipo se valida contra el catálogo (activo y encendido en la compañía), no contra una lista fija; si el tipo «lleva vigencia» el «hasta»
/// es obligatorio (un «hasta» pasado sí se acepta: es el registro de un documento real). Editar (PATCH) con rowVersion; el tipo no se edita. Si el documento respalda una
/// exención de IVU activa: cambiarle número o fechas exige además tax.manage (403), no se le puede quitar el «hasta» (400) y la copia de la exención (número y fechas) se
/// sincroniza en el mismo guardado; quitarlo mientras la exención está vigente da 409. El estado (vigente, por vencer…) se deriva de la fecha y no se guarda.
/// </para>
/// </summary>
public sealed class ClientDocumentService(TeikemDbContext db, ITenantContext tenant, ILookupCache lookups, StoredFileService files, PermissionService permissions,
    ModuleService? modules = null, ITenantClock? clock = null)
{
    /// <summary>Lote 248 (409) — el documento cambió (rowVersion) mientras se editaba.</summary>
    public const string ConcurrencyMessage = "El documento cambió mientras lo editaba. Vuelva a abrirlo.";
    /// <summary>Lote 248 (400) — fecha del formulario de subir que no es AAAA-MM-DD.</summary>
    public const string DateFormatInvalid = "La fecha debe tener el formato AAAA-MM-DD.";

    private DateOnly Today => (clock ?? TenantClock.Default).Today;

    public async Task<IReadOnlyList<ClientDocumentDto>> ListAsync(Guid clientPublicId, CancellationToken ct)
    {
        var client = await db.Clients.AsNoTracking().FirstOrDefaultAsync(c => c.PublicId == clientPublicId, ct) ?? throw new NotFoundException("Cliente");
        return await BuildAsync(client, ct);
    }

    /// <summary>
    /// Los documentos activos del cliente (con el archivo activo), con su vigencia. «Renovado» con <see cref="ClientDocumentRules.IsRenewed"/> contra los demás documentos
    /// activos del cliente (la misma regla que usa ClientDocumentQueries.ValidityRowsAsync); «respalda exención» = hay una exención de IVU ACTIVA vinculada.
    /// </summary>
    private async Task<IReadOnlyList<ClientDocumentDto>> BuildAsync(Client client, CancellationToken ct)
    {
        var tenantId = client.TenantId;
        var rows = await (from d in db.Set<ClientDocument>().AsNoTracking()
                          join f in db.Set<StoredFile>().AsNoTracking() on d.StoredFileId equals f.StoredFileId
                          where d.ClientId == client.ClientId && d.IsActive && f.IsActive
                          orderby d.ClientDocumentId
                          select new { Doc = d, File = f }).ToListAsync(ct);
        if (rows.Count == 0) return Array.Empty<ClientDocumentDto>();
        var userIds = rows.Where(r => r.Doc.CreatedBy is not null).Select(r => r.Doc.CreatedBy!.Value).Distinct().ToList();
        var names = userIds.Count == 0 ? new Dictionary<int, string>()
            : await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName ?? u.Email ?? string.Empty, ct);
        var types = await ClientDocumentQueries.TypesByIdAsync(db, tenantId, rows.Select(r => r.Doc.DocumentTypeLookupId), ct);
        var docIds = rows.Select(r => r.Doc.ClientDocumentId).ToList();
        var backing = (await db.Set<ClientTaxExemption>().AsNoTracking()
                .Where(e => e.IsActive && e.ClientDocumentId != null && docIds.Contains(e.ClientDocumentId.Value))
                .Select(e => e.ClientDocumentId!.Value).ToListAsync(ct)).ToHashSet();
        var keys = rows.Select(r => new ClientDocumentValidityKey(r.Doc.ClientDocumentId, r.Doc.ClientId, r.Doc.DocumentTypeLookupId, r.Doc.ValidTo)).ToList();
        var warnDays = await WarnDaysAsync(tenantId, ct);
        var today = Today;

        var result = new List<ClientDocumentDto>(rows.Count);
        foreach (var r in rows)
        {
            var type = types.GetValueOrDefault(r.Doc.DocumentTypeLookupId);
            var marks = type?.Marks ?? ClientDocumentTypeMarks.Default;
            var key = keys.First(k => k.DocumentId == r.Doc.ClientDocumentId);
            var renewed = ClientDocumentRules.IsRenewed(key, keys);
            result.Add(new ClientDocumentDto(r.Doc.PublicId, type?.Code ?? string.Empty, type is null ? string.Empty : MultilingualText.Resolve(type.LabelJson, tenant.Lang),
                r.File.PublicId, r.File.FileName, r.File.ContentType, r.File.SizeBytes, r.Doc.Notes, r.Doc.CreatedAtUtc,
                r.Doc.CreatedBy is int u ? names.GetValueOrDefault(u) : null,
                r.Doc.DocumentNumber, r.Doc.ValidFrom, r.Doc.ValidTo, ClientDocumentRules.DaysToExpiry(r.Doc.ValidTo, today),
                ClientDocumentRules.ExpiryState(r.Doc.ValidTo, today, warnDays, renewed, marks.RequiresValidTo),
                marks.IsTax, marks.RequiresValidTo, backing.Contains(r.Doc.ClientDocumentId),
                r.Doc.RowVersion is { Length: > 0 } rv ? Convert.ToBase64String(rv) : null));
        }
        return result;
    }

    /// <summary>Días de «por vencer» de la compañía: el mayor umbral positivo de Tenant.DocExpiryThresholdDays (los de omisión si la columna está rota).</summary>
    private async Task<int> WarnDaysAsync(int tenantId, CancellationToken ct)
    {
        var csv = await db.Tenants.AsNoTracking().Where(t => t.TenantId == tenantId).Select(t => t.DocExpiryThresholdDays).FirstOrDefaultAsync(ct);
        return ExpiryNoticeRules.WarnDays(ExpiryNoticeRules.ThresholdsOrDefault(csv));
    }

    /// <summary>
    /// Sube un documento (JPG, PNG, WEBP, GIF o PDF; el tipo real se comprueba por el contenido). 400 con el mensaje del almacén si el archivo no sirve.
    /// Lote 248: número y vigencia opcionales al final (la firma del Lote 222 se conserva: la app de vendedores sube solo con el tipo). Todo se valida ANTES de guardar el
    /// archivo en el almacén (no queda contenido huérfano).
    /// </summary>
    public async Task<ClientDocumentDto> UploadAsync(Guid clientPublicId, string? documentType, string? notes, string? fileName, string? contentType, ReadOnlyMemory<byte> content,
        CancellationToken ct, string? documentNumber = null, DateOnly? validFrom = null, DateOnly? validTo = null)
    {
        var client = await db.Clients.AsNoTracking().FirstOrDefaultAsync(c => c.PublicId == clientPublicId, ct) ?? throw new NotFoundException("Cliente");
        await EnsureCanWriteAsync(client, ct);
        var tenantId = ((TenantContext)tenant).RequireTenantId();
        var type = await ClientDocumentQueries.TypeAsync(db, lookups, tenantId, documentType, ct);
        if (type is not { IsEnabled: true }) throw new ValidationException("documentType", ClientDocumentRules.TypeInvalid);
        var n = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        if (n is { Length: > ClientProspectRules.MaxNotesLength }) throw new ValidationException("notes", ClientProspectRules.DocumentNotesTooLong);
        var number = ClientDocumentRules.NormalizeNumber(documentNumber);
        var (field, error) = ClientDocumentRules.ValidateMetadata(number, validFrom, validTo, type.Marks);
        if (error is not null) throw new ValidationException(field!, error);

        var stored = await files.AttachAsync(EntityTypes.Client, client.ClientId, StoredFileCodes.KindDocument, fileName, contentType, content, ct, enforceOwnerWrite: false);
        var fileId = await db.Set<StoredFile>().AsNoTracking().Where(f => f.PublicId == stored.PublicId).Select(f => f.StoredFileId).FirstAsync(ct);
        var doc = new ClientDocument
        {
            PublicId = Guid.NewGuid(), TenantId = tenantId, ClientId = client.ClientId, StoredFileId = fileId, DocumentTypeLookupId = type.LookupCodeId,
            Notes = n, DocumentNumber = number, ValidFrom = validFrom, ValidTo = validTo, IsActive = true, CreatedAtUtc = DateTime.UtcNow, CreatedBy = tenant.UserId,
        };
        db.Set<ClientDocument>().Add(doc);
        await db.SaveChangesAsync(ct);
        var dto = (await BuildAsync(client, ct)).First(d => d.PublicId == doc.PublicId);
        return dto with { SuggestExemption = type.Marks.IsTax && await CanApplyExemptionAsync(ct) };
    }

    /// <summary>Lee una fecha del formulario de subir (AAAA-MM-DD); vacía = null; otra cosa = 400 en ese campo.</summary>
    public static DateOnly? ParseFormDate(string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return DateOnly.TryParseExact(value.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d
            : throw new ValidationException(field, DateFormatInvalid);
    }

    /// <summary>
    /// Lote 248 — edita número, vigencia y nota del documento (el tipo no se edita). Misma regla de escritura que al subir; rowVersion viejo = 409. Se valida el estado FINAL
    /// (un documento viejo de un tipo que pasó a «lleva vigencia» exige el «hasta» aunque solo se cambie la nota). Si respalda una exención activa: cambiar número o fechas
    /// exige tax.manage (403), quitar el «hasta» da 400 y la copia de la exención se sincroniza en el mismo guardado.
    /// </summary>
    public async Task<ClientDocumentDto> UpdateAsync(Guid clientPublicId, Guid documentPublicId, ClientDocumentPatchRequest req, CancellationToken ct)
    {
        var client = await db.Clients.AsNoTracking().FirstOrDefaultAsync(c => c.PublicId == clientPublicId, ct) ?? throw new NotFoundException("Cliente");
        await EnsureCanWriteAsync(client, ct);
        var tenantId = ((TenantContext)tenant).RequireTenantId();
        var doc = await db.Set<ClientDocument>().AsTracking().FirstOrDefaultAsync(d => d.PublicId == documentPublicId && d.ClientId == client.ClientId && d.IsActive, ct)
                  ?? throw new NotFoundException("Documento", documentPublicId);

        var number = req.DocumentNumber is null ? doc.DocumentNumber : ClientDocumentRules.NormalizeNumber(req.DocumentNumber);
        var validFrom = req.ClearValidFrom ? null : req.ValidFrom ?? doc.ValidFrom;
        var validTo = req.ClearValidTo ? null : req.ValidTo ?? doc.ValidTo;
        var notes = req.Notes is null ? doc.Notes : (string.IsNullOrWhiteSpace(req.Notes) ? null : req.Notes.Trim());
        if (notes is { Length: > ClientProspectRules.MaxNotesLength }) throw new ValidationException("notes", ClientProspectRules.DocumentNotesTooLong);
        var marks = (await ClientDocumentQueries.MarksByTypeIdAsync(db, tenantId, new[] { doc.DocumentTypeLookupId }, ct)).GetValueOrDefault(doc.DocumentTypeLookupId)
                    ?? ClientDocumentTypeMarks.Default;
        var (field, error) = ClientDocumentRules.ValidateMetadata(number, validFrom, validTo, marks);
        if (error is not null) throw new ValidationException(field!, error);

        var metadataChanged = number != doc.DocumentNumber || validFrom != doc.ValidFrom || validTo != doc.ValidTo;
        ClientTaxExemption? exemption = null;
        if (metadataChanged && await ClientDocumentQueries.ActiveBackingExemptionAsync(db, tenantId, doc.ClientDocumentId, Today, ct) is { } backing)
        {
            if (!await permissions.HasPermissionAsync(PermissionCatalog.TaxManage, ct)) throw new ForbiddenException(ClientDocumentRules.BacksExemptionNeedsTax);
            if (validTo is null) throw new ValidationException("validTo", ClientDocumentRules.BacksExemptionNeedsValidTo);
            exemption = await db.Set<ClientTaxExemption>().AsTracking().FirstAsync(e => e.ClientTaxExemptionId == backing.ClientTaxExemptionId, ct);
        }

        db.ApplyRowVersion(doc, req.RowVersion);
        var now = DateTime.UtcNow;
        doc.DocumentNumber = number;
        doc.ValidFrom = validFrom;
        doc.ValidTo = validTo;
        doc.Notes = notes;
        doc.UpdatedAtUtc = now;
        doc.UpdatedBy = tenant.UserId;
        if (exemption is not null)
        {
            // la copia de la exención (solo para los listados: el cálculo lee el documento)
            if (number is not null) exemption.CertificateNumber = number;
            exemption.ValidFrom = validFrom ?? exemption.ValidFrom;
            exemption.ValidTo = validTo;
            exemption.UpdatedAtUtc = now;
            exemption.UpdatedBy = tenant.UserId;
        }
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException(ConcurrencyMessage);
        }
        return (await BuildAsync(client, ct)).First(d => d.PublicId == doc.PublicId);
    }

    /// <summary>
    /// Quita el documento en lógico (y su archivo; el contenido no se borra del almacén). Lote 248: si respalda una exención de IVU activa y vigente («hasta» de hoy en
    /// adelante) da 409: primero se da de baja la exención en Impuestos.
    /// </summary>
    public async Task RemoveAsync(Guid clientPublicId, Guid documentPublicId, CancellationToken ct)
    {
        var client = await db.Clients.AsNoTracking().FirstOrDefaultAsync(c => c.PublicId == clientPublicId, ct) ?? throw new NotFoundException("Cliente");
        await EnsureCanWriteAsync(client, ct);
        var doc = await db.Set<ClientDocument>().AsTracking().FirstOrDefaultAsync(d => d.PublicId == documentPublicId && d.ClientId == client.ClientId && d.IsActive, ct)
                  ?? throw new NotFoundException("Documento", documentPublicId);
        if (await ClientDocumentQueries.ActiveBackingExemptionAsync(db, client.TenantId, doc.ClientDocumentId, Today, ct) is { IsCurrent: true })
            throw new ConflictException(ClientDocumentRules.BacksExemptionCannotRemove);
        doc.IsActive = false;
        doc.UpdatedAtUtc = DateTime.UtcNow;
        doc.UpdatedBy = tenant.UserId;
        var file = await db.Set<StoredFile>().AsTracking().FirstOrDefaultAsync(f => f.StoredFileId == doc.StoredFileId, ct);
        if (file is not null) file.IsActive = false;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>¿Quien sube puede aplicar la exención de IVU? tax.manage y el módulo TAX encendido (la pista «¿Aplicar la exención de IVU?»).</summary>
    private async Task<bool> CanApplyExemptionAsync(CancellationToken ct)
        => await permissions.HasPermissionAsync(PermissionCatalog.TaxManage, ct) && modules is not null && await modules.IsEnabledAsync(ModuleKeys.Tax, ct);

    /// <summary>clients.update siempre; clients.create_basic solo mientras el cliente es PROSPECTO.</summary>
    private async Task EnsureCanWriteAsync(Client client, CancellationToken ct)
    {
        if (await permissions.HasPermissionAsync(PermissionCatalog.ClientsUpdate, ct)) return;
        var status = await db.StatusCodes.AsNoTracking().Where(s => s.StatusCodeId == client.StatusCodeId).Select(s => s.InternalCode).FirstOrDefaultAsync(ct);
        if (status == ClientStatuses.Prospect && await permissions.HasPermissionAsync(PermissionCatalog.ClientsCreateBasic, ct)) return;
        throw new ForbiddenException(ClientProspectRules.DocumentNeedsPermission);
    }
}
