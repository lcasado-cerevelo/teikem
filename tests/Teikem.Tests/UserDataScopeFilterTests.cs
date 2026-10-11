using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Clients;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Persistence;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Alcance de datos del usuario (UserDataScope, 2026-10-11): el filtro global de TeikemDbContext limita lo que ve un usuario con
/// alcance de almacén y/o cliente. Sin alcance de un tipo = sin restricción de ese tipo; filas con almacén/cliente NULL se ven siempre;
/// con los dos tipos se exigen ambos; el tenant sigue mandando; el filtro de tenant apagado (plataforma/seeders) apaga también el alcance.
/// </summary>
public class UserDataScopeFilterTests
{
    private const int TenantId = 1;

    private static (TeikemDbContext Db, TenantContext Tenant) Create()
    {
        var tenant = new TenantContext { TenantId = TenantId, UserId = 5, IsAuthenticated = true };
        var options = new DbContextOptionsBuilder<TeikemDbContext>()
            .UseInMemoryDatabase("data-scope-" + Guid.NewGuid(), b => b.EnableNullChecks(false))
            .Options;
        var db = new TeikemDbContext(options, tenant);

        foreach (var id in new[] { 10, 20 })
            db.Add(new Warehouse { WarehouseId = id, PublicId = Guid.NewGuid(), TenantId = TenantId, Code = "ALM-" + id, Name = "Almacén " + id, IsActive = true });
        db.Add(new Warehouse { WarehouseId = 30, PublicId = Guid.NewGuid(), TenantId = 2, Code = "OTRO", Name = "De otra compañía", IsActive = true });
        foreach (var id in new[] { 1, 2 })
            db.Add(new Client { ClientId = id, PublicId = Guid.NewGuid(), TenantId = TenantId, Code = "C" + id, Name = "Cliente " + id, StatusCodeId = 1, IsActive = true });
        // Productos: del cliente 1, del cliente 2 y compartido (sin cliente).
        db.Add(new Product { ProductId = 1, PublicId = Guid.NewGuid(), TenantId = TenantId, Sku = "P1", Name = "Del cliente 1", ClientId = 1, IsActive = true });
        db.Add(new Product { ProductId = 2, PublicId = Guid.NewGuid(), TenantId = TenantId, Sku = "P2", Name = "Del cliente 2", ClientId = 2, IsActive = true });
        db.Add(new Product { ProductId = 3, PublicId = Guid.NewGuid(), TenantId = TenantId, Sku = "P3", Name = "Compartido", ClientId = null, IsActive = true });
        // ASN con almacén y cliente (opcional) a la vez.
        db.Add(new Asn { AsnId = 1, TenantId = TenantId, WarehouseId = 10, ClientId = 1 });
        db.Add(new Asn { AsnId = 2, TenantId = TenantId, WarehouseId = 10, ClientId = 2 });
        db.Add(new Asn { AsnId = 3, TenantId = TenantId, WarehouseId = 20, ClientId = 1 });
        db.Add(new Asn { AsnId = 4, TenantId = TenantId, WarehouseId = 10, ClientId = null });
        db.SaveChanges();
        return (db, tenant);
    }

    private static int[] Ids(IEnumerable<int> ids) => ids.OrderBy(i => i).ToArray();

    [Fact]
    public async Task Sin_alcance_ve_todo_lo_de_su_compania()
    {
        var (db, _) = Create();
        Assert.Equal(new[] { 10, 20 }, Ids(await db.Warehouses.Select(w => w.WarehouseId).ToListAsync()));
        Assert.Equal(new[] { 1, 2 }, Ids(await db.Clients.Select(c => c.ClientId).ToListAsync()));
        Assert.Equal(4, await db.Asns.CountAsync());
    }

    [Fact]
    public async Task Alcance_de_almacen_solo_ve_sus_almacenes_y_no_toca_los_clientes()
    {
        var (db, tenant) = Create();
        tenant.ScopedWarehouseIds = new[] { 10 };
        Assert.Equal(new[] { 10 }, Ids(await db.Warehouses.Select(w => w.WarehouseId).ToListAsync()));
        Assert.Equal(new[] { 1, 2 }, Ids(await db.Clients.Select(c => c.ClientId).ToListAsync()));
        Assert.Equal(new[] { 1, 2, 4 }, Ids(await db.Asns.Select(a => a.AsnId).ToListAsync()));
        Assert.Null(await db.Warehouses.FirstOrDefaultAsync(w => w.WarehouseId == 20));   // por id: «no encontrado»
    }

    [Fact]
    public async Task Alcance_de_cliente_ve_sus_clientes_y_lo_compartido_sin_cliente()
    {
        var (db, tenant) = Create();
        tenant.ScopedClientIds = new[] { 1 };
        Assert.Equal(new[] { 1 }, Ids(await db.Clients.Select(c => c.ClientId).ToListAsync()));
        Assert.Equal(new[] { 1, 3 }, Ids(await db.Products.Select(p => p.ProductId).ToListAsync()));
        Assert.Equal(new[] { 10, 20 }, Ids(await db.Warehouses.Select(w => w.WarehouseId).ToListAsync()));
    }

    [Fact]
    public async Task Con_los_dos_alcances_se_exigen_ambos()
    {
        var (db, tenant) = Create();
        tenant.ScopedWarehouseIds = new[] { 10 };
        tenant.ScopedClientIds = new[] { 1 };
        // A1 (alm 10, cli 1) sí; A2 (cli 2) no; A3 (alm 20) no; A4 (alm 10, sin cliente) sí.
        Assert.Equal(new[] { 1, 4 }, Ids(await db.Asns.Select(a => a.AsnId).ToListAsync()));
    }

    [Fact]
    public async Task El_tenant_sigue_mandando_con_alcance()
    {
        var (db, tenant) = Create();
        tenant.ScopedWarehouseIds = new[] { 10, 20, 30 };
        Assert.Equal(new[] { 10, 20 }, Ids(await db.Warehouses.Select(w => w.WarehouseId).ToListAsync()));   // el 30 es de otra compañía
    }

    [Fact]
    public async Task Con_el_filtro_de_tenant_apagado_tambien_se_apaga_el_alcance()
    {
        var (db, tenant) = Create();
        tenant.ScopedWarehouseIds = new[] { 10 };
        tenant.TenantFilterDisabled = true;
        Assert.Equal(new[] { 10, 20, 30 }, Ids(await db.Warehouses.Select(w => w.WarehouseId).ToListAsync()));
    }
}
