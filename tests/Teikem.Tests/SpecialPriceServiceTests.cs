using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Clients;
using Teikem.Domain.Sales;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 247 (Lote C, P4) — «Precios especiales» de la ficha del cliente y de la cadena (<c>PriceListService.SpecialPrices.cs</c>) sobre la base en memoria del
/// mundo de precios (datos INVENTADOS): la lista dedicada por dueño y por vigencia, reutilizarla, el cruce de fechas (409), mover un renglón de fechas, la lista
/// vacía que se desactiva, las validaciones (400/404/422), la vista con estado, días, lista general, avisos, precios de la cadena, listas que alcanzan al cliente y
/// lo que se cobra hoy, y el bloqueo de nombre, fechas y público de una dedicada desde los endpoints genéricos de listas.
/// </summary>
public sealed class SpecialPriceServiceTests
{
    private static Task<SpecialPricesDto> AddClientAsync(PriceWorld x, Client c, Product p, decimal? price, DateOnly? from, DateOnly? to = null)
        => x.Lists.AddClientSpecialPriceAsync(c.PublicId, new SpecialPriceRequest(p.PublicId, price, from, to), default);

    private static Task<SpecialPricesDto> AddChainAsync(PriceWorld x, ClientChain ch, Product p, decimal price, DateOnly from, DateOnly? to = null)
        => x.Lists.AddChainSpecialPriceAsync(ch.PublicId, new SpecialPriceRequest(p.PublicId, price, from, to), default);

    private static Task<List<PriceList>> ClientDedicatedAsync(PriceWorld x, Client c)
        => x.F.Db.Set<PriceList>().AsNoTracking().Include(l => l.Items).Include(l => l.Clients).Include(l => l.Chains)
            .Where(l => l.SpecialClientId == c.ClientId).OrderBy(l => l.PriceListId).ToListAsync();

    private static async Task<decimal> DraftPriceAsync(PriceWorld x, Client c, Product p)
        => Assert.Single((await x.DraftAsync(c, new SalesOrderLineRequest(p.PublicId, 1m))).Lines).UnitPrice;

    private static int ItemOf(SpecialPricesDto dto, Product p) => dto.Rows.Single(r => r.ProductPublicId == p.PublicId).ItemId;

    private static async Task SetClientActiveAsync(PriceWorld x, Client c, bool active)
    {
        var row = await x.F.Db.Clients.AsTracking().SingleAsync(k => k.ClientId == c.ClientId);
        row.IsActive = active;
        await x.F.Db.SaveChangesAsync();
        x.F.Db.ChangeTracker.Clear();
    }

    // ---------------------------------------------------------------- alta

    [Fact]
    public async Task Adding_a_row_creates_a_published_dedicated_list_for_owner_and_dates()
    {
        await using var x = await PriceWorld.CreateAsync();
        var p = await x.ProductAsync("A", 10m, "Marca");
        var chain = await x.ChainAsync("Walgreens");
        var member = await x.ClientInAsync("W1", chain);

        // del cliente
        var dto = await AddClientAsync(x, x.C1, p, 8m, x.Today, x.Today.AddDays(30));
        Assert.Equal((SpecialPriceOwnerKinds.Client, x.C1.PublicId, x.C1.Name, true), (dto.OwnerKind, dto.OwnerPublicId, dto.OwnerName, dto.OwnerIsActive));
        var row = Assert.Single(dto.Rows);
        Assert.Equal((8m, PriceListStates.Active, 30, x.Today, x.Today.AddDays(30)), (row.Price, row.State, row.DaysToExpire, row.ValidFrom, row.ValidTo));
        Assert.Equal((8m, PriceSources.ClientList), (row.ChargedPrice, row.ChargedSource));
        var list = Assert.Single(await ClientDedicatedAsync(x, x.C1));
        Assert.True(list.IsPublished && list.IsActive && !list.AppliesToAll && list.SpecialChainId is null);
        Assert.Equal(SpecialPriceRules.DedicatedName(x.C1.Name, x.Today, x.Today.AddDays(30)), list.Name);
        Assert.Equal(new[] { x.C1.ClientId }, list.Clients.Where(c => c.IsActive).Select(c => c.ClientId));
        Assert.Equal(list.PublicId, row.PriceListPublicId);
        Assert.Equal(8m, await DraftPriceAsync(x, x.C1, p));
        Assert.Equal(10m, await DraftPriceAsync(x, x.C2, p));

        // de la cadena
        var cdto = await AddChainAsync(x, chain, p, 7m, x.Today);
        Assert.Equal((SpecialPriceOwnerKinds.Chain, chain.PublicId), (cdto.OwnerKind, cdto.OwnerPublicId));
        Assert.Equal((7m, (int?)null), (Assert.Single(cdto.Rows).Price, cdto.Rows[0].DaysToExpire));
        var clist = await x.F.Db.Set<PriceList>().AsNoTracking().Include(l => l.Chains).SingleAsync(l => l.SpecialChainId == chain.ClientChainId);
        Assert.True(clist.IsPublished && !clist.AppliesToAll && clist.SpecialClientId is null && clist.ValidTo is null);
        Assert.Equal(new[] { chain.ClientChainId }, clist.Chains.Where(c => c.IsActive).Select(c => c.ChainId));
        Assert.Equal(7m, await DraftPriceAsync(x, member, p));
        Assert.Equal(10m, await DraftPriceAsync(x, x.C2, p));
    }

