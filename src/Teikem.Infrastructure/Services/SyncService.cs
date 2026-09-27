using System.Text;
using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Clients;
using Teikem.Domain.Constants;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Contracts
{
    // ------------------------------------------------------------------------------------------------------------------
    // Lote 8A (P2) — DTOs compactos de la sincronización por diferencia del aparato de almacén. Firma posicional FIJA.
    // SyncPage<T> y SyncQuery viven en Contracts/SyncContracts.cs (P0).
    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>Producto compacto para la base local del aparato (búsqueda por código de barras o SKU sin red).</summary>
    public sealed record SyncProductDto(int Id, Guid PublicId, string Sku, string Name, string? Barcode, string TrackingTypeCode,
        string BaseUomCode, int? CategoryId, Guid? OwnerClientPublicId, string? OwnerName, int? PreferredBinId, bool IsActive);

    public sealed record SyncProductCategoryDto(int Id, string Name, int? ParentId, bool IsActive);

    /// <summary>Posición con su zona. IsActive = posición, zona y almacén activos (si no, el aparato la borra).</summary>
    public sealed record SyncBinDto(int Id, string Code, Guid WarehousePublicId, int ZoneId, string ZoneCode, string ZoneName,
        string? ZoneTypeCode, string? Aisle, string? Rack, string? Level, string? Position, bool IsActive);

    public sealed record SyncPurchaseOrderLineDto(int Id, Guid ProductPublicId, string Sku, string ProductName, decimal QtyOrdered,
        decimal QtyReceived, decimal QtyPending);

    /// <summary>Orden de compra. IsActive = activa y abierta (DRAFT, SENT o PARTIAL); cerrada o cancelada → false.</summary>
    public sealed record SyncPurchaseOrderDto(int Id, Guid PublicId, string Number, Guid WarehousePublicId, string SupplierName,
        string StatusCode, DateOnly? ExpectedDate, bool IsActive, IReadOnlyList<SyncPurchaseOrderLineDto> Lines);

    public sealed record SyncAsnLineDto(int Id, Guid ProductPublicId, string Sku, string ProductName, decimal ExpectedQty, string? LotNumber,
        int? PurchaseOrderLineId);

    /// <summary>Aviso de llegada. IsActive = activo y EXPECTED; recibido o cancelado → false.</summary>
    public sealed record SyncAsnDto(int Id, Guid WarehousePublicId, Guid? ClientPublicId, string? ClientName, Guid? PurchaseOrderPublicId,
        string? PurchaseOrderNumber, string? Reference, DateOnly? ExpectedDate, string StatusCode, bool IsActive, IReadOnlyList<SyncAsnLineDto> Lines);

    /// <summary>Tarea de la cola. IsActive = abierta (PENDING o IN_PROGRESS); DONE o CANCELLED → false.</summary>
    public sealed record SyncWarehouseTaskDto(int Id, string TypeCode, string StatusCode, Guid WarehousePublicId, Guid? ProductPublicId,
        string? Sku, string? ProductName, int? LotId, string? LotNumber, decimal? Quantity, int? FromBinId, string? FromBinCode, int? ToBinId,
        string? ToBinCode, string? RefEntityCode, int? RefId, int? AssignedToUserId, int Priority, DateTime CreatedAtUtc, bool IsActive);
}

namespace Teikem.Infrastructure.Services
{
    /// <summary>
    /// Lote 8A (P2) — reglas puras de la sincronización por diferencia: tope de página, cursor opaco (último id) y marca de
    /// agua (serverTimeUtc − 5 minutos).
    /// </summary>
    public static class SyncRules
    {
        public const int MaxTake = 500;
        public const int DefaultTake = 500;
        public const string TakeTooLarge = "El máximo por página es 500.";
        public const string InvalidCursor = "El cursor no es válido.";
        /// <summary>Margen de seguridad de la marca de agua: cubre relojes desfasados y transacciones largas en curso.</summary>
        public static readonly TimeSpan SafetyWindow = TimeSpan.FromMinutes(5);

        private const string CursorPrefix = "c1:";

        /// <summary>Tamaño de página: null o ≤ 0 → 500; más de 500 → error 'El máximo por página es 500.'.</summary>
        public static (int Take, string? Error) ResolveTake(int? take)
        {
            if (take is null or <= 0) return (DefaultTake, null);
            return take > MaxTake ? (0, TakeTooLarge) : (take.Value, null);
        }

