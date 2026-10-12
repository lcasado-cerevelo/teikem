using Teikem.Domain.Orders;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 38 (F-B): el servicio de plantillas atiende ORDER y CONSIGNEE con el tipo que pasa cada controlador. La ruta de órdenes
/// rechaza CONSIGNEE (400 con el mensaje exacto) y no ve las plantillas de consignatarios (404) ni al revés; los defaults de una
/// plantilla CONSIGNEE conservan sus campos (antes el catálogo de órdenes los descartaba).
/// </summary>
public sealed class ImportTemplateKindTests
{
    private static readonly List<ImportTemplateColumnDto> ConsigneeColumns = [new(1, "name"), new(2, "line1"), new(3, "city")];
    private static readonly List<ImportTemplateColumnDto> OrderColumns = [new(1, "consigneeName"), new(2, "pieces")];

    [Fact]
    public async Task The_order_route_rejects_the_consignee_kind_with_the_exact_message()
    {
        await using var f = await ConsigneeImportWorld.CreateAsync();
        var svc = f.Get<ImportTemplateService>();

        var list = await Assert.ThrowsAsync<ValidationException>(() => svc.GetListAsync(ImportKinds.Order, "consignee", null, false, default));
        Assert.Equal("Tipo de plantilla desconocido: CONSIGNEE.", list.Message);
        var create = await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(new ImportTemplateCreateRequest("X", ConsigneeColumns, Kind: "CONSIGNEE"), ImportKinds.Order, default));
        Assert.Equal("Tipo de plantilla desconocido: CONSIGNEE.", create.Message);
        var reverse = await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(new ImportTemplateCreateRequest("X", ConsigneeColumns, Kind: "ORDER"), ImportKinds.Consignee, default));
        Assert.Equal("Tipo de plantilla desconocido: ORDER.", reverse.Message);
    }

    [Fact]
    public async Task A_template_of_one_kind_is_a_404_from_the_other_route_and_lists_do_not_mix()
    {
        await using var f = await ConsigneeImportWorld.CreateAsync();
        var svc = f.Get<ImportTemplateService>();
        var consignee = await svc.CreateAsync(new ImportTemplateCreateRequest("Mismo nombre", ConsigneeColumns), ImportKinds.Consignee, default);
        var order = await svc.CreateAsync(new ImportTemplateCreateRequest("Mismo nombre", OrderColumns), ImportKinds.Order, default); // UQ por (tenant, kind, nombre)
        Assert.Equal(ImportKinds.Consignee, consignee.Kind);
        Assert.Equal(ImportKinds.Order, order.Kind);

        await Assert.ThrowsAsync<NotFoundException>(() => svc.GetAsync(consignee.PublicId, ImportKinds.Order, default));
        await Assert.ThrowsAsync<NotFoundException>(() => svc.UpdateAsync(consignee.PublicId, new ImportTemplatePatchRequest(Name: "Otro"), ImportKinds.Order, default));
        await Assert.ThrowsAsync<NotFoundException>(() => svc.DeactivateAsync(consignee.PublicId, ImportKinds.Order, default));
        await Assert.ThrowsAsync<NotFoundException>(() => svc.ReactivateAsync(order.PublicId, ImportKinds.Consignee, default));
        await Assert.ThrowsAsync<NotFoundException>(() => svc.GetAsync(order.PublicId, ImportKinds.Consignee, default));
        await Assert.ThrowsAsync<NotFoundException>(() => svc.ResolveForImportAsync(consignee.PublicId, null, ImportKinds.Order, default));

        Assert.Equal(order.PublicId, Assert.Single(await svc.GetListAsync(ImportKinds.Order, null, null, false, default)).PublicId);
        Assert.Equal(consignee.PublicId, Assert.Single(await svc.GetListAsync(ImportKinds.Consignee, null, null, false, default)).PublicId);
    }

    [Fact]
    public async Task Consignee_templates_validate_with_their_own_catalog()
    {
        await using var f = await ConsigneeImportWorld.CreateAsync();
        var svc = f.Get<ImportTemplateService>();

        var ex = await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(
            new ImportTemplateCreateRequest("Sin line1", [new(1, "name"), new(2, "city")]), ImportKinds.Consignee, default));
        Assert.Equal("La plantilla debe incluir las columnas name y line1.", Assert.Single(ex.Errors!["columns"]));

        var noDefault = await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(
            new ImportTemplateCreateRequest("Default en name", ConsigneeColumns, new Dictionary<string, string> { ["name"] = "X" }), ImportKinds.Consignee, default));
        Assert.Equal("El campo name no admite valor por defecto.", Assert.Single(noDefault.Errors!["defaults.name"]));

        // Las columnas de órdenes no existen en el catálogo de consignatarios.
        var unknown = await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(
            new ImportTemplateCreateRequest("De órdenes", OrderColumns), ImportKinds.Consignee, default));
        Assert.Equal("Campo desconocido: consigneeName.", Assert.Single(unknown.Errors!["columns[0].field"]));

        // Defaults tipados: se revisan al guardar con el mensaje de la fila (también el teléfono, con la regla de contactos).
        var typed = await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(
            new ImportTemplateCreateRequest("Defaults malos", ConsigneeColumns,
                new Dictionary<string, string> { ["defaultServiceMinutes"] = "diez", ["contactPhone"] = "abc" }), ImportKinds.Consignee, default));
        Assert.Equal("Los minutos de servicio deben ser un número entero.", Assert.Single(typed.Errors!["defaults.defaultServiceMinutes"]));
        Assert.Equal("Teléfono inválido.", Assert.Single(typed.Errors!["defaults.contactPhone"]));
    }

    [Fact]
    public async Task Consignee_defaults_and_columns_are_stored_with_canonical_keys_and_keep_their_fields()
    {
        await using var f = await ConsigneeImportWorld.CreateAsync();
        var svc = f.Get<ImportTemplateService>();
        var dto = await svc.CreateAsync(new ImportTemplateCreateRequest("Con defaults",
            [new(1, "NAME"), new(2, "Line1"), new(3, "city")],
            new Dictionary<string, string> { ["allowdupinvoice"] = "sí", ["defaultServiceMinutes"] = "10", ["country"] = "pr" }), ImportKinds.Consignee, default);

        Assert.Equal(new[] { "name", "line1", "city" }, dto.Columns.Select(c => c.Field));
        Assert.Equal("sí", dto.Defaults!["allowDupInvoice"]);
        Assert.Equal("10", dto.Defaults["defaultServiceMinutes"]);
        Assert.Equal("pr", dto.Defaults["country"]);
    }

    [Fact]
    public void Serialize_defaults_keeps_allow_dup_invoice_for_consignee_and_drops_it_for_orders()
    {
        var defaults = new Dictionary<string, string> { ["allowDupInvoice"] = "sí", ["city"] = "Ponce" };
        var consignee = ImportTemplateService.ParseDefaults(ImportTemplateService.SerializeDefaults(defaults, ImportKinds.Consignee));
        Assert.Equal("sí", consignee["allowDupInvoice"]);
        Assert.Equal("Ponce", consignee["city"]);

        var order = ImportTemplateService.ParseDefaults(ImportTemplateService.SerializeDefaults(defaults));
        Assert.False(order.ContainsKey("allowDupInvoice")); // ORDER (por defecto) sigue igual que antes
        Assert.Equal("Ponce", order["city"]);
    }
}
