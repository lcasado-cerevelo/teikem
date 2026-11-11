using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Catalogs;
using Teikem.Domain.Constants;
using Teikem.Domain.ExitNotices;
using Teikem.Domain.Orders;
using Teikem.Domain.Sales;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 173 (C1, 2026-10-21) — «Recolección y empaque» agrupada por DOCUMENTO, sin tocar datos: una fila por aviso de salida, orden de venta o envío empacado con su avance (pedido, reservado,
/// sacado) y sus recolecciones como movimientos. Una recolección pertenece a UN documento con esta prioridad: aviso de salida &gt; orden de venta &gt; envío (el que se creó al empacar); un
/// despacho manual no tiene documento y no sale aquí. Cuenta solo recolecciones activas (las eliminadas, que se revirtieron, no). Aparecen los documentos abiertos con algo por sacar y los que
/// ya tienen alguna recolección; uno cancelado sin recolecciones no.
/// </summary>
public sealed class PickBatchDocumentService(TeikemDbContext db, ITenantContext tenant)
{
    public const string KindExitNotice = "EXIT_NOTICE";
    public const string KindSalesOrder = "SALES_ORDER";
    public const string KindOrder = "ORDER";
    public static readonly string[] Kinds = { KindExitNotice, KindSalesOrder, KindOrder };

    public const int MaxPageSize = 200;
    public static string UnknownKind(string kind) => $"Tipo de documento desconocido: «{kind}». Use EXIT_NOTICE, SALES_ORDER u ORDER.";

    private sealed class DocRow
    {
        public string Kind { get; set; } = string.Empty;
        public int Id { get; set; }
        public Guid PublicId { get; set; }
        public string Number { get; set; } = string.Empty;
        public int ClientId { get; set; }
        public int StatusCodeId { get; set; }
        public decimal? Ordered { get; set; }
        public decimal? Reserved { get; set; }
        public decimal Shipped { get; set; }
        public int BatchCount { get; set; }
        public DateTime? LastAt { get; set; }
        public DateTime SortAt { get; set; }
    }

    public async Task<PickDocumentPageDto> ListAsync(PickDocumentQuery? q, CancellationToken ct)
    {
        q ??= new PickDocumentQuery();
        var kind = string.IsNullOrWhiteSpace(q.Kind) ? null : q.Kind.Trim().ToUpperInvariant();
        if (kind is not null && !Kinds.Contains(kind)) throw new ValidationException("kind", UnknownKind(q.Kind!));
        var skip = Math.Max(0, q.Skip);
        var take = Math.Clamp(q.Take <= 0 ? 50 : q.Take, 1, MaxPageSize);

        var maps = new Dictionary<string, Dictionary<int, ReceivingSupport.StatusInfo>>
        {
            [KindExitNotice] = await ReceivingSupport.StatusMapAsync(db, tenant, ExitNoticeCodes.StatusDomain, ct),
            [KindSalesOrder] = await ReceivingSupport.StatusMapAsync(db, tenant, SalesCodes.StatusDomain, ct),
            [KindOrder] = await ReceivingSupport.StatusMapAsync(db, tenant, StatusDomains.OrderStatus, ct),
        };
        List<int> Ids(string k, params string[] codes) => maps[k].Where(kv => codes.Contains(kv.Value.Code)).Select(kv => kv.Key).ToList();
        var openExit = Ids(KindExitNotice, ExitNoticeStatuses.Requested, ExitNoticeStatuses.Partial);
        var openSales = Ids(KindSalesOrder, SalesOrderStatuses.Requested, SalesOrderStatuses.Partial);

        // Una consulta por tipo con la MISMA forma; se unen y se ordenan/paginan en la base.
        IQueryable<DocRow>? union = null;
        void Add(IQueryable<DocRow> part) => union = union is null ? part : union.Concat(part);

        // Sin `let` con IQueryable: EF no admite una colección en la proyección final, así que la subconsulta de recolecciones se escribe en cada uso.
        if (kind is null or KindExitNotice)
            Add(from o in db.Set<ExitNotice>()
                where db.Set<PickBatch>().Any(b => b.ExitNoticeId == o.ExitNoticeId && b.IsActive)
                      || (openExit.Contains(o.StatusCodeId) && (db.Set<ExitNoticeLine>().Where(l => l.ExitNoticeId == o.ExitNoticeId).Sum(l => (decimal?)l.QtyReserved) ?? 0m) > 0m)
                select new DocRow
                {
                    Kind = KindExitNotice, Id = o.ExitNoticeId, PublicId = o.PublicId, Number = o.Number, ClientId = o.ClientId, StatusCodeId = o.StatusCodeId,
                    Ordered = db.Set<ExitNoticeLine>().Where(l => l.ExitNoticeId == o.ExitNoticeId).Sum(l => (decimal?)l.QtyOrdered) ?? 0m,
                    Reserved = db.Set<ExitNoticeLine>().Where(l => l.ExitNoticeId == o.ExitNoticeId).Sum(l => (decimal?)l.QtyReserved) ?? 0m,
                    Shipped = db.Set<ExitNoticeLine>().Where(l => l.ExitNoticeId == o.ExitNoticeId).Sum(l => (decimal?)l.QtyShipped) ?? 0m,
                    BatchCount = db.Set<PickBatch>().Count(b => b.ExitNoticeId == o.ExitNoticeId && b.IsActive),
                    LastAt = db.Set<PickBatch>().Where(b => b.ExitNoticeId == o.ExitNoticeId && b.IsActive).Max(b => (DateTime?)b.CollectedAtUtc),
                    SortAt = o.RequestedAtUtc ?? o.CreatedAtUtc,
                });
        if (kind is null or KindSalesOrder)
            Add(from o in db.Set<SalesOrder>()
                where db.Set<PickBatch>().Any(b => b.SalesOrderId == o.SalesOrderId && b.ExitNoticeId == null && b.IsActive)
                      || (openSales.Contains(o.StatusCodeId) && (db.Set<SalesOrderLine>().Where(l => l.SalesOrderId == o.SalesOrderId).Sum(l => (decimal?)l.QtyReserved) ?? 0m) > 0m)
                select new DocRow
                {
                    Kind = KindSalesOrder, Id = o.SalesOrderId, PublicId = o.PublicId, Number = o.Number, ClientId = o.ClientId, StatusCodeId = o.StatusCodeId,
                    Ordered = db.Set<SalesOrderLine>().Where(l => l.SalesOrderId == o.SalesOrderId).Sum(l => (decimal?)l.QtyOrdered) ?? 0m,
                    Reserved = db.Set<SalesOrderLine>().Where(l => l.SalesOrderId == o.SalesOrderId).Sum(l => (decimal?)l.QtyReserved) ?? 0m,
                    Shipped = db.Set<SalesOrderLine>().Where(l => l.SalesOrderId == o.SalesOrderId).Sum(l => (decimal?)l.QtyShipped) ?? 0m,
                    BatchCount = db.Set<PickBatch>().Count(b => b.SalesOrderId == o.SalesOrderId && b.ExitNoticeId == null && b.IsActive),
                    LastAt = db.Set<PickBatch>().Where(b => b.SalesOrderId == o.SalesOrderId && b.ExitNoticeId == null && b.IsActive).Max(b => (DateTime?)b.CollectedAtUtc),
                    SortAt = o.RequestedAtUtc ?? o.CreatedAtUtc,
                });
        if (kind is null or KindOrder)
            Add(from o in db.Set<TransportOrder>()
                where db.Set<PickBatch>().Any(b => b.TransportOrderId == o.TransportOrderId && b.SalesOrderId == null && b.ExitNoticeId == null && b.IsActive)
                select new DocRow
                {
                    Kind = KindOrder, Id = o.TransportOrderId, PublicId = o.PublicId, Number = o.OrderNumber, ClientId = o.ClientId, StatusCodeId = o.StatusCodeId,
                    Ordered = null, Reserved = null,
                    Shipped = (from l in db.Set<PickBatchLine>()
                               join b in db.Set<PickBatch>() on l.PickBatchId equals b.PickBatchId
                               where b.TransportOrderId == o.TransportOrderId && b.SalesOrderId == null && b.ExitNoticeId == null && b.IsActive
                               select (decimal?)l.Quantity).Sum() ?? 0m,
                    BatchCount = db.Set<PickBatch>().Count(b => b.TransportOrderId == o.TransportOrderId && b.SalesOrderId == null && b.ExitNoticeId == null && b.IsActive),
                    LastAt = db.Set<PickBatch>().Where(b => b.TransportOrderId == o.TransportOrderId && b.SalesOrderId == null && b.ExitNoticeId == null && b.IsActive).Max(b => (DateTime?)b.CollectedAtUtc),
                    SortAt = o.CreatedAtUtc,
                });
        if (union is null) return new PickDocumentPageDto(0, skip, take, Array.Empty<PickDocumentDto>());

        var query = union;
        if (q.Status is { Length: > 0 })
        {
            var codes = q.Status.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim().ToUpperInvariant()).ToHashSet();
            var ids = maps.Values.SelectMany(m => m).Where(kv => codes.Contains(kv.Value.Code)).Select(kv => kv.Key).ToList();
            query = query.Where(r => ids.Contains(r.StatusCodeId));
        }
        if (q.PendingOnly) query = query.Where(r => r.Reserved > 0m);
        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var s = q.Search.Trim();
            query = query.Where(r => r.Number.Contains(s) || db.Set<Teikem.Domain.Clients.Client>().Any(c => c.ClientId == r.ClientId && c.Name.Contains(s)));
        }

        var total = await query.CountAsync(ct);
        var page = await query.OrderByDescending(r => r.LastAt ?? r.SortAt).ThenBy(r => r.Number).Skip(skip).Take(take).ToListAsync(ct);
        if (page.Count == 0) return new PickDocumentPageDto(total, skip, take, Array.Empty<PickDocumentDto>());

        var clientIds = page.Select(r => r.ClientId).Distinct().ToList();
        var clients = await db.Set<Teikem.Domain.Clients.Client>().AsNoTracking().Where(c => clientIds.Contains(c.ClientId)).ToDictionaryAsync(c => c.ClientId, c => c.Name, ct);
        var movements = await MovementsAsync(page, ct);
        var detail = await LinesAsync(page, ct);

        var items = page.Select(r =>
        {
            var st = maps[r.Kind].GetValueOrDefault(r.StatusCodeId);
            var d = detail.GetValueOrDefault((r.Kind, r.Id));
            return new PickDocumentDto(r.Kind, r.PublicId, r.Number, clients.GetValueOrDefault(r.ClientId), st?.Code ?? string.Empty, st?.Label ?? string.Empty, r.Ordered, r.Reserved, r.Shipped,
                r.BatchCount, r.LastAt, movements.GetValueOrDefault((r.Kind, r.Id)) ?? new List<PickBatchSummaryDto>(), d.Warehouse, d.Lines);
        }).ToList();
        return new PickDocumentPageDto(total, skip, take, items);
    }

    private sealed record RawRes(int ProductId, int BinId, int? LotId, int WarehouseId, decimal Qty);
    private sealed record RawLine(int ProductId, decimal Ordered, decimal Reserved, decimal Shipped, bool IsKit, List<RawRes> Res);

    /// <summary>
    /// 2026-11-11 (pedido de Luis): lo que hay que recolectar de cada documento de la página, renglón por renglón y con la posición y el lote donde el sistema YA lo reservó (la sugerencia es la reserva,
    /// no una suposición). Solo avisos de salida y órdenes de venta; un envío empacado ya se recolectó. Una línea de kit se muestra por sus piezas (lo que de verdad se saca).
    /// </summary>
    private async Task<Dictionary<(string Kind, int Id), (Guid? Warehouse, IReadOnlyList<PickDocumentLineDto> Lines)>> LinesAsync(List<DocRow> page, CancellationToken ct)
    {
        var raw = new Dictionary<(string, int), List<RawLine>>();
        var exitIds = page.Where(r => r.Kind == KindExitNotice).Select(r => r.Id).ToList();
        var salesIds = page.Where(r => r.Kind == KindSalesOrder).Select(r => r.Id).ToList();

        if (exitIds.Count > 0)
        {
            var lines = await db.Set<ExitNoticeLine>().AsNoTracking().Where(l => exitIds.Contains(l.ExitNoticeId)).OrderBy(l => l.ExitNoticeId).ThenBy(l => l.LineNo).ToListAsync(ct);
            var lineIds = lines.Select(l => l.ExitNoticeLineId).ToList();
            var res = await db.Set<ExitNoticeReservation>().AsNoTracking().Where(r => lineIds.Contains(r.ExitNoticeLineId)).ToListAsync(ct);
            foreach (var l in lines)
            {
                var rs = res.Where(r => r.ExitNoticeLineId == l.ExitNoticeLineId).Select(r => new RawRes(l.ProductId, r.WarehouseBinId, r.LotId, r.WarehouseId, r.Quantity)).ToList();
                if (!raw.TryGetValue((KindExitNotice, l.ExitNoticeId), out var list)) raw[(KindExitNotice, l.ExitNoticeId)] = list = new List<RawLine>();
                list.Add(new RawLine(l.ProductId, l.QtyOrdered, l.QtyReserved, l.QtyShipped, false, rs));
            }
        }
        if (salesIds.Count > 0)
        {
            var lines = await db.Set<SalesOrderLine>().AsNoTracking().Include(l => l.Components).Where(l => salesIds.Contains(l.SalesOrderId)).OrderBy(l => l.SalesOrderId).ThenBy(l => l.LineNo).ToListAsync(ct);
            var lineIds = lines.Select(l => l.SalesOrderLineId).ToList();
            var res = await db.Set<SalesOrderReservation>().AsNoTracking().Where(r => lineIds.Contains(r.SalesOrderLineId)).ToListAsync(ct);
            foreach (var l in lines)
            {
                var mine = res.Where(r => r.SalesOrderLineId == l.SalesOrderLineId).ToList();
                if (!raw.TryGetValue((KindSalesOrder, l.SalesOrderId), out var list)) raw[(KindSalesOrder, l.SalesOrderId)] = list = new List<RawLine>();
                if (l.Components.Count > 0)
                {
                    // línea de kit: una fila por pieza realmente reservada (la que se saca); el producto de la línea es el kit
                    foreach (var g in mine.GroupBy(r => r.ProductId ?? l.ProductId))
                        list.Add(new RawLine(g.Key, 0m, g.Sum(r => r.Quantity), 0m, true, g.Select(r => new RawRes(l.ProductId, r.WarehouseBinId, r.LotId, r.WarehouseId, r.Quantity)).ToList()));
                }
                else
                    list.Add(new RawLine(l.ProductId, l.QtyOrdered, l.QtyReserved, l.QtyShipped, false, mine.Select(r => new RawRes(l.ProductId, r.WarehouseBinId, r.LotId, r.WarehouseId, r.Quantity)).ToList()));
            }
        }
        if (raw.Count == 0) return new();

        var all = raw.Values.SelectMany(v => v).ToList();
        var productIds = all.Select(l => l.ProductId).Concat(all.Where(l => l.IsKit).SelectMany(l => l.Res.Select(r => r.ProductId))).Distinct().ToList();
        var products = await db.Set<Product>().AsNoTracking().Where(p => productIds.Contains(p.ProductId)).Select(p => new { p.ProductId, p.PublicId, p.Sku, p.Name, p.TrackingTypeLookupId, p.ClientId }).ToDictionaryAsync(p => p.ProductId, ct);
        var trackIds = products.Values.Select(x => x.TrackingTypeLookupId).Distinct().ToList();
        var tracking = await db.Set<LookupCode>().AsNoTracking().Where(x => trackIds.Contains(x.LookupCodeId)).ToDictionaryAsync(x => x.LookupCodeId, x => x.InternalCode, ct);
        var ownerIds = products.Values.Where(x => x.ClientId != null).Select(x => x.ClientId!.Value).Distinct().ToList();
        var owners = ownerIds.Count == 0 ? new Dictionary<int, Guid>() : await db.Set<Teikem.Domain.Clients.Client>().AsNoTracking().Where(c => ownerIds.Contains(c.ClientId)).ToDictionaryAsync(c => c.ClientId, c => c.PublicId, ct);
        var binIds = all.SelectMany(l => l.Res.Select(r => r.BinId)).Distinct().ToList();
        var bins = await db.Set<WarehouseBin>().AsNoTracking().Where(b => binIds.Contains(b.WarehouseBinId)).ToDictionaryAsync(b => b.WarehouseBinId, b => b.Code, ct);
        var lotIds = all.SelectMany(l => l.Res.Where(r => r.LotId != null).Select(r => r.LotId!.Value)).Distinct().ToList();
        var lots = lotIds.Count == 0 ? new Dictionary<int, InventoryLot>() : await db.Set<InventoryLot>().AsNoTracking().Where(x => lotIds.Contains(x.LotId)).ToDictionaryAsync(x => x.LotId, ct);
        var whIds = all.SelectMany(l => l.Res.Select(r => r.WarehouseId)).Distinct().ToList();
        var whs = await db.Set<Warehouse>().AsNoTracking().Where(w => whIds.Contains(w.WarehouseId)).ToDictionaryAsync(w => w.WarehouseId, w => w.PublicId, ct);

        var result = new Dictionary<(string, int), (Guid?, IReadOnlyList<PickDocumentLineDto>)>();
        foreach (var (key, lines) in raw)
        {
            var dtos = new List<PickDocumentLineDto>();
            foreach (var l in lines)
            {
                if (!products.TryGetValue(l.ProductId, out var p)) continue;
                var locs = l.Res.Where(r => r.Qty > 0m).Select(r => new PickDocumentLocationDto(r.BinId, bins.GetValueOrDefault(r.BinId) ?? string.Empty, r.LotId,
                    r.LotId is int lid ? lots.GetValueOrDefault(lid)?.LotNumber : null, r.LotId is int lid2 ? lots.GetValueOrDefault(lid2)?.ExpiryDate : null, r.Qty)).ToList();
                var kitSku = l.IsKit && l.Res.Count > 0 && products.TryGetValue(l.Res[0].ProductId, out var kit) ? kit.Sku : null;
                dtos.Add(new PickDocumentLineDto(p.PublicId, p.Sku, p.Name, l.IsKit ? null : l.Ordered, l.Reserved, l.Shipped, kitSku, locs,
                    tracking.GetValueOrDefault(p.TrackingTypeLookupId), p.ClientId is int oc ? owners.GetValueOrDefault(oc) : null));
            }
            var wid = lines.SelectMany(l => l.Res).Select(r => (int?)r.WarehouseId).FirstOrDefault();
            result[key] = (wid is int w ? whs.GetValueOrDefault(w) : null, dtos);
        }
        return result;
    }

    /// <summary>Las recolecciones activas de los documentos de la página (la más reciente primero), con su total sacado.</summary>
    private async Task<Dictionary<(string Kind, int Id), List<PickBatchSummaryDto>>> MovementsAsync(List<DocRow> page, CancellationToken ct)
    {
        var exitIds = page.Where(r => r.Kind == KindExitNotice).Select(r => r.Id).ToList();
        var salesIds = page.Where(r => r.Kind == KindSalesOrder).Select(r => r.Id).ToList();
        var orderIds = page.Where(r => r.Kind == KindOrder).Select(r => r.Id).ToList();
        var batches = await db.Set<PickBatch>().AsNoTracking()
            .Where(b => b.IsActive && ((b.ExitNoticeId != null && exitIds.Contains(b.ExitNoticeId.Value))
                                       || (b.ExitNoticeId == null && b.SalesOrderId != null && salesIds.Contains(b.SalesOrderId.Value))
                                       || (b.ExitNoticeId == null && b.SalesOrderId == null && b.TransportOrderId != null && orderIds.Contains(b.TransportOrderId.Value))))
            .OrderByDescending(b => b.CollectedAtUtc).ThenByDescending(b => b.PickBatchId).ToListAsync(ct);
        var batchIds = batches.Select(b => b.PickBatchId).ToList();
        var totals = await db.Set<PickBatchLine>().AsNoTracking().Where(l => batchIds.Contains(l.PickBatchId)).GroupBy(l => l.PickBatchId)
            .Select(g => new { Id = g.Key, Qty = g.Sum(l => l.Quantity) }).ToDictionaryAsync(x => x.Id, x => x.Qty, ct);
        var statusMap = await ReceivingSupport.StatusMapAsync(db, tenant, StatusDomains.PickBatchStatus, ct);
        var groupIds = batches.Where(b => b.PickGroupId != null).Select(b => b.PickGroupId!.Value).Distinct().ToList();
        var groupNumbers = groupIds.Count == 0 ? new Dictionary<int, string>() : await db.Set<PickGroup>().AsNoTracking().Where(g => groupIds.Contains(g.PickGroupId)).ToDictionaryAsync(g => g.PickGroupId, g => g.Number, ct);
        var userIds = batches.Where(b => b.CollectedBy != null).Select(b => b.CollectedBy!.Value).Distinct().ToList();
        var users = await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).Select(u => new { u.Id, u.FullName, u.UserName }).ToDictionaryAsync(u => u.Id, ct);

        var result = new Dictionary<(string, int), List<PickBatchSummaryDto>>();
        foreach (var b in batches)
        {
            var key = b.ExitNoticeId is int e ? (KindExitNotice, e) : b.SalesOrderId is int s ? (KindSalesOrder, s) : (KindOrder, b.TransportOrderId ?? 0);
            var st = statusMap.GetValueOrDefault(b.StatusCodeId);
            var by = b.CollectedBy is int uid && users.TryGetValue(uid, out var u) ? (u.FullName ?? u.UserName) : null;
            if (!result.TryGetValue(key, out var list)) result[key] = list = new List<PickBatchSummaryDto>();
            list.Add(new PickBatchSummaryDto(b.PublicId, b.Number, st?.Code ?? string.Empty, st?.Label ?? string.Empty, b.CollectedAtUtc, by, totals.GetValueOrDefault(b.PickBatchId), b.PickGroupId is int gid ? groupNumbers.GetValueOrDefault(gid) : null));
        }
        return result;
    }
}
