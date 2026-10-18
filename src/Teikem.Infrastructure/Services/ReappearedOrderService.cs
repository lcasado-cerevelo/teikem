using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Billing;
using Teikem.Domain.Catalogs;
using Teikem.Domain.Common;
using Teikem.Domain.Constants;
using Teikem.Domain.Orders;
using Teikem.Domain.Trips;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Orders;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 111b — la ORDEN NUEVA de las piezas que aparecieron (dictado por Luis, 2026-10-18). Al cerrar el conteo de una orden, las piezas suyas que estaban perdidas o reclamadas y aparecieron se juntan en UNA orden de entrega nueva:
/// el mismo cliente y el mismo consignatario (copia de las paradas de recogida y entrega, el tipo de servicio y la prioridad), un conduce nuevo, y las MISMAS piezas con sus mismos números (se mudan de orden, así sus labels siguen valiendo).
/// La orden nueva repite primero el historial de la original (Sometida y, si existe, Recogida, con las MISMAS fechas) y después se le aplica el estatus de la acción con que se escaneó la pieza, como a cualquier otra orden (Enrutar también la asigna a una ruta).
/// Cobro: si el contrato cobra por recogido, la original ya se cobra completa y la nueva no se cobra de nuevo; si cobra por entrega (o mixto), la nueva se cotiza con sus piezas, se cobra cuando se entregue, y a la original se le quitan esas piezas para no cobrarlas dos veces.
/// </summary>
public sealed class ReappearedOrderService(TeikemDbContext db, ITenantContext tenant, StatusService statuses, INumberSequenceService sequences, OutboundScanService outbound, ILookupCache lookups, PartialBillingService partial, OrderQuoteService? quotes = null, ITenantClock? clock = null)
{
    private readonly ITenantClock _clock = clock ?? TenantClock.Default;

    public const string CreatedMessageFormat = "Se creó la orden {0} (conduce {1}) con {2} pieza(s) que aparecieron de la orden {3}.";

    /// <summary>Lo que va a pasar al cerrar el conteo de la orden, o null si no hay piezas que hayan aparecido.</summary>
    public async Task<ReappearedPreview?> PreviewAsync(int orderId, CancellationToken ct)
    {
        var pieces = await db.OrderPieces.AsNoTracking().Where(p => p.TransportOrderId == orderId && p.IsActive && p.ReappearedAtUtc != null).OrderBy(p => p.Sequence).ToListAsync(ct);
        if (pieces.Count == 0) return null;
        var order = await db.TransportOrders.AsNoTracking().FirstAsync(o => o.TransportOrderId == orderId, ct);
        var clientName = await db.Clients.AsNoTracking().Where(c => c.ClientId == order.ClientId).Select(c => c.Name).FirstOrDefaultAsync(ct);
        var consignee = await db.OrderStops.AsNoTracking().Where(s => s.TransportOrderId == orderId).OrderByDescending(s => s.Sequence).Select(s => s.SnapName).FirstOrDefaultAsync(ct);
        var target = await TargetAsync(pieces, ct);
        return new ReappearedPreview(clientName ?? string.Empty, consignee, target.Label, pieces.Select(p => p.PackageNumber).ToList());
    }

    /// <summary>Asegura los contadores del cliente (en autocommit, antes de abrir la transacción).</summary>
    public async Task EnsureSequencesAsync(int orderId, CancellationToken ct)
    {
        var clientId = await db.TransportOrders.AsNoTracking().Where(o => o.TransportOrderId == orderId).Select(o => o.ClientId).FirstAsync(ct);
        await sequences.EnsureAsync(NumberKinds.Order, NumberingRules.ScopeClientId(NumberKinds.Order, clientId), ct);
        await sequences.EnsureAsync(NumberKinds.Invoice, NumberingRules.ScopeClientId(NumberKinds.Invoice, clientId), ct);
        await sequences.EnsureAsync(NumberKinds.PackBatch, NumberingRules.ScopeClientId(NumberKinds.PackBatch, clientId), ct);
    }

