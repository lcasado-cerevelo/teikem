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
        if (!isKit) return new KitDto(product.PublicId, product.Sku, product.Name, false, false, Array.Empty<KitComponentDto>(), Array.Empty<KitAvailabilityDto>());

        var rows = await (from c in db.Set<ProductKitComponent>().AsNoTracking()
                          join p in db.Set<Product>().AsNoTracking() on c.ComponentProductId equals p.ProductId
                          where c.KitProductId == product.ProductId
                          orderby c.SortOrder, c.ProductKitComponentId
                          select new { c.ComponentProductId, p.PublicId, p.Sku, p.Name, c.Quantity, c.SortOrder, p.IsActive }).ToListAsync(ct);
        var components = rows.Select(r => new KitComponentDto(r.PublicId, r.Sku, r.Name, r.Quantity, r.SortOrder, r.IsActive)).ToList();

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
        return new KitDto(product.PublicId, product.Sku, product.Name, true, product.KitAdjustable, components, availability);
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
                if (!wasKit) throw new ConflictException(KitRules.NotAKit(product.Sku));
                if (await db.Set<SalesOrderLine>().AsNoTracking().AnyAsync(l => l.ProductId == product.ProductId, ct2)) throw new ConflictException(KitRules.UsedInSales(product.Sku));
                db.Set<ProductKitComponent>().RemoveRange(existing);
                product.ProductTypeLookupId = null;
                product.KitAdjustable = false;
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
            foreach (var gone in existing.Where(c => wanted.All(w => w.ProductId != c.ComponentProductId))) db.Set<ProductKitComponent>().Remove(gone);
            var order = 0;
            foreach (var w in wanted)
            {
                order++;
                if (byProduct.TryGetValue(w.ProductId, out var row)) (row.Quantity, row.SortOrder) = (w.Quantity, order);
                else db.Set<ProductKitComponent>().Add(new ProductKitComponent { TenantId = product.TenantId, KitProductId = product.ProductId, ComponentProductId = w.ProductId, Quantity = w.Quantity, SortOrder = order });
            }
            product.ProductTypeLookupId = kitId;
            if (req.Adjustable is bool adj) product.KitAdjustable = adj;
            await db.SaveGuardedAsync(Concurrency, ct2);
        }, ct);
        return await GetAsync(publicId, ct);
    }

    /// <summary>Un solo error se contesta con su mensaje exacto; varios, como lista por campo.</summary>
    private static ValidationException Fail(Dictionary<string, string[]> errors)
        => errors.Count == 1 ? new ValidationException(errors.Keys.First(), errors.Values.First()[0]) : new ValidationException(errors);

    private sealed record WantedComponent(int ProductId, decimal Quantity);

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
            else result.Add(new WantedComponent(p.ProductId, components[i].Quantity!.Value));
        }
        if (errors.Count > 0) throw Fail(errors);
        return result;
    }
}
