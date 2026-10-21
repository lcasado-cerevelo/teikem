using System.Text.Json;
using System.Xml.Linq;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Integrations;
using Teikem.Domain.Orders;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// 2026-10-21 — Salida de un catálogo (productos) por Integración con la plantilla de la entidad, dictado de Luis: reglas de la salida sin estatus ni columnas propias, armado del archivo con las columnas de la plantilla,
/// alcance propio, cola de cambios (al crear o cambiar un producto) y filas del exportador.
/// </summary>
public sealed class EntityOutputTests
{
    private static OutputConfig Cfg(string format = "CSV", string trigger = "EVENT") => new([], trigger, format, ",", null, null, null);

    private static readonly IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows =
    [
        new Dictionary<string, object?> { ["sku"] = "A-1", ["name"] = "Uno, con coma", ["salePrice"] = 10.5m, ["barcode"] = null },
        new Dictionary<string, object?> { ["sku"] = "A-2", ["name"] = "Dos", ["salePrice"] = 3m, ["barcode"] = "777" },
    ];

    private static readonly IReadOnlyList<OutputColumn> Columns = [new("sku", "sku"), new("name", "name"), new("salePrice", "salePrice")];

    [Fact]
    public void An_entity_output_needs_neither_statuses_nor_columns_but_the_rest_is_still_validated()
    {
        Assert.Empty(OutputRules.Validate(Cfg(), remote: false, http: false, allowInsecureUrls: false, hasSecret: false, entity: true));
        Assert.Contains("output.statuses", OutputRules.Validate(Cfg(), false, false, false, false).Keys);   // la de envíos sí los pide
        Assert.Contains("output.columns", OutputRules.Validate(Cfg(), false, false, false, false).Keys);
        Assert.Contains("output.format", OutputRules.Validate(Cfg("YAML"), false, false, false, false, entity: true).Keys);
        Assert.Contains("output.trigger", OutputRules.Validate(Cfg(trigger: "NUNCA"), false, false, false, false, entity: true).Keys);
        Assert.Contains("output.http", OutputRules.Validate(Cfg(), remote: false, http: true, allowInsecureUrls: false, hasSecret: false, entity: true).Keys);   // la API saliente se valida igual
    }

    [Fact]
    public void The_file_is_built_with_the_template_columns_in_csv_json_and_xml()
    {
        Assert.Equal("sku,name,salePrice\r\nA-1,\"Uno, con coma\",10.5\r\nA-2,Dos,3\r\n", OutputRules.RenderRows(Cfg(), Columns, Rows, "products", "product"));

        using var json = JsonDocument.Parse(OutputRules.RenderRows(Cfg("JSON"), Columns, Rows, "products", "product"));
        var items = json.RootElement.GetProperty("products");
        Assert.Equal(2, items.GetArrayLength());
        Assert.Equal("Uno, con coma", items[0].GetProperty("name").GetString());
        Assert.Equal(10.5m, items[0].GetProperty("salePrice").GetDecimal());

        var xml = XDocument.Parse(OutputRules.RenderRows(Cfg("XML"), Columns, Rows, "products", "product"));
        Assert.Equal("products", xml.Root!.Name.LocalName);
        Assert.Equal(new[] { "A-1", "A-2" }, xml.Root.Elements("product").Select(e => e.Element("sku")!.Value).ToArray());
        Assert.Equal(("products", "product"), OutputRules.EntityNames("PRODUCT"));
    }

    [Fact]
    public void The_orders_output_still_renders_the_same_after_the_generalization()
    {
        var cfg = new OutputConfig(["DELIVERED"], "EVENT", "CSV", ";", null, [new("Orden", "orderNumber")], null);
        Assert.Equal("Orden\r\nA-1\r\n", OutputRules.Render(cfg, [new Dictionary<string, object?> { ["orderNumber"] = "A-1" }]));
    }

    [Fact]
    public void The_catalog_scope_exists_in_the_files_family_and_gives_only_inventory_view()
    {
        var scope = IntegrationScopes.Find(IntegrationScopes.FilesCatalogExport);
        Assert.NotNull(scope);
        Assert.Equal(IntegrationFamilies.Files, scope!.Family);
        Assert.False(scope.NeedsClient);
        Assert.Equal(new[] { "inventory.view" }, IntegrationScopes.PermissionsFor([IntegrationScopes.FilesCatalogExport]));
        Assert.Contains("PRODUCT", OutputRules.EntityKinds);
    }