    [Fact]
    public async Task Same_dates_reuse_the_list_and_the_same_product_changes_the_price()
    {
        await using var x = await PriceWorld.CreateAsync();
        var p1 = await x.ProductAsync("P1", 10m);
        var p2 = await x.ProductAsync("P2", 10m);
        var p3 = await x.ProductAsync("P3", 10m);
        await AddClientAsync(x, x.C1, p1, 8m, x.Today);
        await AddClientAsync(x, x.C1, p2, 5m, x.Today);
        var dto = await AddClientAsync(x, x.C1, p1, 7.5m, x.Today);   // mismo producto, mismas fechas: cambia el precio
        Assert.Equal(new[] { 7.5m, 5m }, dto.Rows.OrderBy(r => r.Sku).Select(r => r.Price));
        var list = Assert.Single(await ClientDedicatedAsync(x, x.C1));
        Assert.Equal(2, list.Items.Count(i => i.IsActive));

        // otras fechas: otra lista
        await AddClientAsync(x, x.C1, p3, 9m, x.Today.AddDays(10), x.Today.AddDays(20));
        Assert.Equal(2, (await ClientDedicatedAsync(x, x.C1)).Count(l => l.IsActive));
    }

    [Fact]
    public async Task Overlapping_dates_same_product_same_owner_is_409_overlap_message()
    {
        await using var x = await PriceWorld.CreateAsync();
        var p = await x.ProductAsync("SKU-1", 10m);
        await AddClientAsync(x, x.C1, p, 8m, x.Today, x.Today.AddDays(30));
        var ex = await Assert.ThrowsAsync<ConflictException>(() => AddClientAsync(x, x.C1, p, 7m, x.Today.AddDays(10)));
        Assert.Equal(PriceListRules.Overlap(SpecialPriceRules.DedicatedName(x.C1.Name, x.Today, x.Today.AddDays(30)), new[] { "SKU-1" }), ex.Message);
        Assert.Single(await ClientDedicatedAsync(x, x.C1));

        // de la cadena, igual
        var chain = await x.ChainAsync("Walgreens");
        await AddChainAsync(x, chain, p, 6m, x.Today);
        await Assert.ThrowsAsync<ConflictException>(() => AddChainAsync(x, chain, p, 5m, x.Today.AddDays(1), x.Today.AddDays(2)));

        // otro dueño, la cadena del cliente o una lista normal que lo incluye: conviven (DECISIÓN 14)
        await AddClientAsync(x, x.C2, p, 7m, x.Today);
        await x.MoveToChainAsync(x.C1, chain);
        await x.PublishedListAsync("Grupo", x.Today, null, new[] { x.C1 }, (p, 7.25m));
        await AddClientAsync(x, x.C1, p, 6.5m, x.Today.AddDays(31));
        Assert.Equal(6m, await DraftPriceAsync(x, x.C1, p));   // gana el más bajo: el de la cadena
    }

