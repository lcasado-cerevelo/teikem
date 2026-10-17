namespace Teikem.Domain.Analytics;

/// <summary>
/// Lote 91 (P12) — indicadores y gráficos de fábrica de COD y facturación: las claves de las dos fuentes nuevas (COD_COLLECTION e INVOICE), los filtros y los nombres
/// de lo sembrado. Los nombres de campo son los de CodCollectionDataSource e InvoiceDataSource; no cambiarlos sin cambiar ambos.
/// </summary>
public static class FinanceAnalyticsRules
{
    public const string CodSource = "COD_COLLECTION";
    public const string InvoiceSource = "INVOICE";

    /// <summary>Un cobro COD que cuenta: todo menos los anulados.</summary>
    public const string CodLiveFilter = "{\"and\":[{\"field\":\"StatusCode\",\"op\":\"neq\",\"value\":\"VOID\"}]}";
    /// <summary>Cobrado en la calle y todavía sin cuadrar.</summary>
    public const string CodUnreconciledFilter = "{\"and\":[{\"field\":\"StatusCode\",\"op\":\"eq\",\"value\":\"COLLECTED\"}]}";
    /// <summary>Cuadrado y todavía sin remitir al cliente.</summary>
    public const string CodUnremittedFilter = "{\"and\":[{\"field\":\"StatusCode\",\"op\":\"eq\",\"value\":\"RECONCILED\"}]}";

    /// <summary>Factura emitida con algo por cobrar: Emitida o Vencida, activa.</summary>
    public const string InvoiceOpenFilter = "{\"and\":[{\"field\":\"IsActive\",\"op\":\"isTrue\"},{\"field\":\"IsOpen\",\"op\":\"isTrue\"}]}";
    public const string InvoiceOverdueFilter = "{\"and\":[{\"field\":\"IsActive\",\"op\":\"isTrue\"},{\"field\":\"StatusCode\",\"op\":\"eq\",\"value\":\"OVERDUE\"}]}";
    /// <summary>Facturas emitidas (no borrador ni anuladas) para sumar lo facturado del período.</summary>
    public const string InvoiceIssuedFilter = "{\"and\":[{\"field\":\"IsActive\",\"op\":\"isTrue\"},{\"field\":\"StatusCode\",\"op\":\"in\",\"value\":[\"ISSUED\",\"PAID\",\"OVERDUE\"]}]}";

    /// <summary>Ventas confirmadas: órdenes de venta que no son borrador ni canceladas (para sumar su total).</summary>
    public const string SalesConfirmedFilter = "{\"and\":[{\"field\":\"StatusCode\",\"op\":\"in\",\"value\":[\"REQUESTED\",\"PARTIAL\",\"SHIPPED\"]}]}";

    public const string SalesLast30IndicatorName = "Ventas (30 días)";
    public const string CodCollectedIndicatorName = "COD cobrado (30 días)";
    public const string CodUnreconciledIndicatorName = "COD cobrado sin cuadrar";
    public const string CodUnremittedIndicatorName = "COD cuadrado sin remesa";
    public const string InvoicesReceivableIndicatorName = "Facturas por cobrar";
    public const string InvoicesOverdueIndicatorName = "Facturas vencidas";
    public const string InvoicedIndicatorName = "Facturado (30 días)";
    public const string CodByMethodChartName = "Cobros COD por método";
    public const string CodByDayChartName = "COD cobrado por día";
    public const string InvoicesByStatusChartName = "Facturas por estatus";
    public const string InvoicedByDayChartName = "Facturado por día";
    public const string SalesByDayChartName = "Ventas por día";
}
