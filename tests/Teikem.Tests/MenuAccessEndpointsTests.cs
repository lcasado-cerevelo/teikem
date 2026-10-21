using System.Reflection;
using Microsoft.AspNetCore.Mvc.Routing;
using Teikem.Api.Auth;
using Teikem.Api.Controllers;
using Teikem.Domain.Constants;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 185 — endurecer los endpoints con el permiso de ACCESO del ítem de menú (decidido por Luis, 2026-10-21): cada controlador que sirve a una sola pantalla exige, además del permiso de datos de
/// cada acción, el permiso de acceso de esa pantalla. Para no dejar sin acceso a quien hoy trabaja (aparatos incluidos), todo permiso de ACCIÓN de esos controladores debe implicar el de acceso
/// (MenuPermissions.EditImplies); solo las lecturas (inventory.view…) piden el de acceso por separado, y ver ⇒ leer los datos (ViewImplies).
/// </summary>
public sealed class MenuAccessEndpointsTests
{
    private static readonly (Type Controller, string Access)[] Hardened =
    {
        (typeof(PickBatchesController), "pickbatches.view"),
        (typeof(ManualIssuesController), "pickbatches.view"),
        (typeof(MultiAdjustmentsController), "adjustments.view"),
        (typeof(CrossDockPlansController), "crossdock.view"),
        (typeof(JobsController), "jobs.view"),
    };

    public static IEnumerable<object[]> Rows() => Hardened.Select(h => new object[] { h.Controller, h.Access });

    private static IEnumerable<string> ActionPermissions(Type controller)
        => controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttributes<HttpMethodAttribute>().Any())
            .SelectMany(m => m.GetCustomAttributes<RequirePermissionAttribute>())
            .SelectMany(a => a.Policy[RequirePermissionAttribute.Prefix.Length..].Split(RequirePermissionAttribute.AnyOfSeparator));

    [Theory]
    [MemberData(nameof(Rows))]
    public void The_controller_requires_the_screens_access_permission(Type controller, string access)
        => Assert.Contains(controller.GetCustomAttributes<RequirePermissionAttribute>(), a => a.Policy == RequirePermissionAttribute.Prefix + access);

    [Theory]
    [MemberData(nameof(Rows))]
    public void Every_action_permission_that_is_not_a_plain_read_implies_the_access_permission(Type controller, string access)
    {
        var reads = new HashSet<string> { PermissionCatalog.InventoryView, access };
        var offenders = new List<string>();
        foreach (var perm in ActionPermissions(controller).Distinct().Where(p => !reads.Contains(p)))
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { perm };
            PermissionCatalog.ExpandImplied(set);
            if (!set.Contains(access)) offenders.Add(perm);
        }
        Assert.True(offenders.Count == 0, $"{controller.Name}: estos permisos de acción no implican '{access}' y dejarían sin acceso a quien los tiene: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void Access_implies_the_data_read_so_the_screen_is_not_left_empty()
    {
        foreach (var access in new[] { "pickbatches.view", "adjustments.view", "crossdock.view" })
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { access };
            PermissionCatalog.ExpandImplied(set);
            Assert.Contains(PermissionCatalog.InventoryView, set);
        }
    }
}
