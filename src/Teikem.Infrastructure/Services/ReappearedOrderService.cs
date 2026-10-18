using Microsoft.EntityFrameworkCore;
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
/// La orden nueva entra al estatus de la acción con que se escaneó la pieza (Enrutar queda en «Sin asignar»: no hay ruta que la reciba todavía). No se cotiza ni se cobra: es el mismo envío que ya se cotizó en la original.
/// </summary>
public sealed class ReappearedOrderService(TeikemDbContext db, ITenantContext tenant, StatusService statuses, INumberSequenceService sequences, ITenantClock? clock = null)
{
    private readonly ITenantClock _clock = clock ?? TenantClock.Default;

    public const string ConfirmMessageFormat = "Al cerrar el conteo se creará una orden nueva con {0} pieza(s) que aparecieron de la orden {1}: mismo cliente y mismo consignatario, estatus «{2}».";
    public const string CreatedMessageFormat = "Se creó la orden {0} (conduce {1}) con {2} pieza(s) que aparecieron de la orden {3}.";

    /// <summary>Lo que va a pasar al cerrar el conteo de la orden, o null si no hay piezas que hayan aparecido.</summary>
    public async Task<ScanReappearedPreviewDto?> PreviewAsync(int orderId, CancellationToken ct)
    {
        var pieces = await db.OrderPieces.AsNoTracking().Where(p => p.TransportOrderId == orderId && p.IsActive && p.ReappearedAtUtc != null).OrderBy(p => p.Sequence).ToListAsync(ct);
        if (pieces.Count == 0) return null;
        var order = await db.TransportOrders.AsNoTracking().FirstAsync(o => o.TransportOrderId == orderId, ct);
        var clientName = await db.Clients.AsNoTracking().Where(c => c.ClientId == order.ClientId).Select(c => c.Name).FirstOrDefaultAsync(ct);
        var consignee = await db.OrderStops.AsNoTracking().Where(s => s.TransportOrderId == orderId).OrderByDescending(s => s.Sequence).Select(s => s.SnapName).FirstOrDefaultAsync(ct);
        var target = await TargetAsync(pieces, ct);
        return new ScanReappearedPreviewDto(order.OrderNumber, clientName ?? string.Empty, consignee, target.Label, pieces.Select(p => p.PackageNumber).ToList());
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
    public async Task<ReappearedOrderResult?> CreateAsync(int orderId, CancellationToken ct)
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
        var born = await statuses.TransitionAsync(StatusDomains.OrderStatus, EntityTypes.TransportOrder, order.TransportOrderId, null, initial.InternalCode, null, ct);
        order.StatusCodeId = born.StatusCodeId;

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

        // al estatus de la acción con que se escanearon
        var pipeline = await statuses.GetPipelineAsync(StatusDomains.OrderStatus, includeDisabled: true, ct);
        var stages = pipeline.Select(s => new PipelineStage(s.Code, s.SortOrder, s.StageKind, s.IsInitial, s.IsEnabled)).ToList();
        var steps = PipelinePath.StepsTo(stages, initial.InternalCode, target.Code);
        if (steps.Count > 0)
        {
            var to = await statuses.AdvanceSkippingAsync(StatusDomains.OrderStatus, EntityTypes.TransportOrder, order.TransportOrderId, order.StatusCodeId, steps, null, ct);
            order.StatusCodeId = to.StatusCodeId;
            if (steps.Contains(OrderStatuses.Pickup)) order.ConfirmedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }
        return new ReappearedOrderResult(order.PublicId, order.OrderNumber, order.WaybillNumber, original.OrderNumber, pieces.Count, target.Label);
    }

    /// <summary>El estatus de la acción más avanzada con que se escanearon las piezas (Enrutar queda en «Sin asignar»: no hay ruta todavía).</summary>
    private async Task<(string Code, string Label)> TargetAsync(IReadOnlyList<OrderPiece> pieces, CancellationToken ct)
    {
        var action = pieces.Select(p => p.ReappearedAction).Where(a => a is not null).OrderByDescending(a => ScanActions.Rank(a!)).FirstOrDefault() ?? ScanActions.Arrive;
        if (action == ScanActions.Route) action = ScanActions.Arrive;
        var code = ScanActions.TargetStatus(action);
        var pipeline = await statuses.GetPipelineAsync(StatusDomains.OrderStatus, includeDisabled: true, ct);
        return (code, pipeline.FirstOrDefault(s => s.Code == code)?.Label ?? code);
    }

    private static string Auto(string kind, NumberingSettings settings, long seq)
    {
        try { return NumberingRules.ResolveChecked(kind, NumberingRules.EffectivePattern(kind, settings), seq); }
        catch (ArgumentException ex) { throw new ConflictException(ex.Message); }
    }
}

/// <summary>La orden nueva creada con las piezas que aparecieron.</summary>
public sealed record ReappearedOrderResult(Guid PublicId, string OrderNumber, string WaybillNumber, string OriginalOrderNumber, int Pieces, string StatusLabel);
