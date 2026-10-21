using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Constants;
using Teikem.Domain.Fleet;
using Teikem.Domain.Orders;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 93 (P9) — registro de intentos de entrega, la fuente de verdad de la liquidación a choferes. Los intentos nacen de tres maneras: del cambio de estatus de la orden a Entregada o Fallida
/// (DeliveryAttemptOrderEffect, con el chofer de la ruta vigente), a mano por la oficina (hasta que exista la app del chofer) y, más adelante, de la app. Una entrega especial (con viaje pagado) no genera
/// intentos: se paga por viaje. Un intento no se borra: se anula con motivo y solo si no está en una liquidación.
/// </summary>
public sealed class DeliveryAttemptService(TeikemDbContext db, ITenantContext tenant, ILookupCache lookups, ITenantClock clock)
{
    private TenantContext T => (TenantContext)tenant;
    public const int MaxPageSize = 200;
    public const string Concurrency = "Los intentos cambiaron mientras se guardaban. Revise y vuelva a intentar.";

    // ================================================================ desde el estatus de la orden

    /// <summary>
    /// Anota el intento que produce un cambio de estatus de la orden a Entregada (entregó) o Fallida (no entregó). No guarda: queda en la unidad de trabajo del llamador. No hace nada si la orden es una
    /// entrega especial, si ya tiene un intento entregado vigente o si no hay chofer que lo haga (ruta sin chofer): en ese caso queda sin chofer para que la oficina lo complete.
    /// </summary>
    public async Task RecordFromStatusAsync(int orderId, bool delivered, string? comment, CancellationToken ct)
    {
        if (await IsSpecialDeliveryAsync(orderId, ct)) return;
        var attempts = await ActiveOfOrderAsync(orderId, ct);
        if (attempts.Any(a => a.Delivered)) return;
        var link = await db.TripOrders.AsNoTracking().Where(x => x.TransportOrderId == orderId && x.IsCurrent).OrderByDescending(x => x.TripOrderId)
            .Select(x => new { x.TripId, DriverId = x.Trip!.DriverId }).FirstOrDefaultAsync(ct);
        db.Set<DeliveryAttempt>().Add(new DeliveryAttempt
        {
            PublicId = Guid.NewGuid(), TenantId = T.RequireTenantId(), TransportOrderId = orderId, AttemptNumber = DeliveryAttemptRules.NextNumber(attempts.Select(a => a.AttemptNumber)),
            DriverId = link?.DriverId, TripId = link?.TripId, AttemptedAtUtc = DateTime.UtcNow, Delivered = delivered,
            Notes = string.IsNullOrWhiteSpace(comment) ? null : Trim(comment, DeliveryAttemptRules.MaxNotesLength), Source = DeliveryAttemptSources.Status, RecordedBy = tenant.UserId,
        });
    }

    private async Task<bool> IsSpecialDeliveryAsync(int orderId, CancellationToken ct)
        => db.DriverTrips.Local.Any(t => t.TransportOrderId == orderId && t.IsActive) || await db.DriverTrips.AsNoTracking().AnyAsync(t => t.TransportOrderId == orderId && t.IsActive, ct);

    private async Task<List<DeliveryAttempt>> ActiveOfOrderAsync(int orderId, CancellationToken ct)
    {
        var local = db.Set<DeliveryAttempt>().Local.Where(a => a.TransportOrderId == orderId && a.IsActive).ToList();
        var stored = await db.Set<DeliveryAttempt>().AsNoTracking().Where(a => a.TransportOrderId == orderId && a.IsActive).ToListAsync(ct);
        return local.Concat(stored.Where(s => local.All(l => l.DeliveryAttemptId != s.DeliveryAttemptId))).ToList();
    }

    // ================================================================ a mano

