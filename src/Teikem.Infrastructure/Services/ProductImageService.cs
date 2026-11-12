using Microsoft.EntityFrameworkCore;
using SkiaSharp;
using Teikem.Domain.Constants;
using Teikem.Domain.Files;
using Teikem.Domain.Sales;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Wms;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 229 — la foto del producto (Product.ImageFileId; vale para productos, kits y kits de oferta): subirla, quitarla y leer cuál se muestra. El archivo es un
/// StoredFile del producto (dueño PRODUCT); quitarla solo la desliga (el archivo no se borra). Solo JPG, PNG o WEBP, hasta Files:MaxMegabytes; una foto con el lado
/// mayor de más de 1200 px se reduce a 1200 px en su mismo formato (si no se puede leer, se guarda tal cual). Quien tiene sales.view la puede ver por GET /files/{id}
/// aunque no tenga inventory.view (StoredFileService).
/// </summary>
public sealed class ProductImageService(TeikemDbContext db, ILookupCache lookups, StoredFileService files)
{
    public async Task<ProductImageDto> GetAsync(Guid productPublicId, CancellationToken ct)
    {
        var p = await db.Set<Product>().AsNoTracking().FirstOrDefaultAsync(x => x.PublicId == productPublicId, ct) ?? throw WmsResolve.ProductNotFound();
        var kitTypeId = await lookups.TryGetIdAsync(KitCodes.ProductTypeDomain, ProductTypes.Kit, ct);
        var effective = (await PriceListQueries.ProductImagesAsync(db, new[] { p }, kitTypeId, ct))[p.ProductId];
        var publicIds = await PriceListQueries.FilePublicIdsAsync(db, new[] { p.ImageFileId, effective }, ct);
        return new ProductImageDto(p.PublicId, p.Sku, kitTypeId is not null && p.ProductTypeLookupId == kitTypeId,
            p.ImageFileId is int own && publicIds.TryGetValue(own, out var g) ? g : null, effective is int e && publicIds.TryGetValue(e, out var ge) ? ge : null);
    }

    /// <summary>Pone (o cambia) la foto del producto. 400 si no es JPG/PNG/WEBP o pasa del tope; 404 producto.</summary>
    public async Task<ProductImageDto> SetAsync(Guid productPublicId, string? fileName, ReadOnlyMemory<byte> content, CancellationToken ct)
    {
        await PriceListQueries.EnsurePhotosEnabledAsync(db, ct);   // Lote 240
        var bytes = PreparePhoto(content);
        var productId = await db.Set<Product>().AsNoTracking().Where(x => x.PublicId == productPublicId).Select(x => (int?)x.ProductId).FirstOrDefaultAsync(ct)
                        ?? throw WmsResolve.ProductNotFound();
        var file = await files.AttachAsync(EntityTypes.Product, productId, StoredFileCodes.KindDocument, fileName, null, bytes, ct, enforceOwnerWrite: false);
        var fileId = await db.Set<StoredFile>().AsNoTracking().Where(f => f.PublicId == file.PublicId).Select(f => f.StoredFileId).FirstAsync(ct);
        await SetIdAsync(productPublicId, fileId, ct);
        return await GetAsync(productPublicId, ct);
    }

    /// <summary>Quita la foto del producto (la desliga; el archivo no se borra).</summary>
    public async Task<ProductImageDto> ClearAsync(Guid productPublicId, CancellationToken ct)
    {
        await SetIdAsync(productPublicId, null, ct);
        return await GetAsync(productPublicId, ct);
    }

    private async Task SetIdAsync(Guid productPublicId, int? fileId, CancellationToken ct)
    {
        await db.RunInTransactionAsync(async ct2 =>
        {
            var p = await db.Set<Product>().AsTracking().FirstOrDefaultAsync(x => x.PublicId == productPublicId, ct2) ?? throw WmsResolve.ProductNotFound();
            p.ImageFileId = fileId;
            await db.SaveGuardedAsync(DbExtensions.ConcurrencyMessage, ct2);
        }, ct);
    }

    // ---------------------------------------------------------------- la foto

    /// <summary>
    /// Valida que sea una foto JPG, PNG o WEBP (por su contenido, no por lo que diga el cliente) y, si su lado mayor pasa de 1200 px, la reduce en su mismo formato
    /// respetando la orientación de la cámara. Si no se puede leer o reducir, devuelve el original (el almacén de archivos aplica el tope de tamaño).
    /// </summary>
    internal static byte[] PreparePhoto(ReadOnlyMemory<byte> content)
    {
        if (content.Length == 0) throw new ValidationException("file", ProductImageRules.ImageRequired);
        var type = StoredFileRules.SniffContentType(content.Span);
        if (!ProductImageRules.IsAllowed(type)) throw new ValidationException("file", ProductImageRules.NotAPhoto);
        var original = content.ToArray();
        return TryShrink(original, type!, ProductImageRules.MaxSidePx) ?? original;
    }

