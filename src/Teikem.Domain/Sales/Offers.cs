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
    /// <summary>Lote 247: tope de cadenas por oferta (los clientes siguen en 2000).</summary>
    public const int MaxChains = 500;
}

/// <summary>
/// Lote 247 — a quién aplica una oferta (Offer.AudienceMode, para toda la oferta): ALL = todos los clientes; INCLUDE = solo los clientes y cadenas escogidos;
/// EXCLUDE = todos menos los escogidos. CK_Offer_AudienceMode lo mantiene sincronizado con Offer.AppliesToAll (ALL ⇔ AppliesToAll = 1).
/// </summary>
public static class OfferAudienceModes
{
    public const string All = "ALL";
    public const string Include = "INCLUDE";
    public const string Exclude = "EXCLUDE";
    public static readonly IReadOnlyList<string> Values = new[] { All, Include, Exclude };

    /// <summary>Recorta y pasa a mayúsculas; vacío = no viene (null, sin error); un valor que no es ALL, INCLUDE ni EXCLUDE = error.</summary>
    public static (string? Value, string? Error) Normalize(string? mode)
    {
        var v = mode?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(v)) return (null, null);
        return Values.Contains(v) ? (v, null) : (null, OfferRules.AudienceInvalid);
    }

    /// <summary>El público que equivale al «aplica a todos» de antes: true = ALL; false = INCLUDE.</summary>
    public static string FromAppliesToAll(bool appliesToAll) => appliesToAll ? All : Include;
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
    /// <summary>Lote 247: un precio especial (lista del cliente o de su cadena) sale más caro que la lista general: se cobra el más bajo.</summary>
    public const string SpecialAboveGeneral = "SPECIAL_ABOVE_GENERAL";
}

/// <summary>
/// Lote 229 — oferta: cosa aparte de las listas de precios, como la circular de una tienda («válido del … al …»): nombre, foto, vigencia (desde obligatorio, hasta
/// opcional: sin «hasta» la oferta no vence), se publica (en borrador no se aplica) y aplica a todos los clientes o a los escogidos. Sus renglones son productos o
/// kits con su precio de oferta. Lote 231: un kit de oferta es de la biblioteca (Product.IsOfferKit, ver OfferKitRules), lo usan varias ofertas con su renglón y solo
/// se vende mientras esté en alguna publicada y vigente que aplique al cliente. Nunca DELETE.
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

    private bool _appliesToAll = true;
    /// <summary>
    /// «Aplica a todos». Lote 247: es el reflejo de <see cref="AudienceMode"/> (ALL ⇔ true). Se conserva por compatibilidad: ponerlo en true deja el público en ALL
    /// y en false lo deja en INCLUDE (si estaba en ALL; si ya era INCLUDE o EXCLUDE no cambia). Lo nuevo usa <see cref="SetAudienceMode"/>.
    /// </summary>
    public bool AppliesToAll
    {
        get => _appliesToAll;
        set
        {
            _appliesToAll = value;
            if (value) AudienceMode = OfferAudienceModes.All;
            else if (AudienceMode == OfferAudienceModes.All) AudienceMode = OfferAudienceModes.Include;
        }
    }

    /// <summary>Lote 247: ALL, INCLUDE o EXCLUDE (<see cref="OfferAudienceModes"/>). Se cambia solo con <see cref="SetAudienceMode"/>.</summary>
    public string AudienceMode { get; private set; } = OfferAudienceModes.All;

    /// <summary>Lote 247: cambia el público de la oferta y deja AppliesToAll sincronizado (true solo con ALL). Un valor desconocido es un error de programación.</summary>
    public void SetAudienceMode(string mode)
    {
        if (!OfferAudienceModes.Values.Contains(mode)) throw new ArgumentOutOfRangeException(nameof(mode), mode, "Público de oferta desconocido.");
        AudienceMode = mode;
        _appliesToAll = mode == OfferAudienceModes.All;
    }

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
    /// <summary>Lote 247: cadenas incluidas (INCLUDE) o excluidas (EXCLUDE), según el público de la oferta.</summary>
    public List<OfferChain> Chains { get; set; } = new();
}

/// <summary>Renglón de la oferta: un producto o kit (también un kit de oferta de la biblioteca) a su precio de oferta, con foto opcional (StoredFile con dueño OFFER). Quitar = IsActive 0.</summary>
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