    public async Task<DeliveryAttemptDto> RecordAsync(DeliveryAttemptCreateRequest? req, CancellationToken ct)
    {
        if (req is null) throw new ValidationException("body", "El cuerpo de la solicitud es obligatorio.");
        if (req.OrderPublicId is not Guid orderPublicId) throw new ValidationException("orderPublicId", DeliveryAttemptRules.OrderRequired);
        if (req.DriverPublicId is not Guid driverPublicId) throw new ValidationException("driverPublicId", DeliveryAttemptRules.DriverRequired);
        var notes = string.IsNullOrWhiteSpace(req.Notes) ? null : req.Notes.Trim();
        if (notes is { Length: > DeliveryAttemptRules.MaxNotesLength }) throw new ValidationException("notes", DeliveryAttemptRules.NotesTooLong);
        var delivered = req.Delivered ?? false;
        var reasonCode = string.IsNullOrWhiteSpace(req.FailureReason) ? null : req.FailureReason.Trim().ToUpperInvariant();
        if (delivered && reasonCode is not null) throw new ValidationException("failureReason", DeliveryAttemptRules.DeliveredWithReason);
        int? reasonId = null;
        if (reasonCode is not null)
            reasonId = await lookups.TryGetIdAsync(DeliveryAttemptCodes.FailureReasonDomain, reasonCode, ct) ?? throw new ValidationException("failureReason", DeliveryAttemptRules.UnknownReason(reasonCode));
        var at = req.AttemptedAtUtc ?? clock.UtcNow;
        if (at > clock.UtcNow.AddMinutes(5)) throw new ValidationException("attemptedAtUtc", DeliveryAttemptRules.FutureAttempt);
        var order = await db.TransportOrders.AsNoTracking().FirstOrDefaultAsync(o => o.PublicId == orderPublicId, ct) ?? throw new NotFoundException("Envío", orderPublicId);
        var driver = await db.Set<Driver>().AsNoTracking().FirstOrDefaultAsync(d => d.PublicId == driverPublicId, ct) ?? throw new NotFoundException("Chofer", driverPublicId);
        if (await IsSpecialDeliveryAsync(order.TransportOrderId, ct)) throw new StatusRuleException(DeliveryAttemptRules.SpecialDelivery);

        var id = await db.RunInTransactionAsync(async ct2 =>
        {
            var existing = await ActiveOfOrderAsync(order.TransportOrderId, ct2);
            var number = req.AttemptNumber ?? DeliveryAttemptRules.NextNumber(existing.Select(a => a.AttemptNumber));
            var problem = DeliveryAttemptRules.ValidateSequence(existing.Select(a => new PayoutAttempt(a.AttemptNumber, a.Delivered)).ToList(), new PayoutAttempt(number, delivered));
            if (problem is not null) throw new ValidationException("attemptNumber", problem);
            var tripId = await db.TripOrders.AsNoTracking().Where(x => x.TransportOrderId == order.TransportOrderId && x.IsCurrent).Select(x => (int?)x.TripId).FirstOrDefaultAsync(ct2);
            var attempt = new DeliveryAttempt
            {
                PublicId = Guid.NewGuid(), TenantId = T.RequireTenantId(), TransportOrderId = order.TransportOrderId, AttemptNumber = number, DriverId = driver.DriverId, TripId = tripId, AttemptedAtUtc = at,
                Delivered = delivered, FailureReasonLookupId = reasonId, Notes = notes, Source = DeliveryAttemptSources.Manual, RecordedBy = tenant.UserId,
            };
            db.Set<DeliveryAttempt>().Add(attempt);
            await db.SaveGuardedAsync(Concurrency, ct2);
            return attempt.DeliveryAttemptId;
        }, ct);
        return await GetAsync(id, ct);
    }

    public async Task<DeliveryAttemptDto> VoidAsync(Guid publicId, string? reason, CancellationToken ct)
    {
        var text = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        if (text is null) throw new ValidationException("reason", DeliveryAttemptRules.ReasonRequired);
        if (text.Length > DeliveryAttemptRules.MaxVoidReasonLength) throw new ValidationException("reason", DeliveryAttemptRules.ReasonTooLong);
        var a = await db.Set<DeliveryAttempt>().AsTracking().FirstOrDefaultAsync(x => x.PublicId == publicId, ct) ?? throw new NotFoundException("Intento", publicId);
        if (!a.IsActive) throw new ConflictException(DeliveryAttemptRules.AlreadyVoided);
        if (a.SettledRunId is not null) throw new ConflictException(DeliveryAttemptRules.AlreadySettled);
        a.IsActive = false;
        (a.VoidedAtUtc, a.VoidedBy, a.VoidReason) = (clock.UtcNow, tenant.UserId, text);
        await db.SaveGuardedAsync(Concurrency, ct);
        return await GetAsync(a.DeliveryAttemptId, ct);
    }

    // ================================================================ lectura

    public async Task<DeliveryAttemptDto> GetAsync(int id, CancellationToken ct)
        => (await ProjectAsync(db.Set<DeliveryAttempt>().AsNoTracking().Where(a => a.DeliveryAttemptId == id), ct)).FirstOrDefault() ?? throw new NotFoundException("Intento", id);

    public async Task<PagedResult<DeliveryAttemptDto>> ListAsync(DeliveryAttemptListQuery q, CancellationToken ct)
    {
        var query = db.Set<DeliveryAttempt>().AsNoTracking().AsQueryable();
        if (!q.IncludeVoided) query = query.Where(a => a.IsActive);
        if (q.DriverPublicId is Guid dp) query = query.Where(a => db.Set<Driver>().Any(d => d.DriverId == a.DriverId && d.PublicId == dp));
        if (q.OrderPublicId is Guid op) query = query.Where(a => db.TransportOrders.Any(o => o.TransportOrderId == a.TransportOrderId && o.PublicId == op));
        if (q.WithoutDriver == true) query = query.Where(a => a.DriverId == null);
        if (q.Settled is bool settled) query = query.Where(a => (a.SettledRunId != null) == settled);
        if (q.From is DateOnly from) { var f = clock.StartOfDayUtc(from); query = query.Where(a => a.AttemptedAtUtc >= f); }
        if (q.To is DateOnly to) { var t = clock.StartOfDayUtc(to.AddDays(1)); query = query.Where(a => a.AttemptedAtUtc < t); }
        if (q.Delivered is bool d) query = query.Where(a => a.Delivered == d);
        var total = await query.CountAsync(ct);
        var take = Math.Clamp(q.Take <= 0 ? 50 : q.Take, 1, MaxPageSize);
        var page = query.OrderByDescending(a => a.AttemptedAtUtc).ThenByDescending(a => a.DeliveryAttemptId).Skip(Math.Max(0, q.Skip)).Take(take);
        return new PagedResult<DeliveryAttemptDto>(await ProjectAsync(page, ct), total, Math.Max(0, q.Skip), take);
    }

