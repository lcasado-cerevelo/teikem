using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Catalogs;
using Teikem.Domain.Constants;
using Teikem.Domain.Entities;
using Teikem.Domain.Identity;
using Teikem.Domain.Security;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 8A (P1) — reglas de negocio de DeviceService y PinService sobre InMemory (WmsFixture): mensajes exactos, alta con
/// código de registro de un solo uso (solo hash), código único por compañía, tema por catálogo, aparato de otra compañía
/// invisible, registro en el aparato (secreto de 32 bytes, código quemado, vencido o sin módulo → 401), desactivar revoca
/// solo las sesiones de ese aparato, lista de usuarios del aparato (activos, con PIN y con inventory.view, por nombre) y PIN
/// propio (contraseña actual obligatoria, formato, reinicio del contador, quitarlo cierra solo las sesiones de aparato).
/// El login con PIN y el heartbeat usan ExecuteUpdate (no soportado por InMemory): los cubre el smoke contra SQL Server.
/// </summary>
public class DeviceServiceTests
{
    private const string Password = "Clave-Segura1";
    private const int ActiveMembershipId = 900;
    private const int SuspendedMembershipId = 901;

    private static async Task<WmsFixture> FixtureAsync()
    {
        var f = await WmsFixture.CreateAsync(s =>
        {
            s.AddIdentityCore<ApplicationUser>().AddRoles<ApplicationRole>().AddEntityFrameworkStores<TeikemDbContext>();
            s.AddSingleton<DeviceService>();
            s.AddSingleton<PinService>();
        });
        // Catálogos del Lote 8A que WmsFixture no siembra (como logistica-db-seed.sql).
        f.Db.LookupCodes.AddRange(
            new LookupCode { LookupCodeId = 900, Entity = LookupDomains.DevicePlatform, InternalCode = DeviceService.DefaultPlatform, LabelJson = "{\"es\":\"Android\"}", IsActive = true },
            new LookupCode { LookupCodeId = 901, Entity = LookupDomains.UiTheme, InternalCode = UiThemes.Light, LabelJson = "{\"es\":\"Claro\"}", IsActive = true },
            new LookupCode { LookupCodeId = 902, Entity = LookupDomains.UiTheme, InternalCode = UiThemes.Dark, LabelJson = "{\"es\":\"Oscuro\"}", IsActive = true });
        var pipeline = f.LookupId(LookupDomains.StageKind, StageKinds.Pipeline);
        f.Db.StatusCodes.AddRange(
            new StatusCode { StatusCodeId = ActiveMembershipId, Entity = StatusDomains.MembershipStatus, InternalCode = MembershipStatuses.Active, LabelJson = "{\"es\":\"Activa\"}", StageKindLookupId = pipeline, SortOrder = 1, IsInitial = true, IsActive = true },
            new StatusCode { StatusCodeId = SuspendedMembershipId, Entity = StatusDomains.MembershipStatus, InternalCode = "SUSPENDED", LabelJson = "{\"es\":\"Suspendida\"}", StageKindLookupId = pipeline, SortOrder = 2, IsActive = true });
        var hasher = new PasswordHasher<ApplicationUser>();
        var me = new ApplicationUser { Id = 1, UserName = "yo@t.local", Email = "yo@t.local", FullName = "Yo Mismo", SecurityStamp = "s1", IsActive = true };
        me.PasswordHash = hasher.HashPassword(me, Password);
        f.Db.Users.Add(me);
        f.Db.UserTenants.Add(new UserTenant { UserId = 1, TenantId = WmsFixture.TenantId, StatusCodeId = ActiveMembershipId, IsDefault = true });
        await f.Db.SaveChangesAsync();
        f.Lookups.Load(f.Db.LookupCodes.IgnoreQueryFilters().AsNoTracking().ToList());
        f.Db.ChangeTracker.Clear();
        return f;
    }

