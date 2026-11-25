using Teikem.Domain.Catalogs;
using Teikem.Domain.Clients;
using Teikem.Domain.Sales;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 245 (Lote B del plan, P5) — los filtros «Cadena» contra SQL Server REAL: GET /clients con chainPublicId / noChain y GET /sales-orders con chainPublicId repetible
/// (subconsulta sobre el cliente con ChainId.Value dentro de un Contains) se traducen a SQL, y los mapas de nombres (ChainsByIdAsync) salen bien. El esquema de
/// EnsureCreated basta (no necesita la FK compuesta). Base desechable de NotificationSqlFixture; se omite sin TEIKEM_TEST_SQLSERVER. Datos INVENTADOS.
/// </summary>
public sealed class ClientChainListFilterSqlTests(NotificationSqlFixture fx) : IClassFixture<NotificationSqlFixture>
{
    [Fact]
    public async Task Filtros_de_clientes_y_ordenes_por_cadena_se_traducen_a_sql()
    {
        if (fx.ConnectionString is null) return;
        var tenantId = await fx.NewTenantAsync("chains-filter-sql");
        var otherTenantId = await fx.NewTenantAsync("chains-filter-sql-otra");
        ClientChain walgreens, cvs, ajena;
        Client w1, c1, n1;
        await using (var seed = fx.NewDb(tenantId))
        {
            var draft = new StatusCode { Entity = SalesCodes.StatusDomain, InternalCode = "DRAFT", LabelJson = "{\"es\":\"Borrador\"}", StageKindLookupId = 1, SortOrder = 1, IsActive = true };
            seed.StatusCodes.Add(draft);
            walgreens = new ClientChain { PublicId = Guid.NewGuid(), TenantId = tenantId, Name = "Walgreens", IsActive = true, CreatedAtUtc = DateTime.UtcNow };
            cvs = new ClientChain { PublicId = Guid.NewGuid(), TenantId = tenantId, Name = "CVS", IsActive = true, CreatedAtUtc = DateTime.UtcNow };
            ajena = new ClientChain { PublicId = Guid.NewGuid(), TenantId = otherTenantId, Name = "Walgreens", IsActive = true, CreatedAtUtc = DateTime.UtcNow };
            seed.Set<ClientChain>().AddRange(walgreens, cvs, ajena);
            var w = new Warehouse { PublicId = Guid.NewGuid(), TenantId = tenantId, Code = "W1", Name = "Almacén Ponce", CountryLookupId = 1, StatusCodeId = 1, IsActive = true };
            seed.Warehouses.Add(w);
            await seed.SaveChangesAsync();

            Client NewClient(string code, int? chainId) => new()
            {
                PublicId = Guid.NewGuid(), TenantId = tenantId, Code = code, Name = "Farmacia " + code, StatusCodeId = 1, IsActive = true, CreatedAtUtc = DateTime.UtcNow, ChainId = chainId,
            };
            w1 = NewClient("W1", walgreens.ClientChainId);
            c1 = NewClient("C1", cvs.ClientChainId);
            n1 = NewClient("N1", null);
            seed.Clients.AddRange(w1, c1, n1);
            await seed.SaveChangesAsync();

            var n = 0;
            foreach (var c in new[] { w1, c1, n1 })
                seed.Set<SalesOrder>().Add(new SalesOrder
                {
                    PublicId = Guid.NewGuid(), TenantId = tenantId, Number = $"OV-{++n:D5}", WarehouseId = w.WarehouseId, ClientId = c.ClientId, RequestedDate = new DateOnly(2026, 11, 25),
                    CreatedAtUtc = DateTime.UtcNow, StatusCodeId = draft.StatusCodeId,
                });
            await seed.SaveChangesAsync();
        }

        var tenant = new TenantContext { TenantId = tenantId };
        await using var db = fx.NewDb(tenant);
        var clients = new ClientService(db, tenant, null!, null!, null!);

        var all = await clients.GetListAsync(null, false, default);
        Assert.Equal(new[] { "C1", "N1", "W1" }, all.Select(i => i.Code).OrderBy(c => c));
        Assert.Equal(("Walgreens", walgreens.PublicId), (all.Single(i => i.Code == "W1").ChainName, all.Single(i => i.Code == "W1").ChainPublicId!.Value));
        Assert.Equal("W1", Assert.Single(await clients.GetListAsync(null, false, default, chainPublicId: walgreens.PublicId)).Code);
        Assert.Equal("N1", Assert.Single(await clients.GetListAsync(null, false, default, noChain: true)).Code);
        Assert.Empty(await clients.GetListAsync(null, true, default, chainPublicId: ajena.PublicId));   // la de otra compañía: vacía, sin 404
        await Assert.ThrowsAsync<ValidationException>(() => clients.GetListAsync(null, false, default, chainPublicId: walgreens.PublicId, noChain: true));

        var sales = new SalesOrderService(db, tenant, null!, null!, null!, null!, null!, TenantClock.Default, null!, null!, null!, null!);
        var orders = await sales.ListAsync(null, null, null, false, null, null, 0, 50, default);
        Assert.Equal(3, orders.Total);
        Assert.Equal(new[] { "CVS", "Walgreens" }, orders.Items.Where(i => i.ChainName != null).Select(i => i.ChainName!).OrderBy(x => x, StringComparer.Ordinal));
        Assert.Null(orders.Items.Single(i => i.ClientPublicId == n1.PublicId).ChainPublicId);

        var both = await sales.ListAsync(null, null, null, false, null, null, 0, 50, default, chainPublicIds: new[] { walgreens.PublicId, cvs.PublicId });
        Assert.Equal(2, both.Total);
        Assert.Equal(new[] { c1.PublicId, w1.PublicId }.OrderBy(g => g), both.Items.Select(i => i.ClientPublicId).OrderBy(g => g));
        var onlyW = await sales.ListAsync(null, null, null, false, null, null, 0, 50, default, chainPublicIds: new[] { walgreens.PublicId });
        Assert.Equal(w1.PublicId, Assert.Single(onlyW.Items).ClientPublicId);
        Assert.Equal(0, (await sales.ListAsync(null, null, null, false, null, null, 0, 50, default, chainPublicIds: new[] { ajena.PublicId })).Total);
    }
}
