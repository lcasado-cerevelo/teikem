namespace Teikem.Domain.Constants;

/// <summary>Scope de CatalogDomain: constante de sistema, aquí se corta la recursión de "catálogo de catálogos".</summary>
public static class CatalogScope
{
    public const byte Lookup = 1;
    public const byte Status = 2;
}

/// <summary>Llaves de dominios de catálogo (LookupCode.Entity) que el código transversal usa por nombre.</summary>
public static class LookupDomains
{
    public const string StageKind = "StageKind";
    public const string EntityType = "EntityType";
    public const string ContactType = "ContactType";
    public const string Capability = "Capability";
    public const string UserKind = "UserKind";
    public const string PermissionCategory = "PermissionCategory";
    public const string MfaFactorType = "MfaFactorType";
    public const string AuditAction = "AuditAction";
    public const string SecurityEventType = "SecurityEventType";
    public const string SecurityOutcome = "SecurityOutcome";
    public const string CustomFieldDataType = "CustomFieldDataType";
    public const string ReportVisibility = "ReportVisibility";
    public const string ReportChartType = "ReportChartType";
    public const string AggregateFn = "AggregateFn";
    public const string BusinessModule = "BusinessModule";
    public const string DateRangeMode = "DateRangeMode";
    public const string PackageType = "PackageType";
    public const string ServiceType = "ServiceType";
    // Lote 2 — Clientes y contratos
    public const string PaymentTerm = "PaymentTerm";
    public const string Currency = "Currency";
    public const string LocationType = "LocationType";
    public const string Country = "Country";
    public const string BillingModel = "BillingModel";
    public const string PricingType = "PricingType";
    public const string PricingMode = "PricingMode";
    public const string TierMode = "TierMode";
    public const string RateBasis = "RateBasis";
    public const string RateComponentType = "RateComponentType";
    public const string PortalRole = "PortalRole";
    // Lote 3 — Órdenes de transporte
    public const string StopType = "StopType";
    public const string OrderPriority = "OrderPriority";
    public const string OrderRefType = "OrderRefType";
    public const string CodType = "CodType";
    public const string GeocodeAccuracy = "GeocodeAccuracy";
    public const string UnitOfMeasure = "UnitOfMeasure";
    // Lote 4 — Flota, choferes y mantenimiento
    public const string VehicleType = "VehicleType";
    public const string Ownership = "Ownership";
    public const string FuelType = "FuelType";
    public const string VehicleDocType = "VehicleDocType";
    public const string MaintenanceTrigger = "MaintenanceTrigger";
    public const string MaintenanceType = "MaintenanceType";
    public const string LicenseClass = "LicenseClass";
    public const string CertificationType = "CertificationType";
    public const string DevicePlatform = "DevicePlatform";
    /// <summary>Fórmula de pago a choferes (DriverPayPolicy.PayoutFormulaLookupId).</summary>
    public const string DriverPayoutFormula = "DriverPayoutFormula";
    // Lote 5 — Trips y rutas (GeocodeAccuracy ya existe desde el Lote 3)
    /// <summary>Criterio de un miembro de zona de despacho (DispatchZoneMember.MatchTypeLookupId).</summary>
    public const string ZoneMatchType = "ZoneMatchType";
    /// <summary>Motor de una corrida de optimización (OptimizationRun.EngineLookupId).</summary>
    public const string OptimizerEngine = "OptimizerEngine";
    // Lote 6 — Inventario y almacén (UnitOfMeasure y PaymentTerm ya existen)
    public const string ZoneType = "ZoneType";
    public const string DockType = "DockType";
    public const string DockDirection = "DockDirection";
    public const string TrackingType = "TrackingType";
    public const string InventoryTxnType = "InventoryTxnType";
    public const string ReceiptType = "ReceiptType";
    public const string WarehouseTaskType = "WarehouseTaskType";
    /// <summary>Motivo de un ajuste de inventario (InventoryTransaction.ReasonLookupId; obligatorio en ADJUSTMENT, D7).</summary>
    public const string AdjustmentReason = "AdjustmentReason";
    /// <summary>Acción con la que se resuelve un faltante de orden de compra (PurchaseOrderShortageResolution.ActionLookupId, D8).</summary>
    public const string ShortageAction = "ShortageAction";
    // Lote 7A — Pulso y Actividad reciente
    /// <summary>
    /// Catálogo de eventos de "Actividad reciente" (ActivityEvents); ExtraJson {"module","mandatory","defaultOn"}. El mismo
    /// catálogo y bandera alimentarán las notificaciones.
    /// </summary>
    public const string ActivityEventType = "ActivityEventType";
    // Lote 8A — App de almacén (aparatos de confianza e idempotencia)
    /// <summary>Tema por defecto del aparato de almacén (UserDevice.ThemeLookupId): LIGHT | DARK.</summary>
    public const string UiTheme = "UiTheme";
    /// <summary>Dirección del mensaje registrado en IntegrationMessageLog (la idempotencia del API escribe INBOUND).</summary>
    public const string MessageDirection = "MessageDirection";
    // Lote 14 — conciliación Kárdex ↔ saldo con tabla de descuadres
    /// <summary>Tipo de descuadre (InventoryDiscrepancy.KindLookupId): BALANCE | PRODUCT_TOTAL.</summary>
    public const string InventoryDiscrepancyKind = "InventoryDiscrepancyKind";
    /// <summary>Origen de la revisión que detectó el descuadre (InventoryDiscrepancy.TriggerLookupId).</summary>
    public const string ReconciliationTrigger = "ReconciliationTrigger";
    /// <summary>Origen del conteo cíclico (CycleCount.OriginLookupId): MANUAL (selección) | CHANGES (lo cambiado).</summary>
    public const string CycleCountOrigin = "CycleCountOrigin";
    // Lote 16 — recibo directo a posición
    /// <summary>Modo de recepción del almacén y del recibo (Warehouse/ReceiptHeader.ReceivingModeLookupId): PUTAWAY | DIRECT.</summary>
    public const string ReceivingMode = "ReceivingMode";
}

