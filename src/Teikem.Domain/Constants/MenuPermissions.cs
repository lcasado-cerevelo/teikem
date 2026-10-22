namespace Teikem.Domain.Constants;

/// <summary>
/// Lote 152 — permiso de ACCESO por ítem del menú (2026-10-20, Luis): cada opción del menú tiene su permiso para entrar y VER esa pantalla, aparte de los que crean, editan o ejecutan. Son 44: los 16 que ya
/// existían y servían (orders.view, trips.scan, trips.view, claims.view, rental.view, sales.view, purchasing.view, billing.view, cod.view, driverpay.view, clients.read, locations.read, fleet.view,
/// integrations.view, notifications.send, admin.audit) y 28 nuevos. Desde el 2026-10-22 (Luis) cada uno va en la categoría de SU sección (Transporte, Almacén, Facturación…), junto a los permisos que editan esa pantalla, y no en un grupo aparte «Acceso al menú».
/// Reglas (lógica pura, probada con xunit):
/// - <see cref="EditImplies"/>: quien edita o ejecuta en una pantalla también entra a ella (editar ⇒ ver).
/// - <see cref="ViewImplies"/>: entrar a una pantalla incluye leer sus datos (ver ⇒ permiso de lectura de los datos), para que no salga vacía ni con 403; no incluye editar.
/// - <see cref="Grants"/>: migración y plantillas — quien tenía el permiso de la izquierda (que antes daba la entrada) recibe los de la derecha, así nadie pierde un ítem.
/// </summary>
public static class MenuPermissions
{
    /// <summary>Categoría histórica «Acceso al menú»: ya no se usa (cada permiso de acceso vive en la categoría de su sección).</summary>
    public const string Category = "MENU";