    private async Task<List<DeliveryAttemptDto>> ProjectAsync(IQueryable<DeliveryAttempt> query, CancellationToken ct)
    {
        var rows = await query.ToListAsync(ct);
        if (rows.Count == 0) return new List<DeliveryAttemptDto>();
        var orderIds = rows.Select(r => r.TransportOrderId).Distinct().ToList();
        var orders = await db.TransportOrders.AsNoTracking().Where(o => orderIds.Contains(o.TransportOrderId)).Select(o => new { o.TransportOrderId, o.PublicId, o.OrderNumber, o.ClientInvoiceNumber, o.ClientId }).ToDictionaryAsync(o => o.TransportOrderId, ct);
        var clientIds = orders.Values.Select(o => o.ClientId).Distinct().ToList();
        var clients = await db.Set<Teikem.Domain.Clients.Client>().AsNoTracking().Where(c => clientIds.Contains(c.ClientId)).ToDictionaryAsync(c => c.ClientId, c => c.Name, ct);
        var driverIds = rows.Where(r => r.DriverId != null).Select(r => r.DriverId!.Value).Distinct().ToList();
        var drivers = await db.Set<Driver>().AsNoTracking().Where(d => driverIds.Contains(d.DriverId)).Select(d => new { d.DriverId, d.PublicId, d.EmployeeCode, d.FullName }).ToDictionaryAsync(d => d.DriverId, ct);
        var tripIds = rows.Where(r => r.TripId != null).Select(r => r.TripId!.Value).Distinct().ToList();
        var trips = await db.Set<Teikem.Domain.Trips.Trip>().AsNoTracking().Where(t => tripIds.Contains(t.TripId)).ToDictionaryAsync(t => t.TripId, t => t.Code, ct);
        var lang = tenant.Lang;
        var list = new List<DeliveryAttemptDto>(rows.Count);
        foreach (var r in rows)
        {
            var o = orders.GetValueOrDefault(r.TransportOrderId);
            var d = r.DriverId is int di ? drivers.GetValueOrDefault(di) : null;
            string? reasonCode = null, reason = null;
            if (r.FailureReasonLookupId is int rid && await lookups.GetAsync(rid, ct) is { } lc)
                (reasonCode, reason) = (lc.InternalCode, Domain.Common.MultilingualText.Resolve(lc.LabelJson, lang));
            list.Add(new DeliveryAttemptDto(r.DeliveryAttemptId, r.PublicId, o?.PublicId ?? Guid.Empty, o?.OrderNumber ?? string.Empty, o?.ClientInvoiceNumber, r.AttemptNumber,
                d?.PublicId, d?.EmployeeCode, d?.FullName, r.TripId is int ti ? trips.GetValueOrDefault(ti) : null, r.AttemptedAtUtc, r.Delivered, reasonCode, reason, r.Notes, r.Source,
                r.SettledRunId != null, r.SettledRunId, r.IsActive, r.VoidReason, o is null ? null : clients.GetValueOrDefault(o.ClientId)));
        }
        return list;
    }

    private static string Trim(string s, int max) => s.Trim().Length <= max ? s.Trim() : s.Trim()[..max];
}

/// <summary>
/// Lote 93 (P9) — efecto del dominio OrderStatus: cuando una orden pasa a Entregada o Fallida se anota el intento (con el chofer de su ruta vigente). No guarda: va en la unidad de trabajo de quien
/// cambia el estatus. El servicio se resuelve de forma perezosa (como los demás efectos del dominio) para no formar un ciclo de DI con StatusService.
/// </summary>
public sealed class DeliveryAttemptOrderEffect(IServiceProvider services) : IStatusTransitionEffect
{
    public string StatusDomain => StatusDomains.OrderStatus;

    public async Task OnTransitionedAsync(StatusTransitionContext context, CancellationToken ct)
    {
        if (!string.Equals(context.EntityTypeCode, EntityTypes.TransportOrder, StringComparison.OrdinalIgnoreCase)) return;
        var to = context.To.InternalCode;
        var delivered = string.Equals(to, OrderStatuses.Delivered, StringComparison.OrdinalIgnoreCase);
        if (!delivered && !string.Equals(to, OrderStatuses.Failed, StringComparison.OrdinalIgnoreCase)) return;
        await Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<DeliveryAttemptService>(services)
            .RecordFromStatusAsync(context.EntityId, delivered, context.Comment, ct);
    }
}
