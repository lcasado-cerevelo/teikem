using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Constants;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Services;
using Teikem.Infrastructure.Wms;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 12 (Lote 2 del plan de cambios) — marca y modelo del producto y filtros nuevos de las listas:
/// - Product.Brand / Product.Model: texto libre recortado (vacío = NULL, más de 100 → 400), en alta, PATCH (null = sin
///   cambio, "" = quitar), lista y ficha; columnas mapeadas y declaradas (guardadas) en el SQL de estructura.
/// - GET /products: warehousePublicIds (combinados con el singular), productPublicIds, name, brands, serialOnly y
///   serialMissing (KPI 'series por capturar'); marcas distintas para el filtro.
/// - GET /inventory/transactions: brands y name. GET /purchase-orders: supplierIds y warehousePublicIds.
/// InMemory con WmsFixture; la traducción a SQL Server de los predicados nuevos se verifica con ToQueryString.
/// </summary>
public sealed class ProductBrandFilterTests
{
    private static Task<WmsFixture> CreateAsync() => WmsFixture.CreateAsync(s =>
    {
        s.AddSingleton<ProductService>();
        s.AddSingleton<InventoryReadService>();
        s.AddSingleton<PurchaseOrderService>();
    });

    private static async Task SetAsync(WmsFixture f, Product p, string? brand, string? model = null)
    {
        var tracked = await f.Db.Products.AsTracking().SingleAsync(x => x.ProductId == p.ProductId);
        tracked.Brand = brand;
        tracked.Model = model;
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
    }

    private static InventoryPosting In(int productId, WarehouseBin bin, decimal qty)
        => new(InventoryTxnTypes.Receipt, productId, qty, ToWarehouseId: bin.WarehouseId, ToBinId: bin.WarehouseBinId);

    // ================================================================ reglas puras

    [Fact]
    public void Brand_and_model_are_trimmed_optional_and_capped()
    {
        Assert.Equal(("Acme", (string?)null), ProductRules.NormalizeBrand("  Acme "));
        Assert.Equal(((string?)null, (string?)null), ProductRules.NormalizeBrand("   "));
        Assert.Equal(((string?)null, (string?)null), ProductRules.NormalizeModel(null));
        Assert.Equal("La marca no puede exceder 100 caracteres.", ProductRules.NormalizeBrand(new string('a', 101)).Error);
        Assert.Equal("El modelo no puede exceder 100 caracteres.", ProductRules.NormalizeModel(new string('m', 101)).Error);
        Assert.Equal(new string('m', 100), ProductRules.NormalizeModel(new string('m', 100)).Model);
    }

    [Fact]
    public void Text_filter_and_distinct_brands_ignore_case()
    {
        Assert.Null(ProductRules.NormalizeTextFilter(null));
        Assert.Null(ProductRules.NormalizeTextFilter(new[] { " ", "" }));
        Assert.Equal(new[] { "acme", "zeta" }, ProductRules.NormalizeTextFilter(new[] { " ACME", "acme", "Zeta", null }));
        Assert.Equal(new[] { "ACME", "beta", "Zeta" }, ProductRules.DistinctBrands(new[] { "Zeta", "acme", "ACME", " beta ", null, "" }));
    }

    [Fact]
    public void Serial_missing_rule()
    {
        Assert.True(ProductRules.IsSerialMissing(true, true, 3m, 1));
        Assert.False(ProductRules.IsSerialMissing(true, true, 2m, 2));
        Assert.False(ProductRules.IsSerialMissing(false, true, 3m, 0));
        Assert.False(ProductRules.IsSerialMissing(true, false, 3m, 0));
        Assert.False(ProductRules.IsSerialMissing(true, true, 0m, 0));
    }

    // ================================================================ alta, edición, lista y ficha

