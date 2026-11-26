using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Teikem.Domain.Catalogs;
using Teikem.Domain.Clients;
using Teikem.Domain.Constants;
using Teikem.Domain.Sales;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Persistence.Interceptors;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 247 (Lote C, P3) — <see cref="OfferService"/> con el público de la oferta (ALL / INCLUDE / EXCLUDE) y las cadenas, sobre la base en memoria del mundo de
/// precios (datos INVENTADOS): alta con audienceMode o con el appliesToAll de antes, conflicto entre los dos, cambios de público con escogidos, agregar y quitar
/// cadenas, publicar, avisos de cruces con cadenas y exclusiones, la lista por cadena, la sincronía de AppliesToAll y la bitácora de OfferChain.
/// </summary>
public sealed class OfferAudienceServiceTests
{
    private static int _chainSeq;

    /// <summary>Una cadena (activa o no, de la compañía activa o de otra) con los clientes indicados como miembros.</summary>
    private static async Task<ClientChain> ChainAsync(PriceWorld x, string name, bool isActive = true, int? tenantId = null, params Client[] members)
    {
        var chain = new ClientChain
        {
            PublicId = Guid.NewGuid(), TenantId = tenantId ?? WmsFixture.TenantId, Name = name + " " + Interlocked.Increment(ref _chainSeq), IsActive = isActive,
            CreatedAtUtc = DateTime.UtcNow,
        };
        x.F.Db.Set<ClientChain>().Add(chain);
        await x.F.Db.SaveChangesAsync();
        foreach (var m in members)
        {
            var c = await x.F.Db.Clients.IgnoreQueryFilters().AsTracking().SingleAsync(k => k.ClientId == m.ClientId);
            c.ChainId = chain.ClientChainId;
        }
        await x.F.Db.SaveChangesAsync();
        x.F.Db.ChangeTracker.Clear();
        return chain;
    }

    private static async Task SetChainActiveAsync(PriceWorld x, ClientChain chain, bool active)
    {
        var row = await x.F.Db.Set<ClientChain>().IgnoreQueryFilters().AsTracking().SingleAsync(c => c.ClientChainId == chain.ClientChainId);
        row.IsActive = active;
        await x.F.Db.SaveChangesAsync();
        x.F.Db.ChangeTracker.Clear();
    }

    private static Task<OfferDto> CreateAsync(PriceWorld x, string name, string? mode = null, bool? all = null, DateOnly? from = null, DateOnly? to = null)
        => x.Offers.CreateAsync(new OfferRequest(name, from ?? x.Today.AddDays(-1), to, AppliesToAll: all, AudienceMode: mode), default);

    private static Task<OfferDto> ItemAsync(PriceWorld x, OfferDto o, Product p, decimal price)
        => x.Offers.UpsertItemsAsync(o.PublicId, new OfferItemsRequest(new[] { new OfferItemRequest(p.PublicId, price) }), default);

    private static Task<OfferDto> AddChainsAsync(PriceWorld x, OfferDto o, params ClientChain[] chains)
        => x.Offers.AddChainsAsync(o.PublicId, new OfferChainsRequest(chains.Select(c => c.PublicId).ToList()), default);

    private static Task<OfferDto> AddClientsAsync(PriceWorld x, OfferDto o, params Client[] clients)
        => x.Offers.AddClientsAsync(o.PublicId, new OfferClientsRequest(clients.Select(c => c.PublicId).ToList()), default);

    private static async Task<(string Mode, bool All)> StoredAsync(PriceWorld x, Guid publicId)
    {
        var o = await x.F.Db.Set<Offer>().IgnoreQueryFilters().AsNoTracking().SingleAsync(k => k.PublicId == publicId);
        return (o.AudienceMode, o.AppliesToAll);
    }

    // ---------------------------------------------------------------- alta

