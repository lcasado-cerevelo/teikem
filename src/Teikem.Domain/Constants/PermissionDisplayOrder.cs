namespace Teikem.Domain.Constants;

/// <summary>
/// 2026-10-22 (Luis) — el ORDEN LÓGICO en que la pantalla de roles muestra los permisos: las secciones en el orden del menú (Pulso, Transporte, Almacén, Rentas, Facturación, Clientes, Flota, Análisis, Sistema) y,
/// dentro de cada una, por pantalla: primero «entrar y ver», luego lo que crea, edita o ejecuta en ella. Lo que no esté aquí sale al final de su sección por código. Lógica pura (probada con xunit).
/// </summary>
public static class PermissionDisplayOrder
{
    /// <summary>Las categorías en el orden en que se muestran (las que no estén, al final).</summary>
    public static readonly IReadOnlyList<string> Categories = new[] { "PULSE", "ORDERS", "WAREHOUSE", "RENTAL", "BILLING", "CLIENTS", "FLEET", "ANALYTICS", "ADMIN", "SECURITY" };

    /// <summary>Todos los permisos, en orden lógico y por sección.</summary>
    public static readonly IReadOnlyList<string> Codes = new[]
    {
        // Pulso
        "pulse.view", "pulse.indicators", "pulse.charts", "pulse.warehouse", "pulse.activity", "pulse.orders", "pulse.cod", "pulse.attention", "pulse.organize_company",
        // Transporte: envíos, escaneo, rutas, reclamaciones
        "orders.view", "orders.create", "orders.edit", "orders.cancel", "orders.reopen", "orders.return", "orders.deliver", "orders.view_price", "orders.credit_override",
        "trips.scan",
        "dispatch.view", "trips.plan", "trips.optimize", "trips.dispatch", "trips.close",
        "trips.view",
        "claims.view", "claims.manage",
        // Almacén: almacenes y posiciones, productos e inventario, llegadas y salidas, recibo, recolección, ajustes, conteo, cruce, daños, kárdex y aparato
        "warehouses.view", "bins.view", "warehouse.manage",
        "products.view", "inventory.view", "inventory.manage", "inventory.value",
        "notices.view", "exitnotice.create",
        "receipts.view", "warehouse.receive",
        "pickbatches.view", "warehouse.pick", "warehouse.issue",
        "adjustments.view", "inventory.adjust", "inventory.adjust.multi", "warehouse.transfer", "warehouse.adjust",
        "counts.view", "warehouse.count", "warehouse.count.capture",
        "crossdock.view", "warehouse.crossdock",
        "warehouse.damage",
        "kardex.view",
        "warehouse.pick.any", "warehouse.app.viewall", "warehouse.app.reassign",
        // Rentas
        "rental.view", "rental.manage", "rental.extend", "rental.return", "rental.maintenance", "rental.billing",
        // Facturación (Contabilidad): compras, contabilización de compras, ventas, contabilización de despachos, facturación, procesar entregas, liquidación a choferes, impuestos
        "suppliers.view", "suppliers.manage",
        "purchasing.view", "purchasing.manage", "purchasing.receive",
        "purchaseaccounting.view", "purchaseaccounting.manage",
        "sales.view", "sales.create", "sales.price", "sales.cancel", "sales.backorder", "sales.return", "sales.return.authorize",
        "dispatchaccounting.view",
        "billing.view", "billing.generate", "billing.approve", "billing.credit", "billing.export",
        "cod.view", "cod.collect", "cod.reconcile", "cod.remit",
        "driverpay.view", "driverpay.manage", "driverpay.approve", "driverpay.export",
        "tax.view", "tax.manage",
        // Clientes y contratos
        "clients.read", "clients.create", "clients.update", "locations.read", "locations.create", "locations.update", "contracts.read", "contracts.create", "contracts.update",
        "portalusers.manage", "clientmessages.reply", "clientmessages.manage",
        // Flota
        "drivers.view", "fleet.view", "fleet.manage", "fleet.maintenance",
        // Análisis
        "analytics.view", "reports.view", "indicators.view", "charts.view", "analytics.manage", "analytics.dates",
        // Sistema
        "users.view", "admin.users", "admin.roles",
        "settings.view", "admin.tenant",
        "catalogs.view", "admin.catalogs",
        "customfields.view", "admin.customfields", "admin.statusconfig",
        "printers.view", "printers.manage",
        "contacts.manage",
        "notifications.view", "notifications.send", "notifications.manage", "messages.use",
        "integrations.view", "integrations.create", "integrations.create.transport", "integrations.create.warehouse", "integrations.create.files", "integrations.manage", "integrations.inbox",
        "jobs.view", "jobs.manage",
        "admin.audit",
        // Seguridad
        "devices.view", "devices.manage",
    };

    private static readonly Dictionary<string, int> CodeIndex = Codes.Select((c, i) => (c, i)).ToDictionary(x => x.c, x => x.i, StringComparer.OrdinalIgnoreCase);

    /// <summary>Posición de un permiso (los desconocidos, al final).</summary>
    public static int IndexOf(string code) => CodeIndex.TryGetValue(code, out var i) ? i : int.MaxValue;

    /// <summary>Posición de una categoría (las desconocidas, al final).</summary>
    public static int CategoryIndex(string category)
    {
        for (var i = 0; i < Categories.Count; i++) if (string.Equals(Categories[i], category, StringComparison.OrdinalIgnoreCase)) return i;
        return int.MaxValue;
    }
}
