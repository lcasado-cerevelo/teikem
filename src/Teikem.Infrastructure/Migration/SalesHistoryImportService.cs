using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Constants;
using Teikem.Domain.CustomFields;
using Teikem.Domain.Migration;
using Teikem.Domain.Sales;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Services;
using R = Teikem.Domain.Sales.SalesAnalysisRules;
using C = Teikem.Domain.Sales.SalesAnalysisCodes;

namespace Teikem.Infrastructure.Migration;

/// <summary>Un archivo a importar: nombre, contenido y, si se sabe, de qué tipo se declaró (SALES / PURCHASES; null = el que diga el archivo).</summary>
public sealed record SalesHistoryFile(string FileName, byte[] Content, string? DeclaredKind = null);

/// <summary>
/// Lote 241 — importador del historial de QuickBooks («Sales by Item Detail» y «Purchases by Item Detail» en CSV) a la compañía de la sesión. Lo usan el verbo CLI
/// <c>import-sales-history</c> y el endpoint POST /sales-analysis/import (misma lógica, con simulación).
/// - Empareja el CLIENTE por el enlace a mano, por el campo personalizado «Código QuickBooks» (qb_code, el nombre de QuickBooks que dejó la migración), por el nombre
///   normalizado del cliente o, si no, por su razón social normalizada (un nombre que normaliza igual a dos clientes no empareja). El ÍTEM por el enlace a mano o por el
///   SKU con la MISMA normalización de la migración (sin espacios y en mayúsculas: «NECH 1003» = «NECH1003»).
/// - Ventas: guarda todas las transacciones con su clase (PRODUCT, DISCOUNT, SHIPPING, OTHER). Compras: omite las de cantidad 0 y el Bill que repite a su Item Receipt.
/// - Idempotente: reimportar el MISMO archivo (misma huella SHA-256) reemplaza su lote; un archivo distinto del mismo tipo reemplaza las líneas ya importadas que caen en
///   su rango de fechas [primera, última]. En simulación no escribe nada y reporta lo que haría.
/// </summary>
public sealed class SalesHistoryImportService(TeikemDbContext db, ITenantContext tenant, SalesAnalysisSetupService setup, ILookupCache lookups)
{
    public const string ActionNew = "NEW";
    public const string ActionReplaceSameFile = "REPLACE_SAME_FILE";
    public const string ActionReplaceRange = "REPLACE_RANGE";

    public static string KindMismatch(string file, string actual, string declared) =>
        $"El archivo {file} es un reporte de {(actual == C.KindSales ? "ventas" : "compras")} pero se indicó como de {(declared == C.KindSales ? "ventas" : "compras")}.";

