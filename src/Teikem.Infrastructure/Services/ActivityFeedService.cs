using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 7A (P1) — feed "Actividad reciente" del Pulso (GET /api/v1/analytics/activity). No hay tabla de eventos: cada
/// proveedor (IActivityEventProvider, uno por BusinessModule) calcula sus eventos al leer, en una ventana corta.
/// - Módulos visibles = proveedores cuyo permiso tiene el usuario y cuyo módulo del tenant está encendido (Almacén:
///   inventory.view + WMS_LOTSERIAL), en el orden de las pestañas (Almacén, Operación, Contabilidad).
/// - module pedido y no visible → 403 'No tiene permiso para ver la actividad del módulo {module}.' (y PERMISSION_DENIED);
///   sin module → el primer visible; ninguno visible → página vacía.
/// - Une, filtra 'solo obligatorios', ordena por fecha descendente y pagina (máximo 50: 400 'El máximo por página es 50.').
/// No es la auditoría: AuditLog sigue exigiendo admin.audit; aquí solo se exponen eventos de negocio.
/// </summary>
public sealed class ActivityFeedService(
    IEnumerable<IActivityEventProvider> providers,
    TeikemDbContext db,
    ITenantContext tenant,
    PermissionService permissions,
    ModuleService modules,
    ISecurityEventWriter security,
    ITenantClock? clock = null)
{
    /// <summary>Lote 15: la ventana 'today' empieza a la medianoche LOCAL de la compañía (hora de Puerto Rico, ITenantClock).</summary>
    private readonly ITenantClock _clock = clock ?? TenantClock.Default;

    public async Task<ActivityPageDto> GetAsync(ActivityQuery query, CancellationToken ct)
    {
        query ??= new ActivityQuery();
        var (skip, take) = ActivityRules.Page(query.Skip, query.Take);
        var fromUtc = ActivityRules.FromUtc(query.Window, _clock.UtcNow, _clock.Zone);
        var tenantId = tenant.TenantId ?? throw new ForbiddenException("No hay tenant activo en la sesión.");

        var enabled = await modules.GetEnabledKeysAsync(tenantId, ct);
        var visible = new List<IActivityEventProvider>();
        foreach (var p in providers.OrderBy(p => ActivityRules.ModuleRank(p.Module)).ThenBy(p => p.Module, StringComparer.Ordinal))
        {
            if (ActivityRules.TenantModuleFor(p.Module) is string key && !enabled.Contains(key)) continue;
            if (!await permissions.HasPermissionAsync(p.RequiredPermission, ct)) continue;
            visible.Add(p);
        }
        var visibleModules = visible.Select(p => p.Module.ToUpperInvariant()).Distinct().ToList();

        string? module;
        try
        {
            module = ActivityRules.ResolveModule(query.Module, visibleModules);
        }
        catch (ForbiddenException)
        {
            await security.WriteAsync(SecurityEventTypes.PermissionDenied, SecurityOutcomes.Blocked, tenant.UserId, tenantId,
                new { activityModule = query.Module }, ct);
            throw;
        }
        if (module is null) return new ActivityPageDto(0, visibleModules, Array.Empty<ActivityEventDto>());

        var scope = new ActivityScope(tenantId, tenant.UserId, tenant.Lang, enabled);
        var events = new List<ActivityEventDto>();
        foreach (var p in visible.Where(p => string.Equals(p.Module, module, StringComparison.OrdinalIgnoreCase)))
            events.AddRange(await p.ReadAsync(scope, fromUtc, query.OnlyMandatory, ct));

        var filtered = query.OnlyMandatory ? events.Where(e => e.Mandatory) : events;
        var (total, page) = ActivityRules.Paginate(filtered, skip, take);
        var names = await UserNamesAsync(page.Select(e => e.UserId), ct);
        var items = page.Select(e => e.UserId is int uid ? e with { UserName = names.GetValueOrDefault(uid) } : e).ToList();
        return new ActivityPageDto(total, visibleModules, items);
    }

    /// <summary>Nombre visible de quien hizo el evento: nombre completo o correo (igual que la bitácora).</summary>
    private async Task<Dictionary<int, string?>> UserNamesAsync(IEnumerable<int?> ids, CancellationToken ct)
    {
        var list = ids.Where(i => i.HasValue).Select(i => i!.Value).Distinct().ToList();
        if (list.Count == 0) return new Dictionary<int, string?>();
        return await db.Users.AsNoTracking().Where(u => list.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName ?? u.Email, ct);
    }
}
