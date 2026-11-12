using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Billing;
using Teikem.Domain.Constants;
using Teikem.Domain.Migration;
using Teikem.Domain.Sales;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 241 — una línea de venta del análisis, venga del historial importado de QuickBooks o de una factura / nota de crédito de Teikem. Sku y Item: el SKU y el nombre
/// (del producto de Teikem o, sin emparejar, los del historial). Quantity y Amount con signo (las notas de crédito restan).
/// </summary>
public sealed record SalesAnalysisLine(DateOnly Date, string Source, string Type, string? Number, int? ClientId, string ClientName, int? ProductId, string Sku, string Item,
    string LineClass, decimal Quantity, decimal? UnitPrice, decimal Amount);

/// <summary>Una línea de compra del análisis (historial importado o recibo de una orden de compra en Teikem).</summary>
public sealed record SalesAnalysisPurchase(DateOnly Date, string Source, int? ProductId, string Sku, string Item, decimal Quantity, decimal Amount);

/// <summary>Datos de un producto que usa el análisis.</summary>
public sealed record SalesAnalysisProduct(int ProductId, string Sku, string Name, string? Brand, string? Category, decimal? Price, decimal? Cost, decimal? MinQty,
    bool IsActive, bool IsKit);

/// <summary>
/// Lote 241 — lector de las líneas del análisis (decisión de Luis): con fecha de corte C, el HISTORIAL importado cuenta antes de C (exclusiva) y las FACTURAS de Teikem
/// desde C; sin fecha de corte cuentan las dos fuentes completas (el estado avisa si se traslapan).
/// - Factura de Teikem: emitida, vencida o pagada (activa), por su fecha de factura (IssueDate). Sus líneas de producto de una orden de venta son PRODUCT (Cantidad ×
///   precio de la línea: el descuento ya va en el precio); un kit se reparte entre sus piezas con la regla del IIF de QuickBooks (la foto del kit de la línea de la orden o,
///   sin ella, la receta del catálogo; peso = cantidad por kit × precio de lista); el cargo de envío de la orden es SHIPPING. Las demás líneas (transporte, almacén) no son
///   venta y no entran.
/// - Nota de crédito de Teikem aprobada o aplicada, por el día (de la compañía) en que se aprobó: sus líneas de producto (o que acreditan una línea de producto o de envío de
///   una factura de venta) restan igual; las de concepto libre sin producto no entran.
/// - Compras: historial importado antes de C; desde C, lo recibido en Teikem contra órdenes de compra (por el día en que se recibió; costo de la línea de la orden).
/// Todo bajo el filtro de tenant (AsNoTracking).
/// </summary>
public sealed class SalesAnalysisLineReader(TeikemDbContext db, ITenantClock clock, ILookupCache lookups)
{
    private static readonly string[] InvoiceCounted = { InvoiceStatuses.Issued, InvoiceStatuses.Overdue, InvoiceStatuses.Paid };
    private static readonly string[] CreditCounted = { CreditNoteStatuses.Approved, CreditNoteStatuses.Applied };

    private Dictionary<int, SalesAnalysisProduct>? _products;
    private Dictionary<int, string>? _clientNames;
    private SalesAnalysisSetting? _settings;

    /// <summary>Los ajustes de la compañía (una fila; si no hay, los valores por omisión sin fecha de corte).</summary>
    public async Task<SalesAnalysisSetting> SettingsAsync(CancellationToken ct)
        => _settings ??= await db.Set<SalesAnalysisSetting>().AsNoTracking().FirstOrDefaultAsync(ct) ?? new SalesAnalysisSetting();

