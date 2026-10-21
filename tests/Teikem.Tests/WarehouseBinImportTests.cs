using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Constants;
using Teikem.Domain.Orders;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>2026-10-21 — Importación de almacenes y de posiciones (3.ª y 4.ª entidades del motor genérico): plantillas, revisión sin guardar con las reglas del alta manual, creación por fila y rechazo de repetidos.</summary>
public sealed class WarehouseBinImportTests
{
    private static Task<WmsFixture> CreateAsync() => WmsFixture.CreateAsync(s =>
    {
        s.AddSingleton<ImportTemplateService>();
        s.AddSingleton<StatusService>();
        s.AddSingleton<WarehouseService>();
        s.AddSingleton<WarehouseLayoutService>();
        s.AddSingleton<EntityImportService>();
        s.AddSingleton<IImportEntityRows, WarehouseImportRows>();
        s.AddSingleton<IImportEntityRows, BinImportRows>();
    });

    private static Task<ImportTemplateDto> TemplateAsync(WmsFixture f, string kind, string name, IList<ImportTemplateColumnDto> columns, IDictionary<string, string>? defaults = null)
        => f.Get<ImportTemplateService>().CreateAsync(new ImportTemplateCreateRequest(name, columns, defaults), kind, default);

    private static byte[] Csv(params string[] lines) => Encoding.UTF8.GetBytes(string.Join("\n", lines));

    [Fact]
    public async Task Warehouse_template_needs_code_and_name_columns_and_no_defaults_for_them()
    {
        await using var f = await CreateAsync();
        var noCode = await Assert.ThrowsAsync<ValidationException>(() => TemplateAsync(f, ImportKinds.Warehouse, "A", [new(1, "name")]));
        Assert.Equal(WarehouseImportFields.CodeColumnMessage, noCode.Errors!["columns"][0]);
        var noName = await Assert.ThrowsAsync<ValidationException>(() => TemplateAsync(f, ImportKinds.Warehouse, "B", [new(1, "code")]));
        Assert.Equal(WarehouseImportFields.NameColumnMessage, noName.Errors!["columns"][0]);
        var def = await Assert.ThrowsAsync<ValidationException>(() => TemplateAsync(f, ImportKinds.Warehouse, "C", [new(1, "code"), new(2, "name")], new Dictionary<string, string> { ["code"] = "X" }));
        Assert.Equal(ImportEntityMeta.DefaultNotAllowedMessage("code"), def.Errors!["defaults.code"][0]);
        await TemplateAsync(f, ImportKinds.Warehouse, "D", [new(1, "code"), new(2, "name")], new Dictionary<string, string> { ["country"] = "PR", ["receivingMode"] = "PUTAWAY" });
    }

    [Fact]
    public async Task Warehouses_review_without_saving_commit_the_valid_ones_and_reject_repeats()
    {
        await using var f = await CreateAsync();
        var tpl = await TemplateAsync(f, ImportKinds.Warehouse, "Almacenes", [new(1, "code"), new(2, "name"), new(3, "city")]);
        var imports = f.Get<EntityImportService>();
        await f.AddWarehouseAsync("EXIST");
        var file = Csv("code,name,city", "ALM-1,Almacén uno,San Juan", "ALM-1,Repetido,Ponce", "EXIST,Ya existe,Caguas", ",Sin código,Humacao", "ALM 2,Código inválido,Ponce");

        var review = await imports.ImportAsync(ImportKinds.Warehouse, tpl.PublicId, file, "a.csv", commit: false, default);
        Assert.Equal((5, 1, 4, 0), (review.Total, review.Valid, review.Rejected, review.Created));
        Assert.Equal(WarehouseImportRows.RepeatedInFileMessage, review.Rows[1].Errors["code"]);
        Assert.Equal(WarehouseRules.DuplicateWarehouseMessage, review.Rows[2].Errors["code"]);
        Assert.True(review.Rows[3].Errors.ContainsKey("code"));
        Assert.True(review.Rows[4].Errors.ContainsKey("code"));
        Assert.DoesNotContain(f.Db.Warehouses, w => w.Code == "ALM-1");

        var done = await imports.ImportAsync(ImportKinds.Warehouse, tpl.PublicId, file, "a.csv", commit: true, default);
        Assert.Equal(1, done.Created);
        Assert.Contains(f.Db.Warehouses, w => w.Code == "ALM-1" && w.Name == "Almacén uno");
    }

