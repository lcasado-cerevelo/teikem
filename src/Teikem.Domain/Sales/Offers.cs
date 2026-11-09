using System.Globalization;
using Teikem.Domain.Common;

namespace Teikem.Domain.Sales;

/// <summary>Constantes de las ofertas (Lote 229). Viven aquí para que el lote se pueda pasar solo a otra rama.</summary>
public static class OfferCodes
{
    /// <summary>EntityType de la bitácora y dueño de las fotos de la oferta y de sus renglones (sembrado en la sección «Lote 229» de update.sql).</summary>
    public const string EntityType = "OFFER";
    public const int MaxNameLength = 120;
    public const int MaxNotesLength = 500;
    public const int MaxItems = 500;
    public const int MaxClients = 2000;
}

/// <summary>Avisos de precio (no son errores): la orden y el catálogo los muestran; la oferta los trae al crearla o cambiarla.</summary>
public static class PriceNoticeCodes
{
    /// <summary>El producto está en dos ofertas vigentes a la vez para el cliente: gana la más barata.</summary>
    public const string TwoOffers = "TWO_OFFERS";
    /// <summary>La oferta sale más cara que la lista general: gana el precio más bajo.</summary>
    public const string OfferAboveGeneral = "OFFER_ABOVE_GENERAL";
    /// <summary>(Al armar la oferta) otra oferta publicada, en fechas y clientes que se cruzan, trae el mismo producto igual o más barato.</summary>
    public const string CheaperOffer = "CHEAPER_OFFER";
}

