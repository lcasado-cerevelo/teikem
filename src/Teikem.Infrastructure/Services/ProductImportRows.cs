using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Constants;
using Teikem.Domain.Orders;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// 2026-10-21 — Productos en el motor genérico de importación (<see cref="EntityImportService"/>). Decisiones de Luis: solo CREA y rechaza los repetidos (mismo SKU del mismo dueño, mismo código de
/// barras activo, o repetidos dentro del archivo). Revisar: mismas reglas del alta manual, sin guardar. Crear: <see cref="ProductService.CreateAsync"/> (todas sus reglas) fila por fila.
/// </summary>
public sealed class ProductImportRows(TeikemDbContext db, ProductService products, ILookupCache lookups) : IImportEntityRows
{
    public const string UnknownOwnerMessage = "Cliente dueño no encontrado por ese código.";
    public const string RepeatedInFileMessage = "El SKU está repetido en el archivo.";

    public string Kind => ImportKinds.Product;

    private sealed record Owner(string Code, Guid PublicId, int ClientId);

    private sealed class Context
    {
        public Dictionary<string, Owner> OwnerByCode { get; init; } = new();
        public HashSet<(int?, string)> SkuTaken { get; init; } = new();
        public HashSet<string> BarcodeTaken { get; init; } = new(StringComparer.Ordinal);
        public HashSet<(int?, string)> SeenSku { get; } = new();
        public HashSet<string> SeenBarcode { get; } = new(StringComparer.Ordinal);
    }

    public async Task<object> LoadContextAsync(CancellationToken ct)
    {
        var owners = await db.Clients.AsNoTracking().Where(c => c.IsActive).Select(c => new Owner(c.Code, c.PublicId, c.ClientId)).ToListAsync(ct);
        var existing = await db.Set<Product>().AsNoTracking().Select(p => new { p.ClientId, p.Sku, p.Barcode, p.IsActive }).ToListAsync(ct);
        return new Context
        {
            OwnerByCode = owners.ToDictionary(o => o.Code.Trim().ToUpperInvariant(), o => o, StringComparer.Ordinal),
            SkuTaken = new HashSet<(int?, string)>(existing.Select(p => (p.ClientId, p.Sku.ToUpperInvariant()))),
            BarcodeTaken = new HashSet<string>(existing.Where(p => p.IsActive && p.Barcode != null).Select(p => p.Barcode!), StringComparer.Ordinal),
        };
    }

    public async Task<ImportRowCheck> CheckRowAsync(object context, IReadOnlyDictionary<string, string> values, CancellationToken ct)
    {
        var ctx = (Context)context;
        var errors = new Dictionary<string, string>(StringComparer.Ordinal);
        string Get(string f) => values.GetValueOrDefault(f, string.Empty);

        var (sku, skuError) = ProductRules.NormalizeSku(Get(ProductImportFields.Sku));
        if (skuError is not null) errors[ProductImportFields.Sku] = skuError;
        var (name, nameError) = ProductRules.NormalizeName(Get(ProductImportFields.Name));
        if (nameError is not null) errors[ProductImportFields.Name] = nameError;
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

        Guid? ownerPublicId = null;
        int? ownerId = null;
        var ownerCode = Get(ProductImportFields.OwnerClientCode);
        if (ownerCode.Length > 0)
        {
            if (ctx.OwnerByCode.TryGetValue(ownerCode.ToUpperInvariant(), out var owner)) { ownerPublicId = owner.PublicId; ownerId = owner.ClientId; }
            else errors[ProductImportFields.OwnerClientCode] = UnknownOwnerMessage;
        }

        // repetidos: contra lo que ya existe y dentro del archivo (solo crea, nunca actualiza)
        if (sku is not null && !errors.ContainsKey(ProductImportFields.OwnerClientCode))
        {
            var key = (ownerId, sku.ToUpperInvariant());
            if (ctx.SkuTaken.Contains(key)) errors[ProductImportFields.Sku] = ProductRules.SkuTaken;
            else if (!ctx.SeenSku.Add(key)) errors[ProductImportFields.Sku] = RepeatedInFileMessage;
        }
        if (barcode is not null && !errors.ContainsKey(ProductImportFields.Barcode))
        {
            if (ctx.BarcodeTaken.Contains(barcode) || !ctx.SeenBarcode.Add(barcode)) errors[ProductImportFields.Barcode] = ProductRules.BarcodeTaken;
        }

        var request = new ProductCreateRequest(sku, name, ownerPublicId, null, uom.Length > 0 ? uom : null, tracking.Length > 0 ? tracking : null, weight, volume, barcode, cost, price,
            null, null, minQty, minPick, maxPick, brand, model, packUom.Length > 0 ? packUom : null, packQty, Get(ProductImportFields.RotationMethod) is { Length: > 0 } rot ? rot : null);
        return new ImportRowCheck(errors, sku ?? Get(ProductImportFields.Sku), name ?? Get(ProductImportFields.Name), request);
    }

    public Task CommitRowAsync(object payload, CancellationToken ct) => products.CreateAsync((ProductCreateRequest)payload, ct);
}
