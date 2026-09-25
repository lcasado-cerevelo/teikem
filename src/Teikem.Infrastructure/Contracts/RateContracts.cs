namespace Teikem.Infrastructure.Contracts;

// Lote 2 (P4): tarifas por servicio y pieza extra del contrato (RateComponent/RateTier) y cotización multi-línea.

/// <summary>Tarifa "por servicio": monto fijo por envío para un (servicio, tipo de paquete). IsCurrent = vigente en la fecha consultada.</summary>
public sealed record RateRowDto(
    int Id, string ServiceType, string ServiceTypeLabel, string PackageType, string PackageTypeLabel,
    decimal Rate, DateOnly EffectiveFrom, DateOnly? EffectiveTo, bool IsCurrent);

/// <summary>Tramo de pieza extra: de FromUnit a ToUnit (null = abierto) pagan Rate por pieza.</summary>
public sealed record TierDto(int Id, int FromUnit, int? ToUnit, decimal Rate, DateOnly EffectiveFrom, DateOnly? EffectiveTo, bool IsCurrent);

/// <summary>Componente "pieza extra" de un (servicio, tipo de paquete) con sus tramos.</summary>
public sealed record ExtraPieceRowDto(
    int ComponentId, string ServiceType, string ServiceTypeLabel, string PackageType, string PackageTypeLabel,
    DateOnly EffectiveFrom, DateOnly? EffectiveTo, bool IsCurrent, IReadOnlyList<TierDto> Tiers);

/// <summary>Tarifas del contrato en la fecha AsOf (o el historial completo con includeHistory).</summary>
public sealed record RateComponentsDto(Guid ContractPublicId, DateOnly AsOf, IReadOnlyList<RateRowDto> PerService, IReadOnlyList<ExtraPieceRowDto> ExtraPiece);

/// <summary>
/// Respuesta de las escrituras sobre un componente (alta, nueva versión, cierre). Kind = PER_SERVICE | EXTRA_PIECE;
/// Rate solo aplica a PER_SERVICE; Tiers solo a EXTRA_PIECE.
/// </summary>
public sealed record RateComponentDto(
    int Id, string Kind, string ServiceType, string ServiceTypeLabel, string PackageType, string PackageTypeLabel,
    decimal? Rate, DateOnly EffectiveFrom, DateOnly? EffectiveTo, bool IsCurrent, IReadOnlyList<TierDto> Tiers);

/// <summary>
/// Alta de componente. Kind PER_SERVICE exige Rate (>= 0); EXTRA_PIECE no lleva monto (se agregan tramos después).
/// Servicio y paquete son inmutables (R25): para cambiarlos se cierra el componente y se crea otro.
/// EffectiveFrom vacío = hoy.
/// </summary>
public sealed record RateComponentCreateRequest(string Kind, string ServiceType, string PackageType, decimal? Rate = null, DateOnly? EffectiveFrom = null);

/// <summary>Nueva versión de una tarifa por servicio: cierra la vigente en EffectiveFrom y abre otra con Rate (R28). EffectiveFrom vacío = hoy.</summary>
public sealed record RateUpdateRequest(decimal Rate, DateOnly? EffectiveFrom = null);

/// <summary>Cierre (quitar conservando historial). EffectiveTo vacío = hoy.</summary>
public sealed record RateCloseRequest(DateOnly? EffectiveTo = null);

/// <summary>Alta de tramo: FromUnit >= 2, ToUnit vacío = abierto, Rate por pieza >= 0. EffectiveFrom vacío = hoy.</summary>
public sealed record TierUpsertRequest(int FromUnit, int? ToUnit, decimal Rate, DateOnly? EffectiveFrom = null);

/// <summary>Nueva versión de un tramo: null = sin cambio; ClearToUnit=true deja el tramo abierto hacia arriba. EffectiveFrom vacío = hoy.</summary>
public sealed record TierPatchRequest(int? FromUnit = null, int? ToUnit = null, bool? ClearToUnit = null, decimal? Rate = null, DateOnly? EffectiveFrom = null);

/// <summary>Línea de cotización: una por (servicio, tipo de paquete) con el número total de piezas (>= 1).</summary>
public sealed record RateQuoteLineRequest(string ServiceType, string PackageType, int Pieces);

/// <summary>Cotización del contrato vigente del cliente en AsOf (vacío = hoy). CodAmount = monto COD a cobrar (para el cargo por ciento).</summary>
public sealed record RateQuoteRequest(Guid ClientPublicId, IList<RateQuoteLineRequest> Lines, decimal? CodAmount = null, DateOnly? AsOf = null);

/// <summary>
/// Línea cotizada. BaseSource/ExtraSource = CONTRACT | GENERIC | NONE (R35). LineTotal = base + extra; la primera línea
/// incluye además despacho y COD (se cobran una sola vez por orden, R31–R33).
/// </summary>
public sealed record QuoteLineDto(
    string ServiceType, string PackageType, int Pieces,
    decimal? BaseRate, string BaseSource, decimal ExtraPieces, string ExtraSource, decimal LineTotal);

/// <summary>Resultado de la cotización. ContractPublicId null = sin contrato vigente (solo tarifas genéricas). RateComponentIds = filas usadas (R30).</summary>
public sealed record RateQuoteDto(
    Guid? ContractPublicId, string? ContractStatus, DateOnly AsOf,
    IReadOnlyList<QuoteLineDto> Lines, decimal DispatchFee, decimal CodFee, decimal Total, IReadOnlyList<int> RateComponentIds);
