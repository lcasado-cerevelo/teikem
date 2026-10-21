using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Accounting;
using Teikem.Domain.Clients;
using Teikem.Domain.Common;
using Teikem.Domain.Constants;
using Teikem.Domain.ExitNotices;
using Teikem.Domain.Export;
using Teikem.Domain.Sales;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Orders;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 181 (H1) — contabilización de compras (CMP-#####) y de despachos (DSP-#####). GENERADA → ACEPTADA → EXPORTADA; una generada o aceptada se cancela y libera sus líneas.
/// Regla acordada (por confirmar con el dueño): se contabiliza por DUEÑO DEL INVENTARIO —lo propio sí, lo de un cliente no—, sin importar el tipo de documento (orden de compra, aviso de llegada o recibo
/// a ciegas) y con el COSTO YA CONGELADO de cada línea (la capa del recibo del Lote 147; el costo de la salida del Lote 148): nada se recalcula al generar ni después.
/// - Compras: una línea por línea de recibo CONFIRMADO (con fecha de recepción) del período, que no sea la devolución de una venta, con lo recibido (incluye lo dañado: costó igual).
/// - Despachos: una línea por línea de recolección o despacho vivo (no eliminado ni revertido) del período.
/// - Una línea nunca está en dos corridas vivas (índice único filtrado por origen): dos corridas a la vez no asientan lo mismo. Cancelar la corrida libera sus líneas.
/// Teikem junta y exporta; no emite el asiento en el sistema contable.
/// </summary>
public sealed class AccountingRunService(TeikemDbContext db, ITenantContext tenant, StatusService statuses, INumberSequenceService numbers, ILookupCache lookups, ITenantClock clock, ExportTemplateService templates)
{
    public const int MaxPageSize = 200;
    private TenantContext T => (TenantContext)tenant;

    // ================================================================ lo que entraría (sin guardar)

    private sealed record Candidate(string SourceType, int SourceId, DateOnly DocDate, string DocumentNumber, string? ReferenceNumber, string? PartyName, string? WarehouseCode, int ProductId, string Sku,
        string ProductName, string? LotNumber, decimal Quantity, decimal? UnitCost, string? CostSource);

    private async Task<List<Candidate>> BuildAsync(string kind, DateOnly start, DateOnly end, CancellationToken ct)
        => kind == AccountingCodes.KindDispatches ? await BuildDispatchesAsync(start, end, ct) : await BuildPurchasesAsync(start, end, ct);

    private async Task<List<Candidate>> BuildPurchasesAsync(DateOnly start, DateOnly end, CancellationToken ct)
    {
        var (since, until) = clock.UtcRange(start, end);
        var rows = await (from l in db.Set<ReceiptLine>().AsNoTracking()
                          join h in db.Set<ReceiptHeader>().AsNoTracking() on l.ReceiptHeaderId equals h.ReceiptHeaderId
                          join p in db.Set<Product>().AsNoTracking() on l.ProductId equals p.ProductId
                          where h.IsActive && h.ReceivedAtUtc != null && h.ReceivedAtUtc >= since && h.ReceivedAtUtc < until && h.SalesReturnId == null && l.ReceivedQty > 0m && p.ClientId == null
                              && !db.Set<AccountingRunLine>().Any(x => x.SourceType == AccountingCodes.SourceReceiptLine && x.SourceId == l.ReceiptLineId && x.IsActive)
                          select new { l.ReceiptLineId, h.ReceivedAtUtc, h.Number, h.WarehouseId, h.AsnId, l.ProductId, p.Sku, ProductName = p.Name, l.LotId, l.ReceivedQty, l.UnitCost, l.CostSource }).ToListAsync(ct);
        if (rows.Count == 0) return new List<Candidate>();

        var asnIds = rows.Where(r => r.AsnId != null).Select(r => r.AsnId!.Value).Distinct().ToList();
        var asns = await db.Set<Asn>().AsNoTracking().Where(a => asnIds.Contains(a.AsnId)).Select(a => new { a.AsnId, a.Number, a.PurchaseOrderId, a.ClientId }).ToDictionaryAsync(a => a.AsnId, ct);
        var poIds = asns.Values.Where(a => a.PurchaseOrderId != null).Select(a => a.PurchaseOrderId!.Value).Distinct().ToList();
        var pos = await db.Set<PurchaseOrder>().AsNoTracking().Where(o => poIds.Contains(o.PurchaseOrderId)).Select(o => new { o.PurchaseOrderId, o.Number, o.SupplierId }).ToDictionaryAsync(o => o.PurchaseOrderId, ct);
        var supplierIds = pos.Values.Select(o => o.SupplierId).Distinct().ToList();
        var suppliers = await db.Set<Supplier>().AsNoTracking().Where(s => supplierIds.Contains(s.SupplierId)).ToDictionaryAsync(s => s.SupplierId, s => s.Name, ct);
        var clientIds = asns.Values.Where(a => a.ClientId != null).Select(a => a.ClientId!.Value).Distinct().ToList();
        var clients = await db.Set<Client>().AsNoTracking().Where(c => clientIds.Contains(c.ClientId)).ToDictionaryAsync(c => c.ClientId, c => c.Name, ct);
        var warehouses = await WarehouseCodesAsync(rows.Select(r => r.WarehouseId), ct);
        var lots = await LotNumbersAsync(rows.Where(r => r.LotId != null).Select(r => r.LotId!.Value), ct);

        return rows.OrderBy(r => r.ReceivedAtUtc).ThenBy(r => r.ReceiptLineId).Select(r =>
        {
            var asn = r.AsnId is int a ? asns.GetValueOrDefault(a) : null;
            var po = asn?.PurchaseOrderId is int pid ? pos.GetValueOrDefault(pid) : null;
            var reference = po?.Number ?? asn?.Number;
            var party = po is not null ? suppliers.GetValueOrDefault(po.SupplierId) : asn?.ClientId is int cid ? clients.GetValueOrDefault(cid) : null;
            return new Candidate(AccountingCodes.SourceReceiptLine, r.ReceiptLineId, clock.DayOf(r.ReceivedAtUtc!.Value), r.Number, reference, party, warehouses.GetValueOrDefault(r.WarehouseId), r.ProductId, r.Sku, r.ProductName,
                r.LotId is int lid ? lots.GetValueOrDefault(lid) : null, r.ReceivedQty, r.UnitCost, r.CostSource);
        }).ToList();
    }

    private async Task<List<Candidate>> BuildDispatchesAsync(DateOnly start, DateOnly end, CancellationToken ct)
    {
        var (since, until) = clock.UtcRange(start, end);
        var rows = await (from l in db.Set<PickBatchLine>().AsNoTracking()
                          join b in db.Set<PickBatch>().AsNoTracking() on l.PickBatchId equals b.PickBatchId
                          join p in db.Set<Product>().AsNoTracking() on l.ProductId equals p.ProductId
                          where b.IsActive && l.ReversalTxnId == null && b.CollectedAtUtc >= since && b.CollectedAtUtc < until && p.ClientId == null
                              && !db.Set<AccountingRunLine>().Any(x => x.SourceType == AccountingCodes.SourcePickLine && x.SourceId == l.PickBatchLineId && x.IsActive)
                          select new { l.PickBatchLineId, b.CollectedAtUtc, b.Number, b.WarehouseId, b.SalesOrderId, b.ExitNoticeId, l.ProductId, p.Sku, ProductName = p.Name, l.LotId, l.Quantity, l.UnitCost }).ToListAsync(ct);
        if (rows.Count == 0) return new List<Candidate>();

        var soIds = rows.Where(r => r.SalesOrderId != null).Select(r => r.SalesOrderId!.Value).Distinct().ToList();
        var sales = await db.Set<SalesOrder>().AsNoTracking().Where(o => soIds.Contains(o.SalesOrderId)).Select(o => new { o.SalesOrderId, o.Number, o.ClientId }).ToDictionaryAsync(o => o.SalesOrderId, ct);
        var enIds = rows.Where(r => r.ExitNoticeId != null).Select(r => r.ExitNoticeId!.Value).Distinct().ToList();
        var notices = await db.Set<ExitNotice>().AsNoTracking().Where(n => enIds.Contains(n.ExitNoticeId)).Select(n => new { n.ExitNoticeId, n.Number, n.ClientId }).ToDictionaryAsync(n => n.ExitNoticeId, ct);
        var clientIds = sales.Values.Select(s => s.ClientId).Concat(notices.Values.Select(n => n.ClientId)).Distinct().ToList();
        var clients = await db.Set<Client>().AsNoTracking().Where(c => clientIds.Contains(c.ClientId)).ToDictionaryAsync(c => c.ClientId, c => c.Name, ct);
        var warehouses = await WarehouseCodesAsync(rows.Select(r => r.WarehouseId), ct);
        var lots = await LotNumbersAsync(rows.Where(r => r.LotId != null).Select(r => r.LotId!.Value), ct);

        return rows.OrderBy(r => r.CollectedAtUtc).ThenBy(r => r.PickBatchLineId).Select(r =>
        {
            var so = r.SalesOrderId is int s ? sales.GetValueOrDefault(s) : null;
            var en = r.ExitNoticeId is int e ? notices.GetValueOrDefault(e) : null;
            var clientId = so?.ClientId ?? en?.ClientId;
            return new Candidate(AccountingCodes.SourcePickLine, r.PickBatchLineId, clock.DayOf(r.CollectedAtUtc), r.Number, so?.Number ?? en?.Number, clientId is int c ? clients.GetValueOrDefault(c) : null,
                warehouses.GetValueOrDefault(r.WarehouseId), r.ProductId, r.Sku, r.ProductName, r.LotId is int lid ? lots.GetValueOrDefault(lid) : null, r.Quantity, r.UnitCost, null);
        }).ToList();
    }

    private async Task<Dictionary<int, string>> WarehouseCodesAsync(IEnumerable<int> ids, CancellationToken ct)
    {
        var list = ids.Distinct().ToList();
        return await db.Set<Warehouse>().AsNoTracking().Where(w => list.Contains(w.WarehouseId)).ToDictionaryAsync(w => w.WarehouseId, w => w.Code, ct);
    }

    private async Task<Dictionary<int, string>> LotNumbersAsync(IEnumerable<int> ids, CancellationToken ct)
    {
        var list = ids.Distinct().ToList();
        return list.Count == 0 ? new Dictionary<int, string>() : await db.Set<InventoryLot>().AsNoTracking().Where(l => list.Contains(l.LotId)).ToDictionaryAsync(l => l.LotId, l => l.LotNumber, ct);
    }

    private static AccountingLineDto View(int id, Candidate c) => new(id, c.DocDate, c.DocumentNumber, c.ReferenceNumber, c.PartyName, c.WarehouseCode, c.Sku, c.ProductName, c.LotNumber, c.Quantity, c.UnitCost,
        AccountingRules.LineTotal(c.Quantity, c.UnitCost), c.CostSource);

    private static void ValidateRequest(AccountingRunRequest? req, out string kind, out DateOnly start, out DateOnly end, out string? note)
    {
        if (req is null) throw new ValidationException("body", "El cuerpo de la solicitud es obligatorio.");
        kind = req.Kind?.Trim().ToUpperInvariant() ?? string.Empty;
        if (!AccountingRules.IsKnownKind(kind)) throw new ValidationException("kind", AccountingRules.KindInvalid);
        if (req.PeriodStart is not DateOnly s || req.PeriodEnd is not DateOnly e) throw new ValidationException("periodStart", AccountingRules.PeriodRequired);
        if (e < s) throw new ValidationException("periodEnd", AccountingRules.PeriodInvalid);
        note = string.IsNullOrWhiteSpace(req.Note) ? null : req.Note.Trim();
        if (note is { Length: > AccountingRules.MaxNoteLength }) throw new ValidationException("note", AccountingRules.NoteTooLong);
        (start, end) = (s, e);
    }

    /// <summary>Lo que entraría en la corrida del período; no guarda nada.</summary>
    public async Task<AccountingPreviewDto> PreviewAsync(string fixedKind, AccountingRunRequest? req, CancellationToken ct)
    {
        ValidateRequest(req is null ? null : req with { Kind = fixedKind }, out var kind, out var start, out var end, out _);
        var built = await BuildAsync(kind, start, end, ct);
        var views = built.Select((c, i) => View(0, c)).ToList();
        return new AccountingPreviewDto(kind, views.Count, Total(views), views.Count(v => v.UnitCost is null), views);
    }

    private static decimal Total(IEnumerable<AccountingLineDto> lines) => decimal.Round(lines.Sum(l => l.TotalCost ?? 0m), 4, MidpointRounding.AwayFromZero);

    // ================================================================ generar

    public async Task<AccountingRunDto> GenerateAsync(string fixedKind, AccountingRunRequest? req, CancellationToken ct)
    {
        ValidateRequest(req is null ? null : req with { Kind = fixedKind }, out var kind, out var start, out var end, out var note);
        if ((await BuildAsync(kind, start, end, ct)).Count == 0) throw new StatusRuleException(AccountingRules.NothingToAccount);
        var numberKind = AccountingCodes.NumberKindOf(kind);
        await numbers.EnsureAsync(numberKind, null, ct);
        Guid publicId = Guid.Empty;

        await db.RunInTransactionAsync(async ct2 =>
        {
            // se vuelve a juntar ya dentro de la transacción: si otra corrida tomó líneas entretanto, no se repiten (y el índice único lo impide si aun así coincidieran)
            var built = await BuildAsync(kind, start, end, ct2);
            if (built.Count == 0) throw new StatusRuleException(AccountingRules.NothingToAccount);
            var tenantRow = await db.Tenants.AsNoTracking().FirstAsync(t => t.TenantId == T.RequireTenantId(), ct2);
            var currencyId = await lookups.GetIdAsync(LookupDomains.Currency, tenantRow.CurrencyCode, ct2);
            var initial = (await statuses.GetInitialAsync(AccountingCodes.StatusDomain, ct2)).StatusCodeId;
            var seq = await numbers.NextAsync(numberKind, null, ct2);
            var run = new AccountingRun
            {
                PublicId = Guid.NewGuid(), TenantId = T.RequireTenantId(), Kind = kind, Number = NumberFormat.Resolve(AccountingCodes.NumberPatternOf(kind), seq), PeriodStart = start, PeriodEnd = end,
                StatusCodeId = initial, CurrencyLookupId = currencyId, Note = note, GeneratedAtUtc = clock.UtcNow, GeneratedBy = tenant.UserId,
            };
            foreach (var c in built)
                run.Lines.Add(new AccountingRunLine
                {
                    TenantId = run.TenantId, SourceType = c.SourceType, SourceId = c.SourceId, DocDate = c.DocDate, DocumentNumber = Trim(c.DocumentNumber, 60), ReferenceNumber = c.ReferenceNumber is null ? null : Trim(c.ReferenceNumber, 60),
                    PartyName = c.PartyName is null ? null : Trim(c.PartyName, 200), WarehouseCode = c.WarehouseCode, ProductId = c.ProductId, Sku = Trim(c.Sku, 60), ProductName = Trim(c.ProductName, 200), LotNumber = c.LotNumber,
                    Quantity = c.Quantity, UnitCost = c.UnitCost, TotalCost = AccountingRules.LineTotal(c.Quantity, c.UnitCost), CostSource = c.CostSource,
                });
            run.LineCount = run.Lines.Count;
            run.TotalAmount = decimal.Round(run.Lines.Sum(l => l.TotalCost ?? 0m), 4, MidpointRounding.AwayFromZero);
            run.UnpricedLineCount = run.Lines.Count(l => l.UnitCost is null);
            db.Set<AccountingRun>().Add(run);
            await db.SaveGuardedAsync(AccountingRules.Concurrency, ct2);
            await statuses.TransitionAsync(AccountingCodes.StatusDomain, AccountingCodes.EntityType, run.AccountingRunId, null, AccountingRunStatuses.Generated, null, ct2);
            publicId = run.PublicId;
        }, ct);
        return await GetAsync(kind, publicId, ct);
    }

    private static string Trim(string s, int max) => s.Length <= max ? s : s[..max];

    // ================================================================ estatus

    private sealed record StatusInfo(string Code, string Label, string Color);

    private async Task<Dictionary<int, StatusInfo>> StatusMapAsync(CancellationToken ct)
        => (await db.StatusCodes.AsNoTracking().Where(s => s.Entity == AccountingCodes.StatusDomain).ToListAsync(ct))
            .ToDictionary(s => s.StatusCodeId, s => new StatusInfo(s.InternalCode, MultilingualText.Resolve(s.LabelJson, T.Lang) ?? s.InternalCode, s.ColorHex ?? "#9CA3AF"));

    /// <summary>La transición va PRIMERO y la corrida se vuelve a cargar después (StatusService puede soltar lo rastreado).</summary>
    private async Task<AccountingRunDto> MoveAsync(string kind, Guid publicId, string[] from, string to, string wrongMessage, string? comment, Func<AccountingRun, CancellationToken, Task>? effect, CancellationToken ct)
    {
        await db.RunInTransactionAsync(async ct2 =>
        {
            var head = await db.Set<AccountingRun>().AsNoTracking().FirstOrDefaultAsync(x => x.PublicId == publicId && x.Kind == kind, ct2) ?? throw new NotFoundException("Corrida", feminine: true);
            var map = await StatusMapAsync(ct2);
            if (!from.Contains(map[head.StatusCodeId].Code)) throw new StatusRuleException(wrongMessage);
            var st = await statuses.TransitionAsync(AccountingCodes.StatusDomain, AccountingCodes.EntityType, head.AccountingRunId, head.StatusCodeId, to, comment, ct2);
            var r = await db.Set<AccountingRun>().AsTracking().FirstAsync(x => x.AccountingRunId == head.AccountingRunId, ct2);
            r.StatusCodeId = st.StatusCodeId;
            if (effect is not null) await effect(r, ct2);
            await db.SaveGuardedAsync(AccountingRules.Concurrency, ct2);
        }, ct);
        return await GetAsync(kind, publicId, ct);
    }

    /// <summary>GENERADA → ACEPTADA: la corrida queda congelada; quién y cuándo.</summary>
    public Task<AccountingRunDto> AcceptAsync(string kind, Guid publicId, CancellationToken ct)
        => MoveAsync(kind, publicId, new[] { AccountingRunStatuses.Generated }, AccountingRunStatuses.Accepted, AccountingRules.AcceptNeedsGenerated, null, (r, _) =>
        {
            (r.AcceptedBy, r.AcceptedAtUtc) = (tenant.UserId, clock.UtcNow);
            return Task.CompletedTask;
        }, ct);

    /// <summary>Cancela una corrida generada o aceptada: sus líneas quedan libres para otra corrida.</summary>
    public async Task<AccountingRunDto> CancelAsync(string kind, Guid publicId, string? comment, CancellationToken ct)
    {
        var text = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();
        return await MoveAsync(kind, publicId, new[] { AccountingRunStatuses.Generated, AccountingRunStatuses.Accepted }, AccountingRunStatuses.Cancelled, AccountingRules.CancelNeedsOpen, text, async (r, ct2) =>
        {
            var runId = r.AccountingRunId;
            foreach (var l in await db.Set<AccountingRunLine>().AsTracking().Where(l => l.AccountingRunId == runId).ToListAsync(ct2)) l.IsActive = false;
            var tracked = await db.Set<AccountingRun>().AsTracking().FirstAsync(x => x.AccountingRunId == runId, ct2);
            tracked.StatusCodeId = r.StatusCodeId;
        }, ct);
    }

    // ================================================================ exportar

    /// <summary>El archivo de la corrida (una fila por línea) con una plantilla de su alcance, de una corrida aceptada o exportada; sin cambiar nada.</summary>
    public async Task<ExportFileDto> PreviewExportAsync(string kind, Guid publicId, Guid? templatePublicId, CancellationToken ct)
    {
        var r = await db.Set<AccountingRun>().AsNoTracking().Include(x => x.Lines).FirstOrDefaultAsync(x => x.PublicId == publicId && x.Kind == kind, ct) ?? throw new NotFoundException("Corrida", feminine: true);
        var code = (await StatusMapAsync(ct))[r.StatusCodeId].Code;
        if (code is not (AccountingRunStatuses.Accepted or AccountingRunStatuses.Exported)) throw new StatusRuleException(AccountingRules.ExportNeedsAccepted);
        var scope = AccountingCodes.ScopeOf(r.Kind);
        var template = await templates.ResolveAsync(scope, templatePublicId, ct);
        var currency = (await lookups.GetAsync(r.CurrencyLookupId, ct))?.InternalCode ?? "USD";
        var rows = new List<IReadOnlyDictionary<string, object?>>();
        var n = 0;
        foreach (var l in r.Lines.OrderBy(x => x.DocDate).ThenBy(x => x.AccountingRunLineId))
        {
            n++;
            rows.Add(new Dictionary<string, object?>
            {
                ["RUN_NUMBER"] = r.Number, ["PERIOD_START"] = r.PeriodStart, ["PERIOD_END"] = r.PeriodEnd, ["LINE_NO"] = n, ["DOC_DATE"] = l.DocDate, ["DOCUMENT_NUMBER"] = l.DocumentNumber,
                ["REFERENCE_NUMBER"] = l.ReferenceNumber, ["PARTY_NAME"] = l.PartyName, ["WAREHOUSE_CODE"] = l.WarehouseCode, ["SKU"] = l.Sku, ["PRODUCT_NAME"] = l.ProductName, ["LOT_NUMBER"] = l.LotNumber,
                ["QUANTITY"] = l.Quantity, ["UNIT_COST"] = l.UnitCost, ["TOTAL_COST"] = l.TotalCost, ["COST_SOURCE"] = l.CostSource, ["CURRENCY"] = currency, ["RUN_TOTAL"] = r.TotalAmount,
            });
        }
        var baseName = (r.Kind == AccountingCodes.KindDispatches ? "contabilizacion-despachos-" : "contabilizacion-compras-") + r.Number;
        var file = ExportEngine.Render(scope, template.Format, template.Delimiter, template.DateFormat, template.IncludeHeader, ExportRules.ParseColumns(template.ColumnsJson), rows, baseName);
        return new ExportFileDto(file.FileName, file.ContentType, file.Text, file.RowCount, template.Name);
    }

    /// <summary>Arma el archivo y, si la corrida estaba aceptada, la pasa a EXPORTADA (quién y cuándo). Una exportada se puede volver a exportar (otra plantilla, otra copia).</summary>
    public async Task<ExportFileDto> ExportAsync(string kind, Guid publicId, Guid? templatePublicId, CancellationToken ct)
    {
        var file = await PreviewExportAsync(kind, publicId, templatePublicId, ct);
        var head = await db.Set<AccountingRun>().AsNoTracking().FirstAsync(x => x.PublicId == publicId && x.Kind == kind, ct);
        if ((await StatusMapAsync(ct))[head.StatusCodeId].Code == AccountingRunStatuses.Accepted)
            await MoveAsync(kind, publicId, new[] { AccountingRunStatuses.Accepted }, AccountingRunStatuses.Exported, AccountingRules.ExportNeedsAccepted, null, (r, _) =>
            {
                (r.ExportedBy, r.ExportedAtUtc) = (tenant.UserId, clock.UtcNow);
                return Task.CompletedTask;
            }, ct);
        return file;
    }

    // ================================================================ consultas

    public async Task<PagedResult<AccountingRunListItemDto>> ListAsync(string? kind, string? status, string? q, int skip, int take, CancellationToken ct)
    {
        var k = kind?.Trim().ToUpperInvariant() ?? string.Empty;
        if (!AccountingRules.IsKnownKind(k)) throw new ValidationException("kind", AccountingRules.KindInvalid);
        skip = Math.Max(0, skip);
        take = Math.Clamp(take <= 0 ? 50 : take, 1, MaxPageSize);
        var map = await StatusMapAsync(ct);
        var query = db.Set<AccountingRun>().AsNoTracking().Where(r => r.Kind == k);
        if (!string.IsNullOrWhiteSpace(status))
        {
            var ids = map.Where(kv => kv.Value.Code.Equals(status.Trim(), StringComparison.OrdinalIgnoreCase)).Select(kv => kv.Key).ToList();
            query = query.Where(r => ids.Contains(r.StatusCodeId));
        }
        if (!string.IsNullOrWhiteSpace(q))
        {
            var s = q.Trim();
            query = query.Where(r => r.Number.Contains(s));
        }
        var total = await query.CountAsync(ct);
        var page = await query.OrderByDescending(r => r.GeneratedAtUtc).ThenByDescending(r => r.AccountingRunId).Skip(skip).Take(take).ToListAsync(ct);
        var items = page.Select(r =>
        {
            var st = map.GetValueOrDefault(r.StatusCodeId) ?? new StatusInfo("", "", "#9CA3AF");
            return new AccountingRunListItemDto(r.PublicId, r.Kind, r.Number, st.Code, st.Label, st.Color, r.PeriodStart, r.PeriodEnd, r.LineCount, r.TotalAmount, r.UnpricedLineCount, r.GeneratedAtUtc);
        }).ToList();
        return new PagedResult<AccountingRunListItemDto>(items, total, skip, take);
    }

    public async Task<AccountingRunDto> GetAsync(string kind, Guid publicId, CancellationToken ct)
    {
        var r = await db.Set<AccountingRun>().AsNoTracking().Include(x => x.Lines).FirstOrDefaultAsync(x => x.PublicId == publicId && x.Kind == kind, ct) ?? throw new NotFoundException("Corrida", feminine: true);
        var st = (await StatusMapAsync(ct)).GetValueOrDefault(r.StatusCodeId) ?? new StatusInfo("", "", "#9CA3AF");
        async Task<string?> UserAsync(int? id) => id is int u ? await db.Users.AsNoTracking().Where(x => x.Id == u).Select(x => x.FullName ?? x.Email).FirstOrDefaultAsync(ct) : null;
        var currency = (await lookups.GetAsync(r.CurrencyLookupId, ct))?.InternalCode ?? "USD";
        var lines = r.Lines.OrderBy(l => l.DocDate).ThenBy(l => l.AccountingRunLineId)
            .Select(l => new AccountingLineDto(l.AccountingRunLineId, l.DocDate, l.DocumentNumber, l.ReferenceNumber, l.PartyName, l.WarehouseCode, l.Sku, l.ProductName, l.LotNumber, l.Quantity, l.UnitCost, l.TotalCost, l.CostSource)).ToList();
        return new AccountingRunDto(r.PublicId, r.Kind, r.Number, st.Code, st.Label, st.Color, r.PeriodStart, r.PeriodEnd, currency, r.LineCount, r.TotalAmount, r.UnpricedLineCount, r.Note, r.GeneratedAtUtc,
            await UserAsync(r.GeneratedBy), r.AcceptedAtUtc, await UserAsync(r.AcceptedBy), r.ExportedAtUtc, await UserAsync(r.ExportedBy), r.RowVersion is null ? string.Empty : Convert.ToBase64String(r.RowVersion), lines);
    }
}