    // ---------------------------------------------------------------- cambio y baja

    [Fact]
    public async Task Changing_dates_moves_the_row_and_an_emptied_list_is_deactivated()
    {
        await using var x = await PriceWorld.CreateAsync();
        var p1 = await x.ProductAsync("P1", 10m);
        var p2 = await x.ProductAsync("P2", 10m);
        await AddClientAsync(x, x.C1, p1, 8m, x.Today);
        var dto = await AddClientAsync(x, x.C1, p2, 6m, x.Today);
        var l1 = Assert.Single(await ClientDedicatedAsync(x, x.C1));

        // P1 cierra en 15 días: pasa a otra lista; la primera sigue (le queda P2)
        dto = await x.Lists.UpdateClientSpecialPriceAsync(x.C1.PublicId, ItemOf(dto, p1), new SpecialPricePatchRequest(ValidTo: x.Today.AddDays(15)), default);
        var r1 = dto.Rows.Single(r => r.ProductPublicId == p1.PublicId);
        Assert.Equal((8m, x.Today, x.Today.AddDays(15)), (r1.Price, r1.ValidFrom, r1.ValidTo));
        Assert.NotEqual(l1.PublicId, r1.PriceListPublicId);
        var lists = await ClientDedicatedAsync(x, x.C1);
        Assert.Equal(2, lists.Count(l => l.IsActive));
        Assert.Contains(lists.Single(l => l.PriceListId == l1.PriceListId).Items, i => i.ProductId == p1.ProductId && !i.IsActive);   // queda en el historial

        // P2 empieza mañana y a otro precio: la primera lista se queda sin renglones y se desactiva sola
        dto = await x.Lists.UpdateClientSpecialPriceAsync(x.C1.PublicId, ItemOf(dto, p2), new SpecialPricePatchRequest(5.5m, ValidFrom: x.Today.AddDays(1)), default);
        var r2 = dto.Rows.Single(r => r.ProductPublicId == p2.PublicId);
        Assert.Equal((5.5m, x.Today.AddDays(1), (DateOnly?)null, PriceListStates.Scheduled), (r2.Price, r2.ValidFrom, r2.ValidTo, r2.State));
        Assert.False((await ClientDedicatedAsync(x, x.C1)).Single(l => l.PriceListId == l1.PriceListId).IsActive);

        // solo el precio: el mismo renglón
        var id1 = ItemOf(dto, p1);
        dto = await x.Lists.UpdateClientSpecialPriceAsync(x.C1.PublicId, id1, new SpecialPricePatchRequest(7.75m), default);
        Assert.Equal((id1, 7.75m), (ItemOf(dto, p1), dto.Rows.Single(r => r.ItemId == id1).Price));

        // quitar: en lógico, y la lista vacía se desactiva
        dto = await x.Lists.RemoveClientSpecialPriceAsync(x.C1.PublicId, id1, default);
        Assert.Equal(new[] { p2.PublicId }, dto.Rows.Select(r => r.ProductPublicId));
        var after = await ClientDedicatedAsync(x, x.C1);
        Assert.Single(after, l => l.IsActive);
        Assert.Contains(after.SelectMany(l => l.Items), i => i.PriceListItemId == id1 && !i.IsActive);
        Assert.Equal(10m, await DraftPriceAsync(x, x.C1, p1));
        var gone = await Assert.ThrowsAsync<NotFoundException>(() => x.Lists.RemoveClientSpecialPriceAsync(x.C1.PublicId, id1, default));
        Assert.Equal("Precio especial no encontrado.", gone.Message);
    }

    // ---------------------------------------------------------------- validaciones

