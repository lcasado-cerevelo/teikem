using Teikem.Domain.Constants;

namespace Teikem.Domain.Sales;

/// <summary>
/// Lote 67 (P4 S4) — reglas puras del análisis de órdenes de venta: nombres y filtros sembrados de las vistas, indicadores y gráfico de sistema (los nombres de
/// campo son los de SalesOrderDataSource y SalesOrderLineDataSource; no cambiarlos sin cambiar ambos), el cumplimiento (fill rate) y los días a despachar. Sin BD.
/// </summary>
public static class SalesAnalyticsRules
{
    // ---------------------------------------------------------------- contenido de sistema (nombres estables)
    public const string OpenOrdersIndicatorName = "Órdenes de venta abiertas";
    public const string BackorderUnitsIndicatorName = "Unidades en backorder";
    public const string FillRateIndicatorName = "Cumplimiento de órdenes de venta (%)";
    public const string DaysToShipIndicatorName = "Días promedio a despachar";
    public const string OrdersByStatusChartName = "Órdenes de venta por estatus";
    public const string OrdersReportName = "Órdenes de venta";
    public const string BackordersReportName = "Backorders de órdenes de venta";
    public const string OrdersByClientReportName = "Órdenes de venta por cliente";

    /// <summary>Abierta = solicitada o despachada parcial (todavía hay algo por despachar o esperando inventario).</summary>
    public const string OpenFilter = "{\"and\":[{\"field\":\"IsOpen\",\"op\":\"isTrue\"}]}";

    /// <summary>Líneas con backorder pendiente (no cerradas con faltante) de órdenes abiertas.</summary>
    public const string PendingBackorderFilter =
        "{\"and\":[{\"field\":\"IsOpen\",\"op\":\"isTrue\"},{\"field\":\"QtyBackordered\",\"op\":\"gt\",\"value\":0},{\"field\":\"IsClosed\",\"op\":\"isFalse\"}]}";

    /// <summary>Órdenes terminadas (despachadas por completo): las que tienen cumplimiento y días a despachar.</summary>
    public const string ShippedFilter = "{\"and\":[{\"field\":\"StatusCode\",\"op\":\"eq\",\"value\":\"SHIPPED\"}]}";

    /// <summary>Vista agrupada 'Órdenes de venta por cliente': órdenes y unidades pedidas por cliente, con totales.</summary>
    public const string OrdersByClientGroup =
        "{\"by\":[\"ClientName\"],\"aggregates\":[{\"fn\":\"COUNT\"},{\"fn\":\"SUM\",\"field\":\"UnitsOrdered\"},{\"fn\":\"SUM\",\"field\":\"Total\"}],\"totals\":true}";

    // ---------------------------------------------------------------- cálculos

    public static bool IsOpen(string? statusCode)
        => statusCode is SalesOrderStatuses.Requested or SalesOrderStatuses.Partial;

    /// <summary>Cumplimiento = lo despachado sobre lo pedido, en porcentaje con dos decimales (0 si no se pidió nada).</summary>
    public static decimal FillRatePct(decimal ordered, decimal shipped)
        => ordered <= 0m ? 0m : Math.Round(Math.Min(shipped, ordered) / ordered * 100m, 2);

    /// <summary>Días entre solicitarla y despacharla por completo, con dos decimales; null si falta alguna de las dos fechas.</summary>
    public static decimal? DaysToShip(DateTime? requestedAtUtc, DateTime? shippedAtUtc)
        => requestedAtUtc is DateTime a && shippedAtUtc is DateTime b && b >= a ? Math.Round((decimal)(b - a).TotalDays, 2) : null;

    /// <summary>Atrasada = abierta con fecha requerida anterior a hoy.</summary>
    public static bool IsLate(string? statusCode, DateOnly? requiredDate, DateOnly today)
        => IsOpen(statusCode) && requiredDate is DateOnly d && d < today;

    /// <summary>Días que lleva esperando un backorder pendiente (desde que se solicitó la orden); 0 si no hay backorder.</summary>
    public static int DaysWaiting(decimal backordered, bool closed, DateTime? requestedAtUtc, DateTime nowUtc)
        => backordered > 0m && !closed && requestedAtUtc is DateTime a ? Math.Max(0, (int)(nowUtc - a).TotalDays) : 0;
}
