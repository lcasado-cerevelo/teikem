using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Catalogs;
using Teikem.Domain.Common;
using Teikem.Domain.Constants;
using Teikem.Domain.Orders;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Clients;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Orders;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 3 (P3): lectura de órdenes de transporte. Ficha con capacidades por estatus, listado paginado con filtros
/// (Empaque como primera columna, siempre con valor) y lookup exacto para el escaneo de los tres identificadores.
/// - Todo pasa por OrderScope: el operador interno usa OrderScope.Any y el portal (módulo 10) fijará el ClientId del
///   principal; una orden de otro cliente del scope responde 404 (sin oráculo) igual que una de otro tenant.
/// - Todas las lecturas quedan bajo el filtro global de tenant: las paradas y las líneas (sin TenantId) se alcanzan
///   SOLO a través de la orden resuelta (ResolveOrderForReadAsync / SelectMany sobre ScopedOrders); nada se consulta
///   por id suelto.
/// - Las etiquetas salen de ILookupCache + MultilingualText; los estatus de un mapa por dominio (OrderStatus,
///   StopStatus, CodStatus) con la etiqueta personalizada del tenant si existe.
/// - CodType siempre es null en este lote: se define al entregar (11B/POD).
/// - Lote 5 (P7): la ficha interna (OrderScope.Any) de una orden no especial muestra su ruta vigente y el chofer de esa ruta;
///   LookupAsync no cambia (lo reutiliza el escaneo Outbound).
/// </summary>
public sealed class OrderReadService(TeikemDbContext db, ITenantContext tenant, ILookupCache lookups, StatusService statuses)
{
    public const string MatchedByOrderNumber = "ORDER_NUMBER";
    public const string MatchedByPackBatch = "PACK_BATCH";
    public const string MatchedByInvoice = "INVOICE";

    private sealed record StatusInfo(string Code, string Label, bool IsInitial, string StageKind);

    // ---------------------------------------------------------------- ficha

