using System.Globalization;
using Teikem.Domain.Common;

namespace Teikem.Domain.Sales;

/// <summary>Constantes de las listas de precios (Lote 221). Viven aquí para que el lote se pueda pasar solo a otra rama.</summary>
public static class PriceListCodes
{
    /// <summary>EntityType de la bitácora (sembrado en la sección 2026-11-07 de update.sql).</summary>
    public const string EntityType = "PRICE_LIST";
    public const int MaxNameLength = 120;
    public const int MaxNotesLength = 500;
    public const int MaxItems = 2000;
    public const int MaxClients = 2000;
    /// <summary>Lote 247: tope de cadenas por lista (los clientes siguen en 2000).</summary>
    public const int MaxChains = 500;
}

/// <summary>
/// Estado que se MUESTRA de una lista (no se guarda: sale de «publicada» y de las fechas). DRAFT = borrador (no se aplica); SCHEDULED = publicada, empieza más
/// adelante; ACTIVE = publicada y vigente hoy; EXPIRED = publicada y ya vencida.
/// </summary>
public static class PriceListStates
{
    public const string Draft = "DRAFT";
    public const string Scheduled = "SCHEDULED";
    public const string Active = "ACTIVE";
    public const string Expired = "EXPIRED";
}

/// <summary>De dónde salió el precio de una línea: lista del cliente, lista general, oferta (Lote 229), precio de venta del producto, o ninguno (no tiene precio).</summary>
public static class PriceSources
{
    public const string ClientList = "CLIENT_LIST";
    /// <summary>Lote 247: una lista (normal o el precio especial) de la cadena del cliente.</summary>
    public const string ChainList = "CHAIN_LIST";
    /// <summary>Lote 229: una oferta vigente que aplica al cliente (ganó por ser el precio más bajo).</summary>
    public const string Offer = "OFFER";
    public const string GeneralList = "GENERAL_LIST";
    public const string Product = "PRODUCT";
    public const string None = "NONE";
}

/// <summary>
/// Lote 221 — lista de precios (la establece el supervisor de ventas, permiso sales.pricelist). Tiene vigencia (desde obligatorio, hasta opcional), se publica
/// (borrador = no se aplica) y aplica a todos los clientes o solo a los escogidos (precios negociados). Al pedir, el precio sale de la lista vigente del cliente,
/// si no de la lista vigente general y si no del precio de venta del producto. Nunca DELETE: se desactiva.
/// </summary>
[AuditEntity(PriceListCodes.EntityType)]
public class PriceList : ITenantScoped, ISoftDeletable, IAuditStamped
{
    public int PriceListId { get; set; }
    [NotAudited] public Guid PublicId { get; set; }
    public int TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateOnly ValidFrom { get; set; }
    public DateOnly? ValidTo { get; set; }
    public bool IsPublished { get; set; }
    public bool AppliesToAll { get; set; } = true;
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
    [NotAudited] public DateTime CreatedAtUtc { get; set; }
    public int? CreatedBy { get; set; }
    [NotAudited] public DateTime? UpdatedAtUtc { get; set; }
    public int? UpdatedBy { get; set; }
    [NotAudited] public byte[]? RowVersion { get; set; }

    // --- Lote 247 (Lote C): lista DEDICADA = el precio especial de un cliente o de una cadena (uno solo de los dos; CK_PriceList_SpecialOwner exige además
    // AppliesToAll = 0). Lleva también su fila PriceListClient o PriceListChain, así la resolución del precio no necesita conocer la marca.
    public int? SpecialClientId { get; set; }
    public int? SpecialChainId { get; set; }
    /// <summary>¿Es el precio especial (lista dedicada) de un cliente o de una cadena? (no se guarda: sale de las dos columnas).</summary>
    public bool IsSpecial => SpecialClientId != null || SpecialChainId != null;

    public List<PriceListItem> Items { get; set; } = new();
    public List<PriceListClient> Clients { get; set; } = new();
    /// <summary>Lote 247: cadenas a las que aplica una lista que NO es general.</summary>
    public List<PriceListChain> Chains { get; set; } = new();
}

