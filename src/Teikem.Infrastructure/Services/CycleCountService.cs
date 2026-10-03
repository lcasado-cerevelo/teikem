using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Catalogs;
using Teikem.Domain.Clients;
using Teikem.Domain.Common;
using Teikem.Domain.Constants;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Clients;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Orders;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Wms;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 6 (P6) — Conteo cíclico en modo informado (R16, R33; bitácora L542: 'al confirmar, cada línea con diferencia genera
/// un ajuste'; D22).
/// - Alta: CC-#####, nace OPEN con una línea por saldo en mano (posición, producto, lote) del almacén según los filtros; la
///   foto del sistema queda en SystemQty. Crea una tarea COUNT (Ref CYCLE_COUNT) en la cola.
/// - Ficha en modo informado: foto, series esperadas (las que hoy están en la posición), saldo actual y marca IsStale.
/// - Captura de cantidades (o de series en productos con serie), líneas agregadas a mano (lo encontrado), terminar
///   (OPEN → COUNTED) y 'Refrescar' (opcional: re-fotografía las líneas viejas y borra su captura para recontar).
/// - Reconciliar (warehouse.count, D22): con el encabezado bloqueado, bloquea los saldos de los productos del conteo y ajusta
///   CONTADO − SALDO ACTUAL (no contra la foto); guarda ReconciledSystemQty y marca SystemQtyChanged si el saldo se movió desde
///   la foto. Si lo contado es menor que lo reservado → 409 sin escribir nada. Los ajustes van al ledger como ADJUSTMENT
///   COUNT_VARIANCE (Ref CYCLE_COUNT + id) y se enlazan en AdjustmentTxnId; en serie: bajas, altas y TRANSFER contra la
///   ubicación actual. El conteo pasa a RECONCILED (escalonado) y su tarea COUNT a DONE. Su contenido queda congelado.
///
/// Orden de bloqueo (InventoryQueries): CycleCount → WarehouseTask → saldos (rango por producto con HOLDLOCK, en ProductId
/// ascendente: respeta el orden por clave (ProductId, …) y cubre también los saldos por nacer) → series (las bloquea el
/// ledger; con los rangos de producto tomados ningún otro movimiento de esas series puede intercalarse) → NumberSequence.
/// La única vía de escritura del inventario es InventoryLedger.PostAsync. El TenantId sale del principal; el conteo (sin
/// PublicId) se expone por id y sus líneas (sin TenantId) SIEMPRE dentro de su conteo filtrado (otra línea → 404).
///
/// Lote 14 (D2, D3, D4, D7, D8):
/// - Estatus: Pendiente (OPEN) → Contado (COUNTED, solo al terminar a ciegas) → Concordancia (RECONCILED, sin ajustes) o
///   Diferencia (RECONCILED_VARIANCE, alguna línea asentó un movimiento). "Confirmar conteo y ajustar" (reconciliar) pasa en UN
///   paso desde Pendiente, sin pasar por Contado.
/// - "Conteo de lo cambiado": un conteo por posición con movimientos en la ventana (sin los de un conteo), con todo lo que hay
///   en ella y las claves que quedaron en 0; salta posiciones inactivas o con un conteo abierto; tope 200 por generación; una
///   sola transacción; guarda origen CHANGES y ventana. "Hoy" = día local de la compañía (ITenantClock).
/// - Lista paginada con total, origen, posición/zona de un conteo de una posición y la tarea COUNT con su asignado.
/// </summary>
public sealed class CycleCountService(
    TeikemDbContext db,
    ITenantContext tenant,
    ILookupCache lookups,
    StatusService statuses,
    INumberSequenceService numbers,
    InventoryLedger ledger,
    WarehouseTaskWriter taskWriter,
    ITenantClock clock)
{
    public const string NumberPattern = "CC-#####";
    public const string NumberTakenMessage = "Ya existe un conteo con ese número; intente de nuevo.";
    public const string CountWhat = "Conteo";
    public const string WarehouseRequired = "Indique el almacén: la compañía tiene más de uno.";
    public const string NoActiveWarehouse = "La compañía no tiene almacenes activos.";
    public const string WarehouseInactive = "El almacén está inactivo.";
    public static string BinInactive(string code) => $"La posición {code} está inactiva.";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private sealed record LineKey(int ProductId, int BinId, int? LotId);

    // ================================================================ lista

    /// <summary>
    /// Lote 8A — lista para quien consulta: con blind = true (sin warehouse.count) VarianceLines y NetVariance llegan null
    /// (conteo a ciegas; ver Blind).
    /// </summary>
    public async Task<IReadOnlyList<CycleCountDto>> ListAsync(CycleCountQuery? q, bool blind, CancellationToken ct)
    {
        var list = await ListAsync(q, ct);
        return blind ? list.Select(c => c with { VarianceLines = null, NetVariance = null }).ToList() : list;
    }

    /// <summary>
    /// Conteos activos (los 200 más recientes) con filtros por almacén, estatus, fecha de alta (días locales de la compañía),
    /// posición, zona, origen, producto, categoría y búsqueda.
    /// </summary>
    public async Task<IReadOnlyList<CycleCountDto>> ListAsync(CycleCountQuery? q, CancellationToken ct)
    {
        var query = await BuildListQueryAsync(q ?? new CycleCountQuery(), ct);
        var page = await query.OrderByDescending(c => c.CreatedAtUtc).ThenByDescending(c => c.CycleCountId)
            .Take(CycleCountRules.MaxListRows).ToListAsync(ct);
        return await MapHeadersAsync(page, ct);
    }

    /// <summary>
    /// Lote 14 (hallazgo 14) — página de conteos con el total: mismos filtros que la lista, más recientes primero; Take 1..200
    /// (por defecto 50). Con blind = true (sin warehouse.count) VarianceLines y NetVariance llegan null.
    /// </summary>
    public async Task<CycleCountPageDto> ListPageAsync(CycleCountQuery? q, bool blind, CancellationToken ct)
    {
        q ??= new CycleCountQuery();
        var skip = Math.Max(0, q.Skip);
        var take = Math.Clamp(q.Take <= 0 ? 50 : q.Take, 1, CycleCountRules.MaxListRows);
        var query = await BuildListQueryAsync(q, ct);
        var total = await query.CountAsync(ct);
        var page = total == 0 ? new List<CycleCount>() : await query.OrderByDescending(c => c.CreatedAtUtc).ThenByDescending(c => c.CycleCountId)
            .Skip(skip).Take(take).ToListAsync(ct);
        var items = await MapHeadersAsync(page, ct);
        if (blind) items = items.Select(c => c with { VarianceLines = null, NetVariance = null }).ToList();
        return new CycleCountPageDto(total, skip, take, items);
    }

    /// <summary>Consulta filtrada de conteos activos (sin orden ni página).</summary>
    private async Task<IQueryable<CycleCount>> BuildListQueryAsync(CycleCountQuery q, CancellationToken ct)
    {
        var query = db.Set<CycleCount>().AsNoTracking().Where(c => c.IsActive);

        if (q.WarehousePublicIds is { Length: > 0 })
        {
            var whs = q.WarehousePublicIds.Distinct().ToList();
            query = query.Where(c => db.Set<Warehouse>().Any(w => w.WarehouseId == c.WarehouseId && whs.Contains(w.PublicId)));
        }
        if (q.Status is { Length: > 0 })
        {
            var codes = Codes(q.Status);
            var ids = await db.StatusCodes.AsNoTracking()
                .Where(s => s.Entity == StatusDomains.CycleCountStatus && codes.Contains(s.InternalCode))
                .Select(s => s.StatusCodeId).ToListAsync(ct);
            query = query.Where(c => ids.Contains(c.StatusCodeId));
        }
        // Lote 14: días LOCALES de la compañía (hora de Puerto Rico), 'hasta' inclusive.
        var (fromUtc, toUtc) = clock.UtcRange(q.From, q.To);
        if (fromUtc is DateTime f) query = query.Where(c => c.CreatedAtUtc >= f);
        if (toUtc is DateTime t) query = query.Where(c => c.CreatedAtUtc < t);
        if (q.Origins is { Length: > 0 })
        {
            var origins = Codes(q.Origins);
            var originIds = new List<int>();
            foreach (var code in origins)
                if (await lookups.TryGetIdAsync(LookupDomains.CycleCountOrigin, code, ct) is int oid) originIds.Add(oid);
            // Los conteos sin origen (anteriores al seed del Lote 14) son de selección.
            var includeNull = origins.Contains(CycleCountOrigins.Manual);
            query = query.Where(c => (c.OriginLookupId != null && originIds.Contains(c.OriginLookupId.Value)) || (includeNull && c.OriginLookupId == null));
        }

        // Filtros por línea: el conteo aparece si alguna de sus líneas cumple TODOS los filtros de línea.
        var lineFilter = db.Set<CycleCountLine>().AsNoTracking();
        var anyLineFilter = false;
        if (q.BinIds is { Length: > 0 })
        {
            var binIds = q.BinIds.Distinct().ToList();
            lineFilter = lineFilter.Where(l => binIds.Contains(l.WarehouseBinId));
            anyLineFilter = true;
        }
        if (q.ZoneIds is { Length: > 0 })
        {
            var zoneIds = q.ZoneIds.Distinct().ToList();
            lineFilter = lineFilter.Where(l => db.Set<WarehouseBin>().Any(b => b.WarehouseBinId == l.WarehouseBinId && zoneIds.Contains(b.WarehouseZoneId)));
            anyLineFilter = true;
        }
        if (q.ProductPublicIds is { Length: > 0 })
        {
            var pubs = q.ProductPublicIds.Distinct().ToList();
            var pids = await db.Set<Product>().AsNoTracking().Where(p => pubs.Contains(p.PublicId)).Select(p => p.ProductId).ToListAsync(ct);
            lineFilter = lineFilter.Where(l => pids.Contains(l.ProductId));
            anyLineFilter = true;
        }
        if (q.CategoryIds is { Length: > 0 })
        {
            var cats = (await ExpandCategoriesAsync(q.CategoryIds, ct)).ToList();
            lineFilter = lineFilter.Where(l => db.Set<Product>().Any(p => p.ProductId == l.ProductId && p.ProductCategoryId != null && cats.Contains(p.ProductCategoryId.Value)));
            anyLineFilter = true;
        }
        if (anyLineFilter) query = query.Where(c => lineFilter.Any(l => l.CycleCountId == c.CycleCountId));

        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var s = q.Search.Trim();
            query = query.Where(c => c.Number.Contains(s)
                || db.Set<CycleCountLine>().Any(l => l.CycleCountId == c.CycleCountId
                    && db.Set<Product>().Any(p => p.ProductId == l.ProductId && (p.Sku.Contains(s) || p.Name.Contains(s)))));
        }
        return query;
    }

    /// <summary>
    /// Encabezados de una página de conteos (en su orden): resumen de líneas en SQL, almacén, estatus (con la etiqueta del
    /// tenant) y, desde el Lote 14, posiciones (código y zona si es una sola), origen con su ventana y la tarea COUNT (la
    /// abierta; si no hay, la última) con su asignado.
    /// </summary>
    private async Task<List<CycleCountDto>> MapHeadersAsync(IReadOnlyList<CycleCount> page, CancellationToken ct)
    {
        if (page.Count == 0) return new List<CycleCountDto>();

        var countIds = page.Select(c => c.CycleCountId).ToList();
        var summaries = await db.Set<CycleCountLine>().AsNoTracking()
            .Where(l => countIds.Contains(l.CycleCountId))
            .GroupBy(l => l.CycleCountId)
            .Select(g => new
            {
                CycleCountId = g.Key,
                Lines = g.Count(),
                Counted = g.Count(l => l.CountedQty != null),
                WithVariance = g.Count(l => l.CountedQty != null && l.CountedQty != l.SystemQty),
                Net = g.Sum(l => l.CountedQty != null ? l.CountedQty.Value - l.SystemQty : 0m),
                Corrected = g.Count(l => l.CorrectedAtUtc != null),
            })
            .ToDictionaryAsync(x => x.CycleCountId, ct);
        var warehouseIds = page.Select(c => c.WarehouseId).Distinct().ToList();
        var warehouses = await db.Set<Warehouse>().AsNoTracking().Where(w => warehouseIds.Contains(w.WarehouseId))
            .ToDictionaryAsync(w => w.WarehouseId, ct);
        var statusMap = await StatusMapAsync(StatusDomains.CycleCountStatus, ct);

        // Posiciones por conteo (pares distintos; el código y la zona solo si es una sola posición).
        var binPairs = await db.Set<CycleCountLine>().AsNoTracking()
            .Where(l => countIds.Contains(l.CycleCountId))
            .Select(l => new { l.CycleCountId, l.WarehouseBinId }).Distinct().ToListAsync(ct);
        var binsByCount = binPairs.GroupBy(p => p.CycleCountId).ToDictionary(g => g.Key, g => g.Select(p => p.WarehouseBinId).ToList());
        var singleBinIds = binsByCount.Values.Where(b => b.Count == 1).Select(b => b[0]).Distinct().ToList();
        var singleBins = singleBinIds.Count == 0
            ? new Dictionary<int, (string Code, string ZoneCode)>()
            : (await (from b in db.Set<WarehouseBin>().AsNoTracking()
                      join z in db.Set<WarehouseZone>().AsNoTracking() on b.WarehouseZoneId equals z.WarehouseZoneId
                      where singleBinIds.Contains(b.WarehouseBinId) && warehouseIds.Contains(b.WarehouseId)
                      select new { b.WarehouseBinId, b.Code, ZoneCode = z.Code }).ToListAsync(ct))
                .ToDictionary(b => b.WarehouseBinId, b => (b.Code, b.ZoneCode));

        // Origen (MANUAL | CHANGES) con su etiqueta.
        var origins = (await lookups.GetDomainAsync(LookupDomains.CycleCountOrigin, ct))
            .ToDictionary(l => l.LookupCodeId, l => (l.InternalCode, Label: MultilingualText.Resolve(l.LabelJson, tenant.Lang)));

        // Tarea COUNT del conteo: la abierta (PENDING/IN_PROGRESS) o, si no hay, la última; con su asignado.
        var tasks = await CountTasksAsync(countIds, ct);
        var userIds = tasks.Values.Where(t => t.AssignedToUserId != null).Select(t => t.AssignedToUserId!.Value).Distinct().ToList();
        var users = userIds.Count == 0
            ? new Dictionary<int, string>()
            : await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName ?? u.Email ?? string.Empty, ct);

        return page.Select(c =>
        {
            var s = summaries.GetValueOrDefault(c.CycleCountId);
            var w = warehouses.GetValueOrDefault(c.WarehouseId);
            var st = statusMap.GetValueOrDefault(c.StatusCodeId);
            var bins = binsByCount.GetValueOrDefault(c.CycleCountId);
            (string Code, string ZoneCode)? single = bins is { Count: 1 } && singleBins.TryGetValue(bins[0], out var sb) ? sb : null;
            (string InternalCode, string Label)? origin = c.OriginLookupId is int oid && origins.TryGetValue(oid, out var o) ? o : null;
            var task = tasks.GetValueOrDefault(c.CycleCountId);
            return new CycleCountDto(c.CycleCountId, c.Number, w?.PublicId ?? Guid.Empty, w?.Code ?? string.Empty,
                st.Code ?? string.Empty, st.Label ?? string.Empty, s?.Lines ?? 0, s?.Counted ?? 0, s?.WithVariance ?? 0, s?.Net ?? 0m,
                c.CreatedAtUtc, c.ReconciledAtUtc, c.IsActive,
                BinCount: bins?.Count ?? 0, BinCode: single?.Code, ZoneCode: single?.ZoneCode,
                OriginCode: origin?.InternalCode ?? (c.OriginLookupId is null ? CycleCountOrigins.Manual : null),
                TaskId: task?.WarehouseTaskId,
                AssignedToName: task?.AssignedToUserId is int uid ? users.GetValueOrDefault(uid) : null,
                Origin: origin?.Label, ChangesFromUtc: c.ChangesFromUtc, ChangesToUtc: c.ChangesToUtc,
                AssignedToUserId: task?.AssignedToUserId, CorrectedLines: s?.Corrected ?? 0);
        }).ToList();
    }

    private sealed record CountTaskInfo(int WarehouseTaskId, int? AssignedToUserId);

    /// <summary>Por conteo: su tarea COUNT abierta (PENDING/IN_PROGRESS, la más antigua) o, si no hay, la más reciente.</summary>
    private async Task<Dictionary<int, CountTaskInfo>> CountTasksAsync(IReadOnlyCollection<int> countIds, CancellationToken ct)
    {
        var result = new Dictionary<int, CountTaskInfo>();
        var refType = await lookups.TryGetIdAsync(LookupDomains.EntityType, EntityTypes.CycleCount, ct);
        var countType = await lookups.TryGetIdAsync(LookupDomains.WarehouseTaskType, WarehouseTaskTypes.Count, ct);
        if (refType is null || countType is null || countIds.Count == 0) return result;
        var ids = countIds.ToList();
        var open = await db.StatusCodes.AsNoTracking()
            .Where(s => s.Entity == StatusDomains.WarehouseTaskStatus
                        && (s.InternalCode == WarehouseTaskStatuses.Pending || s.InternalCode == WarehouseTaskStatuses.InProgress))
            .Select(s => s.StatusCodeId).ToListAsync(ct);
        var rows = await db.Set<WarehouseTask>().AsNoTracking()
            .Where(t => t.RefEntityLookupId == refType && t.TaskTypeLookupId == countType && t.RefId != null && ids.Contains(t.RefId.Value))
            .Select(t => new { t.WarehouseTaskId, RefId = t.RefId!.Value, t.StatusCodeId, t.AssignedToUserId }).ToListAsync(ct);
        foreach (var g in rows.GroupBy(r => r.RefId))
        {
            var pick = g.Where(r => open.Contains(r.StatusCodeId)).OrderBy(r => r.WarehouseTaskId).FirstOrDefault()
                       ?? g.OrderByDescending(r => r.WarehouseTaskId).First();
            result[g.Key] = new CountTaskInfo(pick.WarehouseTaskId, pick.AssignedToUserId);
        }
        return result;
    }

    // ================================================================ ficha (modo informado)

    public async Task<CycleCountDetailDto> GetAsync(int id, CycleCountLinesQuery? q, CancellationToken ct)
    {
        var cc = await ResolveAsync(id, ct);
        return await BuildDetailAsync(cc, q, ct);
    }

    /// <summary>
    /// Lote 8A — ficha para quien consulta: con blind = true (sin warehouse.count, solo inventory.view) es el conteo a ciegas:
    /// las cantidades esperadas de las líneas se omiten (ver Blind) y el filtro onlyVariance se ignora (filtrar por diferencia
    /// revelaría lo esperado).
    /// </summary>
    public async Task<CycleCountDetailDto> GetAsync(int id, CycleCountLinesQuery? q, bool blind, CancellationToken ct)
    {
        if (!blind) return await GetAsync(id, q, ct);
        var detail = await GetAsync(id, q is null ? null : q with { OnlyVariance = null }, ct);
        return Blind(detail);
    }

    /// <summary>
    /// Conteo a ciegas (Lote 8A): quita de la ficha todo lo que revela la cantidad esperada por línea (foto, diferencia, saldo
    /// actual, series esperadas, lo reconciliado y el ajuste) y, en el encabezado, las líneas con diferencia y la diferencia
    /// neta (con lo contado permitirían deducir lo esperado); conserva lo contado. Función pura.
    /// </summary>
    public static CycleCountDetailDto Blind(CycleCountDetailDto detail)
        => detail with
        {
            Count = detail.Count with { VarianceLines = null, NetVariance = null },
            Lines = detail.Lines.Select(l => l with
            {
                SystemQty = null,
                VarianceQty = null,
                ExpectedSerials = Array.Empty<string>(),
                IsStale = false,
                CurrentQty = null,
                ReconciledSystemQty = null,
                SystemQtyChanged = false,
                AdjustedQty = null,
                AdjustmentTxnId = null,
            }).ToList(),
            IsBlind = true,
        };

    // ================================================================ alta

    /// <summary>
    /// Alta OPEN con número CC-#####: una línea por saldo en mano &gt; 0 de posiciones activas del almacén (el indicado o el
    /// único activo), filtrado por zonas, posiciones, productos y categorías (con subcategorías). SystemQty = foto. Más de
    /// 1000 líneas → 400 'El conteo admite como máximo 1000 líneas; acote los filtros.'. Crea una tarea COUNT en la cola.
    /// Lote 21: con productPublicIds y sin binIds ni zoneIds es un conteo POR PRODUCTO (origen PRODUCT): una línea por cada
    /// posición y lote donde el sistema dice que hay existencia; el producto sin existencia → 400 'NothingSelected'.
    /// Lote 21 (adenda): AllowEmpty con exactamente un producto y sin otros filtros → si el producto no tiene existencia el conteo
    /// se crea vacío (sin líneas, origen PRODUCT); con otros filtros o más de un producto → 400 'allowEmpty'.
    /// </summary>
    public async Task<CycleCountDetailDto> CreateAsync(CycleCountCreateRequest? req, CancellationToken ct)
    {
        req ??= new CycleCountCreateRequest();
        if (req.AllowEmpty
            && (req.ProductPublicIds?.Distinct().Count() != 1 || req.BinIds is { Length: > 0 } || req.ZoneIds is { Length: > 0 } || req.CategoryIds is { Length: > 0 }))
            throw new ValidationException("allowEmpty", CycleCountRules.AllowEmptyOnlyOneProduct);
        var tenantId = ((TenantContext)tenant).RequireTenantId();
        var warehouse = await ResolveWarehouseOrDefaultAsync(req.WarehousePublicId, ct);
        if (!warehouse.IsActive) throw new StatusRuleException(WarehouseInactive);
        var wid = warehouse.WarehouseId;

        // Hijas resueltas SIEMPRE dentro del almacén filtrado: de otro almacén u otro tenant → 404 sin oráculo.
        List<int>? zoneIds = null, binIds = null, productIds = null;
        HashSet<int>? categoryIds = null;
        if (req.ZoneIds is { Length: > 0 })
        {
            zoneIds = req.ZoneIds.Distinct().ToList();
            var found = await db.Set<WarehouseZone>().AsNoTracking().CountAsync(z => z.WarehouseId == wid && zoneIds.Contains(z.WarehouseZoneId), ct);
            if (found != zoneIds.Count) throw new NotFoundException("Zona", feminine: true);
        }
        if (req.BinIds is { Length: > 0 })
        {
            binIds = req.BinIds.Distinct().ToList();
            var found = await db.Set<WarehouseBin>().AsNoTracking().CountAsync(b => b.WarehouseId == wid && binIds.Contains(b.WarehouseBinId), ct);
            if (found != binIds.Count) throw new NotFoundException("Posición", feminine: true);
        }
        if (req.ProductPublicIds is { Length: > 0 })
        {
            var pubs = req.ProductPublicIds.Distinct().ToList();
            productIds = await db.Set<Product>().AsNoTracking().Where(p => pubs.Contains(p.PublicId)).Select(p => p.ProductId).ToListAsync(ct);
            if (productIds.Count != pubs.Count) throw new NotFoundException("Producto");
        }
        if (req.CategoryIds is { Length: > 0 }) categoryIds = await ExpandCategoriesAsync(req.CategoryIds, ct);

        // Foto del sistema (sin bloqueo: es informativa; el ajuste se calcula contra el saldo actual al reconciliar).
        var candidatesQuery =
            from b in db.Set<StockBalance>().AsNoTracking()
            join bin in db.Set<WarehouseBin>().AsNoTracking() on b.WarehouseBinId equals (int?)bin.WarehouseBinId
            join p in db.Set<Product>().AsNoTracking() on b.ProductId equals p.ProductId
            where b.WarehouseId == wid && bin.WarehouseId == wid && bin.IsActive && b.QtyOnHand > 0m
            select new { b.ProductId, bin.WarehouseBinId, BinCode = bin.Code, bin.WarehouseZoneId, b.LotId, b.QtyOnHand, p.Sku, p.ProductCategoryId };
        if (zoneIds is not null) candidatesQuery = candidatesQuery.Where(x => zoneIds.Contains(x.WarehouseZoneId));
        if (binIds is not null) candidatesQuery = candidatesQuery.Where(x => binIds.Contains(x.WarehouseBinId));
        if (productIds is not null) candidatesQuery = candidatesQuery.Where(x => productIds.Contains(x.ProductId));
        if (categoryIds is not null)
        {
            var cats = categoryIds.ToList();
            candidatesQuery = candidatesQuery.Where(x => x.ProductCategoryId != null && cats.Contains(x.ProductCategoryId.Value));
        }
        // Tope técnico: se leen a lo sumo MaxLines + 1 saldos para detectar el exceso sin cargar el almacén entero.
        var raw = await candidatesQuery.OrderBy(x => x.WarehouseBinId).ThenBy(x => x.ProductId).ThenBy(x => x.LotId)
            .Take(CycleCountRules.MaxLines + 1).ToListAsync(ct);
        var lotIds = raw.Where(x => x.LotId != null).Select(x => x.LotId!.Value).Distinct().ToList();
        var lotNumbers = await LotNumbersAsync(lotIds, raw.Select(x => x.ProductId), ct);
        var (selected, selectError) = CycleCountRules.SelectLines(raw.Select(x => new CountCandidate(
            x.WarehouseBinId, x.BinCode, x.ProductId, x.Sku, x.LotId, x.LotId is int l ? lotNumbers.GetValueOrDefault(l) : null, x.QtyOnHand)));
        // Adenda: el producto existe (ya se resolvió; si no, 404) pero no tiene existencia → conteo vacío, solo si se pidió.
        var emptyByProduct = req.AllowEmpty && selectError == CycleCountRules.NothingSelected;
        if (selectError is not null && !emptyByProduct) throw new ValidationException("filters", selectError);

        await numbers.EnsureAsync(NumberKinds.CycleCount, null, ct);
        var initial = await statuses.GetInitialAsync(StatusDomains.CycleCountStatus, ct);
        // Lote 14: origen MANUAL. Lote 21: PRODUCT cuando se crea con productos y sin posiciones ni zonas (conteo por producto);
        // si el catálogo aún no trae PRODUCT, cae a MANUAL.
        var byProduct = req.ProductPublicIds is { Length: > 0 } && req.BinIds is not { Length: > 0 } && req.ZoneIds is not { Length: > 0 };
        var manualOrigin = (byProduct ? await lookups.TryGetIdAsync(LookupDomains.CycleCountOrigin, CycleCountOrigins.Product, ct) : null)
                           ?? await lookups.TryGetIdAsync(LookupDomains.CycleCountOrigin, CycleCountOrigins.Manual, ct);

        var id = await db.RunInTransactionAsync(async ct2 =>
        {
            var seq = await numbers.NextAsync(NumberKinds.CycleCount, null, ct2);
            var cc = new CycleCount
            {
                TenantId = tenantId, WarehouseId = wid, Number = NumberFormat.Resolve(NumberPattern, seq),
                StatusCodeId = initial.StatusCodeId, CreatedAtUtc = DateTime.UtcNow, CreatedBy = tenant.UserId, IsActive = true,
                OriginLookupId = manualOrigin,
            };
            db.Set<CycleCount>().Add(cc);
            await db.SaveGuardedAsync(NumberTakenMessage, ct2);

            db.Set<CycleCountLine>().AddRange(selected.Select(s => new CycleCountLine
            {
                CycleCountId = cc.CycleCountId, WarehouseBinId = s.BinId, ProductId = s.ProductId, LotId = s.LotId, SystemQty = s.QtyOnHand,
            }));
            var born = await statuses.TransitionAsync(StatusDomains.CycleCountStatus, EntityTypes.CycleCount, cc.CycleCountId, null, initial.InternalCode, null, ct2);
            cc.StatusCodeId = born.StatusCodeId;
            await CreateCountTaskAsync(cc, ct2);
            await db.SaveGuardedAsync(CycleCountRules.LineDuplicated, ct2);
            return cc.CycleCountId;
        }, ct);

        return await GetAsync(id, null, ct);
    }

    // ================================================================ lo cambiado (Lote 14: D2, D3, D4)

    private sealed record ChangesPosition(WarehouseBin Bin, IReadOnlyList<CountCandidate> Lines);

    private sealed record ChangesPlan(
        Warehouse Warehouse, DateTime FromUtc, DateTime ToUtc, DateTime? LastChangesToUtc, int Movements,
        IReadOnlyList<ChangesPosition> Positions, int ReportedPositions, int WithOpenCount, int Inactive, int Empty, string? Problem)
    {
        /// <summary>Positions = conteos que se crearían (con más de 200, cuántas posiciones hay para que la pantalla lo diga).</summary>
        public CycleCountChangesPreviewDto ToDto() => new(FromUtc, ToUtc, Movements, ReportedPositions,
            WithOpenCount, Inactive, Positions.Sum(p => p.Lines.Count), ChangedCountRules.MaxPositions, Warehouse.PublicId, Warehouse.Code,
            Empty, LastChangesToUtc is DateTime last ? ChangedCountRules.AsUtc(last) : null, Problem);
    }

    /// <summary>
    /// Vista previa de "lo cambiado": lo mismo que haría el alta, sin escribir. Ventana inválida → 400 (desde &gt; hasta, más de 31
    /// días); almacén inactivo → 422; zona de otro almacén → 404. Los casos sin nada que crear NO son error aquí: llegan en
    /// Problem con el mensaje exacto con que respondería el alta.
    /// </summary>
    public async Task<CycleCountChangesPreviewDto> PreviewChangesAsync(CycleCountFromChangesRequest? req, CancellationToken ct)
        => (await PlanChangesAsync(req ?? new CycleCountFromChangesRequest(), ct)).ToDto();

    /// <summary>
    /// Alta de "lo cambiado" (D2, D3, D4): un conteo CC-##### Pendiente por posición con movimientos en la ventana (sin los de
    /// un conteo), con todo lo que tiene en mano y, con IncludeEmpty, las claves movidas que hoy están en 0 (SystemQty 0);
    /// salta posiciones inactivas o con un conteo Pendiente o Contado; origen CHANGES y la ventana guardados; una tarea COUNT
    /// por conteo. TODO en una transacción (un error no deja conteos a medias). 400 'filters' si no hay movimientos, si todas
    /// ya tienen conteo pendiente, si son más de 200 o si no hay nada que contar.
    /// </summary>
    public async Task<CycleCountBatchResultDto> CreateFromChangesAsync(CycleCountFromChangesRequest? req, CancellationToken ct)
    {
        var plan = await PlanChangesAsync(req ?? new CycleCountFromChangesRequest(), ct);
        if (plan.Problem is not null) throw new ValidationException("filters", plan.Problem);

        var tenantId = ((TenantContext)tenant).RequireTenantId();
        var wid = plan.Warehouse.WarehouseId;
        var changesOrigin = await lookups.TryGetIdAsync(LookupDomains.CycleCountOrigin, CycleCountOrigins.Changes, ct);
        await numbers.EnsureAsync(NumberKinds.CycleCount, null, ct);
        var initial = await statuses.GetInitialAsync(StatusDomains.CycleCountStatus, ct);

        var ids = await db.RunInTransactionAsync(async ct2 =>
        {
            // Otra generación pudo tomar alguna posición mientras tanto: se vuelve a mirar dentro de la transacción.
            var busyNow = await BinsWithOpenCountAsync(wid, plan.Positions.Select(p => p.Bin.WarehouseBinId).ToList(), ct2);
            var positions = plan.Positions.Where(p => !busyNow.Contains(p.Bin.WarehouseBinId)).ToList();
            if (positions.Count == 0) throw new ValidationException("filters", ChangedCountRules.AllHaveOpenCount(plan.Positions.Count));

            var created = new List<int>();
            var now = DateTime.UtcNow;
            foreach (var position in positions)
            {
                var seq = await numbers.NextAsync(NumberKinds.CycleCount, null, ct2);
                var cc = new CycleCount
                {
                    TenantId = tenantId, WarehouseId = wid, Number = NumberFormat.Resolve(NumberPattern, seq),
                    StatusCodeId = initial.StatusCodeId, CreatedAtUtc = now, CreatedBy = tenant.UserId, IsActive = true,
                    OriginLookupId = changesOrigin, ChangesFromUtc = plan.FromUtc, ChangesToUtc = plan.ToUtc,
                };
                db.Set<CycleCount>().Add(cc);
                await db.SaveGuardedAsync(NumberTakenMessage, ct2);

                db.Set<CycleCountLine>().AddRange(position.Lines.Select(s => new CycleCountLine
                {
                    CycleCountId = cc.CycleCountId, WarehouseBinId = s.BinId, ProductId = s.ProductId, LotId = s.LotId, SystemQty = s.QtyOnHand,
                }));
                var born = await statuses.TransitionAsync(StatusDomains.CycleCountStatus, EntityTypes.CycleCount, cc.CycleCountId, null, initial.InternalCode, null, ct2);
                cc.StatusCodeId = born.StatusCodeId;
                await CreateCountTaskAsync(cc, ct2);
                await db.SaveGuardedAsync(CycleCountRules.LineDuplicated, ct2);
                created.Add(cc.CycleCountId);
            }
            return created;
        }, ct);

        db.ChangeTracker.Clear();
        var counts = await db.Set<CycleCount>().AsNoTracking().Where(c => ids.Contains(c.CycleCountId)).ToListAsync(ct);
        var headers = await MapHeadersAsync(counts.OrderBy(c => c.CycleCountId).ToList(), ct);
        return new CycleCountBatchResultDto(plan.ToDto(), headers);
    }

    /// <summary>
    /// Plan de "lo cambiado" (solo lectura): almacén, ventana, movimientos, posiciones y líneas; Problem = el 400 del alta si no
    /// hay nada que crear.
    /// </summary>
    private async Task<ChangesPlan> PlanChangesAsync(CycleCountFromChangesRequest req, CancellationToken ct)
    {
        var warehouse = await ResolveWarehouseOrDefaultAsync(req.WarehousePublicId, ct);
        if (!warehouse.IsActive) throw new StatusRuleException(WarehouseInactive);
        var wid = warehouse.WarehouseId;

        List<int>? zoneIds = null;
        if (req.ZoneIds is { Length: > 0 })
        {
            zoneIds = req.ZoneIds.Distinct().ToList();
            var found = await db.Set<WarehouseZone>().AsNoTracking().CountAsync(z => z.WarehouseId == wid && zoneIds.Contains(z.WarehouseZoneId), ct);
            if (found != zoneIds.Count) throw new NotFoundException("Zona", feminine: true);
        }

        // Ventana: por defecto desde el último 'hasta' de lo cambiado de este almacén (o las 00:00 locales de hoy).
        var changesOrigin = await lookups.TryGetIdAsync(LookupDomains.CycleCountOrigin, CycleCountOrigins.Changes, ct);
        DateTime? lastTo = changesOrigin is int co
            ? await db.Set<CycleCount>().AsNoTracking()
                .Where(c => c.WarehouseId == wid && c.OriginLookupId == co && c.ChangesToUtc != null)
                .MaxAsync(c => c.ChangesToUtc, ct)
            : null;
        var (fromUtc, toUtc, field, error) = ChangedCountRules.ResolveWindow(req.FromUtc, req.ToUtc, lastTo, clock.UtcNow, clock.Zone);
        if (error is not null) throw new ValidationException(field!, error);

        // Movimientos de la ventana [desde, hasta) que tocan el almacén, sin los que vienen de un conteo (D3).
        var countRef = await lookups.TryGetIdAsync(LookupDomains.EntityType, EntityTypes.CycleCount, ct);
        var txns = db.Set<InventoryTransaction>().AsNoTracking()
            .Where(t => t.CreatedAtUtc >= fromUtc && t.CreatedAtUtc < toUtc && (t.FromWarehouseId == wid || t.ToWarehouseId == wid));
        if (countRef is int cr) txns = txns.Where(t => t.RefEntityLookupId == null || t.RefEntityLookupId != cr);
        var movements = await txns.CountAsync(ct);
        var shapes = movements == 0
            ? []
            : await txns.Select(t => new { t.FromWarehouseId, t.FromBinId, t.ToWarehouseId, t.ToBinId, t.ProductId, t.LotId }).Distinct().ToListAsync(ct);
        var keys = ChangedCountRules.MovedKeys(shapes.Select(s => new ChangedMovement(s.FromWarehouseId, s.FromBinId, s.ToWarehouseId, s.ToBinId, s.ProductId, s.LotId)), wid);
        var binIds = ChangedCountRules.Positions(keys).ToList();

        var bins = binIds.Count == 0
            ? new List<WarehouseBin>()
            : await db.Set<WarehouseBin>().AsNoTracking().Where(b => b.WarehouseId == wid && binIds.Contains(b.WarehouseBinId)).ToListAsync(ct);
        if (zoneIds is not null) bins = bins.Where(b => zoneIds.Contains(b.WarehouseZoneId)).ToList();
        var inactive = bins.Count(b => !b.IsActive);
        var active = bins.Where(b => b.IsActive).ToList();
        var busy = await BinsWithOpenCountAsync(wid, active.Select(b => b.WarehouseBinId).ToList(), ct);
        var free = active.Where(b => !busy.Contains(b.WarehouseBinId)).OrderBy(b => b.Code, StringComparer.Ordinal).ThenBy(b => b.WarehouseBinId).ToList();
        var withOpen = active.Count - free.Count;

        ChangesPlan Plan(IReadOnlyList<ChangesPosition> positions, int empty, string? problem, int? reported = null)
            => new(warehouse, fromUtc, toUtc, lastTo, movements, positions, reported ?? positions.Count, withOpen, inactive, empty, problem);

        if (active.Count == 0) return Plan([], 0, ChangedCountRules.NoMovements(warehouse.Code, fromUtc, toUtc, clock.Zone));
        if (free.Count == 0) return Plan([], 0, ChangedCountRules.AllHaveOpenCount(withOpen));
        if (free.Count > ChangedCountRules.MaxPositions) return Plan([], 0, ChangedCountRules.TooManyPositions(free.Count), free.Count);

        // Líneas: todo lo que hay en mano en cada posición y (IncludeEmpty) las claves movidas que hoy están en 0.
        var freeIds = free.Select(b => b.WarehouseBinId).ToList();
        var balances = await (from b in db.Set<StockBalance>().AsNoTracking()
                              join p in db.Set<Product>().AsNoTracking() on b.ProductId equals p.ProductId
                              where b.WarehouseId == wid && b.WarehouseBinId != null && freeIds.Contains(b.WarehouseBinId.Value) && b.QtyOnHand >= 0m
                              select new { BinId = b.WarehouseBinId!.Value, b.ProductId, b.LotId, b.QtyOnHand, p.Sku }).ToListAsync(ct);
        var lotIds = balances.Where(x => x.LotId != null).Select(x => x.LotId!.Value).Distinct().ToList();
        var lotNumbers = await LotNumbersAsync(lotIds, balances.Select(x => x.ProductId), ct);
        var balancesByBin = balances.ToLookup(x => x.BinId);
        var keysByBin = keys.ToLookup(k => k.BinId);

        var positions = new List<ChangesPosition>();
        var empty = 0;
        foreach (var bin in free)
        {
            var candidates = balancesByBin[bin.WarehouseBinId].Select(x => new CountCandidate(
                bin.WarehouseBinId, bin.Code, x.ProductId, x.Sku, x.LotId, x.LotId is int l ? lotNumbers.GetValueOrDefault(l) : null, x.QtyOnHand));
            var lines = ChangedCountRules.SelectLines(candidates, keysByBin[bin.WarehouseBinId], req.IncludeEmpty);
            if (lines.Count == 0) { empty++; continue; }
            if (lines.Count > CycleCountRules.MaxLines) return Plan([], empty, CycleCountRules.TooManyLines);
            positions.Add(new ChangesPosition(bin, lines));
        }
        return positions.Count == 0 ? Plan([], empty, CycleCountRules.NothingSelected) : Plan(positions, empty, null);
    }

    /// <summary>Posiciones del almacén (de la lista) con una línea en un conteo activo Pendiente o Contado.</summary>
    private async Task<HashSet<int>> BinsWithOpenCountAsync(int warehouseId, IReadOnlyCollection<int> binIds, CancellationToken ct)
    {
        if (binIds.Count == 0) return new HashSet<int>();
        var ids = binIds.ToList();
        var openCodes = CycleCountStatuses.OpenCodes;
        var openIds = await db.StatusCodes.AsNoTracking()
            .Where(s => s.Entity == StatusDomains.CycleCountStatus && openCodes.Contains(s.InternalCode))
            .Select(s => s.StatusCodeId).ToListAsync(ct);
        var busy = await (from l in db.Set<CycleCountLine>().AsNoTracking()
                          join c in db.Set<CycleCount>().AsNoTracking() on l.CycleCountId equals c.CycleCountId
                          where c.IsActive && c.WarehouseId == warehouseId && openIds.Contains(c.StatusCodeId) && ids.Contains(l.WarehouseBinId)
                          select l.WarehouseBinId).Distinct().ToListAsync(ct);
        return busy.ToHashSet();
    }

    // ================================================================ captura

    /// <summary>
    /// Captura de cantidades (NONE/LOT) o de series (SERIAL: la cantidad contada es el número de series) en un conteo no
    /// reconciliado. Sin cantidad ni series la captura se borra. Errores por línea → 400 con Errors, sin guardar nada.
    /// </summary>
    public async Task<CycleCountDetailDto> CaptureAsync(int id, CountCaptureRequest req, CancellationToken ct)
    {
        if (req?.Lines is not { Count: > 0 }) throw new ValidationException("lines", CycleCountRules.CaptureRequired);
        var current = await ResolveAsync(id, ct);

        await db.RunInTransactionAsync(async ct2 =>
        {
            var (cc, ccStatus) = await LockEditableAsync(current.CycleCountId, ct2);
            EnsureRowVersion(cc, req.RowVersion);

            var lineIds = req.Lines.Select(l => l.LineId).Distinct().ToList();
            var lines = await db.Set<CycleCountLine>()
                .Where(l => l.CycleCountId == cc.CycleCountId && lineIds.Contains(l.CycleCountLineId))
                .ToDictionaryAsync(l => l.CycleCountLineId, ct2);
            if (lines.Count != lineIds.Count) throw new NotFoundException(CycleCountRules.LineNotFoundWhat, feminine: true);

            var productIds = lines.Values.Select(l => l.ProductId).Distinct().ToList();
            var products = await db.Set<Product>().AsNoTracking().Where(p => productIds.Contains(p.ProductId)).ToDictionaryAsync(p => p.ProductId, ct2);
            var tracking = await TrackingCodesAsync(products.Values.Select(p => p.TrackingTypeLookupId), ct2);

            // Primero se valida TODO; solo si no hay errores se aplica (nada queda a medias en el contexto).
            var errors = new Dictionary<string, string[]>();
            var changes = new List<(CycleCountLine Line, decimal? Counted, IReadOnlyList<string>? Serials)>();
            for (var i = 0; i < req.Lines.Count; i++)
            {
                var item = req.Lines[i];
                var line = lines[item.LineId];
                var p = products[line.ProductId];
                var code = tracking.GetValueOrDefault(p.TrackingTypeLookupId, TrackingTypes.None);
                var (counted, serials, field, error) = CycleCountRules.Capture(code, p.Sku, item.CountedQty, item.SerialNumbers);
                if (error is not null) errors[$"lines[{i}].{field}"] = new[] { error };
                else changes.Add((line, counted, serials));
            }
            if (errors.Count > 0) throw new ValidationException(errors);

            var now = DateTime.UtcNow;
            foreach (var (line, counted, serials) in changes) ApplyCaptureTo(line, counted, serials, ccStatus == CycleCountStatuses.Counted, now);
            await db.SaveGuardedAsync(DbExtensions.ConcurrencyMessage, ct2);
        }, ct);

        return await GetAsync(id, null, ct);
    }

    /// <summary>
    /// Lote 21 — aplica una captura a la línea con la regla única de evidencia (CycleCountRules.ApplyCapture): primera captura,
    /// recaptura del mismo usuario con el conteo abierto, o CORRECCIÓN (otro usuario, o cualquiera con el conteo ya Contado).
    /// CountedQty queda siempre con el valor vigente; la captura original se conserva. No mueve inventario.
    /// </summary>
    private void ApplyCaptureTo(CycleCountLine line, decimal? counted, IReadOnlyList<string>? serials, bool countFinished, DateTime nowUtc)
    {
        var next = CycleCountRules.ApplyCapture(
            new CycleCountRules.CaptureState(line.CountedQty, line.CountedSerialsJson, line.CapturedQty, line.CapturedSerialsJson,
                line.CapturedBy, line.CapturedAtUtc, line.CorrectedBy, line.CorrectedAtUtc),
            counted, serials, tenant.UserId, nowUtc, countFinished);
        line.CountedQty = next.CountedQty;
        line.CountedSerialsJson = next.CountedSerialsJson;
        line.CapturedQty = next.CapturedQty;
        line.CapturedSerialsJson = next.CapturedSerialsJson;
        line.CapturedBy = next.CapturedBy;
        line.CapturedAtUtc = next.CapturedAtUtc;
        line.CorrectedBy = next.CorrectedBy;
        line.CorrectedAtUtc = next.CorrectedAtUtc;
    }

    public const string BatchLineRepeated = "La línea se repite en la solicitud.";

    /// <summary>
    /// Lote 8A — captura en lote (cola del aparato): cada renglón por LineId o por posición + producto (+ lote por id o por
    /// número). Una línea existente se captura (mismas reglas que CaptureAsync); una que no está en el conteo se agrega con su
    /// captura (mismas reglas que AddLineAsync: posición del almacén 404 / inactiva 422, producto 404 / inactivo 422, lote del
    /// producto 404, seguimiento 400). Todo en UNA transacción: errores de forma o de captura → 400 con Errors por renglón
    /// ('lines[i].campo') sin guardar nada. Un renglón repetido (misma línea) → 400 'La línea se repite en la solicitud.'.
    /// </summary>
    public async Task<CycleCountDetailDto> CaptureBatchAsync(int id, CountBatchRequest req, CancellationToken ct)
    {
        if (req?.Lines is not { Count: > 0 }) throw new ValidationException("lines", CycleCountRules.CaptureRequired);
        if (req.Lines.Count > CycleCountRules.MaxLines) throw new ValidationException("lines", CycleCountRules.TooManyLines);

        // Forma (400) antes de tocar la BD.
        var shapeErrors = new Dictionary<string, string[]>();
        var lotNumbers = new string?[req.Lines.Count];
        for (var i = 0; i < req.Lines.Count; i++)
        {
            var item = req.Lines[i];
            if (item is null) { shapeErrors[$"lines[{i}]"] = new[] { CycleCountRules.ProductRequired }; continue; }
            if (item.LineId is not null) continue;
            if (item.BinId is null) shapeErrors[$"lines[{i}].binId"] = new[] { CycleCountRules.BinRequired };
            if (item.ProductPublicId is null) shapeErrors[$"lines[{i}].productPublicId"] = new[] { CycleCountRules.ProductRequired };
            if (item.LotId is not null && item.Lot is not null) shapeErrors[$"lines[{i}].lot"] = new[] { CycleCountRules.LotAmbiguous };
            if (item.Lot is not null)
            {
                var n = item.Lot.Number?.Trim();
                if (string.IsNullOrEmpty(n)) shapeErrors[$"lines[{i}].lot.number"] = new[] { CycleCountRules.LotNumberRequired };
                else if (n.Length > 60) shapeErrors[$"lines[{i}].lot.number"] = new[] { CycleCountRules.LotNumberTooLong };
                else lotNumbers[i] = n;
                if (item.Lot.ManufactureDate is DateOnly m && item.Lot.ExpiryDate is DateOnly e && m > e)
                    shapeErrors[$"lines[{i}].lot.manufactureDate"] = new[] { CycleCountRules.LotDates };
            }
        }
        if (shapeErrors.Count > 0) throw new ValidationException(shapeErrors);
        var current = await ResolveAsync(id, ct);

        await db.RunInTransactionAsync(async ct2 =>
        {
            var (cc, ccStatus) = await LockEditableAsync(current.CycleCountId, ct2);
            EnsureRowVersion(cc, req.RowVersion);

            var lines = await db.Set<CycleCountLine>().Where(l => l.CycleCountId == cc.CycleCountId).ToListAsync(ct2);
            var byId = lines.ToDictionary(l => l.CycleCountLineId);
            var byKey = new Dictionary<LineKey, CycleCountLine>();
            foreach (var l in lines) byKey.TryAdd(new LineKey(l.ProductId, l.WarehouseBinId, l.LotId), l);

            var items = req.Lines;
            var newItems = items.Where(x => x.LineId is null).ToList();
            var binIds = newItems.Select(x => x.BinId!.Value).Distinct().ToList();
            var bins = await db.Set<WarehouseBin>().AsNoTracking()
                .Where(b => b.WarehouseId == cc.WarehouseId && binIds.Contains(b.WarehouseBinId))
                .ToDictionaryAsync(b => b.WarehouseBinId, ct2);
            var publicIds = newItems.Select(x => x.ProductPublicId!.Value).Distinct().ToList();
            var productsByPublic = await db.Set<Product>().AsNoTracking().Where(p => publicIds.Contains(p.PublicId))
                .ToDictionaryAsync(p => p.PublicId, ct2);
            var productIds = lines.Select(l => l.ProductId).Concat(productsByPublic.Values.Select(p => p.ProductId)).Distinct().ToList();
            var products = await db.Set<Product>().AsNoTracking().Where(p => productIds.Contains(p.ProductId))
                .ToDictionaryAsync(p => p.ProductId, ct2);
            var tracking = await TrackingCodesAsync(products.Values.Select(p => p.TrackingTypeLookupId), ct2);

            var errors = new Dictionary<string, string[]>();
            var touched = new HashSet<CycleCountLine>(ReferenceEqualityComparer.Instance);
            var changes = new List<(CycleCountLine Line, decimal? Counted, IReadOnlyList<string>? Serials)>();
            var added = new List<CycleCountLine>();
            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i];
                CycleCountLine line;
                Product product;
                if (item.LineId is int lineId)
                {
                    line = byId.GetValueOrDefault(lineId) ?? throw new NotFoundException(CycleCountRules.LineNotFoundWhat, feminine: true);
                    product = products[line.ProductId];
                }
                else
                {
                    var bin = bins.GetValueOrDefault(item.BinId!.Value) ?? throw new NotFoundException("Posición", feminine: true);
                    product = productsByPublic.GetValueOrDefault(item.ProductPublicId!.Value) ?? throw new NotFoundException("Producto");
                    int? lotId = null;
                    if (item.LotId is int lid)
                    {
                        lotId = await db.Set<InventoryLot>().AsNoTracking()
                                    .Where(l => l.LotId == lid && l.ProductId == product.ProductId).Select(l => (int?)l.LotId).FirstOrDefaultAsync(ct2)
                                ?? throw new NotFoundException("Lote");
                    }
                    else if (lotNumbers[i] is string lotNumber)
                    {
                        // Lote por número: el existente del producto o uno nuevo (EnsureLot, en esta transacción: un error revierte).
                        lotId = await EnsureLotAsync(product.ProductId, lotNumber, item.Lot!.ManufactureDate, item.Lot.ExpiryDate, ct2);
                    }

                    var key = new LineKey(product.ProductId, bin.WarehouseBinId, lotId);
                    if (byKey.TryGetValue(key, out var existing))
                    {
                        line = existing;
                    }
                    else
                    {
                        // Lo encontrado: mismas reglas que agregar una línea a mano.
                        if (!bin.IsActive) throw new StatusRuleException(BinInactive(bin.Code));
                        if (!product.IsActive) throw new StatusRuleException(CycleCountRules.ProductInactive(product.Sku));
                        var trackingCode = tracking.GetValueOrDefault(product.TrackingTypeLookupId, TrackingTypes.None);
                        var lotError = CycleCountRules.ValidateLot(trackingCode, product.Sku, lotId is not null);
                        if (lotError is not null) { errors[$"lines[{i}].lot"] = new[] { lotError }; continue; }
                        var systemQty = await db.Set<StockBalance>().AsNoTracking()
                            .Where(b => b.WarehouseId == cc.WarehouseId && b.ProductId == product.ProductId && b.WarehouseBinId == bin.WarehouseBinId && b.LotId == lotId)
                            .SumAsync(b => b.QtyOnHand, ct2);
                        line = new CycleCountLine
                        {
                            CycleCountId = cc.CycleCountId, WarehouseBinId = bin.WarehouseBinId, ProductId = product.ProductId, LotId = lotId,
                            SystemQty = systemQty,
                        };
                        byKey[key] = line;
                        added.Add(line);
                    }
                }

                if (!touched.Add(line)) { errors[$"lines[{i}]"] = new[] { BatchLineRepeated }; continue; }
                var code = tracking.GetValueOrDefault(product.TrackingTypeLookupId, TrackingTypes.None);
                var (counted, serials, field, error) = CycleCountRules.Capture(code, product.Sku, item.CountedQty, item.SerialNumbers);
                if (error is not null) errors[$"lines[{i}].{field}"] = new[] { error };
                else changes.Add((line, counted, serials));
            }
            if (errors.Count > 0) throw new ValidationException(errors);
            if (lines.Count + added.Count > CycleCountRules.MaxLines) throw new ValidationException("lines", CycleCountRules.TooManyLines);

            var now = DateTime.UtcNow;
            foreach (var (line, counted, serials) in changes) ApplyCaptureTo(line, counted, serials, ccStatus == CycleCountStatuses.Counted, now);
            db.Set<CycleCountLine>().AddRange(added);
            await db.SaveGuardedAsync(CycleCountRules.LineDuplicated, ct2);
        }, ct);

        return await GetAsync(id, null, ct);
    }

    /// <summary>
    /// Línea agregada a mano (lo encontrado en una posición del almacén del conteo): posición del almacén (404), activa (422);
    /// producto activo (404/422); lote por id (del producto, 404) o por número (EnsureLot: otras fechas → 409); seguimiento
    /// coherente (400). Repetida → 409 'Esa posición, producto y lote ya están en el conteo.'. SystemQty = saldo actual.
    /// </summary>
    public async Task<CycleCountDetailDto> AddLineAsync(int id, CountAddLineRequest req, CancellationToken ct)
    {
        if (req is null) throw new ValidationException("body", "El cuerpo de la solicitud es obligatorio.");
        var errors = new Dictionary<string, string[]>();
        if (req.BinId is null) errors["binId"] = new[] { CycleCountRules.BinRequired };
        if (req.ProductPublicId is null) errors["productPublicId"] = new[] { CycleCountRules.ProductRequired };
        if (req.LotId is not null && req.Lot is not null) errors["lot"] = new[] { CycleCountRules.LotAmbiguous };
        string? lotNumber = null;
        if (req.Lot is not null)
        {
            lotNumber = req.Lot.Number?.Trim();
            if (string.IsNullOrEmpty(lotNumber)) errors["lot.number"] = new[] { CycleCountRules.LotNumberRequired };
            else if (lotNumber.Length > 60) errors["lot.number"] = new[] { CycleCountRules.LotNumberTooLong };
            if (req.Lot.ManufactureDate is DateOnly m && req.Lot.ExpiryDate is DateOnly e && m > e) errors["lot.manufactureDate"] = new[] { CycleCountRules.LotDates };
        }
        if (errors.Count > 0) throw new ValidationException(errors);
        var current = await ResolveAsync(id, ct);

        await db.RunInTransactionAsync(async ct2 =>
        {
            var (cc, _) = await LockEditableAsync(current.CycleCountId, ct2);

            var bin = await db.Set<WarehouseBin>().AsNoTracking()
                          .FirstOrDefaultAsync(b => b.WarehouseBinId == req.BinId!.Value && b.WarehouseId == cc.WarehouseId, ct2)
                      ?? throw new NotFoundException("Posición", feminine: true);
            if (!bin.IsActive) throw new StatusRuleException(BinInactive(bin.Code));
            var product = await db.Set<Product>().AsNoTracking().FirstOrDefaultAsync(p => p.PublicId == req.ProductPublicId!.Value, ct2)
                          ?? throw new NotFoundException("Producto");
            if (!product.IsActive) throw new StatusRuleException(CycleCountRules.ProductInactive(product.Sku));
            var code = await TrackingOfAsync(product.TrackingTypeLookupId, ct2);

            var lotError = CycleCountRules.ValidateLot(code, product.Sku, req.LotId is not null || req.Lot is not null);
            if (lotError is not null) throw new ValidationException("lot", lotError);
            var (counted, serials, field, captureError) = CycleCountRules.Capture(code, product.Sku, req.CountedQty, req.SerialNumbers);
            if (captureError is not null) throw new ValidationException(field!, captureError);

            int? lotId = null;
            if (req.LotId is int lid)
            {
                lotId = await db.Set<InventoryLot>().AsNoTracking()
                            .Where(l => l.LotId == lid && l.ProductId == product.ProductId).Select(l => (int?)l.LotId).FirstOrDefaultAsync(ct2)
                        ?? throw new NotFoundException("Lote");
            }
            else if (lotNumber is not null)
            {
                lotId = await EnsureLotAsync(product.ProductId, lotNumber, req.Lot!.ManufactureDate, req.Lot.ExpiryDate, ct2);
            }

            var lines = await db.Set<CycleCountLine>().AsNoTracking().Where(l => l.CycleCountId == cc.CycleCountId)
                .Select(l => new { l.WarehouseBinId, l.ProductId, l.LotId }).ToListAsync(ct2);
            if (lines.Count >= CycleCountRules.MaxLines) throw new ValidationException("lines", CycleCountRules.TooManyLines);
            if (lines.Any(l => l.WarehouseBinId == bin.WarehouseBinId && l.ProductId == product.ProductId && l.LotId == lotId))
                throw new ConflictException(CycleCountRules.LineDuplicated);

            var systemQty = await db.Set<StockBalance>().AsNoTracking()
                .Where(b => b.WarehouseId == cc.WarehouseId && b.ProductId == product.ProductId && b.WarehouseBinId == bin.WarehouseBinId && b.LotId == lotId)
                .SumAsync(b => b.QtyOnHand, ct2);

            var added = new CycleCountLine
            {
                CycleCountId = cc.CycleCountId, WarehouseBinId = bin.WarehouseBinId, ProductId = product.ProductId, LotId = lotId,
                SystemQty = systemQty,
            };
            ApplyCaptureTo(added, counted, serials, false, DateTime.UtcNow);   // lo encontrado es una primera captura de quien lo agrega
            db.Set<CycleCountLine>().Add(added);
            await db.SaveGuardedAsync(CycleCountRules.LineDuplicated, ct2);
        }, ct);

        return await GetAsync(id, null, ct);
    }

    /// <summary>Terminar de contar: OPEN → COUNTED con todas las líneas capturadas (si no, 422 'Faltan {n} línea(s) por contar.').</summary>
    public async Task<CycleCountDetailDto> FinishAsync(int id, CountReconcileRequest? req, CancellationToken ct)
    {
        var comment = TrimComment(req?.Comment);
        var current = await ResolveAsync(id, ct);
        await db.RunInTransactionAsync(async ct2 =>
        {
            var (cc, code) = await LockEditableAsync(current.CycleCountId, ct2);
            if (code == CycleCountStatuses.Counted) throw new StatusRuleException(CycleCountRules.CountAlreadyFinished);
            EnsureRowVersion(cc, req?.RowVersion);
            var counted = await db.Set<CycleCountLine>().AsNoTracking().Where(l => l.CycleCountId == cc.CycleCountId)
                .Select(l => l.CountedQty).ToListAsync(ct2);
            if (counted.Count == 0) throw new StatusRuleException(CycleCountRules.NoLines);
            var pending = CycleCountRules.PendingLines(counted);
            if (pending > 0) throw new StatusRuleException(CycleCountRules.CountIncomplete(pending));

            var to = await statuses.TransitionAsync(StatusDomains.CycleCountStatus, EntityTypes.CycleCount, cc.CycleCountId, cc.StatusCodeId,
                CycleCountStatuses.Counted, comment, ct2);
            cc.StatusCodeId = to.StatusCodeId;
            await db.SaveGuardedAsync(DbExtensions.ConcurrencyMessage, ct2);
        }, ct);
        return await GetAsync(id, null, ct);
    }

    /// <summary>
    /// 'Refrescar' (opcional, para quien prefiere recontar): las líneas cuya foto quedó vieja (el saldo actual ya no es la
    /// foto) toman el saldo actual como nueva foto y pierden su captura. Las líneas al día no cambian.
    /// </summary>
    public async Task<CycleCountDetailDto> RefreshAsync(int id, CancellationToken ct)
    {
        var current = await ResolveAsync(id, ct);
        await db.RunInTransactionAsync(async ct2 =>
        {
            var (cc, _) = await LockEditableAsync(current.CycleCountId, ct2);
            var lines = await db.Set<CycleCountLine>().Where(l => l.CycleCountId == cc.CycleCountId).ToListAsync(ct2);
            var balances = await CurrentBalancesAsync(cc.WarehouseId, lines.Select(l => l.ProductId), tracked: false, ct2);
            foreach (var line in lines)
            {
                var now = balances.GetValueOrDefault(new LineKey(line.ProductId, line.WarehouseBinId, line.LotId))?.QtyOnHand ?? 0m;
                if (!CycleCountRules.IsStale(line.SystemQty, now)) continue;
                line.SystemQty = now;
                ApplyCaptureTo(line, null, null, false, DateTime.UtcNow);   // sin captura: la línea vuelve a pendiente y pierde la evidencia
            }
            await db.SaveGuardedAsync(DbExtensions.ConcurrencyMessage, ct2);
        }, ct);
        return await GetAsync(id, null, ct);
    }

    // ================================================================ reconciliación (D22)

    /// <summary>
    /// Reconciliar (warehouse.count). Dentro de una transacción: encabezado bloqueado (el segundo de dos → 422
    /// CountNotOpen); todas las líneas contadas (422 CountIncomplete); saldos bloqueados; por línea ReconciledSystemQty =
    /// saldo en mano actual y SystemQtyChanged = actual ≠ foto; lo contado por debajo de lo reservado → 409 con Errors por
    /// línea SIN escribir nada; ajustes ADJUSTMENT COUNT_VARIANCE por CONTADO − ACTUAL (o bajas/altas/TRANSFER en serie)
    /// con Ref CYCLE_COUNT; AdjustmentTxnId; tarea COUNT → DONE. Lote 14 (D7, D8): en un paso desde Pendiente o Contado a
    /// RECONCILED_VARIANCE 'Diferencia' (se asentó algún movimiento) o RECONCILED 'Concordancia', con fecha y usuario.
    /// Lote 21: el cálculo de qué se asienta es BuildPlanAsync, el MISMO que usa la vista previa (PreviewReconcileAsync) y el
    /// cierre en bloque; el motivo del movimiento de una línea corregida lleva la evidencia (quién contó, qué y quién corrigió).
    /// </summary>
    public async Task<CycleCountDetailDto> ReconcileAsync(int id, CountReconcileRequest? req, CancellationToken ct)
    {
        await ReconcileCoreAsync(id, TrimComment(req?.Comment), req?.RowVersion, requireMatching: false, ct);
        return await GetAsync(id, null, ct);
    }

    /// <summary>
    /// El conteo cuadraba al revisarlo pero, ya con los saldos bloqueados, asentaría algo o tendría errores (la existencia cambió
    /// entre la revisión y el cierre). Solo la lanza el cierre en bloque (requireMatching); revierte la transacción de ese conteo.
    /// </summary>
    private sealed class CountNoLongerMatchesException() : Exception("La existencia cambió mientras se cerraba el conteo.");

    /// <summary>Reconciliación en una transacción; devuelve el estatus final (RECONCILED | RECONCILED_VARIANCE).</summary>
    private async Task<string> ReconcileCoreAsync(int id, string? comment, string? rowVersion, bool requireMatching, CancellationToken ct)
    {
        var current = await ResolveAsync(id, ct);

        return await db.RunInTransactionAsync(async ct2 =>
        {
            // 1-2. Encabezado bloqueado y re-verificación.
            var cc = await LockCountAsync(current.CycleCountId, ct2);
            if (!cc.IsActive) throw new NotFoundException(CountWhat);
            var statusCode = await StatusCodeOfAsync(cc.StatusCodeId, ct2);
            if (CycleCountStatuses.IsReconciled(statusCode)) throw new StatusRuleException(CycleCountRules.CountNotOpen);
            EnsureRowVersion(cc, rowVersion);

            // 3. Todas contadas.
            var lines = await db.Set<CycleCountLine>().Where(l => l.CycleCountId == cc.CycleCountId)
                .OrderBy(l => l.CycleCountLineId).ToListAsync(ct2);
            if (lines.Count == 0) throw new StatusRuleException(CycleCountRules.NoLines);
            var pending = CycleCountRules.PendingLines(lines.Select(l => l.CountedQty));
            if (pending > 0) throw new StatusRuleException(CycleCountRules.CountIncomplete(pending));

            // Tarea COUNT después del encabezado (orden del lote: CycleCount < WarehouseTask).
            var task = await LockOpenCountTaskAsync(cc, ct2);

            // 4. Saldos: rango por producto en ProductId ascendente (HOLDLOCK: incluye los saldos por nacer).
            var productIds = lines.Select(l => l.ProductId).Distinct().OrderBy(x => x).ToList();
            foreach (var pid in productIds) await LockProductBalancesAsync(pid, ct2);
            var balances = await CurrentBalancesAsync(cc.WarehouseId, productIds, tracked: true, ct2);

            // 5-6. Cálculo por línea contra el saldo ACTUAL (regla compartida con la vista previa); la guarda de reservado se
            // evalúa ANTES de escribir nada.
            var inputs = await LoadPlanInputsAsync(new[] { cc }, lines, ct2);
            var plan = await BuildPlanAsync(cc, lines, inputs, balances, ct2);
            if (requireMatching && (plan.BlockingError is not null || plan.ErrorLines > 0 || plan.Movements > 0))
                throw new CountNoLongerMatchesException();
            if (plan.BlockingError is not null) throw new ValidationException("lines", plan.BlockingError);
            var errors = new Dictionary<string, string[]>();
            string? firstError = null;
            for (var i = 0; i < plan.Lines.Count; i++)
            {
                if (plan.Lines[i].Error is not string msg) continue;
                errors[$"lines[{i}]"] = new[] { msg };
                firstError ??= msg;
            }
            if (errors.Count > 0) throw new ConflictException(firstError!) { Errors = errors };

            // 7. Ledger (única vía de escritura): bloquea y re-verifica; un faltante → 409 y se revierte todo.
            var all = plan.Lines.SelectMany(p => p.Postings).ToList();
            IReadOnlyList<long> txnIds = all.Count == 0 ? Array.Empty<long>() : await ledger.PostAsync(all, ct2);

            // 5 y 8. Saldo al reconciliar, marca de foto vieja y enlace al primer movimiento de la línea.
            var k = 0;
            foreach (var p in plan.Lines)
            {
                p.Line.ReconciledSystemQty = p.Current;
                p.Line.SystemQtyChanged = p.Current != p.Line.SystemQty;
                if (p.Postings.Count > 0) p.Line.AdjustmentTxnId = txnIds[k];
                k += p.Postings.Count;
            }

            // 9. Lote 14 (D7, D8): en UN paso desde Pendiente o Contado al final que corresponda: Diferencia si alguna línea
            // asentó un movimiento, Concordancia si no. Contado queda solo para el conteo a ciegas (terminar). Fecha y usuario.
            var target = CycleCountRules.ReconcileTarget(all.Count);
            var reconciled = await statuses.TransitionAsync(StatusDomains.CycleCountStatus, EntityTypes.CycleCount, cc.CycleCountId, cc.StatusCodeId,
                target, comment, ct2);
            cc.StatusCodeId = reconciled.StatusCodeId;
            cc.ReconciledAtUtc = DateTime.UtcNow;
            cc.ReconciledBy = tenant.UserId;

            // 10. Tarea COUNT → DONE.
            if (task is not null) await CompleteCountTaskAsync(task, ct2);
            await db.SaveGuardedAsync(DbExtensions.ConcurrencyMessage, ct2);
            return target;
        }, ct);
    }

    // ---------------------------------------------------------------- plan de reconciliación (compartido; Lote 21)

    private sealed record SerialPlanInfo(IReadOnlyList<string> Removals, IReadOnlyList<string> Additions, IReadOnlyList<string> Transfers);

    /// <summary>
    /// Qué asentaría una línea: existencia actual y reservada, lo contado, el ajuste neto con signo y los asientos del ledger.
    /// Pending = sin contar (no se planea; no es error). Error = el mensaje de 409 de la reconciliación para esta línea.
    /// </summary>
    private sealed record LinePlan(CycleCountLine Line, Product Product, string Tracking, decimal Current, decimal Reserved,
        decimal? Counted, bool Pending, decimal Adjustment, List<InventoryPosting> Postings, string? Error, SerialPlanInfo? Serials);

    private sealed record CountPlan(IReadOnlyList<LinePlan> Lines, string? BlockingError)
    {
        public int Pending => Lines.Count(l => l.Pending);
        public int ErrorLines => Lines.Count(l => l.Error is not null);
        public int Movements => Lines.Sum(l => l.Postings.Count);
        public int LinesWithDifference => Lines.Count(l => l.Postings.Count > 0);
        /// <summary>Se puede reconciliar ya: hay líneas, todas contadas, ninguna con error ni error del conteo entero.</summary>
        public bool CanReconcile => Lines.Count > 0 && Pending == 0 && ErrorLines == 0 && BlockingError is null;
        /// <summary>CUADRA: se puede reconciliar y no asentaría ningún movimiento (terminaría en Concordancia).</summary>
        public bool Matches => CanReconcile && Movements == 0;
    }

    /// <summary>Datos de apoyo del plan, cargados en lote (uno o varios conteos): productos, seguimiento, posiciones, usuarios.</summary>
    private sealed record PlanInputs(
        IReadOnlyDictionary<int, Product> Products,
        IReadOnlyDictionary<int, string> Tracking,
        IReadOnlyDictionary<int, (string Code, string ZoneCode, bool IsProvisional)> Bins,
        IReadOnlyDictionary<int, string> UserNames,
        IReadOnlyDictionary<int, string> LotNumbers);

    private async Task<PlanInputs> LoadPlanInputsAsync(IReadOnlyCollection<CycleCount> counts, IReadOnlyCollection<CycleCountLine> lines, CancellationToken ct)
    {
        var productIds = lines.Select(l => l.ProductId).Distinct().ToList();
        var products = await db.Set<Product>().AsNoTracking().Where(p => productIds.Contains(p.ProductId)).ToDictionaryAsync(p => p.ProductId, ct);
        var tracking = await TrackingCodesAsync(products.Values.Select(p => p.TrackingTypeLookupId), ct);
        var warehouseIds = counts.Select(c => c.WarehouseId).Distinct().ToList();
        var binIds = lines.Select(l => l.WarehouseBinId).Distinct().ToList();
        var bins = await (from b in db.Set<WarehouseBin>().AsNoTracking()
                          join z in db.Set<WarehouseZone>().AsNoTracking() on b.WarehouseZoneId equals z.WarehouseZoneId
                          where warehouseIds.Contains(b.WarehouseId) && binIds.Contains(b.WarehouseBinId)
                          select new { b.WarehouseBinId, b.Code, ZoneCode = z.Code, b.IsProvisional }).ToListAsync(ct);
        var userIds = lines.SelectMany(l => new[] { l.CapturedBy, l.CorrectedBy }).Where(u => u != null).Select(u => u!.Value).Distinct().ToList();
        var users = userIds.Count == 0
            ? new Dictionary<int, string>()
            : await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName ?? u.Email ?? string.Empty, ct);
        var lotIds = lines.Where(l => l.LotId != null).Select(l => l.LotId!.Value).Distinct().ToList();
        var lots = await LotNumbersAsync(lotIds, productIds, ct);
        return new PlanInputs(products, tracking, bins.ToDictionary(b => b.WarehouseBinId, b => (b.Code, b.ZoneCode, b.IsProvisional)), users, lots);
    }

    /// <summary>
    /// ÚNICA implementación de la regla de reconciliación (D22): por línea, ajuste = CONTADO − existencia ACTUAL (en serie,
    /// bajas, altas y traslados contra la ubicación actual) y la guarda de reservado (contado &lt; reservado → error de la
    /// línea). No escribe nada: la reconciliación real la ejecuta con los saldos bloqueados y la vista previa y el cierre en
    /// bloque con los saldos sin bloquear. Las líneas sin contar quedan Pending. Una serie capturada en dos líneas del mismo
    /// producto → BlockingError (400 al reconciliar).
    /// </summary>
    private async Task<CountPlan> BuildPlanAsync(CycleCount cc, IReadOnlyList<CycleCountLine> lines, PlanInputs inp,
        IReadOnlyDictionary<LineKey, StockBalance> balances, CancellationToken ct)
    {
        string TrackingOf(CycleCountLine l) => inp.Tracking.GetValueOrDefault(inp.Products[l.ProductId].TrackingTypeLookupId, TrackingTypes.None);
        bool IsSerial(CycleCountLine l) => TrackingOf(l) == TrackingTypes.Serial;

        // Series: capturadas por línea (una serie en dos líneas → error del conteo), esperadas HOY en cada posición y
        // ubicación actual de las contadas.
        var serialLines = lines.Where(l => l.CountedQty != null && IsSerial(l)).ToList();
        var countedByLine = serialLines.ToDictionary(l => l.CycleCountLineId, l => ParseSerials(l.CountedSerialsJson));
        // Una serie se identifica por producto: la repetición se busca entre las líneas del MISMO producto.
        string? twice = null;
        foreach (var g in serialLines.GroupBy(l => l.ProductId))
        {
            twice = CycleCountRules.FirstSerialCountedTwice(g.Select(l => (IEnumerable<string>)countedByLine[l.CycleCountLineId]));
            if (twice is not null) break;
        }
        var serialPlans = await SerialContextAsync(cc.WarehouseId, serialLines, countedByLine, ct);

        var plans = new List<LinePlan>(lines.Count);
        foreach (var line in lines)
        {
            var product = inp.Products[line.ProductId];
            var tracking = TrackingOf(line);
            var binCode = inp.Bins.TryGetValue(line.WarehouseBinId, out var bin) ? bin.Code : line.WarehouseBinId.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var balance = balances.GetValueOrDefault(new LineKey(line.ProductId, line.WarehouseBinId, line.LotId));
            var currentQty = balance?.QtyOnHand ?? 0m;
            var reserved = balance?.QtyReserved ?? 0m;
            if (line.CountedQty is not decimal counted)
            {
                plans.Add(new LinePlan(line, product, tracking, currentQty, reserved, null, true, 0m, new List<InventoryPosting>(), null, null));
                continue;
            }
            if (CycleCountRules.CountBelowReserved(counted, reserved))
            {
                var msg = CycleCountRules.ReservedAboveCount(product.Sku, binCode, counted, reserved);
                plans.Add(new LinePlan(line, product, tracking, currentQty, reserved, counted, false, 0m, new List<InventoryPosting>(), msg, null));
                continue;
            }

            var notes = NotesFor(cc, line, inp);
            var postings = new List<InventoryPosting>();
            SerialPlanInfo? serialInfo = null;
            decimal adjustment;
            if (IsSerial(line))
            {
                var ctx = serialPlans[line.CycleCountLineId];
                var v = CycleCountRules.SerialVariance(ctx.Expected, countedByLine[line.CycleCountLineId], ctx.Locations,
                    cc.WarehouseId, line.WarehouseBinId, ctx.CountedElsewhere);
                foreach (var s in v.Removals)
                    postings.Add(Adjustment(cc, line, 1m, inbound: false, line.LotId, s, notes));
                foreach (var s in v.Additions)
                    postings.Add(Adjustment(cc, line, 1m, inbound: true,
                        ctx.Locations.TryGetValue(s, out var known) && known.LotId is not null ? known.LotId : line.LotId, s, notes));
                foreach (var t in v.Transfers)
                    postings.Add(new InventoryPosting(InventoryTxnTypes.Transfer, line.ProductId, 1m,
                        LotId: t.LotId, SerialNumber: t.SerialNumber,
                        FromWarehouseId: t.FromWarehouseId, FromBinId: t.FromBinId,
                        ToWarehouseId: cc.WarehouseId, ToBinId: line.WarehouseBinId,
                        RefEntityType: EntityTypes.CycleCount, RefId: cc.CycleCountId, Notes: notes));
                adjustment = v.Additions.Count + v.Transfers.Count - v.Removals.Count;
                serialInfo = new SerialPlanInfo(v.Removals, v.Additions, v.Transfers.Select(t => t.SerialNumber).ToList());
            }
            else
            {
                adjustment = CycleCountRules.Adjustment(counted, currentQty);
                if (adjustment != 0m)
                    postings.Add(Adjustment(cc, line, Math.Abs(adjustment), inbound: adjustment > 0m, line.LotId, null, notes));
            }
            plans.Add(new LinePlan(line, product, tracking, currentQty, reserved, counted, false, adjustment, postings, null, serialInfo));
        }
        return new CountPlan(plans, twice is null ? null : CycleCountRules.SerialCountedTwice(twice));
    }

    /// <summary>Saldos actuales (sin bloquear) por almacén, para planear varios conteos de una vez.</summary>
    private async Task<Dictionary<int, IReadOnlyDictionary<LineKey, StockBalance>>> BalancesByWarehouseAsync(
        IReadOnlyCollection<CycleCount> counts, IReadOnlyCollection<CycleCountLine> lines, CancellationToken ct)
    {
        var result = new Dictionary<int, IReadOnlyDictionary<LineKey, StockBalance>>();
        var countWarehouse = counts.ToDictionary(c => c.CycleCountId, c => c.WarehouseId);
        foreach (var g in lines.GroupBy(l => countWarehouse[l.CycleCountId]))
            result[g.Key] = await CurrentBalancesAsync(g.Key, g.Select(l => l.ProductId), tracked: false, ct);
        return result;
    }

    /// <summary>
    /// Vista previa de reconciliar (warehouse.count): lo que haría ReconcileAsync AHORA, sin escribir ni bloquear. Por línea:
    /// existencia actual, reservado, contado (con su evidencia), ajuste, saldo resultante y el error que daría; totales y
    /// pendientes (dato, no error). Conteo ya reconciliado → 422 'El conteo ya fue reconciliado; solo se consulta.'.
    /// </summary>
    public async Task<ReconcilePreviewDto> PreviewReconcileAsync(int id, CancellationToken ct)
    {
        var cc = await ResolveAsync(id, ct);
        var statusCode = await StatusCodeOfAsync(cc.StatusCodeId, ct);
        if (CycleCountStatuses.IsReconciled(statusCode)) throw new StatusRuleException(CycleCountRules.CountNotOpen);

        var lines = await db.Set<CycleCountLine>().AsNoTracking().Where(l => l.CycleCountId == cc.CycleCountId)
            .OrderBy(l => l.CycleCountLineId).ToListAsync(ct);
        var header = (await MapHeadersAsync(new[] { cc }, ct))[0];
        var rowVersion = Convert.ToBase64String(cc.RowVersion ?? Array.Empty<byte>());
        if (lines.Count == 0)
            return new ReconcilePreviewDto(header, Array.Empty<ReconcilePreviewLineDto>(), new ReconcilePreviewTotalsDto(0, 0, 0, 0, 0, false, null), null, rowVersion);

        var inputs = await LoadPlanInputsAsync(new[] { cc }, lines, ct);
        var balances = await CurrentBalancesAsync(cc.WarehouseId, lines.Select(l => l.ProductId), tracked: false, ct);
        var plan = await BuildPlanAsync(cc, lines, inputs, balances, ct);
        var dtos = plan.Lines.Select(p => PreviewLine(p, inputs)).ToList();
        var totals = new ReconcilePreviewTotalsDto(plan.Lines.Count, plan.Pending, plan.LinesWithDifference, plan.Movements, plan.ErrorLines,
            plan.Matches, plan.CanReconcile ? CycleCountRules.ReconcileTarget(plan.Movements) : null);
        return new ReconcilePreviewDto(header, dtos, totals, plan.BlockingError, rowVersion);
    }

    private ReconcilePreviewLineDto PreviewLine(LinePlan p, PlanInputs inp)
    {
        var l = p.Line;
        var bin = inp.Bins.GetValueOrDefault(l.WarehouseBinId);
        return new ReconcilePreviewLineDto(l.CycleCountLineId, l.WarehouseBinId, bin.Code ?? string.Empty, bin.ZoneCode ?? string.Empty, bin.IsProvisional,
            p.Product.PublicId, p.Product.Sku, p.Product.Name, p.Tracking, l.LotId, l.LotId is int lid ? inp.LotNumbers.GetValueOrDefault(lid) : null,
            l.SystemQty, p.Current, p.Reserved, p.Counted, p.Pending,
            l.CapturedQty, NameOf(inp, l.CapturedBy), Utc(l.CapturedAtUtc), NameOf(inp, l.CorrectedBy), Utc(l.CorrectedAtUtc), l.CorrectedAtUtc != null,
            p.Adjustment, p.Current + p.Adjustment, p.Postings.Count, p.Current != l.SystemQty, p.Error,
            p.Serials is null ? null : new ReconcilePreviewSerialsDto(p.Serials.Removals, p.Serials.Additions, p.Serials.Transfers));
    }

    private static string? NameOf(PlanInputs inp, int? userId) => userId is int u ? inp.UserNames.GetValueOrDefault(u) : null;

    private static DateTime? Utc(DateTime? value) => value is DateTime v ? DateTime.SpecifyKind(v, DateTimeKind.Utc) : null;

    // ---------------------------------------------------------------- lista "Por revisar" (Lote 21)

    /// <summary>
    /// Lista "Por revisar" (warehouse.count): conteos ya contados (Contado; con IncludeOpen también los Pendientes con todas sus
    /// líneas capturadas), más recientes primero, con quién contó, el primer producto (y cuántos más), posiciones, líneas,
    /// cuántas difieren y si cuadra. Se calcula con el MISMO plan que la vista previa y en lotes: las líneas, los saldos, los
    /// productos y los usuarios de TODA la página salen de una consulta cada uno (sin N+1 por conteo).
    /// </summary>
    public async Task<CycleCountReviewPageDto> ReviewAsync(CycleCountReviewQuery? q, CancellationToken ct)
    {
        q ??= new CycleCountReviewQuery();
        var skip = Math.Max(0, q.Skip);
        var take = Math.Clamp(q.Take <= 0 ? 50 : q.Take, 1, CycleCountRules.MaxListRows);
        var statusCodes = q.IncludeOpen ? new[] { CycleCountStatuses.Counted, CycleCountStatuses.Open } : new[] { CycleCountStatuses.Counted };
        var query = await BuildListQueryAsync(new CycleCountQuery(
            WarehousePublicIds: q.WarehousePublicId is Guid w ? new[] { w } : null, Status: statusCodes, Search: q.Search), ct);

        // Un Pendiente solo es "por revisar" si ya tiene todas sus líneas capturadas.
        var openId = await db.StatusCodes.AsNoTracking()
            .Where(s => s.Entity == StatusDomains.CycleCountStatus && s.InternalCode == CycleCountStatuses.Open)
            .Select(s => (int?)s.StatusCodeId).FirstOrDefaultAsync(ct);
        var lineSet = db.Set<CycleCountLine>().AsNoTracking();
        query = query.Where(c => c.StatusCodeId != openId
                                 || (lineSet.Any(l => l.CycleCountId == c.CycleCountId) && !lineSet.Any(l => l.CycleCountId == c.CycleCountId && l.CountedQty == null)));
        if (q.CountedByUserId is int by) query = query.Where(c => lineSet.Any(l => l.CycleCountId == c.CycleCountId && l.CapturedBy == by));

        var total = await query.CountAsync(ct);
        var page = total == 0 ? new List<CycleCount>() : await query.OrderByDescending(c => c.CreatedAtUtc).ThenByDescending(c => c.CycleCountId)
            .Skip(skip).Take(take).ToListAsync(ct);
        if (page.Count == 0) return new CycleCountReviewPageDto(total, skip, take, Array.Empty<CycleCountReviewItemDto>());

        var headers = await MapHeadersAsync(page, ct);
        var countIds = page.Select(c => c.CycleCountId).ToList();
        var lines = await db.Set<CycleCountLine>().AsNoTracking().Where(l => countIds.Contains(l.CycleCountId)).OrderBy(l => l.CycleCountLineId).ToListAsync(ct);
        var inputs = await LoadPlanInputsAsync(page, lines, ct);
        var balances = await BalancesByWarehouseAsync(page, lines, ct);
        var linesByCount = lines.ToLookup(l => l.CycleCountId);

        var items = new List<CycleCountReviewItemDto>(page.Count);
        for (var i = 0; i < page.Count; i++)
        {
            var cc = page[i];
            var own = linesByCount[cc.CycleCountId].ToList();
            var plan = own.Count == 0
                ? new CountPlan(Array.Empty<LinePlan>(), null)
                : await BuildPlanAsync(cc, own, inputs, balances.GetValueOrDefault(cc.WarehouseId) ?? new Dictionary<LineKey, StockBalance>(), ct);
            var capturers = own.Where(l => l.CapturedBy != null).GroupBy(l => l.CapturedBy!.Value)
                .OrderByDescending(g => g.Count()).ThenBy(g => g.Key).ToList();
            var firstProduct = own.Select(l => inputs.Products[l.ProductId]).OrderBy(p => p.Sku, StringComparer.Ordinal).ThenBy(p => p.ProductId).FirstOrDefault();
            var distinctProducts = own.Select(l => l.ProductId).Distinct().Count();
            items.Add(new CycleCountReviewItemDto(headers[i],
                capturers.Count > 0 ? capturers[0].Key : null, capturers.Count > 0 ? inputs.UserNames.GetValueOrDefault(capturers[0].Key) : null, capturers.Count,
                firstProduct?.PublicId, firstProduct?.Sku, firstProduct?.Name, Math.Max(0, distinctProducts - 1),
                own.Select(l => l.WarehouseBinId).Distinct().Count(), own.Count, plan.Pending, plan.LinesWithDifference, plan.ErrorLines,
                plan.Movements, own.Count(l => l.CorrectedAtUtc != null), plan.Matches));
        }
        return new CycleCountReviewPageDto(total, skip, take, items);
    }

    // ---------------------------------------------------------------- cierre en bloque de los que cuadran (Lote 21)

    public const string SkipWouldPost = "WouldPost";
    public const string SkipErrors = "Errors";
    public const string SkipPending = "Pending";
    public const string SkipStale = "Stale";
    public const string SkipNotCounted = "NotCounted";
    public const string SkipAlreadyReconciled = "AlreadyReconciled";
    public const string SkipNotFound = "NotFound";
    public const string SkipNoLines = "NoLines";
    public const string SkipFailed = "Failed";

    /// <summary>
    /// Cierra en bloque los conteos que CUADRAN (warehouse.count). Candidatos: los indicados en Ids o, sin ellos, los Contados del
    /// almacén (o de todos) —con IncludeOpen también los Pendientes con todas sus líneas capturadas—, hasta 200. Cada uno se
    /// evalúa con el MISMO plan que la reconciliación contra la existencia ACTUAL (no la foto); solo los que no asentarían
    /// ningún movimiento y no tienen errores se reconcilian (terminan en Concordancia), cada uno en su PROPIA transacción y
    /// volviendo a comprobar con los saldos bloqueados (si la existencia cambió en medio → Stale). Los demás se informan con el
    /// motivo (WouldPost n, Errors n, Pending n, Stale, …) y quedan para revisar. Un fallo de uno no afecta a los demás.
    /// </summary>
    public async Task<CycleCountReconcileMatchingResultDto> ReconcileMatchingAsync(CountReconcileMatchingRequest? req, CancellationToken ct)
    {
        req ??= new CountReconcileMatchingRequest();
        var comment = TrimComment(req.Comment);
        var explicitIds = req.Ids is { Length: > 0 } ? req.Ids.Distinct().ToList() : null;
        if (explicitIds is { Count: > CycleCountRules.MaxBulkCounts }) throw new ValidationException("ids", CycleCountRules.BulkTooMany);

        var query = db.Set<CycleCount>().AsNoTracking().Where(c => c.IsActive);
        if (req.WarehousePublicId is Guid wp)
        {
            var warehouse = await ResolveWarehouseOrDefaultAsync(wp, ct);
            query = query.Where(c => c.WarehouseId == warehouse.WarehouseId);
        }
        var skipped = new List<CycleCountSkippedItemDto>();
        var truncated = false;
        List<CycleCount> candidates;
        if (explicitIds is not null)
        {
            candidates = await query.Where(c => explicitIds.Contains(c.CycleCountId)).OrderBy(c => c.CycleCountId).ToListAsync(ct);
            foreach (var missing in explicitIds.Except(candidates.Select(c => c.CycleCountId)).OrderBy(x => x))
                skipped.Add(new CycleCountSkippedItemDto(missing, null, SkipNotFound, "El conteo no existe."));
        }
        else
        {
            var codes = req.IncludeOpen ? new[] { CycleCountStatuses.Counted, CycleCountStatuses.Open } : new[] { CycleCountStatuses.Counted };
            var ids = await db.StatusCodes.AsNoTracking().Where(s => s.Entity == StatusDomains.CycleCountStatus && codes.Contains(s.InternalCode))
                .Select(s => s.StatusCodeId).ToListAsync(ct);
            var openId = await db.StatusCodes.AsNoTracking()
                .Where(s => s.Entity == StatusDomains.CycleCountStatus && s.InternalCode == CycleCountStatuses.Open)
                .Select(s => (int?)s.StatusCodeId).FirstOrDefaultAsync(ct);
            var lineSet = db.Set<CycleCountLine>().AsNoTracking();
            candidates = await query.Where(c => ids.Contains(c.StatusCodeId)
                                                && (c.StatusCodeId != openId
                                                    || (lineSet.Any(l => l.CycleCountId == c.CycleCountId) && !lineSet.Any(l => l.CycleCountId == c.CycleCountId && l.CountedQty == null))))
                .OrderBy(c => c.CycleCountId).Take(CycleCountRules.MaxBulkCounts + 1).ToListAsync(ct);
            if (candidates.Count > CycleCountRules.MaxBulkCounts) { truncated = true; candidates.RemoveAt(candidates.Count - 1); }
        }

        var statusMap = await StatusMapAsync(StatusDomains.CycleCountStatus, ct);
        var countIds = candidates.Select(c => c.CycleCountId).ToList();
        var lines = await db.Set<CycleCountLine>().AsNoTracking().Where(l => countIds.Contains(l.CycleCountId)).OrderBy(l => l.CycleCountLineId).ToListAsync(ct);
        var evaluable = candidates.Where(c =>
        {
            var code = statusMap.GetValueOrDefault(c.StatusCodeId).Code;
            return !CycleCountStatuses.IsReconciled(code);
        }).ToList();
        var evalLines = lines.Where(l => evaluable.Any(c => c.CycleCountId == l.CycleCountId)).ToList();
        var inputs = evalLines.Count == 0 ? null : await LoadPlanInputsAsync(evaluable, evalLines, ct);
        var balances = evalLines.Count == 0 ? new Dictionary<int, IReadOnlyDictionary<LineKey, StockBalance>>() : await BalancesByWarehouseAsync(evaluable, evalLines, ct);
        var linesByCount = lines.ToLookup(l => l.CycleCountId);

        var closed = new List<CycleCountClosedItemDto>();
        foreach (var cc in candidates)
        {
            var code = statusMap.GetValueOrDefault(cc.StatusCodeId).Code;
            if (CycleCountStatuses.IsReconciled(code))
            {
                skipped.Add(new CycleCountSkippedItemDto(cc.CycleCountId, cc.Number, SkipAlreadyReconciled, CycleCountRules.CountNotOpen));
                continue;
            }
            if (code == CycleCountStatuses.Open && !req.IncludeOpen)
            {
                skipped.Add(new CycleCountSkippedItemDto(cc.CycleCountId, cc.Number, SkipNotCounted, "El conteo todavía no se termina de contar."));
                continue;
            }
            var own = linesByCount[cc.CycleCountId].ToList();
            if (own.Count == 0)
            {
                skipped.Add(new CycleCountSkippedItemDto(cc.CycleCountId, cc.Number, SkipNoLines, CycleCountRules.NoLines));
                continue;
            }
            var plan = await BuildPlanAsync(cc, own, inputs!, balances.GetValueOrDefault(cc.WarehouseId) ?? new Dictionary<LineKey, StockBalance>(), ct);
            if (plan.Pending > 0) { skipped.Add(new CycleCountSkippedItemDto(cc.CycleCountId, cc.Number, SkipPending, CycleCountRules.CountIncomplete(plan.Pending), plan.Pending)); continue; }
            if (plan.BlockingError is not null) { skipped.Add(new CycleCountSkippedItemDto(cc.CycleCountId, cc.Number, SkipErrors, plan.BlockingError, 1)); continue; }
            if (plan.ErrorLines > 0)
            {
                skipped.Add(new CycleCountSkippedItemDto(cc.CycleCountId, cc.Number, SkipErrors, plan.Lines.First(l => l.Error is not null).Error!, plan.ErrorLines));
                continue;
            }
            if (plan.Movements > 0)
            {
                skipped.Add(new CycleCountSkippedItemDto(cc.CycleCountId, cc.Number, SkipWouldPost,
                    $"Asentaría {plan.Movements.ToString(System.Globalization.CultureInfo.InvariantCulture)} movimiento(s); revíselo.", plan.Movements));
                continue;
            }

            // Cuadra: se cierra en su propia transacción, comprobando de nuevo con los saldos bloqueados.
            try
            {
                var final = await ReconcileCoreAsync(cc.CycleCountId, comment, null, requireMatching: true, ct);
                closed.Add(new CycleCountClosedItemDto(cc.CycleCountId, cc.Number, final, own.Count));
            }
            catch (CountNoLongerMatchesException)
            {
                skipped.Add(new CycleCountSkippedItemDto(cc.CycleCountId, cc.Number, SkipStale, "La existencia cambió mientras se cerraba; revíselo."));
            }
            catch (Exception ex) when (ex is StatusRuleException or ConflictException or NotFoundException or ValidationException)
            {
                skipped.Add(new CycleCountSkippedItemDto(cc.CycleCountId, cc.Number, SkipFailed, ex.Message));
            }
            finally
            {
                db.ChangeTracker.Clear();
            }
        }
        return new CycleCountReconcileMatchingResultDto(candidates.Count + skipped.Count(s => s.ReasonCode == SkipNotFound), closed,
            skipped.OrderBy(s => s.Id).ToList(), truncated);
    }

    // ================================================================ baja

    /// <summary>Elimina (IsActive = 0) un conteo OPEN y cancela su tarea COUNT. COUNTED → 422 DeleteOnlyOpen; RECONCILED → 422 CountNotOpen.</summary>
    public async Task DeleteAsync(int id, CancellationToken ct)
    {
        var current = await ResolveAsync(id, ct);
        await db.RunInTransactionAsync(async ct2 =>
        {
            var (cc, code) = await LockEditableAsync(current.CycleCountId, ct2);
            if (code != CycleCountStatuses.Open) throw new StatusRuleException(CycleCountRules.DeleteOnlyOpen);
            var task = await LockOpenCountTaskAsync(cc, ct2);
            cc.IsActive = false;
            if (task is not null)
            {
                var to = await statuses.TransitionAsync(StatusDomains.WarehouseTaskStatus, EntityTypes.WarehouseTask, task.WarehouseTaskId,
                    task.StatusCodeId, WarehouseTaskStatuses.Cancelled, $"Conteo {cc.Number} eliminado.", ct2);
                task.StatusCodeId = to.StatusCodeId;
                // Como WarehouseTaskWriter.CancelAsync: la cancelación cierra la tarea (AgeHours y sync/warehouse-tasks por CompletedAtUtc).
                task.CompletedAtUtc = DateTime.UtcNow;
            }
            await db.SaveGuardedAsync(DbExtensions.ConcurrencyMessage, ct2);
        }, ct);
    }

    // ================================================================ lectura: armado de la ficha

    private async Task<CycleCountDetailDto> BuildDetailAsync(CycleCount cc, CycleCountLinesQuery? q, CancellationToken ct)
    {
        var warehouse = await db.Set<Warehouse>().AsNoTracking().FirstAsync(w => w.WarehouseId == cc.WarehouseId, ct);
        var statusMap = await StatusMapAsync(StatusDomains.CycleCountStatus, ct);
        var status = statusMap.GetValueOrDefault(cc.StatusCodeId);
        var reconciled = CycleCountStatuses.IsReconciled(status.Code);

        var lines = await db.Set<CycleCountLine>().AsNoTracking().Where(l => l.CycleCountId == cc.CycleCountId)
            .OrderBy(l => l.CycleCountLineId).ToListAsync(ct);
        // Lote 14: el encabezado de la ficha es el mismo de la lista (origen, posición y zona, tarea COUNT con su asignado).
        var header = (await MapHeadersAsync(new[] { cc }, ct))[0] with
        {
            WarehousePublicId = warehouse.PublicId, WarehouseCode = warehouse.Code,
            LineCount = lines.Count, CountedLines = lines.Count(l => l.CountedQty != null),
            VarianceLines = lines.Count(l => l.CountedQty is decimal c && c != l.SystemQty),
            NetVariance = lines.Sum(l => CycleCountRules.Variance(l.CountedQty, l.SystemQty) ?? 0m),
            CorrectedLines = lines.Count(l => l.CorrectedAtUtc != null),
        };
        var rowVersion = Convert.ToBase64String(cc.RowVersion ?? Array.Empty<byte>());
        if (lines.Count == 0) return new CycleCountDetailDto(header, Array.Empty<CycleCountLineDto>(), rowVersion);

        var productIds = lines.Select(l => l.ProductId).Distinct().ToList();
        var binIds = lines.Select(l => l.WarehouseBinId).Distinct().ToList();
        var bins = await (from b in db.Set<WarehouseBin>().AsNoTracking()
                          join z in db.Set<WarehouseZone>().AsNoTracking() on b.WarehouseZoneId equals z.WarehouseZoneId
                          where b.WarehouseId == cc.WarehouseId && binIds.Contains(b.WarehouseBinId)
                          select new { b.WarehouseBinId, b.Code, ZoneCode = z.Code, b.IsProvisional }).ToDictionaryAsync(b => b.WarehouseBinId, ct);
        var userIds = lines.SelectMany(l => new[] { l.CapturedBy, l.CorrectedBy }).Where(u => u != null).Select(u => u!.Value).Distinct().ToList();
        var userNames = userIds.Count == 0
            ? new Dictionary<int, string>()
            : await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName ?? u.Email ?? string.Empty, ct);
        var products = await db.Set<Product>().AsNoTracking().Where(p => productIds.Contains(p.ProductId))
            .Select(p => new { p.ProductId, p.PublicId, p.Sku, p.Name, p.ProductCategoryId, p.TrackingTypeLookupId, p.Barcode })
            .ToDictionaryAsync(p => p.ProductId, ct);
        var categoryIds = products.Values.Where(p => p.ProductCategoryId != null).Select(p => p.ProductCategoryId!.Value).Distinct().ToList();
        var categories = await db.Set<ProductCategory>().AsNoTracking().Where(c => categoryIds.Contains(c.ProductCategoryId))
            .ToDictionaryAsync(c => c.ProductCategoryId, c => c.Name, ct);
        var tracking = await TrackingCodesAsync(products.Values.Select(p => p.TrackingTypeLookupId), ct);
        var lotIds = lines.Where(l => l.LotId != null).Select(l => l.LotId!.Value).Distinct().ToList();
        var lots = await LotNumbersAsync(lotIds, productIds, ct);
        var balances = await CurrentBalancesAsync(cc.WarehouseId, productIds, tracked: false, ct);

        // Series esperadas: las que HOY están en inventario en cada posición (producto y lote de la línea).
        var serialProductIds = products.Values
            .Where(p => tracking.GetValueOrDefault(p.TrackingTypeLookupId, TrackingTypes.None) == TrackingTypes.Serial)
            .Select(p => p.ProductId).ToList();
        var expectedSerials = new Dictionary<LineKey, List<string>>();
        if (serialProductIds.Count > 0)
        {
            var inStockIds = (await SerialStatusIdsAsync(ct)).Where(kv => CycleCountRules.IsInStock(kv.Value)).Select(kv => kv.Key).ToList();
            var serials = await db.Set<InventorySerial>().AsNoTracking()
                .Where(s => serialProductIds.Contains(s.ProductId) && cc.WarehouseId == s.CurrentWarehouseId && s.CurrentBinId != null
                            && s.StatusCodeId != null && inStockIds.Contains(s.StatusCodeId.Value))
                .Select(s => new { s.ProductId, s.CurrentBinId, s.LotId, s.SerialNumber }).ToListAsync(ct);
            foreach (var g in serials.GroupBy(s => new LineKey(s.ProductId, s.CurrentBinId!.Value, s.LotId)))
                expectedSerials[g.Key] = g.Select(s => s.SerialNumber).OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList();
        }

        // Filtros de la ficha (en memoria: a lo sumo 1000 líneas).
        HashSet<int>? filterBins = q?.BinIds is { Length: > 0 } ? q.BinIds.ToHashSet() : null;
        HashSet<Guid>? filterProducts = q?.ProductPublicIds is { Length: > 0 } ? q.ProductPublicIds.ToHashSet() : null;
        HashSet<int>? filterCategories = q?.CategoryIds is { Length: > 0 } ? await ExpandCategoriesAsync(q.CategoryIds, ct) : null;
        var search = string.IsNullOrWhiteSpace(q?.Search) ? null : q!.Search!.Trim();

        var dtos = new List<CycleCountLineDto>();
        foreach (var l in lines)
        {
            var p = products[l.ProductId];
            var bin = bins.GetValueOrDefault(l.WarehouseBinId);
            var key = new LineKey(l.ProductId, l.WarehouseBinId, l.LotId);
            var currentQty = balances.GetValueOrDefault(key)?.QtyOnHand ?? 0m;
            var lotNumber = l.LotId is int lid ? lots.GetValueOrDefault(lid) : null;
            var variance = CycleCountRules.Variance(l.CountedQty, l.SystemQty);

            if (filterBins is not null && !filterBins.Contains(l.WarehouseBinId)) continue;
            if (filterProducts is not null && !filterProducts.Contains(p.PublicId)) continue;
            if (filterCategories is not null && (p.ProductCategoryId is not int cat || !filterCategories.Contains(cat))) continue;
            if (q?.OnlyPending == true && l.CountedQty is not null) continue;
            if (q?.OnlyVariance == true && (variance ?? 0m) == 0m && !(l.ReconciledSystemQty is decimal r0 && l.CountedQty is decimal c0 && c0 != r0)) continue;
            if (search is not null && !(Contains(p.Sku, search) || Contains(p.Name, search) || Contains(bin?.Code, search) || Contains(lotNumber, search)))
                continue;

            dtos.Add(new CycleCountLineDto(
                l.CycleCountLineId, l.WarehouseBinId, bin?.Code ?? string.Empty, bin?.ZoneCode ?? string.Empty,
                p.PublicId, p.Sku, p.Name, p.ProductCategoryId is int c ? categories.GetValueOrDefault(c) : null,
                tracking.GetValueOrDefault(p.TrackingTypeLookupId, TrackingTypes.None), l.LotId, lotNumber,
                l.SystemQty, l.CountedQty, variance,
                expectedSerials.TryGetValue(key, out var exp) ? exp : Array.Empty<string>(),
                ParseSerials(l.CountedSerialsJson),
                !reconciled && CycleCountRules.IsStale(l.SystemQty, currentQty), currentQty,
                l.ReconciledSystemQty, l.SystemQtyChanged,
                l.ReconciledSystemQty is decimal rs && l.CountedQty is decimal cq ? cq - rs : null,
                l.AdjustmentTxnId, p.Barcode,
                l.CapturedQty, l.CapturedBy is int cb ? userNames.GetValueOrDefault(cb) : null, l.CapturedBy, Utc(l.CapturedAtUtc),
                l.CorrectedBy is int xb ? userNames.GetValueOrDefault(xb) : null, l.CorrectedBy, Utc(l.CorrectedAtUtc), l.CorrectedAtUtc != null,
                bin?.IsProvisional ?? false));
        }
        return new CycleCountDetailDto(header, dtos, rowVersion);
    }

    // ================================================================ apoyo

    private async Task<CycleCount> ResolveAsync(int id, CancellationToken ct)
        => await db.Set<CycleCount>().AsNoTracking().FirstOrDefaultAsync(c => c.CycleCountId == id && c.IsActive, ct)
           ?? throw new NotFoundException(CountWhat);

    /// <summary>Encabezado bloqueado, activo (404 al segundo de dos borrados) y no reconciliado (422 CountNotOpen).</summary>
    private async Task<(CycleCount Count, string StatusCode)> LockEditableAsync(int cycleCountId, CancellationToken ct)
    {
        var cc = await LockCountAsync(cycleCountId, ct);
        if (!cc.IsActive) throw new NotFoundException(CountWhat);
        var code = await StatusCodeOfAsync(cc.StatusCodeId, ct);
        if (CycleCountStatuses.IsReconciled(code)) throw new StatusRuleException(CycleCountRules.CountNotOpen);
        return (cc, code);
    }

    private async Task<string> StatusCodeOfAsync(int statusCodeId, CancellationToken ct)
        => await db.StatusCodes.AsNoTracking().Where(s => s.StatusCodeId == statusCodeId).Select(s => s.InternalCode).FirstOrDefaultAsync(ct)
           ?? string.Empty;

    /// <summary>Control optimista opcional con el RowVersion de la ficha: si el encabezado cambió → 409.</summary>
    private static void EnsureRowVersion(CycleCount cc, string? rowVersionBase64)
    {
        if (string.IsNullOrWhiteSpace(rowVersionBase64) || cc.RowVersion is not { Length: > 0 } currentVersion) return;
        byte[] bytes;
        try { bytes = Convert.FromBase64String(rowVersionBase64.Trim()); }
        catch (FormatException) { throw new ValidationException("rowVersion", "rowVersion inválido: se espera el valor base64 devuelto por la ficha."); }
        if (bytes.Length > 0 && !bytes.AsSpan().SequenceEqual(currentVersion)) throw new ConflictException(DbExtensions.ConcurrencyMessage);
    }

    private static string? TrimComment(string? comment) => string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();

    /// <summary>
    /// Motivo del movimiento de una línea: 'Conteo CC-00001' y, si la cantidad fue corregida, la evidencia (quién contó, qué contó
    /// y quién la corrigió, con fechas en la hora de la compañía). Ver CycleCountRules.LedgerNotes.
    /// </summary>
    private string NotesFor(CycleCount cc, CycleCountLine line, PlanInputs inp)
    {
        string? Local(DateTime? utc) => utc is DateTime v
            ? TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(v, DateTimeKind.Utc), clock.Zone).ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture)
            : null;
        var state = new CycleCountRules.CaptureState(line.CountedQty, line.CountedSerialsJson, line.CapturedQty, line.CapturedSerialsJson,
            line.CapturedBy, line.CapturedAtUtc, line.CorrectedBy, line.CorrectedAtUtc);
        return CycleCountRules.LedgerNotes(cc.Number, state, NameOf(inp, line.CapturedBy), Local(line.CapturedAtUtc),
            NameOf(inp, line.CorrectedBy), Local(line.CorrectedAtUtc));
    }

    private static InventoryPosting Adjustment(CycleCount cc, CycleCountLine line, decimal magnitude, bool inbound, int? lotId, string? serialNumber, string notes)
        => new(InventoryTxnTypes.Adjustment, line.ProductId, magnitude,
            LotId: lotId, SerialNumber: serialNumber,
            FromWarehouseId: inbound ? null : cc.WarehouseId, FromBinId: inbound ? null : line.WarehouseBinId,
            ToWarehouseId: inbound ? cc.WarehouseId : null, ToBinId: inbound ? line.WarehouseBinId : null,
            RefEntityType: EntityTypes.CycleCount, RefId: cc.CycleCountId, ReasonCode: AdjustmentReasons.CountVariance, Notes: notes);

    private static bool Contains(string? value, string search) => value is not null && value.Contains(search, StringComparison.OrdinalIgnoreCase);

    private static string[] Codes(IEnumerable<string> raw)
        => raw.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim().ToUpperInvariant()).Distinct().ToArray();

    private static IReadOnlyList<string> ParseSerials(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<string>();
        try { return JsonSerializer.Deserialize<List<string>>(json, Json) ?? new List<string>(); }
        catch (JsonException) { return Array.Empty<string>(); }
    }

    /// <summary>Saldos del almacén de esos productos por (producto, posición, lote); con tracked = true, las filas ya bloqueadas.</summary>
    private async Task<Dictionary<LineKey, StockBalance>> CurrentBalancesAsync(int warehouseId, IEnumerable<int> productIds, bool tracked, CancellationToken ct)
    {
        var ids = productIds.Distinct().ToList();
        var query = db.Set<StockBalance>().Where(b => b.WarehouseId == warehouseId && ids.Contains(b.ProductId) && b.WarehouseBinId != null);
        if (!tracked) query = query.AsNoTracking();
        var rows = await query.ToListAsync(ct);
        var result = new Dictionary<LineKey, StockBalance>();
        foreach (var b in rows) result[new LineKey(b.ProductId, b.WarehouseBinId!.Value, b.LotId)] = b;
        return result;
    }

    private sealed record SerialLineContext(
        IReadOnlyList<string> Expected,
        IReadOnlyDictionary<string, SerialLocation> Locations,
        IReadOnlySet<string> CountedElsewhere);

    /// <summary>
    /// Por línea con serie: series esperadas HOY en su posición (en inventario), ubicación actual de las contadas que el
    /// sistema conoce, y las contadas en OTRAS líneas del mismo producto (no se dan de baja: la otra línea las transfiere).
    /// </summary>
    private async Task<Dictionary<int, SerialLineContext>> SerialContextAsync(int warehouseId, IReadOnlyList<CycleCountLine> serialLines,
        IReadOnlyDictionary<int, IReadOnlyList<string>> countedByLine, CancellationToken ct)
    {
        var result = new Dictionary<int, SerialLineContext>();
        if (serialLines.Count == 0) return result;
        var productIds = serialLines.Select(l => l.ProductId).Distinct().ToList();
        var statusById = await SerialStatusIdsAsync(ct);
        var inStockIds = statusById.Where(kv => CycleCountRules.IsInStock(kv.Value)).Select(kv => kv.Key).ToList();

        var inBins = await db.Set<InventorySerial>().AsNoTracking()
            .Where(s => productIds.Contains(s.ProductId) && warehouseId == s.CurrentWarehouseId && s.CurrentBinId != null
                        && s.StatusCodeId != null && inStockIds.Contains(s.StatusCodeId.Value))
            .Select(s => new { s.ProductId, s.CurrentBinId, s.LotId, s.SerialNumber }).ToListAsync(ct);

        var countedNumbers = serialLines.SelectMany(l => countedByLine[l.CycleCountLineId]).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var known = countedNumbers.Count == 0
            ? new List<InventorySerial>()
            : await db.Set<InventorySerial>().AsNoTracking()
                .Where(s => productIds.Contains(s.ProductId) && countedNumbers.Contains(s.SerialNumber)).ToListAsync(ct);

        foreach (var line in serialLines)
        {
            var expected = inBins.Where(s => s.ProductId == line.ProductId && line.WarehouseBinId == s.CurrentBinId && s.LotId == line.LotId)
                .Select(s => s.SerialNumber).ToList();
            var locations = new Dictionary<string, SerialLocation>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in known.Where(s => s.ProductId == line.ProductId))
                locations[s.SerialNumber] = new SerialLocation(s.SerialNumber,
                    s.StatusCodeId is int sid ? statusById.GetValueOrDefault(sid) : null, s.CurrentWarehouseId, s.CurrentBinId, s.LotId);
            var elsewhere = new HashSet<string>(
                serialLines.Where(o => o.ProductId == line.ProductId && o.CycleCountLineId != line.CycleCountLineId)
                    .SelectMany(o => countedByLine[o.CycleCountLineId]),
                StringComparer.OrdinalIgnoreCase);
            result[line.CycleCountLineId] = new SerialLineContext(expected, locations, elsewhere);
        }
        return result;
    }

    /// <summary>Código interno por StatusCodeId del dominio SerialStatus.</summary>
    private async Task<Dictionary<int, string>> SerialStatusIdsAsync(CancellationToken ct)
        => await db.StatusCodes.AsNoTracking().Where(s => s.Entity == StatusDomains.SerialStatus)
            .ToDictionaryAsync(s => s.StatusCodeId, s => s.InternalCode, ct);

    private async Task<Dictionary<int, string>> TrackingCodesAsync(IEnumerable<int> lookupIds, CancellationToken ct)
    {
        var result = new Dictionary<int, string>();
        foreach (var id in lookupIds.Distinct()) result[id] = await TrackingOfAsync(id, ct);
        return result;
    }

    private async Task<string> TrackingOfAsync(int trackingLookupId, CancellationToken ct)
        => (await lookups.GetAsync(trackingLookupId, ct))?.InternalCode ?? TrackingTypes.None;

    /// <summary>Número de lote por id, SIEMPRE bajo productos del tenant (InventoryLot no tiene TenantId).</summary>
    private async Task<Dictionary<int, string>> LotNumbersAsync(IReadOnlyCollection<int> lotIds, IEnumerable<int> productIds, CancellationToken ct)
    {
        if (lotIds.Count == 0) return new Dictionary<int, string>();
        var pids = productIds.Distinct().ToList();
        return await (from l in db.Set<InventoryLot>().AsNoTracking()
                      join p in db.Set<Product>().AsNoTracking() on l.ProductId equals p.ProductId
                      where lotIds.Contains(l.LotId) && pids.Contains(l.ProductId)
                      select new { l.LotId, l.LotNumber }).ToDictionaryAsync(x => x.LotId, x => x.LotNumber, ct);
    }

    /// <summary>Categorías indicadas (del tenant) más todas sus descendientes.</summary>
    private async Task<HashSet<int>> ExpandCategoriesAsync(IEnumerable<int> roots, CancellationToken ct)
    {
        var all = await db.Set<ProductCategory>().AsNoTracking().Select(c => new { c.ProductCategoryId, c.ParentId }).ToListAsync(ct);
        var existing = all.Select(c => c.ProductCategoryId).ToHashSet();
        var children = all.Where(c => c.ParentId != null).ToLookup(c => c.ParentId!.Value, c => c.ProductCategoryId);
        var result = new HashSet<int>();
        var queue = new Queue<int>(roots.Where(existing.Contains));
        while (queue.Count > 0)
        {
            var id = queue.Dequeue();
            if (!result.Add(id)) continue;
            foreach (var child in children[id]) queue.Enqueue(child);
        }
        return result;
    }

    /// <summary>
    /// Almacén por PublicId (404 'Almacén no encontrado.') o, sin él, el único almacén activo del tenant (con más de uno →
    /// 400 'Indique el almacén: la compañía tiene más de uno.'; sin ninguno → 422). Mismos mensajes que WmsResolve.
    /// </summary>
    private Task<Warehouse> ResolveWarehouseOrDefaultAsync(Guid? publicId, CancellationToken ct)
        => WmsResolve.ResolveWarehouseOrDefaultAsync(db, publicId, ct); // única implementación (D26): 404 / 400 con más de uno / 422 sin ninguno

    /// <summary>
    /// Lote del producto por número (D34): delega en la sentencia (17) de InventoryQueries (UPDLOCK + HOLDLOCK): existente con
    /// las mismas fechas capturadas → se reutiliza; con otras → 409; inexistente → se crea.
    /// </summary>
    private Task<int> EnsureLotAsync(int productId, string lotNumber, DateOnly? manufacture, DateOnly? expiry, CancellationToken ct)
        => db.EnsureLotAsync(productId, lotNumber, manufacture, expiry, ct);

    /// <summary>Estatus del dominio con la etiqueta personalizada del tenant si existe.</summary>
    private async Task<Dictionary<int, (string Code, string Label)>> StatusMapAsync(string domain, CancellationToken ct)
    {
        var codes = await db.StatusCodes.AsNoTracking().Where(s => s.Entity == domain).ToListAsync(ct);
        var ids = codes.Select(c => c.StatusCodeId).ToList();
        var overrides = tenant.TenantId is null
            ? new Dictionary<int, StatusCodeOverride>()
            : await db.StatusCodeOverrides.AsNoTracking().Where(o => ids.Contains(o.StatusCodeId)).ToDictionaryAsync(o => o.StatusCodeId, ct);
        return codes.ToDictionary(c => c.StatusCodeId, c => (c.InternalCode,
            MultilingualText.Resolve(MultilingualText.Merge(c.LabelJson, overrides.GetValueOrDefault(c.StatusCodeId)?.CustomLabelJson), tenant.Lang)));
    }

    // ================================================================ tarea COUNT
    // La tarea nace con WarehouseTaskWriter (historial null → PENDING). Sus cierres (DONE al reconciliar, CANCELLED al
    // eliminar) van por StatusService.TransitionAsync con el mismo escalonado que WarehouseTaskWriter.AdvanceAsync
    // (PENDING → IN_PROGRESS → DONE), sobre la tarea bloqueada DESPUÉS del encabezado del conteo.

    private Task CreateCountTaskAsync(CycleCount cc, CancellationToken ct)
        => taskWriter.CreateAsync(new WarehouseTaskSpec(
            TaskType: WarehouseTaskTypes.Count, WarehouseId: cc.WarehouseId, ProductId: null, Quantity: null,
            LotId: null, FromBinId: null, ToBinId: null,
            RefEntityType: EntityTypes.CycleCount, RefId: cc.CycleCountId), ct);

    /// <summary>La tarea COUNT abierta (PENDING/IN_PROGRESS) del conteo, bloqueada; null si no hay (ya cerrada o cancelada desde la cola).</summary>
    private async Task<WarehouseTask?> LockOpenCountTaskAsync(CycleCount cc, CancellationToken ct)
    {
        var refType = await lookups.TryGetIdAsync(LookupDomains.EntityType, EntityTypes.CycleCount, ct);
        var countType = await lookups.TryGetIdAsync(LookupDomains.WarehouseTaskType, WarehouseTaskTypes.Count, ct);
        if (refType is null || countType is null) return null;
        var open = await db.StatusCodes.AsNoTracking()
            .Where(s => s.Entity == StatusDomains.WarehouseTaskStatus
                        && (s.InternalCode == WarehouseTaskStatuses.Pending || s.InternalCode == WarehouseTaskStatuses.InProgress))
            .Select(s => s.StatusCodeId).ToListAsync(ct);
        var taskId = await db.Set<WarehouseTask>().AsNoTracking()
            .Where(t => t.RefEntityLookupId == refType && t.RefId == cc.CycleCountId && t.TaskTypeLookupId == countType
                        && t.WarehouseId == cc.WarehouseId && open.Contains(t.StatusCodeId))
            .OrderBy(t => t.WarehouseTaskId).Select(t => (int?)t.WarehouseTaskId).FirstOrDefaultAsync(ct);
        if (taskId is null) return null;
        var task = await LockTaskAsync(taskId.Value, ct);
        return open.Contains(task.StatusCodeId) ? task : null;
    }

    private async Task CompleteCountTaskAsync(WarehouseTask task, CancellationToken ct)
    {
        var code = await StatusCodeOfAsync(task.StatusCodeId, ct);
        if (code == WarehouseTaskStatuses.Pending)
        {
            var started = await statuses.TransitionAsync(StatusDomains.WarehouseTaskStatus, EntityTypes.WarehouseTask, task.WarehouseTaskId,
                task.StatusCodeId, WarehouseTaskStatuses.InProgress, null, ct);
            task.StatusCodeId = started.StatusCodeId;
        }
        var done = await statuses.TransitionAsync(StatusDomains.WarehouseTaskStatus, EntityTypes.WarehouseTask, task.WarehouseTaskId,
            task.StatusCodeId, WarehouseTaskStatuses.Done, null, ct);
        task.StatusCodeId = done.StatusCodeId;
        task.CompletedAtUtc = DateTime.UtcNow;
    }

    // ================================================================ adaptadores a las costuras de P0 (InventoryQueries)
    // Toda dependencia de las firmas de InventoryQueries de esta pieza vive aquí (mismo patrón que P1, P2 y P4).

    /// <summary>Encabezado CycleCount con UPDLOCK, tracked (paso 1 del orden de bloqueo).</summary>
    private Task<CycleCount> LockCountAsync(int cycleCountId, CancellationToken ct)
        => db.LockCycleCountAsync(cycleCountId, ct);

    /// <summary>WarehouseTask con UPDLOCK, tracked (paso 2 del orden de bloqueo).</summary>
    private Task<WarehouseTask> LockTaskAsync(int warehouseTaskId, CancellationToken ct)
        => db.LockWarehouseTaskAsync(warehouseTaskId, ct);

    /// <summary>Rango de saldos del producto con HOLDLOCK (paso 3): bloquea también las filas por nacer.</summary>
    private async Task<IReadOnlyList<StockBalance>> LockProductBalancesAsync(int productId, CancellationToken ct)
        => await db.LockBalancesByProductAsync(productId, ct);
}