    public async Task<OrderDetailDto> GetAsync(Guid publicId, OrderScope scope, CancellationToken ct)
    {
        // 404 'Orden' también cuando la orden es de otro cliente del scope (sin oráculo).
        var order = await db.ResolveOrderForReadAsync(publicId, scope, ct);

        var client = await db.Clients.AsNoTracking().FirstOrDefaultAsync(c => c.ClientId == order.ClientId, ct)
                     ?? throw new NotFoundException("Orden");

        Guid? contractPublicId = null;
        if (order.ContractId is int contractId)
            contractPublicId = await db.Contracts.AsNoTracking().Where(c => c.ContractId == contractId).Select(c => (Guid?)c.PublicId).FirstOrDefaultAsync(ct);

        string? specialServiceName = null;
        if (order.SpecialServiceId is int specialServiceId)
            specialServiceName = await db.SpecialServices.AsNoTracking()
                .Where(s => s.SpecialServiceId == specialServiceId).Select(s => s.Type!.Name).FirstOrDefaultAsync(ct);

        var orderStatuses = await LoadStatusesAsync(StatusDomains.OrderStatus, ct);
        var stopStatuses = await LoadStatusesAsync(StatusDomains.StopStatus, ct);
        var codStatuses = await LoadStatusesAsync(StatusDomains.CodStatus, ct);

        var status = orderStatuses.GetValueOrDefault(order.StatusCodeId) ?? new StatusInfo("", "", false, "");
        var codStatus = order.CodStatusCodeId is int codStatusId ? codStatuses.GetValueOrDefault(codStatusId) : null;

        // Paradas: PublicId de la Location de origen (bajo el filtro global) y etiquetas de tipo/estatus.
        var stops = order.Stops.OrderBy(s => s.Sequence).ThenBy(s => s.OrderStopId).ToList();
        var locationIds = stops.Where(s => s.LocationId.HasValue).Select(s => s.LocationId!.Value).Distinct().ToList();
        var locationPublicIds = locationIds.Count == 0
            ? new Dictionary<int, Guid>()
            : await db.Locations.AsNoTracking().Where(l => locationIds.Contains(l.LocationId)).ToDictionaryAsync(l => l.LocationId, l => l.PublicId, ct);

        OrderStopDto? pickup = null, delivery = null;
        foreach (var stop in stops)
        {
            var stopType = await lookups.GetAsync(stop.StopTypeLookupId, ct);
            var typeCode = stopType?.InternalCode ?? "";
            var dto = ToStopDto(stop, typeCode, stop.LocationId is int locId ? locationPublicIds.GetValueOrDefault(locId) : null, stopStatuses);
            if (typeCode.Equals(StopTypes.Pickup, StringComparison.OrdinalIgnoreCase)) pickup ??= dto;
            else if (typeCode.Equals(StopTypes.Delivery, StringComparison.OrdinalIgnoreCase)) delivery ??= dto;
        }
        if (delivery is null) throw new InvalidOperationException($"La orden {order.OrderNumber} no tiene parada de entrega.");

        var packages = await ToPackageDtosAsync(order.CargoLines, ct);
        var references = new List<OrderReferenceDto>();
        foreach (var r in order.References.OrderBy(r => r.OrderReferenceId))
        {
            var refType = await lookups.GetAsync(r.RefTypeLookupId, ct);
            references.Add(new OrderReferenceDto(r.OrderReferenceId, refType?.InternalCode ?? "", Label(refType), r.RefValue, r.Source));
        }

        var serviceType = await lookups.GetAsync(order.ServiceTypeLookupId, ct);
        var priority = order.PriorityLookupId is int priorityId ? await lookups.GetAsync(priorityId, ct) : null;
        var currency = order.CurrencyLookupId is int currencyId ? await lookups.GetAsync(currencyId, ct) : null;
        var sourceType = order.SourceEntityTypeLookupId is int sourceTypeId ? await lookups.GetAsync(sourceTypeId, ct) : null;

        // Capacidades: regla de negocio por estatus (StatusCapability, tenant pisa default) + estado del registro.
        var canEditCargo = order.IsActive && await statuses.IsAllowedAsync(EntityTypes.TransportOrder, order.StatusCodeId, Capabilities.EditCargo, ct);
        var canReprice = order.QuotedAmount is not null && await statuses.IsAllowedAsync(EntityTypes.TransportOrder, order.StatusCodeId, Capabilities.Reprice, ct);
        var canCancel = order.IsActive && status.StageKind != StageKinds.Terminal
                        && await statuses.IsAllowedAsync(EntityTypes.TransportOrder, order.StatusCodeId, Capabilities.Cancel, ct);
        var capabilities = new OrderCapabilitiesDto(
            CanEditCargo: canEditCargo,
            CanCancel: canCancel,
            CanDelete: status.IsInitial && order.IsActive,
            CanConfirm: status.IsInitial && order.IsActive,
            CanReprice: canReprice);

        // Lote 4 (P7): chofer asignado = el del DriverTrip vigente (IsActive = 1) de la orden; solo identidad, nunca montos.
        var orderId = order.TransportOrderId;
        var assigned = !order.IsSpecialDelivery
            ? null
            : await (from t in db.DriverTrips.AsNoTracking()
                     join d in db.Drivers.AsNoTracking() on t.DriverId equals d.DriverId
                     where t.TransportOrderId == orderId && t.IsActive
                     orderby t.DriverTripId descending
                     select new { d.PublicId, d.EmployeeCode, d.FullName })
                .FirstOrDefaultAsync(ct);

        // Lote 5 (P7): ruta vigente (TripOrder IsCurrent) de una orden NO especial y el chofer de esa ruta. Solo para el
        // operador interno (OrderScope.Any): el portal no recibe nada nuevo. Solo identidad, nunca montos.
        Guid? assignedTripPublicId = null;
        string? assignedTripCode = null;
        if (!order.IsSpecialDelivery && scope.ClientId is null)
        {
            var trip = await (from to in db.TripOrders.AsNoTracking()
                              join t in db.Trips.AsNoTracking() on to.TripId equals t.TripId
                              join d in db.Drivers.AsNoTracking() on t.DriverId equals (int?)d.DriverId into dj
                              from d in dj.DefaultIfEmpty()
                              where to.TransportOrderId == orderId && to.IsCurrent
                              orderby t.TripId descending
                              select new
                              {
                                  t.PublicId, t.Code,
                                  DriverPublicId = d == null ? (Guid?)null : d.PublicId,
                                  DriverCode = d == null ? null : d.EmployeeCode,
                                  DriverName = d == null ? null : d.FullName,
                              })
                .FirstOrDefaultAsync(ct);
            if (trip is not null)
            {
                assignedTripPublicId = trip.PublicId;
                assignedTripCode = trip.Code;
                if (trip.DriverPublicId is Guid driverPublicId)
                    assigned = new { PublicId = driverPublicId, EmployeeCode = trip.DriverCode!, FullName = trip.DriverName! };
            }
        }

        return new OrderDetailDto(
            order.TransportOrderId, order.PublicId, order.OrderNumber, order.ClientInvoiceNumber, order.PackBatchNumber,
            client.PublicId, client.Name, contractPublicId,
            serviceType?.InternalCode ?? "", Label(serviceType),
            priority?.InternalCode,
            status.Code, status.Label, status.IsInitial,
            currency?.InternalCode,
            order.QuotedAmount, order.QuotedAtUtc, order.ConfirmedAtUtc,
            order.CodAmount, codStatus?.Code, codStatus?.Label,
            CodType: null,
            order.TotalPieces ?? packages.Sum(p => p.Pieces), order.TotalWeightKg, order.TotalVolumeM3,
            PackagesSummaryOf(order.IsSpecialDelivery, specialServiceName, packages),
            pickup, delivery, packages, references,
            order.IsSpecialDelivery, order.SpecialServiceId, specialServiceName,
            sourceType?.InternalCode, order.SourceEntityId,
            order.RequestedDate, order.PromisedDate, order.Notes,
            capabilities,
            order.IsActive, order.CreatedAtUtc, order.UpdatedAtUtc,
            Convert.ToBase64String(order.RowVersion ?? Array.Empty<byte>()),
            AssignedDriverPublicId: assigned?.PublicId,
            AssignedDriverCode: assigned?.EmployeeCode,
            AssignedDriverName: assigned?.FullName,
            AssignedTripPublicId: assignedTripPublicId,
            AssignedTripCode: assignedTripCode);
    }