    [Fact]
    public async Task Create_with_audience_modes_and_legacy_applies_to_all_maps_to_ALL_or_INCLUDE()
    {
        await using var x = await PriceWorld.CreateAsync();
        var cases = new (string? Mode, bool? All, string Expected)[]
        {
            (null, null, OfferAudienceModes.All), (null, true, OfferAudienceModes.All), (null, false, OfferAudienceModes.Include),
            ("include", null, OfferAudienceModes.Include), (" Exclude ", null, OfferAudienceModes.Exclude), ("ALL", null, OfferAudienceModes.All),
            ("", false, OfferAudienceModes.Include),
        };
        foreach (var (mode, all, expected) in cases)
        {
            var o = await CreateAsync(x, $"Oferta {mode}/{all}", mode, all);
            Assert.Equal((expected, expected == OfferAudienceModes.All), (o.AudienceMode, o.AppliesToAll));
            Assert.Equal((expected, expected == OfferAudienceModes.All), await StoredAsync(x, o.PublicId));
            Assert.Empty(o.Chains!);
        }

        var bad = await Assert.ThrowsAsync<ValidationException>(() => CreateAsync(x, "Mala", "SOME"));
        Assert.Equal(OfferRules.AudienceInvalid, bad.Message);
        Assert.Equal("El público de la oferta debe ser ALL (todos), INCLUDE (solo los escogidos) o EXCLUDE (todos menos los escogidos).", bad.Message);
        Assert.True(bad.Errors!.ContainsKey("audienceMode"));
        // en la edición, igual
        var any = await CreateAsync(x, "Para editar");
        var badPatch = await Assert.ThrowsAsync<ValidationException>(() => x.Offers.UpdateAsync(any.PublicId, new OfferRequest(AudienceMode: "TODOS"), default));
        Assert.Equal(OfferRules.AudienceInvalid, badPatch.Message);
    }

    [Fact]
    public async Task AppliesToAll_and_audienceMode_disagreeing_is_400_AudienceConflict()
    {
        await using var x = await PriceWorld.CreateAsync();
        foreach (var (mode, all) in new[] { (OfferAudienceModes.Include, true), (OfferAudienceModes.Exclude, true), (OfferAudienceModes.All, false) })
        {
            var ex = await Assert.ThrowsAsync<ValidationException>(() => CreateAsync(x, "Choque", mode, all));
            Assert.Equal(OfferRules.AudienceConflict, ex.Message);
            Assert.True(ex.Errors!.ContainsKey("audienceMode"));
        }
        Assert.Empty(await x.F.Db.Set<Offer>().AsNoTracking().ToListAsync());

        // los que coinciden pasan
        Assert.Equal(OfferAudienceModes.Exclude, (await CreateAsync(x, "Excluye", OfferAudienceModes.Exclude, false)).AudienceMode);
        var all1 = await CreateAsync(x, "Todos", OfferAudienceModes.All, true);
        Assert.Equal(OfferAudienceModes.All, all1.AudienceMode);

        // en la edición, igual (y no cambia nada)
        var patch = await Assert.ThrowsAsync<ValidationException>(() =>
            x.Offers.UpdateAsync(all1.PublicId, new OfferRequest(AppliesToAll: true, AudienceMode: OfferAudienceModes.Exclude), default));
        Assert.Equal(OfferRules.AudienceConflict, patch.Message);
        Assert.Equal((OfferAudienceModes.All, true), await StoredAsync(x, all1.PublicId));
    }

    // ---------------------------------------------------------------- cambios de público

