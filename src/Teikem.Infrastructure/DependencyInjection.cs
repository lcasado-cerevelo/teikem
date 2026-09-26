using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Identity;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Analytics;
using Teikem.Infrastructure.Fleet;
using Teikem.Infrastructure.Orders;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Persistence.Interceptors;
using Teikem.Infrastructure.Persistence.Scripts;
using Teikem.Infrastructure.Seeding;
using Teikem.Infrastructure.Services;

namespace Teikem.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddTeikemInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        services.AddMemoryCache();
        services.AddDataProtection();

        // Contexto de tenant (scoped, lo llena el middleware del API)
        services.AddScoped<TenantContext>();
        services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());

        services.AddSingleton<ILookupCache, LookupCache>();
        services.AddScoped<ISecurityEventWriter, SecurityEventWriter>();
        services.AddScoped<AuditSaveChangesInterceptor>();

        services.AddDbContext<TeikemDbContext>((sp, options) =>
        {
            options.UseSqlServer(config.GetConnectionString("Teikem"), sql =>
            {
                sql.EnableRetryOnFailure(3);
                sql.CommandTimeout(60);
            });
            options.AddInterceptors(sp.GetRequiredService<AuditSaveChangesInterceptor>());
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
        services.AddSingleton<JwtTokenService>();

        // Servicios transversales
        services.AddScoped<LookupService>();
        services.AddScoped<StatusService>();
        services.AddScoped<ContactPointService>();
        services.AddScoped<PermissionService>();
        services.AddScoped<AuditQueryService>();
        services.AddScoped<CustomFieldService>();
        services.AddScoped<AnalyticsService>();
        services.AddScoped<AnalyticsEngine>();
        services.AddScoped<ModuleService>();
        services.AddScoped<TenantService>();
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

        // Seeders e inicialización
        services.AddScoped<PermissionSeeder>();
        services.AddScoped<SystemAnalyticsSeeder>();
        services.AddScoped<DemoTenantSeeder>();
        services.AddSingleton<DatabaseInitializer>();
        return services;
    }
}
