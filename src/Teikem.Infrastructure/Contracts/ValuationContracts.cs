namespace Teikem.Infrastructure.Contracts;

// Lote 148 — valuación del inventario. Firma posicional FIJA.

/// <summary>Una fila del reporte: un producto con existencia, lo que hay en mano, su costo unitario resultante (null si no hay costo en ningún lado) y su valor. UnvaluedQty = unidades sin costo.</summary>
public sealed record ValuationRowDto(Guid ProductPublicId, string Sku, string ProductName, Guid? ClientPublicId, string? ClientName, decimal OnHand, decimal? UnitCost, decimal Value,
    decimal UnvaluedQty, decimal? ProductCost);

/// <summary>Reporte «Valor del inventario»: Total/Skip/Take de las filas; TotalValue/TotalQty/UnvaluedQty de TODO el filtro; Method = método de la compañía (AVERAGE | LAYERS).</summary>
public sealed record ValuationReportDto(int Total, int Skip, int Take, IReadOnlyList<ValuationRowDto> Items, decimal TotalValue, decimal TotalQty, decimal UnvaluedQty, string Method);
