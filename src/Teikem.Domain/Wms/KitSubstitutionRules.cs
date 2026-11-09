namespace Teikem.Domain.Wms;

/// <summary>Lote 230 — una pieza del kit (o un candidato a sustituirla) con lo que la regla necesita.</summary>
public sealed record SubstitutionProduct(int ProductId, string Sku, int? CategoryId, decimal? SalePrice, bool IsActive, bool IsKit, int? ClientId);

/// <summary>
/// Lote 230 — reglas puras de la SUSTITUCIÓN de piezas de un kit en una orden de venta (docs/ofertas-kits-fotos-diseno.md §6, §8 y §9; sin BD).
/// Una pieza se puede cambiar por otro producto si: la pieza no está marcada «no sustituible» en el kit; el sustituto es activo, no es kit y es del mismo dueño de
/// inventario que el kit; es de la MISMA categoría exacta (mismo ProductCategoryId; si la pieza no tiene categoría no se sustituye); tiene EXACTAMENTE el mismo precio
/// de venta (Product.SalePrice; si la pieza no tiene precio no se sustituye); y va en la MISMA cantidad. El precio de la línea no cambia; la reserva, el despacho, el
/// costo y la factura usan la pieza real. Sin permiso aparte (basta sales.create). Vale para cualquier kit (ajustable o no; de oferta o regular).
/// Mensajes públicos (400): los usan el servicio, las pruebas y el manual.
/// </summary>
public static class KitSubstitutionRules
{
    public static string NotSubstitutable(string sku) => $"La pieza {sku} del kit no es sustituible.";
    public static string NotAPiece(string sku, string kitSku) => $"{sku} no es una pieza del kit {kitSku}: solo se sustituyen sus piezas.";
    public static string NoCategory(string sku) => $"La pieza {sku} no tiene categoría: solo se sustituye por un producto de la misma categoría.";
    public static string CategoryDiffers(string substituteSku, string sku) => $"{substituteSku} no es de la misma categoría que {sku}: solo se sustituye por un producto de la misma categoría.";
    public static string NoPrice(string sku) => $"La pieza {sku} no tiene precio de venta: solo se sustituye por un producto del mismo precio.";
    public static string PriceDiffers(string substituteSku, string sku) => $"{substituteSku} no tiene el mismo precio de venta que {sku}: solo se sustituye por un producto del mismo precio.";
    public static string QuantityDiffers(string sku, decimal quantity) => $"El sustituto de {sku} va en la misma cantidad que la pieza: {quantity:0.###}.";
    public static string SubstituteInactive(string sku) => $"El sustituto {sku} está inactivo.";
    public static string SubstituteIsKit(string sku) => $"{sku} es un kit: no puede sustituir una pieza.";
    public static string SubstituteOtherOwner(string sku) => $"El sustituto {sku} es de otro dueño de inventario que el kit.";
    public static string SubstitutedTwice(string sku) => $"La pieza {sku} se sustituyó más de una vez.";
    public static string SubstituteIsAnotherPiece(string substituteSku) => $"{substituteSku} ya es otra pieza del kit: no puede sustituir una pieza.";

    /// <summary>
    /// El error de cambiar <paramref name="piece"/> por <paramref name="candidate"/> (null = se puede). <paramref name="kitOwnerClientId"/> = dueño del inventario del kit.
    /// La cantidad se revisa aparte (<see cref="QuantityDiffers"/>): esta regla no la conoce.
    /// </summary>
    public static string? Check(SubstitutionProduct piece, bool pieceNotSubstitutable, SubstitutionProduct candidate, int? kitOwnerClientId)
    {
        if (pieceNotSubstitutable) return NotSubstitutable(piece.Sku);
        if (!candidate.IsActive) return SubstituteInactive(candidate.Sku);
        if (candidate.IsKit) return SubstituteIsKit(candidate.Sku);
        if (candidate.ClientId != kitOwnerClientId) return SubstituteOtherOwner(candidate.Sku);
        if (piece.CategoryId is null) return NoCategory(piece.Sku);
        if (candidate.CategoryId != piece.CategoryId) return CategoryDiffers(candidate.Sku, piece.Sku);
        if (piece.SalePrice is null) return NoPrice(piece.Sku);
        if (candidate.SalePrice != piece.SalePrice) return PriceDiffers(candidate.Sku, piece.Sku);
        return null;
    }

    /// <summary>¿Puede <paramref name="candidate"/> sustituir a <paramref name="piece"/>? (la lista de sustitutos válidos; excluye la pieza misma).</summary>
    public static bool IsValidSubstitute(SubstitutionProduct piece, bool pieceNotSubstitutable, SubstitutionProduct candidate, int? kitOwnerClientId)
        => candidate.ProductId != piece.ProductId && Check(piece, pieceNotSubstitutable, candidate, kitOwnerClientId) is null;
}
