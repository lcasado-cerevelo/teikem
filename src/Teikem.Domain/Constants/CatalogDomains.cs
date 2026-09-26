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
