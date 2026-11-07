using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Teikem.Domain.Clients;
using Teikem.Domain.Constants;
using Teikem.Domain.Sales;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>Lote 221: el mundo de las pruebas de precios — servicio de órdenes, de listas y de catálogo sobre la base en memoria.</summary>
internal sealed class PriceWorld : IAsyncDisposable
{
    public WmsFixture F { get; private init; } = null!;
    public Warehouse W { get; private init; } = null!;
    public WarehouseBin Bin { get; private init; } = null!;
    public Client C1 { get; private init; } = null!;
    public Client C2 { get; private init; } = null!;
    public SalesOrderService Sales => F.Get<SalesOrderService>();
    public PriceListService Lists => F.Get<PriceListService>();
    public SalesCatalogService Catalog => F.Get<SalesCatalogService>();
    public KitService Kits => F.Get<KitService>();
    public DateOnly Today => TenantClock.Default.Today;

    public static async Task<PriceWorld> CreateAsync()
    {
        var f = await WmsFixture.CreateAsync(s =>
        {
            s.AddSingleton<ITenantClock>(TenantClock.Default);
            s.AddSingleton<TaxService>();
            s.AddSingleton<FakeBalance>();
            s.AddSingleton<Teikem.Infrastructure.Orders.IClientBalanceProvider>(sp => sp.GetRequiredService<FakeBalance>());
            s.AddSingleton<SalesOrderService>();
            s.AddSingleton<KitService>();
            // las fotos no se prueban aquí: el almacén de archivos queda sin configurar
            s.AddSingleton(sp => new StoredFileService(sp.GetRequiredService<Teikem.Infrastructure.Persistence.TeikemDbContext>(), sp.GetRequiredService<ITenantContext>(),
                sp.GetRequiredService<ILookupCache>(), null!, Options.Create(new Teikem.Infrastructure.Files.FileStoreOptions()), sp.GetRequiredService<PermissionService>(),
                Array.Empty<IOwnedEntityResolver>()));
            s.AddSingleton<PriceListService>();
            s.AddSingleton<SalesCatalogService>();
        });
        f.SetModules(ModuleKeys.SalesOrders);
        var w = await f.AddWarehouseAsync("W1");
        var bin = await f.AddBinAsync(await f.AddZoneAsync(w, "PZ", ZoneTypes.Picking), "P-01");
        return new PriceWorld { F = f, W = w, Bin = bin, C1 = await f.AddClientAsync("C1"), C2 = await f.AddClientAsync("C2") };
    }

    /// <summary>Producto propio con precio de venta, marca y existencia opcional.</summary>
    public async Task<Product> ProductAsync(string sku, decimal? salePrice, string? brand = null, decimal stock = 0m)
    {
        var p = await F.AddProductAsync(sku);
        var row = await F.Db.Set<Product>().AsTracking().SingleAsync(x => x.ProductId == p.ProductId);
        row.SalePrice = salePrice;
        row.Brand = brand;
        if (stock > 0m)
            F.Db.StockBalances.Add(new StockBalance { TenantId = WmsFixture.TenantId, ProductId = p.ProductId, WarehouseId = W.WarehouseId, WarehouseBinId = Bin.WarehouseBinId, QtyOnHand = stock, UpdatedAtUtc = DateTime.UtcNow });
        await F.Db.SaveChangesAsync();
        F.Db.ChangeTracker.Clear();
        return row;
    }

    /// <summary>Kit con sus componentes (cantidad por kit) y su precio de venta.</summary>
    public async Task<Product> KitAsync(string sku, decimal salePrice, string? brand, params (Product Component, decimal Qty)[] components)
    {
        var kit = await ProductAsync(sku, salePrice, brand);
        await Kits.SetAsync(kit.PublicId, new KitSetRequest(true, null, components.Select(c => new KitComponentRequest(c.Component.PublicId, c.Qty)).ToList()), default);
        F.Db.ChangeTracker.Clear();
        return kit;
    }

    /// <summary>Lista publicada con sus renglones (y clientes si no es general).</summary>
    public async Task<PriceListDto> PublishedListAsync(string name, DateOnly from, DateOnly? to, IReadOnlyList<Client>? clients, params (Product Product, decimal Price)[] items)
    {
        var l = await Lists.CreateAsync(new PriceListRequest(name, from, to, AppliesToAll: clients is null), default);
        await Lists.UpsertItemsAsync(l.PublicId, new PriceListItemsRequest(items.Select(i => new PriceListItemRequest(i.Product.PublicId, i.Price)).ToList()), default);
        if (clients is not null) await Lists.AddClientsAsync(l.PublicId, new PriceListClientsRequest(clients.Select(c => c.PublicId).ToList()), default);
        return await Lists.PublishAsync(l.PublicId, default);
    }

