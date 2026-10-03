using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Catalogs;
using Teikem.Domain.Common;
using Teikem.Domain.Constants;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Wms;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 6 (P1) — jerarquía física del almacén (R2, R3): zonas tipadas, posiciones pasillo-rack-nivel-posición y muelles con
/// estatus. Toda hija (sin TenantId) se resuelve SIEMPRE a través de su almacén ya filtrado por tenant (WmsResolve): una
/// zona, posición o muelle de otro almacén u otro tenant es 404, sin oráculo.
/// - Zonas: código único por almacén (409), editable desde el Lote 1 de cambios de Almacén; tipo por catálogo ZoneType
///   ('Tipo de zona desconocido: 'X'.'); baja solo sin posiciones activas (409). El listado trae la ocupación de cada zona
///   (capacidad = Σ cupo de sus posiciones activas; no se guarda).
/// - Posiciones con cupo máximo opcional en unidades (MaxCapacityQty &gt; 0); listado paginado en el servidor con estado de
///   ocupación EMPTY/PARTIAL/FULL/NO_CAPACITY (WarehouseRules.Occupancy).
/// - Posiciones: código explícito o compuesto ('A01-R02-N3-P04'), único POR ALMACÉN (UQ_WarehouseBin_WhCode; 409);
///   WarehouseId sale de la zona; código y zona inmutables. Baja, en orden: bloqueo del almacén, rango de saldos de la
///   posición (HOLDLOCK), 409 con inventario (en mano o reservado) o con tareas abiertas que la usan, y solo entonces
///   IsActive = 0.
/// - Muelles: tipo por catálogo DockType; nacen FREE con historial WAREHOUSE_DOCK; estatus manual FREE/OCCUPIED/MAINTENANCE
///   vía StatusService; baja solo sin citas SCHEDULED/ARRIVED (409), con el muelle bloqueado.
/// - Nada se agrega ni se reactiva en un almacén dado de baja (422).
/// </summary>
public sealed class WarehouseLayoutService(TeikemDbContext db, ITenantContext tenant, ILookupCache lookups, StatusService statuses)
{
    private sealed record StatusInfo(string Code, string Label, string? Color);

    // ================================================================ zonas

    /// <summary>
    /// Zonas del almacén con su ocupación (Lote 1 de cambios de Almacén): posiciones activas y ocupadas, capacidad = Σ cupo de
    /// sus posiciones activas con cupo (no se guarda), existencia en esas posiciones, existencia total y posiciones sin cupo.
    /// Las cifras de TODAS las zonas salen de UNA consulta agrupada (ZoneStatsQuery).
    /// </summary>
    public async Task<IReadOnlyList<WarehouseZoneDto>> ListZonesAsync(Guid warehousePublicId, bool includeInactive, CancellationToken ct)
    {
        var w = await ResolveWarehouseAsync(warehousePublicId, ct);
        var q = db.WarehouseZones.AsNoTracking().Where(z => z.WarehouseId == w.WarehouseId);
        if (!includeInactive) q = q.Where(z => z.IsActive);
        var zones = await q.OrderBy(z => z.Code).ToListAsync(ct);
        var stats = await ZoneStatsAsync(db, w.WarehouseId, null, ct);
        var list = new List<WarehouseZoneDto>(zones.Count);
        foreach (var z in zones) list.Add(await ZoneDtoAsync(z, stats.GetValueOrDefault(z.WarehouseZoneId), ct));
        return list;
    }

    public async Task<WarehouseZoneDto> CreateZoneAsync(Guid warehousePublicId, WarehouseZoneRequest req, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        var (code, codeError) = WarehouseRules.NormalizeCode(req.Code);
        if (codeError is not null) errors["code"] = new[] { codeError };
        var name = RequiredText(req.Name, "name", "El nombre", WarehouseRules.ZoneNameMaxLength, errors);
        var typeId = await LookupIdAsync(LookupDomains.ZoneType, req.ZoneType, "zoneType", WarehouseRules.UnknownZoneType, errors, ct);
        if (errors.Count > 0) throw new ValidationException(errors);

        var w = await ResolveWarehouseAsync(warehousePublicId, ct);
        EnsureWarehouseActive(w);
        if (await db.WarehouseZones.AnyAsync(z => z.WarehouseId == w.WarehouseId && z.Code == code, ct))
            throw new ConflictException(WarehouseRules.DuplicateZoneMessage);

        var zone = new WarehouseZone { WarehouseId = w.WarehouseId, Code = code!, Name = name!, ZoneTypeLookupId = typeId, IsActive = true };
        db.WarehouseZones.Add(zone);
        await db.SaveGuardedAsync(WarehouseRules.DuplicateZoneMessage, ct); // UQ_WarehouseZone (WarehouseId, Code)
        return await ZoneDtoAsync(zone, null, ct);
    }

    /// <summary>
    /// PATCH de zona: código, nombre y tipo ("" quita el tipo). Lote 1 (cambios de Almacén): el código se edita — sigue siendo
    /// obligatorio y con el mismo formato (400) y único en el almacén (409 'Ya existe una zona con ese código en el almacén.').
    /// Las posiciones y los saldos apuntan a la zona por id, así que renombrarla no toca nada más. 'warehouseId' → 400.
    /// </summary>
    public async Task<WarehouseZoneDto> UpdateZoneAsync(Guid warehousePublicId, int zoneId, WarehouseZonePatchRequest req, CancellationToken ct)
    {
        RejectImmutable(req.Extra, WarehouseRules.ZoneWarehouseImmutableMessage, "warehouseId");
        var errors = new Dictionary<string, string[]>();
        string? code = null;
        if (req.Code is not null)
        {
            var (normalized, codeError) = WarehouseRules.NormalizeCode(req.Code);
            if (codeError is not null) errors["code"] = new[] { codeError };
            code = normalized;
        }
        string? name = req.Name is null ? null : RequiredText(req.Name, "name", "El nombre", WarehouseRules.ZoneNameMaxLength, errors);
        var typeId = await LookupIdAsync(LookupDomains.ZoneType, req.ZoneType, "zoneType", WarehouseRules.UnknownZoneType, errors, ct);
        if (errors.Count > 0) throw new ValidationException(errors);

        var w = await ResolveWarehouseAsync(warehousePublicId, ct);
        EnsureWarehouseActive(w);
        var zone = await ResolveZoneAsync(w, zoneId, track: true, ct);
        if (code is not null && !string.Equals(code, zone.Code, StringComparison.Ordinal))
        {
            // Unicidad (WarehouseId, Code) dentro del almacén (ya resuelto bajo el filtro de tenant); UQ_WarehouseZone es la
            // segunda barrera ante una carrera (SaveGuardedAsync la traduce al mismo 409).
            if (await db.WarehouseZones.AnyAsync(z => z.WarehouseId == w.WarehouseId && z.Code == code && z.WarehouseZoneId != zone.WarehouseZoneId, ct))
                throw new ConflictException(WarehouseRules.DuplicateZoneMessage);
            zone.Code = code;
        }
        if (req.Name is not null) zone.Name = name!;
        if (req.ZoneType is not null) zone.ZoneTypeLookupId = typeId;
        await db.SaveGuardedAsync(WarehouseRules.DuplicateZoneMessage, ct);
        return await ZoneDtoAsync(zone, (await ZoneStatsAsync(db, w.WarehouseId, zone.WarehouseZoneId, ct)).GetValueOrDefault(zone.WarehouseZoneId), ct);
    }

