using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// 2026-10-11 — filtro «Dueño» (varios) de GET /products: OwnerClientPublicIds (clientes) + IncludeOwn («Propio» = sin cliente).
/// Sin ninguno de los dos no se filtra por dueño. Las opciones del filtro salen de InventoryReadService.OwnersAsync (Propio + clientes
/// dueños de algún producto), que ya cubre KardexSummaryTests.
/// </summary>
public sealed class ProductOwnerFilterTests
{
    private static Task<WmsFixture> CreateAsync() => WmsFixture.CreateAsync(s =>
    {
        s.AddSingleton<ProductService>();
        s.AddSingleton<Teikem.Infrastructure.Abstractions.ITenantClock>(Teikem.Infrastructure.Abstractions.TenantClock.Default);
    });

    [Fact]
    public async Task List_filters_by_several_owners_and_own()
    {
        await using var f = await CreateAsync();
        var a = await f.AddClientAsync("A");
        var b = await f.AddClientAsync("B");
        await f.AddProductAsync("PA", ownerClientId: a.ClientId);
        await f.AddProductAsync("PB", ownerClientId: b.ClientId);
        await f.AddProductAsync("POWN");

        var products = f.Get<ProductService>();
        async Task<string[]> Skus(ProductListQuery q) => (await products.ListAsync(q, InventoryScope.Any, default)).Items.Select(i => i.Sku).OrderBy(x => x).ToArray();

        // Sin filtro: todos.
        Assert.Equal(new[] { "PA", "PB", "POWN" }, await Skus(new ProductListQuery()));
        // Un cliente, dos clientes, solo propio, propio + cliente.
        Assert.Equal(new[] { "PA" }, await Skus(new ProductListQuery(OwnerClientPublicIds: new[] { a.PublicId })));
        Assert.Equal(new[] { "PA", "PB" }, await Skus(new ProductListQuery(OwnerClientPublicIds: new[] { a.PublicId, b.PublicId })));
        Assert.Equal(new[] { "POWN" }, await Skus(new ProductListQuery(IncludeOwn: true)));
        Assert.Equal(new[] { "PB", "POWN" }, await Skus(new ProductListQuery(OwnerClientPublicIds: new[] { b.PublicId }, IncludeOwn: true)));
        // Se combina con los demás filtros.
        Assert.Equal(new[] { "PA" }, await Skus(new ProductListQuery(Name: "producto pa", OwnerClientPublicIds: new[] { a.PublicId, b.PublicId })));

        // Un cliente que no existe → 404, como en el Kárdex.
        var ex = await Assert.ThrowsAsync<NotFoundException>(() =>
            products.ListAsync(new ProductListQuery(OwnerClientPublicIds: new[] { Guid.NewGuid() }), InventoryScope.Any, default));
        Assert.Equal("Cliente no encontrado.", ex.Message);
    }
}