    [Fact]
    public async Task Brand_and_model_are_saved_patched_and_cleared()
    {
        await using var f = await CreateAsync();
        var products = f.Get<ProductService>();

        var created = await products.CreateAsync(new ProductCreateRequest("TV-1", "Televisor", Brand: "  Sony ", Model: " "), default);
        Assert.Equal(("Sony", (string?)null), (created.Product.Brand, created.Product.Model));

        var tooLong = await Assert.ThrowsAsync<ValidationException>(() =>
            products.CreateAsync(new ProductCreateRequest("TV-2", "Otro", Brand: new string('b', 101), Model: new string('m', 101)), default));
        Assert.Equal(ProductRules.BrandTooLong, Assert.Single(tooLong.Errors!["brand"]));
        Assert.Equal(ProductRules.ModelTooLong, Assert.Single(tooLong.Errors!["model"]));

        // PATCH: null = sin cambio.
        var renamed = await products.UpdateAsync(created.Product.PublicId, new ProductPatchRequest(Name: "Televisor 55", Model: "KD-55"), default);
        Assert.Equal(("Sony", "KD-55"), (renamed.Product.Brand, renamed.Product.Model));

        // "" = quitar; el resto queda.
        var cleared = await products.UpdateAsync(created.Product.PublicId, new ProductPatchRequest(Brand: ""), default);
        Assert.Equal(((string?)null, "KD-55"), (cleared.Product.Brand, cleared.Product.Model));

        var patchTooLong = await Assert.ThrowsAsync<ValidationException>(() =>
            products.UpdateAsync(created.Product.PublicId, new ProductPatchRequest(Model: new string('m', 101)), default));
        Assert.Equal(ProductRules.ModelTooLong, Assert.Single(patchTooLong.Errors!["model"]));

        var listed = Assert.Single((await products.ListAsync(new ProductListQuery(), InventoryScope.Any, default)).Items);
        Assert.Equal(((string?)null, "KD-55"), (listed.Brand, listed.Model));
        var stored = await f.Db.Products.AsNoTracking().SingleAsync(p => p.PublicId == created.Product.PublicId);
        Assert.Equal(((string?)null, "KD-55"), (stored.Brand, stored.Model));
    }

    // ================================================================ filtros de GET /products

    [Fact]
    public async Task List_filters_by_name_brands_skus_and_several_warehouses()
    {
        await using var f = await CreateAsync();
        var w1 = await f.AddWarehouseAsync("W1");
        var w2 = await f.AddWarehouseAsync("W2");
        var w3 = await f.AddWarehouseAsync("W3");
        var b1 = await f.AddBinAsync(await f.AddZoneAsync(w1, "PCK", ZoneTypes.Picking), "P-01");
        var b2 = await f.AddBinAsync(await f.AddZoneAsync(w2, "PCK", ZoneTypes.Picking), "P-02");
        var b3 = await f.AddBinAsync(await f.AddZoneAsync(w3, "PCK", ZoneTypes.Picking), "P-03");
        var tv = await f.AddProductAsync("TV");
        var radio = await f.AddProductAsync("RADIO");
        var cable = await f.AddProductAsync("CABLE");
        await SetAsync(f, tv, "Sony", "KD-55");
        await SetAsync(f, radio, "SONY");
        await SetAsync(f, cable, "Belkin");
        await f.PostAsync(In(tv.ProductId, b1, 2m), In(tv.ProductId, b2, 3m), In(tv.ProductId, b3, 7m));

        var products = f.Get<ProductService>();
        async Task<string[]> Skus(ProductListQuery q) => (await products.ListAsync(q, InventoryScope.Any, default)).Items.Select(i => i.Sku).ToArray();

        // Nombre: contiene, sin distinguir mayúsculas ('Producto TV', 'Producto RADIO', 'Producto CABLE').
        Assert.Equal(new[] { "RADIO" }, await Skus(new ProductListQuery(Name: "  producto rad ")));
        // Marcas: igualdad sin distinguir mayúsculas; varias se combinan con O.
        Assert.Equal(new[] { "RADIO", "TV" }, await Skus(new ProductListQuery(Brands: new[] { "sony" })));
        Assert.Equal(new[] { "CABLE", "RADIO", "TV" }, await Skus(new ProductListQuery(Brands: new[] { "SONY", "belkin", " " })));
        Assert.Empty(await Skus(new ProductListQuery(Brands: new[] { "Son" })));
        // Selección de SKU por PublicId, combinable con los demás filtros.
        Assert.Equal(new[] { "CABLE", "TV" }, await Skus(new ProductListQuery(ProductPublicIds: new[] { tv.PublicId, cable.PublicId })));
        Assert.Equal(new[] { "TV" }, await Skus(new ProductListQuery(ProductPublicIds: new[] { tv.PublicId, cable.PublicId }, Brands: new[] { "Sony" })));

        // Almacenes: el singular y la lista se combinan; acotan los totales (la lista de productos no cambia).
        var some = await products.ListAsync(new ProductListQuery(WarehousePublicId: w1.PublicId, WarehousePublicIds: new[] { w2.PublicId, w1.PublicId }),
            InventoryScope.Any, default);
        Assert.Equal(3, some.Total);
        Assert.Equal(5m, some.Items.Single(i => i.Sku == "TV").QtyOnHand);
        var all = await products.ListAsync(new ProductListQuery(), InventoryScope.Any, default);
        Assert.Equal(12m, all.Items.Single(i => i.Sku == "TV").QtyOnHand);
        var onlyW3 = await products.ListAsync(new ProductListQuery(WarehousePublicIds: new[] { w3.PublicId }, OnlyAvailable: true), InventoryScope.Any, default);
        Assert.Equal(7m, Assert.Single(onlyW3.Items).QtyAvailable);

        // Un almacén que no es del tenant → 404, como el singular.
        var foreign = await f.AddWarehouseAsync("WX", tenantId: WmsFixture.OtherTenantId);
        var ex = await Assert.ThrowsAsync<NotFoundException>(() =>
            products.ListAsync(new ProductListQuery(WarehousePublicIds: new[] { w1.PublicId, foreign.PublicId }), InventoryScope.Any, default));
        Assert.Equal("Almacén no encontrado.", ex.Message);
    }