    [Fact]
    public async Task Validations()
    {
        await using var x = await PriceWorld.CreateAsync();
        var p = await x.ProductAsync("P", 10m);
        var today = x.Today;

        async Task Bad(Func<Task> act, string field, string message)
        {
            var ex = await Assert.ThrowsAsync<ValidationException>(act);
            Assert.Equal(message, ex.Message);
            Assert.True(ex.Errors!.ContainsKey(field), field);
        }

        // 400
        await Bad(() => x.Lists.AddClientSpecialPriceAsync(x.C1.PublicId, null, default), "body", "El cuerpo de la solicitud es obligatorio.");
        await Bad(() => x.Lists.AddClientSpecialPriceAsync(x.C1.PublicId, new SpecialPriceRequest(null, 1m, today), default), "productPublicId", PriceListRules.ItemProductRequired);
        await Bad(() => AddClientAsync(x, x.C1, p, null, today), "price", PriceListRules.ItemPriceRequired);
        await Bad(() => AddClientAsync(x, x.C1, p, -1m, today), "price", SalesRules.PriceError(-1m)!);
        await Bad(() => AddClientAsync(x, x.C1, p, 1m, null), "validFrom", "Indique desde qué fecha vale el precio especial.");
        await Bad(() => AddClientAsync(x, x.C1, p, 1m, today, today.AddDays(-1)), "validTo", PriceListRules.DateRange);
        await Bad(() => AddClientAsync(x, x.C1, p, 1m, today.AddDays(-10), today.AddDays(-1)), "validTo",
            "La fecha «hasta» ya pasó: un precio especial nuevo tiene que valer hoy o después.");
        Assert.Empty(await ClientDedicatedAsync(x, x.C1));

        // al editar, un «hasta» pasado sí (cerrar un precio que ya corre); sale solo con includeExpired
        var dto = await AddClientAsync(x, x.C1, p, 8m, today.AddDays(-10));
        var id = ItemOf(dto, p);
        dto = await x.Lists.UpdateClientSpecialPriceAsync(x.C1.PublicId, id, new SpecialPricePatchRequest(ValidTo: today.AddDays(-1)), default);
        Assert.Empty(dto.Rows);
        var withExpired = await x.Lists.GetClientSpecialPricesAsync(x.C1.PublicId, true, default);
        Assert.Equal((PriceListStates.Expired, -1), (Assert.Single(withExpired.Rows).State, withExpired.Rows[0].DaysToExpire));
        await Bad(() => x.Lists.UpdateClientSpecialPriceAsync(x.C1.PublicId, withExpired.Rows[0].ItemId,
            new SpecialPricePatchRequest(ValidFrom: today, ValidTo: today.AddDays(-2)), default), "validTo", PriceListRules.DateRange);
        await Bad(() => x.Lists.UpdateClientSpecialPriceAsync(x.C1.PublicId, withExpired.Rows[0].ItemId, null, default), "body", "El cuerpo de la solicitud es obligatorio.");

        // 404: dueño de otra compañía, producto que no existe, renglón de otro dueño
        var foreign = await x.F.AddClientAsync("AJENO", tenantId: WmsFixture.TenantId + 100);
        Assert.Equal("Cliente no encontrado.", (await Assert.ThrowsAsync<NotFoundException>(() => AddClientAsync(x, foreign, p, 1m, today))).Message);
        Assert.Equal("Cliente no encontrado.", (await Assert.ThrowsAsync<NotFoundException>(() => x.Lists.GetClientSpecialPricesAsync(foreign.PublicId, false, default))).Message);
        var foreignChain = new ClientChain { PublicId = Guid.NewGuid(), TenantId = WmsFixture.TenantId + 100, Name = "Ajena", IsActive = true, CreatedAtUtc = DateTime.UtcNow };
        x.F.Db.Set<ClientChain>().Add(foreignChain);
        await x.F.Db.SaveChangesAsync();
        x.F.Db.ChangeTracker.Clear();
        Assert.Equal("Cadena no encontrada.", (await Assert.ThrowsAsync<NotFoundException>(() => x.Lists.GetChainSpecialPricesAsync(foreignChain.PublicId, false, default))).Message);
        Assert.Equal("Cadena no encontrada.", (await Assert.ThrowsAsync<NotFoundException>(() => AddChainAsync(x, foreignChain, p, 1m, today))).Message);
        Assert.Equal("Producto no encontrado.", (await Assert.ThrowsAsync<NotFoundException>(() =>
            x.Lists.AddClientSpecialPriceAsync(x.C1.PublicId, new SpecialPriceRequest(Guid.NewGuid(), 1m, today), default))).Message);
        var mine = await AddClientAsync(x, x.C1, p, 8m, today);
        Assert.Equal("Precio especial no encontrado.", (await Assert.ThrowsAsync<NotFoundException>(() =>
            x.Lists.UpdateClientSpecialPriceAsync(x.C2.PublicId, ItemOf(mine, p), new SpecialPricePatchRequest(1m), default))).Message);
        Assert.Equal("Precio especial no encontrado.", (await Assert.ThrowsAsync<NotFoundException>(() =>
            x.Lists.RemoveClientSpecialPriceAsync(x.C1.PublicId, 987654, default))).Message);
        // un renglón de una lista NORMAL tampoco es un precio especial
        var normal = await x.PublishedListAsync("Normal", today, null, new[] { x.C1 }, (await x.ProductAsync("N", 5m), 4m));
        Assert.Equal("Precio especial no encontrado.", (await Assert.ThrowsAsync<NotFoundException>(() =>
            x.Lists.RemoveClientSpecialPriceAsync(x.C1.PublicId, normal.Items[0].Id, default))).Message);

        // 422: producto ajeno, inactivo o kit de oferta
        var thirdParty = await x.F.AddProductAsync("3PL", ownerClientId: x.C2.ClientId);
        Assert.Equal(PriceListRules.ItemProductNotOwn, (await Assert.ThrowsAsync<StatusRuleException>(() => AddClientAsync(x, x.C1, thirdParty, 1m, today))).Message);
        var inactive = await x.F.AddProductAsync("OFF", isActive: false);
        Assert.Equal(PriceListRules.ItemProductInactive("OFF"), (await Assert.ThrowsAsync<StatusRuleException>(() => AddClientAsync(x, x.C1, inactive, 1m, today))).Message);
        var kit = await x.ProductAsync("KIT-OF", 10m);
        var kitRow = await x.F.Db.Set<Product>().AsTracking().SingleAsync(k => k.ProductId == kit.ProductId);
        kitRow.IsOfferKit = true;
        await x.F.Db.SaveChangesAsync();
        x.F.Db.ChangeTracker.Clear();
        Assert.Equal(OfferKitRules.NotInPriceLists("KIT-OF"), (await Assert.ThrowsAsync<StatusRuleException>(() => AddClientAsync(x, x.C1, kit, 1m, today))).Message);

        // 422: dueño inactivo (para escribir; ver sí se puede)
        await SetClientActiveAsync(x, x.C1, false);
        Assert.Equal(SalesRules.ClientInactive, (await Assert.ThrowsAsync<StatusRuleException>(() => AddClientAsync(x, x.C1, p, 1m, today.AddDays(400)))).Message);
        Assert.Equal(SalesRules.ClientInactive, (await Assert.ThrowsAsync<StatusRuleException>(() =>
            x.Lists.RemoveClientSpecialPriceAsync(x.C1.PublicId, ItemOf(mine, p), default))).Message);
        Assert.False((await x.Lists.GetClientSpecialPricesAsync(x.C1.PublicId, false, default)).OwnerIsActive);
        var chain = await x.ChainAsync("Inactiva", isActive: false);
        Assert.Equal(ClientChainRules.ChainInactive, (await Assert.ThrowsAsync<StatusRuleException>(() => AddChainAsync(x, chain, p, 1m, today))).Message);
    }

