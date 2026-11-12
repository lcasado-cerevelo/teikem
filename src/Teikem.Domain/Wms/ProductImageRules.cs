namespace Teikem.Domain.Wms;

/// <summary>
/// Lote 229 — la foto del producto (Product.ImageFileId; vale igual para kits y kits de oferta) y la regla de qué foto se muestra en listas, ofertas y en el
/// catálogo del pedido. Mensajes públicos: el manual y la FAQ los citan tal cual.
/// </summary>
public static class ProductImageRules
{
    public const string ImageRequired = "Adjunte la foto.";
    /// <summary>Lote 240: la compañía apagó «Fotos de productos» (Ajustes → Operación): no se suben fotos.</summary>
    public const string PhotosDisabled = "La compañía no maneja fotos de productos; actívelo en Ajustes → Operación para subirlas.";
    public const string NotAPhoto = "La foto debe ser una imagen JPG, PNG o WEBP.";
    /// <summary>Lado mayor (px) al que se reduce una foto más grande al subirla (si no se puede leer, se guarda tal cual).</summary>
    public const int MaxSidePx = 1200;

    /// <summary>Tipos admitidos (por el contenido, no por lo que diga el cliente).</summary>
    public static readonly IReadOnlyList<string> AllowedTypes = new[] { "image/jpeg", "image/png", "image/webp" };

    public static bool IsAllowed(string? sniffedContentType)
        => sniffedContentType is not null && AllowedTypes.Contains(sniffedContentType, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Qué foto se muestra, en este orden: la del renglón (lista u oferta) si se le puso una → la del producto → si es un kit sin foto, la de sus piezas (la primera,
    /// por orden, que tenga foto) → ninguna.
    /// </summary>
    public static int? Resolve(int? rowImage, int? productImage, bool isKit, int? firstComponentImage)
        => rowImage ?? productImage ?? (isKit ? firstComponentImage : null);
}