/// <summary>
/// Lote 16: valores de LookupCode 'ReceivingMode'. PUTAWAY (Con acomodo) = la mercancía entra a la posición de recepción y
/// se crean tareas de acomodo; DIRECT (Directo a posición) = cada línea lleva su posición destino y entra ahí al confirmar,
/// sin tareas. NULL en la base = PUTAWAY (el seed rellena los almacenes y recibos existentes).
/// </summary>
public static class ReceivingModes
{
    public const string Putaway = "PUTAWAY";
    public const string Direct = "DIRECT";
    public static readonly string[] All = { Putaway, Direct };
}

/// <summary>
/// Lote 14: valores de LookupCode 'CycleCountOrigin'. MANUAL = alta por selección (web o app, por posición); CHANGES = "Conteo
/// de lo cambiado" (un conteo por posición con movimientos en una ventana; guarda la ventana en ChangesFromUtc/ChangesToUtc).
/// Los conteos anteriores al Lote 14 se marcan MANUAL en el seed. Lote 21: PRODUCT = conteo por producto (alta con productos y
/// sin posiciones ni zonas: una línea por cada posición donde el sistema dice que hay existencia).
/// </summary>
public static class CycleCountOrigins
{
    public const string Manual = "MANUAL";
    public const string Changes = "CHANGES";
    public const string Product = "PRODUCT";
}

/// <summary>Lote 14: valores de LookupCode 'InventoryDiscrepancyKind'.</summary>
public static class DiscrepancyKinds
{
    /// <summary>Saldo de una clave (producto, almacén, posición, lote) distinto de lo que da el Kárdex.</summary>
    public const string Balance = "BALANCE";
    /// <summary>Total del producto (Σ movimientos sin transferencias ≠ Σ en mano).</summary>
    public const string ProductTotal = "PRODUCT_TOTAL";
}

/// <summary>
/// Lote 14: valores de LookupCode 'ReconciliationTrigger'. EVENT = revisión automática tras un movimiento (P2); MANUAL =
/// "Ejecutar conciliación"; SCHEDULED = barrido programado (reservado: el motor programado queda fuera del lote);
/// MIGRATION = cierre de import-legacy.
/// </summary>
public static class ReconciliationTriggers
{
    public const string Event = "EVENT";
    public const string Manual = "MANUAL";
    public const string Scheduled = "SCHEDULED";
    public const string Migration = "MIGRATION";
}

/// <summary>Lote 8A: valores de LookupCode 'UiTheme' (tema del aparato de almacén).</summary>
public static class UiThemes
{
    public const string Light = "LIGHT";
    public const string Dark = "DARK";
}

/// <summary>Valores de LookupCode 'MessageDirection' (IntegrationMessageLog.DirectionLookupId).</summary>
public static class MessageDirections
{
    public const string Inbound = "INBOUND";
    public const string Outbound = "OUTBOUND";
}

/// <summary>Dominios de estatus (StatusCode.Entity) que usa la capa transversal.</summary>
public static class StatusDomains
{
    public const string MembershipStatus = "MembershipStatus";
    public const string OrderStatus = "OrderStatus";
    // Lote 2 — Clientes y contratos
    public const string ClientStatus = "ClientStatus";
    public const string ContractStatus = "ContractStatus";
    public const string PortalUserStatus = "PortalUserStatus";
    // Lote 3 — Órdenes de transporte
    public const string StopStatus = "StopStatus";
    public const string CodStatus = "CodStatus";
    /// <summary>Lotes del importador de órdenes: VALIDATED (inicial) → CONFIRMED; DISCARDED terminal.</summary>
    public const string ImportBatchStatus = "ImportBatchStatus";
    // Lote 4 — Flota, choferes y mantenimiento
    public const string VehicleStatus = "VehicleStatus";
    public const string DriverStatus = "DriverStatus";
    public const string WorkOrderStatus = "WorkOrderStatus";
    /// <summary>Viaje pagado al chofer: OPEN (inicial) → SETTLED | CANCELLED (terminales).</summary>
    public const string DriverTripStatus = "DriverTripStatus";
    // Lote 5 — Trips y rutas
    public const string TripStatus = "TripStatus";
    public const string RouteStatus = "RouteStatus";
    public const string RouteStopStatus = "RouteStopStatus";
    public const string OptimizationRunStatus = "OptimizationRunStatus";
    // Lote 6 — Inventario y almacén
    public const string WarehouseStatus = "WarehouseStatus";
    public const string DockStatus = "DockStatus";
    public const string SerialStatus = "SerialStatus";
    public const string AsnStatus = "AsnStatus";
    public const string ReceiptStatus = "ReceiptStatus";
    public const string WarehouseTaskStatus = "WarehouseTaskStatus";
    public const string CycleCountStatus = "CycleCountStatus";
    /// <summary>Recolección y empaque ad hoc: COLLECTED (inicial) → PACKED; CANCELLED terminal (D20).</summary>
    public const string PickBatchStatus = "PickBatchStatus";
    public const string PurchaseOrderStatus = "PurchaseOrderStatus";
    public const string AppointmentStatus = "AppointmentStatus";
    public const string CrossDockStatus = "CrossDockStatus";
    public const string AllocationStatus = "AllocationStatus";
    // Lote 14
    /// <summary>Descuadre Kárdex ↔ saldo: OPEN (inicial) → RESOLVED | DISMISSED | SELF_CORRECTED (terminales).</summary>
    public const string InventoryDiscrepancyStatus = "InventoryDiscrepancyStatus";
}

public static class StageKinds
{
    public const string Pipeline = "PIPELINE";
    public const string Lateral = "LATERAL";
    public const string Terminal = "TERMINAL";
}

public static class UserKinds
{
    public const string Internal = "INTERNAL";
    public const string Portal = "PORTAL";
    public const string Service = "SERVICE";
}

public static class MembershipStatuses
{
    public const string Active = "ACTIVE";
    public const string Suspended = "SUSPENDED";
    public const string Invited = "INVITED";
}

// ---------------- Lote 2 — Clientes y contratos ----------------

/// <summary>Dominio ClientStatus: ACTIVE (inicial, pipeline), SUSPENDED (lateral reversible), PROSPECT (pipeline).</summary>
public static class ClientStatuses
{
    public const string Active = "ACTIVE";
    public const string Suspended = "SUSPENDED";
    public const string Prospect = "PROSPECT";
}