/// <summary>Renglón de una lista: un producto o un kit (incluida una oferta) a un precio, con foto opcional (StoredFile del producto). Quitar = IsActive 0.</summary>
[AuditEntity(PriceListCodes.EntityType)]
public class PriceListItem : ITenantScoped, ISoftDeletable
{
    public int PriceListItemId { get; set; }
    public int TenantId { get; set; }
    public int PriceListId { get; set; }
    public int ProductId { get; set; }
    public decimal Price { get; set; }
    public int? ImageFileId { get; set; }
    public bool IsActive { get; set; } = true;
    [NotAudited] public DateTime CreatedAtUtc { get; set; }
}

/// <summary>Cliente al que aplica una lista que NO es general. Quitar = IsActive 0.</summary>
[AuditEntity(PriceListCodes.EntityType)]
public class PriceListClient : ITenantScoped, ISoftDeletable
{
    public int PriceListClientId { get; set; }
    public int TenantId { get; set; }
    public int PriceListId { get; set; }
    public int ClientId { get; set; }
    public bool IsActive { get; set; } = true;
    [NotAudited] public DateTime CreatedAtUtc { get; set; }
}

/// <summary>Lote 247 — cadena a la que aplica una lista que NO es general (alcanza a los clientes de la cadena mientras esté activa). Quitar = IsActive 0.</summary>
[AuditEntity(PriceListCodes.EntityType)]
public class PriceListChain : ITenantScoped, ISoftDeletable
{
    public int PriceListChainId { get; set; }
    public int TenantId { get; set; }
    public int PriceListId { get; set; }
    public int ChainId { get; set; }
    public bool IsActive { get; set; } = true;
    [NotAudited] public DateTime CreatedAtUtc { get; set; }
}

/// <summary>Un renglón visto por las reglas puras.</summary>
public sealed record PriceListItemView(int ProductId, decimal Price, int? ImageFileId = null);

/// <summary>
/// Una lista vista por las reglas puras (solo lo vigente: renglones, clientes y cadenas activos). Lote 247: ChainIds = las cadenas de la lista (null = ninguna) e
/// IsSpecial = es el precio especial (lista dedicada) de un cliente o de una cadena.
/// </summary>
public sealed record PriceListView(int PriceListId, string Name, bool IsPublished, bool AppliesToAll, DateOnly ValidFrom, DateOnly? ValidTo,
    IReadOnlyCollection<int> ClientIds, IReadOnlyList<PriceListItemView> Items, IReadOnlyCollection<int>? ChainIds = null, bool IsSpecial = false, string? OwnerName = null);   // OwnerName: el cliente o la cadena dueña de la lista dedicada (para armar el aviso en el idioma de la web)

/// <summary>Lote 247 — el renglón que ganó dentro de un nivel (cliente, cadena o general) y su lista.</summary>
public sealed record PriceLevelPick(PriceListView List, PriceListItemView Item);

/// <summary>Lote 247 — lo mejor de cada nivel para un producto: la lista del cliente, la de su cadena y la general (null = ese nivel no trae el producto).</summary>
public sealed record PriceLevels(PriceLevelPick? Client, PriceLevelPick? Chain, PriceLevelPick? General);

/// <summary>
/// Lote 247 — la copia del origen del precio que se guarda en la línea de la orden (SalesOrderLine.PriceSource, PriceListId, OfferId, PriceChainId,
/// ResolvedUnitPrice). PriceChainId solo va con CHAIN_LIST.
/// </summary>
public sealed record PriceOrigin(string Source, int? PriceListId, int? OfferId, int? PriceChainId, decimal? ResolvedUnitPrice);

/// <summary>Lote 247 — reglas puras de la copia del origen del precio en la línea.</summary>
public static class PriceOriginRules
{
    /// <summary>La copia del precio resuelto: de dónde salió, la lista o la oferta, la cadena (solo si salió de una lista de cadena) y el precio resuelto.</summary>
    public static PriceOrigin Copy(ResolvedPrice r, int? chainId)
        => new(r.Source, r.PriceListId, r.OfferId, r.Source == PriceSources.ChainList ? chainId : null, r.Price);
}

