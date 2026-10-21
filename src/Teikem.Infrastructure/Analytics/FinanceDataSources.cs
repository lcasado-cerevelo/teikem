using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Analytics;
using Teikem.Domain.Billing;
using Teikem.Domain.Cod;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Analytics;

/// <summary>
/// Lote 91 (P12) — fuente COD_COLLECTION: una fila por cobro de COD (lo cobrado en la calle o en la oficina). Actividad de período por CollectedAtUtc. AsNoTracking bajo el
/// filtro de tenant, tope ClientDataSourceHelpers.MaxRows (los más recientes), respeto de q.Ids y del rango (desde inclusivo, hasta exclusivo). Lectura: cod.view y el módulo
/// COD encendido. Los anulados (VOID) no se excluyen: los filtros de fábrica los dejan fuera por StatusCode.
/// </summary>
public sealed class CodCollectionDataSource(TeikemDbContext db, ILookupCache lookups, ITenantContext tenant) : IDataSource
{
    public string Key => FinanceAnalyticsRules.CodSource;
    public string LabelEs => "Cobros COD";
    public string LabelEn => "COD collections";
    public string? EntityTypeCode => null;
    public string? DateField => "CollectedAtUtc";
    public string IdField => "Id";
    public string DefaultBusinessModule => BusinessModules.Accounting;
    public string? TenantModule => ModuleKeys.Cod;

    public IReadOnlyList<DataField> Fields { get; } = new[]
    {
        new DataField("Id", "Id", "Id", DataFieldType.Number),
        new DataField("OrderId", "Id de envío", "Shipment id", DataFieldType.Number),
        new DataField("OrderNumber", "Envío", "Shipment", DataFieldType.Text),
        new DataField("InvoiceNumber", "Factura del cliente", "Client invoice", DataFieldType.Text),
        new DataField("ClientId", "Id de cliente", "Client id", DataFieldType.Number),
        new DataField("ClientName", "Cliente", "Client", DataFieldType.Text),
        new DataField("Method", "Método", "Method", DataFieldType.Text),
        new DataField("MethodCode", "Código del método", "Method code", DataFieldType.Text),
        new DataField("Status", "Estatus", "Status", DataFieldType.Text),
        new DataField("StatusCode", "Código de estatus", "Status code", DataFieldType.Text),
        new DataField("Amount", "Monto cobrado", "Amount collected", DataFieldType.Number, IsMoney: true),
        new DataField("CollectedAtUtc", "Cobrado el", "Collected at", DataFieldType.Date),
        new DataField("ReconciledAtUtc", "Cuadrado el", "Reconciled at", DataFieldType.Date),
        new DataField("DriverId", "Id de chofer", "Driver id", DataFieldType.Number),
        new DataField("CheckNumber", "Número de cheque", "Check number", DataFieldType.Text),
    };

    public IReadOnlyList<DataRelation> Relations { get; } = new[]
    {
        new DataRelation("Client", EntityTypes.Client, "ClientId", "Cliente", "Client"),
    };

