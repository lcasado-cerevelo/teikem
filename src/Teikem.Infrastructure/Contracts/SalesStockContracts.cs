namespace Teikem.Infrastructure.Contracts;

// Lote 235 — lo disponible al escoger productos en una venta: GET /api/v1/sales-orders/stock (una sola consulta para todo el catálogo y todos los almacenes activos).

/// <summary>Un almacén activo de la compañía. IsMain = el almacén principal (el marcado o, si ninguno lo está, el activo más antiguo): el que la venta escoge por omisión.</summary>
public sealed record SalesStockWarehouseDto(Guid PublicId, string Code, string Name, bool IsMain);

/// <summary>Lo disponible para vender de un producto en un almacén (solo viaja si es mayor que cero).</summary>
public sealed record SalesStockQtyDto(Guid WarehousePublicId, decimal Available);

/// <summary>Un producto con disponible en algún almacén activo. Para un kit que se arma al pedir, Available = kits completos que se pueden armar ahí.</summary>
public sealed record SalesStockProductDto(Guid ProductPublicId, IReadOnlyList<SalesStockQtyDto> Stock);

/// <summary>
/// Los almacenes activos (por código) y lo disponible para vender por producto y almacén. Un producto que no aparece no tiene disponible en ningún almacén (0).
/// </summary>
public sealed record SalesStockDto(IReadOnlyList<SalesStockWarehouseDto> Warehouses, IReadOnlyList<SalesStockProductDto> Products);