    [Fact]
    public async Task Changing_include_exclude_with_rows_is_422_and_to_all_with_rows_is_422()
    {
        await using var x = await PriceWorld.CreateAsync();
        var chain = await ChainAsync(x, "Walgreens");

        // con un cliente escogido
        var inc = await CreateAsync(x, "Incluye", OfferAudienceModes.Include);
        await AddClientsAsync(x, inc, x.C1);
        var toExc = await Assert.ThrowsAsync<StatusRuleException>(() => x.Offers.UpdateAsync(inc.PublicId, new OfferRequest(AudienceMode: OfferAudienceModes.Exclude), default));
        Assert.Equal("Quite los clientes y cadenas de la oferta antes de cambiar entre incluir y excluir.", toExc.Message);
        var toAll = await Assert.ThrowsAsync<StatusRuleException>(() => x.Offers.UpdateAsync(inc.PublicId, new OfferRequest(AudienceMode: OfferAudienceModes.All), default));
        Assert.Equal("La oferta tiene clientes o cadenas escogidos: quítelos antes de hacerla para todos.", toAll.Message);
        var legacyAll = await Assert.ThrowsAsync<StatusRuleException>(() => x.Offers.UpdateAsync(inc.PublicId, new OfferRequest(AppliesToAll: true), default));
        Assert.Equal(OfferRules.GeneralWithClients, legacyAll.Message);
        Assert.Equal((OfferAudienceModes.Include, false), await StoredAsync(x, inc.PublicId));

        // solo con una cadena escogida, lo mismo
        var exc = await CreateAsync(x, "Excluye", OfferAudienceModes.Exclude);
        await AddChainsAsync(x, exc, chain);
        Assert.Equal(OfferRules.AudienceChangeWithRows, (await Assert.ThrowsAsync<StatusRuleException>(() =>
            x.Offers.UpdateAsync(exc.PublicId, new OfferRequest(AudienceMode: OfferAudienceModes.Include), default))).Message);
        Assert.Equal(OfferRules.GeneralWithClients, (await Assert.ThrowsAsync<StatusRuleException>(() =>
            x.Offers.UpdateAsync(exc.PublicId, new OfferRequest(AudienceMode: OfferAudienceModes.All), default))).Message);
        // appliesToAll = false sin audienceMode: una EXCLUDE ya «no es para todos» y se queda como está
        var same = await x.Offers.UpdateAsync(exc.PublicId, new OfferRequest(AppliesToAll: false, Name: "Excluye 2"), default);
        Assert.Equal(("Excluye 2", OfferAudienceModes.Exclude), (same.Name, same.AudienceMode));
        // el mismo público no es un cambio
        Assert.Equal(OfferAudienceModes.Exclude, (await x.Offers.UpdateAsync(exc.PublicId, new OfferRequest(AudienceMode: "exclude"), default)).AudienceMode);

        // sin escogidos sí se cambia
        await x.Offers.RemoveChainAsync(exc.PublicId, chain.PublicId, default);
        var nowInc = await x.Offers.UpdateAsync(exc.PublicId, new OfferRequest(AudienceMode: OfferAudienceModes.Include), default);
        Assert.Equal((OfferAudienceModes.Include, false), (nowInc.AudienceMode, nowInc.AppliesToAll));
        var nowAll = await x.Offers.UpdateAsync(exc.PublicId, new OfferRequest(AudienceMode: OfferAudienceModes.All), default);
        Assert.Equal((OfferAudienceModes.All, true), (nowAll.AudienceMode, nowAll.AppliesToAll));
        var backExc = await x.Offers.UpdateAsync(exc.PublicId, new OfferRequest(AudienceMode: OfferAudienceModes.Exclude), default);
        Assert.Equal((OfferAudienceModes.Exclude, false), await StoredAsync(x, backExc.PublicId));
    }

    // ---------------------------------------------------------------- cadenas

