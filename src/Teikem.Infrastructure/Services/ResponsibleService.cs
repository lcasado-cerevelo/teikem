using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Constants;
using Teikem.Domain.Messaging;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 212 — responsables de una entidad (hoy una ruta o una zona de despacho). Cualquiera con mensajería los puede ver (alimentan las sugerencias de «Hablar sobre esta orden»);
/// cambiarlos exige el permiso de planear la ruta (trips.plan) o de administrar la flota (fleet.manage) según la entidad. Varias personas por entidad; solo usuarios activos de la compañía.
/// </summary>
public sealed class ResponsibleService(TeikemDbContext db, ITenantContext tenant, ILookupCache lookups, PermissionService permissions)
{
    private TenantContext T => (TenantContext)tenant;

    public const string InvalidEntityMessage = "La entidad debe ser TRIP (ruta) o DISPATCH_ZONE (zona de despacho).";
    public const string InvalidKeyMessage = "La clave de la entidad no es válida.";
    public const string UserNotFoundMessage = "Usuario no encontrado en esta compañía.";

    public static readonly string[] Entities = { EntityTypes.Trip, EntityTypes.DispatchZone };

    private static string? NormalizeEntity(string? e)
    {
        var x = (e ?? string.Empty).Trim().ToUpperInvariant();
        return Entities.Contains(x) ? x : null;
    }

    /// <summary>La entidad por su clave (PublicId de la ruta / id de la zona) → su id interno. 404 si no es de esta compañía.</summary>
    private async Task<(string Entity, int EntityId, string WritePermission)> ResolveAsync(string? entity, string? key, CancellationToken ct)
    {
        var e = NormalizeEntity(entity) ?? throw new ValidationException("entity", InvalidEntityMessage);
        if (e == EntityTypes.Trip)
        {
            if (!Guid.TryParse(key, out var g)) throw new ValidationException("key", InvalidKeyMessage);
            var id = await db.Trips.AsNoTracking().Where(t => t.PublicId == g).Select(t => (int?)t.TripId).FirstOrDefaultAsync(ct) ?? throw new NotFoundException("Ruta", null, feminine: true);
            return (e, id, PermissionCatalog.TripsPlan);
        }
        if (!int.TryParse(key, out var zoneId)) throw new ValidationException("key", InvalidKeyMessage);
        if (!await db.DispatchZones.AsNoTracking().AnyAsync(z => z.DispatchZoneId == zoneId, ct)) throw new NotFoundException("Zona de despacho", null, feminine: true);
        return (e, zoneId, PermissionCatalog.FleetManage);
    }

    public async Task<IReadOnlyList<ResponsibleDto>> ListAsync(string? entity, string? key, CancellationToken ct)
    {
        var (e, id, _) = await ResolveAsync(entity, key, ct);
        return await ListByIdAsync(e, id, ct);
    }

    /// <summary>Los responsables activos de una entidad ya resuelta (también lo usan las sugerencias).</summary>
    public async Task<IReadOnlyList<ResponsibleDto>> ListByIdAsync(string entity, int entityId, CancellationToken ct)
    {
        var lookupId = await lookups.GetIdAsync(LookupDomains.EntityType, entity, ct);
        var rows = await (from r in db.EntityResponsibles.AsNoTracking()
                          where r.EntityLookupId == lookupId && r.EntityId == entityId && r.IsActive
                          join u in db.Users.AsNoTracking() on r.UserId equals u.Id
                          where u.IsActive
                          orderby u.FullName ?? u.Email
                          select new { u.Id, u.FullName, u.Email }).ToListAsync(ct);
        return rows.Select(u => new ResponsibleDto(u.Id, string.IsNullOrWhiteSpace(u.FullName) ? u.Email ?? $"#{u.Id}" : u.FullName, u.Email ?? string.Empty)).ToList();
    }

    /// <summary>Deja exactamente a estas personas como responsables (lista vacía = nadie).</summary>
    public async Task<IReadOnlyList<ResponsibleDto>> ReplaceAsync(ResponsiblesRequest req, CancellationToken ct)
    {
        var tenantId = T.RequireTenantId();
        var (e, id, perm) = await ResolveAsync(req.Entity, req.Key, ct);
        if (!await permissions.HasPermissionAsync(perm, ct)) throw new ForbiddenException($"No tiene permiso para cambiar los responsables ({perm}).");
        var wanted = (req.UserIds ?? Array.Empty<int>()).Distinct().ToList();
        if (wanted.Count > 0)
        {
            var valid = await (from m in db.UserTenants.AsNoTracking()
                               join u in db.Users.AsNoTracking() on m.UserId equals u.Id
                               where wanted.Contains(u.Id) && u.IsActive
                               select u.Id).CountAsync(ct);
            if (valid != wanted.Count) throw new ValidationException("userIds", UserNotFoundMessage);
        }
        var lookupId = await lookups.GetIdAsync(LookupDomains.EntityType, e, ct);
        var rows = await db.EntityResponsibles.Where(r => r.EntityLookupId == lookupId && r.EntityId == id).ToListAsync(ct);
        foreach (var r in rows) r.IsActive = wanted.Contains(r.UserId);
        foreach (var userId in wanted.Where(w => rows.All(r => r.UserId != w)))
            db.EntityResponsibles.Add(new EntityResponsible { TenantId = tenantId, EntityLookupId = lookupId, EntityId = id, UserId = userId, CreatedBy = T.UserId });
        await db.SaveChangesAsync(ct);
        return await ListByIdAsync(e, id, ct);
    }

    /// <summary>Suma a alguien como responsable si todavía no lo es (la ruta nueva queda con quien la creó). No guarda: lo hace quien llama.</summary>
    public async Task AddAsync(string entity, int entityId, int userId, CancellationToken ct)
    {
        var lookupId = await lookups.GetIdAsync(LookupDomains.EntityType, entity, ct);
        var row = await db.EntityResponsibles.FirstOrDefaultAsync(r => r.EntityLookupId == lookupId && r.EntityId == entityId && r.UserId == userId, ct);
        if (row is null) db.EntityResponsibles.Add(new EntityResponsible { TenantId = T.RequireTenantId(), EntityLookupId = lookupId, EntityId = entityId, UserId = userId, CreatedBy = userId });
        else row.IsActive = true;
    }
}