    // ---------------------------------------------------------------- la vista

    [Fact]
    public async Task Rows_show_state_days_to_expire_general_price_and_above_general_warning()
    {
        await using var x = await PriceWorld.CreateAsync();
        var today = x.Today;
        var p1 = await x.ProductAsync("A-1", 10m, "Alfa");
        var p2 = await x.ProductAsync("B-2", 10m, "Alfa");
        var p3 = await x.ProductAsync("C-3", 10m, "Beta");
        var p4 = await x.ProductAsync("D-4", 10m, null);
        await x.PublishedListAsync("General", today.AddDays(-10), null, null, (p1, 9m), (p2, 5m));

        await AddClientAsync(x, x.C1, p1, 8m, today, today.AddDays(5));
        await AddClientAsync(x, x.C1, p2, 6m, today);
        await AddClientAsync(x, x.C1, p3, 4m, today.AddDays(3), today.AddDays(10));
        var expired = await AddClientAsync(x, x.C1, p4, 3m, today.AddDays(-20));
        await x.Lists.UpdateClientSpecialPriceAsync(x.C1.PublicId, ItemOf(expired, p4), new SpecialPricePatchRequest(ValidTo: today.AddDays(-1)), default);

        var dto = await x.Lists.GetClientSpecialPricesAsync(x.C1.PublicId, false, default);
        Assert.Equal(new[] { "A-1", "B-2", "C-3" }, dto.Rows.Select(r => r.Sku));   // por marca y SKU; el vencido no sale
        var a = dto.Rows[0];
        Assert.Equal((PriceListStates.Active, 5, 9m, false), (a.State, a.DaysToExpire, a.GeneralListPrice, a.AboveGeneral));
        Assert.Equal((8m, PriceSources.ClientList, SpecialPriceRules.DedicatedName(x.C1.Name, today, today.AddDays(5))), (a.ChargedPrice, a.ChargedSource, a.ChargedSourceName));
        var b = dto.Rows[1];
        Assert.Equal((PriceListStates.Active, (int?)null, 5m, true), (b.State, b.DaysToExpire, b.GeneralListPrice, b.AboveGeneral));
        Assert.Equal((5m, PriceSources.GeneralList, "General"), (b.ChargedPrice, b.ChargedSource, b.ChargedSourceName));   // se cobra el más bajo
        var c = dto.Rows[2];
        Assert.Equal((PriceListStates.Scheduled, 10, (decimal?)null, false), (c.State, c.DaysToExpire, c.GeneralListPrice, c.AboveGeneral));
        Assert.Equal((10m, PriceSources.Product, (string?)null), (c.ChargedPrice, c.ChargedSource, c.ChargedSourceName));   // todavía no empieza
        var w = Assert.Single(dto.Warnings);
        Assert.Equal((PriceNoticeCodes.SpecialAboveGeneral, p2.PublicId, 6m, (decimal?)5m), (w.Code, w.ProductPublicId, w.Price, w.GeneralListPrice));
        Assert.Equal("Precio especial más caro que la lista general: B-2 a $6.00 y en la lista general a $5.00. Se cobra el más bajo; corrija el precio especial.", w.Message);

        var all = await x.Lists.GetClientSpecialPricesAsync(x.C1.PublicId, true, default);
        Assert.Equal(new[] { "A-1", "B-2", "C-3", "D-4" }, all.Rows.Select(r => r.Sku));   // sin marca al final
        Assert.Equal((PriceListStates.Expired, -1), (all.Rows[3].State, all.Rows[3].DaysToExpire));
        Assert.Single(all.Warnings);
        Assert.Null(dto.ChainPublicId);
        Assert.Empty(dto.ChainRows);
    }

