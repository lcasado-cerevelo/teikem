using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Clients;
using Teikem.Domain.ExitNotices;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Notifications;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Wms;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 143 — pendiente (backorder) del aviso de salida: la vista (el aviso más antiguo primero: el primero que pidió, primero), «Reservar» (reserva lo que haya para una
/// línea o para todas) y el trabajo EXIT_NOTICE_BACKORDER_SCAN, que lo hace solo cuando entra mercancía y avisa. Usa el mismo candado del ledger que el alta: nunca se reserva
/// más de lo que hay ni se le quita a otro documento.
/// </summary>
public sealed partial class ExitNoticeService
{
    /// <summary>Líneas con pendiente de avisos solicitados o parciales, del más antiguo al más reciente.</summary>
    public async Task<IReadOnlyList<ExitNoticeBackorderLineDto>> BackordersAsync(string? q, CancellationToken ct)
    {
        var map = await StatusMapAsync(ct);
        var openIds = map.Where(kv => kv.Value.Code is ExitNoticeStatuses.Requested or ExitNoticeStatuses.Partial).Select(kv => kv.Key).ToList();
        var rows = await (from l in db.Set<ExitNoticeLine>().AsNoTracking()
                          join o in db.Set<ExitNotice>().AsNoTracking() on l.ExitNoticeId equals o.ExitNoticeId
                          where l.QtyBackordered > 0 && !l.IsClosed && openIds.Contains(o.StatusCodeId)
                          select new { Line = l, Notice = o }).ToListAsync(ct);
        if (rows.Count == 0) return Array.Empty<ExitNoticeBackorderLineDto>();

        var clientIds = rows.Select(r => r.Notice.ClientId).Distinct().ToList();
        var warehouseIds = rows.Select(r => r.Notice.WarehouseId).Distinct().ToList();
        var productIds = rows.Select(r => r.Line.ProductId).Distinct().ToList();
        var clients = await db.Set<Client>().AsNoTracking().Where(c => clientIds.Contains(c.ClientId)).ToDictionaryAsync(c => c.ClientId, c => c.Name, ct);
        var warehouses = await db.Set<Warehouse>().AsNoTracking().Where(w => warehouseIds.Contains(w.WarehouseId)).ToDictionaryAsync(w => w.WarehouseId, w => w.Name, ct);
        var products = await db.Set<Product>().AsNoTracking().Where(p => productIds.Contains(p.ProductId)).ToDictionaryAsync(p => p.ProductId, ct);

        var now = DateTime.UtcNow;
        var result = new List<ExitNoticeBackorderLineDto>();
        foreach (var g in rows.GroupBy(r => r.Notice.WarehouseId))
        {
            var plan = await PlanAsync(g.Key, g.Select(r => new Want(r.Line.ExitNoticeLineId, r.Line.ProductId, r.Line.LotId, r.Line.QtyBackordered)).ToList(), ct);
            foreach (var r in g)
            {
                var a = plan[r.Line.ExitNoticeLineId];
                var p = products[r.Line.ProductId];
                var asked = r.Notice.RequestedAtUtc ?? r.Notice.CreatedAtUtc;
                result.Add(new ExitNoticeBackorderLineDto(r.Notice.PublicId, r.Notice.Number, map[r.Notice.StatusCodeId].Code, clients[r.Notice.ClientId], warehouses[r.Notice.WarehouseId],
                    r.Line.ExitNoticeLineId, r.Line.LineNo, p.PublicId, p.Sku, p.Name, r.Line.QtyOrdered, r.Line.QtyReserved, r.Line.QtyBackordered, a.Available,
                    Math.Min(r.Line.QtyBackordered, a.Available), asked, Math.Max(0, (int)(now - asked).TotalDays)));
            }
        }
        var ordered = result.OrderBy(r => r.RequestedAtUtc).ThenBy(r => r.Number, StringComparer.Ordinal).ThenBy(r => r.LineNo).ToList();
        if (string.IsNullOrWhiteSpace(q)) return ordered;
        var s = q.Trim();
        return ordered.Where(r => r.Number.Contains(s, StringComparison.OrdinalIgnoreCase) || r.ClientName.Contains(s, StringComparison.OrdinalIgnoreCase)
            || r.Sku.Contains(s, StringComparison.OrdinalIgnoreCase) || r.ProductName.Contains(s, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    /// <summary>Reserva para una línea con pendiente lo que haya (todo o parte). 409 si no hay nada que reservar.</summary>
    public async Task<ExitNoticeDto> FulfillLineAsync(Guid publicId, int lineId, CancellationToken ct)
    {
        var reserved = await FulfillCoreAsync(publicId, lineId, ct);
        if (reserved <= 0m) throw new ConflictException(ExitNoticeRules.NothingToFulfill);
        return await GetAsync(publicId, ct);
    }

    /// <summary>Reserva todas las líneas con pendiente que se puedan, del aviso más antiguo al más reciente (cada una en su transacción: lo que una toma ya no lo ve la siguiente).</summary>
    public async Task<ExitNoticeFulfillResultDto> FulfillAllAsync(CancellationToken ct)
    {
        var done = new List<ExitNoticeFulfillLineDto>();
        foreach (var b in (await BackordersAsync(null, ct)).Where(b => b.Fulfillable > 0m))
        {
            var reserved = await FulfillCoreAsync(b.NoticePublicId, b.LineId, ct);
            if (reserved <= 0m) continue;
            var pending = await db.Set<ExitNoticeLine>().AsNoTracking().Where(l => l.ExitNoticeLineId == b.LineId).Select(l => l.QtyBackordered).FirstAsync(ct);
            done.Add(new ExitNoticeFulfillLineDto(b.NoticePublicId, b.Number, b.LineId, b.Sku, reserved, pending));
        }
        return new ExitNoticeFulfillResultDto(done.Count, done.Sum(d => d.Reserved), done);
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
                    var notice = await db.Set<ExitNotice>().AsTracking().Include(o => o.Lines).FirstOrDefaultAsync(o => o.PublicId == publicId, ct2) ?? throw NotFound();
                    var code = (await StatusMapAsync(ct2))[notice.StatusCodeId].Code;
                    if (code is not (ExitNoticeStatuses.Requested or ExitNoticeStatuses.Partial)) throw new StatusRuleException(ExitNoticeRules.NotOpenForBackorder);
                    var line = notice.Lines.FirstOrDefault(l => l.ExitNoticeLineId == lineId) ?? throw new NotFoundException("Línea", feminine: true);
                    if (line.IsClosed) throw new StatusRuleException(ExitNoticeRules.LineClosed);
                    if (line.QtyBackordered <= 0m) throw new ConflictException(ExitNoticeRules.NoBackorder);

                    var a = (await PlanAsync(notice.WarehouseId, new[] { new Want(line.ExitNoticeLineId, line.ProductId, line.LotId, line.QtyBackordered) }, ct2))[line.ExitNoticeLineId];
                    if (a.Allocated <= 0m) return;
                    var reservations = new List<StockReservation>();
                    AddReservations(notice, line, a.Allocations, reservations);
                    await ledger.ReserveAsync(reservations, ct2);
                    (line.QtyReserved, line.QtyBackordered) = (line.QtyReserved + a.Allocated, line.QtyBackordered - a.Allocated);
                    reserved = a.Allocated;
                    await db.SaveGuardedAsync(ExitNoticeRules.Concurrency, ct2);
                }, ct);
                return reserved;
            }
            catch (InsufficientStockException) when (attempt < MaxReserveAttempts)
            {
                db.ChangeTracker.Clear();
            }
        }
    }

    /// <summary>
    /// El trabajo EXIT_NOTICE_BACKORDER_SCAN: reserva solo el pendiente que ya se puede cubrir (entró mercancía por cualquier vía: recibo, ajuste, transferencia, liberar
    /// cuarentena, cancelar otro aviso), el aviso más antiguo primero, y publica UN aviso por documento. Devuelve cuántos avisos de salida recibieron reserva.
    /// </summary>
    public async Task<int> ReserveBackordersAsync(CancellationToken ct)
    {
        var reservedBy = new Dictionary<Guid, (string Number, decimal Units, List<string> Skus)>();
        foreach (var b in (await BackordersAsync(null, ct)).Where(b => b.Fulfillable > 0m))
        {
            var reserved = await FulfillCoreAsync(b.NoticePublicId, b.LineId, ct);
            if (reserved <= 0m) continue;
            if (!reservedBy.TryGetValue(b.NoticePublicId, out var acc)) acc = (b.Number, 0m, new List<string>());
            acc.Skus.Add($"{b.Sku} × {Qty(reserved)}");
            reservedBy[b.NoticePublicId] = (acc.Number, acc.Units + reserved, acc.Skus);
        }
        if (notifications is null || reservedBy.Count == 0) return reservedBy.Count;

        foreach (var (publicId, info) in reservedBy)
        {
            var notice = await db.Set<ExitNotice>().AsTracking().Include(o => o.Lines).FirstAsync(o => o.PublicId == publicId, ct);
            var totalReserved = notice.Lines.Sum(l => l.QtyReserved + l.QtyShipped);   // crece con cada tanda: la clave cambia solo si se reservó algo nuevo
            await NotifyAsync(ExitNoticeEventCodes.BackorderReserved, notice,
                new Dictionary<string, string?> { ["lines"] = string.Join(", ", info.Skus), ["units"] = Qty(info.Units) }, $"EB-{publicId:N}-{Qty(totalReserved)}", ct);
        }
        await db.SaveGuardedAsync(ExitNoticeRules.Concurrency, ct);
        return reservedBy.Count;
    }
}