    public Task<SalesOrderDto> DraftAsync(Client c, params SalesOrderLineRequest[] lines)
        => Sales.CreateAsync(new SalesOrderRequest(W.PublicId, c.PublicId, Lines: lines), default);

    public ValueTask DisposeAsync() => F.DisposeAsync();
}

// Lote 221 (P1 del pedido de vendedores): el precio de la línea sale de la lista vigente del cliente, si no de la general, y si no del producto; el vendedor no teclea
// precios (otro precio exige sales.price, la excepción del administrador). Listas: publicar, cruces y borradores.
public sealed class SalesPriceResolutionTests
{
    [Fact]
    public async Task A_new_line_takes_the_client_list_then_the_general_list_then_the_product_price()
    {
        await using var x = await PriceWorld.CreateAsync();
        var strips = await x.ProductAsync("52800", 7.50m);
        var monitor = await x.ProductAsync("070120", 8.25m);
        var lancets = await x.ProductAsync("24-081028", 1.25m);
        await x.PublishedListAsync("General", x.Today.AddDays(-10), null, null, (strips, 6.99m), (monitor, 7.99m));
        await x.PublishedListAsync("Negociada", x.Today.AddDays(-1), x.Today.AddDays(30), new[] { x.C1 }, (strips, 5.50m));

        var o1 = await x.DraftAsync(x.C1, new SalesOrderLineRequest(strips.PublicId, 2m), new SalesOrderLineRequest(monitor.PublicId, 1m), new SalesOrderLineRequest(lancets.PublicId, 4m));
        Assert.Equal(new[] { 5.50m, 7.99m, 1.25m }, o1.Lines.Select(l => l.UnitPrice).ToArray());   // cliente > general > producto
        Assert.Equal(11m + 7.99m + 5m, o1.Subtotal);

        var o2 = await x.DraftAsync(x.C2, new SalesOrderLineRequest(strips.PublicId, 1m));
        Assert.Equal(6.99m, Assert.Single(o2.Lines).UnitPrice);   // C2 no está en la negociada
    }

    [Fact]
    public async Task Drafts_and_expired_lists_do_not_apply()
    {
        await using var x = await PriceWorld.CreateAsync();
        var p = await x.ProductAsync("A", 10m);
        var draft = await x.Lists.CreateAsync(new PriceListRequest("Borrador", x.Today.AddDays(-1), null), default);
        await x.Lists.UpsertItemsAsync(draft.PublicId, new PriceListItemsRequest(new[] { new PriceListItemRequest(p.PublicId, 1m) }), default);
        await x.PublishedListAsync("Vencida", x.Today.AddDays(-30), x.Today.AddDays(-1), null, (p, 2m));
        var o = await x.DraftAsync(x.C1, new SalesOrderLineRequest(p.PublicId, 1m));
        Assert.Equal(10m, o.Lines[0].UnitPrice);
    }

    [Fact]
    public async Task The_seller_cannot_type_a_price_but_may_send_the_resolved_one_and_the_admin_keeps_the_exception()
    {
        await using var x = await PriceWorld.CreateAsync();
        var p = await x.ProductAsync("A", 10m);
        await x.PublishedListAsync("General", x.Today, null, null, (p, 8m));

        x.F.SetPermissions(PermissionCatalog.SalesView, PermissionCatalog.SalesCreate);   // el vendedor
        var ok = await x.DraftAsync(x.C1, new SalesOrderLineRequest(p.PublicId, 1m, UnitPrice: 8m));   // el de la lista: sin permiso
        Assert.Equal(8m, ok.Lines[0].UnitPrice);
        var ex = await Assert.ThrowsAsync<ForbiddenException>(() => x.DraftAsync(x.C1, new SalesOrderLineRequest(p.PublicId, 1m, UnitPrice: 10m)));   // ni el del producto
        Assert.Equal(SalesRules.PriceNeedsPermission, ex.Message);

        x.F.SetPermissions(PermissionCatalog.SalesView, PermissionCatalog.SalesCreate, PermissionCatalog.SalesPrice);   // el administrador
        var admin = await x.DraftAsync(x.C1, new SalesOrderLineRequest(p.PublicId, 1m, UnitPrice: 3m));
        Assert.Equal(3m, admin.Lines[0].UnitPrice);

        // el vendedor vuelve a guardar el borrador con la excepción del administrador: lo que la línea ya tenía no se vuelve a autorizar
        x.F.SetPermissions(PermissionCatalog.SalesView, PermissionCatalog.SalesCreate);
        var again = await x.Sales.UpdateAsync(admin.PublicId, new SalesOrderRequest(Lines: new[] { new SalesOrderLineRequest(p.PublicId, 2m, UnitPrice: 3m) }), default);
        Assert.Equal((3m, 2m), (again.Lines[0].UnitPrice, again.Lines[0].QtyOrdered));
        await Assert.ThrowsAsync<ForbiddenException>(() => x.Sales.UpdateAsync(admin.PublicId, new SalesOrderRequest(Lines: new[] { new SalesOrderLineRequest(p.PublicId, 2m, UnitPrice: 2m) }), default));
    }

