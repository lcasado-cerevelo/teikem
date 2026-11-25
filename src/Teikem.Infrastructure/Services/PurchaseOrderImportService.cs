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

/// <summary>Resultado de la importación. Lote 246: con el origen TEMPLATE, la plantilla usada (TemplatePublicId y TemplateName; null en los otros orígenes).</summary>
public sealed record PurchaseOrderImportResultDto(bool Committed, string Source, string TargetStatus, int TotalDocuments, int Ready, int Blocked,
    int Duplicates, int Skipped, int Created, int Failed, IReadOnlyList<PurchaseOrderImportDocDto> Documents,
    IReadOnlyList<PurchaseOrderImportMissingProductDto> MissingProducts, IReadOnlyList<string> MissingSuppliers,
    Guid? TemplatePublicId = null, string? TemplateName = null);

/// <summary>
/// Lote 239 — importar órdenes de compra desde un archivo (origen genérico CSV/Excel o QuickBooks Desktop, 2026-11-12, pedido de Luis).
/// - Una orden de compra dice «se compró»: NO mueve inventario (lo hace el recibo). Se crea en DRAFT o, si se pide, SENT (completada);
///   con SENT queda lista para recibir contra ella.
/// - Los productos se buscan por SKU con una clave sin guiones, espacios ni signos (NECH 1001 = NECH-1001). Producto o proveedor que no
///   existe → la orden se BLOQUEA (no se crea), con el motivo y la lista de lo que falta; nunca se da de alta un producto a medias.
/// - No duplica: una orden ya importada (proveedor + número en sus notas) se informa y se salta.
/// - Sin confirmar (<c>commit=false</c>) es solo vista previa: no guarda nada.
/// Las reglas puras viven en <see cref="PurchaseOrderImportRules"/>; la orden se crea con <see cref="PurchaseOrderService"/> (mismas reglas del alta manual).
/// Lote 246 (Lote F):
/// - Tercer origen TEMPLATE: plantilla del usuario (ImportTemplate Kind PURCHASE_ORDER, <see cref="ImportTemplateReader"/>), filas agrupadas por número de documento.
///   Con la plantilla, una fecha o un número ilegible, o una fila sin número de documento, bloquea la orden (BAD_VALUE).
/// - El mismo producto en varias líneas con costos distintos bloquea la orden en todos los orígenes (COST_CONFLICT), en vez de tomar el primero en silencio.
/// - No duplica por la identidad de importación en columna (PurchaseOrder.ImportKey, índice único entre las activas); dos importaciones simultáneas del mismo
///   documento: una crea y la otra sale DUPLICATE. Con origen QUICKBOOKS, lo que nació en Teikem y ya se pasó a QuickBooks tampoco se vuelve a crear.
/// - Cada orden guarda su origen (QUICKBOOKS o EXCEL) y nace SENT, si se pide, en la misma transacción que el alta (CreateImportedAsync).
/// - Con origen QUICKBOOKS, el proveedor cuyo nombre es exactamente el del archivo queda como ya mandado a QuickBooks (Supplier.QuickBooksSentAt).
/// </summary>
public sealed class PurchaseOrderImportService(TeikemDbContext db, PurchaseOrderService purchaseOrders, ILookupCache lookups, ImportTemplateService templates)
{
    /// <summary>Comentario del historial DRAFT → SENT de las órdenes importadas como enviadas.</summary>
    public const string SentComment = "Importada: se marca como enviada al importar.";

    private sealed class Doc
    {
        public string Key = "";
        public string Vendor = "";
        public string? Number;
        public string? Type;
        public DateOnly? Date;
        public DateOnly? Expected;
        public string? PoRef;
        public List<PoImportLine> Lines = new();
    }

    private sealed record ProductInfo(int ProductId, Guid PublicId, string Sku, bool IsOwn, bool IsActive, bool IsSerial, decimal? PurchaseCost);

    /// <summary>Lote 239 (sin plantilla): GENERIC o QUICKBOOKS.</summary>
    public Task<PurchaseOrderImportResultDto> ImportAsync(string? source, byte[] bytes, string? fileName, bool commit, string? targetStatus,
        Guid? warehousePublicId, CancellationToken ct)
        => ImportAsync(source, bytes, fileName, commit, targetStatus, warehousePublicId, null, ct);

