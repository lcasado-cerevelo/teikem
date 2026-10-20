using Microsoft.EntityFrameworkCore;
using Teikem.Domain.ExitNotices;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Notifications;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Wms;

namespace Teikem.Infrastructure.Services;

/// <summary>Lote 143 — reservar, modificar una línea, cancelar y cerrar con faltante. La reserva usa la rotación de la compañía y el ledger (que reserva por saldo).</summary>
public sealed partial class ExitNoticeService
{
    private sealed record Want(int LineId, int ProductId, int? LotId, decimal Quantity);
    private sealed record LineAllocation(int BinId, int? LotId, decimal Quantity);
    private sealed record Availability(IReadOnlyList<LineAllocation> Allocations, decimal Allocated, decimal Available, decimal Shortage);

    /// <summary>
    /// Foto SIN bloqueo del disponible para sacar y la asignación de cada pedido (FEFO / FIFO / posición según el producto y la compañía). El ledger vuelve a verificar con los
    /// saldos bloqueados al reservar. Solo posiciones activas y recolectables (sin cuarentena, cruce de muelle ni renta).
    /// </summary>
    private async Task<Dictionary<int, Availability>> PlanAsync(int warehouseId, IReadOnlyList<Want> wants, CancellationToken ct)
    {
        var productIds = wants.Select(w => w.ProductId).Distinct().ToList();
        var bins = await (from b in db.Set<WarehouseBin>().AsNoTracking()
                          join z in db.Set<WarehouseZone>().AsNoTracking() on b.WarehouseZoneId equals z.WarehouseZoneId
                          where b.WarehouseId == warehouseId
                          select new { b.WarehouseBinId, b.Code, BinActive = b.IsActive, ZoneActive = z.IsActive, z.ZoneTypeLookupId }).ToListAsync(ct);
        var binInfo = new Dictionary<int, (string Code, bool Active, string? Zone)>();
        foreach (var b in bins)
            binInfo[b.WarehouseBinId] = (b.Code, b.BinActive && b.ZoneActive, b.ZoneTypeLookupId is int zt ? (await lookups.GetAsync(zt, ct))?.InternalCode : null);

        var balances = await (from b in db.Set<StockBalance>().AsNoTracking()
                              join lot in db.Set<InventoryLot>().AsNoTracking() on b.LotId equals (int?)lot.LotId into lj
                              from lot in lj.DefaultIfEmpty()
                              where b.WarehouseId == warehouseId && productIds.Contains(b.ProductId) && b.WarehouseBinId != null && b.QtyOnHand > 0
                              select new { b.ProductId, BinId = b.WarehouseBinId!.Value, b.LotId, b.QtyOnHand, b.QtyReserved, b.FirstReceivedAtUtc, Expiry = lot == null ? (DateOnly?)null : lot.ExpiryDate })
            .ToListAsync(ct);
        var company = await RotationQueries.CompanyAsync(db, ct);
        var lotTracked = await RotationQueries.LotTrackedAsync(db, productIds, ct);
        var rotations = await db.Set<Product>().AsNoTracking().Where(p => productIds.Contains(p.ProductId)).ToDictionaryAsync(p => p.ProductId, p => p.RotationMethod, ct);

        var result = new Dictionary<int, Availability>();
        foreach (var w in wants)
        {
            var candidates = balances.Where(b => b.ProductId == w.ProductId && binInfo.ContainsKey(b.BinId))
                .Select(b => new PickCandidate(b.BinId, binInfo[b.BinId].Code, binInfo[b.BinId].Zone, binInfo[b.BinId].Active, b.LotId, b.Expiry,
                    Math.Max(0m, b.QtyOnHand - b.QtyReserved), b.FirstReceivedAtUtc)).ToList();
            var method = RotationRules.Resolve(rotations.GetValueOrDefault(w.ProductId), company, lotTracked.Contains(w.ProductId));
            var allocation = PickBatchRules.Allocate(w.Quantity, candidates, null, w.LotId, method);
            var sellable = candidates.Where(c => c.BinActive && PickBatchRules.IsPickableZone(c.ZoneTypeCode) && (w.LotId is null || c.LotId == w.LotId)).Sum(c => c.Available);
            result[w.LineId] = new Availability(allocation.Allocations.Select(a => new LineAllocation(a.BinId, a.LotId, a.Quantity)).ToList(), allocation.Allocated, sellable,
                Math.Max(0m, w.Quantity - allocation.Allocated));
        }
        return result;
    }

    /// <summary>Anota (para el ledger y para la tabla de reservas) lo que se reservó a una línea.</summary>
    private void AddReservations(ExitNotice notice, ExitNoticeLine line, IReadOnlyList<LineAllocation> allocations, List<StockReservation> reservations)
    {
        foreach (var al in allocations)
        {
            reservations.Add(new StockReservation(line.ProductId, notice.WarehouseId, al.BinId, al.LotId, al.Quantity));
            db.Set<ExitNoticeReservation>().Add(new ExitNoticeReservation
            {
                TenantId = notice.TenantId, ExitNoticeLineId = line.ExitNoticeLineId, WarehouseId = notice.WarehouseId, WarehouseBinId = al.BinId, LotId = al.LotId, Quantity = al.Quantity,
            });
        }
    }