    [Fact]
    public async Task Editing_a_line_resolves_the_price_again_and_changing_the_client_reprices_list_lines_only()
    {
        await using var x = await PriceWorld.CreateAsync();
        var a = await x.ProductAsync("A", 10m);
        var b = await x.ProductAsync("B", 20m);
        await x.PublishedListAsync("Negociada C2", x.Today, null, new[] { x.C2 }, (a, 7m));

        var o = await x.DraftAsync(x.C1, new SalesOrderLineRequest(a.PublicId, 1m), new SalesOrderLineRequest(b.PublicId, 1m, UnitPrice: 15m));   // B con excepción (admin)
        Assert.Equal(new[] { 10m, 15m }, o.Lines.Select(l => l.UnitPrice).ToArray());

        var moved = await x.Sales.UpdateAsync(o.PublicId, new SalesOrderRequest(ClientPublicId: x.C2.PublicId), default);   // cambia el cliente, mismas líneas
        Assert.Equal(new[] { 7m, 15m }, moved.Lines.Select(l => l.UnitPrice).ToArray());   // A toma la de C2; la excepción de B se queda

        var edited = await x.Sales.UpdateAsync(o.PublicId, new SalesOrderRequest(Lines: new[] { new SalesOrderLineRequest(a.PublicId, 3m) }), default);
        Assert.Equal((7m, 21m), (edited.Lines[0].UnitPrice, edited.Subtotal));
    }

    [Fact]
    public async Task Modifying_a_requested_line_accepts_the_list_price_without_the_price_permission()
    {
        await using var x = await PriceWorld.CreateAsync();
        var a = await x.ProductAsync("A", 10m, stock: 20m);
        var list = await x.PublishedListAsync("General", x.Today, null, null, (a, 9m));
        var o = await x.DraftAsync(x.C1, new SalesOrderLineRequest(a.PublicId, 1m));
        o = await x.Sales.RequestAsync(o.PublicId, null, default);

        x.F.SetPermissions(PermissionCatalog.SalesView, PermissionCatalog.SalesCreate);
        // la lista cambia después de solicitada: la orden ya hecha conserva su precio
        await x.Lists.SetItemPriceAsync(list.PublicId, list.Items[0].Id, new PriceListItemPriceRequest(8m), default);
        Assert.Equal(9m, (await x.Sales.GetAsync(o.PublicId, default)).Lines[0].UnitPrice);
        // pasarla al precio de lista de hoy no exige sales.price; cualquier otro sí
        var mod = await x.Sales.ModifyLineAsync(o.PublicId, o.Lines[0].Id, new SalesOrderLineModifyRequest(UnitPrice: 8m), default);
        Assert.Equal(8m, mod.Lines[0].UnitPrice);
        await Assert.ThrowsAsync<ForbiddenException>(() => x.Sales.ModifyLineAsync(o.PublicId, o.Lines[0].Id, new SalesOrderLineModifyRequest(UnitPrice: 5m), default));
    }