        /// <summary>Cursor opaco (base64url de 'c1:{id}'): el aparato lo devuelve tal cual; no debe interpretarlo.</summary>
        public static string EncodeCursor(int lastId)
            => Convert.ToBase64String(Encoding.UTF8.GetBytes(CursorPrefix + lastId.ToString(System.Globalization.CultureInfo.InvariantCulture)))
                .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        /// <summary>Último id del cursor (null = primera página); un cursor que no salió de EncodeCursor → error.</summary>
        public static (int? LastId, string? Error) DecodeCursor(string? cursor)
        {
            if (string.IsNullOrWhiteSpace(cursor)) return (null, null);
            var s = cursor.Trim().Replace('-', '+').Replace('_', '/');
            if (s.Length > 64) return (null, InvalidCursor);
            s = s.PadRight(s.Length + (4 - s.Length % 4) % 4, '=');
            string text;
            try { text = Encoding.UTF8.GetString(Convert.FromBase64String(s)); }
            catch (FormatException) { return (null, InvalidCursor); }
            if (!text.StartsWith(CursorPrefix, StringComparison.Ordinal)) return (null, InvalidCursor);
            var digits = text[CursorPrefix.Length..];
            if (digits.Length == 0 || !digits.All(char.IsAsciiDigit)) return (null, InvalidCursor);
            return int.TryParse(digits, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var id) && id >= 0
                ? (id, null)
                : (null, InvalidCursor);
        }

        /// <summary>Since normalizado a UTC (Local → UTC; sin zona se toma como UTC). null = carga completa.</summary>
        public static DateTime? NormalizeSince(DateTime? since) => since switch
        {
            null => null,
            { Kind: DateTimeKind.Utc } s => s,
            { Kind: DateTimeKind.Local } s => s.ToUniversalTime(),
            var s => DateTime.SpecifyKind(s.Value, DateTimeKind.Utc),
        };

        /// <summary>Marca de agua de la siguiente pasada: serverTimeUtc de la primera página menos 5 minutos.</summary>
        public static DateTime NextSince(DateTime serverTimeUtc) => serverTimeUtc - SafetyWindow;

        /// <summary>
        /// Corta la página: se piden take + 1 filas en orden de id; si llegaron más de take hay página siguiente y el cursor es el
        /// id de la última devuelta.
        /// </summary>
        public static (IReadOnlyList<T> Items, string? NextCursor) Cut<T>(IReadOnlyList<T> fetched, int take, Func<T, int> id)
        {
            if (fetched.Count <= take) return (fetched, null);
            var items = fetched.Take(take).ToList();
            return (items, EncodeCursor(id(items[^1])));
        }
    }

    /// <summary>
    /// Lote 8A (P2) — sincronización por diferencia para el aparato de almacén (base local SQLite): productos, categorías,
    /// posiciones, órdenes de compra y avisos abiertos y tareas de la cola. Sin since = carga completa (solo lo vigente); con
    /// since = todo lo que cambió desde entonces, INCLUIDO lo dado de baja o cerrado (IsActive = false) para que el aparato lo
    /// borre. Paginación por cursor opaco (último id, ascendente), take ≤ 500.
    ///
    /// Detección de cambios (decisión a revisar en el cierre del lote): estas tablas no tienen UpdatedAtUtc; en lugar de
    /// agregarlo (tocaría estructura, entidades e interceptor compartidos) se usan las bitácoras que YA se escriben en cada
    /// cambio: AuditLog (interceptor de auditoría, por EntityType) y EntityStatusHistory (StatusService), más CreatedAtUtc /
    /// CompletedAtUtc donde existen. Las hijas se auditan bajo el EntityType del padre con SU propio id, así que la búsqueda
    /// es deliberadamente amplia (id del padre O id de una hija): puede devolver de más, nunca de menos. Las tareas abiertas
    /// se devuelven siempre (su asignación no deja bitácora). Todo se lee bajo el filtro global de tenant; las tablas sin
    /// TenantId (posición, zona, líneas) se alcanzan SOLO uniendo con su padre filtrado.
    /// </summary>
    public sealed class SyncService(TeikemDbContext db, ILookupCache lookups)
    {
        private static readonly string[] OpenPurchaseOrderStatuses =
            { PurchaseOrderStatuses.Draft, PurchaseOrderStatuses.Sent, PurchaseOrderStatuses.Partial };
        private static readonly string[] OpenTaskStatuses = { WarehouseTaskStatuses.Pending, WarehouseTaskStatuses.InProgress };

