using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Constants;
using Teikem.Domain.Orders;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Migration;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Wms;

namespace Teikem.Infrastructure.Services;

/// <summary>Un aviso del importador: de una línea del archivo o de la orden completa (Line = 0). Bloqueante = la orden no se importa.</summary>
public sealed record PurchaseOrderImportIssueDto(int Line, string? Sku, string Code, string Message, bool Blocking);

public sealed record PurchaseOrderImportDocDto(string Key, string Vendor, string? Number, DateOnly? Date, string Status, int Lines, decimal Total,
    string? PurchaseOrderNumber, Guid? PurchaseOrderPublicId, IReadOnlyList<PurchaseOrderImportIssueDto> Issues);

/// <summary>Un producto del archivo que no existe en Teikem (lista para dar de alta): SKU, descripción, cantidad total y último costo.</summary>
public sealed record PurchaseOrderImportMissingProductDto(string Sku, string Description, decimal Qty, decimal? Cost, int Orders);

public sealed record PurchaseOrderImportResultDto(bool Committed, string Source, string TargetStatus, int TotalDocuments, int Ready, int Blocked,
    int Duplicates, int Skipped, int Created, int Failed, IReadOnlyList<PurchaseOrderImportDocDto> Documents,
    IReadOnlyList<PurchaseOrderImportMissingProductDto> MissingProducts, IReadOnlyList<string> MissingSuppliers);

/// <summary>
/// Lote 239 — importar órdenes de compra desde un archivo (origen genérico CSV/Excel o QuickBooks Desktop, 2026-11-12, pedido de Luis).
/// - Una orden de compra dice «se compró»: NO mueve inventario (lo hace el recibo). Se crea en DRAFT o, si se pide, SENT (completada);
///   con SENT queda lista para recibir contra ella.
/// - Los productos se buscan por SKU con una clave sin guiones, espacios ni signos (NECH 1001 = NECH-1001). Producto o proveedor que no
///   existe → la orden se BLOQUEA (no se crea), con el motivo y la lista de lo que falta; nunca se da de alta un producto a medias.
/// - No duplica: una orden ya importada (proveedor + número en sus notas) se informa y se salta.
/// - Sin confirmar (<c>commit=false</c>) es solo vista previa: no guarda nada.
/// Las reglas puras viven en <see cref="PurchaseOrderImportRules"/>; la orden se crea con <see cref="PurchaseOrderService"/> (mismas reglas del alta manual).
/// </summary>
public sealed class PurchaseOrderImportService(TeikemDbContext db, PurchaseOrderService purchaseOrders, ILookupCache lookups)
{
    private sealed class Doc
    {
        public string Key = "";
        public string Vendor = "";
        public string? Number;
        public string? Type;
        public DateOnly? Date;
        public DateOnly? Expected;
        public List<PoImportLine> Lines = new();
    }

    private sealed record ProductInfo(int ProductId, Guid PublicId, string Sku, bool IsOwn, bool IsActive, bool IsSerial, decimal? PurchaseCost);