/// <summary>
/// Lote 229 — oferta: cosa aparte de las listas de precios, como la circular de una tienda («válido del … al …»): nombre, foto, vigencia (desde obligatorio, hasta
/// opcional: sin «hasta» la oferta no vence), se publica (en borrador no se aplica) y aplica a todos los clientes o a los escogidos. Sus renglones son productos o
/// kits con su precio de oferta; un kit se puede crear para la oferta (Product.OfferId) y solo se vende mientras ella esté publicada y vigente. Nunca DELETE.
/// </summary>
[AuditEntity(OfferCodes.EntityType)]
public class Offer : ITenantScoped, ISoftDeletable, IAuditStamped
{
    public int OfferId { get; set; }
    [NotAudited] public Guid PublicId { get; set; }
    public int TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateOnly ValidFrom { get; set; }
    public DateOnly? ValidTo { get; set; }
    public bool IsPublished { get; set; }
    public bool AppliesToAll { get; set; } = true;
    /// <summary>La foto de la oferta (StoredFile con dueño OFFER).</summary>
    public int? ImageFileId { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
    [NotAudited] public DateTime CreatedAtUtc { get; set; }
    public int? CreatedBy { get; set; }
    [NotAudited] public DateTime? UpdatedAtUtc { get; set; }
    public int? UpdatedBy { get; set; }
    [NotAudited] public byte[]? RowVersion { get; set; }
    public List<OfferItem> Items { get; set; } = new();
    public List<OfferClient> Clients { get; set; } = new();
}

/// <summary>Renglón de la oferta: un producto o kit (también uno creado para la oferta) a su precio de oferta, con foto opcional (StoredFile con dueño OFFER). Quitar = IsActive 0.</summary>
[AuditEntity(OfferCodes.EntityType)]
public class OfferItem : ITenantScoped, ISoftDeletable
{
    public int OfferItemId { get; set; }
    public int TenantId { get; set; }
    public int OfferId { get; set; }
    public int ProductId { get; set; }
    public decimal Price { get; set; }
    public int? ImageFileId { get; set; }
    public bool IsActive { get; set; } = true;
    [NotAudited] public DateTime CreatedAtUtc { get; set; }
}

/// <summary>Cliente al que aplica una oferta que NO es para todos. Quitar = IsActive 0.</summary>
[AuditEntity(OfferCodes.EntityType)]
public class OfferClient : ITenantScoped, ISoftDeletable
{
    public int OfferClientId { get; set; }
    public int TenantId { get; set; }
    public int OfferId { get; set; }
    public int ClientId { get; set; }
    public bool IsActive { get; set; } = true;
    [NotAudited] public DateTime CreatedAtUtc { get; set; }
}

/// <summary>Un renglón de oferta visto por las reglas puras.</summary>
public sealed record OfferItemView(int ProductId, decimal Price, int? ImageFileId = null);

/// <summary>Una oferta vista por las reglas puras (solo renglones y clientes activos).</summary>
public sealed record OfferView(int OfferId, string Name, bool IsPublished, bool IsActive, bool AppliesToAll, DateOnly ValidFrom, DateOnly? ValidTo,
    IReadOnlyCollection<int> ClientIds, IReadOnlyList<OfferItemView> Items);

/// <summary>
/// Aviso de precio de un producto: TWO_OFFERS (la oferta que ganó y la otra), OFFER_ABOVE_GENERAL (la oferta y el precio de la lista general). El mensaje lo arma
/// el servicio con el SKU (<see cref="OfferRules.NoticeMessage"/>).
/// </summary>
public sealed record PriceNotice(string Code, int OfferId, string OfferName, decimal OfferPrice, int? OtherOfferId = null, string? OtherOfferName = null, decimal? OtherPrice = null);

/// <summary>Un cruce de una oferta con otra publicada (fechas y clientes que se cruzan) que trae el mismo producto igual o más barato.</summary>
public sealed record OfferOverlap(int OtherOfferId, string OtherName, DateOnly OtherFrom, DateOnly? OtherTo, int ProductId, decimal MyPrice, decimal OtherPrice);

/// <summary>
/// Lote 229 — reglas puras de las ofertas y del precio al pedir (sin EF; OfferService, SalesCatalogService y SalesOrderService las traducen a 400/404/422 y a
/// avisos). Mensajes públicos: el manual y la FAQ los citan tal cual.
/// </summary>
public static class OfferRules
{
    public const string NameRequired = "Indique el nombre de la oferta.";
    public static readonly string NameTooLong = $"El nombre de la oferta no puede pasar de {OfferCodes.MaxNameLength} caracteres.";
    public const string ValidFromRequired = "Indique desde qué fecha vale la oferta.";
    public const string DateRange = "La fecha «hasta» de la oferta no puede ser anterior a la fecha «desde».";
    public static readonly string NotesTooLong = $"Las notas no pueden pasar de {OfferCodes.MaxNotesLength} caracteres.";
    public const string NotFound = "Oferta";
    public const string ItemNotFound = "Renglón de la oferta";
    public const string KitNotFound = "Kit de la oferta";
    public const string Inactive = "La oferta está desactivada: reactívela para cambiarla.";
    public const string ItemsRequired = "Indique los productos y sus precios de oferta.";
    public static readonly string TooManyItems = $"Una oferta admite como máximo {OfferCodes.MaxItems} renglones.";
    public const string ItemProductRequired = "Escoja el producto o kit de cada renglón.";
    public const string ItemPriceRequired = "Indique el precio de oferta de cada renglón.";
    public const string ItemProductNotOwn = "Solo se ponen en una oferta productos propios de la compañía.";
    public static string ItemProductInactive(string sku) => $"El producto {sku} está inactivo: no se puede poner en la oferta.";
    public static string ItemRepeated(string sku) => $"El producto {sku} viene repetido.";
    public static string ItemKitOfOtherOffer(string sku, string otherName) => $"El kit {sku} es de la oferta «{otherName}»: no se puede poner en otra.";
    public const string ClientsRequired = "Escoja los clientes de la oferta.";
    public static readonly string TooManyClients = $"Una oferta admite como máximo {OfferCodes.MaxClients} clientes.";
    public const string ClientsOnGeneral = "La oferta aplica a todos los clientes: no lleva clientes escogidos. Desmarque «aplica a todos» para escogerlos.";
    public const string GeneralWithClients = "La oferta tiene clientes escogidos: quítelos antes de hacerla general (aplica a todos).";
    public const string ClientNotInOffer = "Cliente de la oferta";
    public const string PublishNeedsItems = "Agregue al menos un renglón antes de publicar la oferta.";
    public const string PublishNeedsClients = "Escoja al menos un cliente o marque que la oferta aplica a todos los clientes.";
    public const string AlreadyPublished = "La oferta ya está publicada.";
    public const string NotPublished = "La oferta ya está en borrador.";
    public const string AlreadyInactive = "La oferta ya está desactivada.";
    public const string AlreadyActive = "La oferta ya está activa.";
    public const string Concurrency = "La oferta cambió al mismo tiempo. Recárguela e intente de nuevo.";
    public const string KitPriceRequired = "Indique el precio de oferta del kit.";
    public const string KitOfAnotherOffer = "Ese kit no es de esta oferta: cámbielo desde su propia oferta o desde Productos.";

    /// <summary>Un kit de oferta no se pone en una lista de precios: su precio va en su oferta.</summary>
    public static string OfferKitNotInLists(string sku, string offerName) => $"El kit {sku} es de la oferta «{offerName}»: su precio va en la oferta, no en una lista de precios.";

    /// <summary>Recorta el nombre; vacío = error.</summary>
    public static (string? Value, string? Error) NormalizeName(string? name)
    {
        var v = name?.Trim();
        if (string.IsNullOrEmpty(v)) return (null, NameRequired);
        if (v.Length > OfferCodes.MaxNameLength) return (null, NameTooLong);
        return (v, null);
    }

