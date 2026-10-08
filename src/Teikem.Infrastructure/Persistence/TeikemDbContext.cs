using System.Linq.Expressions;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Analytics;
using Teikem.Domain.Audit;
using Teikem.Domain.Catalogs;
using Teikem.Domain.Clients;
using Teikem.Domain.Common;
using Teikem.Domain.Contacts;
using Teikem.Domain.CustomFields;
using Teikem.Domain.Entities;
using Teikem.Domain.Fleet;
using Teikem.Domain.Identity;
using Teikem.Domain.Orders;
using Teikem.Domain.Security;
using Teikem.Domain.Tenancy;
using Teikem.Domain.Trips;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;

namespace Teikem.Infrastructure.Persistence;

/// <summary>
/// DbContext único de la plataforma. Identity (int) + todas las tablas transversales.
/// Aplica el filtro global de tenant a toda entidad ITenantScoped / IOptionallyTenantScoped:
/// olvidarse de filtrar deja de ser posible.
/// </summary>
public class TeikemDbContext : IdentityDbContext<ApplicationUser, ApplicationRole, int>
{
    private readonly ITenantContext _tenant;

    public TeikemDbContext(DbContextOptions<TeikemDbContext> options, ITenantContext tenant) : base(options)
    {
        _tenant = tenant;
    }

    // Usados por los filtros globales (EF los parametriza por consulta al referenciar miembros del contexto).
    public int CurrentTenantId => _tenant.TenantId ?? -1;
    public bool TenantFilterDisabled => _tenant.TenantFilterDisabled;

    /// <summary>Bandera interna del interceptor de auditoría para no auditar la escritura del propio AuditLog.</summary>
    internal bool SuppressAudit { get; set; }
    internal List<Interceptors.PendingAudit> PendingAudits { get; } = new();

