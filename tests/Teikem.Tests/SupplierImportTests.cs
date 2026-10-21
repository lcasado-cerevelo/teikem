using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Orders;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>2026-10-21 — Importación de proveedores (2.ª entidad del motor genérico): plantilla Kind SUPPLIER, revisión con las reglas del alta manual sin guardar, creación por fila y rechazo de repetidos.</summary>
public sealed class SupplierImportTests
{
    private static Task<WmsFixture> CreateAsync() => WmsFixture.CreateAsync(s =>
    {
        s.AddSingleton<ImportTemplateService>();
        s.AddSingleton<SupplierService>();
        s.AddSingleton<EntityImportService>();
        s.AddSingleton<IImportEntityRows, SupplierImportRows>();
    });

    private static Task<ImportTemplateDto> TemplateAsync(WmsFixture f, string name = "Proveedores", IList<ImportTemplateColumnDto>? columns = null)
        => f.Get<ImportTemplateService>().CreateAsync(new ImportTemplateCreateRequest(name, columns ?? [new(1, "name"), new(2, "contactName"), new(3, "email")]), ImportKinds.Supplier, default);

    private static byte[] Csv(params string[] lines) => Encoding.UTF8.GetBytes(string.Join("\n", lines));

    [Fact]
    public async Task The_template_needs_the_name_column_and_does_not_allow_defaults_for_personal_data()
    {
        await using var f = await CreateAsync();
        var noName = await Assert.ThrowsAsync<ValidationException>(() => TemplateAsync(f, "A", [new(1, "email")]));
        Assert.Equal(SupplierImportFields.NameColumnMessage, noName.Errors!["columns"][0]);
        var nameDefault = await Assert.ThrowsAsync<ValidationException>(() => f.Get<ImportTemplateService>().CreateAsync(
            new ImportTemplateCreateRequest("B", [new(1, "email")], new Dictionary<string, string> { ["name"] = "X" }), ImportKinds.Supplier, default));
        Assert.Equal(ImportEntityMeta.DefaultNotAllowedMessage("name"), nameDefault.Errors!["defaults.name"][0]);
        await f.Get<ImportTemplateService>().CreateAsync(new ImportTemplateCreateRequest("C", [new(1, "name")], new Dictionary<string, string> { ["notes"] = "Importado" }), ImportKinds.Supplier, default);
        Assert.Contains(ImportTemplateService.GetEntityFields(ImportKinds.Supplier), x => x.Field == "name" && x.Required && !x.DefaultAllowed);
    }

    [Fact]
    public async Task Review_does_not_save_and_commit_creates_valid_rows_and_rejects_repeats_and_bad_emails()
    {
        await using var f = await CreateAsync();
        var tpl = await TemplateAsync(f);
        var imports = f.Get<EntityImportService>();
        var file = Csv("name,contact,email", "Acme,Ana,ana@acme.com", "Beta,Beto,no-es-correo", "acme,Otra,otra@acme.com", ",Sin nombre,x@y.com");

        var review = await imports.ImportAsync(ImportKinds.Supplier, tpl.PublicId, file, "p.csv", commit: false, default);
        Assert.Equal((4, 1, 3, 0), (review.Total, review.Valid, review.Rejected, review.Created));
        Assert.Equal("OK", review.Rows[0].Status);
        Assert.True(review.Rows[1].Errors.ContainsKey("email"));
        Assert.Equal(SupplierImportRows.RepeatedInFileMessage, review.Rows[2].Errors["name"]);
        Assert.True(review.Rows[3].Errors.ContainsKey("name"));
        Assert.Empty(f.Db.Set<Supplier>().ToList());

        var done = await imports.ImportAsync(ImportKinds.Supplier, tpl.PublicId, file, "p.csv", commit: true, default);
        Assert.Equal(1, done.Created);
        Assert.Equal(new[] { "Acme" }, f.Db.Set<Supplier>().Select(s => s.Name).ToArray());

        var again = await imports.ImportAsync(ImportKinds.Supplier, tpl.PublicId, Csv("name,contact,email", "Acme,Ana,ana@acme.com"), "p.csv", commit: true, default);
        Assert.Equal(0, again.Created);
        Assert.Equal("ERROR", again.Rows[0].Status);   // ya existe: solo crea, nunca actualiza
    }

    [Fact]
    public async Task A_supplier_template_is_a_404_from_the_product_route_and_the_kinds_are_registered()
    {
        await using var f = await CreateAsync();
        var tpl = await TemplateAsync(f);
        await Assert.ThrowsAsync<NotFoundException>(() => f.Get<ImportTemplateService>().GetAsync(tpl.PublicId, ImportKinds.Product, default));
        Assert.Contains(ImportKinds.Product, ImportEntities.Kinds);
        Assert.Contains(ImportKinds.Supplier, ImportEntities.Kinds);
        var unknown = await Assert.ThrowsAsync<ValidationException>(() => f.Get<EntityImportService>().ImportAsync("WAREHOUSE", tpl.PublicId, Csv("a"), "p.csv", false, default));
        Assert.Equal(ImportTemplateService.UnknownKindMessage("WAREHOUSE"), unknown.Message);
    }
}
