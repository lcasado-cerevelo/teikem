using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Identity;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Analytics;
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

        // Seeders e inicialización
        services.AddScoped<PermissionSeeder>();
        services.AddScoped<SystemAnalyticsSeeder>();
        services.AddScoped<DemoTenantSeeder>();
        services.AddSingleton<DatabaseInitializer>();
        return services;
    }
}
