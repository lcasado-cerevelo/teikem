using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Billing;
using Teikem.Domain.Clients;
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
/// Lote 112 — Devolución (dictado por Luis, 2026-10-18). Una orden que no se pudo entregar vuelve al almacén y alguien con orders.return decide DEVOLVERLA: la orden pasa a «Devuelta» (final) y se crea la ORDEN DE VUELTA (round trip), una orden de
/// entrega nueva —mismo cliente, el recogido es el consignatario y la entrega es el recogido del cliente— con SOLO las piezas que se devuelven (las mismas piezas, con sus mismos números: se mudan). Las piezas que no se devuelven siguen su flujo
/// (faltantes → Reclamaciones). La orden de vuelta no se cotiza ni se factura (sin facturación de flete). Dos maneras de devolver: el botón de la ficha (la orden de vuelta nace «Sometida» y se trabaja normal) y el escaneo con la acción «Devolver»
/// (la orden de vuelta nace Sometida y salta —«brincada»— hasta «Recogida», el estatus de la escaneada). Las piezas siguientes de la misma orden se suman a la orden de vuelta que sigue en Sometida o Recogida.
/// </summary>
public sealed class ReturnOrderService(TeikemDbContext db, ITenantContext tenant, StatusService statuses, INumberSequenceService sequences, ILookupCache lookups, PartialBillingService partial, ITenantClock? clock = null)
{
    private readonly ITenantClock _clock = clock ?? TenantClock.Default;

    public const string SubmittedMessage = "Un envío en «Sometido» no se devuelve: todavía no se recoge. Cancélelo.";
    public const string FinalMessageFormat = "El envío {0} está en un estatus final ({1}): no se devuelve.";
    public const string NoPiecesMessage = "Indique al menos una pieza que se devuelve.";
    public const string PieceNotOfOrderMessage = "La pieza {0} no es de ese envío.";
    public const string PieceMissingMessage = "La pieza {0} figura como faltante: no se puede devolver.";
    public const string NoPickupMessage = "El cliente no tiene ninguna dirección registrada (de recogido, corporativa o de facturación) a la que regresar el envío: regístrela en su ficha.";
    public const string CreatedMessageFormat = "El envío {0} quedó «Devuelto»: se creó el envío de vuelta {1} (conduce {2}) con {3} pieza(s).";
    public const string AddedMessageFormat = "La pieza {0} viaja en el envío de vuelta {1}.";

    private TenantContext Ctx => (TenantContext)tenant;

    // ================================================================ desde la ficha

    /// <summary>El botón «Devolver» de la ficha: las piezas indicadas (sin números, todas las que no son faltantes) viajan en la orden de vuelta, que nace «Sometida».</summary>
    public async Task<ReturnOrderResultDto> ReturnAsync(Guid orderPublicId, ReturnOrderRequest? req, CancellationToken ct)
    {
        var orderId = await db.TransportOrders.AsNoTracking().Where(o => o.PublicId == orderPublicId && o.IsActive).Select(o => (int?)o.TransportOrderId).FirstOrDefaultAsync(ct) ?? throw new NotFoundException("Envío");
        ReturnOrderResultDto? done = null;
        await db.RunInTransactionAsync(async ct2 =>
        {
            var original = await db.TransportOrders.FirstAsync(o => o.TransportOrderId == orderId, ct2);
            var all = await db.OrderPieces.Where(p => p.TransportOrderId == orderId && p.IsActive).OrderBy(p => p.Sequence).ToListAsync(ct2);
            List<OrderPiece> chosen;
            if (req?.PieceNumbers is { Count: > 0 } numbers)
            {
                chosen = new List<OrderPiece>();
                foreach (var raw in numbers)
                {
                    var n = raw?.Trim();
                    var piece = all.FirstOrDefault(p => string.Equals(p.PackageNumber, n, StringComparison.Ordinal)) ?? throw new ValidationException("pieceNumbers", string.Format(PieceNotOfOrderMessage, n));
                    if (piece.MissingStatus is not null) throw new StatusRuleException(string.Format(PieceMissingMessage, piece.PackageNumber));
                    if (!chosen.Contains(piece)) chosen.Add(piece);
                }
            }
            else chosen = all.Where(p => p.MissingStatus is null).ToList();
            if (chosen.Count == 0) throw new ValidationException("pieceNumbers", NoPiecesMessage);
            var (returnOrder, created) = await ReturnPiecesAsync(original, chosen, skipToPickup: false, OptionalText(req?.Notes), ct2);
            done = new ReturnOrderResultDto(returnOrder.PublicId, returnOrder.OrderNumber, returnOrder.WaybillNumber, original.OrderNumber, chosen.Count,
                created ? string.Format(CreatedMessageFormat, original.OrderNumber, returnOrder.OrderNumber, returnOrder.WaybillNumber, chosen.Count) : string.Format(AddedMessageFormat, chosen.Count, returnOrder.OrderNumber));
        }, ct);
        return done!;
    }

