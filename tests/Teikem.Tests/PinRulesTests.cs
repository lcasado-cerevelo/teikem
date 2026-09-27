using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Common;
using Teikem.Domain.Constants;
using Teikem.Domain.Entities;
using Teikem.Domain.Identity;
using Teikem.Domain.Security;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 8A / P0 — reglas puras del PIN de los aparatos de almacén (PinRules): formato de 4 a 6 dígitos, secuencias
/// triviales, bloqueo de 15 minutos al 5.º fallo, hash con el PasswordHasher de Identity; y la base compartida del lote
/// (permiso devices.manage, catálogos UiTheme/USER_DEVICE, entidades UserDevice/UserPin mapeadas 1:1 con el SQL).
/// </summary>
public class PinRulesTests
{
    private static readonly Lazy<string> Seed = new(() => File.ReadAllText(Path.Combine(TripCatalogTests.RepoRoot(), "Diseño", "logistica-db-seed.sql")));
    private static readonly Lazy<string> Structure = new(() => File.ReadAllText(Path.Combine(TripCatalogTests.RepoRoot(), "Diseño", "logistica-db-estructura.sql")));

    // ================================================================ formato

    [Theory]
    [InlineData("4826")]
    [InlineData("48261")]
    [InlineData("482619")]
    [InlineData(" 4826 ")]
    [InlineData("1357")]
    [InlineData("1233")]
    [InlineData("9012")]   // sin dar la vuelta de 9 a 0: no es secuencia trivial
    [InlineData("0102")]
    public void Valid_pins_pass(string pin) => Assert.Null(PinRules.Validate(pin));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("123")]
    [InlineData("4826193")]
    [InlineData("48a6")]
    [InlineData("48 26")]
    [InlineData("-482")]
    [InlineData("４８２６")]   // dígitos de ancho completo: no son ASCII
    public void Pins_outside_4_to_6_ascii_digits_are_rejected(string? pin)
        => Assert.Equal("El PIN debe tener de 4 a 6 dígitos.", PinRules.Validate(pin));

    [Theory]
    [InlineData("0000")]
    [InlineData("1111")]
    [InlineData("999999")]
    [InlineData("1234")]
    [InlineData("0123")]
    [InlineData("123456")]
    [InlineData("456789")]
    [InlineData("4321")]
    [InlineData("98765")]
    [InlineData("654321")]
    public void Trivial_sequences_and_repeated_digits_are_rejected(string pin)
        => Assert.Equal("El PIN no puede ser una secuencia trivial.", PinRules.Validate(pin));

    [Fact]
    public void Messages_and_limits_are_the_documented_ones()
    {
        Assert.Equal("El PIN debe tener de 4 a 6 dígitos.", PinRules.FormatMessage);
        Assert.Equal("El PIN no puede ser una secuencia trivial.", PinRules.TrivialMessage);
        Assert.Equal("PIN bloqueado por 15 minutos.", PinRules.LockedMessage);
        Assert.Equal(4, PinRules.MinLength);
        Assert.Equal(6, PinRules.MaxLength);
        Assert.Equal(5, PinRules.MaxFailedAttempts);
        Assert.Equal(TimeSpan.FromMinutes(15), PinRules.LockDuration);
    }

    // ================================================================ intentos y bloqueo

    [Fact]
    public void Four_failures_count_and_the_fifth_locks_for_15_minutes_resetting_the_counter()
    {
        var now = new DateTime(2026, 9, 27, 12, 0, 0, DateTimeKind.Utc);
        var (count, locked) = (0, (DateTime?)null);
        for (var i = 1; i <= 4; i++)
        {
            (count, locked) = PinRules.RegisterFailure(count, now);
            Assert.Equal(i, count);
            Assert.Null(locked);
            Assert.False(PinRules.IsLocked(locked, now));
        }
        (count, locked) = PinRules.RegisterFailure(count, now);
        Assert.Equal(0, count);
        Assert.Equal(now.AddMinutes(15), locked);
        Assert.True(PinRules.IsLocked(locked, now));
        Assert.True(PinRules.IsLocked(locked, now.AddMinutes(14).AddSeconds(59)));
        Assert.False(PinRules.IsLocked(locked, now.AddMinutes(15)));
    }

    [Fact]
    public void A_success_resets_the_counter_and_clears_the_lock()
    {
        Assert.Equal((0, (DateTime?)null), PinRules.RegisterSuccess());
        // Tras 4 fallos, un acierto deja otra vez 5 intentos antes del bloqueo.
        var now = DateTime.UtcNow;
        var (count, _) = PinRules.RegisterFailure(3, now);
        Assert.Equal(4, count);
        (count, var locked) = PinRules.RegisterSuccess();
        for (var i = 0; i < 4; i++) (count, locked) = PinRules.RegisterFailure(count, now);
        Assert.Equal(4, count);
        Assert.Null(locked);
    }

    [Fact]
    public void A_negative_counter_is_treated_as_zero()
    {
        var now = DateTime.UtcNow;
        Assert.Equal((1, (DateTime?)null), PinRules.RegisterFailure(-3, now));
        Assert.False(PinRules.IsLocked(null, now));
    }

    // ================================================================ hash con Identity

    [Fact]
    public void Pin_hash_uses_the_identity_password_hasher_and_never_stores_the_pin()
    {
        var hasher = new PasswordHasher<ApplicationUser>();
        var user = new ApplicationUser { Id = 7, UserName = "almacen@demo" };
        var hash = hasher.HashPassword(user, "4826");
        Assert.DoesNotContain("4826", hash);
        Assert.NotEqual(PasswordVerificationResult.Failed, hasher.VerifyHashedPassword(user, hash, "4826"));
        Assert.Equal(PasswordVerificationResult.Failed, hasher.VerifyHashedPassword(user, hash, "4827"));
        Assert.True(hash.Length <= 200, "PinHash es NVARCHAR(200)");
    }

    // ================================================================ base compartida del lote

    [Fact]
    public void Devices_manage_is_a_SECURITY_permission_mirrored_in_the_seed_and_only_in_TenantAdmin()
    {
        Assert.Equal("devices.manage", PermissionCatalog.DevicesManage);
        var p = Assert.Single(PermissionCatalog.All, x => x.Code == PermissionCatalog.DevicesManage);
        Assert.Equal(("SECURITY", "Gestionar aparatos y PIN", "Manage devices & PINs"), (p.Category, p.LabelEs, p.LabelEn));
        Assert.Contains("('devices.manage','SECURITY','Gestionar aparatos y PIN','Manage devices & PINs')", Seed.Value);
        Assert.Contains("('PermissionCategory','SECURITY',", Seed.Value);
        Assert.Contains(PermissionCatalog.DevicesManage, PermissionCatalog.RoleTemplates["TenantAdmin"]);
        Assert.All(PermissionCatalog.RoleTemplates.Where(kv => kv.Key != "TenantAdmin"), kv => Assert.DoesNotContain(PermissionCatalog.DevicesManage, kv.Value));
    }

    [Fact]
    public void Catalogs_of_the_lot_are_seeded()
    {
        Assert.Equal("UiTheme", LookupDomains.UiTheme);
        Assert.Contains("('UiTheme',1,", Seed.Value);
        Assert.Contains($"('UiTheme','{UiThemes.Light}',", Seed.Value);
        Assert.Contains($"('UiTheme','{UiThemes.Dark}',", Seed.Value);
        Assert.Contains($"('MessageDirection','{MessageDirections.Inbound}',", Seed.Value);
        Assert.Equal("USER_DEVICE", EntityTypes.UserDevice);
        Assert.Contains("('EntityType','USER_DEVICE',", Seed.Value);
        Assert.Contains($"('DevicePlatform','ANDROID',", Seed.Value);
    }

    [Fact]
    public void UserDevice_is_audited_tenant_scoped_and_hides_its_secrets()
    {
        var t = typeof(UserDevice);
        Assert.Equal(EntityTypes.UserDevice, ((AuditEntityAttribute)Attribute.GetCustomAttribute(t, typeof(AuditEntityAttribute))!).EntityTypeCode);
        Assert.True(typeof(ITenantScoped).IsAssignableFrom(t));
        Assert.True(typeof(ISoftDeletable).IsAssignableFrom(t));
        foreach (var prop in new[] { nameof(UserDevice.SecretHash), nameof(UserDevice.EnrollCodeHash) })
            Assert.NotNull(Attribute.GetCustomAttribute(t.GetProperty(prop)!, typeof(SensitiveDataAttribute)));
        Assert.True(typeof(ITenantScoped).IsAssignableFrom(typeof(UserPin)));
        Assert.NotNull(Attribute.GetCustomAttribute(typeof(UserPin).GetProperty(nameof(UserPin.PinHash))!, typeof(SensitiveDataAttribute)));
    }

    [Fact]
    public void Ef_model_mirrors_the_structure_script()
    {
        using var db = TenantIsolationModelTests.CreateSqlServerModelContext();
        var device = db.Model.FindEntityType(typeof(UserDevice))!;
        Assert.Equal("UserDevice", device.GetTableName());
        Assert.NotNull(device.GetQueryFilter());
        Assert.Equal(30, device.FindProperty(nameof(UserDevice.Code))!.GetMaxLength());
        Assert.True(device.FindProperty(nameof(UserDevice.RowVersion))!.IsConcurrencyToken);
        Assert.Contains(device.GetIndexes(), i => i.IsUnique && i.GetDatabaseName() == "UQ_UserDevice_Code");
        var pin = db.Model.FindEntityType(typeof(UserPin))!;
        Assert.NotNull(pin.GetQueryFilter());
        Assert.Contains(pin.GetIndexes(), i => i.IsUnique && i.GetDatabaseName() == "UQ_UserPin_User");
        var log = db.Model.FindEntityType(typeof(IntegrationMessageLog))!;
        Assert.NotNull(log.GetQueryFilter());
        var idem = Assert.Single(log.GetIndexes(), i => i.GetDatabaseName() == "UX_IntegrationLog_Idem");
        Assert.True(idem.IsUnique);
        Assert.Equal("[IdempotencyKey] IS NOT NULL", idem.GetFilter());
        Assert.Equal(new[] { "TenantId", "UserId", "IdempotencyKey" }, idem.Properties.Select(p => p.Name));
        Assert.NotNull(db.Model.FindEntityType(typeof(RefreshToken))!.FindProperty(nameof(RefreshToken.UserDeviceId)));
        Assert.NotNull(db.Model.FindEntityType(typeof(Teikem.Domain.Tenancy.Tenant))!.FindProperty("DeviceSessionDays"));

        var sql = Structure.Value;
        Assert.Contains("CREATE TABLE dbo.UserDevice (", sql);
        Assert.Contains("CONSTRAINT UQ_UserDevice_Code UNIQUE (TenantId, Code)", sql);
        Assert.Contains("CREATE TABLE dbo.UserPin (", sql);
        Assert.Contains("CONSTRAINT UQ_UserPin_User UNIQUE (TenantId, UserId)", sql);
        Assert.Contains("DeviceSessionDays INT NOT NULL DEFAULT 30", sql);
        Assert.Contains("CREATE UNIQUE INDEX UX_IntegrationLog_Idem ON dbo.IntegrationMessageLog(TenantId, UserId, IdempotencyKey) WHERE IdempotencyKey IS NOT NULL;", sql);
        // Orden de capas: UserDevice después de RefreshToken; la FK al almacén, después de crear Warehouse.
        Assert.True(sql.IndexOf("CREATE TABLE dbo.RefreshToken (", StringComparison.Ordinal) < sql.IndexOf("CREATE TABLE dbo.UserDevice (", StringComparison.Ordinal));
        Assert.True(sql.IndexOf("CREATE TABLE dbo.UserDevice (", StringComparison.Ordinal) < sql.IndexOf("ADD CONSTRAINT FK_RefreshToken_UserDevice", StringComparison.Ordinal));
        Assert.True(sql.IndexOf("CREATE TABLE dbo.Warehouse (", StringComparison.Ordinal) < sql.IndexOf("ADD CONSTRAINT FK_UserDevice_DefaultWarehouse", StringComparison.Ordinal));
    }
}
