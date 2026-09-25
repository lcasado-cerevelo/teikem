namespace Teikem.Domain.Constants;

/// <summary>Definición de un permiso sembrado desde código (el tenant no edita el catálogo de permisos).</summary>
public sealed record PermissionDef(string Code, string Category, string LabelEs, string LabelEn);

/// <summary>
/// Vocabulario de permisos de la plataforma. Es la fuente de verdad: PermissionSeeder hace MERGE contra dbo.Permission
/// en cada arranque. Coincide con logistica-db-seed.sql (48 códigos): los 31 de negocio, los de las capas transversales A-I (Lote 1)
/// y los de Clientes y contratos (Lote 2, categoría CLIENTS).
/// Convención: recurso.acción.
/// </summary>
public static class PermissionCatalog
{
    // Órdenes / Trips / Almacén / Facturación / Flota (del seed SQL)
    public const string OrdersView = "orders.view";
    public const string OrdersCreate = "orders.create";
    public const string OrdersEdit = "orders.edit";
    public const string OrdersCancel = "orders.cancel";
    public const string TripsPlan = "trips.plan";
    public const string TripsDispatch = "trips.dispatch";
    public const string TripsOptimize = "trips.optimize";
    public const string WarehouseReceive = "warehouse.receive";
    public const string WarehousePick = "warehouse.pick";
    public const string WarehouseCount = "warehouse.count";
    public const string WarehouseCrossdock = "warehouse.crossdock";
    public const string BillingGenerate = "billing.generate";
    public const string BillingApprove = "billing.approve";
    public const string BillingExport = "billing.export";
    public const string FleetManage = "fleet.manage";
    public const string FleetMaintenance = "fleet.maintenance";
    // Administración
    public const string AdminUsers = "admin.users";
    public const string AdminRoles = "admin.roles";
    public const string AdminCatalogs = "admin.catalogs";
    public const string AdminTenant = "admin.tenant";
    public const string AdminAudit = "admin.audit";
    public const string AdminCustomFields = "admin.customfields";
    public const string AdminStatusConfig = "admin.statusconfig";
    // COD / Rental / Purchasing (del seed SQL)
    public const string CodCollect = "cod.collect";
    public const string CodReconcile = "cod.reconcile";
    public const string CodRemit = "cod.remit";
    public const string CodView = "cod.view";
    public const string RentalView = "rental.view";
    public const string RentalManage = "rental.manage";
    public const string RentalMaintenance = "rental.maintenance";
    public const string RentalBilling = "rental.billing";
    public const string PurchasingView = "purchasing.view";
    public const string PurchasingManage = "purchasing.manage";
    public const string PurchasingReceive = "purchasing.receive";
    // Análisis (módulos G, H, I)
    public const string AnalyticsView = "analytics.view";
    public const string AnalyticsManage = "analytics.manage";
    /// <summary>`analitica.fechas` del mock: cambiar el rango de fecha por defecto de un indicador/gráfico ajeno o de sistema.</summary>
    public const string AnalyticsDates = "analytics.dates";
    // Contactos
    public const string ContactsManage = "contacts.manage";
    // Clientes y contratos (Lote 2; categoría CLIENTS). portalusers.manage es distinto de admin.users (R40).
    public const string ClientsRead = "clients.read";
    public const string ClientsCreate = "clients.create";
    public const string ClientsUpdate = "clients.update";
    public const string LocationsRead = "locations.read";
    public const string LocationsCreate = "locations.create";
    public const string LocationsUpdate = "locations.update";
    public const string ContractsRead = "contracts.read";
    public const string ContractsCreate = "contracts.create";
    public const string ContractsUpdate = "contracts.update";
    public const string PortalUsersManage = "portalusers.manage";