/// <summary>Dominio ContractStatus: DRAFT (inicial) → ACTIVE; EXPIRED y CANCELLED son terminales (solo por transición manual).</summary>
public static class ContractStatuses
{
    public const string Draft = "DRAFT";
    public const string Active = "ACTIVE";
    public const string Expired = "EXPIRED";
    public const string Cancelled = "CANCELLED";
}

/// <summary>Dominio PortalUserStatus: INVITED (inicial) → ACTIVE; SUSPENDED lateral reversible; DISABLED terminal (baja definitiva).</summary>
public static class PortalUserStatuses
{
    public const string Invited = "INVITED";
    public const string Active = "ACTIVE";
    public const string Suspended = "SUSPENDED";
    public const string Disabled = "DISABLED";
}

/// <summary>RateComponentType: BASE_FREIGHT = tarifa por servicio; EXTRA_PIECE = pieza extra (escalonada por piezas).</summary>
public static class RateComponentTypes
{
    public const string BaseFreight = "BASE_FREIGHT";
    public const string PickupFee = "PICKUP_FEE";
    public const string Surcharge = "SURCHARGE";
    public const string ExtraPiece = "EXTRA_PIECE";
}

public static class PricingModes
{
    public const string Fixed = "FIXED";
    public const string PerUnit = "PER_UNIT";
    public const string Tiered = "TIERED";
}

public static class TierModes
{
    public const string Graduated = "GRADUATED";
    public const string Volume = "VOLUME";
}

public static class RateBases
{
    public const string PerShipment = "PER_SHIPMENT";
    public const string PerPiece = "PER_PIECE";
}

/// <summary>PricingType del cargo por COD del contrato: FIXED = monto fijo por orden; PERCENT = por ciento del monto COD cobrado.</summary>
public static class PricingTypes
{
    public const string Fixed = "FIXED";
    public const string Percent = "PERCENT";
}

public static class PortalRoles
{
    public const string ClientAdmin = "CLIENT_ADMIN";
    public const string ClientOperator = "CLIENT_OPERATOR";
    public const string ClientReadOnly = "CLIENT_READONLY";
}

/// <summary>
/// LocationType: PICKUP/BOTH = almacenes o puntos de recogido del cliente; DELIVERY = consignatarios;
/// CORPORATE = dirección corporativa (física); BILLING = dirección postal / de facturación.
/// </summary>
public static class LocationTypes
{
    public const string Pickup = "PICKUP";
    public const string Delivery = "DELIVERY";
    public const string Both = "BOTH";
    public const string Billing = "BILLING";
    public const string Corporate = "CORPORATE";
}

/// <summary>Capacidades del motor de estatus (LookupDomains.Capability) que el código consulta por nombre.</summary>
public static class Capabilities
{
    public const string EditCargo = "EDIT_CARGO";
    public const string AssignTrip = "ASSIGN_TRIP";
    public const string Cancel = "CANCEL";
    public const string Reprice = "REPRICE";
    public const string AddDocument = "ADD_DOCUMENT";
    /// <summary>Lote 2: editar contrato, tarifas y servicios especiales (por defecto no permitido en EXPIRED/CANCELLED).</summary>
    public const string EditContract = "EDIT_CONTRACT";
    /// <summary>Lote 4: editar una orden de trabajo de mantenimiento y sus tareas (por defecto no permitido en CLOSED/CANCELLED).</summary>
    public const string EditWorkOrder = "EDIT_WORK_ORDER";
    /// <summary>
    /// Lote 5: editar la cabecera de una ruta (chofer, vehículo, hora de salida). Por defecto negada desde DISPATCHED,
    /// IN_PROGRESS, COMPLETED y CANCELLED; el tenant puede habilitarla en DISPATCHED/IN_PROGRESS.
    /// </summary>
    public const string EditTrip = "EDIT_TRIP";
    /// <summary>
    /// Lote 6 (D46): editar una orden de compra (fechas, notas y líneas). Por defecto negada en SENT, PARTIAL, RECEIVED y
    /// CANCELLED; el tenant puede habilitarla en SENT/PARTIAL (las líneas con recepciones siguen protegidas).
    /// </summary>
    public const string EditPurchaseOrder = "EDIT_PURCHASE_ORDER";
}

public static class AuditActions
{
    public const string Create = "CREATE";
    public const string Update = "UPDATE";
    public const string Delete = "DELETE";
    public const string Restore = "RESTORE";
}

public static class SecurityEventTypes
{
    public const string Login = "LOGIN";
    public const string Logout = "LOGOUT";
    public const string Mfa = "MFA";
    public const string Reauth = "REAUTH";
    public const string PermissionDenied = "PERMISSION_DENIED";
    public const string TokenRevoked = "TOKEN_REVOKED";
    public const string RoleChange = "ROLE_CHANGE";
    public const string PasswordChange = "PASSWORD_CHANGE";
    public const string Lockout = "LOCKOUT";
    public const string ApiCredential = "API_CREDENTIAL";
    public const string TenantSwitch = "TENANT_SWITCH";
}

public static class SecurityOutcomes
{
    public const string Success = "SUCCESS";
    public const string Failure = "FAILURE";
    public const string Blocked = "BLOCKED";
}

public static class MfaFactorTypes
{
    public const string Totp = "TOTP";
    public const string Sms = "SMS";
    public const string Biometric = "BIOMETRIC";
    public const string Recovery = "RECOVERY";
}

public static class CustomFieldDataTypes
{
    public const string Text = "TEXT";
    public const string Number = "NUMBER";
    public const string Date = "DATE";
    public const string DateTime = "DATETIME";
    public const string Bool = "BOOL";
    public const string Select = "SELECT";
    public const string MultiSelect = "MULTISELECT";
    public const string LookupRef = "LOOKUP_REF";
}

public static class ReportVisibilities
{
    public const string Private = "PRIVATE";
    public const string Tenant = "TENANT";
    public const string Shared = "SHARED";
}

