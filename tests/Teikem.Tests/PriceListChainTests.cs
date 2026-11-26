using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Teikem.Domain.Catalogs;
using Teikem.Domain.Clients;
using Teikem.Domain.Constants;
using Teikem.Domain.Sales;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Persistence.Interceptors;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 247 (Lote C, P4) — las cadenas en las listas de precios (<see cref="PriceListService"/>) sobre la base en memoria del mundo de precios (datos INVENTADOS):
/// agregar y quitar cadenas con sus 400/404/422, publicar exige clientes o cadenas, la guarda de «no dejar sin público» una publicada, el cruce de dos listas
/// normales por cadena (409) frente a una de cliente y una de cadena (conviven), las dedicadas ocultas por omisión, el precio general y los avisos de la ficha, y la
/// bitácora de PriceListChain.
/// </summary>
public sealed class PriceListChainTests
{
    private static Task<PriceListDto> AddChainsAsync(PriceWorld x, PriceListDto l, params Guid[] chains)
        => x.Lists.AddChainsAsync(l.PublicId, new PriceListChainsRequest(chains), default);

    private static async Task<PriceListDto> ChainListAsync(PriceWorld x, string name, ClientChain chain, Teikem.Domain.Wms.Product p, decimal price, bool publish = true)
    {
        var l = await x.Lists.CreateAsync(new PriceListRequest(name, x.Today.AddDays(-1), null, AppliesToAll: false), default);
        await x.Lists.UpsertItemsAsync(l.PublicId, new PriceListItemsRequest(new[] { new PriceListItemRequest(p.PublicId, price) }), default);
        l = await AddChainsAsync(x, l, chain.PublicId);
        return publish ? await x.Lists.PublishAsync(l.PublicId, default) : l;
    }