    /// <summary>Crea la orden nueva con las piezas que aparecieron de la orden (dentro de la transacción del llamador). Null si no hay ninguna.</summary>
    public async Task<ReappearedOrderResult?> CreateAsync(int orderId, DateOnly? planDate, CancellationToken ct)
    {
        var tenantId = tenant.TenantId ?? throw new ForbiddenException("No hay tenant activo en la sesión.");
        var pieces = await db.OrderPieces.Where(p => p.TransportOrderId == orderId && p.IsActive && p.ReappearedAtUtc != null).OrderBy(p => p.Sequence).ToListAsync(ct);
        if (pieces.Count == 0) return null;
        var original = await db.TransportOrders.AsNoTracking().FirstAsync(o => o.TransportOrderId == orderId, ct);
        var client = await db.Clients.AsNoTracking().FirstOrDefaultAsync(c => c.ClientId == original.ClientId, ct);
        var clientId = original.ClientId;
        var stops = await db.OrderStops.AsNoTracking().Where(s => s.TransportOrderId == orderId).OrderBy(s => s.Sequence).ToListAsync(ct);
        var lineIds = pieces.Select(p => p.CargoLineId).Distinct().ToList();
        var lines = await db.CargoLines.AsNoTracking().Where(l => lineIds.Contains(l.CargoLineId)).OrderBy(l => l.CargoLineId).ToListAsync(ct);

        // números: el mismo trato que cualquier orden (orden, factura y empaque del cliente) y el conduce del día del consignatario
        var settings = client is null ? NumberingSettings.Default : NumberingSettings.FromClient(client);
        var orderNumber = Auto(NumberKinds.Order, settings, await sequences.NextAsync(NumberKinds.Order, NumberingRules.ScopeClientId(NumberKinds.Order, clientId), ct));
        var invoiceNumber = Auto(NumberKinds.Invoice, settings, await sequences.NextAsync(NumberKinds.Invoice, NumberingRules.ScopeClientId(NumberKinds.Invoice, clientId), ct));
        var packBatch = Auto(NumberKinds.PackBatch, settings, await sequences.NextAsync(NumberKinds.PackBatch, NumberingRules.ScopeClientId(NumberKinds.PackBatch, clientId), ct));
        var deliveryStop = stops.LastOrDefault();
        var consigneeCode = deliveryStop?.LocationId is int locId ? await db.Locations.AsNoTracking().Where(l => l.LocationId == locId).Select(l => l.Code).FirstOrDefaultAsync(ct) : null;
        var prefix = WaybillCodes.DayPrefix(PieceNumbering.Part(client?.Code, clientId), PieceNumbering.Part(consigneeCode, deliveryStop?.LocationId ?? 0), TimeZoneInfo.ConvertTimeFromUtc(_clock.UtcNow, _clock.Zone));
        var sameDay = await db.TransportOrders.AsNoTracking().Where(o => o.WaybillNumber != null && o.WaybillNumber.StartsWith(prefix)).Select(o => o.WaybillNumber).ToListAsync(ct);
        var waybill = WaybillCodes.Next(prefix, sameDay);

        var target = await TargetAsync(pieces, ct);
        var initial = await statuses.GetInitialAsync(StatusDomains.OrderStatus, ct);
        var weight = 0m;
        var volume = 0m;
        foreach (var l in lines)
        {
            var share = l.Quantity <= 0 ? 0m : pieces.Count(p => p.CargoLineId == l.CargoLineId) / l.Quantity;
            weight += (l.WeightKg ?? 0m) * share;
            volume += (l.VolumeM3 ?? 0m) * share;
        }
        var order = new TransportOrder
        {
            TenantId = tenantId,
            ClientId = original.ClientId,
            ContractId = original.ContractId,
            OrderNumber = orderNumber,
            ClientInvoiceNumber = invoiceNumber,
            ClientInvoiceNumberTyped = false,
            PackBatchNumber = packBatch,
            WaybillNumber = waybill,
            ServiceTypeLookupId = original.ServiceTypeLookupId,
            StatusCodeId = initial.StatusCodeId,
            PriorityLookupId = original.PriorityLookupId,
            RequestedDate = original.RequestedDate,
            PromisedDate = original.PromisedDate,
            TotalWeightKg = weight > 0 ? weight : null,
            TotalVolumeM3 = volume > 0 ? volume : null,
            TotalPieces = pieces.Count,
            CurrencyLookupId = original.CurrencyLookupId,
            IsSpecialDelivery = false,
            Notes = $"Orden nueva de las piezas que aparecieron de la orden {original.OrderNumber}.",
            IsActive = true,
        };
        db.TransportOrders.Add(order);
        await db.SaveChangesAsync(ct);
        // historial: primero lo que ya tenía la original, con las mismas fechas (Sometida y, si existe, Recogida); después se aplica el estatus del escaneo
        var entityTypeId = await lookups.GetIdAsync(LookupDomains.EntityType, EntityTypes.TransportOrder, ct);
        var pickupId = await db.StatusCodes.AsNoTracking().Where(x => x.Entity == StatusDomains.OrderStatus && x.InternalCode == OrderStatuses.Pickup).Select(x => (int?)x.StatusCodeId).FirstOrDefaultAsync(ct);
        var past = await db.EntityStatusHistories.AsNoTracking().Where(h => h.EntityTypeLookupId == entityTypeId && h.EntityId == orderId).OrderBy(h => h.EntityStatusHistoryId).ToListAsync(ct);
        var born = past.FirstOrDefault(h => h.FromStatusCodeId == null);
        var picked = pickupId is null ? null : past.FirstOrDefault(h => h.ToStatusCodeId == pickupId);
        db.EntityStatusHistories.Add(new EntityStatusHistory { TenantId = tenantId, EntityTypeLookupId = entityTypeId, EntityId = order.TransportOrderId, FromStatusCodeId = null, ToStatusCodeId = initial.StatusCodeId, ChangedAtUtc = born?.ChangedAtUtc ?? original.CreatedAtUtc, ChangedBy = born?.ChangedBy ?? tenant.UserId });
        var currentCode = initial.InternalCode;
        order.StatusCodeId = initial.StatusCodeId;
        if (picked is not null)
        {
            db.EntityStatusHistories.Add(new EntityStatusHistory { TenantId = tenantId, EntityTypeLookupId = entityTypeId, EntityId = order.TransportOrderId, FromStatusCodeId = initial.StatusCodeId, ToStatusCodeId = pickupId!.Value, ChangedAtUtc = picked.ChangedAtUtc, ChangedBy = picked.ChangedBy, IsSkipped = picked.IsSkipped });
            currentCode = OrderStatuses.Pickup;
            order.StatusCodeId = pickupId.Value;
            order.ConfirmedAtUtc = original.ConfirmedAtUtc ?? picked.ChangedAtUtc;
        }
        await db.SaveChangesAsync(ct);

        // paradas: las mismas (recogida y entrega) con su foto
        var stopInitial = await statuses.GetInitialAsync(StatusDomains.StopStatus, ct);
        var stopMap = new Dictionary<int, OrderStop>();
        foreach (var s in stops)
        {
            var copy = new OrderStop
            {
                TransportOrderId = order.TransportOrderId, StopTypeLookupId = s.StopTypeLookupId, Sequence = s.Sequence, LocationId = s.LocationId, SnapName = s.SnapName, SnapLine1 = s.SnapLine1, SnapLine2 = s.SnapLine2,
                SnapCity = s.SnapCity, SnapState = s.SnapState, SnapPostalCode = s.SnapPostalCode, SnapCountryCode = s.SnapCountryCode, GeocodeAccuracyLookupId = s.GeocodeAccuracyLookupId,
                WindowStartUtc = s.WindowStartUtc, WindowEndUtc = s.WindowEndUtc, ServiceMinutes = s.ServiceMinutes, StatusCodeId = stopInitial.StatusCodeId, Notes = s.Notes,
            };
            db.OrderStops.Add(copy);
            stopMap[s.OrderStopId] = copy;
        }
        await db.SaveChangesAsync(ct);
        foreach (var copy in stopMap.Values)
        {
            var to = await statuses.TransitionAsync(StatusDomains.StopStatus, EntityTypes.OrderStop, copy.OrderStopId, null, stopInitial.InternalCode, null, ct);
            copy.StatusCodeId = to.StatusCodeId;
        }

        // líneas: una por cada línea de origen que tenga piezas que aparecieron, con solo esas piezas
        var lineMap = new Dictionary<int, CargoLine>();
        foreach (var l in lines)
        {
            var mine = pieces.Where(p => p.CargoLineId == l.CargoLineId).ToList();
            var share = l.Quantity <= 0 ? 0m : mine.Count / l.Quantity;
            var line = new CargoLine
            {
                TransportOrderId = order.TransportOrderId,
                PickupStopId = l.PickupStopId is int ps && stopMap.TryGetValue(ps, out var np) ? np.OrderStopId : null,
                DeliveryStopId = l.DeliveryStopId is int ds && stopMap.TryGetValue(ds, out var nd) ? nd.OrderStopId : null,
                ProductId = l.ProductId, PackageTypeLookupId = l.PackageTypeLookupId, PackageNumber = mine.First().PackageNumber, Description = l.Description, Quantity = mine.Count, UomLookupId = l.UomLookupId,
                WeightKg = l.WeightKg is null ? null : l.WeightKg * share, VolumeM3 = l.VolumeM3 is null ? null : l.VolumeM3 * share, HandlingFlags = l.HandlingFlags, IsActive = true,
            };
            db.CargoLines.Add(line);
            lineMap[l.CargoLineId] = line;
        }
        await db.SaveChangesAsync(ct);

        // las piezas se mudan (mismos números), ya normales: dejan de estar «apareció» y perdidas/reclamadas
        var sequence = 0;
        foreach (var p in pieces)
        {
            p.TransportOrderId = order.TransportOrderId;
            p.CargoLineId = lineMap[p.CargoLineId].CargoLineId;
            p.Sequence = ++sequence;
            p.MissingStatus = null;
            p.MissingAtUtc = null;
            p.ClaimId = null;
            p.ReappearedAtUtc = null;
            p.ReappearedAction = null;
        }
        await db.SaveChangesAsync(ct);

        // cobro: por recogido no se cobra de nuevo (la original se cobra completa); por entrega la nueva se cotiza con sus piezas y a la original se le quitan para no cobrarlas dos veces
        await ApplyBillingAsync(order, original, ct);

        // al estatus de la acción con que se escanearon, como a cualquier otra orden
        var pipeline = await statuses.GetPipelineAsync(StatusDomains.OrderStatus, includeDisabled: true, ct);
        var stages = pipeline.Select(s => new PipelineStage(s.Code, s.SortOrder, s.StageKind, s.IsInitial, s.IsEnabled)).ToList();
        var steps = PipelinePath.StepsTo(stages, currentCode, target.Code);
        if (steps.Count > 0)
        {
            var to = await statuses.AdvanceSkippingAsync(StatusDomains.OrderStatus, EntityTypes.TransportOrder, order.TransportOrderId, order.StatusCodeId, steps, null, ct);
            order.StatusCodeId = to.StatusCodeId;
            if (steps.Contains(OrderStatuses.Pickup) && order.ConfirmedAtUtc is null) order.ConfirmedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }
        if (target.Code == OrderStatuses.Assigned)
        {
            // Enrutar: a una ruta abierta de su zona, igual que cualquier otra; si no hay quién la reciba no se cambia nada (el cierre del conteo se deshace)
            var route = await outbound.ScanAsync(new OutboundScanRequest(order.WaybillNumber, planDate), ct);
            if (route.Outcome is not (ScanOutcomes.FoundAssigned or ScanOutcomes.AlreadyAssigned)) throw new StatusRuleException(route.Message);
        }
        return new ReappearedOrderResult(order.PublicId, order.OrderNumber, order.WaybillNumber, original.OrderNumber, pieces.Count, target.Label);
    }

