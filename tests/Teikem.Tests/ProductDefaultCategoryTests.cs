using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence.Scripts;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// 2026-10-11 (c) — «Categoría por defecto de los productos nuevos» (Ajustes de la compañía): PUT/GET /tenant/settings (un id de categoría
/// activa de la compañía; 0 = quitar; null = sin cambio; 400 con el mensaje exacto y sin guardar nada si no existe, está inactiva o es de
/// otra compañía), una default dada de baja después se ignora sin romper el guardado del resto, isDefault en GET /product-categories y el
/// espejo en logistica-db-update.sql (columna, FK y dato de Advance Depot).
/// </summary>
public sealed class ProductDefaultCategoryTests
{
    private static Task<WmsFixture> FixtureAsync() => WmsFixture.CreateAsync(s =>
    {
        s.AddSingleton<TenantService>();
        s.AddSingleton<ProductCategoryService>();
    });

    private static TenantSettingsUpdateRequest Put(int? defaultCategory = null, string? name = null)
        => new(name, null, null, null, null, null, null, null, null, null, null, null, null, DefaultProductCategoryId: defaultCategory);

    private static async Task<int> AddCategoryAsync(WmsFixture f, string name, int tenantId = WmsFixture.TenantId, bool active = true)
    {
        using (f.AsTenant(tenantId))
        {
            var c = new ProductCategory { TenantId = tenantId, Name = name, IsActive = active };
            f.Db.Set<ProductCategory>().Add(c);
            await f.Db.SaveChangesAsync();
            f.Db.ChangeTracker.Clear();
            return c.ProductCategoryId;
        }
    }

    private static async Task<int?> StoredAsync(WmsFixture f)
    {
        f.Db.ChangeTracker.Clear();
        return await f.Db.Tenants.AsNoTracking().Where(t => t.TenantId == WmsFixture.TenantId).Select(t => t.DefaultProductCategoryId).SingleAsync();
    }

    [Fact]
    public async Task Without_a_default_nothing_is_preselected()
    {
        await using var f = await FixtureAsync();
        await AddCategoryAsync(f, "AxisCare");
        Assert.Null((await f.Get<TenantService>().GetSettingsAsync(default)).DefaultProductCategoryId);
        Assert.DoesNotContain(await f.Get<ProductCategoryService>().ListAsync(false, default), c => c.IsDefault);
    }

    [Fact]
    public async Task Saving_a_category_stores_it_and_marks_only_that_one_as_default_in_the_categories()
    {
        await using var f = await FixtureAsync();
        var axis = await AddCategoryAsync(f, "AxisCare");
        var other = await AddCategoryAsync(f, "CARTONES");

        var dto = await f.Get<TenantService>().UpdateSettingsAsync(Put(axis), default);
        Assert.Equal(axis, dto.DefaultProductCategoryId);
        Assert.Equal(axis, await StoredAsync(f));
        Assert.Equal(axis, (await f.Get<TenantService>().GetSettingsAsync(default)).DefaultProductCategoryId);

        var list = await f.Get<ProductCategoryService>().ListAsync(false, default);
        Assert.Equal(axis, Assert.Single(list, c => c.IsDefault).Id);
        Assert.False(list.Single(c => c.Id == other).IsDefault);
        Assert.True((await f.Get<ProductCategoryService>().GetAsync(axis, default)).IsDefault);

        // null = sin cambio (guardar otro ajuste no la toca); 0 = quitarla.
        await f.Get<TenantService>().UpdateSettingsAsync(Put(name: "Otra"), default);
        Assert.Equal(axis, await StoredAsync(f));
        Assert.Null((await f.Get<TenantService>().UpdateSettingsAsync(Put(0), default)).DefaultProductCategoryId);
        Assert.Null(await StoredAsync(f));
        Assert.DoesNotContain(await f.Get<ProductCategoryService>().ListAsync(false, default), c => c.IsDefault);
    }