    [Fact]
    public async Task Serial_only_and_serial_missing_filters()
    {
        await using var f = await CreateAsync();
        var w1 = await f.AddWarehouseAsync("W1");
        var w2 = await f.AddWarehouseAsync("W2");
        var b1 = await f.AddBinAsync(await f.AddZoneAsync(w1, "PCK", ZoneTypes.Picking), "P-01");
        var b2 = await f.AddBinAsync(await f.AddZoneAsync(w2, "PCK", ZoneTypes.Picking), "P-02");
        var missing = await f.AddProductAsync("S-MISS", TrackingTypes.Serial);      // 3 en mano, 1 serie AVAILABLE + 1 SHIPPED
        var complete = await f.AddProductAsync("S-FULL", TrackingTypes.Serial);     // 2 en mano, 1 AVAILABLE + 1 RESERVED
        var empty = await f.AddProductAsync("S-EMPTY", TrackingTypes.Serial);       // sin existencia ni series
        var off = await f.AddProductAsync("S-OFF", TrackingTypes.Serial, isActive: false);   // inactivo con faltante
        var split = await f.AddProductAsync("S-SPLIT", TrackingTypes.Serial);       // W1: 1 en mano sin serie; W2: 1 en mano con serie
        var legacy = await f.AddProductAsync("N-SER");                              // NONE con una serie registrada
        await f.AddProductAsync("N-PLAIN");                                         // NONE sin series

        var available = f.StatusId(StatusDomains.SerialStatus, SerialStatuses.Available);
        var reserved = f.StatusId(StatusDomains.SerialStatus, SerialStatuses.Reserved);
        var shipped = f.StatusId(StatusDomains.SerialStatus, SerialStatuses.Shipped);
        void Stock(Product p, WarehouseBin bin, decimal qty, decimal qtyReserved = 0m)
            => f.Db.StockBalances.Add(new StockBalance
            {
                TenantId = WmsFixture.TenantId, ProductId = p.ProductId, WarehouseId = bin.WarehouseId, WarehouseBinId = bin.WarehouseBinId,
                QtyOnHand = qty, QtyReserved = qtyReserved, UpdatedAtUtc = DateTime.UtcNow,
            });
        void Serial(Product p, string number, int status, WarehouseBin? bin)
            => f.Db.InventorySerials.Add(new InventorySerial
            {
                ProductId = p.ProductId, SerialNumber = number, StatusCodeId = status, CurrentWarehouseId = bin?.WarehouseId, CurrentBinId = bin?.WarehouseBinId,
            });
        Stock(missing, b1, 3m);
        Serial(missing, "M-1", available, b1);
        Serial(missing, "M-2", shipped, null);
        Stock(complete, b1, 2m, 1m);
        Serial(complete, "F-1", available, b1);
        Serial(complete, "F-2", reserved, b1);
        Stock(off, b1, 1m);
        Stock(split, b1, 1m);
        Stock(split, b2, 1m);
        Serial(split, "X-1", available, b2);
        Serial(legacy, "L-1", available, b1);
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();

        var products = f.Get<ProductService>();
        async Task<string[]> Skus(ProductListQuery q) => (await products.ListAsync(q, InventoryScope.Any, default)).Items.Select(i => i.Sku).ToArray();

        // serialOnly: rastreo SERIAL (activo o no) o con series registradas.
        Assert.Equal(new[] { "N-SER", "S-EMPTY", "S-FULL", "S-MISS", "S-OFF", "S-SPLIT" }, await Skus(new ProductListQuery(SerialOnly: true)));
        Assert.Equal(new[] { "N-SER", "S-EMPTY", "S-FULL", "S-MISS", "S-SPLIT" }, await Skus(new ProductListQuery(SerialOnly: true, ActiveOnly: true)));

        // serialMissing: activos SERIAL con en mano > series en stock (AVAILABLE/RESERVED); SHIPPED no cuenta.
        Assert.Equal(new[] { "S-MISS", "S-SPLIT" }, await Skus(new ProductListQuery(SerialMissing: true)));
        var kpi = await products.ListAsync(new ProductListQuery(SerialMissing: true, Take: 1), InventoryScope.Any, default);
        Assert.Equal(2, kpi.Total);
        // Por almacén: en mano y series de esos almacenes (S-SPLIT falta en W1, está completo en W2).
        Assert.Equal(new[] { "S-MISS", "S-SPLIT" }, await Skus(new ProductListQuery(SerialMissing: true, WarehousePublicIds: new[] { w1.PublicId })));
        Assert.Empty(await Skus(new ProductListQuery(SerialMissing: true, WarehousePublicId: w2.PublicId)));
    }

