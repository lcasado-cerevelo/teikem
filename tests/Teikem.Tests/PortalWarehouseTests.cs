using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Catalogs;
using Teikem.Domain.Clients;
using Teikem.Domain.Constants;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Services;
using Teikem.Infrastructure.Wms;
using Xunit;

namespace Teikem.Tests;

/// <summary>Lote 153 — portal del cliente de almacén (lectura): solo el inventario del cliente de la sesión, sin posiciones, y el valor solo para ADMIN u OPERADOR del cliente.</summary>
public sealed class PortalWarehouseTests
{
    private sealed record World(WmsFixture F, Client Mine, Client Other, Product P, Product Foreign, Product Own, PortalWarehouseService Svc);

    private static async Task<World> WorldAsync(string role = PortalRoles.ClientAdmin)
    {
        var f = await WmsFixture.CreateAsync(s =>
        {
            s.AddSingleton<ITenantClock>(TenantClock.Default);
            s.AddSingleton<InventoryReadService>();
            s.AddSingleton<ValuationService>();
            s.AddSingleton<PortalWarehouseService>();
        });
        var w = await f.AddWarehouseAsync("W1");
        var bin1 = await f.AddBinAsync(await f.AddZoneAsync(w, "PZ", ZoneTypes.Picking), "P-01");
        var bin2 = await f.AddBinAsync(await f.AddZoneAsync(w, "RZ", ZoneTypes.Reserve), "R-01");
        var mine = await f.AddClientAsync("MIO");
        var other = await f.AddClientAsync("OTRO");
        var p = await f.AddProductAsync("MED", ownerClientId: mine.ClientId, purchaseCost: 5m);
        var foreign = await f.AddProductAsync("AJENO", ownerClientId: other.ClientId, purchaseCost: 9m);
        var own = await f.AddProductAsync("PROPIO", purchaseCost: 3m);
        foreach (var (prod, bin, qty) in new[] { (p, bin1, 6m), (p, bin2, 4m), (foreign, bin1, 7m), (own, bin1, 2m) })
            await f.PostAsync(new InventoryPosting(InventoryTxnTypes.Receipt, prod.ProductId, qty, ToWarehouseId: w.WarehouseId, ToBinId: bin.WarehouseBinId));
        // el acceso de portal: usuario 1 con el rol dado en el cliente mío
        var roles = new[] { PortalRoles.ClientAdmin, PortalRoles.ClientOperator, PortalRoles.ClientReadOnly }
            .Select((c, n) => new LookupCode { LookupCodeId = 9000 + n, Entity = LookupDomains.PortalRole, InternalCode = c, LabelJson = "{}", IsActive = true }).ToList();
        f.Db.LookupCodes.AddRange(roles);
        f.Db.PortalUsers.Add(new PortalUser { TenantId = WmsFixture.TenantId, ClientId = mine.ClientId, UserId = 1, Email = "a@b.c", RoleLookupId = roles.Single(r => r.InternalCode == role).LookupCodeId, StatusCodeId = 1, IsActive = true });
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        f.Tenant.PortalClientId = mine.ClientId;
        return new World(f, mine, other, p, foreign, own, f.Get<PortalWarehouseService>());
    }

    [Fact]
    public async Task Stock_shows_only_my_products_summed_over_positions()
    {
        var x = await WorldAsync();
        await using var _ = x.F;
        var page = await x.Svc.StockAsync(null, null, 0, 25, default);
        var row = Assert.Single(page.Items);
        Assert.Equal(("MED", 10m, 0m, 10m), (row.Sku, row.OnHand, row.Reserved, row.Available));   // 6 + 4 de dos posiciones, sumadas
        Assert.Equal((1, 10m), (page.Total, page.TotalOnHand));
        Assert.Empty((await x.Svc.StockAsync("AJENO", null, 0, 25, default)).Items);   // lo de otro cliente no aparece ni buscándolo
        Assert.Empty((await x.Svc.StockAsync("PROPIO", null, 0, 25, default)).Items);   // ni el inventario propio del operador
    }

    [Fact]
    public async Task Products_lists_mine_with_totals_and_hides_inactive_unless_asked()
    {
        var x = await WorldAsync();
        await using var _ = x.F;
        await x.F.AddProductAsync("VIEJO", ownerClientId: x.Mine.ClientId, isActive: false);
        var page = await x.Svc.ProductsAsync(null, false, 0, 25, default);
        Assert.Equal(new[] { "MED" }, page.Items.Select(i => i.Sku).ToArray());
        Assert.Equal((10m, 10m), (page.Items[0].OnHand, page.Items[0].Available));
        Assert.Equal(new[] { "MED", "VIEJO" }, (await x.Svc.ProductsAsync(null, true, 0, 25, default)).Items.Select(i => i.Sku).ToArray());
        Assert.Empty((await x.Svc.ProductsAsync("AJENO", true, 0, 25, default)).Items);
    }

    [Fact]
    public async Task Movements_are_only_mine_and_carry_no_positions_or_users()
    {
        var x = await WorldAsync();
        await using var _ = x.F;
        var page = await x.Svc.MovementsAsync(null, null, null, 0, 25, default);
        Assert.Equal(2, page.Total);   // las dos entradas de MED
        Assert.All(page.Items, m => Assert.Equal("MED", m.Sku));
        Assert.All(page.Items, m => Assert.Equal("W1", m.WarehouseCode));
        Assert.Equal(10m, page.Items.Sum(m => m.Quantity));
    }

    [Fact]
    public async Task Value_is_only_for_admin_and_operator_and_is_limited_to_my_client()
    {
        var x = await WorldAsync();
        await using var _ = x.F;
        var report = await x.Svc.ValuationAsync(null, null, 0, 25, default);
        var row = Assert.Single(report.Items);
        Assert.Equal(("MED", 10m, 50m), (row.Sku, row.OnHand, row.Value));   // 10 × costo del producto 5
        Assert.Equal(50m, report.TotalValue);
        Assert.True((await x.Svc.CapabilitiesAsync(default)).CanSeeValue);
        Assert.True((await x.Svc.CapabilitiesAsync(default)).Warehouse);

        var readOnly = await WorldAsync(PortalRoles.ClientReadOnly);
        await using var _2 = readOnly.F;
        Assert.False((await readOnly.Svc.CapabilitiesAsync(default)).CanSeeValue);
        var ex = await Assert.ThrowsAsync<ForbiddenException>(() => readOnly.Svc.ValuationAsync(null, null, 0, 25, default));
        Assert.Equal(PortalWarehouseService.ValueForbidden, ex.Message);
    }

    [Fact]
    public async Task The_warehouse_menu_depends_on_the_module_of_the_company()
    {
        var x = await WorldAsync();
        await using var _ = x.F;
        x.F.SetModules(ModuleKeys.ClientPortal, ModuleKeys.LtlGround);   // sin almacén: solo transporte
        var caps = await x.Svc.CapabilitiesAsync(default);
        Assert.False(caps.Warehouse);
        Assert.True(caps.Transport);
        x.F.SetModules(ModuleKeys.ClientPortal, ModuleKeys.WmsLotSerial);   // solo almacén
        var only = await x.Svc.CapabilitiesAsync(default);
        Assert.True(only.Warehouse);
        Assert.False(only.Transport);
    }

    [Fact]
    public async Task A_session_without_a_client_is_refused()
    {
        var x = await WorldAsync();
        await using var _ = x.F;
        x.F.Tenant.PortalClientId = null;
        await Assert.ThrowsAsync<ForbiddenException>(() => x.Svc.StockAsync(null, null, 0, 25, default));
    }
}