    private static UserDevice Row(WmsFixture f, Guid publicId)
    {
        f.Db.ChangeTracker.Clear();
        return f.Db.Set<UserDevice>().IgnoreQueryFilters().AsNoTracking().Single(d => d.PublicId == publicId);
    }

    private static async Task<(DeviceCreatedDto Created, DeviceEnrolledDto Enrolled)> EnrolledDeviceAsync(WmsFixture f, string code)
    {
        var created = await f.Get<DeviceService>().CreateAsync(new DeviceCreateRequest(code, null, null, null, null), default);
        f.Db.ChangeTracker.Clear();
        var enrolled = await f.Get<DeviceService>().EnrollAsync(new DeviceEnrollRequest(created.EnrollCode, "TC52", "1.0.0"), default);
        f.Db.ChangeTracker.Clear();
        return (created, enrolled);
    }

    private static RefreshToken Token(int userId, int? deviceId, int tenantId = WmsFixture.TenantId) => new()
    {
        UserId = userId, TenantId = tenantId, TokenHash = Guid.NewGuid().ToString("N"), IssuedAtUtc = DateTime.UtcNow,
        ExpiresAtUtc = DateTime.UtcNow.AddDays(30), UserDeviceId = deviceId,
    };

    // ================================================================ mensajes exactos

    [Fact]
    public void Device_and_pin_messages_are_the_exact_texts_of_the_plan()
    {
        Assert.Equal("Ya existe un aparato con ese código.", DeviceService.DuplicateCodeMessage);
        Assert.Equal("El código de registro no es válido o venció.", DeviceService.InvalidEnrollCodeMessage);
        Assert.Equal("El aparato no está registrado o fue desactivado.", DeviceService.InvalidDeviceMessage);
        Assert.Equal("PIN incorrecto.", PinService.WrongPinMessage);
        Assert.Equal("La contraseña actual es incorrecta.", PinService.WrongPasswordMessage);
        var locked = new PinLockedException();
        Assert.Equal("PIN bloqueado por 15 minutos.", locked.Message);
        Assert.Equal(423, locked.StatusCode);
        Assert.Equal(30, DeviceService.DefaultDeviceSessionDays);
        Assert.Equal("did", DeviceClaims.DeviceId);
    }

    // ================================================================ alta

    [Fact]
    public async Task Creating_a_device_returns_a_one_time_enroll_code_and_stores_only_its_hash()
    {
        await using var f = await FixtureAsync();
        var before = DateTime.UtcNow;

        var created = await f.Get<DeviceService>().CreateAsync(new DeviceCreateRequest(" zb-01 ", "Zebra 1", null, null, null), default);

        Assert.Equal("ZB-01", created.Device.Code);
        Assert.Equal("Zebra 1", created.Device.Name);
        Assert.Equal(DeviceService.DefaultPlatform, created.Device.Platform);
        Assert.Equal(UiThemes.Light, created.Device.Theme);
        Assert.False(created.Device.IsEnrolled);
        Assert.True(created.Device.IsActive);
        Assert.Equal(8, created.EnrollCode.Length);
        Assert.Equal(created.EnrollCode, DeviceService.NormalizeEnrollCode(created.EnrollCode));
        Assert.InRange(created.Device.EnrollCodeExpiresUtc!.Value, before.AddHours(24).AddSeconds(-5), DateTime.UtcNow.AddHours(24).AddSeconds(5));

        var row = Row(f, created.Device.PublicId);
        Assert.Equal(WmsFixture.TenantId, row.TenantId);
        Assert.Equal(1, row.RegisteredBy);
        Assert.Equal(DeviceService.Hash(created.EnrollCode), row.EnrollCodeHash);
        Assert.NotEqual(created.EnrollCode, row.EnrollCodeHash);
        Assert.Null(row.SecretHash);
    }