    private static Task<WmsFixture> CreateAsync() => WmsFixture.CreateAsync(s =>
    {
        s.AddSingleton<OutputEntityRecorder>();
        s.AddSingleton<IExportEntityRows, ProductExportRows>();
        s.AddSingleton<ProductService>();
    });

    private static async Task<Integration> OutboundAsync(WmsFixture f, string name, bool active = true, string kind = ImportKinds.Product)
    {
        var row = new Integration { PublicId = Guid.NewGuid(), TenantId = WmsFixture.TenantId, Name = name, Direction = IntegrationDirections.Outbound, ConnectorKind = IntegrationConnectorKinds.Http, EntityKind = kind, IsActive = active, ScheduleKind = IntegrationScheduleKinds.Manual, ScopesJson = "[]" };
        f.Db.Integrations.Add(row);
        await f.Db.SaveChangesAsync();
        return row;
    }

    [Fact]
    public async Task Creating_and_changing_a_product_queues_it_once_for_each_active_product_output_only()
    {
        await using var f = await CreateAsync();
        var mine = await OutboundAsync(f, "Productos al ERP");
        var off = await OutboundAsync(f, "Apagada", active: false);
        var other = await OutboundAsync(f, "Otra entidad", kind: ImportKinds.Supplier);
        var products = f.Get<ProductService>();

        var created = await products.CreateAsync(new Teikem.Infrastructure.Contracts.ProductCreateRequest("P-1", "Uno"), default);
        var id = f.Db.Set<Product>().Single(p => p.Sku == "P-1").ProductId;
        Assert.Equal(new[] { id }, f.Db.IntegrationOutputEntities.Where(q => q.IntegrationId == mine.IntegrationId).Select(q => q.EntityId).ToArray());
        Assert.Empty(f.Db.IntegrationOutputEntities.Where(q => q.IntegrationId == off.IntegrationId || q.IntegrationId == other.IntegrationId));

        // un cambio mientras el anterior sigue esperando no lo duplica
        await products.UpdateAsync(created.Product.PublicId, new Teikem.Infrastructure.Contracts.ProductPatchRequest(Name: "Uno cambiado"), default);
        Assert.Single(f.Db.IntegrationOutputEntities.Where(q => q.IntegrationId == mine.IntegrationId));

        // ya mandado (tiene envío): un cambio nuevo vuelve a anotarlo
        foreach (var q in f.Db.IntegrationOutputEntities.Where(q => q.IntegrationId == mine.IntegrationId)) q.IntegrationOutputId = 1;
        await f.Db.SaveChangesAsync();
        await products.UpdateAsync(created.Product.PublicId, new Teikem.Infrastructure.Contracts.ProductPatchRequest(Name: "Otra vez"), default);
        Assert.Equal(2, f.Db.IntegrationOutputEntities.Count(q => q.IntegrationId == mine.IntegrationId));
        Assert.Equal(1, f.Db.IntegrationOutputEntities.Count(q => q.IntegrationId == mine.IntegrationId && q.IntegrationOutputId == null));
    }

    [Fact]
    public async Task The_exporter_gives_every_import_field_by_code_with_the_owner_and_units()
    {
        await using var f = await CreateAsync();
        var products = f.Get<ProductService>();
        await products.CreateAsync(new Teikem.Infrastructure.Contracts.ProductCreateRequest("P-9", "Nueve", Barcode: "999", SalePrice: 12.5m, Brand: "ACME"), default);
        var exporter = f.Get<IExportEntityRows>();
        var ids = await exporter.AllIdsAsync(default);
        var row = Assert.Single(await exporter.BuildAsync(ids, default));
        Assert.Equal("P-9", row[ProductImportFields.Sku]);
        Assert.Equal("Nueve", row[ProductImportFields.Name]);
        Assert.Equal("999", row[ProductImportFields.Barcode]);
        Assert.Equal(12.5m, row[ProductImportFields.SalePrice]);
        Assert.Equal("ACME", row[ProductImportFields.Brand]);
        Assert.Equal("UN", row[ProductImportFields.BaseUom]);
        Assert.Equal("NONE", row[ProductImportFields.TrackingType]);
        Assert.Null(row[ProductImportFields.OwnerClientCode]);
        Assert.All(ProductImportFields.All, field => Assert.True(row.ContainsKey(field), field));   // la plantilla puede pedir cualquier campo de la importación
    }
}