/// <summary>
/// Precio resuelto: el precio (null = no hay), de dónde salió, la lista (si salió de una) y la foto del renglón de esa lista (o de la oferta). Lote 229: la oferta
/// (si salió de una) y los avisos de precio (null = ninguno; ver <see cref="OfferRules.Resolve"/>).
/// </summary>
public sealed record ResolvedPrice(decimal? Price, string Source, int? PriceListId = null, int? ImageFileId = null, int? OfferId = null, IReadOnlyList<PriceNotice>? Notices = null);

/// <summary>Un cruce de dos listas publicadas: la otra lista y los productos que tendrían dos precios a la vez.</summary>
public sealed record PriceListConflict(int OtherPriceListId, string OtherName, IReadOnlyList<int> ProductIds);

/// <summary>
/// Lote 221 — reglas puras de las listas de precios (sin EF; PriceListService y SalesOrderService las traducen a 400/404/409/422). Mensajes públicos: el manual
/// y la FAQ los citan tal cual.
/// </summary>
public static class PriceListRules
{
    public const string NameRequired = "Indique el nombre de la lista de precios.";
    public static readonly string NameTooLong = $"El nombre de la lista no puede pasar de {PriceListCodes.MaxNameLength} caracteres.";
    public const string ValidFromRequired = "Indique desde qué fecha vale la lista.";
    public const string DateRange = "La fecha «hasta» no puede ser anterior a la fecha «desde».";
    public static readonly string NotesTooLong = $"Las notas no pueden pasar de {PriceListCodes.MaxNotesLength} caracteres.";
    public const string NotFound = "Lista de precios";
    public const string ItemNotFound = "Renglón de la lista";
    public const string Inactive = "La lista de precios está desactivada: reactívela para cambiarla.";
    public const string ItemsRequired = "Indique los productos y sus precios.";
    public static readonly string TooManyItems = $"Una lista admite como máximo {PriceListCodes.MaxItems} renglones.";
    public const string ItemProductRequired = "Escoja el producto o kit de cada renglón.";
    public const string ItemPriceRequired = "Indique el precio de cada renglón.";
    public const string ItemProductNotOwn = "Solo se ponen en una lista productos propios de la compañía.";
    public static string ItemProductInactive(string sku) => $"El producto {sku} está inactivo: no se puede poner en la lista.";
    public static string ItemRepeated(string sku) => $"El producto {sku} viene repetido.";
    public const string ClientsRequired = "Escoja los clientes de la lista.";
    public static readonly string TooManyClients = $"Una lista admite como máximo {PriceListCodes.MaxClients} clientes.";
    public const string ClientsOnGeneralList = "La lista aplica a todos los clientes: no lleva clientes escogidos. Desmarque «aplica a todos» para escogerlos.";
    public const string GeneralWithClients = "La lista tiene clientes o cadenas escogidos: quítelos antes de hacerla general (aplica a todos).";
    public const string ClientNotInList = "Cliente de la lista";
    public const string PublishNeedsItems = "Agregue al menos un renglón antes de publicar la lista.";
    public const string PublishNeedsClients = "Escoja al menos un cliente o una cadena, o marque que la lista aplica a todos los clientes.";
    // Lote 247: cadenas en las listas y listas dedicadas (precios especiales)
    public const string ChainsRequired = "Escoja las cadenas de la lista.";
    public static readonly string TooManyChains = $"Una lista admite como máximo {PriceListCodes.MaxChains} cadenas.";
    public const string ChainsOnGeneralList = "La lista aplica a todos los clientes: no lleva cadenas escogidas. Desmarque «aplica a todos» para escogerlas.";
    public const string ChainNotInList = "Cadena de la lista";
    public const string AlreadyPublished = "La lista ya está publicada.";
    public const string NotPublished = "La lista ya está en borrador.";
    public const string AlreadyInactive = "La lista ya está desactivada.";
    public const string AlreadyActive = "La lista ya está activa.";
    public const string ImageRequired = "Adjunte la foto.";
    public const string ImageNotImage = "La foto debe ser una imagen (JPG, PNG, WEBP o GIF).";
    public const string Concurrency = "La lista cambió al mismo tiempo. Recárguela e intente de nuevo.";

