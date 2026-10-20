using Teikem.Domain.Constants;
using Xunit;

namespace Teikem.Tests;

/// <summary>Lote 152 — permiso de acceso por ítem del menú: 44 permisos en la categoría MENU, reglas de implicación y migración.</summary>
public sealed class MenuPermissionsTests
{
    private static readonly HashSet<string> All = PermissionCatalog.All.Select(p => p.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void There_are_44_access_permissions_first_in_the_catalog_in_menu_order()
    {
        Assert.Equal(44, MenuPermissions.Items.Count);
        Assert.Equal(44, MenuPermissions.Items.Select(p => p.Code).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(MenuPermissions.Items, p => Assert.Equal("MENU", p.Category));
        Assert.Equal(MenuPermissions.Items.Select(p => p.Code), PermissionCatalog.All.Take(44).Select(p => p.Code));
        Assert.Equal("pulse.view", PermissionCatalog.All[0].Code);
        // el orden de Contabilidad pedido por el dueño: Compras, Contab. compras, Ventas, Contab. despachos, Facturación, Procesar entregas, Liquidación, Impuestos
        var codes = MenuPermissions.Items.Select(p => p.Code).ToList();
        var order = new[] { "purchasing.view", "purchaseaccounting.view", "sales.view", "dispatchaccounting.view", "billing.view", "cod.view", "driverpay.view", "tax.view" };
        Assert.Equal(order, codes.Where(order.Contains).ToArray());
        // ninguno se repite en otra categoría
        Assert.Equal(MenuPermissions.Items.Count, PermissionCatalog.All.Count(p => p.Category == "MENU"));
    }

    [Fact]
    public void Every_rule_mentions_only_codes_that_exist_and_views_are_menu_permissions()
    {
        foreach (var rules in new[] { MenuPermissions.EditImplies, MenuPermissions.ViewImplies, MenuPermissions.Grants })
            foreach (var (key, values) in rules)
            {
                Assert.True(All.Contains(key), key);
                Assert.All(values, v => Assert.True(All.Contains(v), v));
            }
        var menu = MenuPermissions.Items.Select(p => p.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var values in MenuPermissions.EditImplies.Values) Assert.All(values, v => Assert.Contains(v, menu));
        foreach (var key in MenuPermissions.ViewImplies.Keys) Assert.Contains(key, menu);
        foreach (var values in MenuPermissions.Grants.Values) Assert.All(values, v => Assert.Contains(v, menu));
    }

    [Fact]
    public void Editing_implies_entering_and_entering_implies_reading_but_not_editing()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { PermissionCatalog.InventoryAdjust };
        PermissionCatalog.ExpandImplied(set);
        Assert.Contains("kardex.view", set);          // ajustar ⇒ entrar al Kárdex
        Assert.Contains("adjustments.view", set);
        Assert.Contains(PermissionCatalog.InventoryView, set);   // entrar ⇒ leer los datos
        Assert.Contains(PermissionCatalog.WarehouseTransfer, set);   // lo de siempre

        var onlyView = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "kardex.view" };
        PermissionCatalog.ExpandImplied(onlyView);
        Assert.Contains(PermissionCatalog.InventoryView, onlyView);
        Assert.DoesNotContain(PermissionCatalog.InventoryAdjust, onlyView);
        Assert.DoesNotContain("products.view", onlyView);

        var seesNothingElse = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "printers.view" };
        PermissionCatalog.ExpandImplied(seesNothingElse);
        Assert.Single(seesNothingElse);
    }

    [Fact]
    public void Templates_keep_every_screen_they_had_and_everyone_gets_the_pulse()
    {
        var t = PermissionCatalog.RoleTemplates;
        Assert.All(t, kv => Assert.Contains("pulse.view", kv.Value));
        Assert.All(MenuPermissions.Items, p => Assert.Contains(p.Code, t["TenantAdmin"]));
        // quien veía inventario ve todas las pantallas de Almacén; quien veía compras, Proveedores y su contabilización
        foreach (var role in new[] { "WarehouseOperator", "Billing", "ReadOnly" })
            Assert.All(MenuPermissions.Grants[PermissionCatalog.InventoryView], c => Assert.Contains(c, t[role]));
        Assert.Contains("suppliers.view", t["Billing"]);
        Assert.DoesNotContain("suppliers.view", t["WarehouseOperator"]);   // el Operador no trae purchasing.view
        Assert.DoesNotContain("printers.view", t["Dispatcher"]);
    }
}