    public async Task<List<DataRow>> LoadAsync(DataQuery q, CancellationToken ct)
    {
        var query = db.Set<CodCollection>().AsNoTracking().AsQueryable();
        if (q.Ids is not null)
        {
            var wanted = q.Ids.Select(i => (long)i).ToList();
            query = query.Where(c => wanted.Contains(c.CodCollectionId));
        }
        if (q.FromUtc is DateTime from) query = query.Where(c => c.CollectedAtUtc >= from);
        if (q.ToUtc is DateTime to) query = query.Where(c => c.CollectedAtUtc < to);
        var rows = await query.OrderByDescending(c => c.CollectedAtUtc).ThenByDescending(c => c.CodCollectionId).Take(ClientDataSourceHelpers.MaxRows).ToListAsync(ct);
        if (rows.Count == 0) return new List<DataRow>();

        var orderIds = rows.Select(r => r.TransportOrderId).Distinct().ToList();
        var orders = await db.TransportOrders.AsNoTracking().Where(o => orderIds.Contains(o.TransportOrderId))
            .Select(o => new { o.TransportOrderId, o.OrderNumber, o.ClientInvoiceNumber, o.ClientId }).ToDictionaryAsync(o => o.TransportOrderId, ct);
        var clients = await SalesDataSourceHelpers.ClientNamesAsync(db, orders.Values.Select(o => o.ClientId).Distinct().ToList(), ct);
        var statusMap = await ClientDataSourceHelpers.StatusMapAsync(db, CodCodes.CollectionStatusDomain, ct);
        var lang = tenant.Lang;
        var methods = new Dictionary<int, (string? Label, string? Code)>();

        var result = new List<DataRow>(rows.Count);
        foreach (var c in rows)
        {
            if (!methods.TryGetValue(c.MethodLookupId, out var m))
                methods[c.MethodLookupId] = m = (await ClientDataSourceHelpers.LookupLabelAsync(lookups, c.MethodLookupId, lang, ct), await ClientDataSourceHelpers.LookupCodeAsync(lookups, c.MethodLookupId, ct));
            var o = orders.GetValueOrDefault(c.TransportOrderId);
            result.Add(new DataRow
            {
                ["Id"] = c.CodCollectionId,
                ["OrderId"] = c.TransportOrderId,
                ["OrderNumber"] = o?.OrderNumber,
                ["InvoiceNumber"] = o?.ClientInvoiceNumber,
                ["ClientId"] = o?.ClientId,
                ["ClientName"] = o is null ? null : clients.GetValueOrDefault(o.ClientId),
                ["Method"] = m.Label,
                ["MethodCode"] = m.Code,
                ["Status"] = ClientDataSourceHelpers.StatusLabel(statusMap, c.StatusCodeId, lang),
                ["StatusCode"] = ClientDataSourceHelpers.StatusCodeOf(statusMap, c.StatusCodeId),
                ["Amount"] = c.AmountCollected,
                ["CollectedAtUtc"] = c.CollectedAtUtc,
                ["ReconciledAtUtc"] = c.ReconciledAtUtc,
                ["DriverId"] = c.DriverId,
                ["CheckNumber"] = c.CheckNumber,
            });
        }
        return result;
    }
}

/// <summary>
/// Lote 91 (P12) — fuente INVOICE: una fila por factura al cliente con lo pagado y el saldo. Actividad de período por IssueDate (día de la compañía; los borradores no tienen
/// fecha de emisión y quedan fuera cuando hay rango). IsOpen = Emitida o Vencida; DaysOverdue = días desde el vencimiento (0 si no ha vencido). Lectura: billing.view y el
/// módulo Facturación encendido. Pagado = pagos no anulados.
/// </summary>
public sealed class InvoiceDataSource(TeikemDbContext db, ITenantContext tenant, ITenantClock? clock = null) : IDataSource
{
    public string Key => FinanceAnalyticsRules.InvoiceSource;
    public string LabelEs => "Facturas";
    public string LabelEn => "Invoices";
    public string? EntityTypeCode => null;
    public string? DateField => "IssueDate";
    public string IdField => "Id";
    public string DefaultBusinessModule => BusinessModules.Accounting;
    public string? TenantModule => ModuleKeys.Billing;

    public IReadOnlyList<DataField> Fields { get; } = new[]
    {
        new DataField("Id", "Id", "Id", DataFieldType.Number),
        new DataField("PublicId", "Id público", "Public id", DataFieldType.Text),
        new DataField("Number", "Número de factura", "Invoice number", DataFieldType.Text),
        new DataField("ClientId", "Id de cliente", "Client id", DataFieldType.Number),
        new DataField("ClientName", "Cliente", "Client", DataFieldType.Text),
        new DataField("Status", "Estatus", "Status", DataFieldType.Text),
        new DataField("StatusCode", "Código de estatus", "Status code", DataFieldType.Text),
        new DataField("IsOpen", "Con saldo por cobrar", "Open balance", DataFieldType.Bool),
        new DataField("IsActive", "Activa", "Active", DataFieldType.Bool),
        new DataField("IssueDate", "Fecha de emisión", "Issue date", DataFieldType.Date),
        new DataField("DueDate", "Fecha de vencimiento", "Due date", DataFieldType.Date),
        new DataField("DaysOverdue", "Días de vencida", "Days overdue", DataFieldType.Number),
        new DataField("Subtotal", "Subtotal", "Subtotal", DataFieldType.Number, IsMoney: true),
        new DataField("TaxAmount", "Impuesto", "Tax", DataFieldType.Number, IsMoney: true),
        new DataField("Total", "Total", "Total", DataFieldType.Number, IsMoney: true),
        new DataField("Paid", "Pagado", "Paid", DataFieldType.Number, IsMoney: true),
        new DataField("Balance", "Saldo", "Balance", DataFieldType.Number, IsMoney: true),
    };

