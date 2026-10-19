using System.Text;
using Teikem.Domain.Constants;
using Teikem.Domain.Integrations;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

// Lote 132 (P11): reglas puras de las integraciones — alcances por familia sin escalada de privilegios, carpeta y horario, datos crudos de la bandeja y motivo.
public sealed class IntegrationRulesTests
{
    private static readonly string[] Everything = [.. IntegrationScopes.All.SelectMany(s => s.Permissions), PermissionCatalog.IntegrationsCreateFiles, PermissionCatalog.IntegrationsCreate];
    private static ISet<string> Set(params string[] items) => new HashSet<string>(items, StringComparer.OrdinalIgnoreCase);
    private static readonly ISet<string> Modules = Set(ModuleKeys.LtlGround);

    [Fact]
    public void A_scope_needs_its_family_permission_every_permission_it_gives_and_the_company_module()
    {
        Assert.Empty(IntegrationScopes.Validate(new[] { IntegrationScopes.FilesOrdersImport }, Set(Everything), Modules));

        // sin el permiso de la familia
        var noFamily = Everything.Where(p => p != PermissionCatalog.IntegrationsCreateFiles).ToArray();
        Assert.Contains(PermissionCatalog.IntegrationsCreateFiles, IntegrationScopes.Validate(new[] { IntegrationScopes.FilesOrdersImport }, Set(noFamily), Modules)["scopes"]);

        // sin un permiso que el alcance le da a la cuenta de servicio: nadie da lo que no tiene
        var noCreate = Everything.Where(p => p != PermissionCatalog.OrdersCreate).ToArray();
        var missing = IntegrationScopes.Validate(new[] { IntegrationScopes.FilesOrdersImport }, Set(noCreate), Modules)["scopes"];
        Assert.Contains("orders.create", missing);

        // sin el módulo en la compañía
        Assert.Contains("no tiene el módulo", IntegrationScopes.Validate(new[] { IntegrationScopes.FilesOrdersImport }, Set(Everything), Set()) ["scopes"]);
    }

    [Fact]
    public void Scopes_are_validated_with_exact_messages_and_give_the_union_of_permissions()
    {
        Assert.Equal(IntegrationScopes.NoScopesMessage, IntegrationScopes.Validate(Array.Empty<string>(), Set(Everything), Modules)["scopes"]);
        Assert.Equal(IntegrationScopes.UnknownScopeMessage("x.y"), IntegrationScopes.Validate(new[] { "x.y" }, Set(Everything), Modules)["scopes"]);
        Assert.Equal(new[] { "clients.read", "locations.create", "locations.read", "orders.create", "orders.view" }, IntegrationScopes.PermissionsFor(new[] { "FILES.ORDERS_IMPORT", "files.orders_import" }));
        Assert.Equal(IntegrationFamilies.PermissionOf(IntegrationFamilies.Warehouse), PermissionCatalog.IntegrationsCreateWarehouse);
        Assert.Null(IntegrationFamilies.PermissionOf("OTRA"));
    }

    [Theory]
    [InlineData("axiscare", true)]
    [InlineData("Axis_Care-2", true)]
    [InlineData("../fuera", false)]
    [InlineData("a/b", false)]
    [InlineData("con espacio", false)]
    [InlineData("", false)]
    public void The_folder_name_never_leaves_the_root(string folder, bool valid) => Assert.Equal(valid, IntegrationRules.IsValidFolder(folder));

    [Fact]
    public void The_next_run_only_exists_with_an_interval_and_the_service_account_names_are_stable()
    {
        var now = new DateTime(2026, 10, 19, 12, 0, 0, DateTimeKind.Utc);
        Assert.Equal(now.AddMinutes(5), IntegrationRules.NextRun(IntegrationScheduleKinds.Interval, 300, now));
        Assert.Null(IntegrationRules.NextRun(IntegrationScheduleKinds.Manual, null, now));
        var id = Guid.Parse("11111111-2222-3333-4444-555555555555");
        Assert.Equal("integration-11111111222233334444555555555555@service.teikem.local", IntegrationRules.ServiceEmail(id));
        Assert.Equal("Integración: AxisCare", IntegrationRules.ServiceRoleName(" AxisCare "));
        Assert.Contains(IntegrationConnectorKinds.Folder, IntegrationConnectorKinds.Available);
        Assert.Contains(IntegrationConnectorKinds.Email, IntegrationConnectorKinds.Available);
        Assert.DoesNotContain(IntegrationConnectorKinds.Sftp, IntegrationConnectorKinds.Available);
    }

