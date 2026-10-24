using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Common;
using Teikem.Domain.Files;
using Teikem.Domain.Fleet;
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
public sealed class DriverAppService(TeikemDbContext db, ITenantContext tenant, StatusService statuses, TripLifecycleService lifecycle, RouteWriter writer, ILookupCache lookups,
    DeliveryRegistrationService? delivery = null, StoredFileService? files = null, CodService? cod = null)
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
                          select new { t.TripId, t.PublicId, t.Code, t.PlanDate, t.AcceptedAtUtc, t.RowVersion, t.Kind, StatusCode = s.InternalCode, s.LabelJson }).ToListAsync(ct);
        var ids = rows.Select(r => r.TripId).ToList();
        var counts = await db.TripOrders.AsNoTracking().Where(o => ids.Contains(o.TripId) && o.IsCurrent).GroupBy(o => o.TripId).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N, ct);
        return rows.Select(r => new DriverManifestDto(r.PublicId, r.Code, r.Kind, r.PlanDate, r.StatusCode, Label(r.LabelJson, tenant.Lang, r.StatusCode), r.AcceptedAtUtc != null, r.AcceptedAtUtc,
            counts.GetValueOrDefault(r.TripId), r.RowVersion is null ? null : Convert.ToBase64String(r.RowVersion))).ToList();
    }

    /// <summary>Un manifiesto del chofer con sus envíos en el orden de la ruta. 404 si no es suyo.</summary>
    public async Task<DriverManifestDetailDto> GetManifestAsync(Guid publicId, CancellationToken ct)
    {
        var driver = await CurrentDriverAsync(ct);
        var t = await (from x in db.Trips.AsNoTracking()
                       join s in db.StatusCodes.AsNoTracking() on x.StatusCodeId equals s.StatusCodeId
                       where x.PublicId == publicId && x.DriverId == driver.DriverId && x.IsActive
                       select new { x.TripId, x.PublicId, x.Code, x.PlanDate, x.AcceptedAtUtc, x.RowVersion, x.Kind, StatusCode = s.InternalCode, s.LabelJson }).FirstOrDefaultAsync(ct)
                ?? throw new NotFoundException("Manifiesto", null, false);

        var route = await db.Routes.AsNoTracking().Where(r => r.TripId == t.TripId && r.IsActive).Select(r => (int?)r.RouteId).FirstOrDefaultAsync(ct);
        var stopTypeCode = t.Kind == TripKinds.Pickup ? StopTypes.Pickup : StopTypes.Delivery;   // la parada de la ruta: recogida o entrega
        var shipments = await (from l in db.TripOrders.AsNoTracking()
                               join o in db.TransportOrders.AsNoTracking() on l.TransportOrderId equals o.TransportOrderId
                               join st in db.StatusCodes.AsNoTracking() on o.StatusCodeId equals st.StatusCodeId
                               join os in db.OrderStops.AsNoTracking().Where(x => x.StopType!.InternalCode == stopTypeCode) on o.TransportOrderId equals os.TransportOrderId into stops
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
        return new DriverManifestDetailDto(t.PublicId, t.Code, t.Kind, t.PlanDate, t.StatusCode, Label(t.LabelJson, tenant.Lang, t.StatusCode), t.AcceptedAtUtc != null, t.AcceptedAtUtc,
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
                    // en una ruta de recogida el envío sigue «Sometido» (nunca salió de ahí): solo sale de la ruta; en una de entrega vuelve a «Sin asignar»
                    if (trip.Kind != TripKinds.Pickup)
                    {
                        var to = await statuses.AdvanceSkippingAsync(StatusDomains.OrderStatus, EntityTypes.TransportOrder, orderId, order.StatusCodeId, new[] { OrderStatuses.Unassigned },
                            DriverAppRules.DeclinedComment(Label(reason.LabelJson, tenant.Lang, reason.InternalCode)), ct2);
                        order.StatusCodeId = to.StatusCodeId;
                    }
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

    // ================================================================ entregar / no entregar

    private sealed record OwnShipment(int OrderId, Guid OrderPublicId, string OrderNumber, Guid TripPublicId, bool Accepted, int TripId, string TripKind);

    /// <summary>Quién cierra el envío: el chofer (con su ficha, para el intento y el cobro) o una persona del personal desde la app móvil (Lote 214: sin chofer; queda su nombre en el historial).</summary>
    private sealed record Actor(int? DriverId, string Name);

    private static Actor ActorOf(Domain.Fleet.Driver driver) => new(driver.DriverId, driver.FullName);

    private async Task<Actor> StaffActorAsync(CancellationToken ct)
    {
        var userId = ((TenantContext)tenant).RequireUserId();
        var u = await db.Users.AsNoTracking().Where(x => x.Id == userId).Select(x => new { x.FullName, x.Email }).FirstOrDefaultAsync(ct);
        return new Actor(null, string.IsNullOrWhiteSpace(u?.FullName) ? (u?.Email ?? $"usuario {userId}") : u!.FullName!);
    }

    /// <summary>
    /// Lote 214 — cualquier orden activa de la compañía (para el personal con orders.deliver): con su ruta vigente si la tiene (para cerrar la parada y saber si es de recogida);
    /// sin ruta, una orden en su etapa inicial se trata como recogida y las demás como entrega. 404 si no existe.
    /// </summary>
    private async Task<OwnShipment> AnyShipmentAsync(Guid orderPublicId, CancellationToken ct)
    {
        var o = await db.TransportOrders.AsNoTracking().Where(x => x.PublicId == orderPublicId && x.IsActive).Select(x => new { x.TransportOrderId, x.PublicId, x.OrderNumber, x.StatusCodeId }).FirstOrDefaultAsync(ct)
                ?? throw new NotFoundException("Orden", null, true);
        var trip = await (from l in db.TripOrders.AsNoTracking() join t in db.Trips.AsNoTracking() on l.TripId equals t.TripId
                          where l.TransportOrderId == o.TransportOrderId && l.IsCurrent && t.IsActive
                          orderby t.TripId descending
                          select new { t.TripId, t.PublicId, t.Kind, t.AcceptedAtUtc }).FirstOrDefaultAsync(ct);
        string kind;
        if (trip is not null) kind = trip.Kind;
        else
        {
            // Sin ruta: en su etapa inicial se recoge; ya «Recogida» también cuenta como recogida (así el reintento de la cola sin señal responde AlreadyDone en vez de «es de entrega»).
            var pipeline = await statuses.GetPipelineAsync(StatusDomains.OrderStatus, includeDisabled: true, ct);
            var current = pipeline.FirstOrDefault(x => x.Id == o.StatusCodeId);
            kind = current?.IsInitial == true || string.Equals(current?.Code, OrderStatuses.Pickup, StringComparison.OrdinalIgnoreCase) ? TripKinds.Pickup : TripKinds.Delivery;
        }
        return new OwnShipment(o.TransportOrderId, o.PublicId, o.OrderNumber, trip?.PublicId ?? Guid.Empty, Accepted: true, trip?.TripId ?? 0, kind);
    }

    /// <summary>Lote 214 — el personal entrega cualquier orden con la misma evidencia del chofer (firma, fotos, GPS, cobros). Exige orders.deliver (lo pone el controlador).</summary>
    /// <summary>
    /// Quién queda a cuenta de lo que el personal cierra: con `credit` (por omisión) el chofer de la ruta vigente de la orden (su intento y su cobro cuentan para su liquidación); sin ruta o sin chofer, nadie.
    /// El nombre del historial es siempre el de la persona que lo cerró.
    /// </summary>
    private async Task<Actor> StaffActorForAsync(OwnShipment own, bool credit, CancellationToken ct)
    {
        var actor = await StaffActorAsync(ct);
        if (!credit || own.TripId == 0) return actor;
        var driverId = await db.Trips.AsNoTracking().Where(t => t.TripId == own.TripId).Select(t => t.DriverId).FirstOrDefaultAsync(ct);
        return actor with { DriverId = driverId };
    }

    /// <summary>Lote 214 — el personal entrega cualquier orden con la misma evidencia del chofer (firma, fotos, GPS, cobros). Exige orders.deliver (lo pone el controlador).</summary>
    public async Task<DriverShipmentResultDto> DeliverAnyAsync(Guid orderPublicId, DriverDeliverRequest? req, CancellationToken ct)
    {
        var own = await AnyShipmentAsync(orderPublicId, ct);
        return await DeliverCoreAsync(own, await StaffActorForAsync(own, req?.CreditToDriver ?? true, ct), req, ct);
    }

    public async Task<DriverShipmentResultDto> FailAnyAsync(Guid orderPublicId, DriverFailRequest? req, CancellationToken ct)
    {
        var own = await AnyShipmentAsync(orderPublicId, ct);
        return await FailCoreAsync(own, await StaffActorForAsync(own, req?.CreditToDriver ?? true, ct), req, ct);
    }

    public async Task<DriverShipmentResultDto> PickupAnyAsync(Guid orderPublicId, DriverPickupRequest? req, CancellationToken ct)
    {
        var own = await AnyShipmentAsync(orderPublicId, ct);
        return await PickupCoreAsync(own, await StaffActorForAsync(own, req?.CreditToDriver ?? true, ct), req, ct);
    }

    /// <summary>El envío debe estar en un manifiesto vigente del chofer (si no, 404 como si no existiera) y el manifiesto ya aceptado (422).</summary>
    private async Task<OwnShipment> OwnShipmentAsync(Guid shipmentPublicId, Domain.Fleet.Driver driver, bool requireAccepted, CancellationToken ct)
    {
        var x = await (from l in db.TripOrders.AsNoTracking()
                       join o in db.TransportOrders.AsNoTracking() on l.TransportOrderId equals o.TransportOrderId
                       join t in db.Trips.AsNoTracking() on l.TripId equals t.TripId
                       where l.IsCurrent && o.PublicId == shipmentPublicId && t.DriverId == driver.DriverId && t.IsActive
                       select new OwnShipment(o.TransportOrderId, o.PublicId, o.OrderNumber, t.PublicId, t.AcceptedAtUtc != null, t.TripId, t.Kind)).FirstOrDefaultAsync(ct)
                ?? throw new NotFoundException("Envío", null, false);
        if (requireAccepted && !x.Accepted) throw new StatusRuleException(DriverAppRules.NotAcceptedMessage);
        return x;
    }

    /// <summary>Los motivos de «No entregado» (lista «Razón de fallo» de la compañía).</summary>
    public async Task<IReadOnlyList<DriverFailureReasonDto>> ListFailureReasonsAsync(CancellationToken ct)
        => (await lookups.GetDomainAsync(DeliveryAttemptCodes.FailureReasonDomain, ct)).Where(l => l.IsActive).OrderBy(l => l.SortOrder).ThenBy(l => l.InternalCode, StringComparer.Ordinal)
            .Select(l => new DriverFailureReasonDto(l.InternalCode, Label(l.LabelJson, tenant.Lang, l.InternalCode))).ToList();

    /// <summary>Un envío del chofer con lo que necesita en la puerta. 404 si no es suyo.</summary>
    public async Task<DriverShipmentDetailDto> GetShipmentAsync(Guid shipmentPublicId, CancellationToken ct)
    {
        var driver = await CurrentDriverAsync(ct);
        var own = await OwnShipmentAsync(shipmentPublicId, driver, requireAccepted: false, ct);
        return await ShipmentDetailCoreAsync(own, ct);
    }

    /// <summary>Lote 214 — la misma ficha «en la puerta» para cualquier orden activa de la compañía (el personal desde la app móvil). `ManifestPublicId` va vacío si la orden no está en una ruta vigente. 404 si no existe.</summary>
    public async Task<DriverShipmentDetailDto> GetAnyShipmentAsync(Guid orderPublicId, CancellationToken ct)
        => await ShipmentDetailCoreAsync(await AnyShipmentAsync(orderPublicId, ct), ct);

    private async Task<DriverShipmentDetailDto> ShipmentDetailCoreAsync(OwnShipment own, CancellationToken ct)
    {
        var x = await (from o in db.TransportOrders.AsNoTracking()
                       join st in db.StatusCodes.AsNoTracking() on o.StatusCodeId equals st.StatusCodeId
                       join os in db.OrderStops.AsNoTracking().Where(s => s.StopType!.InternalCode == (own.TripKind == TripKinds.Pickup ? StopTypes.Pickup : StopTypes.Delivery)) on o.TransportOrderId equals os.TransportOrderId into stops
                       from os in stops.DefaultIfEmpty()
                       where o.TransportOrderId == own.OrderId
                       select new { o.PublicId, o.OrderNumber, o.WaybillNumber, o.TotalPieces, o.CodAmount, o.ClientId, StatusCode = st.InternalCode, st.LabelJson,
                                    Name = os != null ? os.SnapName : null,
                                    Line1 = os != null ? os.SnapLine1 : null, City = os != null ? os.SnapCity : null, Postal = os != null ? os.SnapPostalCode : null, Notes = os != null ? os.Notes : null }).FirstAsync(ct);
        // El teléfono de contacto de entrega es un ContactPoint de la orden (el principal primero); su etiqueta dice quién es.
        var phone = await db.ContactPoints.AsNoTracking().Where(c => c.OwnerEntity!.InternalCode == EntityTypes.TransportOrder && c.OwnerId == own.OrderId && c.IsActive
                                                                      && (c.ContactType!.InternalCode == "PHONE" || c.ContactType.InternalCode == "MOBILE"))
            .OrderByDescending(c => c.IsPrimary).ThenBy(c => c.ContactPointId).Select(c => new { c.Value, c.Label }).FirstOrDefaultAsync(ct);
        var client = await db.Clients.AsNoTracking().Where(c => c.ClientId == x.ClientId).Select(c => c.Name).FirstOrDefaultAsync(ct);
        var flags = await (from o in db.TransportOrders.AsNoTracking() join cl in db.Clients.AsNoTracking() on o.ClientId equals cl.ClientId where o.TransportOrderId == own.OrderId
                           select new { cl.RequireDeliverySignature, cl.RequireDeliveryPhoto }).FirstOrDefaultAsync(ct) ?? new { RequireDeliverySignature = false, RequireDeliveryPhoto = false };
        decimal? codPending = null;
        if (x.CodAmount is decimal ca && ca > 0)
        {
            var collected = await (from c in db.Set<Teikem.Domain.Cod.CodCollection>().AsNoTracking() join st2 in db.StatusCodes.AsNoTracking() on c.StatusCodeId equals st2.StatusCodeId
                                   where c.TransportOrderId == own.OrderId && st2.InternalCode != Teikem.Domain.Cod.CodCollectionStatuses.Void select (decimal?)c.AmountCollected).SumAsync(ct) ?? 0m;
            codPending = Math.Max(0m, ca - collected);
        }
        var canClose = own.Accepted && string.Equals(x.StatusCode, own.TripKind == TripKinds.Pickup ? OrderStatuses.Submitted : OrderStatuses.InTransit, StringComparison.OrdinalIgnoreCase);
        return new DriverShipmentDetailDto(x.PublicId, own.TripPublicId, x.OrderNumber, x.WaybillNumber, x.StatusCode, Label(x.LabelJson, tenant.Lang, x.StatusCode), x.Name, phone?.Label, phone?.Value,
            x.Line1 ?? string.Empty, x.City, x.Postal, x.TotalPieces, x.CodAmount, x.Notes, client, canClose,
            flags.RequireDeliverySignature, flags.RequireDeliveryPhoto, codPending, own.TripKind);
    }

    private static byte[] DecodeImage(string base64, string message)
    {
        try
        {
            var bytes = Convert.FromBase64String(base64.Contains(',') ? base64[(base64.IndexOf(',') + 1)..] : base64);
            if (bytes.Length == 0) throw new FormatException();
            return bytes;
        }
        catch (FormatException) { throw new ValidationException("file", message); }
    }

    private async Task AttachMissingAsync(string ownerEntity, int ownerId, string kind, IReadOnlyList<byte[]> items, string fileStem, CancellationToken ct)
    {
        if (items.Count == 0 || files is null) return;
        var have = (await files.ListAsync(ownerEntity, ownerId, ct, enforceOwnerRead: false)).Count(f => f.Kind == kind);
        for (var i = have; i < items.Count; i++)
            await files.AttachAsync(ownerEntity, ownerId, kind, $"{fileStem}-{i + 1}", null, items[i], ct, enforceOwnerWrite: false);
    }

    /// <summary>
    /// A cuántos metros de la dirección de entrega estaba el teléfono (línea recta). Null si no hay GPS (sin permiso o sin señal de satélite) o la dirección aún no tiene punto. El GPS del teléfono NO es la ubicación de la entrega:
    /// un chofer puede marcar entregas o excepciones desde su casa; por eso se guarda aparte y con la distancia, para que la oficina lo vea.
    /// </summary>
    private async Task<int?> StopDistanceAsync(int orderId, double? lat, double? lng, CancellationToken ct, string kind = TripKinds.Delivery)
    {
        if (lat is not double la || lng is not double lo || !DriverTrackingRules.ValidCoordinate(la, lo)) return null;
        var deliveryTypeId = await db.LookupCodes.AsNoTracking().Where(l => l.Entity == LookupDomains.StopType && l.InternalCode == (kind == TripKinds.Pickup ? StopTypes.Pickup : StopTypes.Delivery)).Select(l => l.LookupCodeId).FirstOrDefaultAsync(ct);
        var stop = await db.OrderStops.AsNoTracking().Where(s => s.TransportOrderId == orderId).OrderByDescending(s => s.StopTypeLookupId == deliveryTypeId).ThenByDescending(s => s.Sequence).FirstOrDefaultAsync(ct);
        if (stop is null) return null;
        var points = await db.StopPointsAsync(new[] { stop.OrderStopId }, ct);
        if (!points.TryGetValue(stop.OrderStopId, out var p)) return null;
        return (int)Math.Round(EtaCalculator.Haversine(la, lo, p.Lat, p.Lng) * 1000);
    }

    /// <summary>La intención «anotar el intento como hecho desde la app, por este chofer y con su motivo»: el efecto del estatus ya lo agregó (Source STATUS); aquí se le pone el origen, el chofer y el motivo.</summary>
    private async Task TagAttemptAsync(int orderId, int? driverId, bool delivered, int? reasonId, string? note, DateTime at, double? lat, double? lng, int? distance, CancellationToken ct)
    {
        var a = await db.Set<DeliveryAttempt>().Where(x => x.TransportOrderId == orderId && x.IsActive && x.Delivered == delivered && x.Source == DeliveryAttemptSources.Status)
            .OrderByDescending(x => x.AttemptNumber).FirstOrDefaultAsync(ct);
        if (a is null) return;
        a.Source = DeliveryAttemptSources.App;
        a.DriverId = driverId;
        a.AttemptedAtUtc = at;
        a.FailureReasonLookupId = reasonId;
        if (lat is double la && lng is double lo) (a.Latitude, a.Longitude) = (DeliveryProofRules.Round6(la), DeliveryProofRules.Round6(lo));
        a.DistanceFromStopMeters = distance;
        if (!string.IsNullOrWhiteSpace(note)) a.Notes = note.Length > DeliveryAttemptRules.MaxNotesLength ? note[..DeliveryAttemptRules.MaxNotesLength] : note;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Entregar un envío del chofer: la orden pasa a Entregada (con el comprobante: quién recibió, firma y fotos, ubicación del teléfono) y se anota el intento a nombre del chofer. Idempotente: si la app ya lo entregó
    /// (reintento de la cola sin señal) responde `AlreadyDone` y solo completa la firma y fotos que falten. 404 si el envío no es suyo; 422 si el manifiesto no se ha aceptado o el envío ya está cerrado de otra forma;
    /// 400 por el nombre de quien recibió, la firma o las fotos.
    /// </summary>
    public async Task<DriverShipmentResultDto> DeliverAsync(Guid shipmentPublicId, DriverDeliverRequest? req, CancellationToken ct)
    {
        var driver = await CurrentDriverAsync(ct);
        return await DeliverCoreAsync(await OwnShipmentAsync(shipmentPublicId, driver, requireAccepted: true, ct), ActorOf(driver), req, ct);
    }

    private async Task<DriverShipmentResultDto> DeliverCoreAsync(OwnShipment own, Actor actor, DriverDeliverRequest? req, CancellationToken ct)
    {
        if (delivery is null) throw new InvalidOperationException("Falta DeliveryRegistrationService.");
        var shipmentPublicId = own.OrderPublicId;
        if (own.TripKind == TripKinds.Pickup) throw new StatusRuleException(DriverAppRules.WrongKindForDeliverMessage);
        var photos = req?.PhotosBase64 ?? Array.Empty<string>();
        var flags = await (from o in db.TransportOrders.AsNoTracking() join cl in db.Clients.AsNoTracking() on o.ClientId equals cl.ClientId where o.TransportOrderId == own.OrderId
                           select new { cl.RequireDeliverySignature, cl.RequireDeliveryPhoto }).FirstOrDefaultAsync(ct) ?? new { RequireDeliverySignature = false, RequireDeliveryPhoto = false };
        if (photos.Count > DriverAppRules.MaxPhotos) throw new ValidationException("photosBase64", DriverAppRules.TooManyPhotosMessage);
        var signature = string.IsNullOrWhiteSpace(req?.SignaturePngBase64) ? null : DecodeImage(req!.SignaturePngBase64!, DriverAppRules.SignatureInvalidMessage);
        var photoBytes = photos.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => DecodeImage(p, DriverAppRules.PhotoInvalidMessage)).ToList();

        var already = await delivery.FindByOrderAsync(shipmentPublicId, ct);
        // Firma y foto obligatorias por cliente (solo la primera vez: un reintento ya tiene lo suyo).
        if (already is null && flags.RequireDeliverySignature && signature is null) throw new ValidationException("signaturePngBase64", DriverAppRules.SignatureRequiredMessage);
        if (already is null && flags.RequireDeliveryPhoto && photoBytes.Count == 0) throw new ValidationException("photosBase64", DriverAppRules.PhotoRequiredMessage);
        if (already is not null && already.Source != DeliveryProofCodes.SourceDriverApp) throw new ConflictException(string.Format(DeliveryProofRules.AlreadyRegisteredFormat, own.OrderNumber));
        var at = req?.DeliveredAtUtc is DateTime given ? DateTime.SpecifyKind(given, DateTimeKind.Utc) : DateTime.UtcNow;
        // Cobros contra entrega: antes de registrar la entrega y con una referencia propia por cobro (APP:{id}), así un reintento no cobra dos veces.
        if (req?.Cods is { Count: > 0 } cods)
        {
            if (cod is null) throw new InvalidOperationException("Falta CodService.");
            foreach (var c in cods)
            {
                var reference = $"APP:{c.ClientId:N}";
                if (await db.Set<Teikem.Domain.Cod.CodCollection>().AsNoTracking().AnyAsync(x => x.TransportOrderId == own.OrderId && x.Reference == reference, ct)) continue;
                await cod.CollectAsync(shipmentPublicId, new CodCollectionRequest(c.Method, c.Amount, c.CheckNumber, reference, null, req.DeliveredAtUtc), ct);
                var made = await db.Set<Teikem.Domain.Cod.CodCollection>().AsTracking().FirstAsync(x => x.TransportOrderId == own.OrderId && x.Reference == reference, ct);
                made.DriverId = actor.DriverId;
                await db.SaveChangesAsync(ct);
            }
        }
        // El GPS del teléfono se guarda SIEMPRE (aunque el chofer haya apagado el seguimiento de la ruta) y aparte de la dirección: se anota a cuántos metros de ella estaba.
        var distance = already is null ? await StopDistanceAsync(own.OrderId, req?.Latitude, req?.Longitude, ct) : null;
        if (already is null)
        {
            await delivery.RegisterAsync(shipmentPublicId, new DeliveryRegisterRequest(req?.RecipientName, req?.Notes, req?.DeliveredAtUtc), ct,
                new DeliveryRegisterOptions(DeliveryProofCodes.SourceDriverApp, DeliveryProofCodes.SignatureRecipient, DriverAppRules.DeliveredComment(actor.Name), req?.Latitude, req?.Longitude, distance));
            await TagAttemptAsync(own.OrderId, actor.DriverId, delivered: true, reasonId: null, req?.Notes, at, req?.Latitude, req?.Longitude, distance, ct);
            await CloseStopAsync(own, completed: true, DriverAppRules.DeliveredComment(actor.Name), at, ct);
        }
        var proofId = await delivery.RequireProofIdAsync(shipmentPublicId, ct);
        if (signature is not null) await AttachMissingAsync(DeliveryProofCodes.EntityType, proofId, StoredFileCodes.KindSignature, new[] { signature }, "firma", ct);
        await AttachMissingAsync(DeliveryProofCodes.EntityType, proofId, StoredFileCodes.KindDeliveryPhoto, photoBytes, "foto", ct);
        var st = await StatusOfAsync(own.OrderId, ct);
        return new DriverShipmentResultDto(shipmentPublicId, st.Code, st.Label, already is not null, distance);
    }

    private async Task<(string Code, string Label)> StatusOfAsync(int orderId, CancellationToken ct)
    {
        var s = await (from o in db.TransportOrders.AsNoTracking() join c in db.StatusCodes.AsNoTracking() on o.StatusCodeId equals c.StatusCodeId where o.TransportOrderId == orderId select new { c.InternalCode, c.LabelJson }).FirstAsync(ct);
        return (s.InternalCode, Label(s.LabelJson, tenant.Lang, s.InternalCode));
    }

    /// <summary>
    /// No entregar un envío: pasa a «Fallida» con el motivo de la lista «Razón de fallo» y queda el intento fallido a nombre del chofer. Idempotente (si ya está Fallida por la app responde `AlreadyDone`).
    /// 400 motivo faltante/ inválido, nota larga o foto inválida; 404 envío ajeno; 422 manifiesto sin aceptar o envío en otro estatus.
    /// </summary>
    public async Task<DriverShipmentResultDto> FailAsync(Guid shipmentPublicId, DriverFailRequest? req, CancellationToken ct)
    {
        var driver = await CurrentDriverAsync(ct);
        return await FailCoreAsync(await OwnShipmentAsync(shipmentPublicId, driver, requireAccepted: true, ct), ActorOf(driver), req, ct);
    }

    private async Task<DriverShipmentResultDto> FailCoreAsync(OwnShipment own, Actor actor, DriverFailRequest? req, CancellationToken ct)
    {
        var shipmentPublicId = own.OrderPublicId;
        var code = req?.ReasonCode?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(code)) throw new ValidationException("reasonCode", DriverAppRules.FailureReasonRequiredMessage);
        var reason = (await lookups.GetDomainAsync(DeliveryAttemptCodes.FailureReasonDomain, ct)).FirstOrDefault(l => l.IsActive && string.Equals(l.InternalCode, code, StringComparison.OrdinalIgnoreCase))
                     ?? throw new ValidationException("reasonCode", DriverAppRules.FailureReasonInvalidMessage);
        var note = string.IsNullOrWhiteSpace(req?.Notes) ? null : req!.Notes!.Trim();
        if (note is { Length: > DeliveryAttemptRules.MaxNotesLength }) throw new ValidationException("notes", DriverAppRules.FailureNoteTooLongMessage);
        var photo = string.IsNullOrWhiteSpace(req?.PhotoBase64) ? null : DecodeImage(req!.PhotoBase64!, DriverAppRules.PhotoInvalidMessage);
        var at = req?.AttemptedAtUtc is DateTime given ? DateTime.SpecifyKind(given, DateTimeKind.Utc) : DateTime.UtcNow;

        // Ruta de RECOGIDA: «no recogido». El envío sigue «Sometido» (no hubo recogida), la parada queda FALLIDA con el motivo en su historial y la oficina lo ve ahí; no hay intento de entrega.
        if (own.TripKind == TripKinds.Pickup)
        {
            var pickupStatus = await StatusOfAsync(own.OrderId, ct);
            var pickupDistance = await StopDistanceAsync(own.OrderId, req?.Latitude, req?.Longitude, ct, TripKinds.Pickup);
            var comment = DriverAppRules.PickupFailedComment(Label(reason.LabelJson, tenant.Lang, reason.InternalCode), note, pickupDistance);
            await CloseStopAsync(own, completed: false, comment, at, ct);
            if (photo is not null) await AttachMissingAsync(EntityTypes.TransportOrder, own.OrderId, StoredFileCodes.KindDamagePhoto, new[] { photo }, "no-recogido", ct);
            return new DriverShipmentResultDto(shipmentPublicId, pickupStatus.Code, pickupStatus.Label, false, pickupDistance);
        }

        var current = await StatusOfAsync(own.OrderId, ct);
        var already = string.Equals(current.Code, OrderStatuses.Failed, StringComparison.OrdinalIgnoreCase)
                      && await db.Set<DeliveryAttempt>().AsNoTracking().AnyAsync(a => a.TransportOrderId == own.OrderId && a.IsActive && !a.Delivered && a.Source == DeliveryAttemptSources.App, ct);
        var distance = already ? null : await StopDistanceAsync(own.OrderId, req?.Latitude, req?.Longitude, ct);
        if (!already)
        {
            await db.RunInTransactionAsync(async ct2 =>
            {
                var order = (await db.LockOrdersAsync(new List<int> { own.OrderId }, includeStops: false, ct2)).First();
                var to = await statuses.TransitionAsync(StatusDomains.OrderStatus, EntityTypes.TransportOrder, own.OrderId, order.StatusCodeId, OrderStatuses.Failed,
                    DriverAppRules.FailedComment(Label(reason.LabelJson, tenant.Lang, reason.InternalCode)), ct2);
                order.StatusCodeId = to.StatusCodeId;
                await db.SaveGuardedAsync(DbExtensions.ConcurrencyMessage, ct2);
                await TagAttemptAsync(own.OrderId, actor.DriverId, delivered: false, reason.LookupCodeId, note, at, req?.Latitude, req?.Longitude, distance, ct2);
                await CloseStopAsync(own, completed: false, DriverAppRules.FailedComment(Label(reason.LabelJson, tenant.Lang, reason.InternalCode)), at, ct2);
            }, ct);
        }
        if (photo is not null) await AttachMissingAsync(EntityTypes.TransportOrder, own.OrderId, StoredFileCodes.KindDamagePhoto, new[] { photo }, "no-entrega", ct);
        var st = await StatusOfAsync(own.OrderId, ct);
        return new DriverShipmentResultDto(shipmentPublicId, st.Code, st.Label, already, distance);
    }

    /// <summary>
    /// Cierra la parada del envío en la ruta del chofer: la parada de la RUTA (RouteStop) queda COMPLETED o FAILED (con la hora real) y la parada del envío (OrderStop) pasa por el motor de estatus, con el texto en su historial.
    /// Así el monitor muestra el avance real y se sabe qué paradas faltan. Si el motor no permite ese paso (un estatus deshabilitado, por ejemplo), la entrega o recogida ya hecha no se deshace: solo se omite el historial de la parada.
    /// </summary>
    private async Task CloseStopAsync(OwnShipment own, bool completed, string comment, DateTime at, CancellationToken ct)
    {
        var routeId = await db.Routes.AsNoTracking().Where(r => r.TripId == own.TripId && r.IsActive).Select(r => (int?)r.RouteId).FirstOrDefaultAsync(ct);
        if (routeId is null) return;
        var link = await (from rs in db.RouteStops.AsTracking()
                          join os in db.OrderStops.AsTracking() on rs.OrderStopId equals os.OrderStopId
                          where rs.RouteId == routeId && os.TransportOrderId == own.OrderId
                          select new { Stop = rs, OrderStop = os }).FirstOrDefaultAsync(ct);
        if (link is null) return;
        var routeStop = await statuses.GetByCodeAsync(StatusDomains.RouteStopStatus, completed ? RouteStopStatuses.Completed : RouteStopStatuses.Failed, ct);
        link.Stop.StatusCodeId = routeStop.StatusCodeId;
        link.Stop.ActualArrivalUtc ??= at;
        link.Stop.ActualDepartureUtc = at;
        try
        {
            if (completed)
            {
                var pipeline = await statuses.GetPipelineAsync(StatusDomains.StopStatus, includeDisabled: true, ct);
                var current = pipeline.FirstOrDefault(x => x.Id == link.OrderStop.StatusCodeId);
                if (current is not null && !string.Equals(current.Code, StopStatuses.Completed, StringComparison.OrdinalIgnoreCase))
                {
                    var stages = pipeline.Select(x => new PipelineStage(x.Code, x.SortOrder, x.StageKind, x.IsInitial, x.IsEnabled)).ToList();
                    var steps = PipelinePath.StepsTo(stages, current.Code, StopStatuses.Completed);
                    if (steps.Count > 0 && steps[^1] == StopStatuses.Completed)
                    {
                        var to = await statuses.AdvanceSkippingAsync(StatusDomains.StopStatus, EntityTypes.OrderStop, link.OrderStop.OrderStopId, link.OrderStop.StatusCodeId, steps, comment, ct);
                        link.OrderStop.StatusCodeId = to.StatusCodeId;
                    }
                }
            }
            else
            {
                var to = await statuses.TransitionAsync(StatusDomains.StopStatus, EntityTypes.OrderStop, link.OrderStop.OrderStopId, link.OrderStop.StatusCodeId, StopStatuses.Failed, comment, ct);
                link.OrderStop.StatusCodeId = to.StatusCodeId;
            }
        }
        catch (Exception ex) when (ex is StatusRuleException or InvalidOperationException) { /* el historial de la parada es secundario */ }
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Recoger un envío de una ruta de RECOGIDA: el envío pasa de «Sometido» a «Recogido» (con quién lo entregó, notas, fotos y el GPS del teléfono a cuántos metros de la dirección de recogida), y la parada se cierra.
    /// Idempotente (si ya salió de «Sometido» responde `AlreadyDone`). 404 si el envío no es suyo; 422 si el manifiesto no se aceptó, es una ruta de entrega o el envío ya no se puede recoger; 400 por fotos o notas.
    /// </summary>
    public async Task<DriverShipmentResultDto> PickupAsync(Guid shipmentPublicId, DriverPickupRequest? req, CancellationToken ct)
    {
        var driver = await CurrentDriverAsync(ct);
        return await PickupCoreAsync(await OwnShipmentAsync(shipmentPublicId, driver, requireAccepted: true, ct), ActorOf(driver), req, ct);
    }

    private async Task<DriverShipmentResultDto> PickupCoreAsync(OwnShipment own, Actor actor, DriverPickupRequest? req, CancellationToken ct)
    {
        var shipmentPublicId = own.OrderPublicId;
        if (own.TripKind != TripKinds.Pickup) throw new StatusRuleException(DriverAppRules.WrongKindForPickupMessage);
        var notes = string.IsNullOrWhiteSpace(req?.Notes) ? null : req!.Notes!.Trim();
        if (notes is { Length: > DeliveryProofRules.MaxNotesLength }) throw new ValidationException("notes", DeliveryProofRules.NotesTooLong);
        var giver = string.IsNullOrWhiteSpace(req?.GiverName) ? null : req!.GiverName!.Trim();
        if (giver is { Length: > DeliveryProofRules.MaxRecipientLength }) throw new ValidationException("giverName", DeliveryProofRules.RecipientTooLong);
        var photos = (req?.PhotosBase64 ?? Array.Empty<string>()).Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
        if (photos.Count > DriverAppRules.MaxPhotos) throw new ValidationException("photosBase64", DriverAppRules.TooManyPhotosMessage);
        var photoBytes = photos.Select(x => DecodeImage(x, DriverAppRules.PhotoInvalidMessage)).ToList();
        var at = req?.PickedAtUtc is DateTime given ? DateTime.SpecifyKind(given, DateTimeKind.Utc) : DateTime.UtcNow;
        if (at > DateTime.UtcNow + DeliveryProofRules.FutureTolerance) throw new ValidationException("pickedAtUtc", DeliveryProofRules.FutureDelivery);

        var before = await StatusOfAsync(own.OrderId, ct);
        var already = !string.Equals(before.Code, OrderStatuses.Submitted, StringComparison.OrdinalIgnoreCase);
        int? distance = null;
        if (!already)
        {
            distance = await StopDistanceAsync(own.OrderId, req?.Latitude, req?.Longitude, ct, TripKinds.Pickup);
            await db.RunInTransactionAsync(async ct2 =>
            {
                var order = (await db.LockOrdersAsync(new List<int> { own.OrderId }, includeStops: false, ct2)).First();
                var pipeline = await statuses.GetPipelineAsync(StatusDomains.OrderStatus, includeDisabled: true, ct2);
                var current = pipeline.First(x => x.Id == order.StatusCodeId);
                if (!current.IsInitial) throw new StatusRuleException(DriverAppRules.NotPickableMessage(order.OrderNumber));
                var stages = pipeline.Select(x => new PipelineStage(x.Code, x.SortOrder, x.StageKind, x.IsInitial, x.IsEnabled)).ToList();
                var steps = PipelinePath.StepsTo(stages, current.Code, OrderStatuses.Pickup);
                if (steps.Count == 0 || steps[^1] != OrderStatuses.Pickup) throw new StatusRuleException(DriverAppRules.PickupStageMissingMessage);
                var to = await statuses.AdvanceSkippingAsync(StatusDomains.OrderStatus, EntityTypes.TransportOrder, own.OrderId, order.StatusCodeId, steps, DriverAppRules.PickedComment(actor.Name, giver, distance), ct2);
                order.StatusCodeId = to.StatusCodeId;
                order.ConfirmedAtUtc ??= at;
                await db.SaveGuardedAsync(DbExtensions.ConcurrencyMessage, ct2);
                await CloseStopAsync(own, completed: true, DriverAppRules.PickedComment(actor.Name, giver, distance), at, ct2);
            }, ct);
        }
        await AttachMissingAsync(EntityTypes.TransportOrder, own.OrderId, StoredFileCodes.KindDeliveryPhoto, photoBytes, "recogida", ct);
        var st = await StatusOfAsync(own.OrderId, ct);
        return new DriverShipmentResultDto(shipmentPublicId, st.Code, st.Label, already, distance);
    }
}
