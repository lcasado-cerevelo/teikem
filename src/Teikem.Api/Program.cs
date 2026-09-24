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
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Persistence.Scripts;
using Teikem.Infrastructure.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddTeikemInfrastructure(builder.Configuration);

builder.Services.AddControllers().AddJsonOptions(o =>
{
    o.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(o =>
{
    o.SwaggerDoc("v1", new OpenApiInfo { Title = "Teikem API", Version = "v1", Description = "Plataforma de logística multi-tenant — Lote 1: capas transversales A-I + módulo 0B." });
    o.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme { Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT", In = ParameterLocation.Header, Name = "Authorization" });
    o.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        { new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } }, Array.Empty<string>() }
    });
});
builder.Services.AddHttpContextAccessor();
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .WithOrigins(builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? Array.Empty<string>())
    .AllowAnyHeader().AllowAnyMethod().WithExposedHeaders(TenantContextMiddleware.CorrelationHeader)));

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
            var current = await cache.GetOrCreateAsync($"stamp:{userId}", async e =>
            {
                e.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(60);
                var db = ctx.HttpContext.RequestServices.GetRequiredService<TeikemDbContext>();
                var u = await db.Users.AsNoTracking().Where(x => x.Id == userId).Select(x => new { x.SecurityStamp, x.IsActive }).FirstOrDefaultAsync();
                return u is null || !u.IsActive ? null : JwtTokenService.StampHash(u.SecurityStamp);
            });
            if (current is null || current != ss) ctx.Fail("sesión invalidada");
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
if (app.Configuration.GetValue<bool>("Database:InitOnStartup"))
    await app.Services.GetRequiredService<DatabaseInitializer>().RunAsync();

app.UseMiddleware<ExceptionHandlingMiddleware>();
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
app.UseCors();
app.UseAuthentication();
app.UseMiddleware<TenantContextMiddleware>();
app.UseAuthorization();
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
