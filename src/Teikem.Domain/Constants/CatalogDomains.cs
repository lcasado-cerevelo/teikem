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
}

/// <summary>Dominios de estatus (StatusCode.Entity) que usa la capa transversal.</summary>
public static class StatusDomains
{
    public const string MembershipStatus = "MembershipStatus";
    public const string OrderStatus = "OrderStatus";
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
