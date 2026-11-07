namespace Teikem.Infrastructure.Contracts;

// ---------------------------------------------------------------- Lote 221 — listas de precios y ofertas

/// <summary>Alta o edición (PATCH: lo que no viene no cambia) de una lista de precios. ClearValidTo quita la fecha «hasta» (sin fin).</summary>
public sealed record PriceListRequest(string? Name = null, DateOnly? ValidFrom = null, DateOnly? ValidTo = null, bool? ClearValidTo = null, bool? AppliesToAll = null,
    string? Notes = null);

/// <summary>Un renglón a agregar o cambiar: el producto o kit y su precio.</summary>
public sealed record PriceListItemRequest(Guid? ProductPublicId = null, decimal? Price = null);

/// <summary>Renglones a agregar o cambiar (si el producto ya está en la lista, se le cambia el precio). Los que no vienen no se tocan.</summary>
public sealed record PriceListItemsRequest(IReadOnlyList<PriceListItemRequest>? Items = null);

/// <summary>Cambiar el precio de un renglón.</summary>
public sealed record PriceListItemPriceRequest(decimal? Price = null);

/// <summary>Clientes a agregar a una lista que no es general (los que ya están se ignoran).</summary>
public sealed record PriceListClientsRequest(IReadOnlyList<Guid>? ClientPublicIds = null);

/// <summary>
/// Una lista en la lista de listas. State = DRAFT (borrador), SCHEDULED (publicada, empieza más adelante), ACTIVE (vigente hoy) o EXPIRED (vencida); sale de IsPublished
/// y de las fechas.
/// </summary>
public sealed record PriceListListItemDto(Guid PublicId, string Name, DateOnly ValidFrom, DateOnly? ValidTo, bool IsPublished, string State, bool AppliesToAll,
    int ItemCount, int ClientCount, bool IsActive, DateTime CreatedAtUtc, DateTime? UpdatedAtUtc);

/// <summary>Renglón: el producto o kit, su precio de lista (el de venta del producto, para comparar) y la foto opcional.</summary>
public sealed record PriceListItemDto(int Id, Guid ProductPublicId, string Sku, string ProductName, string? Brand, bool IsKit, bool IsPromotion, decimal Price,
    decimal? ProductSalePrice, Guid? ImagePublicId, bool ProductIsActive);

public sealed record PriceListClientDto(Guid ClientPublicId, string Code, string Name);

public sealed record PriceListDto(Guid PublicId, string Name, DateOnly ValidFrom, DateOnly? ValidTo, bool IsPublished, string State, bool AppliesToAll, string? Notes,
    bool IsActive, IReadOnlyList<PriceListItemDto> Items, IReadOnlyList<PriceListClientDto> Clients, DateTime CreatedAtUtc, string? CreatedByName,
    DateTime? UpdatedAtUtc, string? UpdatedByName);

/// <summary>La oferta de un kit: vigencia (opcional en ambos lados) y la marca «es promoción». Todo se reemplaza (null = sin esa fecha).</summary>
public sealed record ProductPromoRequest(bool? IsPromotion = null, DateOnly? PromoFrom = null, DateOnly? PromoTo = null);

/// <summary>
/// La oferta de un producto: si es kit, si es promoción, su vigencia, su foto y si se puede vender hoy (InEffect). Price = el precio de venta del kit (el de la lista
/// sale al pedir, por cliente).
/// </summary>
public sealed record ProductPromoDto(Guid ProductPublicId, string Sku, string Name, string? Brand, bool IsKit, bool IsActive, bool IsPromotion, DateOnly? PromoFrom,
    DateOnly? PromoTo, bool InEffect, Guid? ImagePublicId, decimal? SalePrice);

/// <summary>
/// Un producto o kit del catálogo del pedido con su precio ya resuelto para el cliente: PriceSource = CLIENT_LIST (lista del cliente), GENERAL_LIST (lista general),
/// PRODUCT (precio de venta del producto) o NONE (no tiene precio: no se puede pedir sin sales.price). ImagePublicId = la foto de la oferta o la del renglón de la lista.
/// </summary>
public sealed record SalesCatalogItemDto(Guid ProductPublicId, string Sku, string Name, string? Brand, bool IsKit, bool KitAdjustable, bool IsPromotion,
    DateOnly? PromoFrom, DateOnly? PromoTo, decimal? Price, string PriceSource, Guid? PriceListPublicId, string? PriceListName, decimal? SalePrice, Guid? ImagePublicId);

/// <summary>Una marca del catálogo (Brand null = productos sin marca, al final): sus ofertas vigentes arriba y la lista completa de la marca debajo.</summary>
public sealed record SalesCatalogBrandDto(string? Brand, IReadOnlyList<SalesCatalogItemDto> Offers, IReadOnlyList<SalesCatalogItemDto> Products);

/// <summary>El catálogo del pedido: por marca, a la fecha de hoy y con los precios del cliente (sin cliente: los de la lista general).</summary>
public sealed record SalesCatalogDto(Guid? ClientPublicId, DateOnly Date, IReadOnlyList<SalesCatalogBrandDto> Brands);
