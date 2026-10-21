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
/// confirmación que crea las filas válidas y rechazo de repetidos (solo crea, decisión de Luis).
/// </summary>
public sealed class ProductImportTests
{
    private static Task<WmsFixture> CreateAsync() => WmsFixture.CreateAsync(s =>
    {
        s.AddSingleton<ImportTemplateService>();
        s.AddSingleton<ProductService>();
        s.AddSingleton<ProductImportService>();
    });

    private static Task<ImportTemplateDto> TemplateAsync(WmsFixture f, string name = "Productos", IList<ImportTemplateColumnDto>? columns = null, IDictionary<string, string>? defaults = null)
        => f.Get<ImportTemplateService>().CreateAsync(new ImportTemplateCreateRequest(name, columns ?? [new(1, "sku"), new(2, "name"), new(3, "salePrice"), new(4, "barcode")], defaults), ImportKinds.Product, default);

    private static byte[] Csv(params string[] lines) => Encoding.UTF8.GetBytes(string.Join("\n", lines));

    [Fact]
    public void The_field_catalog_marks_sku_required_and_sku_and_barcode_without_defaults()
    {
        var fields = ImportTemplateService.GetProductFields();
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
        var imports = f.Get<ProductImportService>();
        var file = Csv("sku,name,price,barcode", "A-1,Producto uno,10.50,111", "A-2,Producto dos,abc,222", ",Sin sku,1,333", "A-3,Producto tres,1,444");

        var preview = await imports.ImportAsync(tpl.PublicId, file, "p.csv", commit: false, default);
        Assert.False(preview.Committed);
        Assert.Equal((4, 2, 2, 0), (preview.Total, preview.Valid, preview.Rejected, preview.Created));
        Assert.Equal("ERROR", preview.Rows[1].Status);
        Assert.Equal(ProductImportService.NotANumberMessage, preview.Rows[1].Errors["salePrice"]);
        Assert.True(preview.Rows[2].Errors.ContainsKey("sku"));
        Assert.Empty(f.Db.Set<Product>().ToList());   // la vista previa no guarda nada

        var done = await imports.ImportAsync(tpl.PublicId, file, "p.csv", commit: true, default);
        Assert.Equal((2, 2), (done.Created, done.Rows.Count(r => r.Status == "CREATED")));
        Assert.Equal(new[] { "A-1", "A-3" }, f.Db.Set<Product>().Select(p => p.Sku).OrderBy(s => s).ToArray());
    }

    [Fact]
    public async Task Existing_sku_or_barcode_and_repeats_inside_the_file_are_rejected_never_updated()
    {
        await using var f = await CreateAsync();
        var tpl = await TemplateAsync(f);
        var imports = f.Get<ProductImportService>();
        await imports.ImportAsync(tpl.PublicId, Csv("sku,name,price,barcode", "B-1,Original,5,900"), "p.csv", commit: true, default);

        var second = await imports.ImportAsync(tpl.PublicId, Csv("sku,name,price,barcode", "b-1,Cambiado,9,901", "B-2,Otro,1,900", "B-3,Tres,1,902", "B-3,Tres otra vez,1,903"), "p.csv", commit: true, default);
        Assert.Equal(ProductRules.SkuTaken, second.Rows[0].Errors["sku"]);
        Assert.Equal(ProductRules.BarcodeTaken, second.Rows[1].Errors["barcode"]);
        Assert.Equal("CREATED", second.Rows[2].Status);
        Assert.Equal(ProductImportService.RepeatedInFileMessage, second.Rows[3].Errors["sku"]);
        Assert.Equal("Original", f.Db.Set<Product>().Single(p => p.Sku == "B-1").Name);   // lo existente no se tocó
    }

    [Fact]
    public async Task Defaults_fill_empty_cells_and_unknown_unit_or_tracking_are_reported_per_row()
    {
        await using var f = await CreateAsync();
        var tpl = await TemplateAsync(f, "Con defaults", [new(1, "sku"), new(2, "name"), new(3, "baseUom")], new Dictionary<string, string> { ["trackingType"] = "NONE", ["baseUom"] = "UN" });
        var imports = f.Get<ProductImportService>();
        var r = await imports.ImportAsync(tpl.PublicId, Csv("sku,name,uom", "C-1,Uno,", "C-2,Dos,XYZ"), "p.csv", commit: false, default);
        Assert.Equal("OK", r.Rows[0].Status);
        Assert.Contains("XYZ", r.Rows[1].Errors["baseUom"]);
    }

    [Fact]
    public async Task An_empty_file_or_a_file_without_rows_is_a_400()
    {
        await using var f = await CreateAsync();
        var tpl = await TemplateAsync(f);
        var imports = f.Get<ProductImportService>();
        var none = await Assert.ThrowsAsync<ValidationException>(() => imports.ImportAsync(tpl.PublicId, [], "p.csv", false, default));
        Assert.Equal(ProductImportService.NoFileMessage, none.Message);
        var header = await Assert.ThrowsAsync<ValidationException>(() => imports.ImportAsync(tpl.PublicId, Csv("sku,name,price,barcode"), "p.csv", false, default));
        Assert.Equal(ProductImportService.NoRowsMessage, header.Message);
    }
}