    // Tenancy / módulos
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<TenantHoliday> TenantHolidays => Set<TenantHoliday>();
    public DbSet<TenantBrandLogo> TenantBrandLogos => Set<TenantBrandLogo>();
    public DbSet<ModuleDefinition> ModuleDefinitions => Set<ModuleDefinition>();
    public DbSet<TenantModule> TenantModules => Set<TenantModule>();
    // Catálogos / estatus
    public DbSet<CatalogDomain> CatalogDomains => Set<CatalogDomain>();
    public DbSet<LookupCode> LookupCodes => Set<LookupCode>();
    public DbSet<LookupCodeOverride> LookupCodeOverrides => Set<LookupCodeOverride>();
    public DbSet<StatusCode> StatusCodes => Set<StatusCode>();
    public DbSet<StatusCodeOverride> StatusCodeOverrides => Set<StatusCodeOverride>();
    public DbSet<StatusLateralEntry> StatusLateralEntries => Set<StatusLateralEntry>();
    public DbSet<StatusCapability> StatusCapabilities => Set<StatusCapability>();
    public DbSet<EntityStatusHistory> EntityStatusHistories => Set<EntityStatusHistory>();
    // Seguridad
    public DbSet<UserTenant> UserTenants => Set<UserTenant>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<Role> AppRoles => Set<Role>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<UserRole> AppUserRoles => Set<UserRole>();
    public DbSet<UserPermission> UserPermissions => Set<UserPermission>();
    public DbSet<UserDataScope> UserDataScopes => Set<UserDataScope>();
    public DbSet<UserMfaFactor> UserMfaFactors => Set<UserMfaFactor>();
    public DbSet<MfaRecoveryCode> MfaRecoveryCodes => Set<MfaRecoveryCode>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<SecurityEvent> SecurityEvents => Set<SecurityEvent>();
    // Lote 8A — aparatos de confianza de la app de almacén, PIN por usuario e idempotencia del API
    public DbSet<UserDevice> UserDevices => Set<UserDevice>();
    public DbSet<UserDeviceActivity> UserDeviceActivities => Set<UserDeviceActivity>();
    public DbSet<UserPin> UserPins => Set<UserPin>();
    public DbSet<IntegrationMessageLog> IntegrationMessageLogs => Set<IntegrationMessageLog>();
    // Auditoría / contactos / campos / análisis
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<ContactPoint> ContactPoints => Set<ContactPoint>();
    public DbSet<CustomFieldDefinition> CustomFieldDefinitions => Set<CustomFieldDefinition>();
    public DbSet<CustomFieldOption> CustomFieldOptions => Set<CustomFieldOption>();
    public DbSet<CustomFieldValue> CustomFieldValues => Set<CustomFieldValue>();
    public DbSet<ReportDefinition> ReportDefinitions => Set<ReportDefinition>();
    public DbSet<ReportShare> ReportShares => Set<ReportShare>();
    public DbSet<IndicatorDefinition> IndicatorDefinitions => Set<IndicatorDefinition>();
    public DbSet<IndicatorShare> IndicatorShares => Set<IndicatorShare>();
    public DbSet<ChartDefinition> ChartDefinitions => Set<ChartDefinition>();
    public DbSet<ChartShare> ChartShares => Set<ChartShare>();
    public DbSet<UserAnalyticsPreference> UserAnalyticsPreferences => Set<UserAnalyticsPreference>();
    // Lote 2 — Clientes y contratos
    public DbSet<Client> Clients => Set<Client>();
    public DbSet<ClientContact> ClientContacts => Set<ClientContact>();
    public DbSet<Location> Locations => Set<Location>();
    public DbSet<Contract> Contracts => Set<Contract>();
    public DbSet<ContractServiceLevel> ContractServiceLevels => Set<ContractServiceLevel>();
    public DbSet<RateComponent> RateComponents => Set<RateComponent>();
    public DbSet<RateTier> RateTiers => Set<RateTier>();
    public DbSet<SpecialServiceType> SpecialServiceTypes => Set<SpecialServiceType>();
    public DbSet<SpecialService> SpecialServices => Set<SpecialService>();
    public DbSet<PortalUser> PortalUsers => Set<PortalUser>();
    // Lote 3 — Órdenes de transporte (las hijas sin TenantId se alcanzan solo a través de la orden filtrada)
    public DbSet<TransportOrder> TransportOrders => Set<TransportOrder>();
    public DbSet<OrderStop> OrderStops => Set<OrderStop>();
    public DbSet<CargoLine> CargoLines => Set<CargoLine>();
    public DbSet<OrderReference> OrderReferences => Set<OrderReference>();
    public DbSet<NumberSequence> NumberSequences => Set<NumberSequence>();
    public DbSet<ImportTemplate> ImportTemplates => Set<ImportTemplate>();
    public DbSet<ImportBatch> ImportBatches => Set<ImportBatch>();
    // Lote 4 — Flota, choferes y mantenimiento (las hijas sin TenantId — documentos, licencias, certificaciones, zonas del
    // chofer y tareas — se alcanzan solo a través de su dueño filtrado)
    public DbSet<Vehicle> Vehicles => Set<Vehicle>();
    public DbSet<VehicleDocument> VehicleDocuments => Set<VehicleDocument>();
    public DbSet<Driver> Drivers => Set<Driver>();
    public DbSet<DriverLicense> DriverLicenses => Set<DriverLicense>();
    public DbSet<DriverCertification> DriverCertifications => Set<DriverCertification>();
    public DbSet<DriverDevice> DriverDevices => Set<DriverDevice>();
    public DbSet<DispatchZone> DispatchZones => Set<DispatchZone>();
    public DbSet<DriverZone> DriverZones => Set<DriverZone>();
    public DbSet<MaintenanceSchedule> MaintenanceSchedules => Set<MaintenanceSchedule>();
    public DbSet<MaintenanceWorkOrder> MaintenanceWorkOrders => Set<MaintenanceWorkOrder>();
    public DbSet<MaintenanceTask> MaintenanceTasks => Set<MaintenanceTask>();
    public DbSet<FuelLog> FuelLogs => Set<FuelLog>();
    public DbSet<DriverPayPolicy> DriverPayPolicies => Set<DriverPayPolicy>();
    public DbSet<DriverDeliveryRate> DriverDeliveryRates => Set<DriverDeliveryRate>();
    public DbSet<DriverAttemptRate> DriverAttemptRates => Set<DriverAttemptRate>();
    public DbSet<DriverTripRate> DriverTripRates => Set<DriverTripRate>();
    public DbSet<DriverTrip> DriverTrips => Set<DriverTrip>();
    // Lote 5 — Trips y rutas. Trip, TripOrder y OptimizationRun llevan TenantId (filtro global); Route, RouteStop y
    // DispatchZoneMember no: se alcanzan SOLO a través de un Trip o una DispatchZone ya filtrados.
    public DbSet<DispatchZoneMember> DispatchZoneMembers => Set<DispatchZoneMember>();
    public DbSet<Trip> Trips => Set<Trip>();
    public DbSet<TripOrder> TripOrders => Set<TripOrder>();
    public DbSet<Route> Routes => Set<Route>();
    public DbSet<RouteStop> RouteStops => Set<RouteStop>();
    public DbSet<OptimizationRun> OptimizationRuns => Set<OptimizationRun>();
    // Lote 6 — Inventario y almacén. 15 entidades llevan TenantId y reciben el filtro global: Warehouse, ProductCategory,
    // Product, StockBalance, InventoryTransaction, Supplier, PurchaseOrder, PurchaseOrderShortageResolution, Asn, ReceiptHeader,
    // WarehouseTask, CycleCount, PickBatch, DockAppointment y CrossDockPlan. Las 11 hijas SIN TenantId se alcanzan SOLO a
    // través de su padre filtrado (WmsResolve): WarehouseZone, WarehouseBin y WarehouseDock (almacén); InventoryLot e
    // InventorySerial (producto; heredan la tenencia por FK compuesta); PurchaseOrderLine (PO); AsnLine (ASN); ReceiptLine
    // (recibo); CycleCountLine (conteo); PickBatchLine (recolección); CrossDockAllocation (plan).
    public DbSet<Warehouse> Warehouses => Set<Warehouse>();
    public DbSet<WarehouseZone> WarehouseZones => Set<WarehouseZone>();
    public DbSet<WarehouseBin> WarehouseBins => Set<WarehouseBin>();
    public DbSet<WarehouseDock> WarehouseDocks => Set<WarehouseDock>();
    public DbSet<ProductCategory> ProductCategories => Set<ProductCategory>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<InventoryLot> InventoryLots => Set<InventoryLot>();
    public DbSet<InventorySerial> InventorySerials => Set<InventorySerial>();
    public DbSet<StockBalance> StockBalances => Set<StockBalance>();
    public DbSet<InventoryTransaction> InventoryTransactions => Set<InventoryTransaction>();
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<PurchaseOrder> PurchaseOrders => Set<PurchaseOrder>();
    public DbSet<PurchaseOrderLine> PurchaseOrderLines => Set<PurchaseOrderLine>();
    public DbSet<PurchaseOrderShortageResolution> PurchaseOrderShortageResolutions => Set<PurchaseOrderShortageResolution>();
    public DbSet<Asn> Asns => Set<Asn>();
    public DbSet<AsnLine> AsnLines => Set<AsnLine>();
    public DbSet<ReceiptHeader> ReceiptHeaders => Set<ReceiptHeader>();
    public DbSet<ReceiptLine> ReceiptLines => Set<ReceiptLine>();
    public DbSet<WarehouseTask> WarehouseTasks => Set<WarehouseTask>();
    public DbSet<CycleCount> CycleCounts => Set<CycleCount>();
    public DbSet<DamageReport> DamageReports => Set<DamageReport>();   // 2026-10-08: daños
    public DbSet<CycleCountLine> CycleCountLines => Set<CycleCountLine>();
    public DbSet<PickBatch> PickBatches => Set<PickBatch>();
    public DbSet<PickBatchLine> PickBatchLines => Set<PickBatchLine>();
    public DbSet<InventoryDiscrepancy> InventoryDiscrepancies => Set<InventoryDiscrepancy>();   // Lote 14: descuadres Kárdex ↔ saldo
    public DbSet<DockAppointment> DockAppointments => Set<DockAppointment>();
    public DbSet<CrossDockPlan> CrossDockPlans => Set<CrossDockPlan>();
    public DbSet<CrossDockAllocation> CrossDockAllocations => Set<CrossDockAllocation>();
    // Lote 27 — Rentas (capa 16C reescrita). RentalCharge (cobro por período) queda sin mapear.
    public DbSet<Rental> Rentals => Set<Rental>();
    public DbSet<RentalLine> RentalLines => Set<RentalLine>();
    public DbSet<RentalLineRate> RentalLineRates => Set<RentalLineRate>();
    public DbSet<RentalExtension> RentalExtensions => Set<RentalExtension>();
    public DbSet<RentalReturn> RentalReturns => Set<RentalReturn>();
    public DbSet<RentalReturnLine> RentalReturnLines => Set<RentalReturnLine>();
    public DbSet<RentalProcess> RentalProcesses => Set<RentalProcess>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(TeikemDbContext).Assembly);

        foreach (var entityType in builder.Model.GetEntityTypes())
        {
            var clr = entityType.ClrType;
            if (clr.Namespace is null || !clr.Namespace.StartsWith("Teikem.Domain")) continue;

            // Sin cascadas EF: el esquema SQL no las declara; la baja es soft-delete o cancelación con bitácora.
            foreach (var fk in entityType.GetForeignKeys())
                fk.DeleteBehavior = DeleteBehavior.Restrict;

            // Filtro global de tenant
            if (typeof(ITenantScoped).IsAssignableFrom(clr))
                entityType.SetQueryFilter(BuildTenantFilter(clr, optional: false));
            else if (typeof(IOptionallyTenantScoped).IsAssignableFrom(clr))
                entityType.SetQueryFilter(BuildTenantFilter(clr, optional: true));
        }
    }

    /// <summary>e => TenantFilterDisabled || e.TenantId == CurrentTenantId  (|| e.TenantId == null si es opcional).</summary>
    private LambdaExpression BuildTenantFilter(Type clr, bool optional)
    {
        var e = Expression.Parameter(clr, "e");
        var ctx = Expression.Constant(this);
        var disabled = Expression.Property(ctx, nameof(TenantFilterDisabled));
        var current = Expression.Property(ctx, nameof(CurrentTenantId));
        var tenantProp = Expression.Property(e, "TenantId");
        Expression body;
        if (!optional)
        {
            body = Expression.OrElse(disabled, Expression.Equal(tenantProp, current));
        }
        else
        {
            var isNull = Expression.Equal(tenantProp, Expression.Constant(null, typeof(int?)));
            var eq = Expression.Equal(tenantProp, Expression.Convert(current, typeof(int?)));
            body = Expression.OrElse(disabled, Expression.OrElse(isNull, eq));
        }
        return Expression.Lambda(body, e);
    }
}