    [Fact]
    public async Task Add_and_remove_chains()
    {
        await using var x = await PriceWorld.CreateAsync();
        var walg = await ChainAsync(x, "Walgreens", members: new[] { x.C1, x.C2 });
        var cvs = await ChainAsync(x, "CVS");
        var off = await ChainAsync(x, "Cerrada", isActive: false);
        var foreign = await ChainAsync(x, "Ajena", tenantId: WmsFixture.OtherTenantId);
        var inc = await CreateAsync(x, "Incluye", OfferAudienceModes.Include);

        Assert.Equal(OfferRules.ChainsRequired, (await Assert.ThrowsAsync<ValidationException>(() =>
            x.Offers.AddChainsAsync(inc.PublicId, new OfferChainsRequest(Array.Empty<Guid>()), default))).Message);
        Assert.Equal(OfferRules.ChainsRequired, (await Assert.ThrowsAsync<ValidationException>(() => x.Offers.AddChainsAsync(inc.PublicId, null, default))).Message);
        var tooMany = Enumerable.Range(0, OfferCodes.MaxChains + 1).Select(_ => Guid.NewGuid()).ToList();
        Assert.Equal("Una oferta admite como máximo 500 cadenas.", (await Assert.ThrowsAsync<ValidationException>(() =>
            x.Offers.AddChainsAsync(inc.PublicId, new OfferChainsRequest(tooMany), default))).Message);

        // de otra compañía = no existe
        var nf = await Assert.ThrowsAsync<NotFoundException>(() => AddChainsAsync(x, inc, foreign));
        Assert.Equal("Cadena no encontrada.", nf.Message);
        Assert.Equal("Cadena no encontrada.", (await Assert.ThrowsAsync<NotFoundException>(() =>
            x.Offers.AddChainsAsync(inc.PublicId, new OfferChainsRequest(new[] { walg.PublicId, Guid.NewGuid() }), default))).Message);
        // inactiva
        Assert.Equal(ClientChainRules.ChainInactive, (await Assert.ThrowsAsync<StatusRuleException>(() => AddChainsAsync(x, inc, walg, off))).Message);
        Assert.Empty(await x.F.Db.Set<OfferChain>().AsNoTracking().ToListAsync());

        // agrega; repetir (en la misma petición o en otra) no duplica
        var dto = await x.Offers.AddChainsAsync(inc.PublicId, new OfferChainsRequest(new[] { walg.PublicId, walg.PublicId }), default);
        dto = await AddChainsAsync(x, dto, walg, cvs);
        Assert.Equal(new[] { cvs.Name, walg.Name }.OrderBy(n => n, StringComparer.OrdinalIgnoreCase), dto.Chains!.Select(c => c.Name));
        var w = dto.Chains!.Single(c => c.ChainPublicId == walg.PublicId);
        Assert.Equal((true, 2), (w.IsActive, w.ClientCount));
        Assert.Equal(2, await x.F.Db.Set<OfferChain>().AsNoTracking().CountAsync(c => c.IsActive));
        var listed = Assert.Single(await x.Offers.ListAsync(null, null, false, default), o => o.PublicId == inc.PublicId);
        Assert.Equal((OfferAudienceModes.Include, 2, 0), (listed.AudienceMode, listed.ChainCount, listed.ClientCount));

        // una oferta para todos no lleva cadenas
        var all = await CreateAsync(x, "Todos");
        Assert.Equal("La oferta aplica a todos los clientes: no lleva cadenas escogidas. Desmarque «aplica a todos» para escogerlas.",
            (await Assert.ThrowsAsync<StatusRuleException>(() => AddChainsAsync(x, all, cvs))).Message);
        // EXCLUDE también acepta cadenas
        var exc = await CreateAsync(x, "Excluye", OfferAudienceModes.Exclude);
        Assert.Single((await AddChainsAsync(x, exc, cvs)).Chains!);

        // quitar
        var removed = await x.Offers.RemoveChainAsync(inc.PublicId, walg.PublicId, default);
        Assert.Equal(cvs.PublicId, Assert.Single(removed.Chains!).ChainPublicId);
        Assert.Equal(3, await x.F.Db.Set<OfferChain>().AsNoTracking().CountAsync());   // en lógico: la fila sigue
        var again = await Assert.ThrowsAsync<NotFoundException>(() => x.Offers.RemoveChainAsync(inc.PublicId, walg.PublicId, default));
        Assert.Equal("Cadena de la oferta no encontrada.", again.Message);
        Assert.Equal("Cadena no encontrada.", (await Assert.ThrowsAsync<NotFoundException>(() => x.Offers.RemoveChainAsync(inc.PublicId, foreign.PublicId, default))).Message);
        // volver a agregarla crea una fila nueva activa
        Assert.Equal(2, (await AddChainsAsync(x, removed, walg)).Chains!.Count);
    }

