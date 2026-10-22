namespace Teikem.Domain.Constants;



/// <summary>Definición de un permiso sembrado desde código (el tenant no edita el catálogo de permisos).</summary>

public sealed record PermissionDef(string Code, string Category, string LabelEs, string LabelEn);



/// <summary>

/// Vocabulario de permisos de la plataforma. Es la fuente de verdad: PermissionSeeder hace MERGE contra dbo.Permission

/// en cada arranque. Coincide con logistica-db-seed.sql (60 códigos): los 31 de negocio, los de las capas transversales A-I (Lote 1),

/// los de Clientes y contratos (Lote 2, categoría CLIENTS), orders.credit_override (Lote 3, ajuste C), fleet.view,

/// driverpay.view y driverpay.manage (Lote 4, categoría FLEET: flota separada de la compensación de choferes, R8) y

/// trips.view y trips.scan (Lote 5, categoría TRIPS: leer rutas y escanear la salida sin poder planificar) e inventory.view,

/// inventory.manage, inventory.adjust y warehouse.manage (Lote 6, categoría WAREHOUSE) y devices.manage (Lote 8A, categoría

/// SECURITY) y warehouse.count.capture (Lote 8A, categoría WAREHOUSE: contar a ciegas sin reconciliar) y los 5 del Pulso del día

/// (Lote F8a, categoría PULSE: pulse.indicators, pulse.charts, pulse.warehouse, pulse.activity, pulse.organize_company) y

/// pulse.attention (Lote 14, "Necesita tu atención"): 66 códigos. Lote 27 (Rentas R1): rental.extend y rental.return → 68.

/// 2026-10-08/10: warehouse.damage, warehouse.transfer y warehouse.adjust → 71; 2026-10-11: warehouse.issue (despacho manual) → 72; 2026-10-11: trips.close (cerrar ruta) → 73;

/// 2026-10-12 (Lote 39): orders.view_price (ver precios de las órdenes) → 74; 2026-10-12 (Lote 43, F-G1): printers.manage (impresoras y

/// plantillas de label, categoría ADMIN) → 75.

/// Convención: recurso.acción.

/// </summary>

public static class PermissionCatalog

{

    // Órdenes / Trips / Almacén / Facturación / Flota (del seed SQL)

    public const string OrdersView = "orders.view";

    public const string OrdersCreate = "orders.create";

    public const string OrdersEdit = "orders.edit";

    public const string OrdersCancel = "orders.cancel";

    /// <summary>Lote 106: reabrir una orden en estatus final (Entregada, Cancelada, En almacén…); exige razón y queda en el historial.</summary>

    public const string OrdersReopen = "orders.reopen";

    /// <summary>Lote 111: ver los faltantes («Por verificar», «Perdido») y las reclamaciones.</summary>

    public const string ClaimsView = "claims.view";

    /// <summary>Lote 111: marcar piezas como perdidas, reclamarlas y resolver las reclamaciones.</summary>

    public const string ClaimsManage = "claims.manage";

    public const string OrdersReturn = "orders.return";

    /// <summary>Lote 118: registrar la entrega de una orden desde la web (firma automática del usuario; la tiene quien Luis designe).</summary>

    public const string OrdersDeliver = "orders.deliver";

    /// <summary>Lote 3 (ajuste C): autorizar la confirmación de una orden que excede el límite de crédito del cliente (overrideCredit=true).</summary>

    public const string OrdersCreditOverride = "orders.credit_override";

    /// <summary>

    /// 2026-10-12 (Lote 39): ver los precios de las órdenes: monto cotizado (lista, ficha, respuestas de confirmar/reprecio, fuente

    /// de datos de Análisis), la cotización completa (GET /orders/{id}/quote) y las cifras del crédito (422 credit_exceeded, historial

    /// del override, errores de importación). Sin él se ocultan (null / sin cifras); el COD sigue visible. Lo traen TenantAdmin y Billing.

    /// La decisión vive en OrderPriceVisibility.

    /// </summary>

    public const string OrdersViewPrice = "orders.view_price";

    public const string TripsPlan = "trips.plan";

    public const string TripsDispatch = "trips.dispatch";

    /// <summary>2026-10-11: cerrar una ruta en curso (IN_PROGRESS → COMPLETED) cuando todas sus órdenes terminaron.</summary>

    public const string TripsClose = "trips.close";

    public const string TripsOptimize = "trips.optimize";

    /// <summary>Lote 5: ver rutas, sala de despacho (lectura), 'sin asignar' y monitoreo.</summary>

    public const string TripsView = "trips.view";

    /// <summary>Lote 5: estación de escaneo Outbound (asigna la orden a la ruta abierta de su zona).</summary>

    public const string TripsScan = "trips.scan";

    public const string WarehouseReceive = "warehouse.receive";

    public const string WarehousePick = "warehouse.pick";
    /// <summary>Lote 193: ver y despachar en el aparato CUALQUIER documento (también los que no están asignados a la persona); sin él, solo los asignados.</summary>
    public const string WarehousePickAny = "warehouse.pick.any";
    /// <summary>Lote 199: en el aparato, ver también lo asignado a OTRAS personas (además de lo mío y lo sin asignar) en Recibir, Acomodar, Despacho y Mis tareas. Para el supervisor del almacén.</summary>
    public const string WarehouseAppViewAll = "warehouse.app.viewall";
    /// <summary>Lote 199: en el aparato, reasignar un documento o acomodo a otra persona (o dejarlo sin asignar). Exige warehouse.app.viewall.</summary>
    public const string WarehouseAppReassign = "warehouse.app.reassign";

    public const string WarehouseCount = "warehouse.count";

    /// <summary>

    /// Lote 8A: capturar un conteo (alta, captura, lo encontrado y terminar) SIN ver lo esperado ni reconciliar: conteo a