    [Fact]
    public async Task Device_code_is_unique_per_tenant_case_insensitive_and_even_when_inactive()
    {
        await using var f = await FixtureAsync();
        var devices = f.Get<DeviceService>();
        var first = await devices.CreateAsync(new DeviceCreateRequest("ZB-01", null, null, null, null), default);
        f.Db.ChangeTracker.Clear();

        var dup = await Assert.ThrowsAsync<ConflictException>(() => devices.CreateAsync(new DeviceCreateRequest("zb-01", null, null, null, null), default));
        Assert.Equal("Ya existe un aparato con ese código.", dup.Message);
        Assert.Equal(409, dup.StatusCode);

        await devices.DeactivateAsync(first.Device.PublicId, default);
        f.Db.ChangeTracker.Clear();
        await Assert.ThrowsAsync<ConflictException>(() => devices.CreateAsync(new DeviceCreateRequest("ZB-01", null, null, null, null), default));

        // Otra compañía sí puede usar el mismo código.
        using (f.AsTenant(WmsFixture.OtherTenantId))
        {
            var other = await devices.CreateAsync(new DeviceCreateRequest("ZB-01", null, null, null, null), default);
            Assert.Equal("ZB-01", other.Device.Code);
        }
    }

    [Fact]
    public async Task Theme_comes_from_the_ui_theme_catalog()
    {
        await using var f = await FixtureAsync();
        var devices = f.Get<DeviceService>();

        var bad = await Assert.ThrowsAsync<ValidationException>(() => devices.CreateAsync(new DeviceCreateRequest("ZB-01", null, null, null, "BLUE"), default));
        Assert.Equal(new[] { "El tema no es válido; use LIGHT o DARK." }, bad.Errors!["theme"]);

        var dark = await devices.CreateAsync(new DeviceCreateRequest("ZB-02", null, null, null, "dark"), default);
        Assert.Equal(UiThemes.Dark, dark.Device.Theme);
        f.Db.ChangeTracker.Clear();

        var light = await devices.UpdateAsync(dark.Device.PublicId, new DevicePatchRequest(null, null, Theme: "LIGHT"), default);
        Assert.Equal(UiThemes.Light, light.Theme);
    }

    [Fact]
    public async Task Default_warehouse_must_be_active_and_can_be_cleared()
    {
        await using var f = await FixtureAsync();
        var devices = f.Get<DeviceService>();
        var active = await f.AddWarehouseAsync("W1");
        var inactive = await f.AddWarehouseAsync("W2", isActive: false);

        var created = await devices.CreateAsync(new DeviceCreateRequest("ZB-01", null, null, active.PublicId, null), default);
        Assert.Equal(active.PublicId, created.Device.DefaultWarehousePublicId);
        Assert.Equal("W1", created.Device.DefaultWarehouseCode);
        f.Db.ChangeTracker.Clear();

        var ex = await Assert.ThrowsAsync<StatusRuleException>(() => devices.UpdateAsync(created.Device.PublicId, new DevicePatchRequest(null, inactive.PublicId), default));
        Assert.Equal("El almacén por defecto está dado de baja.", ex.Message);
        f.Db.ChangeTracker.Clear();

        var cleared = await devices.UpdateAsync(created.Device.PublicId, new DevicePatchRequest(null, null, ClearDefaultWarehouse: true), default);
        Assert.Null(cleared.DefaultWarehousePublicId);
    }

    [Fact]
    public async Task A_device_of_another_tenant_is_not_found()
    {
        await using var f = await FixtureAsync();
        var devices = f.Get<DeviceService>();
        Guid foreign;
        using (f.AsTenant(WmsFixture.OtherTenantId))
            foreign = (await devices.CreateAsync(new DeviceCreateRequest("ZB-99", null, null, null, null), default)).Device.PublicId;
        f.Db.ChangeTracker.Clear();

        var get = await Assert.ThrowsAsync<NotFoundException>(() => devices.GetAsync(foreign, default));
        Assert.Equal("Aparato no encontrado.", get.Message);
        await Assert.ThrowsAsync<NotFoundException>(() => devices.UpdateAsync(foreign, new DevicePatchRequest("x", null), default));
        await Assert.ThrowsAsync<NotFoundException>(() => devices.DeactivateAsync(foreign, default));
        await Assert.ThrowsAsync<NotFoundException>(() => devices.ReactivateAsync(foreign, default));
        await Assert.ThrowsAsync<NotFoundException>(() => devices.RegenerateEnrollCodeAsync(foreign, default));
        Assert.Empty(await devices.ListAsync(includeInactive: true, default));
        Assert.True(Row(f, foreign).IsActive);
    }