    [Fact]
    public async Task Unknown_inactive_or_other_company_category_is_a_400_with_the_exact_message_and_nothing_is_saved()
    {
        await using var f = await FixtureAsync();
        var svc = f.Get<TenantService>();
        var axis = await AddCategoryAsync(f, "AxisCare");
        var inactive = await AddCategoryAsync(f, "Vieja", active: false);
        var foreign = await AddCategoryAsync(f, "AxisCare", WmsFixture.OtherTenantId);
        await svc.UpdateSettingsAsync(Put(axis), default);

        foreach (var bad in new[] { 9999, inactive, foreign })
        {
            var ex = await Assert.ThrowsAsync<ValidationException>(() => svc.UpdateSettingsAsync(Put(bad, name: "No se guarda"), default));
            Assert.Equal(ProductRules.DefaultCategoryUnusable, Assert.Single(ex.Errors!["defaultProductCategoryId"]));
            Assert.Equal(axis, await StoredAsync(f));                                          // la anterior sigue
            Assert.NotEqual("No se guarda", (await svc.GetSettingsAsync(default)).Name);        // y no se guardó nada más
        }
        Assert.Equal("La categoría por defecto no existe o está inactiva.", ProductRules.DefaultCategoryUnusable);
    }

    [Fact]
    public async Task A_default_that_is_deactivated_later_is_ignored_and_does_not_break_saving_the_rest()
    {
        await using var f = await FixtureAsync();
        var axis = await AddCategoryAsync(f, "AxisCare");
        await f.Get<TenantService>().UpdateSettingsAsync(Put(axis), default);

        using (f.AsTenant(WmsFixture.TenantId))
        {
            var c = await f.Db.Set<ProductCategory>().AsTracking().SingleAsync(x => x.ProductCategoryId == axis);
            c.IsActive = false;
            await f.Db.SaveChangesAsync();
            f.Db.ChangeTracker.Clear();
        }

        var svc = f.Get<TenantService>();
        Assert.Null((await svc.GetSettingsAsync(default)).DefaultProductCategoryId);          // la pantalla no preselecciona una baja
        var saved = await svc.UpdateSettingsAsync(Put(axis, name: "Sigue guardando"), default); // volver a mandar la guardada no se revalida
        Assert.Equal("Sigue guardando", saved.Name);
    }

    // ================================================================ espejo SQL

    [Fact]
    public void Update_sql_adds_the_column_fk_and_the_depot_data_in_a_new_idempotent_section_at_the_end()
    {
        var sql = File.ReadAllText(Path.Combine(DatabaseInitializer.ResolveRepoRoot(null), "Diseño", "logistica-db-update.sql")).Replace("\r\n", "\n");
        var start = sql.IndexOf("2026-10-11 (c) — Categoría por defecto de los productos NUEVOS", StringComparison.Ordinal);
        Assert.True(start > sql.IndexOf("2026-10-11 (b) — Motivo por default del despacho manual", StringComparison.Ordinal));
        var section = sql[start..];
        Assert.Contains("IF COL_LENGTH('dbo.Tenant', 'DefaultProductCategoryId') IS NULL\n    ALTER TABLE dbo.Tenant ADD DefaultProductCategoryId INT NULL;", section);
        Assert.Contains("IF OBJECT_ID('dbo.FK_Tenant_DefaultProductCategory', 'F') IS NULL", section);
        Assert.Contains("FOREIGN KEY (DefaultProductCategoryId) REFERENCES dbo.ProductCategory(ProductCategoryId);", section);
        // Advance Depot: AxisCare (junto, como la creó la migración), solo si sigue vacía y la categoría está activa; nunca pisa lo elegido
        Assert.Contains("t.Name = N'Advance Depot'", section);
        Assert.Contains("t.DefaultProductCategoryId IS NULL", section);
        Assert.Contains("REPLACE(c.Name, N' ', N'') = N'AxisCare'", section);
        Assert.DoesNotContain("DELETE", section, StringComparison.OrdinalIgnoreCase);
    }
}
