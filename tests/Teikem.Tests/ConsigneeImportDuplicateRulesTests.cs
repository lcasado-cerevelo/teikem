using Teikem.Domain.Clients;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 38 (F-B): reglas puras de duplicados de la importación de consignatarios, por dueño (cliente o compartida):
/// código (sin distinguir mayúsculas) y nombre + línea 1 normalizados, dentro del archivo y contra el directorio.
/// </summary>
public class ConsigneeImportDuplicateRulesTests
{
    private const int ClientA = 10, ClientB = 20;

    private static ConsigneeRowKey Row(int row, int? owner, string? code, string? name = "Farmacia", string? line1 = "Calle 1") => new(row, owner, code, name, line1);

    private static ExistingConsignee Loc(int id, int? owner, string? code, string name = "Farmacia Central", string line1 = "Calle 9", bool active = true, bool delivery = true)
        => new(id, owner, code, name, line1, active, delivery);

    // ---------------------------------------------------------------- dentro del archivo

    [Fact]
    public void Repeated_code_with_the_same_owner_is_an_error_naming_the_first_row()
    {
        var findings = ConsigneeImportMapping.FindDuplicatesInFile(new[]
        {
            Row(1, ClientA, "C-1", "Uno", "Calle 1"), Row(2, ClientA, "X", "Dos", "Calle 2"), Row(3, ClientA, "c-1", "Tres", "Calle 3"),
        });
        var f = Assert.Single(findings[3]);
        Assert.Equal("code", f.Field);
        Assert.False(f.IsWarning);
        Assert.Equal("El código c-1 se repite en la fila 1 del archivo.", f.Message);
        Assert.False(findings.ContainsKey(1));
        Assert.False(findings.ContainsKey(2));
    }

    [Fact]
    public void Same_code_with_another_owner_is_not_an_error()
    {
        var findings = ConsigneeImportMapping.FindDuplicatesInFile(new[]
        {
            Row(1, ClientA, "C-1", "Uno", "Calle 1"), Row(2, ClientB, "C-1", "Dos", "Calle 2"), Row(3, null, "C-1", "Tres", "Calle 3"),
        });
        Assert.Empty(findings);
    }

    [Fact]
    public void Name_and_line1_normalized_repeated_in_the_file_is_an_error()
    {
        var findings = ConsigneeImportMapping.FindDuplicatesInFile(new[]
        {
            Row(1, null, null, "Farmacia Río", "Calle  Sol 5"), Row(2, null, null, "  farmacia rio ", "calle sol 5"),
        });
        var f = Assert.Single(findings[2]);
        Assert.Equal("name", f.Field);
        Assert.Equal("El consignatario 'farmacia rio' con esa dirección se repite en la fila 1 del archivo.", f.Message);
    }

    // ---------------------------------------------------------------- contra el directorio

    [Fact]
    public void Client_row_collides_with_its_own_and_shared_codes_but_not_with_other_clients()
    {
        var dir = ConsigneeImportMapping.BuildDirectory(new[] { Loc(1, ClientA, "A-1", "Propio"), Loc(2, null, "S-1", "Compartido"), Loc(3, ClientB, "B-1", "De otro") });

        Assert.Equal("Ya existe un consignatario con el código 'a-1' ('Propio').", Assert.Single(dir.Evaluate(Row(1, ClientA, "a-1"))).Message);
        Assert.Equal("Ya existe un consignatario con el código 'S-1' ('Compartido').", Assert.Single(dir.Evaluate(Row(1, ClientA, "S-1"))).Message);
        Assert.Empty(dir.Evaluate(Row(1, ClientA, "B-1")));
    }

    [Fact]
    public void Only_an_inactive_match_asks_to_reactivate_it()
    {
        var dir = ConsigneeImportMapping.BuildDirectory(new[] { Loc(1, ClientA, "A-1", "Vieja", active: false) });
        var f = Assert.Single(dir.Evaluate(Row(1, ClientA, "A-1")));
        Assert.False(f.IsWarning);
        Assert.Equal("Ya existe un consignatario inactivo con el código 'A-1' ('Vieja'); reactívelo en vez de importarlo.", f.Message);
    }

    [Fact]
    public void Shared_row_whose_code_a_client_uses_is_only_a_warning()
    {
        var dir = ConsigneeImportMapping.BuildDirectory(new[] { Loc(1, ClientA, "A-1", "Del cliente") });
        var f = Assert.Single(dir.Evaluate(Row(1, null, "A-1")));
        Assert.True(f.IsWarning);
        Assert.Equal("El código A-1 también lo usa un consignatario de un cliente; para ese cliente seguirá resolviendo al suyo.", f.Message);
    }

    [Fact]
    public void Same_name_and_line1_normalized_with_the_same_owner_is_an_error()
    {
        var dir = ConsigneeImportMapping.BuildDirectory(new[] { Loc(1, ClientA, null, "Farmacia Río", "Calle Sol 5") });
        var f = Assert.Single(dir.Evaluate(Row(1, ClientA, null, " farmacia   rio", "CALLE SOL 5")));
        Assert.Equal("name", f.Field);
        Assert.False(f.IsWarning);
        Assert.Equal("Ya existe un consignatario activo con el mismo nombre y dirección (línea 1).", f.Message);
        Assert.Empty(dir.Evaluate(Row(1, ClientB, null, "Farmacia Río", "Calle Sol 5"))); // otro dueño: no choca
    }

    [Fact]
    public void Same_name_and_line1_as_an_inactive_one_is_a_warning()
    {
        var dir = ConsigneeImportMapping.BuildDirectory(new[] { Loc(1, null, null, "Farmacia", "Calle 1", active: false) });
        var f = Assert.Single(dir.Evaluate(Row(1, null, null, "Farmacia", "Calle 1")));
        Assert.True(f.IsWarning);
        Assert.Equal("Hay un consignatario inactivo con el mismo nombre y dirección; considere reactivarlo.", f.Message);
    }

    [Fact]
    public void Same_name_with_another_line1_is_a_branch_warning()
    {
        var dir = ConsigneeImportMapping.BuildDirectory(new[] { Loc(1, ClientA, null, "Farmacia Central", "Calle 9") });
        var f = Assert.Single(dir.Evaluate(Row(1, ClientA, null, "Farmacia Central", "Avenida 3")));
        Assert.True(f.IsWarning);
        Assert.Equal("El nombre 'Farmacia Central' ya lo usa otro consignatario de este dueño; se creará como sucursal distinta.", f.Message);
    }

    [Fact]
    public void Name_rules_only_look_at_delivery_or_both_but_code_rules_look_at_every_type()
    {
        var dir = ConsigneeImportMapping.BuildDirectory(new[] { Loc(1, ClientA, "ALM-1", "Almacén", "Calle 1", delivery: false) });
        Assert.Empty(dir.Evaluate(Row(1, ClientA, null, "Almacén", "Calle 1")));
        Assert.Single(dir.Evaluate(Row(1, ClientA, "ALM-1", "Otro", "Calle 2")));
    }

    [Fact]
    public void Adding_the_same_location_twice_is_ignored()
    {
        var dir = ConsigneeImportMapping.BuildDirectory(new[] { Loc(1, ClientA, "A-1") });
        dir.AddRange(new[] { Loc(1, ClientA, "A-1"), Loc(2, ClientA, "A-2") });
        Assert.Equal(2, dir.Count);
        Assert.Single(dir.Evaluate(Row(1, ClientA, "A-1", "Otro", "Otra calle")));
    }
}
