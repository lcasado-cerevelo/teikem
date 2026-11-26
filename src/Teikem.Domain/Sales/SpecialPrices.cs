namespace Teikem.Domain.Sales;

/// <summary>Lote 247 — de quién es un precio especial (la lista dedicada): de un cliente o de una cadena.</summary>
public static class SpecialPriceOwnerKinds
{
    public const string Client = "CLIENT";
    public const string Chain = "CHAIN";
}

/// <summary>Lote 247 — constantes de los precios especiales. Viven aquí para que el lote se pueda pasar solo a otra rama.</summary>
public static class SpecialPriceCodes
{
    /// <summary>Clave de la fuente de datos «Precios especiales» y su EntityType (sembrado en la sección «Lote 247» de update.sql, orden 199).</summary>
    public const string DataSourceKey = "SPECIAL_PRICE";
    public const string EntityType = DataSourceKey;
}

/// <summary>
/// Lote 247 (Lote C) — reglas puras de «Precios especiales» (sin EF; SpecialPriceService las traduce a 400/404/409/422). Un precio especial es un renglón de una
/// lista DEDICADA del cliente o de la cadena, una lista por dueño y por vigencia (DECISIÓN 1). El precio especial es un precio ANTES del IVU: las exenciones del
/// cliente se aplican después, sin cambiar el cálculo (DECISIÓN 20). Mensajes públicos: el manual y la FAQ los citan tal cual.
/// </summary>
public static class SpecialPriceRules
{
    public const string ValidFromRequired = "Indique desde qué fecha vale el precio especial.";
    public const string ValidToPast = "La fecha «hasta» ya pasó: un precio especial nuevo tiene que valer hoy o después.";
    /// <summary>404: «Precio especial no encontrado.»</summary>
    public const string RowNotFound = "Precio especial";

    private const string Prefix = "Precio especial — ";

    /// <summary>
    /// El nombre de la lista dedicada: «Precio especial — {dueño} ({vigencia})», p. ej. «Precio especial — Walgreens (del 01/12/2026 al 31/12/2026)». Nunca pasa
    /// de <see cref="PriceListCodes.MaxNameLength"/>: si no cabe, se recorta el nombre del dueño (con «…»).
    /// </summary>
    public static string DedicatedName(string owner, DateOnly from, DateOnly? to)
    {
        var range = $" ({OfferRules.RangeText(from, to)})";
        var who = (owner ?? string.Empty).Trim();
        var room = PriceListCodes.MaxNameLength - Prefix.Length - range.Length;
        if (who.Length > room) who = room > 1 ? who[..(room - 1)].TrimEnd() + "…" : string.Empty;
        var name = Prefix + who + range;
        return name.Length <= PriceListCodes.MaxNameLength ? name : name[..PriceListCodes.MaxNameLength];
    }

    /// <summary>
    /// Validación de un precio especial (alta o cambio): producto y precio obligatorios, «desde» obligatorio, «hasta» no antes de «desde» y, solo al CREAR, «hasta»
    /// no en el pasado (al editar sí se permite, para cerrar un precio que ya corre; DECISIÓN 25). Null = sin error; si no, el campo y el mensaje (400).
    /// </summary>
    public static (string Field, string Message)? RowError(Guid? productPublicId, decimal? price, DateOnly? from, DateOnly? to, DateOnly today, bool isCreate)
    {
        if (productPublicId is null || productPublicId == Guid.Empty) return ("productPublicId", PriceListRules.ItemProductRequired);
        if (price is null) return ("price", PriceListRules.ItemPriceRequired);
        if (from is not DateOnly f) return ("validFrom", ValidFromRequired);
        if (PriceListRules.DateRangeError(f, to) is { } range) return ("validTo", range);
        if (isCreate && to is DateOnly t && t < today) return ("validTo", ValidToPast);
        return null;
    }

    /// <summary>Días que le quedan a un precio especial (0 = vence hoy; negativo = ya venció); null = sin «hasta» (no vence).</summary>
    public static int? DaysToExpire(DateOnly? validTo, DateOnly today) => validTo is DateOnly t ? t.DayNumber - today.DayNumber : null;
}
