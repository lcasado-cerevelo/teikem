using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Teikem.Domain.Clients;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Clients;
using Teikem.Infrastructure.Persistence;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 245 (Lote B del plan) — ClientChainQueries, el único punto de lectura de la cadena de los clientes: respeta el filtro de tenant (la compañía B no resuelve
/// el PublicId de una cadena de A ni la ve por id), el mapa cliente → cadena solo trae a los clientes con cadena (activos o no) y una lista vacía no consulta nada.
/// </summary>
public sealed class ClientChainQueriesTests
{
    private const int TenantA = 1;
    private const int TenantB = 2;

    private sealed record World(TeikemDbContext Db, TenantContext Tenant, ClientChain Walgreens, ClientChain Cvs, ClientChain OldChain, ClientChain OtherTenant,
        Client InWalgreens, Client InactiveInWalgreens, Client InCvs, Client NoChain, Client OtherTenantClient);

    private static async Task<World> SeedAsync()
    {
        var tenant = new TenantContext { TenantId = TenantA, UserId = 1, IsAuthenticated = true };
        var options = new DbContextOptionsBuilder<TeikemDbContext>()
            .UseInMemoryDatabase("client-chain-queries-" + Guid.NewGuid(), b => b.EnableNullChecks(false))
            .ConfigureWarnings(w => w.Ignore(CoreEventId.PossibleIncorrectRequiredNavigationWithQueryFilterInteractionWarning))
            .Options;
        var db = new TeikemDbContext(options, tenant);

        ClientChain Chain(int tenantId, string name, bool isActive = true)
        {
            var c = new ClientChain { PublicId = Guid.NewGuid(), TenantId = tenantId, Name = name, IsActive = isActive, CreatedAtUtc = DateTime.UtcNow };
            db.Set<ClientChain>().Add(c);
            return c;
        }
        var walgreens = Chain(TenantA, "Walgreens");
        var cvs = Chain(TenantA, "CVS");
        var old = Chain(TenantA, "Cadena vieja", isActive: false);
        var other = Chain(TenantB, "Walgreens B");
        await db.SaveChangesAsync();

        Client NewClient(int tenantId, string code, int? chainId, bool isActive = true)
        {
            var c = new Client
            {
                PublicId = Guid.NewGuid(), TenantId = tenantId, Code = code, Name = "Cliente " + code, StatusCodeId = 1, IsActive = isActive,
                CreatedAtUtc = DateTime.UtcNow, ChainId = chainId,
            };
            db.Clients.Add(c);
            return c;
        }
        var inW = NewClient(TenantA, "W1", walgreens.ClientChainId);
        var inactiveW = NewClient(TenantA, "W2", walgreens.ClientChainId, isActive: false);
        var inC = NewClient(TenantA, "C1", cvs.ClientChainId);
        var none = NewClient(TenantA, "N1", null);
        var otherClient = NewClient(TenantB, "B1", other.ClientChainId);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return new World(db, tenant, walgreens, cvs, old, other, inW, inactiveW, inC, none, otherClient);
    }

    [Fact]
    public async Task ChainsByIdAsync_returns_only_the_chains_of_the_tenant_active_or_not()
    {
        var w = await SeedAsync();
        var map = await ClientChainQueries.ChainsByIdAsync(w.Db,
            new[] { w.Walgreens.ClientChainId, w.OldChain.ClientChainId, w.OtherTenant.ClientChainId, w.Walgreens.ClientChainId });
        Assert.Equal(new[] { w.Walgreens.ClientChainId, w.OldChain.ClientChainId }.OrderBy(x => x), map.Keys.OrderBy(x => x));
        Assert.Equal(new ChainRef(w.Walgreens.ClientChainId, w.Walgreens.PublicId, "Walgreens", true), map[w.Walgreens.ClientChainId]);
        Assert.False(map[w.OldChain.ClientChainId].IsActive);
        Assert.Empty(await ClientChainQueries.ChainsByIdAsync(w.Db, Array.Empty<int>()));
        // la compañía B solo ve la suya
        using (w.Tenant.As(TenantB))
        {
            var mapB = await ClientChainQueries.ChainsByIdAsync(w.Db, new[] { w.Walgreens.ClientChainId, w.OtherTenant.ClientChainId });
            Assert.Equal(new[] { w.OtherTenant.ClientChainId }, mapB.Keys);
        }
    }

    [Fact]
    public async Task ClientChainMapAsync_maps_only_clients_with_a_chain_of_the_tenant()
    {
        var w = await SeedAsync();
        var all = await ClientChainQueries.ClientChainMapAsync(w.Db, null);
        Assert.Equal(3, all.Count);   // W1, W2 (inactivo, conserva la cadena) y C1; sin N1 ni el de la otra compañía
        Assert.Equal(w.Walgreens.ClientChainId, all[w.InWalgreens.ClientId]);
        Assert.Equal(w.Walgreens.ClientChainId, all[w.InactiveInWalgreens.ClientId]);
        Assert.Equal(w.Cvs.ClientChainId, all[w.InCvs.ClientId]);
        Assert.False(all.ContainsKey(w.NoChain.ClientId));
        Assert.False(all.ContainsKey(w.OtherTenantClient.ClientId));

        var some = await ClientChainQueries.ClientChainMapAsync(w.Db, new[] { w.InCvs.ClientId, w.NoChain.ClientId, w.OtherTenantClient.ClientId });
        Assert.Equal(new[] { w.InCvs.ClientId }, some.Keys);
        Assert.Empty(await ClientChainQueries.ClientChainMapAsync(w.Db, Array.Empty<int>()));

        using (w.Tenant.As(TenantB))
        {
            var mapB = await ClientChainQueries.ClientChainMapAsync(w.Db, null);
            Assert.Equal(new[] { w.OtherTenantClient.ClientId }, mapB.Keys);
        }
    }

    [Fact]
    public async Task Public_ids_resolve_only_inside_the_tenant()
    {
        var w = await SeedAsync();
        Assert.Equal(w.Walgreens.ClientChainId, await ClientChainQueries.ChainIdByPublicIdAsync(w.Db, w.Walgreens.PublicId));
        Assert.Equal(w.OldChain.ClientChainId, await ClientChainQueries.ChainIdByPublicIdAsync(w.Db, w.OldChain.PublicId));
        Assert.Null(await ClientChainQueries.ChainIdByPublicIdAsync(w.Db, w.OtherTenant.PublicId));
        Assert.Null(await ClientChainQueries.ChainIdByPublicIdAsync(w.Db, Guid.NewGuid()));

        var ids = await ClientChainQueries.ChainIdsByPublicIdsAsync(w.Db, new[] { w.Walgreens.PublicId, w.Cvs.PublicId, w.OtherTenant.PublicId, Guid.NewGuid() });
        Assert.Equal(new[] { w.Walgreens.ClientChainId, w.Cvs.ClientChainId }.OrderBy(x => x), ids.OrderBy(x => x));
        Assert.Empty(await ClientChainQueries.ChainIdsByPublicIdsAsync(w.Db, Array.Empty<Guid>()));

        // la compañía B no resuelve las cadenas de A
        using (w.Tenant.As(TenantB))
        {
            Assert.Null(await ClientChainQueries.ChainIdByPublicIdAsync(w.Db, w.Walgreens.PublicId));
            Assert.Empty(await ClientChainQueries.ChainIdsByPublicIdsAsync(w.Db, new[] { w.Walgreens.PublicId, w.Cvs.PublicId }));
            Assert.Equal(w.OtherTenant.ClientChainId, await ClientChainQueries.ChainIdByPublicIdAsync(w.Db, w.OtherTenant.PublicId));
        }
    }
}