    /// <summary>Los productos propios de la compañía (activos e inactivos) con su categoría.</summary>
    public async Task<Dictionary<int, SalesAnalysisProduct>> ProductsAsync(CancellationToken ct)
    {
        if (_products is not null) return _products;
        var kitType = await lookups.TryGetIdAsync(KitCodes.ProductTypeDomain, ProductTypes.Kit, ct);
        var categories = await db.ProductCategories.AsNoTracking().Select(c => new { c.ProductCategoryId, c.Name }).ToDictionaryAsync(c => c.ProductCategoryId, c => c.Name, ct);
        var rows = await db.Products.AsNoTracking().Where(p => p.ClientId == null)
            .Select(p => new { p.ProductId, p.Sku, p.Name, p.Brand, p.ProductCategoryId, p.SalePrice, p.PurchaseCost, p.MinQty, p.IsActive, p.ProductTypeLookupId, p.IsOfferKit })
            .ToListAsync(ct);
        _products = rows.ToDictionary(p => p.ProductId, p => new SalesAnalysisProduct(p.ProductId, p.Sku, p.Name, string.IsNullOrWhiteSpace(p.Brand) ? null : p.Brand.Trim(),
            p.ProductCategoryId is int c ? categories.GetValueOrDefault(c) : null, p.SalePrice, p.PurchaseCost, p.MinQty, p.IsActive,
            p.IsOfferKit || (kitType is int k && p.ProductTypeLookupId == k)));
        return _products;
    }

    public async Task<Dictionary<int, string>> ClientNamesAsync(CancellationToken ct)
        => _clientNames ??= await db.Clients.AsNoTracking().Select(c => new { c.ClientId, c.Name }).ToDictionaryAsync(c => c.ClientId, c => c.Name, ct);

    /// <summary>Partes [desde, hasta) de cada fuente según la fecha de corte: (historial desde, historial hasta, Teikem desde).</summary>
    public static (DateOnly HistoryFrom, DateOnly HistoryTo, DateOnly TeikemFrom) Split(DateOnly from, DateOnly toExclusive, DateOnly? cutoff)
    {
        if (cutoff is not DateOnly c) return (from, toExclusive, from);
        var hTo = c < toExclusive ? c : toExclusive;
        var tFrom = c > from ? c : from;
        return (from, hTo, tFrom);
    }

    /// <summary>Las líneas de venta de [desde, hasta exclusivo).</summary>
    public async Task<List<SalesAnalysisLine>> LoadSalesAsync(DateOnly from, DateOnly toExclusive, CancellationToken ct)
    {
        var settings = await SettingsAsync(ct);
        var (hFrom, hTo, tFrom) = Split(from, toExclusive, settings.CutoffDate);
        var products = await ProductsAsync(ct);
        var clients = await ClientNamesAsync(ct);
        var lines = new List<SalesAnalysisLine>();

        if (hFrom < hTo)
        {
            var hist = await db.Set<SalesHistoryLine>().AsNoTracking().Where(l => l.TxnDate >= hFrom && l.TxnDate < hTo)
                .Select(l => new { l.TxnDate, l.TxnType, l.DocNumber, l.ClientId, l.ClientNameRaw, l.ProductId, l.ItemSku, l.ItemRaw, l.LineClass, l.Quantity, l.UnitPrice, l.Amount })
                .ToListAsync(ct);
            foreach (var h in hist)
            {
                var p = h.ProductId is int pid ? products.GetValueOrDefault(pid) : null;
                var (_, desc) = QuickBooksDetailReportParser.SplitItem(h.ItemRaw);
                lines.Add(new SalesAnalysisLine(h.TxnDate, SalesAnalysisCodes.SourceHistory, h.TxnType, h.DocNumber, h.ClientId,
                    h.ClientId is int cid && clients.TryGetValue(cid, out var cn) ? cn : h.ClientNameRaw,
                    h.ProductId, p?.Sku ?? h.ItemSku, p?.Name ?? desc ?? h.ItemRaw, h.LineClass, h.Quantity, h.UnitPrice, h.Amount));
            }
        }
        if (tFrom < toExclusive)
        {
            await AddInvoicesAsync(lines, tFrom, toExclusive, products, clients, ct);
            await AddCreditsAsync(lines, tFrom, toExclusive, products, clients, ct);
        }
        return lines;
    }