    /// <summary>Los 44 permisos de acceso, en el orden del menú (Pulso; Transporte; Almacén; Contabilidad; Listas; Análisis; Sistema).</summary>
    public static readonly IReadOnlyList<PermissionDef> Items = new List<PermissionDef>
    {
        new("pulse.view", "PULSE", "Pulso del día: entrar y ver", "Day pulse: open and view"),
        new("orders.view", "ORDERS", "Envíos: entrar y ver", "Shipments: open and view"),
        new("trips.scan", "ORDERS", "Estación de escaneo: entrar y ver", "Scan station: open and view"),
        new("dispatch.view", "ORDERS", "Sala de despacho: entrar y ver", "Dispatch room: open and view"),
        new("trips.view", "ORDERS", "Monitoreo de ruta: entrar y ver", "Route monitor: open and view"),
        new("claims.view", "ORDERS", "Reclamaciones: entrar y ver", "Claims: open and view"),
        new("warehouses.view", "WAREHOUSE", "Almacenes: entrar y ver", "Warehouses: open and view"),
        new("bins.view", "WAREHOUSE", "Posiciones: entrar y ver", "Bins: open and view"),
        new("products.view", "WAREHOUSE", "Productos e inventario: entrar y ver", "Products & inventory: open and view"),
        new("suppliers.view", "BILLING", "Proveedores: entrar y ver", "Suppliers: open and view"),
        new("notices.view", "WAREHOUSE", "Llegadas y salidas: entrar y ver", "Arrivals & departures: open and view"),
        new("receipts.view", "WAREHOUSE", "Recibo: entrar y ver", "Receiving: open and view"),
        new("pickbatches.view", "WAREHOUSE", "Recolección y empaque: entrar y ver", "Picking & packing: open and view"),
        new("adjustments.view", "WAREHOUSE", "Transferencias y ajustes: entrar y ver", "Transfers & adjustments: open and view"),
        new("counts.view", "WAREHOUSE", "Conteo cíclico: entrar y ver", "Cycle count: open and view"),
        new("crossdock.view", "WAREHOUSE", "Cruce de plataforma: entrar y ver", "Cross-dock: open and view"),
        new("rental.view", "RENTAL", "Rentas: entrar y ver", "Rentals: open and view"),
        new("kardex.view", "WAREHOUSE", "Kárdex de movimientos: entrar y ver", "Movement ledger: open and view"),
        new("purchasing.view", "BILLING", "Compras: entrar y ver", "Purchases: open and view"),
        new("purchaseaccounting.view", "BILLING", "Contabilización de compras: entrar y ver", "Purchase accounting: open and view"),
        new("sales.view", "BILLING", "Ventas: entrar y ver", "Sales: open and view"),
        new("dispatchaccounting.view", "BILLING", "Contabilización de despachos: entrar y ver", "Dispatch accounting: open and view"),
        new("billing.view", "BILLING", "Facturación: entrar y ver", "Billing: open and view"),
        new("cod.view", "BILLING", "Procesar entregas: entrar y ver", "Process deliveries: open and view"),
        new("driverpay.view", "BILLING", "Liquidación a choferes: entrar y ver", "Driver settlement: open and view"),
        new("tax.view", "BILLING", "Impuestos: entrar y ver", "Taxes: open and view"),
        new("clients.read", "CLIENTS", "Clientes y contratos: entrar y ver", "Clients & contracts: open and view"),
        new("locations.read", "CLIENTS", "Consignatarios: entrar y ver", "Consignees: open and view"),
        new("drivers.view", "FLEET", "Choferes y tarifas: entrar y ver", "Drivers & rates: open and view"),
        new("fleet.view", "FLEET", "Flota y mantenimiento: entrar y ver", "Fleet & maintenance: open and view"),
        new("reports.view", "ANALYTICS", "Vistas e informes: entrar y ver", "Views & reports: open and view"),
        new("customfields.view", "ADMIN", "Campos personalizados: entrar y ver", "Custom fields: open and view"),
        new("indicators.view", "ANALYTICS", "Indicadores: entrar y ver", "Indicators: open and view"),
        new("charts.view", "ANALYTICS", "Gráficos: entrar y ver", "Charts: open and view"),
        new("printers.view", "ADMIN", "Impresoras y labels: entrar y ver", "Printers & labels: open and view"),
        new("users.view", "ADMIN", "Roles y usuarios: entrar y ver", "Roles & users: open and view"),
        new("devices.view", "SECURITY", "Aparatos móviles: entrar y ver", "Mobile devices: open and view"),
        new("catalogs.view", "ADMIN", "Listas de valores: entrar y ver", "Value lists: open and view"),
        new("integrations.view", "ADMIN", "Integraciones / API: entrar y ver", "Integrations / API: open and view"),
        new("notifications.view", "ADMIN", "Reglas de aviso: entrar y ver", "Notification rules: open and view"),
        new("notifications.send", "ADMIN", "Enviar avisos: entrar y ver", "Send notices: open and view"),
        new("jobs.view", "ADMIN", "Trabajos programados: entrar y ver", "Scheduled jobs: open and view"),
        new("admin.audit", "ADMIN", "Seguridad y auditoría: entrar y ver", "Security & audit: open and view"),
        new("settings.view", "ADMIN", "Ajustes de la compañía: entrar y ver", "Tenant settings: open and view"),
    };

