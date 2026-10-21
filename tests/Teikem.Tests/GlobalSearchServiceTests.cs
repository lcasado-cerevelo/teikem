using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Constants;
using Teikem.Domain.Contacts;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Búsqueda global de «Buscar o ejecutar» (2026-10-21): números por prefijo, lo exacto primero, cada tipo solo con su permiso y su módulo, y el teléfono por sus dígitos.
/// Sobre InMemory (WmsFixture): ahí la comparación distingue mayúsculas; en SQL Server (colación por defecto) no, así que los textos de prueba van como están guardados.
/// </summary>
public sealed class GlobalSearchServiceTests
{
    private static Task<WmsFixture> CreateAsync() => WmsFixture.CreateAsync(s => s.AddSingleton<GlobalSearchService>());

    private static void Everything(WmsFixture f)
    {
        f.SetModules(ModuleKeys.WmsLotSerial, ModuleKeys.LtlGround, ModuleKeys.Catalog, ModuleKeys.Purchasing);
        f.SetPermissions(PermissionCatalog.OrdersView, PermissionCatalog.ClientsRead, PermissionCatalog.InventoryView, PermissionCatalog.PurchasingView, PermissionCatalog.LocationsRead);
    }

    [Fact]
    public async Task Short_text_returns_nothing_and_unknown_text_too()
    {
        await using var f = await CreateAsync();
        Everything(f);
        var svc = f.Get<GlobalSearchService>();
        Assert.Empty((await svc.SearchAsync("a", default)).Hits);
        Assert.Empty((await svc.SearchAsync("   ", default)).Hits);
        Assert.Empty((await svc.SearchAsync("zzz-no-existe", default)).Hits);
    }

    [Fact]
    public async Task An_order_is_found_by_its_number_and_by_its_pack_batch_and_the_exact_one_is_marked()
    {
        await using var f = await CreateAsync();
        var client = await f.AddClientAsync("C-100");
        var o1 = await f.AddOrderAsync(client.ClientId, "ORD-100", packBatchNumber: "EMP-00100");
        await f.AddOrderAsync(client.ClientId, "ORD-1000", packBatchNumber: "EMP-01000");
        Everything(f);
        var svc = f.Get<GlobalSearchService>();

        var byNumber = (await svc.SearchAsync("ORD-100", default)).Hits.Where(h => h.Type == "ORDER").ToList();
        Assert.Equal(2, byNumber.Count);
        Assert.True(byNumber[0].Exact);                       // lo exacto primero
        Assert.Equal(o1.PublicId.ToString(), byNumber[0].Id);
        Assert.False(byNumber[1].Exact);

        var byPack = (await svc.SearchAsync("EMP-00100", default)).Hits.Single(h => h.Type == "ORDER");
        Assert.Equal(("PACK_BATCH", true, o1.PublicId.ToString()), (byPack.MatchedBy, byPack.Exact, byPack.Id));
    }

    [Fact]
    public async Task Arrival_notices_are_found_by_their_LLE_number()
    {
        await using var f = await CreateAsync();
        Everything(f);
        var w = await f.AddWarehouseAsync("ALM-300");
        f.Db.Asns.Add(new Asn { AsnId = 900, TenantId = w.TenantId, Number = "LLE-00012", WarehouseId = w.WarehouseId, Reference = "OC-77",
            StatusCodeId = f.StatusId(StatusDomains.AsnStatus, AsnStatuses.Expected), IsActive = true });
        await f.Db.SaveChangesAsync();
        var hit = Assert.Single((await f.Get<GlobalSearchService>().SearchAsync("LLE-00012", default)).Hits, h => h.Type == "ARRIVAL_NOTICE");
        Assert.True(hit.Exact);
        Assert.Equal("OC-77", hit.Subtitle);
    }