    /// <summary>Recorta las notas; vacías = null.</summary>
    public static (string? Value, string? Error) NormalizeNotes(string? notes)
    {
        var v = notes?.Trim();
        if (string.IsNullOrEmpty(v)) return (null, null);
        return v.Length > OfferCodes.MaxNotesLength ? (null, NotesTooLong) : (v, null);
    }

    /// <summary>«hasta» (opcional) no antes de «desde».</summary>
    public static string? DateRangeError(DateOnly from, DateOnly? to) => to is DateOnly t && t < from ? DateRange : null;

    /// <summary>¿La oferta aplica hoy? Publicada, activa y dentro de su vigencia (extremos incluidos; sin «hasta» = no vence).</summary>
    public static bool InEffect(OfferView o, DateOnly date) => o.IsPublished && o.IsActive && PriceListRules.InEffect(o.ValidFrom, o.ValidTo, date);

    /// <summary>¿La oferta alcanza al cliente? Las de todos, siempre; las de clientes escogidos, solo si lo incluyen (sin cliente: solo las de todos).</summary>
    public static bool AppliesTo(OfferView o, int? clientId) => o.AppliesToAll || (clientId is int c && o.ClientIds.Contains(c));

    /// <summary>¿Dos ofertas alcanzan a algún cliente en común? Una para todos se cruza con cualquiera; dos de clientes escogidos, si comparten alguno.</summary>
    public static bool AudiencesOverlap(OfferView a, OfferView b) => a.AppliesToAll || b.AppliesToAll || a.ClientIds.Any(b.ClientIds.Contains);

    /// <summary>
    /// Kit creado para una oferta: solo se vende mientras su oferta esté publicada, activa y vigente. Fuera de eso se trata como inactivo para la venta (no sale en el
    /// catálogo ni se puede pedir); no se borra: las órdenes y facturas viejas lo siguen viendo. Si la oferta se extiende, vuelve.
    /// </summary>
    public static bool KitSellable(OfferView? offer, DateOnly date) => offer is not null && InEffect(offer, date);

    /// <summary>Mensaje (422) del kit de oferta que hoy no se vende, con el motivo y las fechas.</summary>
    public static string KitNotSellable(string sku, OfferView? offer, DateOnly date)
    {
        if (offer is null) return $"El kit {sku} es de una oferta que ya no existe: no se puede vender.";
        var why = !offer.IsActive ? "está desactivada"
            : !offer.IsPublished ? "está en borrador"
            : date < offer.ValidFrom ? $"todavía no empieza (vale {RangeText(offer.ValidFrom, offer.ValidTo)})"
            : $"ya terminó (valía {RangeText(offer.ValidFrom, offer.ValidTo)})";
        return $"El kit {sku} es de la oferta «{offer.Name}», que {why}: no se puede vender.";
    }

    /// <summary>La vigencia en palabras: «del 01/11/2026 al 30/11/2026» o «desde el 01/11/2026, sin fecha de fin».</summary>
    public static string RangeText(DateOnly from, DateOnly? to)
        => to is DateOnly t ? $"del {D(from)} al {D(t)}" : $"desde el {D(from)}, sin fecha de fin";

    /// <summary>
    /// El precio de un producto (o kit) para un cliente en una fecha, con las ofertas (Luis, 2026-11-09):
    /// 1. Entre el precio NEGOCIADO (lista publicada y vigente del cliente) y la OFERTA vigente que le aplique, gana el más bajo (a igual precio, la lista del cliente).
    /// 2. Sin precio negociado: entre la oferta y la lista general gana el más bajo; sin oferta, la lista general.
    /// 3. Si tampoco hay lista: el precio de venta del producto; si no, sin precio.
    /// Dos ofertas vigentes con el producto: gana la más barata (a igual precio, la que empezó más tarde) y se avisa (TWO_OFFERS). Si la oferta sale más cara que la
    /// lista general se avisa (OFFER_ABOVE_GENERAL). Los avisos no son errores.
    /// </summary>
    public static ResolvedPrice Resolve(int productId, int? clientId, DateOnly date, IEnumerable<PriceListView> lists, IEnumerable<OfferView> offers, decimal? productSalePrice)
    {
        var all = lists as IReadOnlyCollection<PriceListView> ?? lists.ToList();
        var fromLists = PriceListRules.Resolve(productId, clientId, date, all, null);
        var negotiated = fromLists.Source == PriceSources.ClientList ? fromLists : null;
        var general = negotiated is null ? (fromLists.Source == PriceSources.GeneralList ? fromLists : null)
            : PriceListRules.Resolve(productId, null, date, all.Where(l => l.AppliesToAll), null) is { Source: PriceSources.GeneralList } g ? g : null;

        var candidates = offers.Where(o => InEffect(o, date) && AppliesTo(o, clientId))
            .Select(o => (Offer: o, Item: o.Items.FirstOrDefault(i => i.ProductId == productId)))
            .Where(x => x.Item is not null)
            .OrderBy(x => x.Item!.Price).ThenByDescending(x => x.Offer.ValidFrom).ThenByDescending(x => x.Offer.OfferId)
            .ToList();
        var notices = new List<PriceNotice>();
        OfferView? best = null;
        OfferItemView? bestItem = null;
        if (candidates.Count > 0)
        {
            (best, bestItem) = (candidates[0].Offer, candidates[0].Item!);
            foreach (var other in candidates.Skip(1))
                notices.Add(new PriceNotice(PriceNoticeCodes.TwoOffers, best.OfferId, best.Name, bestItem.Price, other.Offer.OfferId, other.Offer.Name, other.Item!.Price));
            if (general?.Price is decimal gp && bestItem.Price > gp)
                notices.Add(new PriceNotice(PriceNoticeCodes.OfferAboveGeneral, best.OfferId, best.Name, bestItem.Price, OtherPrice: gp));
        }
        var n = notices.Count == 0 ? null : notices;
        ResolvedPrice FromOffer() => new(bestItem!.Price, PriceSources.Offer, null, bestItem.ImageFileId, best!.OfferId, n);

        if (negotiated?.Price is decimal c)
            return bestItem is not null && bestItem.Price < c ? FromOffer() : negotiated with { Notices = n };
        if (bestItem is not null)
            return general?.Price is decimal g2 && g2 < bestItem.Price ? general with { Notices = n } : FromOffer();
        if (general is not null) return general;
        return productSalePrice is decimal p ? new ResolvedPrice(p, PriceSources.Product) : new ResolvedPrice(null, PriceSources.None);
    }

