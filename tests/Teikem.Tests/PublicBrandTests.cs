using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Tenancy;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>2026-10-22 — logo de la pantalla de entrada: la última compañía usada, si no el dominio, si no el subdominio, si no ninguna.</summary>
public class PublicBrandTests
{
    private static async Task<(WmsFixture F, PublicBrandService Svc, Tenant A, Tenant B)> SeedAsync()
    {
        var f = await WmsFixture.CreateAsync(s => s.AddScoped<PublicBrandService>());
        var a = await f.Db.Tenants.IgnoreQueryFilters().FirstAsync(t => t.TenantId == WmsFixture.TenantId);
        var b = await f.Db.Tenants.IgnoreQueryFilters().FirstAsync(t => t.TenantId == WmsFixture.OtherTenantId);
        a.CustomDomain = "logistica.acme.com"; a.Subdomain = "acme";
        b.Subdomain = "beta";
        foreach (var (t, slot) in new[] { (a, "lockup"), (a, "mark"), (b, "lockup") })
            f.Db.TenantBrandLogos.Add(new TenantBrandLogo { TenantId = t.TenantId, Slot = slot, ContentType = "image/png", SizeBytes = 3, ContentHash = new string('a', 64), Content = new byte[] { 1, 2, 3 }, IsActive = true });
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        return (f, f.Get<PublicBrandService>(), a, b);
    }

    [Fact]
    public async Task La_ultima_compania_usada_manda_sobre_el_dominio()
    {
        var (_, svc, a, b) = await SeedAsync();
        var r = await svc.GetAsync(b.PublicId, "logistica.acme.com", default);
        Assert.Equal(("tenant", b.PublicId, b.Name), (r.Source, r.TenantPublicId, r.Name));
        Assert.Equal(new[] { "lockup" }, r.Logos.Select(l => l.Slot));
    }

    [Fact]
    public async Task Sin_ultima_compania_manda_el_dominio_y_luego_el_subdominio()
    {
        var (_, svc, a, b) = await SeedAsync();
        var byDomain = await svc.GetAsync(null, "Logistica.Acme.com", default);
        Assert.Equal(("domain", a.PublicId), (byDomain.Source, byDomain.TenantPublicId));
        Assert.Equal(new[] { "lockup", "mark" }, byDomain.Logos.Select(l => l.Slot));

        var bySub = await svc.GetAsync(null, "beta.teikem.com", default);
        Assert.Equal(("subdomain", b.PublicId), (bySub.Source, bySub.TenantPublicId));
    }

    [Fact]
    public async Task Un_identificador_que_no_existe_cae_al_dominio_y_sin_nada_no_hay_marca()
    {
        var (_, svc, a, _) = await SeedAsync();
        var fallback = await svc.GetAsync(Guid.NewGuid(), "acme.teikem.com", default);
        Assert.Equal(("subdomain", a.PublicId), (fallback.Source, fallback.TenantPublicId));

        var none = await svc.GetAsync(Guid.NewGuid(), "localhost", default);
        Assert.Equal(("none", null, null), (none.Source, none.TenantPublicId, none.Name));
        Assert.Empty(none.Logos);
    }

    [Fact]
    public async Task Una_compania_inactiva_no_muestra_su_marca()
    {
        var (f, svc, a, _) = await SeedAsync();
        var t = await f.Db.Tenants.IgnoreQueryFilters().FirstAsync(x => x.TenantId == a.TenantId);
        t.IsActive = false;
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        Assert.Equal("none", (await svc.GetAsync(a.PublicId, "logistica.acme.com", default)).Source);
    }

    [Fact]
    public async Task El_archivo_sale_de_la_compania_resuelta_y_sin_logo_es_404()
    {
        var (_, svc, a, b) = await SeedAsync();
        var file = await svc.GetLogoAsync("lockup", a.PublicId, null, default);
        Assert.Equal(("image/png", 3), (file.ContentType, file.Content.Length));
        await Assert.ThrowsAsync<NotFoundException>(() => svc.GetLogoAsync("mark", b.PublicId, null, default));
        await Assert.ThrowsAsync<NotFoundException>(() => svc.GetLogoAsync("no-existe", a.PublicId, null, default));
    }
}
