using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Analytics;
using Teikem.Domain.Billing;
using Teikem.Domain.Common;
using Teikem.Domain.Constants;
using Teikem.Domain.Sales;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Analytics;

/// <summary>
/// Lote 224 (P6 del pedido de vendedores) — fuente SALES_REVENUE, «lo vendido»: lo que los reportes e indicadores de ventas suman. Una fila por
/// - orden de venta confirmada (solicitada, despachada en parte o despachada) que NO es de consignación: su total (subtotal + IVU, como «Ventas (30 días)» de antes) en su fecha de
///   creación (CreatedAtUtc), y
/// - pago de consignación que cuenta (no anulado, ni anulado su pago de la factura, de una factura emitida, vencida o pagada): su monto en la fecha del pago (mediodía del día de la
///   compañía, para que caiga en su día). Lo consignado sin pagar no sale; cada pago lo mueve a vendido en su fecha.
/// Actividad de período por SoldAtUtc. AsNoTracking bajo el filtro de tenant (SalesOrder, ConsignmentPayment e Invoice son ITenantScoped), tope MaxRows por cada clase de fila.
/// Lectura: sales.view y el módulo Órdenes de venta encendido.
/// </summary>
public sealed class SalesRevenueDataSource(TeikemDbContext db, ITenantContext tenant, ITenantClock? clock = null) : IDataSource
{
    public const string KindOrder = "ORDER";
    public const string KindConsignmentPayment = "CONSIGNMENT_PAYMENT";

    private static readonly string[] Confirmed = { SalesOrderStatuses.Requested, SalesOrderStatuses.Partial, SalesOrderStatuses.Shipped };
    private static readonly string[] InvoiceCounted = { InvoiceStatuses.Issued, InvoiceStatuses.Overdue, InvoiceStatuses.Paid };

    public string Key => FinanceAnalyticsRules.SalesRevenueSource;
    public string LabelEs => "Ventas (lo vendido)";
    public string LabelEn => "Sales (what was sold)";
    public string? EntityTypeCode => null;
    public string? DateField => "SoldAtUtc";
    public string IdField => "Id";
    public string DefaultBusinessModule => BusinessModules.Warehouse;
    public string? TenantModule => ModuleKeys.SalesOrders;

    public IReadOnlyList<DataField> Fields { get; } = new[]
    {
        new DataField("Id", "Id", "Id", DataFieldType.Text),
        new DataField("Kind", "Tipo", "Kind", DataFieldType.Text),
        new DataField("KindCode", "Código del tipo", "Kind code", DataFieldType.Text),
        new DataField("IsConsignment", "Consignación", "Consignment", DataFieldType.Bool),
        new DataField("OrderNumber", "Orden", "Order", DataFieldType.Text),
        new DataField("InvoiceNumber", "Factura", "Invoice", DataFieldType.Text),
        new DataField("ClientId", "Id de cliente", "Client id", DataFieldType.Number),
        new DataField("ClientName", "Cliente", "Client", DataFieldType.Text),
        new DataField("Amount", "Monto vendido", "Amount sold", DataFieldType.Number, IsMoney: true),
        new DataField("SoldAtUtc", "Vendido el", "Sold at", DataFieldType.Date),
    };

    public IReadOnlyList<DataRelation> Relations { get; } = new[]
    {
        new DataRelation("Client", EntityTypes.Client, "ClientId", "Cliente", "Client"),
    };

