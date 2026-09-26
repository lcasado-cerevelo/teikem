using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Clients;
using Teikem.Domain.Common;
using Teikem.Domain.Constants;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 6 (P3) — lecturas de inventario: saldos por almacén/posición/lote (R1, R27) y Kárdex de solo lectura (R28, R33).
/// - Solo lectura: nunca escribe StockBalance ni InventoryTransaction (la única vía de escritura es InventoryLedger).
/// - El TenantId sale del principal: StockBalance, InventoryTransaction, Product y Warehouse llevan el filtro global de
///   tenant. Las hijas sin TenantId (posición, zona, lote, serie) se alcanzan SIEMPRE uniendo con su padre filtrado
///   (Warehouse o Product): un id de otro tenant no devuelve nada.
/// - Todas las lecturas reciben InventoryScope (D44): con OwnerClientId solo se ven movimientos y saldos de productos de
///   ese dueño. Los controladores internos pasan InventoryScope.Any; el Portal (Lote 8) pasará el cliente.
/// - QtyAvailable (columna computada) NUNCA se lee: el disponible se calcula en código (en mano − reservado), porque
///   InMemory no calcula columnas computadas y la regla vive en InventoryRules.Available.
/// - Kárdex: Quantity = la cantidad del ledger CON signo (D3, L331); SignedQuantity = perspectiva del filtro de ubicación
///   (KardexRules.SignedQuantity). Fechas en UTC: desde inclusivo, hasta EXCLUSIVO (+1 día). Orden CreatedAtUtc desc, Id desc.
///   Origen legible (RefLabel), motivo y usuario se resuelven por lotes de consultas (sin N+1).
/// </summary>
public sealed class InventoryReadService(TeikemDbContext db, ITenantContext tenant, ILookupCache lookups)
{
    // ================================================================ saldos

    public async Task<BalancePageDto> BalancesAsync(BalanceQuery q, InventoryScope scope, CancellationToken ct)
    {
        q ??= new BalanceQuery();
        scope ??= InventoryScope.Any;
        var (skip, take) = KardexRules.Page(q.Skip, q.Take, InventoryRules.MaxPageSize);

        var query = db.Set<StockBalance>().AsNoTracking().AsQueryable();

        var productIds = await FilteredProductIdsAsync(scope, q.ProductPublicIds, q.CategoryIds, ct);
        if (productIds is not null) query = query.Where(b => productIds.Contains(b.ProductId));

        if (q.WarehousePublicIds is { Length: > 0 })
        {
            var whIds = await WarehouseIdsAsync(q.WarehousePublicIds, ct);
            query = query.Where(b => whIds.Contains(b.WarehouseId));
        }
        if (q.BinIds is { Length: > 0 })
        {
            var binIds = q.BinIds.Select(i => (int?)i).Distinct().ToList();
            query = query.Where(b => binIds.Contains(b.WarehouseBinId));
        }
        if (!string.IsNullOrWhiteSpace(q.LotNumber))
        {
            var lotTerm = q.LotNumber.Trim();
            var lotIds = LotIdsMatching(lotTerm);
            query = query.Where(b => lotIds.Contains(b.LotId));
        }
        if (!q.IncludeZero) query = query.Where(b => b.QtyOnHand != 0 || b.QtyReserved != 0);
        if (q.OnlyAvailable) query = query.Where(b => b.QtyOnHand - b.QtyReserved > 0);
        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var s = q.Search.Trim();
            var bySku = db.Set<Product>().AsNoTracking()
                .Where(p => p.Sku.Contains(s) || p.Name.Contains(s) || (p.Barcode != null && p.Barcode.Contains(s)))
                .Select(p => p.ProductId);
            var byLot = LotIdsMatching(s);
            var byBin = from b in db.Set<WarehouseBin>().AsNoTracking()
                        join w in db.Set<Warehouse>().AsNoTracking() on b.WarehouseId equals w.WarehouseId
                        where b.Code.Contains(s)
                        select (int?)b.WarehouseBinId;
            query = query.Where(b => bySku.Contains(b.ProductId) || byLot.Contains(b.LotId) || byBin.Contains(b.WarehouseBinId));
        }

        var total = await query.CountAsync(ct);
        var totalOnHand = total == 0 ? 0m : await query.SumAsync(b => b.QtyOnHand, ct);
        var totalReserved = total == 0 ? 0m : await query.SumAsync(b => b.QtyReserved, ct);

