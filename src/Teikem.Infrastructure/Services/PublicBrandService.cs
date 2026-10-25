using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Tenancy;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// 2026-10-22 (Luis) — la marca que se ve ANTES de iniciar sesión (pantalla de entrada): el logo y el nombre de la compañía, nada más. Qué compañía:
///   1. la última con la que entró esa computadora (por login o al cambiar de compañía en el encabezado), si llega su identificador público y está activa;
///   2. si no, la que tiene como DOMINIO la dirección con la que se abrió la página;
///   3. si no, la que tiene como SUBDOMINIO la primera parte de esa dirección;
///   4. si no, y SOLO si la instalación tiene una única compañía activa, esa (Lote 219, Luis: «con una sola compañía en la instalación sale la suya»);
///   5. si no, ninguna (la pantalla usa el logo de Teikem).
/// Es anónimo: solo sale lo que ya es público en la pantalla de entrada. Una dirección o un identificador que no existe responde igual que «ninguna».
/// </summary>
public sealed class PublicBrandService(TeikemDbContext db)
{
    public const string SourceTenant = "tenant", SourceDomain = "domain", SourceSubdomain = "subdomain", SourceSingle = "single", SourceNone = "none";

    private async Task<(Tenant? Tenant, string Source)> ResolveAsync(Guid? tenantPublicId, string? host, CancellationToken ct)
    {
        var q = db.Tenants.AsNoTracking().IgnoreQueryFilters().Where(t => t.IsActive);
        if (tenantPublicId is { } id && await q.FirstOrDefaultAsync(t => t.PublicId == id, ct) is { } last) return (last, SourceTenant);
        var (domain, sub) = TenantDomainRules.Candidates(host);
        if (domain is not null && await q.FirstOrDefaultAsync(t => t.CustomDomain == domain, ct) is { } byDomain) return (byDomain, SourceDomain);
        if (sub is not null && await q.FirstOrDefaultAsync(t => t.Subdomain == sub, ct) is { } bySub) return (bySub, SourceSubdomain);
        // una instalación de una sola compañía (el caso de cada cliente que compra Teikem para sí): su marca sale sin que nadie la haya usado antes ni haya dominio
        var two = await q.OrderBy(t => t.TenantId).Take(2).ToListAsync(ct);
        return two.Count == 1 ? (two[0], SourceSingle) : (null, SourceNone);
    }

    public async Task<PublicBrandDto> GetAsync(Guid? tenantPublicId, string? host, CancellationToken ct)
    {
        var (t, source) = await ResolveAsync(tenantPublicId, host, ct);
        if (t is null) return new PublicBrandDto(SourceNone, null, null, Array.Empty<PublicBrandLogoDto>());
        var rows = await db.TenantBrandLogos.AsNoTracking().IgnoreQueryFilters().Where(l => l.TenantId == t.TenantId && l.IsActive)
            .Select(l => new { l.Slot, l.UpdatedAtUtc, l.CreatedAtUtc }).ToListAsync(ct);
        var logos = BrandLogoSlots.All.SelectMany(s => rows.Where(r => r.Slot == s)).Select(r => new PublicBrandLogoDto(r.Slot, r.UpdatedAtUtc ?? r.CreatedAtUtc)).ToList();
        return new PublicBrandDto(source, t.PublicId, t.Name, logos);
    }

    /// <summary>El archivo de una ranura de la compañía resuelta; 404 si la ranura no existe, no hay compañía o no tiene logo ahí.</summary>
    public async Task<BrandLogoFile> GetLogoAsync(string slot, Guid? tenantPublicId, string? host, CancellationToken ct)
    {
        if (!BrandLogoSlots.IsValid(slot)) throw new NotFoundException(BrandLogoRules.SlotNotFoundWhat, slot, feminine: true);
        var (t, _) = await ResolveAsync(tenantPublicId, host, ct);
        var l = t is null ? null : await db.TenantBrandLogos.AsNoTracking().IgnoreQueryFilters().FirstOrDefaultAsync(x => x.TenantId == t.TenantId && x.Slot == slot && x.IsActive, ct);
        if (l is null) throw new NotFoundException("Logo", slot);
        return new BrandLogoFile(l.Content, l.ContentType, l.ContentHash, l.UpdatedAtUtc ?? l.CreatedAtUtc);
    }
}
