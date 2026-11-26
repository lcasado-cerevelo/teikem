using Teikem.Domain.Sales;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 247 (Lote C, P1) — propiedades de las reglas de precio con cadenas, con casos aleatorios de semilla FIJA (siempre los mismos): sin ofertas, la resolución de
/// las ofertas da lo mismo que la de las listas; y el cruce de públicos de dos ofertas nunca dice «no se cruzan» cuando, cliente por cliente, las dos le aplican.
/// </summary>
public sealed class ChainPricingPropertyTests
{
    private static readonly DateOnly D0 = new(2026, 12, 1);
    private static readonly decimal[] Prices = { 4m, 5m, 5m, 6m, 7.5m, 9m };

    private static int[] Subset(Random rnd, int max, double p)
        => Enumerable.Range(1, max).Where(_ => rnd.NextDouble() < p).ToArray();

    private static PriceListView RandomList(Random rnd, int id)
    {
        var kind = rnd.Next(4);   // 0 = general, 1 = de clientes, 2 = de cadenas, 3 = dedicada (de un cliente o de una cadena)
        var from = D0.AddDays(rnd.Next(-10, 5));
        DateOnly? to = rnd.Next(3) == 0 ? null : from.AddDays(rnd.Next(0, 15));
        var items = Enumerable.Range(1, 3).Where(_ => rnd.NextDouble() < 0.7)
            .Select(p => new PriceListItemView(p, Prices[rnd.Next(Prices.Length)], rnd.Next(4) == 0 ? rnd.Next(1, 50) : null)).ToList();
        int[] clients = Array.Empty<int>(), chains = Array.Empty<int>();
        var special = false;
        switch (kind)
        {
            case 1: clients = Subset(rnd, 4, 0.5); break;
            case 2: chains = Subset(rnd, 3, 0.5); break;
            case 3:
                special = true;
                if (rnd.Next(2) == 0) clients = new[] { rnd.Next(1, 5) }; else chains = new[] { rnd.Next(1, 4) };
                break;
        }
        return new PriceListView(id, $"Lista {id}", rnd.Next(5) != 0, kind == 0, from, to, clients, items, rnd.Next(5) == 0 && chains.Length == 0 ? null : chains, special);
    }

    [Fact]
    public void PriceListRules_Resolve_equals_OfferRules_Resolve_without_offers()
    {
        var rnd = new Random(247);
        for (var n = 0; n < 5000; n++)
        {
            var lists = Enumerable.Range(1, rnd.Next(0, 6)).Select(i => RandomList(rnd, i)).ToList();
            var product = rnd.Next(1, 4);
            int? client = rnd.Next(5) == 0 ? null : rnd.Next(1, 5);
            int? chain = rnd.Next(3) == 0 ? null : rnd.Next(1, 4);
            var date = D0.AddDays(rnd.Next(-5, 12));
            decimal? sale = rnd.Next(4) == 0 ? null : Prices[rnd.Next(Prices.Length)];

            var fromLists = PriceListRules.Resolve(product, client, date, lists, sale, chain);
            var fromOffers = OfferRules.Resolve(product, client, date, lists, Array.Empty<OfferView>(), sale, chain);
            Assert.True((fromLists.Price, fromLists.Source, fromLists.PriceListId, fromLists.ImageFileId, fromLists.OfferId)
                        == (fromOffers.Price, fromOffers.Source, fromOffers.PriceListId, fromOffers.ImageFileId, fromOffers.OfferId),
                $"Caso {n}: listas {fromLists} ≠ ofertas {fromOffers}");
            // solo pueden avisar los precios especiales más caros que la general (no hay ofertas)
            Assert.All(fromOffers.Notices ?? Array.Empty<PriceNotice>(), x => Assert.Equal(PriceNoticeCodes.SpecialAboveGeneral, x.Code));
            // y nunca gana algo más caro que otro candidato vigente
            var levels = PriceListRules.BestByLevel(product, client, chain, date, lists);
            var cheapest = new[] { levels.Client, levels.Chain, levels.General }.Where(x => x is not null).Select(x => (decimal?)x!.Item.Price).Min();
            if (cheapest is decimal min) Assert.Equal(min, fromLists.Price);
        }
    }

    [Fact]
    public void AudiencesOverlap_is_never_false_when_brute_force_finds_a_shared_client()
    {
        var rnd = new Random(2470);
        var modes = OfferAudienceModes.Values;
        for (var n = 0; n < 5000; n++)
        {
            // universo: 6 clientes y 3 cadenas; algunos clientes sin cadena
            var map = new Dictionary<int, int>();
            for (var c = 1; c <= 6; c++)
                if (rnd.Next(3) != 0) map[c] = rnd.Next(1, 4);
            OfferView Make(int id)
            {
                var mode = modes[rnd.Next(modes.Count)];
                var clients = mode == OfferAudienceModes.All ? Array.Empty<int>() : Subset(rnd, 6, 0.3);
                var chains = mode == OfferAudienceModes.All ? Array.Empty<int>() : Subset(rnd, 3, 0.3);
                return new OfferView(id, $"Oferta {id}", true, true, mode == OfferAudienceModes.All, D0, null, clients, new[] { new OfferItemView(1, 1m) }, mode, chains);
            }
            var a = Make(1);
            var b = Make(2);
            var shared = Enumerable.Range(1, 6).Any(c =>
            {
                int? chain = map.TryGetValue(c, out var ch) ? ch : null;
                return OfferRules.AppliesTo(a, c, chain) && OfferRules.AppliesTo(b, c, chain);
            });
            if (shared)
                Assert.True(OfferRules.AudiencesOverlap(a, b, map), $"Caso {n}: {a.Mode}({string.Join(",", a.ClientIds)}|{string.Join(",", a.ChainIds!)}) y "
                    + $"{b.Mode}({string.Join(",", b.ClientIds)}|{string.Join(",", b.ChainIds!)}) comparten un cliente.");
            // simétrica
            Assert.Equal(OfferRules.AudiencesOverlap(a, b, map), OfferRules.AudiencesOverlap(b, a, map));
        }
    }
}
