using Teikem.Domain.Constants;
using Teikem.Domain.Sales;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Services;

namespace Teikem.Infrastructure.Analytics;

/// <summary>
/// Lote 241 — fuente SALES_ANALYSIS_LINE, «Líneas de venta para análisis»: una fila por línea de venta del análisis (historial importado de QuickBooks antes de la fecha de
/// corte y facturas / notas de crédito de Teikem desde ella, con los kits repartidos entre sus piezas), con su representante (el actual del cliente), ruta, línea y clase.
/// Actividad de período por Date (fecha de factura, día de calendario): el rango llega en días de la compañía (FromDay/ToDayExclusive). Sin rango = desde 2000. Tope
/// ClientDataSourceHelpers.MaxRows (las más recientes). Lectura: sales.analysis.view y el módulo Órdenes de venta encendido.
/// </summary>
public sealed class SalesAnalysisLineDataSource(SalesAnalysisService analysis, ITenantClock? clock = null) : IDataSource
{
    public string Key => SalesAnalysisCodes.DataSourceKey;
    public string LabelEs => "Líneas de venta para análisis";
    public string LabelEn => "Sales lines for analysis";
    public string? EntityTypeCode => null;
    public string? DateField => "Date";
    public string IdField => "Id";
    public string DefaultBusinessModule => BusinessModules.Warehouse;
    public string? TenantModule => ModuleKeys.SalesOrders;

    public IReadOnlyList<DataField> Fields { get; } = new[]
    {
        new DataField("Id", "Id", "Id", DataFieldType.Text),
        new DataField("Date", "Fecha", "Date", DataFieldType.Date),
        new DataField("Month", "Mes", "Month", DataFieldType.Text),
        new DataField("Source", "Fuente", "Source", DataFieldType.Text),
        new DataField("Type", "Tipo", "Type", DataFieldType.Text),
        new DataField("Number", "Número", "Number", DataFieldType.Text),
        new DataField("ClientId", "Id de cliente", "Client id", DataFieldType.Number),
        new DataField("ClientName", "Cliente", "Client", DataFieldType.Text),
        new DataField("RepName", "Representante", "Sales rep", DataFieldType.Text),
        new DataField("RouteName", "Ruta", "Route", DataFieldType.Text),
        new DataField("Sku", "SKU", "SKU", DataFieldType.Text),
        new DataField("Product", "Producto", "Product", DataFieldType.Text),
        new DataField("Brand", "Marca", "Brand", DataFieldType.Text),
        new DataField("Line", "Línea", "Line", DataFieldType.Text),
        new DataField("LineClass", "Clase", "Class", DataFieldType.Text),
        new DataField("Quantity", "Unidades", "Units", DataFieldType.Number),
        new DataField("UnitPrice", "Precio", "Price", DataFieldType.Number, IsMoney: true),
        new DataField("Amount", "Venta", "Sales", DataFieldType.Number, IsMoney: true),
    };

    public IReadOnlyList<DataRelation> Relations { get; } = new[]
    {
        new DataRelation("Client", EntityTypes.Client, "ClientId", "Cliente", "Client"),
    };

    public async Task<List<DataRow>> LoadAsync(DataQuery q, CancellationToken ct)
    {
        var c = clock ?? TenantClock.Default;
        var from = q.FromDay ?? (q.FromUtc is DateTime fu ? c.DayOf(fu) : new DateOnly(2000, 1, 1));
        var to = q.ToDayExclusive ?? (q.ToUtc is DateTime tu ? c.DayOf(tu) : c.Today.AddDays(1));
        if (to <= from) return new List<DataRow>();
        var lines = await analysis.LinesForRangeAsync(from, to, ct);
        var rows = new List<DataRow>(Math.Min(lines.Count, ClientDataSourceHelpers.MaxRows));
        var i = 0;
        foreach (var (l, brand) in lines.Take(ClientDataSourceHelpers.MaxRows))
        {
            i++;
            rows.Add(new DataRow
            {
                ["Id"] = $"{l.Source}-{l.Number}-{l.Date:yyyyMMdd}-{i}",
                ["Date"] = l.Date,
                ["Month"] = SalesAnalysisRules.MonthKey(SalesAnalysisRules.MonthStart(l.Date)),
                ["Source"] = l.Source,
                ["Type"] = l.Type,
                ["Number"] = l.Number,
                ["ClientId"] = l.ClientId,
                ["ClientName"] = l.ClientName,
                ["RepName"] = l.RepName ?? SalesAnalysisRules.NoRepName,
                ["RouteName"] = l.RouteName ?? (l.RepName is null ? SalesAnalysisRules.NoRepName : SalesAnalysisRules.NoRouteName),
                ["Sku"] = l.Sku,
                ["Product"] = l.Product,
                ["Brand"] = brand,
                ["Line"] = l.Line,
                ["LineClass"] = l.LineClass,
                ["Quantity"] = l.Quantity,
                ["UnitPrice"] = l.UnitPrice,
                ["Amount"] = l.Amount,
            });
        }
        return rows;
    }
}