    [Fact]
    public async Task Removing_the_last_client_or_chain_of_an_INCLUDE_offer_is_409_and_EXCLUDE_can_be_emptied()
    {
        await using var x = await PriceWorld.CreateAsync();
        var p = await x.ProductAsync("A", 10m);
        var walg = await ChainAsync(x, "Walgreens");

        async Task Last(Func<Task> act)
        {
            var ex = await Assert.ThrowsAsync<ConflictException>(act);
            Assert.Equal("Una oferta «solo para» los escogidos necesita al menos un cliente o una cadena: agregue otro antes de quitar el último, o desactive la oferta.", ex.Message);
        }

        // un cliente y una cadena: se quita uno; el que queda, no (ni el cliente ni la cadena)
        var inc = await ItemAsync(x, await CreateAsync(x, "Solo C1 y Walgreens", OfferAudienceModes.Include), p, 5m);
        await AddClientsAsync(x, inc, x.C1);
        await AddChainsAsync(x, inc, walg);
        var left = await x.Offers.RemoveClientAsync(inc.PublicId, x.C1.PublicId, default);
        Assert.Equal((0, 1), (left.Clients.Count, left.Chains!.Count));
        await Last(() => x.Offers.RemoveChainAsync(inc.PublicId, walg.PublicId, default));
        await AddClientsAsync(x, inc, x.C2);
        await x.Offers.RemoveChainAsync(inc.PublicId, walg.PublicId, default);
        await Last(() => x.Offers.RemoveClientAsync(inc.PublicId, x.C2.PublicId, default));
        // nada cambió: sigue «solo para» C2, no para todos
        var stored = await x.Offers.GetAsync(inc.PublicId, default);
        Assert.Equal((OfferAudienceModes.Include, false, 1), (stored.AudienceMode, stored.AppliesToAll, stored.Clients.Count));
        Assert.Equal(5m, Assert.Single((await x.DraftAsync(x.C2, new SalesOrderLineRequest(p.PublicId, 1m))).Lines).UnitPrice);
        Assert.Equal(10m, Assert.Single((await x.DraftAsync(x.C1, new SalesOrderLineRequest(p.PublicId, 1m))).Lines).UnitPrice);

        // una EXCLUDE sí se vacía (vale para todos)
        var exc = await CreateAsync(x, "Todos menos Walgreens", OfferAudienceModes.Exclude);
        await AddChainsAsync(x, exc, walg);
        var emptied = await x.Offers.RemoveChainAsync(exc.PublicId, walg.PublicId, default);
        Assert.Equal((OfferAudienceModes.Exclude, 0), (emptied.AudienceMode, emptied.Chains!.Count));
    }

    [Fact]
    public async Task Publish_include_without_rows_is_422_exclude_without_rows_publishes()
    {
        await using var x = await PriceWorld.CreateAsync();
        var p = await x.ProductAsync("A", 10m);
        var chain = await ChainAsync(x, "Walgreens");

        var inc = await ItemAsync(x, await CreateAsync(x, "Incluye", OfferAudienceModes.Include), p, 5m);
        await x.Offers.UnpublishAsync(inc.PublicId, default);   // las de antes del lote 234 podían quedar en borrador
        var ex = await Assert.ThrowsAsync<StatusRuleException>(() => x.Offers.PublishAsync(inc.PublicId, default));
        Assert.Equal("Escoja al menos un cliente o una cadena, o marque que la oferta aplica a todos los clientes.", ex.Message);
        // con solo una cadena, se publica
        await AddChainsAsync(x, inc, chain);
        Assert.True((await x.Offers.PublishAsync(inc.PublicId, default)).IsPublished);

        var exc = await ItemAsync(x, await CreateAsync(x, "Excluye", OfferAudienceModes.Exclude), p, 6m);
        await x.Offers.UnpublishAsync(exc.PublicId, default);
        var published = await x.Offers.PublishAsync(exc.PublicId, default);
        Assert.Equal((true, OfferAudienceModes.Exclude, false), (published.IsPublished, published.AudienceMode, published.AppliesToAll));
    }

    // ---------------------------------------------------------------- avisos

