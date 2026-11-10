using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Constants;
using Teikem.Domain.Sales;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Wms;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 68 (P4-T) — kits. Un kit es un producto que se arma al pedir con otros productos: NO tiene existencia ni movimientos propios, todo pasa en sus componentes.
/// Aquí viven la definición (convertir un producto en kit y sus componentes) y su disponibilidad («se pueden armar N» por almacén). La explosión del kit en las
/// órdenes de venta y en la recolección es de los lotes siguientes. Reglas: seguimiento NONE; sin existencia ni reservas para convertirlo; los componentes son
/// productos activos, no kits, del mismo dueño de inventario; sin kits dentro de kits; volver a producto normal solo si ningún pedido lo usó.
/// Lote 231: el kit que se arma al pedir es el kit de OFERTA (Product.IsOfferKit, lo crea OfferService); desde Productos ya no se convierte un producto en kit
/// (SetFromProductsAsync) y la composición de un kit de oferta ya usado no cambia.
/// </summary>
public sealed class KitService(TeikemDbContext db, ITenantContext tenant, ILookupCache lookups, SalesOrderService sales)
{
    public const string Concurrency = "El kit cambió mientras se guardaba. Revíselo y vuelva a intentar.";

    private async Task<int?> KitTypeIdAsync(CancellationToken ct) => await lookups.TryGetIdAsync(KitCodes.ProductTypeDomain, ProductTypes.Kit, ct);

    public async Task<KitDto> GetAsync(Guid publicId, CancellationToken ct)
    {
        var product = await db.Set<Product>().AsNoTracking().FirstOrDefaultAsync(p => p.PublicId == publicId, ct) ?? throw WmsResolve.ProductNotFound();
        var kitId = await KitTypeIdAsync(ct);
        var isKit = kitId is not null && product.ProductTypeLookupId == kitId;
        if (!isKit) return new KitDto(product.PublicId, product.Sku, product.Name, false, false, Array.Empty<KitComponentDto>(), Array.Empty<KitAvailabilityDto>(), product.IsOfferKit);

        var rows = await (from c in db.Set<ProductKitComponent>().AsNoTracking()
                          join p in db.Set<Product>().AsNoTracking() on c.ComponentProductId equals p.ProductId
                          where c.KitProductId == product.ProductId
                          orderby c.SortOrder, c.ProductKitComponentId
                          select new { c.ComponentProductId, p.PublicId, p.Sku, p.Name, c.Quantity, c.SortOrder, p.IsActive, c.NotSubstitutable }).ToListAsync(ct);
        var components = rows.Select(r => new KitComponentDto(r.PublicId, r.Sku, r.Name, r.Quantity, r.SortOrder, r.IsActive, r.NotSubstitutable)).ToList();

        var availability = new List<KitAvailabilityDto>();
        if (rows.Count > 0)
        {
            var ids = rows.Select(r => r.ComponentProductId).ToList();
            var warehouses = await db.Set<Warehouse>().AsNoTracking().Where(w => w.IsActive).OrderBy(w => w.Code).ToListAsync(ct);
            foreach (var w in warehouses)
            {
                var sellable = await sales.SellableAsync(w.WarehouseId, ids, ct);
                availability.Add(new KitAvailabilityDto(w.PublicId, w.Code, KitRules.CanBuild(rows.Select(r => (sellable.GetValueOrDefault(r.ComponentProductId), r.Quantity)))));
            }
        }
        return new KitDto(product.PublicId, product.Sku, product.Name, true, product.KitAdjustable, components, availability, product.IsOfferKit);
    }

    /// <summary>
    /// Lote 231 — PUT /products/{id}/kit (la ruta de Productos). Desde Productos ya NO se convierte un producto normal en kit (422: los kits que se arman al pedir se
    /// crean como kit de oferta en Ofertas) ni se cambia un kit de oferta (422: se cambia en Ofertas). Un kit viejo (lote 68, no de oferta) se sigue pudiendo editar o
    /// volver a producto normal como antes.
    /// </summary>
    public async Task<KitDto> SetFromProductsAsync(Guid publicId, KitSetRequest? req, CancellationToken ct)
    {
        if (req is null || req.IsKit is null) throw new ValidationException("isKit", "Indique si el producto es un kit.");
        var kitId = await KitTypeIdAsync(ct);
        var product = await db.Set<Product>().AsNoTracking().Where(p => p.PublicId == publicId).Select(p => new { p.Sku, p.ProductTypeLookupId, p.IsOfferKit }).FirstOrDefaultAsync(ct)
                      ?? throw WmsResolve.ProductNotFound();
        if (product.IsOfferKit) throw new StatusRuleException(OfferKitRules.ManagedInOffers(product.Sku));
        if (req.IsKit == true && (kitId is null || product.ProductTypeLookupId != kitId)) throw new StatusRuleException(OfferKitRules.NoConvertFromProducts);
        return await SetAsync(publicId, req, ct);
    }

