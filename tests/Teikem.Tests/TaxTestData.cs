using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Catalogs;
using Teikem.Domain.Clients;
using Teikem.Domain.Tax;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Services;

namespace Teikem.Tests;

/// <summary>
/// Lote 248 — siembra de exenciones para las pruebas de impuestos. Desde este lote una exención nueva nace de un documento del cliente «de impuestos», así que:
/// - <see cref="SeedLegacyExemptionAsync"/>: una exención VIEJA sin documento («falta el documento»), insertada directo en el DbContext sin las reglas nuevas (admite
///   «hasta» pasado o NULL): es lo que hay en producción y debe calcular exactamente igual que antes.
/// - <see cref="SeedTaxDocumentAsync"/>: un documento del cliente de un tipo «de impuestos» (crea el tipo en el catálogo si falta).
/// - <see cref="SeedDocumentExemptionAsync"/>: documento + exención por el servicio (el camino nuevo).
/// Sirve para InMemory (WmsFixture) y para las bases desechables de SQL Server (sin llaves foráneas: el archivo del documento no hace falta).
/// </summary>
internal static class TaxTestData
{
    public const string TaxDocumentType = ClientDocumentCodes.IvuExemptionTotal;
    public const string PlainDocumentType = ClientDocumentCodes.Other;

    public static async Task<int> SeedLegacyExemptionAsync(TeikemDbContext db, int tenantId, int clientId, string exemptionType, string certificate, DateOnly from,
        DateOnly? to, string scope = TaxExemptionScopes.All, params string[] components)
    {
        var e = new ClientTaxExemption
        {
            PublicId = Guid.NewGuid(), TenantId = tenantId, ClientId = clientId, ExemptionType = exemptionType, CertificateNumber = certificate, ValidFrom = from, ValidTo = to,
            IsActive = true, Scope = scope, ComponentCodes = components.Length == 0 ? null : string.Join(',', components.Select(c => c.ToUpperInvariant()).OrderBy(c => c, StringComparer.Ordinal)),
            CreatedAtUtc = DateTime.UtcNow,
        };
        db.Set<ClientTaxExemption>().Add(e);
        await db.SaveChangesAsync();
        db.Entry(e).State = EntityState.Detached;
        return e.ClientTaxExemptionId;
    }

    /// <summary>El tipo de documento (global) con sus marcas; lo crea si no existe. isTax/requiresValidTo se escriben en ExtraJson.</summary>
    public static async Task<int> EnsureDocumentTypeAsync(TeikemDbContext db, string code = TaxDocumentType, bool isTax = true, bool requiresValidTo = true)
    {
        var json = $"{{\"requiresValidTo\":{(requiresValidTo ? "true" : "false")},\"isTax\":{(isTax ? "true" : "false")}}}";
        var row = await db.Set<LookupCode>().AsTracking().FirstOrDefaultAsync(l => l.Entity == ClientDocumentCodes.TypeDomain && l.InternalCode == code && l.TenantId == null);
        if (row is not null)
        {
            if (row.ExtraJson != json) { row.ExtraJson = json; await db.SaveChangesAsync(); }
            db.Entry(row).State = EntityState.Detached;
            return row.LookupCodeId;
        }
        var l = new LookupCode { Entity = ClientDocumentCodes.TypeDomain, InternalCode = code, LabelJson = $"{{\"es\":\"Certificado {code}\",\"en\":\"Certificate {code}\"}}", ExtraJson = json, IsActive = true };
        if (db.Database.IsInMemory()) l.LookupCodeId = (await db.Set<LookupCode>().MaxAsync(x => (int?)x.LookupCodeId) ?? 0) + 1;
        db.Set<LookupCode>().Add(l);
        await db.SaveChangesAsync();
        db.Entry(l).State = EntityState.Detached;
        return l.LookupCodeId;
    }

    /// <summary>Un documento del cliente (activo salvo isActive: false). StoredFileId ficticio: ni InMemory ni la base desechable tienen la llave foránea.</summary>
    public static async Task<ClientDocument> SeedTaxDocumentAsync(TeikemDbContext db, int tenantId, int clientId, string? number, DateOnly? from, DateOnly? to,
        string typeCode = TaxDocumentType, bool isTax = true, bool isActive = true)
    {
        var typeId = await EnsureDocumentTypeAsync(db, typeCode, isTax);
        var d = new ClientDocument
        {
            PublicId = Guid.NewGuid(), TenantId = tenantId, ClientId = clientId, StoredFileId = 1, DocumentTypeLookupId = typeId, DocumentNumber = number, ValidFrom = from,
            ValidTo = to, IsActive = isActive, CreatedAtUtc = DateTime.UtcNow,
        };
        db.Set<ClientDocument>().Add(d);
        await db.SaveChangesAsync();
        db.Entry(d).State = EntityState.Detached;
        return d;
    }

    /// <summary>Documento «de impuestos» + exención por el servicio (el número y las fechas salen del documento).</summary>
    public static async Task<TaxExemptionDto> SeedDocumentExemptionAsync(TaxService tax, TeikemDbContext db, int tenantId, int clientId, string certificate, DateOnly from,
        DateOnly to, string? scope = null, params string[] components)
    {
        var doc = await SeedTaxDocumentAsync(db, tenantId, clientId, certificate, from, to);
        return await tax.CreateExemptionAsync(new TaxExemptionRequest(clientId, null, null, null, null, null, scope, components.Length == 0 ? null : components, doc.PublicId), default);
    }
}