    [Fact]
    public async Task Warnings_consider_chains_exclusions_and_ignore_inactive_chains_in_the_map()
    {
        await using var x = await PriceWorld.CreateAsync();
        var p = await x.ProductAsync("A", 10m);
        var c3 = await x.F.AddClientAsync("C3");
        var walg = await ChainAsync(x, "Walgreens", members: new[] { x.C1 });

        // A incluye la cadena Walgreens; B incluye a C1 (miembro de Walgreens) más barata → aviso en A
        var a = await ItemAsync(x, await CreateAsync(x, "A por cadena", OfferAudienceModes.Include), p, 5m);
        a = await AddChainsAsync(x, a, walg);
        var b = await ItemAsync(x, await CreateAsync(x, "B por cliente", OfferAudienceModes.Include), p, 4m);
        b = await AddClientsAsync(x, b, x.C1);
        var w = Assert.Single((await x.Offers.GetAsync(a.PublicId, default)).Warnings, k => k.Code == PriceNoticeCodes.CheaperOffer);
        Assert.Equal((b.PublicId, 4m), (w.OtherOfferPublicId!.Value, w.OtherPrice!.Value));

        // con la cadena desactivada, C1 ya no cuenta como de Walgreens: no se cruzan
        await SetChainActiveAsync(x, walg, false);
        Assert.DoesNotContain((await x.Offers.GetAsync(a.PublicId, default)).Warnings, k => k.Code == PriceNoticeCodes.CheaperOffer);
        await SetChainActiveAsync(x, walg, true);

        // B pasa a excluir (el último escogido de una INCLUDE no se quita: se desactiva y se arma otra B que excluye a C1) → A (solo la cadena Walgreens, que no
        // está excluida) sigue cruzándose
        await x.Offers.DeactivateAsync(b.PublicId, default);
        b = await ItemAsync(x, await CreateAsync(x, "B excluye", OfferAudienceModes.Exclude), p, 4m);
        await AddClientsAsync(x, b, x.C1);
        Assert.Contains((await x.Offers.GetAsync(a.PublicId, default)).Warnings, k => k.Code == PriceNoticeCodes.CheaperOffer);
        // B excluye además la cadena Walgreens: A no alcanza a nadie de B
        await x.Offers.AddChainsAsync(b.PublicId, new OfferChainsRequest(new[] { walg.PublicId }), default);
        Assert.DoesNotContain((await x.Offers.GetAsync(a.PublicId, default)).Warnings, k => k.Code == PriceNoticeCodes.CheaperOffer);

        // C: incluye a C1 y C3; B excluye a C1 y a Walgreens, pero no a C3 → se cruzan
        var c = await ItemAsync(x, await CreateAsync(x, "C por clientes", OfferAudienceModes.Include), p, 6m);
        c = await AddClientsAsync(x, c, x.C1, c3);
        Assert.Contains((await x.Offers.GetAsync(c.PublicId, default)).Warnings, k => k.Code == PriceNoticeCodes.CheaperOffer && k.OtherOfferPublicId == b.PublicId);
        // D: INCLUDE sin escogidos no alcanza a nadie: no se cruza con nada
        var d = await ItemAsync(x, await CreateAsync(x, "D vacía", OfferAudienceModes.Include), p, 9m);
        Assert.DoesNotContain(d.Warnings, k => k.Code == PriceNoticeCodes.CheaperOffer);
    }

    // ---------------------------------------------------------------- lista por cadena

