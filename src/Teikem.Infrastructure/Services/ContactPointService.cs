using System.Net.Mail;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Common;
using Teikem.Domain.Constants;
using Teikem.Domain.Contacts;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Resolver de pertenencia para asociaciones polimórficas: cada módulo registra uno por EntityType
/// (CLIENT, DRIVER, ...) que responde si el id existe en el tenant activo. El Lote 1 registra USER.
/// </summary>
public interface IOwnedEntityResolver
{
    string EntityTypeCode { get; }
    Task<bool> ExistsInTenantAsync(int id, CancellationToken ct);
}

public sealed class UserOwnedEntityResolver(TeikemDbContext db, ITenantContext tenant) : IOwnedEntityResolver
{
    public string EntityTypeCode => EntityTypes.User;
    public Task<bool> ExistsInTenantAsync(int id, CancellationToken ct)
        => db.UserTenants.AsNoTracking().AnyAsync(m => m.UserId == id && m.TenantId == tenant.TenantId, ct);
}

/// <summary>Capa C: N contactos tipados por entidad. La integridad de la asociación polimórfica vive aquí, no en FK.</summary>
public sealed class ContactPointService(TeikemDbContext db, ITenantContext tenant, ILookupCache lookups, IEnumerable<IOwnedEntityResolver> resolvers, PermissionService permissions)
{
    private static readonly Regex PhoneRegex = new(@"^\+?[0-9][0-9\s\-().]{6,24}$", RegexOptions.Compiled);

    public async Task<IReadOnlyList<ContactPointDto>> GetForOwnerAsync(string ownerEntity, int ownerId, bool includeInactive, CancellationToken ct)
    {
        // Ver los contactos de un dueño exige el permiso de lectura de esa entidad (CLIENT/CLIENT_CONTACT → clients.read, ...).
        if (PermissionCatalog.OwnerReadPermission.TryGetValue(ownerEntity, out var readPerm)) await permissions.EnsureAsync(readPerm, ct);
        var ownerEntityId = await lookups.GetIdAsync(LookupDomains.EntityType, ownerEntity, ct);
        var q = db.ContactPoints.AsNoTracking().Include(c => c.ContactType).Include(c => c.OwnerEntity)
            .Where(c => c.OwnerEntityLookupId == ownerEntityId && c.OwnerId == ownerId);
        if (!includeInactive) q = q.Where(c => c.IsActive);
        var list = await q.OrderByDescending(c => c.IsPrimary).ThenBy(c => c.ContactPointId).ToListAsync(ct);
        return list.Select(ToDto).ToList();
    }

    /// <summary>
    /// Agrega un contacto al dueño. Exige el permiso de escritura del módulo dueño (TRANSPORT_ORDER → orders.edit, CLIENT →
    /// clients.update, ...) ANTES de verificar que el dueño exista: sin él no hay oráculo 200/404 sobre los ids del tenant.
    /// enforceOwnerWrite=false solo para llamadores internos que ya autorizaron la operación de negocio (p. ej. la importación
    /// de órdenes guarda el teléfono de la fila en la orden que acaba de crear con orders.create).
    /// </summary>
    public async Task<ContactPointDto> AddAsync(string ownerEntity, int ownerId, ContactPointUpsertRequest req, CancellationToken ct,
        bool enforceOwnerWrite = true)
    {
        var tenantId = ((TenantContext)tenant).RequireTenantId();
        if (enforceOwnerWrite) await EnsureOwnerWriteAsync(ownerEntity, ct);
        var ownerEntityId = await lookups.GetIdAsync(LookupDomains.EntityType, ownerEntity, ct);
        await EnsureOwnerExistsAsync(ownerEntity, ownerId, ct);
        var typeId = await lookups.GetIdAsync(LookupDomains.ContactType, req.ContactType, ct);
        var value = ValidateValue(req.ContactType, req.Value);

        var cp = new ContactPoint
        {
            TenantId = tenantId, OwnerEntityLookupId = ownerEntityId, OwnerId = ownerId, ContactTypeLookupId = typeId,
            Value = value, Extension = req.Extension, Label = req.Label, IsPrimary = req.IsPrimary, IsActive = true,
        };
        db.ContactPoints.Add(cp);
        if (req.IsPrimary) await ClearOtherPrimariesAsync(ownerEntityId, ownerId, typeId, cp, ct);
        else if (!await db.ContactPoints.AnyAsync(c => c.OwnerEntityLookupId == ownerEntityId && c.OwnerId == ownerId && c.ContactTypeLookupId == typeId && c.IsActive, ct))
            cp.IsPrimary = true; // el primero de su tipo es principal
        await db.SaveChangesAsync(ct);
        await db.Entry(cp).Reference(c => c.ContactType).LoadAsync(ct);
        await db.Entry(cp).Reference(c => c.OwnerEntity).LoadAsync(ct);
        return ToDto(cp);
    }