    /// <summary>
    /// ¿Está la librería nativa de imágenes? Se pregunta sin crear objetos de Skia: si falta, crear uno deja un finalizador que tumba el proceso (pasa en Linux sin
    /// SkiaSharp.NativeAssets.Linux; en Windows viaja con el paquete).
    /// </summary>
    private static readonly Lazy<bool> SkiaAvailable = new(() =>
    {
        try { return SkiaSharpVersion.Native.Major > 0; }
        catch { return false; }
    });

    private static byte[]? TryShrink(byte[] image, string contentType, int maxSide)
    {
        if (!SkiaAvailable.Value) return null;
        try
        {
            using var codec = SKCodec.Create(new SKMemoryStream(image));
            if (codec is null) return null;
            var info = codec.Info;
            if (info.Width <= 0 || info.Height <= 0 || Math.Max(info.Width, info.Height) <= maxSide) return null;
            var origin = codec.EncodedOrigin;
            using var decoded = SKBitmap.Decode(codec);
            if (decoded is null) return null;
            var scale = (double)maxSide / Math.Max(decoded.Width, decoded.Height);
            var w = Math.Max(1, (int)Math.Round(decoded.Width * scale));
            var h = Math.Max(1, (int)Math.Round(decoded.Height * scale));
            using var resized = decoded.Resize(new SKImageInfo(w, h, decoded.ColorType, decoded.AlphaType), SKFilterQuality.High);
            if (resized is null) return null;
            using var oriented = Orient(resized, origin);
            var format = contentType switch
            {
                "image/png" => SKEncodedImageFormat.Png,
                "image/webp" => SKEncodedImageFormat.Webp,
                _ => SKEncodedImageFormat.Jpeg,
            };
            using var img = SKImage.FromBitmap(oriented);
            using var data = img.Encode(format, 85);
            var bytes = data?.ToArray();
            return bytes is { Length: > 0 } && StoredFileRules.SniffContentType(bytes) == contentType ? bytes : null;
        }
        catch
        {
            return null;   // sin la librería nativa o con una imagen que no se lee: se guarda el original
        }
    }

    /// <summary>Aplica la orientación EXIF (la foto reducida se guarda sin metadatos). Devuelve una copia siempre (quien llama la libera).</summary>
    private static SKBitmap Orient(SKBitmap src, SKEncodedOrigin origin)
    {
        var swap = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var dst = new SKBitmap(swap ? src.Height : src.Width, swap ? src.Width : src.Height, src.ColorType, src.AlphaType);
        using var canvas = new SKCanvas(dst);
        switch (origin)
        {
            case SKEncodedOrigin.TopRight: canvas.Translate(dst.Width, 0); canvas.Scale(-1, 1); break;
            case SKEncodedOrigin.BottomRight: canvas.Translate(dst.Width, dst.Height); canvas.RotateDegrees(180); break;
            case SKEncodedOrigin.BottomLeft: canvas.Translate(0, dst.Height); canvas.Scale(1, -1); break;
            case SKEncodedOrigin.LeftTop: canvas.RotateDegrees(90); canvas.Scale(1, -1); break;
            case SKEncodedOrigin.RightTop: canvas.Translate(dst.Width, 0); canvas.RotateDegrees(90); break;
            case SKEncodedOrigin.RightBottom: canvas.Translate(dst.Width, dst.Height); canvas.RotateDegrees(90); canvas.Scale(-1, 1); break;
            case SKEncodedOrigin.LeftBottom: canvas.Translate(0, dst.Height); canvas.RotateDegrees(270); break;
        }
        canvas.DrawBitmap(src, 0, 0);
        canvas.Flush();
        return dst;
    }
}

/// <summary>Lote 229 — la oferta como dueña de archivos (su foto y las de sus renglones): existe en la compañía si la ve el filtro global.</summary>
public sealed class OfferOwnedEntityResolver(TeikemDbContext db) : IOwnedEntityResolver
{
    public string EntityTypeCode => OfferCodes.EntityType;
    public Task<bool> ExistsInTenantAsync(int id, CancellationToken ct) => db.Set<Offer>().AsNoTracking().AnyAsync(o => o.OfferId == id, ct);
}
