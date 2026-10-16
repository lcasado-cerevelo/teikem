namespace Teikem.Infrastructure.Contracts;

/// <summary>Un componente del kit: el producto y cuántas unidades lleva cada kit.</summary>
public sealed record KitComponentDto(Guid ProductPublicId, string Sku, string Name, decimal Quantity, int SortOrder, bool IsActive);

/// <summary>«Se pueden armar N» en un almacén: el menor cociente entre lo disponible para vender de cada componente y su cantidad por kit.</summary>
public sealed record KitAvailabilityDto(Guid WarehousePublicId, string WarehouseCode, int CanBuild);

/// <summary>El kit: si el producto es kit, si se puede ajustar al pedir, sus componentes y cuántos se pueden armar por almacén.</summary>
public sealed record KitDto(Guid ProductPublicId, string Sku, string Name, bool IsKit, bool Adjustable, IReadOnlyList<KitComponentDto> Components,
    IReadOnlyList<KitAvailabilityDto> Availability);

public sealed record KitComponentRequest(Guid? ProductPublicId, decimal? Quantity);

/// <summary>
/// PUT /products/{id}/kit: convierte el producto en kit (IsKit = true, con sus componentes y si es ajustable al pedir) o lo vuelve un producto normal (IsKit = false).
/// Los componentes reemplazan a los anteriores.
/// </summary>
public sealed record KitSetRequest(bool? IsKit, bool? Adjustable = null, IReadOnlyList<KitComponentRequest>? Components = null);