    /// <summary>Quién edita o ejecuta en una pantalla entra a ella: permiso de acción ⇒ permiso de acceso.</summary>
    public static readonly IReadOnlyDictionary<string, string[]> EditImplies = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
    {
        [PermissionCatalog.OrdersCreate] = new[] { PermissionCatalog.OrdersView },
        [PermissionCatalog.OrdersEdit] = new[] { PermissionCatalog.OrdersView },
        [PermissionCatalog.OrdersCancel] = new[] { PermissionCatalog.OrdersView },
        [PermissionCatalog.TripsPlan] = new[] { "dispatch.view" },
        [PermissionCatalog.TripsDispatch] = new[] { "dispatch.view" },
        [PermissionCatalog.TripsClose] = new[] { "dispatch.view" },
        [PermissionCatalog.TripsOptimize] = new[] { "dispatch.view" },
        [PermissionCatalog.ClaimsManage] = new[] { PermissionCatalog.ClaimsView },
        [PermissionCatalog.WarehouseManage] = new[] { "warehouses.view", "bins.view" },
        [PermissionCatalog.InventoryManage] = new[] { "products.view" },
        [PermissionCatalog.WarehouseDamage] = new[] { "products.view" },
        [PermissionCatalog.PurchasingManage] = new[] { PermissionCatalog.PurchasingView, "suppliers.view", "purchaseaccounting.view" },
        [PermissionCatalog.SuppliersManage] = new[] { "suppliers.view" },   // 2026-10-22
        [PermissionCatalog.PurchaseAccountingManage] = new[] { "purchaseaccounting.view" },
        [PermissionCatalog.PurchasingReceive] = new[] { PermissionCatalog.PurchasingView },
        [PermissionCatalog.ExitNoticeCreate] = new[] { "notices.view" },
        [PermissionCatalog.WarehouseReceive] = new[] { "receipts.view", "notices.view" },
        [PermissionCatalog.WarehousePick] = new[] { "pickbatches.view" },
        [PermissionCatalog.WarehouseIssue] = new[] { "pickbatches.view" },
        [PermissionCatalog.InventoryAdjust] = new[] { "adjustments.view", "kardex.view" },
        [PermissionCatalog.InventoryAdjustMulti] = new[] { "adjustments.view" },
        [PermissionCatalog.WarehouseTransfer] = new[] { "adjustments.view" },
        [PermissionCatalog.WarehouseAdjust] = new[] { "adjustments.view" },
        [PermissionCatalog.WarehouseCount] = new[] { "counts.view" },
        [PermissionCatalog.WarehouseCountCapture] = new[] { "counts.view" },
        [PermissionCatalog.WarehouseCrossdock] = new[] { "crossdock.view" },
        [PermissionCatalog.RentalManage] = new[] { PermissionCatalog.RentalView },
        [PermissionCatalog.SalesCreate] = new[] { PermissionCatalog.SalesView },
        [PermissionCatalog.BillingGenerate] = new[] { PermissionCatalog.BillingView, "dispatchaccounting.view" },   // Lote 181: quien genera corridas de despachos entra a su pantalla
        [PermissionCatalog.BillingApprove] = new[] { PermissionCatalog.BillingView },
        [PermissionCatalog.BillingExport] = new[] { PermissionCatalog.BillingView, "dispatchaccounting.view" },
        [PermissionCatalog.BillingCredit] = new[] { PermissionCatalog.BillingView },
        [PermissionCatalog.CodCollect] = new[] { PermissionCatalog.CodView },
        [PermissionCatalog.CodReconcile] = new[] { PermissionCatalog.CodView },
        [PermissionCatalog.CodRemit] = new[] { PermissionCatalog.CodView },
        [PermissionCatalog.DriverPayManage] = new[] { PermissionCatalog.DriverPayView, "drivers.view" },
        [PermissionCatalog.TaxManage] = new[] { "tax.view" },
        [PermissionCatalog.ClientsCreate] = new[] { PermissionCatalog.ClientsRead },
        [PermissionCatalog.ClientsUpdate] = new[] { PermissionCatalog.ClientsRead },
        [PermissionCatalog.LocationsCreate] = new[] { PermissionCatalog.LocationsRead },
        [PermissionCatalog.LocationsUpdate] = new[] { PermissionCatalog.LocationsRead },
        [PermissionCatalog.FleetManage] = new[] { PermissionCatalog.FleetView, "drivers.view" },
        [PermissionCatalog.FleetMaintenance] = new[] { PermissionCatalog.FleetView },
        [PermissionCatalog.AnalyticsManage] = new[] { "reports.view", "indicators.view", "charts.view" },
        [PermissionCatalog.AdminCustomFields] = new[] { "customfields.view" },
        [PermissionCatalog.PrintersManage] = new[] { "printers.view" },
        [PermissionCatalog.AdminUsers] = new[] { "users.view" },
        [PermissionCatalog.AdminRoles] = new[] { "users.view" },
        [PermissionCatalog.DevicesManage] = new[] { "devices.view" },
        [PermissionCatalog.AdminCatalogs] = new[] { "catalogs.view" },
        [PermissionCatalog.IntegrationsManage] = new[] { PermissionCatalog.IntegrationsView },
        [PermissionCatalog.IntegrationsInbox] = new[] { PermissionCatalog.IntegrationsView },
        [PermissionCatalog.NotificationsManage] = new[] { "notifications.view" },
        [PermissionCatalog.JobsManage] = new[] { "jobs.view" },
        [PermissionCatalog.AdminTenant] = new[] { "settings.view" },
    };

