using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Catalogs;
using Teikem.Domain.Constants;
using Teikem.Domain.Entities;
using Teikem.Domain.Identity;
using Teikem.Domain.Security;
using Teikem.Domain.Tenancy;
using Teikem.Infrastructure.Abstractions;
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

    private static async Task<WmsFixture> FixtureAsync(Action<IServiceCollection>? configure = null)
    {
        var f = await WmsFixture.CreateAsync(s =>
        {
            s.AddIdentityCore<ApplicationUser>().AddRoles<ApplicationRole>().AddEntityFrameworkStores<TeikemDbContext>();
            s.AddSingleton<DeviceService>();
            s.AddSingleton<PinService>();
            configure?.Invoke(s);
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

    [Fact]
    public async Task Enroll_reports_the_receiving_mode_of_the_default_warehouse()
    {
        // Lote 16: el registro (y el heartbeat, por la misma costura PreferencesAsync) trae el modo del almacén por defecto.
        await using var f = await FixtureAsync();
        var w = await f.AddWarehouseAsync("W1");
        var row = await f.Db.Warehouses.SingleAsync(x => x.WarehouseId == w.WarehouseId);
        row.ReceivingModeLookupId = f.LookupId(LookupDomains.ReceivingMode, ReceivingModes.Direct);
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();

        var withWarehouse = await f.Get<DeviceService>().CreateAsync(new DeviceCreateRequest("AP-16", null, null, w.PublicId, null), default);
        f.Db.ChangeTracker.Clear();
        var enrolled = await f.Get<DeviceService>().EnrollAsync(new DeviceEnrollRequest(withWarehouse.EnrollCode, "TC52", "1.0.0"), default);
        Assert.Equal(w.PublicId, enrolled.DefaultWarehousePublicId);
        Assert.Equal(ReceivingModes.Direct, enrolled.DefaultWarehouseReceivingMode);

        f.Db.ChangeTracker.Clear();
        var (_, noWarehouse) = await EnrolledDeviceAsync(f, "AP-17");
        Assert.Null(noWarehouse.DefaultWarehouseReceivingMode);
    }

    /// <summary>SecurityEvent escritos por el servicio (tipo, resultado, compañía y detalle serializado).</summary>
    private sealed class RecordingSecurityEventWriter : ISecurityEventWriter
    {
        public List<(string EventType, string Outcome, int? TenantId, string Detail)> Events { get; } = new();

        public Task WriteAsync(string eventType, string outcome, int? userId = null, int? tenantId = null, object? detail = null, CancellationToken ct = default)
        {
            Events.Add((eventType, outcome, tenantId, detail is null ? "" : JsonSerializer.Serialize(detail)));
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Caché que ejecuta una acción (una sola vez) cuando se consultan los módulos de una compañía: el enroll lo hace entre la
    /// búsqueda del código y su transacción, así se simula otro enroll que gana la carrera en ese intervalo.
    /// </summary>
    private sealed class HookedCache(IMemoryCache inner) : IMemoryCache
    {
        public Action? OnModulesLookup { get; set; }

        public bool TryGetValue(object key, out object? value)
        {
            if (key is string k && k.StartsWith("modules:", StringComparison.Ordinal) && OnModulesLookup is { } hook)
            {
                OnModulesLookup = null;
                hook();
            }
            return inner.TryGetValue(key, out value);
        }

        public ICacheEntry CreateEntry(object key) => inner.CreateEntry(key);
        public void Remove(object key) => inner.Remove(key);
        public void Dispose() { }
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

    // ================================================================ sello de sesiones del aparato (access tokens con `did`)

    [Fact]
    public void Access_tokens_issued_before_the_device_sessions_cutoff_are_rejected()
    {
        var cutoff = new DateTime(2026, 9, 28, 12, 0, 0, 500, DateTimeKind.Utc);
        var cutoffSeconds = new DateTimeOffset(cutoff).ToUnixTimeSeconds();
        // Emitido antes del sello (p. ej. antes de la baja) → rechazado, aunque el aparato esté activo otra vez.
        Assert.True(DeviceClaims.IssuedBeforeSessionsCutoff(cutoffSeconds - 1, cutoff));
        Assert.True(DeviceClaims.IssuedBeforeSessionsCutoff(cutoffSeconds - 3600, cutoff));
        // Emitido después del sello (o en el mismo segundo: el iat va en segundos enteros) → aceptado.
        Assert.False(DeviceClaims.IssuedBeforeSessionsCutoff(cutoffSeconds, cutoff));
        Assert.False(DeviceClaims.IssuedBeforeSessionsCutoff(cutoffSeconds + 1, cutoff));
        // El sello leído de SQL Server llega con Kind Unspecified: se interpreta como UTC.
        Assert.True(DeviceClaims.IssuedBeforeSessionsCutoff(cutoffSeconds - 1, DateTime.SpecifyKind(cutoff, DateTimeKind.Unspecified)));
        Assert.False(DeviceClaims.IssuedBeforeSessionsCutoff(cutoffSeconds, DateTime.SpecifyKind(cutoff, DateTimeKind.Unspecified)));
        // Sin sello → aceptado (con o sin iat); con sello y sin iat → rechazado.
        Assert.False(DeviceClaims.IssuedBeforeSessionsCutoff(cutoffSeconds - 1, null));
        Assert.False(DeviceClaims.IssuedBeforeSessionsCutoff(null, null));
        Assert.True(DeviceClaims.IssuedBeforeSessionsCutoff(null, cutoff));
    }

    [Fact]
    public void Access_tokens_carry_a_numeric_iat_claim()
    {
        var jwt = new JwtTokenService(Microsoft.Extensions.Options.Options.Create(new JwtOptions { SigningKey = new string('k', 64) }));
        var user = new ApplicationUser { Id = 1, Email = "yo@t.local", FullName = "Yo Mismo", SecurityStamp = "s1" };
        var before = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var (token, _) = jwt.CreateAccessToken(user, WmsFixture.TenantId, 1, null, null);
        var parsed = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().ReadJwtToken(token);
        var iat = Assert.Single(parsed.Claims, c => c.Type == "iat");
        Assert.InRange(long.Parse(iat.Value), before, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        Assert.Contains("\"iat\":" + iat.Value, parsed.Payload.SerializeToJson());   // número, no cadena
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

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Device_without_a_code_gets_one_generated_by_the_server(string? code)
    {
        // Lote F8a (P5): la pantalla de alta no pide código (solo Nombre y almacén) — antes esto daba 400 "El código
        // del aparato es obligatorio."; ahora el servidor genera uno legible.
        await using var f = await FixtureAsync();
        var created = await f.Get<DeviceService>().CreateAsync(new DeviceCreateRequest(code, "Zebra 1", null, null, null), default);
        Assert.Matches("^AP-[A-Z0-9]{6}$", created.Device.Code);
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
    public async Task Default_warehouse_of_another_tenant_is_not_found()
    {
        await using var f = await FixtureAsync();
        var devices = f.Get<DeviceService>();
        var foreign = await f.AddWarehouseAsync("WX", tenantId: WmsFixture.OtherTenantId);
        var own = await devices.CreateAsync(new DeviceCreateRequest("ZB-01", null, null, null, null), default);
        f.Db.ChangeTracker.Clear();

        var c = await Assert.ThrowsAsync<NotFoundException>(() => devices.CreateAsync(new DeviceCreateRequest("ZB-02", null, null, foreign.PublicId, null), default));
        Assert.Equal("Almacén no encontrado.", c.Message);
        Assert.Equal(404, c.StatusCode);
        f.Db.ChangeTracker.Clear();
        Assert.False(await f.Db.Set<UserDevice>().IgnoreQueryFilters().AnyAsync(d => d.Code == "ZB-02"));

        var u = await Assert.ThrowsAsync<NotFoundException>(() => devices.UpdateAsync(own.Device.PublicId, new DevicePatchRequest(null, foreign.PublicId), default));
        Assert.Equal("Almacén no encontrado.", u.Message);
        Assert.Null(Row(f, own.Device.PublicId).DefaultWarehouseId);
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
        var w = await f.AddWarehouseAsync("W1");
        var created = await devices.CreateAsync(new DeviceCreateRequest("ZB-01", null, null, w.PublicId, "DARK"), default);
        f.Db.ChangeTracker.Clear();

        // El código se acepta en minúsculas y con guion (como lo teclea el operador).
        var typed = created.EnrollCode[..4].ToLowerInvariant() + "-" + created.EnrollCode[4..];
        var enrolled = await devices.EnrollAsync(new DeviceEnrollRequest(typed, "TC52", "1.0.0"), default);

        Assert.Equal(created.Device.PublicId, enrolled.DevicePublicId);
        Assert.Equal("Tenant de prueba", enrolled.TenantName);
        Assert.Equal(UiThemes.Dark, enrolled.Theme);
        Assert.Equal(w.PublicId, enrolled.DefaultWarehousePublicId);
        Assert.Equal(43, enrolled.DeviceSecret.Length);   // 32 bytes en base64url sin relleno
        Assert.Equal(32, Convert.FromBase64String(enrolled.DeviceSecret.Replace('-', '+').Replace('_', '/') + "=").Length);

        var row = Row(f, created.Device.PublicId);
        Assert.Equal(DeviceService.Hash(enrolled.DeviceSecret), row.SecretHash);
        Assert.Null(row.EnrollCodeHash);
        Assert.Null(row.EnrollCodeExpiresUtc);
        Assert.NotNull(row.EnrolledAtUtc);
        // Registrar (o reinstalar) fija el sello de sesiones: los access tokens anteriores del aparato dejan de servir.
        Assert.Equal(row.EnrolledAtUtc, row.SessionsNotBeforeUtc);
        Assert.Equal("TC52", row.Model);
        // Datos técnicos en UserDeviceActivity (no en UserDevice): no tocan el RowVersion del aparato.
        var activity = f.Db.Set<UserDeviceActivity>().IgnoreQueryFilters().AsNoTracking().Single(a => a.UserDeviceId == row.UserDeviceId);
        Assert.Equal("1.0.0", activity.AppVersion);
        Assert.NotNull(activity.LastSeenUtc);
        Assert.Equal(row.TenantId, activity.TenantId);
        Assert.Equal("1.0.0", (await devices.GetAsync(created.Device.PublicId, default)).AppVersion);

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

    [Fact]
    public async Task Enroll_is_401_with_a_failure_event_when_another_enroll_consumed_the_code_first()
    {
        HookedCache? hooked = null;
        var events = new RecordingSecurityEventWriter();
        await using var f = await FixtureAsync(s =>
        {
            var inner = (IMemoryCache)s.Last(d => d.ServiceType == typeof(IMemoryCache)).ImplementationInstance!;
            hooked = new HookedCache(inner);
            s.AddSingleton<IMemoryCache>(hooked);
            s.AddSingleton<ISecurityEventWriter>(events);
        });
        var created = await f.Get<DeviceService>().CreateAsync(new DeviceCreateRequest("ZB-01", null, null, null, null), default);
        f.Db.ChangeTracker.Clear();
        var otherSecret = DeviceService.Hash("secreto-del-otro-enroll");
        // Entre la búsqueda del código y la transacción, otro enroll con el mismo código ya guardó su secreto y lo quemó.
        hooked!.OnModulesLookup = () =>
        {
            var row = f.Db.Set<UserDevice>().Local.Single(d => d.PublicId == created.Device.PublicId);
            row.SecretHash = otherSecret;
            row.EnrollCodeHash = null;
            row.EnrollCodeExpiresUtc = null;
            row.EnrolledAtUtc = DateTime.UtcNow;
            f.Db.SaveChangesAsync().GetAwaiter().GetResult();
        };

        var ex = await Assert.ThrowsAsync<UnauthorizedException>(() => f.Get<DeviceService>().EnrollAsync(new DeviceEnrollRequest(created.EnrollCode, "TC52", "1.0.0"), default));

        Assert.Null(hooked.OnModulesLookup);
        Assert.Equal(DeviceService.InvalidEnrollCodeMessage, ex.Message);
        Assert.Equal(401, ex.StatusCode);
        Assert.Equal(otherSecret, Row(f, created.Device.PublicId).SecretHash);
        var failure = Assert.Single(events.Events);
        Assert.Equal((SecurityEventTypes.ApiCredential, SecurityOutcomes.Failure, (int?)WmsFixture.TenantId, "{\"action\":\"device_enroll\"}"), failure);
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

        var beforeOff = DateTime.UtcNow;
        var off = await devices.DeactivateAsync(a.Device.PublicId, default);
        Assert.False(off.IsActive);
        Assert.Null(off.EnrollCodeExpiresUtc);
        Assert.Null(Row(f, a.Device.PublicId).EnrollCodeHash);
        // La baja fija el sello de sesiones del aparato (solo el de ese aparato).
        var offCutoff = Row(f, a.Device.PublicId).SessionsNotBeforeUtc;
        Assert.NotNull(offCutoff);
        Assert.InRange(offCutoff!.Value, beforeOff, DateTime.UtcNow);
        Assert.Null(Row(f, b.Device.PublicId).SessionsNotBeforeUtc);
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
        // La reactivación mueve el sello: los access tokens de antes de la baja siguen rechazados.
        Assert.True(Row(f, a.Device.PublicId).SessionsNotBeforeUtc >= offCutoff);
        f.Db.ChangeTracker.Clear();
        var fresh = await devices.RegenerateEnrollCodeAsync(a.Device.PublicId, default);
        Assert.Equal(8, fresh.EnrollCode.Length);
        Assert.Equal(DeviceService.Hash(fresh.EnrollCode), Row(f, a.Device.PublicId).EnrollCodeHash);
        f.Db.ChangeTracker.Clear();
        await Assert.ThrowsAsync<UnauthorizedException>(() => devices.EnrollAsync(new DeviceEnrollRequest(a.EnrollCode, null, null), default));
    }

    [Fact]
    public async Task Reactivating_revokes_any_session_of_the_device_left_alive_and_records_device_reactivated()
    {
        var events = new RecordingSecurityEventWriter();
        await using var f = await FixtureAsync(s => s.AddSingleton<ISecurityEventWriter>(events));
        var devices = f.Get<DeviceService>();
        var a = await devices.CreateAsync(new DeviceCreateRequest("ZB-01", null, null, null, null), default);
        var b = await devices.CreateAsync(new DeviceCreateRequest("ZB-02", null, null, null, null), default);
        f.Db.ChangeTracker.Clear();
        var aId = Row(f, a.Device.PublicId).UserDeviceId;
        var bId = Row(f, b.Device.PublicId).UserDeviceId;
        await devices.DeactivateAsync(a.Device.PublicId, default);
        f.Db.ChangeTracker.Clear();
        // Sesión emitida al aparato durante la baja (un refresh que corría en paralelo) y sesiones ajenas al aparato.
        var late = Token(1, aId);
        f.Db.RefreshTokens.AddRange(late, Token(1, bId), Token(1, null));
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        events.Events.Clear();
        var offCutoff = Row(f, a.Device.PublicId).SessionsNotBeforeUtc;
        Assert.NotNull(offCutoff);
        // La caché del estado del aparato (did) se borra al reactivar: el sello nuevo rige en el acto en esta instancia.
        var cache = f.Get<IMemoryCache>();
        cache.Set(DeviceClaims.ActiveCacheKey(a.Device.PublicId), new DeviceTokenState(false, offCutoff));
        var beforeOn = DateTime.UtcNow;

        var on = await devices.ReactivateAsync(a.Device.PublicId, default);

        Assert.True(on.IsActive);
        var onCutoff = Row(f, a.Device.PublicId).SessionsNotBeforeUtc;
        Assert.NotNull(onCutoff);
        Assert.InRange(onCutoff!.Value, beforeOn, DateTime.UtcNow);
        Assert.True(onCutoff >= offCutoff);
        Assert.False(cache.TryGetValue(DeviceClaims.ActiveCacheKey(a.Device.PublicId), out _));
        var tokens = f.Db.RefreshTokens.IgnoreQueryFilters().AsNoTracking().ToList();
        Assert.NotNull(tokens.Single(t => t.TokenHash == late.TokenHash).RevokedAtUtc);
        Assert.Null(tokens.Single(t => t.UserDeviceId == bId).RevokedAtUtc);
        Assert.Null(tokens.Single(t => t.UserDeviceId == null).RevokedAtUtc);
        var revoked = Assert.Single(events.Events);
        Assert.Equal(SecurityEventTypes.TokenRevoked, revoked.EventType);
        Assert.Contains("\"reason\":\"device_reactivated\"", revoked.Detail);
        Assert.Contains("\"count\":1", revoked.Detail);

        // Reactivar un aparato ya activo no hace nada (ni revoca ni escribe).
        f.Db.ChangeTracker.Clear();
        await devices.ReactivateAsync(a.Device.PublicId, default);
        Assert.Single(events.Events);
        Assert.Equal(onCutoff, Row(f, a.Device.PublicId).SessionsNotBeforeUtc);
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
    public async Task Device_users_ignore_a_pin_defined_only_in_another_company()
    {
        // El PIN es por compañía (UserPin único por TenantId + UserId): un miembro activo de la compañía del aparato, con
        // inventory.view, cuyo único PIN es de otra compañía no aparece en la lista del aparato.
        await using var f = await FixtureAsync();
        var (_, enrolled) = await EnrolledDeviceAsync(f, "ZB-01");
        f.Db.Users.Add(new ApplicationUser { Id = 20, UserName = "u20@t.local", Email = "u20@t.local", FullName = "Pin Ajeno", SecurityStamp = "s20", IsActive = true });
        f.Db.UserTenants.Add(new UserTenant { UserId = 20, TenantId = WmsFixture.TenantId, StatusCodeId = ActiveMembershipId, IsDefault = true });
        f.Db.UserTenants.Add(new UserTenant { UserId = 20, TenantId = WmsFixture.OtherTenantId, StatusCodeId = ActiveMembershipId });
        f.Db.Set<UserPin>().Add(new UserPin { TenantId = WmsFixture.OtherTenantId, UserId = 20, PinHash = "hash" });
        f.Get<IMemoryCache>().Set($"perms:20:{WmsFixture.TenantId}", new HashSet<string>(new[] { PermissionCatalog.InventoryView }, StringComparer.OrdinalIgnoreCase));
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();

        var list = await f.Get<DeviceService>().GetDeviceUsersAsync(new DeviceUsersRequest(enrolled.DevicePublicId, enrolled.DeviceSecret), default);

        Assert.DoesNotContain(list, u => u.UserId == 20);
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

    [Fact]
    public async Task Device_users_is_401_when_the_tenant_is_unusable()
    {
        // TenantUsableAsync en AuthenticateAsync (device/users y device/login): módulo WMS_LOTSERIAL apagado o compañía
        // inactiva → el mismo 401 que un aparato desactivado. El heartbeat (isActive=false) y el login usan ExecuteUpdate,
        // no soportado por InMemory: los cubre el smoke con el módulo apagado.
        await using var f = await FixtureAsync();
        var devices = f.Get<DeviceService>();
        var (_, enrolled) = await EnrolledDeviceAsync(f, "ZB-01");
        var request = new DeviceUsersRequest(enrolled.DevicePublicId, enrolled.DeviceSecret);
        Assert.Empty(await devices.GetDeviceUsersAsync(request, default));   // utilizable: sin error
        f.Db.ChangeTracker.Clear();

        f.SetModules(ModuleKeys.Purchasing);
        var moduleOff = await Assert.ThrowsAsync<UnauthorizedException>(() => devices.GetDeviceUsersAsync(request, default));
        Assert.Equal(DeviceService.InvalidDeviceMessage, moduleOff.Message);
        f.Db.ChangeTracker.Clear();

        f.SetModules(ModuleKeys.WmsLotSerial, ModuleKeys.Purchasing);
        var tenantRow = f.Db.Set<Tenant>().IgnoreQueryFilters().Single(t => t.TenantId == WmsFixture.TenantId);
        tenantRow.IsActive = false;
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        var inactive = await Assert.ThrowsAsync<UnauthorizedException>(() => devices.GetDeviceUsersAsync(request, default));
        Assert.Equal(DeviceService.InvalidDeviceMessage, inactive.Message);
    }

    /// <summary>Guarda el usuario efectivo de cada evento (userId ?? contexto, como SecurityEventWriter).</summary>
    private sealed class CapturingSecurityEventWriter(TenantContext tenant) : ISecurityEventWriter
    {
        public List<(string EventType, string Outcome, int? UserId)> Events { get; } = new();

        public Task WriteAsync(string eventType, string outcome, int? userId = null, int? tenantId = null, object? detail = null, CancellationToken ct = default)
        {
            Events.Add((eventType, outcome, userId ?? tenant.UserId));
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Anonymous_device_flows_do_not_use_the_user_of_a_bearer_in_the_context()
    {
        // Decisión 17: enroll y device/users corren con AsAnonymous(tenant del aparato): ni los SecurityEvent (TOKEN_REVOKED,
        // API_CREDENTIAL, fallos) ni el SaveChanges (autor del AuditLog/UpdatedBy) llevan el usuario de un bearer ajeno; al
        // terminar el contexto se restaura.
        await using var f = await FixtureAsync(s =>
        {
            s.AddSingleton<CapturingSecurityEventWriter>();
            s.AddSingleton<ISecurityEventWriter>(sp => sp.GetRequiredService<CapturingSecurityEventWriter>());
        });
        var devices = f.Get<DeviceService>();
        var writer = f.Get<CapturingSecurityEventWriter>();
        var (created, _) = await EnrolledDeviceAsync(f, "ZB-01");
        // Reinstalación con una sesión viva del aparato: el enroll la revoca (TOKEN_REVOKED).
        f.Db.RefreshTokens.Add(Token(1, Row(f, created.Device.PublicId).UserDeviceId));
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        var code = (await devices.RegenerateEnrollCodeAsync(created.Device.PublicId, default)).EnrollCode;
        f.Db.ChangeTracker.Clear();

        writer.Events.Clear();
        var saveUsers = new List<int?>();
        f.Db.SavingChanges += (_, _) => saveUsers.Add(f.Tenant.UserId);
        Assert.Equal(1, f.Tenant.UserId);   // el contexto trae el usuario de un bearer

        var again = await devices.EnrollAsync(new DeviceEnrollRequest(code, null, null), default);
        f.Db.ChangeTracker.Clear();
        await Assert.ThrowsAsync<UnauthorizedException>(() => devices.EnrollAsync(new DeviceEnrollRequest("ZZZZ2222", null, null), default));
        await devices.GetDeviceUsersAsync(new DeviceUsersRequest(again.DevicePublicId, again.DeviceSecret), default);
        await Assert.ThrowsAsync<UnauthorizedException>(() => devices.GetDeviceUsersAsync(new DeviceUsersRequest(again.DevicePublicId, "otro"), default));

        Assert.Contains(writer.Events, e => e.EventType == SecurityEventTypes.TokenRevoked);
        Assert.Contains(writer.Events, e => e.EventType == SecurityEventTypes.ApiCredential && e.Outcome == SecurityOutcomes.Success);
        Assert.Contains(writer.Events, e => e.Outcome == SecurityOutcomes.Failure);
        Assert.All(writer.Events, e => Assert.Null(e.UserId));
        Assert.NotEmpty(saveUsers);
        Assert.All(saveUsers, u => Assert.Null(u));
        Assert.Equal(1, f.Tenant.UserId);
        Assert.Equal(WmsFixture.TenantId, f.Tenant.TenantId);
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

    [Fact]
    public async Task Wrong_password_on_my_pin_counts_toward_the_account_lockout()
    {
        // PUT /me/pin sirve con el token de una sesión de aparato (solo PIN): la contraseña equivocada cuenta en el bloqueo de
        // Identity (5 fallos → 15 minutos, el mismo del login) y, bloqueada, responde el mismo 400 aunque la contraseña sea
        // correcta. Un acierto antes del bloqueo reinicia el contador.
        await using var f = await FixtureAsync(s => s.Configure<IdentityOptions>(o => { o.Lockout.MaxFailedAccessAttempts = 5; o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15); }));
        var me = f.Db.Users.Single(u => u.Id == 1);
        me.LockoutEnabled = true;
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        var pins = f.Get<PinService>();

        await Assert.ThrowsAsync<ValidationException>(() => pins.SetMineAsync(new PinSetRequest("equivocada", "4826"), default));
        await Assert.ThrowsAsync<ValidationException>(() => pins.SetMineAsync(new PinSetRequest("equivocada", "4826"), default));
        f.Db.ChangeTracker.Clear();
        Assert.Equal(2, f.Db.Users.AsNoTracking().Single(u => u.Id == 1).AccessFailedCount);
        await pins.SetMineAsync(new PinSetRequest(Password, "4826"), default);
        f.Db.ChangeTracker.Clear();
        Assert.Equal(0, f.Db.Users.AsNoTracking().Single(u => u.Id == 1).AccessFailedCount);

        for (var i = 0; i < 5; i++)
            await Assert.ThrowsAsync<ValidationException>(() => pins.SetMineAsync(new PinSetRequest("equivocada", "5937"), default));
        f.Db.ChangeTracker.Clear();
        Assert.True(f.Db.Users.AsNoTracking().Single(u => u.Id == 1).LockoutEnd > DateTimeOffset.UtcNow.AddMinutes(14));

        var locked = await Assert.ThrowsAsync<ValidationException>(() => pins.SetMineAsync(new PinSetRequest(Password, "5937"), default));
        Assert.Equal(new[] { "La contraseña actual es incorrecta." }, locked.Errors!["currentPassword"]);
        f.Db.ChangeTracker.Clear();
        var hasher = new PasswordHasher<ApplicationUser>();
        var user = f.Db.Users.AsNoTracking().Single(u => u.Id == 1);
        var pin = Assert.Single(f.Db.Set<UserPin>().AsNoTracking().ToList());
        Assert.NotEqual(PasswordVerificationResult.Failed, hasher.VerifyHashedPassword(user, pin.PinHash, "4826"));
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
    public async Task My_pin_status_returns_the_lock_only_while_it_is_in_force()
    {
        // GET /me/pin: lockedUntilUtc mientras el bloqueo sigue vigente; vencido, null (y hasPin sigue en true).
        await using var f = await FixtureAsync();
        var until = DateTime.UtcNow.AddMinutes(15);
        f.Db.Set<UserPin>().Add(new UserPin { TenantId = WmsFixture.TenantId, UserId = 1, PinHash = "hash", FailedCount = 5, LockedUntilUtc = until });
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();

        var locked = await f.Get<PinService>().GetMineAsync(default);
        Assert.True(locked.HasPin);
        Assert.Equal(until, locked.LockedUntilUtc);

        var pin = f.Db.Set<UserPin>().AsTracking().Single(p => p.UserId == 1);
        pin.LockedUntilUtc = DateTime.UtcNow.AddMinutes(-1);
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();

        var expired = await f.Get<PinService>().GetMineAsync(default);
        Assert.True(expired.HasPin);
        Assert.Null(expired.LockedUntilUtc);
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

    // ================================================================ nombre y modelo del aparato (400)

    [Fact]
    public async Task Device_name_and_model_have_length_limits_on_create_and_update()
    {
        await using var f = await FixtureAsync();
        var devices = f.Get<DeviceService>();

        var name = await Assert.ThrowsAsync<ValidationException>(() => devices.CreateAsync(new DeviceCreateRequest("ZB-01", new string('N', 101), null, null, null), default));
        Assert.Equal(400, name.StatusCode);
        Assert.Equal(new[] { "El nombre admite hasta 100 caracteres." }, name.Errors!["name"]);
        var model = await Assert.ThrowsAsync<ValidationException>(() => devices.CreateAsync(new DeviceCreateRequest("ZB-01", null, new string('M', 81), null, null), default));
        Assert.Equal(new[] { "El modelo admite hasta 80 caracteres." }, model.Errors!["model"]);
        f.Db.ChangeTracker.Clear();
        Assert.Empty(f.Db.Set<UserDevice>().IgnoreQueryFilters().AsNoTracking().ToList());

        // En el límite exacto se aceptan.
        var created = await devices.CreateAsync(new DeviceCreateRequest("ZB-01", new string('N', 100), new string('M', 80), null, null), default);
        Assert.Equal(100, created.Device.Name!.Length);
        f.Db.ChangeTracker.Clear();
        Assert.Equal(80, Row(f, created.Device.PublicId).Model!.Length);

        // Edición: el mismo 400 y el nombre no cambia.
        var upd = await Assert.ThrowsAsync<ValidationException>(() => devices.UpdateAsync(created.Device.PublicId, new DevicePatchRequest(new string('X', 101), null), default));
        Assert.Equal(new[] { "El nombre admite hasta 100 caracteres." }, upd.Errors!["name"]);
        f.Db.ChangeTracker.Clear();
        Assert.Equal(new string('N', 100), Row(f, created.Device.PublicId).Name);
    }

    // ================================================================ PIN impuesto por otro: límite de privilegios en cada login

    [Fact]
    public async Task Pin_assigned_by_another_user_stops_working_when_the_user_gets_more_privileges()
    {
        // Hallazgo de revisión: el límite 'los permisos del usuario caben en los de quien asigna' se revisaba solo al asignar.
        // AssignerStillCoversAsync (login por aparato y refresh de sesión de aparato) lo vuelve a comprobar con los permisos
        // ACTUALES; si quien lo asignó ya no los cubre (o se desactivó, dejó la compañía), el PIN deja de servir.
        await using var f = await FixtureAsync();
        const int T = WmsFixture.TenantId;
        f.Db.Users.Add(new ApplicationUser { Id = 2, UserName = "b@t.local", Email = "b@t.local", SecurityStamp = "s2", IsActive = true });
        f.Db.UserTenants.Add(new UserTenant { UserId = 2, TenantId = T, StatusCodeId = ActiveMembershipId });
        f.Db.Set<UserPin>().Add(new UserPin { TenantId = T, UserId = 2, PinHash = "hash", UpdatedBy = 1 });
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        var cache = f.Get<IMemoryCache>();
        void Perms(int userId, params string[] codes) => cache.Set($"perms:{userId}:{T}", new HashSet<string>(codes, StringComparer.OrdinalIgnoreCase));
        var pins = f.Get<PinService>();

        // A (1) = {devices.manage, inventory.view}; B (2) = {inventory.view}: los permisos de B caben en los de A.
        Perms(1, PermissionCatalog.DevicesManage, PermissionCatalog.InventoryView);
        Perms(2, PermissionCatalog.InventoryView);
        Assert.True(await pins.AssignerStillCoversAsync(2, T, default));

        // A B le suben los permisos (TenantAdmin): el PIN que puso A ya no sirve.
        Perms(2, PermissionCatalog.InventoryView, PermissionCatalog.AdminUsers);
        Assert.False(await pins.AssignerStillCoversAsync(2, T, default));

        // Si B define su propio PIN (UpdatedBy = él mismo o null) vuelve a servir.
        await SetUpdatedByAsync(f, 2, 2);
        Assert.True(await pins.AssignerStillCoversAsync(2, T, default));
        await SetUpdatedByAsync(f, 2, null);
        Assert.True(await pins.AssignerStillCoversAsync(2, T, default));

        // Quien asignó se desactiva o deja la compañía → no sirve aunque los permisos quepan.
        await SetUpdatedByAsync(f, 2, 1);
        Perms(2, PermissionCatalog.InventoryView);
        Assert.True(await pins.AssignerStillCoversAsync(2, T, default));
        var membership = f.Db.UserTenants.Single(m => m.UserId == 1 && m.TenantId == T);
        membership.StatusCodeId = SuspendedMembershipId;
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        Assert.False(await pins.AssignerStillCoversAsync(2, T, default));
        membership = f.Db.UserTenants.Single(m => m.UserId == 1 && m.TenantId == T);
        membership.StatusCodeId = ActiveMembershipId;
        var a = f.Db.Users.Single(u => u.Id == 1);
        a.IsActive = false;
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        Assert.False(await pins.AssignerStillCoversAsync(2, T, default));

        // Asignado por un admin de plataforma (activo): se exceptúa.
        a = f.Db.Users.Single(u => u.Id == 1);
        a.IsActive = true;
        a.IsPlatformAdmin = true;
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        Perms(2, PermissionCatalog.InventoryView, PermissionCatalog.AdminUsers);
        Assert.True(await pins.AssignerStillCoversAsync(2, T, default));

        // Sin PIN no hay nada que revisar.
        Assert.True(await pins.AssignerStillCoversAsync(99, T, default));
        Assert.Equal("Su PIN lo asignó otra persona que ya no tiene sus permisos; defina su propio PIN en Mi cuenta.", PinService.AssignerLowerPrivilegesMessage);
    }

    private static async Task SetUpdatedByAsync(WmsFixture f, int userId, int? by)
    {
        var pin = f.Db.Set<UserPin>().Single(p => p.UserId == userId);
        pin.UpdatedBy = by;
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
    }
}