    public async Task<PurchaseOrderImportResultDto> ImportAsync(string? source, byte[] bytes, string? fileName, bool commit, string? targetStatus,
        Guid? warehousePublicId, CancellationToken ct)
    {
        var src = PurchaseOrderImportRules.NormalizeSource(source);
        if (src.Length == 0) throw new ValidationException("source", PurchaseOrderImportRules.SourceUnknown);
        var target = PurchaseOrderImportRules.NormalizeStatus(targetStatus);
        if (target.Length == 0) throw new ValidationException("status", PurchaseOrderImportRules.StatusUnknown);
        if (bytes is null || bytes.Length == 0) throw new ValidationException("file", PurchaseOrderImportRules.NoFile);

        var lines = src == PurchaseOrderImportRules.SourceQuickBooks ? ReadQuickBooks(bytes, fileName) : ReadGeneric(bytes, fileName);
        var skippedTypes = new List<PurchaseOrderImportDocDto>();
        var docs = GroupDocuments(lines, skippedTypes);
        if (docs.Count + skippedTypes.Count == 0) throw new ValidationException("file", PurchaseOrderImportRules.NoDocuments);
        if (docs.Count > PurchaseOrderImportRules.MaxDocuments) throw new ValidationException("file", PurchaseOrderImportRules.TooManyDocuments);

        // El almacén se resuelve una vez (con varios almacenes hay que indicarlo): falla antes de revisar nada.
        var warehouse = await db.ResolveWarehouseOrDefaultAsync(warehousePublicId, ct);

        var suppliers = await db.Set<Supplier>().AsNoTracking().ToListAsync(ct);
        var supplierByKey = suppliers.GroupBy(s => PurchaseOrderImportRules.NameKey(s.Name)).ToDictionary(g => g.Key, g => g.OrderByDescending(s => s.IsActive).First());
        var products = await LoadProductsAsync(ct);
        var existingMarkers = await ExistingMarkersAsync(ct);

        var result = new List<PurchaseOrderImportDocDto>(skippedTypes);
        var plans = new Dictionary<string, (Doc Doc, Supplier Supplier, List<PurchaseOrderLineRequest> Lines)>();
        var missing = new Dictionary<string, (string Description, decimal Qty, decimal? Cost, HashSet<string> Docs)>(StringComparer.OrdinalIgnoreCase);
        var missingSuppliers = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var doc in docs)
        {
            var issues = new List<PurchaseOrderImportIssueDto>();
            var marker = PurchaseOrderImportRules.Marker(src, doc.Vendor, doc.Number, doc.Date);

            if (existingMarkers.TryGetValue(marker, out var existingNumber))
            {
                issues.Add(new(0, null, PurchaseOrderImportRules.DocDuplicate, PurchaseOrderImportRules.Duplicate(existingNumber), false));
                result.Add(new(doc.Key, doc.Vendor, doc.Number, doc.Date, PurchaseOrderImportRules.DocDuplicate, doc.Lines.Count, 0m, existingNumber, null, issues));
                continue;
            }

            Supplier? supplier = null;
            if (string.IsNullOrWhiteSpace(doc.Vendor))
                issues.Add(new(0, null, PurchaseOrderImportRules.IssueMissingSupplier, PurchaseOrderImportRules.NoSupplierInFile, true));
            else if (!supplierByKey.TryGetValue(PurchaseOrderImportRules.NameKey(doc.Vendor), out supplier))
            {
                issues.Add(new(0, null, PurchaseOrderImportRules.IssueMissingSupplier, PurchaseOrderImportRules.MissingSupplier(doc.Vendor), true));
                missingSuppliers.Add(doc.Vendor);
            }
            else if (!supplier.IsActive)
                issues.Add(new(0, null, PurchaseOrderImportRules.IssueInactiveSupplier, PurchaseOrderImportRules.InactiveSupplier(doc.Vendor), true));

            // Líneas: resolver el producto de cada una, sumar las que repiten producto y omitir las de cantidad 0.
            var merged = new Dictionary<int, (ProductInfo Product, decimal Qty, decimal? Cost, int Count, int FirstLine)>();
            foreach (var l in doc.Lines)
            {
                if (string.IsNullOrWhiteSpace(l.Sku))
                {
                    issues.Add(new(l.Line, null, PurchaseOrderImportRules.IssueMissingProduct, PurchaseOrderImportRules.NoSkuInFile, true));
                    continue;
                }
                var sku = l.Sku.Trim();
                var key = PurchaseOrderImportRules.SkuKey(sku);
                if (!products.TryGetValue(key, out var candidates) || candidates.Count == 0)
                {
                    var similar = PurchaseOrderImportRules.SimilarSkus(sku, products.Values.SelectMany(v => v).Select(p => p.Sku));
                    issues.Add(new(l.Line, sku, PurchaseOrderImportRules.IssueMissingProduct, PurchaseOrderImportRules.MissingProduct(sku, similar), true));
                    var prev = missing.TryGetValue(sku, out var m) ? m : (l.Description ?? string.Empty, 0m, (decimal?)null, new HashSet<string>());
                    prev.Item4.Add(doc.Key);
                    missing[sku] = (string.IsNullOrEmpty(prev.Item1) ? l.Description ?? string.Empty : prev.Item1, prev.Item2 + (l.Qty ?? 0m), l.Cost ?? prev.Item3, prev.Item4);
                    continue;
                }
                if (candidates.Count > 1)
                {
                    issues.Add(new(l.Line, sku, PurchaseOrderImportRules.IssueAmbiguousProduct, PurchaseOrderImportRules.AmbiguousProduct(sku, candidates.Select(c => c.Sku).ToList()), true));
                    continue;
                }
                var prod = candidates[0];
                if (!string.Equals(prod.Sku, sku, StringComparison.OrdinalIgnoreCase))
                    issues.Add(new(l.Line, sku, PurchaseOrderImportRules.IssueAlias, PurchaseOrderImportRules.AliasUsed(sku, prod.Sku), false));
                if (l.Qty is null or <= 0m)
                {
                    issues.Add(new(l.Line, prod.Sku, PurchaseOrderImportRules.IssueZeroQty, PurchaseOrderImportRules.ZeroQty(prod.Sku), false));
                    continue;
                }
                // QuickBooks puede traer costos con más de 4 decimales (el costo de una línea admite 4): se redondea y se avisa, no se bloquea la orden.
                var cost = l.Cost is decimal c0 ? PurchaseOrderRules.Round4(c0) : (decimal?)null;
                if (l.Cost is decimal raw && cost is decimal rounded && rounded != raw)
                    issues.Add(new(l.Line, prod.Sku, PurchaseOrderImportRules.IssueCostRounded, PurchaseOrderImportRules.CostRounded(prod.Sku, raw, rounded), false));
                if (merged.TryGetValue(prod.ProductId, out var cur))
                    merged[prod.ProductId] = (prod, cur.Qty + l.Qty.Value, cur.Cost ?? cost, cur.Count + 1, cur.FirstLine);
                else merged[prod.ProductId] = (prod, l.Qty.Value, cost, 1, l.Line);
            }
            foreach (var m in merged.Values.Where(m => m.Count > 1))
                issues.Add(new(m.FirstLine, m.Product.Sku, PurchaseOrderImportRules.IssueMergedLines, PurchaseOrderImportRules.MergedLines(m.Product.Sku, m.Count), false));

            var requests = merged.Values.OrderBy(m => m.FirstLine).Select(m => new PurchaseOrderLineRequest(m.Product.PublicId, m.Qty, m.Cost)).ToList();
            if (merged.Count == 0 && !issues.Any(i => i.Blocking))
                issues.Add(new(0, null, PurchaseOrderImportRules.IssueNoLines, PurchaseOrderImportRules.NoLinesWithQty, true));

            // Mismas reglas de línea del alta manual (producto propio y activo, cantidad, costo, serie).
            var inputs = merged.Values.OrderBy(m => m.FirstLine)
                .Select(m => new PurchaseOrderLineInput(m.Product.ProductId, m.Product.Sku, m.Product.IsOwn, m.Product.IsSerial, m.Qty, m.Cost, m.Product.PurchaseCost)).ToList();
            if (inputs.Count > 0)
            {
                var (_, ruleErrors) = PurchaseOrderRules.ValidateLines(inputs);
                foreach (var (_, messages) in ruleErrors)
                    foreach (var msg in messages) issues.Add(new(0, null, PurchaseOrderImportRules.IssueInvalid, msg, true));
                foreach (var m in merged.Values.Where(m => !m.Product.IsActive))
                    issues.Add(new(m.FirstLine, m.Product.Sku, PurchaseOrderImportRules.IssueInvalid, PurchaseOrderRules.ProductInactive(m.Product.Sku), true));
            }

            // Fecha esperada anterior a la de la orden: se ignora (no bloquea).
            var orderDate = doc.Date;
            var expected = doc.Expected;
            if (orderDate is DateOnly od && expected is DateOnly ex && ex < od)
            {
                issues.Add(new(0, null, PurchaseOrderImportRules.IssueExpectedIgnored, PurchaseOrderImportRules.ExpectedIgnored(ex, od), false));
                expected = null;
            }
            doc.Expected = expected;

            var total = PurchaseOrderRules.Total(merged.Values.Select(m => (m.Qty, m.Cost ?? m.Product.PurchaseCost ?? 0m)));
            var blocked = issues.Any(i => i.Blocking);
            result.Add(new(doc.Key, doc.Vendor, doc.Number, doc.Date, blocked ? PurchaseOrderImportRules.DocBlocked : PurchaseOrderImportRules.DocReady,
                requests.Count, total, null, null, issues));
            if (!blocked) plans[doc.Key] = (doc, supplier!, requests);
        }

