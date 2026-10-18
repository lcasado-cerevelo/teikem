using Teikem.Domain.Constants;

namespace Teikem.Domain.Billing;

/// <summary>Códigos de cada concepto del flete (el cargo de la factura sale del ChargeType que les asigna <see cref="FreightRules.ChargeTypeOf"/>).</summary>
public static class FreightComponents
{
    public const string BaseFreight = "BASE_FREIGHT";
    public const string ExtraPieces = "EXTRA_PIECES";
    public const string DispatchFee = "DISPATCH_FEE";
    public const string CodFee = "COD_FEE";
    public const string SpecialService = "SPECIAL_SERVICE";
    /// <summary>Una sola línea con el total cotizado: se usa cuando el desglose de hoy ya no coincide con lo cotizado a la orden.</summary>
    public const string Quoted = "QUOTED";
}

/// <summary>
/// Lote 74 (P5) — reglas puras del flete de las órdenes de transporte (sin EF ni servicios). Cuándo se factura una orden según el modelo de cobro del contrato y qué tipo de cargo lleva
/// cada concepto. Los mensajes son públicos porque el manual (capítulo 19) y la FAQ los citan tal cual.
/// </summary>
public static class FreightRules
{
    public const string BillingModelPickup = "BY_PICKUP";
    public const string BillingModelDelivery = "BY_DELIVERY";
    public const string BillingModelMixed = "MIXED";

    /// <summary>Estatus de la orden que ya cuentan como «recogida» (de Recogida en adelante en el flujo normal, incluida «En almacén» de los servicios de solo recogida; la fallida y la cancelada nunca).</summary>
    public static readonly string[] PickedUpOrLater =
    {
        OrderStatuses.Pickup, OrderStatuses.Unassigned, OrderStatuses.Assigned, OrderStatuses.InTransit, OrderStatuses.Delivered, OrderStatuses.InWarehouse,
    };

    public static readonly string[] DeliveredOnly = { OrderStatuses.Delivered };

    /// <summary>
    /// Estatus en que la orden ya se puede facturar según el modelo de cobro del contrato. Por recogido: desde que se recoge. Por entrega y mixto (y sin modelo o sin contrato): solo
    /// entregada. Mixto cobra al entregar porque necesita las dos cosas (recogida y entrega) cumplidas.
    /// </summary>
    public static IReadOnlyList<string> BillableStatuses(string? billingModel)
        => string.Equals(billingModel?.Trim(), BillingModelPickup, StringComparison.OrdinalIgnoreCase) ? PickedUpOrLater : DeliveredOnly;

    /// <summary>
    /// ChargeType de cada concepto: la tarifa base, las piezas extra y el servicio especial son FREIGHT (flete); el cargo por despacho es HANDLING (manejo) y el cargo por COD es SURCHARGE (recargo).
    /// Así la regla de impuesto por tipo de cargo (flete y manejo gravables o no) se decide por separado.
    /// </summary>
    public static string ChargeTypeOf(string component) => component switch
    {
        FreightComponents.DispatchFee => "HANDLING",
        FreightComponents.CodFee => "SURCHARGE",
        _ => "FREIGHT",
    };

    public static string Describe(string orderNumber, string component, string? serviceType, string? packageType, string? special = null) => component switch
    {
        FreightComponents.BaseFreight => $"{orderNumber} · Flete {serviceType}/{packageType}".Trim(),
        FreightComponents.ExtraPieces => $"{orderNumber} · Piezas extra {serviceType}/{packageType}".Trim(),
        FreightComponents.DispatchFee => $"{orderNumber} · Cargo por despacho",
        FreightComponents.CodFee => $"{orderNumber} · Cargo por COD",
        FreightComponents.SpecialService => $"{orderNumber} · Servicio especial {special}".Trim(),
        _ => $"{orderNumber} · Flete (monto cotizado)",
    };

    /// <summary>El desglose coincide con lo cotizado a la orden cuando la suma de sus conceptos (a 4 decimales) es el monto congelado en la orden.</summary>
    public static bool MatchesQuote(decimal breakdownTotal, decimal quotedAmount)
        => Math.Round(breakdownTotal, 4, MidpointRounding.AwayFromZero) == Math.Round(quotedAmount, 4, MidpointRounding.AwayFromZero);

    public const string NothingSelected = "Elija qué facturar: la venta de producto, el flete o las dos cosas.";
}
