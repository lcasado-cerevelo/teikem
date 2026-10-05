using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Teikem.Domain.Identity;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Analytics;
using Teikem.Infrastructure.Fleet;
using Teikem.Infrastructure.Migration;
using Teikem.Infrastructure.Orders;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Persistence.Interceptors;
using Teikem.Infrastructure.Persistence.Scripts;
using Teikem.Infrastructure.Seeding;
using Teikem.Infrastructure.Services;
using Teikem.Infrastructure.Trips;
using Teikem.Infrastructure.Wms;

namespace Teikem.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddTeikemInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        services.AddMemoryCache();
        // Data Protection cifra el secreto del MFA y los enlaces de invitación. Sin configuración (desarrollo, pruebas) las llaves viven donde
        // .NET decida; en un servidor (IIS) se pierden al reiniciar el sitio y NADA cifrado antes se puede volver a leer. Por eso el
        // instalador define DataProtection:KeysPath (carpeta fija, llaves protegidas con DPAPI de la máquina en Windows).
        var protection = services.AddDataProtection().SetApplicationName("Teikem");
        var keysPath = config["DataProtection:KeysPath"];
        if (!string.IsNullOrWhiteSpace(keysPath))
        {
            Directory.CreateDirectory(keysPath);
            protection.PersistKeysToFileSystem(new DirectoryInfo(keysPath));
            if (OperatingSystem.IsWindows()) protection.ProtectKeysWithDpapi(protectToLocalMachine: true);
        }

        // Contexto de tenant (scoped, lo llena el middleware del API)
        services.AddScoped<TenantContext>();
        services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());

        services.AddSingleton<ILookupCache, LookupCache>();
        services.AddScoped<ISecurityEventWriter, SecurityEventWriter>();
        services.AddScoped<AuditSaveChangesInterceptor>();
        // Lote 14 (P2, D14): bandeja de cambios de inventario de la petición → cola en memoria SOLO en el commit real.
        services.AddScoped<IInventoryChangeSink, InventoryChangeSink>();
        services.AddScoped<InventoryChangeCommitInterceptor>();

        services.AddDbContext<TeikemDbContext>((sp, options) =>
        {
            options.UseSqlServer(config.GetConnectionString("Teikem"), sql =>
            {
                sql.EnableRetryOnFailure(3);
                sql.CommandTimeout(60);
            });
            options.AddInterceptors(sp.GetRequiredService<AuditSaveChangesInterceptor>(), sp.GetRequiredService<InventoryChangeCommitInterceptor>());
            // Los filtros de tenant sobre catálogos/definiciones son intencionales: la fila requerida siempre es visible
            // (global o del mismo tenant), así que esta advertencia de EF no aplica.
            options.ConfigureWarnings(w => w.Ignore(CoreEventId.PossibleIncorrectRequiredNavigationWithQueryFilterInteractionWarning));
        });

        // Identity core con llaves int. Contraseñas según NIST 800-63B: longitud ≥ 12, sin reglas de composición.
        services.AddIdentityCore<ApplicationUser>(o =>
            {
                o.Password.RequiredLength = 12;
                o.Password.RequireDigit = false; o.Password.RequireLowercase = false; o.Password.RequireUppercase = false; o.Password.RequireNonAlphanumeric = false;
                o.Password.RequiredUniqueChars = 4;
                o.Lockout.AllowedForNewUsers = true; o.Lockout.MaxFailedAccessAttempts = 5; o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                o.User.RequireUniqueEmail = true;
            })
            .AddRoles<ApplicationRole>()
            .AddEntityFrameworkStores<TeikemDbContext>()
            .AddDefaultTokenProviders();
        services.Configure<PasswordHasherOptions>(o => o.IterationCount = 600_000); // PBKDF2 reforzado (Argon2id: ver decisiones)
        services.AddScoped<IPasswordBreachChecker, NoOpPasswordBreachChecker>();

        services.Configure<JwtOptions>(config.GetSection("Jwt"));
        services.Configure<OnboardingOptions>(config.GetSection("Auth:Onboarding"));
        services.AddSingleton<JwtTokenService>();

        // Servicios transversales
        services.AddScoped<LookupService>();
        services.AddScoped<PostalLocalityService>();       // Lote 1 (cambios de Almacén): catálogo global ciudad ↔ código postal
        services.AddScoped<StatusService>();
        services.AddScoped<ContactPointService>();
        services.AddScoped<PermissionService>();
        services.AddScoped<IdempotencyCheckRecorder>();   // Lote 8A: comprobaciones a repetir en la idempotencia
        services.AddScoped<AuditQueryService>();
        services.AddScoped<CompanySessionService>(); // Lote F10: sesiones de toda la compañía (Seguridad y auditoría)
        services.AddScoped<CustomFieldService>();
        services.AddScoped<AnalyticsService>();
        services.AddScoped<AnalyticsEngine>();
        services.AddScoped<ModuleService>();
        services.AddScoped<TenantService>();
        services.AddScoped<BrandLogoService>(); // Lote 19: logos de la marca por compañía
        services.AddScoped<AuthService>();
        services.AddScoped<UserAdminService>();
        services.AddScoped<ProvisioningService>();

        // Lote 2 — Clientes y contratos
        services.AddScoped<ClientService>();
        services.AddScoped<LocationService>();
        services.AddScoped<ContractService>();
        services.AddScoped<RateService>();
        services.AddScoped<SpecialServiceService>();
        services.AddScoped<PortalUserService>();
        services.AddScoped<IStatusTransitionEffect, ContractStatusEffect>();
        services.AddScoped<IStatusTransitionEffect, PortalUserStatusEffect>();
        services.AddScoped<IInvitationSender, LoggingInvitationSender>();
        // 2026-09-30: correo transaccional (código de verificación del primer ingreso) por Brevo.
        services.AddHttpClient<ITransactionalEmailSender, BrevoEmailSender>(c => c.Timeout = TimeSpan.FromSeconds(15));
        // La invitación al portal reutiliza el token DataProtector de Identity: su vida es Portal:InviteHours (48 h por defecto).
        // Nota: afecta a todos los tokens DataProtector de Identity (incluido el futuro reset de contraseña).
        services.Configure<DataProtectionTokenProviderOptions>(o => o.TokenLifespan = TimeSpan.FromHours(Math.Max(1, config.GetValue("Portal:InviteHours", 48))));

        // Lote 3 — Órdenes de transporte
        services.AddScoped<OrderService>();
        services.AddScoped<OrderReadService>();
        services.AddScoped<OrderStatusService>();
        services.AddScoped<OrderQuoteService>();
        services.AddScoped<INumberSequenceService, NumberSequenceService>();
        // Costura del "saldo pendiente" de crédito (R18): hoy Σ QuotedAmount de órdenes en curso; Facturación registrará otra implementación.
        services.AddScoped<IClientBalanceProvider, QuotedOrdersBalanceProvider>();
        services.AddScoped<IStatusTransitionEffect, OrderStatusEffect>();
        // Importador de órdenes (ajuste D)
        services.AddScoped<ImportTemplateService>();
        services.AddScoped<OrderImportService>();

        // Lote 4 — Flota, choferes y mantenimiento
        services.AddScoped<VehicleService>();
        services.AddScoped<VehicleDocumentService>();
        services.AddScoped<DriverService>();
        services.AddScoped<DriverDocumentService>();
        services.AddScoped<DispatchZoneService>();
        services.AddScoped<FleetDocumentService>();
        // Disponibilidad para despacho (R7): costura que consumirá tal cual el planificador de Despacho.
        services.AddScoped<IFleetAvailabilityService, FleetAvailabilityService>();
        services.AddScoped<MaintenanceScheduleService>();
        services.AddScoped<MaintenanceWorkOrderService>();
        services.AddScoped<FuelLogService>();
        services.AddScoped<DriverRateService>();
        services.AddScoped<DriverPayPolicyService>();
        // Resolvedor de tarifas del chofer: costura para la entrega especial y la liquidación del Lote 9.
        services.AddScoped<IDriverRateResolver, DriverRateResolver>();
        services.AddScoped<DriverTripService>();
        services.AddScoped<SpecialDeliveryDispatchService>();
        services.AddScoped<IStatusTransitionEffect, VehicleStatusEffect>();
        services.AddScoped<IStatusTransitionEffect, DriverStatusEffect>();
        services.AddScoped<IStatusTransitionEffect, DriverRatesRetirementEffect>();
        services.AddScoped<IStatusTransitionEffect, WorkOrderStatusEffect>();
        services.AddScoped<IStatusTransitionEffect, DriverTripOrderEffect>();
        services.AddScoped<IStatusTransitionEffect, DriverTripStatusEffect>();

        // Lote 5 — Trips y rutas. RouteWriter es la única vía de escritura de TripOrder/Route/RouteStop; el motor de
        // optimización va detrás de IRouteOptimizer (HEURISTIC en este lote). Los efectos de TripStatus y OrderStatus resuelven
        // StatusService/RouteWriter de forma perezosa (IServiceProvider) para no formar un ciclo con StatusService.
        services.AddScoped<RouteWriter>();
        services.AddScoped<TripIssueBuilder>();
        services.AddScoped<IRouteOptimizer, HeuristicRouteOptimizer>();
        services.AddScoped<TripReadService>();
        services.AddScoped<TripService>();
        services.AddScoped<TripOrderService>();
        services.AddScoped<RouteOptimizationService>();
        services.AddScoped<TripDispatchService>();
        services.AddScoped<TripLifecycleService>();
        services.AddScoped<TripDayPlanningService>();
        services.AddScoped<OutboundScanService>();
        services.AddScoped<TripMonitorService>();
        services.AddScoped<IStatusTransitionEffect, TripStatusEffect>();
        services.AddScoped<IStatusTransitionEffect, TripOrderReleaseEffect>();

        // Lote 6 — Inventario y almacén. InventoryLedger es la ÚNICA vía de escritura del inventario (saldos, ledger y series);
        // WarehouseTaskWriter crea y cierra tareas con historial; PutawaySuggester sugiere posiciones con rotación (D24).
        services.AddScoped<InventoryLedger>();
        services.AddScoped<WarehouseTaskWriter>();
        services.AddScoped<PutawaySuggester>();
        services.AddScoped<WarehouseService>();
        services.AddScoped<WarehouseLayoutService>();
        services.AddScoped<BinProductsService>();   // informe "Productos por posición"
        services.AddScoped<ProductService>();
        services.AddScoped<ProductSerialConversionService>();   // Lote 26 (Rentas R0): conversión a serie
        services.AddScoped<ProductCategoryService>();
        services.AddScoped<InventoryReadService>();
        services.AddScoped<InventoryAdjustmentService>();
        services.AddScoped<TraceabilityService>();
        services.AddScoped<AsnService>();
        services.AddScoped<ReceiptService>();
        services.AddScoped<WarehouseTaskService>();
        services.AddScoped<ReplenishmentService>();
        services.AddScoped<CycleCountService>();
        services.AddScoped<PickBatchService>();
        services.AddScoped<SupplierService>();
        services.AddScoped<PurchaseOrderService>();
        services.AddScoped<PurchaseShortageService>();
        services.AddScoped<DockAppointmentService>();
        services.AddScoped<CrossDockService>();
        // Cola unificada: un handler por tipo de tarea (D41); PICK, PACK y LOAD no tienen handler en este lote (422).
        services.AddScoped<IWarehouseTaskHandler, PutawayTaskHandler>();
        services.AddScoped<IWarehouseTaskHandler, ReplenishTaskHandler>();
        services.AddScoped<IWarehouseTaskHandler, CountTaskHandler>();
        services.AddScoped<IWarehouseTaskHandler, CrossDockTaskHandler>();
        // Costuras entre piezas (interfaz en P0, implementación en su pieza).
        services.AddScoped<IPurchaseOrderReceiving, PurchaseOrderReceivingService>();
        services.AddScoped<IReceiptConfirmationParticipant, CrossDockReceiptParticipant>();
        services.AddScoped<IOrderInventoryLines, OrderInventoryLinesProvider>();
        // Efectos de estatus: resuelven sus dependencias de forma perezosa (IServiceProvider) para no formar ciclo con StatusService.
        services.AddScoped<IStatusTransitionEffect, WarehouseTaskStatusEffect>();
        services.AddScoped<IStatusTransitionEffect, DockAppointmentStatusEffect>();

        // Lote 14 — punto único de "hoy" en hora de la compañía (Puerto Rico por defecto) y conciliación Kárdex ↔ saldo con
        // descuadres (P1, síncrona; la revisión en segundo plano de P2 llamará a InventoryReconciliationService.CheckProductsAsync).
        // Región y formatos (2026-10): la zona es la de la compañía del contexto (Tenant.TimeZoneId, con caché por compañía).
        services.AddSingleton<TenantZoneCache>();
        services.AddScoped<ITenantClock>(sp => new ContextTenantClock(
            sp.GetRequiredService<ITenantContext>(), sp.GetRequiredService<TeikemDbContext>(), sp.GetRequiredService<TenantZoneCache>()));
        services.AddScoped<InventoryReconciler>();
        services.AddScoped<InventoryReconciliationService>();
        // Lote 15 — franja "Almacén hoy" del Pulso (días locales con el mismo ITenantClock).
        services.AddScoped<WarehousePulseService>();
        // P2 (D14): revisión en segundo plano segundos después de cada movimiento. Cola en memoria acotada (singleton) y un
        // BackgroundService que la consume; solo arranca con app.Run() (los comandos de consola no encolan nada).
        services.Configure<InventoryReconciliationOptions>(config.GetSection(InventoryReconciliationOptions.Section));
        services.AddSingleton(sp => new InventoryReconciliationQueue(sp.GetRequiredService<IOptions<InventoryReconciliationOptions>>().Value));
        services.AddHostedService<InventoryReconciliationWorker>();

        // Lote 8A — app de almacén: aparatos de confianza, PIN por usuario (login por aparato en AuthService) y
        // sincronización por diferencia. La idempotencia (Idempotency-Key) es un middleware del API sin servicio propio.
        services.AddScoped<DeviceService>();
        services.AddScoped<PinService>();
        services.AddScoped<SyncService>();

        // Registro de fuentes de datos (cada lote agrega las suyas) y resolvers de pertenencia
        services.AddScoped<IDataSourceRegistry, DataSourceRegistry>();
        services.AddScoped<IDataSource, AuditLogDataSource>();
        services.AddScoped<IDataSource, SecurityEventDataSource>();
        services.AddScoped<IDataSource, UserDataSource>();
        services.AddScoped<IOwnedEntityResolver, UserOwnedEntityResolver>();
        // Lote 2
        services.AddScoped<IDataSource, ClientDataSource>();
        services.AddScoped<IDataSource, ContractDataSource>();
        services.AddScoped<IDataSource, LocationDataSource>();
        services.AddScoped<IOwnedEntityResolver, ClientOwnedEntityResolver>();
        services.AddScoped<IOwnedEntityResolver, ClientContactOwnedEntityResolver>();
        services.AddScoped<IOwnedEntityResolver, LocationOwnedEntityResolver>();
        services.AddScoped<IOwnedEntityResolver, ContractOwnedEntityResolver>();
        // Lote 3
        services.AddScoped<IDataSource, TransportOrderDataSource>();
        services.AddScoped<IOwnedEntityResolver, TransportOrderOwnedEntityResolver>();
        services.AddScoped<IOwnedEntityResolver, OrderStopOwnedEntityResolver>();
        services.AddScoped<IOwnedEntityResolver, OrderCodOwnedEntityResolver>();
        services.AddScoped<IOwnedEntityResolver, ImportBatchOwnedEntityResolver>();
        services.AddScoped<IOwnedEntityResolver, ImportTemplateOwnedEntityResolver>();
        // Lote 4 (no hay fuentes de tarifas ni de DriverTrip: Análisis solo exige analytics.view y expondría la compensación)
        services.AddScoped<IDataSource, VehicleDataSource>();
        services.AddScoped<IDataSource, DriverDataSource>();
        services.AddScoped<IDataSource, WorkOrderDataSource>();
        services.AddScoped<IDataSource, FuelLogDataSource>();
        services.AddScoped<IDataSource, FleetDocumentDataSource>();
        services.AddScoped<IOwnedEntityResolver, VehicleOwnedEntityResolver>();
        services.AddScoped<IOwnedEntityResolver, DriverOwnedEntityResolver>();
        services.AddScoped<IOwnedEntityResolver, DispatchZoneOwnedEntityResolver>();
        services.AddScoped<IOwnedEntityResolver, MaintenanceScheduleOwnedEntityResolver>();
        services.AddScoped<IOwnedEntityResolver, WorkOrderOwnedEntityResolver>();
        services.AddScoped<IOwnedEntityResolver, FuelLogOwnedEntityResolver>();
        services.AddScoped<IOwnedEntityResolver, DriverTripOwnedEntityResolver>();
        // Resolver cerrado (siempre 404): tarifas y documentos de flota no admiten contactos ni campos por id suelto.
        services.AddScoped<IOwnedEntityResolver>(_ => new ClosedOwnedEntityResolver(Domain.Constants.EntityTypes.DriverRate));
        services.AddScoped<IOwnedEntityResolver>(_ => new ClosedOwnedEntityResolver(Domain.Constants.EntityTypes.FleetDocument));
        // Hueco del Lote 2: PORTAL_USER tenía permiso de dueño pero no resolver (CustomFieldService omitía la verificación).
        services.AddScoped<IOwnedEntityResolver, PortalUserOwnedEntityResolver>();
        // Lote 5 — fuente TRIP (DateField PlanDate; sin montos) y resolvers de pertenencia de rutas
        services.AddScoped<IDataSource, TripDataSource>();
        services.AddScoped<IOwnedEntityResolver, TripOwnedEntityResolver>();
        services.AddScoped<IOwnedEntityResolver, RouteOwnedEntityResolver>();
        services.AddScoped<IOwnedEntityResolver, RouteStopOwnedEntityResolver>();
        // OPTIMIZATION_RUN es bitácora de solo lectura (trips.view): sin permiso de escritura de dueño, así que la escritura
        // por id suelto (contactos, campos personalizados) se cierra con el resolver cerrado (siempre 404, sin oráculo).
        services.AddScoped<IOwnedEntityResolver>(_ => new ClosedOwnedEntityResolver(Domain.Constants.EntityTypes.OptimizationRun));
        // Lote 6 — fuentes de inventario y almacén (DateField en las que representan actividad de un período) y resolvers de
        // pertenencia. INVENTORY_SERIAL, WAREHOUSE_TASK y CROSSDOCK_ALLOCATION no tienen escritura de dueño: resolver cerrado.
        services.AddScoped<IDataSource, WarehouseDataSource>();
        services.AddScoped<IDataSource, ProductDataSource>();
        services.AddScoped<IDataSource, StockBalanceDataSource>();
        services.AddScoped<IDataSource, InventoryTransactionDataSource>();
        services.AddScoped<IDataSource, ReceiptDataSource>();
        services.AddScoped<IDataSource, WarehouseTaskDataSource>();
        services.AddScoped<IDataSource, PickBatchDataSource>();
        services.AddScoped<IDataSource, CycleCountDataSource>();   // Lote 7A (P2): indicador de Pulso 'Conteos con diferencia'
        // Lote 7A — "Actividad reciente": un proveedor de eventos por BusinessModule (colección, igual que IDataSource) y el feed.
        services.AddScoped<IActivityEventProvider, WarehouseActivityProvider>();
        services.AddScoped<ActivityFeedService>();
        // Lote 14 (D6) — "Necesita tu atención": un proveedor de filas por tipo de aviso (colección) y el servicio del panel.
        services.AddScoped<IAttentionItemProvider, InventoryDiscrepancyAttentionProvider>();
        services.AddScoped<AttentionFeedService>();
        services.AddScoped<IOwnedEntityResolver, WarehouseOwnedEntityResolver>();
        services.AddScoped<IOwnedEntityResolver, WarehouseDockOwnedEntityResolver>();
        services.AddScoped<IOwnedEntityResolver, ProductOwnedEntityResolver>();
        services.AddScoped<IOwnedEntityResolver, ReceiptOwnedEntityResolver>();
        services.AddScoped<IOwnedEntityResolver, AsnOwnedEntityResolver>();
        services.AddScoped<IOwnedEntityResolver, CycleCountOwnedEntityResolver>();
        services.AddScoped<IOwnedEntityResolver, PickBatchOwnedEntityResolver>();
        services.AddScoped<IOwnedEntityResolver, DockAppointmentOwnedEntityResolver>();
        services.AddScoped<IOwnedEntityResolver, CrossDockPlanOwnedEntityResolver>();
        services.AddScoped<IOwnedEntityResolver, PurchaseOrderOwnedEntityResolver>();
        services.AddScoped<IOwnedEntityResolver, SupplierOwnedEntityResolver>();
        services.AddScoped<IOwnedEntityResolver>(_ => new ClosedOwnedEntityResolver(Domain.Constants.EntityTypes.InventorySerial));
        services.AddScoped<IOwnedEntityResolver>(_ => new ClosedOwnedEntityResolver(Domain.Constants.EntityTypes.WarehouseTask));
        services.AddScoped<IOwnedEntityResolver>(_ => new ClosedOwnedEntityResolver(Domain.Constants.EntityTypes.CrossDockAllocation));
        // STOCK_BALANCE e INVENTORY_TRANSACTION (proyección y ledger, solo los escribe InventoryLedger) y RECEIPT_LINE (hija del
        // recibo): sin resolver, CustomFieldService omitiría la pertenencia (PUT de valores sobre ids ajenos → 200). Cerrados → 404.
        services.AddScoped<IOwnedEntityResolver>(_ => new ClosedOwnedEntityResolver(Domain.Constants.EntityTypes.StockBalance));
        services.AddScoped<IOwnedEntityResolver>(_ => new ClosedOwnedEntityResolver(Domain.Constants.EntityTypes.InventoryTransaction));
        services.AddScoped<IOwnedEntityResolver>(_ => new ClosedOwnedEntityResolver(Domain.Constants.EntityTypes.ReceiptLine));
        // Lote 7A: PRODUCT_CATEGORY existe solo para auditoría; la ruta polimórfica siempre responde 404.
        services.AddScoped<IOwnedEntityResolver>(_ => new ClosedOwnedEntityResolver(Domain.Constants.EntityTypes.ProductCategory));
        // Lote 8A: USER_DEVICE (aparato de almacén) existe solo para auditoría; sin campos personalizados → siempre 404.
        services.AddScoped<IOwnedEntityResolver>(_ => new ClosedOwnedEntityResolver(Domain.Constants.EntityTypes.UserDevice));
        // Lote 14: descuadres Kárdex ↔ saldo (fuente con DateField DetectedAtUtc); sin escritura de dueño → resolver cerrado.
        services.AddScoped<IDataSource, InventoryDiscrepancyDataSource>();
        services.AddScoped<IOwnedEntityResolver>(_ => new ClosedOwnedEntityResolver(Domain.Constants.EntityTypes.InventoryDiscrepancy));

        // Seeders e inicialización
        services.AddScoped<PermissionSeeder>();
        services.AddScoped<SystemAnalyticsSeeder>();
        services.AddScoped<DemoTenantSeeder>();
        services.AddSingleton<DatabaseInitializer>();
        // Lote 10 — migración de datos heredados (QuickBooks + WMS MSWM): comando CLI import-legacy, fuera del pipeline HTTP.
        services.AddScoped<LegacyImportService>();
        services.AddSingleton<LegacyImportRunner>();
        return services;
    }
}
