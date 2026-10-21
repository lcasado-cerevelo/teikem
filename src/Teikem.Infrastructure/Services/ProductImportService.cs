using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Constants;
using Teikem.Domain.Orders;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

public sealed record ProductImportRowDto(int Row, int Line, string? Sku, string? Name, string Status, IReadOnlyDictionary<string, string> Errors);

/// <summary>Resultado de importar productos: sin confirmar (vista previa, no guarda nada) o confirmado (crea los válidos y dice por fila qué pasó).</summary>
public sealed record ProductImportResultDto(bool Committed, int Total, int Valid, int Rejected, int Created, IReadOnlyList<ProductImportRowDto> Rows);

/// <summary>
/// 2026-10-21 — Importación de PRODUCTOS con una plantilla (Kind PRODUCT; primera entidad del motor genérico, dictado de Luis). Decisiones de Luis: solo CREA y rechaza los repetidos (mismo SKU
/// del mismo dueño, mismo código de barras activo, o repetidos dentro del archivo). Sin confirmar: revisa cada fila con las mismas reglas del alta manual (sin guardar). Confirmado: crea las filas válidas
/// con <see cref="ProductService.CreateAsync"/> (todas sus reglas) y reporta por fila; una fila mala no frena a las demás. Máximo 5.000 filas / 2 MB (como los envíos).
/// </summary>
public sealed class ProductImportService(TeikemDbContext db, ImportTemplateService templates, ProductService products, ILookupCache lookups)
{
    public const string NoFileMessage = "Elija el archivo a importar.";
    public const string NoRowsMessage = "El archivo no trae filas.";
    public const string RepeatedInFileMessage = "El SKU está repetido en el archivo.";
    public const string UnknownOwnerMessage = "Cliente dueño no encontrado por ese código.";
    public const string NotANumberMessage = "Debe ser un número.";

    public async Task<ProductImportResultDto> ImportAsync(Guid templatePublicId, byte[] bytes, string? fileName, bool commit, CancellationToken ct)
    {
        if (bytes is null || bytes.Length == 0) throw new ValidationException("file", NoFileMessage);
        var template = await templates.ResolveForImportAsync(templatePublicId, null, ImportKinds.Product, ct);
        var columns = ImportTemplateService.ParseColumns(template.ColumnsJson);
        var defaults = ImportTemplateService.ParseDefaults(template.DefaultsJson);

        ImportFileTable table;
        try { table = ImportFileReader.Read(bytes, fileName, ImportTemplateService.DelimiterOf(template), template.HasHeader, null); }
        catch (CsvLimitException) { throw new ValidationException("file", CsvParser.LimitMessage); }
        if (table.Rows.Count == 0) throw new ValidationException("file", NoRowsMessage);

        var owners = await db.Clients.AsNoTracking().Where(c => c.IsActive).Select(c => new { c.Code, c.PublicId, c.ClientId }).ToListAsync(ct);
        var ownerByCode = owners.ToDictionary(o => o.Code.Trim().ToUpperInvariant(), o => o, StringComparer.Ordinal);
        var existing = await db.Set<Product>().AsNoTracking().Select(p => new { p.ClientId, p.Sku, p.Barcode, p.IsActive }).ToListAsync(ct);
        var skuTaken = new HashSet<(int?, string)>(existing.Select(p => (p.ClientId, p.Sku.ToUpperInvariant())));
        var barcodeTaken = new HashSet<string>(existing.Where(p => p.IsActive && p.Barcode != null).Select(p => p.Barcode!), StringComparer.Ordinal);

        var seenSku = new HashSet<(int?, string)>();
        var seenBarcode = new HashSet<string>(StringComparer.Ordinal);
        var rows = new List<ProductImportRowDto>();
        var requests = new List<(int Index, ProductCreateRequest Request)>();
        foreach (var row in table.Rows)
        {
            var values = ProductImportMapping.MapRow(row.Cells, columns, defaults);
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
                if (!ProductImportMapping.TryDecimal(Get(field), out var n)) { errors[field] = NotANumberMessage; return null; }
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
                if (ownerByCode.TryGetValue(ownerCode.ToUpperInvariant(), out var owner)) { ownerPublicId = owner.PublicId; ownerId = owner.ClientId; }
                else errors[ProductImportFields.OwnerClientCode] = UnknownOwnerMessage;
            }

            // repetidos: contra lo que ya existe y dentro del archivo (solo crea, nunca actualiza)
            if (sku is not null && !errors.ContainsKey(ProductImportFields.OwnerClientCode))
            {
                var key = (ownerId, sku.ToUpperInvariant());
                if (skuTaken.Contains(key)) errors[ProductImportFields.Sku] = ProductRules.SkuTaken;
                else if (!seenSku.Add(key)) errors[ProductImportFields.Sku] = RepeatedInFileMessage;
            }
            if (barcode is not null && !errors.ContainsKey(ProductImportFields.Barcode))
            {
                if (barcodeTaken.Contains(barcode)) errors[ProductImportFields.Barcode] = ProductRules.BarcodeTaken;
                else if (!seenBarcode.Add(barcode)) errors[ProductImportFields.Barcode] = ProductRules.BarcodeTaken;
            }

            rows.Add(new ProductImportRowDto(row.RowNumber, row.Line, sku ?? Get(ProductImportFields.Sku), name ?? Get(ProductImportFields.Name), errors.Count == 0 ? "OK" : "ERROR", errors));
            if (errors.Count == 0)
                requests.Add((rows.Count - 1, new ProductCreateRequest(sku, name, ownerPublicId, null, uom.Length > 0 ? uom : null, tracking.Length > 0 ? tracking : null, weight, volume, barcode, cost, price,
                    null, null, minQty, minPick, maxPick, brand, model, packUom.Length > 0 ? packUom : null, packQty, Get(ProductImportFields.RotationMethod) is { Length: > 0 } rot ? rot : null)));
        }

        var created = 0;
        if (commit)
        {
            foreach (var (index, request) in requests)
            {
                try
                {
                    await products.CreateAsync(request, ct);
                    rows[index] = rows[index] with { Status = "CREATED" };
                    created++;
                }
                catch (TeikemException ex)
                {
                    var errs = ex.Errors is { Count: > 0 }
                        ? ex.Errors.ToDictionary(e => e.Key, e => string.Join(" ", e.Value), StringComparer.Ordinal)
                        : new Dictionary<string, string>(StringComparer.Ordinal) { ["row"] = ex.Message };
                    rows[index] = rows[index] with { Status = "ERROR", Errors = errs };
                }
            }
        }
        var valid = rows.Count(r => r.Status is "OK" or "CREATED");
        return new ProductImportResultDto(commit, rows.Count, valid, rows.Count - valid, created, rows);
    }
}
