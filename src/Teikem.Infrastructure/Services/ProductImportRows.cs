using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Constants;
using Teikem.Domain.Orders;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// 2026-10-21 — Productos en el motor genérico de importación (<see cref="EntityImportService"/>). Revisar: mismas reglas del alta/edición manual, sin guardar.
/// 2026-11-05 (pedido de Luis): ya NO es solo-crea. Un SKU que ya existe para ese dueño ACTUALIZA ese producto (<see cref="ProductService.UpdateAsync"/>, todas sus reglas): la celda
/// vacía del archivo significa "sin cambio" y los valores por defecto de la plantilla NO se aplican a lo existente (no pisan nada); el SKU y el dueño identifican, no se cambian. Un SKU
/// nuevo se crea como siempre (<see cref="ProductService.CreateAsync"/>). Se siguen rechazando por fila: el SKU repetido dentro del archivo y el código de barras que ya usa OTRO producto activo
/// (o repetido en el archivo). Un producto dado de baja también se actualiza, pero no se reactiva.
/// </summary>
public sealed class ProductImportRows(TeikemDbContext db, ProductService products, ILookupCache lookups) : IImportEntityRows
{
    public const string UnknownOwnerMessage = "Cliente dueño no encontrado por ese código.";
    public const string UnknownCategoryMessage = "No existe una categoría activa con ese nombre o ruta.";
    public const string AmbiguousCategoryMessage = "Hay varias categorías con ese nombre: use la ruta completa (Raíz / Hija).";
    public const string RepeatedInFileMessage = "El SKU está repetido en el archivo.";
    /// <summary>Lote 231: un kit de oferta no se cambia por la importación de productos (no aparece en Productos e inventario).</summary>
    public const string OfferKitMessage = "Ese SKU es un kit de oferta: no se cambia por la importación de productos, sino en Contabilidad → Precios y ofertas.";

    public string Kind => ImportKinds.Product;

    private sealed record Owner(string Code, Guid PublicId, int ClientId);

    /// <summary>Lo que hace falta para actualizar una fila ya revisada.</summary>
    private sealed record UpdatePayload(Guid ProductPublicId, ProductPatchRequest Patch);

    private sealed class Context
    {
        public Dictionary<string, Owner> OwnerByCode { get; init; } = new();
        /// <summary>Productos que ya existen: (dueño, SKU en mayúsculas) → PublicId.</summary>
        public Dictionary<(int?, string), Guid> SkuTaken { get; init; } = new();
        /// <summary>Códigos de barras de productos activos → PublicId del producto que lo tiene.</summary>
        public Dictionary<string, Guid> BarcodeOwner { get; init; } = new(StringComparer.Ordinal);
        /// <summary>Categorías activas por ruta y por nombre en mayúsculas; null = el nombre lo comparten varias (ambiguo).</summary>
        public Dictionary<string, int?> CategoryByKey { get; init; } = new(StringComparer.Ordinal);
        public HashSet<(int?, string)> SeenSku { get; } = new();
        /// <summary>Lote 231: los kits de oferta (PublicId): su SKU no se actualiza por la importación.</summary>
        public HashSet<Guid> OfferKits { get; init; } = new();
        public HashSet<string> SeenBarcode { get; } = new(StringComparer.Ordinal);
    }

    public async Task<object> LoadContextAsync(CancellationToken ct)
    {
        var owners = await db.Clients.AsNoTracking().Where(c => c.IsActive).Select(c => new Owner(c.Code, c.PublicId, c.ClientId)).ToListAsync(ct);
        var existing = await db.Set<Product>().AsNoTracking().Select(p => new { p.PublicId, p.ClientId, p.Sku, p.Barcode, p.IsActive, p.IsOfferKit }).ToListAsync(ct);
        var cats = await db.Set<ProductCategory>().AsNoTracking().Where(c => c.IsActive).Select(c => new { c.ProductCategoryId, c.ParentId, c.Name }).ToListAsync(ct);
        var parents = cats.ToDictionary(c => c.ProductCategoryId, c => c.ParentId);
        var names = cats.ToDictionary(c => c.ProductCategoryId, c => c.Name);
        var byKey = new Dictionary<string, int?>(StringComparer.Ordinal);
        foreach (var g in cats.GroupBy(c => c.Name.Trim().ToUpperInvariant())) byKey[g.Key] = g.Count() == 1 ? g.First().ProductCategoryId : null;   // por nombre: null si se repite
        foreach (var c in cats) byKey[ProductRules.CategoryPath(c.ProductCategoryId, parents, names).ToUpperInvariant()] = c.ProductCategoryId;   // la ruta completa manda y es única
        return new Context
        {
            CategoryByKey = byKey,
            OwnerByCode = owners.ToDictionary(o => o.Code.Trim().ToUpperInvariant(), o => o, StringComparer.Ordinal),
            SkuTaken = existing.ToDictionary(p => (p.ClientId, p.Sku.ToUpperInvariant()), p => p.PublicId),
            OfferKits = existing.Where(p => p.IsOfferKit).Select(p => p.PublicId).ToHashSet(),
            BarcodeOwner = existing.Where(p => p.IsActive && p.Barcode != null).GroupBy(p => p.Barcode!, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First().PublicId, StringComparer.Ordinal),
        };
    }

