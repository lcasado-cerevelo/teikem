using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Constants;
using Teikem.Domain.Orders;
using Teikem.Domain.Sales;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Notifications;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Wms;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 61 (P4, S2) — despachar contra una orden de venta. UNA transacción: consume la reserva, saca el inventario por el ledger (movimiento Issue con la
/// recolección como referencia), crea la recolección enlazada a la orden (PickBatch.SalesOrderId / PickBatchLine.SalesOrderLineId), sube QtyShipped, baja
/// QtyReserved y deja la orden en parcial o despachada. Por partes; nunca más de lo reservado.
/// - Posición y lote GUIADOS con cambio permitido: sin posición se toma de lo que reservó el sistema; con otra posición (con saldo) la reserva SE MUEVE a esa
///   posición (se reserva ahí, se libera lo equivalente de las otras) y de ahí sale; sin saldo, 409 con el aviso de inventario.
/// - Una línea está TERMINADA cuando no le queda reserva ni backorder. La orden es SHIPPED cuando todas lo están (con algo despachado) y CANCELLED si ninguna
///   despachó nada (todas se cerraron con faltante).
/// </summary>
public sealed partial class SalesOrderService
{
    private sealed record Pick(int LineId, int ProductId, int BinId, int? LotId, decimal Quantity, int? KitProductId = null, decimal? KitQuantity = null);

    public async Task<SalesDispatchResultDto> DispatchAsync(Guid publicId, SalesDispatchRequest? req, CancellationToken ct)
    {
        var requested = req?.Lines ?? Array.Empty<SalesDispatchLineRequest>();
        if (requested.Count == 0) throw new Exceptions.ValidationException("lines", SalesRules.DispatchLinesRequired);
        var seen = new HashSet<int>();
        for (var i = 0; i < requested.Count; i++)
        {
            var d = requested[i];
            if (d.LineId is null) throw new Exceptions.ValidationException($"lines[{i}].lineId", SalesRules.DispatchLineRequired);
            if (!seen.Add(d.LineId.Value)) throw new Exceptions.ValidationException($"lines[{i}].lineId", SalesRules.DispatchLineRepeated);
            var qe = SalesRules.QuantityError(d.Quantity);
            if (qe is not null) throw new Exceptions.ValidationException($"lines[{i}].quantity", qe);
        }
        var (note, noteError) = SalesRules.NormalizeNotes(req?.Note);
        if (noteError is not null) throw new Exceptions.ValidationException("note", noteError);

        await numbers.EnsureAsync(NumberKinds.PackBatch, null, ct);
        Guid batchId = Guid.Empty;
        string batchNumber = string.Empty;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await db.RunInTransactionAsync(async ct2 =>
                {
                    var tenantId = T.RequireTenantId();
                    var order = await db.Set<SalesOrder>().AsTracking().Include(o => o.Lines).FirstOrDefaultAsync(o => o.PublicId == publicId, ct2) ?? throw NotFound();
                    var code = (await StatusMapAsync(ct2))[order.StatusCodeId].Code;
                    if (code is not (SalesOrderStatuses.Requested or SalesOrderStatuses.Partial)) throw new StatusRuleException(SalesRules.NotDispatchable);

                    var lineIds = order.Lines.Select(l => l.SalesOrderLineId).ToList();
                    var rows = await db.Set<SalesOrderReservation>().AsTracking().Where(r => lineIds.Contains(r.SalesOrderLineId)).OrderBy(r => r.SalesOrderReservationId).ToListAsync(ct2);
                    // Lote 69: las líneas de kit se despachan en kits enteros, sobre lo reservado de sus componentes
                    var snapshots = (await db.Set<SalesOrderLineComponent>().AsNoTracking().Where(c => lineIds.Contains(c.SalesOrderLineId)).ToListAsync(ct2))
                        .GroupBy(c => c.SalesOrderLineId).ToDictionary(g => g.Key, g => g.ToList());
                    var neededProducts = order.Lines.Select(l => l.ProductId).Concat(snapshots.Values.SelectMany(v => v.Select(c => c.ComponentProductId))).Distinct().ToList();
                    var skus = await db.Set<Product>().AsNoTracking().Where(p => neededProducts.Contains(p.ProductId)).ToDictionaryAsync(p => p.ProductId, ct2);
                    var lineProduct = order.Lines.ToDictionary(l => l.SalesOrderLineId, l => l.ProductId);
                    var kitUnits = requested.Where(d => snapshots.ContainsKey(d.LineId!.Value)).ToDictionary(d => d.LineId!.Value, d => d.Quantity!.Value);

                    // 1) cantidades contra lo reservado
                    foreach (var d in requested)
                    {
                        var line = order.Lines.FirstOrDefault(l => l.SalesOrderLineId == d.LineId!.Value) ?? throw new NotFoundException("Línea", feminine: true);
                        if (d.Quantity!.Value > line.QtyReserved)
                            throw new ConflictException(SalesRules.DispatchMoreThanReserved(skus[line.ProductId].Sku, line.QtyReserved));
                        if (snapshots.ContainsKey(line.SalesOrderLineId))
                        {
                            if (d.BinId is not null || d.LotId is not null) throw new Exceptions.ValidationException("lines", SalesRules.KitNoBinChoice);
                            if (d.Quantity.Value != Math.Floor(d.Quantity.Value)) throw new Exceptions.ValidationException("lines", SalesRules.KitQuantityWhole(skus[line.ProductId].Sku));
                        }
                    }

                    // 2) de dónde sale cada línea (y mover la reserva si el operario tomó de otra posición)
                    var picks = new List<Pick>();
                    foreach (var d in requested)
                    {
                        var line = order.Lines.First(l => l.SalesOrderLineId == d.LineId!.Value);
                        var mine = rows.Where(r => r.SalesOrderLineId == line.SalesOrderLineId).ToList();
                        var q = d.Quantity!.Value;
                        if (snapshots.TryGetValue(line.SalesOrderLineId, out var comps))
                        {
                            // kit: de cada componente sale (cantidad por kit × kits) de lo que tiene reservado; la primera porción lleva el número de kits (para deshacer en kits)
                            var firstOfKit = true;
                            foreach (var c in comps)
                            {
                                var need = q * c.Quantity;
                                foreach (var r in mine.Where(r => (r.ProductId ?? line.ProductId) == c.ComponentProductId))
                                {
                                    if (need <= 0m) break;
                                    var take = Math.Min(r.Quantity, need);
                                    picks.Add(new Pick(line.SalesOrderLineId, c.ComponentProductId, r.WarehouseBinId, r.LotId, take, line.ProductId, firstOfKit ? q : null));
                                    firstOfKit = false;
                                    need -= take;
                                }
                                if (need > 0m) throw new ConflictException(SalesRules.DispatchMoreThanReserved(skus[line.ProductId].Sku, line.QtyReserved));
                            }
                            continue;
                        }
                        if (d.BinId is null)
                        {
                            var left = q;
                            foreach (var r in mine)
                            {
                                if (left <= 0m) break;
                                var take = Math.Min(r.Quantity, left);
                                picks.Add(new Pick(line.SalesOrderLineId, line.ProductId, r.WarehouseBinId, r.LotId, take));
                                left -= take;
                            }
                        }
                        else
                        {
                            var bin = d.BinId.Value;
                            await EnsureSellableBinAsync(order.WarehouseId, bin, ct2);
                            var covered = mine.Where(r => r.WarehouseBinId == bin && r.LotId == d.LotId).Sum(r => r.Quantity);
                            var extra = q - Math.Min(q, covered);
                            if (extra > 0m)
                            {
                                // la reserva se mueve: se reserva en la posición escogida (el ledger verifica el saldo) y se libera lo equivalente de las demás
                                await ledger.ReserveAsync(new[] { new StockReservation(line.ProductId, order.WarehouseId, bin, d.LotId, extra) }, ct2);
                                var toFree = extra;
                                var release = new List<StockReservation>();
                                foreach (var r in mine.Where(r => !(r.WarehouseBinId == bin && r.LotId == d.LotId)).OrderByDescending(r => r.SalesOrderReservationId))
                                {
                                    if (toFree <= 0m) break;
                                    var take = Math.Min(r.Quantity, toFree);
                                    release.Add(new StockReservation(line.ProductId, r.WarehouseId, r.WarehouseBinId, r.LotId, take));
                                    r.Quantity -= take;
                                    if (r.Quantity <= 0m) db.Set<SalesOrderReservation>().Remove(r);   // CHECK Quantity > 0: la fila vacía se borra al instante (el ledger guarda en medio)
                                    toFree -= take;
                                }
                                await ledger.ReleaseAsync(release, ct2);
                                db.Set<SalesOrderReservation>().Add(new SalesOrderReservation { TenantId = order.TenantId, SalesOrderLineId = line.SalesOrderLineId, WarehouseId = order.WarehouseId, WarehouseBinId = bin, LotId = d.LotId, Quantity = extra });
                                mine = mine.Where(r => r.Quantity > 0m).ToList();
                                await db.SaveGuardedAsync(SalesRules.Concurrency, ct2);
                                rows = await db.Set<SalesOrderReservation>().AsTracking().Where(r => lineIds.Contains(r.SalesOrderLineId)).OrderBy(r => r.SalesOrderReservationId).ToListAsync(ct2);
                            }
                            picks.Add(new Pick(line.SalesOrderLineId, line.ProductId, bin, d.LotId, q));
                        }
                    }

                    // 3) se consume la reserva y sale el inventario
                    foreach (var pk in picks)
                    {
                        var left = pk.Quantity;
                        foreach (var r in rows.Where(r => r.SalesOrderLineId == pk.LineId && (r.ProductId ?? lineProduct[pk.LineId]) == pk.ProductId && r.WarehouseBinId == pk.BinId && r.LotId == pk.LotId && r.Quantity > 0m).ToList())
                        {
                            if (left <= 0m) break;
                            var take = Math.Min(r.Quantity, left);
                            r.Quantity -= take;
                            if (r.Quantity <= 0m) db.Set<SalesOrderReservation>().Remove(r);   // idem: nunca una fila con cantidad cero hacia la base
                            left -= take;
                        }
                    }
                    await ledger.ReleaseAsync(picks.Select(pk => new StockReservation(pk.ProductId, order.WarehouseId, pk.BinId, pk.LotId, pk.Quantity)).ToList(), ct2);

                    var initial = await statuses.GetInitialAsync(StatusDomains.PickBatchStatus, ct2);
                    var seq = await numbers.NextAsync(NumberKinds.PackBatch, null, ct2);
                    var batch = new PickBatch
                    {
                        PublicId = Guid.NewGuid(), TenantId = tenantId, WarehouseId = order.WarehouseId, Number = NumberingRules.Resolve(NumberingRules.PackBatchPattern, seq),
                        StatusCodeId = initial.StatusCodeId, CollectedAtUtc = DateTime.UtcNow, CollectedBy = tenant.UserId, IsActive = true, SalesOrderId = order.SalesOrderId, Note = note,
                    };
                    db.Set<PickBatch>().Add(batch);
                    await db.SaveGuardedAsync(SalesRules.NumberTaken, ct2);
                    var born = await statuses.TransitionAsync(StatusDomains.PickBatchStatus, EntityTypes.PickBatch, batch.PickBatchId, null, initial.InternalCode, null, ct2);
                    batch.StatusCodeId = born.StatusCodeId;

                    var postings = picks.Select(pk => new InventoryPosting(InventoryTxnTypes.Issue, pk.ProductId, pk.Quantity, LotId: pk.LotId, FromWarehouseId: order.WarehouseId, FromBinId: pk.BinId,
                        RefEntityType: EntityTypes.PickBatch, RefId: batch.PickBatchId,
                        Notes: pk.KitProductId is int kp ? $"{skus[kp].Sku} × {kitUnits[pk.LineId]:0.###} · {batch.Number} · {order.Number}" : $"{batch.Number} · {order.Number}")).ToList();
                    var txnIds = await ledger.PostAsync(postings, ct2);
                    for (var i = 0; i < picks.Count; i++)
                        db.Set<PickBatchLine>().Add(new PickBatchLine
                        {
                            PickBatchId = batch.PickBatchId, ProductId = picks[i].ProductId, LotId = picks[i].LotId, FromBinId = picks[i].BinId, Quantity = picks[i].Quantity,
                            UnitCost = skus[picks[i].ProductId].PurchaseCost, IssueTxnId = txnIds[i], SalesOrderLineId = picks[i].LineId,
                            KitProductId = picks[i].KitProductId, KitQuantity = picks[i].KitQuantity,
                        });

                    // 4) lo despachado y lo reservado de cada línea; el estatus de la orden
                    foreach (var line in order.Lines)
                    {
                        var shipped = kitUnits.TryGetValue(line.SalesOrderLineId, out var kitsShipped) ? kitsShipped : picks.Where(pk => pk.LineId == line.SalesOrderLineId).Sum(pk => pk.Quantity);
                        if (shipped > 0m) (line.QtyShipped, line.QtyReserved) = (line.QtyShipped + shipped, line.QtyReserved - shipped);
                    }
                    await RefreshStatusAsync(order, "Despacho " + batch.Number, ct2);
                    await db.SaveGuardedAsync(SalesRules.Concurrency, ct2);
                    batchId = batch.PublicId;
                    batchNumber = batch.Number;
                }, ct);
                break;
            }
            catch (InsufficientStockException) when (attempt < MaxReserveAttempts)
            {
                db.ChangeTracker.Clear();   // otra venta tomó el saldo de la posición escogida: se reintenta con la foto nueva
            }
        }
        return new SalesDispatchResultDto(batchId, batchNumber, await GetAsync(publicId, ct));
    }

    /// <summary>Despachos (recolecciones) de cada orden, con su entrega si ya la tienen; los deshechos salen como CANCELLED.</summary>
    private async Task<Dictionary<int, List<SalesDispatchInfoDto>>> DispatchesOfAsync(IReadOnlyCollection<int> orderIds, CancellationToken ct)
    {
        var batches = await db.Set<PickBatch>().AsNoTracking().Where(b => b.SalesOrderId != null && orderIds.Contains(b.SalesOrderId.Value)).OrderBy(b => b.PickBatchId).ToListAsync(ct);
        if (batches.Count == 0) return new();
        var ids = batches.Select(b => b.PickBatchId).ToList();
        var qty = await db.Set<PickBatchLine>().AsNoTracking().Where(l => ids.Contains(l.PickBatchId)).GroupBy(l => l.PickBatchId).Select(g => new { g.Key, Total = g.Sum(l => l.KitProductId == null ? l.Quantity : (l.KitQuantity ?? 0m)) }).ToDictionaryAsync(x => x.Key, x => x.Total, ct);
        var statusCodes = batches.Select(b => b.StatusCodeId).Distinct().ToList();
        var codes = await db.StatusCodes.AsNoTracking().Where(s => statusCodes.Contains(s.StatusCodeId)).ToDictionaryAsync(s => s.StatusCodeId, s => s.InternalCode, ct);
        var transportIds = batches.Where(b => b.TransportOrderId != null).Select(b => b.TransportOrderId!.Value).ToList();
        var transports = transportIds.Count == 0 ? new() : await db.TransportOrders.AsNoTracking().Where(t => transportIds.Contains(t.TransportOrderId)).ToDictionaryAsync(t => t.TransportOrderId, t => (t.PublicId, t.OrderNumber), ct);
        return batches.GroupBy(b => b.SalesOrderId!.Value).ToDictionary(g => g.Key, g => g.Select(b =>
        {
            var tr = b.TransportOrderId is int tid && transports.TryGetValue(tid, out var t) ? ((Guid, string)?)t : null;
            return new SalesDispatchInfoDto(b.PublicId, b.Number, codes.GetValueOrDefault(b.StatusCodeId, string.Empty), b.CollectedAtUtc, qty.GetValueOrDefault(b.PickBatchId), tr?.Item1, tr?.Item2);
        }).ToList());
    }

    // ---------------------------------------------------------------- deshacer una recolección de una orden de venta (gancho de PickBatchService)

    /// <summary>
    /// Se eliminó una recolección que atendía esta orden (la existencia ya volvió a su posición, en esta misma transacción): baja lo despachado de cada línea y, si la
    /// orden no está cancelada, VUELVE A RESERVAR esa cantidad en la misma posición y lote. Una orden despachada o parcial se reabre (parcial / solicitada).
    /// </summary>
    public async Task OnPickBatchReversedAsync(int salesOrderId, string pickBatchNumber, IReadOnlyList<SalesReversedLine> reversed, CancellationToken ct)
    {
        var order = await db.Set<SalesOrder>().AsTracking().Include(o => o.Lines).FirstOrDefaultAsync(o => o.SalesOrderId == salesOrderId, ct);
        if (order is null || reversed.Count == 0) return;
        var code = (await StatusMapAsync(ct))[order.StatusCodeId].Code;
        var reserve = code != SalesOrderStatuses.Cancelled;
        var reservations = new List<StockReservation>();
        foreach (var r in reversed)
        {
            var line = order.Lines.FirstOrDefault(l => l.SalesOrderLineId == r.SalesOrderLineId);
            if (line is null) continue;
            if (r.KitProductId is not null)
            {
                // Lote 69: una porción de un kit — los kits se descuentan una sola vez (la porción que lleva KitQuantity); toda porción vuelve a reservarse en su componente
                if (r.KitQuantity is decimal kits && kits > 0m)
                {
                    var backKits = Math.Min(kits, line.QtyShipped);
                    (line.QtyShipped, line.QtyReserved) = (line.QtyShipped - backKits, reserve ? line.QtyReserved + backKits : line.QtyReserved);
                }
                if (!reserve) continue;
                reservations.Add(new StockReservation(r.ProductId, order.WarehouseId, r.BinId, r.LotId, r.Quantity));
                db.Set<SalesOrderReservation>().Add(new SalesOrderReservation { TenantId = order.TenantId, SalesOrderLineId = line.SalesOrderLineId, WarehouseId = order.WarehouseId, WarehouseBinId = r.BinId, LotId = r.LotId, Quantity = r.Quantity, ProductId = r.ProductId });
                continue;
            }
            var back = Math.Min(r.Quantity, line.QtyShipped);
            if (back <= 0m) continue;
            (line.QtyShipped, line.QtyReserved) = (line.QtyShipped - back, reserve ? line.QtyReserved + back : line.QtyReserved);
            if (!reserve) continue;
            reservations.Add(new StockReservation(r.ProductId, order.WarehouseId, r.BinId, r.LotId, back));
            db.Set<SalesOrderReservation>().Add(new SalesOrderReservation { TenantId = order.TenantId, SalesOrderLineId = line.SalesOrderLineId, WarehouseId = order.WarehouseId, WarehouseBinId = r.BinId, LotId = r.LotId, Quantity = back, ProductId = r.ProductId });
        }
        await ledger.ReserveAsync(reservations, ct);
        if (reserve)
        {
            var anyShipped = order.Lines.Any(l => l.QtyShipped > 0m);
            var anyPending = order.Lines.Any(l => l.QtyReserved > 0m || l.QtyBackordered > 0m);
            string? target = null;
            if (code == SalesOrderStatuses.Shipped) target = anyShipped ? SalesOrderStatuses.Partial : SalesOrderStatuses.Requested;
            else if (code == SalesOrderStatuses.Partial && !anyShipped) target = SalesOrderStatuses.Requested;
            if (target is not null && anyPending)
            {
                var to = await statuses.TransitionAsync(SalesCodes.StatusDomain, SalesCodes.EntityType, order.SalesOrderId, order.StatusCodeId, target, "Se deshizo el despacho " + pickBatchNumber, ct);
                order.StatusCodeId = to.StatusCodeId;
            }
        }
        await db.SaveGuardedAsync(SalesRules.Concurrency, ct);
    }

    /// <summary>La posición es del almacén de la orden, está activa y es vendible (sin cuarentena, cross-dock ni renta).</summary>
    private async Task EnsureSellableBinAsync(int warehouseId, int binId, CancellationToken ct)
    {
        var info = await (from b in db.Set<WarehouseBin>().AsNoTracking()
                          join z in db.Set<WarehouseZone>().AsNoTracking() on b.WarehouseZoneId equals z.WarehouseZoneId
                          where b.WarehouseBinId == binId && b.WarehouseId == warehouseId
                          select new { b.Code, Active = b.IsActive && z.IsActive, z.ZoneTypeLookupId }).FirstOrDefaultAsync(ct);
        if (info is null) throw new NotFoundException("Posición", feminine: true);
        var zone = info.ZoneTypeLookupId is int zt ? (await lookups.GetAsync(zt, ct))?.InternalCode : null;
        if (!info.Active || !PickBatchRules.IsPickableZone(zone)) throw new StatusRuleException(SalesRules.BinNotSellable(info.Code));
    }

    /// <summary>
    /// Estatus de la orden según sus líneas: terminada = sin reserva ni backorder. Todas terminadas: SHIPPED si se despachó algo, CANCELLED si nada (se cerraron todas
    /// con faltante). Con algo despachado y líneas pendientes: PARTIAL. (REQUESTED con nada despachado no cambia.)
    /// </summary>
    private async Task RefreshStatusAsync(SalesOrder order, string comment, CancellationToken ct)
    {
        var code = (await StatusMapAsync(ct))[order.StatusCodeId].Code;
        if (code is not (SalesOrderStatuses.Requested or SalesOrderStatuses.Partial)) return;
        var anyShipped = order.Lines.Any(l => l.QtyShipped > 0m);
        var allDone = order.Lines.All(l => l.QtyReserved <= 0m && l.QtyBackordered <= 0m);
        string? target = null;
        if (allDone) target = anyShipped ? SalesOrderStatuses.Shipped : SalesOrderStatuses.Cancelled;
        else if (anyShipped && code == SalesOrderStatuses.Requested) target = SalesOrderStatuses.Partial;
        if (target is null || target == code) return;
        var text = target == SalesOrderStatuses.Cancelled ? "Todas las líneas se cerraron con faltante y no se despachó nada." : comment;
        var to = await statuses.TransitionAsync(SalesCodes.StatusDomain, SalesCodes.EntityType, order.SalesOrderId, order.StatusCodeId, target, text, ct);
        order.StatusCodeId = to.StatusCodeId;
        // lote 66: orden despachada por completo / cancelada sola (todo cerrado con faltante); en la misma transacción de quien llama
        if (target == SalesOrderStatuses.Shipped)
            await NotifyOrderAsync(SalesEventCodes.OrderShipped, order, new Dictionary<string, string?> { ["shipped"] = Qty(order.Lines.Sum(l => l.QtyShipped)) }, null, ct);
        else if (target == SalesOrderStatuses.Cancelled)
            await NotifyOrderAsync(SalesEventCodes.OrderCancelled, order, new Dictionary<string, string?> { ["reason"] = text }, null, ct);
    }
}