    [Fact]
    public async Task Add_remove_chains_publish_needs_client_or_chain_and_the_last_audience_guard()
    {
        await using var x = await PriceWorld.CreateAsync();
        var p = await x.ProductAsync("P", 10m);
        var chain = await x.ChainAsync("Walgreens");
        var member = await x.ClientInAsync("W1", chain);
        await x.ClientInAsync("W2", chain);
        var inactiveChain = await x.ChainAsync("Cerrada", isActive: false);

        var l = await x.Lists.CreateAsync(new PriceListRequest("Por cadena", x.Today.AddDays(-1), null, AppliesToAll: false), default);
        await x.Lists.UpsertItemsAsync(l.PublicId, new PriceListItemsRequest(new[] { new PriceListItemRequest(p.PublicId, 8m) }), default);
        var publish = await Assert.ThrowsAsync<StatusRuleException>(() => x.Lists.PublishAsync(l.PublicId, default));
        Assert.Equal("Escoja al menos un cliente o una cadena, o marque que la lista aplica a todos los clientes.", publish.Message);

        // 400, 404 y 422
        var none = await Assert.ThrowsAsync<ValidationException>(() => AddChainsAsync(x, l));
        Assert.Equal(("Escoja las cadenas de la lista.", true), (none.Message, none.Errors!.ContainsKey("chainPublicIds")));
        await Assert.ThrowsAsync<ValidationException>(() => x.Lists.AddChainsAsync(l.PublicId, null, default));
        var many = await Assert.ThrowsAsync<ValidationException>(() => AddChainsAsync(x, l, Enumerable.Range(0, 501).Select(_ => Guid.NewGuid()).ToArray()));
        Assert.Equal("Una lista admite como máximo 500 cadenas.", many.Message);
        Assert.Equal("Cadena no encontrada.", (await Assert.ThrowsAsync<NotFoundException>(() => AddChainsAsync(x, l, Guid.NewGuid()))).Message);
        var foreign = new ClientChain { PublicId = Guid.NewGuid(), TenantId = WmsFixture.TenantId + 100, Name = "Ajena", IsActive = true, CreatedAtUtc = DateTime.UtcNow };
        x.F.Db.Set<ClientChain>().Add(foreign);
        await x.F.Db.SaveChangesAsync();
        x.F.Db.ChangeTracker.Clear();
        Assert.Equal("Cadena no encontrada.", (await Assert.ThrowsAsync<NotFoundException>(() => AddChainsAsync(x, l, foreign.PublicId))).Message);
        Assert.Equal(ClientChainRules.ChainInactive, (await Assert.ThrowsAsync<StatusRuleException>(() => AddChainsAsync(x, l, inactiveChain.PublicId))).Message);

        // agregar (repetir no duplica) y publicar
        l = await AddChainsAsync(x, l, chain.PublicId);
        l = await AddChainsAsync(x, l, chain.PublicId);
        var dtoChain = Assert.Single(l.Chains!);
        Assert.Equal((chain.PublicId, chain.Name, true, 2), (dtoChain.ChainPublicId, dtoChain.Name, dtoChain.IsActive, dtoChain.ClientCount));
        Assert.Equal(1, await x.F.Db.Set<PriceListChain>().AsNoTracking().CountAsync(c => c.IsActive));
        l = await x.Lists.PublishAsync(l.PublicId, default);
        Assert.Equal(8m, Assert.Single((await x.DraftAsync(member, new SalesOrderLineRequest(p.PublicId, 1m))).Lines).UnitPrice);
        Assert.Equal(10m, Assert.Single((await x.DraftAsync(x.C1, new SalesOrderLineRequest(p.PublicId, 1m))).Lines).UnitPrice);

        // la última audiencia de una publicada no se quita; con un cliente, sí
        var last = await Assert.ThrowsAsync<StatusRuleException>(() => x.Lists.RemoveChainAsync(l.PublicId, chain.PublicId, default));
        Assert.Equal(PriceListRules.PublishNeedsClients, last.Message);
        await x.Lists.AddClientsAsync(l.PublicId, new PriceListClientsRequest(new[] { x.C1.PublicId }), default);
        var lastClient = await Assert.ThrowsAsync<StatusRuleException>(() => x.Lists.UpdateAsync(l.PublicId, new PriceListRequest(AppliesToAll: true), default));
        Assert.Equal("La lista tiene clientes o cadenas escogidos: quítelos antes de hacerla general (aplica a todos).", lastClient.Message);
        l = await x.Lists.RemoveChainAsync(l.PublicId, chain.PublicId, default);
        Assert.Empty(l.Chains!);
        Assert.Equal(PriceListRules.PublishNeedsClients, (await Assert.ThrowsAsync<StatusRuleException>(() => x.Lists.RemoveClientAsync(l.PublicId, x.C1.PublicId, default))).Message);
        var notIn = await Assert.ThrowsAsync<NotFoundException>(() => x.Lists.RemoveChainAsync(l.PublicId, chain.PublicId, default));
        Assert.Equal("Cadena de la lista no encontrada.", notIn.Message);
        Assert.Equal("Cadena no encontrada.", (await Assert.ThrowsAsync<NotFoundException>(() => x.Lists.RemoveChainAsync(l.PublicId, Guid.NewGuid(), default))).Message);

        // una general no lleva cadenas; una en borrador con cadenas no se hace general
        var general = await x.Lists.CreateAsync(new PriceListRequest("General", x.Today, null), default);
        Assert.Equal("La lista aplica a todos los clientes: no lleva cadenas escogidas. Desmarque «aplica a todos» para escogerlas.",
            (await Assert.ThrowsAsync<StatusRuleException>(() => AddChainsAsync(x, general, chain.PublicId))).Message);
        var draft = await ChainListAsync(x, "Borrador", chain, p, 7m, publish: false);
        Assert.Equal(PriceListRules.GeneralWithClients,
            (await Assert.ThrowsAsync<StatusRuleException>(() => x.Lists.UpdateAsync(draft.PublicId, new PriceListRequest(AppliesToAll: true), default))).Message);
    }