    // ---------------------------------------------------------------- listado paginado

    public async Task<OrderPageDto> GetListAsync(OrderListQuery q, OrderScope scope, CancellationToken ct)
    {
        var (skip, take) = OrderRules.NormalizePaging(q.Skip, q.Take);

        var query = db.ScopedOrders(scope).AsNoTracking();
        if (!q.IncludeInactive) query = query.Where(o => o.IsActive);

        if (q.ClientPublicId is Guid clientPublicId)
        {
            var client = await db.ResolveClientAsync(clientPublicId, ct); // 404 si no es del tenant
            // El scope ya fija otro cliente: 404 'Cliente' sin revelar que existe.
            if (scope.ClientId is int scopedClientId && scopedClientId != client.ClientId) throw new NotFoundException("Cliente");
            var clientId = client.ClientId;
            query = query.Where(o => o.ClientId == clientId);
        }

        if (!string.IsNullOrWhiteSpace(q.Status))
        {
            var code = q.Status.Trim().ToUpperInvariant();
            var statusId = await db.StatusCodes.AsNoTracking()
                .Where(s => s.Entity == StatusDomains.OrderStatus && s.InternalCode == code)
                .Select(s => (int?)s.StatusCodeId).FirstOrDefaultAsync(ct)
                ?? throw new ValidationException("status", $"Estatus desconocido: {q.Status.Trim()}.");
            query = query.Where(o => o.StatusCodeId == statusId);
        }

        // Búsqueda parcial (LIKE) sobre los identificadores, documento L806.
        if (!string.IsNullOrWhiteSpace(q.OrderNumber)) { var v = q.OrderNumber.Trim(); query = query.Where(o => o.OrderNumber.Contains(v)); }
        if (!string.IsNullOrWhiteSpace(q.Invoice)) { var v = q.Invoice.Trim(); query = query.Where(o => o.ClientInvoiceNumber.Contains(v)); }
        if (!string.IsNullOrWhiteSpace(q.PackBatch)) { var v = q.PackBatch.Trim(); query = query.Where(o => o.PackBatchNumber.Contains(v)); }

        var deliveryTypeId = await lookups.GetIdAsync(LookupDomains.StopType, StopTypes.Delivery, ct);
        if (!string.IsNullOrWhiteSpace(q.Consignee))
        {
            var v = q.Consignee.Trim();
            query = query.Where(o => o.Stops.Any(s => s.StopTypeLookupId == deliveryTypeId && s.SnapName != null && s.SnapName.Contains(v)));
        }

        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var v = q.Search.Trim();
            query = query.Where(o => o.OrderNumber.Contains(v)
                                     || o.ClientInvoiceNumber.Contains(v)
                                     || o.PackBatchNumber.Contains(v)
                                     || o.Stops.Any(s => s.StopTypeLookupId == deliveryTypeId && s.SnapName != null && s.SnapName.Contains(v)));
        }

