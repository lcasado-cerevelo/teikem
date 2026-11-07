namespace Teikem.Domain.Sales;

/// <summary>Lote 222 — cómo se entrega una orden de venta. NULL en la orden = se deduce del consignatario (con consignatario, entrega; sin él, recogido).</summary>
public static class SalesDeliveryModes
{
    public const string Delivery = "DELIVERY";
    public const string Pickup = "PICKUP";
    public static readonly string[] All = { Delivery, Pickup };
}

/// <summary>Lote 222 — la regla de envío del cliente. NULL = usa la de la compañía; FREE = no se le cobra; OWN = regla propia (cargo y mínimo opcional).</summary>
public static class ClientShippingModes
{
    /// <summary>Lo que la ficha manda para volver a «usa la regla de la compañía» (se guarda como NULL).</summary>
    public const string Company = "COMPANY";
    public const string Free = "FREE";
    public const string Own = "OWN";
    public static readonly string[] Stored = { Free, Own };
}

/// <summary>Lote 222 — por qué la orden lleva (o no) cargo de envío. Catálogo LookupCode «ShippingReason».</summary>
public static class ShippingReasons
{
    public const string Domain = "ShippingReason";
    /// <summary>Cargo de envío según la regla de la compañía.</summary>
    public const string CompanyRule = "COMPANY_RULE";
    /// <summary>Sin cargo por acuerdo con el cliente («No se le cobra envío»).</summary>
    public const string ClientFree = "CLIENT_FREE";
    /// <summary>Cargo de envío según la regla propia del cliente.</summary>
    public const string ClientRule = "CLIENT_RULE";
    /// <summary>Gratis por monto: el total después del IVU llegó al mínimo.</summary>
    public const string Amount = "AMOUNT";
    /// <summary>Recogido: no hay envío.</summary>
    public const string Pickup = "PICKUP";
    public static readonly string[] All = { CompanyRule, ClientFree, ClientRule, Amount, Pickup };
}

/// <summary>Lo que la regla necesita saber de la orden, del cliente y de la compañía.</summary>
/// <param name="DeliveryMode">DELIVERY / PICKUP (null = se deduce con <paramref name="HasConsignee"/>).</param>
/// <param name="OrderTotalAfterTax">Total de los productos con su IVU (sin el envío): con esto se compara el mínimo.</param>
public sealed record ShippingInput(string? DeliveryMode, bool HasConsignee, decimal OrderTotalAfterTax,
    string? ClientMode, decimal? ClientCharge, decimal? ClientFreeFrom, decimal? CompanyCharge, decimal? CompanyFreeFrom);

/// <summary>El cargo de envío (antes de su IVU) y el porqué (código de ShippingReasons; null = no hay cargo de envío configurado).</summary>
public sealed record ShippingResult(decimal Charge, string? Reason);

/// <summary>
/// Lote 222 (plan del pedido de vendedores §9b) — regla de envío, lógica pura. Compañía y cliente, todo opcional, y el cliente gana:
/// - Solo en ENTREGA: un recogido no lleva envío (PICKUP).
/// - El cliente: «no se le cobra» (CLIENT_FREE) o «regla propia» (cargo y mínimo opcional, CLIENT_RULE). Sin regla propia (NULL) vale la de la compañía (COMPANY_RULE),
///   así un cambio en la compañía alcanza al instante a todos los que no tienen la suya (no se copia nada).
/// - Con la regla que toque: sin cargo configurado = $0 (sin motivo); si el total de los productos DESPUÉS DEL IVU llega al mínimo = $0 (AMOUNT); si no, el cargo.
///   Sin mínimo, siempre se cobra.
/// </summary>
public static class ShippingRules
{
    public const decimal MaxAmount = 99_999_999m;