    // ---------------------------------------------------------------- cancelar (solicitado sin despachar)

    /// <summary>Cancela el aviso: libera EXACTAMENTE lo que tenía reservado y borra su pendiente. Uno con algo ya despachado no se cancela.</summary>
    public async Task<ExitNoticeDto> CancelAsync(Guid publicId, string? comment, CancellationToken ct)
    {
        await db.RunInTransactionAsync(async ct2 =>
        {
            var notice = await db.Set<ExitNotice>().AsTracking().Include(o => o.Lines).FirstOrDefaultAsync(o => o.PublicId == publicId, ct2) ?? throw NotFound();
            var code = (await StatusMapAsync(ct2))[notice.StatusCodeId].Code;
            if (code == ExitNoticeStatuses.Cancelled) throw new ConflictException(ExitNoticeRules.AlreadyCancelled);
            if (code != ExitNoticeStatuses.Requested || notice.Lines.Any(l => l.QtyShipped > 0m)) throw new StatusRuleException(ExitNoticeRules.OnlyRequestedCancellable);

            await ReleaseAllAsync(notice, ct2);
            var to = await statuses.TransitionAsync(ExitNoticeCodes.StatusDomain, ExitNoticeCodes.EntityType, notice.ExitNoticeId, notice.StatusCodeId, ExitNoticeStatuses.Cancelled, comment, ct2);
            notice.StatusCodeId = to.StatusCodeId;
            await NotifyAsync(ExitNoticeEventCodes.Cancelled, notice, new Dictionary<string, string?> { ["reason"] = string.IsNullOrWhiteSpace(comment) ? string.Empty : comment.Trim() }, null, ct2);
            await db.SaveGuardedAsync(ExitNoticeRules.Concurrency, ct2);
        }, ct);
        return await GetAsync(publicId, ct);
    }

    /// <summary>Libera en el ledger lo reservado por cada línea (según ExitNoticeReservation), borra esas filas y deja las líneas sin reserva ni pendiente.</summary>
    private async Task ReleaseAllAsync(ExitNotice notice, CancellationToken ct)
    {
        var lineIds = notice.Lines.Select(l => l.ExitNoticeLineId).ToList();
        var rows = await db.Set<ExitNoticeReservation>().AsTracking().Where(r => lineIds.Contains(r.ExitNoticeLineId)).ToListAsync(ct);
        var productOf = notice.Lines.ToDictionary(l => l.ExitNoticeLineId, l => l.ProductId);
        await ledger.ReleaseAsync(rows.Select(r => new StockReservation(productOf[r.ExitNoticeLineId], r.WarehouseId, r.WarehouseBinId, r.LotId, r.Quantity)).ToList(), ct);
        db.Set<ExitNoticeReservation>().RemoveRange(rows);
        foreach (var l in notice.Lines) (l.QtyReserved, l.QtyBackordered) = (0m, 0m);
    }

    // ---------------------------------------------------------------- modificar la cantidad de una línea (solicitado, sin despachar)