    [Fact]
    public async Task Two_normal_lists_sharing_a_chain_with_same_product_and_dates_is_409()
    {
        await using var x = await PriceWorld.CreateAsync();
        var p = await x.ProductAsync("SKU-9", 10m);
        var chain = await x.ChainAsync("Walgreens");
        var other = await x.ChainAsync("CVS");
        var member = await x.ClientInAsync("W1", chain);

        await ChainListAsync(x, "Cadena 1", chain, p, 8m);
        var second = await ChainListAsync(x, "Cadena 2", chain, p, 7m, publish: false);
        var ex = await Assert.ThrowsAsync<ConflictException>(() => x.Lists.PublishAsync(second.PublicId, default));
        Assert.Equal(PriceListRules.Overlap("Cadena 1", new[] { "SKU-9" }), ex.Message);

        // la de otra cadena y la del cliente miembro conviven con la de su cadena (gana el más bajo)
        await ChainListAsync(x, "Otra cadena", other, p, 6m);
        await x.PublishedListAsync("Del miembro", x.Today.AddDays(-1), null, new[] { member }, (p, 7.5m));
        Assert.Equal(7.5m, Assert.Single((await x.DraftAsync(member, new SalesOrderLineRequest(p.PublicId, 1m))).Lines).UnitPrice);
    }

    [Fact]
    public async Task List_hides_special_lists_unless_includeSpecial()
    {
        await using var x = await PriceWorld.CreateAsync();
        var p = await x.ProductAsync("P", 10m);
        var chain = await x.ChainAsync("Walgreens");
        var normal = await ChainListAsync(x, "Normal por cadena", chain, p, 9m);
        await x.Lists.AddClientSpecialPriceAsync(x.C1.PublicId, new SpecialPriceRequest(p.PublicId, 8m, x.Today), default);
        await x.Lists.AddChainSpecialPriceAsync(chain.PublicId, new SpecialPriceRequest(p.PublicId, 7m, x.Today), default);

        var plain = await x.Lists.ListAsync(null, null, false, default);
        var row = Assert.Single(plain);
        Assert.Equal((normal.PublicId, 1, 0, (string?)null, (string?)null), (row.PublicId, row.ChainCount, row.ClientCount, row.SpecialOwnerKind, row.SpecialOwnerName));

        var all = await x.Lists.ListAsync(null, null, false, default, includeSpecial: true);
        Assert.Equal(3, all.Count);
        var client = all.Single(r => r.SpecialOwnerKind == SpecialPriceOwnerKinds.Client);
        Assert.Equal((x.C1.Name, 1, 1, 0), (client.SpecialOwnerName, client.ItemCount, client.ClientCount, client.ChainCount));
        var ofChain = all.Single(r => r.SpecialOwnerKind == SpecialPriceOwnerKinds.Chain);
        Assert.Equal((chain.Name, 1), (ofChain.SpecialOwnerName, ofChain.ChainCount));
        Assert.Equal(3, (await x.Lists.ListAsync(null, PriceListStates.Active, false, default, includeSpecial: true)).Count);
    }

    [Fact]
    public async Task Price_list_dto_brings_general_price_and_warnings()
    {
        await using var x = await PriceWorld.CreateAsync();
        var p = await x.ProductAsync("SKU-1", 12m);
        var q = await x.ProductAsync("SKU-2", 12m);
        var g1 = await x.PublishedListAsync("General hoy", x.Today.AddDays(-10), x.Today.AddDays(14), null, (p, 9m), (q, 4m));
        await x.PublishedListAsync("General después", x.Today.AddDays(15), null, null, (p, 11m));

        var now = await x.PublishedListAsync("Negociada", x.Today, null, new[] { x.C1 }, (p, 10m), (q, 3m));
        Assert.Equal(new decimal?[] { 9m, 4m }, now.Items.Select(i => i.GeneralListPrice));
        var w = Assert.Single(now.Warnings!);
        Assert.Equal((PriceNoticeCodes.SpecialAboveGeneral, p.PublicId, "SKU-1", 10m, (decimal?)9m), (w.Code, w.ProductPublicId, w.Sku, w.Price, w.GeneralListPrice));
        Assert.Equal(PriceListRules.SpecialAboveGeneral("SKU-1", 10m, 9m), w.Message);

        // una que empieza más adelante se compara con la general de su «desde»
        var later = await x.PublishedListAsync("Programada", x.Today.AddDays(20), null, new[] { x.C2 }, (p, 10m), (q, 3m));
        Assert.Equal(new decimal?[] { 11m, null }, later.Items.Select(i => i.GeneralListPrice));
        Assert.Empty(later.Warnings!);

        // la general no se compara consigo misma
        var general = await x.Lists.GetAsync(g1.PublicId, default);
        Assert.All(general.Items, i => Assert.Null(i.GeneralListPrice));
        Assert.Empty(general.Warnings!);
        Assert.Empty(general.Chains!);
        Assert.Null(general.SpecialOwnerKind);
    }

