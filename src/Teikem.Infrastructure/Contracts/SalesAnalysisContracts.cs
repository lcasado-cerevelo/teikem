namespace Teikem.Infrastructure.Contracts;

// Lote 241 — «Análisis de ventas» (/api/v1/sales-analysis). Montos en dólares con 2 decimales; proporciones como fracción (0.25 = 25 %); meses «AAAA-MM».

// ---------------------------------------------------------------- ajustes

/// <summary>Ajustes del análisis: fecha de corte (historial antes, facturas de Teikem desde ella; null = todo cuenta), días de entrega y nivel de servicio (z).</summary>
public sealed record SalesAnalysisSettingsDto(DateOnly? CutoffDate, int LeadTimeDays, decimal ServiceLevelZ);

/// <summary>Cambio parcial: null = sin cambio; ClearCutoff = quitar la fecha de corte.</summary>
public sealed record SalesAnalysisSettingsRequest(DateOnly? CutoffDate = null, bool ClearCutoff = false, int? LeadTimeDays = null, decimal? ServiceLevelZ = null);

// ---------------------------------------------------------------- rutas, representantes, cuotas, líneas

public sealed record SalesRouteDto(int Id, string Name, int SortOrder, bool IsActive, int RepCount);
public sealed record SalesRouteRequest(string? Name, int? SortOrder = null);

/// <summary>Representante con su ruta, su usuario de Teikem (opcional), cuántos clientes activos tiene y su cuota vigente del mes en curso.</summary>
public sealed record SalesRepDto(int Id, string Name, int? RouteId, string? RouteName, int? UserId, string? UserName, bool IsActive, int ClientCount, decimal? CurrentQuota);

/// <summary>Alta o cambio: null = sin cambio (en el alta, sin ruta / sin usuario); ClearRoute / ClearUser los quitan.</summary>
public sealed record SalesRepRequest(string? Name, int? RouteId = null, bool ClearRoute = false, int? UserId = null, bool ClearUser = false);

/// <summary>Cuotas de un mes: una fila por representante (activos y los que tienen cuota propia ese mes). Amount = la propia del mes; Effective = la que vale (la del mes o la anterior más reciente).</summary>
public sealed record SalesQuotaMonthDto(string Month, IReadOnlyList<SalesQuotaRowDto> Rows, decimal TotalEffective);
public sealed record SalesQuotaRowDto(int RepId, string RepName, string? RouteName, bool RepActive, decimal? Amount, decimal? Effective, string? EffectiveFrom);

/// <summary>Edición en bloque de un mes: Amount null = quitar la cuota propia de ese mes (vuelve a valer la anterior).</summary>
public sealed record SalesQuotaBulkRequest(IReadOnlyList<SalesQuotaItemRequest>? Items);
public sealed record SalesQuotaItemRequest(int RepId, decimal? Amount);

/// <summary>Las cuotas propias de un representante, mes por mes.</summary>
public sealed record SalesQuotaHistoryDto(int RepId, string RepName, IReadOnlyList<SalesQuotaPointDto> Quotas);
public sealed record SalesQuotaPointDto(string Month, decimal Amount);

public sealed record ProductLineDto(int Id, string Name, int SortOrder, bool IsActive, IReadOnlyList<string> Brands);
public sealed record ProductLineRequest(string? Name, int? SortOrder = null);

/// <summary>Las marcas de la línea (reemplaza la lista completa). Una marca que está en otra línea da 409.</summary>
public sealed record ProductLineBrandsRequest(IReadOnlyList<string>? Brands);

/// <summary>Una marca de los productos con su línea efectiva (la de la tabla marca → línea o, sin ella, la propia marca).</summary>
public sealed record SalesBrandDto(string Brand, int ProductCount, int? LineId, string Line, bool HasLine);

// ---------------------------------------------------------------- clientes ↔ representante