    /// <summary>
    /// Lote 246: con <paramref name="templatePublicId"/> y sin <paramref name="source"/> el origen es TEMPLATE. TEMPLATE sin plantilla → 400
    /// <see cref="PurchaseOrderImportRules.TemplateRequired"/>; plantilla con GENERIC o QUICKBOOKS → 400 <see cref="PurchaseOrderImportRules.TemplateOnlyForTemplate"/>.
    /// La plantilla es del tipo PURCHASE_ORDER y de la compañía (otra → 404 sin oráculo; inactiva → 400).
    /// </summary>
    public async Task<PurchaseOrderImportResultDto> ImportAsync(string? source, byte[] bytes, string? fileName, bool commit, string? targetStatus,
        Guid? warehousePublicId, Guid? templatePublicId, CancellationToken ct)
    {
        var src = string.IsNullOrWhiteSpace(source) && templatePublicId is not null
            ? PurchaseOrderImportRules.SourceTemplate
            : PurchaseOrderImportRules.NormalizeSource(source);
        if (src.Length == 0) throw new ValidationException("source", PurchaseOrderImportRules.SourceUnknown);
        if (src == PurchaseOrderImportRules.SourceTemplate && templatePublicId is null)
            throw new ValidationException("templatePublicId", PurchaseOrderImportRules.TemplateRequired);
        if (src != PurchaseOrderImportRules.SourceTemplate && templatePublicId is not null)
            throw new ValidationException("templatePublicId", PurchaseOrderImportRules.TemplateOnlyForTemplate);
        var target = PurchaseOrderImportRules.NormalizeStatus(targetStatus);
        if (target.Length == 0) throw new ValidationException("status", PurchaseOrderImportRules.StatusUnknown);
        if (bytes is null || bytes.Length == 0) throw new ValidationException("file", PurchaseOrderImportRules.NoFile);

        ImportTemplate? template = null;
        if (src == PurchaseOrderImportRules.SourceTemplate)
            template = await templates.ResolveForImportAsync(templatePublicId!.Value, null, ImportKinds.PurchaseOrder, ct);

        var lines = src switch
        {
            PurchaseOrderImportRules.SourceQuickBooks => ReadQuickBooks(bytes, fileName),
            PurchaseOrderImportRules.SourceTemplate => ReadTemplate(template!, bytes, fileName),
            _ => ReadGeneric(bytes, fileName),
        };
        var skippedTypes = new List<PurchaseOrderImportDocDto>();
        var docs = GroupDocuments(lines, skippedTypes, separateUnnumbered: src == PurchaseOrderImportRules.SourceTemplate);
        if (docs.Count + skippedTypes.Count == 0) throw new ValidationException("file", PurchaseOrderImportRules.NoDocuments);
        if (docs.Count > PurchaseOrderImportRules.MaxDocuments) throw new ValidationException("file", PurchaseOrderImportRules.TooManyDocuments);

        // El almacén se resuelve una vez (con varios almacenes hay que indicarlo): falla antes de revisar nada.
        var warehouse = await db.ResolveWarehouseOrDefaultAsync(warehousePublicId, ct);

        var suppliers = await db.Set<Supplier>().AsNoTracking().ToListAsync(ct);
        var supplierByKey = suppliers.GroupBy(s => PurchaseOrderImportRules.NameKey(s.Name)).ToDictionary(g => g.Key, g => g.OrderByDescending(s => s.IsActive).First());
        var products = await LoadProductsAsync(ct);
        var existingKeys = await ExistingImportKeysAsync(ct);
        var cameFromTeikem = src == PurchaseOrderImportRules.SourceQuickBooks ? await ExportedTeikemOrdersAsync(ct) : null;

        var result = new List<PurchaseOrderImportDocDto>(skippedTypes);
        var plans = new Dictionary<string, (Doc Doc, Supplier Supplier, List<PurchaseOrderLineRequest> Lines)>();
        var missing = new Dictionary<string, (string Description, decimal Qty, decimal? Cost, HashSet<string> Docs)>(StringComparer.OrdinalIgnoreCase);
        var missingSuppliers = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var doc in docs)
        {
            var issues = new List<PurchaseOrderImportIssueDto>();
            var importKey = PurchaseOrderImportRules.ImportKey(src, doc.Vendor, doc.Number, doc.Date);

            if (existingKeys.TryGetValue(importKey, out var existingNumber))
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

            // Ir y venir (Lote 246): un documento de QuickBooks cuyo número o «P. O. #» es el de una orden de Teikem del mismo proveedor que ya se pasó a QuickBooks
            // (exportada o marcada) es esa misma orden de vuelta: no se crea otra.
            if (supplier is not null && cameFromTeikem is not null && CameFromTeikem(cameFromTeikem, supplier.SupplierId, doc) is string teikemNumber)
            {
                var dupIssues = new List<PurchaseOrderImportIssueDto>
                    { new(0, null, PurchaseOrderImportRules.DocDuplicate, PurchaseOrderImportRules.CameFromTeikem(teikemNumber), false) };
                result.Add(new(doc.Key, doc.Vendor, doc.Number, doc.Date, PurchaseOrderImportRules.DocDuplicate, doc.Lines.Count, 0m, teikemNumber, null, dupIssues));
                continue;
            }

            // Líneas: resolver el producto de cada una, sumar las que repiten producto y omitir las de cantidad 0.
            var merged = new Dictionary<int, (ProductInfo Product, decimal Qty, decimal? Cost, int Count, int FirstLine)>();
            // Costos (redondeados) que trae el archivo por producto: si una orden trae el mismo producto a costos distintos se bloquea (COST_CONFLICT).
            var costsByProduct = new Dictionary<int, List<decimal>>();
            foreach (var l in doc.Lines)
            {
                // Plantilla (Lote 246): valores ilegibles o fila sin número de documento → BAD_VALUE bloqueante.
                var hasProblems = l.Problems is { Count: > 0 };
                foreach (var problem in l.Problems ?? Array.Empty<string>())
                    issues.Add(new(l.Line, string.IsNullOrWhiteSpace(l.Sku) ? null : l.Sku.Trim(), PurchaseOrderImportRules.IssueBadValue, problem, true));
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
                    // Una cantidad ilegible ya se informó como BAD_VALUE: no se repite como «cantidad 0».
                    if (!(hasProblems && l.Qty is null))
                        issues.Add(new(l.Line, prod.Sku, PurchaseOrderImportRules.IssueZeroQty, PurchaseOrderImportRules.ZeroQty(prod.Sku), false));
                    continue;
                }
                // QuickBooks puede traer costos con más de 4 decimales (el costo de una línea admite 4): se redondea y se avisa, no se bloquea la orden.
                var cost = l.Cost is decimal c0 ? PurchaseOrderRules.Round4(c0) : (decimal?)null;
                if (l.Cost is decimal raw && cost is decimal rounded && rounded != raw)
                    issues.Add(new(l.Line, prod.Sku, PurchaseOrderImportRules.IssueCostRounded, PurchaseOrderImportRules.CostRounded(prod.Sku, raw, rounded), false));
                if (cost is decimal fileCost)
                {
                    if (!costsByProduct.TryGetValue(prod.ProductId, out var costs)) costsByProduct[prod.ProductId] = costs = new List<decimal>();
                    if (!costs.Contains(fileCost)) costs.Add(fileCost);
                }
                if (merged.TryGetValue(prod.ProductId, out var cur))
                    merged[prod.ProductId] = (prod, cur.Qty + l.Qty.Value, cur.Cost ?? cost, cur.Count + 1, cur.FirstLine);
                else merged[prod.ProductId] = (prod, l.Qty.Value, cost, 1, l.Line);
            }
            foreach (var m in merged.Values.Where(m => m.Count > 1))
            {
                // Una línea sin costo no choca (toma el del producto o el de la otra línea); dos costos escritos distintos sí.
                if (costsByProduct.TryGetValue(m.Product.ProductId, out var costs) && costs.Count > 1)
                    issues.Add(new(m.FirstLine, m.Product.Sku, PurchaseOrderImportRules.IssueCostConflict, PurchaseOrderImportRules.CostConflict(m.Product.Sku, costs), true));
                else
                    issues.Add(new(m.FirstLine, m.Product.Sku, PurchaseOrderImportRules.IssueMergedLines, PurchaseOrderImportRules.MergedLines(m.Product.Sku, m.Count), false));
            }

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
                        PurchaseOrderImportRules.BuildNotes(src, plan.Doc.Vendor, plan.Doc.Number, plan.Doc.Date, template?.Name), plan.Lines);
                    // Alta y envío en UNA transacción: si DRAFT → SENT falla no queda un borrador huérfano.
                    var po = await purchaseOrders.CreateImportedAsync(req, PurchaseOrderImportRules.OriginOf(src),
                        PurchaseOrderImportRules.ImportKey(src, plan.Doc.Vendor, plan.Doc.Number, plan.Doc.Date),
                        send: target == PurchaseOrderImportRules.StatusSent, SentComment, ct);
                    result[i] = d with { Status = PurchaseOrderImportRules.DocCreated, PurchaseOrderNumber = po.Number, PurchaseOrderPublicId = po.PublicId };
                    created++;
                    if (src == PurchaseOrderImportRules.SourceQuickBooks) await MarkSupplierSentAsync(plan.Supplier.SupplierId, plan.Doc.Vendor, ct);
                }
                catch (ConflictException ex) when (ex.Message == PurchaseOrderService.AlreadyImportedMessage)
                {
                    // Otra importación creó la misma orden al mismo tiempo (UX_PurchaseOrder_ImportKey): es un duplicado, no un error.
                    db.ChangeTracker.Clear();
                    var issues = d.Issues.Append(new PurchaseOrderImportIssueDto(0, null, PurchaseOrderImportRules.DocDuplicate, ex.Message, false)).ToList();
                    result[i] = d with { Status = PurchaseOrderImportRules.DocDuplicate, Issues = issues };
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
            created, failed, result, missingList, missingSuppliers.ToList(), template?.PublicId, template?.Name);
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
                sku, description, PurchaseOrderImportRules.ParseNumber(r.GetOrNull("Qty")), PurchaseOrderImportRules.ParseNumber(r.GetOrNull("Cost Price") ?? r.GetOrNull("Cost")),
                PoRef: r.GetOrNull("P. O. #")));
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

    /// <summary>
    /// Lote 246 — filas de una plantilla PURCHASE_ORDER → líneas «Purchase Order». Fechas con ParseDate y números con ParseNumber (regla del Lote 239: coma = miles);
    /// un texto ilegible o la falta de documentNumber van a Problems (BAD_VALUE bloqueante). El SKU no se separa «SKU (Desc)».
    /// </summary>
    private static List<PoImportLine> ReadTemplate(ImportTemplate template, byte[] bytes, string? fileName)
    {
        var rows = ImportTemplateReader.Read(template, PurchaseOrderImportFields.Meta, bytes, fileName);
        var list = new List<PoImportLine>(rows.Count);
        foreach (var r in rows)
        {
            var problems = new List<string>();
            string? Value(string field) => r.Values.TryGetValue(field, out var v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : null;
            DateOnly? Date(string field)
            {
                var text = Value(field);
                if (text is null) return null;
                var d = PurchaseOrderImportRules.ParseDate(text);
                if (d is null) problems.Add(PurchaseOrderImportRules.BadDate(field, text));
                return d;
            }
            decimal? Number(string field)
            {
                var text = Value(field);
                if (text is null) return null;
                var n = PurchaseOrderImportRules.ParseNumber(text);
                if (n is null) problems.Add(PurchaseOrderImportRules.BadNumber(field, text));
                return n;
            }
            var number = Value(PurchaseOrderImportFields.DocumentNumber);
            if (number is null) problems.Add(PurchaseOrderImportRules.MissingDocumentNumber(r.Line));
            var orderDate = Date(PurchaseOrderImportFields.OrderDate);
            var expected = Date(PurchaseOrderImportFields.ExpectedDate);
            var qty = Number(PurchaseOrderImportFields.Quantity);
            var cost = Number(PurchaseOrderImportFields.UnitCost);
            list.Add(new PoImportLine(r.Line, "Purchase Order", Value(PurchaseOrderImportFields.Supplier), number, orderDate, expected,
                Value(PurchaseOrderImportFields.Sku), Value(PurchaseOrderImportFields.Description), qty, cost, Problems: problems.Count == 0 ? null : problems));
        }
        return list;
    }

    /// <summary>
    /// Agrupa las líneas en documentos por proveedor + número (sin número: proveedor + fecha, solo GENERIC y QUICKBOOKS). Con <paramref name="separateUnnumbered"/>
    /// (plantilla) una fila sin número es su propio documento (bloqueado por BAD_VALUE): no se funde con otras.
    /// </summary>
    private static List<Doc> GroupDocuments(List<PoImportLine> lines, List<PurchaseOrderImportDocDto> skipped, bool separateUnnumbered = false)
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
            if (separateUnnumbered && string.IsNullOrWhiteSpace(l.DocNumber)) key = $"{key}#L{l.Line}";
            if (!docs.TryGetValue(key, out var d))
            {
                d = new Doc { Key = key, Vendor = l.Vendor ?? string.Empty, Number = l.DocNumber, Type = l.Type, Date = l.Date, Expected = l.ExpectedDate };
                docs[key] = d;
                order.Add(key);
            }
            if (d.PoRef is null && !string.IsNullOrWhiteSpace(l.PoRef)) d.PoRef = l.PoRef.Trim();
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

    /// <summary>
    /// Lote 246 — órdenes activas ya importadas: identidad (PurchaseOrder.ImportKey, cargada desde la marca de las notas para las anteriores) → número de la orden.
    /// Ya no se busca la marca en las notas: borrarla de las notas no hace que la orden se duplique.
    /// </summary>
    private async Task<Dictionary<string, string>> ExistingImportKeysAsync(CancellationToken ct)
    {
        var rows = await db.Set<PurchaseOrder>().AsNoTracking()
            .Where(p => p.IsActive && p.ImportKey != null)
            .Select(p => new { p.ImportKey, p.Number }).ToListAsync(ct);
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var r in rows) map.TryAdd(r.ImportKey!, r.Number);
        return map;
    }

    /// <summary>
    /// Lote 246 — ir y venir: órdenes de Teikem activas, de origen distinto de QUICKBOOKS (NULL = TEIKEM) y ya pasadas a QuickBooks (exportadas o marcadas):
    /// proveedor → números de orden (sin distinguir mayúsculas).
    /// </summary>
    private async Task<Dictionary<int, HashSet<string>>> ExportedTeikemOrdersAsync(CancellationToken ct)
    {
        var qb = await lookups.TryGetIdAsync(PurchaseOrderOrigins.Domain, PurchaseOrderOrigins.QuickBooks, ct) ?? -1;
        var rows = await db.Set<PurchaseOrder>().AsNoTracking()
            .Where(p => p.IsActive && (p.OriginLookupId == null || p.OriginLookupId != qb) && (p.QuickBooksExportedAt != null || p.QuickBooksAt != null))
            .Select(p => new { p.SupplierId, p.Number }).ToListAsync(ct);
        var map = new Dictionary<int, HashSet<string>>();
        foreach (var r in rows)
        {
            if (!map.TryGetValue(r.SupplierId, out var set)) map[r.SupplierId] = set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            set.Add(r.Number.Trim());
        }
        return map;
    }

    /// <summary>Número de la orden de Teikem del mismo proveedor que el documento trae como «Num» o «P. O. #»; null si no es una que vuelve.</summary>
    private static string? CameFromTeikem(Dictionary<int, HashSet<string>> exported, int supplierId, Doc doc)
    {
        if (!exported.TryGetValue(supplierId, out var numbers)) return null;
        foreach (var candidate in new[] { doc.Number, doc.PoRef })
            if (!string.IsNullOrWhiteSpace(candidate) && numbers.TryGetValue(candidate.Trim(), out var number)) return number;
        return null;
    }

    /// <summary>
    /// Lote 246 — al crear una orden importada de QuickBooks, su proveedor ya existe en QuickBooks: queda como mandado (Supplier.QuickBooksSentAt) si su nombre es
    /// EXACTAMENTE el del archivo (recortado, sin distinguir mayúsculas; no basta la clave sin signos, porque QuickBooks crearía otro proveedor con el nombre de Teikem).
    /// </summary>
    private async Task MarkSupplierSentAsync(int supplierId, string? fileVendor, CancellationToken ct)
    {
        var supplier = await db.Set<Supplier>().FirstOrDefaultAsync(s => s.SupplierId == supplierId, ct);
        if (supplier is null || supplier.QuickBooksSentAt is not null) return;
        if (!string.Equals(supplier.Name.Trim(), fileVendor?.Trim(), StringComparison.OrdinalIgnoreCase)) return;
        supplier.QuickBooksSentAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }
}
