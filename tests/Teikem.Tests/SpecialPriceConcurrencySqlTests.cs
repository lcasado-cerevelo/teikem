using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Catalogs;
using Teikem.Domain.Clients;
using Teikem.Domain.Sales;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 247 (Lote C, P4) — dos altas SIMULTÁNEAS del mismo precio especial (mismo dueño, mismas fechas y mismo producto) contra SQL Server REAL (se omite sin
/// TEIKEM_TEST_SQLSERVER), en dos contextos, sobre la base desechable de <see cref="NotificationSqlFixture"/> (creada desde el modelo EF, con sus índices únicos
/// filtrados UX_PriceList_SpecialClient y UX_PriceListItem_Product, y sin FK). Una gana; la otra recibe 409 (Concurrency u Overlap) o, si llegó después de que la
/// primera confirmó, reutiliza su lista. Siempre queda UNA sola lista dedicada y un solo renglón activo.
/// </summary>
public sealed class SpecialPriceConcurrencySqlTests(NotificationSqlFixture fx) : IClassFixture<NotificationSqlFixture>
{
    /// <summary>Sin catálogos: el tipo «kit» no existe (IsKit = false).</summary>
    private sealed class NoLookups : ILookupCache
    {
        public Task<int> GetIdAsync(string entity, string code, CancellationToken ct = default) => throw new InvalidOperationException($"{entity}/{code}");
        public Task<int?> TryGetIdAsync(string entity, string code, CancellationToken ct = default) => Task.FromResult<int?>(null);
        public Task<LookupCode?> GetAsync(int lookupCodeId, CancellationToken ct = default) => Task.FromResult<LookupCode?>(null);
        public Task<IReadOnlyList<LookupCode>> GetDomainAsync(string entity, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<LookupCode>>(Array.Empty<LookupCode>());
        public void Invalidate() { }
    }

    private static TenantContext Ctx(int tenantId) => new() { TenantId = tenantId, UserId = 1, IsAuthenticated = true };

    [SqlServerFact]
    public async Task Two_simultaneous_adds_of_the_same_special_price_leave_one_dedicated_list()
    {
        var tenantId = await fx.NewTenantAsync("Compañía precios especiales (carrera)");
        Guid clientPublicId;
        int clientId;
        var products = new List<Product>();
        await using (var db = fx.NewDb(Ctx(tenantId)))
        {
            var client = new Client
            {
                PublicId = Guid.NewGuid(), TenantId = tenantId, Code = "R" + Guid.NewGuid().ToString("N")[..8], Name = "Farmacia Carrera", StatusCodeId = 1, IsActive = true,
                CreatedAtUtc = DateTime.UtcNow,
            };
            db.Clients.Add(client);
            for (var i = 0; i < 5; i++)
                products.Add(new Product
                {
                    PublicId = Guid.NewGuid(), TenantId = tenantId, Sku = $"CAR-{i}-" + Guid.NewGuid().ToString("N")[..6], Name = "Producto carrera", BaseUomLookupId = 1,
                    TrackingTypeLookupId = 1, IsActive = true, SalePrice = 10m,
                });
            db.Set<Product>().AddRange(products);
            await db.SaveChangesAsync();
            (clientPublicId, clientId) = (client.PublicId, client.ClientId);
        }

        var today = TenantClock.Default.Today;
        for (var i = 0; i < products.Count; i++)
        {
            // cada vuelta, otras fechas: las dos llamadas compiten por CREAR la misma lista dedicada
            var from = today.AddDays(i * 10);
            var to = from.AddDays(5);
            var req = new SpecialPriceRequest(products[i].PublicId, 7m + i, from, to);
            await using var db1 = fx.NewDb(Ctx(tenantId));
            await using var db2 = fx.NewDb(Ctx(tenantId));
            var s1 = new PriceListService(db1, Ctx(tenantId), TenantClock.Default, new NoLookups(), null!);
            var s2 = new PriceListService(db2, Ctx(tenantId), TenantClock.Default, new NoLookups(), null!);

            using var start = new ManualResetEventSlim(false);
            var t1 = Task.Run(async () => { start.Wait(); return await s1.AddClientSpecialPriceAsync(clientPublicId, req, default); });
            var t2 = Task.Run(async () => { start.Wait(); return await s2.AddClientSpecialPriceAsync(clientPublicId, req, default); });
            start.Set();
            var outcomes = new List<Exception?>();
            foreach (var t in new[] { t1, t2 })
            {
                try { await t; outcomes.Add(null); }
                catch (Exception ex) { outcomes.Add(ex); }
            }

            Assert.Contains(outcomes, o => o is null);   // al menos una queda
            foreach (var ex in outcomes.Where(o => o is not null))
            {
                var conflict = Assert.IsType<ConflictException>(ex);
                Assert.True(conflict.Message == PriceListRules.Concurrency || conflict.Message.StartsWith("La lista se cruza con", StringComparison.Ordinal), conflict.Message);
            }

            await using var check = fx.NewDb(Ctx(tenantId));
            var lists = await check.Set<PriceList>().AsNoTracking()
                .Where(l => l.SpecialClientId == clientId && l.IsActive && l.ValidFrom == from && l.ValidTo == to).Select(l => l.PriceListId).ToListAsync();
            var listId = Assert.Single(lists);
            Assert.Equal(1, await check.Set<PriceListItem>().AsNoTracking().CountAsync(x => x.PriceListId == listId && x.IsActive && x.ProductId == products[i].ProductId));
            Assert.Equal(1, await check.Set<PriceListClient>().AsNoTracking().CountAsync(x => x.PriceListId == listId && x.IsActive && x.ClientId == clientId));
        }

        // y la vista lo muestra una vez por producto
        await using var read = fx.NewDb(Ctx(tenantId));
        var dto = await new PriceListService(read, Ctx(tenantId), TenantClock.Default, new NoLookups(), null!).GetClientSpecialPricesAsync(clientPublicId, false, default);
        Assert.Equal(products.Count, dto.Rows.Count);
        Assert.Equal(products.Count, dto.Rows.Select(r => r.ProductPublicId).Distinct().Count());
    }
}
