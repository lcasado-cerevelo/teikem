namespace Teikem.Infrastructure.Contracts;

// ---------------------------------------------------------------- Lote 247 (Lote C) — precios especiales por cliente y por cadena

/// <summary>
/// Alta de un precio especial del cliente o de la cadena: el producto o kit, el precio (ANTES del IVU: las exenciones del cliente se aplican después) y su vigencia
/// («desde» obligatorio; «hasta» opcional, y al crear no puede estar en el pasado). Si el producto ya tiene precio en la lista dedicada de esas fechas, se le cambia.
/// </summary>
public sealed record SpecialPriceRequest(Guid? ProductPublicId = null, decimal? Price = null, DateOnly? ValidFrom = null, DateOnly? ValidTo = null);

/// <summary>Cambiar un precio especial (lo que no viene no cambia). Cambiar las fechas lo pasa a la lista dedicada de esas fechas; ClearValidTo quita el «hasta».</summary>
public sealed record SpecialPricePatchRequest(decimal? Price = null, DateOnly? ValidFrom = null, DateOnly? ValidTo = null, bool? ClearValidTo = null);

/// <summary>
/// Un precio especial (renglón de una lista dedicada). State = DRAFT, SCHEDULED, ACTIVE o EXPIRED; DaysToExpire null = sin «hasta». GeneralListPrice = el de la lista
/// general vigente y AboveGeneral = el especial sale más caro (se cobra el más bajo). ChargedPrice / ChargedSource / ChargedSourceName = lo que se cobra HOY por ese
/// producto y de dónde sale (CLIENT_LIST, CHAIN_LIST, OFFER, GENERAL_LIST, PRODUCT o NONE).
/// </summary>
public sealed record SpecialPriceRowDto(int ItemId, Guid PriceListPublicId, Guid ProductPublicId, string Sku, string ProductName, string? Brand, bool IsKit, decimal Price,
    DateOnly ValidFrom, DateOnly? ValidTo, string State, int? DaysToExpire, decimal? GeneralListPrice, bool AboveGeneral, decimal? ProductSalePrice, bool ProductIsActive,
    decimal? ChargedPrice = null, string? ChargedSource = null, string? ChargedSourceName = null);

/// <summary>Una lista normal (no dedicada) que alcanza al cliente: por él o por su cadena (MatchedBy = CLIENT o CHAIN).</summary>
public sealed record SpecialPriceListRefDto(Guid PriceListPublicId, string Name, string State, int ItemCount, string MatchedBy);

/// <summary>
/// «Precios especiales» de la ficha del cliente o de la cadena (OwnerKind = CLIENT o CHAIN). Rows = los suyos (se editan). En la ficha del cliente: ChainPublicId /
/// ChainName = su cadena, ChainRows = los precios especiales de la cadena (solo lectura) y ChainRowCount cuántos son, y ClientLists = las listas normales que lo
/// alcanzan. Warnings = avisos (SPECIAL_ABOVE_GENERAL).
/// </summary>
public sealed record SpecialPricesDto(string OwnerKind, Guid OwnerPublicId, string OwnerName, bool OwnerIsActive, Guid? ChainPublicId, string? ChainName, int ChainRowCount,
    IReadOnlyList<SpecialPriceRowDto> Rows, IReadOnlyList<SpecialPriceRowDto> ChainRows, IReadOnlyList<SpecialPriceListRefDto> ClientLists,
    IReadOnlyList<PriceListWarningDto> Warnings);