    /// <summary>El estatus de la acción más avanzada con que se escanearon las piezas.</summary>
    private async Task<(string Code, string Label)> TargetAsync(IReadOnlyList<OrderPiece> pieces, CancellationToken ct)
    {
        var action = pieces.Select(p => p.ReappearedAction).Where(a => a is not null).OrderByDescending(a => ScanActions.Rank(a!)).FirstOrDefault() ?? ScanActions.Arrive;
        var code = ScanActions.TargetStatus(action);
        var pipeline = await statuses.GetPipelineAsync(StatusDomains.OrderStatus, includeDisabled: true, ct);
        return (code, pipeline.FirstOrDefault(s => s.Code == code)?.Label ?? code);
    }

    /// <summary>
    /// Por recogido: la nueva no se cotiza (nunca se factura; la original ya se cobra completa y el cliente reclama). Por entrega o mixto: si la original todavía no se facturó, la nueva se cotiza con sus piezas (se cobra al entregarse) y la original se
    /// recalcula sin esas piezas (<see cref="PartialBillingService"/>: lo que no se entrega en ella no se cobra en ella).
    /// </summary>
    private async Task ApplyBillingAsync(TransportOrder created, TransportOrder originalSnapshot, CancellationToken ct)
    {
        if (quotes is null) return;
        if (await partial.BillsByPickupAsync(originalSnapshot.ContractId, ct)) return;
        var invoiced = await db.TransportOrders.AsNoTracking().Where(o => o.TransportOrderId == originalSnapshot.TransportOrderId).Select(o => o.FreightInvoiced).FirstAsync(ct);
        if (invoiced) return;   // ya se le cobró todo a la original
        var client = await db.Clients.AsNoTracking().FirstOrDefaultAsync(c => c.ClientId == created.ClientId, ct);
        if (client is null) return;
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(_clock.UtcNow, _clock.Zone));
        await quotes.ApplyQuoteAsync(created, client, today, ct);
        await partial.AdjustAsync(originalSnapshot.TransportOrderId, ct);
    }

    private static string Auto(string kind, NumberingSettings settings, long seq)
    {
        try { return NumberingRules.ResolveChecked(kind, NumberingRules.EffectivePattern(kind, settings), seq); }
        catch (ArgumentException ex) { throw new ConflictException(ex.Message); }
    }
}

/// <summary>La orden nueva creada con las piezas que aparecieron.</summary>
public sealed record ReappearedOrderResult(Guid PublicId, string OrderNumber, string WaybillNumber, string OriginalOrderNumber, int Pieces, string StatusLabel);

/// <summary>Lo que pasará con las piezas que aparecieron al cerrar el conteo: la orden nueva (cliente, consignatario, estatus) y sus piezas.</summary>
public sealed record ReappearedPreview(string ClientName, string? ConsigneeName, string StatusLabel, IReadOnlyList<string> Numbers);
