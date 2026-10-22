using System.IdentityModel.Tokens.Jwt;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Teikem.Domain.Catalogs;
using Teikem.Domain.Clients;
using Teikem.Domain.Constants;
using Teikem.Domain.Contacts;
using Teikem.Domain.Identity;
using Teikem.Domain.Security;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 117 — acceso del consignatario (docs/p10-portal-diseno.md): el cliente lo invita por correo; el consignatario entra a su propio espacio (sin MFA, con computadora de confianza), apaga o vuelve a prender
/// lo que él apagó, ve la bandeja de lo que se le avisó y no toca nada que no sea suyo. Las rutas del cliente le responden 403.
/// </summary>
public class ConsigneeAccessTests
{
    private const string Password = "Clave-Portal-2026x";
    private const string Browser = "dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd";
    private const int ClientA = 501, ClientB = 502;
    private const int Admin = 21, ReadOnly = 24, Tienda = 25, Colmado = 26, OtroCliente = 27;       // usuarios de portal
    private const int LocTienda = 7001, LocColmado = 7002, LocOtro = 7003;

    private sealed class FakeInvites : IInvitationSender
    {
        public List<(string Email, string Token, string Client)> Sent { get; } = new();
        public Task SendInviteAsync(string email, string token, string clientName, DateTime expiresAtUtc, CancellationToken ct) { Sent.Add((email, token, clientName)); return Task.CompletedTask; }
    }

    private sealed record World(WmsFixture F, AuthService Auth, TrustedBrowserService Browsers, NoEmailSender Mail, FakeInvites Invites, Guid LocTiendaId, Guid LocColmadoId, Guid LocOtroId, Guid ClientAId) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => F.DisposeAsync();
        public ConsigneeAccessService Access => F.Get<ConsigneeAccessService>();
        public ConsigneeSelfService Self => F.Get<ConsigneeSelfService>();

        /// <summary>La sesión de una persona del cliente A.</summary>
        public void AsClient(int userId)
        {
            F.Tenant.IsPlatformAdmin = false;
            F.Tenant.UserId = userId; F.Tenant.IsPortal = true; F.Tenant.PortalClientId = ClientA; F.Tenant.PortalLocationId = null; F.Tenant.ScopedClientIds = new[] { ClientA };
            F.Db.ChangeTracker.Clear();
        }