    /// <summary>Convierte el producto en kit (con sus componentes) o lo vuelve un producto normal. Los componentes reemplazan a los anteriores.</summary>
    public async Task<KitDto> SetAsync(Guid publicId, KitSetRequest? req, CancellationToken ct)
    {
        if (req is null || req.IsKit is null) throw new ValidationException("isKit", "Indique si el producto es un kit.");
        var kitId = await KitTypeIdAsync(ct) ?? throw new InvalidOperationException("Falta el catálogo de tipos de producto (ProductType / KIT).");
        var wantKit = req.IsKit == true;

        await db.RunInTransactionAsync(async ct2 =>
        {
            var product = await db.Set<Product>().AsTracking().FirstOrDefaultAsync(p => p.PublicId == publicId, ct2) ?? throw WmsResolve.ProductNotFound();
            var wasKit = product.ProductTypeLookupId == kitId;
            var existing = await db.Set<ProductKitComponent>().AsTracking().Where(c => c.KitProductId == product.ProductId).ToListAsync(ct2);

            if (!wantKit)
            {
                if (product.IsOfferKit) throw new StatusRuleException(OfferKitRules.ManagedInOffers(product.Sku));   // Lote 231: un kit de oferta no vuelve a ser producto normal
                if (!wasKit) throw new ConflictException(KitRules.NotAKit(product.Sku));
                if (await db.Set<SalesOrderLine>().AsNoTracking().AnyAsync(l => l.ProductId == product.ProductId, ct2)) throw new ConflictException(KitRules.UsedInSales(product.Sku));
                db.Set<ProductKitComponent>().RemoveRange(existing);
                product.ProductTypeLookupId = null;
                product.KitAdjustable = false;
                product.IsPromotion = false;   // Lote 221: solo un kit puede ser oferta (la vigencia y la foto se quedan por si vuelve a ser kit)
                await db.SaveGuardedAsync(Concurrency, ct2);
                return;
            }

            var errors = new Dictionary<string, string[]>();
            var trackingCode = (await lookups.GetAsync(product.TrackingTypeLookupId, ct2))?.InternalCode;
            if (!string.Equals(trackingCode, TrackingTypes.None, StringComparison.OrdinalIgnoreCase)) errors["trackingType"] = new[] { KitRules.TrackingNotAllowed };
            if (errors.Count > 0) throw Fail(errors);

            if (!wasKit)
            {
                var stocked = await db.Set<StockBalance>().AsNoTracking().AnyAsync(b => b.ProductId == product.ProductId && (b.QtyOnHand != 0m || b.QtyReserved != 0m), ct2);
                if (stocked) throw new ConflictException(KitRules.HasStock(product.Sku));
                if (await db.Set<ProductKitComponent>().AsNoTracking().AnyAsync(c => c.ComponentProductId == product.ProductId, ct2)) throw new ConflictException(KitRules.IsComponent(product.Sku));
            }

            var wanted = await ValidateComponentsAsync(product, req.Components, kitId, ct2);
            var byProduct = existing.ToDictionary(c => c.ComponentProductId);
            if (product.IsOfferKit && wasKit)
            {
                // Lote 231: la composición de un kit de oferta ya usado (en una oferta o en un pedido) no cambia: cambiarla es crear un kit nuevo
                var current = existing.OrderBy(c => c.SortOrder).ThenBy(c => c.ProductKitComponentId).Select(c => new OfferKitPiece(c.ComponentProductId, c.Quantity, c.NotSubstitutable)).ToList();
                var next = wanted.Select(w => new OfferKitPiece(w.ProductId, w.Quantity, w.NotSubstitutable ?? (byProduct.TryGetValue(w.ProductId, out var had) && had.NotSubstitutable))).ToList();
                if (OfferKitRules.CompositionChanges(current, product.KitAdjustable, next, req.Adjustable ?? product.KitAdjustable) && await IsUsedAsync(product.ProductId, ct2))
                    throw new StatusRuleException(OfferKitRules.CompositionLocked(product.Sku));
            }
            foreach (var gone in existing.Where(c => wanted.All(w => w.ProductId != c.ComponentProductId))) db.Set<ProductKitComponent>().Remove(gone);
            var order = 0;
            foreach (var w in wanted)
            {
                order++;
                // Lote 230: «no sustituible» (null = la marca que ya tenía; una pieza nueva nace sustituible)
                if (byProduct.TryGetValue(w.ProductId, out var row)) (row.Quantity, row.SortOrder, row.NotSubstitutable) = (w.Quantity, order, w.NotSubstitutable ?? row.NotSubstitutable);
                else db.Set<ProductKitComponent>().Add(new ProductKitComponent { TenantId = product.TenantId, KitProductId = product.ProductId, ComponentProductId = w.ProductId, Quantity = w.Quantity, SortOrder = order, NotSubstitutable = w.NotSubstitutable ?? false });
            }
            product.ProductTypeLookupId = kitId;
            if (req.Adjustable is bool adj) product.KitAdjustable = adj;
            await db.SaveGuardedAsync(Concurrency, ct2);
        }, ct);
        return await GetAsync(publicId, ct);
    }