    [Fact]
    public async Task Brands_are_distinct_sorted_and_searchable_within_the_tenant()
    {
        await using var f = await CreateAsync();
        await SetAsync(f, await f.AddProductAsync("A"), "Sony");
        await SetAsync(f, await f.AddProductAsync("B"), "SONY");
        await SetAsync(f, await f.AddProductAsync("C"), "Belkin");
        await SetAsync(f, await f.AddProductAsync("D", isActive: false), "Apple");
        await f.AddProductAsync("E");
        var foreign = await f.AddProductAsync("X", tenantId: WmsFixture.OtherTenantId);
        using (f.AsTenant(WmsFixture.OtherTenantId))
            await SetAsync(f, foreign, "Otra");

        var products = f.Get<ProductService>();
        Assert.Equal(new[] { "Apple", "Belkin", "SONY" }, await products.ListBrandsAsync(null, InventoryScope.Any, default));
        Assert.Equal(new[] { "SONY" }, await products.ListBrandsAsync(" so ", InventoryScope.Any, default));
        Assert.Empty(await products.ListBrandsAsync("otra", InventoryScope.Any, default));
    }

    // ================================================================ Kárdex y órdenes de compra

    [Fact]
    public async Task Kardex_filters_by_brand_and_product_name()
    {
        await using var f = await CreateAsync();
        var w = await f.AddWarehouseAsync("W1");
        var bin = await f.AddBinAsync(await f.AddZoneAsync(w, "PCK", ZoneTypes.Picking), "P-01");
        var tv = await f.AddProductAsync("TV");
        var cable = await f.AddProductAsync("CABLE");
        await SetAsync(f, tv, "Sony");
        await SetAsync(f, cable, "Belkin");
        await f.PostAsync(In(tv.ProductId, bin, 1m), In(cable.ProductId, bin, 2m), In(cable.ProductId, bin, 3m));

        var reads = f.Get<InventoryReadService>();
        var sony = await reads.KardexAsync(new KardexQuery(Brands: new[] { "SONY" }), InventoryScope.Any, default);
        Assert.Equal(new[] { "TV" }, sony.Items.Select(i => i.Sku).ToArray());
        var byName = await reads.KardexAsync(new KardexQuery(Name: "producto cab"), InventoryScope.Any, default);
        Assert.Equal(2, byName.Total);
        Assert.All(byName.Items, i => Assert.Equal("CABLE", i.Sku));
        var both = await reads.KardexAsync(new KardexQuery(Types: new[] { InventoryTxnTypes.Receipt }, Brands: new[] { "sony", "belkin" }, Name: "tv"),
            InventoryScope.Any, default);
        Assert.Equal(new[] { "TV" }, both.Items.Select(i => i.Sku).ToArray());
    }

