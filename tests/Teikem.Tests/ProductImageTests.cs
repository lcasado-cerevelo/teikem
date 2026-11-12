using Microsoft.EntityFrameworkCore;
using SkiaSharp;
using Teikem.Domain.Constants;
using Teikem.Domain.Files;
using Teikem.Domain.Sales;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Files;
using Xunit;

namespace Teikem.Tests;

// Lote 229: la foto del producto — qué foto se muestra (renglón → producto → primera pieza con foto si es kit → ninguna), solo JPG/PNG/WEBP, se reduce a 1200 px
// y quien tiene sales.view la ve por GET /files/{id} aunque no tenga inventory.view.
public sealed class ProductImageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "teikem-photos-" + Guid.NewGuid().ToString("N"));
    private static readonly byte[] Png = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
    private static readonly byte[] Jpeg = { 0xFF, 0xD8, 0xFF, 0xE0, 0, 0x10, (byte)'J', (byte)'F', (byte)'I', (byte)'F', 0, 1, 2, 3 };
    private static readonly byte[] Gif = { (byte)'G', (byte)'I', (byte)'F', (byte)'8', (byte)'9', (byte)'a', 1, 0, 1, 0, 0, 0, 0 };

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, true); } catch { /* temporal */ }
    }

    // Lote 240: la compañía puede NO manejar fotos de productos. Apagado: subir es 422 y el API no devuelve ninguna foto (ni en la ficha ni en la lista); las ya subidas se conservan y
    // reaparecen al volver a encender.
    [Fact]
    public async Task A_company_without_photos_cannot_upload_and_the_api_returns_none_but_keeps_them()
    {
        await using var x = await PriceWorld.CreateAsync(_root);
        var strips = await x.ProductAsync("52800", 6.99m, "PRODIGY");
        var uploaded = await x.Images.SetAsync(strips.PublicId, "tirillas.jpg", Jpeg, default);
        Assert.NotNull(uploaded.ImagePublicId);

        var tenant = await x.F.Db.Tenants.AsTracking().SingleAsync(t => t.TenantId == WmsFixture.TenantId);
        tenant.ProductPhotosEnabled = false;
        await x.F.Db.SaveChangesAsync();

        var off = await x.Images.GetAsync(strips.PublicId, default);
        Assert.Null(off.ImagePublicId);
        Assert.Null(off.EffectiveImagePublicId);
        Assert.Equal(ProductImageRules.PhotosDisabled, (await Assert.ThrowsAsync<StatusRuleException>(() => x.Images.SetAsync(strips.PublicId, "otra.jpg", Jpeg, default))).Message);

        tenant.ProductPhotosEnabled = true;
        await x.F.Db.SaveChangesAsync();
        Assert.Equal(uploaded.ImagePublicId, (await x.Images.GetAsync(strips.PublicId, default)).ImagePublicId);
    }

    [Fact]
    public void The_photo_shown_is_the_row_then_the_product_then_the_first_piece_for_a_kit()
    {
        Assert.Equal(1, ProductImageRules.Resolve(1, 2, true, 3));
        Assert.Equal(2, ProductImageRules.Resolve(null, 2, true, 3));
        Assert.Equal(3, ProductImageRules.Resolve(null, null, true, 3));
        Assert.Null(ProductImageRules.Resolve(null, null, false, 3));   // un producto normal no toma la de nadie
        Assert.Null(ProductImageRules.Resolve(null, null, true, null));
        Assert.True(ProductImageRules.IsAllowed("image/webp"));
        Assert.False(ProductImageRules.IsAllowed("image/gif"));
        Assert.False(ProductImageRules.IsAllowed("application/pdf"));
    }

    [Fact]
    public async Task The_product_photo_is_used_by_the_catalog_lists_and_offers_and_a_kit_without_photo_takes_its_first_piece()
    {
        await using var x = await PriceWorld.CreateAsync(_root);
        var strips = await x.ProductAsync("52800", 6.99m, "PRODIGY");
        var monitor = await x.ProductAsync("070120", 7.99m, "PRODIGY");
        var kit = await x.KitAsync("KIT1", 40m, "PRODIGY", (strips, 6m), (monitor, 1m));

        Assert.Equal(ProductImageRules.NotAPhoto, (await Assert.ThrowsAsync<ValidationException>(() => x.Images.SetAsync(strips.PublicId, "a.gif", Gif, default))).Message);
        Assert.Equal(ProductImageRules.ImageRequired, (await Assert.ThrowsAsync<ValidationException>(() => x.Images.SetAsync(strips.PublicId, "a.png", Array.Empty<byte>(), default))).Message);

        var monitorPhoto = await x.Images.SetAsync(monitor.PublicId, "monitor.png", Png, default);   // la 2.ª pieza del kit
        Assert.NotNull(monitorPhoto.ImagePublicId);
        var kitPhoto = await x.Images.GetAsync(kit.PublicId, default);
        Assert.Null(kitPhoto.ImagePublicId);
        Assert.Equal(monitorPhoto.ImagePublicId, kitPhoto.EffectiveImagePublicId);   // la primera pieza (por orden) que tiene foto
        var stripsPhoto = await x.Images.SetAsync(strips.PublicId, "tirillas.jpg", Jpeg, default);
        Assert.Equal(stripsPhoto.ImagePublicId, (await x.Images.GetAsync(kit.PublicId, default)).EffectiveImagePublicId);   // ahora la 1.ª pieza

        // catálogo y lista de precios: la del producto; con foto propia del renglón, la del renglón
        var cat = await x.Catalog.CatalogAsync(x.C1.PublicId, default);
        Assert.Equal(stripsPhoto.ImagePublicId, cat.Brands[0].Products.Single(p => p.Sku == "52800").ImagePublicId);
        Assert.Equal(stripsPhoto.ImagePublicId, cat.Brands[0].Products.Single(p => p.Sku == "KIT1").ImagePublicId);
        var list = await x.PublishedListAsync("General", x.Today, null, null, (strips, 6.5m), (monitor, 7.5m));
        Assert.All(list.Items, i => Assert.Null(i.ImagePublicId));
        Assert.Equal(monitorPhoto.ImagePublicId, list.Items.Single(i => i.Sku == "070120").EffectiveImagePublicId);
        list = await x.Lists.SetItemImageAsync(list.PublicId, list.Items.Single(i => i.Sku == "070120").Id, "renglon.png", "image/png", Png, default);
        var row = list.Items.Single(i => i.Sku == "070120");
        Assert.NotEqual(monitorPhoto.ImagePublicId, row.ImagePublicId);
        Assert.Equal(row.ImagePublicId, row.EffectiveImagePublicId);
        Assert.Equal(row.ImagePublicId, (await x.Catalog.CatalogAsync(x.C1.PublicId, default)).Brands[0].Products.Single(p => p.Sku == "070120").ImagePublicId);

        // oferta: por omisión la del producto; quitar la foto del producto la deja sin foto (no es kit)
        var offer = await x.Offers.CreateAsync(new OfferRequest("Navidad", x.Today), default);
        offer = await x.Offers.UpsertItemsAsync(offer.PublicId, new OfferItemsRequest(new[] { new OfferItemRequest(strips.PublicId, 5m) }), default);
        Assert.Equal(stripsPhoto.ImagePublicId, offer.Items[0].EffectiveImagePublicId);
        await x.Images.ClearAsync(strips.PublicId, default);
        Assert.Null((await x.Offers.GetAsync(offer.PublicId, default)).Items[0].EffectiveImagePublicId);
        Assert.Equal(monitorPhoto.ImagePublicId, (await x.Images.GetAsync(kit.PublicId, default)).EffectiveImagePublicId);   // el kit vuelve a la del monitor
    }

    [Fact]
    public async Task Whoever_has_sales_view_can_read_a_product_photo_without_inventory_view_but_not_other_product_files()
    {
        await using var x = await PriceWorld.CreateAsync(_root);
        var strips = await x.ProductAsync("52800", 6.99m, "PRODIGY");
        var photo = await x.Images.SetAsync(strips.PublicId, "tirillas.png", Png, default);
        // otro archivo del producto que NO es su foto (p. ej. una ficha técnica)
        var doc = await x.Files.AttachAsync(EntityTypes.Product, strips.ProductId, StoredFileCodes.KindDocument, "ficha.png", null, Png, default, enforceOwnerWrite: false);

        x.F.SetPermissions(PermissionCatalog.SalesView);   // el vendedor
        var content = await x.Files.GetContentAsync(photo.ImagePublicId!.Value, default);
        Assert.Equal(Png, content.Bytes);
        await Assert.ThrowsAsync<ForbiddenException>(() => x.Files.GetContentAsync(doc.PublicId, default));

        x.F.SetPermissions(PermissionCatalog.ClientsRead);   // ni ventas ni inventario
        await Assert.ThrowsAsync<ForbiddenException>(() => x.Files.GetContentAsync(photo.ImagePublicId!.Value, default));

        x.F.SetPermissions(PermissionCatalog.InventoryView);   // inventario sigue viendo todo lo del producto
        Assert.Equal(Png, (await x.Files.GetContentAsync(doc.PublicId, default)).Bytes);
    }

    [Fact]
    public async Task A_big_photo_is_reduced_to_1200_px_in_its_own_format()
    {
        // sin la librería nativa de imágenes (en Linux no viaja con el paquete) no se crea ningún objeto de Skia: su finalizador tumbaría el proceso de pruebas
        try { if (SkiaSharpVersion.Native.Major == 0) return; }
        catch (Exception) { return; }
        byte[] big;
        try
        {
            using var bmp = new SKBitmap(2400, 1200);
            using var canvas = new SKCanvas(bmp);
            canvas.Clear(SKColors.SteelBlue);
            using var img = SKImage.FromBitmap(bmp);
            using var data = img.Encode(SKEncodedImageFormat.Png, 100);
            big = data.ToArray();
        }
        catch (Exception)
        {
            return;   // sin la librería nativa de imágenes (en Windows viaja con el paquete): la foto se guarda tal cual y no hay nada que medir
        }
        await using var x = await PriceWorld.CreateAsync(_root);
        var strips = await x.ProductAsync("52800", 6.99m);
        var photo = await x.Images.SetAsync(strips.PublicId, "grande.png", big, default);
        var stored = await x.Files.GetContentAsync(photo.ImagePublicId!.Value, default);
        Assert.Equal("image/png", stored.ContentType);
        using var back = SKBitmap.Decode(stored.Bytes);
        Assert.Equal((1200, 600), (back.Width, back.Height));
    }
}