public static class ChartTypes
{
    public const string Table = "TABLE";
    public const string Bar = "BAR";
    public const string Line = "LINE";
    public const string Pie = "PIE";
    public const string Donut = "DONUT";
}

/// <summary>
/// Lote 15 (D9, D14): claves de siembra (ChartDefinition.SeedKey) de los gráficos de la compañía que trae la plataforma. No son
/// de sistema: son de la compañía, sin dueño, y los edita o borra quien tenga analytics.manage; si se borran no vuelven.
/// </summary>
public static class ChartSeedKeys
{
    /// <summary>"Valor de inventario por categoría" (dona con el total; 7 categorías mayores + "Otras").</summary>
    public const string InventoryValue = "INVENTORY_VALUE";
    /// <summary>"Movimientos de inventario por tipo" (barras en unidades positivas; últimos 7 días, rango editable).</summary>
    public const string MovementsByType = "MOVEMENTS_BY_TYPE";
}

public static class AggregateFns
{
    public const string Count = "COUNT";
    public const string Sum = "SUM";
    public const string Avg = "AVG";
    public const string Min = "MIN";
    public const string Max = "MAX";
}

public static class BusinessModules
{
    public const string Operations = "OPERATIONS";
    public const string Warehouse = "WAREHOUSE";
    public const string Accounting = "ACCOUNTING";
}

public static class DateRangeModes
{
    public const string Last7 = "LAST7";
    public const string Last30 = "LAST30";
    public const string ThisMonth = "THIS_MONTH";
    public const string Custom = "CUSTOM";
    public const string All = "ALL";
}

/// <summary>Códigos del catálogo EntityType usados por la capa transversal (los de negocio llegan en lotes posteriores).</summary>
public static class EntityTypes
{
    public const string Client = "CLIENT";
    public const string ClientContact = "CLIENT_CONTACT";
    public const string Driver = "DRIVER";
    public const string Location = "LOCATION";
    public const string Warehouse = "WAREHOUSE";
    public const string Vehicle = "VEHICLE";
    public const string TransportOrder = "TRANSPORT_ORDER";
    public const string Trip = "TRIP";
    public const string Contract = "CONTRACT";
    public const string Product = "PRODUCT";
    public const string Invoice = "INVOICE";
    // Agregados por el Lote 1 (capa transversal)
    public const string Tenant = "TENANT";
    public const string User = "USER";
    public const string Role = "ROLE";
    public const string LookupCode = "LOOKUP_CODE";
    public const string CatalogDomain = "CATALOG_DOMAIN";
    public const string StatusCode = "STATUS_CODE";
    public const string ContactPoint = "CONTACT_POINT";
    public const string CustomFieldDefinition = "CUSTOM_FIELD_DEFINITION";
    public const string ReportDefinition = "REPORT_DEFINITION";
    public const string IndicatorDefinition = "INDICATOR_DEFINITION";
    public const string ChartDefinition = "CHART_DEFINITION";
    public const string TenantModule = "TENANT_MODULE";
    /// <summary>Logo de la marca de la compañía (Ajustes → Marca, Lote 19).</summary>
    public const string TenantLogo = "TENANT_LOGO";
    public const string StatusConfig = "STATUS_CONFIG";
    public const string ApiCredential = "API_CREDENTIAL";
    public const string AuditLog = "AUDIT_LOG";
    public const string SecurityEvent = "SECURITY_EVENT";
    // Agregados por el Lote 2 (clientes y contratos)
    public const string PortalUser = "PORTAL_USER";
    public const string RateComponent = "RATE_COMPONENT";
    public const string SpecialService = "SPECIAL_SERVICE";
    // Agregados por el Lote 3 (órdenes de transporte)
    /// <summary>Historial del ciclo COD de la orden, separado del de OrderStatus (DECISIÓN 7: StatusService resuelve el regreso por (EntityType, EntityId)).</summary>
    public const string OrderCod = "ORDER_COD";
    /// <summary>Historial de StopStatus de cada parada de la orden.</summary>
    public const string OrderStop = "ORDER_STOP";
    public const string ImportTemplate = "IMPORT_TEMPLATE";
    public const string ImportBatch = "IMPORT_BATCH";
    // Agregados por el Lote 4 (flota, choferes y mantenimiento)
    /// <summary>Orden de trabajo de mantenimiento: reutiliza el EntityType 'WORK_ORDER' ya sembrado (no existe MAINTENANCE_WORK_ORDER).</summary>
    public const string WorkOrder = "WORK_ORDER";
    public const string MaintenanceSchedule = "MAINTENANCE_SCHEDULE";
    public const string FuelLog = "FUEL_LOG";
    /// <summary>Fila virtual que une documentos de vehículo, licencias y certificaciones (fuente de datos; sin tabla propia).</summary>
    public const string FleetDocument = "FLEET_DOCUMENT";
    /// <summary>Tarifas del chofer (entrega, intento, viaje) y política de pago del tenant.</summary>
    public const string DriverRate = "DRIVER_RATE";
    public const string DriverTrip = "DRIVER_TRIP";
    public const string DispatchZone = "DISPATCH_ZONE";
    // Agregados por el Lote 5 (Trips y rutas); TRIP ya existía.
    /// <summary>Versión del plan de una ruta (Route); su ciclo es RouteStatus.</summary>
    public const string Route = "ROUTE";
    public const string RouteStop = "ROUTE_STOP";
    public const string OptimizationRun = "OPTIMIZATION_RUN";
    // Agregados por el Lote 6 (Inventario y almacén); WAREHOUSE y PRODUCT ya existían.
    /// <summary>Recibo (ReceiptHeader + ReceiptLine); su ciclo es ReceiptStatus.</summary>
    public const string Receipt = "RECEIPT";
    /// <summary>Aviso de llegada (Asn + AsnLine); su ciclo es AsnStatus.</summary>
    public const string Asn = "ASN";
    public const string WarehouseDock = "WAREHOUSE_DOCK";
    /// <summary>Serie de inventario: su rastro es EntityStatusHistory (SerialStatus) más el ledger; sin AuditLog.</summary>
    public const string InventorySerial = "INVENTORY_SERIAL";
    public const string WarehouseTask = "WAREHOUSE_TASK";
    public const string PickBatch = "PICK_BATCH";
    public const string DockAppointment = "DOCK_APPOINTMENT";
    public const string CrossDockAllocation = "CROSSDOCK_ALLOCATION";
    /// <summary>Movimiento del ledger (fuente de datos; solo inserción, sin AuditLog).</summary>
    public const string InventoryTransaction = "INVENTORY_TRANSACTION";
    /// <summary>Saldo por almacén/posición/lote (fuente de datos; proyección del ledger, sin AuditLog).</summary>
    public const string StockBalance = "STOCK_BALANCE";
    // Códigos ya sembrados antes del Lote 6 que este lote usa por nombre
    public const string CycleCount = "CYCLE_COUNT";
    public const string CrossDockPlan = "CROSSDOCK_PLAN";
    public const string PurchaseOrder = "PURCHASE_ORDER";
    public const string Supplier = "SUPPLIER";
    public const string ReceiptLine = "RECEIPT_LINE";
    // Agregado por el Lote 7A: la categoría deja de compartir PRODUCT en la bitácora (su baja no es la de un producto:
    // PRODUCT_DEACTIVATED del feed "Actividad reciente" lee AuditLog PRODUCT DELETE).
    public const string ProductCategory = "PRODUCT_CATEGORY";
    // Lote 8A: aparato de confianza de la app de almacén (UserDevice; DriverDevice del Lote 4 se audita como DRIVER).
    public const string UserDevice = "USER_DEVICE";
    // Lote F8a (P1): orden y visibilidad de los paneles del Pulso del día (nivel compañía y por usuario), auditado.
    public const string PulsePanelSetting = "PULSE_PANEL_SETTING";
    // Lote 14: descuadre Kárdex ↔ saldo (conciliación), con historial de estatus y bitácora.
    public const string InventoryDiscrepancy = "INVENTORY_DISCREPANCY";
}

