namespace Teikem.Infrastructure.Contracts;

/// <summary>Un componente del kit: el producto y cuántas unidades lleva cada kit.</summary>
/// <remarks>Lote 230: NotSubstitutable = la pieza NO se puede sustituir en una orden de venta (por omisión toda pieza es sustituible).</remarks>
public sealed record KitComponentDto(Guid ProductPublicId, string Sku, string Name, decimal Quantity, int SortOrder, bool IsActive, bool NotSubstitutable = false);

/// <summary>«Se pueden armar N» en un almacén: el menor cociente entre lo disponible para vender de cada componente y su cantidad por kit.</summary>
public sealed record KitAvailabilityDto(Guid WarehousePublicId, string WarehouseCode, int CanBuild);

/// <summary>El kit: si el producto es kit, si se puede ajustar al pedir, sus componentes y cuántos se pueden armar por almacén.</summary>
public sealed record KitDto(Guid ProductPublicId, string Sku, string Name, bool IsKit, bool Adjustable, IReadOnlyList<KitComponentDto> Components,
    IReadOnlyList<KitAvailabilityDto> Availability);

/// <summary>
/// Una pieza del kit (editor del kit, kit de una oferta) o de la línea de un kit en una orden de venta.
/// Lote 230: NotSubstitutable (solo al definir el kit: editor del kit y kit de una oferta) = la pieza no se puede sustituir; null = la que ya tenía (o sustituible si es
/// nueva). SubstitutesProductPublicId (solo en las piezas de una línea de orden de venta) = esta pieza SUSTITUYE a esa pieza original del kit (misma categoría exacta,
/// mismo precio de venta, misma cantidad); el editor del kit lo ignora.
/// </summary>
public sealed record KitComponentRequest(Guid? ProductPublicId, decimal? Quantity, bool? NotSubstitutable = null, Guid? SubstitutesProductPublicId = null);

/// <summary>
/// PUT /products/{id}/kit: convierte el producto en kit (IsKit = true, con sus componentes y si es ajustable al pedir) o lo vuelve un producto normal (IsKit = false).
/// Los componentes reemplazan a los anteriores.
/// </summary>
public sealed record KitSetRequest(bool? IsKit, bool? Adjustable = null, IReadOnlyList<KitComponentRequest>? Components = null);

// ---------------------------------------------------------------- Lote 230 — sustitución de piezas

/// <summary>Un producto que puede sustituir a la pieza: misma categoría exacta y mismo precio de venta (propio del dueño del kit, activo, no kit).</summary>
public sealed record KitSubstituteDto(Guid ProductPublicId, string Sku, string Name, string? Brand, string? Model, decimal? SalePrice);

/// <summary>
/// Una pieza del kit con sus sustitutos válidos. NotSubstitutable = el kit la marcó «no sustituible» (sin sustitutos). CategoryPath = «Guantes / Nitrilo».
/// Substitutes vacío = no hay con qué cambiarla (sin categoría, sin precio o sin productos iguales).
/// </summary>
public sealed record KitComponentSubstitutesDto(Guid ProductPublicId, string Sku, string Name, decimal Quantity, bool NotSubstitutable, int? CategoryId, string? CategoryPath,
    decimal? SalePrice, IReadOnlyList<KitSubstituteDto> Substitutes);

/// <summary>GET /sales-orders/kits/{kitPublicId}/substitutes: las piezas del kit (del catálogo) y con qué se puede cambiar cada una en una orden de venta.</summary>
public sealed record KitSubstitutesDto(Guid KitPublicId, string Sku, string Name, bool Adjustable, IReadOnlyList<KitComponentSubstitutesDto> Components);
