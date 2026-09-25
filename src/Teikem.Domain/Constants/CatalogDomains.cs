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
