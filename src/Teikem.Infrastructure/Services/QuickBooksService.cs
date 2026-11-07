using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Billing;
using Teikem.Domain.Clients;
using Teikem.Domain.Common;
using Teikem.Domain.Constants;
using Teikem.Domain.Sales;
using Teikem.Domain.Tax;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Migration;
using Teikem.Infrastructure.Notifications;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 225 (P5 del pedido de vendedores) — QuickBooks Desktop. «Por pasar» = factura de venta (con al menos una línea de orden de venta o su cargo de envío) EMITIDA
/// (ISSUED, OVERDUE o PAID; ni borrador ni anulada) sin la marca «en QuickBooks». Exportar saca SIEMPRE todas las por pasar (hayan salido antes o no) y no las marca;
/// las marca quien confirma «sí, ya están» (sales.quickbooks.export) o quien las marca a mano (sales.quickbooks.mark). Un cliente o producto es «nuevo» hasta que se marca
/// o confirma su primera factura (QuickBooksSentAt); el cliente migrado de QuickBooks (campo «Código QuickBooks») nunca es nuevo y sale con ese nombre.
/// </summary>
public sealed class QuickBooksService(TeikemDbContext db, ITenantContext tenant, PermissionService permissions, ILookupCache lookups, ITenantClock clock,
    INotificationPublisher? notifications = null)
{
    public const int MaxPageSize = 200;
    private TenantContext T => (TenantContext)tenant;
    private static readonly string[] IssuedCodes = { InvoiceStatuses.Issued, InvoiceStatuses.Overdue, InvoiceStatuses.Paid };

    // ================================================================ consulta

    /// <summary>Los tres permisos de QuickBooks ven la lista y el histórico (quien marca o exporta necesita verlas); el aviso, el panel y «Necesita tu atención» son de sales.quickbooks.view.</summary>
    public static readonly string[] ListPermissions = { PermissionCatalog.SalesQuickBooksView, PermissionCatalog.SalesQuickBooksMark, PermissionCatalog.SalesQuickBooksExport };

    private async Task EnsureListAsync(CancellationToken ct)
    {
        foreach (var code in ListPermissions)
            if (await permissions.HasPermissionAsync(code, ct)) return;
        await permissions.EnsureAsync(PermissionCatalog.SalesQuickBooksView, ct);   // 403 con el mensaje de siempre
    }

    public async Task<QuickBooksPendingDto> GetPendingAsync(CancellationToken ct)
    {
        await EnsureListAsync(ct);
        var pkg = await LoadAsync(await PendingQueryAsync(ct), ct);
        var rows = Rows(pkg);
        var exported = pkg.Invoices.Where(i => i.QuickBooksExportedAt is not null).ToList();
        return new QuickBooksPendingDto(rows.Count, rows.Sum(r => r.Total), exported.Count, exported.Max(i => i.QuickBooksExportedAt),
            rows, NewClients(pkg), NewProducts(pkg), Warnings(pkg));
    }

    /// <summary>Cuántas hay por pasar, cuántas ya salieron en un archivo y la emisión más vieja (para el aviso, el panel Ventas y «Necesita tu atención»). Sin permiso: lo pone quien llama.</summary>
    public async Task<(int Count, int Exported, DateOnly? Oldest)> CountPendingAsync(CancellationToken ct)
    {
        var q = await PendingQueryAsync(ct);
        var n = await q.CountAsync(ct);
        if (n == 0) return (0, 0, null);
        return (n, await q.CountAsync(i => i.QuickBooksExportedAt != null, ct), await q.MinAsync(i => i.IssueDate, ct));
    }

    /// <summary>Histórico: las ya pasadas, la más reciente primero. from/to = día (de la compañía) de la marca; q = número de factura, cliente o número del cliente.</summary>
    public async Task<PagedResult<QuickBooksHistoryItemDto>> GetHistoryAsync(DateOnly? from, DateOnly? to, string? q, int skip, int take, CancellationToken ct)
    {
        await EnsureListAsync(ct);
        skip = Math.Max(0, skip);
        take = take <= 0 ? 50 : Math.Min(take, MaxPageSize);
        var issued = await IssuedIdsAsync(ct);
        var query = from i in SalesInvoices(issued).Where(i => i.QuickBooksAt != null)
                    join c in db.Clients.AsNoTracking() on i.ClientId equals c.ClientId
                    select new { I = i, c.Code, c.Name };
        if (from is DateOnly f) { var fu = clock.StartOfDayUtc(f); query = query.Where(x => x.I.QuickBooksAt >= fu); }
        if (to is DateOnly t) { var tu = clock.StartOfDayUtc(t.AddDays(1)); query = query.Where(x => x.I.QuickBooksAt < tu); }
        if (!string.IsNullOrWhiteSpace(q))
        {
            var s = q.Trim();
            query = query.Where(x => x.I.InvoiceNumber.Contains(s) || x.Name.Contains(s) || x.Code.Contains(s));
        }
        var total = await query.CountAsync(ct);
        var page = await query.OrderByDescending(x => x.I.QuickBooksAt).ThenByDescending(x => x.I.InvoiceId).Skip(skip).Take(take)
            .Select(x => new { x.I.PublicId, x.I.InvoiceNumber, x.I.IssueDate, x.Code, x.Name, x.I.Subtotal, x.I.TaxAmount, x.I.QuickBooksAt, x.I.QuickBooksBy, x.I.QuickBooksExportedAt })
            .ToListAsync(ct);
        var userIds = page.Where(p => p.QuickBooksBy is not null).Select(p => p.QuickBooksBy!.Value).Distinct().ToList();
        var users = userIds.Count == 0 ? new Dictionary<int, string>()
            : await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName ?? u.Email ?? string.Empty, ct);
        var items = page.Select(p => new QuickBooksHistoryItemDto(p.PublicId, p.InvoiceNumber, p.IssueDate, p.Code, p.Name, Money(p.Subtotal + p.TaxAmount), p.QuickBooksAt!.Value,
            p.QuickBooksBy, p.QuickBooksBy is int u ? users.GetValueOrDefault(u) : null, p.QuickBooksExportedAt)).ToList();
        return new PagedResult<QuickBooksHistoryItemDto>(items, total, skip, take);
    }

    // ================================================================ exportar, marcar, confirmar

    /// <summary>
    /// El archivo IIF con TODAS las facturas por pasar (y, si IncludeNew, sus clientes y productos nuevos). Anota en cada una la fecha de este archivo (QuickBooksExportedAt)
    /// pero no la marca. Sin nada por pasar = 422.
    /// </summary>
    public async Task<QuickBooksExportDto> ExportAsync(QuickBooksExportRequest? req, CancellationToken ct)
    {
        await permissions.EnsureAsync(PermissionCatalog.SalesQuickBooksExport, ct);
        var includeNew = req?.IncludeNew ?? true;
        var pkg = await LoadAsync(await PendingQueryAsync(ct), ct);
        if (pkg.Invoices.Count == 0) throw new StatusRuleException(QuickBooksRules.NothingPending);
        var rows = Rows(pkg);
        var before = pkg.Invoices.Where(i => i.QuickBooksExportedAt is not null).ToList();
        var newClients = pkg.Clients.Values.Where(c => IsNewClient(pkg, c)).OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList();
        var newProducts = ItemProducts(pkg).Where(p => p.QuickBooksSentAt is null).OrderBy(p => p.Sku, StringComparer.OrdinalIgnoreCase).ToList();

        var iif = QuickBooksIifWriter.Write(pkg.Accounts,
            includeNew ? newProducts.Select(p => new QuickBooksItem(p.Sku, p.Name, p.SalePrice, p.PurchaseCost, IsTaxable(pkg, p))).ToList() : Array.Empty<QuickBooksItem>(),
            includeNew ? newClients.Select(c => Customer(pkg, c)).ToList() : Array.Empty<QuickBooksCustomer>(),
            pkg.Invoices.Select(i => ToIif(pkg, i)).ToList());

        var now = clock.UtcNow;
        var ids = pkg.Invoices.Select(i => i.InvoiceId).ToList();
        foreach (var inv in await db.Invoices.AsTracking().Where(i => ids.Contains(i.InvoiceId)).ToListAsync(ct)) inv.QuickBooksExportedAt = now;
        await db.SaveChangesAsync(ct);

        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(now, DateTimeKind.Utc), clock.Zone);
        return new QuickBooksExportDto(QuickBooksIifWriter.FileName(local), QuickBooksIifWriter.ContentType, Convert.ToBase64String(QuickBooksIifWriter.Encode(iif)), now,
            rows.Count, rows.Sum(r => r.Total), before.Count, before.Max(i => i.QuickBooksExportedAt), includeNew, newClients.Count, newProducts.Count,
            rows.Select(r => r with { ExportedAtUtc = now }).ToList());
    }

    /// <summary>Marcar a mano (sales.quickbooks.mark): las indicadas o todas las por pasar. Las ya marcadas no cambian (ni quién ni cuándo).</summary>
    public async Task<QuickBooksMarkResultDto> MarkAsync(QuickBooksMarkRequest? req, CancellationToken ct)
    {
        await permissions.EnsureAsync(PermissionCatalog.SalesQuickBooksMark, ct);
        if (req is null) throw new ValidationException("body", QuickBooksRules.BodyRequired);
        var invoices = req.All
            ? await (await PendingQueryAsync(ct)).AsTracking().Include(i => i.Lines).ToListAsync(ct)
            : await ResolveAsync(req.InvoiceIds, ct);
        return await MarkCoreAsync(invoices, ct);
    }

    /// <summary>«Sí, ya están» después de exportar (sales.quickbooks.export): solo facturas que salieron en algún archivo (si no, 422: se marcan a mano).</summary>
    public async Task<QuickBooksMarkResultDto> ConfirmAsync(QuickBooksConfirmRequest? req, CancellationToken ct)
    {
        await permissions.EnsureAsync(PermissionCatalog.SalesQuickBooksExport, ct);
        if (req is null) throw new ValidationException("body", QuickBooksRules.BodyRequired);
        var invoices = await ResolveAsync(req.InvoiceIds, ct);
        var never = invoices.FirstOrDefault(i => i.QuickBooksAt is null && i.QuickBooksExportedAt is null);
        if (never is not null) throw new StatusRuleException(QuickBooksRules.NotExported(never.InvoiceNumber));
        return await MarkCoreAsync(invoices, ct);
    }

    /// <summary>Quitar la marca (sales.quickbooks.mark) a las indicadas: vuelven a «por pasar». Los clientes y productos siguen como ya mandados.</summary>
    public async Task<QuickBooksMarkResultDto> UnmarkAsync(QuickBooksMarkRequest? req, CancellationToken ct)
    {
        await permissions.EnsureAsync(PermissionCatalog.SalesQuickBooksMark, ct);
        if (req is null) throw new ValidationException("body", QuickBooksRules.BodyRequired);
        var invoices = await ResolveAsync(req.InvoiceIds, ct);
        var n = 0;
        foreach (var inv in invoices.Where(i => i.QuickBooksAt is not null)) { (inv.QuickBooksAt, inv.QuickBooksBy) = (null, null); n++; }
        await db.SaveChangesAsync(ct);
        return new QuickBooksMarkResultDto(n, invoices.Count - n, 0, 0, clock.UtcNow);
    }

    private async Task<QuickBooksMarkResultDto> MarkCoreAsync(List<Invoice> invoices, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var fresh = invoices.Where(i => i.QuickBooksAt is null).ToList();
        foreach (var inv in fresh) (inv.QuickBooksAt, inv.QuickBooksBy) = (now, tenant.UserId);
        // los clientes y productos de esas facturas quedan como ya mandados (fueran en el archivo o tecleados a mano)
        var pkg = await LoadAsync(null, ct, fresh);
        var clientIds = pkg.Clients.Keys.ToList();
        var productIds = ItemProducts(pkg).Select(p => p.ProductId).ToList();
        var clients = clientIds.Count == 0 ? new List<Client>()
            : await db.Clients.AsTracking().Where(c => clientIds.Contains(c.ClientId) && c.QuickBooksSentAt == null).ToListAsync(ct);
        var products = productIds.Count == 0 ? new List<Product>()
            : await db.Products.AsTracking().Where(p => productIds.Contains(p.ProductId) && p.QuickBooksSentAt == null).ToListAsync(ct);
        foreach (var c in clients) c.QuickBooksSentAt = now;
        foreach (var p in products) p.QuickBooksSentAt = now;
        await db.SaveChangesAsync(ct);
        return new QuickBooksMarkResultDto(fresh.Count, invoices.Count - fresh.Count, clients.Count, products.Count, now);
    }

    /// <summary>Las facturas de venta emitidas indicadas (rastreadas, con sus líneas). Vacío = 400; alguna que no exista o no sea de venta emitida = 400.</summary>
    private async Task<List<Invoice>> ResolveAsync(IReadOnlyList<Guid>? publicIds, CancellationToken ct)
    {
        var ids = (publicIds ?? Array.Empty<Guid>()).Distinct().ToList();
        if (ids.Count == 0) throw new ValidationException("invoiceIds", QuickBooksRules.SelectionRequired);
        if (ids.Count > QuickBooksRules.MaxIdsPerCall) throw new ValidationException("invoiceIds", QuickBooksRules.TooManyIds);
        var issued = await IssuedIdsAsync(ct);
        var rows = await SalesInvoices(issued).AsTracking().Include(i => i.Lines).Where(i => ids.Contains(i.PublicId)).ToListAsync(ct);
        if (rows.Count != ids.Count) throw new ValidationException("invoiceIds", QuickBooksRules.InvoicesNotFound);
        return rows;
    }

    // ================================================================ aviso de la mañana

    /// <summary>
    /// El aviso del día «Facturas por pasar a QuickBooks: N» (evento SALES_QUICKBOOKS_PENDING; a quién y por qué canal lo dicen las reglas de aviso). Uno al día como mucho
    /// y solo si hay algo pendiente. Devuelve 1 si salió, 0 si no.
    /// </summary>
    public async Task<int> NotifyPendingAsync(CancellationToken ct)
    {
        if (notifications is null) return 0;
        var (count, exported, oldest) = await CountPendingAsync(ct);
        if (count == 0) return 0;
        var today = clock.Today;
        var values = new Dictionary<string, string?>
        {
            ["count"] = count.ToString(CultureInfo.InvariantCulture),
            ["exported"] = exported.ToString(CultureInfo.InvariantCulture),
            ["oldest"] = oldest?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty,
        };
        if (!await notifications.PublishAsync(SalesEventCodes.QuickBooksPending, values, "QBP-" + today.ToString("yyyyMMdd", CultureInfo.InvariantCulture), ct)) return 0;
        await db.SaveChangesAsync(ct);
        return 1;
    }

    // ================================================================ ajustes

    public async Task<QuickBooksSettingsDto> GetSettingsAsync(CancellationToken ct)
    {
        if (!await permissions.HasPermissionAsync(PermissionCatalog.SalesQuickBooksView, ct)) await permissions.EnsureAsync(PermissionCatalog.AdminTenant, ct);
        var t = await db.Tenants.AsNoTracking().FirstAsync(x => x.TenantId == T.RequireTenantId(), ct);
        return await SettingsDtoAsync(t, ct);
    }

    /// <summary>null = sin cambio; "" = el valor por omisión. Terms: código del catálogo PaymentTerm → nombre en QuickBooks ("" o null lo quita). 400 con el mensaje exacto si algo no es válido.</summary>
    public async Task<QuickBooksSettingsDto> UpdateSettingsAsync(QuickBooksSettingsRequest? req, CancellationToken ct)
    {
        await permissions.EnsureAsync(PermissionCatalog.AdminTenant, ct);
        if (req is null) throw new ValidationException("body", QuickBooksRules.BodyRequired);
        static string? Clean(string? v) => v is null ? null : QuickBooksIifWriter.Text(v);
        foreach (var (field, value) in new[] { ("receivableAccount", req.ReceivableAccount), ("salesAccount", req.SalesAccount), ("costAccount", req.CostAccount),
                     ("inventoryAccount", req.InventoryAccount), ("taxAccount", req.TaxAccount) })
            if (Clean(value) is { Length: > QuickBooksRules.MaxAccountLength }) throw new ValidationException(field, QuickBooksRules.AccountTooLong);
        foreach (var (field, value) in new[] { ("taxItem", req.TaxItem), ("shippingItem", req.ShippingItem) })
            if (Clean(value) is { Length: > QuickBooksRules.MaxItemLength }) throw new ValidationException(field, QuickBooksRules.ItemTooLong);

        var t = await db.Tenants.AsTracking().FirstAsync(x => x.TenantId == T.RequireTenantId(), ct);
        if (req.Terms is not null)
        {
            var known = (await lookups.GetDomainAsync(LookupDomains.PaymentTerm, ct)).Select(l => l.InternalCode).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var map = new Dictionary<string, string>(QuickBooksRules.ParseTerms(t.QuickBooksTermsJson), StringComparer.OrdinalIgnoreCase);
            foreach (var (rawCode, rawName) in req.Terms)
            {
                var code = (rawCode ?? string.Empty).Trim().ToUpperInvariant();
                if (!known.Contains(code)) throw new ValidationException("terms", QuickBooksRules.UnknownTerm(rawCode ?? string.Empty));
                var name = Clean(rawName);
                if (string.IsNullOrEmpty(name)) map.Remove(code);
                else if (name.Length > QuickBooksRules.MaxTermLength) throw new ValidationException("terms", QuickBooksRules.TermTooLong);
                else map[code] = name;
            }
            t.QuickBooksTermsJson = QuickBooksRules.SerializeTerms(map);
        }
        static string? Store(string? v) => string.IsNullOrEmpty(v) ? null : v;
        if (req.ReceivableAccount is not null) t.QuickBooksReceivableAccount = Store(Clean(req.ReceivableAccount));
        if (req.SalesAccount is not null) t.QuickBooksSalesAccount = Store(Clean(req.SalesAccount));
        if (req.CostAccount is not null) t.QuickBooksCostAccount = Store(Clean(req.CostAccount));
        if (req.InventoryAccount is not null) t.QuickBooksInventoryAccount = Store(Clean(req.InventoryAccount));
        if (req.TaxAccount is not null) t.QuickBooksTaxAccount = Store(Clean(req.TaxAccount));
        if (req.TaxItem is not null) t.QuickBooksTaxItem = Store(Clean(req.TaxItem));
        if (req.ShippingItem is not null) t.QuickBooksShippingItem = Store(Clean(req.ShippingItem));
        await db.SaveChangesAsync(ct);
        return await SettingsDtoAsync(t, ct);
    }

    private async Task<QuickBooksSettingsDto> SettingsDtoAsync(Teikem.Domain.Tenancy.Tenant t, CancellationToken ct)
    {
        var a = AccountsOf(t);
        var map = QuickBooksRules.ParseTerms(t.QuickBooksTermsJson);
        var terms = (await lookups.GetDomainAsync(LookupDomains.PaymentTerm, ct)).Where(l => l.IsActive).OrderBy(l => l.SortOrder).ThenBy(l => l.InternalCode, StringComparer.Ordinal)
            .Select(l => new QuickBooksTermMapDto(l.InternalCode, MultilingualText.Resolve(l.LabelJson, T.Lang), map.GetValueOrDefault(l.InternalCode))).ToList();
        return new QuickBooksSettingsDto(a.Receivable, a.Sales, a.Cost, a.Inventory, a.TaxAccount, a.TaxItem, a.ShippingItem, terms);
    }

    private static QuickBooksAccounts AccountsOf(Teikem.Domain.Tenancy.Tenant t) => new(
        QuickBooksRules.Effective(t.QuickBooksReceivableAccount, QuickBooksRules.DefaultReceivableAccount),
        QuickBooksRules.Effective(t.QuickBooksSalesAccount, QuickBooksRules.DefaultSalesAccount),
        QuickBooksRules.Effective(t.QuickBooksCostAccount, QuickBooksRules.DefaultCostAccount),
        QuickBooksRules.Effective(t.QuickBooksInventoryAccount, QuickBooksRules.DefaultInventoryAccount),
        QuickBooksRules.Effective(t.QuickBooksTaxAccount, QuickBooksRules.DefaultTaxAccount),
        QuickBooksRules.Effective(t.QuickBooksTaxItem, QuickBooksRules.DefaultTaxItem),
        QuickBooksRules.Effective(t.QuickBooksShippingItem, QuickBooksRules.DefaultShippingItem));

    // ================================================================ lectura del paquete

    private async Task<List<int>> IssuedIdsAsync(CancellationToken ct)
        => await db.StatusCodes.AsNoTracking().Where(s => s.Entity == BillingCodes.InvoiceStatusDomain && IssuedCodes.Contains(s.InternalCode)).Select(s => s.StatusCodeId).ToListAsync(ct);

    /// <summary>Facturas de venta emitidas (alguna línea de una orden de venta o su cargo de envío).</summary>
    private IQueryable<Invoice> SalesInvoices(List<int> issued)
        => db.Invoices.AsNoTracking().Where(i => i.IsActive && issued.Contains(i.StatusCodeId) && i.Lines.Any(l => l.SalesOrderLineId != null || l.SalesOrderId != null));

    private async Task<IQueryable<Invoice>> PendingQueryAsync(CancellationToken ct)
        => SalesInvoices(await IssuedIdsAsync(ct)).Where(i => i.QuickBooksAt == null);

    private sealed class Package
    {
        public List<Invoice> Invoices { get; init; } = new();
        public QuickBooksAccounts Accounts { get; init; } = QuickBooksAccounts.Defaults;
        public IReadOnlyDictionary<string, string> Terms { get; init; } = new Dictionary<string, string>();
        public Dictionary<int, Client> Clients { get; } = new();
        public Dictionary<int, string> QbNames { get; } = new();
        public Dictionary<int, Product> Products { get; } = new();
        /// <summary>Componentes por línea de orden de venta (la foto del kit al pedir; si la línea no la tiene, la receta del catálogo).</summary>
        public Dictionary<int, List<(int ProductId, decimal Quantity)>> Parts { get; } = new();
        public Dictionary<int, int> LineOrder { get; } = new();
        public Dictionary<int, string> OrderNumbers { get; } = new();
        public Dictionary<int, string> ChargeCodes { get; } = new();
        public Dictionary<int, string> StatusCodes { get; } = new();
        public Dictionary<int, string> TermCodes { get; } = new();
        public HashSet<int> NonTaxableProducts { get; } = new();
        public Dictionary<int, List<Location>> Locations { get; } = new();
        public Dictionary<string, int> LocationTypeIds { get; } = new(StringComparer.Ordinal);
        public Dictionary<int, (string? Contact, List<string> Phones, string? Email)> Contacts { get; } = new();
    }

    /// <summary>Todo lo que hace falta para la lista y el archivo, en pocas consultas. query = las facturas (o, si es null, las ya leídas en invoices).</summary>
    private async Task<Package> LoadAsync(IQueryable<Invoice>? query, CancellationToken ct, List<Invoice>? invoices = null)
    {
        var list = invoices ?? await query!.Include(i => i.Lines).OrderBy(i => i.IssueDate).ThenBy(i => i.InvoiceId).ToListAsync(ct);
        var tenantRow = await db.Tenants.AsNoTracking().FirstAsync(x => x.TenantId == T.RequireTenantId(), ct);
        var pkg = new Package { Invoices = list, Accounts = AccountsOf(tenantRow), Terms = QuickBooksRules.ParseTerms(tenantRow.QuickBooksTermsJson) };
        if (list.Count == 0) return pkg;

        foreach (var s in await db.StatusCodes.AsNoTracking().Where(s => s.Entity == BillingCodes.InvoiceStatusDomain).ToListAsync(ct)) pkg.StatusCodes[s.StatusCodeId] = s.InternalCode;
        var lines = list.SelectMany(i => i.Lines).ToList();
        foreach (var id in lines.Select(l => l.ChargeTypeLookupId).Distinct())
            pkg.ChargeCodes[id] = (await lookups.GetAsync(id, ct))?.InternalCode ?? string.Empty;

        // órdenes de venta (números para el memo) y componentes de los kits
        var lineIds = lines.Where(l => l.SalesOrderLineId != null).Select(l => l.SalesOrderLineId!.Value).Distinct().ToList();
        var soLines = lineIds.Count == 0 ? new List<SalesOrderLine>()
            : await db.Set<SalesOrderLine>().AsNoTracking().Where(l => lineIds.Contains(l.SalesOrderLineId)).ToListAsync(ct);
        foreach (var l in soLines) pkg.LineOrder[l.SalesOrderLineId] = l.SalesOrderId;
        var orderIds = soLines.Select(l => l.SalesOrderId).Concat(lines.Where(l => l.SalesOrderId != null).Select(l => l.SalesOrderId!.Value)).Distinct().ToList();
        if (orderIds.Count > 0)
            foreach (var o in await db.Set<SalesOrder>().AsNoTracking().Where(o => orderIds.Contains(o.SalesOrderId)).Select(o => new { o.SalesOrderId, o.Number }).ToListAsync(ct))
                pkg.OrderNumbers[o.SalesOrderId] = o.Number;
        var comps = lineIds.Count == 0 ? new List<SalesOrderLineComponent>()
            : await db.Set<SalesOrderLineComponent>().AsNoTracking().Where(c => lineIds.Contains(c.SalesOrderLineId)).OrderBy(c => c.SalesOrderLineComponentId).ToListAsync(ct);
        foreach (var g in comps.GroupBy(c => c.SalesOrderLineId)) pkg.Parts[g.Key] = g.Select(c => (c.ComponentProductId, c.Quantity)).ToList();

        var productIds = lines.Where(l => l.ProductId != null).Select(l => l.ProductId!.Value).Concat(comps.Select(c => c.ComponentProductId)).Distinct().ToList();
        await AddProductsAsync(pkg, productIds, ct);
        // kits sin foto de componentes (líneas anteriores al Lote 69): la receta del catálogo
        var kitTypeId = await lookups.TryGetIdAsync(KitCodes.ProductTypeDomain, ProductTypes.Kit, ct);
        var orphanKits = lines.Where(l => l.SalesOrderLineId is int sl && !pkg.Parts.ContainsKey(sl) && l.ProductId is int p && pkg.Products.TryGetValue(p, out var prod)
                                          && kitTypeId is not null && prod.ProductTypeLookupId == kitTypeId).ToList();
        if (orphanKits.Count > 0)
        {
            var kitIds = orphanKits.Select(l => l.ProductId!.Value).Distinct().ToList();
            var recipes = await db.ProductKitComponents.AsNoTracking().Where(k => kitIds.Contains(k.KitProductId)).OrderBy(k => k.SortOrder).ThenBy(k => k.ProductKitComponentId).ToListAsync(ct);
            foreach (var l in orphanKits)
            {
                var parts = recipes.Where(r => r.KitProductId == l.ProductId).Select(r => (r.ComponentProductId, r.Quantity)).ToList();
                if (parts.Count > 0) pkg.Parts[l.SalesOrderLineId!.Value] = parts;
            }
            await AddProductsAsync(pkg, recipes.Select(r => r.ComponentProductId).Distinct().Where(id => !pkg.Products.ContainsKey(id)).ToList(), ct);
        }

        // clientes, su «Código QuickBooks» (migración) y su término
        var clientIds = list.Select(i => i.ClientId).Distinct().ToList();
        foreach (var c in await db.Clients.AsNoTracking().Where(c => clientIds.Contains(c.ClientId)).ToListAsync(ct))
        {
            pkg.Clients[c.ClientId] = c;
            if (c.PaymentTermLookupId is int term) pkg.TermCodes[c.ClientId] = (await lookups.GetAsync(term, ct))?.InternalCode ?? string.Empty;
        }
        var clientEntity = await lookups.TryGetIdAsync(LookupDomains.EntityType, EntityTypes.Client, ct);
        if (clientEntity is int ce)
        {
            var defIds = await db.CustomFieldDefinitions.AsNoTracking().Where(d => d.EntityTypeLookupId == ce && d.FieldKey == LegacyImportPlanner.QbCodeFieldKey && d.IsActive)
                .Select(d => d.CustomFieldDefinitionId).ToListAsync(ct);
            if (defIds.Count > 0)
                foreach (var v in await db.CustomFieldValues.AsNoTracking().Where(v => defIds.Contains(v.CustomFieldDefinitionId) && clientIds.Contains(v.EntityId) && v.ValueText != null)
                             .Select(v => new { v.EntityId, v.ValueText }).ToListAsync(ct))
                    if (!string.IsNullOrWhiteSpace(v.ValueText)) pkg.QbNames[v.EntityId] = v.ValueText.Trim();
        }

        // datos de los clientes nuevos: direcciones (física = CORPORATE, postal = BILLING), contacto principal, teléfonos y correo
        var newIds = pkg.Clients.Values.Where(c => IsNewClient(pkg, c)).Select(c => c.ClientId).ToList();
        if (newIds.Count > 0) await AddClientDataAsync(pkg, newIds, ct);
        return pkg;
    }

    private async Task AddProductsAsync(Package pkg, List<int> ids, CancellationToken ct)
    {
        if (ids.Count == 0) return;
        foreach (var p in await db.Products.AsNoTracking().Where(p => ids.Contains(p.ProductId)).ToListAsync(ct))
        {
            pkg.Products[p.ProductId] = p;
            if (p.TaxCategoryLookupId is int tc && (await lookups.GetAsync(tc, ct))?.InternalCode is TaxCategories.Exempt or TaxCategories.ZeroRate) pkg.NonTaxableProducts.Add(p.ProductId);
        }
    }

    private async Task AddClientDataAsync(Package pkg, List<int> clientIds, CancellationToken ct)
    {
        var corporate = await lookups.TryGetIdAsync(LookupDomains.LocationType, LocationTypes.Corporate, ct);
        var billing = await lookups.TryGetIdAsync(LookupDomains.LocationType, LocationTypes.Billing, ct);
        var typeIds = new[] { corporate, billing }.Where(x => x is not null).Select(x => x!.Value).ToList();
        if (corporate is int co) pkg.LocationTypeIds[LocationTypes.Corporate] = co;
        if (billing is int bi) pkg.LocationTypeIds[LocationTypes.Billing] = bi;
        foreach (var g in (await db.Locations.AsNoTracking().Where(l => l.ClientId != null && clientIds.Contains(l.ClientId.Value) && l.IsActive && typeIds.Contains(l.LocationTypeLookupId))
                     .OrderBy(l => l.LocationId).ToListAsync(ct)).GroupBy(l => l.ClientId!.Value))
            pkg.Locations[g.Key] = g.ToList();

        var contacts = await db.ClientContacts.AsNoTracking().Where(c => clientIds.Contains(c.ClientId) && c.IsActive)
            .OrderByDescending(c => c.IsPrimary).ThenBy(c => c.ClientContactId).ToListAsync(ct);
        var contactEntity = await lookups.TryGetIdAsync(LookupDomains.EntityType, EntityTypes.ClientContact, ct);
        var clientEntity = await lookups.TryGetIdAsync(LookupDomains.EntityType, EntityTypes.Client, ct);
        var contactIds = contacts.Select(c => c.ClientContactId).ToList();
        var points = await db.ContactPoints.AsNoTracking()
            .Where(p => p.IsActive && ((contactEntity != null && p.OwnerEntityLookupId == contactEntity && contactIds.Contains(p.OwnerId))
                                       || (clientEntity != null && p.OwnerEntityLookupId == clientEntity && clientIds.Contains(p.OwnerId))))
            .OrderByDescending(p => p.IsPrimary).ThenBy(p => p.ContactPointId).ToListAsync(ct);
        var typeCodes = new Dictionary<int, string>();
        foreach (var id in points.Select(p => p.ContactTypeLookupId).Distinct()) typeCodes[id] = (await lookups.GetAsync(id, ct))?.InternalCode ?? string.Empty;
        foreach (var clientId in clientIds)
        {
            var mine = contacts.Where(c => c.ClientId == clientId).ToList();
            var mineIds = mine.Select(c => c.ClientContactId).ToHashSet();
            // primero los del contacto principal, luego los de los demás contactos y los del cliente
            var own = points.Where(p => (p.OwnerEntityLookupId == contactEntity && mineIds.Contains(p.OwnerId)) || (p.OwnerEntityLookupId == clientEntity && p.OwnerId == clientId))
                .OrderBy(p => p.OwnerEntityLookupId == contactEntity && mine.Count > 0 && p.OwnerId == mine[0].ClientContactId ? 0 : 1).ToList();
            var phones = own.Where(p => typeCodes.GetValueOrDefault(p.ContactTypeLookupId) is "PHONE" or "MOBILE")
                .Select(p => string.IsNullOrWhiteSpace(p.Extension) ? p.Value : $"{p.Value} x{p.Extension}").Distinct().ToList();
            var email = own.FirstOrDefault(p => typeCodes.GetValueOrDefault(p.ContactTypeLookupId) == "EMAIL")?.Value;
            pkg.Contacts[clientId] = (mine.FirstOrDefault()?.FullName, phones, email);
        }
    }

    // ================================================================ armar filas y archivo

    private static bool IsNewClient(Package pkg, Client c) => c.QuickBooksSentAt is null && !pkg.QbNames.ContainsKey(c.ClientId);

    private static string QbName(Package pkg, Client c) => pkg.QbNames.GetValueOrDefault(c.ClientId) ?? c.Name;

    private static bool IsShipping(Package pkg, InvoiceLine l) => l.SalesOrderId != null || pkg.ChargeCodes.GetValueOrDefault(l.ChargeTypeLookupId) == SalesShippingCodes.ChargeType;

    /// <summary>Los productos que viajan como ítems de QuickBooks: los de las líneas que no son kit y los componentes de los kits (el kit no).</summary>
    private static IEnumerable<Product> ItemProducts(Package pkg) => ItemProductsOf(pkg, pkg.Invoices);

    private static IEnumerable<Product> ItemProductsOf(Package pkg, IEnumerable<Invoice> invoices)
    {
        var seen = new HashSet<int>();
        foreach (var l in invoices.SelectMany(i => i.Lines))
        {
            if (l.ProductId is not int pid || IsShipping(pkg, l)) continue;
            var ids = l.SalesOrderLineId is int sl && pkg.Parts.TryGetValue(sl, out var parts) ? parts.Select(p => p.ProductId) : new[] { pid };
            foreach (var id in ids)
                if (seen.Add(id) && pkg.Products.TryGetValue(id, out var p)) yield return p;
        }
    }

    private static bool IsTaxable(Package pkg, Product p) => !pkg.NonTaxableProducts.Contains(p.ProductId);

    private static decimal Money(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);

    private List<QuickBooksPendingInvoiceDto> Rows(Package pkg) => pkg.Invoices.Select(i =>
    {
        var c = pkg.Clients[i.ClientId];
        var orders = OrdersOf(pkg, i);
        var newSkus = ItemProductsOf(pkg, new[] { i }).Where(p => p.QuickBooksSentAt is null).Select(p => p.Sku).OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList();
        return new QuickBooksPendingInvoiceDto(i.PublicId, i.InvoiceNumber, i.IssueDate, i.DueDate, pkg.StatusCodes.GetValueOrDefault(i.StatusCodeId, string.Empty), c.PublicId, c.Code,
            c.Name, QuickBooksIifWriter.Name(QbName(pkg, c)), Money(i.Subtotal), Money(i.TaxAmount), Money(i.Subtotal + i.TaxAmount), orders, i.QuickBooksExportedAt, IsNewClient(pkg, c), newSkus);
    }).ToList();

    private static List<string> OrdersOf(Package pkg, Invoice i)
        => i.Lines.Select(l => l.SalesOrderId ?? (l.SalesOrderLineId is int sl && pkg.LineOrder.TryGetValue(sl, out var o) ? o : (int?)null))
            .Where(o => o is not null).Select(o => pkg.OrderNumbers.GetValueOrDefault(o!.Value)).Where(n => n is not null).Select(n => n!).Distinct().OrderBy(n => n, StringComparer.Ordinal).ToList();

    private List<QuickBooksNewClientDto> NewClients(Package pkg) => pkg.Clients.Values.Where(c => IsNewClient(pkg, c)).OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).Select(c =>
    {
        var cu = Customer(pkg, c);
        return new QuickBooksNewClientDto(c.PublicId, c.Code, QuickBooksIifWriter.Name(cu.Name), cu.CompanyName, cu.BillTo, cu.ShipTo, cu.Phone1, cu.Phone2, cu.Email, cu.Contact,
            pkg.TermCodes.GetValueOrDefault(c.ClientId), cu.Terms, cu.ResaleNumber, cu.CreditLimit);
    }).ToList();

    private static List<QuickBooksNewProductDto> NewProducts(Package pkg) => ItemProducts(pkg).Where(p => p.QuickBooksSentAt is null).OrderBy(p => p.Sku, StringComparer.OrdinalIgnoreCase)
        .Select(p => new QuickBooksNewProductDto(p.PublicId, p.Sku, QuickBooksIifWriter.Name(p.Sku), p.Name, p.SalePrice, p.PurchaseCost, IsTaxable(pkg, p))).ToList();

    private static List<string> Warnings(Package pkg)
    {
        var w = new List<string>();
        foreach (var c in pkg.Clients.Values.Where(c => IsNewClient(pkg, c) && QuickBooksIifWriter.Name(QbName(pkg, c)).Length > QuickBooksRules.MaxCustomerLength).OrderBy(c => c.Name))
            w.Add(QuickBooksRules.CustomerNameTooLong(QbName(pkg, c)));
        foreach (var p in ItemProducts(pkg).Where(p => QuickBooksIifWriter.Name(p.Sku).Length > QuickBooksRules.MaxItemLength).OrderBy(p => p.Sku))
            w.Add(QuickBooksRules.ItemNameTooLong(p.Sku));
        foreach (var code in pkg.Invoices.Select(i => pkg.TermCodes.GetValueOrDefault(i.ClientId)).Where(c => !string.IsNullOrEmpty(c) && !pkg.Terms.ContainsKey(c!)).Distinct().OrderBy(c => c))
            w.Add(QuickBooksRules.UnmappedTerm(code!));
        return w;
    }

    private static string? TermsOf(Package pkg, int clientId)
        => pkg.TermCodes.TryGetValue(clientId, out var code) && pkg.Terms.TryGetValue(code, out var name) ? name : null;

    private static QuickBooksCustomer Customer(Package pkg, Client c)
    {
        var locs = pkg.Locations.GetValueOrDefault(c.ClientId) ?? new List<Location>();
        // la física es la CORPORATE y la postal la BILLING; sin postal, se factura a la física
        var physical = locs.FirstOrDefault(l => IsType(l, LocationTypes.Corporate, pkg));
        var postal = locs.FirstOrDefault(l => IsType(l, LocationTypes.Billing, pkg)) ?? physical;
        var (contact, phones, email) = pkg.Contacts.GetValueOrDefault(c.ClientId, (null, new List<string>(), null));
        return new QuickBooksCustomer(QbName(pkg, c), c.Code, c.LegalName ?? c.Name, AddressLines(c, postal), AddressLines(c, physical),
            phones.ElementAtOrDefault(0), phones.ElementAtOrDefault(1), email, contact, TermsOf(pkg, c.ClientId), c.MerchantRegistration, c.CreditLimit);
    }

    /// <summary>¿La dirección es de ese tipo (CORPORATE = física, BILLING = postal)?</summary>
    private static bool IsType(Location l, string type, Package pkg) => pkg.LocationTypeIds.TryGetValue(type, out var id) && l.LocationTypeLookupId == id;

    private static IReadOnlyList<string> AddressLines(Client c, Location? l)
    {
        if (l is null) return Array.Empty<string>();
        var city = string.Join(" ", new[] { l.City, l.State, l.PostalCode }.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!.Trim()));
        return new[] { c.LegalName ?? c.Name, l.Line1, l.Line2 ?? string.Empty, city }.Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
    }

    private QuickBooksInvoice ToIif(Package pkg, Invoice i)
    {
        var c = pkg.Clients[i.ClientId];
        var lines = new List<QuickBooksInvoiceLine>();
        foreach (var l in i.Lines.OrderBy(l => l.InvoiceLineId))
        {
            var taxable = l.TaxAmount != 0m || (!l.IsTaxExempt && (l.TaxRatePct ?? 0m) > 0m);
            if (IsShipping(pkg, l))
            {
                lines.Add(new QuickBooksInvoiceLine(pkg.Accounts.ShippingItem, l.Description, l.Quantity, l.UnitAmount, taxable));
                continue;
            }
            if (l.ProductId is int pid && pkg.Products.TryGetValue(pid, out var p))
            {
                IReadOnlyList<QuickBooksKitPart>? parts = null;
                if (l.SalesOrderLineId is int sl && pkg.Parts.TryGetValue(sl, out var comp))
                    parts = comp.Select(x => pkg.Products.TryGetValue(x.ProductId, out var cp)
                        ? new QuickBooksKitPart(cp.Sku, cp.Name, x.Quantity, cp.SalePrice)
                        : new QuickBooksKitPart(x.ProductId.ToString(CultureInfo.InvariantCulture), null, x.Quantity, null)).ToList();
                lines.Add(new QuickBooksInvoiceLine(p.Sku, p.Name, l.Quantity, l.UnitAmount, taxable, parts));
                continue;
            }
            lines.Add(new QuickBooksInvoiceLine(pkg.ChargeCodes.GetValueOrDefault(l.ChargeTypeLookupId, "OTHER"), l.Description, l.Quantity, l.UnitAmount, taxable));
        }
        var memo = string.Join(", ", OrdersOf(pkg, i));
        return new QuickBooksInvoice(i.InvoiceNumber, i.IssueDate ?? clock.Today, i.DueDate, QbName(pkg, c), TermsOf(pkg, c.ClientId), memo, lines, i.TaxAmount);
    }
}