    [Fact]
    public async Task Client_view_brings_chain_rows_read_only_client_lists_and_charged_today_with_origin()
    {
        await using var x = await PriceWorld.CreateAsync();
        var today = x.Today;
        var p1 = await x.ProductAsync("P1", 10m);
        var p2 = await x.ProductAsync("P2", 10m);
        var p3 = await x.ProductAsync("P3", 10m);
        var chain = await x.ChainAsync("Walgreens");
        var c3 = await x.ClientInAsync("C3", chain);

        await AddChainAsync(x, chain, p1, 7m, today);
        await AddChainAsync(x, chain, p2, 4m, today.AddDays(5));
        var exp = await AddChainAsync(x, chain, p3, 2m, today.AddDays(-30));
        await x.Lists.UpdateChainSpecialPriceAsync(chain.PublicId, ItemOf(exp, p3), new SpecialPricePatchRequest(ValidTo: today.AddDays(-1)), default);
        await AddClientAsync(x, c3, p1, 7.5m, today);

        // listas normales: por el cliente, por la cadena, un borrador y una vencida (estas dos no salen)
        await x.PublishedListAsync("Grupo", today.AddDays(-1), null, new[] { c3 }, (p2, 3m));
        var byChain = await x.Lists.CreateAsync(new PriceListRequest("Cadena normal", today.AddDays(-1), null, AppliesToAll: false), default);
        await x.Lists.UpsertItemsAsync(byChain.PublicId, new PriceListItemsRequest(new[] { new PriceListItemRequest(p3.PublicId, 9m) }), default);
        await x.Lists.AddChainsAsync(byChain.PublicId, new PriceListChainsRequest(new[] { chain.PublicId }), default);
        await x.Lists.PublishAsync(byChain.PublicId, default);
        var draft = await x.Lists.CreateAsync(new PriceListRequest("Borrador", today, null, AppliesToAll: false), default);
        await x.Lists.AddClientsAsync(draft.PublicId, new PriceListClientsRequest(new[] { c3.PublicId }), default);
        await x.PublishedListAsync("Vieja", today.AddDays(-30), today.AddDays(-2), new[] { c3 }, (p2, 1m));

        var dto = await x.Lists.GetClientSpecialPricesAsync(c3.PublicId, false, default);
        var own = Assert.Single(dto.Rows);
        Assert.Equal((7.5m, 7m, PriceSources.ChainList), (own.Price, own.ChargedPrice, own.ChargedSource));   // gana el de la cadena, más bajo
        Assert.Equal(SpecialPriceRules.DedicatedName(chain.Name, today, null), own.ChargedSourceName);
        Assert.Equal((chain.PublicId, chain.Name, 2), (dto.ChainPublicId, dto.ChainName, dto.ChainRowCount));
        Assert.Equal(new[] { "P1", "P2" }, dto.ChainRows.Select(r => r.Sku));   // vigente y programado; el vencido no
        Assert.Equal(PriceListStates.Scheduled, dto.ChainRows[1].State);
        Assert.Equal((3m, PriceSources.ClientList, "Grupo"), (dto.ChainRows[1].ChargedPrice, dto.ChainRows[1].ChargedSource, dto.ChainRows[1].ChargedSourceName));
        Assert.Equal(new[] { ("Cadena normal", SpecialPriceOwnerKinds.Chain, 1), ("Grupo", SpecialPriceOwnerKinds.Client, 1) },
            dto.ClientLists.Select(l => (l.Name, l.MatchedBy, l.ItemCount)));

        // la ficha de la cadena: sus precios, la lista que la incluye y lo que paga un miembro sin precio propio
        var cdto = await x.Lists.GetChainSpecialPricesAsync(chain.PublicId, false, default);
        Assert.Equal(new[] { "P1", "P2" }, cdto.Rows.Select(r => r.Sku));
        Assert.Equal((7m, PriceSources.ChainList), (cdto.Rows[0].ChargedPrice, cdto.Rows[0].ChargedSource));
        Assert.Equal((10m, PriceSources.Product), (cdto.Rows[1].ChargedPrice, cdto.Rows[1].ChargedSource));
        Assert.Empty(cdto.ChainRows);
        Assert.Equal(new[] { ("Cadena normal", SpecialPriceOwnerKinds.Chain) }, cdto.ClientLists.Select(l => (l.Name, l.MatchedBy)));
        Assert.Equal(3, (await x.Lists.GetChainSpecialPricesAsync(chain.PublicId, true, default)).Rows.Count);

        // cadena desactivada = sin cadena
        await x.SetChainActiveAsync(chain, false);
        dto = await x.Lists.GetClientSpecialPricesAsync(c3.PublicId, false, default);
        Assert.Null(dto.ChainPublicId);
        Assert.Empty(dto.ChainRows);
        Assert.Equal((7.5m, PriceSources.ClientList), (dto.Rows[0].ChargedPrice, dto.Rows[0].ChargedSource));
        Assert.Equal(new[] { "Grupo" }, dto.ClientLists.Select(l => l.Name));
    }