        var created = 0;
        var failed = 0;
        if (commit)
        {
            for (var i = 0; i < result.Count; i++)
            {
                var d = result[i];
                if (d.Status != PurchaseOrderImportRules.DocReady || !plans.TryGetValue(d.Key, out var plan)) continue;
                try
                {
                    var req = new PurchaseOrderCreateRequest(plan.Supplier.SupplierId, warehouse.PublicId, plan.Doc.Date, plan.Doc.Expected, null,
                        PurchaseOrderImportRules.BuildNotes(src, plan.Doc.Vendor, plan.Doc.Number, plan.Doc.Date), plan.Lines);
                    var po = await purchaseOrders.CreateAsync(req, ct);
                    if (target == PurchaseOrderImportRules.StatusSent)
                        po = await purchaseOrders.SendAsync(po.PublicId, new PurchaseOrderStatusRequest("Importada: se marca como enviada al importar."), ct);
                    result[i] = d with { Status = PurchaseOrderImportRules.DocCreated, PurchaseOrderNumber = po.Number, PurchaseOrderPublicId = po.PublicId };
                    created++;
                }
                catch (TeikemException ex)
                {
                    var msgs = ex.Errors is { Count: > 0 } ? ex.Errors.SelectMany(e => e.Value).ToList() : new List<string> { ex.Message };
                    var issues = d.Issues.Concat(msgs.Select(m => new PurchaseOrderImportIssueDto(0, null, PurchaseOrderImportRules.IssueInvalid, m, true))).ToList();
                    result[i] = d with { Status = PurchaseOrderImportRules.DocError, Issues = issues };
                    failed++;
                }
            }
        }