    public static readonly IReadOnlyList<PermissionDef> All = new List<PermissionDef>
    {
        new(OrdersView, "ORDERS", "Ver órdenes", "View orders"),
        new(OrdersCreate, "ORDERS", "Crear órdenes", "Create orders"),
        new(OrdersEdit, "ORDERS", "Editar órdenes", "Edit orders"),
        new(OrdersCancel, "ORDERS", "Cancelar órdenes", "Cancel orders"),
        new(TripsPlan, "TRIPS", "Planificar trips", "Plan trips"),
        new(TripsDispatch, "TRIPS", "Despachar trips", "Dispatch trips"),
        new(TripsOptimize, "TRIPS", "Optimizar rutas", "Optimize routes"),
        new(WarehouseReceive, "WAREHOUSE", "Recibir", "Receive"),
        new(WarehousePick, "WAREHOUSE", "Pickear", "Pick"),
        new(WarehouseCount, "WAREHOUSE", "Contar", "Count"),
        new(WarehouseCrossdock, "WAREHOUSE", "Cross-dock", "Cross-dock"),
        new(BillingGenerate, "BILLING", "Generar facturación", "Generate billing"),
        new(BillingApprove, "BILLING", "Aprobar facturación", "Approve billing"),
        new(BillingExport, "BILLING", "Exportar facturación", "Export billing"),
        new(FleetManage, "FLEET", "Gestionar flota", "Manage fleet"),
        new(FleetMaintenance, "FLEET", "Mantenimiento", "Maintenance"),
        new(AdminUsers, "ADMIN", "Gestionar usuarios", "Manage users"),
        new(AdminRoles, "ADMIN", "Gestionar roles", "Manage roles"),
        new(AdminCatalogs, "ADMIN", "Gestionar catálogos", "Manage catalogs"),
        new(AdminTenant, "ADMIN", "Configurar compañía", "Configure tenant"),
        new(AdminAudit, "ADMIN", "Ver seguridad y auditoría", "View security & audit"),
        new(AdminCustomFields, "ADMIN", "Gestionar campos personalizados", "Manage custom fields"),
        new(AdminStatusConfig, "ADMIN", "Configurar pipeline de estatus", "Configure status pipeline"),
        new(CodCollect, "COD", "Cobrar COD en entrega", "Collect COD on delivery"),
        new(CodReconcile, "COD", "Reconciliar COD", "Reconcile COD"),
        new(CodRemit, "COD", "Generar remesas COD", "Generate COD remittances"),
        new(CodView, "COD", "Ver COD", "View COD"),
        new(RentalView, "RENTAL", "Ver equipos en alquiler", "View rental equipment"),
        new(RentalManage, "RENTAL", "Gestionar contratos de alquiler", "Manage rental contracts"),
        new(RentalMaintenance, "RENTAL", "Registrar mantenimiento de equipo", "Log equipment maintenance"),
        new(RentalBilling, "RENTAL", "Generar cargos de alquiler", "Generate rental charges"),
        new(PurchasingView, "PURCHASING", "Ver órdenes de compra", "View purchase orders"),
        new(PurchasingManage, "PURCHASING", "Crear y editar órdenes de compra", "Create and edit purchase orders"),
        new(PurchasingReceive, "PURCHASING", "Recibir contra orden de compra", "Receive against purchase order"),
        new(AnalyticsView, "ANALYTICS", "Ver vistas, indicadores y gráficos", "View reports, indicators & charts"),
        new(AnalyticsManage, "ANALYTICS", "Crear vistas, indicadores y gráficos", "Create reports, indicators & charts"),
        new(AnalyticsDates, "ANALYTICS", "Cambiar rango de fecha de indicadores/gráficos ajenos", "Change date range of others' indicators/charts"),
        new(ContactsManage, "ADMIN", "Gestionar contactos", "Manage contacts"),
        // Lote 2 — Clientes y contratos
        new(ClientsRead, "CLIENTS", "Ver clientes", "View clients"),
        new(ClientsCreate, "CLIENTS", "Crear clientes", "Create clients"),
        new(ClientsUpdate, "CLIENTS", "Editar clientes", "Edit clients"),
        new(LocationsRead, "CLIENTS", "Ver consignatarios", "View locations"),
        new(LocationsCreate, "CLIENTS", "Crear consignatarios", "Create locations"),
        new(LocationsUpdate, "CLIENTS", "Editar consignatarios", "Edit locations"),
        new(ContractsRead, "CLIENTS", "Ver contratos y tarifas", "View contracts & rates"),
        new(ContractsCreate, "CLIENTS", "Crear contratos", "Create contracts"),
        new(ContractsUpdate, "CLIENTS", "Editar contratos y tarifas", "Edit contracts & rates"),
        new(PortalUsersManage, "CLIENTS", "Administrar usuarios de portal del cliente", "Manage client portal users"),
    };