    public async Task<ContactPointDto> UpdateAsync(int id, ContactPointUpsertRequest req, CancellationToken ct)
    {
        var cp = await db.ContactPoints.Include(c => c.ContactType).Include(c => c.OwnerEntity).FirstOrDefaultAsync(c => c.ContactPointId == id, ct)
                 ?? throw new NotFoundException("Contacto", id);
        await EnsureOwnerWriteAsync(cp.OwnerEntity!.InternalCode, ct);
        var typeId = await lookups.GetIdAsync(LookupDomains.ContactType, req.ContactType, ct);
        cp.ContactTypeLookupId = typeId;
        cp.Value = ValidateValue(req.ContactType, req.Value);
        cp.Extension = req.Extension;
        cp.Label = req.Label;
        if (req.IsPrimary && !cp.IsPrimary) await ClearOtherPrimariesAsync(cp.OwnerEntityLookupId, cp.OwnerId, typeId, cp, ct);
        cp.IsPrimary = req.IsPrimary;
        await db.SaveChangesAsync(ct);
        await db.Entry(cp).Reference(c => c.ContactType).LoadAsync(ct);
        return ToDto(cp);
    }

    public async Task DeactivateAsync(int id, CancellationToken ct)
    {
        var cp = await db.ContactPoints.Include(c => c.OwnerEntity).FirstOrDefaultAsync(c => c.ContactPointId == id, ct)
                 ?? throw new NotFoundException("Contacto", id);
        await EnsureOwnerWriteAsync(cp.OwnerEntity!.InternalCode, ct);
        cp.IsActive = false;
        cp.IsPrimary = false;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Permiso de escritura del módulo dueño (sin entrada en el catálogo, hoy USER, basta contacts.manage).</summary>
    private async Task EnsureOwnerWriteAsync(string ownerEntity, CancellationToken ct)
    {
        if (PermissionCatalog.OwnerWritePermission.TryGetValue(ownerEntity, out var writePerm)) await permissions.EnsureAsync(writePerm, ct);
    }

    private async Task EnsureOwnerExistsAsync(string ownerEntity, int ownerId, CancellationToken ct)
    {
        var resolver = resolvers.FirstOrDefault(r => r.EntityTypeCode.Equals(ownerEntity, StringComparison.OrdinalIgnoreCase));
        if (resolver is null)
            throw new ValidationException("ownerEntity", $"No hay resolver de pertenencia para '{ownerEntity}' (el módulo dueño aún no está construido).");
        if (!await resolver.ExistsInTenantAsync(ownerId, ct))
            throw new NotFoundException(ownerEntity, ownerId);
    }

    private async Task ClearOtherPrimariesAsync(int ownerEntityId, int ownerId, int typeId, ContactPoint keep, CancellationToken ct)
    {
        var others = await db.ContactPoints
            .Where(c => c.OwnerEntityLookupId == ownerEntityId && c.OwnerId == ownerId && c.ContactTypeLookupId == typeId && c.IsPrimary && c != keep)
            .ToListAsync(ct);
        foreach (var o in others) o.IsPrimary = false;
    }

    public static string ValidateValue(string contactType, string value)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ValidationException("value", "El valor es obligatorio.");
        var v = value.Trim();
        switch (contactType.ToUpperInvariant())
        {
            case "EMAIL":
                if (!MailAddress.TryCreate(v, out var addr) || addr.Address != v || !v.Contains('.')) throw new ValidationException("value", "Correo inválido.");
                return v.ToLowerInvariant();
            case "PHONE": case "MOBILE": case "FAX": case "WHATSAPP":
                if (!PhoneRegex.IsMatch(v)) throw new ValidationException("value", "Teléfono inválido.");
                return v;
            default:
                return v;
        }
    }

    private ContactPointDto ToDto(ContactPoint c) => new(c.ContactPointId, c.OwnerEntity?.InternalCode ?? "", c.OwnerId,
        c.ContactType?.InternalCode ?? "", MultilingualText.Resolve(c.ContactType?.LabelJson, tenant.Lang), c.Value, c.Extension, c.Label, c.IsPrimary, c.IsActive);
}
