using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Tenancy;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>2026-10-22 — El portal se viste con la marca de la compañía de la sesión: los colores (el JSON de Ajustes → Marca) y qué logos tiene; el archivo de un logo solo si existe.</summary>
public sealed class PortalBrandTests
{
    private static Task<WmsFixture> CreateAsync() => WmsFixture.CreateAsync(s => { s.AddSingleton<BrandLogoService>(); s.AddSingleton<PortalBrandService>(); });

    private const string Json = "{\"preset\":\"marino\"}";

    private static async Task SeedAsync(WmsFixture f, string? json, params string[] logoSlots)
    {
        var t = await f.Db.Tenants.FindAsync(WmsFixture.TenantId) ?? f.Db.Tenants.Add(new Tenant { TenantId = WmsFixture.TenantId, IsActive = true }).Entity;
        t.Name = "Advance Logistics";
        t.BrandingJson = json;
        foreach (var slot in logoSlots)
            f.Db.TenantBrandLogos.Add(new TenantBrandLogo { TenantId = WmsFixture.TenantId, Slot = slot, ContentType = "image/png", SizeBytes = 3, ContentHash = "abc", Content = [1, 2, 3], IsActive = true });
        await f.Db.SaveChangesAsync();
    }

    [Fact]
    public async Task Gives_the_colors_and_the_logo_slots_of_the_session_company()
    {
        await using var f = await CreateAsync();
        await SeedAsync(f, Json, BrandLogoSlots.Lockup, BrandLogoSlots.MarkInverted);

        var dto = await f.Get<PortalBrandService>().GetAsync(default);

        Assert.Equal(Json, dto.BrandingJson);
        Assert.Equal(new[] { BrandLogoSlots.Lockup, BrandLogoSlots.MarkInverted }, dto.LogoSlots);
    }

    [Fact]
    public async Task A_company_without_brand_gives_null_colors_and_no_logos()
    {
        await using var f = await CreateAsync();
        await SeedAsync(f, "  ");

        var dto = await f.Get<PortalBrandService>().GetAsync(default);

        Assert.Null(dto.BrandingJson);
        Assert.Empty(dto.LogoSlots);
    }

    [Fact]
    public async Task The_logo_file_comes_only_when_the_slot_has_one()
    {
        await using var f = await CreateAsync();
        await SeedAsync(f, null, BrandLogoSlots.Lockup);
        var svc = f.Get<PortalBrandService>();

        var file = await svc.LogoAsync(BrandLogoSlots.Lockup, default);
        Assert.Equal("image/png", file.ContentType);
        Assert.Equal(new byte[] { 1, 2, 3 }, file.Content);
        await Assert.ThrowsAsync<NotFoundException>(() => svc.LogoAsync(BrandLogoSlots.Mark, default));
        await Assert.ThrowsAsync<NotFoundException>(() => svc.LogoAsync("no-existe", default));
    }
}
