using Teikem.Domain.Clients;
using Teikem.Domain.Sales;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 247 (Lote C, P4; DECISIÓN 13, contrato del Lote 245) — desactivar una cadena da 409 si tiene listas de precios (incluidos sus precios especiales) u ofertas
/// INCLUDE publicadas y no vencidas; primero sale el mensaje de clientes activos, y las ofertas EXCLUDE, las listas vencidas, las no publicadas y las filas quitadas
/// no bloquean. Sobre la base en memoria del mundo de precios (datos INVENTADOS).
/// </summary>
public sealed class ClientChainDeactivationPricesTests
{
    private static ClientChainService Chains(PriceWorld x) => new(x.F.Db, x.F.Tenant, TenantClock.Default);

    private static Task<ClientChainDetailDto> DeactivateAsync(PriceWorld x, ClientChain chain) => Chains(x).SetActiveAsync(chain.PublicId, false, default);

    private static async Task<string> BlockedAsync(PriceWorld x, ClientChain chain)
        => (await Assert.ThrowsAsync<ConflictException>(() => DeactivateAsync(x, chain))).Message;

    private static async Task<OfferDto> OfferAsync(PriceWorld x, string name, string mode, ClientChain chain, Teikem.Domain.Wms.Product p, DateOnly? to = null)
    {
        var o = await x.Offers.CreateAsync(new OfferRequest(name, x.Today.AddDays(-1), to, AudienceMode: mode), default);
        await x.Offers.UpsertItemsAsync(o.PublicId, new OfferItemsRequest(new[] { new OfferItemRequest(p.PublicId, 5m) }), default);
        await x.Offers.AddChainsAsync(o.PublicId, new OfferChainsRequest(new[] { chain.PublicId }), default);
        return await x.Offers.PublishAsync(o.PublicId, default);
    }

    [Fact]
    public async Task A_published_list_a_dedicated_list_or_an_include_offer_block_with_the_exact_text()
    {
        await using var x = await PriceWorld.CreateAsync();
        var p = await x.ProductAsync("P", 10m);

        // lista normal publicada y vigente con la cadena
        var a = await x.ChainAsync("Cadena A");
        var l = await x.Lists.CreateAsync(new PriceListRequest("Por cadena", x.Today.AddDays(-1), null, AppliesToAll: false), default);
        await x.Lists.UpsertItemsAsync(l.PublicId, new PriceListItemsRequest(new[] { new PriceListItemRequest(p.PublicId, 8m) }), default);
        await x.Lists.AddChainsAsync(l.PublicId, new PriceListChainsRequest(new[] { a.PublicId }), default);
        await x.Lists.PublishAsync(l.PublicId, default);
        Assert.Equal("La cadena tiene precios especiales u ofertas publicados y vigentes (1 listas de precios y 0 ofertas): quítela de ellos antes de desactivarla.",
            await BlockedAsync(x, a));

        // su precio especial (programado también cuenta: no está vencido)
        var b = await x.ChainAsync("Cadena B");
        await x.Lists.AddChainSpecialPriceAsync(b.PublicId, new SpecialPriceRequest(p.PublicId, 7m, x.Today.AddDays(3)), default);
        Assert.Equal(ClientChainRules.HasPricesOrOffers(1, 0), await BlockedAsync(x, b));

        // una oferta INCLUDE, y todo junto
        var c = await x.ChainAsync("Cadena C");
        await OfferAsync(x, "Circular", OfferAudienceModes.Include, c, p);
        Assert.Equal(ClientChainRules.HasPricesOrOffers(0, 1), await BlockedAsync(x, c));
        await x.Lists.AddChainSpecialPriceAsync(c.PublicId, new SpecialPriceRequest(p.PublicId, 6m, x.Today), default);
        await OfferAsync(x, "Circular 2", OfferAudienceModes.Include, c, await x.ProductAsync("Q", 10m));
        Assert.Equal(ClientChainRules.HasPricesOrOffers(1, 2), await BlockedAsync(x, c));

        // al quitar el bloqueo, se desactiva
        var special = await x.Lists.GetChainSpecialPricesAsync(b.PublicId, false, default);
        await x.Lists.RemoveChainSpecialPriceAsync(b.PublicId, special.Rows[0].ItemId, default);
        Assert.False((await DeactivateAsync(x, b)).IsActive);
    }

    [Fact]
    public async Task Exclude_offers_expired_or_draft_lists_and_removed_rows_do_not_block()
    {
        await using var x = await PriceWorld.CreateAsync();
        var p = await x.ProductAsync("P", 10m);
        var chain = await x.ChainAsync("Walgreens");

        await OfferAsync(x, "Todos menos Walgreens", OfferAudienceModes.Exclude, chain, p);
        await x.ChainListAsync("Vencida", x.Today.AddDays(-30), x.Today.AddDays(-1), chain, special: false, (p, 8m));
        await x.ChainListAsync("Especial vencido", x.Today.AddDays(-30), x.Today.AddDays(-1), chain, special: true, (p, 7m));
        var draft = await x.Lists.CreateAsync(new PriceListRequest("Borrador", x.Today, null, AppliesToAll: false), default);
        await x.Lists.AddChainsAsync(draft.PublicId, new PriceListChainsRequest(new[] { chain.PublicId }), default);
        var removed = await x.Lists.CreateAsync(new PriceListRequest("Quitada", x.Today, null, AppliesToAll: false), default);
        await x.Lists.UpsertItemsAsync(removed.PublicId, new PriceListItemsRequest(new[] { new PriceListItemRequest(p.PublicId, 9m) }), default);
        await x.Lists.AddChainsAsync(removed.PublicId, new PriceListChainsRequest(new[] { chain.PublicId }), default);
        await x.Lists.AddClientsAsync(removed.PublicId, new PriceListClientsRequest(new[] { x.C1.PublicId }), default);
        await x.Lists.PublishAsync(removed.PublicId, default);
        await x.Lists.RemoveChainAsync(removed.PublicId, chain.PublicId, default);
        var unpublishedOffer = await x.Offers.CreateAsync(new OfferRequest("Sin publicar", x.Today, null, AudienceMode: OfferAudienceModes.Include), default);
        await x.Offers.AddChainsAsync(unpublishedOffer.PublicId, new OfferChainsRequest(new[] { chain.PublicId }), default);
        await x.Offers.UnpublishAsync(unpublishedOffer.PublicId, default);   // las ofertas nacen publicadas

        Assert.False((await DeactivateAsync(x, chain)).IsActive);
    }

    [Fact]
    public async Task With_active_clients_the_clients_message_comes_first()
    {
        await using var x = await PriceWorld.CreateAsync();
        var p = await x.ProductAsync("P", 10m);
        var chain = await x.ChainAsync("Walgreens");
        var member = await x.ClientInAsync("W1", chain);
        await x.Lists.AddChainSpecialPriceAsync(chain.PublicId, new SpecialPriceRequest(p.PublicId, 7m, x.Today), default);

        Assert.Equal(ClientChainRules.HasActiveClients(1), await BlockedAsync(x, chain));
        await x.MoveToChainAsync(member, null);
        Assert.Equal(ClientChainRules.HasPricesOrOffers(1, 0), await BlockedAsync(x, chain));
    }
}
