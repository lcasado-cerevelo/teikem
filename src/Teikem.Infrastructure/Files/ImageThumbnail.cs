using SkiaSharp;

namespace Teikem.Infrastructure.Files;

/// <summary>Lote 212 (B) — la miniatura de una foto del chat (JPEG, lado mayor de 320 px). Si la imagen no se puede leer devuelve null: el mensaje se guarda igual, sin miniatura.</summary>
public static class ImageThumbnail
{
    public static byte[]? TryCreate(ReadOnlySpan<byte> image, int maxSide = 320)
    {
        try
        {
            using var bitmap = SKBitmap.Decode(image);
            if (bitmap is null || bitmap.Width == 0 || bitmap.Height == 0) return null;
            var scale = Math.Min(1d, (double)maxSide / Math.Max(bitmap.Width, bitmap.Height));
            var w = Math.Max(1, (int)Math.Round(bitmap.Width * scale));
            var h = Math.Max(1, (int)Math.Round(bitmap.Height * scale));
            using var resized = bitmap.Resize(new SKImageInfo(w, h), SKFilterQuality.Medium);
            if (resized is null) return null;
            using var data = SKImage.FromBitmap(resized).Encode(SKEncodedImageFormat.Jpeg, 70);
            return data?.ToArray();
        }
        catch
        {
            return null;
        }
    }
}
