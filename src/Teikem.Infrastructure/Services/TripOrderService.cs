using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Constants;
using Teikem.Domain.Fleet;
using Teikem.Domain.Trips;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Clients;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Orders;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Trips;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 5 (P2): órdenes en la ruta (consolidación multi-cliente) y lista 'Sin asignar'.
/// - AddOrdersAsync: resuelve las órdenes del tenant (404 'Orden no encontrado.') y, dentro de una transacción, bloquea el Trip
///   (LockTripAsync), aplica el RowVersion y exige ruta editable; RouteWriter.AddOrdersAsync bloquea las órdenes por id
///   ascendente (paso 3 del orden de bloqueo del lote), re-verifica elegibilidad y ruta vigente bajo bloqueo y agrega todo o
///   nada. Asignar NO cambia el OrderStatus (DECISIÓN V2): 'sin asignar' = sin TripOrder vigente.
/// - RemoveOrderAsync: libera la orden (DELETE del TripOrder y de sus RouteStop de la versión vigente, L272), resecuencia 1..N y
///   recalcula ETAs. Una orden que no está en ESA ruta responde 404 'La orden no está en esta ruta.' (BOLA por id hijo).
/// - PoolAsync: núcleo compartido con 'Planificar el día' (P9): órdenes asignables sin ruta vigente, con zona resuelta.
/// - ListUnassignedAsync: la lista 'Sin asignar' con filtros; la búsqueda libre se aplica DESPUÉS de los filtros.
/// </summary>
public sealed class TripOrderService(
    TeikemDbContext db,
    ITenantContext tenant,
    ILookupCache lookups,
    StatusService statuses,
    RouteWriter writer,
    TripReadService reader)
{
    public const string ZoneLabel = "Zona de despacho";

    // ================================================================ POST /trips/{publicId}/orders

    public async Task<TripDetailDto> AddOrdersAsync(Guid tripPublicId, TripOrdersAddRequest req, CancellationToken ct)
    {
        ((TenantContext)tenant).RequireTenantId();
        var (publicIds, error) = TripOrderRequestRules.ValidateAdd(req?.OrderPublicIds);
        if (error is not null) throw new ValidationException("orderPublicIds", error);

        // Órdenes del tenant (filtro global) y activas; cualquier id ajeno o inexistente → 404 sin decir cuál (sin oráculo).
        var found = await db.TransportOrders.AsNoTracking()
            .Where(o => publicIds.Contains(o.PublicId) && o.IsActive)
            .Select(o => new { o.PublicId, o.TransportOrderId })
            .ToListAsync(ct);
        if (found.Count != publicIds.Count) throw new NotFoundException(OrderQueries.OrderLabel);

        // Orden de llegada de la solicitud = orden de las paradas nuevas al final de la ruta.
        var idByPublic = found.ToDictionary(o => o.PublicId, o => o.TransportOrderId);
        var orderIds = publicIds.Select(p => idByPublic[p]).ToList();

        await db.RunInTransactionAsync(async ct2 =>
        {
            var trip = await db.LockTripAsync(tripPublicId, ct2);        // 404 'Ruta no encontrada.'
            db.ApplyRowVersion(trip, req!.RowVersion);                     // 409 al guardar si cambió
            await writer.EnsureEditableAsync(trip, ct2);                   // 422 'La ruta … ya fue despachada …'
            await writer.AddOrdersAsync(trip, orderIds, ct2);              // 422 elegibilidad / 409 / 400 tope
            await db.SaveGuardedAsync(TripRules.OrderTakenMessage, ct2);   // carrera en UX_TripOrder_Current → 409
        }, ct);

        return await reader.GetAsync(tripPublicId, ct);
    }

    // ================================================================ DELETE /trips/{publicId}/orders/{orderPublicId}

    public async Task<TripDetailDto> RemoveOrderAsync(Guid tripPublicId, Guid orderPublicId, TripOrderRemoveRequest? req, CancellationToken ct)
    {
        ((TenantContext)tenant).RequireTenantId();

        await db.RunInTransactionAsync(async ct2 =>
        {
            var trip = await db.LockTripAsync(tripPublicId, ct2);        // 404 'Ruta no encontrada.'
            db.ApplyRowVersion(trip, req?.RowVersion);
            await writer.EnsureEditableAsync(trip, ct2);                   // 422 en rutas despachadas o cerradas

            var orderId = await db.TransportOrders.AsNoTracking()
                              .Where(o => o.PublicId == orderPublicId)
                              .Select(o => (int?)o.TransportOrderId)
                              .FirstOrDefaultAsync(ct2)
                          ?? throw new NotFoundException(OrderQueries.OrderLabel);   // 404 'Orden no encontrado.'

            await writer.ReleaseOrderAsync(trip, orderId, ct2);           // 404 'La orden no está en esta ruta.'
            await db.SaveGuardedAsync(DbExtensions.ConcurrencyMessage, ct2);
        }, ct);

        return await reader.GetAsync(tripPublicId, ct);
    }

    // ================================================================ pool reutilizable (P9)

    /// <summary>
    /// Órdenes asignables a una ruta que hoy no tienen ruta vigente: activas del tenant, no especiales, sin TripOrder vigente,
    /// con parada DELIVERY no terminal y en una etapa PIPELINE posterior a la inicial y anterior a IN_TRANSIT del pipeline del
    /// tenant (incluidas las etapas deshabilitadas, que pueden tener órdenes). RequestedUpTo: fecha solicitada ≤ ese día, o nula.
    /// Zona resuelta con DispatchZoneMatcher sobre los miembros de las zonas activas. Filtro de zona:
    /// ZoneIds null = cualquier zona; ZoneIds con valores = solo esas zonas (una lista vacía = ninguna zona);
    /// IncludeNoZone agrega las órdenes sin zona o con zona ambigua.
    /// IneligibleReason: el motivo de TripRules.CheckEligibility (hoy, la capacidad ASSIGN_TRIP del estatus) o null.
    /// Orden: fecha solicitada (sin fecha al final), número de orden. Consultas por lote, sin N+1.
    /// </summary>
    public async Task<IReadOnlyList<UnassignedCandidate>> PoolAsync(UnassignedPoolFilter filter, CancellationToken ct)
    {
        ((TenantContext)tenant).RequireTenantId();
        var rows = await LoadPoolAsync(filter ?? new UnassignedPoolFilter(null, null, true), withEligibility: true, ct);
        return rows.Select(r => new UnassignedCandidate(
            r.TransportOrderId, r.PublicId, r.OrderNumber, r.ZoneId, r.ZoneCode, r.ZoneAmbiguous,
            r.StatusCode, r.RequestedDate, r.IneligibleReason)).ToList();
    }

    // ================================================================ GET /trips/unassigned-orders

    public async Task<UnassignedOrderPageDto> ListUnassignedAsync(UnassignedOrdersQuery? query, CancellationToken ct)
    {
        ((TenantContext)tenant).RequireTenantId();
        var q = query ?? new UnassignedOrdersQuery();
        var check = TripOrderRequestRules.NormalizeUnassignedQuery(q.DispatchZoneId, q.NoZone, q.RequestedFrom, q.RequestedTo, q.Skip, q.Take);
        if (!check.IsValid) throw new ValidationException(check.Field!, check.Error!);

        UnassignedPoolFilter filter;
        if (q.DispatchZoneId is int zoneId)
        {
            // La zona debe ser del tenant (filtro global): ajena o inexistente → 404 sin oráculo.
            if (!await db.DispatchZones.AsNoTracking().AnyAsync(z => z.DispatchZoneId == zoneId, ct))
                throw new NotFoundException(ZoneLabel, null, true);
            filter = new UnassignedPoolFilter(null, new[] { zoneId }, IncludeNoZone: false);
        }
        else if (q.NoZone) filter = new UnassignedPoolFilter(null, Array.Empty<int>(), IncludeNoZone: true);
        else filter = new UnassignedPoolFilter(null, null, IncludeNoZone: true);

        int? clientId = null;
        if (q.ClientPublicId is Guid clientPublicId)
            clientId = (await db.ResolveClientAsync(clientPublicId, ct)).ClientId;   // 404 'Cliente no encontrado.'

        // ASSIGN_TRIP no esconde órdenes de la lista: se valida al agregar.
        var rows = await LoadPoolAsync(filter, withEligibility: false, ct);

        IEnumerable<PoolRow> filtered = rows;
        if (clientId is int cid) filtered = filtered.Where(r => r.ClientId == cid);
        if (!string.IsNullOrWhiteSpace(q.PostalCode)) filtered = filtered.Where(r => TripOrderRequestRules.MatchesPostalPrefix(r.PostalCode, q.PostalCode));
        if (!string.IsNullOrWhiteSpace(q.City)) filtered = filtered.Where(r => TripOrderRequestRules.MatchesCity(r.City, q.City));
        if (q.RequestedFrom.HasValue || q.RequestedTo.HasValue)
            filtered = filtered.Where(r => TripOrderRequestRules.InRequestedRange(r.RequestedDate, q.RequestedFrom, q.RequestedTo));
        // Búsqueda libre DESPUÉS de los filtros, sobre número, empaque, factura y consignatario (sin acentos ni mayúsculas).
        if (!string.IsNullOrWhiteSpace(q.Search))
            filtered = filtered.Where(r => FleetRules.MatchesSearch(q.Search, r.OrderNumber, r.PackBatchNumber, r.ClientInvoiceNumber, r.ConsigneeName));

        var all = filtered.ToList();
        var page = all.Skip(check.Skip).Take(check.Take).ToList();

        // Solo para la página: nombres de cliente, coordenadas (GEOGRAPHY vía TripQueries) y precisión de geocodificación.
        var clientIds = page.Select(r => r.ClientId).Distinct().ToList();
        var clientNames = clientIds.Count == 0
            ? new Dictionary<int, string>()
            : await db.Clients.AsNoTracking().Where(c => clientIds.Contains(c.ClientId))
                .Select(c => new { c.ClientId, c.Name }).ToDictionaryAsync(c => c.ClientId, c => c.Name, ct);
        var points = await PointsAsync(page.Select(r => r.OrderStopId).Distinct().ToList(), ct);

        var items = new List<UnassignedOrderDto>(page.Count);
        foreach (var r in page)
        {
            var accuracy = r.GeocodeAccuracyLookupId is int accId ? await lookups.GetAsync(accId, ct) : null;
            items.Add(new UnassignedOrderDto(
                r.TransportOrderId, r.PublicId, r.OrderNumber, r.PackBatchNumber, r.ClientInvoiceNumber,
                clientNames.GetValueOrDefault(r.ClientId) ?? "", r.ConsigneeName,
                r.City, r.PostalCode,
                r.ZoneId, r.ZoneCode, r.ZoneAmbiguous,
                r.StatusCode, r.StatusLabel,
                r.RequestedDate, r.WindowStartUtc, r.WindowEndUtc,
                r.Pieces, r.WeightKg, r.VolumeM3,
                points.GetValueOrDefault(r.OrderStopId),
                accuracy?.InternalCode));
        }
        return new UnassignedOrderPageDto(all.Count, check.Skip, check.Take, items);
    }

    // ================================================================ núcleo del pool

    /// <summary>Fila interna del pool: lo que necesitan tanto PoolAsync (P9) como la lista 'Sin asignar'.</summary>
    private sealed record PoolRow(
        int TransportOrderId, Guid PublicId, string OrderNumber, string PackBatchNumber, string ClientInvoiceNumber, int ClientId,
        int OrderStopId, string? ConsigneeName, string City, string? PostalCode, DateTime? WindowStartUtc, DateTime? WindowEndUtc,
        int? GeocodeAccuracyLookupId, int? ZoneId, string? ZoneCode, bool ZoneAmbiguous,
        string StatusCode, string StatusLabel, DateTime? RequestedDate, int? Pieces, decimal? WeightKg, decimal? VolumeM3,
        string? IneligibleReason);

    private async Task<List<PoolRow>> LoadPoolAsync(UnassignedPoolFilter filter, bool withEligibility, CancellationToken ct)
    {
        // Etapas asignables según el pipeline del tenant (con deshabilitadas: una etapa apagada puede tener órdenes).
        var pipeline = await statuses.GetPipelineAsync(StatusDomains.OrderStatus, includeDisabled: true, ct);
        var inTransit = pipeline.FirstOrDefault(s => string.Equals(s.Code, OrderStatuses.InTransit, StringComparison.OrdinalIgnoreCase))
                        ?? throw new InvalidOperationException("El pipeline de órdenes no tiene la etapa IN_TRANSIT.");
        var initialSort = pipeline.Where(s => s.IsInitial).Select(s => (int?)s.SortOrder).Min() ?? int.MinValue;
        var assignable = pipeline
            .Where(s => string.Equals(s.StageKind, StageKinds.Pipeline, StringComparison.OrdinalIgnoreCase)
                        && !s.IsInitial && s.SortOrder > initialSort && s.SortOrder < inTransit.SortOrder)
            .ToDictionary(s => s.Id);
        if (assignable.Count == 0) return new List<PoolRow>();
        var statusIds = assignable.Keys.ToList();

        // Parada DELIVERY no terminal (la de menor secuencia es la que se rutea).
        var deliveryTypeId = await lookups.GetIdAsync(LookupDomains.StopType, StopTypes.Delivery, ct);
        var terminalStopIds = await db.StatusCodes.AsNoTracking()
            .Where(s => s.Entity == StatusDomains.StopStatus && s.StageKind!.InternalCode == StageKinds.Terminal)
            .Select(s => s.StatusCodeId).ToListAsync(ct);

        var query = db.TransportOrders.AsNoTracking()
            .Where(o => o.IsActive && !o.IsSpecialDelivery && statusIds.Contains(o.StatusCodeId))
            .Where(o => !db.TripOrders.Any(t => t.TransportOrderId == o.TransportOrderId && t.IsCurrent))
            .Where(o => o.Stops.Any(s => s.StopTypeLookupId == deliveryTypeId && !terminalStopIds.Contains(s.StatusCodeId)));
        if (filter.RequestedUpTo is DateOnly upTo)
        {
            var limit = upTo.AddDays(1).ToDateTime(TimeOnly.MinValue);   // ≤ ese día (hasta exclusivo +1)
            query = query.Where(o => o.RequestedDate == null || o.RequestedDate < limit);
        }

        var raw = await query
            .Select(o => new
            {
                o.TransportOrderId, o.PublicId, o.OrderNumber, o.PackBatchNumber, o.ClientInvoiceNumber, o.ClientId,
                o.StatusCodeId, o.RequestedDate, o.TotalPieces, o.TotalWeightKg, o.TotalVolumeM3,
                Stop = o.Stops
                    .Where(s => s.StopTypeLookupId == deliveryTypeId && !terminalStopIds.Contains(s.StatusCodeId))
                    .OrderBy(s => s.Sequence).ThenBy(s => s.OrderStopId)
                    .Select(s => new { s.OrderStopId, s.SnapName, s.SnapCity, s.SnapPostalCode, s.WindowStartUtc, s.WindowEndUtc, s.GeocodeAccuracyLookupId })
                    .FirstOrDefault(),
            })
            .ToListAsync(ct);

        var resolveZone = await ZoneResolverAsync(ct);
        var zoneCache = new Dictionary<(string, string), ZoneHit>();
        var allowedByStatus = new Dictionary<int, bool>();
        var zoneFilter = filter.ZoneIds is null ? null : new HashSet<int>(filter.ZoneIds);

        var result = new List<PoolRow>(raw.Count);
        foreach (var o in raw)
        {
            if (o.Stop is null) continue;   // no ocurre (el WHERE lo exige); defensa ante datos a medias

            var key = (o.Stop.SnapPostalCode?.Trim() ?? "", o.Stop.SnapCity?.Trim() ?? "");
            if (!zoneCache.TryGetValue(key, out var zone))
            {
                zone = resolveZone(o.Stop.SnapPostalCode, o.Stop.SnapCity);
                zoneCache[key] = zone;
            }

            if (zone.ZoneId is int zid)
            {
                if (zoneFilter is not null && !zoneFilter.Contains(zid)) continue;
            }
            else if (!filter.IncludeNoZone) continue;

            var status = assignable[o.StatusCodeId];
            string? ineligible = null;
            if (withEligibility)
            {
                if (!allowedByStatus.TryGetValue(o.StatusCodeId, out var allowed))
                {
                    allowed = await statuses.IsAllowedAsync(EntityTypes.TransportOrder, o.StatusCodeId, Capabilities.AssignTrip, ct);
                    allowedByStatus[o.StatusCodeId] = allowed;
                }
                ineligible = TripRules.CheckEligibility(new OrderEligibilityInput(
                    o.OrderNumber, IsActive: true, IsSpecialDelivery: false, status.Code, status.Label, status.StageKind,
                    status.IsInitial, status.SortOrder, inTransit.SortOrder, allowed, HasPendingDelivery: true));
            }

            result.Add(new PoolRow(
                o.TransportOrderId, o.PublicId, o.OrderNumber, o.PackBatchNumber, o.ClientInvoiceNumber, o.ClientId,
                o.Stop.OrderStopId, o.Stop.SnapName, o.Stop.SnapCity ?? "", o.Stop.SnapPostalCode,
                o.Stop.WindowStartUtc, o.Stop.WindowEndUtc, o.Stop.GeocodeAccuracyLookupId,
                zone.ZoneId, zone.ZoneCode, zone.Ambiguous,
                status.Code, status.Label, o.RequestedDate, o.TotalPieces, o.TotalWeightKg, o.TotalVolumeM3,
                ineligible));
        }

        return result
            .OrderBy(r => r.RequestedDate is null)
            .ThenBy(r => r.RequestedDate)
            .ThenBy(r => r.OrderNumber, StringComparer.Ordinal)
            .ThenBy(r => r.TransportOrderId)
            .ToList();
    }

    // ================================================================ adaptadores a las costuras de P0 (TripQueries / DispatchZoneMatcher)

    /// <summary>Zona resuelta de una dirección: id y código si hay una sola zona ganadora; Ambiguous si empatan varias.</summary>
    private readonly record struct ZoneHit(int? ZoneId, string? ZoneCode, bool Ambiguous);

    /// <summary>
    /// Miembros de las zonas ACTIVAS del tenant cargados una sola vez (TripQueries.ActiveZoneMembersAsync) y resolución pura
    /// con DispatchZoneMatcher.Resolve (precedencia CP &gt; rango postal &gt; municipio sin acentos; empate = ambigua). El id
    /// sale del código de la zona ganadora (UQ_DispatchZone: código único por tenant), en una sola consulta.
    /// </summary>
    private async Task<Func<string?, string?, ZoneHit>> ZoneResolverAsync(CancellationToken ct)
    {
        var members = await db.ActiveZoneMembersAsync(ct);
        var idByCode = (await db.DispatchZones.AsNoTracking().Where(z => z.IsActive)
                .Select(z => new { z.DispatchZoneId, z.Code }).ToListAsync(ct))
            .GroupBy(z => z.Code, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Min(z => z.DispatchZoneId), StringComparer.OrdinalIgnoreCase);
        return (postalCode, city) =>
        {
            var r = DispatchZoneMatcher.Resolve(members, postalCode, city);
            if (r.Ambiguous) return new ZoneHit(null, null, true);
            if (string.IsNullOrEmpty(r.ZoneCode) || !idByCode.TryGetValue(r.ZoneCode, out var zoneId)) return new ZoneHit(null, null, false);
            return new ZoneHit(zoneId, r.ZoneCode, false);
        };
    }

    /// <summary>Coordenadas de las paradas (GEOGRAPHY, SQL crudo confinado en TripQueries.StopPointsAsync con TenantId explícito).</summary>
    private async Task<Dictionary<int, GeoPointDto>> PointsAsync(IReadOnlyCollection<int> orderStopIds, CancellationToken ct)
    {
        var result = new Dictionary<int, GeoPointDto>();
        if (orderStopIds.Count == 0) return result;
        var points = await db.StopPointsAsync(orderStopIds, ct);
        foreach (var id in orderStopIds)
            if (points.TryGetValue(id, out var p))
                result[id] = new GeoPointDto(p.Lat, p.Lng);
        return result;
    }
}
