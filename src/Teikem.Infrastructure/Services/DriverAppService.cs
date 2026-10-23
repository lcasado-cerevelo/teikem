using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Common;
using Teikem.Domain.Constants;
using Teikem.Domain.Orders;
using Teikem.Domain.Trips;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Trips;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// App del chofer (2026-10-23, Luis). El chofer es el usuario vinculado a un Driver (Driver.UserId): solo ve SUS manifiestos (rutas despachadas o en curso a su nombre); una ruta de otro chofer
/// responde igual que una que no existe (404 sin oráculo). ACEPTAR el manifiesto es la SALIDA: los envíos que acepta pasan a En tránsito (la ruta a En curso, con TripLifecycleService.StartTrackedAsync, la misma
/// que usa el personal); los que NO acepta llevan motivo (lista «Motivos de no aceptación»), salen de la ruta y vuelven a «Sin asignar», y quedan en TripOrderDecline para que la oficina los vuelva a asignar. Todo en una transacción.
/// </summary>
public sealed class DriverAppService(TeikemDbContext db, ITenantContext tenant, StatusService statuses, TripLifecycleService lifecycle, RouteWriter writer, ILookupCache lookups)
{
    private async Task<Domain.Fleet.Driver> CurrentDriverAsync(CancellationToken ct)
    {
        var userId = ((TenantContext)tenant).RequireUserId();
        return await db.Drivers.AsNoTracking().FirstOrDefaultAsync(d => d.UserId == userId && d.IsActive, ct)
               ?? throw new ForbiddenException(DriverAppRules.NotADriverMessage);
    }

    private static string Label(string? json, string lang, string fallback) => string.IsNullOrWhiteSpace(json) ? fallback : MultilingualText.Resolve(json, lang);

