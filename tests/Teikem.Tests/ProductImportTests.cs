using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Orders;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// 2026-10-21 — Importación de productos con plantilla (Kind PRODUCT, primera entidad del motor genérico): campos del catálogo, reglas de la plantilla, vista previa que no guarda,
/// confirmación que crea las filas válidas, y (2026-11-05, pedido de Luis) actualiza por SKU el producto que ya existe: celda vacía = sin cambio y sin valores por defecto de la plantilla.
/// </summary>
public sealed class ProductImportTests
{
    private static Task<WmsFixture> CreateAsync() => WmsFixture.CreateAsync(s =>
    {
        s.AddSingleton<ImportTemplateService>();
        s.AddSingleton<ProductService>();
        s.AddSingleton<EntityImportService>();
        s.AddSingleton<IImportEntityRows, ProductImportRows>();
    });

    private static Task<ImportTemplateDto> TemplateAsync(WmsFixture f, string name = "Productos", IList<ImportTemplateColumnDto>? columns = null, IDictionary<string, string>? defaults = null)
        => f.Get<ImportTemplateService>().CreateAsync(new ImportTemplateCreateRequest(name, columns ?? [new(1, "sku"), new(2, "name"), new(3, "salePrice"), new(4, "barcode")], defaults), ImportKinds.Product, default);

    private static byte[] Csv(params string[] lines) => Encoding.UTF8.GetBytes(string.Join("\n", lines));

    [Fact]
    public void The_field_catalog_marks_sku_required_and_sku_and_barcode_without_defaults()
    {
        var fields = ImportTemplateService.GetEntityFields(ImportKinds.Product);
        Assert.Contains(fields, f => f.Field == "sku" && f.Required && !f.DefaultAllowed);
        Assert.Contains(fields, f => f.Field == "barcode" && !f.DefaultAllowed);
        Assert.Contains(fields, f => f.Field == "baseUom" && f.DefaultAllowed);
    }

    [Fact]
    public async Task A_product_template_needs_sku_and_a_name_column_or_default_and_rejects_unknown_fields()
    {
        await using var f = await CreateAsync();
        var noSku = await Assert.ThrowsAsync<ValidationException>(() => TemplateAsync(f, "A", [new(1, "name")]));
        Assert.Equal(ProductImportMapping.SkuColumnMessage, noSku.Errors!["columns"][0]);
        var noName = await Assert.ThrowsAsync<ValidationException>(() => TemplateAsync(f, "B", [new(1, "sku")]));
        Assert.Equal(ProductImportMapping.NameColumnMessage, noName.Errors!["columns"][0]);
        await TemplateAsync(f, "C", [new(1, "sku")], new Dictionary<string, string> { ["name"] = "Sin nombre" });   // el nombre por defecto basta
        var unknown = await Assert.ThrowsAsync<ValidationException>(() => TemplateAsync(f, "D", [new(1, "sku"), new(2, "name"), new(3, "color")]));
        Assert.Equal(ProductImportMapping.UnknownFieldMessage("color"), unknown.Errors!["columns[2].field"][0]);
        var skuDefault = await Assert.ThrowsAsync<ValidationException>(() => TemplateAsync(f, "E", [new(1, "sku"), new(2, "name")], new Dictionary<string, string> { ["sku"] = "X" }));
        Assert.Equal(ProductImportMapping.DefaultNotAllowedMessage("sku"), skuDefault.Errors!["defaults.sku"][0]);
    }

    [Fact]
    public async Task The_other_routes_do_not_see_product_templates()
    {
        await using var f = await CreateAsync();
        var tpl = await TemplateAsync(f);
        var svc = f.Get<ImportTemplateService>();
        await Assert.ThrowsAsync<NotFoundException>(() => svc.GetAsync(tpl.PublicId, ImportKinds.Order, default));
        Assert.Empty(await svc.GetListAsync(ImportKinds.Consignee, null, null, false, default));
        Assert.Single(await svc.GetListAsync(ImportKinds.Product, null, null, false, default));
    }

    [Fact]
    public async Task Preview_does_not_save_and_commit_creates_only_the_valid_rows()
    {
        await using var f = await CreateAsync();
        var tpl = await TemplateAsync(f);
        var imports = f.Get<EntityImportService>();
        var file = Csv("sku,name,price,barcode", "A-1,Producto uno,10.50,111", "A-2,Producto dos,abc,222", ",Sin sku,1,333", "A-3,Producto tres,1,444");

        var preview = await imports.ImportAsync(ImportKinds.Product, tpl.PublicId, file, "p.csv", commit: false, default);
        Assert.False(preview.Committed);
        Assert.Equal((4, 2, 2, 0), (preview.Total, preview.Valid, preview.Rejected, preview.Created));
        Assert.Equal("ERROR", preview.Rows[1].Status);
        Assert.Equal(EntityImportService.NotANumberMessage, preview.Rows[1].Errors["salePrice"]);
        Assert.True(preview.Rows[2].Errors.ContainsKey("sku"));
        Assert.Empty(f.Db.Set<Product>().ToList());   // la vista previa no guarda nada

        var done = await imports.ImportAsync(ImportKinds.Product, tpl.PublicId, file, "p.csv", commit: true, default);
        Assert.Equal((2, 2), (done.Created, done.Rows.Count(r => r.Status == "CREATED")));
        Assert.Equal(new[] { "A-1", "A-3" }, f.Db.Set<Product>().Select(p => p.Sku).OrderBy(s => s).ToArray());
    }

