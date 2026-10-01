using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Teikem.Api.Auth;
using Teikem.Api.Middleware;
using Teikem.Infrastructure;
using Teikem.Infrastructure.Migration;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Persistence.Scripts;
using Teikem.Infrastructure.Services;

var builder = WebApplication.CreateBuilder(args);

// Lote 10: appsettings.{Entorno}.local.json (en .gitignore, opcional) sobreescribe valores de la máquina, como la cadena
// ConnectionStrings:LegacyMswm del importador. Se inserta justo después de los appsettings versionados para que las variables
// de entorno y la línea de comandos sigan teniendo prioridad.
{
    var sources = builder.Configuration.Sources;
    var lastJson = sources.OfType<Microsoft.Extensions.Configuration.Json.JsonConfigurationSource>().LastOrDefault();
    var localJson = new Microsoft.Extensions.Configuration.Json.JsonConfigurationSource
    {
        Path = $"appsettings.{builder.Environment.EnvironmentName}.local.json",
        Optional = true,
        ReloadOnChange = false,
        FileProvider = builder.Environment.ContentRootFileProvider,
    };
    sources.Insert(lastJson is null ? sources.Count : sources.IndexOf(lastJson) + 1, localJson);
}

builder.Services.AddTeikemInfrastructure(builder.Configuration);

builder.Services.AddControllers().AddJsonOptions(o =>
{
    o.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(o =>
{
    o.SwaggerDoc("v1", new OpenApiInfo { Title = "Teikem API", Version = "v1", Description = "Plataforma de logística multi-tenant — Lotes 1 a 6." });
    o.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme { Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT", In = ParameterLocation.Header, Name = "Authorization" });
    o.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        { new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } }, Array.Empty<string>() }
    });
});
builder.Services.AddHttpContextAccessor();
// Lote 8A: limitador de intentos de los endpoints anónimos del aparato (enroll, device/users, device/login, heartbeat).
builder.Services.AddDeviceRateLimiting(builder.Configuration);
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .WithOrigins(builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? Array.Empty<string>())
    .AllowAnyHeader().AllowAnyMethod().WithExposedHeaders(TenantContextMiddleware.CorrelationHeader, Teikem.Domain.Security.IdempotencyRules.ReplayedHeader)));

// --- Autenticación JWT (access tokens cortos; el SecurityStamp invalida tokens vivos) ---
var jwtKey = builder.Configuration["Jwt:SigningKey"] ?? throw new InvalidOperationException("Falta Jwt:SigningKey.");
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
{
    o.MapInboundClaims = false;
    o.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true, ValidIssuer = builder.Configuration["Jwt:Issuer"],
        ValidateAudience = true, ValidAudience = builder.Configuration["Jwt:Audience"],
        ValidateIssuerSigningKey = true, IssuerSigningKey = new SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(jwtKey)),
        ValidateLifetime = true, ClockSkew = TimeSpan.FromSeconds(30),
    };
    o.Events = new JwtBearerEvents
    {
        OnTokenValidated = async ctx =>
        {
            // El SecurityStamp de Identity invalida access tokens vivos (logout global, cambio de contraseña, desactivación).
            var sub = ctx.Principal?.FindFirst(TeikemClaims.Subject)?.Value;
            var ss = ctx.Principal?.FindFirst(TeikemClaims.SecurityStamp)?.Value;
            if (!int.TryParse(sub, out var userId) || ss is null) { ctx.Fail("token inválido"); return; }
            var cache = ctx.HttpContext.RequestServices.GetRequiredService<IMemoryCache>();
            var current = await cache.GetOrCreateAsync(JwtTokenService.StampCacheKey(userId), async e =>
            {
                e.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(60);
                var db = ctx.HttpContext.RequestServices.GetRequiredService<TeikemDbContext>();
                var u = await db.Users.AsNoTracking().Where(x => x.Id == userId).Select(x => new { x.SecurityStamp, x.IsActive }).FirstOrDefaultAsync();
                return u is null || !u.IsActive ? null : JwtTokenService.StampHash(u.SecurityStamp);
            });
            if (current is null || current != ss) { ctx.Fail("sesión invalidada"); return; }
            // Lote 8A: un token de aparato (claim `did`) deja de servir en cuanto el aparato se desactiva, y los emitidos antes
            // del sello de sesiones del aparato (baja, reactivación o registro) no reviven al reactivarlo (caché de 60 s que
            // DeviceService borra en esos tres casos; en otras instancias el retraso máximo es ese TTL).
            if (ctx.Principal?.FindFirst(DeviceClaims.DeviceId)?.Value is string didRaw)
            {
                if (!Guid.TryParse(didRaw, out var did)) { ctx.Fail("token inválido"); return; }
                var device = await cache.GetOrCreateAsync(DeviceClaims.ActiveCacheKey(did), async e =>
                {
                    e.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(60);
                    var db = ctx.HttpContext.RequestServices.GetRequiredService<TeikemDbContext>();
                    return await db.Set<Teikem.Domain.Entities.UserDevice>().IgnoreQueryFilters().AsNoTracking()
                        .Where(d => d.PublicId == did).Select(d => new DeviceTokenState(d.IsActive, d.SessionsNotBeforeUtc)).FirstOrDefaultAsync();
                });
                if (device is not { IsActive: true }) { ctx.Fail("aparato desactivado"); return; }
                long? iat = long.TryParse(ctx.Principal.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Iat)?.Value,
                    System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var iatValue) ? iatValue : null;
                if (DeviceClaims.IssuedBeforeSessionsCutoff(iat, device.SessionsNotBeforeUtc)) ctx.Fail("sesión del aparato cerrada");
            }
        },
    };
});

builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
builder.Services.AddScoped<IAuthorizationHandler, PermissionHandler>();
builder.Services.AddSingleton<IAuthorizationHandler, AccessTokenHandler>();
builder.Services.AddSingleton<IAuthorizationHandler, MfaChallengeHandler>();
builder.Services.AddAuthorization(o =>
{
    o.DefaultPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().AddRequirements(new AccessTokenRequirement()).Build();
    o.AddPolicy(Policies.MfaChallenge, p => p.RequireAuthenticatedUser().AddRequirements(new MfaChallengeRequirement()));
    o.AddPolicy(Policies.AccessOrMfa, p => p.RequireAuthenticatedUser());
    o.AddPolicy(Policies.PlatformAdmin, p => p.RequireAuthenticatedUser().AddRequirements(new AccessTokenRequirement()).RequireClaim(TeikemClaims.PlatformAdmin, "1"));
});

var app = builder.Build();

// --- Modo CLI: `dotnet run -- db-init` crea la BD, corre los dos scripts SQL de Diseño/ y los seeders ---
if (args.Contains("db-init", StringComparer.OrdinalIgnoreCase))
{
    await app.Services.GetRequiredService<DatabaseInitializer>().RunAsync();
    return;
}
// --- Modo CLI (Lote 10): `dotnet run -- db-reset --yes [--allow-remote]` borra la base de ConnectionStrings:Teikem y la vuelve a
// inicializar como db-init sobre servidor limpio (para repetir la migración desde cero). Nunca toca MSWM*. ---
if (args.Contains(DbResetRules.Verb, StringComparer.OrdinalIgnoreCase))
{
    var teikemCs = app.Configuration.GetConnectionString("Teikem");
    var csb = string.IsNullOrWhiteSpace(teikemCs) ? null : new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(teikemCs);
    var decision = DbResetRules.Decide(args, csb?.DataSource, csb?.InitialCatalog);
    if (!decision.Ok)
    {
        Console.Error.WriteLine(decision.Message);
        Environment.ExitCode = decision.ExitCode;
        return;
    }
    Console.WriteLine($"db-reset: borrando y reinicializando la base '{csb!.InitialCatalog}' en '{csb.DataSource}'.");
    await app.Services.GetRequiredService<DatabaseInitializer>().ResetAsync();
    return;
}

// --- Modo CLI (Lote 10): `dotnet run -- import-legacy <config.json> [--dry-run] [--update]` migra QuickBooks + WMS MSWM a una compañía ---
if (args.Contains(LegacyImportRunner.Verb, StringComparer.OrdinalIgnoreCase))
{
    Environment.ExitCode = await app.Services.GetRequiredService<LegacyImportRunner>().RunAsync(args);
    return;
}
if (app.Configuration.GetValue<bool>("Database:InitOnStartup"))
    await app.Services.GetRequiredService<DatabaseInitializer>().RunAsync();

app.UseMiddleware<ExceptionHandlingMiddleware>();
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseMiddleware<TenantContextMiddleware>();
app.UseAuthorization();
// Lote 8A: Idempotency-Key en escrituras autenticadas (después de autenticación, TenantContext y autorización).
app.UseMiddleware<IdempotencyMiddleware>();
app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "ok", utc = DateTime.UtcNow })).AllowAnonymous();

app.Run();

namespace Teikem.Api.Auth
{
    public static class Policies
    {
        public const string MfaChallenge = "MfaChallenge";
        public const string AccessOrMfa = "AccessOrMfa";
        public const string PlatformAdmin = "PlatformAdmin";
    }
}
