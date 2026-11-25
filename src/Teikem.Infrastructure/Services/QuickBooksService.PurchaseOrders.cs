using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Catalogs;
using Teikem.Domain.Common;
using Teikem.Domain.Constants;
using Teikem.Domain.Contacts;
using Teikem.Domain.Sales;
using Teikem.Domain.Tax;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 246 (Lote F del plan de Solutions) — QuickBooks Desktop de COMPRAS: las órdenes de compra por pasar, el archivo IIF (TRNS PURCHORD) y las marcas. Todo exige
/// purchasing.quickbooks. «Por pasar» = orden activa, que no vino de QuickBooks (origen TEIKEM, EXCEL o NULL), enviada, recibida en parte o recibida, sin la marca «en
/// QuickBooks». Exportar (una, varias o todas) anota la fecha del archivo y quién lo sacó, pero NO marca: queda «exportada, por confirmar» hasta «Sí, ya están» (confirm) o
/// «No pasó» (vuelve a por pasar). Ninguna de estas acciones cambia el estatus de la orden (no hay transición ni IStatusTransitionEffect).
/// <para>
/// Toda escritura corre en una transacción y empieza por el candado de las órdenes (UPDLOCK, en orden de id); el estado y las reglas se calculan DESPUÉS del candado, así que
/// dos personas que marcan y exportan la misma orden a la vez no la dejan a medias (la segunda ve la marca y recibe el 409).
/// </para>
/// <para>
/// El formato PURCHORD todavía NO se ha probado contra un QuickBooks Desktop real (falta la copia de la compañía que aportará el dueño): ver QuickBooksIifWriter.
/// </para>
/// </summary>
public sealed partial class QuickBooksService
{
    // ================================================================ consulta

    /// <summary>Todas las órdenes de compra por pasar (sin paginar: «Exportar todas» las lleva todas), con proveedores y productos nuevos, avisos y la cuenta de órdenes de compra.</summary>
    public async Task<PurchaseQuickBooksPendingDto> GetPurchasePendingAsync(CancellationToken ct)
    {
        await permissions.EnsureAsync(PermissionCatalog.PurchasingQuickBooks, ct);
        var cat = await PoCatalogAsync(ct);
        var orders = await PoOrdered(PoPendingQuery(cat).AsNoTracking().Include(o => o.Lines)).ToListAsync(ct);
        var pkg = await LoadPoAsync(cat, orders, ct);
        var rows = PoRows(pkg);
        var exported = orders.Where(o => o.QuickBooksExportedAt is not null).ToList();
        return new PurchaseQuickBooksPendingDto(rows.Count, rows.Sum(r => r.Total), exported.Count, exported.Max(o => o.QuickBooksExportedAt), rows,
            PoNewSupplierRows(pkg), PoNewProductRows(pkg), PoWarnings(pkg, includeNew: true), pkg.Accounts.PurchaseOrders);
    }

    // ================================================================ exportar, confirmar, «No pasó», marcar y quitar la marca