// ---------------- Lote 3 — Órdenes de transporte ----------------

/// <summary>
/// Dominio OrderStatus (seed): DRAFT (inicial) → CONFIRMED → PICKUP → INBOUND → PLANNED → IN_TRANSIT → ARRIVED → DELIVERED (terminal);
/// ON_HOLD, PARTIAL y FAILED laterales; CANCELLED terminal. Este lote solo mueve DRAFT → siguiente etapa (confirmar), laterales y CANCELLED.
/// </summary>
public static class OrderStatuses
{
    public const string Draft = "DRAFT";
    public const string Confirmed = "CONFIRMED";
    public const string Pickup = "PICKUP";
    public const string Inbound = "INBOUND";
    public const string Planned = "PLANNED";
    public const string InTransit = "IN_TRANSIT";
    public const string Arrived = "ARRIVED";
    public const string Delivered = "DELIVERED";
    public const string OnHold = "ON_HOLD";
    public const string Partial = "PARTIAL";
    public const string Failed = "FAILED";
    public const string Cancelled = "CANCELLED";
}

/// <summary>Dominio StopStatus: PENDING (inicial) → EN_ROUTE → COMPLETED (terminal); FAILED lateral.</summary>
public static class StopStatuses
{
    public const string Pending = "PENDING";
    public const string EnRoute = "EN_ROUTE";
    public const string Completed = "COMPLETED";
    public const string Failed = "FAILED";
}

/// <summary>Dominio CodStatus de la orden: PENDING (inicial) → PARTIAL → COLLECTED → RECONCILED → REMITTED (terminal). Este lote solo escribe PENDING.</summary>
public static class CodStatuses
{
    public const string Pending = "PENDING";
    public const string Partial = "PARTIAL";
    public const string Collected = "COLLECTED";
    public const string Reconciled = "RECONCILED";
    public const string Remitted = "REMITTED";
}

/// <summary>LookupDomains.StopType: parada de recogido o de entrega.</summary>
public static class StopTypes
{
    public const string Pickup = "PICKUP";
    public const string Delivery = "DELIVERY";
}

/// <summary>
/// Tipos de contador de dbo.NumberSequence (columna Kind; CHECK CK_NumberSequence_Kind). ORDER, INVOICE y PACKAGE se
/// cuentan por cliente; PACKBATCH es uno por tenant (ClientId NULL) con patrón fijo EMP-#####.
/// </summary>
public static class NumberKinds
{
    public const string Order = "ORDER";
    public const string Invoice = "INVOICE";
    public const string Package = "PACKAGE";
    public const string PackBatch = "PACKBATCH";
    /// <summary>Lote 4: número de orden de trabajo OT-##### (uno por tenant, ClientId NULL).</summary>
    public const string WorkOrder = "WORKORDER";
    /// <summary>Lote 5: número de ruta AAAA-#### (uno por tenant, ClientId NULL; no se reinicia por año).</summary>
    public const string Trip = "TRIP";
    /// <summary>Lote 6: número de recibo REC-##### por tenant (ClientId NULL).</summary>
    public const string Receipt = "RECEIPT";
    /// <summary>Lote 6: número de conteo cíclico CC-##### por tenant (ClientId NULL).</summary>
    public const string CycleCount = "CYCLECOUNT";
    /// <summary>Lote 6: número de plan de cruce de muelle XD-##### por tenant (ClientId NULL).</summary>
    public const string CrossDock = "CROSSDOCK";
    /// <summary>Lote 6: número de orden de compra PO-##### por tenant (ClientId NULL).</summary>
    public const string Purchase = "PURCHASE";
}

public static class ModuleKeys
{
    public const string LtlGround = "LTL_GROUND";
    public const string Cod = "COD";
    public const string WmsLotSerial = "WMS_LOTSERIAL";
    public const string CrossDock = "CROSSDOCK";
    public const string RentalEquipment = "RENTAL_EQUIPMENT";
    public const string RentalBilling = "RENTAL_BILLING";
    public const string Maritime = "MARITIME";
    public const string ClientPortal = "CLIENT_PORTAL";
    public const string CustomFields = "CUSTOM_FIELDS";
    public const string Purchasing = "PURCHASING";
    public const string Catalog = "CATALOG";
    public const string Analytics = "ANALYTICS";
    public const string System = "SYSTEM";
}

