using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Catalogs;
using Teikem.Domain.Constants;
using Teikem.Domain.Identity;
using Teikem.Domain.Sales;
using Teikem.Domain.Security;

namespace Teikem.Tests;

/// <summary>
/// Lote 244 (Lote A del plan de rutas) — ayudante de pruebas de las rutas de venta sobre <see cref="WmsFixture"/> (datos INVENTADOS): personas de la compañía con su
/// membresía (activa, suspendida o invitada; el estatus MembershipStatus se siembra aquí porque WmsFixture no lo trae), usuarios del portal (sin UserTenant), rutas,
/// representantes, asignaciones de titular y apoyo y la ruta / representante / vendedor preferido de un cliente. Lo comparten las pruebas del lote.
/// Cada método guarda y limpia el ChangeTracker, como los de la fixture.
/// </summary>
internal static class SalesRouteTestSeed
{
    /// <summary>Persona activa: ApplicationUser activo + UserTenant con MembershipStatus ACTIVE en la compañía (por omisión la activa de la fixture).</summary>
    public static Task<int> AddActiveMemberAsync(this WmsFixture f, int userId, string name, int? tenantId = null)
        => f.AddMemberAsync(userId, name, MembershipStatuses.Active, true, tenantId);

    /// <summary>Miembro con la membresía SUSPENDED (conserva la fila y el usuario activo, pero no es persona activa).</summary>
    public static Task<int> AddSuspendedMemberAsync(this WmsFixture f, int userId, string name, int? tenantId = null)
        => f.AddMemberAsync(userId, name, MembershipStatuses.Suspended, true, tenantId);

    /// <summary>Usuario con la membresía y el estado que se pidan (INVITED, usuario inactivo…).</summary>
    public static async Task<int> AddMemberAsync(this WmsFixture f, int userId, string name, string membership, bool userActive, int? tenantId = null)
    {
        var statusId = await MembershipStatusIdAsync(f, membership);
        f.Db.Users.Add(NewUser(userId, name, userActive));
        f.Db.UserTenants.Add(new UserTenant { UserId = userId, TenantId = tenantId ?? f.Tenant.TenantId!.Value, StatusCodeId = statusId });
        await SaveAsync(f);
        return userId;
    }

    /// <summary>Le agrega al usuario (ya creado) una membresía en otra compañía.</summary>
    public static async Task AddMembershipAsync(this WmsFixture f, int userId, int tenantId, string membership = MembershipStatuses.Active)
    {
        var statusId = await MembershipStatusIdAsync(f, membership);
        f.Db.UserTenants.Add(new UserTenant { UserId = userId, TenantId = tenantId, StatusCodeId = statusId });
        await SaveAsync(f);
    }

    /// <summary>Usuario del portal de un cliente: ApplicationUser activo SIN UserTenant (nunca es persona activa de la compañía).</summary>
    public static async Task<int> AddPortalUserAsync(this WmsFixture f, int userId, string name)
    {
        f.Db.Users.Add(NewUser(userId, name, true));
        await SaveAsync(f);
        return userId;
    }

    public static async Task<SalesRoute> AddRouteAsync(this WmsFixture f, string name, int? tenantId = null, bool isActive = true, int sortOrder = 100)
    {
        var r = new SalesRoute { TenantId = tenantId ?? f.Tenant.TenantId!.Value, Name = name, IsActive = isActive, SortOrder = sortOrder };
        f.Db.Set<SalesRoute>().Add(r);
        await SaveAsync(f);
        return r;
    }

    public static async Task<SalesRep> AddRepAsync(this WmsFixture f, string name, int? routeId, int? userId, int? tenantId = null, bool isActive = true)
    {
        var r = new SalesRep { TenantId = tenantId ?? f.Tenant.TenantId!.Value, Name = name, SalesRouteId = routeId, UserId = userId, IsActive = isActive };
        f.Db.Set<SalesRep>().Add(r);
        await SaveAsync(f);
        return r;
    }

    /// <summary>Una asignación tal cual (sin pasar por las reglas): titular o apoyo, con su vigencia; isActive false = anulada (con ValidTo ??= ValidFrom).</summary>
    public static async Task<SalesRouteAssignment> AddAssignmentAsync(this WmsFixture f, int routeId, int userId, bool isPrimary, DateOnly validFrom,
        DateOnly? validTo = null, bool isActive = true, int? tenantId = null)
    {
        var a = new SalesRouteAssignment
        {
            TenantId = tenantId ?? f.Tenant.TenantId!.Value, SalesRouteId = routeId, UserId = userId, IsPrimary = isPrimary, ValidFrom = validFrom,
            ValidTo = isActive ? validTo : validTo ?? validFrom, IsActive = isActive, CreatedAtUtc = DateTime.UtcNow,
        };
        f.Db.Set<SalesRouteAssignment>().Add(a);
        await SaveAsync(f);
        return a;
    }

    /// <summary>Pone la ruta directa, el representante y el vendedor preferido del cliente (lo que no se pasa queda en NULL).</summary>
    public static async Task SetClientSalesAsync(this WmsFixture f, int clientId, int? routeId = null, int? repId = null, int? preferredUserId = null)
    {
        var c = await f.Db.Clients.IgnoreQueryFilters().AsTracking().SingleAsync(x => x.ClientId == clientId);
        c.SalesRouteId = routeId;
        c.SalesRepId = repId;
        c.PreferredSalesUserId = preferredUserId;
        await SaveAsync(f);
    }

    /// <summary>El id del estatus de membresía (lo siembra la primera vez que se pide).</summary>
    public static async Task<int> MembershipStatusIdAsync(WmsFixture f, string code)
    {
        var id = await f.Db.StatusCodes.AsNoTracking().Where(s => s.Entity == StatusDomains.MembershipStatus && s.InternalCode == code).Select(s => (int?)s.StatusCodeId).FirstOrDefaultAsync();
        if (id is int found) return found;
        var next = await f.Db.StatusCodes.AsNoTracking().MaxAsync(s => s.StatusCodeId) + 1;
        f.Db.StatusCodes.Add(new StatusCode
        {
            StatusCodeId = next, Entity = StatusDomains.MembershipStatus, InternalCode = code, LabelJson = $"{{\"es\":\"{code}\"}}",
            StageKindLookupId = f.LookupId(LookupDomains.StageKind, StageKinds.Pipeline), SortOrder = 1, IsActive = true,
        });
        await SaveAsync(f);
        return next;
    }

    private static ApplicationUser NewUser(int id, string name, bool active)
    {
        var mail = $"{name.ToLowerInvariant().Replace(' ', '.')}.{id}@x.test";
        return new ApplicationUser { Id = id, UserName = mail, Email = mail, FullName = name, IsActive = active };
    }

    private static async Task SaveAsync(WmsFixture f)
    {
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
    }
}