    /// <summary>
    /// El archivo IIF con las órdenes indicadas (o, con all, todas las por pasar) y, si IncludeNew, sus proveedores y productos nuevos. Anota en cada una la fecha del archivo y
    /// quién lo sacó, pero no la marca. Errores: sin selección / más de 1000 / inexistente = 400; vino de QuickBooks o estatus que no se pasa = 422; ya en QuickBooks = 409;
    /// nada por pasar = 422.
    /// </summary>
    public async Task<PurchaseQuickBooksExportDto> ExportPurchaseOrdersAsync(PurchaseQuickBooksExportRequest? req, CancellationToken ct)
    {
        await permissions.EnsureAsync(PermissionCatalog.PurchasingQuickBooks, ct);
        var all = req?.All ?? false;
        var includeNew = req?.IncludeNew ?? true;
        var publicIds = all ? null : RequirePoIds(req?.PurchaseOrderIds);
        return await db.RunInTransactionAsync(async ct2 =>
        {
            var cat = await PoCatalogAsync(ct2);
            List<PurchaseOrder> orders;
            if (all)
            {
                // primero los ids de las por pasar, después el candado y después se recalcula (otra persona pudo marcarlas o cancelarlas mientras tanto)
                var ids = await PoPendingQuery(cat).AsNoTracking().Select(o => o.PurchaseOrderId).ToListAsync(ct2);
                await LockPurchaseOrdersAsync(ids, ct2);
                orders = ids.Count == 0 ? new List<PurchaseOrder>()
                    : await PoOrdered(PoPendingQuery(cat).AsTracking().Include(o => o.Lines).Where(o => ids.Contains(o.PurchaseOrderId))).ToListAsync(ct2);
            }
            else
            {
                orders = await ResolvePoAsync(publicIds!, ct2);
                foreach (var o in orders)
                    if (PurchaseOrderQuickBooksRules.ExportError(o.Number, OriginOf(cat, o), StatusCodeOf(cat, o), StatusLabel(cat, o), o.QuickBooksAt) is { } e) throw PoError(e);
            }
            if (orders.Count == 0) throw new StatusRuleException(PurchaseOrderQuickBooksRules.NothingPending);

            // el aviso contra duplicados se calcula con las fechas de ANTES de este archivo
            var before = orders.Where(o => o.QuickBooksExportedAt is not null).ToList();
            var beforeCount = before.Count;
            var beforeLast = before.Max(o => o.QuickBooksExportedAt);

            var now = clock.UtcNow;
            foreach (var o in orders) (o.QuickBooksExportedAt, o.QuickBooksExportedBy) = (now, tenant.UserId);
            await db.SaveChangesAsync(ct2);

            var pkg = await LoadPoAsync(cat, orders, ct2);
            var newSuppliers = PoNewSuppliers(pkg);
            var newProducts = PoProducts(pkg, orders).Where(p => p.QuickBooksSentAt is null).OrderBy(p => p.Sku, StringComparer.OrdinalIgnoreCase).ToList();
            var iif = QuickBooksIifWriter.WritePurchaseOrders(pkg.Accounts,
                includeNew ? newProducts.Select(p => new QuickBooksItem(p.Sku, p.Name, p.SalePrice, p.PurchaseCost, !pkg.NonTaxable.Contains(p.ProductId))).ToList() : Array.Empty<QuickBooksItem>(),
                includeNew ? newSuppliers.Select(s => Vendor(pkg, s)).ToList() : Array.Empty<QuickBooksVendor>(),
                orders.Select(o => PoIif(pkg, o)).ToList());
            var rows = PoRows(pkg);
            var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(now, DateTimeKind.Utc), clock.Zone);
            return new PurchaseQuickBooksExportDto(QuickBooksIifWriter.PurchaseOrderFileName(local), QuickBooksIifWriter.ContentType,
                Convert.ToBase64String(QuickBooksIifWriter.Encode(iif)), now, rows.Count, rows.Sum(r => r.Total), beforeCount, beforeLast, includeNew,
                includeNew ? newSuppliers.Count : 0, includeNew ? newProducts.Count : 0, PoWarnings(pkg, includeNew), rows);
        }, ct);
    }

    /// <summary>
    /// «Sí, ya están» después de exportar: marca las indicadas (solo las que salieron en algún archivo; si no, 422 NotExported) y deja a sus proveedores y productos como ya
    /// mandados. Las ya marcadas no cambian.
    /// </summary>
    public async Task<PurchaseQuickBooksMarkResultDto> ConfirmPurchaseOrdersAsync(PurchaseQuickBooksSelectionRequest? req, CancellationToken ct)
    {
        await permissions.EnsureAsync(PermissionCatalog.PurchasingQuickBooks, ct);
        var publicIds = RequirePoIds(req?.PurchaseOrderIds);
        return await db.RunInTransactionAsync(async ct2 =>
        {
            var cat = await PoCatalogAsync(ct2);
            var orders = await ResolvePoAsync(publicIds, ct2);
            foreach (var o in orders)
                if (PurchaseOrderQuickBooksRules.MarkError(o.Number, OriginOf(cat, o), StatusCodeOf(cat, o), StatusLabel(cat, o), o.QuickBooksExportedAt, o.QuickBooksAt, confirm: true) is { } e)
                    throw PoError(e);
            return await PoMarkCoreAsync(orders, ct2);
        }, ct);
    }

    /// <summary>
    /// «No pasó»: las indicadas que salieron en un archivo y no están marcadas pierden la fecha del archivo y vuelven a por pasar (o a «no se pasa» si ya se cancelaron).
    /// Vino de QuickBooks = 422; marcada = 409 (se le quita la marca). Una que nunca salió queda igual (Unchanged).
    /// </summary>
    public async Task<PurchaseQuickBooksNotPassedResultDto> NotPassedPurchaseOrdersAsync(PurchaseQuickBooksSelectionRequest? req, CancellationToken ct)
    {
        await permissions.EnsureAsync(PermissionCatalog.PurchasingQuickBooks, ct);
        var publicIds = RequirePoIds(req?.PurchaseOrderIds);
        return await db.RunInTransactionAsync(async ct2 =>
        {
            var cat = await PoCatalogAsync(ct2);
            var orders = await ResolvePoAsync(publicIds, ct2);
            foreach (var o in orders)
                if (PurchaseOrderQuickBooksRules.NotPassedError(o.Number, OriginOf(cat, o), o.QuickBooksAt) is { } e) throw PoError(e);
            var reset = 0;
            foreach (var o in orders.Where(o => o.QuickBooksExportedAt is not null)) { (o.QuickBooksExportedAt, o.QuickBooksExportedBy) = (null, null); reset++; }
            await db.SaveChangesAsync(ct2);
            return new PurchaseQuickBooksNotPassedResultDto(reset, orders.Count - reset, clock.UtcNow);
        }, ct);
    }

    /// <summary>
    /// Marcar a mano como en QuickBooks: las indicadas o, con all, todas las por pasar. Mismas reglas de elegibilidad (vino de QuickBooks = 422; estatus que no se pasa y nunca
    /// exportada = 422). Las ya marcadas no cambian (ni quién ni cuándo). Sus proveedores y productos quedan como ya mandados.
    /// </summary>
    public async Task<PurchaseQuickBooksMarkResultDto> MarkPurchaseOrdersAsync(PurchaseQuickBooksSelectionRequest? req, CancellationToken ct)
    {
        await permissions.EnsureAsync(PermissionCatalog.PurchasingQuickBooks, ct);
        var all = req?.All ?? false;
        var publicIds = all ? null : RequirePoIds(req?.PurchaseOrderIds);
        return await db.RunInTransactionAsync(async ct2 =>
        {
            var cat = await PoCatalogAsync(ct2);
            List<PurchaseOrder> orders;
            if (all)
            {
                var ids = await PoPendingQuery(cat).AsNoTracking().Select(o => o.PurchaseOrderId).ToListAsync(ct2);
                await LockPurchaseOrdersAsync(ids, ct2);
                orders = ids.Count == 0 ? new List<PurchaseOrder>()
                    : await PoOrdered(PoPendingQuery(cat).AsTracking().Include(o => o.Lines).Where(o => ids.Contains(o.PurchaseOrderId))).ToListAsync(ct2);
            }
            else
            {
                orders = await ResolvePoAsync(publicIds!, ct2);
                foreach (var o in orders)
                    if (PurchaseOrderQuickBooksRules.MarkError(o.Number, OriginOf(cat, o), StatusCodeOf(cat, o), StatusLabel(cat, o), o.QuickBooksExportedAt, o.QuickBooksAt, confirm: false) is { } e)
                        throw PoError(e);
            }
            return await PoMarkCoreAsync(orders, ct2);
        }, ct);
    }

    /// <summary>Quitar la marca a las indicadas: vuelven a «exportada, por confirmar» (si salieron en un archivo) o a por pasar. Proveedores y productos siguen como ya mandados.</summary>
    public async Task<PurchaseQuickBooksMarkResultDto> UnmarkPurchaseOrdersAsync(PurchaseQuickBooksSelectionRequest? req, CancellationToken ct)
    {
        await permissions.EnsureAsync(PermissionCatalog.PurchasingQuickBooks, ct);
        var publicIds = RequirePoIds(req?.PurchaseOrderIds);
        return await db.RunInTransactionAsync(async ct2 =>
        {
            var cat = await PoCatalogAsync(ct2);
            var orders = await ResolvePoAsync(publicIds, ct2);
            foreach (var o in orders)
                if (PurchaseOrderQuickBooksRules.UnmarkError(o.Number, OriginOf(cat, o)) is { } e) throw PoError(e);
            var n = 0;
            foreach (var o in orders.Where(o => o.QuickBooksAt is not null)) { (o.QuickBooksAt, o.QuickBooksBy) = (null, null); n++; }
            await db.SaveChangesAsync(ct2);
            return new PurchaseQuickBooksMarkResultDto(n, orders.Count - n, 0, 0, clock.UtcNow);
        }, ct);
    }

    private async Task<PurchaseQuickBooksMarkResultDto> PoMarkCoreAsync(List<PurchaseOrder> orders, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var fresh = orders.Where(o => o.QuickBooksAt is null).ToList();
        foreach (var o in fresh) (o.QuickBooksAt, o.QuickBooksBy) = (now, tenant.UserId);
        // los proveedores y productos de esas órdenes quedan como ya mandados (fueran en el archivo o tecleados a mano en QuickBooks)
        var supplierIds = fresh.Select(o => o.SupplierId).Distinct().ToList();
        var productIds = fresh.SelectMany(o => o.Lines).Select(l => l.ProductId).Distinct().ToList();
        var suppliers = supplierIds.Count == 0 ? new List<Supplier>()
            : await db.Suppliers.AsTracking().Where(s => supplierIds.Contains(s.SupplierId) && s.QuickBooksSentAt == null).ToListAsync(ct);
        var products = productIds.Count == 0 ? new List<Product>()
            : await db.Products.AsTracking().Where(p => productIds.Contains(p.ProductId) && p.QuickBooksSentAt == null).ToListAsync(ct);
        foreach (var s in suppliers) s.QuickBooksSentAt = now;
        foreach (var p in products) p.QuickBooksSentAt = now;
        await db.SaveChangesAsync(ct);
        return new PurchaseQuickBooksMarkResultDto(fresh.Count, orders.Count - fresh.Count, suppliers.Count, products.Count, now);
    }

    // ================================================================ selección y candado

    /// <summary>Los PublicId pedidos, sin repetir. Ninguno = 400 SelectionRequired; más de 1000 = 400 TooManyIds.</summary>
    private static List<Guid> RequirePoIds(IReadOnlyList<Guid>? publicIds)
    {
        var ids = (publicIds ?? Array.Empty<Guid>()).Distinct().ToList();
        if (ids.Count == 0) throw new ValidationException("purchaseOrderIds", PurchaseOrderQuickBooksRules.SelectionRequired);
        if (ids.Count > PurchaseOrderQuickBooksRules.MaxIdsPerCall) throw new ValidationException("purchaseOrderIds", PurchaseOrderQuickBooksRules.TooManyIds);
        return ids;
    }

    /// <summary>
    /// Las órdenes indicadas, bloqueadas y rastreadas (con sus líneas), ordenadas por fecha. Alguna que no exista, esté dada de baja o sea de otra compañía = 400 NotFound (el
    /// mismo mensaje: sin oráculo). Se vuelve a comprobar después del candado.
    /// </summary>
    private async Task<List<PurchaseOrder>> ResolvePoAsync(List<Guid> publicIds, CancellationToken ct)
    {
        var ids = await db.PurchaseOrders.AsNoTracking().Where(o => o.IsActive && publicIds.Contains(o.PublicId)).Select(o => o.PurchaseOrderId).ToListAsync(ct);
        if (ids.Count != publicIds.Count) throw new ValidationException("purchaseOrderIds", PurchaseOrderQuickBooksRules.NotFound);
        await LockPurchaseOrdersAsync(ids, ct);
        var rows = await PoOrdered(db.PurchaseOrders.AsTracking().Include(o => o.Lines).Where(o => o.IsActive && ids.Contains(o.PurchaseOrderId))).ToListAsync(ct);
        if (rows.Count != ids.Count) throw new ValidationException("purchaseOrderIds", PurchaseOrderQuickBooksRules.NotFound);
        return rows;
    }

    /// <summary>
    /// Candado de las órdenes (UPDLOCK, ROWLOCK, en orden de id para no cruzarse con otra corrida). Los ids viajan como JSON (OPENJSON), nunca pegados al texto. En InMemory
    /// no hace nada (como InventoryQueries.LockPurchaseOrderAsync). Exige estar dentro de RunInTransactionAsync.
    /// </summary>
    private async Task LockPurchaseOrdersAsync(IReadOnlyCollection<int> ids, CancellationToken ct)
    {
        if (ids.Count == 0 || !db.Database.IsRelational()) return;
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("LockPurchaseOrdersAsync debe llamarse dentro de RunInTransactionAsync.");
        var tenantId = db.CurrentTenantId;
        var json = JsonSerializer.Serialize(ids.Distinct().OrderBy(i => i));
        await db.Database.SqlQuery<int>(
                $"SELECT PurchaseOrderId AS Value FROM dbo.PurchaseOrder WITH (UPDLOCK, ROWLOCK) WHERE TenantId = {tenantId} AND PurchaseOrderId IN (SELECT CAST([value] AS INT) FROM OPENJSON({json})) ORDER BY PurchaseOrderId")
            .ToListAsync(ct);
    }

    private static Exception PoError((int Status, string Message) e)
        => e.Status == 409 ? new ConflictException(e.Message) : new StatusRuleException(e.Message);

    // ================================================================ catálogos y consulta base

    /// <summary>Estatus de la orden de compra (id → fila) y origen (id → código); QuickBooksOriginId = el id de QUICKBOOKS (NULL si el catálogo no lo tiene).</summary>
    private sealed record PoCatalog(Dictionary<int, StatusCode> Statuses, List<int> ExportableStatusIds, Dictionary<int, string> Origins, int? QuickBooksOriginId);

    private async Task<PoCatalog> PoCatalogAsync(CancellationToken ct)
    {
        var statuses = await db.StatusCodes.AsNoTracking().Where(s => s.Entity == StatusDomains.PurchaseOrderStatus).ToDictionaryAsync(s => s.StatusCodeId, ct);
        var exportable = statuses.Values.Where(s => PurchaseOrderQuickBooksRules.IsExportableStatus(s.InternalCode)).Select(s => s.StatusCodeId).ToList();
        var origins = (await lookups.GetDomainAsync(PurchaseOrderOrigins.Domain, ct)).ToDictionary(l => l.LookupCodeId, l => l.InternalCode);
        int? qb = origins.Where(kv => kv.Value == PurchaseOrderOrigins.QuickBooks).Select(kv => (int?)kv.Key).FirstOrDefault();
        return new PoCatalog(statuses, exportable, origins, qb);
    }

    /// <summary>Por pasar: activa, sin marca, en un estatus que se pasa y que no vino de QuickBooks (origen NULL = TEIKEM).</summary>
    private IQueryable<PurchaseOrder> PoPendingQuery(PoCatalog cat)
    {
        var statusIds = cat.ExportableStatusIds;
        var q = db.PurchaseOrders.Where(o => o.IsActive && o.QuickBooksAt == null && statusIds.Contains(o.StatusCodeId));
        if (cat.QuickBooksOriginId is int qb) q = q.Where(o => o.OriginLookupId == null || o.OriginLookupId != qb);
        return q;
    }

    private static IQueryable<PurchaseOrder> PoOrdered(IQueryable<PurchaseOrder> q) => q.OrderBy(o => o.OrderDate).ThenBy(o => o.PurchaseOrderId);

    private static string OriginOf(PoCatalog cat, PurchaseOrder o)
        => o.OriginLookupId is int id && cat.Origins.TryGetValue(id, out var code) ? PurchaseOrderOrigins.OrDefault(code) : PurchaseOrderOrigins.Teikem;

    private static string StatusCodeOf(PoCatalog cat, PurchaseOrder o) => cat.Statuses.GetValueOrDefault(o.StatusCodeId)?.InternalCode ?? string.Empty;

    private string StatusLabel(PoCatalog cat, PurchaseOrder o)
        => cat.Statuses.TryGetValue(o.StatusCodeId, out var s) ? MultilingualText.Resolve(s.LabelJson, T.Lang) : string.Empty;

    // ================================================================ paquete

    private sealed class PoPackage
    {
        public required PoCatalog Catalog { get; init; }
        public required List<PurchaseOrder> Orders { get; init; }
        public required Teikem.Domain.Tenancy.Tenant TenantRow { get; init; }
        public required QuickBooksAccounts Accounts { get; init; }
        public required IReadOnlyDictionary<string, string> Terms { get; init; }
        public required string Lang { get; init; }
        public Dictionary<int, Supplier> Suppliers { get; } = new();
        public Dictionary<int, Product> Products { get; } = new();
        public HashSet<int> NonTaxable { get; } = new();
        /// <summary>Proveedor → código de su término de pago.</summary>
        public Dictionary<int, string> SupplierTerms { get; } = new();
        /// <summary>Moneda (LookupCode) → código.</summary>
        public Dictionary<int, string> Currencies { get; } = new();
        public Dictionary<int, string> Users { get; } = new();
    }

    /// <summary>Lo que hace falta para la lista y el archivo, en pocas consultas: compañía (cuentas, términos, moneda), proveedores, productos, monedas y usuarios.</summary>
    private async Task<PoPackage> LoadPoAsync(PoCatalog cat, List<PurchaseOrder> orders, CancellationToken ct)
    {
        var t = await db.Tenants.AsNoTracking().FirstAsync(x => x.TenantId == T.RequireTenantId(), ct);
        var pkg = new PoPackage { Catalog = cat, Orders = orders, TenantRow = t, Accounts = AccountsOf(t), Terms = QuickBooksRules.ParseTerms(t.QuickBooksTermsJson), Lang = T.Lang };
        if (orders.Count == 0) return pkg;

        var supplierIds = orders.Select(o => o.SupplierId).Distinct().ToList();
        foreach (var s in await db.Suppliers.AsNoTracking().Where(s => supplierIds.Contains(s.SupplierId)).ToListAsync(ct))
        {
            pkg.Suppliers[s.SupplierId] = s;
            if (s.PaymentTermLookupId is int term) pkg.SupplierTerms[s.SupplierId] = (await lookups.GetAsync(term, ct))?.InternalCode ?? string.Empty;
        }
        var productIds = orders.SelectMany(o => o.Lines).Select(l => l.ProductId).Distinct().ToList();
        if (productIds.Count > 0)
            foreach (var p in await db.Products.AsNoTracking().Where(p => productIds.Contains(p.ProductId)).ToListAsync(ct))
            {
                pkg.Products[p.ProductId] = p;
                if (p.TaxCategoryLookupId is int tc && (await lookups.GetAsync(tc, ct))?.InternalCode is TaxCategories.Exempt or TaxCategories.ZeroRate) pkg.NonTaxable.Add(p.ProductId);
            }
        foreach (var id in orders.Where(o => o.CurrencyLookupId is not null).Select(o => o.CurrencyLookupId!.Value).Distinct())
            pkg.Currencies[id] = (await lookups.GetAsync(id, ct))?.InternalCode ?? string.Empty;
        var userIds = orders.Where(o => o.QuickBooksExportedBy is not null).Select(o => o.QuickBooksExportedBy!.Value).Distinct().ToList();
        if (userIds.Count > 0)
            foreach (var u in await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).Select(u => new { u.Id, u.FullName, u.Email }).ToListAsync(ct))
                pkg.Users[u.Id] = u.FullName ?? u.Email ?? string.Empty;
        return pkg;
    }

    // ================================================================ filas, avisos y archivo

    private static bool IsNewSupplier(Supplier s) => s.QuickBooksSentAt is null;

    /// <summary>Los productos de las líneas de esas órdenes, sin repetir (un kit va como el producto que es: no se desglosa).</summary>
    private static IEnumerable<Product> PoProducts(PoPackage pkg, IEnumerable<PurchaseOrder> orders)
    {
        var seen = new HashSet<int>();
        foreach (var l in orders.SelectMany(o => o.Lines.OrderBy(x => x.PurchaseOrderLineId)))
            if (seen.Add(l.ProductId) && pkg.Products.TryGetValue(l.ProductId, out var p)) yield return p;
    }

    private static List<Supplier> PoNewSuppliers(PoPackage pkg)
        => pkg.Suppliers.Values.Where(IsNewSupplier).OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ThenBy(s => s.SupplierId).ToList();

    private static decimal PoTotal(PurchaseOrder o) => PurchaseOrderRules.Total(o.Lines.Select(l => (l.QtyOrdered, l.UnitCost)));

    private static QuickBooksPurchaseOrder PoIif(PoPackage pkg, PurchaseOrder o)
    {
        var lines = o.Lines.OrderBy(l => l.PurchaseOrderLineId).Select(l => pkg.Products.TryGetValue(l.ProductId, out var p)
            ? new QuickBooksPurchaseOrderLine(p.Sku, p.Name, l.QtyOrdered, l.UnitCost)
            : new QuickBooksPurchaseOrderLine(l.ProductId.ToString(CultureInfo.InvariantCulture), null, l.QtyOrdered, l.UnitCost)).ToList();
        return new QuickBooksPurchaseOrder(o.Number, o.OrderDate, o.ExpectedDate, pkg.Suppliers.GetValueOrDefault(o.SupplierId)?.Name ?? string.Empty,
            PurchaseOrderQuickBooksRules.Memo(o.Notes), lines);
    }

    private static List<PurchaseQuickBooksOrderDto> PoRows(PoPackage pkg) => pkg.Orders.Select(o =>
    {
        var s = pkg.Suppliers.GetValueOrDefault(o.SupplierId);
        var status = pkg.Catalog.Statuses.GetValueOrDefault(o.StatusCodeId);
        var newSkus = PoProducts(pkg, new[] { o }).Where(p => p.QuickBooksSentAt is null).Select(p => p.Sku).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        return new PurchaseQuickBooksOrderDto(o.PublicId, o.Number, o.OrderDate, o.ExpectedDate, status?.InternalCode ?? string.Empty,
            status is null ? string.Empty : MultilingualText.Resolve(status.LabelJson, pkg.Lang), o.SupplierId, s?.Name ?? string.Empty, QuickBooksIifWriter.Name(s?.Name),
            OriginOf(pkg.Catalog, o), PoTotal(o), QuickBooksIifWriter.PurchaseOrderFileTotal(PoIif(pkg, o)), o.Lines.Count, o.QuickBooksExportedAt,
            o.QuickBooksExportedBy is int u ? pkg.Users.GetValueOrDefault(u) : null, s is not null && IsNewSupplier(s), newSkus);
    }).ToList();

    private static string? SupplierTermsOf(PoPackage pkg, int supplierId)
        => pkg.SupplierTerms.TryGetValue(supplierId, out var code) && pkg.Terms.TryGetValue(code, out var name) ? name : null;

    /// <summary>Teléfono con prefijo internacional (E.164); el prefijo, si falta, es el de la compañía. Un prefijo dañado = el número tal como está.</summary>
    private static string? SupplierPhone(PoPackage pkg, Supplier s)
    {
        if (string.IsNullOrWhiteSpace(s.Phone)) return null;
        try { return PhoneRules.ToE164(s.PhoneCountryCallingCode, s.Phone, pkg.TenantRow.PhoneCountryCode); }
        catch (FormatException) { return s.Phone.Trim(); }
    }

    private static QuickBooksVendor Vendor(PoPackage pkg, Supplier s)
    {
        var note = string.IsNullOrWhiteSpace(s.Notes) ? null : s.Notes.Length > PurchaseOrderQuickBooksRules.MaxMemoLength ? s.Notes[..PurchaseOrderQuickBooksRules.MaxMemoLength] : s.Notes;
        return new QuickBooksVendor(s.Name, s.Name, s.ContactName, SupplierPhone(pkg, s), s.Email, SupplierTermsOf(pkg, s.SupplierId), note);
    }

    private static List<PurchaseQuickBooksNewSupplierDto> PoNewSupplierRows(PoPackage pkg) => PoNewSuppliers(pkg).Select(s =>
    {
        var v = Vendor(pkg, s);
        return new PurchaseQuickBooksNewSupplierDto(s.SupplierId, s.Name, QuickBooksIifWriter.Name(s.Name), s.ContactName, v.Phone, s.Email,
            pkg.SupplierTerms.GetValueOrDefault(s.SupplierId), v.Terms);
    }).ToList();

    private static List<QuickBooksNewProductDto> PoNewProductRows(PoPackage pkg)
        => PoProducts(pkg, pkg.Orders).Where(p => p.QuickBooksSentAt is null).OrderBy(p => p.Sku, StringComparer.OrdinalIgnoreCase)
            .Select(p => new QuickBooksNewProductDto(p.PublicId, p.Sku, QuickBooksIifWriter.Name(p.Sku), p.Name, p.SalePrice, p.PurchaseCost, !pkg.NonTaxable.Contains(p.ProductId)))
            .ToList();

    /// <summary>
    /// Avisos NO bloqueantes: nombre de proveedor de más de 41 y SKU de más de 31 caracteres (QuickBooks los rechaza), término del proveedor nuevo sin nombre en QuickBooks
    /// (solo si el archivo lleva los nuevos), diferencia de redondeo entre Teikem y el archivo, moneda distinta a la de la compañía y caracteres fuera de Windows-1252.
    /// </summary>
    private static List<string> PoWarnings(PoPackage pkg, bool includeNew)
    {
        var w = new List<string>();
        var suppliers = pkg.Suppliers.Values.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToList();
        var products = PoProducts(pkg, pkg.Orders).OrderBy(p => p.Sku, StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var s in suppliers.Where(s => QuickBooksIifWriter.Name(s.Name).Length > QuickBooksRules.MaxVendorLength))
            w.Add(QuickBooksRules.VendorNameTooLong(s.Name));
        foreach (var p in products.Where(p => QuickBooksIifWriter.Name(p.Sku).Length > QuickBooksRules.MaxItemLength))
            w.Add(QuickBooksRules.ItemNameTooLong(p.Sku));
        if (includeNew)
            foreach (var code in PoNewSuppliers(pkg).Select(s => pkg.SupplierTerms.GetValueOrDefault(s.SupplierId)).Where(c => !string.IsNullOrEmpty(c) && !pkg.Terms.ContainsKey(c!))
                         .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(c => c, StringComparer.Ordinal))
                w.Add(PurchaseOrderQuickBooksRules.UnmappedSupplierTerm(code!));
        var tenantCurrency = (pkg.TenantRow.CurrencyCode ?? string.Empty).Trim();
        foreach (var o in pkg.Orders)
        {
            var total = PoTotal(o);
            var fileTotal = QuickBooksIifWriter.PurchaseOrderFileTotal(PoIif(pkg, o));
            if (total != fileTotal) w.Add(PurchaseOrderQuickBooksRules.RoundingDifference(o.Number, total, fileTotal));
            if (o.CurrencyLookupId is int cur && pkg.Currencies.GetValueOrDefault(cur) is { Length: > 0 } code && tenantCurrency.Length > 0
                && !string.Equals(code, tenantCurrency, StringComparison.OrdinalIgnoreCase))
                w.Add(PurchaseOrderQuickBooksRules.CurrencyDiffers(o.Number, code, tenantCurrency));
            if (QuickBooksIifWriter.UnencodableChars(PurchaseOrderQuickBooksRules.Memo(o.Notes)) is { Length: > 0 } memoChars)
                w.Add(PurchaseOrderQuickBooksRules.UnencodableText($"El memo de la orden de compra {o.Number}", memoChars));
        }
        foreach (var s in suppliers)
            if (QuickBooksIifWriter.UnencodableChars(s.Name) is { Length: > 0 } chars) w.Add(PurchaseOrderQuickBooksRules.UnencodableText($"El proveedor «{s.Name}»", chars));
        foreach (var p in products)
        {
            if (QuickBooksIifWriter.UnencodableChars(p.Sku) is { Length: > 0 } skuChars) w.Add(PurchaseOrderQuickBooksRules.UnencodableText($"El SKU «{p.Sku}»", skuChars));
            if (QuickBooksIifWriter.UnencodableChars(p.Name) is { Length: > 0 } nameChars)
                w.Add(PurchaseOrderQuickBooksRules.UnencodableText($"La descripción del producto {p.Sku}", nameChars));
        }
        return w.Distinct(StringComparer.Ordinal).ToList();
    }
}