    private async Task AddInvoicesAsync(List<SalesAnalysisLine> lines, DateOnly from, DateOnly toExclusive, Dictionary<int, SalesAnalysisProduct> products,
        Dictionary<int, string> clients, CancellationToken ct)
    {
        var issued = await db.StatusCodes.AsNoTracking().Where(s => s.Entity == BillingCodes.InvoiceStatusDomain && InvoiceCounted.Contains(s.InternalCode))
            .Select(s => s.StatusCodeId).ToListAsync(ct);
        var rows = await (from i in db.Invoices.AsNoTracking()
                          where i.IsActive && issued.Contains(i.StatusCodeId) && i.IssueDate != null && i.IssueDate >= @from && i.IssueDate < toExclusive
                          from l in i.Lines
                          where l.SalesOrderLineId != null || l.SalesOrderId != null
                          select new { i.InvoiceNumber, Date = i.IssueDate!.Value, i.ClientId, l.ProductId, l.SalesOrderLineId, l.SalesOrderId, l.Quantity, l.UnitAmount, l.Description })
            .ToListAsync(ct);
        if (rows.Count == 0) return;
        var parts = await KitPartsAsync(rows.Where(r => r.SalesOrderLineId != null).Select(r => r.SalesOrderLineId!.Value).Distinct().ToList(),
            rows.Where(r => r.ProductId != null).Select(r => r.ProductId!.Value).Distinct().ToList(), products, ct);
        foreach (var r in rows)
        {
            var client = clients.GetValueOrDefault(r.ClientId) ?? string.Empty;
            var amount = QuickBooksRules.LineAmount(r.Quantity, r.UnitAmount);
            Expand(lines, r.Date, SalesAnalysisCodes.SourceInvoice, "Invoice", r.InvoiceNumber, r.ClientId, client, r.ProductId, r.SalesOrderLineId,
                r.SalesOrderId != null, r.Description, r.Quantity, r.UnitAmount, amount, products, parts);
        }
    }

    private async Task AddCreditsAsync(List<SalesAnalysisLine> lines, DateOnly from, DateOnly toExclusive, Dictionary<int, SalesAnalysisProduct> products,
        Dictionary<int, string> clients, CancellationToken ct)
    {
        var ok = await db.StatusCodes.AsNoTracking().Where(s => s.Entity == CreditCodes.StatusDomain && CreditCounted.Contains(s.InternalCode))
            .Select(s => s.StatusCodeId).ToListAsync(ct);
        var (fromUtc, toUtc) = (clock.StartOfDayUtc(from), clock.StartOfDayUtc(toExclusive));
        var rows = await (from n in db.CreditNotes.AsNoTracking()
                          where ok.Contains(n.StatusCodeId) && n.ApprovedAtUtc != null && n.ApprovedAtUtc >= fromUtc && n.ApprovedAtUtc < toUtc
                          from l in n.Lines
                          select new { n.CreditNumber, At = n.ApprovedAtUtc!.Value, n.ClientId, l.ProductId, l.InvoiceLineId, l.Quantity, l.UnitAmount, l.Description })
            .ToListAsync(ct);
        if (rows.Count == 0) return;
        var invLineIds = rows.Where(r => r.InvoiceLineId != null).Select(r => r.InvoiceLineId!.Value).Distinct().ToList();
        var invLines = invLineIds.Count == 0
            ? new Dictionary<int, (int? ProductId, int? SoLine, bool Shipping)>()
            : (await db.Set<InvoiceLine>().AsNoTracking().Where(l => invLineIds.Contains(l.InvoiceLineId))
                .Select(l => new { l.InvoiceLineId, l.ProductId, l.SalesOrderLineId, l.SalesOrderId }).ToListAsync(ct))
                .ToDictionary(l => l.InvoiceLineId, l => (ProductId: l.ProductId, SoLine: l.SalesOrderLineId, Shipping: l.SalesOrderId != null));
        var soLineIds = invLines.Values.Where(v => v.SoLine != null).Select(v => v.SoLine!.Value).Distinct().ToList();
        var productIds = rows.Where(r => r.ProductId != null).Select(r => r.ProductId!.Value).Concat(invLines.Values.Where(v => v.ProductId != null).Select(v => v.ProductId!.Value))
            .Distinct().ToList();
        var parts = await KitPartsAsync(soLineIds, productIds, products, ct);
        foreach (var r in rows)
        {
            (int? ProductId, int? SoLine, bool Shipping) inv = r.InvoiceLineId is int il && invLines.TryGetValue(il, out var x) ? x : (null, null, false);
            var productId = r.ProductId ?? inv.ProductId;
            var isSalesLine = inv.SoLine != null || inv.Shipping;
            if (productId is null && !isSalesLine) continue;   // concepto libre (transporte, almacén…): no es venta
            var amount = -QuickBooksRules.LineAmount(r.Quantity, r.UnitAmount);
            Expand(lines, clock.DayOf(r.At), SalesAnalysisCodes.SourceCredit, "Credit Memo", r.CreditNumber, r.ClientId, clients.GetValueOrDefault(r.ClientId) ?? string.Empty,
                productId, inv.SoLine, inv.Shipping && productId is null, r.Description, -r.Quantity, r.UnitAmount, amount, products, parts);
        }
    }