    [Fact]
    public async Task Each_type_needs_its_own_permission_and_module()
    {
        await using var f = await CreateAsync();
        var client = await f.AddClientAsync("C-200");
        await f.AddOrderAsync(client.ClientId, "ORD-200");
        await f.AddWarehouseAsync("ALM-200");
        f.SetModules(ModuleKeys.WmsLotSerial, ModuleKeys.LtlGround, ModuleKeys.Catalog);
        var svc = f.Get<GlobalSearchService>();

        f.SetPermissions(PermissionCatalog.InventoryView);                       // solo almacén
        Assert.Contains((await svc.SearchAsync("ALM-200", default)).Hits, h => h.Type == "WAREHOUSE");
        Assert.DoesNotContain((await svc.SearchAsync("ORD-200", default)).Hits, h => h.Type == "ORDER");

        f.SetPermissions(PermissionCatalog.OrdersView);                          // solo envíos
        Assert.Contains((await svc.SearchAsync("ORD-200", default)).Hits, h => h.Type == "ORDER");
        Assert.DoesNotContain((await svc.SearchAsync("ALM-200", default)).Hits, h => h.Type == "WAREHOUSE");

        f.SetModules(ModuleKeys.Catalog);                                          // permiso sin módulo encendido
        f.SetPermissions(PermissionCatalog.OrdersView, PermissionCatalog.InventoryView);
        Assert.Empty((await svc.SearchAsync("ORD-200", default)).Hits);
        Assert.Empty((await svc.SearchAsync("ALM-200", default)).Hits);
    }

    [Fact]
    public async Task Warehouse_zone_bin_and_product_are_found_by_code_or_name()
    {
        await using var f = await CreateAsync();
        var w = await f.AddWarehouseAsync("ALM-DEPOT");
        var z = await f.AddZoneAsync(w, "ZONA-A", ZoneTypes.Picking);
        await f.AddBinAsync(z, "A-01-01");
        await f.AddProductAsync("166-10401-BR-2");
        Everything(f);
        var svc = f.Get<GlobalSearchService>();

        Assert.Contains((await svc.SearchAsync("ALM-DEP", default)).Hits, h => h.Type == "WAREHOUSE" && h.MatchedBy == "WAREHOUSE_CODE");
        Assert.Contains((await svc.SearchAsync("DEPOT", default)).Hits, h => h.Type == "WAREHOUSE" && h.MatchedBy == "WAREHOUSE_NAME");
        Assert.Contains((await svc.SearchAsync("ZONA-A", default)).Hits, h => h.Type == "ZONE" && h.Exact && h.Keys!["warehouse"] == w.PublicId.ToString());
        var bin = (await svc.SearchAsync("A-01", default)).Hits.Single(h => h.Type == "BIN");
        Assert.Equal("A-01-01", bin.Title);
        Assert.Equal("A-01-01", bin.Keys!["bin"]);
        var sku = (await svc.SearchAsync("166-10401", default)).Hits.Single(h => h.Type == "PRODUCT");
        Assert.Equal("SKU", sku.MatchedBy);
        Assert.Contains((await svc.SearchAsync("Producto 166", default)).Hits, h => h.Type == "PRODUCT" && h.MatchedBy == "PRODUCT_NAME");
    }

    [Fact]
    public async Task A_phone_is_found_by_its_digits_and_leads_to_the_owner_when_the_user_can_read_it()
    {
        await using var f = await CreateAsync();
        var client = await f.AddClientAsync("C-300");
        var phoneType = new Teikem.Domain.Catalogs.LookupCode { Entity = LookupDomains.ContactType, InternalCode = "PHONE", LabelJson = "{}", IsActive = true };
        f.Db.LookupCodes.Add(phoneType);
        await f.Db.SaveChangesAsync();
        f.Db.ContactPoints.Add(new ContactPoint
        {
            TenantId = client.TenantId, OwnerEntityLookupId = f.LookupId(LookupDomains.EntityType, EntityTypes.Client), OwnerId = client.ClientId,
            ContactTypeLookupId = phoneType.LookupCodeId, Value = "7875550142", CountryCallingCode = "1", IsPrimary = true, IsActive = true,
        });
        await f.Db.SaveChangesAsync();
        Everything(f);
        var svc = f.Get<GlobalSearchService>();

        foreach (var typed in new[] { "7875550142", "(787) 555-0142", "+1 787 555 0142", "17875550142", "787555" + "0" })
        {
            var hit = (await svc.SearchAsync(typed, default)).Hits.SingleOrDefault(h => h.Type == "CLIENT" && h.MatchedBy == "PHONE");
            Assert.NotNull(hit);
            Assert.Equal(client.PublicId.ToString(), hit!.Id);
        }
        Assert.Contains((await svc.SearchAsync("7875550142", default)).Hits, h => h.MatchedBy == "PHONE" && h.Exact);

        f.SetPermissions(PermissionCatalog.OrdersView);   // sin clients.read no se llega al cliente por su teléfono
        Assert.DoesNotContain((await svc.SearchAsync("7875550142", default)).Hits, h => h.Type == "CLIENT");
    }
}
