using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Fleet;
using Teikem.Domain.Trips;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Avisos del chofer (2026-10-23): la bandeja de la app. El chofer ve SOLO los suyos (los últimos 100, de la compañía de su token); la oficina (trips.dispatch) escribe avisos a un chofer.
/// Despachar una ruta le deja el aviso «Nuevo manifiesto» (lo agrega TripStatusEffect en la misma transacción).
/// </summary>
public sealed class DriverNoticeService(TeikemDbContext db, ITenantContext tenant, ITenantClock? clock = null)
{
    private readonly ITenantClock _clock = clock ?? TenantClock.Default;

    private async Task<Driver> CurrentDriverAsync(CancellationToken ct)
    {
        var userId = ((TenantContext)tenant).RequireUserId();
        return await db.Drivers.AsNoTracking().FirstOrDefaultAsync(d => d.UserId == userId && d.IsActive, ct) ?? throw new ForbiddenException(DriverAppRules.NotADriverMessage);
    }

    /// <summary>Sus avisos, los más nuevos primero, y cuántos no ha leído.</summary>
    public async Task<DriverNoticesDto> ListAsync(CancellationToken ct)
    {
        var driver = await CurrentDriverAsync(ct);
        var unread = await db.DriverNotices.AsNoTracking().CountAsync(n => n.DriverId == driver.DriverId && n.ReadAtUtc == null, ct);
        var rows = await (from n in db.DriverNotices.AsNoTracking()
                          where n.DriverId == driver.DriverId
                          orderby n.CreatedAtUtc descending, n.DriverNoticeId descending
                          select new { n.PublicId, n.Kind, n.Title, n.Body, n.CreatedAtUtc, n.ReadAtUtc, n.TripId }).Take(DriverNoticeCodes.MaxListed).ToListAsync(ct);
        var tripIds = rows.Where(r => r.TripId != null).Select(r => r.TripId!.Value).Distinct().ToList();
        var trips = tripIds.Count == 0 ? new Dictionary<int, Guid>() : await db.Trips.AsNoTracking().Where(t => tripIds.Contains(t.TripId)).ToDictionaryAsync(t => t.TripId, t => t.PublicId, ct);
        return new DriverNoticesDto(unread, rows.Select(r => new DriverNoticeDto(r.PublicId, r.Kind, r.Title, r.Body, r.CreatedAtUtc, r.ReadAtUtc != null, r.TripId is int t && trips.TryGetValue(t, out var g) ? g : null)).ToList());
    }

    /// <summary>Marca como leídos los avisos indicados (o todos). Los ajenos se ignoran. Devuelve cuántos cambió.</summary>
    public async Task<int> MarkReadAsync(DriverNoticesReadRequest? req, CancellationToken ct)
    {
        var driver = await CurrentDriverAsync(ct);
        var ids = req?.Ids ?? Array.Empty<Guid>();
        if (req?.All != true && ids.Count == 0) return 0;
        var rows = await db.DriverNotices.AsTracking().Where(n => n.DriverId == driver.DriverId && n.ReadAtUtc == null && (req!.All == true || ids.Contains(n.PublicId))).ToListAsync(ct);
        foreach (var n in rows) n.ReadAtUtc = _clock.UtcNow;
        if (rows.Count > 0) await db.SaveChangesAsync(ct);
        return rows.Count;
    }

    /// <summary>La oficina escribe un aviso a un chofer. 400 título faltante o largo, texto largo; 404 chofer.</summary>
    public async Task<DriverNoticeDto> SendAsync(DriverNoticeSendRequest? req, CancellationToken ct)
    {
        var title = req?.Title?.Trim();
        if (string.IsNullOrEmpty(title)) throw new ValidationException("title", DriverNoticeRules.TitleRequiredMessage);
        if (title.Length > DriverNoticeCodes.TitleMaxLength) throw new ValidationException("title", DriverNoticeRules.TitleTooLongMessage);
        var body = string.IsNullOrWhiteSpace(req?.Body) ? null : req!.Body!.Trim();
        if (body is { Length: > DriverNoticeCodes.BodyMaxLength }) throw new ValidationException("body", DriverNoticeRules.BodyTooLongMessage);
        var driver = await db.Drivers.AsNoTracking().FirstOrDefaultAsync(d => d.PublicId == req!.DriverPublicId && d.IsActive, ct) ?? throw new NotFoundException("Chofer", req?.DriverPublicId);
        var n = new DriverNotice { TenantId = ((TenantContext)tenant).RequireTenantId(), DriverId = driver.DriverId, Kind = DriverNoticeCodes.Message, Title = title, Body = body, CreatedBy = tenant.UserId, CreatedAtUtc = _clock.UtcNow };
        db.DriverNotices.Add(n);
        await db.SaveChangesAsync(ct);
        return new DriverNoticeDto(n.PublicId, n.Kind, n.Title, n.Body, n.CreatedAtUtc, false, null);
    }

    /// <summary>Retención: borra los avisos más viejos que 60 días (lo llama el trabajo de limpieza).</summary>
    public async Task<int> PurgeAsync(CancellationToken ct)
        => db.Database.IsRelational() ? await db.DriverNotices.Where(n => n.CreatedAtUtc < _clock.UtcNow.AddDays(-DriverNoticeCodes.RetentionDays)).ExecuteDeleteAsync(ct) : 0;
}
