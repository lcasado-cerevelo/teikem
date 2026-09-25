using Teikem.Domain.Constants;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 2: propagación de permisos nuevos a los roles ya clonados de los tenants (PermissionSeeder).
/// La selección es pura (PermissionCatalog.CodesToPropagate): plantilla del mismo nombre ∩ códigos nuevos − ya asignados.
/// </summary>
public class PermissionPropagationTests
{
    private static IReadOnlySet<string> Set(params string[] codes) => new HashSet<string>(codes, StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void Role_without_template_gets_nothing()
        => Assert.Empty(PermissionCatalog.CodesToPropagate("Supervisor", Set(PermissionCatalog.ClientsRead), Set()));

    [Fact]
    public void New_code_in_template_and_not_assigned_is_included()
        => Assert.Equal(new[] { PermissionCatalog.ClientsRead }, PermissionCatalog.CodesToPropagate("Dispatcher", Set(PermissionCatalog.ClientsRead), Set(PermissionCatalog.OrdersView)));

    [Fact]
    public void Already_assigned_code_is_excluded()
        => Assert.Empty(PermissionCatalog.CodesToPropagate("Dispatcher", Set(PermissionCatalog.ClientsRead), Set(PermissionCatalog.ClientsRead)));

    [Fact]
    public void Code_in_template_but_not_new_is_excluded()
        => Assert.Empty(PermissionCatalog.CodesToPropagate("Dispatcher", Set(PermissionCatalog.ContractsRead), Set()));

    [Fact]
    public void Code_new_but_outside_the_template_is_excluded()
        => Assert.Empty(PermissionCatalog.CodesToPropagate("Driver", Set(PermissionCatalog.ClientsRead), Set()));

    [Fact]
    public void TenantAdmin_receives_every_new_code()
    {
        var news = Set(PermissionCatalog.ClientsRead, PermissionCatalog.PortalUsersManage, PermissionCatalog.ContractsUpdate);
        var result = PermissionCatalog.CodesToPropagate("TenantAdmin", news, Set());
        Assert.Equal(3, result.Count);
        Assert.Contains(PermissionCatalog.PortalUsersManage, result);
    }

    [Fact]
    public void Nothing_new_means_nothing_to_propagate()
        => Assert.Empty(PermissionCatalog.CodesToPropagate("TenantAdmin", Set(), Set()));
}