    [Fact]
    public async Task Publishing_needs_items_and_clients_and_rejects_two_prices_for_the_same_audience()
    {
        await using var x = await PriceWorld.CreateAsync();
        var a = await x.ProductAsync("A", 10m);
        var b = await x.ProductAsync("B", 10m);
        var empty = await x.Lists.CreateAsync(new PriceListRequest("Vacía", x.Today, null), default);
        Assert.Equal(PriceListRules.PublishNeedsItems, (await Assert.ThrowsAsync<StatusRuleException>(() => x.Lists.PublishAsync(empty.PublicId, default))).Message);

        var noClients = await x.Lists.CreateAsync(new PriceListRequest("Sin clientes", x.Today, null, AppliesToAll: false), default);
        await x.Lists.UpsertItemsAsync(noClients.PublicId, new PriceListItemsRequest(new[] { new PriceListItemRequest(a.PublicId, 1m) }), default);
        Assert.Equal(PriceListRules.PublishNeedsClients, (await Assert.ThrowsAsync<StatusRuleException>(() => x.Lists.PublishAsync(noClients.PublicId, default))).Message);

        await x.PublishedListAsync("Base", x.Today, null, null, (a, 5m));
        // otra general con A en fechas que se cruzan: 409; con B solamente, se puede
        var clash = await Assert.ThrowsAsync<ConflictException>(() => x.PublishedListAsync("Navidad", x.Today.AddDays(5), x.Today.AddDays(10), null, (a, 4m), (b, 4m)));
        Assert.Equal(PriceListRules.Overlap("Base", new[] { "A" }), clash.Message);
        var bOnly = await x.PublishedListAsync("Solo B", x.Today.AddDays(5), x.Today.AddDays(10), null, (b, 4m));
        Assert.Equal(PriceListStates.Scheduled, bOnly.State);
        // agregar A a la publicada también choca (se comprueba en cada cambio)
        await Assert.ThrowsAsync<ConflictException>(() => x.Lists.UpsertItemsAsync(bOnly.PublicId, new PriceListItemsRequest(new[] { new PriceListItemRequest(a.PublicId, 4m) }), default));
        // una lista del cliente con A sí se puede: la del cliente gana a la general
        var client = await x.PublishedListAsync("C1", x.Today, null, new[] { x.C1 }, (a, 3m));
        Assert.Equal(PriceListStates.Active, client.State);
        Assert.Equal(3m, (await x.DraftAsync(x.C1, new SalesOrderLineRequest(a.PublicId, 1m))).Lines[0].UnitPrice);
    }

    [Fact]
    public async Task List_items_are_own_active_products_and_a_general_list_takes_no_clients()
    {
        await using var x = await PriceWorld.CreateAsync();
        var a = await x.ProductAsync("A", 10m);
        var thirdParty = await x.F.AddProductAsync("3PL", ownerClientId: x.C1.ClientId);
        var l = await x.Lists.CreateAsync(new PriceListRequest("General", x.Today, null), default);
        Assert.Equal(PriceListRules.ItemProductNotOwn, (await Assert.ThrowsAsync<StatusRuleException>(() =>
            x.Lists.UpsertItemsAsync(l.PublicId, new PriceListItemsRequest(new[] { new PriceListItemRequest(thirdParty.PublicId, 1m) }), default))).Message);
        await Assert.ThrowsAsync<ValidationException>(() =>
            x.Lists.UpsertItemsAsync(l.PublicId, new PriceListItemsRequest(new[] { new PriceListItemRequest(a.PublicId, 1m), new PriceListItemRequest(a.PublicId, 2m) }), default));
        Assert.Equal(PriceListRules.ClientsOnGeneralList, (await Assert.ThrowsAsync<StatusRuleException>(() =>
            x.Lists.AddClientsAsync(l.PublicId, new PriceListClientsRequest(new[] { x.C1.PublicId }), default))).Message);

        // un mismo producto dos veces: el segundo cambia el precio; quitar es en lógico
        await x.Lists.UpsertItemsAsync(l.PublicId, new PriceListItemsRequest(new[] { new PriceListItemRequest(a.PublicId, 1m) }), default);
        var dto = await x.Lists.UpsertItemsAsync(l.PublicId, new PriceListItemsRequest(new[] { new PriceListItemRequest(a.PublicId, 2m) }), default);
        Assert.Equal(2m, Assert.Single(dto.Items).Price);
        dto = await x.Lists.RemoveItemAsync(l.PublicId, dto.Items[0].Id, default);
        Assert.Empty(dto.Items);
        Assert.False(Assert.Single(await x.F.Db.Set<PriceListItem>().ToListAsync()).IsActive);   // nada se borra

        // desactivada: no se cambia ni aplica
        await x.Lists.DeactivateAsync(l.PublicId, default);
        Assert.Equal(PriceListRules.Inactive, (await Assert.ThrowsAsync<StatusRuleException>(() => x.Lists.PublishAsync(l.PublicId, default))).Message);
        Assert.Empty(await x.Lists.ListAsync(null, null, false, default));
        Assert.Single(await x.Lists.ListAsync(null, null, true, default));
    }
}