    [Fact]
    public async Task PriceListChain_leaves_an_audit_log_with_the_PRICE_LIST_entity_type()
    {
        var tenant = new TenantContext { TenantId = 1, UserId = 7, IsAuthenticated = true };
        var lookups = new TripTestLookups();
        lookups.Load(new[]
        {
            new LookupCode { LookupCodeId = 1, Entity = LookupDomains.EntityType, InternalCode = PriceListCodes.EntityType, LabelJson = "{}", IsActive = true },
            new LookupCode { LookupCodeId = 3, Entity = LookupDomains.AuditAction, InternalCode = AuditActions.Create, LabelJson = "{}", IsActive = true },
            new LookupCode { LookupCodeId = 4, Entity = LookupDomains.AuditAction, InternalCode = AuditActions.Update, LabelJson = "{}", IsActive = true },
            new LookupCode { LookupCodeId = 5, Entity = LookupDomains.AuditAction, InternalCode = AuditActions.Delete, LabelJson = "{}", IsActive = true },
            new LookupCode { LookupCodeId = 6, Entity = LookupDomains.AuditAction, InternalCode = AuditActions.Restore, LabelJson = "{}", IsActive = true },
        });
        var options = new DbContextOptionsBuilder<TeikemDbContext>()
            .UseInMemoryDatabase("price-list-chain-audit-" + Guid.NewGuid(), b => b.EnableNullChecks(false))
            .AddInterceptors(new AuditSaveChangesInterceptor(tenant, lookups, NullLogger<AuditSaveChangesInterceptor>.Instance))
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        await using var db = new TeikemDbContext(options, tenant);
        var chain = new ClientChain { ClientChainId = 40, PublicId = Guid.NewGuid(), TenantId = 1, Name = "Walgreens", IsActive = true, CreatedAtUtc = DateTime.UtcNow };
        db.Set<ClientChain>().Add(chain);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        // el almacén de archivos no se usa en estos caminos
        var svc = new PriceListService(db, tenant, TenantClock.Default, lookups, null!);

        var list = await svc.CreateAsync(new PriceListRequest("Por cadena", TenantClock.Default.Today, AppliesToAll: false), default);
        var before = await db.AuditLogs.IgnoreQueryFilters().AsNoTracking().CountAsync(l => l.EntityTypeLookupId == 1);
        await svc.AddChainsAsync(list.PublicId, new PriceListChainsRequest(new[] { chain.PublicId }), default);
        var logs = await db.AuditLogs.IgnoreQueryFilters().AsNoTracking().Where(l => l.EntityTypeLookupId == 1).OrderBy(l => l.AuditLogId).ToListAsync();
        Assert.True(logs.Count > before);
        Assert.Contains(logs.Skip(before), l => l.ActionLookupId == 3 && l.ChangesJson!.Contains("ChainId") && l.ChangesJson.Contains("40"));

        var afterAdd = logs.Count;
        await svc.RemoveChainAsync(list.PublicId, chain.PublicId, default);
        var afterRemove = await db.AuditLogs.IgnoreQueryFilters().AsNoTracking().Where(l => l.EntityTypeLookupId == 1).OrderBy(l => l.AuditLogId).ToListAsync();
        Assert.Contains(afterRemove.Skip(afterAdd), l => l.ChangesJson!.Contains("IsActive"));
    }
}
