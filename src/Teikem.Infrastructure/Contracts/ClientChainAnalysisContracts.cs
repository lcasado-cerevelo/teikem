namespace Teikem.Infrastructure.Contracts;

// Lote 245 (Lote B del plan) — análisis de ventas por cadena (/api/v1/sales-analysis/by-chain y by-chain/series). Venta = producto + descuentos (envío y otros
// cargos aparte), con las reglas del Lote 241 (fecha de corte, kits repartidos, notas de crédito restan). La venta se atribuye a la cadena ACTUAL del cliente,
// también la del historial importado. Meses «AAAA-MM». Importes redondeados a 2 decimales (AwayFromZero) solo al final.

/// <summary>
/// La vista «Por cadena» de un mes contra otro: Rows = las cadenas (activas, y las inactivas con venta) con su desglose por cliente; NoChain = los clientes sin
/// cadena y las líneas del historial sin emparejar (null cuando se pide una sola cadena); Reconciliation = suma de cadenas + sin cadena contra la venta neta del mes.
/// </summary>
public sealed record ClientChainSalesDto(string Month, string CompareMonth, DateOnly? CutoffDate, IReadOnlyList<ClientChainSalesRowDto> Rows,
    ClientChainSalesRowDto? NoChain, ClientChainSalesTotalsDto Totals, ClientChainReconciliationDto Reconciliation);

/// <summary>Una cadena en la vista (ChainPublicId null = «Sin cadena»). ShareOfNet = participación en la venta neta del mes (null si es 0).</summary>
public sealed record ClientChainSalesRowDto(Guid? ChainPublicId, string ChainName, bool IsActive, int ClientCount, int ClientsWithSales, decimal CompareSales,
    decimal Sales, decimal Change, decimal? ChangePct, decimal? ShareOfNet, IReadOnlyList<ClientChainSalesClientDto> Clients);

/// <summary>Un cliente dentro de una cadena (ClientPublicId null = nombre del historial sin emparejar). ShareOfChain = participación en la venta de la cadena.</summary>
public sealed record ClientChainSalesClientDto(Guid? ClientPublicId, string ClientName, decimal CompareSales, decimal Sales, decimal Change, decimal? ChangePct,
    decimal? ShareOfChain);

/// <summary>Totales de la vista.</summary>
public sealed record ClientChainSalesTotalsDto(decimal CompareSales, decimal Sales, decimal Change, decimal? ChangePct);

/// <summary>Conciliación: ChainsTotal + NoChain = MonthNet; Difference = MonthNet − (ChainsTotal + NoChain), debe ser 0.</summary>
public sealed record ClientChainReconciliationDto(decimal ChainsTotal, decimal NoChain, decimal MonthNet, decimal Difference);

/// <summary>Venta por mes de los clientes de una cadena (para su ficha) y sus clientes principales en el rango.</summary>
public sealed record ClientChainSeriesDto(Guid ChainPublicId, string ChainName, string FromMonth, string ToMonth, IReadOnlyList<ClientChainMonthDto> Months,
    IReadOnlyList<ClientChainTopClientDto> TopClients);

/// <summary>Venta neta de un mes.</summary>
public sealed record ClientChainMonthDto(string Month, decimal Sales);

/// <summary>Un cliente principal de la cadena en el rango, con su participación (null si la venta del rango es 0).</summary>
public sealed record ClientChainTopClientDto(Guid ClientPublicId, string Code, string Name, decimal Sales, decimal? Share);
