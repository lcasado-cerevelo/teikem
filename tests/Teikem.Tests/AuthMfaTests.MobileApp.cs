using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Constants;
using Teikem.Domain.Security;
using Teikem.Domain.Tenancy;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 213 — la app del chofer crece para el personal: entra quien tiene mobile.use (o es chofer activo) en alguna compañía; la sesión trae, por compañía, los permisos efectivos y si es chofer,
/// y dice si el usuario es «solo chofer» (la app le muestra únicamente lo del chofer).
/// </summary>
public partial class AuthMfaTests
{
    [Fact]
    public async Task Staff_with_mobile_use_enters_the_mobile_app_without_being_a_driver_and_the_session_carries_permissions()
    {
        await using var f = await FixtureAsync();
        f.SetPermissions(PermissionCatalog.MobileUse, PermissionCatalog.OrdersView, PermissionCatalog.PulseAttention);   // personal de oficina: no es chofer en ninguna compañía

        var r = await f.Get<AuthService>().LoginAsync(new LoginRequest("yo@t.local", Password, null, null), default, driverApp: true);
        Assert.Equal("mfa_required", r.Status);   // la compañía exige MFA por omisión: pasó la puerta de «¿puede usar la app?»

        var session = await f.Get<AuthService>().DriverCompanyTokensAsync(await DriverSessionAsync(f, DateTime.UtcNow), default);
        var company = Assert.Single(session.Companies);
        Assert.Equal(WmsFixture.TenantId, company.TenantId);
        Assert.NotNull(company.AccessToken);
        Assert.False(company.IsDriver);
        Assert.Equal(new[] { PermissionCatalog.MobileUse, PermissionCatalog.OrdersView, PermissionCatalog.PulseAttention }, company.Permissions);
        Assert.Equal(UserKinds.Internal, session.UserKind);
        Assert.False(session.DriverOnly);
    }

    [Fact]
    public async Task Without_mobile_use_and_without_being_a_driver_the_mobile_app_rejects_the_login()
    {
        await using var f = await FixtureAsync();
        f.SetPermissions(PermissionCatalog.OrdersView);
        var ex = await Assert.ThrowsAsync<ForbiddenException>(() => f.Get<AuthService>().LoginAsync(new LoginRequest("yo@t.local", Password, null, null), default, driverApp: true));
        Assert.Equal(DriverAuthMessages.NoMobileAccessMessage, ex.Message);
    }

    [Fact]
    public async Task A_driver_only_user_is_marked_as_such_in_the_mobile_session_and_a_driver_enters_even_without_the_new_permission()
    {
        await using var f = await FixtureAsync();
        await SetKindAsync(f, UserKinds.Driver);
        await MakeDriverAsync(f, WmsFixture.TenantId);
        f.SetPermissions(PermissionCatalog.DriverAppUse);   // rol de chofer anterior al permiso mobile.use

        var r = await f.Get<AuthService>().LoginAsync(new LoginRequest("yo@t.local", Password, null, null), default, driverApp: true);
        Assert.Equal("mfa_required", r.Status);
        var session = await f.Get<AuthService>().DriverCompanyTokensAsync(await DriverSessionAsync(f, DateTime.UtcNow), default);
        Assert.True(session.DriverOnly);
        Assert.Equal(UserKinds.Driver, session.UserKind);
        Assert.True(Assert.Single(session.Companies).IsDriver);
    }
}
