using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Teikem.Domain.Constants;
using Teikem.Domain.Files;
using Teikem.Domain.Orders;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Files;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>El comprobante de entrega como dueño de archivos (firma y fotos): existe en la compañía si está activo.</summary>
public sealed class DeliveryProofOwnedEntityResolver(TeikemDbContext db) : IOwnedEntityResolver
{
    public string EntityTypeCode => DeliveryProofCodes.EntityType;
    public Task<bool> ExistsInTenantAsync(int id, CancellationToken ct) => db.Set<DeliveryProof>().AsNoTracking().AnyAsync(p => p.DeliveryProofId == id && p.IsActive, ct);
}

/// <summary>
/// Lote 127 — el almacén de archivos de Teikem (firma del que recibió, fotos de la entrega o de un daño, documentos). Guarda la referencia en la base (StoredFile) y el contenido cifrado en el
/// almacén (IFileStore). Un archivo pertenece a una entidad (patrón de ContactPoint): ver sus archivos exige el permiso de lectura del dueño y subir o quitar el de escritura (para el comprobante de
/// entrega: orders.view / orders.deliver). El tipo real se comprueba por el contenido (no por lo que diga el cliente); tope de tamaño configurable (Files:MaxMegabytes, 10 por omisión).
/// Nunca se sirve con un enlace al disco: siempre por el API.
/// </summary>
public sealed class StoredFileService(TeikemDbContext db, ITenantContext tenant, ILookupCache lookups, IFileStore store, IOptions<FileStoreOptions> options, PermissionService permissions,
    IEnumerable<IOwnedEntityResolver> resolvers)
{
    /// <summary>Permisos del dueño que no están en el catálogo general: el comprobante de entrega se ve con orders.view y se le adjunta con orders.deliver.</summary>
    private static readonly Dictionary<string, (string Read, string Write)> ExtraOwnerPermissions = new(StringComparer.OrdinalIgnoreCase)
    {
        [DeliveryProofCodes.EntityType] = (PermissionCatalog.OrdersView, PermissionCatalog.OrdersDeliver),
    };

    public int MaxBytes => Math.Max(1, options.Value.MaxMegabytes) * 1024 * 1024;

    /// <summary>Adjunta un archivo al dueño. Con enforceOwnerWrite=false solo para llamadores internos que ya autorizaron la operación (p. ej. el registro de la entrega).</summary>
    public async Task<StoredFileDto> AttachAsync(string ownerEntity, int ownerId, string? kind, string? fileName, string? claimedContentType, ReadOnlyMemory<byte> content, CancellationToken ct,
        bool enforceOwnerWrite = true)
    {
        if (enforceOwnerWrite) await EnsureOwnerAsync(ownerEntity, write: true, ct);
        if (!store.IsConfigured) throw new StatusRuleException(StoredFileRules.NotConfigured);
        var k = StoredFileRules.NormalizeKind(kind);
        if (!StoredFileRules.IsKind(k)) throw new ValidationException("kind", StoredFileRules.KindInvalid);
        if (content.Length == 0) throw new ValidationException("file", StoredFileRules.FileRequired);
        if (content.Length > MaxBytes) throw new ValidationException("file", StoredFileRules.TooLarge(Math.Max(1, options.Value.MaxMegabytes)));
        var real = StoredFileRules.SniffContentType(content.Span) ?? throw new ValidationException("file", StoredFileRules.TypeNotAllowed);
        if (!string.IsNullOrWhiteSpace(claimedContentType) && StoredFileRules.AllowedTypes.ContainsKey(claimedContentType.Trim()) && !string.Equals(claimedContentType.Trim(), real, StringComparison.OrdinalIgnoreCase))
            throw new ValidationException("file", StoredFileRules.ContentMismatch);
        var ownerEntityId = await lookups.GetIdAsync(LookupDomains.EntityType, ownerEntity, ct);
        await EnsureOwnerExistsAsync(ownerEntity, ownerId, ct);

        var tenantId = T.RequireTenantId();
        var publicId = Guid.NewGuid();
        var key = await store.SaveAsync(tenantId, publicId, content, ct);
        var row = new StoredFile
        {
            PublicId = publicId, TenantId = tenantId, OwnerEntityLookupId = ownerEntityId, OwnerId = ownerId, Kind = k, FileName = StoredFileRules.SafeFileName(fileName, real), ContentType = real,
            SizeBytes = content.Length, Sha256 = StoredFileRules.Sha256Hex(content.Span), StorageKey = key, IsEncrypted = true, UploadedBy = tenant.UserId, IsActive = true, CreatedAtUtc = DateTime.UtcNow,
        };
        db.Set<StoredFile>().Add(row);
        await db.SaveChangesAsync(ct);
        return await ToDtoAsync(row, ct);
    }

    /// <summary>Los archivos vigentes de un dueño (exige el permiso de lectura del dueño).</summary>
    public async Task<IReadOnlyList<StoredFileDto>> ListAsync(string ownerEntity, int ownerId, CancellationToken ct, bool enforceOwnerRead = true)
    {
        if (enforceOwnerRead) await EnsureOwnerAsync(ownerEntity, write: false, ct);
        var ownerEntityId = await lookups.TryGetIdAsync(LookupDomains.EntityType, ownerEntity, ct);
        if (ownerEntityId is null) return Array.Empty<StoredFileDto>();
        var rows = await db.Set<StoredFile>().AsNoTracking().Where(f => f.OwnerEntityLookupId == ownerEntityId && f.OwnerId == ownerId && f.IsActive).OrderBy(f => f.StoredFileId).ToListAsync(ct);
        var names = await NamesAsync(rows.Select(r => r.UploadedBy), ct);
        return rows.Select(r => ToDto(r, names)).ToList();
    }

    /// <summary>El contenido en claro de un archivo (exige el permiso de lectura de su dueño).</summary>
    public async Task<StoredFileContent> GetContentAsync(Guid publicId, CancellationToken ct)
    {
        var row = await db.Set<StoredFile>().AsNoTracking().Include(f => f.OwnerEntity).FirstOrDefaultAsync(f => f.PublicId == publicId && f.IsActive, ct) ?? throw new NotFoundException("Archivo", publicId);
        await EnsureOwnerAsync(row.OwnerEntity!.InternalCode, write: false, ct);
        var bytes = await store.ReadAsync(row.TenantId, row.StorageKey, ct);
        return new StoredFileContent(row.FileName, row.ContentType, bytes);
    }

    /// <summary>Quita el archivo en lógico (exige el permiso de escritura de su dueño). El contenido no se borra del almacén.</summary>
    public async Task RemoveAsync(Guid publicId, CancellationToken ct)
    {
        var row = await db.Set<StoredFile>().Include(f => f.OwnerEntity).FirstOrDefaultAsync(f => f.PublicId == publicId && f.IsActive, ct) ?? throw new NotFoundException("Archivo", publicId);
        await EnsureOwnerAsync(row.OwnerEntity!.InternalCode, write: true, ct);
        row.IsActive = false;
        await db.SaveChangesAsync(ct);
    }

    private TenantContext T => (TenantContext)tenant;

    private async Task EnsureOwnerAsync(string ownerEntity, bool write, CancellationToken ct)
    {
        string? perm = null;
        if (ExtraOwnerPermissions.TryGetValue(ownerEntity, out var extra)) perm = write ? extra.Write : extra.Read;
        else if (write && PermissionCatalog.OwnerWritePermission.TryGetValue(ownerEntity, out var w)) perm = w;
        else if (!write && PermissionCatalog.OwnerReadPermission.TryGetValue(ownerEntity, out var r)) perm = r;
        if (perm is null) throw new ForbiddenException($"No hay permiso definido para los archivos de '{ownerEntity}'.");
        await permissions.EnsureAsync(perm, ct);
    }

    private async Task EnsureOwnerExistsAsync(string ownerEntity, int ownerId, CancellationToken ct)
    {
        var resolver = resolvers.FirstOrDefault(r => r.EntityTypeCode.Equals(ownerEntity, StringComparison.OrdinalIgnoreCase))
                       ?? throw new ValidationException("ownerEntity", $"No hay resolver de pertenencia para '{ownerEntity}' (el módulo dueño aún no está construido).");
        if (!await resolver.ExistsInTenantAsync(ownerId, ct)) throw new NotFoundException(ownerEntity, ownerId);
    }

    private async Task<Dictionary<int, string>> NamesAsync(IEnumerable<int?> ids, CancellationToken ct)
    {
        var list = ids.Where(i => i != null).Select(i => i!.Value).Distinct().ToList();
        if (list.Count == 0) return new Dictionary<int, string>();
        return await db.Users.AsNoTracking().Where(u => list.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName ?? u.Email ?? string.Empty, ct);
    }

    private async Task<StoredFileDto> ToDtoAsync(StoredFile row, CancellationToken ct) => ToDto(row, await NamesAsync(new[] { row.UploadedBy }, ct));

    private static StoredFileDto ToDto(StoredFile r, IReadOnlyDictionary<int, string> names)
        => new(r.PublicId, r.Kind, r.FileName, r.ContentType, r.SizeBytes, r.CreatedAtUtc, r.UploadedBy is int u ? names.GetValueOrDefault(u) : null);
}