    public const string ChargeInvalid = "El cargo de envío debe ser un monto mayor que cero con hasta 2 decimales.";
    public const string FreeFromInvalid = "El mínimo para envío gratis debe ser un monto de cero o más con hasta 2 decimales.";
    public const string ModeInvalid = "La regla de envío debe ser COMPANY (la de la compañía), FREE (no se le cobra) u OWN (regla propia).";
    public const string OwnNeedsCharge = "La regla propia de envío necesita el cargo.";
    public const string DeliveryModeInvalid = "La entrega debe ser DELIVERY (entrega) o PICKUP (recogido).";

    /// <summary>La forma de entrega efectiva: la de la orden o, si no la dice, entrega si tiene consignatario y recogido si no.</summary>
    public static string EffectiveMode(string? deliveryMode, bool hasConsignee)
        => string.Equals(deliveryMode, SalesDeliveryModes.Pickup, StringComparison.OrdinalIgnoreCase) ? SalesDeliveryModes.Pickup
            : string.Equals(deliveryMode, SalesDeliveryModes.Delivery, StringComparison.OrdinalIgnoreCase) ? SalesDeliveryModes.Delivery
            : hasConsignee ? SalesDeliveryModes.Delivery : SalesDeliveryModes.Pickup;

    public static ShippingResult Compute(ShippingInput i)
    {
        if (EffectiveMode(i.DeliveryMode, i.HasConsignee) == SalesDeliveryModes.Pickup) return new ShippingResult(0m, ShippingReasons.Pickup);
        var mode = NormalizeClientMode(i.ClientMode);
        if (mode == ClientShippingModes.Free) return new ShippingResult(0m, ShippingReasons.ClientFree);
        var own = mode == ClientShippingModes.Own;
        var charge = own ? i.ClientCharge : i.CompanyCharge;
        var freeFrom = own ? i.ClientFreeFrom : i.CompanyFreeFrom;
        var reason = own ? ShippingReasons.ClientRule : ShippingReasons.CompanyRule;
        if (charge is not decimal c || c <= 0m) return new ShippingResult(0m, null);   // sin cargo configurado
        if (freeFrom is decimal min && i.OrderTotalAfterTax >= min) return new ShippingResult(0m, ShippingReasons.Amount);
        return new ShippingResult(decimal.Round(c, 2, MidpointRounding.AwayFromZero), reason);
    }

    /// <summary>NULL / vacío / COMPANY = la de la compañía (null); FREE u OWN tal cual; cualquier otra cosa también null (lo valida <see cref="ParseClientMode"/>).</summary>
    public static string? NormalizeClientMode(string? mode)
    {
        var m = (mode ?? string.Empty).Trim().ToUpperInvariant();
        return ClientShippingModes.Stored.Contains(m) ? m : null;
    }

    /// <summary>Lo que manda la ficha del cliente: (cambia, modo a guardar, error). "" o COMPANY = volver a la de la compañía (NULL).</summary>
    public static (string? Mode, string? Error) ParseClientMode(string mode)
    {
        var m = mode.Trim().ToUpperInvariant();
        if (m.Length == 0 || m == ClientShippingModes.Company) return (null, null);
        return ClientShippingModes.Stored.Contains(m) ? (m, null) : (null, ModeInvalid);
    }

    /// <summary>DELIVERY / PICKUP; "" = quitarla (se deduce del consignatario). Devuelve (valor, error).</summary>
    public static (string? Mode, string? Error) ParseDeliveryMode(string mode)
    {
        var m = mode.Trim().ToUpperInvariant();
        if (m.Length == 0) return (null, null);
        return SalesDeliveryModes.All.Contains(m) ? (m, null) : (null, DeliveryModeInvalid);
    }

    /// <summary>Un cargo válido: mayor que cero, hasta 2 decimales y dentro del tope.</summary>
    public static bool IsValidCharge(decimal v) => v > 0m && v <= MaxAmount && decimal.Round(v, 2) == v;

    /// <summary>Un mínimo válido: cero o más, hasta 2 decimales y dentro del tope.</summary>
    public static bool IsValidFreeFrom(decimal v) => v >= 0m && v <= MaxAmount && decimal.Round(v, 2) == v;
}