    // ---------------------------------------------------------------- los endpoints genéricos de listas

    [Fact]
    public async Task Generic_endpoints_block_name_dates_and_audience_of_dedicated_lists_with_422()
    {
        await using var x = await PriceWorld.CreateAsync();
        var p = await x.ProductAsync("P", 10m);
        var q = await x.ProductAsync("Q", 10m);
        var chain = await x.ChainAsync("Walgreens");
        var dto = await AddClientAsync(x, x.C1, p, 8m, x.Today);
        var listId = dto.Rows[0].PriceListPublicId;
        var locked = PriceListRules.DedicatedLocked(x.C1.Name);
        Assert.Equal($"Esta lista es el precio especial de «{x.C1.Name}»: el nombre, las fechas y a quién aplica se cambian en «Precios especiales» de su ficha.", locked);

        async Task Locked(Func<Task> act) => Assert.Equal(locked, (await Assert.ThrowsAsync<StatusRuleException>(act)).Message);
        await Locked(() => x.Lists.UpdateAsync(listId, new PriceListRequest(Name: "Otro nombre"), default));
        await Locked(() => x.Lists.UpdateAsync(listId, new PriceListRequest(ValidFrom: x.Today.AddDays(1)), default));
        await Locked(() => x.Lists.UpdateAsync(listId, new PriceListRequest(ValidTo: x.Today.AddDays(9)), default));
        await Locked(() => x.Lists.UpdateAsync(listId, new PriceListRequest(AppliesToAll: true), default));
        await Locked(() => x.Lists.AddClientsAsync(listId, new PriceListClientsRequest(new[] { x.C2.PublicId }), default));
        await Locked(() => x.Lists.RemoveClientAsync(listId, x.C1.PublicId, default));
        await Locked(() => x.Lists.AddChainsAsync(listId, new PriceListChainsRequest(new[] { chain.PublicId }), default));
        await Locked(() => x.Lists.RemoveChainAsync(listId, chain.PublicId, default));

        // lo mismo que ya tiene y las notas sí pasan; precios, renglones, publicar y desactivar también
        var currentName = (await x.Lists.GetAsync(listId, default)).Name;
        var same = await x.Lists.UpdateAsync(listId, new PriceListRequest(Name: currentName, ClearValidTo: true, AppliesToAll: false, Notes: "Pactado en la visita"), default);
        Assert.Equal(("Pactado en la visita", SpecialPriceOwnerKinds.Client, x.C1.PublicId, x.C1.Name), (same.Notes, same.SpecialOwnerKind, same.SpecialOwnerPublicId, same.SpecialOwnerName));
        var item = Assert.Single(same.Items);
        await x.Lists.SetItemPriceAsync(listId, item.Id, new PriceListItemPriceRequest(7m), default);
        await x.Lists.UpsertItemsAsync(listId, new PriceListItemsRequest(new[] { new PriceListItemRequest(q.PublicId, 6m) }), default);
        Assert.Equal(new[] { 7m, 6m }, (await x.Lists.GetClientSpecialPricesAsync(x.C1.PublicId, false, default)).Rows.Select(r => r.Price));
        await x.Lists.UnpublishAsync(listId, default);
        Assert.All((await x.Lists.GetClientSpecialPricesAsync(x.C1.PublicId, false, default)).Rows, r => Assert.Equal(PriceListStates.Draft, r.State));
        await x.Lists.PublishAsync(listId, default);
        await x.Lists.DeactivateAsync(listId, default);
        Assert.Empty((await x.Lists.GetClientSpecialPricesAsync(x.C1.PublicId, false, default)).Rows);
        Assert.Equal(10m, await DraftPriceAsync(x, x.C1, p));
    }
}