    [Fact]
    public async Task Purchase_orders_filter_by_several_suppliers_and_warehouses()
    {
        await using var f = await CreateAsync();
        var w1 = await f.AddWarehouseAsync("W1");
        var w2 = await f.AddWarehouseAsync("W2");
        var p = await f.AddProductAsync("PA");
        var suppliers = new[] { "Uno", "Dos", "Tres" }.Select(n => new Supplier { TenantId = WmsFixture.TenantId, Name = n, IsActive = true }).ToList();
        f.Db.Set<Supplier>().AddRange(suppliers);
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();

        var orders = f.Get<PurchaseOrderService>();
        async Task<PurchaseOrderDto> Po(Supplier s, Warehouse w)
            => await orders.CreateAsync(new PurchaseOrderCreateRequest(s.SupplierId, w.PublicId, Lines: new[] { new PurchaseOrderLineRequest(p.PublicId, 1m, 1m) }), default);
        var a = await Po(suppliers[0], w1);
        var b = await Po(suppliers[1], w2);
        var c = await Po(suppliers[2], w1);

        async Task<string[]> Numbers(PurchaseOrderQuery q) => (await orders.ListAsync(q, default)).Items.Select(i => i.Number).OrderBy(n => n).ToArray();
        Assert.Equal(new[] { a.Number, b.Number }.OrderBy(n => n).ToArray(), await Numbers(new PurchaseOrderQuery(SupplierIds: new[] { suppliers[0].SupplierId, suppliers[1].SupplierId })));
        // El singular se combina con la lista.
        Assert.Equal(new[] { a.Number, b.Number, c.Number }.OrderBy(n => n).ToArray(),
            await Numbers(new PurchaseOrderQuery(SupplierId: suppliers[2].SupplierId, SupplierIds: new[] { suppliers[0].SupplierId, suppliers[1].SupplierId })));
        Assert.Equal(new[] { a.Number, c.Number }.OrderBy(n => n).ToArray(), await Numbers(new PurchaseOrderQuery(WarehousePublicIds: new[] { w1.PublicId })));
        Assert.Equal(new[] { a.Number, b.Number, c.Number }.OrderBy(n => n).ToArray(),
            await Numbers(new PurchaseOrderQuery(WarehousePublicId: w2.PublicId, WarehousePublicIds: new[] { w1.PublicId })));
        Assert.Equal(new[] { b.Number }, await Numbers(new PurchaseOrderQuery(SupplierIds: new[] { suppliers[1].SupplierId, suppliers[2].SupplierId },
            WarehousePublicIds: new[] { w2.PublicId })));
    }

    [Fact]
    public async Task Purchase_order_requires_lines_with_positive_quantity()
    {
        // Ya lo exigía el dominio (Lote 6): sin líneas → 400 LinesRequired; cantidad 0 → 400 QtyRequired.
        await using var f = await CreateAsync();
        var w = await f.AddWarehouseAsync("W1");
        var p = await f.AddProductAsync("PA");
        var supplier = new Supplier { TenantId = WmsFixture.TenantId, Name = "Uno", IsActive = true };
        f.Db.Set<Supplier>().Add(supplier);
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        var orders = f.Get<PurchaseOrderService>();

        var noLines = await Assert.ThrowsAsync<ValidationException>(() =>
            orders.CreateAsync(new PurchaseOrderCreateRequest(supplier.SupplierId, w.PublicId, Lines: Array.Empty<PurchaseOrderLineRequest>()), default));
        Assert.Equal("La orden de compra debe tener al menos una línea.", Assert.Single(noLines.Errors!["lines"]));
        var zero = await Assert.ThrowsAsync<ValidationException>(() =>
            orders.CreateAsync(new PurchaseOrderCreateRequest(supplier.SupplierId, w.PublicId, Lines: new[] { new PurchaseOrderLineRequest(p.PublicId, 0m, 1m) }), default));
        Assert.Equal("La cantidad ordenada debe ser mayor que cero.", Assert.Single(zero.Errors!["lines[0]"]));
    }