    // ================================================================ registro en el aparato

    [Fact]
    public async Task Enroll_with_a_valid_code_returns_the_secret_once_and_burns_the_code()
    {
        await using var f = await FixtureAsync();
        var devices = f.Get<DeviceService>();
        var created = await devices.CreateAsync(new DeviceCreateRequest("ZB-01", null, null, null, "DARK"), default);
        f.Db.ChangeTracker.Clear();

        // El código se acepta en minúsculas y con guion (como lo teclea el operador).
        var typed = created.EnrollCode[..4].ToLowerInvariant() + "-" + created.EnrollCode[4..];
        var enrolled = await devices.EnrollAsync(new DeviceEnrollRequest(typed, "TC52", "1.0.0"), default);

        Assert.Equal(created.Device.PublicId, enrolled.DevicePublicId);
        Assert.Equal("Tenant de prueba", enrolled.TenantName);
        Assert.Equal(UiThemes.Dark, enrolled.Theme);
        Assert.Equal(43, enrolled.DeviceSecret.Length);   // 32 bytes en base64url sin relleno
        Assert.Equal(32, Convert.FromBase64String(enrolled.DeviceSecret.Replace('-', '+').Replace('_', '/') + "=").Length);

        var row = Row(f, created.Device.PublicId);
        Assert.Equal(DeviceService.Hash(enrolled.DeviceSecret), row.SecretHash);
        Assert.Null(row.EnrollCodeHash);
        Assert.Null(row.EnrollCodeExpiresUtc);
        Assert.NotNull(row.EnrolledAtUtc);
        Assert.Equal("TC52", row.Model);
        Assert.Equal("1.0.0", row.AppVersion);

        // Un solo uso.
        var again = await Assert.ThrowsAsync<UnauthorizedException>(() => devices.EnrollAsync(new DeviceEnrollRequest(created.EnrollCode, null, null), default));
        Assert.Equal("El código de registro no es válido o venció.", again.Message);
        Assert.Equal(401, again.StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("XYZ")]
    [InlineData("ABCDEFGH")]   // bien formado pero inexistente
    public async Task Enroll_with_a_malformed_or_unknown_code_is_401(string? code)
    {
        await using var f = await FixtureAsync();
        await f.Get<DeviceService>().CreateAsync(new DeviceCreateRequest("ZB-01", null, null, null, null), default);
        f.Db.ChangeTracker.Clear();
        var ex = await Assert.ThrowsAsync<UnauthorizedException>(() => f.Get<DeviceService>().EnrollAsync(new DeviceEnrollRequest(code!, null, null), default));
        Assert.Equal(DeviceService.InvalidEnrollCodeMessage, ex.Message);
    }

    [Fact]
    public async Task Enroll_with_an_expired_code_is_401()
    {
        await using var f = await FixtureAsync();
        var created = await f.Get<DeviceService>().CreateAsync(new DeviceCreateRequest("ZB-01", null, null, null, null), default);
        f.Db.ChangeTracker.Clear();
        var row = f.Db.Set<UserDevice>().AsTracking().Single();
        row.EnrollCodeExpiresUtc = DateTime.UtcNow.AddMinutes(-1);
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();

        var ex = await Assert.ThrowsAsync<UnauthorizedException>(() => f.Get<DeviceService>().EnrollAsync(new DeviceEnrollRequest(created.EnrollCode, null, null), default));
        Assert.Equal(DeviceService.InvalidEnrollCodeMessage, ex.Message);
        Assert.Null(Row(f, created.Device.PublicId).SecretHash);
    }

    [Fact]
    public async Task Enroll_is_401_when_the_tenant_has_the_wms_module_off()
    {
        await using var f = await FixtureAsync();
        var created = await f.Get<DeviceService>().CreateAsync(new DeviceCreateRequest("ZB-01", null, null, null, null), default);
        f.Db.ChangeTracker.Clear();
        f.SetModules(ModuleKeys.Purchasing);
        var ex = await Assert.ThrowsAsync<UnauthorizedException>(() => f.Get<DeviceService>().EnrollAsync(new DeviceEnrollRequest(created.EnrollCode, null, null), default));
        Assert.Equal(DeviceService.InvalidEnrollCodeMessage, ex.Message);
    }

    // ================================================================ desactivar / reactivar / código nuevo

    [Fact]
    public async Task Deactivating_revokes_only_the_sessions_of_that_device_and_blocks_new_enroll_codes()
    {
        await using var f = await FixtureAsync();
        var devices = f.Get<DeviceService>();
        var a = await devices.CreateAsync(new DeviceCreateRequest("ZB-01", null, null, null, null), default);
        var b = await devices.CreateAsync(new DeviceCreateRequest("ZB-02", null, null, null, null), default);
        f.Db.ChangeTracker.Clear();
        var aId = Row(f, a.Device.PublicId).UserDeviceId;
        var bId = Row(f, b.Device.PublicId).UserDeviceId;
        f.Db.RefreshTokens.AddRange(Token(1, aId), Token(1, bId), Token(1, null));
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();

        var off = await devices.DeactivateAsync(a.Device.PublicId, default);
        Assert.False(off.IsActive);
        Assert.Null(off.EnrollCodeExpiresUtc);
        Assert.Null(Row(f, a.Device.PublicId).EnrollCodeHash);
        var tokens = f.Db.RefreshTokens.IgnoreQueryFilters().AsNoTracking().ToList();
        Assert.NotNull(tokens.Single(t => t.UserDeviceId == aId).RevokedAtUtc);
        Assert.Null(tokens.Single(t => t.UserDeviceId == bId).RevokedAtUtc);
        Assert.Null(tokens.Single(t => t.UserDeviceId == null).RevokedAtUtc);

        var regen = await Assert.ThrowsAsync<StatusRuleException>(() => devices.RegenerateEnrollCodeAsync(a.Device.PublicId, default));
        Assert.Equal(DeviceService.InactiveDeviceMessage, regen.Message);
        Assert.Equal(422, regen.StatusCode);
        f.Db.ChangeTracker.Clear();

        // Reactivar no revive sesiones; el código nuevo sí se genera (y el anterior deja de servir).
        var on = await devices.ReactivateAsync(a.Device.PublicId, default);
        Assert.True(on.IsActive);
        Assert.NotNull(f.Db.RefreshTokens.IgnoreQueryFilters().AsNoTracking().Single(t => t.UserDeviceId == aId).RevokedAtUtc);
        f.Db.ChangeTracker.Clear();
        var fresh = await devices.RegenerateEnrollCodeAsync(a.Device.PublicId, default);
        Assert.Equal(8, fresh.EnrollCode.Length);
        Assert.Equal(DeviceService.Hash(fresh.EnrollCode), Row(f, a.Device.PublicId).EnrollCodeHash);
        f.Db.ChangeTracker.Clear();
        await Assert.ThrowsAsync<UnauthorizedException>(() => devices.EnrollAsync(new DeviceEnrollRequest(a.EnrollCode, null, null), default));
    }

    // ================================================================ usuarios del aparato

    [Fact]
    public async Task Device_users_are_active_members_with_pin_and_inventory_view_sorted_by_name()
    {
        await using var f = await FixtureAsync();
        var (_, enrolled) = await EnrolledDeviceAsync(f, "ZB-01");

        void User(int id, string name, int? membership = ActiveMembershipId, bool pin = true, bool active = true, bool platform = false,
            int tenantId = WmsFixture.TenantId, string[]? perms = null)
        {
            f.Db.Users.Add(new ApplicationUser { Id = id, UserName = $"u{id}@t.local", Email = $"u{id}@t.local", FullName = name, SecurityStamp = $"s{id}", IsActive = active, IsPlatformAdmin = platform });
            if (membership is int m) f.Db.UserTenants.Add(new UserTenant { UserId = id, TenantId = tenantId, StatusCodeId = m, IsDefault = true });
            if (pin) f.Db.Set<UserPin>().Add(new UserPin { TenantId = tenantId, UserId = id, PinHash = "hash" });
            f.Get<IMemoryCache>().Set($"perms:{id}:{tenantId}", new HashSet<string>(perms ?? new[] { PermissionCatalog.InventoryView }, StringComparer.OrdinalIgnoreCase));
        }
        User(10, "Zoe Ruiz");
        User(11, "  ana pérez ");
        User(12, "Sin Pin", pin: false);
        User(13, "Sin Permiso", perms: new[] { PermissionCatalog.OrdersView });
        User(14, "Inactivo", active: false);
        User(15, "Plataforma", platform: true);
        User(16, "Otra Compañía", tenantId: WmsFixture.OtherTenantId);
        User(17, "Suspendido", membership: SuspendedMembershipId);
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();

        var list = await f.Get<DeviceService>().GetDeviceUsersAsync(new DeviceUsersRequest(enrolled.DevicePublicId, enrolled.DeviceSecret), default);

        Assert.Equal(new[] { "ana pérez", "Zoe Ruiz" }, list.Select(u => u.FullName));
        Assert.Equal(new[] { 11, 10 }, list.Select(u => u.UserId));
        Assert.Equal(new[] { "AP", "ZR" }, list.Select(u => u.Initials));
    }

    [Fact]
    public async Task Device_users_with_a_wrong_secret_or_a_deactivated_device_is_401()
    {
        await using var f = await FixtureAsync();
        var devices = f.Get<DeviceService>();
        var (created, enrolled) = await EnrolledDeviceAsync(f, "ZB-01");

        var wrong = await Assert.ThrowsAsync<UnauthorizedException>(() => devices.GetDeviceUsersAsync(new DeviceUsersRequest(enrolled.DevicePublicId, "otro-secreto"), default));
        Assert.Equal("El aparato no está registrado o fue desactivado.", wrong.Message);
        await Assert.ThrowsAsync<UnauthorizedException>(() => devices.GetDeviceUsersAsync(new DeviceUsersRequest(Guid.NewGuid(), enrolled.DeviceSecret), default));
        await Assert.ThrowsAsync<UnauthorizedException>(() => devices.GetDeviceUsersAsync(new DeviceUsersRequest(enrolled.DevicePublicId, ""), default));
        // Heartbeat con secreto inválido: 401 antes de tocar el aparato.
        var hb = await Assert.ThrowsAsync<UnauthorizedException>(() => devices.HeartbeatAsync(new HeartbeatRequest(enrolled.DevicePublicId, "otro", null), default));
        Assert.Equal(DeviceService.InvalidDeviceMessage, hb.Message);

        await devices.DeactivateAsync(created.Device.PublicId, default);
        f.Db.ChangeTracker.Clear();
        var off = await Assert.ThrowsAsync<UnauthorizedException>(() => devices.GetDeviceUsersAsync(new DeviceUsersRequest(enrolled.DevicePublicId, enrolled.DeviceSecret), default));
        Assert.Equal(DeviceService.InvalidDeviceMessage, off.Message);
    }

    // ================================================================ PIN propio (Mi cuenta)

    [Fact]
    public async Task Setting_my_pin_requires_the_current_password()
    {
        await using var f = await FixtureAsync();
        var ex = await Assert.ThrowsAsync<ValidationException>(() => f.Get<PinService>().SetMineAsync(new PinSetRequest("equivocada", "4826"), default));
        Assert.Equal("La contraseña actual es incorrecta.", ex.Message);
        Assert.Equal(400, ex.StatusCode);
        Assert.Equal(new[] { "La contraseña actual es incorrecta." }, ex.Errors!["currentPassword"]);
        Assert.Empty(f.Db.Set<UserPin>().AsNoTracking().ToList());
    }

    [Theory]
    [InlineData("12a4", PinRules.FormatMessage)]
    [InlineData("123", PinRules.FormatMessage)]
    [InlineData("1234567", PinRules.FormatMessage)]
    [InlineData("", PinRules.FormatMessage)]
    [InlineData("1234", PinRules.TrivialMessage)]
    [InlineData("0000", PinRules.TrivialMessage)]
    public async Task My_pin_must_have_a_valid_format(string pin, string expected)
    {
        await using var f = await FixtureAsync();
        var ex = await Assert.ThrowsAsync<ValidationException>(() => f.Get<PinService>().SetMineAsync(new PinSetRequest(Password, pin), default));
        Assert.Equal(new[] { expected }, ex.Errors!["pin"]);
        Assert.Empty(f.Db.Set<UserPin>().AsNoTracking().ToList());
    }

    [Fact]
    public async Task Setting_my_pin_stores_only_the_hash_and_resets_the_counter_and_lock()
    {
        await using var f = await FixtureAsync();
        f.Db.Set<UserPin>().Add(new UserPin { TenantId = WmsFixture.TenantId, UserId = 1, PinHash = "viejo", FailedCount = 4, LockedUntilUtc = DateTime.UtcNow.AddMinutes(10) });
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();

        var status = await f.Get<PinService>().SetMineAsync(new PinSetRequest(Password, "4826"), default);

        Assert.True(status.HasPin);
        Assert.Null(status.LockedUntilUtc);
        f.Db.ChangeTracker.Clear();
        var pin = Assert.Single(f.Db.Set<UserPin>().AsNoTracking().ToList());
        Assert.Equal(0, pin.FailedCount);
        Assert.Null(pin.LockedUntilUtc);
        Assert.NotEqual("4826", pin.PinHash);
        var user = f.Db.Users.AsNoTracking().Single(u => u.Id == 1);
        Assert.NotEqual(PasswordVerificationResult.Failed, new PasswordHasher<ApplicationUser>().VerifyHashedPassword(user, pin.PinHash, "4826"));
    }

    [Fact]
    public async Task Removing_my_pin_closes_only_my_device_sessions()
    {
        await using var f = await FixtureAsync();
        var created = await f.Get<DeviceService>().CreateAsync(new DeviceCreateRequest("ZB-01", null, null, null, null), default);
        f.Db.ChangeTracker.Clear();
        var deviceId = Row(f, created.Device.PublicId).UserDeviceId;
        f.Db.Set<UserPin>().Add(new UserPin { TenantId = WmsFixture.TenantId, UserId = 1, PinHash = "hash" });
        f.Db.Users.Add(new ApplicationUser { Id = 2, UserName = "u2@t.local", Email = "u2@t.local", SecurityStamp = "s2", IsActive = true });
        f.Db.RefreshTokens.AddRange(Token(1, deviceId), Token(1, null), Token(2, deviceId));
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();

        await f.Get<PinService>().RemoveMineAsync(default);

        f.Db.ChangeTracker.Clear();
        Assert.Empty(f.Db.Set<UserPin>().AsNoTracking().ToList());
        var tokens = f.Db.RefreshTokens.IgnoreQueryFilters().AsNoTracking().ToList();
        Assert.NotNull(tokens.Single(t => t.UserId == 1 && t.UserDeviceId == deviceId).RevokedAtUtc);
        Assert.Null(tokens.Single(t => t.UserId == 1 && t.UserDeviceId == null).RevokedAtUtc);   // la sesión web sigue
        Assert.Null(tokens.Single(t => t.UserId == 2).RevokedAtUtc);                             // otro usuario no se toca
        Assert.False((await f.Get<PinService>().GetMineAsync(default)).HasPin);
    }
}
