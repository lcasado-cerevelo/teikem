using Microsoft.EntityFrameworkCore;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>Marca de la compañía para vestir el portal: el JSON de Ajustes → Marca (colores; el portal lo interpreta igual que la web del equipo) y las ranuras que tienen logo.</summary>
public sealed record PortalBrandDto(string? BrandingJson, IReadOnlyList<string> LogoSlots);

/// <summary>
/// 2026-10-22 (pedido de Luis): el portal se ve con los colores y el logo de la compañía, los mismos de Ajustes → Marca del equipo. Los endpoints de marca del equipo piden sesión del staff
/// (el portal no entra a esas rutas), así que el portal lee la marca de SU compañía —la de la sesión— por aquí. Solo lectura.
/// </summary>
public sealed class PortalBrandService(TeikemDbContext db, ITenantContext tenant, BrandLogoService logos)
{
    public async Task<PortalBrandDto> GetAsync(CancellationToken ct)
    {
        var tenantId = tenant.TenantId ?? throw new ForbiddenException(AuthService.PortalNoAccessMessage);
        var json = await db.Tenants.IgnoreQueryFilters().AsNoTracking().Where(t => t.TenantId == tenantId).Select(t => t.BrandingJson).FirstOrDefaultAsync(ct);
        var slots = (await logos.ListAsync(ct)).Select(l => l.Slot).ToList();
        return new PortalBrandDto(string.IsNullOrWhiteSpace(json) ? null : json, slots);
    }

    /// <summary>El archivo de un logo de la compañía de la sesión (404 si la ranura no existe o no tiene logo).</summary>
    public Task<BrandLogoFile> LogoAsync(string slot, CancellationToken ct) => logos.GetAsync(slot, ct);
}
