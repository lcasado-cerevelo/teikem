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
using Teikem.Domain.Printing;
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

    /// <summary>
    /// Lote 42: lo que lee el filtro global de tenant. <c>null</c> SOLO con el bypass encendido (plataforma, seeders); si no, la
    /// compañía activa, o -1 si no hay ninguna (falla cerrado: no se ve ninguna fila con TenantId). Ver <see cref="TenantFilterEvaluatablePlugin"/>.
    /// </summary>
    public int? QueryFilterTenantId => _tenant.TenantFilterDisabled ? null : (_tenant.TenantId ?? -1);

    /// <summary>
    /// Lote 42: registra el plugin que deja el filtro de tenant buscable por índice (<c>[t].[TenantId] = @p</c> sin el OR del bypass).
    /// Va aquí y no en DependencyInjection para que TODO contexto lo tenga (API, seeders, migración, pruebas), con cualquier proveedor.
    /// </summary>
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);
        if (optionsBuilder.Options.FindExtension<TenantFilterOptionsExtension>() is null)
            ((Microsoft.EntityFrameworkCore.Infrastructure.IDbContextOptionsBuilderInfrastructure)optionsBuilder).AddOrUpdateExtension(new TenantFilterOptionsExtension());
    }

    // Alcance de datos del usuario (UserDataScope, 2026-10-11): además del tenant, un usuario con alcance de almacén y/o cliente
    // solo ve lo de esos almacenes/clientes. Sin alcance de ese tipo = sin restricción. Lo lee el filtro global (ver AddScopeFilter).
    // Lote 42: null = sin alcance de ese tipo (o filtro apagado); si no, la lista (vacía = no ve ninguno). Misma técnica que el tenant:
    // sin alcance, el SQL no lleva el término (antes «@p = 1 OR [x].[ClientId] IN (OPENJSON…)» en TODA consulta, que confundía al optimizador).
    public int[]? QueryFilterWarehouseIds => _tenant.TenantFilterDisabled ? null : _tenant.ScopedWarehouseIds?.ToArray();
    public int[]? QueryFilterClientIds => _tenant.TenantFilterDisabled ? null : _tenant.ScopedClientIds?.ToArray();

    /// <summary>Bandera interna del interceptor de auditoría para no auditar la escritura del propio AuditLog.</summary>
    internal bool SuppressAudit { get; set; }
    internal List<Interceptors.PendingAudit> PendingAudits { get; } = new();

    // Tenancy / módulos
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<TenantHoliday> TenantHolidays => Set<TenantHoliday>();
    public DbSet<TenantTrustedNetwork> TenantTrustedNetworks => Set<TenantTrustedNetwork>();   // 2026-10-12: IP preferida
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
    public DbSet<LegalAcceptance> LegalAcceptances => Set<LegalAcceptance>();   // Lote 99
    public DbSet<TrustedBrowser> TrustedBrowsers => Set<TrustedBrowser>();   // Lote 100
    // Lote 8A — aparatos de confianza de la app de almacén, PIN por usuario e idempotencia del API
    public DbSet<UserDevice> UserDevices => Set<UserDevice>();
    public DbSet<UserDeviceActivity> UserDeviceActivities => Set<UserDeviceActivity>();
    public DbSet<UserPin> UserPins => Set<UserPin>();
    public DbSet<IntegrationMessageLog> IntegrationMessageLogs => Set<IntegrationMessageLog>();
    // Auditoría / contactos / campos / análisis
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<ContactPoint> ContactPoints => Set<ContactPoint>();
    public DbSet<Teikem.Domain.Fleet.ZoneGroup> ZoneGroups => Set<Teikem.Domain.Fleet.ZoneGroup>();   // Lote 129
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
    public DbSet<ContractReturnFee> ContractReturnFees => Set<ContractReturnFee>();   // Lote 115
    public DbSet<ConsigneeNoticeConsent> ConsigneeNoticeConsents => Set<ConsigneeNoticeConsent>();   // Lote 116
    public DbSet<ConsigneeNotice> ConsigneeNotices => Set<ConsigneeNotice>();
    public DbSet<ConsigneeUnsubscribe> ConsigneeUnsubscribes => Set<ConsigneeUnsubscribe>();
    public DbSet<RateComponent> RateComponents => Set<RateComponent>();
    public DbSet<RateTier> RateTiers => Set<RateTier>();
    public DbSet<SpecialServiceType> SpecialServiceTypes => Set<SpecialServiceType>();
    public DbSet<SpecialService> SpecialServices => Set<SpecialService>();
    public DbSet<PortalUser> PortalUsers => Set<PortalUser>();
    // Lote 3 — Órdenes de transporte (las hijas sin TenantId se alcanzan solo a través de la orden filtrada)
    public DbSet<TransportOrder> TransportOrders => Set<TransportOrder>();
    public DbSet<OrderStop> OrderStops => Set<OrderStop>();
    public DbSet<CargoLine> CargoLines => Set<CargoLine>();
    public DbSet<OrderPiece> OrderPieces => Set<OrderPiece>();
    public DbSet<Claim> Claims => Set<Claim>();   // Lote 111
    public DbSet<OrderPieceScan> OrderPieceScans => Set<OrderPieceScan>();   // Lote 110   // Lote 109: cada pieza con su número único
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
    public DbSet<ProductKitComponent> ProductKitComponents => Set<ProductKitComponent>();   // Lote 68
    public DbSet<Teikem.Domain.Billing.BillingRun> BillingRuns => Set<Teikem.Domain.Billing.BillingRun>();   // Lote 72 (P5)
    public DbSet<Teikem.Domain.Billing.Invoice> Invoices => Set<Teikem.Domain.Billing.Invoice>();
    public DbSet<Teikem.Domain.Billing.InvoiceLine> InvoiceLines => Set<Teikem.Domain.Billing.InvoiceLine>();
    public DbSet<Teikem.Domain.Billing.Payment> Payments => Set<Teikem.Domain.Billing.Payment>();
    public DbSet<Teikem.Domain.Export.ExportTemplate> ExportTemplates => Set<Teikem.Domain.Export.ExportTemplate>();   // Lote 77 (P5)
    public DbSet<Teikem.Domain.Billing.CreditNote> CreditNotes => Set<Teikem.Domain.Billing.CreditNote>();   // Lote 79 (P6)
    public DbSet<Teikem.Domain.Billing.CreditNoteLine> CreditNoteLines => Set<Teikem.Domain.Billing.CreditNoteLine>();
    public DbSet<Teikem.Domain.Billing.CreditApplication> CreditApplications => Set<Teikem.Domain.Billing.CreditApplication>();
    public DbSet<Teikem.Domain.Sales.SalesReturn> SalesReturns => Set<Teikem.Domain.Sales.SalesReturn>();   // Lote 81 (P6)
    public DbSet<Teikem.Domain.Sales.SalesReturnLine> SalesReturnLines => Set<Teikem.Domain.Sales.SalesReturnLine>();
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
    public DbSet<ReceiptBatch> ReceiptBatches => Set<ReceiptBatch>();   // Lote 174
    public DbSet<WarehouseTask> WarehouseTasks => Set<WarehouseTask>();
    public DbSet<CycleCount> CycleCounts => Set<CycleCount>();
    public DbSet<DamageReport> DamageReports => Set<DamageReport>();   // 2026-10-08: daños
    public DbSet<CycleCountLine> CycleCountLines => Set<CycleCountLine>();
    public DbSet<PickBatch> PickBatches => Set<PickBatch>();
    public DbSet<PickBatchLine> PickBatchLines => Set<PickBatchLine>();
    public DbSet<PickGroup> PickGroups => Set<PickGroup>();   // Lote 176
    public DbSet<MultiAdjustment> MultiAdjustments => Set<MultiAdjustment>();   // 2026-10-12: ajuste múltiple AJM-######
    public DbSet<MultiAdjustmentLine> MultiAdjustmentLines => Set<MultiAdjustmentLine>();
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
    // Lote 43 (F-G1) — impresoras y plantillas de label
    public DbSet<Printer> Printers => Set<Printer>();
    public DbSet<LabelTemplate> LabelTemplates => Set<LabelTemplate>();
    // Lote 50 (P2.2) — trabajos programados por compañía
    public DbSet<Teikem.Domain.Jobs.ScheduledJob> ScheduledJobs => Set<Teikem.Domain.Jobs.ScheduledJob>();
    public DbSet<Teikem.Domain.Jobs.ScheduledJobRun> ScheduledJobRuns => Set<Teikem.Domain.Jobs.ScheduledJobRun>();
    // Lote 132 (P11): integraciones
    public DbSet<Teikem.Domain.Integrations.Integration> Integrations => Set<Teikem.Domain.Integrations.Integration>();
    public DbSet<Teikem.Domain.Integrations.IntegrationRun> IntegrationRuns => Set<Teikem.Domain.Integrations.IntegrationRun>();
    public DbSet<Teikem.Domain.Integrations.IntegrationFileLog> IntegrationFileLogs => Set<Teikem.Domain.Integrations.IntegrationFileLog>();
    public DbSet<Teikem.Domain.Integrations.IntegrationInboxItem> IntegrationInboxItems => Set<Teikem.Domain.Integrations.IntegrationInboxItem>();
    public DbSet<Teikem.Domain.Integrations.IntegrationApiKey> IntegrationApiKeys => Set<Teikem.Domain.Integrations.IntegrationApiKey>();   // Lote 135
    public DbSet<Teikem.Domain.Integrations.IntegrationApiLog> IntegrationApiLogs => Set<Teikem.Domain.Integrations.IntegrationApiLog>();
    public DbSet<Teikem.Domain.Integrations.IntegrationOutput> IntegrationOutputs => Set<Teikem.Domain.Integrations.IntegrationOutput>();
    public DbSet<Teikem.Domain.Integrations.IntegrationOutputOrder> IntegrationOutputOrders => Set<Teikem.Domain.Integrations.IntegrationOutputOrder>();
    public DbSet<Teikem.Domain.Integrations.IntegrationOutputEntity> IntegrationOutputEntities => Set<Teikem.Domain.Integrations.IntegrationOutputEntity>();
    public DbSet<Teikem.Domain.Integrations.IntegrationWebhook> IntegrationWebhooks => Set<Teikem.Domain.Integrations.IntegrationWebhook>();
    public DbSet<Teikem.Domain.Integrations.IntegrationWebhookDelivery> IntegrationWebhookDeliveries => Set<Teikem.Domain.Integrations.IntegrationWebhookDelivery>();
    // Lote 51 — avisos (reglas, cola, registro y bandeja)
    public DbSet<Teikem.Domain.Notifications.NotificationRule> NotificationRules => Set<Teikem.Domain.Notifications.NotificationRule>();
    public DbSet<Teikem.Domain.Notifications.NotificationRuleChannel> NotificationRuleChannels => Set<Teikem.Domain.Notifications.NotificationRuleChannel>();
    public DbSet<Teikem.Domain.Notifications.NotificationRuleRecipient> NotificationRuleRecipients => Set<Teikem.Domain.Notifications.NotificationRuleRecipient>();
    public DbSet<Teikem.Domain.Notifications.NotificationOutbox> NotificationOutbox => Set<Teikem.Domain.Notifications.NotificationOutbox>();
    public DbSet<Teikem.Domain.Notifications.NotificationLog> NotificationLogs => Set<Teikem.Domain.Notifications.NotificationLog>();
    public DbSet<Teikem.Domain.Notifications.UserNotification> UserNotifications => Set<Teikem.Domain.Notifications.UserNotification>();
    public DbSet<Teikem.Domain.Notifications.UserNotificationPref> UserNotificationPrefs => Set<Teikem.Domain.Notifications.UserNotificationPref>();
    public DbSet<Teikem.Domain.Notifications.Announcement> Announcements => Set<Teikem.Domain.Notifications.Announcement>();   // Lote 54
    // Lote 55 (P3) — impuestos (IVU)
    public DbSet<Teikem.Domain.Tax.TaxRate> TaxRates => Set<Teikem.Domain.Tax.TaxRate>();
    public DbSet<Teikem.Domain.Tax.TaxRateComponent> TaxRateComponents => Set<Teikem.Domain.Tax.TaxRateComponent>();
    public DbSet<Teikem.Domain.Tax.TenantTaxSetting> TenantTaxSettings => Set<Teikem.Domain.Tax.TenantTaxSetting>();
    public DbSet<Teikem.Domain.Tax.TaxChargeRule> TaxChargeRules => Set<Teikem.Domain.Tax.TaxChargeRule>();
    public DbSet<Teikem.Domain.Tax.ClientTaxExemption> ClientTaxExemptions => Set<Teikem.Domain.Tax.ClientTaxExemption>();
    // Lote 57 (P4, S1) — órdenes de venta
    public DbSet<Teikem.Domain.Sales.SalesOrder> SalesOrders => Set<Teikem.Domain.Sales.SalesOrder>();
    public DbSet<Teikem.Domain.Sales.SalesOrderLine> SalesOrderLines => Set<Teikem.Domain.Sales.SalesOrderLine>();
    public DbSet<Teikem.Domain.Sales.SalesOrderReservation> SalesOrderReservations => Set<Teikem.Domain.Sales.SalesOrderReservation>();
    public DbSet<Teikem.Domain.ExitNotices.ExitNotice> ExitNotices => Set<Teikem.Domain.ExitNotices.ExitNotice>();   // Lote 143
    public DbSet<Teikem.Domain.ExitNotices.ExitNoticeLine> ExitNoticeLines => Set<Teikem.Domain.ExitNotices.ExitNoticeLine>();
    public DbSet<Teikem.Domain.ExitNotices.ExitNoticeReservation> ExitNoticeReservations => Set<Teikem.Domain.ExitNotices.ExitNoticeReservation>();
    // Lote 53 — mensajería interna (hilos, participantes, mensajes y presencia)
    public DbSet<Teikem.Domain.Messaging.MessageThread> MessageThreads => Set<Teikem.Domain.Messaging.MessageThread>();
    public DbSet<Teikem.Domain.Messaging.MessageThreadMember> MessageThreadMembers => Set<Teikem.Domain.Messaging.MessageThreadMember>();
    // Lote 104 — mensajes del portal con el equipo de servicio al cliente
    public DbSet<Teikem.Domain.Messaging.ClientConversation> ClientConversations => Set<Teikem.Domain.Messaging.ClientConversation>();
    public DbSet<Teikem.Domain.Messaging.ClientMessage> ClientMessages => Set<Teikem.Domain.Messaging.ClientMessage>();
    public DbSet<Teikem.Domain.Messaging.ConsigneeConversation> ConsigneeConversations => Set<Teikem.Domain.Messaging.ConsigneeConversation>();
    public DbSet<Teikem.Domain.Messaging.ConsigneeMessage> ConsigneeMessages => Set<Teikem.Domain.Messaging.ConsigneeMessage>();
    public DbSet<Teikem.Domain.Messaging.ClientServiceTeamMember> ClientServiceTeamMembers => Set<Teikem.Domain.Messaging.ClientServiceTeamMember>();
    public DbSet<Teikem.Domain.Messaging.Message> Messages => Set<Teikem.Domain.Messaging.Message>();
    public DbSet<Teikem.Domain.Messaging.UserPresence> UserPresences => Set<Teikem.Domain.Messaging.UserPresence>();

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

            // Filtro global de tenant + alcance de datos del usuario (almacén/cliente)
            LambdaExpression? filter = null;
            if (typeof(ITenantScoped).IsAssignableFrom(clr))
                filter = BuildTenantFilter(clr, optional: false);
            else if (typeof(IOptionallyTenantScoped).IsAssignableFrom(clr))
                filter = BuildTenantFilter(clr, optional: true);
            filter = AddScopeFilter(clr, filter, "WarehouseId", nameof(QueryFilterWarehouseIds));
            filter = AddScopeFilter(clr, filter, "ClientId", nameof(QueryFilterClientIds));
            if (filter is not null) entityType.SetQueryFilter(filter);
        }
    }

    /// <summary>
    /// e => QueryFilterTenantId == null || e.TenantId == QueryFilterTenantId  (|| e.TenantId == null si es opcional).
    /// Lote 42: equivale al filtro anterior (TenantFilterDisabled || e.TenantId == CurrentTenantId) fila por fila, pero con el
    /// plugin <see cref="TenantFilterEvaluatablePlugin"/> EF lo traduce SIN el OR del bypass: <c>[e].[TenantId] = @p</c> (buscable).
    /// </summary>
    private LambdaExpression BuildTenantFilter(Type clr, bool optional)
    {
        var e = Expression.Parameter(clr, "e");
        var ctx = Expression.Constant(this);
        var filterTenant = Expression.Property(ctx, nameof(QueryFilterTenantId));                       // int?
        var bypass = Expression.Equal(filterTenant, Expression.Constant(null, typeof(int?)));          // la forma exacta que reconoce el plugin
        var tenantProp = Expression.Property(e, "TenantId");
        var tenantAsNullable = tenantProp.Type == typeof(int?) ? (Expression)tenantProp : Expression.Convert(tenantProp, typeof(int?));
        var sameTenant = Expression.Equal(tenantAsNullable, filterTenant);
        Expression body = !optional
            ? Expression.OrElse(bypass, sameTenant)
            : Expression.OrElse(bypass, Expression.OrElse(Expression.Equal(tenantProp, Expression.Constant(null, typeof(int?))), sameTenant));
        return Expression.Lambda(body, e);
    }

    /// <summary>
    /// Agrega al filtro (si la entidad tiene la columna) el alcance de datos:  e => ids == null || ids.Contains(e.Col)  (|| e.Col == null si es int?).
    /// Una fila sin almacén/cliente (NULL) no pertenece a ninguno y se ve siempre. Los catálogos y lo demás sin la columna no se tocan.
    /// Lote 42: «ids == null» es la forma exacta que reconoce <see cref="TenantFilterEvaluatablePlugin"/> (sin alcance, el término desaparece).
    /// </summary>
    private LambdaExpression? AddScopeFilter(Type clr, LambdaExpression? existing, string column, string idList)
    {
        var prop = clr.GetProperty(column);
        if (prop is null || (prop.PropertyType != typeof(int) && prop.PropertyType != typeof(int?))) return existing;
        var e = existing?.Parameters[0] ?? Expression.Parameter(clr, "e");
        var ctx = Expression.Constant(this);
        var ids = Expression.Property(ctx, idList);                                                   // int[]?
        var notScoped = Expression.Equal(ids, Expression.Constant(null, typeof(int[])));
        var col = Expression.Property(e, prop);
        Expression inList = prop.PropertyType == typeof(int?)
            ? Expression.Call(typeof(Enumerable), nameof(Enumerable.Contains), new[] { typeof(int) }, ids, Expression.Property(col, "Value"))
            : Expression.Call(typeof(Enumerable), nameof(Enumerable.Contains), new[] { typeof(int) }, ids, col);
        if (prop.PropertyType == typeof(int?))
            inList = Expression.OrElse(Expression.Equal(col, Expression.Constant(null, typeof(int?))), inList);
        var scope = Expression.OrElse(notScoped, inList);
        var body = existing is null ? scope : Expression.AndAlso(existing.Body, scope);
        return Expression.Lambda(body, e);
    }
}