// ---------------- Lote 4 — Flota, choferes y mantenimiento ----------------

/// <summary>Dominio VehicleStatus: ACTIVE (inicial) ↔ MAINTENANCE (lateral); INACTIVE terminal (baja definitiva).</summary>
public static class VehicleStatuses
{
    public const string Active = "ACTIVE";
    public const string Maintenance = "MAINTENANCE";
    public const string Inactive = "INACTIVE";
}

/// <summary>Dominio DriverStatus: ACTIVE (inicial) ↔ UNAVAILABLE (lateral); INACTIVE terminal ('Eliminar chofer').</summary>
public static class DriverStatuses
{
    public const string Active = "ACTIVE";
    public const string Unavailable = "UNAVAILABLE";
    public const string Inactive = "INACTIVE";
}

/// <summary>Dominio WorkOrderStatus: OPEN (inicial) → IN_PROGRESS → CLOSED; CANCELLED terminal.</summary>
public static class WorkOrderStatuses
{
    public const string Open = "OPEN";
    public const string InProgress = "IN_PROGRESS";
    public const string Closed = "CLOSED";
    public const string Cancelled = "CANCELLED";
}

/// <summary>Dominio DriverTripStatus: OPEN (inicial, por liquidar) → SETTLED (Lote 9) | CANCELLED.</summary>
public static class DriverTripStatuses
{
    public const string Open = "OPEN";
    public const string Settled = "SETTLED";
    public const string Cancelled = "CANCELLED";
}

/// <summary>LookupDomains.MaintenanceTrigger: por kilometraje, por tiempo o ambos (el peor).</summary>
public static class MaintenanceTriggers
{
    public const string Mileage = "MILEAGE";
    public const string Time = "TIME";
    public const string Both = "BOTH";
}

/// <summary>LookupDomains.MaintenanceType.</summary>
public static class MaintenanceTypes
{
    public const string Preventive = "PREVENTIVE";
    public const string Corrective = "CORRECTIVE";
}

/// <summary>LookupDomains.DriverPayoutFormula: los tres modelos de pago a choferes (DriverPayoutRules.Compute).</summary>
public static class DriverPayoutFormulas
{
    public const string DeliveryPlusAttempts = "DELIVERY_PLUS_ATTEMPTS";
    public const string DeliveryIncludesFirst = "DELIVERY_INCLUDES_FIRST";
    public const string FailedReplacesDelivery = "FAILED_REPLACES_DELIVERY";
}

/// <summary>
/// Clase de documento de flota (panel 'Documentos por vencer'): los tipos de VehicleDocType en un vehículo; LICENSE o
/// CERTIFICATION en un chofer.
/// </summary>
public static class FleetDocumentKinds
{
    public const string Registration = "REGISTRATION";
    public const string Insurance = "INSURANCE";
    public const string Inspection = "INSPECTION";
    public const string Permit = "PERMIT";
    public const string License = "LICENSE";
    public const string Certification = "CERTIFICATION";
}

/// <summary>Dueño de un documento de flota.</summary>
public static class FleetOwnerKinds
{
    public const string Vehicle = "VEHICLE";
    public const string Driver = "DRIVER";
}

/// <summary>Estado de vencimiento de un documento (FleetRules.ExpiryState).</summary>
public static class ExpiryStates
{
    public const string Expired = "EXPIRED";
    public const string Expiring = "EXPIRING";
    public const string Ok = "OK";
    public const string NoExpiry = "NO_EXPIRY";
}

/// <summary>Estado del mantenimiento preventivo: Al día / Por vencer / Vencido / Sin historial.</summary>
public static class MaintenanceDueStates
{
    public const string Ok = "OK";
    public const string DueSoon = "DUE_SOON";
    public const string Overdue = "OVERDUE";
    public const string NoBaseline = "NO_BASELINE";
}

// ---------------- Lote 5 — Trips y rutas ----------------

/// <summary>
/// Dominio TripStatus: DRAFT (inicial) → PLANNED → DISPATCHED → IN_PROGRESS → COMPLETED (terminal); CANCELLED terminal
/// ('Eliminar ruta', solo desde DRAFT/PLANNED).
/// </summary>
public static class TripStatuses
{
    public const string Draft = "DRAFT";
    public const string Planned = "PLANNED";
    public const string Dispatched = "DISPATCHED";
    public const string InProgress = "IN_PROGRESS";
    public const string Completed = "COMPLETED";
    public const string Cancelled = "CANCELLED";
}

/// <summary>Dominio RouteStatus (ciclo de la VERSIÓN del plan): DRAFT (inicial) → OPTIMIZED → ACTIVE (congelada); ARCHIVED terminal.</summary>
public static class RouteStatuses
{
    public const string Draft = "DRAFT";
    public const string Optimized = "OPTIMIZED";
    public const string Active = "ACTIVE";
    public const string Archived = "ARCHIVED";
}

/// <summary>Dominio RouteStopStatus: PENDING (inicial) → ON_THE_WAY → ARRIVED → COMPLETED (terminal); FAILED lateral.</summary>
public static class RouteStopStatuses
{
    public const string Pending = "PENDING";
    public const string OnTheWay = "ON_THE_WAY";
    public const string Arrived = "ARRIVED";
    public const string Completed = "COMPLETED";
    public const string Failed = "FAILED";
}

/// <summary>Dominio OptimizationRunStatus: PENDING (inicial) → OK | ERROR (terminales).</summary>
public static class OptimizationRunStatuses
{
    public const string Pending = "PENDING";
    public const string Ok = "OK";
    public const string Error = "ERROR";
}

/// <summary>LookupDomains.ZoneMatchType: criterio de un miembro de zona. POLYGON existe en el catálogo pero no se soporta aún.</summary>
public static class ZoneMatchTypes
{
    public const string PostalCode = "POSTAL_CODE";
    public const string PostalRange = "POSTAL_RANGE";
    public const string Municipality = "MUNICIPALITY";
    public const string Polygon = "POLYGON";
}