    /// <summary>Lote 231: ¿el kit ya se usó? En alguna oferta (un renglón, aunque se haya quitado) o en alguna orden de venta.</summary>
    internal async Task<bool> IsUsedAsync(int productId, CancellationToken ct)
        => await db.Set<OfferItem>().AsNoTracking().AnyAsync(i => i.ProductId == productId, ct)
           || await db.Set<SalesOrderLine>().AsNoTracking().AnyAsync(l => l.ProductId == productId, ct);

    /// <summary>Un solo error se contesta con su mensaje exacto; varios, como lista por campo.</summary>
    private static ValidationException Fail(Dictionary<string, string[]> errors)
        => errors.Count == 1 ? new ValidationException(errors.Keys.First(), errors.Values.First()[0]) : new ValidationException(errors);

    private sealed record WantedComponent(int ProductId, decimal Quantity, bool? NotSubstitutable = null);

    private async Task<List<WantedComponent>> ValidateComponentsAsync(Product kit, IReadOnlyList<KitComponentRequest>? components, int kitTypeId, CancellationToken ct)
    {
        if (components is null || components.Count == 0) throw new ValidationException("components", KitRules.NeedComponents);
        if (components.Count > KitCodes.MaxComponents) throw new ValidationException("components", KitRules.TooManyComponents());
        var errors = new Dictionary<string, string[]>();
        for (var i = 0; i < components.Count; i++)
        {
            if (components[i].ProductPublicId is null) errors[$"components[{i}].productPublicId"] = new[] { "Indique el producto del componente." };
            else if (components[i].Quantity is not decimal q || q <= 0m) errors[$"components[{i}].quantity"] = new[] { KitRules.ComponentQuantity };
        }
        if (errors.Count > 0) throw Fail(errors);

        var publicIds = components.Select(c => c.ProductPublicId!.Value).ToList();
        var products = await db.Set<Product>().AsNoTracking().Where(p => publicIds.Contains(p.PublicId)).ToDictionaryAsync(p => p.PublicId, ct);
        var seen = new HashSet<Guid>();
        var result = new List<WantedComponent>();
        for (var i = 0; i < components.Count; i++)
        {
            var id = components[i].ProductPublicId!.Value;
            if (!products.TryGetValue(id, out var p)) throw new NotFoundException(KitRules.ComponentNotFound);
            if (!seen.Add(id)) errors[$"components[{i}]"] = new[] { KitRules.DuplicateComponent(p.Sku) };
            else if (p.ProductId == kit.ProductId) errors[$"components[{i}]"] = new[] { KitRules.SelfComponent(p.Sku) };
            else if (p.ProductTypeLookupId == kitTypeId) errors[$"components[{i}]"] = new[] { KitRules.ComponentIsKit(p.Sku) };
            else if (!p.IsActive) errors[$"components[{i}]"] = new[] { KitRules.ComponentInactive(p.Sku) };
            else if (p.ClientId != kit.ClientId) errors[$"components[{i}]"] = new[] { KitRules.ComponentOtherOwner(p.Sku) };
            else result.Add(new WantedComponent(p.ProductId, components[i].Quantity!.Value, components[i].NotSubstitutable));
        }
        if (errors.Count > 0) throw Fail(errors);
        return result;
    }
}
