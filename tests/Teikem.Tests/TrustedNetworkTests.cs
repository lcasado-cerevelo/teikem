using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Teikem.Domain.Catalogs;
using Teikem.Domain.Constants;
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

/// <summary>Bitácora de seguridad en memoria para las pruebas.</summary>
internal sealed class RecordingSecurityEvents : ISecurityEventWriter
{
    public List<(string Type, string Outcome, int? UserId, string Detail)> Events { get; } = [];
    public Task WriteAsync(string eventType, string outcome, int? userId = null, int? tenantId = null, object? detail = null, CancellationToken ct = default)
    {
        Events.Add((eventType, outcome, userId, detail is null ? "" : System.Text.Json.JsonSerializer.Serialize(detail)));
        return Task.CompletedTask;
    }
}

/// <summary>
/// 2026-10-12 — IP preferida / red de confianza (docs/ip-preferida-mfa-diseno.md): la lista por compañía (alta, cambio, baja, máximo, avisos) y la regla
/// de decisión del login (las cinco reglas de la sección 3 y que la sesión nazca sin AAL2).
/// </summary>
public class TrustedNetworkTests
{
    private const string OfficeIp = "203.0.113.10";
    private const string Password = "Clave-Segura-Larga-2026";

    private static async Task<WmsFixture> CreateAsync(bool enabled = true)
    {
        var f = await WmsFixture.CreateAsync(s =>
        {
            s.AddIdentityCore<ApplicationUser>(o =>
            {
                o.Password.RequiredLength = 12; o.Password.RequireDigit = false; o.Password.RequireLowercase = false; o.Password.RequireUppercase = false;
                o.Password.RequireNonAlphanumeric = false; o.Password.RequiredUniqueChars = 4;
            }).AddRoles<ApplicationRole>().AddEntityFrameworkStores<TeikemDbContext>().AddDefaultTokenProviders();
            s.AddSingleton<RecordingSecurityEvents>();
            s.AddSingleton<ISecurityEventWriter>(sp => sp.GetRequiredService<RecordingSecurityEvents>());
            s.AddSingleton(new JwtTokenService(Options.Create(new JwtOptions { SigningKey = new string('k', 64) })));
            s.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
            s.AddSingleton<IPasswordBreachChecker, NoOpPasswordBreachChecker>();
            s.AddSingleton<DeviceService>();
            s.AddSingleton<PinService>();
            s.AddSingleton<IOptions<OnboardingOptions>>(Options.Create(new OnboardingOptions { Enabled = true }));
            s.AddSingleton<IOptions<TrustedNetworkOptions>>(Options.Create(new TrustedNetworkOptions { Enabled = enabled }));
            s.AddSingleton<NoEmailSender>();
            s.AddSingleton<ITransactionalEmailSender>(sp => sp.GetRequiredService<NoEmailSender>());
            s.AddSingleton<TrustedNetworkService>();
            s.AddSingleton<AuthService>();
        });
        f.Db.StatusCodes.Add(new StatusCode { StatusCodeId = 900, Entity = StatusDomains.MembershipStatus, InternalCode = MembershipStatuses.Active, LabelJson = "{\"es\":\"Activo\"}", IsActive = true });
        f.Db.LookupCodes.Add(new LookupCode { LookupCodeId = 950, Entity = LookupDomains.MfaFactorType, InternalCode = MfaFactorTypes.Totp, LabelJson = "{\"es\":\"TOTP\"}", IsActive = true });
        f.Db.LookupCodes.Add(new LookupCode { LookupCodeId = 951, Entity = LookupDomains.SecurityEventType, InternalCode = SecurityEventTypes.TrustedNetwork, LabelJson = "{\"es\":\"Red de confianza\"}", IsActive = true });
        f.Db.Permissions.Add(new Permission { PermissionId = 700, Code = PermissionCatalog.AdminTenant, CategoryLookupId = 950, LabelJson = "{}" });
        // 1 = administrador (el usuario del contexto); 2 = Ana (la que inicia sesión); 3 = otro administrador con correo verificado; 4 = administrador de plataforma
        foreach (var (id, mail, platform) in new[] { (1, "admin@t.local", false), (2, "ana@t.local", false), (3, "beto@t.local", false), (4, "root@t.local", true) })
        {
            var u = new ApplicationUser
            {
                Id = id, UserName = mail, NormalizedUserName = mail.ToUpperInvariant(), Email = mail, NormalizedEmail = mail.ToUpperInvariant(), FullName = mail,
                SecurityStamp = "s" + id, IsActive = true, OnboardingRequired = false, MustChangePassword = false, EmailVerifiedUtc = DateTime.UtcNow, EmailConfirmed = true,
                IsPlatformAdmin = platform,
            };
            u.PasswordHash = new PasswordHasher<ApplicationUser>().HashPassword(u, Password);
            f.Db.Users.Add(u);
        }
        await f.Db.SaveChangesAsync();
        foreach (var id in new[] { 1, 2, 3, 4 }) f.Db.UserTenants.Add(new UserTenant { UserId = id, TenantId = WmsFixture.TenantId, StatusCodeId = 900, IsDefault = true });
        foreach (var id in new[] { 1, 3 }) f.Db.UserPermissions.Add(new UserPermission { UserId = id, TenantId = WmsFixture.TenantId, PermissionId = 700 });
        await f.Db.SaveChangesAsync();
        f.Lookups.Load(f.Db.LookupCodes.IgnoreQueryFilters().AsNoTracking().ToList());
        f.Db.ChangeTracker.Clear();
        f.Tenant.IpAddress = OfficeIp;
        return f;
    }

