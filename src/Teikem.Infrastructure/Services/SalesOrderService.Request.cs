using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Clients;
using Teikem.Domain.Constants;
using Teikem.Domain.Orders;
using Teikem.Domain.Sales;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Notifications;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Wms;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 58 (P4, S1) — solicitar la orden de venta: VALIDAR Y RESERVAR en la misma transacción, con el candado del ledger (dos vendedores no pueden
/// vender lo mismo a la vez); disponibilidad, faltante y backorder; crédito del cliente; cancelar libera EXACTAMENTE lo reservado; modificar una línea
/// de una orden solicitada reajusta su reserva.
/// - «Disponible para vender» = en mano − reservado en posiciones activas SIN cuarentena, cross-dock ni renta, del almacén de la orden (y del lote, si
///   la línea pide uno); el mismo orden de rotación (FEFO/FIFO) que la recolección.
/// - Falta inventario: 409 insufficient_stock con «pedido / disponible / faltante» por línea y NO se reserva nada. Con AllowBackorder se reserva lo
///   disponible y lo demás queda en QtyBackordered.
/// - Una carrera entre vendedores la resuelve el ledger al reservar (vuelve a verificar con el saldo bloqueado): se reintenta con la foto nueva.
/// </summary>
public sealed partial class SalesOrderService
{
    private const int MaxReserveAttempts = 3;

    private sealed record Want(int LineId, int ProductId, int? LotId, decimal Quantity);
    /// <summary>Lote 69: de qué producto sale cada porción (el de la línea o, en una línea de kit, un componente).</summary>
    private sealed record LineAllocation(int ProductId, int BinId, int? LotId, decimal Quantity);
    private sealed record Availability(IReadOnlyList<LineAllocation> Allocations, decimal Allocated, decimal Available, decimal Shortage);

    // ---------------------------------------------------------------- comprobación (no guarda)

    /// <summary>Qué pasaría si la orden en borrador se solicitara hoy: pedido / disponible / faltante por línea y el estado del crédito.</summary>
    public async Task<SalesAvailabilityDto> CheckAsync(Guid publicId, CancellationToken ct)
    {
        var order = await db.Set<SalesOrder>().AsNoTracking().Include(o => o.Lines).FirstOrDefaultAsync(o => o.PublicId == publicId, ct) ?? throw NotFound();
        var code = (await StatusMapAsync(ct))[order.StatusCodeId].Code;
        if (code != SalesOrderStatuses.Draft) throw new StatusRuleException(SalesRules.OnlyDraftRequestable);
        var wants = order.Lines.OrderBy(l => l.LineNo).Select(l => new Want(l.SalesOrderLineId, l.ProductId, l.LotId, l.QtyOrdered)).ToList();
        var plan = await PlanAsync(order.WarehouseId, wants, ct);
        var products = await db.Set<Product>().AsNoTracking().Where(p => wants.Select(w => w.ProductId).Contains(p.ProductId)).ToDictionaryAsync(p => p.ProductId, ct);
        var lines = order.Lines.OrderBy(l => l.LineNo).Select(l =>
        {
            var a = plan[l.SalesOrderLineId];
            var p = products[l.ProductId];
            return new SalesAvailabilityLineDto(l.LineNo, p.PublicId, p.Sku, p.Name, l.QtyOrdered, a.Available, a.Shortage);
        }).ToList();
        var (_, credit) = await EvaluateCreditAsync(order, ct);
        return new SalesAvailabilityDto(lines, lines.Any(l => l.Shortage > 0m), credit);
    }

    // ---------------------------------------------------------------- solicitar

    public async Task<SalesOrderDto> RequestAsync(Guid publicId, SalesOrderRequestRequest? req, CancellationToken ct)
    {
        req ??= new SalesOrderRequestRequest();
        var allowBackorder = req.AllowBackorder == true;
        var peek = await db.Set<SalesOrder>().AsNoTracking().Include(o => o.Lines).FirstOrDefaultAsync(o => o.PublicId == publicId, ct) ?? throw NotFound();
        if ((await StatusMapAsync(ct))[peek.StatusCodeId].Code != SalesOrderStatuses.Draft) throw new StatusRuleException(SalesRules.OnlyDraftRequestable);
        var client = await db.Set<Client>().AsNoTracking().FirstAsync(c => c.ClientId == peek.ClientId, ct);
        if (!client.IsActive) throw new StatusRuleException(SalesRules.ClientInactive);
        var warehouse = await db.Set<Warehouse>().AsNoTracking().FirstAsync(w => w.WarehouseId == peek.WarehouseId, ct);
        if (!warehouse.IsActive) throw new StatusRuleException(SalesRules.WarehouseInactive);

        // crédito: excedido = 409 con el aviso; con OverrideCredit y el permiso se autoriza y queda en el historial y en la bitácora de seguridad
        var (check, _) = await EvaluateCreditAsync(peek, ct);
        string? comment = string.IsNullOrWhiteSpace(req.Comment) ? null : req.Comment.Trim();
        var overridden = false;
        if (!CreditRules.Evaluate(check).Ok)
        {
            if (req.OverrideCredit != true)
            {
                await NotifyCreditNeededAsync(peek, check, ct);   // lote 66: quien autoriza se entera
                throw new CreditExceededException(check, CreditRules.Evaluate(check).Available);
            }
            await permissions.EnsureAsync(OrderStatusService.CreditOverridePermission, ct);
            overridden = true;
            comment = OrderStatusService.CreditOverrideCommentPrefix + await CurrentUserNameAsync(ct)
                      + $" (límite {CreditRules.Format(check.Limit ?? 0m)}, en curso {CreditRules.Format(CreditRules.Exposure(check))}, esta orden {CreditRules.Format(check.NewAmount)})"
                      + (comment is null ? string.Empty : " — " + comment);
        }

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await db.RunInTransactionAsync(async ct2 =>
                {
                    var order = await db.Set<SalesOrder>().AsTracking().Include(o => o.Lines).FirstOrDefaultAsync(o => o.PublicId == publicId, ct2) ?? throw NotFound();
                    if ((await StatusMapAsync(ct2))[order.StatusCodeId].Code != SalesOrderStatuses.Draft) throw new StatusRuleException(SalesRules.OnlyDraftRequestable);
                    var lines = order.Lines.OrderBy(l => l.LineNo).ToList();
                    var plan = await PlanAsync(order.WarehouseId, lines.Select(l => new Want(l.SalesOrderLineId, l.ProductId, l.LotId, l.QtyOrdered)).ToList(), ct2);
                    await EnsureNoShortageUnlessAllowedAsync(lines, plan, allowBackorder, ct2);

                    var reservations = new List<StockReservation>();
                    foreach (var l in lines)
                    {
                        var a = plan[l.SalesOrderLineId];
                        l.QtyReserved = a.Allocated;
                        l.QtyBackordered = a.Shortage;
                        foreach (var al in a.Allocations)
                        {
                            reservations.Add(new StockReservation(al.ProductId, order.WarehouseId, al.BinId, al.LotId, al.Quantity));
                            db.Set<SalesOrderReservation>().Add(new SalesOrderReservation
                            {
                                TenantId = order.TenantId, SalesOrderLineId = l.SalesOrderLineId, WarehouseId = order.WarehouseId, WarehouseBinId = al.BinId, LotId = al.LotId, Quantity = al.Quantity,
                                ProductId = al.ProductId,
                            });
                        }
                    }
                    await ledger.ReserveAsync(reservations, ct2);   // el saldo se bloquea y se vuelve a verificar: una carrera lanza insufficient_stock
                    var to = await statuses.TransitionAsync(SalesCodes.StatusDomain, SalesCodes.EntityType, order.SalesOrderId, order.StatusCodeId, SalesOrderStatuses.Requested, comment, ct2);
                    order.StatusCodeId = to.StatusCodeId;
                    order.RequestedAtUtc = DateTime.UtcNow;
                    await NotifyRequestedAsync(order, ct2);
                    await db.SaveGuardedAsync(SalesRules.Concurrency, ct2);
                }, ct);
                break;
            }
            catch (InsufficientStockException) when (attempt < MaxReserveAttempts)
            {
                // otro vendedor reservó entre la foto y el candado: se vuelve a calcular con lo que quedó
                db.ChangeTracker.Clear();
            }
        }

        if (overridden)
            await security.WriteAsync(SecurityEventTypes.RoleChange, SecurityOutcomes.Success, tenant.UserId, tenant.TenantId,
                new { action = "credit_override", salesOrder = publicId, limit = check.Limit, exposure = CreditRules.Exposure(check), amount = check.NewAmount }, ct);
        return await GetAsync(publicId, ct);
    }

    // ---------------------------------------------------------------- cancelar (borrador o solicitada sin despachar)

    /// <summary>Cancela la orden. Una solicitada libera EXACTAMENTE lo que tenía reservado y borra su backorder; una con algo ya despachado no se cancela.</summary>
    public async Task<SalesOrderDto> CancelAsync(Guid publicId, string? comment, CancellationToken ct)
    {
        await db.RunInTransactionAsync(async ct2 =>
        {
            var order = await db.Set<SalesOrder>().AsTracking().Include(o => o.Lines).FirstOrDefaultAsync(o => o.PublicId == publicId, ct2) ?? throw NotFound();
            var code = (await StatusMapAsync(ct2))[order.StatusCodeId].Code;
            if (code == SalesOrderStatuses.Cancelled) throw new ConflictException(SalesRules.AlreadyCancelled);
            var anyShipped = order.Lines.Any(l => l.QtyShipped > 0m);
            if (code is SalesOrderStatuses.Shipped or SalesOrderStatuses.Partial || anyShipped)
                throw new StatusRuleException(SalesRules.OnlyDraftOrRequestedCancellable);

            if (code == SalesOrderStatuses.Requested) await ReleaseAllAsync(order, ct2);
            var to = await statuses.TransitionAsync(SalesCodes.StatusDomain, SalesCodes.EntityType, order.SalesOrderId, order.StatusCodeId, SalesOrderStatuses.Cancelled, comment, ct2);
            order.StatusCodeId = to.StatusCodeId;
            await NotifyOrderAsync(SalesEventCodes.OrderCancelled, order, new Dictionary<string, string?> { ["reason"] = string.IsNullOrWhiteSpace(comment) ? string.Empty : comment.Trim() }, null, ct2);
            await db.SaveGuardedAsync(SalesRules.Concurrency, ct2);
        }, ct);
        return await GetAsync(publicId, ct);
    }

    /// <summary>Libera en el ledger lo reservado por cada línea (según SalesOrderReservation), borra esas filas y deja las líneas sin reserva ni backorder.</summary>
    private async Task ReleaseAllAsync(SalesOrder order, CancellationToken ct)
    {
        var lineIds = order.Lines.Select(l => l.SalesOrderLineId).ToList();
        var rows = await db.Set<SalesOrderReservation>().AsTracking().Where(r => lineIds.Contains(r.SalesOrderLineId)).ToListAsync(ct);
        var productOf = order.Lines.ToDictionary(l => l.SalesOrderLineId, l => l.ProductId);
        await ledger.ReleaseAsync(rows.Select(r => new StockReservation(r.ProductId ?? productOf[r.SalesOrderLineId], r.WarehouseId, r.WarehouseBinId, r.LotId, r.Quantity)).ToList(), ct);
        db.Set<SalesOrderReservation>().RemoveRange(rows);
        foreach (var l in order.Lines) (l.QtyReserved, l.QtyBackordered) = (0m, 0m);
    }

    // ---------------------------------------------------------------- modificar una línea solicitada

    /// <summary>
    /// Cambia la cantidad y/o el precio de una línea de una orden SOLICITADA que no se haya despachado nada de ella. Subir la cantidad reserva lo que
    /// haya (y el resto 409, o backorder con AllowBackorder); bajarla libera primero el backorder y luego la reserva. Recalcula el impuesto.
    /// </summary>
    public async Task<SalesOrderDto> ModifyLineAsync(Guid publicId, int lineId, SalesOrderLineModifyRequest req, CancellationToken ct)
    {
        if (req is null) throw new Exceptions.ValidationException("body", "El cuerpo de la solicitud es obligatorio.");
        if (req.Quantity is not null)
        {
            var qe = SalesRules.QuantityError(req.Quantity);
            if (qe is not null) throw new Exceptions.ValidationException("quantity", qe);
        }
        var pe = SalesRules.PriceError(req.UnitPrice);
        if (pe is not null) throw new Exceptions.ValidationException("unitPrice", pe);
        var allowBackorder = req.AllowBackorder == true;

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await db.RunInTransactionAsync(async ct2 =>
                {
                    var order = await db.Set<SalesOrder>().AsTracking().Include(o => o.Lines).FirstOrDefaultAsync(o => o.PublicId == publicId, ct2) ?? throw NotFound();
                    if ((await StatusMapAsync(ct2))[order.StatusCodeId].Code != SalesOrderStatuses.Requested) throw new StatusRuleException(SalesRules.OnlyRequestedModifiable);
                    var line = order.Lines.FirstOrDefault(l => l.SalesOrderLineId == lineId) ?? throw new NotFoundException("Línea", feminine: true);
                    if (line.IsClosed) throw new StatusRuleException(SalesRules.LineClosed);
                    if (line.QtyShipped > 0m) throw new StatusRuleException(SalesRules.LineAlreadyShipped);

                    if (req.UnitPrice is decimal price && price != line.UnitPrice)
                    {
                        var listPrice = await db.Set<Product>().AsNoTracking().Where(p => p.ProductId == line.ProductId).Select(p => p.SalePrice).FirstAsync(ct2);
                        if (price != (listPrice ?? -1m) && !await permissions.HasPermissionAsync(PermissionCatalog.SalesPrice, ct2)) throw new ForbiddenException(SalesRules.PriceNeedsPermission);
                        line.UnitPrice = price;
                    }
                    if (req.Quantity is decimal newQty && newQty != line.QtyOrdered)
                    {
                        if (newQty > line.QtyOrdered) await IncreaseAsync(order, line, newQty - line.QtyOrdered, allowBackorder, ct2);
                        else await DecreaseAsync(order, line, line.QtyOrdered - newQty, ct2);
                        line.QtyOrdered = newQty;
                    }
                    await RecalculateTaxAsync(order, ct2);
                    await db.SaveGuardedAsync(SalesRules.Concurrency, ct2);
                }, ct);
                break;
            }
            catch (InsufficientStockException) when (attempt < MaxReserveAttempts)
            {
                db.ChangeTracker.Clear();
            }
        }
        return await GetAsync(publicId, ct);
    }

    private async Task IncreaseAsync(SalesOrder order, SalesOrderLine line, decimal extra, bool allowBackorder, CancellationToken ct)
    {
        var want = new Want(line.SalesOrderLineId, line.ProductId, line.LotId, extra);
        var a = (await PlanAsync(order.WarehouseId, new[] { want }, ct))[line.SalesOrderLineId];
        if (a.Shortage > 0m && !allowBackorder)
        {
            var sku = await db.Set<Product>().AsNoTracking().Where(p => p.ProductId == line.ProductId).Select(p => p.Sku).FirstAsync(ct);
            var msg = SalesRules.IncreaseShortageMessage(sku, extra, a.Available);
            throw new InsufficientStockException(msg, new Dictionary<string, string[]> { ["quantity"] = new[] { msg } });
        }
        var reservations = new List<StockReservation>();
        foreach (var al in a.Allocations)
        {
            reservations.Add(new StockReservation(al.ProductId, order.WarehouseId, al.BinId, al.LotId, al.Quantity));
            db.Set<SalesOrderReservation>().Add(new SalesOrderReservation { TenantId = order.TenantId, SalesOrderLineId = line.SalesOrderLineId, WarehouseId = order.WarehouseId, WarehouseBinId = al.BinId, LotId = al.LotId, Quantity = al.Quantity, ProductId = al.ProductId });
        }
        await ledger.ReserveAsync(reservations, ct);
        line.QtyReserved += a.Allocated;
        line.QtyBackordered += a.Shortage;
    }

    private async Task DecreaseAsync(SalesOrder order, SalesOrderLine line, decimal less, CancellationToken ct)
    {
        var fromBackorder = Math.Min(less, line.QtyBackordered);
        line.QtyBackordered -= fromBackorder;
        var toRelease = less - fromBackorder;
        if (toRelease <= 0m) return;
        var factors = await LineFactorsAsync(line, ct);
        var rows = await db.Set<SalesOrderReservation>().AsTracking().Where(r => r.SalesOrderLineId == line.SalesOrderLineId).OrderByDescending(r => r.SalesOrderReservationId).ToListAsync(ct);
        var release = new List<StockReservation>();
        foreach (var (productId, perUnit) in factors)
        {
            var left = toRelease * perUnit;
            foreach (var r in rows.Where(r => (r.ProductId ?? line.ProductId) == productId))
            {
                if (left <= 0m) break;
                var take = Math.Min(r.Quantity, left);
                release.Add(new StockReservation(productId, r.WarehouseId, r.WarehouseBinId, r.LotId, take));
                r.Quantity -= take;
                left -= take;
                if (r.Quantity <= 0m) db.Set<SalesOrderReservation>().Remove(r);
            }
        }
        await ledger.ReleaseAsync(release, ct);
        line.QtyReserved -= factors.Count == 1 && factors.ContainsKey(line.ProductId) && factors[line.ProductId] == 1m ? release.Sum(x => x.Quantity) : toRelease;
    }

    /// <summary>
    /// Lote 69 — cuántas piezas de cada producto lleva una unidad de la línea: un producto normal = {su producto: 1}; un kit = la foto de sus componentes. Las reservas y los
    /// despachos de una línea se hacen sobre estos productos.
    /// </summary>
    private async Task<Dictionary<int, decimal>> LineFactorsAsync(SalesOrderLine line, CancellationToken ct)
    {
        var comps = await db.Set<SalesOrderLineComponent>().AsNoTracking().Where(c => c.SalesOrderLineId == line.SalesOrderLineId).ToListAsync(ct);
        return comps.Count == 0 ? new Dictionary<int, decimal> { [line.ProductId] = 1m } : comps.ToDictionary(c => c.ComponentProductId, c => c.Quantity);
    }

    /// <summary>Recalcula el impuesto de todas las líneas EN SU LUGAR (las reservas cuelgan de los ids de línea) y los totales del encabezado.</summary>
    private async Task RecalculateTaxAsync(SalesOrder order, CancellationToken ct)
    {
        var ordered = order.Lines.OrderBy(l => l.LineNo).ToList();
        var products = await db.Set<Product>().AsNoTracking().Where(p => ordered.Select(l => l.ProductId).Contains(p.ProductId)).ToDictionaryAsync(p => p.ProductId, ct);
        var priced = await PriceAsync(ordered.Select(l => new PlannedLine(products[l.ProductId], l.QtyOrdered, l.UnitPrice, l.LotId)).ToList(), order.ClientId, order.RequestedDate, ct);
        for (var i = 0; i < ordered.Count; i++)
            (ordered[i].TaxRateId, ordered[i].TaxRatePct, ordered[i].TaxAmount, ordered[i].IsTaxExempt) = (priced[i].TaxRateId, priced[i].TaxRatePct, priced[i].TaxAmount, priced[i].IsExempt);
        order.Subtotal = priced.Sum(p => p.Subtotal);
        order.TaxAmount = priced.Sum(p => p.TaxAmount);
    }

    // ---------------------------------------------------------------- disponibilidad

    /// <summary>
    /// Foto SIN bloqueo del disponible para vender y la asignación de cada pedido (FEFO/FIFO según el producto y la compañía). El ledger vuelve a verificar con
    /// los saldos bloqueados al reservar.
    /// </summary>
    private async Task<Dictionary<int, Availability>> PlanAsync(int warehouseId, IReadOnlyList<Want> wants, CancellationToken ct)
    {
        // Lote 69: una línea de KIT se planea sobre la foto de sus componentes (todo-o-nada por kit entero); el resto, sobre su propio producto
        var kitTypeId = await lookups.TryGetIdAsync(KitCodes.ProductTypeDomain, ProductTypes.Kit, ct);
        var wantProducts = wants.Select(w => w.ProductId).Distinct().ToList();
        var kitProducts = kitTypeId is null ? new HashSet<int>()
            : (await db.Set<Product>().AsNoTracking().Where(p => wantProducts.Contains(p.ProductId) && p.ProductTypeLookupId == kitTypeId).Select(p => p.ProductId).ToListAsync(ct)).ToHashSet();
        var kitLineIds = wants.Where(w => kitProducts.Contains(w.ProductId)).Select(w => w.LineId).ToList();
        var snapshots = kitLineIds.Count == 0 ? new Dictionary<int, List<(int ProductId, decimal Quantity)>>()
            : (await db.Set<SalesOrderLineComponent>().AsNoTracking().Where(c => kitLineIds.Contains(c.SalesOrderLineId)).ToListAsync(ct))
                .GroupBy(c => c.SalesOrderLineId).ToDictionary(g => g.Key, g => g.Select(c => (ProductId: c.ComponentProductId, Quantity: c.Quantity)).ToList());
        var productIds = wantProducts.Where(id => !kitProducts.Contains(id)).Concat(snapshots.Values.SelectMany(v => v.Select(c => c.ProductId))).Distinct().ToList();

        var bins = await (from b in db.Set<WarehouseBin>().AsNoTracking()
                          join z in db.Set<WarehouseZone>().AsNoTracking() on b.WarehouseZoneId equals z.WarehouseZoneId
                          join w in db.Set<Warehouse>().AsNoTracking() on b.WarehouseId equals w.WarehouseId
                          where b.WarehouseId == warehouseId
                          select new { b.WarehouseBinId, b.Code, BinActive = b.IsActive, ZoneActive = z.IsActive, z.ZoneTypeLookupId }).ToListAsync(ct);
        var zoneTypes = new Dictionary<int, string?>();
        foreach (var b in bins)
            zoneTypes[b.WarehouseBinId] = b.ZoneTypeLookupId is int zt ? (await lookups.GetAsync(zt, ct))?.InternalCode : null;
        var binInfo = bins.ToDictionary(b => b.WarehouseBinId, b => (b.Code, Active: b.BinActive && b.ZoneActive, Zone: zoneTypes[b.WarehouseBinId]));

        var balances = await (from b in db.Set<StockBalance>().AsNoTracking()
                              join lot in db.Set<InventoryLot>().AsNoTracking() on b.LotId equals (int?)lot.LotId into lj
                              from lot in lj.DefaultIfEmpty()
                              where b.WarehouseId == warehouseId && productIds.Contains(b.ProductId) && b.WarehouseBinId != null && b.QtyOnHand > 0
                              select new { b.ProductId, BinId = b.WarehouseBinId!.Value, b.LotId, b.QtyOnHand, b.QtyReserved, b.FirstReceivedAtUtc, Expiry = lot == null ? (DateOnly?)null : lot.ExpiryDate })
            .ToListAsync(ct);
        var company = await RotationQueries.CompanyAsync(db, ct);   // 2026-10-20: rotación con lote / sin lote
        var lotTracked = await RotationQueries.LotTrackedAsync(db, productIds, ct);
        var rotations = await db.Set<Product>().AsNoTracking().Where(p => productIds.Contains(p.ProductId)).ToDictionaryAsync(p => p.ProductId, p => p.RotationMethod, ct);

        List<PickCandidate> CandidatesOf(int productId)
            => balances.Where(b => b.ProductId == productId && binInfo.ContainsKey(b.BinId))
                .Select(b => new PickCandidate(b.BinId, binInfo[b.BinId].Code, binInfo[b.BinId].Zone, binInfo[b.BinId].Active, b.LotId, b.Expiry,
                    Math.Max(0m, b.QtyOnHand - b.QtyReserved), b.FirstReceivedAtUtc)).ToList();
        decimal SellableOf(List<PickCandidate> candidates, int? lotId)
            => candidates.Where(c => c.BinActive && PickBatchRules.IsPickableZone(c.ZoneTypeCode) && (lotId is null || c.LotId == lotId)).Sum(c => c.Available);

        var result = new Dictionary<int, Availability>();
        foreach (var w in wants)
        {
            if (kitProducts.Contains(w.ProductId))
            {
                result[w.LineId] = PlanKit(w, snapshots.GetValueOrDefault(w.LineId), CandidatesOf, SellableOf, rotations, company, lotTracked);
                continue;
            }
            var candidates = CandidatesOf(w.ProductId);
            var allocation = PickBatchRules.Allocate(w.Quantity, candidates, null, w.LotId, RotationRules.Resolve(rotations.GetValueOrDefault(w.ProductId), company, lotTracked.Contains(w.ProductId)));
            result[w.LineId] = new Availability(allocation.Allocations.Select(a => new LineAllocation(w.ProductId, a.BinId, a.LotId, a.Quantity)).ToList(), allocation.Allocated,
                SellableOf(candidates, w.LotId), Math.Max(0m, w.Quantity - allocation.Allocated));
        }
        return result;
    }

    /// <summary>
    /// Lote 69 — disponibilidad y asignación de una línea de kit: se pueden armar tantos kits ENTEROS como alcance el componente que menos da (disponible ÷ cantidad por kit);
    /// cada componente se asigna por su rotación (FEFO/FIFO). Available = kits que se podrían armar; Allocated = kits que se reservan; Shortage = kits que faltan.
    /// </summary>
    private static Availability PlanKit(Want w, List<(int ProductId, decimal Quantity)>? comps, Func<int, List<PickCandidate>> candidatesOf, Func<List<PickCandidate>, int?, decimal> sellableOf,
        Dictionary<int, string?> rotations, CompanyRotation company, HashSet<int> lotTracked)
    {
        if (comps is null || comps.Count == 0) return new Availability(Array.Empty<LineAllocation>(), 0m, 0m, w.Quantity);
        var byProduct = comps.ToDictionary(c => c.ProductId, c => candidatesOf(c.ProductId));
        var canBuild = KitRules.CanBuild(comps.Select(c => (sellableOf(byProduct[c.ProductId], null), c.Quantity)));
        var kits = Math.Min(Math.Floor(w.Quantity), canBuild);
        var allocations = new List<LineAllocation>();
        for (var pass = 0; pass < 3 && kits > 0m; pass++)
        {
            allocations = new List<LineAllocation>();
            var achievable = kits;
            foreach (var c in comps)
            {
                var r = PickBatchRules.Allocate(kits * c.Quantity, byProduct[c.ProductId], null, null, RotationRules.Resolve(rotations.GetValueOrDefault(c.ProductId), company, lotTracked.Contains(c.ProductId)));
                allocations.AddRange(r.Allocations.Select(a => new LineAllocation(c.ProductId, a.BinId, a.LotId, a.Quantity)));
                achievable = Math.Min(achievable, Math.Floor(r.Allocated / c.Quantity));
            }
            if (achievable == kits) break;
            kits = achievable;
            allocations = new List<LineAllocation>();
        }
        if (kits <= 0m) allocations = new List<LineAllocation>();
        return new Availability(allocations, kits, canBuild, Math.Max(0m, w.Quantity - kits));
    }

    /// <summary>
    /// Lote 68 — lo disponible para vender de cada producto en el almacén (misma cuenta que usa Solicitar: sin cuarentena, cruce de plataforma ni renta, en posiciones
    /// activas). Lo usan los kits para decir «se pueden armar N». Foto sin bloqueo.
    /// </summary>
    public async Task<IReadOnlyDictionary<int, decimal>> SellableAsync(int warehouseId, IReadOnlyCollection<int> productIds, CancellationToken ct)
    {
        if (productIds.Count == 0) return new Dictionary<int, decimal>();
        var plan = await PlanAsync(warehouseId, productIds.Select(id => new Want(id, id, null, 0m)).ToList(), ct);
        return plan.ToDictionary(kv => kv.Key, kv => kv.Value.Available);
    }

    private async Task EnsureNoShortageUnlessAllowedAsync(IReadOnlyList<SalesOrderLine> lines, Dictionary<int, Availability> plan, bool allowBackorder, CancellationToken ct)
    {
        var short1 = lines.Where(l => plan[l.SalesOrderLineId].Shortage > 0m).Select(l => (l, plan[l.SalesOrderLineId], l.QtyOrdered)).ToList();
        if (short1.Count > 0 && !allowBackorder) throw await ShortageAsync(short1, ct);
    }

    private async Task<InsufficientStockException> ShortageAsync(IReadOnlyList<(SalesOrderLine Line, Availability A, decimal Ordered)> short1, CancellationToken ct)
    {
        var ids = short1.Select(s => s.Line.ProductId).ToList();
        var skus = await db.Set<Product>().AsNoTracking().Where(p => ids.Contains(p.ProductId)).ToDictionaryAsync(p => p.ProductId, p => p.Sku, ct);
        var errors = new Dictionary<string, string[]>();
        string? first = null;
        foreach (var (line, a, ordered) in short1)
        {
            var msg = SalesRules.ShortageMessage(skus[line.ProductId], ordered, a.Available);
            errors[$"lines[{line.LineNo - 1}]"] = new[] { msg };
            first ??= msg;
        }
        return new InsufficientStockException(first!, errors);
    }

    // ---------------------------------------------------------------- crédito

    private async Task<(CreditCheck Check, SalesCreditDto Dto)> EvaluateCreditAsync(SalesOrder order, CancellationToken ct)
    {
        var client = await db.Set<Client>().AsNoTracking().FirstAsync(c => c.ClientId == order.ClientId, ct);
        var statusMap = await StatusMapAsync(ct);
        var openIds = statusMap.Where(kv => kv.Value.Code is SalesOrderStatuses.Requested or SalesOrderStatuses.Partial).Select(kv => kv.Key).ToList();
        // en curso = lo pendiente de las órdenes de transporte del cliente + lo vendido y aún sin despachar de sus otras órdenes de venta
        var pendingTransport = await balance.PendingBalanceAsync(client.ClientId, null, ct);
        var openSales = await db.Set<SalesOrder>().AsNoTracking().Where(o => o.ClientId == client.ClientId && o.SalesOrderId != order.SalesOrderId && openIds.Contains(o.StatusCodeId))
            .SumAsync(o => (decimal?)(o.Subtotal + o.TaxAmount), ct) ?? 0m;
        var check = new CreditCheck(client.CreditLimit, pendingTransport + openSales, order.Subtotal + order.TaxAmount);
        var (ok, available) = CreditRules.Evaluate(check);
        return (check, new SalesCreditDto(client.CreditLimit, CreditRules.Exposure(check), check.NewAmount, available, !ok, ok ? null : CreditRules.Message(check)));
    }

    private async Task<string> CurrentUserNameAsync(CancellationToken ct)
    {
        if (tenant.UserId is not int userId) return "sistema";
        var name = await db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.FullName ?? u.Email).FirstOrDefaultAsync(ct);
        return string.IsNullOrWhiteSpace(name) ? $"usuario {userId}" : name;
    }
}