    public IReadOnlyList<DataRelation> Relations { get; } = new[]
    {
        new DataRelation("Client", EntityTypes.Client, "ClientId", "Cliente", "Client"),
    };

    public async Task<List<DataRow>> LoadAsync(DataQuery q, CancellationToken ct)
    {
        var query = db.Set<Invoice>().AsNoTracking().AsQueryable();
        if (q.Ids is not null)
        {
            var wanted = q.Ids.ToList();
            query = query.Where(i => wanted.Contains(i.InvoiceId));
        }
        if ((q.FromDay ?? (q.FromUtc.HasValue ? DateOnly.FromDateTime(q.FromUtc.Value) : (DateOnly?)null)) is DateOnly from)
            query = query.Where(i => i.IssueDate >= from);
        DateOnly? toDay = q.ToDayExclusive;
        if (toDay is null && q.ToUtc.HasValue)
        {
            toDay = DateOnly.FromDateTime(q.ToUtc.Value);
            if (q.ToUtc.Value.TimeOfDay != TimeSpan.Zero) toDay = toDay.Value.AddDays(1);
        }
        if (toDay is DateOnly to) query = query.Where(i => i.IssueDate < to);
        var invoices = await query.OrderByDescending(i => i.InvoiceId).Take(ClientDataSourceHelpers.MaxRows).ToListAsync(ct);
        if (invoices.Count == 0) return new List<DataRow>();

        var ids = invoices.Select(i => i.InvoiceId).ToList();
        var paid = await db.Set<Payment>().AsNoTracking().Where(p => ids.Contains(p.InvoiceId) && p.VoidedAtUtc == null)
            .GroupBy(p => p.InvoiceId).Select(g => new { Id = g.Key, Amount = g.Sum(p => p.Amount) }).ToDictionaryAsync(x => x.Id, x => x.Amount, ct);
        var clients = await SalesDataSourceHelpers.ClientNamesAsync(db, invoices.Select(i => i.ClientId).Distinct().ToList(), ct);
        var statusMap = await ClientDataSourceHelpers.StatusMapAsync(db, BillingCodes.InvoiceStatusDomain, ct);
        var today = ClientDataSourceHelpers.Today(clock);
        var lang = tenant.Lang;

        var rows = new List<DataRow>(invoices.Count);
        foreach (var i in invoices)
        {
            var code = ClientDataSourceHelpers.StatusCodeOf(statusMap, i.StatusCodeId);
            var total = i.Subtotal + i.TaxAmount;
            var paidAmount = paid.GetValueOrDefault(i.InvoiceId);
            var open = code is InvoiceStatuses.Issued or InvoiceStatuses.Overdue;
            rows.Add(new DataRow
            {
                ["Id"] = i.InvoiceId,
                ["PublicId"] = i.PublicId.ToString(),
                ["Number"] = i.InvoiceNumber,
                ["ClientId"] = i.ClientId,
                ["ClientName"] = clients.GetValueOrDefault(i.ClientId),
                ["Status"] = ClientDataSourceHelpers.StatusLabel(statusMap, i.StatusCodeId, lang),
                ["StatusCode"] = code,
                ["IsOpen"] = open,
                ["IsActive"] = i.IsActive,
                ["IssueDate"] = i.IssueDate,
                ["DueDate"] = i.DueDate,
                ["DaysOverdue"] = open && i.DueDate is DateOnly due && due < today ? today.DayNumber - due.DayNumber : 0,
                ["Subtotal"] = i.Subtotal,
                ["TaxAmount"] = i.TaxAmount,
                ["Total"] = total,
                ["Paid"] = paidAmount,
                ["Balance"] = open ? Math.Max(0m, total - paidAmount) : 0m,
            });
        }
        return rows;
    }
}