    public Task<ImportRowCheck> CheckRowAsync(object context, IReadOnlyDictionary<string, string> values, CancellationToken ct)
        => CheckRowAsync(context, values, values, ct);

    public async Task<ImportRowCheck> CheckRowAsync(object context, IReadOnlyDictionary<string, string> values, IReadOnlyDictionary<string, string> fileValues, CancellationToken ct)
    {
        var ctx = (Context)context;
        var errors = new Dictionary<string, string>(StringComparer.Ordinal);

        // 1. Quién es: SKU y dueño (con los valores por defecto, si los hay) deciden si la fila crea o actualiza.
        var (sku, skuError) = ProductRules.NormalizeSku(values.GetValueOrDefault(ProductImportFields.Sku, string.Empty));
        if (skuError is not null) errors[ProductImportFields.Sku] = skuError;
        Guid? ownerPublicId = null;
        int? ownerId = null;
        var ownerCode = values.GetValueOrDefault(ProductImportFields.OwnerClientCode, string.Empty);
        if (ownerCode.Length > 0)
        {
            if (ctx.OwnerByCode.TryGetValue(ownerCode.ToUpperInvariant(), out var owner)) { ownerPublicId = owner.PublicId; ownerId = owner.ClientId; }
            else errors[ProductImportFields.OwnerClientCode] = UnknownOwnerMessage;
        }
        Guid? existingId = null;
        if (sku is not null && !errors.ContainsKey(ProductImportFields.OwnerClientCode) && ctx.SkuTaken.TryGetValue((ownerId, sku.ToUpperInvariant()), out var found)) existingId = found;
        var isUpdate = existingId is not null;
        if (existingId is Guid kitId && ctx.OfferKits.Contains(kitId)) errors[ProductImportFields.Sku] = OfferKitMessage;   // Lote 231

        // Lo existente solo recibe lo que trae el archivo (celda vacía = sin cambio); lo nuevo, también los valores por defecto de la plantilla.
        var source = isUpdate ? fileValues : values;
        string Get(string f) => source.GetValueOrDefault(f, string.Empty);

        string? name = null;
        if (!isUpdate || Get(ProductImportFields.Name).Length > 0)
        {
            var (n, nameError) = ProductRules.NormalizeName(Get(ProductImportFields.Name));
            if (nameError is not null) errors[ProductImportFields.Name] = nameError;
            name = n;
        }
        var (barcode, barcodeError) = ProductRules.NormalizeBarcode(Get(ProductImportFields.Barcode));
        if (barcodeError is not null) errors[ProductImportFields.Barcode] = barcodeError;
        var (brand, brandError) = ProductRules.NormalizeBrand(Get(ProductImportFields.Brand));
        if (brandError is not null) errors[ProductImportFields.Brand] = brandError;
        var (model, modelError) = ProductRules.NormalizeModel(Get(ProductImportFields.Model));
        if (modelError is not null) errors[ProductImportFields.Model] = modelError;

        decimal? Num(string field)
        {
            if (!ImportEntityMeta.TryDecimal(Get(field), out var n)) { errors[field] = EntityImportService.NotANumberMessage; return null; }
            return n;
        }
        var weight = Num(ProductImportFields.WeightKg);
        var volume = Num(ProductImportFields.VolumeM3);
        var cost = Num(ProductImportFields.PurchaseCost);
        var price = Num(ProductImportFields.SalePrice);
        var minQty = Num(ProductImportFields.MinQty);
        var minPick = Num(ProductImportFields.MinPickQty);
        var maxPick = Num(ProductImportFields.MaxPickQty);
        var packQty = Num(ProductImportFields.PackQty);
        // Lote 230: dimensiones y peso con su unidad (la misma regla del alta y la edición: con las tres medidas el volumen se calcula)
        var length = Num(ProductImportFields.Length);
        var width = Num(ProductImportFields.Width);
        var height = Num(ProductImportFields.Height);
        var weightInUnit = Num(ProductImportFields.Weight);
        foreach (var (field, message) in ProductDimensionRules.ValidateDimensions(length, width, height)) errors[field] = message;
        var (dimensionUnit, dimensionUnitError) = ProductDimensionRules.NormalizeDimensionUnit(Get(ProductImportFields.DimensionUnit));
        if (dimensionUnitError is not null) errors[ProductImportFields.DimensionUnit] = dimensionUnitError;
        var (weightUnit, weightUnitError) = ProductDimensionRules.NormalizeWeightUnit(Get(ProductImportFields.WeightUnit));
        if (weightUnitError is not null) errors[ProductImportFields.WeightUnit] = weightUnitError;
        else if (!errors.ContainsKey(ProductImportFields.Weight) && ProductDimensionRules.ValidateWeight(weightInUnit, weightUnit ?? WeightUnits.Kilogram) is string weightError)
            errors[ProductImportFields.Weight] = weightError;
        if (!errors.ContainsKey(ProductImportFields.PurchaseCost) && ProductRules.Money(cost, "costo") is string costError) errors[ProductImportFields.PurchaseCost] = costError;
        if (!errors.ContainsKey(ProductImportFields.SalePrice) && ProductRules.Money(price, "precio") is string priceError) errors[ProductImportFields.SalePrice] = priceError;
        if (!errors.ContainsKey(ProductImportFields.WeightKg) && !errors.ContainsKey(ProductImportFields.VolumeM3))
            foreach (var (field, message) in ProductRules.ValidateMeasures(weight, volume)) errors[field] = message;

        var uom = Get(ProductImportFields.BaseUom);
        if (uom.Length > 0 && await lookups.TryGetIdAsync(LookupDomains.UnitOfMeasure, uom.ToUpperInvariant(), ct) is null) errors[ProductImportFields.BaseUom] = ProductService.UnknownUomMessage(uom.ToUpperInvariant());
        var tracking = Get(ProductImportFields.TrackingType);
        if (tracking.Length > 0 && await lookups.TryGetIdAsync(LookupDomains.TrackingType, tracking.ToUpperInvariant(), ct) is null) errors[ProductImportFields.TrackingType] = ProductService.UnknownTrackingMessage(tracking.ToUpperInvariant());
        var packUom = Get(ProductImportFields.PackUom);
        if (packUom.Length > 0 && await lookups.TryGetIdAsync(LookupDomains.UnitOfMeasure, packUom.ToUpperInvariant(), ct) is null) errors[ProductImportFields.PackUom] = ProductRules.UnknownPackUom(packUom.ToUpperInvariant());

        // repetidos: el SKU dentro del archivo (aplica a crear y a actualizar) y el código de barras de OTRO producto activo.
        if (sku is not null && !errors.ContainsKey(ProductImportFields.OwnerClientCode) && !ctx.SeenSku.Add((ownerId, sku.ToUpperInvariant())))
            errors[ProductImportFields.Sku] = RepeatedInFileMessage;
        if (barcode is not null && !errors.ContainsKey(ProductImportFields.Barcode))
        {
            var usedByOther = ctx.BarcodeOwner.TryGetValue(barcode, out var holder) && holder != existingId;
            if (usedByOther || !ctx.SeenBarcode.Add(barcode)) errors[ProductImportFields.Barcode] = ProductRules.BarcodeTaken;
        }

        int? categoryId = null;
        var categoryText = Get(ProductImportFields.Category);
        if (categoryText.Length > 0)
        {
            if (!ctx.CategoryByKey.TryGetValue(string.Join(" / ", categoryText.Split('/', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant(), out var cid)) errors[ProductImportFields.Category] = UnknownCategoryMessage;
            else if (cid is null) errors[ProductImportFields.Category] = AmbiguousCategoryMessage;
            else categoryId = cid;
        }

        var rotation = Get(ProductImportFields.RotationMethod) is { Length: > 0 } rot ? rot : null;
        var key = sku ?? values.GetValueOrDefault(ProductImportFields.Sku, string.Empty);
        if (existingId is Guid productId)
        {
            var patch = new ProductPatchRequest(Name: name, BaseUom: uom.Length > 0 ? uom : null, TrackingType: tracking.Length > 0 ? tracking : null, WeightKg: weight, VolumeM3: volume,
                Barcode: barcode, PurchaseCost: cost, SalePrice: price, MinQty: minQty, MinPickQty: minPick, MaxPickQty: maxPick, Brand: brand, Model: model,
                PackUom: packUom.Length > 0 ? packUom : null, PackQty: packQty, RotationMethod: rotation, CategoryId: categoryId,
                Length: length, Width: width, Height: height, DimensionUnit: dimensionUnit, Weight: weightInUnit, WeightUnit: weightUnit);
            return new ImportRowCheck(errors, key, name ?? Get(ProductImportFields.Name), new UpdatePayload(productId, patch), IsUpdate: true);
        }

        var request = new ProductCreateRequest(sku, name, ownerPublicId, categoryId, uom.Length > 0 ? uom : null, tracking.Length > 0 ? tracking : null, weight, volume, barcode, cost, price,
            null, null, minQty, minPick, maxPick, brand, model, packUom.Length > 0 ? packUom : null, packQty, rotation,
            length, width, height, dimensionUnit, weightInUnit, weightUnit);
        return new ImportRowCheck(errors, key, name ?? Get(ProductImportFields.Name), request);
    }

    public Task CommitRowAsync(object payload, CancellationToken ct)
        => payload is UpdatePayload update
            ? products.UpdateAsync(update.ProductPublicId, update.Patch, ct)
            : products.CreateAsync((ProductCreateRequest)payload, ct);
}