    /// <summary>
    /// Permiso de lectura de la entidad dueña en las rutas polimórficas (contactos, historial de estatus, valores de campos
    /// personalizados). Sin entrada (hoy USER) se conserva "cualquier autenticado" hasta que su lote la registre.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> OwnerReadPermission = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [EntityTypes.Client] = ClientsRead,
        [EntityTypes.ClientContact] = ClientsRead,
        [EntityTypes.Location] = LocationsRead,
        [EntityTypes.Contract] = ContractsRead,
        [EntityTypes.PortalUser] = PortalUsersManage,
    };

    /// <summary>Permiso de escritura del módulo dueño para poner valores de campos personalizados en un registro.</summary>
    public static readonly IReadOnlyDictionary<string, string> OwnerWritePermission = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [EntityTypes.Client] = ClientsUpdate,
        [EntityTypes.ClientContact] = ClientsUpdate,
        [EntityTypes.Location] = LocationsUpdate,
        [EntityTypes.Contract] = ContractsUpdate,
        [EntityTypes.PortalUser] = PortalUsersManage,
    };

    /// <summary>Plantillas de rol de sistema (TenantId NULL) y sus permisos por defecto — clonables al aprovisionar.</summary>
    public static readonly IReadOnlyDictionary<string, string[]> RoleTemplates = new Dictionary<string, string[]>
    {
        ["TenantAdmin"] = All.Select(p => p.Code).ToArray(),
        ["Dispatcher"] = new[] { OrdersView, OrdersCreate, OrdersEdit, OrdersCancel, TripsPlan, TripsDispatch, TripsOptimize, AnalyticsView, ClientsRead, LocationsRead, LocationsCreate },
        ["Billing"] = new[] { OrdersView, BillingGenerate, BillingApprove, BillingExport, CodView, CodReconcile, CodRemit, RentalBilling, RentalView, PurchasingView, PurchasingManage, AnalyticsView, ClientsRead, ContractsRead },
        ["WarehouseOperator"] = new[] { WarehouseReceive, WarehousePick, WarehouseCount, WarehouseCrossdock, CodReconcile, RentalView, RentalManage, RentalMaintenance, PurchasingView, PurchasingReceive },
        ["Driver"] = new[] { OrdersView, CodCollect },
        ["ReadOnly"] = new[] { OrdersView, CodView, AnalyticsView, ClientsRead, LocationsRead, ContractsRead },
    };

    /// <summary>
    /// Códigos que un rol clonado de un tenant debe recibir al actualizar la plataforma (Lote 2, PermissionSeeder):
    /// los de la plantilla del mismo nombre que sean nuevos en esta corrida y que el rol todavía no tenga.
    /// Lista vacía si el nombre no es una plantilla. Solo agrega, nunca quita (lógica pura, probada con xunit).
    /// </summary>
    public static IReadOnlyList<string> CodesToPropagate(string roleName, IReadOnlySet<string> newCodes, IReadOnlySet<string> assignedCodes)
    {
        if (string.IsNullOrEmpty(roleName) || !RoleTemplates.TryGetValue(roleName, out var template)) return Array.Empty<string>();
        return template.Where(c => newCodes.Contains(c) && !assignedCodes.Contains(c)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static readonly IReadOnlyDictionary<string, (string Es, string En)> RoleTemplateLabels = new Dictionary<string, (string, string)>
    {
        ["TenantAdmin"] = ("Admin de compañía", "Tenant admin"),
        ["Dispatcher"] = ("Despachador", "Dispatcher"),
        ["Billing"] = ("Facturación", "Billing"),
        ["WarehouseOperator"] = ("Operador de almacén", "Warehouse operator"),
        ["Driver"] = ("Chofer", "Driver"),
        ["ReadOnly"] = ("Solo lectura", "Read only"),
    };
}