    public async Task<List<DataRow>> LoadAsync(DataQuery q, CancellationToken ct)
    {
        var c = clock ?? TenantClock.Default;
        var lang = tenant.Lang;
        var rows = new List<DataRow>();

        // 1) órdenes confirmadas que no son de consignación, en su fecha de creación
        var confirmed = await db.StatusCodes.AsNoTracking().Where(s => s.Entity == SalesCodes.StatusDomain && Confirmed.Contains(s.InternalCode)).Select(s => s.StatusCodeId).ToListAsync(ct);
        var orders = db.Set<SalesOrder>().AsNoTracking().Where(o => !o.IsConsignment && confirmed.Contains(o.StatusCodeId));
        if (q.FromUtc is DateTime from) orders = orders.Where(o => o.CreatedAtUtc >= from);
        if (q.ToUtc is DateTime to) orders = orders.Where(o => o.CreatedAtUtc < to);
        var orderRows = await orders.OrderByDescending(o => o.SalesOrderId).Take(ClientDataSourceHelpers.MaxRows)
            .Select(o => new { o.SalesOrderId, o.Number, o.ClientId, Amount = o.Subtotal + o.TaxAmount, o.CreatedAtUtc }).ToListAsync(ct);

        // 2) pagos de consignación que cuentan, en la fecha del pago (días de la compañía)
        var fromDay = q.FromDay ?? (q.FromUtc is DateTime fu ? (DateOnly?)LocalDay.DayOf(fu, c.Zone) : null);
        var toDay = q.ToDayExclusive ?? (q.ToUtc is DateTime tu ? (DateOnly?)LocalDay.DayOf(tu, c.Zone) : null);
        var invoiceOk = await db.StatusCodes.AsNoTracking().Where(s => s.Entity == BillingCodes.InvoiceStatusDomain && InvoiceCounted.Contains(s.InternalCode))
            .Select(s => s.StatusCodeId).ToListAsync(ct);
        var payments = from cp in db.Set<ConsignmentPayment>().AsNoTracking()
                       join i in db.Set<Invoice>().AsNoTracking() on cp.InvoiceId equals i.InvoiceId
                       join p in db.Set<Payment>().AsNoTracking() on cp.PaymentId equals (int?)p.PaymentId into pj
                       from p in pj.DefaultIfEmpty()
                       where cp.VoidedAtUtc == null && (p == null || p.VoidedAtUtc == null) && i.IsActive && invoiceOk.Contains(i.StatusCodeId)
                       select new { cp.ConsignmentPaymentId, cp.InvoiceId, i.InvoiceNumber, i.ClientId, cp.Amount, cp.PaidDate };
        if (fromDay is DateOnly fd) payments = payments.Where(x => x.PaidDate >= fd);
        if (toDay is DateOnly td) payments = payments.Where(x => x.PaidDate < td);
        var paymentRows = await payments.OrderByDescending(x => x.ConsignmentPaymentId).Take(ClientDataSourceHelpers.MaxRows).ToListAsync(ct);
        var invoiceIds = paymentRows.Select(x => x.InvoiceId).Distinct().ToList();
        var orderNumbers = invoiceIds.Count == 0 ? new Dictionary<int, string>() : await ConsignmentOrderNumbersAsync(invoiceIds, ct);

        var clients = await SalesDataSourceHelpers.ClientNamesAsync(db, orderRows.Select(o => o.ClientId).Concat(paymentRows.Select(x => x.ClientId)).Distinct().ToList(), ct);
        string order = lang == "en" ? "Sale" : "Venta";
        string consignment = lang == "en" ? "Consignment payment" : "Pago de consignación";
        foreach (var o in orderRows)
            rows.Add(new DataRow
            {
                ["Id"] = $"{KindOrder}:{o.SalesOrderId}", ["Kind"] = order, ["KindCode"] = KindOrder, ["IsConsignment"] = false, ["OrderNumber"] = o.Number, ["InvoiceNumber"] = null,
                ["ClientId"] = o.ClientId, ["ClientName"] = clients.GetValueOrDefault(o.ClientId), ["Amount"] = o.Amount, ["SoldAtUtc"] = o.CreatedAtUtc,
            });
        foreach (var x in paymentRows)
            rows.Add(new DataRow
            {
                ["Id"] = $"{KindConsignmentPayment}:{x.ConsignmentPaymentId}", ["Kind"] = consignment, ["KindCode"] = KindConsignmentPayment, ["IsConsignment"] = true,
                ["OrderNumber"] = orderNumbers.GetValueOrDefault(x.InvoiceId), ["InvoiceNumber"] = x.InvoiceNumber, ["ClientId"] = x.ClientId,
                ["ClientName"] = clients.GetValueOrDefault(x.ClientId), ["Amount"] = x.Amount, ["SoldAtUtc"] = c.StartOfDayUtc(x.PaidDate).AddHours(12),
            });
        return rows;
    }

    /// <summary>Números de las órdenes en consignación de cada factura (separados por coma).</summary>
    private async Task<Dictionary<int, string>> ConsignmentOrderNumbersAsync(List<int> invoiceIds, CancellationToken ct)
    {
        var byLine = await (from il in db.Set<InvoiceLine>().AsNoTracking()
                            join sol in db.Set<SalesOrderLine>().AsNoTracking() on il.SalesOrderLineId equals (int?)sol.SalesOrderLineId
                            join so in db.Set<SalesOrder>().AsNoTracking() on sol.SalesOrderId equals so.SalesOrderId
                            where invoiceIds.Contains(il.InvoiceId) && so.IsConsignment
                            select new { il.InvoiceId, so.Number }).Distinct().ToListAsync(ct);
        var byOrder = await (from il in db.Set<InvoiceLine>().AsNoTracking()
                             join so in db.Set<SalesOrder>().AsNoTracking() on il.SalesOrderId equals (int?)so.SalesOrderId
                             where invoiceIds.Contains(il.InvoiceId) && so.IsConsignment
                             select new { il.InvoiceId, so.Number }).Distinct().ToListAsync(ct);
        return byLine.Concat(byOrder).GroupBy(x => x.InvoiceId)
            .ToDictionary(g => g.Key, g => string.Join(", ", g.Select(x => x.Number).Distinct().OrderBy(n => n, StringComparer.Ordinal)));
    }
}