        var ordered = from b in query
                      join p in db.Set<Product>().AsNoTracking() on b.ProductId equals p.ProductId
                      join w in db.Set<Warehouse>().AsNoTracking() on b.WarehouseId equals w.WarehouseId
                      orderby w.Code, p.Sku, b.WarehouseBinId, b.LotId, b.StockBalanceId
                      select b;
        var page = await ordered.Skip(skip).Take(take).ToListAsync(ct);
        var items = await ToBalanceDtosAsync(page, ct);
        return new BalancePageDto(total, skip, take, InventoryRules.Round4(totalOnHand),
            InventoryRules.Round4(InventoryRules.Available(totalOnHand, totalReserved)), items);
    }

    /// <summary>
    /// Saldos de las claves indicadas (producto, almacén, posición, lote), en el orden de las claves; una clave sin fila de
    /// saldo se omite. Lo usa el resultado de ajustes y transferencias.
    /// </summary>
    public async Task<IReadOnlyList<BalanceDto>> BalancesForKeysAsync(IEnumerable<LedgerKey> keys, CancellationToken ct)
    {
        var wanted = keys.Distinct().ToList();
        if (wanted.Count == 0) return Array.Empty<BalanceDto>();
        var productIds = wanted.Select(k => k.ProductId).Distinct().ToList();
        var warehouseIds = wanted.Select(k => k.WarehouseId).Distinct().ToList();
        var candidates = await db.Set<StockBalance>().AsNoTracking()
            .Where(b => productIds.Contains(b.ProductId) && warehouseIds.Contains(b.WarehouseId))
            .ToListAsync(ct);
        var byKey = candidates.GroupBy(b => new LedgerKey(b.ProductId, b.WarehouseId, b.WarehouseBinId, b.LotId))
            .ToDictionary(g => g.Key, g => g.First());
        var rows = wanted.Where(byKey.ContainsKey).Select(k => byKey[k]).ToList();
        return await ToBalanceDtosAsync(rows, ct);
    }

    /// <summary>Proyección de saldos a DTO en el mismo orden de entrada, con los catálogos cargados por lotes (sin N+1).</summary>
    public async Task<IReadOnlyList<BalanceDto>> ToBalanceDtosAsync(IReadOnlyList<StockBalance> rows, CancellationToken ct)
    {
        if (rows.Count == 0) return Array.Empty<BalanceDto>();
        var refs = await LoadBalanceRefsAsync(rows, ct);
        var result = new List<BalanceDto>(rows.Count);
        foreach (var b in rows)
        {
            // El saldo y su producto comparten tenant; un producto ausente solo ocurre con datos corruptos.
            if (!refs.Products.TryGetValue(b.ProductId, out var p)) continue;
            var w = refs.Warehouses.GetValueOrDefault(b.WarehouseId);
            var bin = b.WarehouseBinId is int bid ? refs.Bins.GetValueOrDefault(bid) : null;
            var zone = bin is not null ? refs.Zones.GetValueOrDefault(bin.ZoneId) : null;
            var lot = b.LotId is int lid ? refs.Lots.GetValueOrDefault(lid) : null;
            result.Add(new BalanceDto(b.StockBalanceId, w?.PublicId ?? Guid.Empty, w?.Code ?? string.Empty,
                b.WarehouseBinId, bin?.Code, zone?.Code, zone?.TypeCode,
                p.PublicId, p.Sku, p.Name,
                p.CategoryId is int cid ? refs.Categories.GetValueOrDefault(cid) : null,
                KardexRules.OwnerLabel(p.ClientId is int oid ? refs.Owners.GetValueOrDefault(oid) : null), p.ClientId is null,
                b.LotId, lot?.Number, lot?.Expiry,
                b.QtyOnHand, b.QtyReserved, InventoryRules.Available(b.QtyOnHand, b.QtyReserved),
                Value(b.QtyOnHand, p.PurchaseCost), Value(b.QtyOnHand, p.SalePrice), b.UpdatedAtUtc));
        }
        return result;
    }

    /// <summary>Catálogos de una tanda de saldos (lo reutiliza StockBalanceDataSource).</summary>
    internal async Task<BalanceRefs> LoadBalanceRefsAsync(IReadOnlyList<StockBalance> rows, CancellationToken ct)
    {
        var productIds = rows.Select(r => r.ProductId).Distinct().ToList();
        var products = await ProductInfoAsync(productIds, ct);
        var warehouses = await WarehouseInfoAsync(rows.Select(r => r.WarehouseId), ct);
        var bins = await BinInfoAsync(rows.Where(r => r.WarehouseBinId.HasValue).Select(r => r.WarehouseBinId!.Value), ct);
        var zones = await ZoneInfoAsync(bins.Values.Select(b => b.ZoneId), ct);
        var lots = await LotInfoAsync(rows.Where(r => r.LotId.HasValue).Select(r => r.LotId!.Value), ct);
        var categories = await CategoryNamesAsync(products.Values.Where(p => p.CategoryId.HasValue).Select(p => p.CategoryId!.Value), ct);
        var owners = await ClientNamesAsync(products.Values.Where(p => p.ClientId.HasValue).Select(p => p.ClientId!.Value), ct);
        return new BalanceRefs(products, warehouses, bins, zones, lots, categories, owners);
    }

    // ================================================================ Kárdex

    public async Task<KardexPageDto> KardexAsync(KardexQuery q, InventoryScope scope, CancellationToken ct)
    {
        q ??= new KardexQuery();
        scope ??= InventoryScope.Any;
        if (KardexRules.IsRangeInverted(q.From, q.To)) throw new ValidationException("to", KardexRules.RangeInverted);
        var (skip, take) = KardexRules.Page(q.Skip, q.Take, InventoryRules.MaxPageSize);
        var (fromUtc, toUtc) = KardexRules.UtcRange(q.From, q.To);

        var query = db.Set<InventoryTransaction>().AsNoTracking().AsQueryable();

        var productIds = await FilteredProductIdsAsync(scope, q.ProductPublicIds, q.CategoryIds, ct);
        if (productIds is not null) query = query.Where(t => productIds.Contains(t.ProductId));
        if (fromUtc is DateTime f) query = query.Where(t => t.CreatedAtUtc >= f);
        if (toUtc is DateTime to) query = query.Where(t => t.CreatedAtUtc < to);

        if (q.Types is { Length: > 0 })
        {
            var typeIds = new List<int>();
            foreach (var code in SplitCodes(q.Types))
            {
                var id = await lookups.TryGetIdAsync(LookupDomains.InventoryTxnType, code, ct)
                         ?? throw new ValidationException("types", KardexRules.UnknownTypeMessage(code));
                typeIds.Add(id);
            }
            query = query.Where(t => typeIds.Contains(t.TxnTypeLookupId));
        }

        // Filtro de ubicación: el movimiento aparece si su origen o su destino cae en el filtro (almacén Y posición cuando
        // se indican ambos). La misma definición da la perspectiva de SignedQuantity.
        List<int?>? whIds = null;
        List<int?>? binIds = null;
        if (q.WarehousePublicIds is { Length: > 0 })
            whIds = (await WarehouseIdsAsync(q.WarehousePublicIds, ct)).Select(i => (int?)i).ToList();
        if (q.BinIds is { Length: > 0 }) binIds = q.BinIds.Select(i => (int?)i).Distinct().ToList();
        if (whIds is not null && binIds is not null)
            query = query.Where(t => (whIds.Contains(t.FromWarehouseId) && binIds.Contains(t.FromBinId))
                                     || (whIds.Contains(t.ToWarehouseId) && binIds.Contains(t.ToBinId)));
        else if (whIds is not null)
            query = query.Where(t => whIds.Contains(t.FromWarehouseId) || whIds.Contains(t.ToWarehouseId));
        else if (binIds is not null)
            query = query.Where(t => binIds.Contains(t.FromBinId) || binIds.Contains(t.ToBinId));
        var filter = new KardexLocationFilter(
            whIds?.Where(i => i.HasValue).Select(i => i!.Value).ToHashSet(),
            binIds?.Where(i => i.HasValue).Select(i => i!.Value).ToHashSet());

        if (!string.IsNullOrWhiteSpace(q.LotNumber))
        {
            var lotIds = LotIdsMatching(q.LotNumber.Trim());
            query = query.Where(t => lotIds.Contains(t.LotId));
        }
        if (!string.IsNullOrWhiteSpace(q.SerialNumber))
        {
            var serialIds = SerialIdsMatching(q.SerialNumber.Trim());
            query = query.Where(t => serialIds.Contains(t.SerialId));
        }
        if (!string.IsNullOrWhiteSpace(q.RefEntity))
        {
            var code = q.RefEntity.Trim().ToUpperInvariant();
            var refEntityId = await lookups.TryGetIdAsync(LookupDomains.EntityType, code, ct)
                              ?? throw new ValidationException("refEntity", KardexRules.UnknownRefEntityMessage(code));
            query = query.Where(t => t.RefEntityLookupId == refEntityId);
        }
        if (q.RefId is int refId) query = query.Where(t => t.RefId == refId);
        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var s = q.Search.Trim();
            var bySku = db.Set<Product>().AsNoTracking()
                .Where(p => p.Sku.Contains(s) || p.Name.Contains(s) || (p.Barcode != null && p.Barcode.Contains(s)))
                .Select(p => p.ProductId);
            var byLot = LotIdsMatching(s);
            var bySerial = SerialIdsMatching(s);
            query = query.Where(t => bySku.Contains(t.ProductId) || byLot.Contains(t.LotId) || bySerial.Contains(t.SerialId)
                                     || (t.Notes != null && t.Notes.Contains(s)));
        }

        var total = await query.CountAsync(ct);
        var page = await query.OrderByDescending(t => t.CreatedAtUtc).ThenByDescending(t => t.InventoryTransactionId)
            .Skip(skip).Take(take).ToListAsync(ct);
        var items = await ToKardexRowsAsync(page, filter, ct);
        return new KardexPageDto(total, skip, take, items);
    }

    /// <summary>Movimientos por id (en el orden de los ids), sin filtro de ubicación. Resultado de ajustes y transferencias.</summary>
    public async Task<IReadOnlyList<KardexRowDto>> TransactionsByIdsAsync(IEnumerable<long> ids, CancellationToken ct)
    {
        var wanted = ids.Distinct().ToList();
        if (wanted.Count == 0) return Array.Empty<KardexRowDto>();
        var rows = await db.Set<InventoryTransaction>().AsNoTracking()
            .Where(t => wanted.Contains(t.InventoryTransactionId)).ToListAsync(ct);
        var ordered = wanted.Select(id => rows.FirstOrDefault(r => r.InventoryTransactionId == id)).Where(r => r is not null).Select(r => r!).ToList();
        return await ToKardexRowsAsync(ordered, KardexLocationFilter.None, ct);
    }

    /// <summary>
    /// Filas del Kárdex en el orden de entrada: producto, ubicaciones, lote, serie, tipo, motivo, origen legible y usuario
    /// resueltos por lotes de consultas (sin N+1). filter = perspectiva de SignedQuantity (None = sin filtro).
    /// </summary>
    public async Task<IReadOnlyList<KardexRowDto>> ToKardexRowsAsync(IReadOnlyList<InventoryTransaction> rows, KardexLocationFilter filter,
        CancellationToken ct)
    {
        if (rows.Count == 0) return Array.Empty<KardexRowDto>();
        var refs = await LoadKardexRefsAsync(rows, ct);
        var result = new List<KardexRowDto>(rows.Count);
        foreach (var t in rows)
        {
            var p = refs.Products.GetValueOrDefault(t.ProductId);
            var typeCode = refs.Codes.GetValueOrDefault(t.TxnTypeLookupId) ?? string.Empty;
            var fromWh = t.FromWarehouseId is int fw ? refs.Warehouses.GetValueOrDefault(fw)?.Code : null;
            var toWh = t.ToWarehouseId is int tw ? refs.Warehouses.GetValueOrDefault(tw)?.Code : null;
            var fromBin = t.FromBinId is int fb ? refs.Bins.GetValueOrDefault(fb)?.Code : null;
            var toBin = t.ToBinId is int tb ? refs.Bins.GetValueOrDefault(tb)?.Code : null;
            var refCode = t.RefEntityLookupId is int re ? refs.Codes.GetValueOrDefault(re) : null;
            string? refNumber = null;
            if (refCode is not null && t.RefId is int rid && refs.RefNumbers.TryGetValue(refCode, out var numbers))
                refNumber = numbers.GetValueOrDefault(rid);
            var reasonCode = t.ReasonLookupId is int rl ? refs.Codes.GetValueOrDefault(rl) : null;
            var reason = t.ReasonLookupId is int rl2 ? refs.Labels.GetValueOrDefault(rl2) : null;

            result.Add(new KardexRowDto(t.InventoryTransactionId, t.CreatedAtUtc, typeCode, KardexRules.TypeChip(typeCode),
                p?.PublicId ?? Guid.Empty, p?.Sku ?? string.Empty, p?.Name ?? string.Empty,
                t.Quantity,
                KardexRules.SignedQuantity(t.Quantity, typeCode, t.FromWarehouseId, t.FromBinId, t.ToWarehouseId, t.ToBinId, filter),
                fromWh, fromBin, toWh, toBin, KardexRules.Position(fromWh, fromBin, toWh, toBin),
                t.LotId is int lid ? refs.Lots.GetValueOrDefault(lid)?.Number : null,
                t.SerialId is int sid ? refs.Serials.GetValueOrDefault(sid) : null,
                refCode, t.RefId, KardexRules.RefLabel(refCode, t.RefId, refNumber),
                reasonCode, reason, t.Notes,
                t.CreatedBy, t.CreatedBy is int uid ? refs.Users.GetValueOrDefault(uid) : null));
        }
        return result;
    }

    /// <summary>Catálogos de una tanda de movimientos (lo reutiliza InventoryTransactionDataSource).</summary>
    internal async Task<KardexRefs> LoadKardexRefsAsync(IReadOnlyList<InventoryTransaction> rows, CancellationToken ct)
    {
        var products = await ProductInfoAsync(rows.Select(r => r.ProductId).Distinct().ToList(), ct);
        var warehouses = await WarehouseInfoAsync(rows.SelectMany(r => new[] { r.FromWarehouseId, r.ToWarehouseId })
            .Where(i => i.HasValue).Select(i => i!.Value), ct);
        var bins = await BinInfoAsync(rows.SelectMany(r => new[] { r.FromBinId, r.ToBinId }).Where(i => i.HasValue).Select(i => i!.Value), ct);
        var lots = await LotInfoAsync(rows.Where(r => r.LotId.HasValue).Select(r => r.LotId!.Value), ct);
        var serials = await SerialNumbersAsync(rows.Where(r => r.SerialId.HasValue).Select(r => r.SerialId!.Value), ct);

        // Códigos y etiquetas de catálogo (tipo, motivo, EntityType de la referencia) desde la caché de LookupCode.
        var codes = new Dictionary<int, string>();
        var labels = new Dictionary<int, string>();
        foreach (var id in rows.SelectMany(r => new int?[] { r.TxnTypeLookupId, r.ReasonLookupId, r.RefEntityLookupId })
                     .Where(i => i.HasValue).Select(i => i!.Value).Distinct())
        {
            var lc = await lookups.GetAsync(id, ct);
            if (lc is null) continue;
            codes[id] = lc.InternalCode;
            labels[id] = MultilingualText.Resolve(lc.LabelJson, tenant.Lang);
        }

        var refNumbers = await RefNumbersAsync(rows
            .Where(r => r.RefEntityLookupId.HasValue && r.RefId.HasValue && codes.ContainsKey(r.RefEntityLookupId!.Value))
            .Select(r => (codes[r.RefEntityLookupId!.Value], r.RefId!.Value)), ct);

        var userIds = rows.Where(r => r.CreatedBy.HasValue).Select(r => r.CreatedBy!.Value).Distinct().ToList();
        var users = userIds.Count == 0
            ? new Dictionary<int, string>()
            : await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id))
                .Select(u => new { u.Id, Name = u.FullName ?? u.Email ?? u.UserName ?? "" })
                .ToDictionaryAsync(u => u.Id, u => u.Name, ct);

        return new KardexRefs(products, warehouses, bins, lots, serials, codes, labels, refNumbers, users);
    }

    /// <summary>
    /// Números de documento por EntityType e id (RefLabel), una consulta por tipo. Todos los encabezados llevan filtro de
    /// tenant; la asignación de cross-dock (sin TenantId) se alcanza por su plan filtrado.
    /// </summary>
    internal async Task<Dictionary<string, Dictionary<int, string>>> RefNumbersAsync(IEnumerable<(string Code, int Id)> refs, CancellationToken ct)
    {
        var result = new Dictionary<string, Dictionary<int, string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in refs.GroupBy(r => r.Code.ToUpperInvariant()))
        {
            var ids = group.Select(g => g.Id).Distinct().ToList();
            Dictionary<int, string>? map = group.Key switch
            {
                EntityTypes.Receipt => await db.Set<ReceiptHeader>().AsNoTracking().Where(x => ids.Contains(x.ReceiptHeaderId))
                    .ToDictionaryAsync(x => x.ReceiptHeaderId, x => x.Number, ct),
                EntityTypes.PickBatch => await db.Set<PickBatch>().AsNoTracking().Where(x => ids.Contains(x.PickBatchId))
                    .ToDictionaryAsync(x => x.PickBatchId, x => x.Number, ct),
                EntityTypes.CycleCount => await db.Set<CycleCount>().AsNoTracking().Where(x => ids.Contains(x.CycleCountId))
                    .ToDictionaryAsync(x => x.CycleCountId, x => x.Number, ct),
                EntityTypes.PurchaseOrder => await db.Set<PurchaseOrder>().AsNoTracking().Where(x => ids.Contains(x.PurchaseOrderId))
                    .ToDictionaryAsync(x => x.PurchaseOrderId, x => x.Number, ct),
                EntityTypes.TransportOrder => await db.TransportOrders.AsNoTracking().Where(x => ids.Contains(x.TransportOrderId))
                    .ToDictionaryAsync(x => x.TransportOrderId, x => x.OrderNumber, ct),
                EntityTypes.CrossDockPlan => await db.Set<CrossDockPlan>().AsNoTracking().Where(x => ids.Contains(x.CrossDockPlanId))
                    .ToDictionaryAsync(x => x.CrossDockPlanId, x => x.Number, ct),
                EntityTypes.CrossDockAllocation => await (from a in db.Set<CrossDockAllocation>().AsNoTracking()
                                                          join pl in db.Set<CrossDockPlan>().AsNoTracking() on a.CrossDockPlanId equals pl.CrossDockPlanId
                                                          where ids.Contains(a.CrossDockAllocationId)
                                                          select new { a.CrossDockAllocationId, pl.Number })
                    .ToDictionaryAsync(x => x.CrossDockAllocationId, x => x.Number, ct),
                _ => null,
            };
            if (map is not null) result[group.Key] = map;
        }
        return result;
    }

    // ================================================================ filtros compartidos

    /// <summary>
    /// Subconsulta de ids de producto según el scope (dueño), los PublicId y las categorías (con sus subcategorías).
    /// NULL = sin filtro de producto (scope Any, sin productos ni categorías).
    /// </summary>
    internal async Task<IQueryable<int>?> FilteredProductIdsAsync(InventoryScope scope, Guid[]? productPublicIds, int[]? categoryIds, CancellationToken ct)
    {
        var any = false;
        var products = db.Set<Product>().AsNoTracking().AsQueryable();
        if (scope.OwnerClientId is int owner) { products = products.Where(p => p.ClientId == owner); any = true; }
        if (productPublicIds is { Length: > 0 })
        {
            var pubs = productPublicIds.Distinct().ToList();
            products = products.Where(p => pubs.Contains(p.PublicId));
            any = true;
        }
        if (categoryIds is { Length: > 0 })
        {
            var parents = await db.Set<ProductCategory>().AsNoTracking()
                .Select(c => new { c.ProductCategoryId, c.ParentId })
                .ToDictionaryAsync(c => c.ProductCategoryId, c => c.ParentId, ct);
            var wanted = KardexRules.WithDescendants(categoryIds, parents).Select(i => (int?)i).ToList();
            products = products.Where(p => wanted.Contains(p.ProductCategoryId));
            any = true;
        }
        return any ? products.Select(p => p.ProductId) : null;
    }

    /// <summary>Productos visibles para el scope (lecturas de trazabilidad): el dueño acota; Any = todo el tenant.</summary>
    internal IQueryable<Product> ScopedProducts(InventoryScope scope)
    {
        var q = db.Set<Product>().AsNoTracking().AsQueryable();
        if (scope.OwnerClientId is int owner) q = q.Where(p => p.ClientId == owner);
        return q;
    }

    private async Task<List<int>> WarehouseIdsAsync(IEnumerable<Guid> publicIds, CancellationToken ct)
    {
        var pubs = publicIds.Distinct().ToList();
        return await db.Set<Warehouse>().AsNoTracking().Where(w => pubs.Contains(w.PublicId)).Select(w => w.WarehouseId).ToListAsync(ct);
    }

    /// <summary>Lotes cuyo número contiene el término, alcanzados por su producto filtrado.</summary>
    private IQueryable<int?> LotIdsMatching(string term)
        => from l in db.Set<InventoryLot>().AsNoTracking()
           join p in db.Set<Product>().AsNoTracking() on l.ProductId equals p.ProductId
           where l.LotNumber.Contains(term)
           select (int?)l.LotId;

    /// <summary>Series cuyo número contiene el término, alcanzadas por su producto filtrado.</summary>
    private IQueryable<int?> SerialIdsMatching(string term)
        => from s in db.Set<InventorySerial>().AsNoTracking()
           join p in db.Set<Product>().AsNoTracking() on s.ProductId equals p.ProductId
           where s.SerialNumber.Contains(term)
           select (int?)s.SerialId;

    private static IEnumerable<string> SplitCodes(IEnumerable<string> values)
        => values.SelectMany(v => (v ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Select(v => v.ToUpperInvariant()).Distinct();

    // ================================================================ catálogos por lotes (sin N+1)

    internal async Task<Dictionary<int, ProductInfo>> ProductInfoAsync(IReadOnlyCollection<int> ids, CancellationToken ct)
    {
        if (ids.Count == 0) return new Dictionary<int, ProductInfo>();
        var list = ids.Distinct().ToList();
        return await db.Set<Product>().AsNoTracking().Where(p => list.Contains(p.ProductId))
            .Select(p => new ProductInfo(p.ProductId, p.PublicId, p.Sku, p.Name, p.ProductCategoryId, p.ClientId, p.PurchaseCost, p.SalePrice))
            .ToDictionaryAsync(p => p.ProductId, ct);
    }

    internal async Task<Dictionary<int, WarehouseInfo>> WarehouseInfoAsync(IEnumerable<int> ids, CancellationToken ct)
    {
        var list = ids.Distinct().ToList();
        if (list.Count == 0) return new Dictionary<int, WarehouseInfo>();
        return await db.Set<Warehouse>().AsNoTracking().Where(w => list.Contains(w.WarehouseId))
            .Select(w => new WarehouseInfo(w.WarehouseId, w.PublicId, w.Code))
            .ToDictionaryAsync(w => w.Id, ct);
    }

    /// <summary>Posiciones por id, unidas a su almacén filtrado (una posición de otro tenant no aparece).</summary>
    internal async Task<Dictionary<int, BinInfo>> BinInfoAsync(IEnumerable<int> ids, CancellationToken ct)
    {
        var list = ids.Distinct().ToList();
        if (list.Count == 0) return new Dictionary<int, BinInfo>();
        return await (from b in db.Set<WarehouseBin>().AsNoTracking()
                      join w in db.Set<Warehouse>().AsNoTracking() on b.WarehouseId equals w.WarehouseId
                      where list.Contains(b.WarehouseBinId)
                      select new BinInfo(b.WarehouseBinId, b.WarehouseId, b.WarehouseZoneId, b.Code))
            .ToDictionaryAsync(b => b.Id, ct);
    }

    /// <summary>Zonas por id, unidas a su almacén filtrado; el tipo se expone con su código de catálogo.</summary>
    internal async Task<Dictionary<int, ZoneInfo>> ZoneInfoAsync(IEnumerable<int> ids, CancellationToken ct)
    {
        var list = ids.Distinct().ToList();
        if (list.Count == 0) return new Dictionary<int, ZoneInfo>();
        var zones = await (from z in db.Set<WarehouseZone>().AsNoTracking()
                           join w in db.Set<Warehouse>().AsNoTracking() on z.WarehouseId equals w.WarehouseId
                           where list.Contains(z.WarehouseZoneId)
                           select new { z.WarehouseZoneId, z.Code, z.ZoneTypeLookupId }).ToListAsync(ct);
        var result = new Dictionary<int, ZoneInfo>();
        foreach (var z in zones)
        {
            string? typeCode = null;
            if (z.ZoneTypeLookupId is int tid) typeCode = (await lookups.GetAsync(tid, ct))?.InternalCode;
            result[z.WarehouseZoneId] = new ZoneInfo(z.WarehouseZoneId, z.Code, typeCode);
        }
        return result;
    }

    /// <summary>Lotes por id, unidos a su producto filtrado.</summary>
    internal async Task<Dictionary<int, LotInfo>> LotInfoAsync(IEnumerable<int> ids, CancellationToken ct)
    {
        var list = ids.Distinct().ToList();
        if (list.Count == 0) return new Dictionary<int, LotInfo>();
        return await (from l in db.Set<InventoryLot>().AsNoTracking()
                      join p in db.Set<Product>().AsNoTracking() on l.ProductId equals p.ProductId
                      where list.Contains(l.LotId)
                      select new LotInfo(l.LotId, l.LotNumber, l.ManufactureDate, l.ExpiryDate))
            .ToDictionaryAsync(l => l.Id, ct);
    }

    /// <summary>Números de serie por id, unidos a su producto filtrado.</summary>
    internal async Task<Dictionary<int, string>> SerialNumbersAsync(IEnumerable<int> ids, CancellationToken ct)
    {
        var list = ids.Distinct().ToList();
        if (list.Count == 0) return new Dictionary<int, string>();
        return await (from s in db.Set<InventorySerial>().AsNoTracking()
                      join p in db.Set<Product>().AsNoTracking() on s.ProductId equals p.ProductId
                      where list.Contains(s.SerialId)
                      select new { s.SerialId, s.SerialNumber })
            .ToDictionaryAsync(s => s.SerialId, s => s.SerialNumber, ct);
    }

    internal async Task<Dictionary<int, string>> CategoryNamesAsync(IEnumerable<int> ids, CancellationToken ct)
    {
        var list = ids.Distinct().ToList();
        if (list.Count == 0) return new Dictionary<int, string>();
        return await db.Set<ProductCategory>().AsNoTracking().Where(c => list.Contains(c.ProductCategoryId))
            .ToDictionaryAsync(c => c.ProductCategoryId, c => c.Name, ct);
    }

    internal async Task<Dictionary<int, string>> ClientNamesAsync(IEnumerable<int> ids, CancellationToken ct)
    {
        var list = ids.Distinct().ToList();
        if (list.Count == 0) return new Dictionary<int, string>();
        return await db.Set<Client>().AsNoTracking().Where(c => list.Contains(c.ClientId))
            .ToDictionaryAsync(c => c.ClientId, c => c.Name, ct);
    }

    /// <summary>Valor monetario (cantidad × costo o precio) con Round4; NULL sin costo o precio.</summary>
    internal static decimal? Value(decimal qty, decimal? unit) => unit is decimal u ? InventoryRules.Round4(qty * u) : null;

    // ================================================================ tipos internos

    internal sealed record ProductInfo(int ProductId, Guid PublicId, string Sku, string Name, int? CategoryId, int? ClientId,
        decimal? PurchaseCost, decimal? SalePrice);
    internal sealed record WarehouseInfo(int Id, Guid PublicId, string Code);
    internal sealed record BinInfo(int Id, int WarehouseId, int ZoneId, string Code);
    internal sealed record ZoneInfo(int Id, string Code, string? TypeCode);
    internal sealed record LotInfo(int Id, string Number, DateOnly? Manufacture, DateOnly? Expiry);

    internal sealed record BalanceRefs(Dictionary<int, ProductInfo> Products, Dictionary<int, WarehouseInfo> Warehouses,
        Dictionary<int, BinInfo> Bins, Dictionary<int, ZoneInfo> Zones, Dictionary<int, LotInfo> Lots,
        Dictionary<int, string> Categories, Dictionary<int, string> Owners);

    internal sealed record KardexRefs(Dictionary<int, ProductInfo> Products, Dictionary<int, WarehouseInfo> Warehouses,
        Dictionary<int, BinInfo> Bins, Dictionary<int, LotInfo> Lots, Dictionary<int, string> Serials,
        Dictionary<int, string> Codes, Dictionary<int, string> Labels, Dictionary<string, Dictionary<int, string>> RefNumbers,
        Dictionary<int, string> Users);
}