        var ready = result.Count(d => d.Status is PurchaseOrderImportRules.DocReady);
        var missingList = missing.Select(kv => new PurchaseOrderImportMissingProductDto(kv.Key, kv.Value.Description, kv.Value.Qty, kv.Value.Cost, kv.Value.Docs.Count))
            .OrderBy(m => m.Sku, StringComparer.OrdinalIgnoreCase).ToList();
        return new PurchaseOrderImportResultDto(commit, src, target, result.Count,
            ready + created, result.Count(d => d.Status == PurchaseOrderImportRules.DocBlocked || d.Status == PurchaseOrderImportRules.DocError),
            result.Count(d => d.Status == PurchaseOrderImportRules.DocDuplicate), result.Count(d => d.Status == PurchaseOrderImportRules.DocSkipped),
            created, failed, result, missingList, missingSuppliers.ToList());
    }

    // ---------------------------------------------------------------- lectura de los orígenes

    private static List<PoImportLine> ReadQuickBooks(byte[] bytes, string? fileName)
    {
        QbTable table;
        try { table = QuickBooksCsvReader.ParseTable(QuickBooksCsvReader.Decode(bytes), fileName ?? "archivo.csv"); }
        catch (Exception ex) when (ex is not TeikemException) { throw new ValidationException("file", ImportFileReader.UnsupportedFormatMessage); }
        foreach (var required in new[] { "Type", "Date", "Num", "Item", "Qty" })
            if (!table.Headers.Contains(required, StringComparer.OrdinalIgnoreCase))
                throw new ValidationException("file", PurchaseOrderImportRules.MissingColumn(required));

        var list = new List<PoImportLine>();
        foreach (var r in table.Rows)
        {
            var type = r.GetOrNull("Type");
            if (type is null) continue; // encabezado de proveedor o fila «Total …»
            var (sku, desc) = PurchaseOrderImportRules.SplitQuickBooksItem(r.GetOrNull("Item"));
            var description = string.IsNullOrEmpty(desc) ? r.GetOrNull("Item Description") ?? string.Empty : desc;
            list.Add(new PoImportLine(r.Line, type, r.GetOrNull("Name") ?? r.GetOrNull("Source Name"), r.GetOrNull("Num"),
                PurchaseOrderImportRules.ParseDate(r.GetOrNull("Date")), PurchaseOrderImportRules.ParseDate(r.GetOrNull("Deliv Date")),
                sku, description, PurchaseOrderImportRules.ParseNumber(r.GetOrNull("Qty")), PurchaseOrderImportRules.ParseNumber(r.GetOrNull("Cost Price") ?? r.GetOrNull("Cost"))));
        }
        return list;
    }

    private static readonly Dictionary<string, string[]> GenericColumns = new()
    {
        ["vendor"] = new[] { "supplier", "vendor", "proveedor" },
        ["number"] = new[] { "number", "document", "po", "po number", "numero", "número", "documento", "orden" },
        ["date"] = new[] { "date", "order date", "fecha", "fecha de la orden" },
        ["expected"] = new[] { "expected date", "expected", "fecha esperada", "fecha de entrega" },
        ["sku"] = new[] { "sku", "item", "producto", "codigo", "código" },
        ["desc"] = new[] { "description", "descripcion", "descripción", "product name", "nombre" },
        ["qty"] = new[] { "qty", "quantity", "cantidad" },
        ["cost"] = new[] { "cost", "unit cost", "cost price", "costo", "costo unitario", "precio" },
    };

    private static List<PoImportLine> ReadGeneric(byte[] bytes, string? fileName)
    {
        ImportFileTable table;
        try { table = ImportFileReader.Read(bytes, fileName, ',', true, null); }
        catch (CsvLimitException) { throw new ValidationException("file", Teikem.Domain.Orders.CsvParser.LimitMessage); }
        var header = table.Header ?? throw new ValidationException("file", PurchaseOrderImportRules.NoDocuments);
        int Find(string field)
        {
            for (var i = 0; i < header.Count; i++)
                if (GenericColumns[field].Contains(header[i].Trim().ToLowerInvariant())) return i;
            return -1;
        }
        int iVendor = Find("vendor"), iNumber = Find("number"), iDate = Find("date"), iExpected = Find("expected"), iSku = Find("sku"), iDesc = Find("desc"), iQty = Find("qty"), iCost = Find("cost");
        if (iVendor < 0) throw new ValidationException("file", PurchaseOrderImportRules.MissingColumn("Supplier"));
        if (iSku < 0) throw new ValidationException("file", PurchaseOrderImportRules.MissingColumn("SKU"));
        if (iQty < 0) throw new ValidationException("file", PurchaseOrderImportRules.MissingColumn("Qty"));
        string? Cell(IReadOnlyList<string> cells, int i) => i >= 0 && i < cells.Count && !string.IsNullOrWhiteSpace(cells[i]) ? cells[i].Trim() : null;

        var list = new List<PoImportLine>();
        foreach (var r in table.Rows)
        {
            if (r.Cells.All(string.IsNullOrWhiteSpace)) continue;
            list.Add(new PoImportLine(r.Line, "Purchase Order", Cell(r.Cells, iVendor), Cell(r.Cells, iNumber), PurchaseOrderImportRules.ParseDate(Cell(r.Cells, iDate)),
                PurchaseOrderImportRules.ParseDate(Cell(r.Cells, iExpected)), Cell(r.Cells, iSku), Cell(r.Cells, iDesc),
                PurchaseOrderImportRules.ParseNumber(Cell(r.Cells, iQty)), PurchaseOrderImportRules.ParseNumber(Cell(r.Cells, iCost))));
        }
        return list;
    }

    private static List<Doc> GroupDocuments(List<PoImportLine> lines, List<PurchaseOrderImportDocDto> skipped)
    {
        var docs = new Dictionary<string, Doc>();
        var order = new List<string>();
        var skippedKeys = new HashSet<string>();
        foreach (var l in lines)
        {
            if (!PurchaseOrderImportRules.IsPurchaseType(l.Type))
            {
                var skey = $"{PurchaseOrderImportRules.DocKey(l.Vendor, l.DocNumber, l.Date)}#{l.Type}";
                if (skippedKeys.Add(skey))
                    skipped.Add(new(skey, l.Vendor ?? string.Empty, l.DocNumber, l.Date, PurchaseOrderImportRules.DocSkipped, 0, 0m, null, null,
                        new[] { new PurchaseOrderImportIssueDto(l.Line, null, PurchaseOrderImportRules.DocSkipped, PurchaseOrderImportRules.NotAPurchase(l.Type ?? string.Empty), false) }));
                continue;
            }
            var key = PurchaseOrderImportRules.DocKey(l.Vendor, l.DocNumber, l.Date);
            if (!docs.TryGetValue(key, out var d))
            {
                d = new Doc { Key = key, Vendor = l.Vendor ?? string.Empty, Number = l.DocNumber, Type = l.Type, Date = l.Date, Expected = l.ExpectedDate };
                docs[key] = d;
                order.Add(key);
            }
            d.Lines.Add(l);
        }
        return order.Select(k => docs[k]).ToList();
    }

    // ---------------------------------------------------------------- datos de la base

    private async Task<Dictionary<string, List<ProductInfo>>> LoadProductsAsync(CancellationToken ct)
    {
        var rows = await db.Set<Product>().AsNoTracking()
            .Select(p => new { p.ProductId, p.PublicId, p.Sku, p.ClientId, p.IsActive, p.PurchaseCost, p.TrackingTypeLookupId }).ToListAsync(ct);
        var serial = new Dictionary<int, bool>();
        foreach (var id in rows.Select(r => r.TrackingTypeLookupId).Distinct())
            serial[id] = (await lookups.GetAsync(id, ct))?.InternalCode == TrackingTypes.Serial;
        var map = new Dictionary<string, List<ProductInfo>>();
        foreach (var r in rows.Where(r => r.ClientId is null))
        {
            var key = PurchaseOrderImportRules.SkuKey(r.Sku);
            if (key.Length == 0) continue;
            if (!map.TryGetValue(key, out var list)) map[key] = list = new List<ProductInfo>();
            list.Add(new ProductInfo(r.ProductId, r.PublicId, r.Sku, true, r.IsActive, serial[r.TrackingTypeLookupId], r.PurchaseCost));
        }
        // Una clave con un activo y otros dados de baja se resuelve al activo.
        foreach (var (key, list) in map.ToList())
            if (list.Count > 1 && list.Count(p => p.IsActive) == 1) map[key] = list.Where(p => p.IsActive).ToList();
        return map;
    }

    /// <summary>Órdenes activas ya importadas: marca «[QB:…]»/«[IMP:…]» de sus notas → número de la orden.</summary>
    private async Task<Dictionary<string, string>> ExistingMarkersAsync(CancellationToken ct)
    {
        var rows = await db.Set<PurchaseOrder>().AsNoTracking()
            .Where(p => p.IsActive && p.Notes != null && (p.Notes.Contains("[QB:") || p.Notes.Contains("[IMP:")))
            .Select(p => new { p.Number, p.Notes }).ToListAsync(ct);
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var r in rows)
        {
            var notes = r.Notes!;
            foreach (var prefix in new[] { "[QB:", "[IMP:" })
            {
                var start = notes.IndexOf(prefix, StringComparison.Ordinal);
                if (start < 0) continue;
                var end = notes.IndexOf(']', start);
                if (end > start) map.TryAdd(notes[start..(end + 1)], r.Number);
            }
        }
        return map;
    }
}
