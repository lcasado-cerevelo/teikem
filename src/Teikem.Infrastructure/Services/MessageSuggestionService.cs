using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Constants;
using Teikem.Domain.Messaging;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Messaging;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 212 — a quién proponerle primero en «Hablar sobre esta orden/ruta». Nunca abre nada solo. En este orden y sin repetir: (1) responsables de la RUTA (la de la ruta, o la ruta activa
/// de la orden), (2) responsables de la ZONA de la ruta, (3) los roles que tienen el permiso de despachar (trips.dispatch) como grupo, (4) con quien se habló hace poco (hasta 5).
/// Solo personas que quien pregunta puede ver (modo de mensajería) y nunca uno mismo.
/// </summary>
public sealed class MessageSuggestionService(TeikemDbContext db, ITenantContext tenant, ILookupCache lookups, ResponsibleService responsibles, PresenceService presence)
{
    private TenantContext T => (TenantContext)tenant;
    public const int RecentLimit = 5;

    public async Task<IReadOnlyList<MessageSuggestionDto>> SuggestAsync(string? entity, Guid publicId, CancellationToken ct)
    {
        var e = MessageRefs.Normalize(entity) ?? throw new ValidationException("entity", MessageRefs.InvalidMessage);
        var me = T.RequireUserId();
        var myMode = await db.UserTenants.AsNoTracking().Where(m => m.UserId == me).Select(m => m.MessagingMode).FirstOrDefaultAsync(ct) ?? MessagingModes.Full;
        if (myMode == MessagingModes.None) return Array.Empty<MessageSuggestionDto>();
        var modes = await db.UserTenants.AsNoTracking().ToDictionaryAsync(m => m.UserId, m => m.MessagingMode, ct);
        bool Visible(int userId) => userId != me && modes.TryGetValue(userId, out var m) && MessagingRules.CanSee(myMode, m);

        // la ruta de la que se habla (para una orden: la ruta activa donde va)
        int? tripId = null, zoneId = null;
        if (e == MessageRefs.Trip)
        {
            var t = await db.Trips.AsNoTracking().Where(x => x.PublicId == publicId).Select(x => new { x.TripId, x.DispatchZoneId }).FirstOrDefaultAsync(ct)
                ?? throw new NotFoundException("Ruta", null, feminine: true);
            (tripId, zoneId) = (t.TripId, t.DispatchZoneId);
        }
        else
        {
            var orderId = await db.TransportOrders.AsNoTracking().Where(o => o.PublicId == publicId).Select(o => (int?)o.TransportOrderId).FirstOrDefaultAsync(ct)
                ?? throw new NotFoundException("Orden", null, feminine: true);
            var t = await (from os in db.OrderStops.AsNoTracking()
                           where os.TransportOrderId == orderId
                           join rs in db.RouteStops.AsNoTracking() on os.OrderStopId equals rs.OrderStopId
                           join r in db.Routes.AsNoTracking() on rs.RouteId equals r.RouteId
                           join tr in db.Trips.AsNoTracking() on r.TripId equals tr.TripId
                           where tr.IsActive
                           orderby tr.TripId descending
                           select new { tr.TripId, tr.DispatchZoneId }).FirstOrDefaultAsync(ct);
            if (t is not null) (tripId, zoneId) = (t.TripId, t.DispatchZoneId);
        }

        var seen = new HashSet<int>();
        var people = new List<(string Source, string Label, int UserId, string Name)>();
        async Task AddPeopleAsync(string source, string label, IEnumerable<ResponsibleDto> list)
        {
            await Task.CompletedTask;
            foreach (var r in list)
                if (Visible(r.UserId) && seen.Add(r.UserId)) people.Add((source, label, r.UserId, r.Name));
        }
        if (tripId is int tid) await AddPeopleAsync("TRIP", "Responsable de la ruta", await responsibles.ListByIdAsync(EntityTypes.Trip, tid, ct));
        if (zoneId is int zid)
        {
            var zoneCode = await db.DispatchZones.AsNoTracking().Where(z => z.DispatchZoneId == zid).Select(z => z.Code).FirstOrDefaultAsync(ct);
            await AddPeopleAsync("ZONE", $"Responsable de la zona {zoneCode}", await responsibles.ListByIdAsync(EntityTypes.DispatchZone, zid, ct));
        }

        // roles que despachan (los que tienen trips.dispatch), con cuántas personas visibles tienen
        var dispatchRoles = await (from rp in db.RolePermissions.AsNoTracking()
                                   join p in db.Permissions.AsNoTracking() on rp.PermissionId equals p.PermissionId
                                   where p.Code == PermissionCatalog.TripsDispatch
                                   join r in db.AppRoles.AsNoTracking() on rp.RoleId equals r.RoleId
                                   where r.IsActive
                                   select new { r.RoleId, r.Name }).Distinct().ToListAsync(ct);
        var roleSuggestions = new List<MessageSuggestionDto>();
        foreach (var role in dispatchRoles.OrderBy(r => r.Name))
        {
            var holders = await db.AppUserRoles.AsNoTracking().Where(ur => ur.RoleId == role.RoleId).Select(ur => ur.UserId).Distinct().ToListAsync(ct);
            if (holders.Any(Visible)) roleSuggestions.Add(new MessageSuggestionDto("ROLE", "Despachadores", null, role.RoleId, role.Name, false));
        }

        // con quien se habló hace poco (hilos entre dos)
        var recentIds = await (from m in db.MessageThreadMembers.AsNoTracking()
                               where m.UserId == me && m.LeftAtUtc == null
                               join t in db.MessageThreads.AsNoTracking() on m.MessageThreadId equals t.MessageThreadId
                               where t.Kind == MessageThreadKinds.Direct
                               orderby t.LastMessageAtUtc descending
                               join o in db.MessageThreadMembers.AsNoTracking() on t.MessageThreadId equals o.MessageThreadId
                               where o.UserId != me && o.LeftAtUtc == null
                               select o.UserId).Take(RecentLimit * 2).ToListAsync(ct);
        var recent = recentIds.Where(Visible).Where(id => !seen.Contains(id)).Distinct().Take(RecentLimit).ToList();

        var allIds = people.Select(p => p.UserId).Concat(recent).Distinct().ToList();
        var names = allIds.Count == 0 ? new Dictionary<int, string>() : (await db.Users.AsNoTracking().Where(u => allIds.Contains(u.Id)).Select(u => new { u.Id, u.FullName, u.Email }).ToListAsync(ct))
            .ToDictionary(u => u.Id, u => string.IsNullOrWhiteSpace(u.FullName) ? u.Email ?? $"#{u.Id}" : u.FullName);
        var seenAt = await presence.LoadAsync(allIds, ct);
        var now = DateTime.UtcNow;
        bool Online(int id) => seenAt.TryGetValue(id, out var s) && MessagingRules.IsOnline(s, now);

        var result = people.Select(p => new MessageSuggestionDto(p.Source, p.Label, p.UserId, null, p.Name, Online(p.UserId))).ToList();
        result.AddRange(roleSuggestions);
        result.AddRange(recent.Select(id => new MessageSuggestionDto("RECENT", "Conversación reciente", id, null, names.GetValueOrDefault(id, $"#{id}"), Online(id))));
        return result;
    }
}
