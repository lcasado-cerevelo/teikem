using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Tenancy;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>Un logo listo para servir: el archivo, su tipo real y los validadores de caché HTTP.</summary>
public sealed record BrandLogoFile(byte[] Content, string ContentType, string ETag, DateTime LastModifiedUtc);

/// <summary>
/// Logos de la marca de la compañía (Ajustes → Marca): cuatro ranuras (BrandLogoSlots), almacenadas en dbo.TenantBrandLogo.
/// Escribir exige admin.tenant (lo pone el controlador); leer solo sesión (es la marca de la interfaz). El TenantId sale del
/// contexto y el filtro global aísla las compañías. El contenido se valida por sus bytes (BrandLogoRules): formato real, tamaño
/// y, en SVG, que no traiga contenido activo ni referencias externas. Quitar es soft delete y libera el binario.
/// </summary>
public sealed class BrandLogoService(TeikemDbContext db, ITenantContext tenant)
{
    private static string Slot(string slot) =>
        BrandLogoSlots.IsValid(slot) ? slot : throw new NotFoundException(BrandLogoRules.SlotNotFoundWhat, slot, feminine: true);

    private static DateTime StampOf(TenantBrandLogo l) => l.UpdatedAtUtc ?? l.CreatedAtUtc;
    private static BrandLogoDto ToDto(TenantBrandLogo l) => new(l.Slot, l.ContentType, l.SizeBytes, l.ContentHash, StampOf(l));

    /// <summary>Los logos activos de la compañía (sin el binario), en el orden de las ranuras.</summary>
    public async Task<IReadOnlyList<BrandLogoDto>> ListAsync(CancellationToken ct)
    {
        var rows = await db.TenantBrandLogos.AsNoTracking().Where(l => l.IsActive)
            .Select(l => new { l.Slot, l.ContentType, l.SizeBytes, l.ContentHash, l.UpdatedAtUtc, l.CreatedAtUtc }).ToListAsync(ct);
        return BrandLogoSlots.All.SelectMany(s => rows.Where(r => r.Slot == s))
            .Select(r => new BrandLogoDto(r.Slot, r.ContentType, r.SizeBytes, r.ContentHash, r.UpdatedAtUtc ?? r.CreatedAtUtc)).ToList();
    }

    /// <summary>El archivo de una ranura; 404 si la ranura no existe o la compañía no tiene logo en ella.</summary>
    public async Task<BrandLogoFile> GetAsync(string slot, CancellationToken ct)
    {
        slot = Slot(slot);
        var l = await db.TenantBrandLogos.AsNoTracking().FirstOrDefaultAsync(x => x.Slot == slot && x.IsActive, ct)
            ?? throw new NotFoundException("Logo", slot);
        return new BrandLogoFile(l.Content, l.ContentType, l.ContentHash, StampOf(l));
    }

    /// <summary>
    /// Sube o reemplaza el logo de la ranura. 400 (vacío, imagen dañada, SVG con contenido activo), 413 (más de 512 KB) y
    /// 415 (formato no admitido o distinto del tipo declarado), con el mensaje exacto de BrandLogoRules.
    /// </summary>
    public async Task<BrandLogoDto> SaveAsync(string slot, byte[] data, string? declaredContentType, CancellationToken ct)
    {
        slot = Slot(slot);
        var inspection = BrandLogoRules.Inspect(data, declaredContentType);
        if (!inspection.Ok)
        {
            var msg = inspection.Error!;
            throw inspection.StatusCode switch
            {
                413 => new PayloadTooLargeException(msg),
                415 => new UnsupportedMediaException(msg),
                _ => new ValidationException("file", msg),
            };
        }
        // Incluye una fila dada de baja: subir otro logo la reactiva (una fila por compañía y ranura).
        var row = await db.TenantBrandLogos.FirstOrDefaultAsync(x => x.Slot == slot, ct);
        if (row is null)
        {
            row = new TenantBrandLogo { TenantId = ((TenantContext)tenant).RequireTenantId(), Slot = slot };
            db.TenantBrandLogos.Add(row);
        }
        row.ContentType = inspection.ContentType!;
        row.SizeBytes = data.Length;
        row.ContentHash = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
        row.Content = data;
        row.IsActive = true;
        row.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return ToDto(row);
    }

    /// <summary>Quita el logo de la ranura (soft delete; el binario se libera). 404 si no hay logo en esa ranura.</summary>
    public async Task RemoveAsync(string slot, CancellationToken ct)
    {
        slot = Slot(slot);
        var row = await db.TenantBrandLogos.FirstOrDefaultAsync(x => x.Slot == slot && x.IsActive, ct) ?? throw new NotFoundException("Logo", slot);
        row.IsActive = false;
        row.Content = [];
        row.SizeBytes = 0;
        row.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }
}