    [Fact]
    public async Task Existing_sku_is_updated_new_sku_is_created_and_barcode_of_another_product_or_repeats_in_the_file_are_rejected()
    {
        await using var f = await CreateAsync();
        var tpl = await TemplateAsync(f);
        var imports = f.Get<EntityImportService>();
        await imports.ImportAsync(ImportKinds.Product, tpl.PublicId, Csv("sku,name,price,barcode", "B-1,Original,5,900"), "p.csv", commit: true, default);

        var file = Csv("sku,name,price,barcode", "b-1,Cambiado,9,901", "B-2,Otro,1,900", "B-3,Tres,1,902", "B-3,Tres otra vez,1,903");
        var preview = await imports.ImportAsync(ImportKinds.Product, tpl.PublicId, file, "p.csv", commit: false, default);
        Assert.Equal(("UPDATE", "ERROR", "OK", "ERROR"), (preview.Rows[0].Status, preview.Rows[1].Status, preview.Rows[2].Status, preview.Rows[3].Status));
        Assert.Equal("Original", f.Db.Set<Product>().Single(p => p.Sku == "B-1").Name);   // la vista previa no guarda nada

        var second = await imports.ImportAsync(ImportKinds.Product, tpl.PublicId, file, "p.csv", commit: true, default);
        Assert.Equal("UPDATED", second.Rows[0].Status);
        Assert.Equal(ProductRules.BarcodeTaken, second.Rows[1].Errors["barcode"]);   // el 900 lo tiene B-1 al revisar (aunque esta misma carga se lo cambie a 901): B-2 nuevo no lo puede usar
        Assert.Equal("CREATED", second.Rows[2].Status);
        Assert.Equal(ProductImportRows.RepeatedInFileMessage, second.Rows[3].Errors["sku"]);
        Assert.Equal((1, 1), (second.Created, second.Updated));
        var b1 = f.Db.Set<Product>().Single(p => p.Sku == "B-1");
        Assert.Equal(("Cambiado", "901", 9m), (b1.Name, b1.Barcode, b1.SalePrice));
    }

    [Fact]
    public async Task Update_keeps_what_the_empty_cells_leave_out_and_ignores_the_template_defaults()
    {
        await using var f = await CreateAsync();
        var tpl = await TemplateAsync(f, "Con defaults", [new(1, "sku"), new(2, "name"), new(3, "salePrice"), new(4, "barcode")], new Dictionary<string, string> { ["name"] = "Sin nombre", ["salePrice"] = "1" });
        var imports = f.Get<EntityImportService>();
        await imports.ImportAsync(ImportKinds.Product, tpl.PublicId, Csv("sku,name,price,barcode", "D-1,Nombre real,25,700"), "p.csv", commit: true, default);

        // nombre y precio vacíos: por defecto la plantilla pondría "Sin nombre" y 1, pero a un producto que ya existe no se los pone; el código de barras sí cambia
        var done = await imports.ImportAsync(ImportKinds.Product, tpl.PublicId, Csv("sku,name,price,barcode", "D-1,,,701"), "p.csv", commit: true, default);
        Assert.Equal("UPDATED", done.Rows[0].Status);
        var p = f.Db.Set<Product>().Single(x => x.Sku == "D-1");
        Assert.Equal(("Nombre real", 25m, "701"), (p.Name, p.SalePrice, p.Barcode));

        // un SKU nuevo con las mismas celdas vacías SÍ recibe los valores por defecto
        var created = await imports.ImportAsync(ImportKinds.Product, tpl.PublicId, Csv("sku,name,price,barcode", "D-2,,,"), "p.csv", commit: true, default);
        Assert.Equal("CREATED", created.Rows[0].Status);
        Assert.Equal(("Sin nombre", 1m), (f.Db.Set<Product>().Single(x => x.Sku == "D-2").Name, f.Db.Set<Product>().Single(x => x.Sku == "D-2").SalePrice));
    }

    [Fact]
    public async Task Updating_a_product_keeps_its_own_barcode_without_conflict()
    {
        await using var f = await CreateAsync();
        var tpl = await TemplateAsync(f);
        var imports = f.Get<EntityImportService>();
        await imports.ImportAsync(ImportKinds.Product, tpl.PublicId, Csv("sku,name,price,barcode", "E-1,Uno,5,800"), "p.csv", commit: true, default);

        var done = await imports.ImportAsync(ImportKinds.Product, tpl.PublicId, Csv("sku,name,price,barcode", "E-1,Uno,7,800"), "p.csv", commit: true, default);
        Assert.Equal("UPDATED", done.Rows[0].Status);
        Assert.Equal(7m, f.Db.Set<Product>().Single(x => x.Sku == "E-1").SalePrice);
    }