/// <summary>LookupDomains.OptimizerEngine. El motor de este lote es HEURISTIC (determinista, sin llamadas externas).</summary>
public static class OptimizerEngines
{
    public const string Vroom = "VROOM";
    public const string OrTools = "ORTOOLS";
    public const string Manual = "MANUAL";
    public const string Heuristic = "HEURISTIC";
}

/// <summary>LookupDomains.GeocodeAccuracy: precisión del pin de una parada. EXACT y MANUAL no son aproximados.</summary>
public static class GeocodeAccuracies
{
    public const string Exact = "EXACT";
    public const string ZipCentroid = "ZIP_CENTROID";
    public const string CityCentroid = "CITY_CENTROID";
    public const string Manual = "MANUAL";
}

// ---------------- Lote 6 — Inventario y almacén ----------------

/// <summary>Dominio WarehouseStatus: ACTIVE (inicial) → INACTIVE (terminal: baja definitiva, D26).</summary>
public static class WarehouseStatuses
{
    public const string Active = "ACTIVE";
    public const string Inactive = "INACTIVE";
}

/// <summary>Dominio DockStatus: FREE (inicial); OCCUPIED y MAINTENANCE laterales (la llegada de una cita ocupa el muelle).</summary>
public static class DockStatuses
{
    public const string Free = "FREE";
    public const string Occupied = "OCCUPIED";
    public const string Maintenance = "MAINTENANCE";
}

/// <summary>
/// Dominio SerialStatus (D16): AVAILABLE (inicial); RESERVED y SHIPPED laterales (AVAILABLE → SHIPPED → AVAILABLE es legal:
/// reversas y devoluciones); SCRAPPED terminal (una serie dada de baja no vuelve).
/// </summary>
public static class SerialStatuses
{
    public const string Available = "AVAILABLE";
    public const string Reserved = "RESERVED";
    public const string Shipped = "SHIPPED";
    public const string Scrapped = "SCRAPPED";
}

/// <summary>Dominio AsnStatus: EXPECTED (inicial) → RECEIVED; CANCELLED terminal.</summary>
public static class AsnStatuses
{
    public const string Expected = "EXPECTED";
    public const string Received = "RECEIVED";
    public const string Cancelled = "CANCELLED";
}

/// <summary>
/// Dominio ReceiptStatus (Lote 13): EXPECTED (inicial, sin líneas) → RECEIVING (capturando) ↔ DISCREPANCY (lateral: lo
/// recibido difiere de lo esperado) → RECEIVED (Completado) o RECEIVED_VARIANCE (lateral: Completado con diferencia) →
/// PUTAWAY (terminal: último acomodo cerrado). OPEN (Lote 6) se retiró: sus recibos pasaron a RECEIVING o EXPECTED.
/// Los tres abiertos los sincroniza el servicio del recibo con cada cambio de líneas (ReceiptStatusRules.OpenTarget).
/// </summary>
public static class ReceiptStatuses
{
    public const string Expected = "EXPECTED";
    public const string Receiving = "RECEIVING";
    public const string Discrepancy = "DISCREPANCY";
    public const string Received = "RECEIVED";
    public const string ReceivedWithVariance = "RECEIVED_VARIANCE";
    public const string Putaway = "PUTAWAY";

    /// <summary>Estatus abiertos (el recibo aún se edita y se puede borrar).</summary>
    public static readonly string[] OpenCodes = { Expected, Receiving, Discrepancy };

    /// <summary>Estatus confirmados (ya asentados en el Kárdex) con acomodo por cerrar.</summary>
    public static readonly string[] ConfirmedCodes = { Received, ReceivedWithVariance };
}

/// <summary>Dominio WarehouseTaskStatus: PENDING (inicial) → IN_PROGRESS → DONE; CANCELLED terminal (D20).</summary>
public static class WarehouseTaskStatuses
{
    public const string Pending = "PENDING";
    public const string InProgress = "IN_PROGRESS";
    public const string Done = "DONE";
    public const string Cancelled = "CANCELLED";
}

/// <summary>
/// Dominio CycleCountStatus. Lote 14 (D7): OPEN 'Pendiente' (inicial) → COUNTED 'Contado' (solo cuando se termina de contar a
/// ciegas, app de almacén) → RECONCILED 'Concordancia' (terminal: no se asentó ningún ajuste) o RECONCILED_VARIANCE
/// 'Diferencia' (terminal: alguna línea asentó un movimiento). "Confirmar conteo y ajustar" (D8) pasa en un paso desde OPEN
/// (o desde COUNTED) al terminal que corresponda. Los dos terminales son "reconciliado": usar IsReconciled en las guardas.
/// </summary>
public static class CycleCountStatuses
{
    public const string Open = "OPEN";
    public const string Counted = "COUNTED";
    public const string Reconciled = "RECONCILED";
    public const string ReconciledVariance = "RECONCILED_VARIANCE";

    /// <summary>Estatus abiertos (el conteo se captura, se confirma o se elimina; ocupan su posición para "lo cambiado").</summary>
    public static readonly string[] OpenCodes = { Open, Counted };

    /// <summary>Estatus finales (ya asentado en el Kárdex; solo se consulta).</summary>
    public static readonly string[] ClosedCodes = { Reconciled, ReconciledVariance };

    /// <summary>¿El conteo ya se reconcilió (Concordancia o Diferencia)?</summary>
    public static bool IsReconciled(string? code) => code is Reconciled or ReconciledVariance;
}

/// <summary>Dominio PickBatchStatus (D20): COLLECTED (inicial) → PACKED; CANCELLED terminal (eliminar con reversa).</summary>
public static class PickBatchStatuses
{
    public const string Collected = "COLLECTED";
    public const string Packed = "PACKED";
    public const string Cancelled = "CANCELLED";
}

/// <summary>
/// Dominio PurchaseOrderStatus: DRAFT (inicial) → SENT → PARTIAL → RECEIVED (terminal); CANCELLED terminal, permitido desde
/// DRAFT, SENT y PARTIAL (sin regla lateral sembrada, D47).
/// </summary>
public static class PurchaseOrderStatuses
{
    public const string Draft = "DRAFT";
    public const string Sent = "SENT";
    public const string Partial = "PARTIAL";
    public const string Received = "RECEIVED";
    public const string Cancelled = "CANCELLED";
}

