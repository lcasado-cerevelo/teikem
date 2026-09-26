using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Common;
using Teikem.Domain.Constants;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 6 (P3) — trazabilidad por lote y serie (R29) y conciliación ledger ↔ saldo (invariante del riesgo 1).
/// - Genealogía de lote: se arma por LINQ con el filtro de tenant (D32); la vista SQL vw_LotGenealogy ampliada queda para BI
///   y SQL externo. El lote (sin TenantId) se alcanza por su producto filtrado Y acotado por el InventoryScope: un lote de
///   otro tenant o de otro dueño → 404 'Lote no encontrado.' (sin oráculo).
///   Destinos: ISSUE con Ref PICK_BATCH → la orden del empaque (si ya se empacó); Ref TRANSPORT_ORDER → la orden;
///   CROSSDOCK con Ref CROSSDOCK_ALLOCATION → la orden de la asignación. La cantidad del destino es neta (una reversa con
///   la misma referencia la neutraliza; un destino con neto ≤ 0 no se muestra).
/// - Rastro de serie: movimientos del ledger más el historial de estatus INVENTORY_SERIAL (EntityStatusHistory filtrado por
///   tenant). El controlador exige inventory.view, el mismo permiso de lectura que INVENTORY_SERIAL.
/// - Conciliación: reconstruye cada saldo desde el ledger (To suma |Q|, From resta |Q|) y lo compara con StockBalance
///   por clave; además compara por producto Σ Quantity sin TRANSFER contra Σ QtyOnHand (lectura literal de L331).
///   Solo lectura: nunca corrige nada.
/// </summary>
public sealed class TraceabilityService(TeikemDbContext db, ITenantContext tenant, ILookupCache lookups, InventoryReadService reads)
{
    /// <summary>Tope de movimientos que devuelve la genealogía (los totales se calculan sobre TODOS en SQL).</summary>
    public const int MaxGenealogyMovements = 1000;
    /// <summary>Tope de movimientos del rastro de una serie.</summary>
    public const int MaxSerialMovements = 1000;
    public const string SerialNumberRequired = "Indique el número de serie.";
    public const string ProductRequired = "Indique el producto.";
    /// <summary>Código de almacén de la fila de descuadre por producto (total del producto, sin ubicación).</summary>
    public const string ProductTotalMarker = "—";

    // ================================================================ genealogía de lote

    public async Task<GenealogyDto> LotGenealogyAsync(int lotId, InventoryScope scope, CancellationToken ct)
    {
        scope ??= InventoryScope.Any;
        var hit = await (from l in db.Set<InventoryLot>().AsNoTracking()
                         join p in reads.ScopedProducts(scope) on l.ProductId equals p.ProductId
                         where l.LotId == lotId
                         select new { Lot = l, Product = p }).FirstOrDefaultAsync(ct)
                  ?? throw new NotFoundException("Lote");
        var lot = hit.Lot;
        var product = hit.Product;

        var txns = db.Set<InventoryTransaction>().AsNoTracking().Where(t => t.ProductId == product.ProductId && t.LotId == lot.LotId);
        var transferId = await lookups.TryGetIdAsync(LookupDomains.InventoryTxnType, InventoryTxnTypes.Transfer, ct) ?? -1;

        // Entradas y salidas con signo del ledger, sin TRANSFER (no cambia la existencia del lote).
        var stockMoves = txns.Where(t => t.TxnTypeLookupId != transferId);
        var qtyIn = await stockMoves.SumAsync(t => t.Quantity > 0 ? t.Quantity : 0m, ct);
        var qtyOut = await stockMoves.SumAsync(t => t.Quantity < 0 ? -t.Quantity : 0m, ct);
        var onHand = await db.Set<StockBalance>().AsNoTracking()
            .Where(b => b.ProductId == product.ProductId && b.LotId == lot.LotId)
            .SumAsync(b => b.QtyOnHand, ct);

        var movements = await txns.OrderBy(t => t.CreatedAtUtc).ThenBy(t => t.InventoryTransactionId)
            .Take(MaxGenealogyMovements).ToListAsync(ct);
        var rows = await reads.ToKardexRowsAsync(movements, KardexLocationFilter.None, ct);

        var destinations = await DestinationsAsync(txns, transferId, ct);
        return new GenealogyDto(product.PublicId, product.Sku, product.Name, lot.LotId, lot.LotNumber, lot.ManufactureDate, lot.ExpiryDate,
            qtyIn, qtyOut, onHand, rows, destinations);
    }

