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
    public void Lote27_rental_extend_and_return_go_to_tenant_admin_and_warehouse_operator_clones_only()
    {
        // Lote 27 (Rentas R1): los códigos nuevos llegan a los clones de las plantillas que gestionan rentas; el seed (5b3) los
        // completa además en todo rol de tenant con rental.manage (roles propios incluidos), una sola vez.
        var news = Set(PermissionCatalog.RentalExtend, PermissionCatalog.RentalReturn);
        Assert.Equal(new[] { PermissionCatalog.RentalExtend, PermissionCatalog.RentalReturn },
            PermissionCatalog.CodesToPropagate("WarehouseOperator", news, Set(PermissionCatalog.RentalManage)));
        Assert.Equal(2, PermissionCatalog.CodesToPropagate("TenantAdmin", news, Set()).Count);
        Assert.Equal(new[] { PermissionCatalog.RentalReturn }, PermissionCatalog.CodesToPropagate("WarehouseOperator", news, Set(PermissionCatalog.RentalExtend)));
        foreach (var role in new[] { "Billing", "Dispatcher", "Driver", "ReadOnly" })
            Assert.Empty(PermissionCatalog.CodesToPropagate(role, news, Set()));
    }

    [Fact]
    public void Lote28_returns_and_the_process_need_no_new_codes_and_the_warehouse_operator_already_holds_them()
    {
        // Lote 28 (Rentas R2) no siembra permisos nuevos: devolver usa rental.return (Lote 27, ya propagado), el proceso
        // rental.maintenance (seed original) y dar de baja además inventory.adjust. El Operador de almacén los tiene todos; Facturación
        // y Solo lectura no devuelven ni procesan.
        var operatorTemplate = PermissionCatalog.RoleTemplates["WarehouseOperator"];
        foreach (var code in new[] { PermissionCatalog.RentalView, PermissionCatalog.RentalReturn, PermissionCatalog.RentalMaintenance, PermissionCatalog.InventoryAdjust })
            Assert.Contains(code, operatorTemplate);
        foreach (var role in new[] { "Billing", "ReadOnly", "Dispatcher", "Driver" })
        {
            Assert.DoesNotContain(PermissionCatalog.RentalReturn, PermissionCatalog.RoleTemplates[role]);
            Assert.DoesNotContain(PermissionCatalog.RentalMaintenance, PermissionCatalog.RoleTemplates[role]);
        }
        Assert.Contains(PermissionCatalog.All, p => p.Code == PermissionCatalog.RentalReturn);
        Assert.Contains(PermissionCatalog.All, p => p.Code == PermissionCatalog.RentalMaintenance);
        // Re-correr la propagación con los códigos de R1 no vuelve a dar nada a quien ya los tiene.
        Assert.Empty(PermissionCatalog.CodesToPropagate("WarehouseOperator", Set(PermissionCatalog.RentalExtend, PermissionCatalog.RentalReturn),
            Set(PermissionCatalog.RentalExtend, PermissionCatalog.RentalReturn)));
    }

    [Fact]
    public void Nothing_new_means_nothing_to_propagate()
        => Assert.Empty(PermissionCatalog.CodesToPropagate("TenantAdmin", Set(), Set()));
}
