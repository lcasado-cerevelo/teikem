using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Clients;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 199 — reasignar desde el aparato. Los endpoints «assign» aceptan warehouse.manage (supervisión desde la web) o warehouse.app.reassign (el aparato); quien entra solo por
/// reasignar debe además poder ver todo (warehouse.app.viewall), porque no se reasigna lo que no se ve.
/// </summary>
public static class ReassignGuard
{
    public const string NeedsViewAll = "Para reasignar hace falta poder ver todo en el aparato.";

    public static async Task EnsureAsync(PermissionService permissions, CancellationToken ct)
    {
        if (await permissions.HasPermissionAsync(PermissionCatalog.WarehouseManage, ct)) return;
        if (!await permissions.HasPermissionAsync(PermissionCatalog.WarehouseAppViewAll, ct)) throw new ForbiddenException(NeedsViewAll);
    }

    /// <summary>Las personas a quienes se puede asignar: miembros activos de la compañía, por nombre.</summary>
    public static async Task<IReadOnlyList<AssigneeDto>> ListAssigneesAsync(TeikemDbContext db, CancellationToken ct)
    {
        var activeId = await db.StatusIdAsync(StatusDomains.MembershipStatus, MembershipStatuses.Active, ct);
        var ids = await db.UserTenants.AsNoTracking().Where(m => m.StatusCodeId == activeId).Select(m => m.UserId).ToListAsync(ct);
        var names = await AssigneeCheck.NamesAsync(db, ids, ct);
        return ids.Distinct().Select(i => new AssigneeDto(i, names.GetValueOrDefault(i, string.Empty))).OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }
}

/// <summary>Una persona asignable (id y nombre para mostrar).</summary>
public sealed record AssigneeDto(int Id, string Name);