        if (q.FromUtc is DateTime from) query = query.Where(o => o.CreatedAtUtc >= from);   // desde inclusivo
        if (q.ToUtc is DateTime to) query = query.Where(o => o.CreatedAtUtc < to);           // hasta exclusivo

        var total = await query.CountAsync(ct);
        var page = await query.OrderByDescending(o => o.CreatedAtUtc).ThenByDescending(o => o.TransportOrderId)
            .Skip(skip).Take(take).ToListAsync(ct);

        return new OrderPageDto(total, await ToListItemsAsync(page, ct));
    }

    // ---------------------------------------------------------------- lookup exacto (escaneo)

    /// <summary>
    /// Coincidencia EXACTA (igualdad bajo la colación CI de la BD) sobre órdenes activas del scope: primero número de
    /// orden, luego número de empaque, luego factura del cliente. Nunca parcial: sin coincidencias el front cae al buscador.
    /// El número de orden puede repetirse entre clientes y la factura entre órdenes (se devuelven todas); el empaque es único.
    /// </summary>
    public async Task<OrderLookupDto> LookupAsync(string code, OrderScope scope, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(code)) throw new ValidationException("code", "Indique el código a buscar.");
        var c = code.Trim();

        var candidates = await db.ScopedOrders(scope).AsNoTracking()
            .Where(o => o.IsActive && (o.OrderNumber == c || o.PackBatchNumber == c || o.ClientInvoiceNumber == c))
            .OrderByDescending(o => o.CreatedAtUtc).ThenByDescending(o => o.TransportOrderId)
            .ToListAsync(ct);

        // La BD ya comparó sin distinguir mayúsculas: la clasificación en memoria usa la misma regla.
        var byOrder = candidates.Where(o => string.Equals(o.OrderNumber, c, StringComparison.OrdinalIgnoreCase)).ToList();
        var byPackBatch = candidates.Where(o => string.Equals(o.PackBatchNumber, c, StringComparison.OrdinalIgnoreCase)).ToList();
        var byInvoice = candidates.Where(o => string.Equals(o.ClientInvoiceNumber, c, StringComparison.OrdinalIgnoreCase)).ToList();

        string? matchedBy = null;
        var matches = new List<TransportOrder>();
        if (byOrder.Count > 0) { matchedBy = MatchedByOrderNumber; matches = byOrder; }
        else if (byPackBatch.Count > 0) { matchedBy = MatchedByPackBatch; matches = byPackBatch; }
        else if (byInvoice.Count > 0) { matchedBy = MatchedByInvoice; matches = byInvoice; }

        return new OrderLookupDto(c, matchedBy, await ToListItemsAsync(matches, ct));
    }

    // ---------------------------------------------------------------- mapeo del listado

    /// <summary>
    /// Filas del listado a partir de órdenes ya cargadas (misma secuencia de entrada). Paradas DELIVERY y líneas activas
    /// se leen en dos consultas por ids a través de las órdenes del scope (sin N+1 y sin salir del filtro de tenant);
    /// nombres de cliente y de servicio especial en una consulta cada uno.
    /// </summary>
    private async Task<IReadOnlyList<OrderListItemDto>> ToListItemsAsync(List<TransportOrder> orders, CancellationToken ct)
    {
        if (orders.Count == 0) return Array.Empty<OrderListItemDto>();

        var ids = orders.Select(o => o.TransportOrderId).ToList();
        var deliveryTypeId = await lookups.GetIdAsync(LookupDomains.StopType, StopTypes.Delivery, ct);

        var deliveryStops = await db.TransportOrders.AsNoTracking()
            .Where(o => ids.Contains(o.TransportOrderId))
            .SelectMany(o => o.Stops)
            .Where(s => s.StopTypeLookupId == deliveryTypeId)
            .ToListAsync(ct);
        var deliveryByOrder = deliveryStops.GroupBy(s => s.TransportOrderId)
            .ToDictionary(g => g.Key, g => g.OrderBy(s => s.Sequence).ThenBy(s => s.OrderStopId).First());

        var lines = await db.TransportOrders.AsNoTracking()
            .Where(o => ids.Contains(o.TransportOrderId))
            .SelectMany(o => o.CargoLines)
            .Where(l => l.IsActive)
            .ToListAsync(ct);
        var linesByOrder = lines.GroupBy(l => l.TransportOrderId).ToDictionary(g => g.Key, g => g.ToList());

        var clientIds = orders.Select(o => o.ClientId).Distinct().ToList();
        var clients = await db.Clients.AsNoTracking().Where(c => clientIds.Contains(c.ClientId))
            .Select(c => new { c.ClientId, c.PublicId, c.Name }).ToDictionaryAsync(c => c.ClientId, ct);

        var specialIds = orders.Where(o => o.SpecialServiceId.HasValue).Select(o => o.SpecialServiceId!.Value).Distinct().ToList();
        var specialNames = specialIds.Count == 0
            ? new Dictionary<int, string>()
            : await db.SpecialServices.AsNoTracking().Where(s => specialIds.Contains(s.SpecialServiceId))
                .Select(s => new { s.SpecialServiceId, Name = s.Type!.Name }).ToDictionaryAsync(s => s.SpecialServiceId, s => s.Name, ct);

        var orderStatuses = await LoadStatusesAsync(StatusDomains.OrderStatus, ct);

        var items = new List<OrderListItemDto>(orders.Count);
        foreach (var o in orders)
        {
            var client = clients.GetValueOrDefault(o.ClientId);
            var delivery = deliveryByOrder.GetValueOrDefault(o.TransportOrderId);
            var packages = await ToPackageDtosAsync(linesByOrder.GetValueOrDefault(o.TransportOrderId) ?? new List<CargoLine>(), ct);
            var serviceType = await lookups.GetAsync(o.ServiceTypeLookupId, ct);
            var status = orderStatuses.GetValueOrDefault(o.StatusCodeId) ?? new StatusInfo("", "", false, "");
            var specialName = o.SpecialServiceId is int ssId ? specialNames.GetValueOrDefault(ssId) : null;

            items.Add(new OrderListItemDto(
                o.TransportOrderId, o.PublicId,
                o.PackBatchNumber, o.OrderNumber, o.ClientInvoiceNumber,
                client?.PublicId ?? Guid.Empty, client?.Name ?? "",
                delivery?.SnapName ?? "", delivery?.SnapCity ?? "", delivery?.SnapPostalCode,
                serviceType?.InternalCode ?? "", Label(serviceType),
                PackagesSummaryOf(o.IsSpecialDelivery, specialName, packages),
                o.TotalPieces ?? packages.Sum(p => p.Pieces),
                o.CodAmount, o.QuotedAmount,
                status.Code, status.Label, status.IsInitial,
                o.IsSpecialDelivery, o.IsActive, o.CreatedAtUtc,
                packages));
        }
        return items;
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>Líneas activas de la orden como paquetes (etiqueta del tipo desde el catálogo; una entrega especial trae PackageType null).</summary>
    private async Task<IReadOnlyList<OrderPackageLineDto>> ToPackageDtosAsync(IEnumerable<CargoLine> cargoLines, CancellationToken ct)
    {
        var result = new List<OrderPackageLineDto>();
        foreach (var l in cargoLines.Where(l => l.IsActive).OrderBy(l => l.CargoLineId))
        {
            var packageType = l.PackageTypeLookupId is int typeId ? await lookups.GetAsync(typeId, ct) : null;
            result.Add(new OrderPackageLineDto(
                l.CargoLineId,
                packageType?.InternalCode,
                packageType is null ? null : Label(packageType),
                l.PackageNumber,
                l.Description,
                (int)l.Quantity,
                l.WeightKg,
                l.VolumeM3));
        }
        return result;
    }

    /// <summary>'Caja ×2 + Sobre ×1' consolidado por tipo; una entrega especial devuelve el nombre del servicio.</summary>
    private static string PackagesSummaryOf(bool isSpecialDelivery, string? specialServiceName, IReadOnlyList<OrderPackageLineDto> packages)
    {
        if (isSpecialDelivery)
            return specialServiceName ?? packages.FirstOrDefault()?.Description ?? "";
        return OrderRules.PackagesSummary(packages.Select(p => (Label: p.PackageTypeLabel ?? p.PackageType ?? p.Description, Pieces: p.Pieces)));
    }

    private OrderStopDto ToStopDto(OrderStop s, string stopTypeCode, Guid? locationPublicId, Dictionary<int, StatusInfo> stopStatuses)
    {
        var status = stopStatuses.GetValueOrDefault(s.StatusCodeId) ?? new StatusInfo("", "", false, "");
        return new OrderStopDto(
            s.OrderStopId, stopTypeCode, s.Sequence, locationPublicId,
            s.SnapName, s.SnapLine1, s.SnapLine2, s.SnapCity, s.SnapState, s.SnapPostalCode, s.SnapCountryCode,
            s.WindowStartUtc, s.WindowEndUtc, s.ServiceMinutes, s.Notes,
            status.Code, status.Label);
    }

    /// <summary>
    /// Mapa id → (código, etiqueta, inicial, StageKind) de un dominio de estatus. La etiqueta respeta el override del
    /// tenant (StatusCodeOverride.CustomLabelJson, bajo el filtro global) igual que el pipeline de /status.
    /// </summary>
    private async Task<Dictionary<int, StatusInfo>> LoadStatusesAsync(string domain, CancellationToken ct)
    {
        var codes = await db.StatusCodes.AsNoTracking().Include(s => s.StageKind).Where(s => s.Entity == domain).ToListAsync(ct);
        if (codes.Count == 0) return new Dictionary<int, StatusInfo>();
        var ids = codes.Select(c => c.StatusCodeId).ToList();
        var customLabels = tenant.TenantId is null
            ? new Dictionary<int, string?>()
            : await db.StatusCodeOverrides.AsNoTracking()
                .Where(o => ids.Contains(o.StatusCodeId) && o.CustomLabelJson != null)
                .ToDictionaryAsync(o => o.StatusCodeId, o => o.CustomLabelJson, ct);
        return codes.ToDictionary(
            c => c.StatusCodeId,
            c => new StatusInfo(
                c.InternalCode,
                MultilingualText.Resolve(MultilingualText.Merge(c.LabelJson, customLabels.GetValueOrDefault(c.StatusCodeId)), tenant.Lang),
                c.IsInitial,
                c.StageKind?.InternalCode ?? ""));
    }

    private string Label(LookupCode? code) => MultilingualText.Resolve(code?.LabelJson, tenant.Lang);
}
