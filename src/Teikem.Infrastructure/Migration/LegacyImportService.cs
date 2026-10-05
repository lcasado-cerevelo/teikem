using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Teikem.Domain.Constants;
using Teikem.Domain.Migration;
using Teikem.Domain.Tenancy;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Services;
using Teikem.Infrastructure.Wms;

namespace Teikem.Infrastructure.Migration;

// Lote 10 (P0) — orquestador de la migración desde QuickBooks Desktop y el WMS MSWM. Dos piezas en este archivo:
// - LegacyImportPlanner: construcción PURA del plan (qué se crearía) a partir de las filas ya leídas y de la configuración,
//   aplicando LegacyImportRules y las mismas validaciones puras que los servicios (ProductRules, WarehouseRules,
//   ContactPointService.ValidateValue). Sin EF ni IO: se prueba con la muestra sintética (LegacyImportServiceTests).
// - LegacyImportService: lee las fuentes, arma el plan y lo ejecuta SIEMPRE a través de los servicios existentes y del
//   InventoryLedger (nada de INSERT directo). Idempotente por clave natural; en dry-run solo lee para saber qué ya existe.

/// <summary>Nombres de las entidades en el reporte (sección Resumen).</summary>
public static class LegacyImportEntities
{
    public const string Company = "Compañía";
    public const string Categories = "Categorías";
    public const string Warehouse = "Almacén";
    public const string Zones = "Zonas";
    public const string Bins = "Posiciones";
    public const string Suppliers = "Proveedores";
    public const string Products = "Productos";
    public const string CustomFields = "Campos personalizados";
    public const string Clients = "Clientes";
    public const string Consignees = "Consignatarios";
    public const string Contacts = "Contactos";
    public const string OpeningBalance = "Saldo inicial";
    public const string Reconciliation = "Conciliación";
}

/// <summary>Filas ya leídas de todas las fuentes (QuickBooks y, si se configuró, MSWM).</summary>
public sealed record LegacyImportSources(
    IReadOnlyList<QbItem> Items,
    IReadOnlyList<QbItem> ExtraItems,
    IReadOnlyList<QbCustomer> Customers,
    IReadOnlyList<QbVendor> Vendors,
    IReadOnlyList<WmsItem> WmsItems,
    IReadOnlyList<WmsLocation> WmsLocations,
    IReadOnlyList<WmsInventoryRow> WmsInventory,
    IReadOnlyList<WmsUpc> WmsUpcs,
    IReadOnlyList<WmsBinQuantity>? WmsBinHistory = null)
{
    /// <summary>Solo QuickBooks (sin WMS).</summary>
    public static LegacyImportSources FromQuickBooks(IReadOnlyList<QbItem> items, IReadOnlyList<QbCustomer> customers,
        IReadOnlyList<QbVendor> vendors, IReadOnlyList<QbItem>? extraItems = null)
        => new(items, extraItems ?? Array.Empty<QbItem>(), customers, vendors, Array.Empty<WmsItem>(), Array.Empty<WmsLocation>(),
            Array.Empty<WmsInventoryRow>(), Array.Empty<WmsUpc>());
}

/// <summary>Producto planificado. Key = SkuKey para cruzar fuentes; Origin dice de qué archivo salió.</summary>
public sealed class PlannedProduct
{
    public const string OriginQuickBooks = "QuickBooks";
    public const string OriginExtra = "QuickBooks (adicional)";
    public const string OriginWms = "WMS";

    public required string Sku { get; init; }
    public required string Key { get; init; }
    public required string Name { get; init; }
    public string? Category { get; init; }
    /// <summary>Marca de QuickBooks (columna Brand), recortada; null si viene vacía.</summary>
    public string? Brand { get; init; }
    public string? Barcode { get; set; }
    public decimal? PurchaseCost { get; init; }
    public decimal? SalePrice { get; init; }
    public bool IsActiveInSource { get; init; } = true;
    public decimal? SourceQuantity { get; init; }
    public string Origin { get; init; } = OriginQuickBooks;
    /// <summary>Inactivo en QuickBooks y sin existencia: se crea y se da de baja al final.</summary>
    public bool DeactivateAtEnd { get; set; }
}

/// <summary>Lote 16: ReceivingMode (PUTAWAY | DIRECT) y DefaultReceivingBin (código) solo se aplican al crear el almacén.</summary>
public sealed record PlannedWarehouse(string Code, string Name, string? Line1, string? City, string? State, string? PostalCode, string Country,
    string? ReceivingMode = null, string? DefaultReceivingBin = null);
public sealed record PlannedZone(string Code, string Name, string? ZoneType);
public sealed record PlannedBin(string Code, string ZoneCode, string? Aisle, string? Level, string? Position, string SourceId);
public sealed record PlannedBalance(string Sku, string Key, string BinCode, decimal Quantity);
public sealed record PlannedSupplier(string Name, string? ContactName, string? Phone, string? Notes);
public sealed record PlannedAddress(string LocationType, QuickBooksAddress Address);
public sealed record PlannedClient(string QbCode, string Name, string? LegalName, string? PaymentTerm, decimal? CreditLimit, string? Rep,
    IReadOnlyList<PlannedAddress> Addresses, IReadOnlyList<string> Emails, IReadOnlyList<string> Phones);

/// <summary>Plan en memoria de la carga: qué se crearía, en el orden en que se crea.</summary>
public sealed class LegacyImportPlan
{
    public List<string> Categories { get; } = new();
    public List<PlannedProduct> Products { get; } = new();
    public PlannedWarehouse? Warehouse { get; set; }
    public List<PlannedZone> Zones { get; } = new();
    public List<PlannedBin> Bins { get; } = new();
    /// <summary>Lote 11: cupo estimado de las posiciones que vienen del WMS (vacío sin MSWM o sin historial).</summary>
    public List<BinCapacityEstimate> Capacities { get; } = new();
    public List<PlannedBalance> Balances { get; } = new();
    public List<PlannedSupplier> Suppliers { get; } = new();
    public List<PlannedClient> Clients { get; } = new();
    /// <summary>Opciones del campo personalizado 'Representante' (valores distintos de Rep).</summary>
    public List<string> RepOptions { get; } = new();
    public bool QbCodeField { get; set; }
    public bool RepField { get; set; }
}

/// <summary>
/// Construcción pura del plan. Anota en el reporte lo leído, lo omitido, lo rechazado por las reglas puras, las advertencias
/// y los mapeos; lo creado y lo que ya existía lo cuenta el servicio al ejecutar (o al simular) contra la base.
/// </summary>
public static class LegacyImportPlanner
{
    public const string QbCodeFieldKey = "qb_code";
    public const string RepFieldKey = "sales_rep";

    // ---------------------------------------------------------------- mensajes del reporte (el manual y la FAQ los citan)

    public static string ItemWithoutSku(int line) => $"La fila {line} no tiene código de ítem; se omite.";
    public static string TypeNotInventory(string sku, string? type) => $"El ítem {sku} es de tipo '{type}' (no es de inventario); no se carga.";
    public static string SkuExcluded(string sku) => $"El SKU {sku} está excluido por la configuración (products.excludeSkus); no se carga.";
    public static string CategoryExcluded(string sku, string cat)
        => $"El ítem {sku} tiene la categoría de QuickBooks '{cat}', excluida por la configuración; no se carga.";
    public static string DuplicateSku(string sku) => $"El SKU {sku} está repetido en el archivo; se conserva la primera fila.";
    public static string NameTruncated(string sku) => $"El nombre del ítem {sku} excede {ProductRules.NameMaxLength} caracteres; se recorta.";
    public static string UpcMultiple(string sku, int count, string upc)
        => $"El ítem {sku} tiene {count} códigos de barras en el WMS; se usa el primero ({upc}).";
    public static string UpcShared(string upc, string skus) => $"El código de barras {upc} del WMS está en varios productos ({skus}); no se asigna a ninguno.";
    public static string BarcodeInvalid(string sku, string upc, string error) => $"El código de barras {upc} del ítem {sku} no es válido ({error}); no se asigna.";
    public static string BarcodeInUse(string sku, string upc) => $"El código de barras {upc} ya lo usa otro producto activo; el ítem {sku} se crea sin código de barras.";
    public static string WmsExcludedWithStock(string sku, decimal qty)
        => $"El SKU {sku} tiene {ProductRules.FormatQty(qty)} unidades en el WMS pero está excluido; no se carga.";
    public static string WmsOrphan(string sku) => $"El SKU {sku} del WMS no está en QuickBooks y no tiene existencia; se ignora.";
    public static string LocationSkippedByConfig(string id) => $"La posición {id} del WMS está en warehouse.skipLocationIds; se omite.";
    public static string BinCollision(string id, string code) => $"La posición {id} del WMS coincide con {code} al pasarla a mayúsculas; se conserva la primera.";
    public static string SkuMissingInTeikem(string sku) => $"SKU {sku} con existencia en el WMS no existe en Teikem.";
    public static string BinMissing(string code, string sku) => $"La posición {code} no se creó en Teikem; no se carga el saldo de {sku}.";
    public static string InactiveWithStock(string sku) => $"El ítem {sku} está inactivo en QuickBooks pero tiene existencia; se deja activo.";
    public static string OpeningBalanceAlreadyLoaded(string warehouse) => $"El saldo inicial ya fue cargado en {warehouse}; no se repite.";
    public const string SingleBinRequired = "Con openingBalances.source=quickbooks se requiere warehouse.singleBin.";
    public const string WarehouseRequired = "Hay saldo inicial que cargar pero la configuración no define warehouse.code.";
    public static string SupplierNotInFile(string name) => $"El proveedor '{name}' de suppliers.includeNames no está en el archivo.";
    public static string SupplierExcluded(string name) => $"Proveedor '{name}' excluido por la configuración; no se carga.";
    public static string DuplicateName(string name) => $"'{name}' está repetido en el archivo; se conserva la primera fila.";
    public static string RowWithoutName(int line) => $"La fila {line} no tiene nombre; se omite.";
    public static string ClientInactive(string name) => $"Cliente '{name}' inactivo en QuickBooks; no se carga.";
    public static string ClientWithoutAddress(string name) => $"El cliente '{name}' no tiene dirección; no se crea consignatario.";
    public static string CategoryInactive(string name) => $"La categoría '{name}' existe pero está inactiva; sus productos se crean sin categoría.";
    public static string ZoneMissing(string zone, string code) => $"La zona {zone} no existe; no se crea la posición {code}.";
    public static string CustomFieldInactive(string key) => $"El campo personalizado '{key}' existe pero está inactivo; no se guardan sus valores.";
    public static string ReasonMissing(string code) => $"El motivo de ajuste {code} no existe en el catálogo; ejecute db-init antes de la carga.";
    public static string PaymentTermMissing(string code) => $"El término de pago {code} no existe en el catálogo; ejecute db-init antes de la carga.";
    public const string OpeningBalanceNotInUpdate = "El saldo inicial no se toca en modo --update: el inventario lo mueve Teikem.";
    public static string MswmConnectionMissing(string name) => $"Falta la cadena de conexión ConnectionStrings:{name}.";
    public const string AdminEmailRequired = "company.adminEmail es obligatorio para aprovisionar la compañía.";
    public static string DbUnavailable(string detail) => $"No se pudo consultar la base de Teikem ({detail}); la simulación supone que la compañía es nueva.";
    public static string CapacityWithoutHistory(string warehouse)
        => $"Ninguna posición de {warehouse} tiene historial de existencias en el WMS; las posiciones quedan sin cupo.";

    // Lote 11: columna Resultado del CSV de cupos.
    public const string CapacityAssigned = "Asignado al crear la posición";
    public const string CapacityWouldAssign = "Se asignaría al crear la posición";
    public const string CapacityFilled = "Asignado (--update: la posición no tenía cupo)";
    public const string CapacityWouldFill = "Se asignaría (--update: la posición no tenía cupo)";
    public const string CapacitySkippedWithoutUpdate = "Sin cambio: la posición ya existía sin cupo (use --update para llenarlo)";
    public const string CapacityNotApplied = "No se asignó: la posición no se creó o la escritura fue rechazada (ver rechazos)";
    public static string CapacityKept(int current) => $"Se conserva el cupo actual ({current.ToString(CultureInfo.InvariantCulture)})";

