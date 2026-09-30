using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Api.Auth;
using Teikem.Api.Controllers;
using Teikem.Domain.Catalogs;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Ajuste del dueño del producto (2026-09-30) al Lote 12 — Empacar: la pantalla muestra "Predeterminado de la compañía
/// (…)" solo si la compañía tiene tipo de servicio / tipo de paquete predeterminados. No hace falta un endpoint nuevo:
/// GET /api/v1/tenant/settings ya los expone (defaultServiceType y defaultPackageType: código o null) y solo exige sesión
/// (sin permiso ni módulo), así que lo lee quien empaca (warehouse.pick). La etiqueta sale del catálogo (ServiceType,
/// PackageType) que la pantalla ya carga. Aquí se fija que el endpoint siga abierto a cualquier usuario autenticado y que
/// devuelva null sin predeterminado y el código con él.
/// </summary>
public sealed class TenantDefaultsForPackingTests
{
    [Fact]
    public void Tenant_settings_are_readable_by_any_authenticated_user()
    {
        var t = typeof(TenantController);
        Assert.Equal("api/v1/tenant", t.GetCustomAttribute<RouteAttribute>()!.Template);
        Assert.NotNull(t.GetCustomAttribute<AuthorizeAttribute>());
        Assert.Null(t.GetCustomAttribute<AllowAnonymousAttribute>());
        Assert.Empty(t.GetCustomAttributes<RequirePermissionAttribute>(inherit: true));
        Assert.Empty(t.GetCustomAttributes<RequireModuleAttribute>(inherit: true));

        var get = t.GetMethod(nameof(TenantController.Settings))!;
        Assert.Equal("settings", get.GetCustomAttribute<HttpGetAttribute>()!.Template);
        Assert.Empty(get.GetCustomAttributes<RequirePermissionAttribute>());
        Assert.Empty(get.GetCustomAttributes<RequireModuleAttribute>());
        Assert.Null(get.GetCustomAttribute<AllowAnonymousAttribute>());
        Assert.DoesNotContain(get.GetParameters(), p => string.Equals(p.Name, "tenantId", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Settings_return_the_default_service_and_package_type_codes_or_null()
    {
        await using var f = await WmsFixture.CreateAsync(s => s.AddSingleton<TenantService>());
        var all = await f.Db.LookupCodes.AsNoTracking().ToListAsync();
        var service = new LookupCode { LookupCodeId = 9001, Entity = LookupDomains.ServiceType, InternalCode = "STANDARD", LabelJson = "{\"es\":\"Estándar\"}", IsActive = true };
        var package = new LookupCode { LookupCodeId = 9002, Entity = LookupDomains.PackageType, InternalCode = "BOX", LabelJson = "{\"es\":\"Caja\"}", IsActive = true };
        f.Db.LookupCodes.AddRange(service, package);
        await f.Db.SaveChangesAsync();
        f.Lookups.Load(all.Append(service).Append(package));
        var tenants = f.Get<TenantService>();

        // Sin predeterminados: null (la pantalla oculta la opción y el campo es obligatorio).
        var none = await tenants.GetSettingsAsync(default);
        Assert.Null(none.DefaultServiceType);
        Assert.Null(none.DefaultPackageType);

        var tenant = await f.Db.Tenants.AsTracking().SingleAsync(t => t.TenantId == WmsFixture.TenantId);
        tenant.DefaultServiceTypeLookupId = service.LookupCodeId;
        tenant.DefaultPackageTypeLookupId = package.LookupCodeId;
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();

        var both = await tenants.GetSettingsAsync(default);
        Assert.Equal(("STANDARD", "BOX"), (both.DefaultServiceType, both.DefaultPackageType));

        // Otro tenant no ve los predeterminados de este (el TenantId sale del contexto).
        using (f.AsTenant(WmsFixture.OtherTenantId))
        {
            var other = await tenants.GetSettingsAsync(default);
            Assert.Null(other.DefaultServiceType);
            Assert.Null(other.DefaultPackageType);
        }
    }
}