    /// <summary>Entrar a una pantalla incluye leer sus datos: permiso de acceso ⇒ permiso de lectura que sus consultas exigen.</summary>
    public static readonly IReadOnlyDictionary<string, string[]> ViewImplies = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
    {
        ["dispatch.view"] = new[] { PermissionCatalog.TripsView },
        ["warehouses.view"] = new[] { PermissionCatalog.InventoryView },
        ["bins.view"] = new[] { PermissionCatalog.InventoryView },
        ["products.view"] = new[] { PermissionCatalog.InventoryView },
        ["suppliers.view"] = new[] { PermissionCatalog.PurchasingView },
        ["notices.view"] = new[] { PermissionCatalog.InventoryView },
        ["receipts.view"] = new[] { PermissionCatalog.InventoryView },
        ["pickbatches.view"] = new[] { PermissionCatalog.InventoryView },
        ["adjustments.view"] = new[] { PermissionCatalog.InventoryView },
        ["counts.view"] = new[] { PermissionCatalog.InventoryView },
        ["crossdock.view"] = new[] { PermissionCatalog.InventoryView },
        ["kardex.view"] = new[] { PermissionCatalog.InventoryView },
        ["purchaseaccounting.view"] = new[] { PermissionCatalog.PurchasingView },
        ["dispatchaccounting.view"] = new[] { PermissionCatalog.BillingView },   // Lote 181: las corridas de despachos se leen con billing.view
        ["drivers.view"] = new[] { PermissionCatalog.FleetView },
        ["reports.view"] = new[] { PermissionCatalog.AnalyticsView },
        ["indicators.view"] = new[] { PermissionCatalog.AnalyticsView },
        ["charts.view"] = new[] { PermissionCatalog.AnalyticsView },
    };

    /// <summary>
    /// Migración y plantillas: el permiso que ANTES daba la entrada ⇒ los de acceso nuevos que lo reemplazan. Se aplica a las plantillas de rol y (en SQL) a todos los roles ya existentes; a partir de ahí
    /// cada acceso se puede quitar por separado. pulse.view lo reciben todos los roles.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string[]> Grants = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
    {
        [PermissionCatalog.InventoryView] = new[] { "warehouses.view", "bins.view", "products.view", "notices.view", "receipts.view", "pickbatches.view", "adjustments.view", "counts.view", "crossdock.view", "kardex.view" },
        [PermissionCatalog.PurchasingView] = new[] { "suppliers.view", "purchaseaccounting.view" },
        [PermissionCatalog.TripsDispatch] = new[] { "dispatch.view" },
        [PermissionCatalog.BillingExport] = new[] { "dispatchaccounting.view" },
        [PermissionCatalog.TaxManage] = new[] { "tax.view" },
        [PermissionCatalog.FleetView] = new[] { "drivers.view" },
        [PermissionCatalog.AnalyticsView] = new[] { "reports.view", "indicators.view", "charts.view" },
        [PermissionCatalog.AdminCustomFields] = new[] { "customfields.view" },
        [PermissionCatalog.PrintersManage] = new[] { "printers.view" },
        [PermissionCatalog.AdminUsers] = new[] { "users.view" },
        [PermissionCatalog.AdminRoles] = new[] { "users.view" },
        [PermissionCatalog.DevicesManage] = new[] { "devices.view" },
        [PermissionCatalog.AdminCatalogs] = new[] { "catalogs.view" },
        [PermissionCatalog.NotificationsManage] = new[] { "notifications.view" },
        [PermissionCatalog.JobsManage] = new[] { "jobs.view" },
        [PermissionCatalog.AdminTenant] = new[] { "settings.view" },
    };

    public const string PulseView = "pulse.view";
}