    private sealed class Matchers
    {
        public Dictionary<string, int> QbCode { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, int?> Names { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, int?> LegalNames { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, int> ClientAlias { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, int?> Skus { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, int> ProductAlias { get; } = new(StringComparer.Ordinal);

        public int? Client(string raw)
        {
            var key = R.NameKey(raw);
            if (ClientAlias.TryGetValue(key, out var a)) return a;
            if (QbCode.TryGetValue(raw.Trim(), out var q)) return q;
            if (Names.TryGetValue(key, out var n)) return n;
            return LegalNames.TryGetValue(key, out var l) ? l : null;
        }

        public int? Product(string sku)
        {
            var key = LegacyImportRules.SkuKey(sku);
            if (ProductAlias.TryGetValue(key, out var a)) return a;
            return Skus.TryGetValue(key, out var p) ? p : null;
        }
    }

    private static void AddUnique(Dictionary<string, int?> map, string key, int id)
    {
        if (key.Length == 0) return;
        if (map.TryGetValue(key, out var existing)) { if (existing != id) map[key] = null; }   // dos clientes con el mismo nombre normalizado: no empareja
        else map[key] = id;
    }

    private async Task<Matchers> MatchersAsync(CancellationToken ct)
    {
        var m = new Matchers();
        foreach (var c in await db.Clients.AsNoTracking().Select(c => new { c.ClientId, c.Name, c.LegalName, c.IsActive }).OrderByDescending(c => c.IsActive).ToListAsync(ct))
        {
            AddUnique(m.Names, R.NameKey(c.Name), c.ClientId);
            if (!string.IsNullOrWhiteSpace(c.LegalName)) AddUnique(m.LegalNames, R.NameKey(c.LegalName), c.ClientId);
        }
        var clientEntity = await lookups.TryGetIdAsync(LookupDomains.EntityType, EntityTypes.Client, ct);
        if (clientEntity is int ce)
        {
            var defIds = await db.Set<CustomFieldDefinition>().AsNoTracking().Where(d => d.EntityTypeLookupId == ce && d.FieldKey == LegacyImportPlanner.QbCodeFieldKey)
                .Select(d => d.CustomFieldDefinitionId).ToListAsync(ct);
            if (defIds.Count > 0)
                foreach (var v in await db.Set<CustomFieldValue>().AsNoTracking().Where(v => defIds.Contains(v.CustomFieldDefinitionId) && v.ValueText != null)
                             .Select(v => new { v.EntityId, v.ValueText }).ToListAsync(ct))
                    m.QbCode.TryAdd(v.ValueText!.Trim(), v.EntityId);
        }
        // productos propios: el activo gana si dos SKU normalizan igual
        foreach (var p in await db.Products.AsNoTracking().Where(p => p.ClientId == null).Select(p => new { p.ProductId, p.Sku, p.IsActive }).OrderByDescending(p => p.IsActive)
                     .ThenBy(p => p.ProductId).ToListAsync(ct))
            m.Skus.TryAdd(LegacyImportRules.SkuKey(p.Sku), p.ProductId);
        foreach (var a in await db.Set<SalesHistoryAlias>().AsNoTracking().ToListAsync(ct))
        {
            if (a.Kind == C.AliasClient && a.ClientId is int cid) m.ClientAlias[a.RawKey] = cid;
            if (a.Kind == C.AliasProduct && a.ProductId is int pid) m.ProductAlias[a.RawKey] = pid;
        }
        return m;
    }

    private static string Cut(string? s, int max) => s is null ? string.Empty : s.Length <= max ? s : s[..max];
    private static string? CutOrNull(string? s, int max) => string.IsNullOrEmpty(s) ? null : s.Length <= max ? s : s[..max];

    /// <summary>Importa (o simula) los archivos. company = nombre para el reporte; cutoff = fecha de corte a guardar (null = no se cambia).</summary>
    public async Task<SalesHistoryImportReportDto> ImportAsync(string? company, IReadOnlyList<SalesHistoryFile> files, bool dryRun, DateOnly? cutoff, bool repsFromCustomField,
        string source, CancellationToken ct)
    {
        if (files.Count == 0) throw new ValidationException("files", R.FileRequired);
        company ??= await db.Tenants.AsNoTracking().Where(t => t.TenantId == TenantId).Select(t => t.Name).FirstOrDefaultAsync(ct) ?? string.Empty;
        var matchers = await MatchersAsync(ct);
        var reports = new List<SalesHistoryFileReportDto>();
        var warnings = new List<string>();
        foreach (var f in files)
        {
            QbDetailReport parsed;
            try { parsed = QuickBooksDetailReportParser.Parse(QuickBooksDetailReportParser.Decode(f.Content), f.FileName); }
            catch (FormatException ex) { throw new ValidationException("files", ex.Message); }
            if (f.DeclaredKind is string declared && declared != parsed.Kind) throw new ValidationException("files", KindMismatch(f.FileName, parsed.Kind, declared));
            var hash = QuickBooksDetailReportParser.Hash(f.Content);
            reports.Add(parsed.Kind == C.KindSales
                ? await ImportSalesAsync(f.FileName, hash, parsed, matchers, dryRun, source, ct)
                : await ImportPurchasesAsync(f.FileName, hash, parsed, matchers, dryRun, source, ct));
        }
        if (!dryRun && cutoff is DateOnly cut)
        {
            await setup.UpdateSettingsAsync(new SalesAnalysisSettingsRequest(CutoffDate: cut), ct);
            warnings.Add($"Fecha de corte guardada: {cut:yyyy-MM-dd} (el historial cuenta antes de ella; las facturas de Teikem desde ella).");
        }
        if (repsFromCustomField)
        {
            if (dryRun) warnings.Add("Simulación: no se pasan los valores del campo «Representante» a representantes.");
            else
            {
                var mig = await setup.RepsFromCustomFieldAsync(ct);
                warnings.Add($"Campo «Representante»: {mig.RepsCreated} representante(s) creado(s), {mig.ClientsLinked} cliente(s) enlazado(s), {mig.ClientsAlreadyLinked} ya tenían representante.");
            }
        }
        return new SalesHistoryImportReportDto(dryRun, company, cutoff, reports, warnings);
    }

    // ---------------------------------------------------------------- ventas

    private async Task<SalesHistoryFileReportDto> ImportSalesAsync(string fileName, string hash, QbDetailReport parsed, Matchers m, bool dryRun, string source, CancellationToken ct)
    {
        var lines = new List<SalesHistoryLine>(parsed.Rows.Count);
        foreach (var r in parsed.Rows)
        {
            var productId = m.Product(r.ItemSku);
            var cls = R.ClassOf(r.Section, r.ItemHeader, productId is not null);
            lines.Add(new SalesHistoryLine
            {
                SourceLine = r.Line, TxnDate = r.Date, TxnType = Cut(r.Type, 30), DocNumber = CutOrNull(r.Number, 40), Memo = CutOrNull(r.Memo, 250),
                ClientNameRaw = Cut(r.Name, 200), ClientId = m.Client(r.Name), Section = CutOrNull(r.Section, 60), ItemRaw = Cut(r.ItemHeader, 300), ItemSku = Cut(r.ItemSku, 100),
                ProductId = cls == C.ClassProduct ? productId : null, Quantity = r.Quantity, UnitPrice = r.Price is decimal p ? Math.Round(p, 5) : null, Amount = Math.Round(r.Amount, 2),
                LineClass = cls,
            });
        }
        var (action, replaced) = dryRun ? await PreviewReplaceAsync(C.KindSales, hash, lines.Select(l => l.TxnDate), ct)
            : await SaveAsync(C.KindSales, fileName, hash, source, lines.Select(l => l.TxnDate).ToList(), lines.Sum(l => l.Amount),
                db.Set<SalesHistoryLine>(), lines, (l, id) => { l.SalesHistoryBatchId = id; l.TenantId = TenantId; }, ct);
        var unmatchedClients = lines.Where(l => l.ClientId is null).GroupBy(l => l.ClientNameRaw)
            .Select(g => new SalesUnmatchedDto(g.Key, g.Count(), g.Sum(l => l.Amount), g.Min(l => l.TxnDate), g.Max(l => l.TxnDate))).OrderByDescending(u => Math.Abs(u.Amount)).ToList();
        var unmatchedItems = lines.Where(l => l.ProductId is null && l.LineClass == C.ClassProduct).GroupBy(l => l.ItemRaw)
            .Select(g => new SalesUnmatchedDto(g.Key, g.Count(), g.Sum(l => l.Amount), g.Min(l => l.TxnDate), g.Max(l => l.TxnDate))).OrderByDescending(u => Math.Abs(u.Amount)).ToList();
        var byMonth = lines.GroupBy(l => R.MonthStart(l.TxnDate)).OrderBy(g => g.Key)
            .Select(g => new SalesMonthTotalDto(R.MonthKey(g.Key), g.Count(), g.Where(l => l.LineClass == C.ClassProduct).Sum(l => l.Quantity), g.Sum(l => l.Amount),
                g.Where(l => l.LineClass == C.ClassProduct).Sum(l => l.Amount))).ToList();
        return new SalesHistoryFileReportDto(C.KindSales, fileName, parsed.Rows.Count, lines.Count, 0, 0,
            C.Classes.ToDictionary(c => c, c => lines.Count(l => l.LineClass == c)), C.Classes.ToDictionary(c => c, c => lines.Where(l => l.LineClass == c).Sum(l => l.Amount)),
            lines.Count == 0 ? null : lines.Min(l => l.TxnDate), lines.Count == 0 ? null : lines.Max(l => l.TxnDate), lines.Sum(l => l.Amount), parsed.ReportTotal,
            lines.Where(l => l.ClientId is not null).Select(l => l.ClientNameRaw).Distinct().Count(),
            lines.Where(l => l.ProductId is not null).Select(l => l.ItemSku).Distinct().Count(),
            unmatchedClients, unmatchedItems, byMonth, action, replaced, parsed.Warnings);
    }

    // ---------------------------------------------------------------- compras

    private async Task<SalesHistoryFileReportDto> ImportPurchasesAsync(string fileName, string hash, QbDetailReport parsed, Matchers m, bool dryRun, string source,
        CancellationToken ct)
    {
        var dups = R.DuplicatedBills(parsed.Rows.Select(r => new PurchaseDupRow(r.Type, r.Number, r.Name, LegacyImportRules.SkuKey(r.ItemSku), r.Quantity)).ToList());
        var zero = 0;
        var lines = new List<PurchaseHistoryLine>();
        for (var i = 0; i < parsed.Rows.Count; i++)
        {
            var r = parsed.Rows[i];
            if (r.Quantity == 0m) { zero++; continue; }
            if (dups.Contains(i)) continue;
            lines.Add(new PurchaseHistoryLine
            {
                SourceLine = r.Line, TxnDate = r.Date, TxnType = Cut(r.Type, 30), DocNumber = CutOrNull(r.Number, 40), SupplierNameRaw = Cut(r.Name, 200),
                ItemRaw = Cut(r.ItemHeader, 300), ItemSku = Cut(r.ItemSku, 100), ProductId = m.Product(r.ItemSku), Quantity = r.Quantity,
                UnitCost = r.Price is decimal p ? Math.Round(p, 5) : null, Amount = Math.Round(r.Amount, 2),
            });
        }
        var (action, replaced) = dryRun ? await PreviewReplaceAsync(C.KindPurchases, hash, lines.Select(l => l.TxnDate), ct)
            : await SaveAsync(C.KindPurchases, fileName, hash, source, lines.Select(l => l.TxnDate).ToList(), lines.Sum(l => l.Amount),
                db.Set<PurchaseHistoryLine>(), lines, (l, id) => { l.SalesHistoryBatchId = id; l.TenantId = TenantId; }, ct);
        var unmatchedItems = lines.Where(l => l.ProductId is null).GroupBy(l => l.ItemRaw)
            .Select(g => new SalesUnmatchedDto(g.Key, g.Count(), g.Sum(l => l.Amount), g.Min(l => l.TxnDate), g.Max(l => l.TxnDate))).OrderByDescending(u => Math.Abs(u.Amount)).ToList();
        var byMonth = lines.GroupBy(l => R.MonthStart(l.TxnDate)).OrderBy(g => g.Key)
            .Select(g => new SalesMonthTotalDto(R.MonthKey(g.Key), g.Count(), g.Sum(l => l.Quantity), g.Sum(l => l.Amount), g.Sum(l => l.Amount))).ToList();
        var byType = lines.GroupBy(l => l.TxnType).ToDictionary(g => g.Key, g => g.Count());
        var amountByType = lines.GroupBy(l => l.TxnType).ToDictionary(g => g.Key, g => g.Sum(l => l.Amount));
        return new SalesHistoryFileReportDto(C.KindPurchases, fileName, parsed.Rows.Count, lines.Count, zero, dups.Count, byType, amountByType,
            lines.Count == 0 ? null : lines.Min(l => l.TxnDate), lines.Count == 0 ? null : lines.Max(l => l.TxnDate), lines.Sum(l => l.Amount), parsed.ReportTotal,
            0, lines.Where(l => l.ProductId is not null).Select(l => l.ItemSku).Distinct().Count(),
            Array.Empty<SalesUnmatchedDto>(), unmatchedItems, byMonth, action, replaced, parsed.Warnings);
    }

    // ---------------------------------------------------------------- guardar / reemplazar

    private int TenantId => ((TenantContext)tenant).RequireTenantId();

    private IQueryable<long> LineIdsOf(string kind, Func<IQueryable<SalesHistoryLine>, IQueryable<SalesHistoryLine>> sales, Func<IQueryable<PurchaseHistoryLine>, IQueryable<PurchaseHistoryLine>> purchases)
        => kind == C.KindSales ? sales(db.Set<SalesHistoryLine>()).Select(l => l.SalesHistoryLineId) : purchases(db.Set<PurchaseHistoryLine>()).Select(l => l.PurchaseHistoryLineId);

    /// <summary>Lo que haría guardar: reemplazar el lote del mismo archivo o las líneas del rango de fechas.</summary>
    private async Task<(string Action, int Replaced)> PreviewReplaceAsync(string kind, string hash, IEnumerable<DateOnly> dates, CancellationToken ct)
    {
        var same = await db.Set<SalesHistoryBatch>().AsNoTracking().Where(b => b.IsActive && b.Kind == kind && b.FileHash == hash).Select(b => b.SalesHistoryBatchId).ToListAsync(ct);
        if (same.Count > 0) return (ActionReplaceSameFile, await LineIdsOf(kind, q => q.Where(l => same.Contains(l.SalesHistoryBatchId)), q => q.Where(l => same.Contains(l.SalesHistoryBatchId))).CountAsync(ct));
        var list = dates.ToList();
        if (list.Count == 0) return (ActionNew, 0);
        var (from, to) = (list.Min(), list.Max());
        var n = await LineIdsOf(kind, q => q.Where(l => l.TxnDate >= from && l.TxnDate <= to), q => q.Where(l => l.TxnDate >= from && l.TxnDate <= to)).CountAsync(ct);
        return (n > 0 ? ActionReplaceRange : ActionNew, n);
    }

    private async Task<(string Action, int Replaced)> SaveAsync<T>(string kind, string fileName, string hash, string source, List<DateOnly> dates, decimal total,
        DbSet<T> set, List<T> lines, Action<T, int> stamp, CancellationToken ct) where T : class
    {
        return await db.RunInTransactionAsync(async ct2 =>
        {
            var now = DateTime.UtcNow;
            var action = ActionNew;
            var replaced = 0;
            var same = await db.Set<SalesHistoryBatch>().AsTracking().Where(b => b.IsActive && b.Kind == kind && b.FileHash == hash).ToListAsync(ct2);
            if (same.Count > 0)
            {
                action = ActionReplaceSameFile;
                var ids = same.Select(b => b.SalesHistoryBatchId).ToList();
                replaced = kind == C.KindSales
                    ? await DeleteAsync(db.Set<SalesHistoryLine>().Where(l => ids.Contains(l.SalesHistoryBatchId)), ct2)
                    : await DeleteAsync(db.Set<PurchaseHistoryLine>().Where(l => ids.Contains(l.SalesHistoryBatchId)), ct2);
                foreach (var b in same) { b.IsActive = false; b.ReplacedAtUtc = now; }
            }
            else if (dates.Count > 0)
            {
                var (from, to) = (dates.Min(), dates.Max());
                replaced = kind == C.KindSales
                    ? await DeleteAsync(db.Set<SalesHistoryLine>().Where(l => l.TxnDate >= from && l.TxnDate <= to), ct2)
                    : await DeleteAsync(db.Set<PurchaseHistoryLine>().Where(l => l.TxnDate >= from && l.TxnDate <= to), ct2);
                if (replaced > 0)
                {
                    action = ActionReplaceRange;
                    // los lotes tocados se recuentan; el que quedó vacío queda reemplazado
                    var batches = await db.Set<SalesHistoryBatch>().AsTracking().Where(b => b.IsActive && b.Kind == kind).ToListAsync(ct2);
                    foreach (var b in batches)
                    {
                        var (count, sum) = kind == C.KindSales
                            ? (await db.Set<SalesHistoryLine>().CountAsync(l => l.SalesHistoryBatchId == b.SalesHistoryBatchId, ct2),
                               await db.Set<SalesHistoryLine>().Where(l => l.SalesHistoryBatchId == b.SalesHistoryBatchId).SumAsync(l => (decimal?)l.Amount, ct2) ?? 0m)
                            : (await db.Set<PurchaseHistoryLine>().CountAsync(l => l.SalesHistoryBatchId == b.SalesHistoryBatchId, ct2),
                               await db.Set<PurchaseHistoryLine>().Where(l => l.SalesHistoryBatchId == b.SalesHistoryBatchId).SumAsync(l => (decimal?)l.Amount, ct2) ?? 0m);
                        b.LineCount = count;
                        b.TotalAmount = sum;
                        if (count == 0) { b.IsActive = false; b.ReplacedAtUtc = now; }
                    }
                }
            }
            var batch = new SalesHistoryBatch
            {
                TenantId = TenantId, Kind = kind, FileName = Cut(Path.GetFileName(fileName), 260), FileHash = hash, DateFrom = dates.Count == 0 ? null : dates.Min(),
                DateTo = dates.Count == 0 ? null : dates.Max(), LineCount = lines.Count, TotalAmount = Math.Round(total, 2), Source = source, ImportedAtUtc = now, ImportedBy = tenant.UserId,
            };
            db.Set<SalesHistoryBatch>().Add(batch);
            await db.SaveChangesAsync(ct2);
            var auto = db.ChangeTracker.AutoDetectChangesEnabled;
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            try
            {
                foreach (var chunk in lines.Chunk(2000))
                {
                    foreach (var l in chunk) stamp(l, batch.SalesHistoryBatchId);
                    set.AddRange(chunk);
                    await db.SaveChangesAsync(ct2);
                    db.ChangeTracker.Clear();
                }
            }
            finally { db.ChangeTracker.AutoDetectChangesEnabled = auto; }
            return (action, replaced);
        }, ct);
    }

    /// <summary>Borrado masivo de líneas importadas (ExecuteDelete en SQL Server; fila por fila en la base en memoria de las pruebas).</summary>
    private async Task<int> DeleteAsync<T>(IQueryable<T> query, CancellationToken ct) where T : class
    {
        if (db.Database.IsRelational()) return await query.ExecuteDeleteAsync(ct);
        var rows = await query.ToListAsync(ct);
        db.Set<T>().RemoveRange(rows);
        await db.SaveChangesAsync(ct);
        return rows.Count;
    }

    /// <summary>Texto del reporte para la consola (lo que imprime el verbo CLI).</summary>
    public static string Describe(SalesHistoryImportReportDto r)
    {
        var inv = CultureInfo.InvariantCulture;
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Historial de QuickBooks → {r.Company} — {(r.DryRun ? "SIMULACIÓN (no se escribió nada)" : "IMPORTACIÓN REAL")}");
        if (r.CutoffDate is DateOnly c) sb.AppendLine($"Fecha de corte: {c:yyyy-MM-dd}");
        foreach (var f in r.Files)
        {
            sb.AppendLine();
            sb.AppendLine($"== {f.FileName} ({(f.Kind == C.KindSales ? "ventas" : "compras")})");
            sb.AppendLine(string.Create(inv, $"Líneas leídas: {f.LinesRead} · {(r.DryRun ? "se guardarían" : "guardadas")}: {f.LinesSaved} · cantidad 0 omitidas: {f.ZeroQtySkipped} · duplicadas (Bill = Item Receipt) omitidas: {f.DuplicatesSkipped}"));
            sb.AppendLine(string.Create(inv, $"Rango: {f.DateFrom:yyyy-MM-dd} a {f.DateTo:yyyy-MM-dd} · Total: {f.TotalAmount:0.00} · TOTAL del reporte: {f.ReportTotal:0.00}"));
            sb.AppendLine("Por clase/tipo: " + string.Join(" · ", f.LinesByClass.Where(kv => kv.Value > 0).Select(kv =>
                string.Create(inv, $"{kv.Key} {kv.Value} ({f.AmountByClass.GetValueOrDefault(kv.Key):0.00})"))));
            sb.AppendLine($"Lo ya importado: {f.Action} ({f.ReplacedLines} línea(s) reemplazada(s))");
            if (f.Kind == C.KindSales) sb.AppendLine($"Clientes emparejados: {f.MatchedClients} · sin emparejar: {f.UnmatchedClients.Count}");
            sb.AppendLine($"Ítems emparejados: {f.MatchedItems} · sin emparejar: {f.UnmatchedItems.Count}");
            sb.AppendLine("Por mes (líneas · unidades de producto · monto · monto de producto):");
            foreach (var mth in f.ByMonth) sb.AppendLine(string.Create(inv, $"  {mth.Month}  {mth.Lines,6}  {mth.Quantity,12:0.##}  {mth.Amount,14:0.00}  {mth.ProductAmount,14:0.00}"));
            if (f.UnmatchedClients.Count > 0)
            {
                sb.AppendLine("Clientes SIN emparejar (nombre · líneas · monto):");
                foreach (var u in f.UnmatchedClients) sb.AppendLine(string.Create(inv, $"  {u.Raw} · {u.Lines} · {u.Amount:0.00}"));
            }
            if (f.UnmatchedItems.Count > 0)
            {
                sb.AppendLine("Ítems SIN emparejar (ítem · líneas · monto):");
                foreach (var u in f.UnmatchedItems) sb.AppendLine(string.Create(inv, $"  {u.Raw} · {u.Lines} · {u.Amount:0.00}"));
            }
            foreach (var w in f.Warnings) sb.AppendLine("Aviso: " + w);
        }
        foreach (var w in r.Warnings) sb.AppendLine(w);
        return sb.ToString();
    }
}
