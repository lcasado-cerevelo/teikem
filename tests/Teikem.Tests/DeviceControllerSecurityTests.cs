using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Api.Auth;
using Teikem.Api.Controllers;
using Teikem.Domain.Constants;
using Teikem.Domain.Identity;
using Teikem.Domain.Security;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 8A — seguridad de los controladores de aparatos y PIN fijada por reflexión (patrón de WmsControllerSecurityTests):
/// módulo WMS_LOTSERIAL, devices.manage en la administración de aparatos, anónimos solo los del aparato (enroll, heartbeat,
/// device/users, device/login) y AAL2 para asignar el PIN de otro. El permiso del PIN de otros vive en PinService y se
/// prueba aquí sobre InMemory (403 sin devices.manage ni admin.users; anti-escalada 403/404). Más la normalización del
/// código de registro.
/// </summary>
public class DeviceControllerSecurityTests
{
    private static List<MethodInfo> Actions(Type controller)
        => controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttributes<HttpMethodAttribute>().Any()).ToList();

    private static List<string> Permissions(MethodInfo action)
        => action.GetCustomAttributes<RequirePermissionAttribute>().Select(a => a.Policy!).ToList();

    private static List<string> Modules(MemberInfo m)
        => m.GetCustomAttributes<RequireModuleAttribute>(inherit: true).Select(a => a.ModuleKey).ToList();

    [Fact]
    public void Devices_controller_requires_the_wms_module_and_devices_manage_on_its_seven_actions()
    {
        var t = typeof(DevicesController);
        Assert.Equal(new[] { ModuleKeys.WmsLotSerial }, Modules(t));
        Assert.Equal("api/v1/devices", t.GetCustomAttribute<RouteAttribute>()!.Template);
        Assert.NotNull(t.GetCustomAttribute<AuthorizeAttribute>());
        var actions = Actions(t);
        Assert.Equal(7, actions.Count);
        Assert.Equal(new[] { "Create", "Deactivate", "Get", "List", "Reactivate", "RegenerateEnrollCode", "Update" }, actions.Select(a => a.Name).OrderBy(n => n));
        foreach (var a in actions)
        {
            Assert.Equal(new[] { RequirePermissionAttribute.Prefix + PermissionCatalog.DevicesManage }, Permissions(a));
            Assert.Null(a.GetCustomAttribute<AllowAnonymousAttribute>());
        }
    }

    [Fact]
    public void Device_enrollment_endpoints_are_anonymous_and_without_permissions()
    {
        var t = typeof(DeviceEnrollmentController);
        Assert.Equal("api/v1/devices", t.GetCustomAttribute<RouteAttribute>()!.Template);
        var actions = Actions(t);
        Assert.Equal(new[] { "Enroll", "Heartbeat" }, actions.Select(a => a.Name).OrderBy(n => n));
        foreach (var a in actions)
        {
            Assert.NotNull(a.GetCustomAttribute<AllowAnonymousAttribute>());
            Assert.Empty(Permissions(a));
        }
    }

    [Fact]
    public void Anonymous_device_endpoints_are_rate_limited()
    {
        string? Policy(Type t, string name) => t.GetMethod(name)!.GetCustomAttribute<EnableRateLimitingAttribute>()?.PolicyName;
        Assert.Equal(DeviceRateLimits.Enroll, Policy(typeof(DeviceEnrollmentController), nameof(DeviceEnrollmentController.Enroll)));
        Assert.Equal(DeviceRateLimits.DeviceAuth, Policy(typeof(DeviceEnrollmentController), nameof(DeviceEnrollmentController.Heartbeat)));
        Assert.Equal(DeviceRateLimits.DeviceAuth, Policy(typeof(AuthController), nameof(AuthController.DeviceUsers)));
        Assert.Equal(DeviceRateLimits.DeviceAuth, Policy(typeof(AuthController), nameof(AuthController.DeviceLogin)));
    }

    [Fact]
    public void Enroll_code_lives_24_hours() => Assert.Equal(TimeSpan.FromHours(24), DeviceService.EnrollCodeLifetime);

    [Fact]
    public void Device_users_and_device_login_are_anonymous()
    {
        foreach (var name in new[] { nameof(AuthController.DeviceUsers), nameof(AuthController.DeviceLogin) })
        {
            var a = typeof(AuthController).GetMethod(name)!;
            Assert.NotNull(a.GetCustomAttribute<AllowAnonymousAttribute>());
            Assert.Empty(Permissions(a));
        }
        Assert.Equal("device/users", typeof(AuthController).GetMethod(nameof(AuthController.DeviceUsers))!.GetCustomAttribute<HttpPostAttribute>()!.Template);
        Assert.Equal("device/login", typeof(AuthController).GetMethod(nameof(AuthController.DeviceLogin))!.GetCustomAttribute<HttpPostAttribute>()!.Template);
    }

    [Fact]
    public void User_pins_controller_is_authorized_under_the_wms_module_and_setting_a_pin_requires_aal2()
    {
        var t = typeof(UserPinsController);
        Assert.Equal(new[] { ModuleKeys.WmsLotSerial }, Modules(t));
        Assert.Equal("api/v1/users", t.GetCustomAttribute<RouteAttribute>()!.Template);
        Assert.NotNull(t.GetCustomAttribute<AuthorizeAttribute>());
        var actions = Actions(t);
        Assert.Equal(new[] { "RemovePin", "SetPin" }, actions.Select(a => a.Name).OrderBy(n => n));
        // El permiso (devices.manage o admin.users) lo valida PinService: ver las pruebas de servicio de abajo.
        Assert.All(actions, a => Assert.Empty(Permissions(a)));
        var set = t.GetMethod(nameof(UserPinsController.SetPin))!;
        Assert.Equal("{id:int}/pin", set.GetCustomAttribute<HttpPutAttribute>()!.Template);
        Assert.NotNull(set.GetCustomAttribute<RequireAal2Attribute>());
        Assert.Equal("{id:int}/pin", t.GetMethod(nameof(UserPinsController.RemovePin))!.GetCustomAttribute<HttpDeleteAttribute>()!.Template);
    }

    [Fact]
    public void My_pin_endpoints_require_the_wms_module()
    {
        foreach (var name in new[] { nameof(MeController.GetPin), nameof(MeController.SetPin), nameof(MeController.RemovePin) })
            Assert.Equal(new[] { ModuleKeys.WmsLotSerial }, Modules(typeof(MeController).GetMethod(name)!));
    }

    // ================================================================ código de registro

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("ABCD234", null)]          // 7 caracteres
    [InlineData("ABCD23456", null)]        // 9 caracteres
    [InlineData("ABCD2340", null)]         // 0 fuera del alfabeto
    [InlineData("ABCDO234", null)]         // O fuera del alfabeto
    [InlineData("abcd2345", "ABCD2345")]
    [InlineData(" abcd-2345 ", "ABCD2345")]
    [InlineData("ab cd 23 45", "ABCD2345")]
    public void Enroll_code_is_normalized_or_rejected(string? raw, string? expected)
        => Assert.Equal(expected, DeviceService.NormalizeEnrollCode(raw));

    // ================================================================ PIN de otros (PinService, InMemory)

    private const int CallerId = 1;   // WmsFixture: el contexto corre como el usuario 1
    private const int TargetId = 2;

    private static async Task<WmsFixture> PinFixtureAsync(bool targetIsPlatformAdmin = false)
    {
        var f = await WmsFixture.CreateAsync(s =>
        {
            s.AddIdentityCore<ApplicationUser>().AddRoles<ApplicationRole>().AddEntityFrameworkStores<TeikemDbContext>();
            s.AddSingleton<PinService>();
        });
        f.Db.Users.AddRange(
            new ApplicationUser { Id = CallerId, UserName = "caller@t.local", Email = "caller@t.local", FullName = "Quien llama", SecurityStamp = "s1" },
            new ApplicationUser { Id = TargetId, UserName = "target@t.local", Email = "target@t.local", FullName = "Destino", SecurityStamp = "s2", IsPlatformAdmin = targetIsPlatformAdmin });
        f.Db.UserTenants.AddRange(
            new UserTenant { UserId = CallerId, TenantId = WmsFixture.TenantId, StatusCodeId = 1, IsDefault = true },
            new UserTenant { UserId = TargetId, TenantId = WmsFixture.TenantId, StatusCodeId = 1, IsDefault = true });
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        return f;
    }

    private static void TargetPermissions(WmsFixture f, params string[] codes)
        => f.Get<IMemoryCache>().Set($"perms:{TargetId}:{WmsFixture.TenantId}", new HashSet<string>(codes, StringComparer.OrdinalIgnoreCase));

    [Fact]
    public async Task Setting_another_users_pin_without_devices_manage_or_admin_users_is_403()
    {
        await using var f = await PinFixtureAsync();
        f.SetPermissions(PermissionCatalog.InventoryView);
        var ex = await Assert.ThrowsAsync<ForbiddenException>(() => f.Get<PinService>().SetForUserAsync(TargetId, new PinAdminSetRequest("4826"), default));
        Assert.Equal("Falta el permiso 'devices.manage' o 'admin.users'.", ex.Message);
        await Assert.ThrowsAsync<ForbiddenException>(() => f.Get<PinService>().RemoveForUserAsync(TargetId, default));
    }

    [Fact]
    public async Task A_user_with_more_permissions_than_the_caller_cannot_get_a_pin_imposed()
    {
        await using var f = await PinFixtureAsync();
        f.SetPermissions(PermissionCatalog.DevicesManage, PermissionCatalog.InventoryView);
        TargetPermissions(f, PermissionCatalog.InventoryView, PermissionCatalog.AdminUsers);
        var pins = f.Get<PinService>();

        var set = await Assert.ThrowsAsync<ForbiddenException>(() => pins.SetForUserAsync(TargetId, new PinAdminSetRequest("4826"), default));
        Assert.Equal(PinService.HigherPrivilegesMessage, set.Message);
        var remove = await Assert.ThrowsAsync<ForbiddenException>(() => pins.RemoveForUserAsync(TargetId, default));
        Assert.Equal(PinService.HigherPrivilegesMessage, remove.Message);
        Assert.Empty(f.Db.Set<Teikem.Domain.Entities.UserPin>().ToList());
    }

    [Fact]
    public async Task A_user_whose_permissions_are_a_subset_of_the_callers_gets_the_pin()
    {
        await using var f = await PinFixtureAsync();
        f.SetPermissions(PermissionCatalog.DevicesManage, PermissionCatalog.InventoryView, PermissionCatalog.WarehouseCount);
        TargetPermissions(f, PermissionCatalog.InventoryView, PermissionCatalog.WarehouseCount);

        var status = await f.Get<PinService>().SetForUserAsync(TargetId, new PinAdminSetRequest("4826"), default);

        Assert.True(status.HasPin);
        var pin = Assert.Single(f.Db.Set<Teikem.Domain.Entities.UserPin>().ToList());
        Assert.Equal(TargetId, pin.UserId);
        Assert.NotEqual("4826", pin.PinHash);   // solo el hash
    }

    [Fact]
    public async Task The_platform_admin_is_invisible_to_a_tenant_admin_setting_pins()
    {
        await using var f = await PinFixtureAsync(targetIsPlatformAdmin: true);
        f.SetPermissions(PermissionCatalog.All.Select(p => p.Code).ToArray());
        TargetPermissions(f, PermissionCatalog.InventoryView);
        await Assert.ThrowsAsync<NotFoundException>(() => f.Get<PinService>().SetForUserAsync(TargetId, new PinAdminSetRequest("4826"), default));
        await Assert.ThrowsAsync<NotFoundException>(() => f.Get<PinService>().RemoveForUserAsync(TargetId, default));
    }

    [Fact]
    public async Task Admin_users_without_devices_manage_can_set_and_remove_another_users_pin()
    {
        // Vía alternativa del plan (P1): admin.users basta para el PIN de otro, sin devices.manage.
        await using var f = await PinFixtureAsync();
        f.SetPermissions(PermissionCatalog.AdminUsers, PermissionCatalog.InventoryView);
        TargetPermissions(f, PermissionCatalog.InventoryView);
        var pins = f.Get<PinService>();

        var status = await pins.SetForUserAsync(TargetId, new PinAdminSetRequest("4826"), default);
        Assert.True(status.HasPin);
        Assert.Single(f.Db.Set<Teikem.Domain.Entities.UserPin>().ToList());

        await pins.RemoveForUserAsync(TargetId, default);
        f.Db.ChangeTracker.Clear();
        Assert.Empty(f.Db.Set<Teikem.Domain.Entities.UserPin>().ToList());
    }

    [Theory]
    [InlineData("luis casado prado", null, "LC")]
    [InlineData("  ana   ", null, "A")]
    [InlineData(null, "zeta@x.com", "Z")]
    [InlineData("   ", "bob@x.com", "B")]
    [InlineData(null, null, "?")]
    [InlineData("", "", "?")]
    public void Device_user_initials(string? fullName, string? email, string expected)
        => Assert.Equal(expected, DeviceService.Initials(fullName, email));

    [Theory]
    [InlineData("", DeviceService.CodeRequiredMessage)]
    [InlineData("   ", DeviceService.CodeRequiredMessage)]
    [InlineData(null, DeviceService.CodeRequiredMessage)]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", DeviceService.CodeTooLongMessage)]   // 31 caracteres
    public async Task Device_code_is_required_and_up_to_30_characters(string? code, string expected)
    {
        await using var f = await WmsFixture.CreateAsync(s => s.AddSingleton<DeviceService>());
        f.SetPermissions(PermissionCatalog.DevicesManage);
        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            f.Get<DeviceService>().CreateAsync(new DeviceCreateRequest(code, null, null, null, null), default));
        Assert.Equal(new[] { expected }, ex.Errors!["code"]);
    }
}