    private static async Task<TrustedNetworkDto> AddAsync(WmsFixture f, string cidr, string name = "Almacén", Action<TrustedNetworkRequest>? _ = null)
        => await f.Get<TrustedNetworkService>().CreateAsync(new TrustedNetworkRequest(name, cidr, ConfirmInternalOnly: true), default);

    private static string CurrentCode(byte[] secret) => TotpService.ComputeCode(secret, DateTimeOffset.UtcNow.ToUnixTimeSeconds());

    /// <summary>Inscribe y confirma el TOTP de un usuario con el flujo real.</summary>
    private static async Task EnrollAsync(AuthService auth, int userId)
    {
        var enrolled = await auth.EnrollTotpAsync(userId, default);
        await auth.ConfirmTotpAsync(userId, new MfaConfirmRequest(CurrentCode(TotpService.Base32Decode(enrolled.Secret))), default);
    }

    private static Task<AuthResultDto> LoginAsync(WmsFixture f, string email = "ana@t.local")
        => f.Get<AuthService>().LoginAsync(new LoginRequest(email, Password, null, null), default);

    /// <summary>
    /// Qué pasó con el segundo paso: "mfa_required" (se pidió el código o la inscripción) o "session_issued" (no se pidió: se llegó a emitir la sesión).
    /// El proveedor en memoria no soporta <c>ExecuteUpdate</c>, con el que AuthService cierra la emisión de la sesión; llegar ahí es la prueba de que no se
    /// pidió el código, y para entonces el token de sesión ya quedó guardado (por eso se puede revisar su AAL2).
    /// </summary>
    private static async Task<string> OutcomeAsync(WmsFixture f, string email = "ana@t.local")
    {
        try { return (await LoginAsync(f, email)).Status == "mfa_required" ? "mfa_required" : "session_issued"; }
        catch (InvalidOperationException ex) when (ex.Message.Contains("ExecuteUpdate", StringComparison.Ordinal)) { return "session_issued"; }
    }

    // ================================================================ la lista

    [Fact]
    public async Task Create_normalizes_the_range_requires_the_internal_only_confirmation_and_records_who_confirmed()
    {
        await using var f = await CreateAsync();
        var svc = f.Get<TrustedNetworkService>();

        var missing = await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(new TrustedNetworkRequest("Almacén", OfficeIp), default));
        Assert.Equal(TrustedNetworkRules.NeedsInternalOnlyConfirmation, missing.Errors!["confirmInternalOnly"].Single());

