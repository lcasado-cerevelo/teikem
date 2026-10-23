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
using Teikem.Infrastructure.Jobs;
using Teikem.Infrastructure.Messaging;
using Teikem.Infrastructure.Migration;
using Teikem.Infrastructure.Notifications;
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
            .AddErrorDescriber<Teikem.Infrastructure.Identity.LocalizedIdentityErrorDescriber>()   // Lote 120: los mensajes de contraseña en el idioma de la persona
            .AddDefaultTokenProviders();
        services.Configure<PasswordHasherOptions>(o => o.IterationCount = 600_000); // PBKDF2 reforzado (Argon2id: ver decisiones)
        services.AddScoped<IPasswordBreachChecker, NoOpPasswordBreachChecker>();

        services.Configure<JwtOptions>(config.GetSection("Jwt"));
        services.Configure<Teikem.Infrastructure.Files.FileStoreOptions>(config.GetSection(Teikem.Infrastructure.Files.FileStoreOptions.Section));   // Lote 127: almacén de archivos
        services.AddSingleton<Teikem.Infrastructure.Files.IFileStore, Teikem.Infrastructure.Files.EncryptedDiskFileStore>();
        services.AddScoped<StoredFileService>();
        services.Configure<OnboardingOptions>(config.GetSection("Auth:Onboarding"));
        services.Configure<DriverTrackingOptions>(config.GetSection("DriverApp:Tracking"));   // 2026-10-23: valores globales del seguimiento de ruta
        services.AddScoped<DriverTrackingService>();
        services.AddScoped<DriverNoticeService>();
        services.Configure<DriverAppOptions>(config.GetSection("DriverApp"));   // 2026-10-23: tope de la sesión del chofer
        services.Configure<TurnstileOptions>(config.GetSection("Auth:Turnstile"));   // Lote 101: apagado por omisión
        services.Configure<TrustedBrowserOptions>(config.GetSection("Auth:TrustedBrowsers"));   // Lote 100: apagado por omisión
        services.Configure<Teikem.Infrastructure.Legal.LegalOptions>(config.GetSection("Legal"));   // Lote 99
        services.Configure<PasswordResetOptions>(config.GetSection("Auth:PasswordReset"));
        services.Configure<SessionOptions>(config.GetSection("Auth:Sessions"));
        services.Configure<TrustedNetworkOptions>(config.GetSection("Auth:TrustedNetworks"));   // 2026-10-12: IP preferida (apagada por defecto)
        services.AddSingleton<JwtTokenService>();

        // Servicios transversales
        services.AddScoped<LookupService>();
        services.AddScoped<PostalLocalityService>();       // Lote 1 (cambios de Almacén): lista global ciudad ↔ código postal
        services.AddScoped<StatusService>();
        services.AddScoped<ContactPointService>();
        services.AddScoped<PermissionService>();
        services.AddScoped<IdempotencyCheckRecorder>();   // Lote 8A: comprobaciones a repetir en la idempotencia
        services.AddScoped<AuditQueryService>();
        services.AddScoped<CompanySessionService>(); // Lote F10: sesiones de toda la compañía (Seguridad y auditoría)
        services.AddScoped<CustomFieldService>();
        services.AddScoped<AnalyticsService>();
        services.AddScoped<GlobalSearchService>();
        services.AddScoped<AnalyticsEngine>();
        services.AddScoped<ModuleService>();
        services.AddScoped<TenantService>();
        services.AddScoped<PortalBrandService>();   // 2026-10-22: el portal se viste con la marca de la compañía
        services.AddScoped<BrandLogoService>(); // Lote 19: logos de la marca por compañía
        services.AddScoped<DriverAppService>(); // 2026-10-23: app del chofer (manifiestos, aceptar, no aceptar con motivo)
        services.AddScoped<PublicBrandService>(); // 2026-10-22: logo de la pantalla de entrada (sin sesión)
        services.AddScoped<TrustedNetworkService>();
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
        services.AddScoped<IInvitationSender, EmailInvitationSender>();   // 2026-10-22: la invitación al portal sale por correo (Brevo); LoggingInvitationSender queda para pruebas
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
        // Lote 39: única decisión de "ver precios de las órdenes" (orders.view_price)
        services.AddScoped<OrderPriceVisibility>();
        services.AddScoped<INumberSequenceService, NumberSequenceService>();
        // Costura del "saldo pendiente" de crédito (R18): hoy Σ QuotedAmount de órdenes en curso; Facturación registrará otra implementación.
        services.AddScoped<IClientBalanceProvider, QuotedOrdersBalanceProvider>();
        services.AddScoped<IStatusTransitionEffect, OrderStatusEffect>();
        // Importador de órdenes (ajuste D)
        services.AddScoped<ImportTemplateService>();
        services.AddScoped<OrderImportService>();
        // Lote 38 (F-B): importación CSV de consignatarios (mismas plantillas y lotes, Kind CONSIGNEE)
        services.AddScoped<ConsigneeImportService>();
        services.AddScoped<EntityImportService>();   // 2026-10-21: motor genérico de importación por plantilla
        services.AddScoped<IImportEntityRows, ProductImportRows>();
        services.AddScoped<IImportEntityRows, SupplierImportRows>();
        services.AddScoped<IImportEntityRows, WarehouseImportRows>();
        services.AddScoped<OutputEntityRecorder>();   // 2026-10-21: salida de catálogos por Integración (productos)
        services.AddScoped<IExportEntityRows, ProductExportRows>();
        services.AddScoped<IImportEntityRows, BinImportRows>();

        // Lote 43 (F-G1) — impresoras y plantillas de label (sin IDataSource ni StatusService: solo IsActive, D-FG5)
        services.AddScoped<PrinterService>();
        services.AddScoped<LabelTemplateService>();
        services.AddScoped<PrintingOptionsService>();
        services.AddSingleton(new QzSigningService(config));   // certificado y clave de QZ Tray (Printing:Qz); nunca salen en una respuesta
        // Lote 4 — Flota, choferes y mantenimiento
        services.AddScoped<VehicleService>();
        services.AddScoped<VehicleDocumentService>();
        services.AddScoped<DriverService>();
        services.AddScoped<DriverDocumentService>();
        services.AddScoped<DispatchZoneService>();
        services.AddScoped<ZoneGroupService>();   // Lote 129: niveles de zona
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
        services.AddScoped<DeliveryAttemptService>();   // Lote 93 (P9)
        services.AddScoped<DriverSettlementService>();   // Lote 94 (P9)
        services.AddScoped<AccountingRunService>();   // Lote 181 (H1)
        services.AddScoped<ReceiptGroupService>();   // Lote 182 (C4)
        services.AddScoped<IStatusTransitionEffect, DeliveryAttemptOrderEffect>();
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
        services.AddScoped<PartialBillingService>();   // Lote 111c: entrega parcial y cobro
        services.AddScoped<ReturnSuggestionService>();   // Lote 113: devolver tras N intentos
        services.AddScoped<ReturnOrderService>();   // Lote 112: devolución
        services.AddScoped<ReappearedOrderService>();   // Lote 111b: la orden nueva de las piezas que aparecieron
        services.AddScoped<ScanActionService>();   // Lote 107: escaneo con acción escogida
        services.AddScoped<ClaimService>();   // Lote 111: faltantes y reclamaciones
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
        services.AddScoped<ValuationService>();   // Lote 148: valuación del inventario (reporte y costo de las salidas)
        services.AddScoped<StockExitService>();   // orden de salida (FEFO) de lo disponible, una sola regla para la app y las pantallas
        services.AddScoped<InventoryAdjustmentService>();
        services.AddScoped<MultiAdjustmentService>();   // 2026-10-12: ajuste múltiple AJM-######
        services.AddScoped<TraceabilityService>();
        services.AddScoped<AsnService>();
        services.AddScoped<ReceiptService>();
        services.AddScoped<WarehouseTaskService>();
        services.AddScoped<ReplenishmentService>();
        services.AddScoped<CycleCountService>();
        services.AddScoped<DamageService>();   // 2026-10-08: daños (cuarentena / desechar / recuperar)
        services.AddScoped<PickBatchService>();
        services.AddScoped<PickBatchDocumentService>();   // Lote 173: Recolección y empaque agrupada por documento
        services.AddScoped<PickGroupService>();   // Lote 176: recolección maestra (varios documentos, un envío)
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
        // Lote 27 — Rentas (R1): renta hasta el despacho, extensiones y cancelación; posición EN-RENTA a demanda; efecto de estatus
        // (sella fechas; no usa el ledger) y resolvers de pertenencia de RENTAL, RENTAL_RETURN y RENTAL_PROCESS.
        services.AddScoped<RentalBinResolver>();
        services.AddScoped<RentalService>();
        services.AddScoped<IStatusTransitionEffect, RentalStatusEffect>();
        services.AddScoped<IOwnedEntityResolver, RentalOwnedEntityResolver>();
        services.AddScoped<IOwnedEntityResolver, RentalReturnOwnedEntityResolver>();
        services.AddScoped<IOwnedEntityResolver, RentalProcessOwnedEntityResolver>();
        // Lote 28 — Rentas (R2): devolución de renta y proceso configurable del equipo devuelto; el efecto del proceso usa el
        // ledger resuelto con IServiceProvider (sin ciclo StatusService ↔ InventoryLedger).
        services.AddScoped<RentalReturnService>();
        services.AddScoped<RentalProcessService>();
        services.AddScoped<IStatusTransitionEffect, RentalProcessStatusEffect>();

        // Lote 14 — punto único de "hoy" en hora de la compañía (Puerto Rico por defecto) y conciliación Kárdex ↔ saldo con
        // descuadres (P1, síncrona; la revisión en segundo plano de P2 llamará a InventoryReconciliationService.CheckProductsAsync).
        // Región y formatos (2026-10): la zona es la de la compañía del contexto (Tenant.TimeZoneId, con caché por compañía).
        services.AddSingleton<TenantZoneCache>();
        services.AddScoped<ITenantClock>(sp => new ContextTenantClock(
            sp.GetRequiredService<ITenantContext>(), sp.GetRequiredService<TeikemDbContext>(), sp.GetRequiredService<TenantZoneCache>()));
        services.AddScoped<InventoryReconciler>();
        services.AddScoped<InventoryReconciliationService>();
        services.AddScoped<InventoryAlertService>();
        services.AddScoped<FleetAlertService>();   // Lote 168: avisos de flota
        services.AddScoped<PortalCreateService>();   // Lote 156: lo que el cliente crea desde el portal   // Lote 141: avisos de inventario
        services.AddScoped<ExitNoticeService>();   // Lote 143: avisos de salida
        services.AddScoped<IStockArrivalHook, ExitNoticeBackorderHook>();   // Lote 194: al entrar mercancía por un recibo se reserva el pendiente de los avisos de salida
        services.AddScoped<IExitNoticeDispatchReversal>(sp => sp.GetRequiredService<ExitNoticeService>());   // Lote 143: deshacer una recolección de un aviso de salida
        // Lote 15 — franja "Almacén hoy" del Pulso (días locales con el mismo ITenantClock).
        services.AddScoped<WarehousePulseService>();
        // P2 (D14): revisión en segundo plano segundos después de cada movimiento. Cola en memoria acotada (singleton) y un
        // BackgroundService que la consume; solo arranca con app.Run() (los comandos de consola no encolan nada).
        services.Configure<InventoryReconciliationOptions>(config.GetSection(InventoryReconciliationOptions.Section));
        services.AddSingleton(sp => new InventoryReconciliationQueue(sp.GetRequiredService<IOptions<InventoryReconciliationOptions>>().Value));
        services.AddHostedService<InventoryReconciliationWorker>();

        // Lote 50 (P2.2) — trabajos programados por compañía: trabajos en código + ejecutor (reclamo atómico) + worker + servicio de la pantalla.
        services.Configure<JobsOptions>(config.GetSection(JobsOptions.Section));
        services.AddSingleton<IScheduledJob, JobRunPurgeJob>();
        services.AddSingleton<IScheduledJob, DriverTrackingPurgeJob>();   // 2026-10-23
        services.AddSingleton<IScheduledJob, InventorySweepJob>();
        services.AddSingleton<IScheduledJob, TestJob>();
        services.AddSingleton<IScheduledJob, SalesBackorderScanJob>();   // Lote 66
        services.AddSingleton<IScheduledJob, InventoryAlertScanJob>();   // Lote 141
        services.AddSingleton<IScheduledJob, FleetAlertScanJob>();   // Lote 168
        services.AddSingleton<IScheduledJob, ExitNoticeBackorderScanJob>();   // Lote 143
        services.AddSingleton<IScheduledJob, BillingOverdueScanJob>();   // Lote 75
        services.AddSingleton<IScheduledJob, KpiSnapshotJob>();   // Lote 90
        services.AddSingleton<IScheduledJob, ReportDeliveryJob>();   // Lote 92
        services.AddSingleton<IScheduledJob, ClientNotificationDigestJob>();   // Lote 98
        services.AddSingleton<IScheduledJob, IntegrationRunDueJob>();   // Lote 132 (P11)
        services.Configure<IntegrationOptions>(config.GetSection(IntegrationOptions.Section));
        services.AddScoped<IntegrationNotifier>();
        services.AddSingleton<ApiRateLimiter>();   // Lote 135: API de los clientes
        services.AddScoped<ApiKeyService>();
        // Lote 136 (P11): webhooks de salida (firma HMAC, reintentos, bandeja de salidas; solo salen a direcciones públicas salvo AllowInsecure de desarrollo)
        services.Configure<WebhookOptions>(config.GetSection(WebhookOptions.Section));
        services.AddHttpClient(WebhookSender.ClientName, c => c.Timeout = TimeSpan.FromSeconds(Teikem.Domain.Integrations.WebhookRules.TimeoutSeconds + 5)).RemoveAllLoggers()
            .ConfigurePrimaryHttpMessageHandler(sp => WebhookSender.CreateHandler(sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<WebhookOptions>>().Value.AllowInsecure));
        services.AddScoped<WebhookSender>();
        services.AddScoped<WebhookRecorder>();
        services.AddScoped<WebhookService>();
        services.AddScoped<WebhookProcessor>();
        services.AddScoped<IStatusTransitionEffect, WebhookOrderEffect>();
        services.AddScoped<IStatusTransitionEffect, WebhookInvoiceEffect>();
        services.AddSingleton<IScheduledJob, WebhookDeliveryJob>();
        // Lote 137b (P11): integraciones de salida (SFTP/FTP y API saliente HTTP), con cola por estatus, reintentos y bandeja de salidas
        services.AddScoped<OutputRecorder>();
        services.AddScoped<OutputBuilder>();
        services.AddScoped<OutputSender>();
        services.AddScoped<IntegrationOutputService>();
        services.AddScoped<IStatusTransitionEffect, OutputOrderEffect>();
        services.AddSingleton<IScheduledJob, IntegrationOutputJob>();
        services.AddSingleton<Teikem.Infrastructure.Security.ISecretProtector, Teikem.Infrastructure.Security.SecretProtector>();   // Lote 133: secretos de conectores cifrados
        services.AddSingleton<Teikem.Infrastructure.Integrations.IMailboxReader, Teikem.Infrastructure.Integrations.ImapMailboxReader>();
        services.AddSingleton<Teikem.Infrastructure.Integrations.IPdfTextReader, Teikem.Infrastructure.Integrations.PdfTextReader>();   // 2026-10-22: PDF adjuntos (texto real y OCR)
        services.AddSingleton<Teikem.Infrastructure.Integrations.IRemoteFileClient, Teikem.Infrastructure.Integrations.RemoteFileClient>();   // Lote 134: SFTP/FTP
        services.AddScoped<IntegrationService>();
        services.AddScoped<IntegrationRunner>();
        services.AddScoped<IntegrationInboxService>();
        services.AddSingleton<IScheduledJob, ConsigneeNoticeDispatchJob>();   // Lote 116
        services.AddSingleton<ScheduledJobRunner>();
        services.AddHostedService<ScheduledJobWorker>();
        services.AddScoped<ScheduledJobService>();

        // Lote 51 — avisos: catálogo de eventos (cada módulo registra su proveedor), publicador (anota en la cola dentro de la transacción),
        // procesador + worker (cola con reclamo atómico y reintentos 1/5/30 min), canales (correo real; APP bandeja; SMS y push «no configurado»)
        // y los servicios de las reglas y de la bandeja.
        services.Configure<NotificationsOptions>(config.GetSection(NotificationsOptions.Section));
        services.AddSingleton<INotificationEventProvider, TestEventProvider>();
        services.AddSingleton<INotificationEventProvider, SalesEventProvider>();   // Lote 66
        services.AddSingleton<INotificationEventProvider, IntegrationEventProvider>();   // Lote 132 (P11)
        services.AddSingleton<INotificationEventProvider, InventoryEventProvider>();   // Lote 141
        services.AddSingleton<INotificationEventProvider, FleetEventProvider>();   // Lote 168
        services.AddSingleton<INotificationEventProvider, ExitNoticeEventProvider>();   // Lote 143
        services.AddSingleton<INotificationEventProvider, AsnEventProvider>();   // Lote 157: avisos de llegada
        services.AddSingleton<NotificationEventCatalog>();
        services.AddScoped<INotificationPublisher, NotificationPublisher>();
        services.AddScoped<INotificationChannelSender, EmailChannelSender>();
        services.AddScoped<INotificationChannelSender, AppChannelSender>();
        services.AddScoped<INotificationChannelSender, SmsChannelSender>();
        services.AddScoped<INotificationChannelSender, PushChannelSender>();
        // Lote 118: registrar la entrega desde la web; la ubicación se busca aparte con Google (un solo intento, 30 s)
        services.Configure<Teikem.Infrastructure.Geocoding.GoogleMapsOptions>(config.GetSection(Teikem.Infrastructure.Geocoding.GoogleMapsOptions.Section));
        services.AddHttpClient<Teikem.Infrastructure.Geocoding.GoogleGeocoder>(c => c.Timeout = TimeSpan.FromSeconds(40)).RemoveAllLoggers();   // el registro estándar del cliente HTTP escribiría la URL: la llave y la dirección del consignatario
        services.AddScoped<DeliveryRegistrationService>();
        services.AddScoped<PortalUserDirectoryService>();   // Lote 121: lista de todas las personas de portal y consignatarios de la compañía
        services.AddScoped<PortalProfileService>();      // Lote 120: mi perfil y los datos de mi empresa
        services.AddScoped<PortalIndicatorsService>();   // Lote 120: indicadores del cliente
        services.AddScoped<PortalNoticePrefService>();   // Lote 119: preferencias de aviso de cada persona del portal
        services.AddScoped<PortalInvoiceService>();   // Lote 119: facturas del cliente en su portal (solo lectura)
        services.AddSingleton<Teikem.Infrastructure.Geocoding.DeliveryGeocodeProcessor>();
        services.AddHostedService<Teikem.Infrastructure.Geocoding.DeliveryGeocodeWorker>();
        // 2026-10-22: ubicación de las paradas de las rutas (solo al armar la ruta, con tope mensual a nivel de aplicación)
        services.AddScoped<Teikem.Infrastructure.Geocoding.IAddressGeocoder>(sp => sp.GetRequiredService<Teikem.Infrastructure.Geocoding.GoogleGeocoder>());
        services.AddSingleton<Teikem.Infrastructure.Geocoding.StopGeocodeProcessor>();
        services.AddHostedService<Teikem.Infrastructure.Geocoding.StopGeocodeWorker>();
        services.AddSingleton<NotificationProcessor>();
        services.AddHostedService<NotificationWorker>();
        services.AddScoped<NotificationRuleService>();
        services.AddScoped<NotificationInboxService>();

        // Lote 53 — mensajería interna con hilos: servicio, presencia («visto por última vez») y el empuje en vivo (nulo aquí; el API pone SignalR).
        services.AddScoped<PresenceService>();
        services.AddScoped<MessagingService>();
        services.AddScoped<AnnouncementService>();   // Lote 54
        services.AddScoped<TaxService>();   // Lote 55
        services.AddScoped<SalesOrderService>();   // Lote 57
        services.AddScoped<ISalesDispatchReversal>(sp => sp.GetRequiredService<SalesOrderService>());   // Lote 62: deshacer una recolección de venta
        services.AddScoped<SalesDeliveryService>();   // Lote 62: entrega de una orden de venta
        services.AddSingleton<IMessageLiveNotifier, NullMessageLiveNotifier>();

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
        services.AddScoped<IOwnedEntityResolver, DeliveryProofOwnedEntityResolver>();   // Lote 127
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
        services.AddScoped<IDataSource, DamageDataSource>();   // 2026-10-08: daños
        services.AddScoped<IDataSource, WarehouseTaskDataSource>();
        services.AddScoped<IDataSource, PickBatchDataSource>();
        services.AddScoped<IDataSource, CycleCountDataSource>();   // Lote 7A (P2): indicador de Pulso 'Conteos con diferencia'
        // Lote 7A — "Actividad reciente": un proveedor de eventos por BusinessModule (colección, igual que IDataSource) y el feed.
        services.AddScoped<IActivityEventProvider, WarehouseActivityProvider>();
        services.AddScoped<IActivityEventProvider, OperationsActivityProvider>();   // Lote 91 (P12)
        services.AddScoped<IActivityEventProvider, BillingActivityProvider>();
        services.AddScoped<IActivityEventProvider, CodActivityProvider>();
        services.AddScoped<ActivityFeedService>();
        // Lote 14 (D6) — "Necesita tu atención": un proveedor de filas por tipo de aviso (colección) y el servicio del panel.
        services.AddScoped<IAttentionItemProvider, InventoryDiscrepancyAttentionProvider>();
        services.AddScoped<AttentionFeedService>();
        services.AddScoped<IOwnedEntityResolver, WarehouseOwnedEntityResolver>();
        services.AddScoped<IOwnedEntityResolver, WarehouseDockOwnedEntityResolver>();
        services.AddScoped<IOwnedEntityResolver, WarehouseZoneOwnedEntityResolver>();
        services.AddScoped<IOwnedEntityResolver, WarehouseBinOwnedEntityResolver>();
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
        services.AddScoped<IOwnedEntityResolver>(_ => new ClosedOwnedEntityResolver(Domain.Constants.EntityTypes.MultiAdjustment));   // 2026-10-12: ajuste múltiple (solo auditoría)
        // Lote 8A: USER_DEVICE (aparato de almacén) existe solo para auditoría; sin campos personalizados → siempre 404.
        services.AddScoped<IOwnedEntityResolver>(_ => new ClosedOwnedEntityResolver(Domain.Constants.EntityTypes.UserDevice));
        // Lote 14: descuadres Kárdex ↔ saldo (fuente con DateField DetectedAtUtc); sin escritura de dueño → resolver cerrado.
        services.AddScoped<IDataSource, InventoryDiscrepancyDataSource>();
        services.AddScoped<IOwnedEntityResolver>(_ => new ClosedOwnedEntityResolver(Domain.Constants.EntityTypes.InventoryDiscrepancy));
        // Lote 29 (Rentas R3): fuentes RENTAL (DateField StartDate), RENTAL_RETURN (ReturnedOn) y RENTAL_PROCESS (StartedAtUtc), solo
        // con el módulo Rentas encendido (IDataSource.TenantModule), y el aviso de rentas vencidas o por vencer en "Necesita tu atención".
        services.AddScoped<IDataSource, RentalDataSource>();
        services.AddScoped<IDataSource, RentalReturnDataSource>();
        services.AddScoped<KitService>();   // Lote 68
        services.AddScoped<BillingService>();   // Lote 72 (P5)
        services.AddScoped<PaymentService>();   // Lote 75 (P5)
        services.AddScoped<ExportTemplateService>();   // Lote 77 (P5)
        services.AddScoped<CreditNoteService>();   // Lote 79 (P6)
        services.AddScoped<SalesReturnService>();   // Lote 81 (P6)
        services.AddScoped<CodService>();   // Lote 84 (P8)
        services.AddScoped<CodRemittanceService>();   // Lote 86 (P8)
        services.AddScoped<PulseRiverService>();   // Lote 88 (P12)
        services.AddScoped<PulseRadarService>();   // Lote 89 (P12)
        services.AddScoped<KpiHistory>();   // Lote 90 (P12)
        services.AddScoped<KpiSnapshotService>();
        services.AddScoped<ReportDeliveryService>();   // Lote 92 (P12)
        services.AddScoped<ClientNotificationService>();   // Lote 98 (P10)
        services.AddScoped<ClientMessagingService>();   // Lote 104: mensajes del portal con el equipo de servicio al cliente
        services.AddScoped<OrderProgressService>();   // Lote 103: barra de progreso (etapas = estatus de la compañía)
        services.AddScoped<PublicTrackingService>();   // Lote 103/116: «¿Cómo va mi orden?» público por enlace
        services.AddScoped<PortalOrderEntryService>();   // Lote 105: entrada de órdenes del cliente
        services.AddScoped<PortalWarehouseService>();   // Lote 153: portal del cliente de almacén (lectura)
        services.AddScoped<PortalService>();   // Lote 101: lecturas del portal del cliente
        services.AddHttpClient<ITurnstileVerifier, CloudflareTurnstileVerifier>(c => c.Timeout = TimeSpan.FromSeconds(8));   // Lote 101: reto anti-robot de los portales
        services.AddScoped<TrustedBrowserService>();   // Lote 100: computadoras de confianza
        services.AddScoped<Teikem.Infrastructure.Legal.LegalService>();   // Lote 99: documento legal único y su aceptación
        services.AddScoped<IStatusTransitionEffect, ClientNotificationOrderEffect>();
        services.AddScoped<ConsigneeNoticeService>();   // Lote 116: avisos al consignatario
        services.AddScoped<ConsigneeAccessService>();   // Lote 117: el cliente le da acceso al consignatario
        services.AddScoped<ConsigneeMessagingService>();   // Lote 117b: mensajes del consignatario con su cliente
        services.AddScoped<ConsigneeOrdersService>();      // Lote 117d: los envíos del consignatario (solo ve)
        services.AddScoped<ConsigneeSelfService>();     // Lote 117: el espacio del consignatario (perfil y bandeja)
        services.AddScoped<IStatusTransitionEffect, ConsigneeNoticeOrderEffect>();
        services.AddScoped<IStatusTransitionEffect, ClientNotificationInvoiceEffect>();
        services.AddScoped<IStatusTransitionEffect, ClientNotificationRemittanceEffect>();
        services.AddScoped<IDataSource, KpiDailyDataSource>();
        services.AddScoped<IDataSource, CodCollectionDataSource>();   // Lote 91 (P12)
        services.AddScoped<IDataSource, InvoiceDataSource>();   // historial por día para indicadores y gráficos
        services.AddScoped<SlaService>();   // Lote 122: SLA de las órdenes (nivel de servicio del contrato)
        services.AddScoped<IAttentionItemProvider, SlaAtRiskAttentionProvider>();   // Lote 122: SLA en riesgo
        services.AddScoped<IAttentionItemProvider, OrdersFailedAttentionProvider>();   // Lote 125: alertas operacionales
        services.AddScoped<IAttentionItemProvider, ClaimsOpenAttentionProvider>();
        services.AddScoped<IAttentionItemProvider, FleetDocumentsDueAttentionProvider>();
        services.AddScoped<IAttentionItemProvider, InvoicesOverdueAttentionProvider>();
        services.AddScoped<IAttentionItemProvider, OrdersNoDriverAttentionProvider>();   // Lote 89
        services.AddScoped<IAttentionItemProvider, CodUnreconciledAttentionProvider>();   // Lote 89
        services.AddScoped<BillingExportService>();
        services.AddScoped<IDataSource, SalesOrderDataSource>();        // Lote 67: órdenes de venta
        services.AddScoped<IDataSource, ExitNoticeDataSource>();        // menores 2026-10-21: avisos de salida como fuente de Vistas
        services.AddScoped<IDataSource, SalesOrderLineDataSource>();
        services.AddScoped<IDataSource, RentalProcessDataSource>();
        services.AddScoped<IAttentionItemProvider, RentalDueAttentionProvider>();
        services.AddScoped<IAttentionItemProvider, SalesBackorderReadyAttentionProvider>();   // Lote 67
        services.AddScoped<IAttentionItemProvider, ExitNoticeBackorderAttentionProvider>();   // Lote 194: avisos de salida con pendiente

        // Seeders e inicialización
        services.AddScoped<PermissionSeeder>();
        services.AddScoped<SystemAnalyticsSeeder>();
        services.AddScoped<DemoTenantSeeder>();
        services.AddSingleton<DatabaseInitializer>();
        // Lote 10 — migración de datos heredados (QuickBooks + WMS MSWM): comando CLI import-legacy, fuera del pipeline HTTP.
        services.AddScoped<LegacyImportService>();
        // Migración de Colavora Courier (2026-10-18): aparte del importador de almacén.
        services.AddScoped<Migration.Colavora.ColavoraImportService>();
        services.AddSingleton<Migration.Colavora.ColavoraImportRunner>();
        services.AddSingleton<LegacyImportRunner>();
        return services;
    }
}