    /// ciegas del almacenista en el aparato. warehouse.count (reconciliar, modo informado) lo implica (<see cref="Implied"/>).

    /// </summary>

    public const string WarehouseCountCapture = "warehouse.count.capture";

    /// <summary>2026-10-08: reportar daños (llegaron dañados o se dañaron en el almacén), mandarlos a cuarentena, desecharlos o recuperarlos.</summary>

    public const string WarehouseDamage = "warehouse.damage";

    public const string WarehouseCrossdock = "warehouse.crossdock";

    /// <summary>Lote 6: estructura del almacén (almacenes, zonas, posiciones, muelles) y asignar/cancelar tareas de la cola.</summary>

    public const string WarehouseManage = "warehouse.manage";

    /// <summary>Lote 6: ver inventario, productos, Kárdex, trazabilidad y documentos de almacén (incluye costos, D31).</summary>

    public const string InventoryView = "inventory.view";

    /// <summary>Lote 148: ver el valor del inventario (reporte de valuación, con costos). Lo traen TenantAdmin y Billing.</summary>

    public const string InventoryValue = "inventory.value";

    /// <summary>Lote 6: maestro de productos y categorías.</summary>

    public const string InventoryManage = "inventory.manage";

    /// <summary>Lote 6: ajustes manuales, transferencias, conciliación y resolución de faltantes de compras.</summary>

    public const string InventoryAdjust = "inventory.adjust";

    /// <summary>2026-10-12: «Ajuste múltiple» (poner al día el inventario por producto y posición, varias a la vez). NO lo trae ningún rol por defecto: el administrador lo da a un rol propio (docs/ajuste-multiple-diseno.md).</summary>

    public const string InventoryAdjustMulti = "inventory.adjust.multi";

    /// <summary>2026-10-10: transferir inventario entre posiciones del mismo almacén desde el aparato (el operario). inventory.adjust lo implica (<see cref="Implied"/>).</summary>

    public const string WarehouseTransfer = "warehouse.transfer";

    /// <summary>2026-10-10: ajustar la CANTIDAD de una posición desde el aparato (sube o baja; nunca mueve). NO lo trae ninguna plantilla de rol: el administrador lo da a un rol propio.</summary>

    public const string WarehouseAdjust = "warehouse.adjust";

    /// <summary>

    /// 2026-10-11: despacho manual (DMA-#####): sacar inventario SIN entrega (muestra, uso interno, retiro del cliente, venta…)

    /// con motivo obligatorio, desde la web y el aparato; y eliminarlo (con reversa). Lo traen WarehouseOperator y TenantAdmin.

    /// </summary>

    public const string WarehouseIssue = "warehouse.issue";

    public const string BillingGenerate = "billing.generate";

    public const string BillingApprove = "billing.approve";

    public const string BillingExport = "billing.export";

    /// <summary>Lote 72 (P5): ver facturas y corridas de facturación (generar, aprobar y exportar siguen siendo sus propios permisos).</summary>

    public const string BillingView = "billing.view";

    /// <summary>Lote 79 (P6): crear, aprobar (con reautenticación), aplicar y anular notas de crédito.</summary>

    public const string BillingCredit = "billing.credit";

    public const string FleetManage = "fleet.manage";

    public const string FleetMaintenance = "fleet.maintenance";

    /// <summary>Lote 4: lectura de flota, choferes, mantenimiento y combustible (sin tarifas ni viajes pagados).</summary>

    public const string FleetView = "fleet.view";

    /// <summary>Lote 4: ver tarifas, política de pago y viajes pagados a choferes (compensación, R8).</summary>

    public const string DriverPayView = "driverpay.view";

    /// <summary>Lote 4: gestionar tarifas, política de pago y viajes pagados a choferes.</summary>

    public const string DriverPayManage = "driverpay.manage";

    /// <summary>Lote 94 (P9): aprobar una liquidación a choferes (con reautenticación) y exportarla. Quien genera no aprueba: solo TenantAdmin los trae por omisión.</summary>

    public const string DriverPayApprove = "driverpay.approve";

    public const string DriverPayExport = "driverpay.export";

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

    /// <summary>Lote 27 (Rentas, D4): extender la fecha de recogido de una renta (bitácora, sin aprobación de un segundo usuario).</summary>

    public const string RentalExtend = "rental.extend";

    /// <summary>Lote 27 (Rentas): registrar la devolución de los equipos rentados (bloque R2).</summary>

    public const string RentalReturn = "rental.return";

    public const string PurchasingView = "purchasing.view";

    public const string PurchasingManage = "purchasing.manage";
    /// <summary>2026-10-22 (Luis): crear y editar proveedores (antes lo daba purchasing.manage). Quien ya tenía purchasing.manage lo recibe.</summary>
    public const string SuppliersManage = "suppliers.manage";
    /// <summary>2026-10-22 (Luis): generar, aceptar, cancelar y exportar corridas de la contabilización de compras (antes lo daba purchasing.manage). Quien ya tenía purchasing.manage lo recibe.</summary>
    public const string PurchaseAccountingManage = "purchaseaccounting.manage";
    /// <summary>2026-10-22 (Luis): crear, editar y desactivar almacenes y sus muelles (antes warehouse.manage, que se sigue aceptando).</summary>
    public const string WarehousesManage = "warehouses.manage";
    /// <summary>2026-10-22 (Luis): crear, editar y eliminar posiciones y zonas (antes warehouse.manage, que se sigue aceptando).</summary>
    public const string BinsManage = "bins.manage";
    /// <summary>2026-10-22 (Luis): generar, aceptar, cancelar y exportar corridas de la contabilización de despachos (antes billing.generate / billing.export, que se siguen aceptando).</summary>
    public const string DispatchAccountingManage = "dispatchaccounting.manage";

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

    /// <summary>Lote 104: atender los mensajes que los clientes mandan desde su portal (leer y contestar las conversaciones de sus clientes).</summary>