    // ================================================================ desde el escaneo

    /// <summary>
    /// La acción «Devolver» de la estación sobre una pieza (dentro de la transacción del escaneo): la primera pieza crea la orden de vuelta (Sometida → Recogida, brincada) y deja la orden «Devuelta»; las siguientes se suman a esa orden de vuelta.
    /// Devuelve la orden de vuelta y si se acaba de crear.
    /// </summary>
    public Task<(TransportOrder ReturnOrder, bool Created)> ReturnScannedPieceAsync(TransportOrder original, OrderPiece piece, CancellationToken ct)
        => ReturnPiecesAsync(original, new List<OrderPiece> { piece }, skipToPickup: true, null, ct);

    // ================================================================ el trabajo

    private async Task<(TransportOrder Order, bool Created)> ReturnPiecesAsync(TransportOrder original, List<OrderPiece> pieces, bool skipToPickup, string? notes, CancellationToken ct)
    {
        var tenantId = Ctx.RequireTenantId();
        var originalStatus = await db.StatusCodes.AsNoTracking().Include(s => s.StageKind).FirstAsync(s => s.StatusCodeId == original.StatusCodeId, ct);
        var isReturned = originalStatus.InternalCode == OrderStatuses.Returned;
        if (originalStatus.InternalCode == OrderStatuses.Submitted) throw new StatusRuleException(SubmittedMessage);
        if (!isReturned && originalStatus.StageKind!.InternalCode == StageKinds.Terminal)
            throw new StatusRuleException(string.Format(FinalMessageFormat, original.OrderNumber, MultilingualText.Resolve(originalStatus.LabelJson, tenant.Lang)));

        // una orden de vuelta que todavía no sale (Sometida o Recogida) recibe las piezas siguientes
        var openStatusIds = await db.StatusCodes.AsNoTracking().Where(s => s.Entity == StatusDomains.OrderStatus && (s.InternalCode == OrderStatuses.Submitted || s.InternalCode == OrderStatuses.Pickup)).Select(s => s.StatusCodeId).ToListAsync(ct);
        var target = await db.TransportOrders.Where(o => o.ReturnOfOrderId == original.TransportOrderId && o.IsActive && openStatusIds.Contains(o.StatusCodeId)).OrderByDescending(o => o.TransportOrderId).FirstOrDefaultAsync(ct);
        var created = target is null;
        var originalStops = await db.OrderStops.AsNoTracking().Where(s => s.TransportOrderId == original.TransportOrderId).OrderBy(s => s.Sequence).ToListAsync(ct);
        var stopMap = new Dictionary<int, int>();   // parada de la original → parada de la orden de vuelta (recogido ↔ entrega)

        if (target is null)
        {
            var clientId = original.ClientId;
            var client = await db.Clients.AsNoTracking().FirstOrDefaultAsync(c => c.ClientId == clientId, ct);
            var settings = client is null ? NumberingSettings.Default : NumberingSettings.FromClient(client);
            await sequences.EnsureAsync(NumberKinds.Order, NumberingRules.ScopeClientId(NumberKinds.Order, clientId), ct);
            await sequences.EnsureAsync(NumberKinds.Invoice, NumberingRules.ScopeClientId(NumberKinds.Invoice, clientId), ct);
            await sequences.EnsureAsync(NumberKinds.PackBatch, NumberingRules.ScopeClientId(NumberKinds.PackBatch, clientId), ct);
            var orderNumber = Auto(NumberKinds.Order, settings, await sequences.NextAsync(NumberKinds.Order, NumberingRules.ScopeClientId(NumberKinds.Order, clientId), ct));
            var invoiceNumber = Auto(NumberKinds.Invoice, settings, await sequences.NextAsync(NumberKinds.Invoice, NumberingRules.ScopeClientId(NumberKinds.Invoice, clientId), ct));
            var packBatch = Auto(NumberKinds.PackBatch, settings, await sequences.NextAsync(NumberKinds.PackBatch, NumberingRules.ScopeClientId(NumberKinds.PackBatch, clientId), ct));

            // paradas: el recogido de la vuelta es el consignatario (la entrega de la original) y su entrega es el recogido del cliente (el recogido de la original, o el del cliente)
            var pickupTypeId = await lookups.GetIdAsync(LookupDomains.StopType, StopTypes.Pickup, ct);
            var deliveryTypeId = await lookups.GetIdAsync(LookupDomains.StopType, StopTypes.Delivery, ct);
            var origDelivery = originalStops.LastOrDefault(s => s.StopTypeLookupId == deliveryTypeId) ?? originalStops.Last();
            var origPickup = originalStops.FirstOrDefault(s => s.StopTypeLookupId == pickupTypeId);
            OrderStop? backTo = origPickup is null ? null : Copy(origPickup, 0);
            // la vuelta regresa al cliente que puso la orden: a su recogido (el de la orden o el del cliente) o, si no tiene, a la dirección que tenga registrada (recogido, corporativa o facturación)
            if (backTo is null && client is not null)
            {
                var loc = await db.DefaultPickupAsync(client, ct) ?? await ClientAddressAsync(clientId, ct);
                if (loc is not null)
                {
                    backTo = new OrderStop { StopTypeLookupId = 0 };
                    OrderRules.ApplySnapshot(backTo, loc, (await lookups.GetAsync(loc.CountryLookupId, ct))?.InternalCode, null);
                }
            }
            if (backTo is null) throw new StatusRuleException(NoPickupMessage);

            var consigneeCode = origDelivery.LocationId is int locId ? await db.Locations.AsNoTracking().Where(l => l.LocationId == locId).Select(l => l.Code).FirstOrDefaultAsync(ct) : null;
            var prefix = WaybillCodes.DayPrefix(PieceNumbering.Part(client?.Code, clientId), PieceNumbering.Part(consigneeCode, origDelivery.LocationId ?? 0), TimeZoneInfo.ConvertTimeFromUtc(_clock.UtcNow, _clock.Zone));
            var sameDay = await db.TransportOrders.AsNoTracking().Where(o => o.WaybillNumber != null && o.WaybillNumber.StartsWith(prefix)).Select(o => o.WaybillNumber).ToListAsync(ct);
            var initial = await statuses.GetInitialAsync(StatusDomains.OrderStatus, ct);
            target = new TransportOrder
            {
                TenantId = tenantId, ClientId = clientId, ContractId = original.ContractId, OrderNumber = orderNumber, ClientInvoiceNumber = invoiceNumber, ClientInvoiceNumberTyped = false, PackBatchNumber = packBatch,
                WaybillNumber = WaybillCodes.Next(prefix, sameDay), ServiceTypeLookupId = original.ServiceTypeLookupId, StatusCodeId = initial.StatusCodeId, PriorityLookupId = original.PriorityLookupId,
                RequestedDate = null, CurrencyLookupId = original.CurrencyLookupId, IsSpecialDelivery = false, ReturnOfOrderId = original.TransportOrderId, TotalPieces = 0,
                Notes = notes ?? $"Vuelta del envío {original.OrderNumber}.", IsActive = true,   // sin cotizar: la vuelta no lleva flete
            };
            db.TransportOrders.Add(target);
            await db.SaveChangesAsync(ct);
            var born = await statuses.TransitionAsync(StatusDomains.OrderStatus, EntityTypes.TransportOrder, target.TransportOrderId, null, initial.InternalCode, null, ct);
            target.StatusCodeId = born.StatusCodeId;

            var stopInitial = await statuses.GetInitialAsync(StatusDomains.StopStatus, ct);
            var pickupStop = Copy(origDelivery, 1);
            pickupStop.StopTypeLookupId = pickupTypeId;
            backTo.Sequence = 2;
            backTo.StopTypeLookupId = deliveryTypeId;
            foreach (var st in new[] { pickupStop, backTo })
            {
                st.TransportOrderId = target.TransportOrderId;
                st.StatusCodeId = stopInitial.StatusCodeId;
                db.OrderStops.Add(st);
            }
            await db.SaveChangesAsync(ct);
            foreach (var st in new[] { pickupStop, backTo })
            {
                var to = await statuses.TransitionAsync(StatusDomains.StopStatus, EntityTypes.OrderStop, st.OrderStopId, null, stopInitial.InternalCode, null, ct);
                st.StatusCodeId = to.StatusCodeId;
            }
            await db.SaveChangesAsync(ct);
            stopMap[origDelivery.OrderStopId] = pickupStop.OrderStopId;
            if (origPickup is not null) stopMap[origPickup.OrderStopId] = backTo.OrderStopId;

            if (skipToPickup)
            {
                // nace Sometida y salta hasta el estatus de la escaneada (Recogida): el escaneo no pasó por Sometida
                var picked = await statuses.AdvanceSkippingAsync(StatusDomains.OrderStatus, EntityTypes.TransportOrder, target.TransportOrderId, target.StatusCodeId, new[] { OrderStatuses.Pickup }, null, ct);
                target.StatusCodeId = picked.StatusCodeId;
                target.ConfirmedAtUtc = DateTime.UtcNow;
            }
        }
        else
        {
            var targetStops = await db.OrderStops.AsNoTracking().Where(s => s.TransportOrderId == target.TransportOrderId).OrderBy(s => s.Sequence).ToListAsync(ct);
            var pickupTypeId = await lookups.GetIdAsync(LookupDomains.StopType, StopTypes.Pickup, ct);
            var deliveryTypeId = await lookups.GetIdAsync(LookupDomains.StopType, StopTypes.Delivery, ct);
            var origDelivery = originalStops.LastOrDefault(s => s.StopTypeLookupId == deliveryTypeId) ?? originalStops.Last();
            var origPickup = originalStops.FirstOrDefault(s => s.StopTypeLookupId == pickupTypeId);
            stopMap[origDelivery.OrderStopId] = targetStops.First(s => s.StopTypeLookupId == pickupTypeId).OrderStopId;
            if (origPickup is not null) stopMap[origPickup.OrderStopId] = targetStops.First(s => s.StopTypeLookupId == deliveryTypeId).OrderStopId;
        }

        // las piezas se mudan (mismos números) a una línea de la orden de vuelta que repite la suya
        var targetLines = await db.CargoLines.Where(l => l.TransportOrderId == target.TransportOrderId).ToListAsync(ct);
        var nextSequence = (await db.OrderPieces.Where(p => p.TransportOrderId == target.TransportOrderId).MaxAsync(p => (int?)p.Sequence, ct)) ?? 0;
        var originLines = await db.CargoLines.Where(l => pieces.Select(p => p.CargoLineId).Contains(l.CargoLineId)).ToListAsync(ct);
        var pieceTypes = pieces.Select(p => originLines.First(l => l.CargoLineId == p.CargoLineId).PackageTypeLookupId).ToList();   // Lote 115: el tipo de paquete de cada pieza que se devuelve
        foreach (var group in pieces.GroupBy(p => p.CargoLineId))
        {
            var src = originLines.First(l => l.CargoLineId == group.Key);
            var line = targetLines.FirstOrDefault(l => l.ProductId == src.ProductId && l.PackageTypeLookupId == src.PackageTypeLookupId && l.Description == src.Description);
            var share = src.Quantity <= 0 ? 0m : group.Count() / src.Quantity;
            if (line is null)
            {
                line = new CargoLine
                {
                    TransportOrderId = target.TransportOrderId, ProductId = src.ProductId, PackageTypeLookupId = src.PackageTypeLookupId, Description = src.Description, UomLookupId = src.UomLookupId, HandlingFlags = src.HandlingFlags, IsActive = true,
                    PickupStopId = src.DeliveryStopId is int d && stopMap.TryGetValue(d, out var np) ? np : null,
                    DeliveryStopId = src.PickupStopId is int p && stopMap.TryGetValue(p, out var nd) ? nd : null,
                    Quantity = group.Count(),   // la restricción CK_CargoLine_Quantity exige > 0 desde el alta
                };
                db.CargoLines.Add(line);
                await db.SaveChangesAsync(ct);
                targetLines.Add(line);
            }
            else line.Quantity += group.Count();
            if (src.WeightKg is not null) line.WeightKg = (line.WeightKg ?? 0m) + src.WeightKg * share;
            if (src.VolumeM3 is not null) line.VolumeM3 = (line.VolumeM3 ?? 0m) + src.VolumeM3 * share;
            line.PackageNumber ??= group.First().PackageNumber;
            foreach (var piece in group)
            {
                piece.TransportOrderId = target.TransportOrderId;
                piece.CargoLineId = line.CargoLineId;
                piece.Sequence = ++nextSequence;
                piece.MissingStatus = null; piece.MissingAtUtc = null; piece.ClaimId = null; piece.ReappearedAtUtc = null; piece.ReappearedAction = null;
            }
        }
        target.TotalPieces = (int)targetLines.Sum(l => l.Quantity);
        target.TotalWeightKg = targetLines.Any(l => l.WeightKg is not null) ? targetLines.Sum(l => l.WeightKg ?? 0m) : null;
        target.TotalVolumeM3 = targetLines.Any(l => l.VolumeM3 is not null) ? targetLines.Sum(l => l.VolumeM3 ?? 0m) : null;
        await db.SaveChangesAsync(ct);

        // Lote 115: cargo por devolución según el contrato (opcional): por orden, por tipo de paquete o por pieza; se le suma a la orden devuelta y se factura aparte del flete
        if (original.ContractId is int contractId && !original.ReturnFeeInvoiced)
        {
            var terms = await db.Contracts.AsNoTracking().Where(c => c.ContractId == contractId).Select(c => new { c.ReturnFeeMode, c.ReturnFeeAmount }).FirstOrDefaultAsync(ct);
            if (terms?.ReturnFeeMode is not null)
            {
                var byType = await db.ContractReturnFees.AsNoTracking().Where(r => r.ContractId == contractId && r.IsActive).ToDictionaryAsync(r => r.PackageTypeLookupId, r => r.Amount, ct);
                var fee = ReturnFeeRules.Compute(terms.ReturnFeeMode, terms.ReturnFeeAmount, byType, pieceTypes, alreadyCharged: original.ReturnFeeAmount is not null);
                if (fee > 0m)
                {
                    original.ReturnFeeAmount = (original.ReturnFeeAmount ?? 0m) + fee;
                    await db.SaveChangesAsync(ct);
                }
            }
        }

        // la original queda «Devuelta» (una vez) y, si el contrato cobra por entrega, lo devuelto no se cobra en ella
        if (!isReturned)
        {
            var to = await statuses.TransitionAsync(StatusDomains.OrderStatus, EntityTypes.TransportOrder, original.TransportOrderId, original.StatusCodeId, OrderStatuses.Returned,
                $"Devuelto: envío de vuelta {target.OrderNumber}.", ct);
            original.StatusCodeId = to.StatusCodeId;
            await db.SaveChangesAsync(ct);
        }
        await partial.AdjustAsync(original.TransportOrderId, ct);
        return (target, created);
    }

