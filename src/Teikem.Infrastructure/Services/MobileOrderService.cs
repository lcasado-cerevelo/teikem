using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Common;
using Teikem.Domain.Constants;
using Teikem.Domain.Files;
using Teikem.Domain.Fleet;
using Teikem.Domain.Orders;
using Teikem.Domain.Trips;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Trips;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 214 (2026-10-24, Luis) — órdenes en la app móvil del personal. Buscar como en la web (primero la coincidencia exacta del escáner con OrderReadService.LookupAsync; si no hay, búsqueda libre
/// acotada a 30) y la ficha «en la puerta» de cualquier orden activa, con su ruta vigente, comprobante e intentos. Completarla (entregar, no entregar, recoger) lo hace DriverAppService con la misma
/// evidencia del chofer, a nombre de la persona que lo hace (sin chofer en el intento).
/// </summary>
public sealed class MobileOrderService(TeikemDbContext db, ITenantContext tenant, OrderReadService reader, DriverAppService app, StatusService statuses, ILookupCache lookups, StoredFileService? files = null)
{
    public const int MaxRows = 30;

    public async Task<MobileOrderSearchDto> SearchAsync(string? q, CancellationToken ct)
    {
        var text = (q ?? string.Empty).Trim();
        if (text.Length == 0) throw new ValidationException("q", MobileOrderRules.QueryRequiredMessage);
        var exact = await reader.LookupAsync(text, OrderScope.Any, ct);
        if (exact.Matches.Count > 0) return new MobileOrderSearchDto(text, exact.MatchedBy, exact.Matches.Select(Row).ToList());
        var page = await reader.GetListAsync(new OrderListQuery(null, null, null, null, null, null, text, null, null, false, 0, MaxRows), OrderScope.Any, ct);
        return new MobileOrderSearchDto(text, null, page.Items.Select(Row).ToList());
    }

    private static MobileOrderRowDto Row(OrderListItemDto o)
        => new(o.PublicId, o.OrderNumber, o.WaybillNumber, o.PackBatchNumber, o.ClientName, o.ConsigneeName, o.ConsigneeCity, o.Status, o.StatusLabel, o.IsInitialStatus, o.TotalPieces, o.CodAmount);

    public async Task<MobileOrderDto> GetAsync(Guid publicId, CancellationToken ct)
    {
        var shipment = await app.GetAnyShipmentAsync(publicId, ct);
        var o = await (from x in db.TransportOrders.AsNoTracking()
                       join cl in db.Clients.AsNoTracking() on x.ClientId equals cl.ClientId into cls
                       from cl in cls.DefaultIfEmpty()
                       join svc in db.LookupCodes.AsNoTracking() on x.ServiceTypeLookupId equals svc.LookupCodeId into svcs
                       from svc in svcs.DefaultIfEmpty()
                       where x.PublicId == publicId
                       select new { x.TransportOrderId, x.StatusCodeId, ClientPublicId = cl != null ? cl.PublicId : Guid.Empty, x.PackBatchNumber, x.ClientInvoiceNumber, ServiceLabel = svc != null ? svc.LabelJson : null, x.CreatedAtUtc }).FirstAsync(ct);
        var pipeline = await statuses.GetPipelineAsync(StatusDomains.OrderStatus, includeDisabled: true, ct);
        var isInitial = pipeline.FirstOrDefault(s => s.Id == o.StatusCodeId)?.IsInitial == true;

        var trip = await (from l in db.TripOrders.AsNoTracking() join t in db.Trips.AsNoTracking() on l.TripId equals t.TripId
                          join st in db.StatusCodes.AsNoTracking() on t.StatusCodeId equals st.StatusCodeId
                          join d in db.Drivers.AsNoTracking() on t.DriverId equals d.DriverId into ds
                          from d in ds.DefaultIfEmpty()
                          where l.TransportOrderId == o.TransportOrderId && l.IsCurrent && t.IsActive
                          orderby t.TripId descending
                          select new { t.Code, st.LabelJson, StatusCode = st.InternalCode, Driver = d != null ? d.FullName : null }).FirstOrDefaultAsync(ct);

        // El punto de la parada (entrega o recogida según el tipo), para el mapa y «cómo llegar».
        var stopType = shipment.Kind == TripKinds.Pickup ? StopTypes.Pickup : StopTypes.Delivery;
        var stopId = await db.OrderStops.AsNoTracking().Where(s => s.TransportOrderId == o.TransportOrderId && s.StopType!.InternalCode == stopType).OrderByDescending(s => s.Sequence).Select(s => (int?)s.OrderStopId).FirstOrDefaultAsync(ct);
        double? lat = null, lng = null;
        if (stopId is int sid)
        {
            var points = await db.StopPointsAsync(new[] { sid }, ct);
            if (points.TryGetValue(sid, out var p)) (lat, lng) = (p.Lat, p.Lng);
        }

        var proofRow = await db.Set<DeliveryProof>().AsNoTracking().Where(p => p.TransportOrderId == o.TransportOrderId && p.IsActive).OrderByDescending(p => p.DeliveryProofId).FirstOrDefaultAsync(ct);
        MobileProofDto? proof = null;
        if (proofRow is not null)
        {
            var attached = files is null ? new List<StoredFileDto>() : (await files.ListAsync(DeliveryProofCodes.EntityType, proofRow.DeliveryProofId, ct, enforceOwnerRead: false)).ToList();
            proof = new MobileProofDto(proofRow.DeliveredAtUtc, proofRow.RecipientName, proofRow.Notes, proofRow.Source, proofRow.DistanceFromStopMeters,
                attached.Any(f => f.Kind == StoredFileCodes.KindSignature), attached.Count(f => f.Kind == StoredFileCodes.KindDeliveryPhoto));
        }

        var reasons = (await lookups.GetDomainAsync(DeliveryAttemptCodes.FailureReasonDomain, ct)).ToDictionary(r => r.LookupCodeId, r => MultilingualText.Resolve(r.LabelJson, tenant.Lang));
        var attempts = await (from a in db.Set<DeliveryAttempt>().AsNoTracking()
                              join d in db.Drivers.AsNoTracking() on a.DriverId equals d.DriverId into ds
                              from d in ds.DefaultIfEmpty()
                              where a.TransportOrderId == o.TransportOrderId && a.IsActive
                              orderby a.AttemptNumber
                              select new { a.AttemptNumber, a.AttemptedAtUtc, a.Delivered, a.FailureReasonLookupId, a.Notes, a.Source, Driver = d != null ? d.FullName : null, a.DistanceFromStopMeters }).ToListAsync(ct);
        var attemptDtos = attempts.Select(a => new MobileAttemptDto(a.AttemptNumber, a.AttemptedAtUtc, a.Delivered,
            a.FailureReasonLookupId is int rid && reasons.TryGetValue(rid, out var label) ? label : null, a.Notes, a.Source, a.Driver, a.DistanceFromStopMeters)).ToList();

        var code = shipment.StatusCode;
        var pickupKind = shipment.Kind == TripKinds.Pickup;
        // Entregar / no entregar: la orden va En tránsito (o Fallida: se puede volver a intentar). Recoger: en su etapa inicial. Las mismas reglas que aplica DriverAppService al ejecutar.
        var canDeliver = !pickupKind && (Is(code, OrderStatuses.InTransit) || Is(code, OrderStatuses.Failed));
        var canFail = pickupKind ? Is(code, OrderStatuses.Submitted) : Is(code, OrderStatuses.InTransit);
        var canPickup = pickupKind && isInitial;
        return new MobileOrderDto(shipment, o.ClientPublicId, o.PackBatchNumber, o.ClientInvoiceNumber, MultilingualText.Resolve(o.ServiceLabel, tenant.Lang), o.CreatedAtUtc, isInitial,
            trip?.Code, trip is null ? null : MultilingualText.Resolve(trip.LabelJson, tenant.Lang), trip?.Driver, lat, lng, canDeliver, canFail, canPickup, proof, attemptDtos);
    }

    private static bool Is(string code, string expected) => string.Equals(code, expected, StringComparison.OrdinalIgnoreCase);
}

public static class MobileOrderRules
{
    public const string QueryRequiredMessage = "Escriba o escanee un número de orden, conduce, empaque o factura.";
}
