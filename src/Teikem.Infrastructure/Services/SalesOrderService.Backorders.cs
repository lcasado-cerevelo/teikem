using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Clients;
using Teikem.Domain.Sales;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Wms;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 59 (P4, S1) — backorders. La vista (más antiguo primero: el primero que pidió, primero), «Surtir» (reserva lo que haya para la línea; por
/// orden de antigüedad cuando se surte todo) y «Cerrar con faltante» (la línea se da por terminada sin esperar más inventario). Surtir usa el mismo
/// candado del ledger que solicitar: nunca se reserva más de lo que hay ni se le quita a otra orden.
/// </summary>
public sealed partial class SalesOrderService
{
    // ---------------------------------------------------------------- vista

    /// <summary>Líneas con backorder de órdenes solicitadas o parciales, de la más antigua a la más reciente.</summary>
    public async Task<IReadOnlyList<SalesBackorderLineDto>> BackordersAsync(string? q, CancellationToken ct)
    {
        var map = await StatusMapAsync(ct);
        var openIds = map.Where(kv => kv.Value.Code is SalesOrderStatuses.Requested or SalesOrderStatuses.Partial).Select(kv => kv.Key).ToList();
        var rows = await (from l in db.Set<SalesOrderLine>().AsNoTracking()
                          join o in db.Set<SalesOrder>().AsNoTracking() on l.SalesOrderId equals o.SalesOrderId
                          where l.QtyBackordered > 0 && !l.IsClosed && openIds.Contains(o.StatusCodeId)
                          select new { Line = l, Order = o }).ToListAsync(ct);
        if (rows.Count == 0) return Array.Empty<SalesBackorderLineDto>();

        var clientIds = rows.Select(r => r.Order.ClientId).Distinct().ToList();
        var warehouseIds = rows.Select(r => r.Order.WarehouseId).Distinct().ToList();
        var productIds = rows.Select(r => r.Line.ProductId).Distinct().ToList();
        var clients = await db.Set<Client>().AsNoTracking().Where(c => clientIds.Contains(c.ClientId)).ToDictionaryAsync(c => c.ClientId, c => c.Name, ct);
        var warehouses = await db.Set<Warehouse>().AsNoTracking().Where(w => warehouseIds.Contains(w.WarehouseId)).ToDictionaryAsync(w => w.WarehouseId, w => w.Name, ct);
        var products = await db.Set<Product>().AsNoTracking().Where(p => productIds.Contains(p.ProductId)).ToDictionaryAsync(p => p.ProductId, ct);

        var now = DateTime.UtcNow;
        var result = new List<SalesBackorderLineDto>();
        foreach (var g in rows.GroupBy(r => r.Order.WarehouseId))
        {
            var plan = await PlanAsync(g.Key, g.Select(r => new Want(r.Line.SalesOrderLineId, r.Line.ProductId, r.Line.LotId, r.Line.QtyBackordered)).ToList(), ct);
            foreach (var r in g)
            {
                var a = plan[r.Line.SalesOrderLineId];
                var p = products[r.Line.ProductId];
                var asked = r.Order.RequestedAtUtc ?? r.Order.CreatedAtUtc;
                result.Add(new SalesBackorderLineDto(r.Order.PublicId, r.Order.Number, map[r.Order.StatusCodeId].Code, clients[r.Order.ClientId], warehouses[r.Order.WarehouseId],
                    r.Line.SalesOrderLineId, r.Line.LineNo, p.PublicId, p.Sku, p.Name, r.Line.QtyOrdered, r.Line.QtyReserved, r.Line.QtyBackordered, a.Available,
                    Math.Min(r.Line.QtyBackordered, a.Available), asked, Math.Max(0, (int)(now - asked).TotalDays)));
            }
        }
        var ordered = result.OrderBy(r => r.RequestedAtUtc).ThenBy(r => r.OrderNumber, StringComparer.Ordinal).ThenBy(r => r.LineNo).ToList();
        if (string.IsNullOrWhiteSpace(q)) return ordered;
        var s = q.Trim();
        return ordered.Where(r => r.OrderNumber.Contains(s, StringComparison.OrdinalIgnoreCase) || r.ClientName.Contains(s, StringComparison.OrdinalIgnoreCase)
            || r.Sku.Contains(s, StringComparison.OrdinalIgnoreCase) || r.ProductName.Contains(s, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    // ---------------------------------------------------------------- surtir

    /// <summary>Reserva para una línea con backorder lo que haya (todo o parte). 409 si no hay nada que reservar.</summary>
    public async Task<SalesOrderDto> FulfillLineAsync(Guid publicId, int lineId, CancellationToken ct)
    {
        var reserved = await FulfillCoreAsync(publicId, lineId, ct);
        if (reserved <= 0m) throw new ConflictException(SalesRules.NothingToFulfill);
        return await GetAsync(publicId, ct);
    }

    /// <summary>Surte todas las líneas con backorder que se puedan, de la más antigua a la más reciente (cada una en su transacción: lo que una toma ya no lo ve la siguiente).</summary>
    public async Task<SalesFulfillResultDto> FulfillAllAsync(CancellationToken ct)
    {
        var list = await BackordersAsync(null, ct);
        var done = new List<SalesFulfillLineDto>();
        foreach (var b in list.Where(b => b.Fulfillable > 0m))
        {
            var reserved = await FulfillCoreAsync(b.OrderPublicId, b.LineId, ct);
            if (reserved <= 0m) continue;
            var pending = await db.Set<SalesOrderLine>().AsNoTracking().Where(l => l.SalesOrderLineId == b.LineId).Select(l => l.QtyBackordered).FirstAsync(ct);
            done.Add(new SalesFulfillLineDto(b.OrderPublicId, b.OrderNumber, b.LineId, b.Sku, reserved, pending));
        }
        return new SalesFulfillResultDto(done, done.Sum(d => d.Reserved), done.Count(d => d.StillPending <= 0m));
    }

    private async Task<decimal> FulfillCoreAsync(Guid publicId, int lineId, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                decimal reserved = 0m;
                await db.RunInTransactionAsync(async ct2 =>
                {
                    var order = await db.Set<SalesOrder>().AsTracking().Include(o => o.Lines).FirstOrDefaultAsync(o => o.PublicId == publicId, ct2) ?? throw NotFound();
                    var code = (await StatusMapAsync(ct2))[order.StatusCodeId].Code;
                    if (code is not (SalesOrderStatuses.Requested or SalesOrderStatuses.Partial)) throw new StatusRuleException(SalesRules.OnlyRequestedModifiable);
                    var line = order.Lines.FirstOrDefault(l => l.SalesOrderLineId == lineId) ?? throw new NotFoundException("Línea", feminine: true);
                    if (line.IsClosed) throw new StatusRuleException(SalesRules.LineClosed);
                    if (line.QtyBackordered <= 0m) throw new ConflictException(SalesRules.NoBackorder);

                    var a = (await PlanAsync(order.WarehouseId, new[] { new Want(line.SalesOrderLineId, line.ProductId, line.LotId, line.QtyBackordered) }, ct2))[line.SalesOrderLineId];
                    if (a.Allocated <= 0m) return;
                    var reservations = new List<StockReservation>();
                    foreach (var al in a.Allocations)
                    {
                        reservations.Add(new StockReservation(al.ProductId, order.WarehouseId, al.BinId, al.LotId, al.Quantity));
                        db.Set<SalesOrderReservation>().Add(new SalesOrderReservation { TenantId = order.TenantId, SalesOrderLineId = line.SalesOrderLineId, WarehouseId = order.WarehouseId, WarehouseBinId = al.BinId, LotId = al.LotId, Quantity = al.Quantity, ProductId = al.ProductId });
                    }
                    await ledger.ReserveAsync(reservations, ct2);
                    (line.QtyReserved, line.QtyBackordered) = (line.QtyReserved + a.Allocated, line.QtyBackordered - a.Allocated);
                    reserved = a.Allocated;
                    await db.SaveGuardedAsync(SalesRules.Concurrency, ct2);
                }, ct);
                return reserved;
            }
            catch (InsufficientStockException) when (attempt < MaxReserveAttempts)
            {
                db.ChangeTracker.Clear();
            }
        }
    }

    // ---------------------------------------------------------------- cerrar con faltante

    /// <summary>
    /// Da la línea por terminada sin esperar más inventario: su backorder se borra y la línea queda cerrada (lo ya reservado sigue reservado para despacharse).
    /// La cantidad pedida se conserva (sirve para medir el cumplimiento).
    /// </summary>
    public async Task<SalesOrderDto> CloseLineAsync(Guid publicId, int lineId, string? comment, CancellationToken ct)
    {
        await db.RunInTransactionAsync(async ct2 =>
        {
            var order = await db.Set<SalesOrder>().AsTracking().Include(o => o.Lines).FirstOrDefaultAsync(o => o.PublicId == publicId, ct2) ?? throw NotFound();
            var code = (await StatusMapAsync(ct2))[order.StatusCodeId].Code;
            if (code is not (SalesOrderStatuses.Requested or SalesOrderStatuses.Partial)) throw new StatusRuleException(SalesRules.OnlyRequestedModifiable);
            var line = order.Lines.FirstOrDefault(l => l.SalesOrderLineId == lineId) ?? throw new NotFoundException("Línea", feminine: true);
            if (line.IsClosed) throw new StatusRuleException(SalesRules.LineClosed);
            if (line.QtyBackordered <= 0m) throw new ConflictException(SalesRules.NoBackorder);
            (line.QtyBackordered, line.IsClosed) = (0m, true);
            await RefreshStatusAsync(order, "Línea cerrada con faltante", ct2);
            await db.SaveGuardedAsync(SalesRules.Concurrency, ct2);
        }, ct);
        return await GetAsync(publicId, ct);
    }
}