    /// <summary>Cambia la cantidad pedida de una línea. Subirla reserva lo que haya (el resto queda pendiente); bajarla libera primero el pendiente y luego la reserva.</summary>
    public async Task<ExitNoticeDto> ModifyLineAsync(Guid publicId, int lineId, ExitNoticeLineModifyRequest req, CancellationToken ct)
    {
        if (req is null) throw new ValidationException("body", "El cuerpo de la solicitud es obligatorio.");
        var qe = ExitNoticeRules.QuantityError(req.Quantity);
        if (qe is not null) throw new ValidationException("quantity", qe);
        var newQty = req.Quantity!.Value;

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await db.RunInTransactionAsync(async ct2 =>
                {
                    var notice = await db.Set<ExitNotice>().AsTracking().Include(o => o.Lines).FirstOrDefaultAsync(o => o.PublicId == publicId, ct2) ?? throw NotFound();
                    if ((await StatusMapAsync(ct2))[notice.StatusCodeId].Code != ExitNoticeStatuses.Requested) throw new StatusRuleException(ExitNoticeRules.OnlyRequestedModifiable);
                    var line = notice.Lines.FirstOrDefault(l => l.ExitNoticeLineId == lineId) ?? throw new NotFoundException("Línea", feminine: true);
                    if (line.IsClosed) throw new StatusRuleException(ExitNoticeRules.LineClosed);
                    if (line.QtyShipped > 0m) throw new StatusRuleException(ExitNoticeRules.LineAlreadyShipped);
                    if (newQty != line.QtyOrdered)
                    {
                        if (newQty > line.QtyOrdered) await IncreaseAsync(notice, line, newQty - line.QtyOrdered, ct2);
                        else await DecreaseAsync(line, line.QtyOrdered - newQty, ct2);
                        line.QtyOrdered = newQty;
                    }
                    await db.SaveGuardedAsync(ExitNoticeRules.Concurrency, ct2);
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

    private async Task IncreaseAsync(ExitNotice notice, ExitNoticeLine line, decimal extra, CancellationToken ct)
    {
        var a = (await PlanAsync(notice.WarehouseId, new[] { new Want(line.ExitNoticeLineId, line.ProductId, line.LotId, extra) }, ct))[line.ExitNoticeLineId];
        var reservations = new List<StockReservation>();
        AddReservations(notice, line, a.Allocations, reservations);
        await ledger.ReserveAsync(reservations, ct);
        line.QtyReserved += a.Allocated;
        line.QtyBackordered += a.Shortage;
    }

    private async Task DecreaseAsync(ExitNoticeLine line, decimal less, CancellationToken ct)
    {
        var fromBackorder = Math.Min(less, line.QtyBackordered);
        line.QtyBackordered -= fromBackorder;
        var toRelease = less - fromBackorder;
        if (toRelease <= 0m) return;
        var rows = await db.Set<ExitNoticeReservation>().AsTracking().Where(r => r.ExitNoticeLineId == line.ExitNoticeLineId).OrderByDescending(r => r.ExitNoticeReservationId).ToListAsync(ct);
        var release = new List<StockReservation>();
        var left = toRelease;
        foreach (var r in rows)
        {
            if (left <= 0m) break;
            var take = Math.Min(r.Quantity, left);
            release.Add(new StockReservation(line.ProductId, r.WarehouseId, r.WarehouseBinId, r.LotId, take));
            r.Quantity -= take;
            left -= take;
            if (r.Quantity <= 0m) db.Set<ExitNoticeReservation>().Remove(r);
        }
        await ledger.ReleaseAsync(release, ct);
        line.QtyReserved -= release.Sum(x => x.Quantity);
    }

    // ---------------------------------------------------------------- cerrar con faltante

    /// <summary>
    /// Da la línea por terminada sin esperar más inventario: su pendiente se borra y la línea queda cerrada (lo ya reservado sigue reservado para despacharse). La cantidad
    /// pedida se conserva (sirve para medir el cumplimiento).
    /// </summary>
    public async Task<ExitNoticeDto> CloseLineAsync(Guid publicId, int lineId, string? comment, CancellationToken ct)
    {
        await db.RunInTransactionAsync(async ct2 =>
        {
            var notice = await db.Set<ExitNotice>().AsTracking().Include(o => o.Lines).FirstOrDefaultAsync(o => o.PublicId == publicId, ct2) ?? throw NotFound();
            var code = (await StatusMapAsync(ct2))[notice.StatusCodeId].Code;
            if (code is not (ExitNoticeStatuses.Requested or ExitNoticeStatuses.Partial)) throw new StatusRuleException(ExitNoticeRules.NotOpenForBackorder);
            var line = notice.Lines.FirstOrDefault(l => l.ExitNoticeLineId == lineId) ?? throw new NotFoundException("Línea", feminine: true);
            if (line.IsClosed) throw new StatusRuleException(ExitNoticeRules.LineClosed);
            if (line.QtyBackordered <= 0m) throw new ConflictException(ExitNoticeRules.NoBackorder);
            (line.QtyBackordered, line.IsClosed) = (0m, true);
            await RefreshStatusAsync(notice, string.IsNullOrWhiteSpace(comment) ? "Línea cerrada con faltante" : comment.Trim(), ct2);
            await db.SaveGuardedAsync(ExitNoticeRules.Concurrency, ct2);
        }, ct);
        return await GetAsync(publicId, ct);
    }

    /// <summary>
    /// Estatus del aviso según sus líneas (<see cref="ExitNoticeRules.NextStatus"/>): todas terminadas → SHIPPED si se despachó algo o CANCELLED si nada; con algo despachado y
    /// pendiente → PARTIAL. Avisa el despachado por completo y el cancelado solo (todo cerrado con faltante).
    /// </summary>
    private async Task RefreshStatusAsync(ExitNotice notice, string comment, CancellationToken ct)
    {
        var code = (await StatusMapAsync(ct))[notice.StatusCodeId].Code;
        var target = ExitNoticeRules.NextStatus(code, notice.Lines.Any(l => l.QtyShipped > 0m), notice.Lines.All(l => l.QtyReserved <= 0m && l.QtyBackordered <= 0m));
        if (target is null || target == code) return;
        var text = target == ExitNoticeStatuses.Cancelled ? "Todas las líneas se cerraron con faltante y no se despachó nada." : comment;
        var to = await statuses.TransitionAsync(ExitNoticeCodes.StatusDomain, ExitNoticeCodes.EntityType, notice.ExitNoticeId, notice.StatusCodeId, target, text, ct);
        notice.StatusCodeId = to.StatusCodeId;
        if (target == ExitNoticeStatuses.Shipped)
            await NotifyAsync(ExitNoticeEventCodes.Shipped, notice, new Dictionary<string, string?> { ["shipped"] = Qty(notice.Lines.Sum(l => l.QtyShipped)) }, null, ct);
        else if (target == ExitNoticeStatuses.Cancelled)
            await NotifyAsync(ExitNoticeEventCodes.Cancelled, notice, new Dictionary<string, string?> { ["reason"] = text }, null, ct);
    }
}