    [Fact]
    public async Task List_by_chain_returns_offers_that_apply_to_the_chain()
    {
        await using var x = await PriceWorld.CreateAsync();
        var walg = await ChainAsync(x, "Walgreens");
        var cvs = await ChainAsync(x, "CVS");
        var foreign = await ChainAsync(x, "Ajena", tenantId: WmsFixture.OtherTenantId);

        var all = await CreateAsync(x, "Para todos");
        var incW = await AddChainsAsync(x, await CreateAsync(x, "Incluye Walgreens", OfferAudienceModes.Include), walg);
        await AddChainsAsync(x, await CreateAsync(x, "Incluye CVS", OfferAudienceModes.Include), cvs);
        await AddChainsAsync(x, await CreateAsync(x, "Excluye Walgreens", OfferAudienceModes.Exclude), walg);
        var excC = await AddChainsAsync(x, await CreateAsync(x, "Excluye CVS", OfferAudienceModes.Exclude), cvs);
        var excEmpty = await CreateAsync(x, "Excluye a nadie", OfferAudienceModes.Exclude);
        var future = await CreateAsync(x, "Programada", from: x.Today.AddDays(5));
        await CreateAsync(x, "Vencida", from: x.Today.AddDays(-30), to: x.Today.AddDays(-1));
        var deactivated = await CreateAsync(x, "Desactivada");
        await x.Offers.DeactivateAsync(deactivated.PublicId, default);
        var draft = await CreateAsync(x, "Borrador");
        await x.Offers.UnpublishAsync(draft.PublicId, default);
        // una fila quitada no cuenta: incluye Walgreens y CVS y quita Walgreens (el último de una INCLUDE no se quita)
        var removed = await AddChainsAsync(x, await CreateAsync(x, "Quitada", OfferAudienceModes.Include), walg, cvs);
        await x.Offers.RemoveChainAsync(removed.PublicId, walg.PublicId, default);

        var forWalg = await x.Offers.ListAsync(null, null, false, default, walg.PublicId);
        Assert.Equal(new[] { all.PublicId, incW.PublicId, excC.PublicId, excEmpty.PublicId, future.PublicId }.OrderBy(g => g),
            forWalg.Select(o => o.PublicId).OrderBy(g => g));
        Assert.Equal((OfferAudienceModes.Include, 1), forWalg.Where(o => o.PublicId == incW.PublicId).Select(o => (o.AudienceMode!, o.ChainCount)).Single());
        // includeInactive no trae las desactivadas cuando se pide por cadena; q y state se aplican
        Assert.DoesNotContain(await x.Offers.ListAsync(null, null, true, default, walg.PublicId), o => o.PublicId == deactivated.PublicId);
        Assert.Equal(incW.PublicId, Assert.Single(await x.Offers.ListAsync("Incluye", null, false, default, walg.PublicId)).PublicId);
        Assert.Equal(future.PublicId, Assert.Single(await x.Offers.ListAsync(null, "SCHEDULED", false, default, walg.PublicId)).PublicId);

        var nf = await Assert.ThrowsAsync<NotFoundException>(() => x.Offers.ListAsync(null, null, false, default, foreign.PublicId));
        Assert.Equal("Cadena no encontrada.", nf.Message);
        await Assert.ThrowsAsync<NotFoundException>(() => x.Offers.ListAsync(null, null, false, default, Guid.NewGuid()));
    }

    // ---------------------------------------------------------------- sincronía y bitácora

    [Fact]
    public async Task Every_path_keeps_AppliesToAll_in_sync_with_AudienceMode()
    {
        await using var x = await PriceWorld.CreateAsync();
        var p = await x.ProductAsync("A", 10m);
        var chain = await ChainAsync(x, "Walgreens");

        async Task CheckAllAsync()
        {
            foreach (var o in await x.F.Db.Set<Offer>().IgnoreQueryFilters().AsNoTracking().ToListAsync())
                Assert.Equal(o.AudienceMode == OfferAudienceModes.All, o.AppliesToAll);
        }

        var o1 = await CreateAsync(x, "Uno", all: false);
        await CheckAllAsync();
        await x.Offers.UpdateAsync(o1.PublicId, new OfferRequest(AppliesToAll: true), default);
        await CheckAllAsync();
        await x.Offers.UpdateAsync(o1.PublicId, new OfferRequest(AudienceMode: OfferAudienceModes.Exclude), default);
        await CheckAllAsync();
        await AddChainsAsync(x, o1, chain);
        await AddClientsAsync(x, o1, x.C1);
        await ItemAsync(x, o1, p, 5m);
        await x.Offers.UnpublishAsync(o1.PublicId, default);
        await x.Offers.PublishAsync(o1.PublicId, default);
        await x.Offers.DeactivateAsync(o1.PublicId, default);
        await x.Offers.ReactivateAsync(o1.PublicId, default);
        await CheckAllAsync();
        await x.Offers.RemoveChainAsync(o1.PublicId, chain.PublicId, default);
        await x.Offers.RemoveClientAsync(o1.PublicId, x.C1.PublicId, default);
        await x.Offers.UpdateAsync(o1.PublicId, new OfferRequest(AudienceMode: OfferAudienceModes.All), default);
        await CheckAllAsync();
        await x.Offers.UpdateAsync(o1.PublicId, new OfferRequest(AppliesToAll: false), default);
        Assert.Equal((OfferAudienceModes.Include, false), await StoredAsync(x, o1.PublicId));
        await CreateAsync(x, "Dos", OfferAudienceModes.Exclude, false);
        await CreateAsync(x, "Tres", OfferAudienceModes.Include);
        await CheckAllAsync();
    }

