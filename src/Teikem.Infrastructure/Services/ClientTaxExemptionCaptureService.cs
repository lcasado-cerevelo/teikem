using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Clients;
using Teikem.Domain.Common;
using Teikem.Domain.Constants;
using Teikem.Domain.Files;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Clients;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Tax;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 248 — captura única de la exención de IVU desde la ficha del cliente (tax.manage + módulo TAX, lo exige el controlador):
/// - Con un documento YA SUBIDO (ClientDocumentPublicId): solo se crea la exención (<see cref="TaxService.CreateExemptionAsync"/>); es el camino de
///   «¿Aplicar la exención de IVU?» después de subir un certificado.
/// - Con un archivo NUEVO: se valida TODO antes de tocar el almacén de archivos (cliente, tipo activo y «de impuestos», número, fechas, alcance y archivo no
///   vacío), para no dejar contenido huérfano; luego, en UNA transacción, el archivo (StoredFile DOCUMENT del cliente), el documento y la exención. Si algo falla
///   se revierte todo y no queda ni documento ni exención.
/// Nunca las dos cosas a la vez (400). El documento se crea sin exigir clients.update (enforceOwnerWrite: false): la exención es lo que manda.
/// </summary>
public sealed class ClientTaxExemptionCaptureService(TeikemDbContext db, ITenantContext tenant, ITenantClock clock, ILookupCache lookups, TaxService tax,
    StoredFileService files)
{
    private TenantContext T => (TenantContext)tenant;

    /// <param name="content">El archivo nuevo; null = no se mandó archivo (un archivo vacío da 400 «Adjunte el archivo del certificado.»).</param>
    public async Task<ClientTaxExemptionCaptureDto> CaptureAsync(Guid clientPublicId, ClientTaxExemptionCaptureRequest req, string? fileName, string? contentType,
        byte[]? content, CancellationToken ct)
    {
        if (req is null) throw new ValidationException("body", "El cuerpo de la solicitud es obligatorio.");
        var tenantId = T.RequireTenantId();
        var client = await db.Clients.AsNoTracking().Where(c => c.PublicId == clientPublicId).Select(c => new { c.ClientId }).FirstOrDefaultAsync(ct)
                     ?? throw new NotFoundException("Cliente");
        var hasFile = content is not null;
        var hasDocument = req.ClientDocumentPublicId is not null;
        if (hasFile && hasDocument) throw new ValidationException("file", ClientExemptionRules.FileAndDocument);
        if (!hasFile && !hasDocument) throw new ValidationException("clientDocumentPublicId", ClientExemptionRules.DocumentRequired);

        if (req.ClientDocumentPublicId is Guid documentPublicId)
        {
            var created = await tax.CreateExemptionAsync(ToExemptionRequest(client.ClientId, req, documentPublicId), ct);
            return await WithDocumentAsync(created, ct);
        }

        // ---- archivo nuevo: todo se valida ANTES de guardar el archivo en el almacén
        var type = await ClientDocumentQueries.TypeAsync(db, lookups, tenantId, req.DocumentType, ct);
        if (type is null || !type.IsEnabled) throw new ValidationException("documentType", ClientDocumentRules.TypeInvalid);
        if (!type.Marks.IsTax) throw new ValidationException("documentType", ClientExemptionRules.DocumentNotTax);
        var number = ClientDocumentRules.NormalizeNumber(req.DocumentNumber);
        if (number is null) throw new ValidationException("documentNumber", TaxRules.CertificateRequired);
        if (number.Length > ClientDocumentRules.MaxNumberLength) throw new ValidationException("documentNumber", ClientDocumentRules.NumberTooLong);
        if (req.ValidFrom is not DateOnly from) throw new ValidationException("validFrom", ClientExemptionRules.ValidFromRequired);
        if (req.ValidTo is not DateOnly to) throw new ValidationException("validTo", ClientExemptionRules.ValidToRequired);
        if (to < from) throw new ValidationException("validTo", ClientDocumentRules.DatesOrder);
        if (to < clock.Today) throw new ValidationException("validTo", ClientExemptionRules.ValidToPast);
        await tax.NormalizeScopeAsync(req.Scope, req.ComponentCodes, ct);
        var exType = ClientExemptionRules.DefaultExemptionType(req.ExemptionType, MultilingualText.Resolve(type.LabelJson, T.Lang));
        if (exType.Length > ClientExemptionRules.MaxExemptionTypeLength) throw new ValidationException("exemptionType", TaxRules.ExemptionTypeTooLong);
        if (req.Notes is { } notes && notes.Trim().Length > 300) throw new ValidationException("notes", TaxRules.NotesTooLong);
        if (content!.Length == 0) throw new ValidationException("file", ClientExemptionRules.FileRequired);

        var result = await db.RunInTransactionAsync(async ct2 =>
        {
            var stored = await files.AttachAsync(EntityTypes.Client, client.ClientId, StoredFileCodes.KindDocument, fileName, contentType, content, ct2, enforceOwnerWrite: false);
            var fileId = await db.Set<StoredFile>().AsNoTracking().Where(f => f.PublicId == stored.PublicId).Select(f => f.StoredFileId).FirstAsync(ct2);
            var doc = new ClientDocument
            {
                PublicId = Guid.NewGuid(), TenantId = tenantId, ClientId = client.ClientId, StoredFileId = fileId, DocumentTypeLookupId = type.LookupCodeId,
                DocumentNumber = number, ValidFrom = from, ValidTo = to, IsActive = true, CreatedAtUtc = DateTime.UtcNow, CreatedBy = tenant.UserId,
            };
            db.Set<ClientDocument>().Add(doc);
            await db.SaveChangesAsync(ct2);
            var exemption = await tax.CreateExemptionAsync(ToExemptionRequest(client.ClientId, req with { ExemptionType = exType }, doc.PublicId), ct2);
            return new ClientTaxExemptionCaptureDto(exemption, doc.PublicId, stored.PublicId);
        }, ct);
        return result;
    }

    /// <summary>Las exenciones del cliente (todas, la más reciente primero), con su documento.</summary>
    public async Task<IReadOnlyList<TaxExemptionDto>> ListForClientAsync(Guid clientPublicId, CancellationToken ct)
    {
        var clientId = await db.Clients.AsNoTracking().Where(c => c.PublicId == clientPublicId).Select(c => (int?)c.ClientId).FirstOrDefaultAsync(ct)
                       ?? throw new NotFoundException("Cliente");
        return await tax.ListExemptionsAsync(clientId, ct);
    }

    private static TaxExemptionRequest ToExemptionRequest(int clientId, ClientTaxExemptionCaptureRequest req, Guid documentPublicId)
        => new(clientId, req.ExemptionType, null, null, null, req.Notes, req.Scope, req.ComponentCodes, documentPublicId);

    private async Task<ClientTaxExemptionCaptureDto> WithDocumentAsync(TaxExemptionDto exemption, CancellationToken ct)
    {
        var documentPublicId = exemption.DocumentPublicId ?? Guid.Empty;
        var filePublicId = exemption.FilePublicId
                           ?? await (from d in db.Set<ClientDocument>().AsNoTracking()
                                     join f in db.Set<StoredFile>().AsNoTracking() on d.StoredFileId equals f.StoredFileId
                                     where d.PublicId == documentPublicId
                                     select f.PublicId).FirstOrDefaultAsync(ct);
        return new ClientTaxExemptionCaptureDto(exemption, documentPublicId, filePublicId);
    }
}