        private sealed record Window(int Take, DateTime? Since, int? AfterId, DateTime Now);

        /// <summary>Ids con bitácora (AuditLog o historial de estatus) del EntityType desde since; subconsultas, no se materializan.</summary>
        private sealed record Changed(IQueryable<int> Audit, IQueryable<int> History);

        // ================================================================ productos y categorías

        public async Task<SyncPage<SyncProductDto>> ProductsAsync(SyncQuery q, CancellationToken ct)
        {
            var w = Prepare(q);
            var query = db.Set<Product>().AsNoTracking();
            if (w.Since is DateTime since)
            {
                var c = await ChangedAsync(EntityTypes.Product, since, ct);
                query = query.Where(p => c.Audit.Contains(p.ProductId) || c.History.Contains(p.ProductId));
            }
            else query = query.Where(p => p.IsActive);
            if (w.AfterId is int after) query = query.Where(p => p.ProductId > after);

            var rows = await query.OrderBy(p => p.ProductId).Take(w.Take + 1).ToListAsync(ct);
            var (page, next) = SyncRules.Cut(rows, w.Take, p => p.ProductId);
            var clientIds = page.Where(p => p.ClientId != null).Select(p => p.ClientId!.Value).Distinct().ToList();
            var owners = await db.Set<Client>().AsNoTracking().Where(c => clientIds.Contains(c.ClientId))
                .Select(c => new { c.ClientId, c.PublicId, c.Name }).ToDictionaryAsync(c => c.ClientId, ct);
            var codes = await LookupCodesAsync(page.SelectMany(p => new[] { p.TrackingTypeLookupId, p.BaseUomLookupId }), ct);

            var items = page.Select(p =>
            {
                var owner = p.ClientId is int cid ? owners.GetValueOrDefault(cid) : null;
                return new SyncProductDto(p.ProductId, p.PublicId, p.Sku, p.Name, p.Barcode,
                    codes.GetValueOrDefault(p.TrackingTypeLookupId, TrackingTypes.None), codes.GetValueOrDefault(p.BaseUomLookupId, string.Empty),
                    p.ProductCategoryId, owner?.PublicId, owner?.Name, p.PreferredBinId, p.IsActive);
            }).ToList();
            return new SyncPage<SyncProductDto>(items, next, w.Now);
        }

        public async Task<SyncPage<SyncProductCategoryDto>> ProductCategoriesAsync(SyncQuery q, CancellationToken ct)
        {
            var w = Prepare(q);
            var query = db.Set<ProductCategory>().AsNoTracking();
            if (w.Since is DateTime since)
            {
                var c = await ChangedAsync(EntityTypes.ProductCategory, since, ct);
                query = query.Where(x => c.Audit.Contains(x.ProductCategoryId) || c.History.Contains(x.ProductCategoryId));
            }
            else query = query.Where(x => x.IsActive);
            if (w.AfterId is int after) query = query.Where(x => x.ProductCategoryId > after);

            var rows = await query.OrderBy(x => x.ProductCategoryId).Take(w.Take + 1)
                .Select(x => new SyncProductCategoryDto(x.ProductCategoryId, x.Name, x.ParentId, x.IsActive)).ToListAsync(ct);
            var (items, next) = SyncRules.Cut(rows, w.Take, x => x.Id);
            return new SyncPage<SyncProductCategoryDto>(items, next, w.Now);
        }

        // ================================================================ posiciones