    /// <summary>Texto de la columna Resultado según la decisión (BinCapacityRules.Decide) y el modo.</summary>
    public static string CapacityResult(BinCapacityAction action, int? current, bool dryRun) => action switch
    {
        BinCapacityAction.AssignOnCreate => dryRun ? CapacityWouldAssign : CapacityAssigned,
        BinCapacityAction.FillExisting => dryRun ? CapacityWouldFill : CapacityFilled,
        BinCapacityAction.KeepExisting => CapacityKept(current ?? 0),
        _ => CapacitySkippedWithoutUpdate,
    };

    public static string ReconciliationMismatch(string where, decimal ledgerQty, decimal balanceQty)
        => $"Descuadre en {where}: ledger {ProductRules.FormatQty(ledgerQty)}, saldo {ProductRules.FormatQty(balanceQty)}.";

    /// <summary>
    /// Totales esperados del plan aprobado (docs/migracion-depot-solutions-plan.md, sección 6), por nombre de compañía;
    /// vacío para cualquier otra (muestra, pruebas).
    /// </summary>
    public static IReadOnlyDictionary<string, int> ExpectedTotals(string? company) => company?.Trim() switch
    {
        "Advance Depot" => new Dictionary<string, int>
        {
            [LegacyImportEntities.Categories] = 2, [LegacyImportEntities.Products] = 552, [LegacyImportEntities.Suppliers] = 10,
            [LegacyImportEntities.Clients] = 1, [LegacyImportEntities.Consignees] = 1, [LegacyImportEntities.Zones] = 6,
            [LegacyImportEntities.Bins] = 3887, [LegacyImportEntities.OpeningBalance] = 1310,
        },
        "Advance Solutions" => new Dictionary<string, int>
        {
            [LegacyImportEntities.Categories] = 1, [LegacyImportEntities.Products] = 39, [LegacyImportEntities.Suppliers] = 6,
            [LegacyImportEntities.Clients] = 693,
            // BILLING solo si difiere de Ship to sin distinguir mayúsculas: 50; 12 clientes difieren solo en mayúsculas.
            [LegacyImportEntities.Consignees] = 693 + 50, [LegacyImportEntities.Zones] = 1,
            [LegacyImportEntities.Bins] = 1, [LegacyImportEntities.OpeningBalance] = 32,
        },
        _ => new Dictionary<string, int>(),
    };

    public static LegacyImportPlan Build(LegacyImportConfig cfg, LegacyImportSources src, LegacyImportReport report)
    {
        ArgumentNullException.ThrowIfNull(cfg);
        ArgumentNullException.ThrowIfNull(src);
        ArgumentNullException.ThrowIfNull(report);

        // El resumen sigue el orden de carga.
        foreach (var e in new[]
                 {
                     LegacyImportEntities.Company, LegacyImportEntities.Categories, LegacyImportEntities.Warehouse, LegacyImportEntities.Zones,
                     LegacyImportEntities.Bins, LegacyImportEntities.Suppliers, LegacyImportEntities.Products, LegacyImportEntities.CustomFields,
                     LegacyImportEntities.Clients, LegacyImportEntities.Consignees, LegacyImportEntities.Contacts, LegacyImportEntities.OpeningBalance,
                 })
            report.Total(e);
        foreach (var (entity, expected) in ExpectedTotals(cfg.Company.Name)) report.SetExpected(entity, expected);

        var plan = new LegacyImportPlan();
        PlanProducts(cfg, src, report, plan);
        PlanWarehouse(cfg, src, report, plan);
        PlanOpeningBalances(cfg, src, report, plan);
        PlanSuppliers(cfg, src, report, plan);
        PlanClients(cfg, src, report, plan);
        return plan;
    }

    // ---------------------------------------------------------------- productos

    private static void PlanProducts(LegacyImportConfig cfg, LegacyImportSources src, LegacyImportReport report, LegacyImportPlan plan)
    {
        const string P = LegacyImportEntities.Products;
        var types = new HashSet<string>(cfg.Products.Types.Select(t => t.Trim()), StringComparer.OrdinalIgnoreCase);
        var excludedCats = new HashSet<string>(cfg.Products.ExcludeQuickBooksCategories.Select(c => c.Trim()), StringComparer.OrdinalIgnoreCase);
        var excludedSkus = new HashSet<string>(cfg.Products.ExcludeSkus.Select(LegacyImportRules.SkuKey));
        var byKey = new Dictionary<string, PlannedProduct>();
        var excludedKeys = new HashSet<string>();

        void Add(QbItem item, bool extra)
        {
            report.CountRead(P);
            var rawSku = item.Item?.Trim();
            var sku = LegacyImportRules.NormalizeSku(rawSku);
            if (sku is null) { report.CountSkipped(P); report.Warn(P, $"línea {item.Line}", ItemWithoutSku(item.Line)); return; }
            if (!types.Contains(item.Type?.Trim() ?? string.Empty))
            {
                report.CountSkipped(P);
                report.Warn(P, sku, TypeNotInventory(sku, item.Type));
                return;
            }
            var (validSku, skuError) = ProductRules.NormalizeSku(sku);
            if (skuError is not null) { report.Reject(P, sku, skuError); return; }
            sku = validSku!;
            var key = LegacyImportRules.SkuKey(sku);

            if (excludedSkus.Contains(key)) { excludedKeys.Add(key); report.CountSkipped(P); report.Warn(P, sku, SkuExcluded(sku)); return; }

            var qbCategory = item.Category?.Trim();
            string? category;
            if (extra)
                category = Blank(cfg.Products.DefaultCategory);
            else
            {
                if (!string.IsNullOrEmpty(qbCategory) && excludedCats.Contains(qbCategory))
                {
                    excludedKeys.Add(key);
                    report.CountSkipped(P);
                    report.Warn(P, sku, CategoryExcluded(sku, qbCategory));
                    return;
                }
                if (!ResolveCategory(cfg, qbCategory, out category))
                {
                    excludedKeys.Add(key);
                    report.CountSkipped(P);
                    report.Warn(P, sku, LegacyImportRules.UnknownCategory(qbCategory!));
                    return;
                }
                // Solutions: la categoría es el fabricante; sin fabricante queda la de defecto.
                if (cfg.Products.CategoryFromManufacturer && ManufacturerCategory(item.Manufacturer) is { } fromManufacturer) category = fromManufacturer;
            }

            if (byKey.ContainsKey(key)) { report.Reject(P, sku, DuplicateSku(sku)); return; }

            if (LegacyImportRules.SkuChanged(rawSku, sku))
            {
                report.Map("SKU", rawSku!, sku);
                report.Warn(P, sku, LegacyImportRules.SkuNormalized(rawSku!, sku));
            }

            var name = item.Description?.Trim();
            if (string.IsNullOrEmpty(name))
            {
                if (!cfg.Products.NameFallbackToSku) { report.Reject(P, sku, ProductRules.NameRequired); return; }
                name = sku;
                report.Warn(P, sku, LegacyImportRules.NameFromSku(sku));
            }
            if (name.Length > ProductRules.NameMaxLength)
            {
                name = name[..ProductRules.NameMaxLength].TrimEnd();
                report.Warn(P, sku, NameTruncated(sku));
            }

            var product = new PlannedProduct
            {
                Sku = sku, Key = key, Name = name, Category = category,
                Brand = string.IsNullOrWhiteSpace(item.Brand) ? null : item.Brand.Trim() is var b && b.Length > 100 ? b[..100] : item.Brand.Trim(),
                PurchaseCost = Positive(LegacyImportRules.ParseQuickBooksNumber(item.Cost)),
                SalePrice = Positive(LegacyImportRules.ParseQuickBooksNumber(item.Price)),
                IsActiveInSource = item.IsActive,
                SourceQuantity = LegacyImportRules.ParseQuickBooksNumber(item.QuantityOnHand),
                Origin = extra ? PlannedProduct.OriginExtra : PlannedProduct.OriginQuickBooks,
            };
            byKey[key] = product;
            plan.Products.Add(product);
            if (extra) report.Map("Producto adicional", rawSku!, $"{sku} ({product.Category ?? "sin categoría"})");
        }

        foreach (var item in src.Items) Add(item, extra: false);

        // Archivo adicional: solo los ítems de la categoría indicada que no estén ya (por SKU sin espacios ni mayúsculas).
        var where = cfg.Sources.ExtraProducts?.WhereCategory?.Trim();
        foreach (var item in src.ExtraItems)
        {
            if (!string.IsNullOrEmpty(where) && !string.Equals(item.Category?.Trim(), where, StringComparison.OrdinalIgnoreCase)) continue;
            var key = LegacyImportRules.NormalizeSku(item.Item) is { } s ? LegacyImportRules.SkuKey(s) : null;
            if (key is null || byKey.ContainsKey(key) || excludedKeys.Contains(key)) continue;
            Add(item, extra: true);
        }

        // SKU del WMS: con existencia y sin QuickBooks → se crean (si la configuración lo pide); sin existencia → se ignoran.
        if (src.WmsItems.Count > 0 || src.WmsInventory.Count > 0)
        {
            var stock = src.WmsInventory.GroupBy(r => LegacyImportRules.SkuKey(r.ItemId)).ToDictionary(g => g.Key, g => g.Sum(r => r.OnHandQuantity));
            var wmsItems = src.WmsItems.GroupBy(i => LegacyImportRules.SkuKey(i.ItemId)).ToDictionary(g => g.Key, g => g.First());
            foreach (var key in stock.Keys.Union(wmsItems.Keys).OrderBy(k => k, StringComparer.Ordinal))
            {
                if (key.Length == 0 || byKey.ContainsKey(key)) continue;
                var qty = stock.GetValueOrDefault(key);
                var wmsItem = wmsItems.GetValueOrDefault(key);
                var sku = LegacyImportRules.NormalizeSku(wmsItem?.ItemId ?? key) ?? key;
                if (excludedKeys.Contains(key)) { if (qty > 0) report.Warn(P, sku, WmsExcludedWithStock(sku, qty)); continue; }
                if (qty <= 0) { report.Warn(P, sku, WmsOrphan(sku)); continue; }
                if (!cfg.Products.CreateUnknownWmsSkusWithStock) continue; // el saldo lo rechaza con SkuMissingInTeikem
                report.CountRead(P);
                var (validSku, skuError) = ProductRules.NormalizeSku(sku);
                if (skuError is not null) { report.Reject(P, sku, skuError); continue; }
                var name = LegacyImportRules.IsWmsEmpty(wmsItem?.Description) ? validSku! : wmsItem!.Description!.Trim();
                if (name.Length > ProductRules.NameMaxLength) name = name[..ProductRules.NameMaxLength].TrimEnd();
                var product = new PlannedProduct
                {
                    Sku = validSku!, Key = key, Name = name, Category = Blank(cfg.Products.DefaultCategory), Origin = PlannedProduct.OriginWms,
                };
                byKey[key] = product;
                plan.Products.Add(product);
                report.Warn(P, validSku!, LegacyImportRules.UnknownWmsSku(validSku!, qty));
            }
        }

        AssignBarcodes(src, report, plan);

        foreach (var c in plan.Products.Select(p => p.Category).Where(c => c is not null).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            plan.Categories.Add(c!);
            report.CountRead(LegacyImportEntities.Categories);
        }
    }

    /// <summary>Fabricante recortado como nombre de categoría (hasta ProductRules.CategoryNameMaxLength); vacío = null.</summary>
    public static string? ManufacturerCategory(string? manufacturer)
    {
        var m = manufacturer?.Trim();
        if (string.IsNullOrEmpty(m)) return null;
        return m.Length > ProductRules.CategoryNameMaxLength ? m[..ProductRules.CategoryNameMaxLength].TrimEnd() : m;
    }

    /// <summary>
    /// Categoría destino: sin mapa → la de defecto para todo; con mapa → vacía = defecto, mapeada = su destino, otra = sin regla
    /// (false). Para Depot equivale a LegacyImportRules.DepotCategory con defaultCategory 'AxisCare' y {CARTONES: CARTONES}.
    /// </summary>
    public static bool ResolveCategory(LegacyImportConfig cfg, string? qbCategory, out string? category)
    {
        var map = cfg.Products.CategoryByQuickBooksCategory;
        var cat = qbCategory?.Trim();
        if (map.Count == 0 || string.IsNullOrEmpty(cat)) { category = Blank(cfg.Products.DefaultCategory); return true; }
        if (map.TryGetValue(cat, out var target) && !string.IsNullOrWhiteSpace(target)) { category = target.Trim(); return true; }
        category = null;
        return false;
    }

