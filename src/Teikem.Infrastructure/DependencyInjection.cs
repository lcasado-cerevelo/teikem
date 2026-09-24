using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Identity;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Analytics;
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

        // Registro de fuentes de datos (cada lote agrega las suyas) y resolvers de pertenencia
        services.AddScoped<IDataSourceRegistry, DataSourceRegistry>();
        services.AddScoped<IDataSource, AuditLogDataSource>();
        services.AddScoped<IDataSource, SecurityEventDataSource>();
        services.AddScoped<IDataSource, UserDataSource>();
        services.AddScoped<IOwnedEntityResolver, UserOwnedEntityResolver>();

        // Seeders e inicialización
        services.AddScoped<PermissionSeeder>();
        services.AddScoped<SystemAnalyticsSeeder>();
        services.AddScoped<DemoTenantSeeder>();
        services.AddSingleton<DatabaseInitializer>();
        return services;
    }
}
