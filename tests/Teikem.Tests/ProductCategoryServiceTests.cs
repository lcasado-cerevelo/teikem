using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 6 (P2; maestro L549 y L553: la categoría filtra Inventario, Kárdex y Conteo) — pruebas de SERVICIO de las
/// categorías jerárquicas sobre InMemory (WmsFixture): nombre único por nivel (409), sin ciclos (400), padre activo (400) y
/// baja vigilada por productos y subcategorías activas (409).
/// </summary>
public sealed class ProductCategoryServiceTests
{
    private static Task<WmsFixture> CreateAsync() => WmsFixture.CreateAsync(s => s.AddSingleton<ProductCategoryService>());

    [Fact]
    public async Task Name_is_unique_per_level_and_moving_under_a_descendant_is_a_cycle()
    {
        await using var f = await CreateAsync();
        var svc = f.Get<ProductCategoryService>();
        var root = await svc.CreateAsync(new ProductCategoryRequest("Farmacia"), default);
        var child = await svc.CreateAsync(new ProductCategoryRequest("Glucómetros", root.Id), default);
        Assert.Equal(root.Id, child.ParentId);

        var dup = await Assert.ThrowsAsync<ConflictException>(() => svc.CreateAsync(new ProductCategoryRequest("Glucómetros", root.Id), default));
        Assert.Equal(ProductRules.CategoryNameTaken, dup.Message);
        f.Db.ChangeTracker.Clear();
        var sameNameAtRoot = await svc.CreateAsync(new ProductCategoryRequest("Glucómetros"), default);   // otro nivel: válido
        Assert.Null(sameNameAtRoot.ParentId);

        var cycle = await Assert.ThrowsAsync<ValidationException>(() => svc.UpdateAsync(root.Id, new ProductCategoryPatchRequest(ParentId: child.Id), default));
        Assert.Equal(ProductRules.CategoryCycle, Assert.Single(cycle.Errors!["parentId"]));
        f.Db.ChangeTracker.Clear();
        Assert.Null((await svc.GetAsync(root.Id, default)).ParentId);
    }

    [Fact]
    public async Task Deactivation_is_blocked_by_active_products_and_children_and_inactive_parents_reject_children()
    {
        await using var f = await CreateAsync();
        var svc = f.Get<ProductCategoryService>();
        var root = await svc.CreateAsync(new ProductCategoryRequest("Farmacia"), default);
        var child = await svc.CreateAsync(new ProductCategoryRequest("Glucómetros", root.Id), default);
        var product = await f.AddProductAsync("PN");
        (await f.Db.Set<Product>().SingleAsync(p => p.ProductId == product.ProductId)).ProductCategoryId = child.Id;
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();

        var withChild = await Assert.ThrowsAsync<ConflictException>(() => svc.DeactivateAsync(root.Id, default));
        Assert.Equal(ProductRules.CategoryHasChildren, withChild.Message);
        f.Db.ChangeTracker.Clear();
        var withProduct = await Assert.ThrowsAsync<ConflictException>(() => svc.DeactivateAsync(child.Id, default));
        Assert.Equal(ProductRules.CategoryHasProducts, withProduct.Message);
        f.Db.ChangeTracker.Clear();

        (await f.Db.Set<Product>().SingleAsync(p => p.ProductId == product.ProductId)).IsActive = false;
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        Assert.False((await svc.DeactivateAsync(child.Id, default)).IsActive);
        f.Db.ChangeTracker.Clear();
        Assert.False((await svc.DeactivateAsync(root.Id, default)).IsActive);
        f.Db.ChangeTracker.Clear();

        // Padre inactivo: no admite hijas nuevas, ni movidas, ni reactivadas.
        var create = await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(new ProductCategoryRequest("Tiras", root.Id), default));
        Assert.Equal(ProductRules.CategoryParentInactive, Assert.Single(create.Errors!["parentId"]));
        var other = await svc.CreateAsync(new ProductCategoryRequest("Lancetas"), default);
        var move = await Assert.ThrowsAsync<ValidationException>(() => svc.UpdateAsync(other.Id, new ProductCategoryPatchRequest(ParentId: root.Id), default));
        Assert.Equal(ProductRules.CategoryParentInactive, Assert.Single(move.Errors!["parentId"]));
        f.Db.ChangeTracker.Clear();
        var reactivate = await Assert.ThrowsAsync<ValidationException>(() => svc.ReactivateAsync(child.Id, default));
        Assert.Equal(ProductRules.CategoryParentInactive, Assert.Single(reactivate.Errors!["parentId"]));
    }
}