public sealed record SalesClientRepDto(int ClientId, Guid PublicId, string Code, string Name, bool IsActive, int? RepId, string? RepName, string? RouteName);
public sealed record SalesClientRepRequest(int? RepId);
public sealed record SalesClientRepBulkRequest(IReadOnlyList<int>? ClientIds, int? RepId);
public sealed record SalesClientRepBulkResultDto(int Updated, int? RepId, string? RepName);

/// <summary>Resultado de pasar el campo personalizado «Representante» (sales_rep) a representantes: cuántos se crearon y cuántos clientes se enlazaron.</summary>
public sealed record SalesRepMigrationDto(int RepsCreated, int ClientsLinked, int ClientsAlreadyLinked);

// ---------------------------------------------------------------- vista «Por representante»

public sealed record SalesByRepDto(string Month, string CompareMonth, DateOnly? CutoffDate, IReadOnlyList<string> Warnings,
    IReadOnlyList<SalesRepRowDto> Reps, SalesRepTotalsDto Totals, SalesReconciliationDto Reconciliation,
    IReadOnlyList<SalesLineShareDto> Lines, IReadOnlyList<SalesRepLineDto> RepLines,
    IReadOnlyList<SalesRepProductDto> RepProducts, IReadOnlyList<SalesRouteProductDto> RouteProducts);

/// <summary>Una fila por ruta y representante: venta del mes de comparación y del mes (producto + descuentos), cambio $ y %, cuota vigente y cumplimiento.</summary>
public sealed record SalesRepRowDto(int? RouteId, string RouteName, int? RepId, string RepName, decimal CompareSales, decimal Sales, decimal Change, decimal? ChangePct,
    decimal? Quota, decimal? Compliance);

public sealed record SalesRepTotalsDto(decimal CompareSales, decimal Sales, decimal Change, decimal? ChangePct, decimal Quota, decimal? Compliance);

/// <summary>
/// Conciliación del mes: RoutesTotal (suma de las filas) + Unassigned (clientes sin representante o sin emparejar) + ShippingAndOther (envío y otros cargos) = MonthTotal
/// (todas las líneas del mes). Difference debería ser 0. También el desglose por clase y lo sin representante del mes de comparación.
/// </summary>
public sealed record SalesReconciliationDto(decimal RoutesTotal, decimal Unassigned, decimal ShippingAndOther, decimal MonthTotal, decimal Difference,
    decimal ProductSales, decimal Discounts, decimal Shipping, decimal Other, int UnassignedClients, decimal UnassignedCompare);

/// <summary>Una línea (distribuidor o línea): venta del mes, % de la venta neta y de la venta antes de descuentos, y el líder (representante que más vendió) con su ruta.</summary>
public sealed record SalesLineShareDto(string Line, bool IsDiscounts, decimal Sales, decimal? ShareOfNet, decimal? ShareOfGross, int? LeaderRepId, string? LeaderName,
    string? LeaderRoute, decimal? LeaderSales);

public sealed record SalesRepLineDto(string RouteName, int? RepId, string RepName, string Line, bool IsDiscounts, decimal Sales, decimal? ShareOfRep);

/// <summary>Representante × producto: unidades netas y venta neta del mes (también los ítems de descuento del historial, con LineClass DISCOUNT).</summary>
public sealed record SalesRepProductDto(string RouteName, int? RepId, string RepName, string Line, int? ProductId, string Sku, string Product, string LineClass,
    decimal Units, decimal Sales);

/// <summary>Ruta × producto: todos los productos vendidos en el mes para cada ruta, aunque esa ruta no los haya vendido (0).</summary>
public sealed record SalesRouteProductDto(int? RouteId, string RouteName, string RepNames, string Line, int? ProductId, string Sku, string Product, decimal Units, decimal Sales);

/// <summary>Una línea de venta del análisis (historial o factura/nota de crédito de Teikem).</summary>
public sealed record SalesTransactionDto(DateOnly Date, string Source, string Type, string? Number, int? ClientId, string ClientName, int? RepId, string? RepName,
    string? RouteName, int? ProductId, string Sku, string Product, string Line, string LineClass, decimal Quantity, decimal? UnitPrice, decimal Amount);