    /// <summary>Agrega una línea de Teikem: envío, producto (un kit se reparte entre sus piezas) u otro.</summary>
    private static void Expand(List<SalesAnalysisLine> lines, DateOnly date, string source, string type, string number, int clientId, string client, int? productId,
        int? soLineId, bool shipping, string description, decimal qty, decimal unitPrice, decimal amount, Dictionary<int, SalesAnalysisProduct> products, KitParts parts)
    {
        if (shipping)
        {
            lines.Add(new SalesAnalysisLine(date, source, type, number, clientId, client, null, "SHIPPING", description, SalesAnalysisCodes.ClassShipping, qty, unitPrice, amount));
            return;
        }
        if (productId is not int pid)
        {
            lines.Add(new SalesAnalysisLine(date, source, type, number, clientId, client, null, string.Empty, description, SalesAnalysisCodes.ClassOther, qty, unitPrice, amount));
            return;
        }
        var comp = soLineId is int sl && parts.ByLine.TryGetValue(sl, out var byLine) ? byLine : parts.ByKit.GetValueOrDefault(pid);
        if (comp is { Count: > 0 })
        {
            foreach (var share in SalesAnalysisRules.SplitKit(qty, amount, comp))
            {
                var cp = products.GetValueOrDefault(share.ProductId);
                lines.Add(new SalesAnalysisLine(date, source, type, number, clientId, client, share.ProductId, cp?.Sku ?? string.Empty, cp?.Name ?? description,
                    SalesAnalysisCodes.ClassProduct, share.Quantity, share.Quantity == 0m ? null : Math.Round(share.Amount / share.Quantity, 5), share.Amount));
            }
            return;
        }
        var p = products.GetValueOrDefault(pid);
        lines.Add(new SalesAnalysisLine(date, source, type, number, clientId, client, pid, p?.Sku ?? string.Empty, p?.Name ?? description, SalesAnalysisCodes.ClassProduct,
            qty, unitPrice, amount));
    }

    private sealed record KitParts(Dictionary<int, List<SalesKitPart>> ByLine, Dictionary<int, List<SalesKitPart>> ByKit);

    /// <summary>Piezas de los kits: la foto de cada línea de orden y, para los productos de tipo kit sin foto, la receta del catálogo.</summary>
    private async Task<KitParts> KitPartsAsync(List<int> soLineIds, List<int> productIds, Dictionary<int, SalesAnalysisProduct> products, CancellationToken ct)
    {
        var byLine = new Dictionary<int, List<SalesKitPart>>();
        if (soLineIds.Count > 0)
        {
            foreach (var chunk in soLineIds.Chunk(2000))
            {
                var ids = chunk.ToList();
                var comps = await db.Set<SalesOrderLineComponent>().AsNoTracking().Where(c => ids.Contains(c.SalesOrderLineId))
                    .OrderBy(c => c.SalesOrderLineComponentId).Select(c => new { c.SalesOrderLineId, c.ComponentProductId, c.Quantity }).ToListAsync(ct);
                foreach (var g in comps.GroupBy(c => c.SalesOrderLineId))
                    byLine[g.Key] = g.Select(c => new SalesKitPart(c.ComponentProductId, c.Quantity, products.GetValueOrDefault(c.ComponentProductId)?.Price)).ToList();
            }
        }
        var kitIds = productIds.Where(id => products.TryGetValue(id, out var p) && p.IsKit).ToList();
        var byKit = new Dictionary<int, List<SalesKitPart>>();
        if (kitIds.Count > 0)
        {
            var recipes = await db.ProductKitComponents.AsNoTracking().Where(k => kitIds.Contains(k.KitProductId))
                .OrderBy(k => k.SortOrder).ThenBy(k => k.ProductKitComponentId).Select(k => new { k.KitProductId, k.ComponentProductId, k.Quantity }).ToListAsync(ct);
            foreach (var g in recipes.GroupBy(r => r.KitProductId))
                byKit[g.Key] = g.Select(r => new SalesKitPart(r.ComponentProductId, r.Quantity, products.GetValueOrDefault(r.ComponentProductId)?.Price)).ToList();
        }
        return new KitParts(byLine, byKit);
    }