/// <summary>Dominio AppointmentStatus: SCHEDULED (inicial) → ARRIVED → COMPLETED; NO_SHOW lateral; CANCELLED terminal.</summary>
public static class AppointmentStatuses
{
    public const string Scheduled = "SCHEDULED";
    public const string Arrived = "ARRIVED";
    public const string Completed = "COMPLETED";
    public const string NoShow = "NO_SHOW";
    public const string Cancelled = "CANCELLED";
}

/// <summary>Dominio CrossDockStatus: OPEN (inicial) → ALLOCATED → COMPLETED (terminal).</summary>
public static class CrossDockStatuses
{
    public const string Open = "OPEN";
    public const string Allocated = "ALLOCATED";
    public const string Completed = "COMPLETED";
}

/// <summary>Dominio AllocationStatus: PLANNED (inicial) → MOVED (terminal); CANCELLED terminal.</summary>
public static class AllocationStatuses
{
    public const string Planned = "PLANNED";
    public const string Moved = "MOVED";
    public const string Cancelled = "CANCELLED";
}

/// <summary>
/// Lote 14 (D5): dominio InventoryDiscrepancyStatus. OPEN (Pendiente, inicial) → RESOLVED (se corrigió el saldo según el
/// Kárdex) | DISMISSED (descartado con nota) | SELF_CORRECTED (volvió a cuadrar solo). Los tres cierres son terminales.
/// </summary>
public static class InventoryDiscrepancyStatuses
{
    public const string Open = "OPEN";
    public const string Resolved = "RESOLVED";
    public const string Dismissed = "DISMISSED";
    public const string SelfCorrected = "SELF_CORRECTED";
}

/// <summary>LookupDomains.ZoneType. STAGING (Lote 6, D21) es la zona de recepción.</summary>
public static class ZoneTypes
{
    public const string Picking = "PICKING";
    public const string Reserve = "RESERVE";
    public const string Refrigerated = "REFRIGERATED";
    public const string Quarantine = "QUARANTINE";
    public const string CrossDock = "CROSSDOCK";
    public const string Staging = "STAGING";
}

/// <summary>LookupDomains.DockType.</summary>
public static class DockTypes
{
    public const string Inbound = "INBOUND";
    public const string Outbound = "OUTBOUND";
    public const string Both = "BOTH";
}

/// <summary>LookupDomains.DockDirection (dirección de una cita de muelle).</summary>
public static class DockDirections
{
    public const string Inbound = "INBOUND";
    public const string Outbound = "OUTBOUND";
}

/// <summary>LookupDomains.TrackingType: seguimiento del producto.</summary>
public static class TrackingTypes
{
    public const string None = "NONE";
    public const string Lot = "LOT";
    public const string Serial = "SERIAL";
}

/// <summary>
/// LookupDomains.InventoryTxnType. Signo del ledger (D3, maestro L331): RECEIPT +; ISSUE y CROSSDOCK −; ADJUSTMENT ±;
/// TRANSFER + en una sola fila con From y To.
/// </summary>
public static class InventoryTxnTypes
{
    public const string Receipt = "RECEIPT";
    public const string Issue = "ISSUE";
    public const string Transfer = "TRANSFER";
    public const string Adjustment = "ADJUSTMENT";
    public const string CrossDock = "CROSSDOCK";
}

/// <summary>LookupDomains.ReceiptType.</summary>
public static class ReceiptTypes
{
    public const string Asn = "ASN";
    public const string Blind = "BLIND";
    public const string Return = "RETURN";
}

/// <summary>LookupDomains.WarehouseTaskType. PICK, PACK y LOAD no tienen handler en este lote (D41).</summary>
public static class WarehouseTaskTypes
{
    public const string Putaway = "PUTAWAY";
    public const string Pick = "PICK";
    public const string Pack = "PACK";
    public const string Replenish = "REPLENISH";
    public const string Count = "COUNT";
    public const string Load = "LOAD";
    public const string CrossDock = "CROSSDOCK";
}

/// <summary>
/// LookupDomains.AdjustmentReason (D7). RECEIPT_VARIANCE, COUNT_VARIANCE y PICK_BATCH_REVERSAL los asigna solo el sistema.
/// Lote 10: OPENING_BALANCE (saldo inicial de la migración) también es de sistema: solo lo escribe el importador import-legacy.
/// Lote 26 (Rentas R0): TRACKING_CONVERSION también es de sistema: solo lo escribe la conversión de un producto a serie.
/// </summary>
public static class AdjustmentReasons
{
    public const string ReceiptVariance = "RECEIPT_VARIANCE";
    public const string CountVariance = "COUNT_VARIANCE";
    public const string Damage = "DAMAGE";
    public const string Loss = "LOSS";
    public const string Found = "FOUND";
    public const string Expired = "EXPIRED";
    public const string PoShortage = "PO_SHORTAGE";
    public const string PickBatchReversal = "PICK_BATCH_REVERSAL";
    public const string Other = "OTHER";
    /// <summary>Lote 10: saldo inicial cargado por el importador de datos heredados (import-legacy).</summary>
    public const string OpeningBalance = "OPENING_BALANCE";
    /// <summary>Lote 26 (Rentas R0): salida del saldo sin serie y entrada de cada serie al convertir un producto a serie.</summary>
    public const string TrackingConversion = "TRACKING_CONVERSION";

    /// <summary>Motivos que solo asigna el sistema (un ajuste manual no puede usarlos).</summary>
    public static readonly IReadOnlyList<string> SystemAssigned = new[] { ReceiptVariance, CountVariance, PickBatchReversal, OpeningBalance, TrackingConversion };
}

/// <summary>LookupDomains.ShortageAction (D8).</summary>
public static class ShortageActions
{
    public const string Close = "CLOSE";
    public const string Reorder = "REORDER";
    public const string ManualAdjustment = "MANUAL_ADJUSTMENT";
}

/// <summary>Rotación aproximada del putaway dirigido (D24): salidas de 30 días contra la existencia.</summary>
public static class RotationClasses
{
    public const string Fast = "FAST";
    public const string Slow = "SLOW";
}