    public const string ClientMessagesReply = "clientmessages.reply";

    /// <summary>Lote 104: designar el equipo de servicio al cliente de cada cliente y ver todas las conversaciones.</summary>

    public const string ClientMessagesManage = "clientmessages.manage";

    // Seguridad (Lote 8A, categoría SECURITY)

    /// <summary>

    /// Lote 8A: aparatos de confianza de la app de almacén (registrar, editar, desactivar, regenerar el código de registro) y

    /// asignar o restablecer el PIN de otros usuarios (también lo permite admin.users).

    /// </summary>

    public const string DevicesManage = "devices.manage";

    // Pulso del día (Lote F8a, categoría PULSE): un permiso por panel del registro PulsePanels + organizar el de la compañía.

    // Organizar MI Pulso no exige permiso (son preferencias propias); no existe pulse.view: el inicio siempre existe.

    /// <summary>Lote F8a: sección "Tus indicadores" del Pulso (cada indicador exige además leer su fuente de datos).</summary>

    public const string PulseIndicators = "pulse.indicators";

    /// <summary>Lote F8a: sección "Tus gráficos" del Pulso (cada gráfico exige además leer su fuente de datos).</summary>

    public const string PulseCharts = "pulse.charts";

    /// <summary>Lote F8a: panel Almacén del Pulso (datos: inventory.view; módulo WMS_LOTSERIAL).</summary>

    public const string PulseWarehouse = "pulse.warehouse";

    /// <summary>Lote F8a: panel Actividad reciente del Pulso (datos: analytics.view; módulo ANALYTICS).</summary>

    public const string PulseActivity = "pulse.activity";

    /// <summary>Lote F8a: organizar el Pulso de la compañía (PUT /api/v1/analytics/pulse/layout?scope=company).</summary>

    public const string PulseOrganizeCompany = "pulse.organize_company";

    /// <summary>Lote 88 (P12): el río «Paquetes en la calle» del Pulso (datos: orders.view; módulo LTL_GROUND).</summary>

    public const string PulseOrders = "pulse.orders";

    /// <summary>Lote 88 (P12): el río «Dinero COD» del Pulso (datos: cod.view; módulo COD).</summary>

    public const string PulseCod = "pulse.cod";

    /// <summary>

    /// Lote 14 (D6): panel "Necesita tu atención" del Pulso (GET /api/v1/analytics/attention). Sin permiso de datos ni módulo en

    /// el panel: cada fila decide el suyo (descuadres de inventario: inventory.view + WMS_LOTSERIAL). Lo ve quien ve inventario.

    /// </summary>

    public const string PulseAttention = "pulse.attention";

    /// <summary>

    /// 2026-10-12 (Lote 43, F-G1): administrar las impresoras de la compañía y las plantillas de label (Sistema → Impresoras y labels).

    /// Solo lo trae TenantAdmin (D-FG4). Las opciones de impresión de una estación (GET /printing/station-options) las lee también quien

    /// imprime (orders.view, trips.scan, trips.view, inventory.view).

    /// </summary>

    public const string PrintersManage = "printers.manage";

    public const string JobsManage = "jobs.manage";

    /// <summary>Lote 132 (P11): integraciones. Ver y su historial; crear (base) más un permiso por familia de alcances; administrar (editar, apagar, correr); y la bandeja de lo que no se pudo procesar.</summary>

    public const string IntegrationsView = "integrations.view";

    public const string IntegrationsCreate = "integrations.create";

    public const string IntegrationsCreateTransport = "integrations.create.transport";

    public const string IntegrationsCreateWarehouse = "integrations.create.warehouse";

    public const string IntegrationsCreateFiles = "integrations.create.files";

    public const string IntegrationsManage = "integrations.manage";

    public const string IntegrationsInbox = "integrations.inbox";

    public const string NotificationsManage = "notifications.manage";

    /// <summary>Lote 54: enviar avisos a otras personas (a personas, roles o todos).</summary>

    public const string NotificationsSend = "notifications.send";

    /// <summary>Lote 53: mensajería interna (hilos entre personas de la compañía). La traen TODOS los roles.</summary>

    public const string MessagesUse = "messages.use";

    /// <summary>Lote 55 (P3): administrar impuestos (tasas, reglas, categorías de producto y exenciones de clientes).</summary>

    public const string TaxManage = "tax.manage";

    /// <summary>Lote 57 (P4): órdenes de venta. Ver / crear, editar y solicitar / cancelar / cambiar el precio / surtir y cerrar backorders.</summary>

    public const string SalesView = "sales.view";

    public const string SalesCreate = "sales.create";

    public const string SalesCancel = "sales.cancel";

    public const string SalesPrice = "sales.price";

    public const string SalesBackorder = "sales.backorder";

    /// <summary>Lote 81 (P6): pedir una devolución de venta.</summary>

    public const string SalesReturn = "sales.return";

    /// <summary>Lote 81 (P6): autorizar o rechazar una devolución de venta (también la que está fuera de la ventana).</summary>

    public const string SalesReturnAuthorize = "sales.return.authorize";

    /// <summary>Lote 143 (2026-10-20): crear, modificar y cancelar avisos de salida y atender su pendiente (ver usa inventory.view y despachar warehouse.pick).</summary>

    public const string ExitNoticeCreate = "exitnotice.create";



    private static readonly IReadOnlyList<PermissionDef> AllByResource = new List<PermissionDef>

