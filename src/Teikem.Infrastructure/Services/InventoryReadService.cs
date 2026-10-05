using System.Linq.Expressions;
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
/// - Lote 14: las fechas del Kárdex son días LOCALES de la compañía (ITenantClock, hora de Puerto Rico); una sola consulta
///   (BuildKardexQueryAsync) sirve a la lista, al resumen y a la exportación; filtros de dueño, motivo, dirección, almacén de
///   origen y de destino y "solo manuales"; la fila trae dueño y categoría; detalle por id con su documento de origen.
/// </summary>
public sealed class InventoryReadService(TeikemDbContext db, ITenantContext tenant, ILookupCache lookups, ITenantClock clock)
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
        // solo productos activos (KPI "Unidades totales" de Productos e inventario: la cifra coincide con su filtro)
        if (q.ActiveProductsOnly) query = query.Where(b => db.Set<Product>().Any(p => p.ProductId == b.ProductId && p.IsActive));

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
    public async Task<IReadOnlyList<BalanceDto>> BalancesForKeysAsync(IEnumerable<BalanceKey> keys, CancellationToken ct)
    {
        var wanted = keys.Distinct().ToList();
        if (wanted.Count == 0) return Array.Empty<BalanceDto>();
        var productIds = wanted.Select(k => k.ProductId).Distinct().ToList();
        var warehouseIds = wanted.Select(k => k.WarehouseId).Distinct().ToList();
        var candidates = await db.Set<StockBalance>().AsNoTracking()
            .Where(b => productIds.Contains(b.ProductId) && warehouseIds.Contains(b.WarehouseId))
            .ToListAsync(ct);
        var byKey = candidates.GroupBy(b => new BalanceKey(b.ProductId, b.WarehouseId, b.WarehouseBinId, b.LotId))
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
        var (skip, take) = KardexRules.Page(q.Skip, q.Take, InventoryRules.MaxPageSize);
        var (query, filter) = await BuildKardexQueryAsync(q, scope, ct);

        var total = await query.CountAsync(ct);
        var page = await query.OrderByDescending(t => t.CreatedAtUtc).ThenByDescending(t => t.InventoryTransactionId)
            .Skip(skip).Take(take).ToListAsync(ct);
        var items = await ToKardexRowsAsync(page, filter, ct);
        return new KardexPageDto(total, skip, take, items);
    }

    /// <summary>
    /// Lote 14 (D13) — resumen del Kárdex con los MISMOS filtros de la lista (sin paginar): una consulta agrupada por tipo y
    /// lados (origen y destino) con Σ Quantity y número de movimientos; la perspectiva (entrada, salida o interna) la aplica
    /// KardexRules.Summarize con la regla de SignedQuantity. El agrupado evita subconsultas dentro de los agregados (SQL Server
    /// no las admite) y deja la regla en un solo lugar.
    /// </summary>
    public async Task<KardexSummaryDto> KardexSummaryAsync(KardexQuery q, InventoryScope scope, CancellationToken ct)
    {
        q ??= new KardexQuery();
        scope ??= InventoryScope.Any;
        var (query, filter) = await BuildKardexQueryAsync(q, scope, ct);
        var groups = await SummaryGroupsQuery(query).ToListAsync(ct);
        var codes = new Dictionary<int, string>();
        foreach (var id in groups.Select(g => g.TxnTypeLookupId).Distinct())
            codes[id] = (await lookups.GetAsync(id, ct))?.InternalCode ?? string.Empty;
        var s = KardexRules.Summarize(groups.Select(g => new KardexSummaryGroup(codes[g.TxnTypeLookupId], g.FromWarehouseId, g.FromBinId,
            g.ToWarehouseId, g.ToBinId, g.Quantity, g.Count)), filter);
        return new KardexSummaryDto(s.Movements, s.InCount, s.InQty, s.OutCount, s.OutQty, s.InternalCount);
    }

    /// <summary>
    /// Grupos del resumen: por tipo y lados (origen y destino) con Σ Quantity CON signo y número de movimientos. Público y
    /// estático para probar su traducción a SQL Server sin BD (ToQueryString).
    /// </summary>
    public static IQueryable<KardexGroupRow> SummaryGroupsQuery(IQueryable<InventoryTransaction> query)
        => query.GroupBy(t => new { t.TxnTypeLookupId, t.FromWarehouseId, t.FromBinId, t.ToWarehouseId, t.ToBinId })
            .Select(g => new KardexGroupRow
            {
                TxnTypeLookupId = g.Key.TxnTypeLookupId, FromWarehouseId = g.Key.FromWarehouseId, FromBinId = g.Key.FromBinId,
                ToWarehouseId = g.Key.ToWarehouseId, ToBinId = g.Key.ToBinId, Quantity = g.Sum(t => t.Quantity), Count = g.Count(),
            });

    /// <summary>Fila agrupada del resumen del Kárdex (member-init: traducible por EF).</summary>
    public sealed class KardexGroupRow
    {
        public int TxnTypeLookupId { get; init; }
        public int? FromWarehouseId { get; init; }
        public int? FromBinId { get; init; }
        public int? ToWarehouseId { get; init; }
        public int? ToBinId { get; init; }
        public decimal Quantity { get; init; }
        public int Count { get; init; }
    }

    /// <summary>
    /// Lote 14 — consulta del Kárdex compartida por la lista, el resumen y la exportación (el reporte usa la lista). Valida y
    /// arma TODOS los filtros y devuelve la consulta sin ordenar ni paginar y la perspectiva de SignedQuantity.
    /// - Fechas: días LOCALES de la compañía (ITenantClock, hora de Puerto Rico): desde inclusivo, hasta inclusivo por día.
    /// - Ubicación (almacenes y posiciones): el movimiento aparece si su origen o su destino cae en el filtro (almacén Y
    ///   posición cuando se indican ambos); la misma definición da la perspectiva.
    /// - Dirección IN/OUT con la perspectiva: sin filtro de ubicación IN = cantidad &gt; 0 salvo TRANSFER, OUT = cantidad &lt; 0;
    ///   con filtro, IN = entra al filtro sin salir de él y OUT = sale sin entrar (lo interno no es ni lo uno ni lo otro).
    /// - Dueño: clientes por PublicId (uno ajeno o inexistente → 404 'Cliente no encontrado.') y/o IncludeOwn (productos propios).
    /// - Motivos del catálogo AdjustmentReason (400 'Motivo de ajuste desconocido: 'X'.'), almacén de origen y de destino (cada
    ///   uno contra su lado) y ManualOnly (sin documento de referencia).
    /// </summary>
    public async Task<(IQueryable<InventoryTransaction> Query, KardexLocationFilter Filter)> BuildKardexQueryAsync(KardexQuery q,
        InventoryScope scope, CancellationToken ct)
    {
        if (KardexRules.IsRangeInverted(q.From, q.To)) throw new ValidationException("to", KardexRules.RangeInverted);
        var (direction, directionError) = KardexRules.NormalizeDirection(q.Direction);
        if (directionError is not null) throw new ValidationException("direction", directionError);
        var (fromUtc, toUtc) = clock.UtcRange(q.From, q.To);

        var query = db.Set<InventoryTransaction>().AsNoTracking().AsQueryable();

        var productIds = await FilteredProductIdsAsync(scope, q.ProductPublicIds, q.CategoryIds, ct, q.Brands, q.Name);
        if (productIds is not null) query = query.Where(t => productIds.Contains(t.ProductId));
        if (q.OwnerClientPublicIds is { Length: > 0 } || q.IncludeOwn)
        {
            var ownerProducts = await OwnerProductIdsAsync(q.OwnerClientPublicIds, q.IncludeOwn, ct);
            query = query.Where(t => ownerProducts.Contains(t.ProductId));
        }
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
        if (q.Reasons is { Length: > 0 })
        {
            var reasonIds = new List<int?>();
            foreach (var code in SplitCodes(q.Reasons))
            {
                var id = await lookups.TryGetIdAsync(LookupDomains.AdjustmentReason, code, ct)
                         ?? throw new ValidationException("reasons", AdjustmentRules.UnknownReason(code));
                reasonIds.Add(id);
            }
            query = query.Where(t => reasonIds.Contains(t.ReasonLookupId));
        }

        // Filtro de ubicación y perspectiva.
        List<int?>? whIds = null;
        List<int?>? binIds = null;
        if (q.WarehousePublicIds is { Length: > 0 })
            whIds = (await WarehouseIdsAsync(q.WarehousePublicIds, ct)).Select(i => (int?)i).ToList();
        if (q.BinIds is { Length: > 0 }) binIds = q.BinIds.Select(i => (int?)i).Distinct().ToList();
        Expression<Func<InventoryTransaction, bool>>? fromIn = null, toIn = null;
        if (whIds is not null && binIds is not null)
        {
            fromIn = t => whIds.Contains(t.FromWarehouseId) && binIds.Contains(t.FromBinId);
            toIn = t => whIds.Contains(t.ToWarehouseId) && binIds.Contains(t.ToBinId);
        }
        else if (whIds is not null)
        {
            fromIn = t => whIds.Contains(t.FromWarehouseId);
            toIn = t => whIds.Contains(t.ToWarehouseId);
        }
        else if (binIds is not null)
        {
            fromIn = t => binIds.Contains(t.FromBinId);
            toIn = t => binIds.Contains(t.ToBinId);
        }
        if (fromIn is not null && toIn is not null) query = query.Where(Expr.Or(fromIn, toIn));
        var filter = new KardexLocationFilter(
            whIds?.Where(i => i.HasValue).Select(i => i!.Value).ToHashSet(),
            binIds?.Where(i => i.HasValue).Select(i => i!.Value).ToHashSet());

        if (direction is not null)
        {
            if (fromIn is not null && toIn is not null)
                query = query.Where(direction == KardexRules.DirectionIn ? Expr.And(toIn, Expr.Not(fromIn)) : Expr.And(fromIn, Expr.Not(toIn)));
            else if (direction == KardexRules.DirectionIn)
            {
                var transferId = await lookups.TryGetIdAsync(LookupDomains.InventoryTxnType, InventoryTxnTypes.Transfer, ct) ?? -1;
                query = query.Where(t => t.Quantity > 0 && t.TxnTypeLookupId != transferId);
            }
            else query = query.Where(t => t.Quantity < 0);
        }

        if (q.FromWarehousePublicIds is { Length: > 0 })
        {
            var fromWh = (await WarehouseIdsAsync(q.FromWarehousePublicIds, ct)).Select(i => (int?)i).ToList();
            query = query.Where(t => fromWh.Contains(t.FromWarehouseId));
        }
        if (q.ToWarehousePublicIds is { Length: > 0 })
        {
            var toWh = (await WarehouseIdsAsync(q.ToWarehousePublicIds, ct)).Select(i => (int?)i).ToList();
            query = query.Where(t => toWh.Contains(t.ToWarehouseId));
        }
        if (q.ManualOnly) query = query.Where(t => t.RefEntityLookupId == null);

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
        return (query, filter);
    }

    /// <summary>
    /// Productos de los dueños pedidos (subconsulta): clientes por PublicId bajo el filtro de tenant (uno que no aparece → 404
    /// 'Cliente no encontrado.') y, con includeOwn, también los propios (Product.ClientId NULL).
    /// </summary>
    private async Task<IQueryable<int>> OwnerProductIdsAsync(Guid[]? ownerClientPublicIds, bool includeOwn, CancellationToken ct)
    {
        var ownerIds = new List<int?>();
        if (ownerClientPublicIds is { Length: > 0 })
        {
            var pubs = ownerClientPublicIds.Distinct().ToList();
            ownerIds = await db.Set<Client>().AsNoTracking().Where(c => pubs.Contains(c.PublicId)).Select(c => (int?)c.ClientId).ToListAsync(ct);
            if (ownerIds.Count != pubs.Count) throw new NotFoundException("Cliente");
        }
        var products = db.Set<Product>().AsNoTracking();
        return includeOwn
            ? products.Where(p => p.ClientId == null || ownerIds.Contains(p.ClientId)).Select(p => p.ProductId)
            : products.Where(p => ownerIds.Contains(p.ClientId)).Select(p => p.ProductId);
    }

    /// <summary>
    /// Lote 14 — dueños del inventario para el filtro Dueño: "Propio" (sin cliente; solo sin scope de dueño) y los clientes
    /// distintos que son dueños de algún producto del tenant, por nombre.
    /// </summary>
    public async Task<IReadOnlyList<InventoryOwnerDto>> OwnersAsync(InventoryScope scope, CancellationToken ct)
    {
        scope ??= InventoryScope.Any;
        var ownerIds = ScopedProducts(scope).Where(p => p.ClientId != null).Select(p => p.ClientId!.Value).Distinct();
        var clients = await db.Set<Client>().AsNoTracking().Where(c => ownerIds.Contains(c.ClientId))
            .OrderBy(c => c.Name).ThenBy(c => c.ClientId)
            .Select(c => new InventoryOwnerDto(c.PublicId, c.Name, false))
            .ToListAsync(ct);
        var result = new List<InventoryOwnerDto>(clients.Count + 1);
        if (scope.OwnerClientId is null) result.Add(new InventoryOwnerDto(null, KardexRules.OwnLabel, true));
        result.AddRange(clients);
        return result;
    }

    // ================================================================ detalle de un movimiento (Lote 14)

    /// <summary>Tope de movimientos relacionados del detalle.</summary>
    public const int MaxRelatedMovements = 200;

    /// <summary>
    /// Lote 14 — detalle de un movimiento para abrir su documento: la fila (con dueño y categoría), el vencimiento del lote, el
    /// documento de origen resuelto por tipo (con PublicId para abrirlo; la tarea de almacén trae su documento padre) y los
    /// movimientos relacionados: los de la misma referencia (IX_InvTxn_Ref) o, sin referencia, los del mismo asiento (el ledger
    /// usa un solo instante por asiento: mismo CreatedAtUtc, usuario y producto). Tope 200 (RelatedTruncated).
    /// Otro tenant o, con scope de dueño, un producto ajeno → 404 'Movimiento no encontrado.' sin oráculo.
    /// </summary>
    public async Task<KardexDetailDto> TransactionDetailAsync(long id, InventoryScope scope, CancellationToken ct)
    {
        scope ??= InventoryScope.Any;
        var scopedProducts = ScopedProducts(scope).Select(p => p.ProductId);
        var txn = await db.Set<InventoryTransaction>().AsNoTracking()
                      .Where(t => t.InventoryTransactionId == id && scopedProducts.Contains(t.ProductId))
                      .FirstOrDefaultAsync(ct)
                  ?? throw new NotFoundException("Movimiento");
        var row = (await ToKardexRowsAsync(new[] { txn }, KardexLocationFilter.None, ct))[0];
        DateOnly? expiry = txn.LotId is int lid ? (await LotInfoAsync(new[] { lid }, ct)).GetValueOrDefault(lid)?.Expiry : null;
        var document = row.RefEntityCode is string refCode && txn.RefId is int refId ? await DocumentAsync(refCode, refId, true, ct) : null;

        var related = db.Set<InventoryTransaction>().AsNoTracking().Where(t => scopedProducts.Contains(t.ProductId));
        if (txn.RefEntityLookupId is int re && txn.RefId is int rid)
            related = related.Where(t => t.RefEntityLookupId == re && t.RefId == rid);
        else
        {
            var (at, by, pid) = (txn.CreatedAtUtc, txn.CreatedBy, txn.ProductId);
            related = related.Where(t => t.RefEntityLookupId == null && t.CreatedAtUtc == at && t.CreatedBy == by && t.ProductId == pid);
        }
        var relatedRows = await related.OrderBy(t => t.CreatedAtUtc).ThenBy(t => t.InventoryTransactionId)
            .Take(MaxRelatedMovements + 1).ToListAsync(ct);
        var truncated = relatedRows.Count > MaxRelatedMovements;
        if (truncated) relatedRows = relatedRows.Take(MaxRelatedMovements).ToList();
        var relatedDtos = await ToKardexRowsAsync(relatedRows, KardexLocationFilter.None, ct);
        return new KardexDetailDto(row, row.OwnerName, row.CategoryName, expiry, document, relatedDtos, truncated);
    }

    /// <summary>
    /// Documento de origen por EntityType: RECEIPT (número, estatus, fecha de confirmación o de alta, proveedor de la orden de
    /// compra o cliente del aviso, referencia), PICK_BATCH (número, estatus, fecha, orden y su cliente), CYCLE_COUNT (número,
    /// estatus; se abre por id), PURCHASE_ORDER (número, estatus, fecha, proveedor), TRANSPORT_ORDER, WAREHOUSE_TASK (tipo de
    /// tarea y su documento padre), PRODUCT (reabasto: el producto), CROSSDOCK_ALLOCATION y CROSSDOCK_PLAN (plan XD-#####).
    /// Todos los encabezados llevan filtro de tenant; un documento que no aparece se devuelve solo con su etiqueta legible.
    /// </summary>
    private async Task<KardexDocumentDto> DocumentAsync(string code, int id, bool withParent, CancellationToken ct)
    {
        var entity = code.ToUpperInvariant();
        var label = await EntityLabelAsync(entity, ct);
        KardexDocumentDto Missing() => new(entity, label, id, null, KardexRules.RefLabel(entity, id, null, tenant.Lang), null, null, null, null);

        switch (entity)
        {
            case EntityTypes.Receipt:
            {
                var r = await db.Set<ReceiptHeader>().AsNoTracking().Where(x => x.ReceiptHeaderId == id)
                    .Select(x => new { x.PublicId, x.Number, x.StatusCodeId, x.ReceivedAtUtc, x.CreatedAtUtc, x.AsnId, x.Reference })
                    .FirstOrDefaultAsync(ct);
                if (r is null) return Missing();
                string? party = null;
                var reference = r.Reference;
                if (r.AsnId is int asnId)
                {
                    var asn = await db.Set<Asn>().AsNoTracking().Where(a => a.AsnId == asnId)
                        .Select(a => new { a.ClientId, a.PurchaseOrderId, a.Reference }).FirstOrDefaultAsync(ct);
                    if (asn?.PurchaseOrderId is int poId)
                        party = await (from po in db.Set<PurchaseOrder>().AsNoTracking()
                                       join s in db.Set<Supplier>().AsNoTracking() on po.SupplierId equals s.SupplierId
                                       where po.PurchaseOrderId == poId
                                       select s.Name).FirstOrDefaultAsync(ct);
                    else if (asn?.ClientId is int cid) party = (await ClientNamesAsync(new[] { cid }, ct)).GetValueOrDefault(cid);
                    reference ??= asn?.Reference;
                }
                var (sc, sl) = await StatusOfAsync(r.StatusCodeId, ct);
                return new KardexDocumentDto(entity, label, id, r.PublicId, r.Number, sc, sl, r.ReceivedAtUtc ?? r.CreatedAtUtc, party, reference);
            }
            case EntityTypes.PickBatch:
            {
                var b = await db.Set<PickBatch>().AsNoTracking().Where(x => x.PickBatchId == id)
                    .Select(x => new { x.PublicId, x.Number, x.StatusCodeId, x.CollectedAtUtc, x.TransportOrderId, x.ClientInvoiceNumber })
                    .FirstOrDefaultAsync(ct);
                if (b is null) return Missing();
                string? party = null, orderNumber = null;
                if (b.TransportOrderId is int oid)
                {
                    var o = await db.TransportOrders.AsNoTracking().Where(x => x.TransportOrderId == oid)
                        .Select(x => new { x.OrderNumber, x.ClientId }).FirstOrDefaultAsync(ct);
                    if (o is not null)
                    {
                        orderNumber = o.OrderNumber;
                        party = (await ClientNamesAsync(new[] { o.ClientId }, ct)).GetValueOrDefault(o.ClientId);
                    }
                }
                var (sc, sl) = await StatusOfAsync(b.StatusCodeId, ct);
                return new KardexDocumentDto(entity, label, id, b.PublicId, b.Number, sc, sl, b.CollectedAtUtc, party, orderNumber ?? b.ClientInvoiceNumber);
            }
            case EntityTypes.CycleCount:
            {
                var c = await db.Set<CycleCount>().AsNoTracking().Where(x => x.CycleCountId == id)
                    .Select(x => new { x.Number, x.StatusCodeId, x.ReconciledAtUtc, x.CreatedAtUtc, x.WarehouseId }).FirstOrDefaultAsync(ct);
                if (c is null) return Missing();
                var (sc, sl) = await StatusOfAsync(c.StatusCodeId, ct);
                var wh = (await WarehouseInfoAsync(new[] { c.WarehouseId }, ct)).GetValueOrDefault(c.WarehouseId)?.Code;
                return new KardexDocumentDto(entity, label, id, null, c.Number, sc, sl, c.ReconciledAtUtc ?? c.CreatedAtUtc, null, wh);
            }
            case EntityTypes.PurchaseOrder:
            {
                var po = await (from x in db.Set<PurchaseOrder>().AsNoTracking()
                                join s in db.Set<Supplier>().AsNoTracking() on x.SupplierId equals s.SupplierId
                                where x.PurchaseOrderId == id
                                select new { x.PublicId, x.Number, x.StatusCodeId, x.OrderDate, SupplierName = s.Name })
                    .FirstOrDefaultAsync(ct);
                if (po is null) return Missing();
                var (sc, sl) = await StatusOfAsync(po.StatusCodeId, ct);
                return new KardexDocumentDto(entity, label, id, po.PublicId, po.Number, sc, sl,
                    po.OrderDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), po.SupplierName);
            }
            case EntityTypes.TransportOrder:
            {
                var o = await db.TransportOrders.AsNoTracking().Where(x => x.TransportOrderId == id)
                    .Select(x => new { x.PublicId, x.OrderNumber, x.StatusCodeId, x.CreatedAtUtc, x.ClientId }).FirstOrDefaultAsync(ct);
                if (o is null) return Missing();
                var (sc, sl) = await StatusOfAsync(o.StatusCodeId, ct);
                var party = (await ClientNamesAsync(new[] { o.ClientId }, ct)).GetValueOrDefault(o.ClientId);
                return new KardexDocumentDto(entity, label, id, o.PublicId, o.OrderNumber, sc, sl, o.CreatedAtUtc, party);
            }
            case EntityTypes.WarehouseTask:
            {
                var t = await db.Set<WarehouseTask>().AsNoTracking().Where(x => x.WarehouseTaskId == id)
                    .Select(x => new { x.TaskTypeLookupId, x.StatusCodeId, x.CreatedAtUtc, x.CompletedAtUtc, x.RefEntityLookupId, x.RefId })
                    .FirstOrDefaultAsync(ct);
                if (t is null) return Missing();
                var type = await lookups.GetAsync(t.TaskTypeLookupId, ct);
                var typeLabel = type is null ? null : MultilingualText.Resolve(type.LabelJson, tenant.Lang);
                var (sc, sl) = await StatusOfAsync(t.StatusCodeId, ct);
                KardexDocumentDto? parent = null;
                if (withParent && t.RefEntityLookupId is int pre && t.RefId is int prid && await lookups.GetAsync(pre, ct) is { } parentType)
                    parent = await DocumentAsync(parentType.InternalCode, prid, false, ct);
                return new KardexDocumentDto(entity, label, id, null, KardexRules.RefLabel(entity, id, null, tenant.Lang), sc, sl,
                    t.CompletedAtUtc ?? t.CreatedAtUtc, null, typeLabel, parent);
            }
            case EntityTypes.Rental:
            {
                // Lote 27 (Rentas): número, estatus, fecha de despacho (o de alta), cliente y número de contrato.
                var r = await db.Set<Rental>().AsNoTracking().Where(x => x.RentalId == id)
                    .Select(x => new { x.PublicId, x.Number, x.StatusCodeId, x.DispatchedAtUtc, x.CreatedAtUtc, x.ClientId, x.ContractNumber })
                    .FirstOrDefaultAsync(ct);
                if (r is null) return Missing();
                var (sc, sl) = await StatusOfAsync(r.StatusCodeId, ct);
                var party = (await ClientNamesAsync(new[] { r.ClientId }, ct)).GetValueOrDefault(r.ClientId);
                return new KardexDocumentDto(entity, label, id, r.PublicId, r.Number, sc, sl, r.DispatchedAtUtc ?? r.CreatedAtUtc, party, r.ContractNumber);
            }
            case EntityTypes.Product:
            {
                var p = await db.Set<Product>().AsNoTracking().Where(x => x.ProductId == id)
                    .Select(x => new { x.PublicId, x.Sku, x.Name }).FirstOrDefaultAsync(ct);
                if (p is null) return Missing();
                return new KardexDocumentDto(entity, label, id, p.PublicId, p.Sku, null, null, null, p.Name);
            }
            case EntityTypes.CrossDockAllocation:
            {
                // La asignación (sin TenantId) se alcanza por su plan filtrado.
                var a = await (from x in db.Set<CrossDockAllocation>().AsNoTracking()
                               join pl in db.Set<CrossDockPlan>().AsNoTracking() on x.CrossDockPlanId equals pl.CrossDockPlanId
                               where x.CrossDockAllocationId == id
                               select new { pl.Number, x.StatusCodeId, x.CreatedAtUtc, x.TransportOrderId }).FirstOrDefaultAsync(ct);
                if (a is null) return Missing();
                var (sc, sl) = await StatusOfAsync(a.StatusCodeId, ct);
                var o = await db.TransportOrders.AsNoTracking().Where(x => x.TransportOrderId == a.TransportOrderId)
                    .Select(x => new { x.OrderNumber, x.ClientId }).FirstOrDefaultAsync(ct);
                var party = o is null ? null : (await ClientNamesAsync(new[] { o.ClientId }, ct)).GetValueOrDefault(o.ClientId);
                return new KardexDocumentDto(entity, label, id, null, a.Number, sc, sl, a.CreatedAtUtc, party, o?.OrderNumber);
            }
            case EntityTypes.CrossDockPlan:
            {
                var pl = await db.Set<CrossDockPlan>().AsNoTracking().Where(x => x.CrossDockPlanId == id)
                    .Select(x => new { x.Number, x.StatusCodeId, x.CreatedAtUtc, x.CompletedAtUtc }).FirstOrDefaultAsync(ct);
                if (pl is null) return Missing();
                var (sc, sl) = await StatusOfAsync(pl.StatusCodeId, ct);
                return new KardexDocumentDto(entity, label, id, null, pl.Number, sc, sl, pl.CompletedAtUtc ?? pl.CreatedAtUtc, null);
            }
            default:
                return Missing();
        }
    }

    /// <summary>Etiqueta del EntityType en el idioma del usuario (respaldo: el código).</summary>
    private async Task<string> EntityLabelAsync(string code, CancellationToken ct)
    {
        var id = await lookups.TryGetIdAsync(LookupDomains.EntityType, code, ct);
        var lc = id is int i ? await lookups.GetAsync(i, ct) : null;
        return lc is null ? code : MultilingualText.Resolve(lc.LabelJson, tenant.Lang);
    }

    /// <summary>Código y etiqueta del estatus (con la etiqueta propia de la compañía si la personalizó).</summary>
    private async Task<(string? Code, string? Label)> StatusOfAsync(int statusCodeId, CancellationToken ct)
    {
        var st = await db.StatusCodes.AsNoTracking().FirstOrDefaultAsync(s => s.StatusCodeId == statusCodeId, ct);
        if (st is null) return (null, null);
        var ov = await db.StatusCodeOverrides.AsNoTracking().FirstOrDefaultAsync(o => o.StatusCodeId == statusCodeId, ct);
        return (st.InternalCode, MultilingualText.Resolve(MultilingualText.Merge(st.LabelJson, ov?.CustomLabelJson), tenant.Lang));
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

            // Tipo: etiqueta del catálogo InventoryTxnType en el idioma del usuario (maestro L582); respaldo TypeChip.
            var typeLabel = refs.Labels.GetValueOrDefault(t.TxnTypeLookupId);
            if (string.IsNullOrEmpty(typeLabel)) typeLabel = KardexRules.TypeChip(typeCode, tenant.Lang);

            result.Add(new KardexRowDto(t.InventoryTransactionId, t.CreatedAtUtc, typeCode, typeLabel,
                p?.PublicId ?? Guid.Empty, p?.Sku ?? string.Empty, p?.Name ?? string.Empty,
                t.Quantity,
                KardexRules.SignedQuantity(t.Quantity, typeCode, t.FromWarehouseId, t.FromBinId, t.ToWarehouseId, t.ToBinId, filter),
                fromWh, fromBin, toWh, toBin, KardexRules.Position(fromWh, fromBin, toWh, toBin),
                t.LotId is int lid ? refs.Lots.GetValueOrDefault(lid)?.Number : null,
                t.SerialId is int sid ? refs.Serials.GetValueOrDefault(sid) : null,
                refCode, t.RefId, KardexRules.RefLabel(refCode, t.RefId, refNumber, tenant.Lang),
                reasonCode, reason, t.Notes,
                t.CreatedBy, t.CreatedBy is int uid ? refs.Users.GetValueOrDefault(uid) : null,
                // Lote 14: dueño ('Propio' sin cliente) y categoría del producto.
                p is null ? null : KardexRules.OwnerLabel(p.ClientId is int oid ? refs.Owners.GetValueOrDefault(oid) : null),
                p?.CategoryId is int cid ? refs.Categories.GetValueOrDefault(cid) : null));
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

        // Lote 14: dueño y categoría de la fila.
        var categories = await CategoryNamesAsync(products.Values.Where(p => p.CategoryId.HasValue).Select(p => p.CategoryId!.Value), ct);
        var owners = await ClientNamesAsync(products.Values.Where(p => p.ClientId.HasValue).Select(p => p.ClientId!.Value), ct);

        return new KardexRefs(products, warehouses, bins, lots, serials, codes, labels, refNumbers, users, categories, owners);
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
                // Lote 27 (Rentas): renta y devolución de renta por su número; el proceso (RENTAL_PROCESS) no tiene número
                // (KardexRules lo muestra como 'Proceso #id').
                EntityTypes.Rental => await db.Set<Rental>().AsNoTracking().Where(x => ids.Contains(x.RentalId))
                    .ToDictionaryAsync(x => x.RentalId, x => x.Number, ct),
                EntityTypes.RentalReturn => await db.Set<RentalReturn>().AsNoTracking().Where(x => ids.Contains(x.RentalReturnId))
                    .ToDictionaryAsync(x => x.RentalReturnId, x => x.Number, ct),
                _ => null,
            };
            if (map is not null) result[group.Key] = map;
        }
        return result;
    }

    // ================================================================ filtros compartidos

    /// <summary>
    /// Subconsulta de ids de producto según el scope (dueño), los PublicId y las categorías (con sus subcategorías). Lote 12:
    /// brands (marca igual a alguna) y name (el nombre contiene), ambos sin distinguir mayúsculas.
    /// NULL = sin filtro de producto (scope Any, sin productos, categorías, marcas ni nombre).
    /// </summary>
    internal async Task<IQueryable<int>?> FilteredProductIdsAsync(InventoryScope scope, Guid[]? productPublicIds, int[]? categoryIds, CancellationToken ct,
        string[]? brands = null, string? name = null)
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
        if (ProductRules.NormalizeTextFilter(brands) is { } wantedBrands)
        {
            products = products.Where(p => p.Brand != null && wantedBrands.Contains(p.Brand.ToLower()));
            any = true;
        }
        if (!string.IsNullOrWhiteSpace(name))
        {
            var n = name.Trim().ToLowerInvariant();
            products = products.Where(p => p.Name.ToLower().Contains(n));
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
        Dictionary<int, string> Users, Dictionary<int, string> Categories, Dictionary<int, string> Owners);

    /// <summary>Composición de predicados (Lote 14: dirección con la perspectiva del filtro de ubicación), traducible por EF.</summary>
    private static class Expr
    {
        public static Expression<Func<T, bool>> And<T>(Expression<Func<T, bool>> a, Expression<Func<T, bool>> b)
            => Expression.Lambda<Func<T, bool>>(Expression.AndAlso(a.Body, Rebind(b, a.Parameters[0])), a.Parameters);

        public static Expression<Func<T, bool>> Or<T>(Expression<Func<T, bool>> a, Expression<Func<T, bool>> b)
            => Expression.Lambda<Func<T, bool>>(Expression.OrElse(a.Body, Rebind(b, a.Parameters[0])), a.Parameters);

        public static Expression<Func<T, bool>> Not<T>(Expression<Func<T, bool>> a)
            => Expression.Lambda<Func<T, bool>>(Expression.Not(a.Body), a.Parameters);

        private static Expression Rebind<T>(Expression<Func<T, bool>> e, ParameterExpression to)
            => new Replace(e.Parameters[0], to).Visit(e.Body);

        private sealed class Replace(ParameterExpression from, ParameterExpression to) : ExpressionVisitor
        {
            protected override Expression VisitParameter(ParameterExpression node) => node == from ? to : base.VisitParameter(node);
        }
    }
}
