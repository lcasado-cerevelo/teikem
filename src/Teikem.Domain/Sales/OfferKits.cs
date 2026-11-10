namespace Teikem.Domain.Sales;

/// <summary>Lote 231 — un uso de un kit de oferta en una oferta (un renglón, activo o quitado), visto por las reglas puras.</summary>
public sealed record OfferKitUse(int OfferItemId, int OfferId, string OfferName, decimal Price, bool ItemActive, bool OfferActive);

/// <summary>Lote 231 — una pieza de un kit (producto, cantidad, «no sustituible») para comparar composiciones.</summary>
public sealed record OfferKitPiece(int ProductId, decimal Quantity, bool NotSubstitutable);

/// <summary>
/// Lote 231 — reglas puras del kit de oferta con biblioteca (docs/ofertas-kits-fotos-diseno.md §10–§12; sin BD). El kit de oferta es un producto de tipo KIT sin
/// existencia que se arma al pedir, marcado Product.IsOfferKit: vive en la biblioteca de kits de oferta (no en Productos e inventario) y una oferta lo usa con un
/// renglón normal (su precio). Se vende solo mientras esté en alguna oferta publicada y vigente que aplique al cliente. Su composición (piezas, cantidades, «no
/// sustituible» y «ajustable al pedir») no cambia una vez usado en una oferta o en un pedido: cambiarla es crear un kit nuevo; el nombre sí cambia.
/// Mensajes públicos: el manual y la FAQ los citan tal cual.
/// </summary>
public static class OfferKitRules
{
    /// <summary>404: «Kit de oferta no encontrado.»</summary>
    public const string NotFound = "Kit de oferta";

    /// <summary>422: la composición de un kit ya usado no se cambia.</summary>
    public static string CompositionLocked(string sku)
        => $"La composición del kit {sku} ya no se puede cambiar porque ya se usó en una oferta o en un pedido: cree un kit nuevo con las piezas que quiera.";

    /// <summary>422: el kit de oferta no está en ninguna oferta publicada y vigente para el cliente (no sale en el catálogo ni se puede pedir).</summary>
    public static string NotOnSale(string sku) => $"El kit de oferta {sku} no está en ninguna oferta publicada y vigente para este cliente: no se puede vender.";

    /// <summary>422: un kit de oferta no va en una lista de precios.</summary>
    public static string NotInPriceLists(string sku) => $"El kit {sku} es un kit de oferta: su precio va en una oferta, no en una lista de precios.";

    /// <summary>422: un kit de oferta se cambia en Ofertas, no en Productos.</summary>
    public static string ManagedInOffers(string sku) => $"El kit {sku} es un kit de oferta: se cambia en Contabilidad → Precios y ofertas, no en Productos.";

    /// <summary>422: desde Productos ya no se convierte un producto en kit.</summary>
    public const string NoConvertFromProducts =
        "Un producto ya no se convierte en kit desde Productos: un kit que se arma al pedir se crea como kit de oferta en Contabilidad → Precios y ofertas.";

    /// <summary>400: solo un kit de oferta admite sustituir o ajustar sus piezas en un pedido.</summary>
    public static string PiecesFixed(string sku) => $"El kit {sku} no admite cambiar ni ajustar sus piezas al pedir: eso solo se puede en un kit de oferta.";

    /// <summary>422: el kit (PUT /offers/{id}/kits/{kit}) no está en esa oferta.</summary>
    public const string NotInThisOffer = "Ese kit no está en esta oferta: cámbielo desde la biblioteca de kits de oferta.";

    /// <summary>
    /// El último uso del kit (para sugerir el precio y decir en qué oferta se usó): el renglón más reciente que sigue puesto en una oferta activa; si no hay, el más
    /// reciente de todos (también los quitados). Sin usos = null.
    /// </summary>
    public static OfferKitUse? LastUse(IEnumerable<OfferKitUse> uses)
        => uses.OrderByDescending(u => u.ItemActive && u.OfferActive).ThenByDescending(u => u.OfferItemId).FirstOrDefault();

    /// <summary>¿El kit se vende hoy al cliente? Está en alguna oferta publicada, activa y vigente que le aplica (sin cliente: las de todos).</summary>
    public static bool IsOnSale(int productId, int? clientId, DateOnly date, IEnumerable<OfferView> offers)
        => offers.Any(o => OfferRules.InEffect(o, date) && OfferRules.AppliesTo(o, clientId) && o.Items.Any(i => i.ProductId == productId));

    /// <summary>
    /// ¿Cambia la composición? Mismas piezas (sin importar el orden), mismas cantidades y marcas «no sustituible», y el mismo «ajustable al pedir» = no cambia
    /// (solo reordenar las piezas no es cambiar la composición).
    /// </summary>
    public static bool CompositionChanges(IReadOnlyList<OfferKitPiece> current, bool currentAdjustable, IReadOnlyList<OfferKitPiece> wanted, bool wantedAdjustable)
        => currentAdjustable != wantedAdjustable || !current.OrderBy(p => p.ProductId).SequenceEqual(wanted.OrderBy(p => p.ProductId));
}
