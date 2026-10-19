using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Clients;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 119 (P10) — preferencias de aviso de la persona del portal: la compañía manda (el techo), la persona solo apaga; se guardan por persona y aviso; nadie ve ni cambia las de otro; un aviso que no existe es 400.
/// </summary>
public class PortalNoticePrefServiceTests
{
    private sealed class World(WmsFixture f, Client client, PortalUser pat, PortalUser sam)
    {
        public WmsFixture F { get; } = f;
        public Client Client { get; } = client;
        public PortalUser Pat { get; } = pat;
        public PortalUser Sam { get; } = sam;

        public PortalNoticePrefService As(PortalUser u)
        {
            F.Tenant.IsPortal = true;
            F.Tenant.PortalClientId = Client.ClientId;
            F.Tenant.ScopedClientIds = new[] { Client.ClientId };
            F.Tenant.UserId = u.UserId;
            return new PortalNoticePrefService(F.Db, F.Tenant);
        }
    }

    private static async Task<World> NewWorldAsync()
    {
        var f = await WmsFixture.CreateAsync();
        var client = await f.AddClientAsync("C1");
        var active = f.StatusId(StatusDomains.PortalUserStatus, PortalUserStatuses.Active);
        var pat = new PortalUser { TenantId = WmsFixture.TenantId, ClientId = client.ClientId, UserId = 31, Email = "pat@cliente.co", FullName = "Pat", StatusCodeId = active, IsActive = true };
        var sam = new PortalUser { TenantId = WmsFixture.TenantId, ClientId = client.ClientId, UserId = 32, Email = "sam@cliente.co", FullName = "Sam", StatusCodeId = active, IsActive = true };
        f.Db.Set<PortalUser>().AddRange(pat, sam);
        // la compañía le encendió al cliente: «Salió a entrega» por correo y bandeja, «Entregada» solo por bandeja; el resto apagado
        f.Db.Set<ClientNotificationSetting>().AddRange(
            new ClientNotificationSetting { TenantId = WmsFixture.TenantId, ClientId = client.ClientId, Kind = ClientNotificationKinds.OutForDelivery, EmailEnabled = true, AppEnabled = true },
            new ClientNotificationSetting { TenantId = WmsFixture.TenantId, ClientId = client.ClientId, Kind = ClientNotificationKinds.Delivered, EmailEnabled = false, AppEnabled = true });
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        return new World(f, client, pat, sam);
    }

    [Fact]
    public async Task By_default_a_person_receives_everything_the_company_turned_on_and_sees_the_ceiling()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        var dto = await x.As(x.Pat).GetAsync(default);
        Assert.Equal(ClientNotificationKinds.All, dto.Prefs.Select(p => p.Kind).ToArray());
        var route = dto.Prefs.Single(p => p.Kind == ClientNotificationKinds.OutForDelivery);
        Assert.Equal((true, true, true, true), (route.EmailAvailable, route.AppAvailable, route.EmailEnabled, route.AppEnabled));
        var delivered = dto.Prefs.Single(p => p.Kind == ClientNotificationKinds.Delivered);
        Assert.Equal((false, true), (delivered.EmailAvailable, delivered.AppAvailable));                          // la compañía no encendió el correo de «Entregada»
        var off = dto.Prefs.Single(p => p.Kind == ClientNotificationKinds.InvoiceIssued);
        Assert.Equal((false, false), (off.EmailAvailable, off.AppAvailable));                                       // apagado por la compañía: no disponible
    }

    [Fact]
    public async Task A_person_turns_off_what_she_does_not_want_and_nobody_else_is_affected()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        var saved = await x.As(x.Sam).SaveAsync(new PortalNoticePrefsRequest(new List<PortalNoticePrefDto> { new(ClientNotificationKinds.OutForDelivery, true, true, EmailEnabled: false, AppEnabled: true) }), default);
        var mine = saved.Prefs.Single(p => p.Kind == ClientNotificationKinds.OutForDelivery);
        Assert.Equal((false, true), (mine.EmailEnabled, mine.AppEnabled));
        Assert.True(mine.EmailAvailable);                                                                           // el techo no cambia
        var pat = (await x.As(x.Pat).GetAsync(default)).Prefs.Single(p => p.Kind == ClientNotificationKinds.OutForDelivery);
        Assert.Equal((true, true), (pat.EmailEnabled, pat.AppEnabled));                                             // Pat sigue recibiéndolo
        // volver a encenderlo actualiza la misma fila (una por persona y aviso)
        await x.As(x.Sam).SaveAsync(new PortalNoticePrefsRequest(new List<PortalNoticePrefDto> { new(ClientNotificationKinds.OutForDelivery, true, true, true, false) }), default);
        var rows = await x.F.Db.Set<PortalNoticePref>().AsNoTracking().Where(p => p.PortalUserId == x.Sam.PortalUserId).ToListAsync();
        Assert.Equal((true, false), (Assert.Single(rows).EmailEnabled, rows[0].AppEnabled));
        Assert.Empty(await x.F.Db.Set<PortalNoticePref>().AsNoTracking().Where(p => p.PortalUserId == x.Pat.PortalUserId).ToListAsync());
    }

    [Fact]
    public async Task Everything_on_is_not_stored_and_an_unknown_notice_is_a_400()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        await x.As(x.Pat).SaveAsync(new PortalNoticePrefsRequest(new List<PortalNoticePrefDto> { new(ClientNotificationKinds.Delivered, true, true, true, true) }), default);
        Assert.Empty(await x.F.Db.Set<PortalNoticePref>().AsNoTracking().ToListAsync());
        var bad = await Assert.ThrowsAsync<ValidationException>(() => x.As(x.Pat).SaveAsync(new PortalNoticePrefsRequest(new List<PortalNoticePrefDto> { new("NO_EXISTE", true, true, false, false) }), default));
        Assert.Contains("NO_EXISTE", bad.Message);
        await Assert.ThrowsAsync<ValidationException>(() => x.As(x.Pat).SaveAsync(null, default));
    }

    [Fact]
    public async Task Without_a_portal_session_or_for_a_person_of_another_client_there_is_no_access()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        var stranger = new PortalUser { UserId = 99 };
        await Assert.ThrowsAsync<ForbiddenException>(() => x.As(stranger).GetAsync(default));                      // no es una persona activa de este cliente
        x.F.Tenant.PortalClientId = null;
        await Assert.ThrowsAsync<ForbiddenException>(() => new PortalNoticePrefService(x.F.Db, x.F.Tenant).GetAsync(default));
    }
}
