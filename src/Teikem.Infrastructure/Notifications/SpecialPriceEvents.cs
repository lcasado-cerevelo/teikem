using Teikem.Domain.Constants;

namespace Teikem.Infrastructure.Notifications;

/// <summary>
/// Lote 248 (pieza G) — código del evento de aviso del precio especial por vencer (contrato con el Lote 247). Con el prefijo SALES_ cae en el grupo «Ventas» de Reglas
/// de aviso (<see cref="NotificationEventCategories.Of"/>) sin tocar NotificationEvents.cs. Hasta 60 caracteres (columna EventCode).
/// </summary>
public static class SpecialPriceEventCodes
{
    public const string Expiring = "SALES_SPECIAL_PRICE_EXPIRING";

    public static readonly string[] All = { Expiring };
}

/// <summary>Lote 248 (pieza G) — claves de los destinatarios del evento (Kind CONTEXT) del aviso de precio especial. Nunca va al cliente.</summary>
public static class SpecialPriceContextRecipients
{
    /// <summary>
    /// «Vendedores del cliente o de la cadena»: de un precio de cliente, SalesSellerQueries.NoticeUsersAsync de ese cliente; de un precio de cadena, la unión de los de
    /// sus clientes activos (tope <see cref="SpecialPriceNoticeContent.MaxChainRecipients"/>).
    /// </summary>
    public const string ClientSellers = "CLIENT_SELLERS";
}

/// <summary>
/// Lote 248 (pieza G) — el evento «Precio especial por vencer». Lo publica el trabajo diario DOCUMENT_EXPIRY_SCAN con la fuente SPECIAL_PRICE
/// (<see cref="Services.SpecialPriceExpirySource"/>), uno por lista dedicada y umbral, solo ANTES de vencer o el día que vence (umbrales ≥ 0). El contenido y los
/// destinatarios los arma <see cref="SpecialPriceNoticeContent"/> al enviar. Es opcional; las reglas las arma el administrador en Sistema → Reglas de aviso (no se siembra
/// ninguna). Los precios especiales viven en el módulo de órdenes de venta. El enlace lleva a la ficha del dueño (cliente o cadena), donde está «Precios especiales».
/// </summary>
public sealed class SpecialPriceEventProvider : INotificationEventProvider
{
    /// <summary>La ruta de la ficha del dueño viene armada en la variable <c>ownerLink</c> (cliente o cadena).</summary>
    public const string LinkTemplate = "{ownerLink}";

    private static NotificationVariable[] Variables() => new[]
    {
        new NotificationVariable("owner", "Cliente o cadena", "Client or chain", "Walgreens"),
        new NotificationVariable("ownerKind", "De (cliente o cadena)", "Owner kind (client or chain)", "Cadena"),
        new NotificationVariable("priceList", "Lista del precio especial", "Special price list", "Precio especial — Walgreens (del 01/12/2026 al 31/12/2026)"),
        new NotificationVariable("validTo", "Vence (fecha «hasta»)", "Expires (valid to)", "12/31/2026"),
        new NotificationVariable("days", "Días que faltan", "Days left", "15"),
        new NotificationVariable("threshold", "Umbral que se cruzó (días)", "Threshold crossed (days)", "15"),
        new NotificationVariable("itemCount", "Productos con precio especial", "Products with a special price", "12"),
        new NotificationVariable("priceListPublicId", "Identificador de la lista", "Price list identifier", "3f2504e04f8941d39a0c0305e82c3301"),
        new NotificationVariable("validToIso", "Fecha «hasta» (AAAA-MM-DD)", "Valid to (YYYY-MM-DD)", "2026-12-31"),
        new NotificationVariable("ownerLink", "Enlace a la ficha del cliente o de la cadena", "Link to the client's or chain's page", "/catalog/chains?id=9a1f3c52-7d1e-4b8a-9a55-2d7c0f0e1b11"),
    };

    public IEnumerable<NotificationEventDef> Events { get; } = new[]
    {
        new NotificationEventDef(
            SpecialPriceEventCodes.Expiring, ModuleKeys.SalesOrders, "Precio especial por vencer", "Special price about to expire",
            Variables(),
            IsOptional: true, LinkTemplate: LinkTemplate,
            "Precio especial por vencer: {owner}", "Special price about to expire: {owner}",
            "El precio especial de {owner} ({ownerKind}), «{priceList}», con {itemCount} producto(s), vence el {validTo}: quedan {days} día(s). Si debe seguir, cree el precio especial de la vigencia siguiente.",
            "The special price for {owner} ({ownerKind}), \"{priceList}\", with {itemCount} product(s), expires on {validTo}: {days} day(s) left. If it should continue, create the special price for the next period.",
            ContextRecipients: new[]
            {
                new NotificationContextRecipientDef(SpecialPriceContextRecipients.ClientSellers, "Vendedores del cliente o de la cadena (ruta y preferido)",
                    "Client's or chain's sales reps (route and preferred)", IsDefault: true),
            }),
    };
}