    [Fact]
    public void Inbox_data_round_trips_to_csv_with_quotes_delimiters_and_the_header()
    {
        var data = new IntegrationRunner.InboxData(new List<string> { "orden", "nota" }, new List<InboxRowDto> { new(2, new[] { "A-1", "dice \"hola\", adiós" }), new(3, new[] { "A-2", "línea\nnueva" }) });
        var json = System.Text.Json.JsonSerializer.Serialize(data, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        var back = IntegrationRunner.ParseData(json)!;
        Assert.Equal(2, back.Rows.Count);
        var csv = IntegrationRunner.ToCsv(back, ',');
        Assert.StartsWith("orden,nota", csv);
        Assert.Contains("A-1,\"dice \"\"hola\"\", adiós\"", csv);
        Assert.Contains("A-2,\"línea\nnueva\"", csv.Replace("\r\n", "\n"));
        Assert.Null(IntegrationRunner.ParseData(null));
        Assert.Null(IntegrationRunner.ParseData(" "));
    }

    [Fact]
    public void Text_is_read_as_utf8_and_falls_back_to_latin1()
    {
        Assert.Equal("Ñandú café", IntegrationRunner.Decode(new UTF8Encoding(false).GetBytes("Ñandú café")));
        Assert.Equal("Ñandú café", IntegrationRunner.Decode(new UTF8Encoding(true).GetPreamble().Concat(new UTF8Encoding(false).GetBytes("Ñandú café")).ToArray()).TrimStart('﻿'));
        Assert.Equal("Ñandú café", IntegrationRunner.Decode(Encoding.Latin1.GetBytes("Ñandú café")));   // lo que exporta Excel en Windows
    }

    [Fact]
    public void The_inbox_reason_lists_order_line_and_creation_errors_without_the_lines_prefix()
    {
        var row = new ImportRowDto(2, 2, new Dictionary<string, string>(), null,
            new Dictionary<string, string> { ["lines"] = "La orden no trae ninguna pieza.", ["consigneeName"] = "Falta el consignatario." }, Array.Empty<string>(), false, null, null, null, null, false,
            new[] { new ImportLineDto(4, "PIECE", new Dictionary<string, string>(), new Dictionary<string, string> { ["pieceNumber"] = "repetido" }) });
        Assert.Equal("La orden no trae ninguna pieza. | consigneeName: Falta el consignatario. | Fila 4 · pieceNumber: repetido", IntegrationRunner.ReasonOf(row));
        Assert.Equal("La orden no se pudo procesar.", IntegrationRunner.ReasonOf(row with { Errors = new Dictionary<string, string>(), Lines = null }));
        Assert.Equal("Cliente suspendido.", IntegrationRunner.ReasonOf(row with { Errors = new Dictionary<string, string>(), Lines = null, Error = "Cliente suspendido." }));
    }

    [Fact]
    public void The_service_account_permissions_are_in_the_catalog_and_the_admin_template()
    {
        var all = PermissionCatalog.All.Select(p => p.Code).ToHashSet();
        foreach (var code in new[] { PermissionCatalog.IntegrationsView, PermissionCatalog.IntegrationsCreate, PermissionCatalog.IntegrationsCreateTransport, PermissionCatalog.IntegrationsCreateWarehouse,
                     PermissionCatalog.IntegrationsCreateFiles, PermissionCatalog.IntegrationsManage, PermissionCatalog.IntegrationsInbox })
        {
            Assert.Contains(code, all);
            Assert.Contains(code, PermissionCatalog.RoleTemplates["TenantAdmin"]);
        }
        foreach (var scope in IntegrationScopes.All) foreach (var p in scope.Permissions) Assert.Contains(p, all);
        Assert.Equal("INTEGRATIONS", ModuleKeys.Integrations);
    }
}
