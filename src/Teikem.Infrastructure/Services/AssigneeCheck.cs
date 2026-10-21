using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Constants;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Clients;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>Lote 171 — la persona a quien se asigna un documento debe ser miembro ACTIVO de la compañía (UserTenant está bajo el filtro de tenant: uno de otra compañía no aparece, 400 sin oráculo).</summary>
internal static class AssigneeCheck
{
    public static async Task EnsureActiveMemberAsync(TeikemDbContext db, int userId, CancellationToken ct)
    {
        var activeId = await db.StatusIdAsync(StatusDomains.MembershipStatus, MembershipStatuses.Active, ct);
        var member = await db.UserTenants.AsNoTracking().AnyAsync(m => m.UserId == userId && m.StatusCodeId == activeId, ct);
        if (!member) throw new ValidationException("userId", WarehouseTaskRules.AssigneeNotMember);
    }

    public static async Task<Dictionary<int, string>> NamesAsync(TeikemDbContext db, IEnumerable<int> ids, CancellationToken ct)
    {
        var list = ids.Distinct().ToList();
        if (list.Count == 0) return new();
        return await db.Users.AsNoTracking().Where(u => list.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName ?? u.Email ?? string.Empty, ct);
    }
}
