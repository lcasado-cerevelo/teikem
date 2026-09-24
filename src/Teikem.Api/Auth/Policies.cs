using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Services;

namespace Teikem.Api.Auth;

/// <summary>[RequirePermission("trips.dispatch")] → policy "perm:trips.dispatch" resuelta por PermissionPolicyProvider.</summary>
public sealed class RequirePermissionAttribute : AuthorizeAttribute
{
    public const string Prefix = "perm:";
    public RequirePermissionAttribute(string permission) => Policy = Prefix + permission;
}

public sealed class PermissionRequirement(string permission) : IAuthorizationRequirement
{
    public string Permission { get; } = permission;
}

/// <summary>Verifica que el UserRole del tenant activo (∪ permisos extra) incluya el permiso; registra PERMISSION_DENIED si no.</summary>
public sealed class PermissionHandler(PermissionService permissions, ITenantContext tenant, ISecurityEventWriter security) : AuthorizationHandler<PermissionRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        if (!tenant.IsAuthenticated) return;
        if (await permissions.HasPermissionAsync(requirement.Permission, CancellationToken.None)) { context.Succeed(requirement); return; }
        await security.WriteAsync(SecurityEventTypes.PermissionDenied, SecurityOutcomes.Blocked, tenant.UserId, tenant.TenantId, new { permission = requirement.Permission });
    }
}

/// <summary>El token debe ser de acceso (no un challenge MFA). Política por defecto de todo endpoint autenticado.</summary>
public sealed class AccessTokenRequirement : IAuthorizationRequirement { }

public sealed class AccessTokenHandler : AuthorizationHandler<AccessTokenRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, AccessTokenRequirement requirement)
    {
        var purpose = context.User.FindFirst(TeikemClaims.Purpose)?.Value;
        if (context.User.Identity?.IsAuthenticated == true && (purpose is null || purpose == TeikemClaims.PurposeAccess)) context.Succeed(requirement);
        return Task.CompletedTask;
    }
}

/// <summary>Token de challenge MFA (segundo paso del login / enrolamiento obligatorio).</summary>
public sealed class MfaChallengeRequirement : IAuthorizationRequirement { }

public sealed class MfaChallengeHandler : AuthorizationHandler<MfaChallengeRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, MfaChallengeRequirement requirement)
    {
        if (context.User.FindFirst(TeikemClaims.Purpose)?.Value == TeikemClaims.PurposeMfa) context.Succeed(requirement);
        return Task.CompletedTask;
    }
}

/// <summary>Resuelve dinámicamente las policies "perm:*"; el resto va al provider por defecto.</summary>
public sealed class PermissionPolicyProvider(Microsoft.Extensions.Options.IOptions<AuthorizationOptions> options) : DefaultAuthorizationPolicyProvider(options)
{
    public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (policyName.StartsWith(RequirePermissionAttribute.Prefix, StringComparison.OrdinalIgnoreCase))
            return new AuthorizationPolicyBuilder().RequireAuthenticatedUser()
                .AddRequirements(new AccessTokenRequirement(), new PermissionRequirement(policyName[RequirePermissionAttribute.Prefix.Length..])).Build();
        return await base.GetPolicyAsync(policyName);
    }
}

/// <summary>[RequireModule("COD")]: el endpoint solo existe si el tenant tiene el módulo encendido (ausencia de fila = apagado).</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class RequireModuleAttribute(string moduleKey) : Attribute, IAsyncActionFilter
{
    public string ModuleKey { get; } = moduleKey;
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var modules = context.HttpContext.RequestServices.GetRequiredService<ModuleService>();
        await modules.EnsureEnabledAsync(ModuleKey, context.HttpContext.RequestAborted);
        await next();
    }
}

/// <summary>[RequireAal2]: acción sensible; exige reauth reciente dentro de la ventana del tenant (Tenant.Aal2WindowMinutes).</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class RequireAal2Attribute : Attribute, IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var tenant = context.HttpContext.RequestServices.GetRequiredService<ITenantContext>();
        var db = context.HttpContext.RequestServices.GetRequiredService<TeikemDbContext>();
        var window = tenant.TenantId is null ? 30 : await db.Tenants.AsNoTracking().IgnoreQueryFilters().Where(t => t.TenantId == tenant.TenantId).Select(t => t.Aal2WindowMinutes).FirstOrDefaultAsync(context.HttpContext.RequestAborted);
        if (window <= 0) window = 30;
        if (tenant.Aal2VerifiedAtUtc is null || tenant.Aal2VerifiedAtUtc.Value.AddMinutes(window) < DateTime.UtcNow)
            throw new StepUpRequiredException();
        await next();
    }
}