    /// <summary>Lote 247: 422 al cambiar el nombre, las fechas o a quién aplica una lista dedicada desde los endpoints genéricos de listas.</summary>
    public static string DedicatedLocked(string owner)
        => $"Esta lista es el precio especial de «{owner}»: el nombre, las fechas y a quién aplica se cambian en «Precios especiales» de su ficha.";

    /// <summary>Lote 247: aviso (no bloquea) de un precio especial más caro que la lista general vigente.</summary>
    public static string SpecialAboveGeneral(string sku, decimal price, decimal general)
        => $"Precio especial más caro que la lista general: {sku} a {OfferRules.Money(price)} y en la lista general a {OfferRules.Money(general)}. Se cobra el más bajo; corrija el precio especial.";

    /// <summary>Dos listas publicadas que se cruzan en fechas y clientes (o cadenas) no pueden dar dos precios al mismo producto. Se citan hasta 5 SKU.</summary>
    public static string Overlap(string otherName, IReadOnlyList<string> skus)
    {
        var shown = string.Join(", ", skus.Take(5));
        var more = skus.Count > 5 ? $" y {skus.Count - 5} más" : string.Empty;
        return $"La lista se cruza con «{otherName}» en las mismas fechas y para los mismos clientes o cadenas en: {shown}{more}. Cambie las fechas o quite esos productos de una de las dos.";
    }

    /// <summary>Recorta el nombre; vacío = error.</summary>
    public static (string? Value, string? Error) NormalizeName(string? name)
    {
        var v = name?.Trim();
        if (string.IsNullOrEmpty(v)) return (null, NameRequired);
        if (v.Length > PriceListCodes.MaxNameLength) return (null, NameTooLong);
        return (v, null);
    }

    /// <summary>Recorta las notas; vacías = null.</summary>
    public static (string? Value, string? Error) NormalizeNotes(string? notes)
    {
        var v = notes?.Trim();
        if (string.IsNullOrEmpty(v)) return (null, null);
        return v.Length > PriceListCodes.MaxNotesLength ? (null, NotesTooLong) : (v, null);
    }

    /// <summary>«hasta» (opcional) no antes de «desde».</summary>
    public static string? DateRangeError(DateOnly from, DateOnly? to) => to is DateOnly t && t < from ? DateRange : null;

    /// <summary>¿La fecha cae dentro de la vigencia? Ambos extremos incluidos; «hasta» NULL = sin fin.</summary>
    public static bool InEffect(DateOnly from, DateOnly? to, DateOnly date) => from <= date && (to is null || date <= to.Value);

    /// <summary>Estado que se muestra: borrador, programada, vigente o vencida.</summary>
    public static string StateOf(bool isPublished, DateOnly from, DateOnly? to, DateOnly today)
    {
        if (!isPublished) return PriceListStates.Draft;
        if (today < from) return PriceListStates.Scheduled;
        if (to is DateOnly t && today > t) return PriceListStates.Expired;
        return PriceListStates.Active;
    }

    /// <summary>¿Se cruzan dos vigencias? (extremos incluidos; NULL = sin fin).</summary>
    public static bool DatesOverlap(DateOnly aFrom, DateOnly? aTo, DateOnly bFrom, DateOnly? bTo)
        => (aTo is null || bFrom <= aTo.Value) && (bTo is null || aFrom <= bTo.Value);

    /// <summary>
    /// ¿Las dos listas chocan por alcanzar a los mismos clientes? Lote 247 (DECISIÓN 14): solo si son del MISMO tipo; una dedicada (precio especial) y una normal
    /// conviven y gana el precio más bajo. Dos generales sí chocan; dos no generales, si comparten un cliente o una cadena (dos dedicadas, si son del mismo dueño);
    /// una general y una de clientes o cadenas NO. Una lista de cliente y una de cadena tampoco (aunque el cliente sea de esa cadena): gana el más bajo.
    /// </summary>
    public static bool SameAudience(PriceListView a, PriceListView b)
    {
        if (a.IsSpecial != b.IsSpecial) return false;
        if (a.AppliesToAll != b.AppliesToAll) return false;
        if (a.AppliesToAll) return true;
        if (a.ClientIds.Any(b.ClientIds.Contains)) return true;
        var bChains = b.ChainIds ?? Array.Empty<int>();
        return (a.ChainIds ?? Array.Empty<int>()).Any(bChains.Contains);
    }