    [Fact]
    public async Task Category_is_matched_by_path_or_unique_name_and_updates_keep_it_when_the_cell_is_empty()
    {
        await using var f = await CreateAsync();
        var tenant = f.Tenant.TenantId!.Value;
        f.Db.Set<ProductCategory>().AddRange(
            new ProductCategory { ProductCategoryId = 901, TenantId = tenant, Name = "Bebidas" },
            new ProductCategory { ProductCategoryId = 902, TenantId = tenant, Name = "Jugos", ParentId = 901 },
            new ProductCategory { ProductCategoryId = 903, TenantId = tenant, Name = "Jugos", ParentId = 1001 });
        f.Db.Set<ProductCategory>().Add(new ProductCategory { ProductCategoryId = 1001, TenantId = tenant, Name = "Otra" });
        await f.Db.SaveChangesAsync();
        var tpl = await TemplateAsync(f, "Con categoría", [new(1, "sku"), new(2, "name"), new(3, "category")]);
        var imports = f.Get<EntityImportService>();

        var r = await imports.ImportAsync(ImportKinds.Product, tpl.PublicId, Csv("sku,name,cat", "K-1,Uno,bebidas / jugos", "K-2,Dos,Bebidas", "K-3,Tres,Nada"), "p.csv", commit: true, default);
        Assert.Equal(("CREATED", "CREATED", "ERROR"), (r.Rows[0].Status, r.Rows[1].Status, r.Rows[2].Status));
        Assert.Equal(ProductImportRows.UnknownCategoryMessage, r.Rows[2].Errors["category"]);
        Assert.Equal(902, f.Db.Set<Product>().Single(p => p.Sku == "K-1").ProductCategoryId);

        await imports.ImportAsync(ImportKinds.Product, tpl.PublicId, Csv("sku,name,cat", "K-1,Uno bis,"), "p.csv", commit: true, default);
        Assert.Equal(902, f.Db.Set<Product>().Single(p => p.Sku == "K-1").ProductCategoryId);
    }

    [Fact]
    public async Task An_offer_kit_is_not_changed_by_the_product_import()   // Lote 231: los kits de oferta no viven en Productos e inventario
    {
        await using var f = await CreateAsync();
        var tpl = await TemplateAsync(f);
        var imports = f.Get<EntityImportService>();
        await imports.ImportAsync(ImportKinds.Product, tpl.PublicId, Csv("sku,name,price,barcode", "OFERTA1,Kit,41.94,"), "p.csv", commit: true, default);
        var kit = f.Db.Set<Product>().Single(p => p.Sku == "OFERTA1");
        kit.IsOfferKit = true;
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();

        var r = await imports.ImportAsync(ImportKinds.Product, tpl.PublicId, Csv("sku,name,price,barcode", "OFERTA1,Otro nombre,1,"), "p.csv", commit: true, default);
        Assert.Equal("ERROR", r.Rows[0].Status);
        Assert.Equal(ProductImportRows.OfferKitMessage, r.Rows[0].Errors["sku"]);
        Assert.Equal(("Kit", 41.94m), (f.Db.Set<Product>().Single(p => p.Sku == "OFERTA1").Name, f.Db.Set<Product>().Single(p => p.Sku == "OFERTA1").SalePrice));
    }

    [Fact]
    public async Task Defaults_fill_empty_cells_and_unknown_unit_or_tracking_are_reported_per_row()
    {
        await using var f = await CreateAsync();
        var tpl = await TemplateAsync(f, "Con defaults", [new(1, "sku"), new(2, "name"), new(3, "baseUom")], new Dictionary<string, string> { ["trackingType"] = "NONE", ["baseUom"] = "UN" });
        var imports = f.Get<EntityImportService>();
        var r = await imports.ImportAsync(ImportKinds.Product, tpl.PublicId, Csv("sku,name,uom", "C-1,Uno,", "C-2,Dos,XYZ"), "p.csv", commit: false, default);
        Assert.Equal("OK", r.Rows[0].Status);
        Assert.Contains("XYZ", r.Rows[1].Errors["baseUom"]);
    }

    [Fact]
    public async Task An_empty_file_or_a_file_without_rows_is_a_400()
    {
        await using var f = await CreateAsync();
        var tpl = await TemplateAsync(f);
        var imports = f.Get<EntityImportService>();
        var none = await Assert.ThrowsAsync<ValidationException>(() => imports.ImportAsync(ImportKinds.Product, tpl.PublicId, [], "p.csv", false, default));
        Assert.Equal(EntityImportService.NoFileMessage, none.Message);
        var header = await Assert.ThrowsAsync<ValidationException>(() => imports.ImportAsync(ImportKinds.Product, tpl.PublicId, Csv("sku,name,price,barcode"), "p.csv", false, default));
        Assert.Equal(EntityImportService.NoRowsMessage, header.Message);
    }
}