// ---------------------------------------------------------------- vista «Movimiento de productos»

public sealed record ProductMovementDto(string FromMonth, string ToMonth, DateOnly AsOf, string LastCompleteMonth, DateOnly WeeksFrom, DateOnly WeeksTo,
    int LeadTimeDays, decimal ServiceLevelZ, DateOnly? CutoffDate, IReadOnlyList<string> Months, IReadOnlyList<string> Warnings,
    decimal TotalUnits, decimal TotalSales, IReadOnlyList<ProductMovementRowDto> Products,
    IReadOnlyList<ProductMonthSeriesDto> UnitsByMonth, IReadOnlyList<ProductMonthSeriesDto> SalesByMonth, IReadOnlyList<ProductMonthSeriesDto> PurchasesByMonth,
    IReadOnlyList<ProductClientDto> TopClients);

/// <summary>
/// Un producto: categoría, marca, línea, ABC, estado (NO_SALES, STALE, NEW, RISING, FALLING, STABLE), precio, costo, margen, existencia/reservado/disponible de hoy,
/// unidades y venta del rango, % de la venta, promedio/mínimo/máximo mensual y mejor mes (meses completos desde el primero con ventas), promedio y desviación semanal
/// de 8 semanas (con su confianza NONE/LOW/SPORADIC/HIGH), cobertura en semanas y meses, saldo proyectado a 1/2/3 meses, punto de reorden (el del producto) y el
/// sugerido, tendencia, clientes distintos, facturas, cliente principal, última venta, pendiente por recibir en órdenes de compra y unidades del último mes completo.
/// Matched = false: ítem del historial que no emparejó con ningún producto (ProductId null).
/// </summary>
public sealed record ProductMovementRowDto(int? ProductId, string Sku, string Name, string? Category, string? Brand, string Line, bool Matched,
    string Abc, string Status, decimal? Price, decimal? Cost, decimal? MarginPct, decimal OnHand, decimal Reserved, decimal Available,
    decimal Units, decimal Sales, decimal? ShareOfSales, decimal? MonthlyAverage, decimal? MonthlyMin, decimal? MonthlyMax, string? BestMonth, int MonthsUsed,
    decimal? WeeklyAverage, decimal? WeeklyStdDev, int WeeksUsed, string WeeklyConfidence, decimal? CoverageWeeks, decimal? CoverageMonths,
    decimal Projected1, decimal Projected2, decimal Projected3, decimal? ReorderPoint, int? SuggestedReorderPoint, decimal? Trend,
    int Clients, int Invoices, string? TopClient, decimal? TopClientUnits, DateOnly? LastSale, decimal PendingPurchase, decimal LastMonthUnits);

/// <summary>Matriz por mes de un producto (Values en el orden de Months). Min/Max/Average solo en unidades (meses completos desde el primero con venta).</summary>
public sealed record ProductMonthSeriesDto(int? ProductId, string Sku, string Name, IReadOnlyList<decimal> Values, decimal Total, decimal? Min, decimal? Max, decimal? Average);

public sealed record ProductClientDto(int? ProductId, string Sku, string Name, int Rank, int? ClientId, string Client, decimal Units, decimal? ShareOfProduct);

// ---------------------------------------------------------------- gráficos

/// <summary>
/// Series listas para graficar: venta neta por mes (total y por línea), venta vs. cuota por representante del mes, participación por línea del mes, unidades por mes de
/// los N productos principales y cobertura (semanas) por producto.
/// </summary>
public sealed record SalesChartsDto(IReadOnlyList<string> Months, SalesChartSeriesDto TotalByMonth, IReadOnlyList<SalesChartSeriesDto> ByLineByMonth,
    string Month, IReadOnlyList<SalesRepQuotaPointDto> RepVsQuota, IReadOnlyList<SalesLineSharePointDto> LineShare,
    IReadOnlyList<SalesChartSeriesDto> TopProductUnits, IReadOnlyList<SalesCoveragePointDto> Coverage);