    [Fact]
    public async Task OfferChain_leaves_an_audit_log_with_the_OFFER_entity_type()
    {
        var tenant = new TenantContext { TenantId = 1, UserId = 7, IsAuthenticated = true };
        var lookups = new TripTestLookups();
        lookups.Load(new[]
        {
            new LookupCode { LookupCodeId = 1, Entity = LookupDomains.EntityType, InternalCode = OfferCodes.EntityType, LabelJson = "{}", IsActive = true },
            new LookupCode { LookupCodeId = 3, Entity = LookupDomains.AuditAction, InternalCode = AuditActions.Create, LabelJson = "{}", IsActive = true },
            new LookupCode { LookupCodeId = 4, Entity = LookupDomains.AuditAction, InternalCode = AuditActions.Update, LabelJson = "{}", IsActive = true },
            new LookupCode { LookupCodeId = 5, Entity = LookupDomains.AuditAction, InternalCode = AuditActions.Delete, LabelJson = "{}", IsActive = true },
            new LookupCode { LookupCodeId = 6, Entity = LookupDomains.AuditAction, InternalCode = AuditActions.Restore, LabelJson = "{}", IsActive = true },
        });
        var options = new DbContextOptionsBuilder<TeikemDbContext>()
            .UseInMemoryDatabase("offer-chain-audit-" + Guid.NewGuid(), b => b.EnableNullChecks(false))
            .AddInterceptors(new AuditSaveChangesInterceptor(tenant, lookups, NullLogger<AuditSaveChangesInterceptor>.Instance))
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        await using var db = new TeikemDbContext(options, tenant);
        var chain = new ClientChain { ClientChainId = 30, PublicId = Guid.NewGuid(), TenantId = 1, Name = "Walgreens", IsActive = true, CreatedAtUtc = DateTime.UtcNow };
        db.Set<ClientChain>().Add(chain);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        // las piezas de archivos, productos y kits no se usan en estos caminos
        var svc = new OfferService(db, tenant, TenantClock.Default, lookups, null!, null!, null!);

        // EXCLUDE: el último escogido de una INCLUDE no se quita (Lote 247)
        var offer = await svc.CreateAsync(new OfferRequest("Circular", TenantClock.Default.Today, AudienceMode: OfferAudienceModes.Exclude), default);
        var before = await db.AuditLogs.IgnoreQueryFilters().AsNoTracking().CountAsync(l => l.EntityTypeLookupId == 1);
        await svc.AddChainsAsync(offer.PublicId, new OfferChainsRequest(new[] { chain.PublicId }), default);
        var logs = await db.AuditLogs.IgnoreQueryFilters().AsNoTracking().Where(l => l.EntityTypeLookupId == 1).OrderBy(l => l.AuditLogId).ToListAsync();
        Assert.True(logs.Count > before);
        Assert.Contains(logs.Skip(before), l => l.ActionLookupId == 3 && l.ChangesJson!.Contains("ChainId") && l.ChangesJson.Contains("30"));

        // quitarla (en lógico) también deja bitácora
        var afterAdd = logs.Count;
        await svc.RemoveChainAsync(offer.PublicId, chain.PublicId, default);
        var afterRemove = await db.AuditLogs.IgnoreQueryFilters().AsNoTracking().Where(l => l.EntityTypeLookupId == 1).OrderBy(l => l.AuditLogId).ToListAsync();
        Assert.Contains(afterRemove.Skip(afterAdd), l => l.ChangesJson!.Contains("IsActive"));
    }
}