    /// <summary>
    /// Al armar una oferta (crear, cambiar fechas o clientes, agregar renglones, publicar): las otras ofertas PUBLICADAS y activas que se cruzan en fechas y clientes y
    /// traen el mismo producto igual o más barato. No impide nada (gana el precio más bajo); la respuesta lo avisa.
    /// </summary>
    public static IReadOnlyList<OfferOverlap> FindCheaperOverlaps(OfferView candidate, IEnumerable<OfferView> others)
    {
        var result = new List<OfferOverlap>();
        foreach (var o in others)
        {
            if (o.OfferId == candidate.OfferId || !o.IsPublished || !o.IsActive) continue;
            if (!PriceListRules.DatesOverlap(candidate.ValidFrom, candidate.ValidTo, o.ValidFrom, o.ValidTo) || !AudiencesOverlap(candidate, o)) continue;
            foreach (var mine in candidate.Items)
            {
                var theirs = o.Items.FirstOrDefault(i => i.ProductId == mine.ProductId);
                if (theirs is not null && theirs.Price <= mine.Price)
                    result.Add(new OfferOverlap(o.OfferId, o.Name, o.ValidFrom, o.ValidTo, mine.ProductId, mine.Price, theirs.Price));
            }
        }
        return result.OrderBy(r => r.ProductId).ThenBy(r => r.OtherPrice).ToList();
    }

    /// <summary>El texto del aviso de precio de un producto (los avisos no son errores).</summary>
    public static string NoticeMessage(string sku, PriceNotice n) => n.Code switch
    {
        PriceNoticeCodes.TwoOffers =>
            $"El producto {sku} está en dos ofertas a la vez: «{n.OfferName}» a {Money(n.OfferPrice)} y «{n.OtherOfferName}» a {Money(n.OtherPrice ?? 0m)}. Se aplica la más barata («{n.OfferName}»).",
        PriceNoticeCodes.OfferAboveGeneral =>
            $"La oferta «{n.OfferName}» pone {sku} a {Money(n.OfferPrice)}, más caro que la lista general ({Money(n.OtherPrice ?? 0m)}): se aplica el precio más bajo.",
        _ => n.Code,
    };

    /// <summary>El texto del aviso de otra oferta igual o más barata con el mismo producto (al armar la oferta).</summary>
    public static string CheaperOfferMessage(string sku, OfferOverlap o)
        => $"El producto {sku} también está en la oferta «{o.OtherName}» ({RangeText(o.OtherFrom, o.OtherTo)}) a {Money(o.OtherPrice)}, igual o más barata que aquí ({Money(o.MyPrice)}): mientras las dos estén vigentes se aplica el precio más bajo.";

    /// <summary>El texto del aviso de la oferta más cara que la lista general (al armar la oferta).</summary>
    public static string AboveGeneralMessage(string sku, decimal offerPrice, decimal generalPrice)
        => $"El producto {sku} sale en esta oferta a {Money(offerPrice)}, más caro que en la lista general ({Money(generalPrice)}): se aplicará el precio más bajo.";

    public static string Money(decimal d) => "$" + d.ToString("0.00##", CultureInfo.InvariantCulture);

    private static string D(DateOnly d) => d.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
}