    /// <summary>
    /// Los cruces de una lista (que se va a publicar o ya lo está) con las demás PUBLICADAS: misma audiencia, fechas que se cruzan y productos en común. Sin cruces =
    /// lista vacía. Así ningún producto tiene dos precios a la vez para el mismo cliente desde el mismo nivel (cliente o general).
    /// </summary>
    public static IReadOnlyList<PriceListConflict> FindConflicts(PriceListView candidate, IEnumerable<PriceListView> others)
    {
        var mine = candidate.Items.Select(i => i.ProductId).ToHashSet();
        var result = new List<PriceListConflict>();
        foreach (var o in others)
        {
            if (o.PriceListId == candidate.PriceListId || !o.IsPublished) continue;
            if (!DatesOverlap(candidate.ValidFrom, candidate.ValidTo, o.ValidFrom, o.ValidTo) || !SameAudience(candidate, o)) continue;
            var shared = o.Items.Select(i => i.ProductId).Where(mine.Contains).Distinct().OrderBy(x => x).ToList();
            if (shared.Count > 0) result.Add(new PriceListConflict(o.PriceListId, o.Name, shared));
        }
        return result;
    }

    /// <summary>
    /// Lote 247 — lo mejor de cada nivel para un producto en una fecha, entre las listas PUBLICADAS y vigentes que lo traen: CLIENTE (la lista incluye al cliente),
    /// CADENA (no lo incluye a él, pero sí a su cadena <paramref name="chainId"/>; null = sin cadena o cadena inactiva) y GENERAL (aplica a todos). Dentro de un nivel
    /// gana el precio MÁS BAJO; a igual precio, la lista que empezó más tarde y, a igual fecha, la más nueva (DECISIÓN 15).
    /// </summary>
    public static PriceLevels BestByLevel(int productId, int? clientId, int? chainId, DateOnly date, IEnumerable<PriceListView> lists)
    {
        PriceLevelPick? client = null, chain = null, general = null;
        foreach (var l in lists)
        {
            if (!l.IsPublished || !InEffect(l.ValidFrom, l.ValidTo, date)) continue;
            var item = l.Items.FirstOrDefault(i => i.ProductId == productId);
            if (item is null) continue;
            var pick = new PriceLevelPick(l, item);
            if (l.AppliesToAll) general = Lower(pick, general);
            else if (clientId is int c && l.ClientIds.Contains(c)) client = Lower(pick, client);
            else if (chainId is int ch && l.ChainIds is { } chains && chains.Contains(ch)) chain = Lower(pick, chain);
        }
        return new PriceLevels(client, chain, general);
    }

    /// <summary>
    /// El precio de un producto (o kit) para un cliente en una fecha, solo con listas. Lote 247: gana el MÁS BAJO entre la lista del cliente, la de su cadena
    /// (<paramref name="chainId"/>) y la general (a igual precio: cliente, cadena, general); si ninguna lo trae, el precio de venta del producto; si tampoco hay, sin
    /// precio. Dentro de un nivel, ver <see cref="BestByLevel"/>.
    /// </summary>
    public static ResolvedPrice Resolve(int productId, int? clientId, DateOnly date, IEnumerable<PriceListView> lists, decimal? productSalePrice, int? chainId = null)
    {
        var levels = BestByLevel(productId, clientId, chainId, date, lists);
        (PriceLevelPick Pick, string Source)? best = null;
        foreach (var (pick, source) in new[] { (levels.Client, PriceSources.ClientList), (levels.Chain, PriceSources.ChainList), (levels.General, PriceSources.GeneralList) })
            if (pick is not null && (best is null || pick.Item.Price < best.Value.Pick.Item.Price)) best = (pick, source);
        if (best is { } b) return new ResolvedPrice(b.Pick.Item.Price, b.Source, b.Pick.List.PriceListId, b.Pick.Item.ImageFileId);
        return productSalePrice is decimal p ? new ResolvedPrice(p, PriceSources.Product) : new ResolvedPrice(null, PriceSources.None);
    }

