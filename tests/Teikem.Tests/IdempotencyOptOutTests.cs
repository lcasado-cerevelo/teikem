using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Teikem.Api.Auth;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Idempotencia (2026-10-11): la exclusión ya no es una lista fija de rutas sino el atributo [SkipIdempotency] de cada endpoint. Esta prueba fija el conjunto
/// EXACTO de controladores y acciones excluidos (los que devuelven credenciales en claro): agregar una exclusión nueva, o quitar una, obliga a tocar esta lista
/// a propósito. También comprueba que todo lo que cuelga de las rutas que antes estaban en la lista fija sigue protegido y que ninguna escritura normal quedó excluida.
/// </summary>
public class IdempotencyOptOutTests
{
    private static readonly Assembly Api = typeof(SkipIdempotencyAttribute).Assembly;

    private static IEnumerable<Type> Controllers() => Api.GetTypes().Where(t => !t.IsAbstract && typeof(ControllerBase).IsAssignableFrom(t));

    /// <summary>Plantilla completa de la acción: la del controlador + la de la acción (una plantilla que empieza con «/» ignora la del controlador).</summary>
    private static string FullRoute(Type controller, MethodInfo action)
    {
        var classRoute = controller.GetCustomAttribute<RouteAttribute>()?.Template ?? string.Empty;
        var template = action.GetCustomAttributes().OfType<IRouteTemplateProvider>().Select(a => a.Template).FirstOrDefault(t => !string.IsNullOrEmpty(t));
        if (string.IsNullOrEmpty(template)) return "/" + classRoute.Trim('/');
        return template.StartsWith('/') ? template : "/" + (classRoute.Trim('/') + "/" + template).Trim('/');
    }

    private static bool IsWrite(MethodInfo m) => m.GetCustomAttributes().OfType<HttpMethodAttribute>().Any(a => a.HttpMethods.Any(x => x is "POST" or "PUT" or "PATCH" or "DELETE"));

    [Fact]
    public void The_exact_set_of_opted_out_controllers_and_actions_is_pinned()
    {
        var classes = Controllers().Where(t => t.GetCustomAttribute<SkipIdempotencyAttribute>() is not null).Select(t => t.Name).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        Assert.Equal(new[]
        {
            "AuthController", "DeviceEnrollmentController", "DevicesController", "MeController", "PlatformController", "UserPinsController", "UsersController",
        }, classes);

        var actions = Controllers().SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => m.GetCustomAttribute<SkipIdempotencyAttribute>() is not null).Select(m => $"{t.Name}.{m.Name}"))
            .OrderBy(x => x, StringComparer.Ordinal).ToArray();
        Assert.Equal(new[] { "PortalUsersController.Invite", "PortalUsersController.ResendInvite" }, actions);
    }

    [Fact]
    public void Everything_under_the_old_fixed_prefixes_is_opted_out_and_no_other_write_is()
    {
        string[] oldPrefixes = { "/api/v1/auth", "/api/v1/me", "/api/v1/devices", "/api/v1/platform", "/api/v1/users" };
        var problems = new List<string>();
        foreach (var c in Controllers())
        foreach (var m in c.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Where(IsWrite))
        {
            var route = FullRoute(c, m);
            var underOld = oldPrefixes.Any(p => route.Equals(p, StringComparison.OrdinalIgnoreCase) || route.StartsWith(p + "/", StringComparison.OrdinalIgnoreCase));
            var isInvite = route.EndsWith("/portal-users/invite", StringComparison.OrdinalIgnoreCase) || route.EndsWith("/resend-invite", StringComparison.OrdinalIgnoreCase);
            var optedOut = c.GetCustomAttribute<SkipIdempotencyAttribute>() is not null || m.GetCustomAttribute<SkipIdempotencyAttribute>() is not null;
            if ((underOld || isInvite) && !optedOut) problems.Add($"{c.Name}.{m.Name} ({route}) debería estar excluido");
            if (!(underOld || isInvite) && optedOut) problems.Add($"{c.Name}.{m.Name} ({route}) está excluido y no estaba en la lista de antes");
        }
        Assert.Empty(problems);
    }

    [Fact]
    public void Every_reason_is_documented()
    {
        foreach (var t in Controllers())
        {
            Assert.False(string.IsNullOrWhiteSpace(t.GetCustomAttribute<SkipIdempotencyAttribute>()?.Reason ?? "x"), t.Name);
            foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                Assert.False(string.IsNullOrWhiteSpace(m.GetCustomAttribute<SkipIdempotencyAttribute>()?.Reason ?? "x"), $"{t.Name}.{m.Name}");
        }
    }
}
