using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Billing;
using Teikem.Domain.Clients;
using Teikem.Domain.Export;
using Teikem.Domain.Orders;
using Teikem.Domain.Sales;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 77 (P5) — el archivo de exportación a contabilidad de una corrida de facturación. Una fila por línea de factura (con los datos de la factura y de la corrida repetidos), de las facturas de la
/// corrida que no están anuladas, ordenadas por número de factura y línea: la misma corrida con la misma plantilla da siempre el mismo archivo (reproducible).
/// - Preview arma el archivo sin cambiar nada (la vista previa antes de descargar).
/// - Export lo arma y, si la corrida estaba aprobada, la pasa a EXPORTADA (estatus por StatusService, con quién y cuándo). Una corrida exportada se puede volver a exportar (otra plantilla, otra copia).
/// No altera la máquina de estados generar → revisar → aprobar → exportar: es el último paso, con su archivo.
/// </summary>
public sealed class BillingExportService(TeikemDbContext db, ITenantContext tenant, StatusService statuses, ILookupCache lookups, ExportTemplateService templates)
{
    public async Task<ExportFileDto> PreviewAsync(int runId, Guid? templatePublicId, CancellationToken ct)
    {
        var run = await db.Set<BillingRun>().AsNoTracking().FirstOrDefaultAsync(r => r.BillingRunId == runId, ct) ?? throw new NotFoundException("Corrida de facturación", feminine: true);
        var code = await RunCodeAsync(run, ct);
        if (code is not (BillingRunStatuses.Approved or BillingRunStatuses.Exported)) throw new StatusRuleException(ExportRules.ExportNeedsApproved);
        return await BuildAsync(run, templatePublicId, ct);
    }

    public async Task<ExportFileDto> ExportAsync(int runId, Guid? templatePublicId, CancellationToken ct)
    {
        var file = await PreviewAsync(runId, templatePublicId, ct);
        await db.RunInTransactionAsync(async ct2 =>
        {
            var run = await db.Set<BillingRun>().AsTracking().FirstAsync(r => r.BillingRunId == runId, ct2);
            if (await RunCodeAsync(run, ct2) == BillingRunStatuses.Approved)
            {
                var to = await statuses.TransitionAsync(BillingCodes.RunStatusDomain, BillingCodes.RunEntityType, run.BillingRunId, run.StatusCodeId, BillingRunStatuses.Exported, null, ct2);
                (run.StatusCodeId, run.ExportedAtUtc, run.ExportedBy) = (to.StatusCodeId, DateTime.UtcNow, tenant.UserId);
                await db.SaveGuardedAsync(BillingService.Concurrency, ct2);
            }
        }, ct);
        return file;
    }

    private async Task<string> RunCodeAsync(BillingRun run, CancellationToken ct)
        => await db.StatusCodes.AsNoTracking().Where(s => s.StatusCodeId == run.StatusCodeId).Select(s => s.InternalCode).FirstAsync(ct);