    /// <summary>
    /// Destinos del lote: referencias con al menos una salida (ISSUE o CROSSDOCK) y cantidad neta &gt; 0, resueltas a su
    /// orden de transporte. Una consulta agrupada por referencia y una por tipo de documento (sin N+1).
    /// </summary>
    private async Task<IReadOnlyList<GenealogyDestinationDto>> DestinationsAsync(IQueryable<InventoryTransaction> txns, int transferId,
        CancellationToken ct)
    {
        var grouped = await txns.Where(t => t.RefEntityLookupId != null && t.RefId != null && t.TxnTypeLookupId != transferId)
            .GroupBy(t => new { t.RefEntityLookupId, t.RefId, t.TxnTypeLookupId })
            .Select(g => new { g.Key.RefEntityLookupId, g.Key.RefId, g.Key.TxnTypeLookupId, Qty = g.Sum(t => t.Quantity) })
            .ToListAsync(ct);
        if (grouped.Count == 0) return Array.Empty<GenealogyDestinationDto>();

        var codes = new Dictionary<int, string>();
        foreach (var id in grouped.SelectMany(g => new[] { g.RefEntityLookupId!.Value, g.TxnTypeLookupId }).Distinct())
            if (await lookups.GetAsync(id, ct) is { } lc) codes[id] = lc.InternalCode;

        var candidates = grouped
            .GroupBy(g => (Code: codes.GetValueOrDefault(g.RefEntityLookupId!.Value) ?? string.Empty, Id: g.RefId!.Value))
            .Where(g => g.Any(x => KardexRules.IsOutbound(codes.GetValueOrDefault(x.TxnTypeLookupId) ?? string.Empty)))
            .Select(g => (g.Key.Code, g.Key.Id,
                Qty: KardexRules.DestinationQty(g.Select(x => (codes.GetValueOrDefault(x.TxnTypeLookupId) ?? string.Empty, x.Qty)))))
            .Where(c => c.Qty > 0)
            .ToList();
        if (candidates.Count == 0) return Array.Empty<GenealogyDestinationDto>();

        // Documento → orden de transporte (y número del documento para el origen legible).
        var orderOf = new Dictionary<(string, int), int?>();
        var numberOf = new Dictionary<(string, int), string?>();
        var pickIds = candidates.Where(c => c.Code == EntityTypes.PickBatch).Select(c => c.Id).Distinct().ToList();
        if (pickIds.Count > 0)
            foreach (var b in await db.Set<PickBatch>().AsNoTracking().Where(b => pickIds.Contains(b.PickBatchId))
                         .Select(b => new { b.PickBatchId, b.Number, b.TransportOrderId }).ToListAsync(ct))
            {
                orderOf[(EntityTypes.PickBatch, b.PickBatchId)] = b.TransportOrderId;
                numberOf[(EntityTypes.PickBatch, b.PickBatchId)] = b.Number;
            }
        var allocIds = candidates.Where(c => c.Code == EntityTypes.CrossDockAllocation).Select(c => c.Id).Distinct().ToList();
        if (allocIds.Count > 0)
            // La asignación no lleva TenantId: se alcanza por su plan filtrado.
            foreach (var a in await (from a in db.Set<CrossDockAllocation>().AsNoTracking()
                                     join pl in db.Set<CrossDockPlan>().AsNoTracking() on a.CrossDockPlanId equals pl.CrossDockPlanId
                                     where allocIds.Contains(a.CrossDockAllocationId)
                                     select new { a.CrossDockAllocationId, OrderId = (int?)a.TransportOrderId, pl.Number }).ToListAsync(ct))
            {
                orderOf[(EntityTypes.CrossDockAllocation, a.CrossDockAllocationId)] = a.OrderId;
                numberOf[(EntityTypes.CrossDockAllocation, a.CrossDockAllocationId)] = a.Number;
            }
        foreach (var c in candidates.Where(c => c.Code == EntityTypes.TransportOrder)) orderOf[(c.Code, c.Id)] = c.Id;

        var orderIds = orderOf.Values.Where(v => v.HasValue).Select(v => v!.Value).Distinct().ToList();
        var orders = orderIds.Count == 0
            ? new Dictionary<int, OrderRef>()
            : await db.TransportOrders.AsNoTracking().Where(o => orderIds.Contains(o.TransportOrderId))
                .Select(o => new OrderRef(o.TransportOrderId, o.PublicId, o.OrderNumber, o.PackBatchNumber, o.ClientId))
                .ToDictionaryAsync(o => o.Id, ct);
        var clients = await reads.ClientNamesAsync(orders.Values.Select(o => o.ClientId), ct);
        var consignees = await ConsigneesAsync(orders.Keys, ct);

        var result = new List<GenealogyDestinationDto>(candidates.Count);
        foreach (var (code, id, qty) in candidates.OrderBy(c => c.Code).ThenBy(c => c.Id))
        {
            var order = orderOf.GetValueOrDefault((code, id)) is int oid ? orders.GetValueOrDefault(oid) : null;
            var number = code == EntityTypes.TransportOrder ? order?.OrderNumber : numberOf.GetValueOrDefault((code, id));
            result.Add(new GenealogyDestinationDto(code, id, KardexRules.RefLabel(code, id, number),
                order?.PublicId, order?.PackBatchNumber,
                order is null ? null : clients.GetValueOrDefault(order.ClientId),
                order is null ? null : consignees.GetValueOrDefault(order.Id), qty));
        }
        return result;
    }