    /// <summary>Baja/reactivación de zona. Desactivar con posiciones activas → 409 'La zona tiene posiciones activas; desactívelas primero.'</summary>
    public async Task<WarehouseZoneDto> SetZoneActiveAsync(Guid warehousePublicId, int zoneId, bool active, CancellationToken ct)
    {
        var w = await ResolveWarehouseAsync(warehousePublicId, ct);
        var zoneRef = await ResolveZoneAsync(w, zoneId, track: false, ct);
        if (active) EnsureWarehouseActive(w);

        await db.RunInTransactionAsync(async ct2 =>
        {
            // El almacén bloqueado serializa esta baja contra el alta/reactivación de posiciones de la zona.
            await LockWarehouseAsync(w.WarehouseId, ct2);
            var zone = await db.WarehouseZones.FirstAsync(z => z.WarehouseZoneId == zoneRef.WarehouseZoneId, ct2);
            if (zone.IsActive == active) return;
            if (!active && await db.WarehouseBins.AnyAsync(b => b.WarehouseZoneId == zone.WarehouseZoneId && b.IsActive, ct2))
                throw new ConflictException(WarehouseRules.ZoneHasActiveBins);
            zone.IsActive = active;
            await db.SaveGuardedAsync(WarehouseRules.DuplicateZoneMessage, ct2);
        }, ct);

        var fresh = await ResolveZoneAsync(w, zoneId, track: false, ct);
        return await ZoneDtoAsync(fresh, (await ZoneStatsAsync(db, w.WarehouseId, fresh.WarehouseZoneId, ct)).GetValueOrDefault(fresh.WarehouseZoneId), ct);
    }

    // ================================================================ posiciones

    /// <summary>
    /// Posiciones del almacén, paginadas en el servidor (Lote 1 de cambios de Almacén). Filtrado, orden (código) y paginación
    /// en SQL con BinRowsQuery: búsqueda libre en código, zona, pasillo, rack, nivel y posición; zona(s); partes (contiene);
    /// productos con existencia en la posición; estado de ocupación; inactivas opcionales; solo con existencia. Existencia,
    /// productos distintos y el producto único de cada posición salen en la misma consulta (LEFT JOIN a la existencia
    /// agrupada por posición, sin N+1); el SKU y nombre de esos productos únicos, en UNA consulta más para toda la página.
    /// - zoneId de otro almacén → 404; occupancy desconocido → 400 UnknownOccupancy.
    /// - Lote 23: estado de la hoja de posición por fila (BinSheetRules.Status), filtro sheetStatus (400 si desconocido) y
    ///   StaleCount = posiciones del filtro (todas las páginas) en STALE o NEVER_PRINTED (una consulta COUNT más).
    /// </summary>
    public async Task<WarehouseBinPageDto> ListBinsAsync(Guid warehousePublicId, WarehouseBinQuery query, CancellationToken ct)
    {
        query ??= new WarehouseBinQuery();
        var (skip, take) = WarehouseRules.BinPage(query.Skip, query.Take);
        var (_, q) = await FilteredBinRowsAsync(db, warehousePublicId, query, ct);
        var total = await q.CountAsync(ct);
        var staleCount = total == 0 ? 0 : await q.CountAsync(NeedsSheetPrinting, ct);   // Lote 23
        var page = await q.OrderBy(x => x.Bin.Code).ThenBy(x => x.Bin.WarehouseBinId).Skip(skip).Take(take).ToListAsync(ct);
        return new WarehouseBinPageDto(total, skip, take, await BinDtosAsync(page, ct), staleCount);
    }

    /// <summary>
    /// Validación y resolución comunes del listado de posiciones y de las hojas de posición (Lote 23): occupancy y sheetStatus
    /// desconocidos → 400; almacén de otro tenant → 404; zoneId de otro almacén → 404; productPublicIds → ids del tenant. Devuelve
    /// el almacén y la consulta filtrada (sin ordenar ni paginar).
    /// </summary>
    public static async Task<(Warehouse Warehouse, IQueryable<BinRow> Rows)> FilteredBinRowsAsync(TeikemDbContext db, Guid warehousePublicId,
        WarehouseBinQuery query, CancellationToken ct)
    {
        var (occupancy, occupancyError) = WarehouseRules.ParseOccupancy(query.Occupancy);
        if (occupancyError is not null) throw new ValidationException("occupancy", occupancyError);
        var (sheetStatuses, sheetError) = BinSheetRules.ParseStatuses(query.SheetStatus);
        if (sheetError is not null) throw new ValidationException("sheetStatus", sheetError);

        var w = await db.ResolveWarehouseAsync(warehousePublicId, false, ct);
        if (query.ZoneId is int zid) await db.ResolveZoneAsync(w.WarehouseId, zid, false, ct); // 404 si la zona no es de este almacén

        List<int>? productIds = null;
        if (query.ProductPublicIds is { Length: > 0 } productPublicIds)
            productIds = await db.Set<Product>().AsNoTracking()
                .Where(p => productPublicIds.Contains(p.PublicId))
                .Select(p => p.ProductId)
                .ToListAsync(ct);

        return (w, BinRowsQuery(db, w.WarehouseId, query, productIds, occupancy, sheetStatuses));
    }

    /// <summary>
    /// Lote 23 — la hoja de la posición pide imprimirse (STALE o NEVER_PRINTED), en SQL; mismo criterio que
    /// BinSheetRules.Status + NeedsPrinting. Es el 'staleCount' del listado y de las hojas.
    /// </summary>
    public static readonly System.Linq.Expressions.Expression<Func<BinRow, bool>> NeedsSheetPrinting = x =>
        (x.Bin.SheetPrintedAtUtc != null && x.Bin.SheetContentChangedAtUtc != null && x.Bin.SheetContentChangedAtUtc > x.Bin.SheetPrintedAtUtc)
        || (x.OnHand > 0 && x.Bin.SheetPrintedAtUtc == null);

    /// <summary>Tope de resultados de la búsqueda de posiciones entre almacenes (Lote 14).</summary>
    public const int MaxBinSearch = 50;