    {

        new(OrdersView, "ORDERS", "Ver envíos", "View shipments"),

        new(OrdersCreate, "ORDERS", "Crear envíos", "Create shipments"),

        new(OrdersEdit, "ORDERS", "Editar envíos", "Edit shipments"),

        new(OrdersCancel, "ORDERS", "Cancelar envíos", "Cancel shipments"),

        new(TripsPlan, "ORDERS", "Planificar rutas", "Plan routes"),

        new(TripsDispatch, "ORDERS", "Despachar rutas", "Dispatch routes"),

        new(TripsClose, "ORDERS", "Cerrar rutas", "Close routes"),

        new(TripsOptimize, "ORDERS", "Optimizar rutas", "Optimize routes"),

        new(WarehouseReceive, "WAREHOUSE", "Recibir", "Receive"),

        new(WarehousePick, "WAREHOUSE", "Pickear", "Pick"),

        new(WarehouseCount, "WAREHOUSE", "Contar", "Count"),

        new(WarehouseCrossdock, "WAREHOUSE", "Cross-dock", "Cross-dock"),

        new(BillingView, "BILLING", "Ver facturación", "View billing"),

        new(BillingCredit, "BILLING", "Notas de crédito", "Credit notes"),

        new(BillingGenerate, "BILLING", "Generar facturación", "Generate billing"),

        new(BillingApprove, "BILLING", "Aprobar facturación", "Approve billing"),

        new(BillingExport, "BILLING", "Exportar facturación", "Export billing"),

        new(FleetManage, "FLEET", "Gestionar flota", "Manage fleet"),

        new(FleetMaintenance, "FLEET", "Mantenimiento", "Maintenance"),

        new(AdminUsers, "ADMIN", "Gestionar usuarios", "Manage users"),

        new(AdminRoles, "ADMIN", "Gestionar roles", "Manage roles"),

        new(AdminCatalogs, "ADMIN", "Gestionar listas", "Manage catalogs"),

        new(AdminTenant, "ADMIN", "Configurar compañía", "Configure tenant"),

        new(AdminAudit, "ADMIN", "Ver seguridad y auditoría", "View security & audit"),

        new(AdminCustomFields, "ADMIN", "Gestionar campos personalizados", "Manage custom fields"),

        new(AdminStatusConfig, "ADMIN", "Configurar pipeline de estatus", "Configure status pipeline"),

        new(CodCollect, "BILLING", "Cobrar COD en entrega", "Collect COD on delivery"),

        new(CodReconcile, "BILLING", "Reconciliar COD", "Reconcile COD"),

        new(CodRemit, "BILLING", "Generar remesas COD", "Generate COD remittances"),

        new(CodView, "BILLING", "Ver COD", "View COD"),

        new(RentalView, "RENTAL", "Ver equipos en alquiler", "View rental equipment"),

        new(RentalManage, "RENTAL", "Gestionar contratos de alquiler", "Manage rental contracts"),

        new(RentalMaintenance, "RENTAL", "Registrar mantenimiento de equipo", "Log equipment maintenance"),

        new(RentalBilling, "RENTAL", "Generar cargos de alquiler", "Generate rental charges"),

        new(PurchasingView, "BILLING", "Ver órdenes de compra", "View purchase orders"),

        new(PurchasingManage, "BILLING", "Crear y editar órdenes de compra", "Create and edit purchase orders"),

        new(WarehousesManage, "WAREHOUSE", "Almacenes: crear y editar (incluye muelles)", "Warehouses: create and edit (includes docks)"),

        new(BinsManage, "WAREHOUSE", "Posiciones: crear, editar y eliminar (incluye zonas)", "Bins: create, edit and delete (includes zones)"),

        new(DispatchAccountingManage, "BILLING", "Contabilización de despachos: generar, aceptar, cancelar y exportar", "Dispatch accounting: generate, accept, cancel and export"),

        new(SuppliersManage, "BILLING", "Proveedores: crear y editar", "Suppliers: create and edit"),

        new(PurchaseAccountingManage, "BILLING", "Contabilización de compras: generar, aceptar, cancelar y exportar", "Purchase accounting: generate, accept, cancel and export"),

        new(PurchasingReceive, "BILLING", "Recibir contra orden de compra", "Receive against purchase order"),

        new(AnalyticsView, "ANALYTICS", "Ver vistas, indicadores y gráficos", "View reports, indicators & charts"),

        new(AnalyticsManage, "ANALYTICS", "Crear y editar vistas, indicadores y gráficos", "Create and edit reports, indicators & charts"),

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

        new(ClientMessagesReply, "CLIENTS", "Atender los mensajes de los clientes", "Handle client messages"),

        new(ClientMessagesManage, "CLIENTS", "Designar el equipo de servicio al cliente", "Manage the customer service team"),

        // Lote 3 — Órdenes de transporte (ajuste C: crédito excedido = aviso + autorización con permiso)

        new(OrdersCreditOverride, "ORDERS", "Autorizar envíos sobre el límite de crédito", "Authorize shipments over credit limit"),

        new(OrdersReopen, "ORDERS", "Reabrir envíos en estatus final", "Reopen shipments in a final status"),

        new(ClaimsView, "ORDERS", "Ver los faltantes y las reclamaciones", "View missing pieces and claims"),

        new(ClaimsManage, "ORDERS", "Marcar piezas como perdidas, reclamar y resolver reclamaciones", "Mark pieces as lost, claim and resolve claims"),

        new(OrdersReturn, "ORDERS", "Devolver envíos (crear el envío de vuelta)", "Return shipments (create the return shipment)"),

        new(OrdersDeliver, "ORDERS", "Registrar la entrega de un envío desde la web", "Register a shipment delivery from the web"),

        // Lote 4 — Flota, choferes y mantenimiento (flota separada de la compensación, R8)

        new(FleetView, "FLEET", "Ver flota y choferes", "View fleet & drivers"),

        new(DriverPayView, "BILLING", "Ver tarifas y viajes de choferes", "View driver rates & trips"),

        new(DriverPayManage, "BILLING", "Gestionar tarifas y viajes de choferes", "Manage driver rates & trips"),

        new(DriverPayApprove, "BILLING", "Aprobar liquidaciones a choferes", "Approve driver settlements"),

        new(DriverPayExport, "BILLING", "Exportar liquidaciones a choferes", "Export driver settlements"),

        // Lote 5 — Trips y rutas

        new(TripsView, "ORDERS", "Ver rutas y despacho", "View routes & dispatch"),

        new(TripsScan, "ORDERS", "Escanear salida (Outbound)", "Scan outbound"),

        // Lote 6 — Inventario y almacén

        new(InventoryView, "WAREHOUSE", "Ver inventario y almacén", "View inventory & warehouse"),

        new(InventoryManage, "WAREHOUSE", "Gestionar productos", "Manage products"),

        new(InventoryValue, "WAREHOUSE", "Ver el valor del inventario", "View inventory value"),

        new(InventoryAdjust, "WAREHOUSE", "Ajustar y transferir inventario", "Adjust & transfer inventory"),

        new(InventoryAdjustMulti, "WAREHOUSE", "Ajuste múltiple de inventario", "Multi-position inventory adjustment"),

        new(WarehouseManage, "WAREHOUSE", "Gestionar almacenes y tareas", "Manage warehouses & tasks"),

        // Lote 8A — App de almacén: aparatos de confianza y PIN

        new(DevicesManage, "SECURITY", "Gestionar aparatos y PIN", "Manage devices & PINs"),

        new(WarehouseCountCapture, "WAREHOUSE", "Capturar conteo (a ciegas)", "Capture count (blind)"),

        new(WarehouseDamage, "WAREHOUSE", "Reportar y resolver daños", "Report & resolve damage"),

        new(WarehouseTransfer, "WAREHOUSE", "Transferir inventario entre posiciones (aparato)", "Transfer stock between bins (device)"),

        new(WarehouseAdjust, "WAREHOUSE", "Ajustar la cantidad de una posición (aparato)", "Adjust a bin's quantity (device)"),

        new(WarehouseIssue, "WAREHOUSE", "Despacho manual (salida sin entrega)", "Manual issue (stock out without delivery)"),

        new(WarehousePickAny, "WAREHOUSE", "Despachar cualquier documento en el aparato (no solo los asignados)", "Dispatch any document on the device (not only assigned ones)"),

        new(WarehouseAppViewAll, "WAREHOUSE", "Ver todo en el aparato (también lo asignado a otras personas)", "See everything on the device (also what is assigned to others)"),

        new(WarehouseAppReassign, "WAREHOUSE", "Reasignar desde el aparato (exige ver todo)", "Reassign from the device (requires see-everything)"),

        // Lote F8a — Pulso del día (categoría PULSE)

        new(PulseIndicators, "PULSE", "Ver indicadores en el Pulso", "See indicators on the Pulse"),

        new(PulseCharts, "PULSE", "Ver gráficos en el Pulso", "See charts on the Pulse"),

        new(PulseWarehouse, "PULSE", "Ver el panel Almacén del Pulso", "See the Warehouse panel"),

        new(PulseActivity, "PULSE", "Ver Actividad reciente en el Pulso", "See Recent activity"),

        new(PulseOrganizeCompany, "PULSE", "Organizar el Pulso de la compañía", "Organize the company Pulse"),

        // Lote 88 (P12) — los dos ríos del Pulso

        new(PulseOrders, "PULSE", "Ver el río Paquetes en la calle del Pulso", "See the Packages on the street river"),

        new(PulseCod, "PULSE", "Ver el río Dinero COD del Pulso", "See the COD money river"),

        // Lote 14 — "Necesita tu atención" (D6)

        new(PulseAttention, "PULSE", "Ver la sección Necesita tu atención del Pulso", "See the Needs your attention section"),

        // Lote 27 — Rentas (R1)

        new(RentalExtend, "RENTAL", "Extender rentas", "Extend rentals"),

        new(RentalReturn, "RENTAL", "Registrar devoluciones de renta", "Register rental returns"),

        // Lote 39 — ver precios de las órdenes (2026-10-12)

        new(OrdersViewPrice, "ORDERS", "Ver precios de los envíos", "View shipment prices"),

        // Lote 43 — impresoras y plantillas de label (2026-10-12, F-G1)

        new(PrintersManage, "ADMIN", "Gestionar impresoras y plantillas de label", "Manage printers & label templates"),

        new(JobsManage, "ADMIN", "Gestionar los trabajos programados", "Manage scheduled jobs"),

        new(IntegrationsView, "ADMIN", "Ver las integraciones y su historial", "View integrations and their history"),

        new(IntegrationsCreate, "ADMIN", "Crear integraciones (base; además el permiso de cada familia)", "Create integrations (base; plus each family's permission)"),

        new(IntegrationsCreateTransport, "ADMIN", "Crear integraciones de transporte (envíos, tracking, facturas, consignatarios, perfil)", "Create transport integrations (shipments, tracking, invoices, consignees, profile)"),

        new(IntegrationsCreateWarehouse, "ADMIN", "Crear integraciones de almacén (inventario, productos, recibo, despacho, ajustes, transferencias, órdenes de compra y venta)", "Create warehouse integrations (inventory, products, receiving, dispatch, adjustments, transfers, purchase and sales orders)"),

        new(IntegrationsCreateFiles, "ADMIN", "Crear integraciones por archivo (carpeta, SFTP, correo)", "Create file integrations (folder, SFTP, email)"),

        new(IntegrationsManage, "ADMIN", "Administrar integraciones (editar, apagar, correr ahora)", "Manage integrations (edit, turn off, run now)"),

        new(IntegrationsInbox, "ADMIN", "Trabajar la bandeja de las integraciones (corregir, reprocesar, descartar)", "Work the integrations inbox (fix, reprocess, discard)"),

        new(NotificationsManage, "ADMIN", "Gestionar las reglas de aviso", "Manage notification rules"),

        new(NotificationsSend, "ADMIN", "Enviar avisos a otras personas", "Send notices to other people"),

        new(MessagesUse, "ADMIN", "Usar la mensajería interna", "Use internal messaging"),

        new(TaxManage, "BILLING", "Administrar impuestos (tasas, exenciones)", "Manage taxes (rates, exemptions)"),

        new(SalesView, "BILLING", "Ver órdenes de venta", "View sales orders"),

        new(SalesCreate, "BILLING", "Crear, editar y solicitar órdenes de venta", "Create, edit and request sales orders"),

        new(SalesCancel, "BILLING", "Cancelar órdenes de venta", "Cancel sales orders"),

        new(SalesPrice, "BILLING", "Cambiar el precio en las órdenes de venta", "Change the price on sales orders"),

        new(SalesBackorder, "BILLING", "Surtir y cerrar backorders", "Fulfill and close backorders"),

        new(SalesReturn, "BILLING", "Pedir devoluciones de venta", "Request sales returns"),

        new(SalesReturnAuthorize, "BILLING", "Autorizar devoluciones de venta", "Authorize sales returns"),

        new(ExitNoticeCreate, "WAREHOUSE", "Crear, modificar y cancelar avisos de salida y atender su pendiente", "Create, edit and cancel exit notices and handle their backorder"),

    };