        var created = await svc.CreateAsync(new TrustedNetworkRequest("  Almacén Bayamón ", "192.168.10.5/24", Note: " IP fija ", ConfirmInternalOnly: true), default);
        Assert.Equal("Almacén Bayamón", created.Name);
        Assert.Equal("192.168.10.0/24", created.Cidr);
        Assert.Equal("256", created.AddressCount);
        Assert.Equal("IP fija", created.Note);
        Assert.True(created.IsActive);
        Assert.False(created.IsExpired);
        Assert.NotNull(created.InternalOnlyConfirmedAtUtc);
        Assert.Equal("admin@t.local", created.InternalOnlyConfirmedByName);
    }

    [Fact]
    public async Task Create_validates_name_range_note_and_expiry()
    {
        await using var f = await CreateAsync();
        var svc = f.Get<TrustedNetworkService>();
        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            svc.CreateAsync(new TrustedNetworkRequest(" ", "10.0.0.0/8", ExpiresAtUtc: DateTime.UtcNow.AddDays(-1), Note: new string('n', 301), ConfirmInternalOnly: true), default));
        Assert.Contains("name", ex.Errors!.Keys);
        Assert.Contains("cidr", ex.Errors.Keys);
        Assert.Contains("note", ex.Errors.Keys);
        Assert.Contains("expiresAtUtc", ex.Errors.Keys);
        Assert.DoesNotContain("confirmInternalOnly", ex.Errors.Keys);
    }

    [Fact]
    public async Task A_duplicate_range_is_a_conflict_even_if_written_differently()
    {
        await using var f = await CreateAsync();
        await AddAsync(f, "192.168.10.0/24");
        var ex = await Assert.ThrowsAsync<ConflictException>(() => AddAsync(f, "192.168.10.77/24", "Otro nombre"));
        Assert.StartsWith(TrustedNetworkService.DuplicateMessage, ex.Message);
    }

    [Fact]
    public async Task The_maximum_is_a_setting_of_the_company_empty_means_no_limit_and_lowering_it_keeps_what_exists()
    {
        await using var f = await CreateAsync();
        var svc = f.Get<TrustedNetworkService>();
        // sin valor: sin tope
        Assert.Null((await svc.GetAsync(default)).MaxEntries);
        await AddAsync(f, "198.51.100.1", "A");
        await AddAsync(f, "198.51.100.2", "B");
        await AddAsync(f, "198.51.100.3", "C");

        var set = await svc.SetMaxEntriesAsync(2, default);
        Assert.Equal(2, set.MaxEntries);
        Assert.Equal(3, set.Items.Count); // las que ya había se conservan aunque queden por encima
        var full = await Assert.ThrowsAsync<ConflictException>(() => AddAsync(f, "198.51.100.4", "D"));
        Assert.Equal(TrustedNetworkService.LimitReachedMessage(2), full.Message);

        // subirlo o quitarlo vuelve a dejar agregar
        await svc.SetMaxEntriesAsync(null, default);
        await AddAsync(f, "198.51.100.4", "D");
        await Assert.ThrowsAsync<ValidationException>(() => svc.SetMaxEntriesAsync(-1, default));
        // 0 = no se permiten más
        await svc.SetMaxEntriesAsync(0, default);
        await Assert.ThrowsAsync<ConflictException>(() => AddAsync(f, "198.51.100.5", "E"));
    }

    [Fact]
    public async Task Update_changes_fields_toggles_active_and_clears_the_expiry_but_a_new_range_needs_the_confirmation_again()
    {
        await using var f = await CreateAsync();
        var svc = f.Get<TrustedNetworkService>();
        var entry = await svc.CreateAsync(new TrustedNetworkRequest("Almacén", "192.168.10.0/24", ExpiresAtUtc: DateTime.UtcNow.AddDays(30), ConfirmInternalOnly: true), default);
        Assert.NotNull(entry.ExpiresAtUtc);

        var renamed = await svc.UpdateAsync(entry.PublicId, new TrustedNetworkRequest("Almacén Bayamón", null, IsActive: false, ClearExpiry: true), default);
        Assert.Equal("Almacén Bayamón", renamed.Name);
        Assert.False(renamed.IsActive);
        Assert.Null(renamed.ExpiresAtUtc);
        Assert.Equal("192.168.10.0/24", renamed.Cidr);

        // otro rango: exige confirmar de nuevo
        var needs = await Assert.ThrowsAsync<ValidationException>(() => svc.UpdateAsync(entry.PublicId, new TrustedNetworkRequest(null, "192.168.20.0/24"), default));
        Assert.Contains("confirmInternalOnly", needs.Errors!.Keys);
        var moved = await svc.UpdateAsync(entry.PublicId, new TrustedNetworkRequest(null, "192.168.20.0/24", ConfirmInternalOnly: true), default);
        Assert.Equal("192.168.20.0/24", moved.Cidr);
        // el mismo rango escrito igual no pide confirmar
        await svc.UpdateAsync(entry.PublicId, new TrustedNetworkRequest(null, "192.168.20.9/24"), default);

        await Assert.ThrowsAsync<NotFoundException>(() => svc.UpdateAsync(Guid.NewGuid(), new TrustedNetworkRequest("x", null), default));
    }

    [Fact]
    public async Task Delete_removes_the_entry()
    {
        await using var f = await CreateAsync();
        var svc = f.Get<TrustedNetworkService>();
        var entry = await AddAsync(f, OfficeIp);
        await svc.DeleteAsync(entry.PublicId, default);
        Assert.Empty((await svc.GetAsync(default)).Items);
        await Assert.ThrowsAsync<NotFoundException>(() => svc.DeleteAsync(entry.PublicId, default));
    }

    [Fact]
    public async Task Get_tells_the_current_ip_and_which_entry_covers_it()
    {
        await using var f = await CreateAsync();
        var svc = f.Get<TrustedNetworkService>();
        var none = await svc.GetAsync(default);
        Assert.Equal(OfficeIp, none.CurrentIp);
        Assert.Null(none.CurrentIpNetworkName);
        Assert.True(none.FeatureEnabled);

        await AddAsync(f, "203.0.113.0/24", "Oficina");
        await AddAsync(f, "198.51.100.1", "Otra");
        var got = await svc.GetAsync(default);
        Assert.Equal("Oficina", got.CurrentIpNetworkName);
        Assert.True(got.Items.Single(i => i.Name == "Oficina").ContainsCurrentIp);
        Assert.False(got.Items.Single(i => i.Name == "Otra").ContainsCurrentIp);
        // una entrada desactivada o vencida no cuenta como «la que la cubre»
        await svc.UpdateAsync(got.Items.Single(i => i.Name == "Oficina").PublicId, new TrustedNetworkRequest(null, null, IsActive: false), default);
        Assert.Null((await svc.GetAsync(default)).CurrentIpNetworkName);
    }

    [Fact]
    public async Task Changes_are_recorded_in_the_security_log_and_mailed_to_the_company_administrators()
    {
        await using var f = await CreateAsync();
        var mail = f.Get<NoEmailSender>();
        var log = f.Get<RecordingSecurityEvents>();
        var entry = await AddAsync(f, "192.168.10.0/24", "Almacén <Bayamón>");
        await f.Get<TrustedNetworkService>().SetMaxEntriesAsync(5, default);
        await f.Get<TrustedNetworkService>().DeleteAsync(entry.PublicId, default);

        var events = log.Events.Where(e => e.Type == SecurityEventTypes.TrustedNetwork).ToList();
        Assert.Equal(3, events.Count);
        Assert.All(events, e => Assert.Equal(SecurityOutcomes.Success, e.Outcome));
        Assert.Contains("\"action\":\"created\"", events[0].Detail);
        Assert.Contains("192.168.10.0/24", events[0].Detail);
        Assert.Contains("limit_changed", events[1].Detail);
        Assert.Contains("\"action\":\"deleted\"", events[2].Detail);

        // los dos administradores con correo verificado (el 1, que lo hizo, y el 3) reciben cada aviso; Ana (sin admin.tenant) no
        Assert.Equal(6, mail.Sent.Count);
        Assert.Equal(new[] { "admin@t.local", "beto@t.local" }, mail.Sent.Select(s => s.To).Distinct().OrderBy(x => x).ToArray());
        Assert.DoesNotContain(mail.Sent, s => s.To == "ana@t.local");
        var first = mail.Sent[0];
        Assert.Contains("Se agregó una IP preferida", first.Subject);
        Assert.Contains("&lt;Bay", first.Html);                         // el nombre va escapado (no entra HTML)
        Assert.DoesNotContain("<Bay", first.Html);
        Assert.Contains("192.168.10.0/24", first.Html);
        Assert.Contains("admin@t.local", first.Html);                  // quién lo hizo
        Assert.Contains(OfficeIp, first.Html);                         // desde qué IP
    }

    [Fact]
    public void The_notice_speaks_the_company_language()
    {
        var (es, esHtml) = TrustedNetworkService.BuildEmail(false, "Advance", "deactivated", "Oficina", "10.1.0.0/24", null, "Ana", "1.2.3.4", new DateTime(2026, 10, 12, 15, 0, 0, DateTimeKind.Utc));
        Assert.Equal("Se desactivó una IP preferida — Advance", es);
        Assert.Contains("Lo hizo", esHtml);
        var (en, enHtml) = TrustedNetworkService.BuildEmail(true, "Advance", "limit_changed", null, null, "3 → 5", "Ana", null, DateTime.UtcNow);
        Assert.Equal("The preferred IP entry limit was changed — Advance", en);
        Assert.Contains("Done by", enHtml);
        Assert.Contains("3 → 5", enHtml);
    }

    [Fact]
    public async Task A_failing_mail_never_undoes_the_change()
    {
        await using var f = await CreateAsync();
        var svc = new TrustedNetworkService(f.Db, f.Tenant, f.Get<ISecurityEventWriter>(), Options.Create(new TrustedNetworkOptions { Enabled = true }), new ThrowingEmailSender());
        var created = await svc.CreateAsync(new TrustedNetworkRequest("Almacén", "192.168.10.0/24", ConfirmInternalOnly: true), default);
        Assert.Equal("192.168.10.0/24", created.Cidr);
        Assert.Single((await svc.GetAsync(default)).Items);
    }

    private sealed class ThrowingEmailSender : ITransactionalEmailSender
    {
        public bool IsConfigured => true;
        public Task SendAsync(string toEmail, string? toName, string subject, string htmlContent, CancellationToken ct, string? companyName = null) => throw new InvalidOperationException("sin correo");
    }

    // ================================================================ coincidencia

    [Fact]
    public async Task Match_only_counts_active_unexpired_entries_of_the_same_company_and_only_with_the_global_switch_on()
    {
        await using var f = await CreateAsync();
        var svc = f.Get<TrustedNetworkService>();
        await AddAsync(f, "203.0.113.0/24", "Oficina");
        Assert.Equal("Oficina", (await svc.MatchAsync(WmsFixture.TenantId, "203.0.113.77", default))!.Name);
        Assert.Equal("Oficina", (await svc.MatchAsync(WmsFixture.TenantId, "::ffff:203.0.113.77", default))!.Name); // IPv4 visto como IPv6
        Assert.Null(await svc.MatchAsync(WmsFixture.TenantId, "203.0.114.1", default));
        Assert.Null(await svc.MatchAsync(WmsFixture.TenantId, null, default));
        Assert.Null(await svc.MatchAsync(WmsFixture.TenantId, "basura", default));
        // otra compañía no ve la lista de ésta
        Assert.Null(await svc.MatchAsync(WmsFixture.OtherTenantId, "203.0.113.77", default));

        // vencida: sin tocar el reloj, se guarda con fecha pasada directamente
        var row = await f.Db.TenantTrustedNetworks.AsTracking().SingleAsync();
        row.ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1);
        await f.Db.SaveChangesAsync();
        Assert.Null(await svc.MatchAsync(WmsFixture.TenantId, "203.0.113.77", default));

        // interruptor global apagado
        await using var off = await CreateAsync(enabled: false);
        await AddAsync(off, "203.0.113.0/24", "Oficina");
        Assert.Null(await off.Get<TrustedNetworkService>().MatchAsync(WmsFixture.TenantId, "203.0.113.77", default));
    }

    // ================================================================ el login (docs §3)

    [Fact]
    public async Task From_a_trusted_ip_a_person_with_MFA_enrolled_gets_in_without_the_code_and_the_session_has_no_AAL2()
    {
        await using var f = await CreateAsync();
        var auth = f.Get<AuthService>();
        await EnrollAsync(auth, 2);
        // sin la IP en la lista: pide el código
        Assert.Equal("mfa_required", await OutcomeAsync(f));

        await AddAsync(f, "203.0.113.0/24", "Oficina");
        Assert.Equal("session_issued", await OutcomeAsync(f));
        // la sesión nace sin verificación AAL2: las acciones sensibles ([RequireAal2]) siguen pidiendo MFA
        var rt = await f.Db.RefreshTokens.IgnoreQueryFilters().AsNoTracking().SingleAsync(r => r.UserId == 2);
        Assert.Null(rt.Aal2VerifiedAtUtc);
        // y queda anotado en la bitácora
        var login = f.Get<RecordingSecurityEvents>().Events.Last(e => e.Type == SecurityEventTypes.Login);
        Assert.Contains("skipped_trusted_network", login.Detail);
        Assert.Contains("Oficina", login.Detail);
    }

    [Fact]
    public async Task The_password_is_always_verified_even_from_a_trusted_ip()
    {
        await using var f = await CreateAsync();
        await EnrollAsync(f.Get<AuthService>(), 2);
        await AddAsync(f, OfficeIp);
        await Assert.ThrowsAsync<UnauthorizedException>(() => f.Get<AuthService>().LoginAsync(new LoginRequest("ana@t.local", "mala-clave-larga-1", null, null), default));
    }

    [Fact]
    public async Task Other_ips_inactive_expired_or_switched_off_still_ask_for_the_code()
    {
        await using var f = await CreateAsync();
        await EnrollAsync(f.Get<AuthService>(), 2);
        var entry = await AddAsync(f, "198.51.100.0/24", "Otra red");            // la IP del pedido no está
        Assert.Equal("mfa_required", await OutcomeAsync(f));

        var mine = await AddAsync(f, OfficeIp, "Oficina");
        Assert.Equal("session_issued", await OutcomeAsync(f));
        await f.Get<TrustedNetworkService>().UpdateAsync(mine.PublicId, new TrustedNetworkRequest(null, null, IsActive: false), default);  // desactivada
        Assert.Equal("mfa_required", await OutcomeAsync(f));
        await f.Get<TrustedNetworkService>().UpdateAsync(mine.PublicId, new TrustedNetworkRequest(null, null, IsActive: true), default);
        Assert.Equal("session_issued", await OutcomeAsync(f));
        var row = await f.Db.TenantTrustedNetworks.AsTracking().SingleAsync(n => n.PublicId == mine.PublicId);                          // vencida
        row.ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-5);
        await f.Db.SaveChangesAsync();
        Assert.Equal("mfa_required", await OutcomeAsync(f));
        Assert.NotNull(entry);

        await using var off = await CreateAsync(enabled: false);                                                                      // interruptor global apagado
        await EnrollAsync(off.Get<AuthService>(), 2);
        await AddAsync(off, OfficeIp);
        Assert.Equal("mfa_required", await OutcomeAsync(off));
    }

    [Fact]
    public async Task The_individual_always_MFA_mark_and_platform_administrators_are_never_skipped()
    {
        await using var f = await CreateAsync();
        var auth = f.Get<AuthService>();
        await EnrollAsync(auth, 2);
        await EnrollAsync(auth, 4);
        await AddAsync(f, OfficeIp);
        Assert.Equal("session_issued", await OutcomeAsync(f));

        // marca individual «siempre MFA» de la membresía de Ana (regla 2)
        var m = await f.Db.UserTenants.AsTracking().IgnoreQueryFilters().SingleAsync(x => x.UserId == 2 && x.TenantId == WmsFixture.TenantId);
        m.MfaRequired = true;
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        Assert.Equal("mfa_required", await OutcomeAsync(f));

        // administrador de plataforma (regla 1)
        Assert.Equal("mfa_required", await OutcomeAsync(f, "root@t.local"));
    }

    [Fact]
    public async Task Without_MFA_enrolled_the_ip_does_not_skip_the_enrollment()
    {
        await using var f = await CreateAsync();
        await AddAsync(f, OfficeIp);
        var result = await LoginAsync(f); // Ana nunca inscribió su MFA y la compañía lo exige (por defecto)
        Assert.Equal("mfa_required", result.Status);
        Assert.True(result.MfaEnrollmentRequired);
        Assert.Null(result.Tokens);
    }

    [Fact]
    public async Task A_range_matches_every_address_inside_and_ipv6_clients_use_their_own_entries()
    {
        await using var f = await CreateAsync();
        await EnrollAsync(f.Get<AuthService>(), 2);
        await AddAsync(f, "2001:db8:abcd::/48", "Oficina v6");
        f.Tenant.IpAddress = "2001:db8:abcd:1::25";
        Assert.Equal("session_issued", await OutcomeAsync(f));
        f.Tenant.IpAddress = "2001:db8:abce::1";
        Assert.Equal("mfa_required", await OutcomeAsync(f));
        f.Tenant.IpAddress = OfficeIp; // un cliente IPv4 no coincide con una entrada solo IPv6
        Assert.Equal("mfa_required", await OutcomeAsync(f));
    }

    // ================================================================ una sola sesión por persona y navegador (2026-10-12)

    private static async Task<WmsFixture> SessionFixtureAsync()
    {
        var f = await CreateAsync(enabled: false);
        var t = await f.Db.Tenants.SingleAsync(x => x.TenantId == WmsFixture.TenantId);
        t.MfaRequired = false;   // el inicio de sesión llega a emitir la sesión sin pedir MFA
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        return f;
    }

    private static async Task LoginDeviceAsync(WmsFixture f, string email, string? deviceInfo)
    {
        try { await f.Get<AuthService>().LoginAsync(new LoginRequest(email, Password, null, deviceInfo), default); }
        catch (InvalidOperationException ex) when (ex.Message.Contains("ExecuteUpdate", StringComparison.Ordinal)) { /* la sesión ya quedó guardada */ }
    }

    private static async Task<int> ActiveSessionsAsync(WmsFixture f, int userId)
    {
        f.Db.ChangeTracker.Clear();
        var now = DateTime.UtcNow;
        return await f.Db.RefreshTokens.IgnoreQueryFilters().AsNoTracking().CountAsync(t => t.UserId == userId && t.RevokedAtUtc == null && t.ExpiresAtUtc > now);
    }

    [Fact]
    public async Task Signing_in_again_from_the_same_browser_closes_the_previous_session_of_that_person_only()
    {
        await using var f = await SessionFixtureAsync();
        f.Tenant.IpAddress = "198.51.100.7";
        await LoginDeviceAsync(f, "ana@t.local", "Chrome · Windows");
        await LoginDeviceAsync(f, "beto@t.local", "Chrome · Windows");   // otra persona, mismo navegador: no se toca
        Assert.Equal(1, await ActiveSessionsAsync(f, 2));

        await LoginDeviceAsync(f, "ana@t.local", "Chrome · Windows");     // otra vez Ana, mismo navegador y misma IP
        await LoginDeviceAsync(f, "ana@t.local", "Chrome · Windows");
        Assert.Equal(1, await ActiveSessionsAsync(f, 2));                 // siempre una sola
        Assert.Equal(1, await ActiveSessionsAsync(f, 3));                 // la de Beto sigue viva
        f.Db.ChangeTracker.Clear();
        Assert.Equal(2, await f.Db.RefreshTokens.IgnoreQueryFilters().AsNoTracking().CountAsync(t => t.UserId == 2 && t.RevokedAtUtc != null));   // las anteriores quedaron REVOCADAS (historial)
    }

    [Fact]
    public async Task Another_browser_another_ip_or_no_browser_info_keeps_the_other_sessions()
    {
        await using var f = await SessionFixtureAsync();
        f.Tenant.IpAddress = "198.51.100.7";
        await LoginDeviceAsync(f, "ana@t.local", "Chrome · Windows");
        await LoginDeviceAsync(f, "ana@t.local", "Chrome · Android");                // otro navegador/sistema
        Assert.Equal(2, await ActiveSessionsAsync(f, 2));
        f.Tenant.IpAddress = "203.0.113.99";
        await LoginDeviceAsync(f, "ana@t.local", "Chrome · Windows");                // mismo navegador desde otra IP (otra red)
        Assert.Equal(3, await ActiveSessionsAsync(f, 2));
        await LoginDeviceAsync(f, "ana@t.local", null);                               // sin dato del navegador: no se adivina cuál sería la misma
        await LoginDeviceAsync(f, "ana@t.local", null);
        Assert.Equal(5, await ActiveSessionsAsync(f, 2));
    }
}