        public async Task<SyncPage<SyncBinDto>> BinsAsync(SyncQuery q, CancellationToken ct)
        {
            var w = Prepare(q);
            var warehouseId = await WarehouseIdAsync(q.WarehousePublicId, ct);

            // La posición y la zona no tienen TenantId: se alcanzan SOLO por su almacén filtrado.
            var query = from b in db.Set<WarehouseBin>().AsNoTracking()
                        join z in db.Set<WarehouseZone>().AsNoTracking() on b.WarehouseZoneId equals z.WarehouseZoneId
                        join wh in db.Set<Warehouse>().AsNoTracking() on b.WarehouseId equals wh.WarehouseId
                        select new { Bin = b, Zone = z, Warehouse = wh };
            if (warehouseId is int whId) query = query.Where(x => x.Warehouse.WarehouseId == whId);
            if (w.Since is DateTime since)
            {
                // Zona y posición se auditan bajo WAREHOUSE con su propio id: cambia la posición, su zona o su almacén.
                var c = await ChangedAsync(EntityTypes.Warehouse, since, ct);
                query = query.Where(x => c.Audit.Contains(x.Bin.WarehouseBinId) || c.Audit.Contains(x.Zone.WarehouseZoneId)
                                         || c.Audit.Contains(x.Warehouse.WarehouseId) || c.History.Contains(x.Warehouse.WarehouseId));
            }
            else query = query.Where(x => x.Bin.IsActive && x.Zone.IsActive && x.Warehouse.IsActive);
            if (w.AfterId is int after) query = query.Where(x => x.Bin.WarehouseBinId > after);

            var rows = await query.OrderBy(x => x.Bin.WarehouseBinId).Take(w.Take + 1).ToListAsync(ct);
            var (page, next) = SyncRules.Cut(rows, w.Take, x => x.Bin.WarehouseBinId);
            var zoneTypes = await LookupCodesAsync(page.Where(x => x.Zone.ZoneTypeLookupId != null).Select(x => x.Zone.ZoneTypeLookupId!.Value), ct);
            var items = page.Select(x => new SyncBinDto(x.Bin.WarehouseBinId, x.Bin.Code, x.Warehouse.PublicId, x.Zone.WarehouseZoneId, x.Zone.Code,
                x.Zone.Name, x.Zone.ZoneTypeLookupId is int zt ? zoneTypes.GetValueOrDefault(zt) : null, x.Bin.Aisle, x.Bin.Rack, x.Bin.Level,
                x.Bin.Position, x.Bin.IsActive && x.Zone.IsActive && x.Warehouse.IsActive)).ToList();
            return new SyncPage<SyncBinDto>(items, next, w.Now);
        }

        // ================================================================ órdenes de compra abiertas

        public async Task<SyncPage<SyncPurchaseOrderDto>> PurchaseOrdersAsync(SyncQuery q, CancellationToken ct)
        {
            var w = Prepare(q);
            var warehouseId = await WarehouseIdAsync(q.WarehousePublicId, ct);
            var statuses = await StatusMapAsync(StatusDomains.PurchaseOrderStatus, ct);
            var openIds = statuses.Where(kv => OpenPurchaseOrderStatuses.Contains(kv.Value)).Select(kv => kv.Key).ToList();

            var query = db.Set<PurchaseOrder>().AsNoTracking();
            if (warehouseId is int whId) query = query.Where(p => p.WarehouseId == whId);
            if (w.Since is DateTime since)
            {
                // Encabezado, líneas (lo recibido) y resoluciones de faltantes se auditan bajo PURCHASE_ORDER con su propio id.
                var c = await ChangedAsync(EntityTypes.PurchaseOrder, since, ct);
                query = query.Where(p => p.CreatedAtUtc >= since || c.Audit.Contains(p.PurchaseOrderId) || c.History.Contains(p.PurchaseOrderId)
                    || db.Set<PurchaseOrderLine>().Any(l => l.PurchaseOrderId == p.PurchaseOrderId && c.Audit.Contains(l.PurchaseOrderLineId))
                    || db.Set<PurchaseOrderShortageResolution>().Any(r => r.PurchaseOrderId == p.PurchaseOrderId && r.CreatedAtUtc >= since));
            }
            else query = query.Where(p => p.IsActive && openIds.Contains(p.StatusCodeId));
            if (w.AfterId is int after) query = query.Where(p => p.PurchaseOrderId > after);

            var rows = await query.OrderBy(p => p.PurchaseOrderId).Take(w.Take + 1).ToListAsync(ct);
            var (page, next) = SyncRules.Cut(rows, w.Take, p => p.PurchaseOrderId);
            var poIds = page.Select(p => p.PurchaseOrderId).ToList();
            var warehouses = await WarehousePublicIdsAsync(page.Select(p => p.WarehouseId), ct);
            var supplierIds = page.Select(p => p.SupplierId).Distinct().ToList();
            var suppliers = await db.Set<Supplier>().AsNoTracking().Where(s => supplierIds.Contains(s.SupplierId))
                .ToDictionaryAsync(s => s.SupplierId, s => s.Name, ct);
            var lines = await (from l in db.Set<PurchaseOrderLine>().AsNoTracking()
                               join p in db.Set<Product>().AsNoTracking() on l.ProductId equals p.ProductId
                               where poIds.Contains(l.PurchaseOrderId)
                               orderby l.PurchaseOrderLineId
                               select new { l.PurchaseOrderId, l.PurchaseOrderLineId, p.PublicId, p.Sku, p.Name, l.QtyOrdered, l.QtyReceived })
                .ToListAsync(ct);
            var resolved = await PurchasingSupport.ResolvedByLineAsync(db, poIds, ct);
            var linesByPo = lines.ToLookup(l => l.PurchaseOrderId);

            var items = page.Select(p =>
            {
                var code = statuses.GetValueOrDefault(p.StatusCodeId, string.Empty);
                var poLines = linesByPo[p.PurchaseOrderId].Select(l => new SyncPurchaseOrderLineDto(l.PurchaseOrderLineId, l.PublicId, l.Sku, l.Name,
                    l.QtyOrdered, l.QtyReceived, ShortageRules.Pending(l.QtyOrdered, l.QtyReceived, resolved.GetValueOrDefault(l.PurchaseOrderLineId))))
                    .ToList();
                return new SyncPurchaseOrderDto(p.PurchaseOrderId, p.PublicId, p.Number, warehouses.GetValueOrDefault(p.WarehouseId),
                    suppliers.GetValueOrDefault(p.SupplierId, string.Empty), code, p.ExpectedDate,
                    p.IsActive && OpenPurchaseOrderStatuses.Contains(code), poLines);
            }).ToList();
            return new SyncPage<SyncPurchaseOrderDto>(items, next, w.Now);
        }