    private sealed record OrderRef(int Id, Guid PublicId, string OrderNumber, string PackBatchNumber, int ClientId);

    /// <summary>Consignatario = nombre de la primera parada DELIVERY de la orden (por secuencia).</summary>
    private async Task<Dictionary<int, string>> ConsigneesAsync(IEnumerable<int> orderIds, CancellationToken ct)
    {
        var ids = orderIds.Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<int, string>();
        var deliveryId = await lookups.TryGetIdAsync(LookupDomains.StopType, StopTypes.Delivery, ct);
        if (deliveryId is null) return new Dictionary<int, string>();
        var stops = await (from s in db.OrderStops.AsNoTracking()
                           join o in db.TransportOrders.AsNoTracking() on s.TransportOrderId equals o.TransportOrderId
                           where ids.Contains(s.TransportOrderId) && s.StopTypeLookupId == deliveryId && s.SnapName != null
                           select new { s.TransportOrderId, s.Sequence, s.SnapName }).ToListAsync(ct);
        return stops.GroupBy(s => s.TransportOrderId)
            .ToDictionary(g => g.Key, g => g.OrderBy(s => s.Sequence).First().SnapName!);
    }

    // ================================================================ rastro de serie

    public async Task<SerialTraceDto> SerialTraceAsync(Guid productPublicId, string serialNumber, InventoryScope scope, CancellationToken ct)
    {
        scope ??= InventoryScope.Any;
        var number = serialNumber?.Trim();
        if (string.IsNullOrEmpty(number)) throw new ValidationException("serialNumber", SerialNumberRequired);
        var product = await reads.ScopedProducts(scope).FirstOrDefaultAsync(p => p.PublicId == productPublicId, ct)
                      ?? throw new NotFoundException("Producto");
        var serial = await db.Set<InventorySerial>().AsNoTracking()
                         .FirstOrDefaultAsync(s => s.ProductId == product.ProductId && s.SerialNumber == number, ct)
                     ?? throw new NotFoundException("Serie", feminine: true);

        // Ficha de la serie: lote, estatus y ubicación actual.
        string? lotNumber = null;
        if (serial.LotId is int lid) lotNumber = (await reads.LotInfoAsync(new[] { lid }, ct)).GetValueOrDefault(lid)?.Number;
        string? statusCode = null, statusLabel = null;
        if (serial.StatusCodeId is int sid)
        {
            var st = await db.StatusCodes.AsNoTracking().FirstOrDefaultAsync(s => s.StatusCodeId == sid, ct);
            if (st is not null)
            {
                var ov = await db.StatusCodeOverrides.AsNoTracking().FirstOrDefaultAsync(o => o.StatusCodeId == sid, ct);
                statusCode = st.InternalCode;
                statusLabel = MultilingualText.Resolve(MultilingualText.Merge(st.LabelJson, ov?.CustomLabelJson), tenant.Lang);
            }
        }
        var wh = serial.CurrentWarehouseId is int wid ? (await reads.WarehouseInfoAsync(new[] { wid }, ct)).GetValueOrDefault(wid) : null;
        var binCode = serial.CurrentBinId is int bid ? (await reads.BinInfoAsync(new[] { bid }, ct)).GetValueOrDefault(bid)?.Code : null;
        var dto = new SerialDto(serial.SerialId, serial.SerialNumber, serial.LotId, lotNumber, statusCode, statusLabel,
            wh?.PublicId, wh?.Code, serial.CurrentBinId, binCode);

        var movements = await db.Set<InventoryTransaction>().AsNoTracking()
            .Where(t => t.ProductId == product.ProductId && t.SerialId == serial.SerialId)
            .OrderBy(t => t.CreatedAtUtc).ThenBy(t => t.InventoryTransactionId)
            .Take(MaxSerialMovements).ToListAsync(ct);
        var rows = await reads.ToKardexRowsAsync(movements, KardexLocationFilter.None, ct);

        return new SerialTraceDto(dto, product.PublicId, product.Sku, rows, await SerialHistoryAsync(serial.SerialId, ct));
    }