    /// <summary>Los manifiestos del chofer: rutas DISPATCHED (por aceptar) o IN_PROGRESS (en curso) a su nombre, las más recientes primero.</summary>
    public async Task<IReadOnlyList<DriverManifestDto>> ListManifestsAsync(CancellationToken ct)
    {
        var driver = await CurrentDriverAsync(ct);
        var rows = await (from t in db.Trips.AsNoTracking()
                          join s in db.StatusCodes.AsNoTracking() on t.StatusCodeId equals s.StatusCodeId
                          where t.DriverId == driver.DriverId && t.IsActive && (s.InternalCode == TripStatuses.Dispatched || s.InternalCode == TripStatuses.InProgress)
                          orderby t.PlanDate descending, t.TripId descending
                          select new { t.TripId, t.PublicId, t.Code, t.PlanDate, t.AcceptedAtUtc, t.RowVersion, StatusCode = s.InternalCode, s.LabelJson }).ToListAsync(ct);
        var ids = rows.Select(r => r.TripId).ToList();
        var counts = await db.TripOrders.AsNoTracking().Where(o => ids.Contains(o.TripId) && o.IsCurrent).GroupBy(o => o.TripId).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N, ct);
        return rows.Select(r => new DriverManifestDto(r.PublicId, r.Code, "DELIVERY", r.PlanDate, r.StatusCode, Label(r.LabelJson, tenant.Lang, r.StatusCode), r.AcceptedAtUtc != null, r.AcceptedAtUtc,
            counts.GetValueOrDefault(r.TripId), r.RowVersion is null ? null : Convert.ToBase64String(r.RowVersion))).ToList();
    }

    /// <summary>Un manifiesto del chofer con sus envíos en el orden de la ruta. 404 si no es suyo.</summary>
    public async Task<DriverManifestDetailDto> GetManifestAsync(Guid publicId, CancellationToken ct)
    {
        var driver = await CurrentDriverAsync(ct);
        var t = await (from x in db.Trips.AsNoTracking()
                       join s in db.StatusCodes.AsNoTracking() on x.StatusCodeId equals s.StatusCodeId
                       where x.PublicId == publicId && x.DriverId == driver.DriverId && x.IsActive
                       select new { x.TripId, x.PublicId, x.Code, x.PlanDate, x.AcceptedAtUtc, x.RowVersion, StatusCode = s.InternalCode, s.LabelJson }).FirstOrDefaultAsync(ct)
                ?? throw new NotFoundException("Manifiesto", null, false);

        var route = await db.Routes.AsNoTracking().Where(r => r.TripId == t.TripId && r.IsActive).Select(r => (int?)r.RouteId).FirstOrDefaultAsync(ct);
        var shipments = await (from l in db.TripOrders.AsNoTracking()
                               join o in db.TransportOrders.AsNoTracking() on l.TransportOrderId equals o.TransportOrderId
                               join st in db.StatusCodes.AsNoTracking() on o.StatusCodeId equals st.StatusCodeId
                               join os in db.OrderStops.AsNoTracking().Where(x => x.StopType!.InternalCode == StopTypes.Delivery) on o.TransportOrderId equals os.TransportOrderId into stops
                               from os in stops.DefaultIfEmpty()
                               where l.TripId == t.TripId && l.IsCurrent
                               select new { o.PublicId, o.OrderNumber, o.WaybillNumber, o.TotalPieces, o.CodAmount, StatusCode = st.InternalCode, StatusLabel = st.LabelJson,
                                            OsId = os != null ? (int?)os.OrderStopId : null, Name = os != null ? os.SnapName : null, Line1 = os != null ? os.SnapLine1 : null,
                                            City = os != null ? os.SnapCity : null, Postal = os != null ? os.SnapPostalCode : null, Notes = os != null ? os.Notes : null, l.SortHint }).ToListAsync(ct);
        var osIds = shipments.Where(s => s.OsId != null).Select(s => s.OsId!.Value).ToList();
        var seqs = route is null ? new Dictionary<int, int>()
            : await db.RouteStops.AsNoTracking().Where(rs => rs.RouteId == route && osIds.Contains(rs.OrderStopId)).ToDictionaryAsync(rs => rs.OrderStopId, rs => rs.Sequence, ct);

        var list = shipments
            .Select(s => new DriverShipmentDto(s.PublicId, s.OrderNumber, s.WaybillNumber, s.StatusCode, Label(s.StatusLabel, tenant.Lang, s.StatusCode),
                s.OsId is int id && seqs.TryGetValue(id, out var q) ? q : s.SortHint ?? int.MaxValue, s.Name, s.Line1 ?? string.Empty, s.City, s.Postal, s.TotalPieces, s.CodAmount, s.Notes))
            .OrderBy(s => s.Sequence).ThenBy(s => s.OrderNumber, StringComparer.Ordinal).ToList();
        return new DriverManifestDetailDto(t.PublicId, t.Code, "DELIVERY", t.PlanDate, t.StatusCode, Label(t.LabelJson, tenant.Lang, t.StatusCode), t.AcceptedAtUtc != null, t.AcceptedAtUtc,
            t.RowVersion is null ? null : Convert.ToBase64String(t.RowVersion), list);
    }

    /// <summary>La lista «Motivos de no aceptación» (la de la compañía, ya con sus ajustes).</summary>
    public async Task<IReadOnlyList<DriverDeclineReasonDto>> ListReasonsAsync(CancellationToken ct)
        => (await lookups.GetDomainAsync(TripOrderDeclineCodes.ReasonDomain, ct)).Where(l => l.IsActive).OrderBy(l => l.SortOrder).ThenBy(l => l.InternalCode, StringComparer.Ordinal)
            .Select(l => new DriverDeclineReasonDto(l.InternalCode, Label(l.LabelJson, tenant.Lang, l.InternalCode))).ToList();

    /// <summary>
    /// Aceptar el manifiesto (la salida). 404 si no es suyo; 422 si no está despachado o ya se aceptó; 400 si falta el motivo de un envío que no acepta, el motivo no es válido, la nota pasa de 300 caracteres, un envío se repite
    /// o no está en el manifiesto. Los que no acepta salen de la ruta; si no acepta NINGUNO la ruta no sale (queda despachada y vacía para que la oficina decida).
    /// </summary>
    public async Task<DriverAcceptResultDto> AcceptAsync(Guid publicId, DriverAcceptRequest? req, CancellationToken ct)
    {
        var driver = await CurrentDriverAsync(ct);
        var userId = ((TenantContext)tenant).RequireUserId();
        var declines = req?.Declines ?? Array.Empty<DriverDeclineItem>();

        // Validación sin tocar la base: motivo, nota y repetidos.
        var reasons = (await lookups.GetDomainAsync(TripOrderDeclineCodes.ReasonDomain, ct)).Where(l => l.IsActive).ToDictionary(l => l.InternalCode, StringComparer.OrdinalIgnoreCase);
        if (declines.Select(d => d.ShipmentPublicId).Distinct().Count() != declines.Count) throw new ValidationException("declines", DriverAppRules.DuplicateShipmentMessage);
        foreach (var d in declines)
        {
            if (string.IsNullOrWhiteSpace(d.ReasonCode)) throw new ValidationException("declines", DriverAppRules.ReasonRequiredMessage);
            if (!reasons.ContainsKey(d.ReasonCode.Trim())) throw new ValidationException("declines", DriverAppRules.ReasonInvalidMessage);
            if (d.Note is { Length: > TripOrderDeclineCodes.NoteMaxLength }) throw new ValidationException("declines", DriverAppRules.NoteTooLongMessage);
        }

        var accepted = 0;
        var started = false;
        await db.RunInTransactionAsync(async ct2 =>
        {
            var trip = await db.LockTripAsync(publicId, ct2);   // 404 'Ruta no encontrada.'
            if (trip.DriverId != driver.DriverId) throw new NotFoundException("Manifiesto", null, false);
            var code = await db.StatusCodes.AsNoTracking().Where(s => s.StatusCodeId == trip.StatusCodeId).Select(s => s.InternalCode).FirstOrDefaultAsync(ct2) ?? string.Empty;
            if (trip.AcceptedAtUtc != null) throw new StatusRuleException(DriverAppRules.AlreadyAcceptedMessage(trip.Code));
            if (!string.Equals(code, TripStatuses.Dispatched, StringComparison.OrdinalIgnoreCase)) throw new StatusRuleException(DriverAppRules.NotReadyMessage(trip.Code));
            TripDispatchRowVersion.Ensure(db, trip, req?.RowVersion);

            var current = await db.TripOrders.AsNoTracking().Where(l => l.TripId == trip.TripId && l.IsCurrent)
                .Join(db.TransportOrders.AsNoTracking(), l => l.TransportOrderId, o => o.TransportOrderId, (l, o) => new { o.TransportOrderId, o.PublicId, o.OrderNumber }).ToListAsync(ct2);
            var byPublic = current.ToDictionary(x => x.PublicId);
            foreach (var d in declines) if (!byPublic.ContainsKey(d.ShipmentPublicId)) throw new ValidationException("declines", DriverAppRules.ShipmentNotInManifestMessage);

            if (declines.Count > 0)
            {
                var orders = await db.LockOrdersAsync(declines.Select(d => byPublic[d.ShipmentPublicId].TransportOrderId).ToList(), includeStops: false, ct2);
                foreach (var d in declines)
                {
                    var orderId = byPublic[d.ShipmentPublicId].TransportOrderId;
                    var order = orders.First(o => o.TransportOrderId == orderId);
                    var reason = reasons[d.ReasonCode.Trim()];
                    await writer.ReleaseOrderFromDispatchedAsync(trip, orderId, ct2);
                    var to = await statuses.AdvanceSkippingAsync(StatusDomains.OrderStatus, EntityTypes.TransportOrder, orderId, order.StatusCodeId, new[] { OrderStatuses.Unassigned },
                        DriverAppRules.DeclinedComment(Label(reason.LabelJson, tenant.Lang, reason.InternalCode)), ct2);
                    order.StatusCodeId = to.StatusCodeId;
                    db.TripOrderDeclines.Add(new TripOrderDecline
                    {
                        TenantId = trip.TenantId, TripId = trip.TripId, TransportOrderId = orderId, DriverId = driver.DriverId, ReasonLookupId = reason.LookupCodeId,
                        Note = string.IsNullOrWhiteSpace(d.Note) ? null : d.Note.Trim(), DeclinedAtUtc = DateTime.UtcNow, DeclinedBy = userId,
                    });
                }
            }

            // Se guarda lo de los que no acepta ANTES de la salida: la salida lee las órdenes vigentes de la base y no debe llevar a En tránsito a las que acaban de salir de la ruta.
            if (declines.Count > 0) await db.SaveGuardedAsync(DbExtensions.ConcurrencyMessage, ct2);

            accepted = current.Count - declines.Count;
            if (accepted > 0)
            {
                await lifecycle.StartTrackedAsync(trip, DriverAppRules.AcceptedComment, ct2);
                trip.AcceptedAtUtc = DateTime.UtcNow;
                trip.AcceptedBy = userId;
                started = true;
            }
            await db.SaveGuardedAsync(DbExtensions.ConcurrencyMessage, ct2);
        }, ct);
        return new DriverAcceptResultDto(publicId, accepted, declines.Count, started);
    }
}