        // ================================================================ avisos de llegada EXPECTED

        public async Task<SyncPage<SyncAsnDto>> AsnsAsync(SyncQuery q, CancellationToken ct)
        {
            var w = Prepare(q);
            var warehouseId = await WarehouseIdAsync(q.WarehousePublicId, ct);
            var statuses = await StatusMapAsync(StatusDomains.AsnStatus, ct);
            var expectedId = statuses.Where(kv => kv.Value == AsnStatuses.Expected).Select(kv => kv.Key).FirstOrDefault();

            var query = db.Set<Asn>().AsNoTracking();
            if (warehouseId is int whId) query = query.Where(a => a.WarehouseId == whId);
            if (w.Since is DateTime since)
            {
                var c = await ChangedAsync(EntityTypes.Asn, since, ct);
                query = query.Where(a => a.CreatedAtUtc >= since || c.Audit.Contains(a.AsnId) || c.History.Contains(a.AsnId)
                    || db.Set<AsnLine>().Any(l => l.AsnId == a.AsnId && c.Audit.Contains(l.AsnLineId)));
            }
            else query = query.Where(a => a.IsActive && a.StatusCodeId == expectedId);
            if (w.AfterId is int after) query = query.Where(a => a.AsnId > after);

            var rows = await query.OrderBy(a => a.AsnId).Take(w.Take + 1).ToListAsync(ct);
            var (page, next) = SyncRules.Cut(rows, w.Take, a => a.AsnId);
            var asnIds = page.Select(a => a.AsnId).ToList();
            var warehouses = await WarehousePublicIdsAsync(page.Select(a => a.WarehouseId), ct);
            var clientIds = page.Where(a => a.ClientId != null).Select(a => a.ClientId!.Value).Distinct().ToList();
            var clients = await db.Set<Client>().AsNoTracking().Where(c => clientIds.Contains(c.ClientId))
                .Select(c => new { c.ClientId, c.PublicId, c.Name }).ToDictionaryAsync(c => c.ClientId, ct);
            var poIds = page.Where(a => a.PurchaseOrderId != null).Select(a => a.PurchaseOrderId!.Value).Distinct().ToList();
            var pos = await db.Set<PurchaseOrder>().AsNoTracking().Where(p => poIds.Contains(p.PurchaseOrderId))
                .Select(p => new { p.PurchaseOrderId, p.PublicId, p.Number }).ToDictionaryAsync(p => p.PurchaseOrderId, ct);
            var lines = await (from l in db.Set<AsnLine>().AsNoTracking()
                               join p in db.Set<Product>().AsNoTracking() on l.ProductId equals p.ProductId
                               where asnIds.Contains(l.AsnId)
                               orderby l.AsnLineId
                               select new SyncAsnLineRow(l.AsnId, new SyncAsnLineDto(l.AsnLineId, p.PublicId, p.Sku, p.Name, l.ExpectedQty, l.LotNumber,
                                   l.PurchaseOrderLineId)))
                .ToListAsync(ct);
            var linesByAsn = lines.ToLookup(l => l.AsnId, l => l.Line);

            var items = page.Select(a =>
            {
                var client = a.ClientId is int cid ? clients.GetValueOrDefault(cid) : null;
                var po = a.PurchaseOrderId is int pid ? pos.GetValueOrDefault(pid) : null;
                var code = statuses.GetValueOrDefault(a.StatusCodeId, string.Empty);
                return new SyncAsnDto(a.AsnId, warehouses.GetValueOrDefault(a.WarehouseId), client?.PublicId, client?.Name, po?.PublicId, po?.Number,
                    a.Reference, a.ExpectedDate, code, a.IsActive && code == AsnStatuses.Expected, linesByAsn[a.AsnId].ToList());
            }).ToList();
            return new SyncPage<SyncAsnDto>(items, next, w.Now);
        }