    /// <summary>Las compras de [desde, hasta exclusivo): historial antes del corte y, desde él, lo recibido contra órdenes de compra en Teikem.</summary>
    public async Task<List<SalesAnalysisPurchase>> LoadPurchasesAsync(DateOnly from, DateOnly toExclusive, CancellationToken ct)
    {
        var settings = await SettingsAsync(ct);
        var (hFrom, hTo, tFrom) = Split(from, toExclusive, settings.CutoffDate);
        var products = await ProductsAsync(ct);
        var list = new List<SalesAnalysisPurchase>();
        if (hFrom < hTo)
        {
            var hist = await db.Set<PurchaseHistoryLine>().AsNoTracking().Where(l => l.TxnDate >= hFrom && l.TxnDate < hTo)
                .Select(l => new { l.TxnDate, l.ProductId, l.ItemSku, l.ItemRaw, l.Quantity, l.Amount }).ToListAsync(ct);
            list.AddRange(hist.Select(h => new SalesAnalysisPurchase(h.TxnDate, SalesAnalysisCodes.SourceHistory, h.ProductId,
                h.ProductId is int pid && products.TryGetValue(pid, out var p) ? p.Sku : h.ItemSku,
                h.ProductId is int pid2 && products.TryGetValue(pid2, out var p2) ? p2.Name : (QuickBooksDetailReportParser.SplitItem(h.ItemRaw).Description ?? h.ItemRaw),
                h.Quantity, h.Amount)));
        }
        if (tFrom < toExclusive)
        {
            var (fromUtc, toUtc) = (clock.StartOfDayUtc(tFrom), clock.StartOfDayUtc(toExclusive));
            var received = await (from rl in db.ReceiptLines.AsNoTracking()
                                  join rh in db.ReceiptHeaders.AsNoTracking() on rl.ReceiptHeaderId equals rh.ReceiptHeaderId
                                  join al in db.AsnLines.AsNoTracking() on rl.AsnLineId equals (int?)al.AsnLineId
                                  join pl in db.PurchaseOrderLines.AsNoTracking() on al.PurchaseOrderLineId equals (int?)pl.PurchaseOrderLineId
                                  where rh.IsActive && rh.ReceivedAtUtc != null && rh.ReceivedAtUtc >= fromUtc && rh.ReceivedAtUtc < toUtc && rl.ReceivedQty != 0
                                  select new { At = rh.ReceivedAtUtc!.Value, rl.ProductId, rl.ReceivedQty, pl.UnitCost }).ToListAsync(ct);
            list.AddRange(received.Select(r => new SalesAnalysisPurchase(clock.DayOf(r.At), SalesAnalysisCodes.SourceInvoice, r.ProductId,
                products.GetValueOrDefault(r.ProductId)?.Sku ?? string.Empty, products.GetValueOrDefault(r.ProductId)?.Name ?? string.Empty,
                r.ReceivedQty, Math.Round(r.ReceivedQty * r.UnitCost, 2))));
        }
        return list;
    }

    /// <summary>Clave de agrupación de un producto del análisis: el id del producto o, sin emparejar, el SKU normalizado del historial.</summary>
    public static string ProductKey(int? productId, string sku) => productId is int id ? "P" + id : "S" + LegacyImportRules.SkuKey(sku);
}