    /// <summary>
    /// Lote 14 — búsqueda de posiciones ENTRE almacenes para los filtros Posición del Kárdex y del Conteo: el código de la
    /// posición o de su zona contiene el texto (vacío = las primeras por almacén y código), acotada opcionalmente a almacenes;
    /// solo activas salvo includeInactive (el Kárdex puede filtrar posiciones ya dadas de baja). take en 1..50 (por defecto 20).
    /// La posición y la zona (sin TenantId) se alcanzan por su almacén filtrado por tenant; un almacén de otro tenant no aporta nada.
    /// </summary>
    public async Task<IReadOnlyList<BinSearchItemDto>> SearchBinsAsync(string? search, Guid[]? warehousePublicIds, bool includeInactive,
        int take, CancellationToken ct)
    {
        take = take <= 0 ? 20 : Math.Min(take, MaxBinSearch);
        var q = from b in db.WarehouseBins.AsNoTracking()
                join w in db.Warehouses.AsNoTracking() on b.WarehouseId equals w.WarehouseId
                join z in db.WarehouseZones.AsNoTracking() on b.WarehouseZoneId equals z.WarehouseZoneId
                select new { Bin = b, WarehousePublicId = w.PublicId, WarehouseCode = w.Code, ZoneCode = z.Code };
        if (!includeInactive) q = q.Where(x => x.Bin.IsActive);
        if (warehousePublicIds is { Length: > 0 })
        {
            var pubs = warehousePublicIds.Distinct().ToList();
            q = q.Where(x => pubs.Contains(x.WarehousePublicId));
        }
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(x => x.Bin.Code.Contains(s) || x.ZoneCode.Contains(s));
        }
        var rows = await q.OrderBy(x => x.WarehouseCode).ThenBy(x => x.Bin.Code).ThenBy(x => x.Bin.WarehouseBinId).Take(take).ToListAsync(ct);
        return rows.Select(x => new BinSearchItemDto(x.Bin.WarehouseBinId, x.Bin.Code, x.ZoneCode, x.WarehousePublicId, x.WarehouseCode, x.Bin.IsActive, x.Bin.IsProvisional))
            .ToList();
    }

    /// <summary>Fila del listado de posiciones: la posición, su zona y su existencia (member-init: se puede seguir filtrando en SQL).</summary>
    public sealed class BinRow
    {
        public WarehouseBin Bin { get; init; } = null!;
        public string ZoneCode { get; init; } = string.Empty;
        public int? ZoneTypeLookupId { get; init; }
        public decimal? CapacityQty { get; init; }
        public decimal OnHand { get; init; }
        public int ProductCount { get; init; }
        /// <summary>Menor ProductId con existencia: cuando ProductCount = 1 es EL producto de la posición.</summary>
        public int? AnyProductId { get; init; }
    }

    /// <summary>
    /// Consulta (sin ordenar ni paginar) de las posiciones del almacén con sus filtros. Público y estático para probar su
    /// traducción a SQL Server sin BD (ToQueryString). productIds ya resueltos desde PublicId (null = sin filtro); occupancy ya
    /// validado (null = sin filtro). La existencia cuenta solo saldos con en mano ≠ 0 (CK_StockBalance_Qty: nunca negativos).
    /// </summary>
    public static IQueryable<BinRow> BinRowsQuery(TeikemDbContext db, int warehouseId, WarehouseBinQuery query,
        IReadOnlyCollection<int>? productIds, IReadOnlySet<string>? occupancy, IReadOnlySet<string>? sheetStatuses = null)
    {
        var stock = db.StockBalances.AsNoTracking().Where(s => s.WarehouseId == warehouseId && s.WarehouseBinId != null && s.QtyOnHand != 0);
        // Existencia agrupada por posición (tabla derivada) unida por LEFT JOIN: la existencia se calcula una vez por posición
        // y el filtro de ocupación y la proyección la reutilizan como columna.
        var binStock = stock
            .GroupBy(s => s.WarehouseBinId)
            .Select(g => new
            {
                BinId = g.Key,
                OnHand = g.Sum(s => s.QtyOnHand),
                Products = g.Select(s => s.ProductId).Distinct().Count(),
                MinProductId = g.Min(s => s.ProductId),
            });
        var q = from b in db.WarehouseBins.AsNoTracking()
                join z in db.WarehouseZones.AsNoTracking() on b.WarehouseZoneId equals z.WarehouseZoneId
                join st in binStock on (int?)b.WarehouseBinId equals st.BinId into sj
                from st in sj.DefaultIfEmpty()
                where b.WarehouseId == warehouseId
                select new BinRow
                {
                    Bin = b,
                    ZoneCode = z.Code,
                    ZoneTypeLookupId = z.ZoneTypeLookupId,
                    CapacityQty = b.MaxCapacityQty,
                    OnHand = (decimal?)st!.OnHand ?? 0m,
                    ProductCount = (int?)st!.Products ?? 0,
                    AnyProductId = (int?)st!.MinProductId,
                };

        if (!query.IncludeInactive) q = q.Where(x => x.Bin.IsActive);
        if (query.ZoneId is int zoneId) q = q.Where(x => x.Bin.WarehouseZoneId == zoneId);
        if (query.IsProvisional is bool provisional) q = q.Where(x => x.Bin.IsProvisional == provisional);   // Lote 21
        if (query.ZoneIds is { Length: > 0 } zoneIds) q = q.Where(x => zoneIds.Contains(x.Bin.WarehouseZoneId));
        if (query.BinIds is { Length: > 0 } binIds) q = q.Where(x => binIds.Contains(x.Bin.WarehouseBinId));

        // Códigos y partes se guardan en mayúsculas (WarehouseRules): el término se compara en mayúsculas.
        if (SearchTerm(query.Search) is string s)
            q = q.Where(x => x.Bin.Code.Contains(s) || x.ZoneCode.Contains(s)
                             || (x.Bin.Aisle != null && x.Bin.Aisle.Contains(s)) || (x.Bin.Rack != null && x.Bin.Rack.Contains(s))
                             || (x.Bin.Level != null && x.Bin.Level.Contains(s)) || (x.Bin.Position != null && x.Bin.Position.Contains(s)));
        if (SearchTerm(query.Aisle) is string aisle) q = q.Where(x => x.Bin.Aisle != null && x.Bin.Aisle.Contains(aisle));
        if (SearchTerm(query.Rack) is string rack) q = q.Where(x => x.Bin.Rack != null && x.Bin.Rack.Contains(rack));
        if (SearchTerm(query.Level) is string level) q = q.Where(x => x.Bin.Level != null && x.Bin.Level.Contains(level));
        if (SearchTerm(query.Position) is string position) q = q.Where(x => x.Bin.Position != null && x.Bin.Position.Contains(position));

        if (productIds is not null)
        {
            var ids = productIds.ToList();
            q = q.Where(x => stock.Any(s => s.WarehouseBinId == x.Bin.WarehouseBinId && ids.Contains(s.ProductId)));
        }
        if (query.OnlyWithStock) q = q.Where(x => x.OnHand > 0);

        if (occupancy is not null)
        {
            // Mismo criterio que WarehouseRules.Occupancy (la prueba de ocupación lo compara fila por fila).
            var empty = occupancy.Contains(BinOccupancies.Empty);
            var partial = occupancy.Contains(BinOccupancies.Partial);
            var full = occupancy.Contains(BinOccupancies.Full);
            var noCapacity = occupancy.Contains(BinOccupancies.NoCapacity);
            q = q.Where(x => (empty && x.OnHand <= 0)
                             || (partial && x.OnHand > 0 && x.CapacityQty != null && x.OnHand < x.CapacityQty)
                             || (full && x.OnHand > 0 && x.CapacityQty != null && x.OnHand >= x.CapacityQty)
                             || (noCapacity && x.OnHand > 0 && x.CapacityQty == null));
        }

        if (sheetStatuses is not null)
        {
            // Lote 23 — mismo criterio que BinSheetRules.Status (la prueba de hojas lo compara fila por fila). "Con productos" =
            // existencia en mano > 0 en la posición (los saldos nunca son negativos: CK_StockBalance_Qty).
            var stale = sheetStatuses.Contains(BinSheetStatuses.Stale);
            var empty = sheetStatuses.Contains(BinSheetStatuses.Empty);
            var never = sheetStatuses.Contains(BinSheetStatuses.NeverPrinted);
            var current = sheetStatuses.Contains(BinSheetStatuses.Current);
            q = q.Where(x =>
                (stale && x.Bin.SheetPrintedAtUtc != null && x.Bin.SheetContentChangedAtUtc != null && x.Bin.SheetContentChangedAtUtc > x.Bin.SheetPrintedAtUtc)
                || (empty && x.OnHand <= 0
                    && (x.Bin.SheetPrintedAtUtc == null || x.Bin.SheetContentChangedAtUtc == null || x.Bin.SheetContentChangedAtUtc <= x.Bin.SheetPrintedAtUtc))
                || (never && x.OnHand > 0 && x.Bin.SheetPrintedAtUtc == null)
                || (current && x.OnHand > 0 && x.Bin.SheetPrintedAtUtc != null
                    && (x.Bin.SheetContentChangedAtUtc == null || x.Bin.SheetContentChangedAtUtc <= x.Bin.SheetPrintedAtUtc)));
        }
        return q;
    }

    private static string? SearchTerm(string? raw) => string.IsNullOrWhiteSpace(raw) ? null : raw.Trim().ToUpperInvariant();

    /// <summary>DTOs de una página de posiciones: SKU y nombre de los productos únicos en UNA consulta (sin N+1).</summary>
    private async Task<IReadOnlyList<WarehouseBinDto>> BinDtosAsync(IReadOnlyList<BinRow> rows, CancellationToken ct)
    {
        var singleIds = rows.Where(r => r.ProductCount == 1 && r.AnyProductId is not null).Select(r => r.AnyProductId!.Value).Distinct().ToList();
        var products = singleIds.Count == 0
            ? new Dictionary<int, (Guid PublicId, string Sku, string Name)>()
            : (await db.Set<Product>().AsNoTracking()
                .Where(p => singleIds.Contains(p.ProductId))
                .Select(p => new { p.ProductId, p.PublicId, p.Sku, p.Name })
                .ToListAsync(ct))
              .ToDictionary(p => p.ProductId, p => (p.PublicId, p.Sku, p.Name));

        var list = new List<WarehouseBinDto>(rows.Count);
        foreach (var r in rows)
        {
            (Guid PublicId, string Sku, string Name)? single = r.ProductCount == 1 && r.AnyProductId is int pid && products.TryGetValue(pid, out var p) ? p : null;
            list.Add(new WarehouseBinDto(r.Bin.WarehouseBinId, r.Bin.WarehouseZoneId, r.ZoneCode, await LookupCodeAsync(r.ZoneTypeLookupId, ct),
                r.Bin.Code, r.Bin.Aisle, r.Bin.Rack, r.Bin.Level, r.Bin.Position, r.Bin.MaxWeightKg, r.Bin.IsActive,
                r.OnHand, r.ProductCount, r.Bin.MaxCapacityQty, WarehouseRules.Occupancy(r.OnHand, r.Bin.MaxCapacityQty),
                single?.PublicId, single?.Sku, single?.Name,
                r.Bin.IsProvisional, r.Bin.ProvisionalCycleCountId, r.Bin.ProvisionalCreatedAtUtc,
                BinSheetRules.Status(r.OnHand > 0, r.Bin.SheetPrintedAtUtc, r.Bin.SheetContentChangedAtUtc),
                r.Bin.SheetPrintedAtUtc, r.Bin.SheetContentChangedAtUtc));
        }
        return list;
    }

    /// <summary>
    /// Alta de posición en una zona ACTIVA del almacén. Código explícito o compuesto de sus partes; único por almacén (409);
    /// capacidad de peso &gt; 0. WarehouseId sale de la zona (FK compuesta (Zona, Almacén) en SQL).
    /// </summary>
    public Task<WarehouseBinDto> CreateBinAsync(Guid warehousePublicId, WarehouseBinRequest req, CancellationToken ct)
        => CreateBinCoreAsync(warehousePublicId, req, null, ct);

    /// <summary>Marca de una posición provisional: quién la crea y desde qué conteo (ver CreateProvisionalBinAsync).</summary>
    private sealed record ProvisionalMark(int? UserId, int CycleCountId);

    private async Task<WarehouseBinDto> CreateBinCoreAsync(Guid warehousePublicId, WarehouseBinRequest req, ProvisionalMark? provisional, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        if (req.ZoneId is null) errors["zoneId"] = new[] { WarehouseRules.ZoneRequiredMessage };
        var (code, codeError) = WarehouseRules.ResolveBinCode(req.Code, req.Aisle, req.Rack, req.Level, req.Position);
        if (codeError is not null) errors["code"] = new[] { codeError };
        if (WarehouseRules.ValidateMaxWeight(req.MaxWeightKg) is string weightError) errors["maxWeightKg"] = new[] { weightError };
        if (WarehouseRules.ValidateMaxCapacity(req.MaxCapacityQty) is string capacityError) errors["maxCapacityQty"] = new[] { capacityError };
        if (errors.Count > 0) throw new ValidationException(errors);

        var w = await ResolveWarehouseAsync(warehousePublicId, ct);
        EnsureWarehouseActive(w);
        var zone = await ResolveZoneAsync(w, req.ZoneId!.Value, track: false, ct);
        if (!zone.IsActive) throw new StatusRuleException(WarehouseRules.ZoneInactiveMessage);
        if (await db.WarehouseBins.AnyAsync(b => b.WarehouseId == w.WarehouseId && b.Code == code, ct))
            throw new ConflictException(WarehouseRules.DuplicateBinMessage);

        var bin = new WarehouseBin
        {
            WarehouseZoneId = zone.WarehouseZoneId, WarehouseId = zone.WarehouseId, Code = code!,
            Aisle = WarehouseRules.NormalizeBinPart(req.Aisle).Part, Rack = WarehouseRules.NormalizeBinPart(req.Rack).Part,
            Level = WarehouseRules.NormalizeBinPart(req.Level).Part, Position = WarehouseRules.NormalizeBinPart(req.Position).Part,
            MaxWeightKg = req.MaxWeightKg, MaxCapacityQty = req.MaxCapacityQty, IsActive = true,
        };
        if (provisional is not null)
        {
            bin.IsProvisional = true;
            bin.ProvisionalCreatedBy = provisional.UserId;
            bin.ProvisionalCreatedAtUtc = DateTime.UtcNow;
            bin.ProvisionalCycleCountId = provisional.CycleCountId;
        }
        db.WarehouseBins.Add(bin);
        await db.SaveGuardedAsync(WarehouseRules.DuplicateBinMessage, ct); // UQ_WarehouseBin_WhCode es la segunda barrera
        return new WarehouseBinDto(bin.WarehouseBinId, zone.WarehouseZoneId, zone.Code, await LookupCodeAsync(zone.ZoneTypeLookupId, ct),
            bin.Code, bin.Aisle, bin.Rack, bin.Level, bin.Position, bin.MaxWeightKg, bin.IsActive, 0, 0, bin.MaxCapacityQty,
            WarehouseRules.Occupancy(0, bin.MaxCapacityQty), null, null, null,
            bin.IsProvisional, bin.ProvisionalCycleCountId, bin.ProvisionalCreatedAtUtc);
    }

    /// <summary>
    /// Lote 21 — posición provisional desde un conteo (warehouse.count.capture): quien cuenta encontró producto donde el sistema no
    /// tenía nada y la posición no existe. El conteo debe ser del tenant (404), estar activo y NO reconciliado (422 'El conteo ya
    /// fue reconciliado; no admite posiciones nuevas.'); la zona, del almacén del conteo (404). Mismas validaciones que el alta de
    /// posición (zona activa 422, código o partes 400, código repetido en el almacén 409) pero la posición nace marcada
    /// IsProvisional con quién, cuándo y el conteo que la creó, y se puede usar ya en el conteo y en el inventario. El supervisor
    /// (warehouse.manage) la confirma con ConfirmProvisionalBinAsync, la corrige o la desactiva.
    /// </summary>
    public async Task<WarehouseBinDto> CreateProvisionalBinAsync(int cycleCountId, WarehouseBinRequest req, CancellationToken ct)
    {
        var count = await db.Set<CycleCount>().AsNoTracking().FirstOrDefaultAsync(c => c.CycleCountId == cycleCountId && c.IsActive, ct)
                    ?? throw new NotFoundException("Conteo");
        var statusCode = await db.StatusCodes.AsNoTracking().Where(s => s.StatusCodeId == count.StatusCodeId).Select(s => s.InternalCode).FirstOrDefaultAsync(ct);
        if (CycleCountStatuses.IsReconciled(statusCode ?? string.Empty)) throw new StatusRuleException(CycleCountRules.ProvisionalCountClosed);
        var warehouse = await db.Warehouses.AsNoTracking().FirstAsync(w => w.WarehouseId == count.WarehouseId, ct);
        return await CreateBinCoreAsync(warehouse.PublicId, req, new ProvisionalMark(tenant.UserId, count.CycleCountId), ct);
    }

    /// <summary>
    /// Lote 21 — el supervisor (warehouse.manage) confirma una posición provisional: quita la marca IsProvisional (conserva quién,
    /// cuándo y el conteo que la creó). Editar o desactivar siguen siendo los de siempre. Confirmar una que no es provisional →
    /// 409 'La posición no está pendiente de revisión.'. Posición de otro almacén u otro tenant → 404.
    /// </summary>
    public async Task<WarehouseBinDto> ConfirmProvisionalBinAsync(Guid warehousePublicId, int binId, CancellationToken ct)
    {
        var w = await ResolveWarehouseAsync(warehousePublicId, ct);
        var bin = await ResolveBinAsync(w, binId, track: true, ct);
        if (!bin.IsProvisional) throw new ConflictException(WarehouseRules.BinNotProvisional);
        bin.IsProvisional = false;
        await db.SaveGuardedAsync(WarehouseRules.DuplicateBinMessage, ct);
        return await BinDtoAsync(w.WarehouseId, bin.WarehouseBinId, ct);
    }

    /// <summary>
    /// PATCH de posición: partes (null = sin cambio, "" = quitar), capacidad de peso (clearMaxWeight = sin límite) y cupo máximo
    /// en unidades (clearMaxCapacity = sin configurar; si llega, &gt; 0 → si no, 400). Código y zona son inmutables: 'code',
    /// 'zoneId' o 'warehouseZoneId' en el cuerpo → 400.
    /// </summary>
    public async Task<WarehouseBinDto> UpdateBinAsync(Guid warehousePublicId, int binId, WarehouseBinPatchRequest req, CancellationToken ct)
    {
        RejectImmutable(req.Extra, WarehouseRules.BinCodeImmutableMessage, "code", "zoneId", "warehouseZoneId", "warehouseId");
        var errors = new Dictionary<string, string[]>();
        var aisle = Part(req.Aisle, "aisle", errors);
        var rack = Part(req.Rack, "rack", errors);
        var level = Part(req.Level, "level", errors);
        var position = Part(req.Position, "position", errors);
        if (req.ClearMaxWeight != true && WarehouseRules.ValidateMaxWeight(req.MaxWeightKg) is string weightError) errors["maxWeightKg"] = new[] { weightError };
        if (req.ClearMaxCapacity != true && WarehouseRules.ValidateMaxCapacity(req.MaxCapacityQty) is string capacityError) errors["maxCapacityQty"] = new[] { capacityError };
        if (errors.Count > 0) throw new ValidationException(errors);

        var w = await ResolveWarehouseAsync(warehousePublicId, ct);
        EnsureWarehouseActive(w);
        var bin = await ResolveBinAsync(w, binId, track: true, ct);
        if (req.Aisle is not null) bin.Aisle = aisle;
        if (req.Rack is not null) bin.Rack = rack;
        if (req.Level is not null) bin.Level = level;
        if (req.Position is not null) bin.Position = position;
        if (req.ClearMaxWeight == true) bin.MaxWeightKg = null;
        else if (req.MaxWeightKg.HasValue) bin.MaxWeightKg = req.MaxWeightKg;
        if (req.ClearMaxCapacity == true) bin.MaxCapacityQty = null;
        else if (req.MaxCapacityQty.HasValue) bin.MaxCapacityQty = req.MaxCapacityQty;
        await db.SaveGuardedAsync(WarehouseRules.DuplicateBinMessage, ct);
        return await BinDtoAsync(w.WarehouseId, bin.WarehouseBinId, ct);
    }

    /// <summary>
    /// Lote 11 — cupo máximo en bloque: fija (MaxCapacityQty &gt; 0) o quita (Clear) el cupo de TODAS las posiciones del almacén
    /// que cumplen los filtros, sin paginar y en una transacción; devuelve cuántas cumplen y cuántas cambiaron.
    /// - Los filtros son los del listado y se aplican con la MISMA consulta (BinRowsQuery con un WarehouseBinQuery), así que
    ///   GET .../bins?take=1 con los mismos filtros devuelve en Total exactamente las posiciones que se afectarán.
    ///   OnlyWithoutCapacity además deja fuera las que ya tienen cupo.
    /// - 400: ninguno o ambos de maxCapacityQty/clear, cupo ≤ 0 (el mismo mensaje del PATCH), sin filtros de posiciones y sin
    ///   allBins: true. Almacén de otro tenant → 404; dado de baja → 422.
    /// - Se cargan y modifican las posiciones con seguimiento (no ExecuteUpdate) para que el interceptor audite CADA posición
    ///   cambiada, igual que el PATCH individual. Las que ya tienen ese valor no se tocan (no cuentan como cambiadas).
    /// </summary>
    public async Task<WarehouseBinCapacityResultDto> SetBinsCapacityAsync(Guid warehousePublicId, WarehouseBinCapacityRequest req, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(req);
        var hasFilter = WarehouseRules.HasBinFilter(req.ZoneIds, req.Aisle, req.Rack, req.Level, req.Position, req.Search, req.BinIds);
        var errors = WarehouseRules.ValidateBulkCapacity(hasFilter, req.AllBins, req.MaxCapacityQty, req.Clear);
        if (errors.Count > 0) throw new ValidationException(errors);
        int? target = req.Clear ? null : req.MaxCapacityQty;

        var w = await ResolveWarehouseAsync(warehousePublicId, ct);
        EnsureWarehouseActive(w);
        var query = new WarehouseBinQuery(Search: req.Search, IncludeInactive: req.IncludeInactive, ZoneIds: req.ZoneIds is { Length: > 0 } z ? z : null,
            Aisle: req.Aisle, Rack: req.Rack, Level: req.Level, Position: req.Position, BinIds: req.BinIds is { Length: > 0 } b ? b : null);

        return await db.RunInTransactionAsync(async ct2 =>
        {
            var rows = BinRowsQuery(db, w.WarehouseId, query, null, null);
            if (req.OnlyWithoutCapacity) rows = rows.Where(x => x.Bin.MaxCapacityQty == null);
            var ids = await rows.Select(x => x.Bin.WarehouseBinId).ToListAsync(ct2);
            if (ids.Count == 0) return new WarehouseBinCapacityResultDto(0, 0);

            var changed = 0;
            foreach (var chunk in ids.Chunk(BulkCapacityChunk))
            {
                var bins = await db.WarehouseBins.Where(x => x.WarehouseId == w.WarehouseId && chunk.Contains(x.WarehouseBinId)).ToListAsync(ct2);
                foreach (var bin in bins.Where(x => x.MaxCapacityQty != target))
                {
                    bin.MaxCapacityQty = target;
                    changed++;
                }
            }
            if (changed > 0) await db.SaveGuardedAsync(WarehouseRules.DuplicateBinMessage, ct2);
            return new WarehouseBinCapacityResultDto(ids.Count, changed);
        }, ct);
    }

    /// <summary>Posiciones que se cargan por consulta en la asignación en bloque (acota la lista de ids de cada IN).</summary>
    public const int BulkCapacityChunk = 1000;

    /// <summary>
    /// Baja/reactivación de posición.
    /// - Desactivar, en orden: bloqueo del almacén, rango de saldos de la posición (HOLDLOCK), 409 'La posición {code} tiene
    ///   inventario; no se puede desactivar.' si hay en mano o reservado ≠ 0, 409 si alguna tarea abierta la usa (origen o
    ///   destino), y solo entonces IsActive = 0.
    /// - Reactivar exige el almacén y la zona activos (422).
    /// </summary>
    public async Task<WarehouseBinDto> SetBinActiveAsync(Guid warehousePublicId, int binId, bool active, CancellationToken ct)
    {
        var w = await ResolveWarehouseAsync(warehousePublicId, ct);
        var binRef = await ResolveBinAsync(w, binId, track: false, ct);
        if (active) EnsureWarehouseActive(w);

        await db.RunInTransactionAsync(async ct2 =>
        {
            await LockWarehouseAsync(w.WarehouseId, ct2);
            var bin = await db.WarehouseBins.FirstAsync(b => b.WarehouseBinId == binRef.WarehouseBinId, ct2);
            if (bin.IsActive == active) return;
            if (active)
            {
                var zoneActive = await db.WarehouseZones.Where(z => z.WarehouseZoneId == bin.WarehouseZoneId).Select(z => z.IsActive).FirstAsync(ct2);
                if (!zoneActive) throw new StatusRuleException(WarehouseRules.ZoneInactiveMessage);
            }
            else
            {
                var balances = await LockBinBalancesAsync(bin.WarehouseBinId, ct2);
                if (balances.Any(b => b.QtyOnHand != 0 || b.QtyReserved != 0)) throw new ConflictException(WarehouseRules.BinNotEmpty(bin.Code));
                var openTaskIds = await StatusIdsAsync(StatusDomains.WarehouseTaskStatus, ct2, WarehouseTaskStatuses.Pending, WarehouseTaskStatuses.InProgress);
                if (await db.WarehouseTasks.AnyAsync(t => t.WarehouseId == w.WarehouseId && openTaskIds.Contains(t.StatusCodeId)
                                                          && (t.FromBinId == bin.WarehouseBinId || t.ToBinId == bin.WarehouseBinId), ct2))
                    throw new ConflictException(WarehouseRules.BinHasOpenTasks);
            }
            bin.IsActive = active;
            await db.SaveGuardedAsync(WarehouseRules.DuplicateBinMessage, ct2);
        }, ct);

        return await BinDtoAsync(w.WarehouseId, binRef.WarehouseBinId, ct);
    }

    // ================================================================ muelles

    public async Task<IReadOnlyList<WarehouseDockDto>> ListDocksAsync(Guid warehousePublicId, bool includeInactive, CancellationToken ct)
    {
        var w = await ResolveWarehouseAsync(warehousePublicId, ct);
        var q = db.WarehouseDocks.AsNoTracking().Where(d => d.WarehouseId == w.WarehouseId);
        if (!includeInactive) q = q.Where(d => d.IsActive);
        var docks = await q.OrderBy(d => d.Code).ToListAsync(ct);
        var statusMap = await StatusMapAsync(StatusDomains.DockStatus, ct);
        var list = new List<WarehouseDockDto>(docks.Count);
        foreach (var d in docks) list.Add(await DockDtoAsync(d, statusMap, ct));
        return list;
    }

    /// <summary>Alta de muelle: código único por almacén (409), tipo por catálogo DockType; nace FREE con historial WAREHOUSE_DOCK.</summary>
    public async Task<WarehouseDockDto> CreateDockAsync(Guid warehousePublicId, WarehouseDockRequest req, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        var (code, codeError) = WarehouseRules.NormalizeCode(req.Code);
        if (codeError is not null) errors["code"] = new[] { codeError };
        int? typeId = null;
        if (string.IsNullOrWhiteSpace(req.DockType)) errors["dockType"] = new[] { WarehouseRules.DockTypeRequiredMessage };
        else typeId = await LookupIdAsync(LookupDomains.DockType, req.DockType, "dockType", WarehouseRules.UnknownDockType, errors, ct);
        if (errors.Count > 0) throw new ValidationException(errors);

        var w = await ResolveWarehouseAsync(warehousePublicId, ct);
        EnsureWarehouseActive(w);
        if (await db.WarehouseDocks.AnyAsync(d => d.WarehouseId == w.WarehouseId && d.Code == code, ct))
            throw new ConflictException(WarehouseRules.DuplicateDockMessage);

        var initial = await statuses.GetInitialAsync(StatusDomains.DockStatus, ct);
        var dockId = await db.RunInTransactionAsync(async ct2 =>
        {
            var dock = new WarehouseDock
            {
                WarehouseId = w.WarehouseId, Code = code!, DockTypeLookupId = typeId!.Value, StatusCodeId = initial.StatusCodeId, IsActive = true,
            };
            db.WarehouseDocks.Add(dock);
            await db.SaveGuardedAsync(WarehouseRules.DuplicateDockMessage, ct2); // UQ_WarehouseDock (WarehouseId, Code)
            // Historial desde el inicio: null → FREE (etapa inicial del seed).
            var to = await statuses.TransitionAsync(StatusDomains.DockStatus, EntityTypes.WarehouseDock, dock.WarehouseDockId, null, initial.InternalCode, null, ct2);
            dock.StatusCodeId = to.StatusCodeId;
            await db.SaveGuardedAsync(WarehouseRules.DuplicateDockMessage, ct2);
            return dock.WarehouseDockId;
        }, ct);

        return await DockDtoAsync(w, dockId, ct);
    }

    /// <summary>PATCH de muelle: solo el tipo. 'code' en el cuerpo → 400.</summary>
    public async Task<WarehouseDockDto> UpdateDockAsync(Guid warehousePublicId, int dockId, WarehouseDockPatchRequest req, CancellationToken ct)
    {
        RejectImmutable(req.Extra, WarehouseRules.DockCodeImmutableMessage, "code", "warehouseId");
        var errors = new Dictionary<string, string[]>();
        int? typeId = null;
        if (req.DockType is not null)
        {
            if (string.IsNullOrWhiteSpace(req.DockType)) errors["dockType"] = new[] { WarehouseRules.DockTypeRequiredMessage };
            else typeId = await LookupIdAsync(LookupDomains.DockType, req.DockType, "dockType", WarehouseRules.UnknownDockType, errors, ct);
        }
        if (errors.Count > 0) throw new ValidationException(errors);

        var w = await ResolveWarehouseAsync(warehousePublicId, ct);
        EnsureWarehouseActive(w);
        var dock = await ResolveDockAsync(w, dockId, track: true, ct);
        if (typeId is int tid) dock.DockTypeLookupId = tid;
        await db.SaveGuardedAsync(WarehouseRules.DuplicateDockMessage, ct);
        return await DockDtoAsync(w, dock.WarehouseDockId, ct);
    }

    /// <summary>
    /// Estatus manual del muelle (FREE, OCCUPIED o MAINTENANCE) vía StatusService, con comentario en el historial
    /// WAREHOUSE_DOCK. Otro código → 400; muelle o almacén inactivo → 422; transición ilegal → 422 del motor.
    /// </summary>
    public async Task<WarehouseDockDto> SetDockStatusAsync(Guid warehousePublicId, int dockId, WarehouseDockStatusRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Status)) throw new ValidationException("status", WarehouseRules.DockStatusRequiredMessage);
        if (!WarehouseRules.IsManualDockStatus(req.Status)) throw new ValidationException("status", WarehouseRules.DockStatusNotManual(req.Status.Trim()));
        var toCode = req.Status.Trim().ToUpperInvariant();
        var comment = string.IsNullOrWhiteSpace(req.Comment) ? null : req.Comment.Trim();

        var w = await ResolveWarehouseAsync(warehousePublicId, ct);
        EnsureWarehouseActive(w);
        var dockRef = await ResolveDockAsync(w, dockId, track: false, ct);

        await db.RunInTransactionAsync(async ct2 =>
        {
            // Muelle bloqueado: serializa contra la llegada/cierre de citas (DockAppointmentStatusEffect) y la baja.
            var dock = await LockDockAsync(w.WarehouseId, dockRef.WarehouseDockId, ct2);
            if (!dock.IsActive) throw new StatusRuleException(WarehouseRules.DockInactiveMessage);
            var to = await statuses.TransitionAsync(StatusDomains.DockStatus, EntityTypes.WarehouseDock, dock.WarehouseDockId, dock.StatusCodeId, toCode, comment, ct2);
            dock.StatusCodeId = to.StatusCodeId;
            await db.SaveGuardedAsync(WarehouseRules.DuplicateDockMessage, ct2);
        }, ct);

        return await DockDtoAsync(w, dockRef.WarehouseDockId, ct);
    }

    /// <summary>Baja/reactivación de muelle. Desactivar con citas SCHEDULED/ARRIVED → 409 'El muelle tiene citas agendadas o en curso.'</summary>
    public async Task<WarehouseDockDto> SetDockActiveAsync(Guid warehousePublicId, int dockId, bool active, CancellationToken ct)
    {
        var w = await ResolveWarehouseAsync(warehousePublicId, ct);
        var dockRef = await ResolveDockAsync(w, dockId, track: false, ct);
        if (active) EnsureWarehouseActive(w);

        await db.RunInTransactionAsync(async ct2 =>
        {
            var dock = await LockDockAsync(w.WarehouseId, dockRef.WarehouseDockId, ct2);
            if (dock.IsActive == active) return;
            if (!active)
            {
                var ids = await StatusIdsAsync(StatusDomains.AppointmentStatus, ct2, AppointmentStatuses.Scheduled, AppointmentStatuses.Arrived);
                if (await db.DockAppointments.AnyAsync(a => a.WarehouseDockId == dock.WarehouseDockId && ids.Contains(a.StatusCodeId), ct2))
                    throw new ConflictException(WarehouseRules.DockHasAppointments);
            }
            dock.IsActive = active;
            await db.SaveGuardedAsync(WarehouseRules.DuplicateDockMessage, ct2);
        }, ct);

        return await DockDtoAsync(w, dockRef.WarehouseDockId, ct);
    }

    // ================================================================ helpers

    private static void EnsureWarehouseActive(Warehouse w)
    {
        if (!w.IsActive) throw new StatusRuleException(WarehouseRules.WarehouseInactiveMessage);
    }

    /// <summary>Campos inmutables que llegan en Extra ([JsonExtensionData]) → 400 con el mensaje de la entidad.</summary>
    private static void RejectImmutable(IDictionary<string, System.Text.Json.JsonElement>? extra, string message, params string[] fields)
    {
        if (extra is null) return;
        foreach (var key in extra.Keys)
        {
            var hit = fields.FirstOrDefault(f => string.Equals(f, key, StringComparison.OrdinalIgnoreCase));
            if (hit is not null) throw new ValidationException(hit, message);
        }
    }

    private static string? RequiredText(string? value, string field, string label, int max, IDictionary<string, string[]> errors)
    {
        var v = value?.Trim();
        if (string.IsNullOrEmpty(v)) { errors[field] = new[] { WarehouseRules.NameRequiredMessage }; return null; }
        if (v.Length > max) { errors[field] = new[] { WarehouseRules.TooLong(label, max) }; return null; }
        return v;
    }

    /// <summary>Parte de la posición en un PATCH: null = sin cambio; "" = quitar; otra = normalizada (400 si es inválida).</summary>
    private static string? Part(string? raw, string field, IDictionary<string, string[]> errors)
    {
        if (raw is null) return null;
        var (part, error) = WarehouseRules.NormalizeBinPart(raw);
        if (error is not null) errors[field] = new[] { error };
        return part;
    }

    /// <summary>Código de catálogo → id. null → null (sin cambio); "" → null (quitar); desconocido → error en el campo.</summary>
    private async Task<int?> LookupIdAsync(string domain, string? code, string field, Func<string, string> unknownMessage,
        IDictionary<string, string[]> errors, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        var id = await lookups.TryGetIdAsync(domain, code.Trim().ToUpperInvariant(), ct);
        if (id is null) errors[field] = new[] { unknownMessage(code.Trim()) };
        return id;
    }

    private async Task<string?> LookupCodeAsync(int? id, CancellationToken ct)
        => id is int v ? (await lookups.GetAsync(v, ct))?.InternalCode : null;

    private async Task<LookupCode?> LookupAsync(int? id, CancellationToken ct) => id is int v ? await lookups.GetAsync(v, ct) : null;

    private async Task<WarehouseZoneDto> ZoneDtoAsync(WarehouseZone z, ZoneStats? stats, CancellationToken ct)
        => ZoneDto(z, await LookupAsync(z.ZoneTypeLookupId, ct), tenant.Lang, stats);

    /// <summary>DTO de zona con su ocupación (sin estadísticas = zona sin posiciones: todo en cero). Lo comparte WarehouseService.</summary>
    public static WarehouseZoneDto ZoneDto(WarehouseZone z, LookupCode? type, string lang, ZoneStats? stats)
    {
        var s = stats ?? ZoneStats.Empty;
        return new WarehouseZoneDto(z.WarehouseZoneId, z.Code, z.Name, type?.InternalCode,
            type is null ? null : MultilingualText.Resolve(type.LabelJson, lang), z.IsActive, s.ActiveBins,
            s.OccupiedBins, s.CapacityQty, s.OnHandInCapacityBins, s.OnHand, s.BinsWithoutCapacity);
    }

    /// <summary>
    /// Ocupación de una zona (Lote 1 de cambios de Almacén). La capacidad de zona NO se guarda: es la suma del cupo de sus
    /// posiciones ACTIVAS con cupo; las posiciones sin cupo se cuentan aparte y quedan fuera del porcentaje. OnHand = existencia
    /// de todas las posiciones de la zona.
    /// </summary>
    public sealed class ZoneStats
    {
        public static readonly ZoneStats Empty = new();
        public int ZoneId { get; init; }
        public int ActiveBins { get; init; }
        public int OccupiedBins { get; init; }
        public long CapacityQty { get; init; }
        public decimal OnHandInCapacityBins { get; init; }
        public decimal OnHand { get; init; }
        public int BinsWithoutCapacity { get; init; }
    }

    /// <summary>
    /// UNA consulta agrupada con la ocupación de las zonas del almacén (o de una zona): posiciones LEFT JOIN existencia agrupada
    /// por posición, agrupado por zona. Público y estático para probar su traducción a SQL Server sin BD (ToQueryString).
    /// </summary>
    public static IQueryable<ZoneStats> ZoneStatsQuery(TeikemDbContext db, int warehouseId, int? zoneId)
    {
        var binStock = db.StockBalances.AsNoTracking()
            .Where(s => s.WarehouseId == warehouseId && s.WarehouseBinId != null)
            .GroupBy(s => s.WarehouseBinId)
            .Select(g => new { BinId = g.Key, OnHand = g.Sum(s => s.QtyOnHand) });
        var bins = from b in db.WarehouseBins.AsNoTracking()
                   where b.WarehouseId == warehouseId && (zoneId == null || b.WarehouseZoneId == zoneId)
                   join st in binStock on (int?)b.WarehouseBinId equals st.BinId into sj
                   from st in sj.DefaultIfEmpty()
                   select new { b.WarehouseZoneId, b.IsActive, b.MaxCapacityQty, OnHand = (decimal?)st!.OnHand ?? 0m };
        return bins.GroupBy(x => x.WarehouseZoneId).Select(g => new ZoneStats
        {
            ZoneId = g.Key,
            ActiveBins = g.Count(x => x.IsActive),
            OccupiedBins = g.Count(x => x.IsActive && x.OnHand > 0),
            CapacityQty = g.Sum(x => x.IsActive && x.MaxCapacityQty != null ? (long)x.MaxCapacityQty.Value : 0L),
            OnHandInCapacityBins = g.Sum(x => x.IsActive && x.MaxCapacityQty != null ? x.OnHand : 0m),
            OnHand = g.Sum(x => x.OnHand),
            BinsWithoutCapacity = g.Count(x => x.IsActive && x.MaxCapacityQty == null),
        });
    }

    /// <summary>Ocupación por zona (ZoneStatsQuery) indexada por WarehouseZoneId. Zonas sin posiciones no aparecen (→ Empty).</summary>
    public static async Task<Dictionary<int, ZoneStats>> ZoneStatsAsync(TeikemDbContext db, int warehouseId, int? zoneId, CancellationToken ct)
        => await ZoneStatsQuery(db, warehouseId, zoneId).ToDictionaryAsync(s => s.ZoneId, ct);

    private async Task<WarehouseBinDto> BinDtoAsync(int warehouseId, int binId, CancellationToken ct)
    {
        var row = await BinRowsQuery(db, warehouseId, new WarehouseBinQuery(IncludeInactive: true, BinIds: new[] { binId }), null, null)
            .FirstAsync(ct);
        return (await BinDtosAsync(new[] { row }, ct))[0];
    }

    private async Task<WarehouseDockDto> DockDtoAsync(Warehouse w, int dockId, CancellationToken ct)
    {
        var dock = await db.WarehouseDocks.AsNoTracking().FirstAsync(d => d.WarehouseId == w.WarehouseId && d.WarehouseDockId == dockId, ct);
        return await DockDtoAsync(dock, await StatusMapAsync(StatusDomains.DockStatus, ct), ct);
    }

    private async Task<WarehouseDockDto> DockDtoAsync(WarehouseDock d, Dictionary<int, StatusInfo> statusMap, CancellationToken ct)
    {
        var type = await lookups.GetAsync(d.DockTypeLookupId, ct);
        var s = statusMap.GetValueOrDefault(d.StatusCodeId);
        return new WarehouseDockDto(d.WarehouseDockId, d.Code, type?.InternalCode ?? "",
            type is null ? "" : MultilingualText.Resolve(type.LabelJson, tenant.Lang), s?.Code ?? "", s?.Label ?? "", s?.Color, d.IsActive);
    }

    private async Task<List<int>> StatusIdsAsync(string domain, CancellationToken ct, params string[] codes)
        => await db.StatusCodes.AsNoTracking()
            .Where(s => s.Entity == domain && codes.Contains(s.InternalCode))
            .Select(s => s.StatusCodeId)
            .ToListAsync(ct);

    /// <summary>Estatus del dominio con la etiqueta y el color personalizados del tenant si existen.</summary>
    private async Task<Dictionary<int, StatusInfo>> StatusMapAsync(string domain, CancellationToken ct)
    {
        var codes = await db.StatusCodes.AsNoTracking().Where(s => s.Entity == domain).ToListAsync(ct);
        var ids = codes.Select(c => c.StatusCodeId).ToList();
        var overrides = tenant.TenantId is null
            ? new Dictionary<int, StatusCodeOverride>()
            : await db.StatusCodeOverrides.AsNoTracking().Where(o => ids.Contains(o.StatusCodeId)).ToDictionaryAsync(o => o.StatusCodeId, ct);
        return codes.ToDictionary(c => c.StatusCodeId, c =>
        {
            var o = overrides.GetValueOrDefault(c.StatusCodeId);
            return new StatusInfo(c.InternalCode,
                MultilingualText.Resolve(MultilingualText.Merge(c.LabelJson, o?.CustomLabelJson), tenant.Lang),
                o?.CustomColorHex ?? c.ColorHex);
        });
    }

    // ================================================================ adaptadores a las costuras de P0 (WmsResolve, InventoryQueries)
    // Toda dependencia de las firmas de WmsResolve e InventoryQueries vive aquí. Las hijas se resuelven con el almacén ya
    // resuelto bajo el filtro de tenant: 404 'Zona no encontrada.' / 'Posición no encontrada.' / 'Muelle no encontrado.'

    private Task<Warehouse> ResolveWarehouseAsync(Guid publicId, CancellationToken ct)
        => db.ResolveWarehouseAsync(publicId, false, ct);

    private Task<WarehouseZone> ResolveZoneAsync(Warehouse w, int zoneId, bool track, CancellationToken ct)
        => db.ResolveZoneAsync(w.WarehouseId, zoneId, track, ct);

    private Task<WarehouseBin> ResolveBinAsync(Warehouse w, int binId, bool track, CancellationToken ct)
        => db.ResolveBinAsync(w.WarehouseId, binId, track, ct);

    private Task<WarehouseDock> ResolveDockAsync(Warehouse w, int dockId, bool track, CancellationToken ct)
        => db.ResolveDockAsync(w.WarehouseId, dockId, track, ct);

    /// <summary>Encabezado Warehouse con UPDLOCK (paso 1 del orden de bloqueo).</summary>
    private Task<Warehouse> LockWarehouseAsync(int warehouseId, CancellationToken ct)
        => db.LockWarehouseAsync(warehouseId, ct);

    /// <summary>Muelle con UPDLOCK, con JOIN al almacén del tenant (último encabezado del orden de bloqueo), tracked.</summary>
    private Task<WarehouseDock> LockDockAsync(int warehouseId, int dockId, CancellationToken ct)
        => db.LockDockAsync(warehouseId, dockId, ct);

    /// <summary>Rango de saldos de la posición con HOLDLOCK (paso 3 del orden de bloqueo).</summary>
    private async Task<IReadOnlyList<StockBalance>> LockBinBalancesAsync(int binId, CancellationToken ct)
        => await db.LockBalancesByBinAsync(binId, ct);
}