    /// <summary>Cualquier dirección del cliente (no de un consignatario): de recogido, corporativa o de facturación, la más antigua primero.</summary>
    private async Task<Location?> ClientAddressAsync(int clientId, CancellationToken ct)
    {
        var typeIds = await db.LookupCodes.AsNoTracking()
            .Where(c => c.Entity == LookupDomains.LocationType && (c.InternalCode == LocationTypes.Pickup || c.InternalCode == LocationTypes.Both || c.InternalCode == LocationTypes.Corporate || c.InternalCode == LocationTypes.Billing))
            .Select(c => c.LookupCodeId).ToListAsync(ct);
        return await db.Locations.AsNoTracking().Where(l => l.ClientId == clientId && l.IsActive && typeIds.Contains(l.LocationTypeLookupId)).OrderBy(l => l.LocationId).FirstOrDefaultAsync(ct);
    }

    private static OrderStop Copy(OrderStop s, int sequence) => new()
    {
        StopTypeLookupId = s.StopTypeLookupId, Sequence = sequence, LocationId = s.LocationId, SnapName = s.SnapName, SnapLine1 = s.SnapLine1, SnapLine2 = s.SnapLine2, SnapCity = s.SnapCity, SnapState = s.SnapState,
        SnapPostalCode = s.SnapPostalCode, SnapCountryCode = s.SnapCountryCode, GeocodeAccuracyLookupId = s.GeocodeAccuracyLookupId, ServiceMinutes = s.ServiceMinutes, Notes = s.Notes,
    };

    private static string? OptionalText(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private static string Auto(string kind, NumberingSettings settings, long seq)
    {
        try { return NumberingRules.ResolveChecked(kind, NumberingRules.EffectivePattern(kind, settings), seq); }
        catch (ArgumentException ex) { throw new ConflictException(ex.Message); }
    }
}
