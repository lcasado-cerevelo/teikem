using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Teikem.Api.Auth;
using Teikem.Domain.Constants;
using Teikem.Domain.Security;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 8A — permisos efectivos calculados desde los datos de rol (sin escribir la caché 'perms:'), para fijar la decisión 3
/// (warehouse.count ⇒ warehouse.count.capture en GetEffectivePermissionsAsync), y la política "cualquiera de" (a|b) de
/// PermissionHandler que usan PUT/DELETE /users/{id}/pin (devices.manage|admin.users).
/// </summary>
public class PermissionServiceImpliedTests
{
    private static async Task<WmsFixture> RoleFixtureAsync(params string[] roleCodes)
    {
        var f = await WmsFixture.CreateAsync();
        var perms = roleCodes.Select(c => new Permission { Code = c, CategoryLookupId = 1, LabelJson = "{}" }).ToList();
        f.Db.Permissions.AddRange(perms);
        var role = new Role { TenantId = WmsFixture.TenantId, Name = "Conteo propio", IsActive = true };
        foreach (var p in perms) role.Permissions.Add(new RolePermission { Permission = p });
        f.Db.AppRoles.Add(role);
        await f.Db.SaveChangesAsync();
        f.Db.AppUserRoles.Add(new UserRole { UserId = 1, TenantId = WmsFixture.TenantId, RoleId = role.RoleId });
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        f.Tenant.IsPlatformAdmin = false;   // sin SetPermissions: los permisos salen de los datos del rol
        return f;
    }

    [Fact]
    public async Task A_role_with_warehouse_count_gets_capture_in_its_effective_permissions()
    {
        await using var f = await RoleFixtureAsync(PermissionCatalog.InventoryView, PermissionCatalog.WarehouseCount);
        var service = f.Get<PermissionService>();

        var set = await service.GetEffectivePermissionsAsync(1, WmsFixture.TenantId, default);
        Assert.Contains(PermissionCatalog.WarehouseCountCapture, set);
        Assert.True(await service.HasPermissionAsync(PermissionCatalog.WarehouseCountCapture, default));
        Assert.True(await service.HasPermissionAsync(PermissionCatalog.WarehouseCount, default));
    }

    [Fact]
    public async Task A_role_with_only_capture_does_not_get_warehouse_count()
    {
        await using var f = await RoleFixtureAsync(PermissionCatalog.InventoryView, PermissionCatalog.WarehouseCountCapture);
        var service = f.Get<PermissionService>();

        var set = await service.GetEffectivePermissionsAsync(1, WmsFixture.TenantId, default);
        Assert.Contains(PermissionCatalog.WarehouseCountCapture, set);
        Assert.DoesNotContain(PermissionCatalog.WarehouseCount, set);
        Assert.False(await service.HasPermissionAsync(PermissionCatalog.WarehouseCount, default));
    }

    // ================================================================ PermissionHandler "a|b"

    private static async Task<bool> HandlerSucceedsAsync(WmsFixture f, string permission)
    {
        var handler = new PermissionHandler(f.Get<PermissionService>(), f.Get<ITenantContext>(), f.Get<ISecurityEventWriter>());
        var requirement = new PermissionRequirement(permission);
        var context = new AuthorizationHandlerContext(new[] { requirement }, new ClaimsPrincipal(), null);
        await handler.HandleAsync(context);
        return context.HasSucceeded;
    }

    private const string PinOfOthers = PermissionCatalog.DevicesManage + "|" + PermissionCatalog.AdminUsers;

    [Fact]
    public async Task Any_of_policy_succeeds_with_only_the_second_permission()
    {
        await using var f = await WmsFixture.CreateAsync();
        f.SetPermissions(PermissionCatalog.AdminUsers);
        Assert.True(await HandlerSucceedsAsync(f, PinOfOthers));
    }

    [Fact]
    public async Task Any_of_policy_succeeds_with_only_the_first_permission()
    {
        await using var f = await WmsFixture.CreateAsync();
        f.SetPermissions(PermissionCatalog.DevicesManage);
        Assert.True(await HandlerSucceedsAsync(f, PinOfOthers));
    }

    [Fact]
    public async Task Any_of_policy_fails_without_any_of_the_permissions()
    {
        await using var f = await WmsFixture.CreateAsync();
        f.SetPermissions(PermissionCatalog.InventoryView);
        Assert.False(await HandlerSucceedsAsync(f, PinOfOthers));
    }
}