    [Fact]
    public async Task Bin_template_needs_warehouse_and_zone_and_a_code_or_its_parts()
    {
        await using var f = await CreateAsync();
        var noWh = await Assert.ThrowsAsync<ValidationException>(() => TemplateAsync(f, ImportKinds.Bin, "A", [new(1, "zoneCode"), new(2, "code")]));
        Assert.Equal(BinImportFields.WarehouseMessage, noWh.Errors!["columns"][0]);
        var noZone = await Assert.ThrowsAsync<ValidationException>(() => TemplateAsync(f, ImportKinds.Bin, "B", [new(1, "warehouseCode"), new(2, "code")]));
        Assert.Equal(BinImportFields.ZoneMessage, noZone.Errors!["columns"][0]);
        var noCode = await Assert.ThrowsAsync<ValidationException>(() => TemplateAsync(f, ImportKinds.Bin, "C", [new(1, "warehouseCode"), new(2, "zoneCode")]));
        Assert.Equal(BinImportFields.CodeMessage, noCode.Errors!["columns"][0]);
        await TemplateAsync(f, ImportKinds.Bin, "D", [new(1, "code")], new Dictionary<string, string> { ["warehouseCode"] = "ALM", ["zoneCode"] = "PCK" });   // un archivo de una sola zona
        await TemplateAsync(f, ImportKinds.Bin, "E", [new(1, "warehouseCode"), new(2, "zoneCode"), new(3, "aisle"), new(4, "rack"), new(5, "level"), new(6, "position")]);   // código por sus partes
    }

    [Fact]
    public async Task Bins_are_created_in_their_zone_and_unknown_warehouse_zone_repeats_and_bad_numbers_are_rejected()
    {
        await using var f = await CreateAsync();
        var wh = await f.AddWarehouseAsync("ALM");
        var zone = await f.AddZoneAsync(wh, "PCK", ZoneTypes.Picking);
        await f.AddBinAsync(zone, "A-01");
        var tpl = await TemplateAsync(f, ImportKinds.Bin, "Posiciones", [new(1, "warehouseCode"), new(2, "zoneCode"), new(3, "code"), new(4, "maxCapacityQty")]);
        var imports = f.Get<EntityImportService>();
        var file = Csv("wh,zone,code,cap", "ALM,PCK,B-01,50", "ALM,PCK,B-01,50", "ALM,PCK,A-01,10", "NOPE,PCK,C-01,5", "ALM,NOZ,C-01,5", "ALM,PCK,C-02,mucho");

        var review = await imports.ImportAsync(ImportKinds.Bin, tpl.PublicId, file, "b.csv", commit: false, default);
        Assert.Equal((6, 1, 5, 0), (review.Total, review.Valid, review.Rejected, review.Created));
        Assert.Equal(BinImportRows.RepeatedInFileMessage, review.Rows[1].Errors["code"]);
        Assert.Equal(WarehouseRules.DuplicateBinMessage, review.Rows[2].Errors["code"]);
        Assert.Equal(BinImportRows.UnknownWarehouseMessage, review.Rows[3].Errors["warehouseCode"]);
        Assert.Equal(BinImportRows.UnknownZoneMessage, review.Rows[4].Errors["zoneCode"]);
        Assert.Equal(EntityImportService.NotANumberMessage, review.Rows[5].Errors["maxCapacityQty"]);
        Assert.DoesNotContain(f.Db.WarehouseBins, b => b.Code == "B-01");

        var done = await imports.ImportAsync(ImportKinds.Bin, tpl.PublicId, file, "b.csv", commit: true, default);
        Assert.Equal(1, done.Created);
        var bin = Assert.Single(f.Db.WarehouseBins, b => b.Code == "B-01");
        Assert.Equal((zone.WarehouseZoneId, 50), (bin.WarehouseZoneId, bin.MaxCapacityQty));
    }

    [Fact]
    public async Task A_bin_code_can_come_from_its_parts_and_a_file_of_one_zone_uses_the_template_defaults()
    {
        await using var f = await CreateAsync();
        var wh = await f.AddWarehouseAsync("ALM");
        await f.AddZoneAsync(wh, "PCK", ZoneTypes.Picking);
        var tpl = await TemplateAsync(f, ImportKinds.Bin, "Por partes", [new(1, "aisle"), new(2, "rack"), new(3, "level"), new(4, "position")], new Dictionary<string, string> { ["warehouseCode"] = "ALM", ["zoneCode"] = "PCK" });
        var imports = f.Get<EntityImportService>();
        var done = await imports.ImportAsync(ImportKinds.Bin, tpl.PublicId, Csv("a,r,l,p", "01,02,03,04"), "b.csv", commit: true, default);
        Assert.Equal(1, done.Created);
        var bin = Assert.Single(f.Db.WarehouseBins);
        Assert.Equal(WarehouseRules.ComposeBinCode("01", "02", "03", "04").Code, bin.Code);
    }
}