    // ================================================================ modelo, SQL de estructura y traducción

    [Fact]
    public void Brand_and_model_are_mapped_and_declared_in_the_structure_script()
    {
        using var db = TenantIsolationModelTests.CreateSqlServerModelContext();
        var entity = db.Model.FindEntityType(typeof(Product))!;
        foreach (var name in new[] { nameof(Product.Brand), nameof(Product.Model) })
        {
            var prop = entity.FindProperty(name)!;
            Assert.Equal(100, prop.GetMaxLength());
            Assert.True(prop.IsNullable);
        }

        var sql = File.ReadAllText(Path.Combine(TripCatalogTests.RepoRoot(), "Diseño", "logistica-db-estructura.sql"));
        Assert.Contains("Brand        NVARCHAR(100) NULL,", sql);
        Assert.Contains("Model        NVARCHAR(100) NULL,", sql);
        // Aditivo e idempotente sobre una base existente: columnas solo si faltan.
        Assert.Contains("IF OBJECT_ID('dbo.Product') IS NULL", sql);
        Assert.Contains("IF COL_LENGTH('dbo.Product', 'Brand') IS NULL", sql);
        Assert.Contains("ALTER TABLE dbo.Product ADD Brand NVARCHAR(100) NULL;", sql);
        Assert.Contains("IF COL_LENGTH('dbo.Product', 'Model') IS NULL", sql);
        Assert.Contains("ALTER TABLE dbo.Product ADD Model NVARCHAR(100) NULL;", sql);
    }

    [Fact]
    public void New_product_predicates_translate_to_sql_server()
    {
        // Misma forma que ProductService.ListAsync (marcas, nombre, serialOnly y serialMissing con almacenes).
        using var db = TenantIsolationModelTests.CreateSqlServerModelContext();
        var brands = new List<string> { "sony" };
        var n = "tv";
        var sid = 3;
        var whIds = new List<int> { 1, 2 };
        var inStock = new List<int?> { 10, 11 };
        var serials = db.Set<InventorySerial>().AsNoTracking();
        var serialsInStock = serials.Where(s => inStock.Contains(s.StatusCodeId))
            .Where(s => s.CurrentWarehouseId != null && whIds.Contains(s.CurrentWarehouseId.Value));
        var onHand = db.Set<StockBalance>().AsNoTracking().Where(b => whIds.Contains(b.WarehouseId));
        var sql = db.Set<Product>().AsNoTracking()
            .Where(p => p.Brand != null && brands.Contains(p.Brand.ToLower()))
            .Where(p => p.Name.ToLower().Contains(n))
            .Where(p => p.TrackingTypeLookupId == sid || serials.Any(s => s.ProductId == p.ProductId))
            .Where(p => p.IsActive && p.TrackingTypeLookupId == sid
                        && (onHand.Where(b => b.ProductId == p.ProductId).Sum(b => (decimal?)b.QtyOnHand) ?? 0m)
                           > serialsInStock.Count(s => s.ProductId == p.ProductId))
            .OrderBy(p => p.Sku).Take(1)
            .ToQueryString();
        Assert.Contains("LOWER([p].[Brand])", sql);
        Assert.Contains("LOWER([p].[Name])", sql);
        Assert.Contains("[InventorySerial]", sql);
        Assert.Contains("COUNT(*)", sql);

        var brandSql = db.Set<Product>().AsNoTracking().Where(p => p.Brand != null && p.Brand != "" && p.Brand.ToLower().Contains(n))
            .Select(p => p.Brand).Distinct().ToQueryString();
        Assert.Contains("DISTINCT", brandSql);
    }
}