/// <summary>Lote 247 — cadena incluida o excluida de una oferta que NO es para todos (según Offer.AudienceMode). Quitar = IsActive 0.</summary>
[AuditEntity(OfferCodes.EntityType)]
public class OfferChain : ITenantScoped, ISoftDeletable
{
    public int OfferChainId { get; set; }
    public int TenantId { get; set; }
    public int OfferId { get; set; }
    public int ChainId { get; set; }
    public bool IsActive { get; set; } = true;
    [NotAudited] public DateTime CreatedAtUtc { get; set; }
}

/// <summary>Un renglón de oferta visto por las reglas puras.</summary>
public sealed record OfferItemView(int ProductId, decimal Price, int? ImageFileId = null);

/// <summary>
/// Una oferta vista por las reglas puras (solo renglones, clientes y cadenas activos). Lote 247: AudienceMode (null = se deduce de AppliesToAll: true = ALL,
/// false = INCLUDE, como antes) y ChainIds (las cadenas incluidas o excluidas; null = ninguna). ClientIds son los incluidos o los excluidos, según el público.
/// </summary>
public sealed record OfferView(int OfferId, string Name, bool IsPublished, bool IsActive, bool AppliesToAll, DateOnly ValidFrom, DateOnly? ValidTo,
    IReadOnlyCollection<int> ClientIds, IReadOnlyList<OfferItemView> Items, string? AudienceMode = null, IReadOnlyCollection<int>? ChainIds = null)
{
    /// <summary>El público efectivo: AudienceMode o, si no viene, el que equivale a AppliesToAll.</summary>
    public string Mode => AudienceMode ?? OfferAudienceModes.FromAppliesToAll(AppliesToAll);
}