    /// <summary>Barcode = primer UPC del WMS para el SKU, salvo que el mismo UPC quede en más de un producto o sea inválido.</summary>
    private static void AssignBarcodes(LegacyImportSources src, LegacyImportReport report, LegacyImportPlan plan)
    {
        if (src.WmsUpcs.Count == 0) return;
        const string P = LegacyImportEntities.Products;
        var upcs = src.WmsUpcs.GroupBy(u => LegacyImportRules.SkuKey(u.ItemId))
            .ToDictionary(g => g.Key, g => g.Select(u => u.Upc.Trim()).Where(u => u.Length > 0).Distinct(StringComparer.Ordinal).ToList());
        foreach (var p in plan.Products)
        {
            if (!upcs.TryGetValue(p.Key, out var list) || list.Count == 0) continue;
            var (barcode, error) = ProductRules.NormalizeBarcode(list[0]);
            if (error is not null) { report.Warn(P, p.Sku, BarcodeInvalid(p.Sku, list[0], error)); continue; }
            if (list.Count > 1) report.Warn(P, p.Sku, UpcMultiple(p.Sku, list.Count, barcode!));
            p.Barcode = barcode;
        }
        foreach (var g in plan.Products.Where(p => p.Barcode is not null).GroupBy(p => p.Barcode!, StringComparer.Ordinal).Where(g => g.Count() > 1).ToList())
        {
            report.Warn(P, g.Key, UpcShared(g.Key, string.Join(", ", g.Select(p => p.Sku))));
            foreach (var p in g) p.Barcode = null;
        }
    }

    // ---------------------------------------------------------------- almacén, zonas y posiciones

    private static void PlanWarehouse(LegacyImportConfig cfg, LegacyImportSources src, LegacyImportReport report, LegacyImportPlan plan)
    {
        var w = cfg.Warehouse;
        if (string.IsNullOrWhiteSpace(w.Code)) return;
        plan.Warehouse = new PlannedWarehouse(w.Code.Trim().ToUpperInvariant(), Blank(w.Name) ?? w.Code.Trim(), Blank(w.Line1), Blank(w.City),
            Blank(w.State), Blank(w.PostalCode), Blank(w.Country) ?? "PR", ReceivingModeRules.ParseMode(w.ReceivingMode).Mode,
            Blank(w.DefaultReceivingBin)?.ToUpperInvariant());
        report.CountRead(LegacyImportEntities.Warehouse);
        if (plan.Warehouse.ReceivingMode is { } mode) report.AddInfo("Modo de recepción", mode);
        if (plan.Warehouse.DefaultReceivingBin is { } defaultBin) report.AddInfo("Posición de recepción por defecto", defaultBin);

        var zoneConfigs = w.SingleBin?.Zone is { } single ? new List<LegacyZoneConfig> { single } : w.Zones;
        foreach (var z in zoneConfigs)
        {
            report.CountRead(LegacyImportEntities.Zones);
            var (code, error) = WarehouseRules.NormalizeCode(z.Code);
            if (error is not null) { report.Reject(LegacyImportEntities.Zones, z.Code ?? "(sin código)", error); continue; }
            if (plan.Zones.Any(x => x.Code == code)) { report.Reject(LegacyImportEntities.Zones, code!, DuplicateName(code!)); continue; }
            plan.Zones.Add(new PlannedZone(code!, Blank(z.Name) ?? code!, Blank(z.ZoneType)?.ToUpperInvariant()));
        }

        const string B = LegacyImportEntities.Bins;
        if (w.SingleBin is { } sb)
        {
            report.CountRead(B);
            var zone = plan.Zones.FirstOrDefault();
            var (code, error) = WarehouseRules.ResolveBinCode(sb.Bin, null, null, null, null);
            if (zone is null) return;
            if (error is not null) { report.Reject(B, sb.Bin ?? "(sin código)", error); return; }
            report.Map("Zona de posición", sb.Bin!, $"{code} → {zone.Code}");
            plan.Bins.Add(new PlannedBin(code!, zone.Code, null, null, null, sb.Bin!));
            return;
        }

        var rules = w.Zones.Where(z => !string.IsNullOrWhiteSpace(z.Code))
            .Select(z => new ZoneRule(z.Code!.Trim().ToUpperInvariant(), z.Name ?? z.Code!, z.ZoneType ?? string.Empty, z.MatchDescription, z.MatchLocationIds))
            .ToList();
        var skip = new HashSet<string>(w.SkipLocationIds.Select(s => s.Trim()), StringComparer.OrdinalIgnoreCase);
        var codes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var loc in src.WmsLocations)
        {
            report.CountRead(B);
            var id = loc.LocationId.Trim();
            if (skip.Contains(id)) { report.CountSkipped(B); report.Warn(B, id, LocationSkippedByConfig(id)); continue; }
            var zoneCode = LegacyImportRules.ResolveZone(id, loc.Description, loc.LocationType, rules);
            if (zoneCode is null || plan.Zones.All(z => z.Code != zoneCode))
            {
                report.CountSkipped(B);
                report.Warn(B, id, LegacyImportRules.LocationSkipped(id));
                continue;
            }
            var parts = LegacyImportRules.ParseBinCode(id);
            var (code, error) = WarehouseRules.ResolveBinCode(parts.Code, parts.Aisle, null, parts.Level, parts.Position);
            if (error is not null) { report.Reject(B, id, error); continue; }
            if (!codes.Add(code!)) { report.CountSkipped(B); report.Warn(B, id, BinCollision(id, code!)); continue; }
            if (!string.Equals(id, code, StringComparison.Ordinal)) report.Map("Posición", id, code!);
            report.Map("Zona de posición", string.IsNullOrWhiteSpace(loc.Description) ? id : $"{id} ({loc.Description.Trim()})", $"{code} → {zoneCode}");
            plan.Bins.Add(new PlannedBin(code!, zoneCode, parts.Aisle, parts.Level, parts.Position, id));
        }
        foreach (var g in plan.Bins.GroupBy(b => b.ZoneCode)) report.AddInfo($"Posiciones en zona {g.Key}", g.Count().ToString(CultureInfo.InvariantCulture));
        PlanCapacities(plan.Warehouse!.Code, src, report, plan);
    }

    /// <summary>
    /// Lote 11: cupo estimado de las posiciones que vienen del WMS (BinCapacityRules) a partir del historial de existencias
    /// por posición. Solo si se leyó MSWM (WmsBinHistory != null): Advance Solutions (sin WMS) no estima nada. Sin ninguna
    /// posición con historial, se advierte y las posiciones quedan sin cupo.
    /// </summary>
    private static void PlanCapacities(string warehouseCode, LegacyImportSources src, LegacyImportReport report, LegacyImportPlan plan)
    {
        if (src.WmsBinHistory is null || plan.Bins.Count == 0) return;
        var historical = BinCapacityRules.HistoricalMaxByCode(src.WmsBinHistory.Select(h => (h.LocationId, h.Quantity)));
        var estimates = BinCapacityRules.Estimate(plan.Bins.Select(b => new BinCapacityInput(b.Code, b.ZoneCode, b.Aisle)).ToList(), historical);
        if (estimates.Count == 0) { report.Warn(LegacyImportEntities.Bins, warehouseCode, CapacityWithoutHistory(warehouseCode)); return; }
        plan.Capacities.AddRange(estimates);
        report.AddInfo("Cupos estimados", string.Join(", ", BinCapacityOrigins.All.Select(o =>
            $"{o} {estimates.Count(e => e.Origin == o).ToString(CultureInfo.InvariantCulture)}")));
    }

    // ---------------------------------------------------------------- saldo inicial

    private static void PlanOpeningBalances(LegacyImportConfig cfg, LegacyImportSources src, LegacyImportReport report, LegacyImportPlan plan)
    {
        const string O = LegacyImportEntities.OpeningBalance;
        var source = cfg.OpeningBalances.Source;
        var byKey = plan.Products.ToDictionary(p => p.Key);
        var bins = new HashSet<string>(plan.Bins.Select(b => b.Code), StringComparer.Ordinal);

        if (source != LegacyImportConfig.SourceNone && plan.Warehouse is null)
        {
            report.Reject(O, "configuración", WarehouseRequired, severe: true);
        }
        else if (source == LegacyImportConfig.SourceMswm)
        {
            var groups = src.WmsInventory
                .GroupBy(r => (Key: LegacyImportRules.SkuKey(r.ItemId), Bin: LegacyImportRules.ParseBinCode(r.LocationId).Code))
                .Select(g => (g.Key.Key, g.Key.Bin, Sku: LegacyImportRules.NormalizeSku(g.First().ItemId) ?? g.Key.Key, Qty: g.Sum(r => r.OnHandQuantity)))
                .OrderBy(g => g.Key, StringComparer.Ordinal).ThenBy(g => g.Bin, StringComparer.Ordinal);
            foreach (var g in groups)
            {
                report.CountRead(O);
                if (g.Qty <= 0)
                {
                    report.CountSkipped(O);
                    if (g.Qty < 0) report.Warn(O, g.Sku, LegacyImportRules.NegativeQuantity(g.Sku, g.Qty));
                    continue;
                }
                if (!byKey.TryGetValue(g.Key, out var product)) { report.Reject(O, g.Sku, SkuMissingInTeikem(g.Sku)); continue; }
                if (!bins.Contains(g.Bin)) { report.Reject(O, $"{product.Sku} @ {g.Bin}", BinMissing(g.Bin, product.Sku)); continue; }
                plan.Balances.Add(new PlannedBalance(product.Sku, product.Key, g.Bin, g.Qty));
            }
        }
        else if (source == LegacyImportConfig.SourceQuickBooks)
        {
            var bin = cfg.Warehouse.SingleBin is null ? null : plan.Bins.FirstOrDefault();
            if (bin is null)
            {
                report.Reject(O, "configuración", SingleBinRequired, severe: true);
            }
            else
            {
                // Solo los ítems del archivo principal: las cantidades del archivo adicional son de la otra compañía.
                foreach (var p in plan.Products.Where(p => p.Origin == PlannedProduct.OriginQuickBooks && p.SourceQuantity is not null && p.SourceQuantity != 0))
                {
                    report.CountRead(O);
                    if (p.SourceQuantity < 0)
                    {
                        report.CountSkipped(O);
                        report.Warn(O, p.Sku, LegacyImportRules.NegativeQuantity(p.Sku, p.SourceQuantity!.Value));
                        continue;
                    }
                    plan.Balances.Add(new PlannedBalance(p.Sku, p.Key, bin.Code, p.SourceQuantity!.Value));
                }
            }
        }

        // Inactivos de QuickBooks: se crean y se dan de baja al final, salvo que tengan existencia (se dejan activos).
        var withStock = new HashSet<string>(plan.Balances.Select(b => b.Key), StringComparer.Ordinal);
        foreach (var p in plan.Products.Where(p => !p.IsActiveInSource))
        {
            if (withStock.Contains(p.Key)) report.Warn(LegacyImportEntities.Products, p.Sku, InactiveWithStock(p.Sku));
            else p.DeactivateAtEnd = true;
        }
    }

    // ---------------------------------------------------------------- proveedores

    private static void PlanSuppliers(LegacyImportConfig cfg, LegacyImportSources src, LegacyImportReport report, LegacyImportPlan plan)
    {
        const string S = LegacyImportEntities.Suppliers;
        var include = cfg.Suppliers.IncludeNames is null
            ? null
            : new HashSet<string>(cfg.Suppliers.IncludeNames.Select(n => n.Trim()), StringComparer.OrdinalIgnoreCase);
        var exclude = new HashSet<string>(cfg.Suppliers.ExcludeNames.Select(n => n.Trim()), StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var v in src.Vendors)
        {
            report.CountRead(S);
            var name = v.Vendor?.Trim();
            if (string.IsNullOrEmpty(name)) { report.CountSkipped(S); report.Warn(S, $"línea {v.Line}", RowWithoutName(v.Line)); continue; }
            if (include is not null && !include.Contains(name)) { report.CountSkipped(S); continue; }
            if (exclude.Contains(name)) { report.CountSkipped(S); report.Warn(S, name, SupplierExcluded(name)); continue; }
            if (!seen.Add(name)) { report.Reject(S, name, DuplicateName(name)); continue; }

            var contact = Blank(v.PrimaryContact) ?? Blank($"{v.FirstName} {v.LastName}");
            var address = v.BillFrom.Select(l => l?.Trim()).Where(l => !string.IsNullOrEmpty(l)).ToList();
            plan.Suppliers.Add(new PlannedSupplier(name, contact, LegacyImportRules.NormalizePhone(v.MainPhone),
                address.Count == 0 ? null : string.Join(", ", address)));
        }

        if (include is not null)
            foreach (var n in include.Where(n => !seen.Contains(n)))
                report.Warn(S, n, SupplierNotInFile(n));
    }

    // ---------------------------------------------------------------- clientes, consignatarios y contactos

    private static void PlanClients(LegacyImportConfig cfg, LegacyImportSources src, LegacyImportReport report, LegacyImportPlan plan)
    {
        const string C = LegacyImportEntities.Clients;
        var exclude = new HashSet<string>(cfg.Clients.ExcludeNames.Select(n => n.Trim()), StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var terms = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var reps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var c in src.Customers)
        {
            report.CountRead(C);
            var name = c.Customer?.Trim();
            if (string.IsNullOrEmpty(name)) { report.CountSkipped(C); report.Warn(C, $"línea {c.Line}", RowWithoutName(c.Line)); continue; }
            if (exclude.Contains(name)) { report.CountSkipped(C); report.Warn(C, name, LegacyImportRules.TestRecordSkipped(name)); continue; }
            if (!c.IsActive) { report.CountSkipped(C); report.Warn(C, name, ClientInactive(name)); continue; }
            var qbCode = LegacyImportRules.QuickBooksCodeKey(name);
            if (!seen.Add(qbCode)) { report.Reject(C, name, DuplicateName(name)); continue; }

            var term = LegacyImportRules.MapPaymentTerm(c.Terms);
            if (!string.IsNullOrWhiteSpace(c.Terms))
            {
                var qbTerm = c.Terms.Trim();
                if (term is null) report.Warn(C, name, LegacyImportRules.UnknownPaymentTerm(qbTerm));
                if (terms.TryAdd(qbTerm, term)) report.Map("Término de pago", qbTerm, term ?? "(sin equivalente)");
            }

            var rep = Blank(c.Rep);
            if (rep is not null && reps.Add(rep)) plan.RepOptions.Add(rep);

            var ship = LegacyImportRules.ParseQuickBooksAddress(c.ShipTo);
            var bill = LegacyImportRules.ParseQuickBooksAddress(c.BillTo);
            var addresses = new List<PlannedAddress>();
            var delivery = !ship.IsEmpty ? ship : !bill.IsEmpty ? bill : null;
            if (delivery is null) report.Warn(LegacyImportEntities.Consignees, name, ClientWithoutAddress(name));
            else
            {
                report.CountRead(LegacyImportEntities.Consignees);
                addresses.Add(new PlannedAddress(LocationTypes.Delivery, delivery));
                if (!ship.IsEmpty && !bill.IsEmpty && !SameLines(c.ShipTo, c.BillTo))
                {
                    report.CountRead(LegacyImportEntities.Consignees);
                    addresses.Add(new PlannedAddress(LocationTypes.Billing, bill));
                }
                if (addresses.Any(a => !a.Address.CityParsed)) report.Warn(LegacyImportEntities.Consignees, name, LegacyImportRules.AddressNotParsed(name));
            }

            var emails = new List<string>();
            foreach (var e in LegacyImportRules.SplitEmails(c.MainEmail))
            {
                report.CountRead(LegacyImportEntities.Contacts);
                try
                {
                    var value = ContactPointService.ValidateValue("EMAIL", e);
                    if (!emails.Contains(value, StringComparer.OrdinalIgnoreCase)) emails.Add(value);
                }
                catch (ValidationException ex) { report.Reject(LegacyImportEntities.Contacts, $"{name}: {e}", ex.Message); }
            }

            var phones = new List<string>();
            var phoneDigits = new HashSet<string>(StringComparer.Ordinal);
            foreach (var raw in new[] { c.MainPhone, c.AltPhone }.Concat(addresses.Select(a => a.Address.Phone)))
            {
                var phone = LegacyImportRules.NormalizePhone(raw);
                if (phone is null || !phoneDigits.Add(Digits(phone))) continue;
                report.CountRead(LegacyImportEntities.Contacts);
                var (value, error) = ContactPhone(phone);
                if (error is not null) report.Reject(LegacyImportEntities.Contacts, $"{name}: {phone}", error);
                else phones.Add(value!);
            }

            plan.Clients.Add(new PlannedClient(qbCode, name, Blank(c.Company), term, Positive(LegacyImportRules.ParseQuickBooksNumber(c.CreditLimit)),
                rep, addresses, emails, phones));
        }

        plan.QbCodeField = cfg.Clients.QbCodeCustomField && plan.Clients.Count > 0;
        plan.RepField = cfg.Clients.RepCustomField && plan.RepOptions.Count > 0;
        if (plan.QbCodeField) report.CountRead(LegacyImportEntities.CustomFields);
        if (plan.RepField) report.CountRead(LegacyImportEntities.CustomFields);
    }

    /// <summary>
    /// Teléfono aceptado por ContactPointService ('787.686.6464', '787-249-8344'). Si el formato de QuickBooks no pasa
    /// ('(787) 249-8344'), se reintenta con solo sus dígitos (10 → 'ddd-ddd-dddd'). Devuelve el valor o el mensaje exacto.
    /// </summary>
    public static (string? Value, string? Error) ContactPhone(string phone)
    {
        try { return (ContactPointService.ValidateValue("PHONE", phone), null); }
        catch (ValidationException ex)
        {
            var digits = Digits(phone);
            if (digits.Length == 11 && digits[0] == '1') digits = digits[1..];
            var candidate = digits.Length == 10 ? $"{digits[..3]}-{digits[3..6]}-{digits[6..]}" : digits;
            try { return (ContactPointService.ValidateValue("PHONE", candidate), null); }
            catch (ValidationException) { return (null, ex.Message); }
        }
    }

    // ---------------------------------------------------------------- utilidades

    private static bool SameLines(IReadOnlyList<string?> a, IReadOnlyList<string?> b)
    {
        static List<string> Clean(IReadOnlyList<string?> l) => l.Select(x => x?.Trim()).Where(x => !string.IsNullOrEmpty(x)).Select(x => x!).ToList();
        var x = Clean(a);
        var y = Clean(b);
        return x.Count == y.Count && x.Zip(y).All(p => string.Equals(p.First, p.Second, StringComparison.OrdinalIgnoreCase));
    }

    private static string Digits(string s) => new(s.Where(char.IsAsciiDigit).ToArray());

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    /// <summary>Monto &gt; 0 redondeado a 4 decimales (DECIMAL(18,4)); 0, negativo o vacío → null.</summary>
    private static decimal? Positive(decimal? value) => value is > 0 ? Math.Round(value.Value, 4) : null;
}