    private async Task<ExportFileDto> BuildAsync(BillingRun run, Guid? templatePublicId, CancellationToken ct)
    {
        var template = await templates.ResolveAsync(ExportCodes.ScopeBilling, templatePublicId, ct);
        var voidId = await db.StatusCodes.AsNoTracking().Where(s => s.Entity == BillingCodes.InvoiceStatusDomain && s.InternalCode == InvoiceStatuses.Void).Select(s => s.StatusCodeId).FirstAsync(ct);
        var invoices = await db.Set<Invoice>().AsNoTracking().Include(i => i.Lines).Where(i => i.BillingRunId == run.BillingRunId && i.StatusCodeId != voidId).OrderBy(i => i.InvoiceNumber).ToListAsync(ct);

        var clientIds = invoices.Select(i => i.ClientId).Distinct().ToList();
        var clients = await db.Set<Client>().AsNoTracking().Where(c => clientIds.Contains(c.ClientId)).ToDictionaryAsync(c => c.ClientId, ct);
        var lines = invoices.SelectMany(i => i.Lines).ToList();
        var productIds = lines.Where(l => l.ProductId != null).Select(l => l.ProductId!.Value).Distinct().ToList();
        var skus = await db.Set<Product>().AsNoTracking().Where(p => productIds.Contains(p.ProductId)).ToDictionaryAsync(p => p.ProductId, p => p.Sku, ct);
        var salesLineIds = lines.Where(l => l.SalesOrderLineId != null).Select(l => l.SalesOrderLineId!.Value).Distinct().ToList();
        var salesNumbers = salesLineIds.Count == 0 ? new Dictionary<int, string>()
            : await (from sl in db.Set<SalesOrderLine>().AsNoTracking() join o in db.Set<SalesOrder>().AsNoTracking() on sl.SalesOrderId equals o.SalesOrderId where salesLineIds.Contains(sl.SalesOrderLineId) select new { sl.SalesOrderLineId, o.Number })
                .ToDictionaryAsync(x => x.SalesOrderLineId, x => x.Number, ct);
        var transportIds = lines.Where(l => l.TransportOrderId != null).Select(l => l.TransportOrderId!.Value).Distinct().ToList();
        var transportNumbers = transportIds.Count == 0 ? new Dictionary<int, string>()
            : await db.Set<TransportOrder>().AsNoTracking().Where(o => transportIds.Contains(o.TransportOrderId)).ToDictionaryAsync(o => o.TransportOrderId, o => o.OrderNumber, ct);
        var lookupCodes = new Dictionary<int, string>();
        foreach (var id in invoices.Select(i => i.CurrencyLookupId).Concat(lines.Select(l => l.ChargeTypeLookupId)).Distinct())
            lookupCodes[id] = (await lookups.GetAsync(id, ct))?.InternalCode ?? string.Empty;

        var rows = new List<IReadOnlyDictionary<string, object?>>();
        foreach (var inv in invoices)
        {
            var client = clients[inv.ClientId];
            var n = 0;
            foreach (var l in inv.Lines.OrderBy(x => x.InvoiceLineId))
            {
                n++;
                var sub = Math.Round(l.Quantity * l.UnitAmount, 4, MidpointRounding.AwayFromZero);
                var orderNumber = l.SalesOrderLineId is int sl && salesNumbers.TryGetValue(sl, out var so) ? so : l.TransportOrderId is int to && transportNumbers.TryGetValue(to, out var tn) ? tn : null;
                rows.Add(new Dictionary<string, object?>
                {
                    ["INVOICE_NUMBER"] = inv.InvoiceNumber, ["ISSUE_DATE"] = inv.IssueDate, ["DUE_DATE"] = inv.DueDate, ["CLIENT_CODE"] = client.Code, ["CLIENT_NAME"] = client.Name,
                    ["CURRENCY"] = lookupCodes[inv.CurrencyLookupId], ["LINE_NO"] = n, ["CHARGE_TYPE"] = lookupCodes[l.ChargeTypeLookupId], ["DESCRIPTION"] = l.Description, ["ORDER_NUMBER"] = orderNumber,
                    ["SKU"] = l.ProductId is int p && skus.TryGetValue(p, out var sku) ? sku : null, ["QUANTITY"] = l.Quantity, ["UNIT_AMOUNT"] = l.UnitAmount, ["LINE_SUBTOTAL"] = sub,
                    ["TAX_RATE_PCT"] = l.IsTaxExempt ? 0m : l.TaxRatePct, ["TAX_AMOUNT"] = l.TaxAmount, ["LINE_TOTAL"] = sub + l.TaxAmount, ["INVOICE_SUBTOTAL"] = inv.Subtotal, ["INVOICE_TAX"] = inv.TaxAmount,
                    ["INVOICE_TOTAL"] = inv.Subtotal + inv.TaxAmount, ["RUN_NUMBER"] = run.BillingRunId, ["PERIOD_START"] = run.PeriodStart, ["PERIOD_END"] = run.PeriodEnd,
                });
            }
        }
        var file = ExportEngine.Render(ExportCodes.ScopeBilling, template.Format, template.Delimiter, template.DateFormat, template.IncludeHeader, ExportRules.ParseColumns(template.ColumnsJson), rows,
            $"facturacion-corrida-{run.BillingRunId}");
        return new ExportFileDto(file.FileName, file.ContentType, file.Text, file.RowCount, template.Name);
    }
}