    /// <summary>Historial de estatus INVENTORY_SERIAL de la serie (EntityStatusHistory lleva filtro de tenant).</summary>
    private async Task<IReadOnlyList<StatusHistoryDto>> SerialHistoryAsync(int serialId, CancellationToken ct)
    {
        var entityTypeId = await lookups.TryGetIdAsync(LookupDomains.EntityType, EntityTypes.InventorySerial, ct);
        if (entityTypeId is null) return Array.Empty<StatusHistoryDto>();
        var rows = await db.EntityStatusHistories.AsNoTracking().Include(h => h.FromStatus).Include(h => h.ToStatus)
            .Where(h => h.EntityTypeLookupId == entityTypeId && h.EntityId == serialId)
            .OrderBy(h => h.EntityStatusHistoryId).ToListAsync(ct);
        var userIds = rows.Where(r => r.ChangedBy.HasValue).Select(r => r.ChangedBy!.Value).Distinct().ToList();
        var users = userIds.Count == 0
            ? new Dictionary<int, string>()
            : await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id))
                .Select(u => new { u.Id, Name = u.FullName ?? u.Email ?? u.UserName ?? "" })
                .ToDictionaryAsync(u => u.Id, u => u.Name, ct);
        return rows.Select(h => new StatusHistoryDto(h.EntityStatusHistoryId,
            h.FromStatus?.InternalCode, h.FromStatus is null ? null : MultilingualText.Resolve(h.FromStatus.LabelJson, tenant.Lang),
            h.ToStatus?.InternalCode ?? string.Empty, h.ToStatus is null ? string.Empty : MultilingualText.Resolve(h.ToStatus.LabelJson, tenant.Lang),
            h.Comment, h.ChangedAtUtc, h.ChangedBy, h.ChangedBy is int u ? users.GetValueOrDefault(u) : null)).ToList();
    }

    // ================================================================ conciliación ledger ↔ saldo

    /// <summary>
    /// Reconstruye los saldos desde el ledger y los compara con StockBalance (todo el tenant o un producto). Descuadres:
    /// (a) por clave (producto, almacén, posición, lote): Σ To |Q| − Σ From |Q| ≠ QtyOnHand; (b) por producto:
    /// Σ Quantity sin TRANSFER ≠ Σ QtyOnHand (solo si el producto no tiene ya un descuadre por clave; la fila lleva
    /// almacén '—'). Sin descuadres = el invariante del ledger se cumple.
    /// </summary>
    public async Task<ReconciliationDto> ReconcileAsync(Guid? productPublicId, CancellationToken ct)
    {
        int? productId = null;
        if (productPublicId is Guid pid)
            productId = (await db.Set<Product>().AsNoTracking().FirstOrDefaultAsync(p => p.PublicId == pid, ct) ?? throw new NotFoundException("Producto")).ProductId;

        var txns = db.Set<InventoryTransaction>().AsNoTracking().AsQueryable();
        var balancesQuery = db.Set<StockBalance>().AsNoTracking().AsQueryable();
        if (productId is int only)
        {
            txns = txns.Where(t => t.ProductId == only);
            balancesQuery = balancesQuery.Where(b => b.ProductId == only);
        }

        var toSides = await txns.Where(t => t.ToWarehouseId != null)
            .GroupBy(t => new { t.ProductId, t.ToWarehouseId, t.ToBinId, t.LotId })
            .Select(g => new { g.Key.ProductId, g.Key.ToWarehouseId, g.Key.ToBinId, g.Key.LotId, Qty = g.Sum(t => Math.Abs(t.Quantity)) })
            .ToListAsync(ct);
        var fromSides = await txns.Where(t => t.FromWarehouseId != null)
            .GroupBy(t => new { t.ProductId, t.FromWarehouseId, t.FromBinId, t.LotId })
            .Select(g => new { g.Key.ProductId, g.Key.FromWarehouseId, g.Key.FromBinId, g.Key.LotId, Qty = g.Sum(t => Math.Abs(t.Quantity)) })
            .ToListAsync(ct);
        var rebuilt = KardexRules.Rebuild(
            toSides.Select(x => (new LedgerKey(x.ProductId, x.ToWarehouseId!.Value, x.ToBinId, x.LotId), x.Qty)),
            fromSides.Select(x => (new LedgerKey(x.ProductId, x.FromWarehouseId!.Value, x.FromBinId, x.LotId), x.Qty)));

        var transferId = await lookups.TryGetIdAsync(LookupDomains.InventoryTxnType, InventoryTxnTypes.Transfer, ct) ?? -1;
        var netByProduct = await txns.Where(t => t.TxnTypeLookupId != transferId)
            .GroupBy(t => t.ProductId)
            .Select(g => new { ProductId = g.Key, Net = g.Sum(t => t.Quantity) })
            .ToDictionaryAsync(x => x.ProductId, x => x.Net, ct);

        var balances = await balancesQuery
            .Select(b => new { b.ProductId, b.WarehouseId, b.WarehouseBinId, b.LotId, b.QtyOnHand })
            .ToListAsync(ct);
        var balanceByKey = balances.GroupBy(b => new LedgerKey(b.ProductId, b.WarehouseId, b.WarehouseBinId, b.LotId))
            .ToDictionary(g => g.Key, g => g.Sum(b => b.QtyOnHand));

        var mismatches = new List<(LedgerKey Key, decimal Ledger, decimal Balance, bool ProductTotal)>();
        foreach (var key in rebuilt.Keys.Union(balanceByKey.Keys))
        {
            var ledgerQty = rebuilt.GetValueOrDefault(key);
            var balanceQty = balanceByKey.GetValueOrDefault(key);
            if (ledgerQty != balanceQty) mismatches.Add((key, ledgerQty, balanceQty, false));
        }
        var productsWithKeyMismatch = mismatches.Select(m => m.Key.ProductId).ToHashSet();
        var onHandByProduct = balances.GroupBy(b => b.ProductId).ToDictionary(g => g.Key, g => g.Sum(b => b.QtyOnHand));
        foreach (var p in netByProduct.Keys.Union(onHandByProduct.Keys))
        {
            if (productsWithKeyMismatch.Contains(p)) continue;
            var net = netByProduct.GetValueOrDefault(p);
            var onHand = onHandByProduct.GetValueOrDefault(p);
            if (net != onHand) mismatches.Add((new LedgerKey(p, 0, null, null), net, onHand, true));
        }

        var rows = new List<ReconciliationRowDto>(mismatches.Count);
        if (mismatches.Count > 0)
        {
            var products = await reads.ProductInfoAsync(mismatches.Select(m => m.Key.ProductId).Distinct().ToList(), ct);
            var warehouses = await reads.WarehouseInfoAsync(mismatches.Where(m => !m.ProductTotal).Select(m => m.Key.WarehouseId), ct);
            var bins = await reads.BinInfoAsync(mismatches.Where(m => m.Key.BinId.HasValue).Select(m => m.Key.BinId!.Value), ct);
            var lots = await reads.LotInfoAsync(mismatches.Where(m => m.Key.LotId.HasValue).Select(m => m.Key.LotId!.Value), ct);
            foreach (var m in mismatches.OrderBy(m => m.Key.ProductId).ThenBy(m => m.ProductTotal).ThenBy(m => m.Key.WarehouseId)
                         .ThenBy(m => m.Key.BinId).ThenBy(m => m.Key.LotId))
            {
                var p = products.GetValueOrDefault(m.Key.ProductId);
                rows.Add(new ReconciliationRowDto(p?.PublicId ?? Guid.Empty, p?.Sku ?? string.Empty,
                    m.ProductTotal ? ProductTotalMarker : warehouses.GetValueOrDefault(m.Key.WarehouseId)?.Code ?? string.Empty,
                    m.Key.BinId is int b ? bins.GetValueOrDefault(b)?.Code : null,
                    m.Key.LotId is int l ? lots.GetValueOrDefault(l)?.Number : null,
                    m.Ledger, m.Balance));
            }
        }
        return new ReconciliationDto(DateTime.UtcNow, balances.Count, rows);
    }
}