/// <summary>
/// Lote 10 (P0): ejecuta la migración de una compañía. Lee todo primero, arma el plan (LegacyImportPlanner) y lo aplica con
/// los servicios existentes dentro de la compañía destino, como el administrador de la compañía y con privilegios de
/// plataforma (el comando corre fuera del pipeline HTTP, igual que db-init). Cada rechazo de un servicio se anota con su
/// mensaje exacto y se sigue con la fila siguiente; en dry-run solo se hacen lecturas.
/// </summary>
public sealed class LegacyImportService(
    TeikemDbContext db,
    TenantContext tc,
    ILookupCache lookups,
    ProvisioningService provisioning,
    ProductCategoryService categories,
    ProductService products,
    SupplierService suppliers,
    ClientService clients,
    LocationService locations,
    ContactPointService contacts,
    CustomFieldService customFields,
    WarehouseService warehouses,
    WarehouseLayoutService layout,
    InventoryLedger ledger,
    IConfiguration config,
    ILogger<LegacyImportService> logger,
    InventoryReconciliationService reconciliation)
{
    public const int PostingBatchSize = 200;

    /// <summary>Contraseña temporal si el aprovisionamiento creó al administrador (el runner solo avisa; nunca la imprime ni la reporta).</summary>
    public string? TemporaryAdminPassword { get; private set; }

    /// <summary>Estado de la ejecución: ids resueltos (existentes o creados) por clave natural.</summary>
    private sealed class RunState
    {
        public bool DryRun { get; init; }
        /// <summary>false = compañía nueva en dry-run (o base inaccesible): no hay nada que leer, todo se crearía.</summary>
        public bool ReadDb { get; init; }
        /// <summary>true = la base de Teikem responde: los catálogos globales (motivo OPENING_BALANCE, términos de pago) se pueden verificar aunque la compañía sea nueva.</summary>
        public bool CatalogReadable { get; init; }
        /// <summary>Modo --update: las filas existentes se actualizan desde QuickBooks (nunca el saldo inicial de una compañía que ya existía).</summary>
        public bool Update { get; init; }
        /// <summary>true = la compañía ya existía antes de esta corrida. En --update, solo entonces se omite el saldo inicial: una
        /// compañía que esta misma corrida acaba de crear no tiene saldo que "no tocar" (D51, corregido 2026-09-29 tras una carga
        /// real que dejó Advance Depot con 0 unidades de inventario tras usar --update en la primera carga).</summary>
        public bool CompanyAlreadyExisted { get; init; }
        public Dictionary<string, int?> CategoryIds { get; } = new(StringComparer.OrdinalIgnoreCase);
        public (int Id, Guid PublicId)? Warehouse { get; set; }
        /// <summary>Lote 16: el almacén se creó en esta corrida (modo y posición de recepción por defecto solo al crear).</summary>
        public bool WarehouseCreated { get; set; }
        public bool WarehouseWouldExist { get; set; }
        public Dictionary<string, int> ZoneIds { get; } = new(StringComparer.Ordinal);
        public HashSet<string> ZonesWouldExist { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, int> BinIds { get; } = new(StringComparer.Ordinal);
        public HashSet<string> BinsWouldExist { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, (int Id, Guid PublicId)> ProductIds { get; } = new(StringComparer.Ordinal);
        public HashSet<string> ProductsWouldExist { get; } = new(StringComparer.Ordinal);
        public List<(string Sku, Guid PublicId)> ToDeactivate { get; } = new();
        public int? QbFieldId { get; set; }
        public bool QbFieldUsable { get; set; }
        public bool RepFieldUsable { get; set; }
    }

    public Task<LegacyImportReport> RunAsync(LegacyImportConfig cfg, bool dryRun, CancellationToken ct) => RunAsync(cfg, dryRun, update: false, ct);

    /// <summary>
    /// <paramref name="update"/> = modo --update: además de agregar lo que falta, actualiza desde QuickBooks las filas existentes
    /// (productos, proveedores, clientes y sus consignatarios); nunca toca el saldo inicial.
    /// </summary>
    public async Task<LegacyImportReport> RunAsync(LegacyImportConfig cfg, bool dryRun, bool update, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(cfg);
        var company = cfg.Company.Name!.Trim();
        var report = new LegacyImportReport(company, dryRun) { UpdateMode = update };
        report.AddInfo("Configuración", cfg.ConfigPath ?? "(en memoria)");
        report.AddInfo("Modo", update ? "--update: agrega y actualiza maestros; el saldo inicial no se toca" : "solo agrega lo que falta");
        report.AddInfo("Productos (QuickBooks)", cfg.Sources.Products ?? "—");
        if (cfg.Sources.ExtraProducts?.Path is { } extraPath) report.AddInfo("Productos adicionales", $"{extraPath} (categoría {cfg.Sources.ExtraProducts.WhereCategory})");
        report.AddInfo("Clientes (QuickBooks)", cfg.Sources.Customers ?? "—");
        report.AddInfo("Proveedores (QuickBooks)", cfg.Sources.Vendors ?? "—");
        report.AddInfo("Fuente del saldo inicial", cfg.OpeningBalances.Source);

        // (a) Leer todo primero y armar el plan en memoria.
        var sources = await ReadSourcesAsync(cfg, report, ct);
        var plan = LegacyImportPlanner.Build(cfg, sources, report);
        logger.LogInformation("Plan de {Company}: {Products} productos, {Bins} posiciones, {Clients} clientes, {Balances} asientos de saldo inicial.",
            company, plan.Products.Count, plan.Bins.Count, plan.Clients.Count, plan.Balances.Count);

        // (b) Compañía.
        report.CountRead(LegacyImportEntities.Company);
        var (tenantId, adminUserId, readDb, alreadyExisted) = await ResolveCompanyAsync(cfg, dryRun, report, ct);
        if (tenantId is null && !dryRun) return report; // no se pudo aprovisionar: rechazo grave ya anotado

        var state = new RunState { DryRun = dryRun, ReadDb = readDb && tenantId is not null, CatalogReadable = readDb, Update = update, CompanyAlreadyExisted = alreadyExisted };
        tc.IsPlatformAdmin = true;
        tc.IsAuthenticated = true;
        tc.Lang = string.IsNullOrWhiteSpace(cfg.Company.Lang) ? tc.Lang : cfg.Company.Lang.Trim().ToLowerInvariant();

        // Compañía nueva en dry-run: no hay tenant; todo se crearía. Se simula con el contexto sin tenant y sin lecturas.
        using var asTenant = tenantId is int tid ? tc.As(tid, adminUserId) : null;
        await ImportCategoriesAsync(plan, state, report, ct);
        await ImportWarehouseAsync(plan, state, report, ct);
        await ImportSuppliersAsync(plan, state, report, ct);
        await ImportProductsAsync(cfg, plan, state, report, ct);
        await ImportCustomFieldsAsync(plan, state, report, ct);
        await ImportClientsAsync(cfg, plan, state, report, ct);
        await ImportOpeningBalancesAsync(cfg, plan, state, report, ct);
        if (!dryRun)
        {
            await DeactivateInactiveProductsAsync(state, report, ct);
            await ReconcileAsync(state, report, ct);
        }
        return report;
    }

    // ================================================================ lectura de fuentes

    private async Task<LegacyImportSources> ReadSourcesAsync(LegacyImportConfig cfg, LegacyImportReport report, CancellationToken ct)
    {
        var items = QuickBooksCsvReader.ReadItems(cfg.Sources.Products!);
        var extra = cfg.Sources.ExtraProducts?.Path is { } extraPath ? QuickBooksCsvReader.ReadItems(extraPath) : Array.Empty<QbItem>();
        var customers = cfg.Sources.Customers is { } cPath ? QuickBooksCsvReader.ReadCustomers(cPath) : Array.Empty<QbCustomer>();
        var vendors = cfg.Sources.Vendors is { } vPath ? QuickBooksCsvReader.ReadVendors(vPath) : Array.Empty<QbVendor>();

        IReadOnlyList<WmsItem> wmsItems = Array.Empty<WmsItem>();
        IReadOnlyList<WmsLocation> wmsLocations = Array.Empty<WmsLocation>();
        IReadOnlyList<WmsInventoryRow> wmsInventory = Array.Empty<WmsInventoryRow>();
        IReadOnlyList<WmsUpc> wmsUpcs = Array.Empty<WmsUpc>();
        IReadOnlyList<WmsBinQuantity>? wmsBinHistory = null;
        if (cfg.Sources.Mswm is { } m && !string.IsNullOrWhiteSpace(m.ConnectionStringName) && !string.IsNullOrWhiteSpace(m.WarehouseId))
        {
            var cs = config.GetConnectionString(m.ConnectionStringName.Trim());
            if (string.IsNullOrWhiteSpace(cs)) throw new ValidationException(LegacyImportPlanner.MswmConnectionMissing(m.ConnectionStringName.Trim()));
            var reader = new MswmReader(cs);
            var warehouseId = m.WarehouseId.Trim();
            // Solo el almacén configurado (Main): TrussPR y cualquier otro se ignoran por diseño.
            wmsItems = await reader.ReadItemsAsync(warehouseId, ct);
            wmsLocations = await reader.ReadLocationsAsync(warehouseId, ct);
            wmsInventory = await reader.ReadInventoryAsync(warehouseId, ct);
            wmsUpcs = await reader.ReadUpcsAsync(warehouseId, ct);
            // Lote 11: historial de existencias por posición para estimar el cupo (solo si el almacén viene de MSWM).
            if (cfg.Warehouse.SingleBin is null && !string.IsNullOrWhiteSpace(cfg.Warehouse.Code))
            {
                wmsBinHistory = await reader.ReadBinHistoryAsync(warehouseId, ct);
                report.AddInfo("Historial por posición (MSWM)", string.Join(", ", wmsBinHistory.GroupBy(h => h.Source)
                    .Select(g => $"{g.Key} {g.Count().ToString(CultureInfo.InvariantCulture)} fotos")));
            }
            report.AddInfo("WMS (MSWM)", $"almacén {warehouseId}: {wmsItems.Count} ítems, {wmsLocations.Count} posiciones, {wmsInventory.Count} filas de inventario, {wmsUpcs.Count} UPC");
        }
        return new LegacyImportSources(items, extra, customers, vendors, wmsItems, wmsLocations, wmsInventory, wmsUpcs, wmsBinHistory);
    }

    // ================================================================ compañía

    private async Task<(int? TenantId, int? AdminUserId, bool ReadDb, bool AlreadyExisted)> ResolveCompanyAsync(LegacyImportConfig cfg, bool dryRun,
        LegacyImportReport report, CancellationToken ct)
    {
        const string E = LegacyImportEntities.Company;
        var name = cfg.Company.Name!.Trim();
        Tenant? existing;
        try
        {
            using (tc.BypassTenantFilter())
                existing = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Name == name, ct);
        }
        catch (Exception ex) when (dryRun && ex is not OperationCanceledException)
        {
            report.Warn(E, name, LegacyImportPlanner.DbUnavailable(ex.GetBaseException().Message));
            report.CountCreated(E);
            return (null, null, false, false);
        }

        if (existing is not null)
        {
            report.CountExisting(E);
            report.AddInfo("Compañía", $"{existing.Name} (id {existing.TenantId}, ya existía)");
            if (dryRun || string.IsNullOrWhiteSpace(cfg.Company.AdminEmail)) return (existing.TenantId, null, true, true);
            using (tc.BypassTenantFilter())
            using (tc.As(existing.TenantId))
            {
                var (adminId, temp) = await provisioning.EnsureAdminUserAsync(existing, cfg.Company.AdminEmail.Trim(),
                    cfg.Company.AdminFullName ?? cfg.Company.AdminEmail.Trim(), null, ct);
                TemporaryAdminPassword = temp;
                return (existing.TenantId, adminId, true, true);
            }
        }

        report.CountCreated(E);
        if (dryRun)
        {
            report.AddInfo("Compañía", $"{name} (nueva: se aprovisionaría)");
            return (null, null, true, false);
        }
        if (string.IsNullOrWhiteSpace(cfg.Company.AdminEmail))
        {
            report.Reject(E, name, LegacyImportPlanner.AdminEmailRequired, severe: true);
            return (null, null, false, false);
        }
        try
        {
            var result = await provisioning.ProvisionAsync(new TenantProvisionRequest(name, cfg.Company.LegalName, cfg.Company.TaxId, cfg.Company.Lang,
                cfg.Company.Modules, cfg.Company.AdminEmail.Trim(), cfg.Company.AdminFullName ?? cfg.Company.AdminEmail.Trim(), null), ct);
            TemporaryAdminPassword = result.TemporaryPassword;
            report.AddInfo("Compañía", $"{name} (id {result.Tenant.Id}, aprovisionada)");
            return (result.Tenant.Id, result.AdminUserId, true, false);
        }
        catch (TeikemException ex)
        {
            report.Reject(E, name, Describe(ex), severe: true);
            return (null, null, false, false);
        }
        finally { db.ChangeTracker.Clear(); }
    }

    // ================================================================ categorías

    private async Task ImportCategoriesAsync(LegacyImportPlan plan, RunState s, LegacyImportReport report, CancellationToken ct)
    {
        const string E = LegacyImportEntities.Categories;
        var existing = s.ReadDb
            ? (await categories.ListAsync(includeInactive: true, ct)).Where(c => c.ParentId is null)
                .GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.OrderByDescending(c => c.IsActive).First(), StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, ProductCategoryDto>(StringComparer.OrdinalIgnoreCase);

        foreach (var name in plan.Categories)
        {
            if (existing.TryGetValue(name, out var c))
            {
                report.CountExisting(E);
                if (c.IsActive) s.CategoryIds[name] = c.Id;
                else { s.CategoryIds[name] = null; report.Warn(E, name, LegacyImportPlanner.CategoryInactive(name)); }
                continue;
            }
            if (s.DryRun) { report.CountCreated(E); s.CategoryIds[name] = null; continue; }
            var dto = await TryAsync(report, E, name, () => categories.CreateAsync(new ProductCategoryRequest(name), ct));
            if (dto is not null) report.CountCreated(E);
            s.CategoryIds[name] = dto?.Id;
        }
    }

    // ================================================================ almacén, zonas y posiciones

    private async Task ImportWarehouseAsync(LegacyImportPlan plan, RunState s, LegacyImportReport report, CancellationToken ct)
    {
        if (plan.Warehouse is not { } w) return;
        const string E = LegacyImportEntities.Warehouse;
        var existing = s.ReadDb
            ? await db.Warehouses.AsNoTracking().Where(x => x.Code == w.Code).Select(x => new { x.WarehouseId, x.PublicId }).FirstOrDefaultAsync(ct)
            : null;
        if (existing is not null)
        {
            report.CountExisting(E);
            s.Warehouse = (existing.WarehouseId, existing.PublicId);
        }
        else if (s.DryRun)
        {
            report.CountCreated(E);
            s.WarehouseWouldExist = true;
        }
        else
        {
            var dto = await TryAsync(report, E, w.Code, () => warehouses.CreateAsync(
                new WarehouseCreateRequest(w.Code, w.Name, w.Line1, w.City, w.State, w.PostalCode, w.Country, w.ReceivingMode), ct));
            if (dto is null) return;
            report.CountCreated(E);
            s.Warehouse = (dto.Warehouse.Id, dto.Warehouse.PublicId);
            s.WarehouseCreated = true;
        }

        // Zonas por código.
        const string Z = LegacyImportEntities.Zones;
        var zones = s.Warehouse is { } wh && s.ReadDb
            ? await db.WarehouseZones.AsNoTracking().Where(z => z.WarehouseId == wh.Id).Select(z => new { z.WarehouseZoneId, z.Code }).ToListAsync(ct)
            : new();
        foreach (var z in plan.Zones)
        {
            var found = zones.FirstOrDefault(x => string.Equals(x.Code, z.Code, StringComparison.OrdinalIgnoreCase));
            if (found is not null) { report.CountExisting(Z); s.ZoneIds[z.Code] = found.WarehouseZoneId; continue; }
            if (s.DryRun) { report.CountCreated(Z); s.ZonesWouldExist.Add(z.Code); continue; }
            var dto = await TryAsync(report, Z, z.Code, () => layout.CreateZoneAsync(s.Warehouse!.Value.PublicId, new WarehouseZoneRequest(z.Code, z.Name, z.ZoneType), ct));
            if (dto is null) continue;
            report.CountCreated(Z);
            s.ZoneIds[z.Code] = dto.Id;
        }

        // Posiciones por código (consulta directa de lectura: una sola para las ~3.900 de Depot).
        const string B = LegacyImportEntities.Bins;
        var bins = s.Warehouse is { } wh2 && s.ReadDb
            ? await db.WarehouseBins.AsNoTracking().Where(b => b.WarehouseId == wh2.Id).Select(b => new { b.WarehouseBinId, b.Code, b.MaxCapacityQty })
                .ToDictionaryAsync(b => b.Code, b => (Id: b.WarehouseBinId, Capacity: b.MaxCapacityQty), StringComparer.OrdinalIgnoreCase, ct)
            : new Dictionary<string, (int Id, int? Capacity)>(StringComparer.OrdinalIgnoreCase);
        // Lote 11: cupo estimado por código (solo posiciones del WMS). Nueva → nace con él; existente con cupo → se conserva;
        // existente sin cupo → se llena solo con --update (BinCapacityRules.Decide).
        var capacities = plan.Capacities.ToDictionary(c => c.Code, StringComparer.Ordinal);
        var toFill = new List<(BinCapacityEstimate Estimate, int BinId)>();
        var done = 0;
        foreach (var b in plan.Bins)
        {
            var estimate = capacities.GetValueOrDefault(b.Code);
            if (bins.TryGetValue(b.Code, out var existingBin))
            {
                report.CountExisting(B);
                s.BinIds[b.Code] = existingBin.Id;
                if (estimate is null) continue;
                var action = BinCapacityRules.Decide(binExists: true, existingBin.Capacity, s.Update);
                if (action == BinCapacityAction.FillExisting) toFill.Add((estimate, existingBin.Id));
                else report.AddCapacity(estimate, LegacyImportPlanner.CapacityResult(action, existingBin.Capacity, s.DryRun));
                continue;
            }
            var zoneId = s.ZoneIds.TryGetValue(b.ZoneCode, out var zid) ? zid : (int?)null;
            if (zoneId is null && !s.ZonesWouldExist.Contains(b.ZoneCode))
            {
                report.Reject(B, b.Code, LegacyImportPlanner.ZoneMissing(b.ZoneCode, b.Code));
                if (estimate is not null) report.AddCapacity(estimate, LegacyImportPlanner.CapacityNotApplied);
                continue;
            }
            if (s.DryRun)
            {
                report.CountCreated(B);
                s.BinsWouldExist.Add(b.Code);
                if (estimate is not null) report.AddCapacity(estimate, LegacyImportPlanner.CapacityWouldAssign);
                continue;
            }
            var dto = await TryAsync(report, B, b.Code, () => layout.CreateBinAsync(s.Warehouse!.Value.PublicId,
                new WarehouseBinRequest(zoneId, b.Code, b.Aisle, null, b.Level, b.Position, MaxCapacityQty: estimate?.Capacity), ct));
            if (dto is null)
            {
                if (estimate is not null) report.AddCapacity(estimate, LegacyImportPlanner.CapacityNotApplied);
                continue;
            }
            report.CountCreated(B);
            s.BinIds[b.Code] = dto.Id;
            if (estimate is not null) report.AddCapacity(estimate, LegacyImportPlanner.CapacityAssigned);
            if (++done % 500 == 0) logger.LogInformation("Posiciones creadas: {Count} de {Total}.", done, plan.Bins.Count);
        }
        await FillCapacitiesAsync(toFill, s, report, ct);
        await SetDefaultReceivingBinAsync(w, s, report, ct);
    }

    /// <summary>
    /// Lote 16 (D12): posición de recepción por defecto del almacén recién creado en esta corrida (--update no la pisa en uno
    /// existente). Se fija con el PATCH del almacén, que exige que sea del almacén, de zona STAGING o CROSSDOCK y activa; una
    /// posición que no existe o no es de recepción queda como rechazo del almacén.
    /// </summary>
    private async Task SetDefaultReceivingBinAsync(PlannedWarehouse w, RunState s, LegacyImportReport report, CancellationToken ct)
    {
        if (w.DefaultReceivingBin is not { } code || s.DryRun || !s.WarehouseCreated || s.Warehouse is not { } wh) return;
        const string E = LegacyImportEntities.Warehouse;
        if (!s.BinIds.TryGetValue(code, out var binId))
        {
            report.Reject(E, w.Code, $"La posición de recepción por defecto {code} no existe en el almacén.");
            return;
        }
        var dto = await TryAsync(report, E, w.Code, () => warehouses.UpdateAsync(wh.PublicId, new WarehousePatchRequest(DefaultReceivingBinId: binId), ct));
        if (dto is not null) report.AddInfo("Posición de recepción por defecto", $"{code} asignada");
    }

    /// <summary>
    /// Lote 11, modo --update: llena el cupo de las posiciones existentes que no lo tienen, con la asignación en bloque del
    /// almacén (WarehouseLayoutService.SetBinsCapacityAsync, una llamada por valor de cupo) y onlyWithoutCapacity = true: dentro
    /// de su transacción solo toca las que SIGUEN sin cupo, así que nunca pisa uno capturado a mano entretanto. Cada posición
    /// cambiada queda en la bitácora de auditoría (interceptor). En dry-run solo reporta.
    /// </summary>
    private async Task FillCapacitiesAsync(List<(BinCapacityEstimate Estimate, int BinId)> toFill, RunState s, LegacyImportReport report, CancellationToken ct)
    {
        if (toFill.Count == 0) return;
        const string B = LegacyImportEntities.Bins;
        if (s.DryRun || s.Warehouse is not { } wh)
        {
            foreach (var (e, _) in toFill) report.AddCapacity(e, LegacyImportPlanner.CapacityWouldFill);
            report.CountUpdated(B, toFill.Count);
            return;
        }
        foreach (var group in toFill.GroupBy(x => x.Estimate.Capacity).OrderBy(g => g.Key))
        {
            var rows = group.ToList();
            var result = await TryAsync(report, B, $"cupo {group.Key.ToString(CultureInfo.InvariantCulture)} ({rows.Count} posiciones)",
                () => layout.SetBinsCapacityAsync(wh.PublicId, new WarehouseBinCapacityRequest(BinIds: rows.Select(r => r.BinId).ToArray(),
                    IncludeInactive: true, OnlyWithoutCapacity: true, MaxCapacityQty: group.Key), ct));
            foreach (var (e, _) in rows) report.AddCapacity(e, result is null ? LegacyImportPlanner.CapacityNotApplied : LegacyImportPlanner.CapacityFilled);
            if (result is not null) report.CountUpdated(B, result.Changed);
        }
    }

    // ================================================================ proveedores

    private async Task ImportSuppliersAsync(LegacyImportPlan plan, RunState s, LegacyImportReport report, CancellationToken ct)
    {
        const string E = LegacyImportEntities.Suppliers;
        var existing = s.ReadDb
            ? (await suppliers.ListAsync(includeInactive: true, null, ct)).GroupBy(x => x.Name.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.IsActive).First(), StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, SupplierDto>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in plan.Suppliers)
        {
            if (existing.TryGetValue(p.Name, out var found))
            {
                if (s.Update) await UpdateSupplierAsync(p, found, s, report, ct); else report.CountExisting(E);
                continue;
            }
            if (s.DryRun) { report.CountCreated(E); continue; }
            var dto = await TryAsync(report, E, p.Name, () => suppliers.CreateAsync(
                new SupplierRequest(p.Name, p.ContactName, p.Phone, Email: null, PaymentTerm: null, Notes: p.Notes), ct));
            if (dto is not null) report.CountCreated(E);
        }
    }

    /// <summary>Modo --update: contacto, teléfono y notas del proveedor desde QuickBooks (solo los campos con valor y distintos).</summary>
    private async Task UpdateSupplierAsync(PlannedSupplier p, SupplierDto found, RunState s, LegacyImportReport report, CancellationToken ct)
    {
        const string E = LegacyImportEntities.Suppliers;
        var changes = new List<(string Field, string? From, string? To)>();
        if (Changed(found.ContactName, p.ContactName)) changes.Add(("Contacto", found.ContactName, p.ContactName));
        if (Changed(found.Phone, p.Phone)) changes.Add(("Teléfono", found.Phone, p.Phone));
        if (Changed(found.Notes, p.Notes)) changes.Add(("Notas", found.Notes, p.Notes));
        if (changes.Count == 0) { report.CountExisting(E); return; }
        if (!s.DryRun)
        {
            var dto = await TryAsync(report, E, p.Name, () => suppliers.UpdateAsync(found.Id, new SupplierPatchRequest(
                ContactName: changes.Any(c => c.Field == "Contacto") ? p.ContactName : null,
                Phone: changes.Any(c => c.Field == "Teléfono") ? p.Phone : null,
                Notes: changes.Any(c => c.Field == "Notas") ? p.Notes : null), ct));
            if (dto is null) return;
        }
        report.CountUpdated(E);
        foreach (var (field, from, to) in changes) report.AddUpdate(E, p.Name, field, from, to);
    }

    /// <summary>Hay cambio cuando QuickBooks trae valor y difiere del actual (un vacío de QuickBooks nunca borra un dato de Teikem).</summary>
    private static bool Changed(string? current, string? incoming)
        => !string.IsNullOrWhiteSpace(incoming) && !string.Equals(current?.Trim(), incoming.Trim(), StringComparison.Ordinal);

    private static bool Changed(decimal? current, decimal? incoming) => incoming.HasValue && current != incoming;

    private static string? Money(decimal? v) => v?.ToString("0.####", CultureInfo.InvariantCulture);

    // ================================================================ productos

    private async Task ImportProductsAsync(LegacyImportConfig cfg, LegacyImportPlan plan, RunState s, LegacyImportReport report, CancellationToken ct)
    {
        const string E = LegacyImportEntities.Products;
        var existing = s.ReadDb
            ? (await db.Products.AsNoTracking().Where(p => p.ClientId == null)
                .Select(p => new ExistingProduct(p.ProductId, p.PublicId, p.Sku, p.Name, p.ProductCategoryId, p.PurchaseCost, p.SalePrice, p.Barcode)).ToListAsync(ct))
                .GroupBy(p => LegacyImportRules.SkuKey(p.Sku)).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal)
            : new Dictionary<string, ExistingProduct>(StringComparer.Ordinal);
        var barcodesInUse = s.ReadDb
            ? (await db.Products.AsNoTracking().Where(p => p.IsActive && p.Barcode != null).Select(p => p.Barcode!).ToListAsync(ct)).ToHashSet(StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);

        var done = 0;
        foreach (var p in plan.Products)
        {
            if (existing.TryGetValue(p.Key, out var found))
            {
                s.ProductIds[p.Key] = (found.ProductId, found.PublicId);
                if (s.Update) await UpdateProductAsync(p, found, s, barcodesInUse, report, ct); else report.CountExisting(E);
                continue;
            }
            var barcode = p.Barcode;
            if (barcode is not null && barcodesInUse.Contains(barcode))
            {
                report.Warn(E, p.Sku, LegacyImportPlanner.BarcodeInUse(p.Sku, barcode));
                barcode = null;
            }
            if (s.DryRun) { report.CountCreated(E); s.ProductsWouldExist.Add(p.Key); continue; }

            var categoryId = p.Category is not null && s.CategoryIds.TryGetValue(p.Category, out var cid) ? cid : null;
            var dto = await TryAsync(report, E, p.Sku, () => products.CreateAsync(new ProductCreateRequest(
                Sku: p.Sku, Name: p.Name, CategoryId: categoryId, BaseUom: cfg.Products.BaseUom, TrackingType: cfg.Products.TrackingType,
                Barcode: barcode, PurchaseCost: p.PurchaseCost, SalePrice: p.SalePrice, Brand: p.Brand), ct));
            if (dto is null) continue;
            report.CountCreated(E);
            s.ProductIds[p.Key] = (dto.Product.Id, dto.Product.PublicId);
            if (barcode is not null) barcodesInUse.Add(barcode);
            if (p.DeactivateAtEnd) s.ToDeactivate.Add((p.Sku, dto.Product.PublicId));
            if (++done % 100 == 0) logger.LogInformation("Productos creados: {Count} de {Total}.", done, plan.Products.Count);
        }
    }

    private sealed record ExistingProduct(int ProductId, Guid PublicId, string Sku, string Name, int? ProductCategoryId, decimal? PurchaseCost, decimal? SalePrice, string? Barcode);

    /// <summary>
    /// Modo --update: nombre, categoría, costo, precio y código de barras desde QuickBooks/WMS. Nunca cambia el SKU, la unidad
    /// base ni el seguimiento (inmutables con movimientos) ni el estado activo/inactivo. Un nombre igual al SKU (ítem sin
    /// descripción en QuickBooks) no pisa un nombre ya capturado en Teikem.
    /// </summary>
    private async Task UpdateProductAsync(PlannedProduct p, ExistingProduct found, RunState s, HashSet<string> barcodesInUse, LegacyImportReport report, CancellationToken ct)
    {
        const string E = LegacyImportEntities.Products;
        var changes = new List<(string Field, string? From, string? To)>();
        var nameIsSku = string.Equals(p.Name, p.Sku, StringComparison.Ordinal);
        if (!nameIsSku && Changed(found.Name, p.Name)) changes.Add(("Nombre", found.Name, p.Name));
        int? categoryId = p.Category is not null && s.CategoryIds.TryGetValue(p.Category, out var cid) ? cid : null;
        if (categoryId is int newCat && found.ProductCategoryId != newCat) changes.Add(("Categoría", found.ProductCategoryId?.ToString(CultureInfo.InvariantCulture), p.Category));
        if (Changed(found.PurchaseCost, p.PurchaseCost)) changes.Add(("Costo", Money(found.PurchaseCost), Money(p.PurchaseCost)));
        if (Changed(found.SalePrice, p.SalePrice)) changes.Add(("Precio", Money(found.SalePrice), Money(p.SalePrice)));
        string? barcode = null;
        if (p.Barcode is not null && !string.Equals(found.Barcode, p.Barcode, StringComparison.Ordinal))
        {
            if (barcodesInUse.Contains(p.Barcode)) report.Warn(E, p.Sku, LegacyImportPlanner.BarcodeInUse(p.Sku, p.Barcode));
            else { barcode = p.Barcode; changes.Add(("Código de barras", found.Barcode, p.Barcode)); }
        }
        if (changes.Count == 0) { report.CountExisting(E); return; }
        if (!s.DryRun)
        {
            var dto = await TryAsync(report, E, p.Sku, () => products.UpdateAsync(found.PublicId, new ProductPatchRequest(
                Name: changes.Any(c => c.Field == "Nombre") ? p.Name : null,
                CategoryId: changes.Any(c => c.Field == "Categoría") ? categoryId : null,
                PurchaseCost: changes.Any(c => c.Field == "Costo") ? p.PurchaseCost : null,
                SalePrice: changes.Any(c => c.Field == "Precio") ? p.SalePrice : null,
                Barcode: barcode), ct));
            if (dto is null) return;
            if (barcode is not null) barcodesInUse.Add(barcode);
        }
        report.CountUpdated(E);
        foreach (var (field, from, to) in changes) report.AddUpdate(E, p.Sku, field, from, to);
    }

    // ================================================================ campos personalizados

    private async Task ImportCustomFieldsAsync(LegacyImportPlan plan, RunState s, LegacyImportReport report, CancellationToken ct)
    {
        if (!plan.QbCodeField && !plan.RepField) return;
        const string E = LegacyImportEntities.CustomFields;
        var defs = s.ReadDb
            ? (await customFields.GetDefinitionsAsync(EntityTypes.Client, includeInactive: true, ct)).ToDictionary(d => d.FieldKey, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, CustomFieldDefinitionDto>(StringComparer.OrdinalIgnoreCase);

        if (plan.QbCodeField)
        {
            var key = LegacyImportPlanner.QbCodeFieldKey;
            if (defs.TryGetValue(key, out var d))
            {
                report.CountExisting(E);
                s.QbFieldId = d.Id;
                s.QbFieldUsable = d.IsActive;
                if (!d.IsActive) report.Warn(E, key, LegacyImportPlanner.CustomFieldInactive(key));
            }
            else if (s.DryRun) { report.CountCreated(E); s.QbFieldUsable = true; }
            else
            {
                var dto = await TryAsync(report, E, key, () => customFields.CreateDefinitionAsync(EntityTypes.Client, new CustomFieldDefinitionUpsert(
                    key, Labels("Código QuickBooks", "QuickBooks code"), null, CustomFieldDataTypes.Text, IsRequired: false, IsUnique: false,
                    DefaultValue: null, ValidationJson: null, RefEntity: null, ShowInList: false, SortOrder: null, Options: null), ct));
                if (dto is not null) { report.CountCreated(E); s.QbFieldId = dto.Id; s.QbFieldUsable = true; }
            }
        }

        if (plan.RepField)
        {
            var key = LegacyImportPlanner.RepFieldKey;
            if (defs.TryGetValue(key, out var d))
            {
                report.CountExisting(E);
                s.RepFieldUsable = d.IsActive;
                if (!d.IsActive) { report.Warn(E, key, LegacyImportPlanner.CustomFieldInactive(key)); return; }
                // Idempotente: solo se agregan las opciones que falten (las existentes se conservan tal cual).
                var active = d.Options.Where(o => o.IsActive).ToList();
                var missing = plan.RepOptions.Where(r => !active.Any(o => string.Equals(o.Value, r, StringComparison.OrdinalIgnoreCase))).ToList();
                if (missing.Count == 0 || !string.IsNullOrWhiteSpace(d.RefEntity)) return;
                foreach (var m in missing) report.Map("Representante", m, "opción agregada");
                if (s.DryRun) return;
                var options = active.Select(o => new CustomFieldOptionUpsert(o.Value, o.Labels, o.SortOrder))
                    .Concat(missing.Select(m => new CustomFieldOptionUpsert(m, Labels(m, m), null))).ToList();
                await TryAsync(report, E, key, () => customFields.UpdateDefinitionAsync(d.Id, new CustomFieldDefinitionUpsert(
                    d.FieldKey, d.Labels, null, d.DataType, d.IsRequired, d.IsUnique, d.DefaultValue, d.ValidationJson, d.RefEntity, d.ShowInList,
                    d.SortOrder, options), ct));
            }
            else if (s.DryRun) { report.CountCreated(E); s.RepFieldUsable = true; }
            else
            {
                var options = plan.RepOptions.Select(r => new CustomFieldOptionUpsert(r, Labels(r, r), null)).ToList();
                var dto = await TryAsync(report, E, key, () => customFields.CreateDefinitionAsync(EntityTypes.Client, new CustomFieldDefinitionUpsert(
                    key, Labels("Representante", "Sales rep"), null, CustomFieldDataTypes.Select, IsRequired: false, IsUnique: false,
                    DefaultValue: null, ValidationJson: null, RefEntity: null, ShowInList: true, SortOrder: null, Options: options), ct));
                if (dto is not null) { report.CountCreated(E); s.RepFieldUsable = true; }
            }
        }
    }

    // ================================================================ clientes, consignatarios y contactos

    private async Task ImportClientsAsync(LegacyImportConfig cfg, LegacyImportPlan plan, RunState s, LegacyImportReport report, CancellationToken ct)
    {
        const string C = LegacyImportEntities.Clients, L = LegacyImportEntities.Consignees, K = LegacyImportEntities.Contacts;
        if (plan.Clients.Count == 0) return;

        // Lecturas de idempotencia (una consulta por tipo): clientes, su qb_code, sus consignatarios y sus contactos.
        var allClients = s.ReadDb
            ? await db.Clients.AsNoTracking().Select(c => new ExistingClient(c.ClientId, c.PublicId, c.Name, c.LegalName, c.CreditLimit,
                c.PaymentTerm != null ? c.PaymentTerm.InternalCode : null)).ToListAsync(ct)
            : new List<ExistingClient>();
        // El catálogo es global: los términos de pago mapeados deben existir aunque la compañía sea nueva (si no, la carga real los rechazaría).
        if (s.CatalogReadable)
            foreach (var term in plan.Clients.Select(c => c.PaymentTerm).Where(t => t is not null).Distinct(StringComparer.OrdinalIgnoreCase))
                if (await lookups.TryGetIdAsync(LookupDomains.PaymentTerm, term!, ct) is null)
                    report.Reject(C, term!, LegacyImportPlanner.PaymentTermMissing(term!), severe: true);
        var byQbCode = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (s.ReadDb && s.QbFieldId is int qbId)
            foreach (var v in await db.CustomFieldValues.AsNoTracking().Where(v => v.CustomFieldDefinitionId == qbId && v.ValueText != null)
                         .Select(v => new { v.EntityId, v.ValueText }).ToListAsync(ct))
                byQbCode.TryAdd(v.ValueText!.Trim(), v.EntityId);
        var withQbCode = byQbCode.Values.ToHashSet();
        var byName = allClients.GroupBy(c => c.Name.Trim(), StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var clientById = allClients.ToDictionary(c => c.ClientId);
        var existingLocations = s.ReadDb
            ? (await db.Locations.AsNoTracking().Where(l => l.ClientId != null && l.IsActive)
                .Select(l => new ExistingLocation(l.ClientId!.Value, l.LocationType!.InternalCode, l.PublicId, l.Line1, l.Line2, l.City, l.State, l.PostalCode, l.DeliveryNotes)).ToListAsync(ct))
                .GroupBy(l => (l.ClientId, l.Type)).ToDictionary(g => g.Key, g => g.First())
            : new Dictionary<(int, string), ExistingLocation>();
        var existingContacts = s.ReadDb
            ? (await db.ContactPoints.AsNoTracking().Where(c => c.IsActive && c.OwnerEntity!.InternalCode == EntityTypes.Client)
                .Select(c => new { c.OwnerId, c.Value, Type = c.ContactType!.InternalCode }).ToListAsync(ct))
                .Select(c => (c.OwnerId, c.Type, c.Value.Trim().ToLowerInvariant())).ToHashSet()
            : new HashSet<(int, string, string)>();

        var done = 0;
        foreach (var pc in plan.Clients)
        {
            // Existente: por qb_code; si no, por nombre entre los clientes que aún no tienen qb_code (o todos si no hay campo).
            int? clientId = null;
            Guid publicId = Guid.Empty;
            var isNew = false;
            if (byQbCode.TryGetValue(pc.QbCode, out var foundId) && clientById.TryGetValue(foundId, out var f1))
                (clientId, publicId) = (f1.ClientId, f1.PublicId);
            else if (byName.TryGetValue(pc.Name, out var f2) && (s.QbFieldId is null || !withQbCode.Contains(f2.ClientId)))
                (clientId, publicId) = (f2.ClientId, f2.PublicId);

            if (clientId is not null)
            {
                if (s.Update) await UpdateClientAsync(pc, clientById[clientId.Value], s, report, ct); else report.CountExisting(C);
            }
            else if (s.DryRun) { report.CountCreated(C); isNew = true; }
            else
            {
                var dto = await TryAsync(report, C, pc.Name, () => clients.CreateAsync(new ClientCreateRequest(
                    Code: null, Name: pc.Name, LegalName: pc.LegalName, TaxId: null, PaymentTerm: pc.PaymentTerm,
                    Currency: cfg.Clients.DefaultCurrency, CreditLimit: pc.CreditLimit, Contract: null), ct));
                if (dto is null) continue;
                report.CountCreated(C);
                (clientId, publicId, isNew) = (dto.Id, dto.PublicId, true);
            }

            // Campos personalizados del cliente (qb_code y representante).
            if (!s.DryRun && clientId is int id)
            {
                var values = new Dictionary<string, object?>();
                if (s.QbFieldUsable && !withQbCode.Contains(id)) values[LegacyImportPlanner.QbCodeFieldKey] = pc.QbCode;
                if (s.RepFieldUsable && (isNew || s.Update) && pc.Rep is not null) values[LegacyImportPlanner.RepFieldKey] = pc.Rep;
                if (values.Count > 0)
                    await TryAsync(report, LegacyImportEntities.CustomFields, pc.Name, () => customFields.SetValuesAsync(EntityTypes.Client, id, values, ct));
            }

            // Consignatarios: uno por tipo y cliente.
            foreach (var a in pc.Addresses)
            {
                if (clientId is int cid && existingLocations.TryGetValue((cid, a.LocationType), out var currentLoc))
                {
                    if (s.Update) await UpdateLocationAsync(pc, a, currentLoc, s, report, ct); else report.CountExisting(L);
                    continue;
                }
                if (s.DryRun) { report.CountCreated(L); continue; }
                var addr = a.Address;
                var dto = await TryAsync(report, L, $"{pc.Name} ({a.LocationType})", () => locations.CreateAsync(new LocationUpsertRequest(
                    publicId, null, pc.Name, a.LocationType, addr.Line1 ?? pc.Name, addr.Line2, addr.City, addr.State, addr.PostalCode, "PR",
                    DeliveryNotes: addr.Phone), ct));
                if (dto is null) continue;
                report.CountCreated(L);
                existingLocations[(clientId!.Value, a.LocationType)] = new ExistingLocation(clientId.Value, a.LocationType, dto.PublicId,
                    addr.Line1 ?? pc.Name, addr.Line2, addr.City, addr.State, addr.PostalCode, addr.Phone);
            }

            // Contactos: correos y teléfonos sin duplicar por valor; el primero de cada tipo es el principal si no había otro.
            foreach (var (type, list) in new[] { ("EMAIL", pc.Emails), ("PHONE", pc.Phones) })
            {
                var hasOfType = clientId is int cid0 && existingContacts.Any(c => c.Item1 == cid0 && c.Item2 == type);
                for (var i = 0; i < list.Count; i++)
                {
                    var value = list[i];
                    if (clientId is int cid && existingContacts.Contains((cid, type, value.Trim().ToLowerInvariant()))) { report.CountExisting(K); continue; }
                    if (s.DryRun) { report.CountCreated(K); continue; }
                    var primary = i == 0 && !hasOfType;
                    var dto = await TryAsync(report, K, $"{pc.Name}: {value}", () => contacts.AddAsync(EntityTypes.Client, clientId!.Value,
                        new ContactPointUpsertRequest(type, value, null, null, primary), ct, enforceOwnerWrite: false));
                    if (dto is null) continue;
                    report.CountCreated(K);
                    existingContacts.Add((clientId!.Value, type, value.Trim().ToLowerInvariant()));
                }
            }
            if (!s.DryRun && ++done % 100 == 0) logger.LogInformation("Clientes procesados: {Count} de {Total}.", done, plan.Clients.Count);
        }
    }

    private sealed record ExistingClient(int ClientId, Guid PublicId, string Name, string? LegalName, decimal? CreditLimit, string? PaymentTerm);
    private sealed record ExistingLocation(int ClientId, string Type, Guid PublicId, string Line1, string? Line2, string City, string? State, string? PostalCode, string? DeliveryNotes);

    /// <summary>Modo --update: nombre legal, término de pago y límite de crédito desde QuickBooks (el nombre y el código del cliente no cambian).</summary>
    private async Task UpdateClientAsync(PlannedClient pc, ExistingClient found, RunState s, LegacyImportReport report, CancellationToken ct)
    {
        const string C = LegacyImportEntities.Clients;
        var changes = new List<(string Field, string? From, string? To)>();
        if (Changed(found.LegalName, pc.LegalName)) changes.Add(("Nombre legal", found.LegalName, pc.LegalName));
        if (pc.PaymentTerm is not null && !string.Equals(found.PaymentTerm, pc.PaymentTerm, StringComparison.OrdinalIgnoreCase)) changes.Add(("Término de pago", found.PaymentTerm, pc.PaymentTerm));
        if (Changed(found.CreditLimit, pc.CreditLimit)) changes.Add(("Límite de crédito", Money(found.CreditLimit), Money(pc.CreditLimit)));
        if (changes.Count == 0) { report.CountExisting(C); return; }
        if (!s.DryRun)
        {
            var dto = await TryAsync(report, C, pc.Name, () => clients.UpdateProfileAsync(found.PublicId, new ClientProfileUpdateRequest(
                LegalName: changes.Any(c => c.Field == "Nombre legal") ? pc.LegalName : null, TaxId: null,
                CreditLimit: changes.Any(c => c.Field == "Límite de crédito") ? pc.CreditLimit : null,
                PaymentTerm: changes.Any(c => c.Field == "Término de pago") ? pc.PaymentTerm : null,
                Currency: null, DefaultPickupLocationPublicId: null, ClearDefaultPickup: false, RowVersion: null), ct));
            if (dto is null) return;
        }
        report.CountUpdated(C);
        foreach (var (field, from, to) in changes) report.AddUpdate(C, pc.Name, field, from, to);
    }

    /// <summary>Modo --update: la dirección del consignatario existente (mismo cliente y tipo) se actualiza con la de QuickBooks; los vacíos no borran.</summary>
    private async Task UpdateLocationAsync(PlannedClient pc, PlannedAddress a, ExistingLocation found, RunState s, LegacyImportReport report, CancellationToken ct)
    {
        const string L = LegacyImportEntities.Consignees;
        var addr = a.Address;
        var key = $"{pc.Name} ({a.LocationType})";
        var changes = new List<(string Field, string? From, string? To)>();
        var line1 = addr.Line1 ?? pc.Name;
        if (Changed(found.Line1, line1)) changes.Add(("Línea 1", found.Line1, line1));
        if (Changed(found.Line2, addr.Line2)) changes.Add(("Línea 2", found.Line2, addr.Line2));
        if (Changed(found.City, addr.City)) changes.Add(("Ciudad", found.City, addr.City));
        if (Changed(found.State, addr.State)) changes.Add(("Estado", found.State, addr.State));
        if (Changed(found.PostalCode, addr.PostalCode)) changes.Add(("Código postal", found.PostalCode, addr.PostalCode));
        if (Changed(found.DeliveryNotes, addr.Phone)) changes.Add(("Teléfono (notas de entrega)", found.DeliveryNotes, addr.Phone));
        if (changes.Count == 0) { report.CountExisting(L); return; }
        if (!s.DryRun)
        {
            var dto = await TryAsync(report, L, key, () => locations.UpdateAsync(found.PublicId, new LocationPatchRequest(
                Line1: changes.Any(c => c.Field == "Línea 1") ? line1 : null,
                Line2: changes.Any(c => c.Field == "Línea 2") ? addr.Line2 : null,
                City: changes.Any(c => c.Field == "Ciudad") ? addr.City : null,
                State: changes.Any(c => c.Field == "Estado") ? addr.State : null,
                PostalCode: changes.Any(c => c.Field == "Código postal") ? addr.PostalCode : null,
                DeliveryNotes: changes.Any(c => c.Field.StartsWith("Teléfono", StringComparison.Ordinal)) ? addr.Phone : null), ct));
            if (dto is null) return;
        }
        report.CountUpdated(L);
        foreach (var (field, from, to) in changes) report.AddUpdate(L, key, field, from, to);
    }

    // ================================================================ saldo inicial

    private async Task ImportOpeningBalancesAsync(LegacyImportConfig cfg, LegacyImportPlan plan, RunState s, LegacyImportReport report, CancellationToken ct)
    {
        const string E = LegacyImportEntities.OpeningBalance;
        if (plan.Balances.Count == 0 || plan.Warehouse is null) return;
        var reason = string.IsNullOrWhiteSpace(cfg.OpeningBalances.Reason) ? AdjustmentReasons.OpeningBalance : cfg.OpeningBalances.Reason.Trim().ToUpperInvariant();
        var warehouseCode = plan.Warehouse.Code;
        // --update nunca toca el saldo inicial de una compañía que YA existía: el inventario ya lo mueve Teikem, no QuickBooks.
        // Pero si esta misma corrida acaba de crear la compañía, no hay nada que "no tocar" — es la primera carga y necesita su
        // saldo inicial igual que sin --update (D51: una carga real con --update en la primera vez dejó 0 unidades de inventario).
        if (s.Update && s.CompanyAlreadyExisted)
        {
            report.AddInfo("Saldo inicial", LegacyImportPlanner.OpeningBalanceNotInUpdate);
            report.CountSkipped(E, plan.Balances.Count);
            return;
        }

        // El catálogo es global: se verifica aunque la compañía sea nueva (dry-run), para anticipar el rechazo de la carga real.
        if (s.CatalogReadable)
        {
            var reasonId = await lookups.TryGetIdAsync(LookupDomains.AdjustmentReason, reason, ct);
            if (reasonId is null) { report.Reject(E, reason, LegacyImportPlanner.ReasonMissing(reason), severe: true); return; }
            if (s.ReadDb && s.Warehouse is { } wh && await db.InventoryTransactions.AsNoTracking().AnyAsync(t => t.ReasonLookupId == reasonId && t.ToWarehouseId == wh.Id, ct))
            {
                report.Warn(E, warehouseCode, LegacyImportPlanner.OpeningBalanceAlreadyLoaded(warehouseCode));
                report.CountExisting(E, plan.Balances.Count);
                return;
            }
        }

        if (s.DryRun)
        {
            foreach (var b in plan.Balances)
            {
                var productOk = s.ProductIds.ContainsKey(b.Key) || s.ProductsWouldExist.Contains(b.Key);
                var binOk = s.BinIds.ContainsKey(b.BinCode) || s.BinsWouldExist.Contains(b.BinCode);
                if (!productOk) { report.Reject(E, b.Sku, LegacyImportPlanner.SkuMissingInTeikem(b.Sku)); continue; }
                if (!binOk) { report.Reject(E, $"{b.Sku} @ {b.BinCode}", LegacyImportPlanner.BinMissing(b.BinCode, b.Sku)); continue; }
                report.CountCreated(E);
                report.AddOpeningBalance(b.Sku, b.BinCode, b.Quantity);
            }
            return;
        }

        if (s.Warehouse is not { } target) { report.Reject(E, warehouseCode, LegacyImportPlanner.WarehouseRequired); return; }
        var postings = new List<(PlannedBalance Balance, InventoryPosting Posting)>();
        foreach (var b in plan.Balances)
        {
            if (!s.ProductIds.TryGetValue(b.Key, out var product)) { report.Reject(E, b.Sku, LegacyImportPlanner.SkuMissingInTeikem(b.Sku)); continue; }
            if (!s.BinIds.TryGetValue(b.BinCode, out var binId)) { report.Reject(E, $"{b.Sku} @ {b.BinCode}", LegacyImportPlanner.BinMissing(b.BinCode, b.Sku)); continue; }
            postings.Add((b, new InventoryPosting(InventoryTxnTypes.Adjustment, product.Id, b.Quantity, ToWarehouseId: target.Id, ToBinId: binId,
                ReasonCode: reason, Notes: cfg.OpeningBalances.Notes)));
        }

        // Lotes de 200 en una transacción cada uno; si un lote falla, se reintenta fila por fila para aislar el rechazo.
        foreach (var chunk in postings.Chunk(PostingBatchSize))
        {
            try
            {
                await db.RunInTransactionAsync(ct2 => ledger.PostAsync(chunk.Select(c => c.Posting).ToList(), ct2), ct);
                foreach (var (b, _) in chunk) { report.CountCreated(E); report.AddOpeningBalance(b.Sku, b.BinCode, b.Quantity); }
            }
            catch (TeikemException)
            {
                db.ChangeTracker.Clear();
                foreach (var (b, posting) in chunk)
                {
                    var ids = await TryAsync(report, E, $"{b.Sku} @ {b.BinCode}",
                        () => db.RunInTransactionAsync(ct2 => ledger.PostAsync(new[] { posting }, ct2), ct));
                    if (ids is null) continue;
                    report.CountCreated(E);
                    report.AddOpeningBalance(b.Sku, b.BinCode, b.Quantity);
                }
            }
            finally { db.ChangeTracker.Clear(); }
            logger.LogInformation("Saldo inicial: {Count} asientos contabilizados.", report.Total(E).Created);
        }
    }

    // ================================================================ cierre

    private async Task DeactivateInactiveProductsAsync(RunState s, LegacyImportReport report, CancellationToken ct)
    {
        foreach (var (sku, publicId) in s.ToDeactivate)
        {
            var dto = await TryAsync(report, LegacyImportEntities.Products, sku, () => products.DeactivateAsync(publicId, ct));
            if (dto is not null) report.Map("Producto inactivo en QuickBooks", sku, "creado y dado de baja");
        }
    }

    /// <summary>
    /// Cierre de la carga: conciliación Kárdex ↔ saldo de toda la compañía. Lote 14: pasa por InventoryReconciliationService
    /// (origen MIGRATION), así que cada descuadre además queda guardado como descuadre pendiente para resolverlo en la pantalla.
    /// </summary>
    private async Task ReconcileAsync(RunState s, LegacyImportReport report, CancellationToken ct)
    {
        ReconciliationRunDto result;
        try { result = await reconciliation.SweepAsync(ReconciliationTriggers.Migration, ct); }
        catch (TeikemException ex)
        {
            report.Reject(LegacyImportEntities.Reconciliation, "—", Describe(ex), severe: true);
            return;
        }
        finally { db.ChangeTracker.Clear(); }
        if (result.Mismatches.Count == 0) { report.AddInfo("Conciliación", "Sin diferencias (mismatches: [])."); return; }
        foreach (var r in result.Mismatches)
        {
            var where = r.WarehouseCode == TraceabilityService.ProductTotalMarker ? r.Sku : $"{r.Sku}, almacén {r.WarehouseCode}, posición {r.BinCode ?? "—"}";
            report.Reject(LegacyImportEntities.Reconciliation, r.Sku, LegacyImportPlanner.ReconciliationMismatch(where, r.LedgerQty, r.BalanceQty), severe: true);
        }
    }

    // ================================================================ utilidades

    /// <summary>
    /// Ejecuta una escritura de servicio; un rechazo (400/404/409/422 o error de base) se anota con su mensaje exacto y
    /// devuelve null. Siempre limpia el ChangeTracker: una entidad que falló no debe reintentarse en el SaveChanges siguiente.
    /// </summary>
    private async Task<T?> TryAsync<T>(LegacyImportReport report, string entity, string key, Func<Task<T>> action) where T : class
    {
        try { return await action(); }
        catch (TeikemException ex) { report.Reject(entity, key, Describe(ex)); return null; }
        catch (DbUpdateException ex) { report.Reject(entity, key, $"Error de base de datos: {ex.GetBaseException().Message}"); return null; }
        finally { db.ChangeTracker.Clear(); }
    }

    private async Task TryAsync(LegacyImportReport report, string entity, string key, Func<Task> action)
        => await TryAsync<object>(report, entity, key, async () => { await action(); return new object(); });

    /// <summary>Mensaje exacto de la excepción; con errores por campo, sus mensajes unidos.</summary>
    public static string Describe(TeikemException ex)
        => ex.Errors is { Count: > 0 } errors && errors.Values.SelectMany(v => v).ToList() is { Count: > 0 } messages
            ? string.Join(" ", messages.Distinct())
            : ex.Message;

    private static IDictionary<string, string> Labels(string es, string en) => new Dictionary<string, string> { ["es"] = es, ["en"] = en };
}