    /// <summary>

    /// El catálogo en orden de aparición (la pantalla de roles agrupa por categoría en este orden): primero «Acceso al menú» (<see cref="MenuPermissions.Items"/>, 44 permisos en el orden del menú) y

    /// después los demás por recurso. Los 16 permisos de acceso que ya existían se mueven a la categoría MENU (conservan su código).

    /// </summary>

    public static readonly IReadOnlyList<PermissionDef> All = MenuPermissions.Items

        .Concat(AllByResource.Where(p => !MenuPermissions.Items.Any(m => string.Equals(m.Code, p.Code, StringComparison.OrdinalIgnoreCase))))

        .ToList();



    /// <summary>

    /// Permisos implícitos (Lote 8A): tener la llave de la izquierda da también las de la derecha en los permisos efectivos

    /// (PermissionService). warehouse.count (contar en modo informado y reconciliar) incluye capturar a ciegas, para que los

    /// roles propios que ya cuentan con warehouse.count no pierdan la captura al separarse el permiso.

    /// </summary>

    public static readonly IReadOnlyDictionary<string, string[]> Implied = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)

    {

        [WarehouseCount] = new[] { WarehouseCountCapture },

        [InventoryAdjust] = new[] { WarehouseTransfer },   // 2026-10-10: quien ya ajusta inventario puede transferir

    };



    /// <summary>Agrega al conjunto los permisos implícitos de los que ya contiene (lógica pura, probada con xunit).</summary>

    public static void ExpandImplied(ISet<string> codes)

    {

        // hasta que no se agregue nada: editar ⇒ ver (acceso al menú) ⇒ leer los datos (Lote 152)

        bool grew;

        do

        {

            grew = false;

            foreach (var rules in new[] { Implied, MenuPermissions.EditImplies, MenuPermissions.ViewImplies })

                foreach (var (code, implied) in rules)

                    if (codes.Contains(code))

                        foreach (var i in implied)

                            grew |= codes.Add(i);

        } while (grew);

    }



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

        // Lote 3

        [EntityTypes.TransportOrder] = OrdersView,

        [EntityTypes.OrderCod] = OrdersView,

        [EntityTypes.OrderStop] = OrdersView,

        [EntityTypes.ImportBatch] = OrdersView,

        [EntityTypes.ImportTemplate] = OrdersEdit,

        // Lote 4 — flota con fleet.view; compensación con driverpay.view

        [EntityTypes.Vehicle] = FleetView,

        [EntityTypes.Driver] = FleetView,

        [EntityTypes.DispatchZone] = FleetView,

        [EntityTypes.MaintenanceSchedule] = FleetView,

        [EntityTypes.WorkOrder] = FleetView,

        [EntityTypes.FuelLog] = FleetView,

        [EntityTypes.FleetDocument] = FleetView,

        [EntityTypes.DriverRate] = DriverPayView,

        [EntityTypes.DriverTrip] = DriverPayView,

        // Lote 5 — rutas

        [EntityTypes.Trip] = TripsView,

        [EntityTypes.Route] = TripsView,

        [EntityTypes.RouteStop] = TripsView,

        [EntityTypes.OptimizationRun] = TripsView,

        // Lote 6 — inventario y almacén con inventory.view; compras con purchasing.view

        [EntityTypes.Warehouse] = InventoryView,

        [EntityTypes.WarehouseDock] = InventoryView,
        [EntityTypes.WarehouseZone] = InventoryView,
        [EntityTypes.WarehouseBin] = InventoryView,

        [EntityTypes.Product] = InventoryView,

        [EntityTypes.InventorySerial] = InventoryView,

        [EntityTypes.Receipt] = InventoryView,

        [EntityTypes.Asn] = InventoryView,

        [EntityTypes.WarehouseTask] = InventoryView,

        [EntityTypes.CycleCount] = InventoryView,

        [EntityTypes.MultiAdjustment] = InventoryView,

        [EntityTypes.PickBatch] = InventoryView,

        [EntityTypes.DockAppointment] = InventoryView,

        [EntityTypes.CrossDockPlan] = InventoryView,

        [EntityTypes.CrossDockAllocation] = InventoryView,

        // Sin escritura de dueño (resolver cerrado): la lectura polimórfica (campos personalizados, contactos) exige inventory.view.

        [EntityTypes.StockBalance] = InventoryView,

        [EntityTypes.InventoryTransaction] = InventoryView,

        [EntityTypes.ReceiptLine] = InventoryView,

        // Lote 7A: la categoría de producto tiene EntityType propio (auditoría); sin campos personalizados (resolver cerrado).

        [EntityTypes.ProductCategory] = InventoryView,

        // Lote 8A: aparato de almacén (solo auditoría; resolver cerrado): sin devices.manage → 403, con él → 404.

        [EntityTypes.UserDevice] = DevicesManage,

        [EntityTypes.PurchaseOrder] = PurchasingView,

        [EntityTypes.Supplier] = PurchasingView,

        // Lote 14: descuadre Kárdex ↔ saldo (lo ve quien ve inventario, D5); sin escritura de dueño (resolver cerrado).

        [EntityTypes.InventoryDiscrepancy] = InventoryView,

        // Lote 27 (Rentas): renta, devolución de renta y proceso del equipo devuelto con rental.view.

        [EntityTypes.Rental] = RentalView,

        [EntityTypes.RentalReturn] = RentalView,

        [EntityTypes.RentalProcess] = RentalView,

    };



    /// <summary>

    /// Lote F8a (§2.2 del plan): permiso de lectura de las fuentes de datos de Análisis que no son dueñas de rutas polimórficas

    /// (no están en <see cref="OwnerReadPermission"/>): la bitácora y los eventos de seguridad exigen admin.audit (como la

    /// auditoría) y el directorio de usuarios admin.users. Solo lo usa Análisis (indicadores, gráficos y Pulso).

    /// </summary>

    public static readonly IReadOnlyDictionary<string, string> DataSourceExtraReadPermission = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)

    {

        [EntityTypes.AuditLog] = AdminAudit,

        [EntityTypes.SecurityEvent] = AdminAudit,

        [EntityTypes.User] = AdminUsers,

        // Lote 67 (P4 S4): análisis de órdenes de venta con sales.view

        [Sales.SalesCodes.EntityType] = SalesView,

        [EntityTypes.SalesOrderLine] = SalesView,
        // menores 2026-10-21: análisis de avisos de salida con inventory.view (como su lectura en el API)
        [ExitNotices.ExitNoticeCodes.EntityType] = InventoryView,

        // Lote 90 (P12): el historial diario de indicadores se lee con analytics.view

        [Analytics.KpiCodes.DataSource] = AnalyticsView,

        // Lote 91 (P12): cobros COD y facturas

        [Analytics.FinanceAnalyticsRules.CodSource] = CodView,

        [Analytics.FinanceAnalyticsRules.InvoiceSource] = BillingView,

    };



    /// <summary>

    /// Permiso de lectura de una fuente de datos por su EntityType (Lote F8a, §2.2 regla 3): el de <see cref="OwnerReadPermission"/>

    /// o, si no está, el de <see cref="DataSourceExtraReadPermission"/>; null = sin requisito (la fuente se trata como visible).

    /// </summary>

    public static string? DataSourceReadPermission(string? entityTypeCode)

    {

        if (string.IsNullOrWhiteSpace(entityTypeCode)) return null;

        if (OwnerReadPermission.TryGetValue(entityTypeCode, out var owner)) return owner;

        return DataSourceExtraReadPermission.TryGetValue(entityTypeCode, out var extra) ? extra : null;

    }



    /// <summary>Permiso de escritura del módulo dueño para poner valores de campos personalizados en un registro.</summary>

    public static readonly IReadOnlyDictionary<string, string> OwnerWritePermission = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)

    {

        [EntityTypes.Client] = ClientsUpdate,

        [EntityTypes.ClientContact] = ClientsUpdate,

        [EntityTypes.Location] = LocationsUpdate,

        [EntityTypes.Contract] = ContractsUpdate,

        [EntityTypes.PortalUser] = PortalUsersManage,

        // Lote 3

        [EntityTypes.TransportOrder] = OrdersEdit,

        [EntityTypes.OrderCod] = OrdersEdit,

        [EntityTypes.OrderStop] = OrdersEdit,

        [EntityTypes.ImportBatch] = OrdersEdit,

        [EntityTypes.ImportTemplate] = OrdersEdit,

        // Lote 4

        [EntityTypes.Vehicle] = FleetManage,

        [EntityTypes.Driver] = FleetManage,

        [EntityTypes.DispatchZone] = FleetManage,

        [EntityTypes.FleetDocument] = FleetManage,

        [EntityTypes.MaintenanceSchedule] = FleetMaintenance,

        [EntityTypes.WorkOrder] = FleetMaintenance,

        [EntityTypes.FuelLog] = FleetMaintenance,

        [EntityTypes.DriverRate] = DriverPayManage,

        [EntityTypes.DriverTrip] = DriverPayManage,

        // Lote 5 — rutas (OPTIMIZATION_RUN es bitácora: solo lectura)

        [EntityTypes.Trip] = TripsPlan,

        [EntityTypes.Route] = TripsPlan,

        [EntityTypes.RouteStop] = TripsPlan,

        // Lote 6 — INVENTORY_SERIAL, WAREHOUSE_TASK y CROSSDOCK_ALLOCATION sin escritura de dueño (resolver cerrado)

        [EntityTypes.Warehouse] = WarehouseManage,

        [EntityTypes.WarehouseDock] = WarehouseManage,
        [EntityTypes.WarehouseZone] = WarehouseManage,
        [EntityTypes.WarehouseBin] = WarehouseManage,

        [EntityTypes.Product] = InventoryManage,

        [EntityTypes.ProductCategory] = InventoryManage,

        [EntityTypes.UserDevice] = DevicesManage,

        [EntityTypes.Receipt] = WarehouseReceive,

        [EntityTypes.Asn] = WarehouseReceive,

        [EntityTypes.CycleCount] = WarehouseCount,

        [EntityTypes.PickBatch] = WarehousePick,

        [EntityTypes.DockAppointment] = WarehouseCrossdock,

        [EntityTypes.CrossDockPlan] = WarehouseCrossdock,

        [EntityTypes.PurchaseOrder] = PurchasingManage,

        [EntityTypes.Supplier] = PurchasingManage,

        // Lote 27 (Rentas): la renta con rental.manage, la devolución con rental.return y el proceso con rental.maintenance.

        [EntityTypes.Rental] = RentalManage,

        [EntityTypes.RentalReturn] = RentalReturn,

        [EntityTypes.RentalProcess] = RentalMaintenance,

    };



    /// <summary>Plantillas de rol de sistema (TenantId NULL) y sus permisos por defecto — clonables al aprovisionar.</summary>

    /// <summary>Lote 53: a cada plantilla se le suma <see cref="MessagesUse"/> (todos los roles se pueden escribir).</summary>

    private static IReadOnlyDictionary<string, string[]> WithMessaging(Dictionary<string, string[]> templates)

        => templates.ToDictionary(kv => kv.Key, kv => WithMenuAccess(kv.Value.Contains(MessagesUse) ? kv.Value : kv.Value.Append(MessagesUse).ToArray()));



    /// <summary>Lote 152: a cada plantilla se le suman los permisos de acceso al menú que reemplazan a los que ya tenía (<see cref="MenuPermissions.Grants"/>) y pulse.view (todos los roles).</summary>

    private static string[] WithMenuAccess(string[] codes)

    {

        var set = new HashSet<string>(codes, StringComparer.OrdinalIgnoreCase) { MenuPermissions.PulseView };

        foreach (var (old, news) in MenuPermissions.Grants)

            if (codes.Contains(old, StringComparer.OrdinalIgnoreCase))

                foreach (var n in news) set.Add(n);

        return set.ToArray();

    }



    public static readonly IReadOnlyDictionary<string, string[]> RoleTemplates = WithMessaging(new Dictionary<string, string[]>

    {

        ["TenantAdmin"] = All.Select(p => p.Code).ToArray(),

        ["Dispatcher"] = new[] { OrdersView, OrdersCreate, OrdersEdit, OrdersCancel, TripsPlan, TripsDispatch, TripsClose, TripsOptimize, AnalyticsView, ClientsRead, LocationsRead, LocationsCreate, FleetView, TripsView, TripsScan,

            PulseIndicators, PulseCharts, PulseActivity,   // Lote F8a

            PulseOrders },   // Lote 88: el río Paquetes en la calle

        ["Billing"] = new[] { OrdersView, BillingView, BillingCredit, BillingGenerate, BillingApprove, BillingExport, CodView, CodReconcile, CodRemit, RentalBilling, RentalView, PurchasingView, PurchasingManage, SuppliersManage, PurchaseAccountingManage, DispatchAccountingManage, AnalyticsView, ClientsRead, ContractsRead, OrdersCreditOverride, DriverPayView, InventoryView, InventoryValue,

            PulseIndicators, PulseCharts, PulseActivity,   // Lote F8a

            PulseAttention,   // Lote 14 (D6): la ve quien ve inventario

            PulseOrders, PulseCod,   // Lote 88: los dos ríos

            OrdersViewPrice },   // Lote 39: Facturación ve los precios de las órdenes

        // 2026-10-01 (Luis), como lo dejó en Advance Logistics: sin cod.reconcile, sin rutas (trips.*) ni compras (purchasing.*;

        // no recibe contra órdenes de compra) y con ajustar/administrar inventario y administrar almacenes.

        ["WarehouseOperator"] = new[] { WarehouseReceive, WarehousePick, WarehousePickAny, WarehouseCount, WarehouseCountCapture, WarehouseDamage, WarehouseTransfer, WarehouseIssue, WarehouseCrossdock, RentalView, RentalManage, RentalMaintenance, InventoryView,

            InventoryAdjust, InventoryManage, WarehouseManage,

            // AnalyticsView: decisión de Luis (Lote F8a) — sin él, la política de /analytics/activity bloquea a este

            // rol antes de llegar al servicio, y no vería "Actividad reciente" en su Pulso pese a tener pulse.activity.

            // El filtro por fuente de datos (§2.2) y el de módulo del servicio lo siguen acotando a WAREHOUSE.

            AnalyticsView, PulseWarehouse, PulseIndicators, PulseCharts, PulseActivity,   // Lote F8a

            PulseAttention,   // Lote 14 (D6)

            RentalExtend, RentalReturn,   // Lote 27 (Rentas R1)

            SalesView, SalesBackorder,   // Lote 57: ve las órdenes de venta y surte los backorders

            ExitNoticeCreate },   // Lote 143: avisos de salida

        // Lote 57: quien arma las ventas (sin precio ni backorder: eso lo da el administrador)

        ["Sales"] = new[] { SalesView, SalesCreate, SalesCancel, SalesReturn, InventoryView, ClientsRead },

        // Lote 104: el rol «Servicio al cliente» atiende los mensajes de los clientes (si un cliente no tiene equipo designado, lo atiende quien tenga ClientMessagesReply)

        ["CustomerService"] = new[] { ClientMessagesReply, ClientsRead, OrdersView, LocationsRead, ContractsRead },

        ["Driver"] = new[] { OrdersView, CodCollect },

        ["ReadOnly"] = new[] { OrdersView, CodView, AnalyticsView, ClientsRead, LocationsRead, ContractsRead, FleetView, TripsView, InventoryView,

            PulseIndicators, PulseCharts, PulseActivity,   // Lote F8a

            PulseAttention,   // Lote 14 (D6)

            PulseOrders, PulseCod },   // Lote 88: los dos ríos

    });



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

        ["Sales"] = ("Ventas", "Sales"),

        ["WarehouseOperator"] = ("Operador de almacén", "Warehouse operator"),

        ["CustomerService"] = ("Servicio al cliente", "Customer service"),

        ["Driver"] = ("Chofer", "Driver"),

        ["ReadOnly"] = ("Solo lectura", "Read only"),

    };

}