        private sealed record SyncAsnLineRow(int AsnId, SyncAsnLineDto Line);

        // ================================================================ tareas de la cola

        public async Task<SyncPage<SyncWarehouseTaskDto>> WarehouseTasksAsync(SyncQuery q, CancellationToken ct)
        {
            var w = Prepare(q);
            var warehouseId = await WarehouseIdAsync(q.WarehousePublicId, ct);
            var statuses = await StatusMapAsync(StatusDomains.WarehouseTaskStatus, ct);
            var openIds = statuses.Where(kv => OpenTaskStatuses.Contains(kv.Value)).Select(kv => kv.Key).ToList();

            var query = db.Set<WarehouseTask>().AsNoTracking();
            if (warehouseId is int whId) query = query.Where(t => t.WarehouseId == whId);
            if (w.Since is DateTime since)
            {
                // Las abiertas van siempre (asignar o cambiar destino no deja bitácora); las cerradas desde since, para borrarlas.
                var c = await ChangedAsync(EntityTypes.WarehouseTask, since, ct);
                query = query.Where(t => openIds.Contains(t.StatusCodeId) || t.CreatedAtUtc >= since
                    || (t.CompletedAtUtc != null && t.CompletedAtUtc >= since) || c.History.Contains(t.WarehouseTaskId));
            }
            else query = query.Where(t => openIds.Contains(t.StatusCodeId));
            if (w.AfterId is int after) query = query.Where(t => t.WarehouseTaskId > after);

            var rows = await query.OrderBy(t => t.WarehouseTaskId).Take(w.Take + 1).ToListAsync(ct);
            var (page, next) = SyncRules.Cut(rows, w.Take, t => t.WarehouseTaskId);
            var warehouses = await WarehousePublicIdsAsync(page.Select(t => t.WarehouseId), ct);
            var productIds = page.Where(t => t.ProductId != null).Select(t => t.ProductId!.Value).Distinct().ToList();
            var products = await db.Set<Product>().AsNoTracking().Where(p => productIds.Contains(p.ProductId))
                .Select(p => new { p.ProductId, p.PublicId, p.Sku, p.Name }).ToDictionaryAsync(p => p.ProductId, ct);
            // Lote y posición sin TenantId: por su producto / almacén filtrados.
            var lotIds = page.Where(t => t.LotId != null).Select(t => t.LotId!.Value).Distinct().ToList();
            var lots = await (from l in db.Set<InventoryLot>().AsNoTracking()
                              join p in db.Set<Product>().AsNoTracking() on l.ProductId equals p.ProductId
                              where lotIds.Contains(l.LotId)
                              select new { l.LotId, l.LotNumber }).ToDictionaryAsync(l => l.LotId, l => l.LotNumber, ct);
            var binIds = page.SelectMany(t => new[] { t.FromBinId, t.ToBinId }).Where(b => b != null).Select(b => b!.Value).Distinct().ToList();
            var bins = await (from b in db.Set<WarehouseBin>().AsNoTracking()
                              join wh in db.Set<Warehouse>().AsNoTracking() on b.WarehouseId equals wh.WarehouseId
                              where binIds.Contains(b.WarehouseBinId)
                              select new { b.WarehouseBinId, b.Code }).ToDictionaryAsync(b => b.WarehouseBinId, b => b.Code, ct);
            var codes = await LookupCodesAsync(page.Select(t => t.TaskTypeLookupId)
                .Concat(page.Where(t => t.RefEntityLookupId != null).Select(t => t.RefEntityLookupId!.Value)), ct);

            var items = page.Select(t =>
            {
                var product = t.ProductId is int pid ? products.GetValueOrDefault(pid) : null;
                var code = statuses.GetValueOrDefault(t.StatusCodeId, string.Empty);
                return new SyncWarehouseTaskDto(t.WarehouseTaskId, codes.GetValueOrDefault(t.TaskTypeLookupId, string.Empty), code,
                    warehouses.GetValueOrDefault(t.WarehouseId), product?.PublicId, product?.Sku, product?.Name, t.LotId,
                    t.LotId is int lid ? lots.GetValueOrDefault(lid) : null, t.Quantity, t.FromBinId,
                    t.FromBinId is int fb ? bins.GetValueOrDefault(fb) : null, t.ToBinId, t.ToBinId is int tb ? bins.GetValueOrDefault(tb) : null,
                    t.RefEntityLookupId is int re ? codes.GetValueOrDefault(re) : null, t.RefId, t.AssignedToUserId, t.Priority, t.CreatedAtUtc,
                    OpenTaskStatuses.Contains(code));
            }).ToList();
            return new SyncPage<SyncWarehouseTaskDto>(items, next, w.Now);
        }

