using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Sales;
using Teikem.Domain.Wms;

namespace Teikem.Tests;

/// <summary>
/// Lote 231 — datos de prueba de los kits de oferta para los mundos que no registran OfferService: marca el kit como kit de oferta (biblioteca) y, si se indica un
/// precio, lo pone a la venta en una oferta publicada, para todos y sin fecha de fin (solo así se vende y admite sustituir o ajustar piezas).
/// </summary>
internal static class OfferKitTestData
{
    public static async Task MakeOfferKitAsync(WmsFixture f, int kitProductId, decimal? onSalePrice, bool? adjustable = null)
    {
        var kit = await f.Db.Set<Product>().AsTracking().SingleAsync(p => p.ProductId == kitProductId);
        kit.IsOfferKit = true;
        if (adjustable is bool adj) kit.KitAdjustable = adj;
        if (onSalePrice is decimal price)
        {
            var offer = new Offer
            {
                PublicId = Guid.NewGuid(), TenantId = WmsFixture.TenantId, Name = "Circular", ValidFrom = new DateOnly(2020, 1, 1), IsPublished = true, AppliesToAll = true,
                IsActive = true, CreatedAtUtc = DateTime.UtcNow,
            };
            offer.Items.Add(new OfferItem { TenantId = WmsFixture.TenantId, ProductId = kitProductId, Price = price, IsActive = true, CreatedAtUtc = DateTime.UtcNow });
            f.Db.Set<Offer>().Add(offer);
        }
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
    }

    /// <summary>Cambia una marca del kit directo en los datos (preparación de la prueba: un kit de oferta ya usado no deja cambiar su composición por el servicio).</summary>
    public static async Task SetPieceNotSubstitutableAsync(WmsFixture f, int kitProductId, int componentProductId, bool value)
    {
        var row = await f.Db.Set<ProductKitComponent>().AsTracking().SingleAsync(c => c.KitProductId == kitProductId && c.ComponentProductId == componentProductId);
        row.NotSubstitutable = value;
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
    }
}