        /// <summary>La sesión de un consignatario (su cliente y su localización salen de los claims).</summary>
        public void AsConsignee(int userId, int clientId, int locationId)
        {
            F.Tenant.IsPlatformAdmin = false;
            F.Tenant.UserId = userId; F.Tenant.IsPortal = true; F.Tenant.PortalClientId = clientId; F.Tenant.PortalLocationId = locationId; F.Tenant.ScopedClientIds = new[] { clientId };
            F.Db.ChangeTracker.Clear();
        }
    }

    private static async Task<World> WorldAsync()
    {
        var mail = new NoEmailSender();
        var invites = new FakeInvites();
        var f = await WmsFixture.CreateAsync(s =>
        {
            s.AddIdentityCore<ApplicationUser>().AddRoles<ApplicationRole>().AddEntityFrameworkStores<TeikemDbContext>().AddDefaultTokenProviders();
            s.Configure<IdentityOptions>(o => { o.Lockout.MaxFailedAccessAttempts = 5; o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15); });
            s.AddSingleton<ISecurityEventWriter>(_ => new ReceivingNullSecurityEventWriter());
            s.AddSingleton(new JwtTokenService(Options.Create(new JwtOptions { SigningKey = new string('k', 64) })));
            s.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
            s.AddSingleton<IPasswordBreachChecker, NoOpPasswordBreachChecker>();
            s.AddSingleton<DeviceService>();
            s.AddSingleton<PinService>();
            s.AddSingleton<IOptions<OnboardingOptions>>(Options.Create(new OnboardingOptions { Enabled = false }));
            s.AddSingleton<IOptions<PasswordResetOptions>>(Options.Create(new PasswordResetOptions { WebBaseUrl = "https://portal.test" }));
            s.AddSingleton<IOptions<TrustedBrowserOptions>>(Options.Create(new TrustedBrowserOptions { Enabled = false }));
            s.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
                { ["Portal:ReturnInviteTokenInResponse"] = "true", ["Portal:WebBaseUrl"] = "https://portal-real.test", [ConsigneeNoticeService.BaseUrlKey] = "https://app.example.com" }).Build());
            s.AddSingleton(mail);
            s.AddSingleton<ITransactionalEmailSender>(mail);
            s.AddSingleton<IInvitationSender>(invites);
            s.AddSingleton<ITenantClock>(TenantClock.Default);
            s.AddSingleton<ModuleService>();
            s.AddSingleton<TrustedBrowserService>();
            s.AddSingleton<ITurnstileVerifier>(new NoCaptcha());
            s.AddSingleton<AuthService>();
            s.AddSingleton<PortalUserService>();
            s.AddSingleton<ConsigneeNoticeService>();
            s.AddSingleton<ConsigneeAccessService>();
            s.AddSingleton<ConsigneeSelfService>();
        });
        var db = f.Db;
        db.StatusCodes.Add(new StatusCode { StatusCodeId = 900, Entity = StatusDomains.MembershipStatus, InternalCode = MembershipStatuses.Active, LabelJson = "{\"es\":\"Activo\"}", IsActive = true });
        db.LookupCodes.AddRange(
            new LookupCode { LookupCodeId = 950, Entity = LookupDomains.MfaFactorType, InternalCode = MfaFactorTypes.Totp, LabelJson = "{\"es\":\"TOTP\"}", IsActive = true },
            new LookupCode { LookupCodeId = 951, Entity = LookupDomains.UserKind, InternalCode = UserKinds.Portal, LabelJson = "{\"es\":\"Portal\"}", IsActive = true },
            new LookupCode { LookupCodeId = 952, Entity = LookupDomains.UserKind, InternalCode = UserKinds.Internal, LabelJson = "{\"es\":\"Interno\"}", IsActive = true },
            new LookupCode { LookupCodeId = 960, Entity = LookupDomains.PortalRole, InternalCode = PortalRoles.ClientAdmin, LabelJson = "{\"es\":\"Admin\"}", IsActive = true },
            new LookupCode { LookupCodeId = 961, Entity = LookupDomains.PortalRole, InternalCode = PortalRoles.ClientReadOnly, LabelJson = "{\"es\":\"Solo lectura\"}", IsActive = true },
            new LookupCode { LookupCodeId = 962, Entity = LookupDomains.PortalRole, InternalCode = PortalRoles.Consignee, LabelJson = "{\"es\":\"Consignatario\"}", IsActive = true },
            new LookupCode { LookupCodeId = 9001, Entity = "ContactType", InternalCode = "EMAIL", LabelJson = "{}", IsActive = true },
            new LookupCode { LookupCodeId = 9002, Entity = "ContactType", InternalCode = "MOBILE", LabelJson = "{}", IsActive = true });
        var a = new Client { ClientId = ClientA, PublicId = Guid.NewGuid(), TenantId = WmsFixture.TenantId, Code = "A", Name = "Cliente A", IsActive = true, StatusCodeId = 900, NotifyConsignee = true };
        var b = new Client { ClientId = ClientB, PublicId = Guid.NewGuid(), TenantId = WmsFixture.TenantId, Code = "B", Name = "Cliente B", IsActive = true, StatusCodeId = 900 };
        db.Clients.AddRange(a, b);
        ApplicationUser U(int id, string mailAddr) => new()
        {
            Id = id, UserName = mailAddr, NormalizedUserName = mailAddr.ToUpperInvariant(), Email = mailAddr, NormalizedEmail = mailAddr.ToUpperInvariant(), FullName = mailAddr, SecurityStamp = "s" + id, IsActive = true,
            LockoutEnabled = true, OnboardingRequired = false, MustChangePassword = false, EmailVerifiedUtc = DateTime.UtcNow, UserKindLookupId = 951,
        };
        var users = new[] { U(Admin, "ana@cliente.co"), U(ReadOnly, "rita@cliente.co"), U(Tienda, "tienda@norte.co"), U(Colmado, "colmado@sur.co"), U(OtroCliente, "otro@b.co") };
        foreach (var u in users) u.PasswordHash = new PasswordHasher<ApplicationUser>().HashPassword(u, Password);
        db.Users.AddRange(users);
        Location L(int id, int client, string name) => new()
        {
            LocationId = id, PublicId = Guid.NewGuid(), TenantId = WmsFixture.TenantId, ClientId = client, Name = name, City = "Ponce", Line1 = "Calle 1",
            LocationTypeLookupId = f.LookupId(LookupDomains.LocationType, LocationTypes.Delivery), IsActive = true,
        };
        var tienda = L(LocTienda, ClientA, "Tienda Norte"); tienda.NoticeEmailOptIn = true;
        var colmado = L(LocColmado, ClientA, "Colmado Sur");
        var otro = L(LocOtro, ClientB, "Tienda de B");
        db.Locations.AddRange(tienda, colmado, otro);
        var ownerType = f.LookupId(LookupDomains.EntityType, EntityTypes.Location);
        db.ContactPoints.AddRange(
            new ContactPoint { TenantId = WmsFixture.TenantId, OwnerEntityLookupId = ownerType, OwnerId = LocTienda, ContactTypeLookupId = 9001, Value = "tienda@norte.co", IsPrimary = true, IsActive = true },
            new ContactPoint { TenantId = WmsFixture.TenantId, OwnerEntityLookupId = ownerType, OwnerId = LocTienda, ContactTypeLookupId = 9002, Value = "787-555-0100", IsPrimary = false, IsActive = true });
        PortalUser P(int id, int client, int user, int role, int? loc = null) => new()
        {
            PortalUserId = id, TenantId = WmsFixture.TenantId, ClientId = client, UserId = user, Email = users.First(x => x.Id == user).Email!, RoleLookupId = role, StatusCodeId = f.StatusId(StatusDomains.PortalUserStatus, PortalUserStatuses.Active), IsActive = true, LocationId = loc,
        };
        db.PortalUsers.AddRange(P(1, ClientA, Admin, 960), P(2, ClientA, ReadOnly, 961), P(3, ClientA, Tienda, 962, LocTienda), P(4, ClientA, Colmado, 962, LocColmado), P(5, ClientB, OtroCliente, 962, LocOtro));
        foreach (var tn in await db.Tenants.IgnoreQueryFilters().ToListAsync()) tn.MfaRequired = false;
        await db.SaveChangesAsync();
        f.Lookups.Load(db.LookupCodes.IgnoreQueryFilters().AsNoTracking().ToList());
        db.ChangeTracker.Clear();
        return new World(f, f.Get<AuthService>(), f.Get<TrustedBrowserService>(), mail, invites, tienda.PublicId, colmado.PublicId, otro.PublicId, a.PublicId);
    }

    private sealed class NoCaptcha : ITurnstileVerifier
    {
        public bool Enabled => false;
        public int RequireAfterFailures => 3;
        public string? SiteKey => null;
        public Task<bool> VerifyAsync(string? token, string? remoteIp, CancellationToken ct) => Task.FromResult(true);
    }

    // ---------------------------------------------------------------- rutas y token

    [Fact]
    public void A_consignee_session_only_reaches_its_own_space_the_login_and_the_session_routes()
    {
        foreach (var ok in new[] { "/api/v1/portal/consignee/me", "/api/v1/portal/consignee/notices", "/api/v1/portal/consignee/me/notices/EMAIL", "/api/v1/portal/auth/login", "/api/v1/auth/refresh", "/api/v1/auth/logout", "/api/v1/auth/trusted-browsers", "/api/v1/legal/accept", "/index.html" })
            Assert.True(PortalScopeRules.IsAllowedForConsignee(ok), ok);
        foreach (var no in new[] { "/api/v1/portal/me", "/api/v1/portal/orders", "/api/v1/portal/messages", "/api/v1/portal/consignee-notices", "/api/v1/portal/consignee-access/abc", "/api/v1/portal/order-form", "/api/v1/orders", "/api/v1/auth/switch-tenant" })
            Assert.False(PortalScopeRules.IsAllowedForConsignee(no), no);
        // la persona del cliente sigue viendo todo el portal (y las rutas nuevas)
        Assert.True(PortalScopeRules.IsAllowedForPortal("/api/v1/portal/consignee-access/abc"));
        Assert.True(PortalScopeRules.IsAllowedForPortal("/api/v1/portal/consignee/me"));
        Assert.True(LegalRules.IsExemptPath("/api/v1/portal/consignee/me"));
        Assert.False(LegalRules.IsExemptPath("/api/v1/portal/consignee/notices"));
    }

    [Fact]
    public void The_access_token_of_a_consignee_carries_the_location_and_the_others_do_not()
    {
        var jwt = new JwtTokenService(Options.Create(new JwtOptions { SigningKey = new string('k', 64) }));
        var user = new ApplicationUser { Id = 5, Email = "p@c.co", FullName = "Portal", SecurityStamp = "s" };
        var withLoc = new JwtSecurityTokenHandler().ReadJwtToken(jwt.CreateAccessToken(user, 1, 10, UserKinds.Portal, null, portalClientId: 77, portalLocationId: 88).Token);
        Assert.Equal("88", withLoc.Claims.Single(c => c.Type == TeikemClaims.LocationId).Value);
        Assert.Equal("77", withLoc.Claims.Single(c => c.Type == TeikemClaims.ClientId).Value);
        var person = new JwtSecurityTokenHandler().ReadJwtToken(jwt.CreateAccessToken(user, 1, 11, UserKinds.Portal, null, portalClientId: 77).Token);
        Assert.DoesNotContain(person.Claims, c => c.Type == TeikemClaims.LocationId);
    }

    // ---------------------------------------------------------------- invitar (el cliente)

    [Fact]
    public async Task The_client_invites_the_consignee_with_its_registered_email_and_the_row_is_a_consignee_one()
    {
        await using var w = await WorldAsync();
        w.AsClient(Admin);
        var r = await w.Access.InviteAsync(w.LocColmadoId, new ConsigneeAccessInviteRequest("colmado.nuevo@sur.co", "Doña Ana"), default);
        Assert.Equal("colmado.nuevo@sur.co", r.Access.Email);
        Assert.Equal(PortalUserStatuses.Invited, r.Access.Status);
        Assert.NotNull(r.InviteToken);
        Assert.Equal(("colmado.nuevo@sur.co", "Cliente A"), (w.Invites.Sent.Single().Email, w.Invites.Sent.Single().Client));
        var row = await w.F.Db.PortalUsers.AsNoTracking().SingleAsync(p => p.Email == "colmado.nuevo@sur.co");
        Assert.Equal(LocColmado, row.LocationId);
        Assert.Equal(ClientA, row.ClientId);
        Assert.Equal(PortalRoles.Consignee, (await w.F.Db.LookupCodes.AsNoTracking().SingleAsync(l => l.LookupCodeId == row.RoleLookupId)).InternalCode);

        // sin correo escrito usa el principal que el consignatario ya tiene registrado (el de Tienda Norte ya tiene acceso: mismo 409 neutro)
        var dup = await Assert.ThrowsAsync<ConflictException>(() => w.Access.InviteAsync(w.LocTiendaId, new ConsigneeAccessInviteRequest(null, null), default));
        Assert.Equal(PortalUserService.DuplicateEmail, dup.Message);
        var list = await w.Access.ListAsync(w.LocColmadoId, default);
        Assert.Contains(list, x => x.Email == "colmado.nuevo@sur.co" && x.Status == PortalUserStatuses.Invited);
        Assert.Contains(list, x => x.Email == "colmado@sur.co" && x.Status == PortalUserStatuses.Active);
    }

    [Fact]
    public async Task Without_a_typed_email_and_without_a_registered_one_it_asks_for_it()
    {
        await using var w = await WorldAsync();
        w.AsClient(Admin);
        var ex = await Assert.ThrowsAsync<ValidationException>(() => w.Access.InviteAsync(w.LocColmadoId, new ConsigneeAccessInviteRequest("  ", null), default));
        Assert.Equal(ConsigneeAccessService.NoEmailMessage, ex.Message);
        Assert.Empty(w.Invites.Sent);
    }

    [Fact]
    public async Task Only_admins_and_operators_invite_and_a_consignee_of_another_client_is_not_found()
    {
        await using var w = await WorldAsync();
        w.AsClient(ReadOnly);
        var ro = await Assert.ThrowsAsync<ForbiddenException>(() => w.Access.InviteAsync(w.LocColmadoId, new ConsigneeAccessInviteRequest("x@y.co", null), default));
        Assert.Equal(ConsigneeAccessService.ReadOnlyMessage, ro.Message);
        Assert.Single(await w.Access.ListAsync(w.LocColmadoId, default));   // quien solo consulta sí ve quién tiene acceso

        w.AsClient(Admin);
        await Assert.ThrowsAsync<NotFoundException>(() => w.Access.InviteAsync(w.LocOtroId, new ConsigneeAccessInviteRequest("x@y.co", null), default));
        await Assert.ThrowsAsync<NotFoundException>(() => w.Access.ListAsync(w.LocOtroId, default));
        Assert.Empty(w.Invites.Sent);
    }

    [Fact]
    public async Task The_consignee_role_can_only_be_given_from_a_consignee_never_as_a_plain_portal_user()
    {
        await using var w = await WorldAsync();
        w.AsClient(Admin);
        var svc = w.F.Get<PortalUserService>();
        var ex = await Assert.ThrowsAsync<ValidationException>(() => svc.InviteAsync(w.ClientAId, new PortalInviteRequest("nuevo@x.co", null, PortalRoles.Consignee), default));
        Assert.Equal(PortalUserService.ConsigneeRoleMessage, ex.Message);
        var ex2 = await Assert.ThrowsAsync<ValidationException>(() => svc.InviteAsync(w.ClientAId, new PortalInviteRequest("nuevo@x.co", null, PortalRoles.ClientAdmin), default, LocColmado));
        Assert.Equal(PortalUserService.ConsigneeRoleMessage, ex2.Message);
    }

    [Fact]
    public async Task Resend_kills_the_previous_link_and_a_removed_access_leaves_the_list()
    {
        await using var w = await WorldAsync();
        w.AsClient(Admin);
        var first = await w.Access.InviteAsync(w.LocColmadoId, new ConsigneeAccessInviteRequest("colmado.nuevo@sur.co", null), default);
        w.AsClient(Admin);
        var again = await w.Access.ResendAsync(w.LocColmadoId, first.Access.Id, default);
        Assert.NotEqual(first.InviteToken, again.InviteToken);
        Assert.Equal(2, w.Invites.Sent.Count);

        // una persona de otro consignatario no se reenvía ni se quita desde éste
        w.AsClient(Admin);
        await Assert.ThrowsAsync<NotFoundException>(() => w.Access.ResendAsync(w.LocTiendaId, first.Access.Id, default));
        await Assert.ThrowsAsync<NotFoundException>(() => w.Access.RemoveAsync(w.LocTiendaId, first.Access.Id, default));

        await w.Access.RemoveAsync(w.LocColmadoId, first.Access.Id, default);
        w.AsClient(Admin);
        Assert.DoesNotContain(await w.Access.ListAsync(w.LocColmadoId, default), x => x.Email == "colmado.nuevo@sur.co");
        Assert.Equal(PortalUserStatuses.Disabled, (await w.F.Db.PortalUsers.AsNoTracking().Include(p => p.Status).SingleAsync(p => p.Email == "colmado.nuevo@sur.co")).Status!.InternalCode);
    }

    // ---------------------------------------------------------------- entrar (el consignatario)

    private static PortalLoginRequest Login(string mail) => new(mail, Password, null, null, Browser, null, false);

    private static async Task TrustTheComputerAsync(World w, string mail)
    {
        w.F.Tenant.UserAgent = "Mozilla/5.0 (Windows NT 10.0) Chrome/120.0 Safari/537.36";
        var first = await w.Auth.PortalLoginAsync(Login(mail), default);
        Assert.Equal("browser_confirmation_required", first.Status);   // el consignatario también confirma la computadora nueva por correo
        Assert.Contains("https://portal-real.test/confirm-browser?token=", w.Mail.Sent.Last().Html);   // 2026-10-22: el enlace va al PORTAL, no a la web del equipo (https://portal.test)
        await w.Browsers.ConfirmAsync(Regex.Match(w.Mail.Sent.Last().Html, "token=([0-9a-f]{64})").Groups[1].Value, default);
        w.F.Db.ChangeTracker.Clear();
    }

    [Fact]
    public async Task The_consignee_with_a_trusted_computer_is_not_asked_for_the_mfa_and_goes_straight_to_the_session()
    {
        await using var w = await WorldAsync();
        await TrustTheComputerAsync(w, "tienda@norte.co");
        // Sin MFA ni inscripción: el ingreso llega a emitir la sesión. (La base en memoria no traduce el UPDATE de «último ingreso» que hace IssueAsync, y ahí se detiene la prueba;
        // la sesión completa —claims cid y loc, renovación— se comprueba contra SQL real.)
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => w.Auth.PortalLoginAsync(Login("tienda@norte.co"), default));
        Assert.Contains("ExecuteUpdate", ex.Message);
    }

    [Fact]
    public async Task A_person_of_the_client_still_goes_through_mfa_and_has_no_location_claim()
    {
        await using var w = await WorldAsync();
        await TrustTheComputerAsync(w, "ana@cliente.co");
        var next = await w.Auth.PortalLoginAsync(Login("ana@cliente.co"), default);
        Assert.Equal("mfa_required", next.Status);
        Assert.Null(next.Tokens);
    }

    // ---------------------------------------------------------------- su perfil

    [Fact]
    public async Task The_profile_shows_who_he_is_his_client_and_how_he_receives_the_notices()
    {
        await using var w = await WorldAsync();
        w.AsConsignee(Tienda, ClientA, LocTienda);
        var me = await w.Self.GetMeAsync(default);
        Assert.Equal(("Tienda Norte", "Cliente A", "tienda@norte.co"), (me.ConsigneeName, me.ClientName, me.Email));
        Assert.True(me.NoticesEnabled);
        Assert.Equal(new ConsigneeChannelDto("tienda@norte.co", true, false, false), me.EmailChannel);
        Assert.Equal(new ConsigneeChannelDto("+17875550100", false, false, false), me.SmsChannel);   // el cliente no lo prendió: no puede prenderlo él
    }

    [Fact]
    public async Task He_can_turn_a_channel_off_and_back_on_but_only_what_he_turned_off_himself()
    {
        await using var w = await WorldAsync();
        w.AsConsignee(Tienda, ClientA, LocTienda);
        var off = await w.Self.SetChannelAsync("email", new ConsigneeChannelRequest(false), default);
        Assert.False(off.EmailChannel.Enabled);
        Assert.True(off.EmailChannel.CanTurnOn);
        var log = await w.F.Db.ConsigneeNoticeConsents.AsNoTracking().SingleAsync();
        Assert.Equal((ConsigneeNoticeSources.Consignee, false, "tienda@norte.co", Tienda), (log.Source, log.Accepted, log.Contact, log.ChangedBy));

        w.AsConsignee(Tienda, ClientA, LocTienda);
        var on = await w.Self.SetChannelAsync("EMAIL", new ConsigneeChannelRequest(true), default);
        Assert.True(on.EmailChannel.Enabled);
        Assert.Equal(2, await w.F.Db.ConsigneeNoticeConsents.AsNoTracking().CountAsync());
        Assert.Empty(await w.F.Db.ConsigneeNotices.AsNoTracking().ToListAsync());   // no se le manda un aviso de alta: lo prendió él

        // apagar lo que ya está apagado no escribe nada
        w.AsConsignee(Tienda, ClientA, LocTienda);
        await w.Self.SetChannelAsync("SMS", new ConsigneeChannelRequest(false), default);
        Assert.Equal(2, await w.F.Db.ConsigneeNoticeConsents.AsNoTracking().CountAsync());
    }

    [Fact]
    public async Task He_cannot_turn_on_what_the_client_never_turned_on_nor_when_the_client_switched_the_notices_off()
    {
        await using var w = await WorldAsync();
        w.AsConsignee(Tienda, ClientA, LocTienda);
        var sms = await Assert.ThrowsAsync<ConflictException>(() => w.Self.SetChannelAsync("SMS", new ConsigneeChannelRequest(true), default));
        Assert.Equal(ConsigneeSelfService.CannotTurnOnMessage, sms.Message);

        // la apagó él, pero el cliente apagó el aviso de todos: tampoco
        await w.Self.SetChannelAsync("EMAIL", new ConsigneeChannelRequest(false), default);
        (await w.F.Db.Clients.AsTracking().SingleAsync(c => c.ClientId == ClientA)).NotifyConsignee = false;
        await w.F.Db.SaveChangesAsync();
        w.AsConsignee(Tienda, ClientA, LocTienda);
        await Assert.ThrowsAsync<ConflictException>(() => w.Self.SetChannelAsync("EMAIL", new ConsigneeChannelRequest(true), default));
    }

    [Fact]
    public async Task What_he_unsubscribed_with_the_link_of_a_notice_stays_off_and_a_medium_without_contact_or_a_bad_request_is_400()
    {
        await using var w = await WorldAsync();
        w.F.Db.ConsigneeUnsubscribes.Add(new ConsigneeUnsubscribe { TenantId = WmsFixture.TenantId, Channel = "EMAIL", Contact = "tienda@norte.co" });
        w.F.Db.ConsigneeNoticeConsents.Add(new ConsigneeNoticeConsent { TenantId = WmsFixture.TenantId, ClientId = ClientA, LocationId = LocTienda, Channel = "EMAIL", Accepted = false, Contact = "tienda@norte.co", Source = ConsigneeNoticeSources.Consignee });
        (await w.F.Db.Locations.AsTracking().SingleAsync(l => l.LocationId == LocTienda)).NoticeEmailOptIn = false;
        await w.F.Db.SaveChangesAsync();
        w.AsConsignee(Tienda, ClientA, LocTienda);
        var me = await w.Self.GetMeAsync(default);
        Assert.True(me.EmailChannel.Unsubscribed);
        Assert.False(me.EmailChannel.CanTurnOn);
        await Assert.ThrowsAsync<ConflictException>(() => w.Self.SetChannelAsync("EMAIL", new ConsigneeChannelRequest(true), default));

        w.AsConsignee(Colmado, ClientA, LocColmado);   // este consignatario no tiene ningún medio registrado
        var noContact = await Assert.ThrowsAsync<ValidationException>(() => w.Self.SetChannelAsync("EMAIL", new ConsigneeChannelRequest(true), default));
        Assert.Equal(ConsigneeSelfService.NoContactMessage, noContact.Message);
        await Assert.ThrowsAsync<ValidationException>(() => w.Self.SetChannelAsync("FAX", new ConsigneeChannelRequest(true), default));
        await Assert.ThrowsAsync<ValidationException>(() => w.Self.SetChannelAsync("EMAIL", new ConsigneeChannelRequest(null), default));
    }

    // ---------------------------------------------------------------- su bandeja y solo lo suyo

    [Fact]
    public async Task The_inbox_lists_only_his_notices_newest_first_with_a_title_and_the_tracking_token()
    {
        await using var w = await WorldAsync();
        var order = await w.F.AddOrderAsync(ClientA, "T-1");
        var tracked = await w.F.Db.TransportOrders.AsTracking().SingleAsync(t => t.TransportOrderId == order.TransportOrderId);
        tracked.WaybillNumber = "W-1"; tracked.TrackToken = "tok-orden-1";
        var t0 = new DateTime(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);
        ConsigneeNotice N(int loc, string kind, string? status, DateTime at, int? orderId = null) => new()
        {
            TenantId = WmsFixture.TenantId, ClientId = ClientA, LocationId = loc, TransportOrderId = orderId, Kind = kind, Channel = "EMAIL", Contact = "x@y.co", StatusCode = status, Lang = "es",
            Token = Guid.NewGuid().ToString("N"), Status = ConsigneeNoticeStatuses.Sent, CreatedAtUtc = at, SentAtUtc = at,
        };
        w.F.Db.ConsigneeNotices.AddRange(N(LocTienda, ConsigneeNoticeKinds.Consent, null, t0), N(LocTienda, ConsigneeNoticeKinds.Status, OrderStatuses.InTransit, t0.AddHours(1), order.TransportOrderId), N(LocColmado, ConsigneeNoticeKinds.Status, OrderStatuses.Delivered, t0.AddHours(2)));
        await w.F.Db.SaveChangesAsync();

        w.AsConsignee(Tienda, ClientA, LocTienda);
        var page = await w.Self.ListNoticesAsync(0, 25, default);
        Assert.Equal(2, page.Total);
        Assert.Equal(new[] { "Su paquete va en camino", "Cliente A le enviará avisos sobre sus envíos" }, page.Items.Select(i => i.Title));
        Assert.Equal(("W-1", "tok-orden-1"), (page.Items[0].Waybill, page.Items[0].TrackToken));
        Assert.Null(page.Items[1].Waybill);

        w.AsConsignee(Colmado, ClientA, LocColmado);
        var other = await w.Self.ListNoticesAsync(0, 25, default);
        Assert.Equal("Su paquete fue entregado", Assert.Single(other.Items).Title);
    }

    [Fact]
    public async Task A_person_of_the_client_or_a_staff_session_has_no_consignee_space()
    {
        await using var w = await WorldAsync();
        w.AsClient(Admin);
        var ex = await Assert.ThrowsAsync<ForbiddenException>(() => w.Self.GetMeAsync(default));
        Assert.Equal(ConsigneeSelfService.NotConsigneeMessage, ex.Message);
        await Assert.ThrowsAsync<ForbiddenException>(() => w.Self.ListNoticesAsync(0, 25, default));
        await Assert.ThrowsAsync<ForbiddenException>(() => w.Self.SetChannelAsync("EMAIL", new ConsigneeChannelRequest(false), default));

        // y un consignatario no puede dar acceso a nadie: sus rutas de cliente ni siquiera lo reconocen como editor
        w.AsConsignee(Tienda, ClientA, LocTienda);
        await Assert.ThrowsAsync<ForbiddenException>(() => w.Access.InviteAsync(w.LocColmadoId, new ConsigneeAccessInviteRequest("x@y.co", null), default));
    }

    [Fact]
    public async Task A_consignee_session_pointing_to_a_location_of_another_client_sees_nothing_of_it()
    {
        await using var w = await WorldAsync();
        w.AsConsignee(OtroCliente, ClientB, LocOtro);
        var me = await w.Self.GetMeAsync(default);
        Assert.Equal("Tienda de B", me.ConsigneeName);
        Assert.Empty((await w.Self.ListNoticesAsync(0, 25, default)).Items);

        // un token mal armado (consignatario de un cliente con el cliente de otro) no encuentra nada
        w.AsConsignee(OtroCliente, ClientA, LocOtro);
        await Assert.ThrowsAsync<ForbiddenException>(() => w.Self.GetMeAsync(default));
    }
}