public sealed record SalesChartSeriesDto(string Key, string Label, IReadOnlyList<decimal> Values);
public sealed record SalesRepQuotaPointDto(int? RepId, string Rep, string Route, decimal Sales, decimal? Quota, decimal? Compliance);
public sealed record SalesLineSharePointDto(string Line, decimal Sales, decimal? Share);
public sealed record SalesCoveragePointDto(int? ProductId, string Sku, string Name, string Abc, decimal Available, decimal? WeeklyAverage, decimal? CoverageWeeks);

// ---------------------------------------------------------------- estado, sin emparejar, importación

public sealed record SalesAnalysisStatusDto(DateOnly? CutoffDate, int LeadTimeDays, decimal ServiceLevelZ, IReadOnlyList<SalesHistoryBatchDto> Batches,
    int ActiveClients, int ClientsWithoutRep, IReadOnlyList<string> BrandsWithoutLine,
    IReadOnlyList<SalesUnmatchedDto> UnmatchedClients, IReadOnlyList<SalesUnmatchedDto> UnmatchedItems, IReadOnlyList<SalesUnmatchedDto> UnmatchedPurchaseItems,
    DateOnly? HistoryFrom, DateOnly? HistoryTo, DateOnly? InvoicesFrom, DateOnly? InvoicesTo, bool Overlap, IReadOnlyList<string> Warnings);

public sealed record SalesHistoryBatchDto(Guid PublicId, string Kind, string FileName, DateOnly? DateFrom, DateOnly? DateTo, int LineCount, decimal TotalAmount,
    string Source, DateTime ImportedAtUtc, string? ImportedBy, bool IsActive, DateTime? ReplacedAtUtc);

/// <summary>Un nombre crudo del historial sin emparejar: cuántas líneas, monto y fechas.</summary>
public sealed record SalesUnmatchedDto(string Raw, int Lines, decimal Amount, DateOnly FirstDate, DateOnly LastDate);

/// <summary>Enlazar a mano un nombre crudo (Kind CLIENT o PRODUCT) a un cliente o producto de Teikem: re-enlaza las líneas y queda para las próximas importaciones.</summary>
public sealed record SalesAliasRequest(string? Kind, string? Raw, int? ClientId = null, int? ProductId = null);
public sealed record SalesAliasResultDto(string Kind, string Raw, int Relinked, int? ClientId, int? ProductId);

/// <summary>Reporte de una importación (simulada o de verdad).</summary>
public sealed record SalesHistoryImportReportDto(bool DryRun, string Company, DateOnly? CutoffDate, IReadOnlyList<SalesHistoryFileReportDto> Files, IReadOnlyList<string> Warnings);

/// <summary>
/// Lo de un archivo: líneas leídas, guardadas (o que se guardarían) y omitidas (cantidad 0 en compras, duplicados Item Receipt/Bill), por clase, rango de fechas, total vs.
/// el «TOTAL» del reporte, clientes e ítems sin emparejar, totales por mes para cuadrar, y qué pasa con lo ya importado (Action NEW | REPLACE_SAME_FILE | REPLACE_RANGE, ReplacedLines).
/// </summary>
public sealed record SalesHistoryFileReportDto(string Kind, string FileName, int LinesRead, int LinesSaved, int ZeroQtySkipped, int DuplicatesSkipped,
    IReadOnlyDictionary<string, int> LinesByClass, IReadOnlyDictionary<string, decimal> AmountByClass, DateOnly? DateFrom, DateOnly? DateTo,
    decimal TotalAmount, decimal ReportTotal, int MatchedClients, int MatchedItems,
    IReadOnlyList<SalesUnmatchedDto> UnmatchedClients, IReadOnlyList<SalesUnmatchedDto> UnmatchedItems, IReadOnlyList<SalesMonthTotalDto> ByMonth,
    string Action, int ReplacedLines, IReadOnlyList<string> Warnings);

public sealed record SalesMonthTotalDto(string Month, int Lines, decimal Quantity, decimal Amount, decimal ProductAmount);