/// <summary>
/// Aviso de precio de un producto: TWO_OFFERS (la oferta que ganó y la otra), OFFER_ABOVE_GENERAL (la oferta y el precio de la lista general). El mensaje lo arma
/// el servicio con el SKU (<see cref="OfferRules.NoticeMessage"/>). Lote 247: SPECIAL_ABOVE_GENERAL (un precio especial más caro que la lista general) lleva la
/// lista en PriceListId / PriceListName, su precio en OfferPrice y el de la lista general en OtherPrice; OfferId = 0 y OfferName vacío (no hay oferta).
/// </summary>
public sealed record PriceNotice(string Code, int OfferId, string OfferName, decimal OfferPrice, int? OtherOfferId = null, string? OtherOfferName = null, decimal? OtherPrice = null,
    int? PriceListId = null, string? PriceListName = null);

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
    public const string ClientsRequired = "Escoja los clientes de la oferta.";
    public static readonly string TooManyClients = $"Una oferta admite como máximo {OfferCodes.MaxClients} clientes.";
    public const string ClientsOnGeneral = "La oferta aplica a todos los clientes: no lleva clientes escogidos. Desmarque «aplica a todos» para escogerlos.";
    public const string GeneralWithClients = "La oferta tiene clientes o cadenas escogidos: quítelos antes de hacerla para todos.";
    public const string ClientNotInOffer = "Cliente de la oferta";
    public const string PublishNeedsItems = "Agregue al menos un renglón antes de publicar la oferta.";
    public const string PublishNeedsClients = "Escoja al menos un cliente o una cadena, o marque que la oferta aplica a todos los clientes.";
    // Lote 247: público (incluir / excluir) y cadenas
    public const string AudienceInvalid = "El público de la oferta debe ser ALL (todos), INCLUDE (solo los escogidos) o EXCLUDE (todos menos los escogidos).";
    public const string AudienceConflict = "“Aplica a todos” no coincide con el público escogido: indique solo el público (todos, solo los escogidos o todos menos los escogidos).";
    public const string AudienceChangeWithRows = "Quite los clientes y cadenas de la oferta antes de cambiar entre incluir y excluir.";
    public const string ChainsRequired = "Escoja las cadenas de la oferta.";
    public static readonly string TooManyChains = $"Una oferta admite como máximo {OfferCodes.MaxChains} cadenas.";
    public const string ChainsOnGeneral = "La oferta aplica a todos los clientes: no lleva cadenas escogidas. Desmarque «aplica a todos» para escogerlas.";
    public const string ChainNotInOffer = "Cadena de la oferta";
    public const string AlreadyPublished = "La oferta ya está publicada.";
    public const string NotPublished = "La oferta ya está en borrador.";
    public const string AlreadyInactive = "La oferta ya está desactivada.";
    public const string AlreadyActive = "La oferta ya está activa.";
    public const string Concurrency = "La oferta cambió al mismo tiempo. Recárguela e intente de nuevo.";
    public const string KitPriceRequired = "Indique el precio de oferta del kit.";

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

    /// <summary>
    /// ¿La oferta alcanza al cliente (de la cadena <paramref name="chainId"/>; null = sin cadena o cadena inactiva)? Lote 247: ALL, siempre; INCLUDE, si están el
    /// cliente o su cadena; EXCLUDE, si no están ni el cliente ni su cadena. Sin cliente (catálogo genérico): las ALL, las INCLUDE que traen la cadena y las EXCLUDE
    /// que no la excluyen (DECISIÓN 9).
    /// </summary>
    public static bool AppliesTo(OfferView o, int? clientId, int? chainId = null)
    {
        var mode = o.Mode;
        if (mode == OfferAudienceModes.All) return true;
        var named = (clientId is int c && o.ClientIds.Contains(c)) || (chainId is int ch && o.ChainIds is { } chains && chains.Contains(ch));
        return mode == OfferAudienceModes.Exclude ? !named : named;
    }

    /// <summary>
    /// ¿Dos ofertas pueden alcanzar a algún cliente en común? Conservadora (ante la duda, sí; el aviso no impide nada):
    /// - una INCLUDE sin clientes ni cadenas no alcanza a nadie: no se cruza;
    /// - una ALL se cruza con cualquiera;
    /// - INCLUDE contra INCLUDE: si comparten un cliente o una cadena, o si <paramref name="chainOfClient"/> (cliente → cadena activa) pone un cliente de una en una
    ///   cadena de la otra (sin mapa no se cruzan por ese lado);
    /// - INCLUDE contra EXCLUDE: si algún incluido no queda excluido (un cliente que no está excluido ni por él ni por su cadena, o una cadena que no está excluida);
    /// - EXCLUDE contra EXCLUDE: siempre (las dos alcanzan a casi todos).
    /// </summary>
    public static bool AudiencesOverlap(OfferView a, OfferView b, IReadOnlyDictionary<int, int>? chainOfClient = null)
    {
        static bool Empty(OfferView o) => o.ClientIds.Count == 0 && (o.ChainIds is null || o.ChainIds.Count == 0);
        var (ma, mb) = (a.Mode, b.Mode);
        if ((ma == OfferAudienceModes.Include && Empty(a)) || (mb == OfferAudienceModes.Include && Empty(b))) return false;
        if (ma == OfferAudienceModes.All || mb == OfferAudienceModes.All) return true;
        if (ma == OfferAudienceModes.Exclude && mb == OfferAudienceModes.Exclude) return true;
        var map = chainOfClient ?? new Dictionary<int, int>();
        int? ChainOf(int client) => map.TryGetValue(client, out var ch) ? ch : null;
        var aChains = a.ChainIds ?? Array.Empty<int>();
        var bChains = b.ChainIds ?? Array.Empty<int>();
        if (ma == OfferAudienceModes.Include && mb == OfferAudienceModes.Include)
            return a.ClientIds.Any(b.ClientIds.Contains) || aChains.Any(bChains.Contains)
                || a.ClientIds.Any(c => ChainOf(c) is int ch && bChains.Contains(ch))
                || b.ClientIds.Any(c => ChainOf(c) is int ch && aChains.Contains(ch));
        var (inc, exc) = ma == OfferAudienceModes.Include ? (a, b) : (b, a);
        var excChains = exc.ChainIds ?? Array.Empty<int>();
        return inc.ClientIds.Any(c => !exc.ClientIds.Contains(c) && !(ChainOf(c) is int ch && excChains.Contains(ch)))
            || (inc.ChainIds ?? Array.Empty<int>()).Any(ch => !excChains.Contains(ch));
    }

    /// <summary>La vigencia en palabras: «del 01/11/2026 al 30/11/2026» o «desde el 01/11/2026, sin fecha de fin».</summary>
    public static string RangeText(DateOnly from, DateOnly? to)
        => to is DateOnly t ? $"del {D(from)} al {D(t)}" : $"desde el {D(from)}, sin fecha de fin";

    /// <summary>
    /// El precio de un producto (o kit) para un cliente en una fecha, con las ofertas (Luis, 2026-11-09; Lote 247, plan l.61: sin jerarquía, gana el más bajo):
    /// 1. Candidatos: la lista del cliente, la de su cadena (<paramref name="chainId"/>; null = sin cadena o cadena inactiva), la oferta vigente más barata que le
    ///    aplique y la lista general. Gana el precio MÁS BAJO; a igual precio: cliente, cadena, oferta, general (DECISIÓN 4).
    /// 2. Si ninguno trae el producto: el precio de venta del producto; si no, sin precio. El precio del producto no compite con los demás (DECISIÓN 5).
    /// Dos ofertas vigentes con el producto: gana la más barata (a igual precio, la que empezó más tarde) y se avisa (TWO_OFFERS). Si la oferta sale más cara que la
    /// lista general se avisa (OFFER_ABOVE_GENERAL); si un precio especial (cliente o cadena) sale más caro que la general, también (SPECIAL_ABOVE_GENERAL, uno por
    /// nivel). Los avisos no son errores.
    /// </summary>
    public static ResolvedPrice Resolve(int productId, int? clientId, DateOnly date, IEnumerable<PriceListView> lists, IEnumerable<OfferView> offers, decimal? productSalePrice,
        int? chainId = null)
    {
        var levels = PriceListRules.BestByLevel(productId, clientId, chainId, date, lists);
        var generalPrice = levels.General?.Item.Price;

        var candidates = offers.Where(o => InEffect(o, date) && AppliesTo(o, clientId, chainId))
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
            if (generalPrice is decimal gp && bestItem.Price > gp)
                notices.Add(new PriceNotice(PriceNoticeCodes.OfferAboveGeneral, best.OfferId, best.Name, bestItem.Price, OtherPrice: gp));
        }
        foreach (var level in new[] { levels.Client, levels.Chain })
            if (level is not null && generalPrice is decimal g && level.Item.Price > g)
                notices.Add(new PriceNotice(PriceNoticeCodes.SpecialAboveGeneral, 0, string.Empty, level.Item.Price, OtherPrice: g,
                    PriceListId: level.List.PriceListId, PriceListName: level.List.Name));
        var n = notices.Count == 0 ? null : notices;

        ResolvedPrice? winner = null;
        void Consider(decimal price, Func<ResolvedPrice> make)
        {
            // en orden de preferencia: solo un precio estrictamente menor desplaza al que ya ganó (empate = el anterior)
            if (winner is null || price < winner.Price) winner = make();
        }
        if (levels.Client is { } cl) Consider(cl.Item.Price, () => new(cl.Item.Price, PriceSources.ClientList, cl.List.PriceListId, cl.Item.ImageFileId, null, n));
        if (levels.Chain is { } chl) Consider(chl.Item.Price, () => new(chl.Item.Price, PriceSources.ChainList, chl.List.PriceListId, chl.Item.ImageFileId, null, n));
        if (bestItem is { } oi && best is { } bo) Consider(oi.Price, () => new(oi.Price, PriceSources.Offer, null, oi.ImageFileId, bo.OfferId, n));
        if (levels.General is { } ge) Consider(ge.Item.Price, () => new(ge.Item.Price, PriceSources.GeneralList, ge.List.PriceListId, ge.Item.ImageFileId, null, n));
        if (winner is not null) return winner;
        return productSalePrice is decimal p ? new ResolvedPrice(p, PriceSources.Product) : new ResolvedPrice(null, PriceSources.None);
    }

    /// <summary>
    /// Al armar una oferta (crear, cambiar fechas o clientes, agregar renglones, publicar): las otras ofertas PUBLICADAS y activas que se cruzan en fechas y clientes y
    /// traen el mismo producto igual o más barato. No impide nada (gana el precio más bajo); la respuesta lo avisa.
    /// </summary>
    public static IReadOnlyList<OfferOverlap> FindCheaperOverlaps(OfferView candidate, IEnumerable<OfferView> others, IReadOnlyDictionary<int, int>? chainOfClient = null)
    {
        var result = new List<OfferOverlap>();
        foreach (var o in others)
        {
            if (o.OfferId == candidate.OfferId || !o.IsPublished || !o.IsActive) continue;
            if (!PriceListRules.DatesOverlap(candidate.ValidFrom, candidate.ValidTo, o.ValidFrom, o.ValidTo) || !AudiencesOverlap(candidate, o, chainOfClient)) continue;
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
        PriceNoticeCodes.SpecialAboveGeneral =>
            $"El precio especial «{n.PriceListName}» pone {sku} a {Money(n.OfferPrice)}, más caro que la lista general ({Money(n.OtherPrice ?? 0m)}): se aplica el de la lista general. Corrija el precio especial.",
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