    /// <summary>
    /// Cambió el cliente de un borrador sin mandar líneas: la línea que tenía el precio resuelto para el cliente anterior toma el del nuevo; la que tenía otro precio
    /// (una excepción con sales.price) lo conserva; sin precio resuelto para el nuevo, se queda el que tenía.
    /// </summary>
    public static decimal RepriceOnClientChange(decimal current, decimal? resolvedForOld, decimal? resolvedForNew)
        => resolvedForOld is decimal o && o == current && resolvedForNew is decimal n ? n : current;

    /// <summary>
    /// ¿El precio escrito en una línea exige sales.price? No, si es el resuelto (lista o producto) o el que la línea ya tenía en el borrador (lo que no cambia no se
    /// vuelve a autorizar). Sí en cualquier otro caso.
    /// </summary>
    public static bool NeedsPricePermission(decimal given, decimal? resolved, decimal? previous)
        => !(resolved is decimal r && r == given) && !(previous is decimal p && p == given);

    /// <summary>Dentro de un nivel: el precio más bajo; a igual precio, la lista que empezó más tarde y, a igual fecha, la más nueva.</summary>
    private static PriceLevelPick Lower(PriceLevelPick candidate, PriceLevelPick? current)
    {
        if (current is null || candidate.Item.Price < current.Item.Price) return candidate;
        if (candidate.Item.Price > current.Item.Price) return current;
        return Later(candidate.List, current.List) ? candidate : current;
    }

    private static bool Later(PriceListView candidate, PriceListView current)
        => candidate.ValidFrom > current.ValidFrom || (candidate.ValidFrom == current.ValidFrom && candidate.PriceListId > current.PriceListId);
}

/// <summary>
/// OBSOLETO desde el Lote 229 (la oferta es ahora una entidad aparte, <see cref="OfferRules"/>): el catálogo y las ventas ya no leen IsPromotion/PromoFrom/PromoTo.
/// Solo lo usan los endpoints viejos /products/{id}/promo, que siguen funcionando hasta que el frontend deje de llamarlos.
/// Lote 221 — reglas puras de las ofertas (kits en promoción): solo un kit puede ser oferta; fuera de su vigencia no se vende (no se puede poner en una línea
/// nueva ni en un borrador que se vuelve a guardar), pero las órdenes ya hechas no cambian.
/// </summary>
public static class KitPromoRules
{
    public const string OnlyKits = "Solo un kit puede ser oferta (promoción).";
    public const string DateRange = "La fecha «hasta» de la oferta no puede ser anterior a la fecha «desde».";
    public const string ImageNeedsPromotion = "Marque el kit como oferta antes de ponerle la foto.";

    /// <summary>¿Se puede vender hoy? Lo que no es oferta, siempre; la oferta, solo dentro de su vigencia (extremos opcionales e incluidos).</summary>
    public static bool IsSellable(bool isPromotion, DateOnly? from, DateOnly? to, DateOnly date)
        => !isPromotion || ((from is null || from.Value <= date) && (to is null || date <= to.Value));

    /// <summary>Validación de la vigencia: «hasta» no antes de «desde».</summary>
    public static string? DatesError(DateOnly? from, DateOnly? to) => from is DateOnly f && to is DateOnly t && t < f ? DateRange : null;

    /// <summary>Mensaje de la oferta fuera de vigencia, con sus fechas (dd/MM/yyyy).</summary>
    public static string NotInEffect(string sku, DateOnly? from, DateOnly? to)
    {
        static string D(DateOnly d) => d.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        var range = (from, to) switch
        {
            (DateOnly f, DateOnly t) => $"del {D(f)} al {D(t)}",
            (DateOnly f, null) => $"desde el {D(f)}",
            (null, DateOnly t) => $"hasta el {D(t)}",
            _ => "sin fechas",
        };
        return $"La oferta {sku} no está vigente hoy (vale {range}): no se puede vender.";
    }
}
