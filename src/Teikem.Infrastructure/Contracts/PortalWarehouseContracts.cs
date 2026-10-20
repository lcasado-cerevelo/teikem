namespace Teikem.Infrastructure.Contracts;

// Lote 153 — portal del cliente de almacén (lectura). DTOs propios y mínimos: sin posiciones internas, usuarios, costos del operador ni datos de otros clientes. Firma posicional FIJA.

/// <summary>
/// Qué ofrece la compañía de la sesión a este acceso: CompanyName, Warehouse = tiene el módulo de almacén (el menú muestra Inventario, Productos y Movimientos) y CanSeeValue = su rol puede ver el valor de su
/// inventario (ADMIN y OPERADOR del cliente; el de solo lectura no).
/// </summary>
public sealed record PortalCapabilitiesDto(string CompanyName, bool Warehouse, bool CanSeeValue);

/// <summary>Una fila de existencias: producto y lote en un almacén, sumadas todas las posiciones (el cliente no ve la estantería).</summary>
public sealed record PortalStockItemDto(Guid ProductPublicId, string Sku, string ProductName, Guid WarehousePublicId, string WarehouseCode, string WarehouseName, string? LotNumber,
    DateOnly? ExpiryDate, decimal OnHand, decimal Reserved, decimal Available);

public sealed record PortalStockPageDto(int Total, int Skip, int Take, decimal TotalOnHand, decimal TotalAvailable, IReadOnlyList<PortalStockItemDto> Items);

/// <summary>Un producto del cliente con lo que hay en total (todos los almacenes).</summary>
public sealed record PortalProductDto(Guid PublicId, string Sku, string Name, string? Barcode, string? Brand, string? Category, string TrackingType, bool IsActive, decimal OnHand, decimal Available);

public sealed record PortalProductPageDto(int Total, int Skip, int Take, IReadOnlyList<PortalProductDto> Items);

/// <summary>Un movimiento del Kárdex del cliente: Quantity CON signo (entra +, sale −). Sin posición, sin usuario.</summary>
public sealed record PortalMovementDto(long Id, DateTime CreatedAtUtc, string TypeCode, string Type, Guid ProductPublicId, string Sku, string ProductName, decimal Quantity,
    string? WarehouseCode, string? LotNumber, string? Reference, string? Reason);

public sealed record PortalMovementPageDto(int Total, int Skip, int Take, IReadOnlyList<PortalMovementDto> Items);