        // ================================================================ apoyo

        /// <summary>Valida take y cursor (400) y fija la hora del servidor ANTES de leer (la marca de agua nunca se adelanta).</summary>
        private static Window Prepare(SyncQuery? q)
        {
            q ??= new SyncQuery();
            var now = DateTime.UtcNow;
            var (take, takeError) = SyncRules.ResolveTake(q.Take);
            if (takeError is not null) throw new ValidationException("take", takeError);
            var (afterId, cursorError) = SyncRules.DecodeCursor(q.Cursor);
            if (cursorError is not null) throw new ValidationException("cursor", cursorError);
            return new Window(take, SyncRules.NormalizeSince(q.Since), afterId, now);
        }

        private async Task<Changed> ChangedAsync(string entityTypeCode, DateTime since, CancellationToken ct)
        {
            var typeId = await lookups.GetIdAsync(LookupDomains.EntityType, entityTypeCode, ct);
            var audit = db.AuditLogs.AsNoTracking().Where(a => a.EntityTypeLookupId == typeId && a.CreatedAtUtc >= since).Select(a => a.EntityId);
            var history = db.EntityStatusHistories.AsNoTracking().Where(h => h.EntityTypeLookupId == typeId && h.ChangedAtUtc >= since)
                .Select(h => h.EntityId);
            return new Changed(audit, history);
        }

        /// <summary>Id del almacén por PublicId (filtro de tenant; otro tenant o inexistente → 404 'Almacén no encontrado.').</summary>
        private async Task<int?> WarehouseIdAsync(Guid? publicId, CancellationToken ct)
        {
            if (publicId is not Guid pid) return null;
            return await db.Set<Warehouse>().AsNoTracking().Where(w => w.PublicId == pid).Select(w => (int?)w.WarehouseId).FirstOrDefaultAsync(ct)
                   ?? throw new NotFoundException("Almacén");
        }

        private async Task<Dictionary<int, Guid>> WarehousePublicIdsAsync(IEnumerable<int> warehouseIds, CancellationToken ct)
        {
            var ids = warehouseIds.Distinct().ToList();
            return await db.Set<Warehouse>().AsNoTracking().Where(w => ids.Contains(w.WarehouseId))
                .ToDictionaryAsync(w => w.WarehouseId, w => w.PublicId, ct);
        }

        private async Task<Dictionary<int, string>> StatusMapAsync(string domain, CancellationToken ct)
            => await db.StatusCodes.AsNoTracking().Where(s => s.Entity == domain)
                .ToDictionaryAsync(s => s.StatusCodeId, s => s.InternalCode, ct);

        private async Task<Dictionary<int, string>> LookupCodesAsync(IEnumerable<int> lookupIds, CancellationToken ct)
        {
            var map = new Dictionary<int, string>();
            foreach (var id in lookupIds.Distinct())
            {
                var code = await lookups.GetAsync(id, ct);
                if (code is not null) map[id] = code.InternalCode;
            }
            return map;
        }
    }
}
