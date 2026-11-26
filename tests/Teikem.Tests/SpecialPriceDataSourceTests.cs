using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Clients;
using Teikem.Domain.Constants;
using Teikem.Domain.Sales;
using Teikem.Domain.Wms;
using Teikem.Infrastructure;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Analytics;
using Teikem.Infrastructure.Persistence;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 247 (Lote C, P1) — fuente SPECIAL_PRICE «Precios especiales»: solo los renglones activos de las listas DEDICADAS activas de la compañía (ni listas normales,
/// ni renglones quitados, ni listas desactivadas, ni otra compañía), con el estado de hoy, días para vencer (null sin «hasta»), el precio de la lista general vigente y
/// la marca «más caro que la general». Registrada en el contenedor con la clave SPECIAL_PRICE y leída con sales.view.
/// </summary>
public sealed class SpecialPriceDataSourceTests
{
    private const int TenantA = 1;
    private const int TenantB = 2;
    private static readonly DateOnly Today = new(2026, 12, 10);
    private static readonly ITenantClock Clock = new TenantClock(TenantClock.Default.Zone, () => new DateTime(2026, 12, 10, 16, 0, 0, DateTimeKind.Utc));

    private sealed record World(TeikemDbContext Db, TenantContext Tenant, int ClientItem, int ChainItem, int RemovedItem, int ExpiredItem);

    private static async Task<World> SeedAsync()
    {
        var tenant = new TenantContext { TenantId = TenantA, UserId = 1, IsAuthenticated = true };
        var db = new TeikemDbContext(new DbContextOptionsBuilder<TeikemDbContext>()
            .UseInMemoryDatabase("special-price-source-" + Guid.NewGuid(), b => b.EnableNullChecks(false))
            .ConfigureWarnings(w => w.Ignore(CoreEventId.PossibleIncorrectRequiredNavigationWithQueryFilterInteractionWarning))
            .Options, tenant);

        var walgreens = new ClientChain { PublicId = Guid.NewGuid(), TenantId = TenantA, Name = "Walgreens", CreatedAtUtc = DateTime.UtcNow };
        var chainB = new ClientChain { PublicId = Guid.NewGuid(), TenantId = TenantB, Name = "Walgreens B", CreatedAtUtc = DateTime.UtcNow };
        db.Set<ClientChain>().AddRange(walgreens, chainB);
        await db.SaveChangesAsync();
        var farmacia = new Client { PublicId = Guid.NewGuid(), TenantId = TenantA, Code = "F1", Name = "Farmacia Uno", StatusCodeId = 1, IsActive = true, CreatedAtUtc = DateTime.UtcNow, ChainId = walgreens.ClientChainId };
        var clientB = new Client { PublicId = Guid.NewGuid(), TenantId = TenantB, Code = "B1", Name = "Cliente B", StatusCodeId = 1, IsActive = true, CreatedAtUtc = DateTime.UtcNow };
        db.Clients.AddRange(farmacia, clientB);
        Product P(int tenantId, string sku, string? brand = null)
        {
            var p = new Product { PublicId = Guid.NewGuid(), TenantId = tenantId, Sku = sku, Name = "Producto " + sku, Brand = brand, IsActive = true };
            db.Products.Add(p);
            return p;
        }
        var p1 = P(TenantA, "SKU-1", "Acme");
        var p2 = P(TenantA, "SKU-2");
        var pB = P(TenantB, "SKU-B");
        await db.SaveChangesAsync();

        PriceList List(int tenantId, string name, bool all, int? specialClient = null, int? specialChain = null, bool active = true, DateOnly? from = null, DateOnly? to = null)
        {
            var l = new PriceList
            {
                PublicId = Guid.NewGuid(), TenantId = tenantId, Name = name, ValidFrom = from ?? Today.AddDays(-30), ValidTo = to, IsPublished = true, AppliesToAll = all,
                SpecialClientId = specialClient, SpecialChainId = specialChain, IsActive = active, CreatedAtUtc = DateTime.UtcNow,
            };
            db.Set<PriceList>().Add(l);
            return l;
        }
        var general = List(TenantA, "General", true);
        var clientSpecial = List(TenantA, "Precio especial — Farmacia Uno", false, specialClient: farmacia.ClientId);
        var chainSpecial = List(TenantA, "Precio especial — Walgreens", false, specialChain: walgreens.ClientChainId, to: Today.AddDays(10));
        var expiredSpecial = List(TenantA, "Precio especial — Walgreens (vencido)", false, specialChain: walgreens.ClientChainId, from: Today.AddDays(-60), to: Today.AddDays(-31));
        var inactiveSpecial = List(TenantA, "Precio especial — viejo", false, specialClient: farmacia.ClientId, active: false);
        var normal = List(TenantA, "Lista normal de clientes", false);
        var otherTenant = List(TenantB, "Precio especial — Cliente B", false, specialClient: clientB.ClientId);
        await db.SaveChangesAsync();

        PriceListItem Item(PriceList l, Product p, decimal price, bool active = true)
        {
            var i = new PriceListItem { TenantId = l.TenantId, PriceListId = l.PriceListId, ProductId = p.ProductId, Price = price, IsActive = active, CreatedAtUtc = DateTime.UtcNow };
            db.Set<PriceListItem>().Add(i);
            return i;
        }
        Item(general, p1, 9m);
        var clientItem = Item(clientSpecial, p1, 10m);   // más caro que la general
        var removed = Item(clientSpecial, p2, 3m, active: false);
        var chainItem = Item(chainSpecial, p2, 4m);       // p2 no está en la general
        var expiredItem = Item(expiredSpecial, p1, 8m);
        Item(inactiveSpecial, p1, 1m);
        Item(normal, p1, 2m);
        Item(otherTenant, pB, 5m);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return new World(db, tenant, clientItem.PriceListItemId, chainItem.PriceListItemId, removed.PriceListItemId, expiredItem.PriceListItemId);
    }

    [Fact]
    public async Task Only_active_rows_of_active_dedicated_lists_of_the_tenant_with_state_days_and_general_price()
    {
        var w = await SeedAsync();
        var rows = await new SpecialPriceDataSource(w.Db, Clock).LoadAsync(new DataQuery(), default);
        Assert.Equal(new[] { w.ClientItem, w.ChainItem, w.ExpiredItem }.OrderBy(x => x), rows.Select(r => (int)r["Id"]!).OrderBy(x => x));

        var client = rows.Single(r => (int)r["Id"]! == w.ClientItem);
        Assert.Equal(SpecialPriceOwnerKinds.Client, client["OwnerKind"]);
        Assert.Equal("Farmacia Uno", client["OwnerName"]);
        Assert.Equal("Walgreens", client["ChainName"]);   // la cadena actual del cliente
        Assert.Equal(("SKU-1", "Producto SKU-1", "Acme"), ((string)client["Sku"]!, (string)client["ProductName"]!, (string?)client["Brand"]));
        Assert.Equal(10m, client["Price"]);
        Assert.Null(client["ValidTo"]);
        Assert.Null(client["DaysToExpire"]);               // sin «hasta»
        Assert.Equal(PriceListStates.Active, client["State"]);
        Assert.Equal(9m, client["GeneralListPrice"]);
        Assert.Equal(true, client["AboveGeneral"]);
        Assert.Equal("Precio especial — Farmacia Uno", client["PriceListName"]);
        Assert.Equal(true, client["IsActive"]);

        var chain = rows.Single(r => (int)r["Id"]! == w.ChainItem);
        Assert.Equal((SpecialPriceOwnerKinds.Chain, "Walgreens", "Walgreens"), ((string)chain["OwnerKind"]!, (string)chain["OwnerName"]!, (string?)chain["ChainName"]));
        Assert.Equal(10, chain["DaysToExpire"]);
        Assert.Equal(Today.AddDays(10), chain["ValidTo"]);
        Assert.Null(chain["GeneralListPrice"]);
        Assert.Equal(false, chain["AboveGeneral"]);

        var expired = rows.Single(r => (int)r["Id"]! == w.ExpiredItem);
        Assert.Equal(PriceListStates.Expired, expired["State"]);
        Assert.Equal(-31, expired["DaysToExpire"]);
        Assert.Equal(false, expired["AboveGeneral"]);       // 8 < 9

        // q.Ids y la otra compañía
        Assert.Single(await new SpecialPriceDataSource(w.Db, Clock).LoadAsync(new DataQuery { Ids = new[] { w.ChainItem, w.RemovedItem } }, default));
        using (w.Tenant.As(TenantB))
        {
            var b = Assert.Single(await new SpecialPriceDataSource(w.Db, Clock).LoadAsync(new DataQuery(), default));
            Assert.Equal("Cliente B", b["OwnerName"]);
        }
    }

    [Fact]
    public void The_source_describes_itself_and_reads_with_sales_view()
    {
        var s = new SpecialPriceDataSource(null!);
        Assert.Equal("SPECIAL_PRICE", s.Key);
        Assert.Null(s.EntityTypeCode);
        Assert.Null(s.DateField);
        Assert.Equal(ModuleKeys.SalesOrders, s.TenantModule);
        Assert.Equal(BusinessModules.Warehouse, s.DefaultBusinessModule);
        Assert.Equal(new[] { "Id", "OwnerKind", "OwnerName", "ChainName", "Sku", "ProductName", "Brand", "Price", "ValidFrom", "ValidTo", "State", "DaysToExpire",
            "GeneralListPrice", "AboveGeneral", "PriceListName", "IsActive" }, s.Fields.Select(f => f.Key));
        Assert.Equal(PermissionCatalog.SalesView, PermissionCatalog.DataSourceReadPermission(SpecialPriceCodes.DataSourceKey));
        Assert.Equal(PermissionCatalog.SalesView, PermissionCatalog.DataSourceExtraReadPermission[SpecialPriceCodes.DataSourceKey]);
    }

    [Fact]
    public void The_source_is_registered_in_the_container()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Teikem"] = "Server=model-only.invalid;Database=Teikem;User Id=x;Password=x;TrustServerCertificate=True",
            ["Jwt:SigningKey"] = new string('k', 64),
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(config);
        services.AddTeikemInfrastructure(config);
        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();
        var registry = scope.ServiceProvider.GetRequiredService<IDataSourceRegistry>();
        Assert.True(registry.TryGet(SpecialPriceCodes.DataSourceKey, out var source));
        Assert.IsType<SpecialPriceDataSource>(source);
        Assert.Equal(registry.All.Count, scope.ServiceProvider.GetServices<IDataSource>().Count());   // claves únicas
    }
}
