namespace Teikem.Infrastructure.Contracts;

// ---------------------------------------------------------------- Lote 229 — ofertas, kits de oferta, fotos del producto y avisos de precio

/// <summary>Alta o edición (PATCH: lo que no viene no cambia) de una oferta. ValidTo null = sin fecha de fin (la oferta no vence); ClearValidTo la quita al editar.</summary>
public sealed record OfferRequest(string? Name = null, DateOnly? ValidFrom = null, DateOnly? ValidTo = null, bool? ClearValidTo = null, bool? AppliesToAll = null,
    string? Notes = null);

/// <summary>Un renglón a agregar o cambiar: el producto o kit y su precio de oferta.</summary>
public sealed record OfferItemRequest(Guid? ProductPublicId = null, decimal? Price = null);

/// <summary>Renglones a agregar o cambiar (si el producto ya está en la oferta, se le cambia el precio). Los que no vienen no se tocan.</summary>
public sealed record OfferItemsRequest(IReadOnlyList<OfferItemRequest>? Items = null);

/// <summary>Cambiar el precio de oferta de un renglón.</summary>
public sealed record OfferItemPriceRequest(decimal? Price = null);

/// <summary>Clientes a agregar a una oferta que no es para todos (los que ya están se ignoran).</summary>
public sealed record OfferClientsRequest(IReadOnlyList<Guid>? ClientPublicIds = null);

/// <summary>
/// Crear un kit PARA la oferta (queda atado a ella y nace no ajustable): SKU y nombre del producto nuevo, sus piezas (las mismas de PUT /products/{id}/kit, con la
/// marca «no sustituible» si el contrato de piezas la trae) y su precio de oferta (el renglón). Brand y CategoryId opcionales (sin categoría: la de la primera pieza);
/// SalePrice = el precio de venta del kit (opcional; por omisión, el de la oferta).
/// </summary>
public sealed record OfferKitRequest(string? Sku = null, string? Name = null, decimal? Price = null, IReadOnlyList<KitComponentRequest>? Components = null,
    string? Brand = null, int? CategoryId = null, decimal? SalePrice = null);

/// <summary>Cambiar un kit de la oferta: el nombre y/o sus piezas (las piezas, si vienen, reemplazan a las anteriores).</summary>
public sealed record OfferKitUpdateRequest(string? Name = null, IReadOnlyList<KitComponentRequest>? Components = null);

/// <summary>
/// Aviso al armar la oferta (no es error): CHEAPER_OFFER = otra oferta publicada, en fechas y clientes que se cruzan, trae el producto igual o más barato;
/// OFFER_ABOVE_GENERAL = el precio de oferta es más caro que la lista general vigente. En ambos casos al pedir gana el precio más bajo.
/// </summary>
public sealed record OfferWarningDto(string Code, string Message, Guid ProductPublicId, string Sku, Guid? OtherOfferPublicId, string? OtherOfferName, decimal? OtherPrice);

/// <summary>
/// Una oferta en la lista de ofertas. HasEndDate = false → «sin fecha de fin» (ValidTo null: no vence); ValidityText la vigencia en palabras. State = DRAFT, SCHEDULED,
/// ACTIVE o EXPIRED (sale de IsPublished y de las fechas); InEffect = se aplica hoy (publicada, activa y vigente).
/// </summary>
public sealed record OfferListItemDto(Guid PublicId, string Name, DateOnly ValidFrom, DateOnly? ValidTo, bool HasEndDate, string ValidityText, bool IsPublished,
    string State, bool InEffect, bool AppliesToAll, int ItemCount, int ClientCount, Guid? ImagePublicId, bool IsActive, DateTime CreatedAtUtc, DateTime? UpdatedAtUtc);

/// <summary>
/// Renglón de la oferta. IsOfferKit = kit creado para ESTA oferta (Kit trae sus piezas). ImagePublicId = la foto propia del renglón (null = no tiene);
/// EffectiveImagePublicId = la que se muestra (renglón → producto → primera pieza con foto si es kit). GeneralListPrice = el de la lista general vigente (para comparar).
/// </summary>
public sealed record OfferItemDto(int Id, Guid ProductPublicId, string Sku, string ProductName, string? Brand, bool IsKit, bool IsOfferKit, bool KitAdjustable, decimal Price,
    decimal? ProductSalePrice, decimal? GeneralListPrice, Guid? ImagePublicId, Guid? EffectiveImagePublicId, bool ProductIsActive, KitDto? Kit);

public sealed record OfferClientDto(Guid ClientPublicId, string Code, string Name);

/// <summary>La oferta completa, con sus renglones, clientes y los avisos (Warnings) de choques con otras ofertas o con la lista general.</summary>
public sealed record OfferDto(Guid PublicId, string Name, DateOnly ValidFrom, DateOnly? ValidTo, bool HasEndDate, string ValidityText, bool IsPublished, string State,
    bool InEffect, bool AppliesToAll, string? Notes, Guid? ImagePublicId, bool IsActive, IReadOnlyList<OfferItemDto> Items, IReadOnlyList<OfferClientDto> Clients,
    IReadOnlyList<OfferWarningDto> Warnings, DateTime CreatedAtUtc, string? CreatedByName, DateTime? UpdatedAtUtc, string? UpdatedByName);

/// <summary>Respuesta de crear un kit para la oferta: el producto nuevo y la oferta como queda.</summary>
public sealed record OfferKitCreatedDto(Guid ProductPublicId, string Sku, OfferDto Offer);

/// <summary>
/// Aviso de precio de un producto (no es error): TWO_OFFERS (está en dos ofertas vigentes a la vez para el cliente: se aplica la más barata, Offer*) u
/// OFFER_ABOVE_GENERAL (la oferta sale más cara que la lista general: se aplica el precio más bajo). Message = el texto para mostrar.
/// </summary>
public sealed record SalesPriceNoticeDto(string Code, string Message, Guid ProductPublicId, string Sku, Guid? OfferPublicId, string? OfferName,
    Guid? OtherOfferPublicId, string? OtherOfferName);

/// <summary>
/// De dónde salió hoy el precio de un producto de la orden (solo en la respuesta de crear o editar el borrador con líneas): PriceSource = OFFER, CLIENT_LIST,
/// GENERAL_LIST, PRODUCT o NONE; la oferta o la lista; y los avisos.
/// </summary>
public sealed record SalesLinePriceDto(Guid ProductPublicId, string Sku, decimal? ResolvedPrice, string PriceSource, Guid? OfferPublicId, string? OfferName,
    Guid? PriceListPublicId, string? PriceListName, IReadOnlyList<SalesPriceNoticeDto> Notices);

/// <summary>La foto del producto: la propia (ImagePublicId; null = no tiene) y la que se muestra (EffectiveImagePublicId: si es un kit sin foto, la de sus piezas).</summary>
public sealed record ProductImageDto(Guid ProductPublicId, string Sku, bool IsKit, Guid? ImagePublicId, Guid? EffectiveImagePublicId);
