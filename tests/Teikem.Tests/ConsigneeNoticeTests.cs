using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Catalogs;
using Teikem.Domain.Clients;
using Teikem.Domain.Constants;
using Teikem.Domain.Contacts;
using Teikem.Domain.Orders;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 116 — avisos al consignatario (docs/p12): el cliente prende el aviso y marca al consignatario (con advertencia y bitácora); los estatus En ruta, intento fallido y finales se encolan con un enlace de token y otro de baja; los estatus
/// de almacén y los brincados no; la baja del consignatario es definitiva.
/// </summary>
public sealed class ConsigneeNoticeTests
{
    private sealed class FakeEmail : ITransactionalEmailSender
    {
        public bool IsConfigured { get; set; } = true;
        public int FailTimes { get; set; }
        public List<(string To, string Subject, string Html)> Sent { get; } = new();
        public Task SendAsync(string toEmail, string? toName, string subject, string htmlContent, CancellationToken ct, string? companyName = null)
        {
            if (FailTimes > 0) { FailTimes--; throw new InvalidOperationException("rechazado"); }
            Sent.Add((toEmail, subject, htmlContent));
            return Task.CompletedTask;
        }
    }

    private sealed class MutableClock(DateTime nowUtc) : ITenantClock
    {
        public DateTime Now { get; set; } = nowUtc;
        public TimeZoneInfo Zone => TenantClock.Default.Zone;
        public DateTime UtcNow => Now;
        public DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(Now, Zone));
        public DateTime StartOfDayUtc(DateOnly day) => TenantClock.Default.StartOfDayUtc(day);
        public DateOnly DayOf(DateTime utc) => TenantClock.Default.DayOf(utc);
        public (DateTime? FromUtc, DateTime? ToUtcExclusive) UtcRange(DateOnly? from, DateOnly? to) => TenantClock.Default.UtcRange(from, to);
    }

    private sealed class World(WmsFixture f, FakeEmail email, MutableClock clock, Client client, Location consignee)
    {
        public WmsFixture F { get; } = f;
        public FakeEmail Email { get; } = email;
        public MutableClock Clock { get; } = clock;
        public Client Client { get; } = client;
        public Location Consignee { get; } = consignee;
        public ConsigneeNoticeService Svc => F.Get<ConsigneeNoticeService>();
    }

    private const string BaseUrl = "https://app.example.com";

    private static async Task<World> NewWorldAsync(string role = PortalRoles.ClientAdmin, bool withEmail = true)
    {
        var email = new FakeEmail();
        var clock = new MutableClock(new DateTime(2026, 10, 18, 15, 0, 0, DateTimeKind.Utc));
        var f = await WmsFixture.CreateAsync(s =>
        {
            s.AddSingleton<ITenantClock>(clock);
            s.AddSingleton<ITransactionalEmailSender>(email);
            s.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { [ConsigneeNoticeService.BaseUrlKey] = BaseUrl }).Build());
            s.AddSingleton<ConsigneeNoticeService>();
        });
        var client = await f.AddClientAsync("C1");
        // catálogos que el fixture no trae: tipos de contacto y roles del portal
        f.Db.LookupCodes.AddRange(
            new LookupCode { LookupCodeId = 9001, Entity = "ContactType", InternalCode = "EMAIL", LabelJson = "{}", IsActive = true },
            new LookupCode { LookupCodeId = 9002, Entity = "ContactType", InternalCode = "MOBILE", LabelJson = "{}", IsActive = true },
            new LookupCode { LookupCodeId = 9003, Entity = "PortalRole", InternalCode = PortalRoles.ClientAdmin, LabelJson = "{}", IsActive = true },
            new LookupCode { LookupCodeId = 9004, Entity = "PortalRole", InternalCode = PortalRoles.ClientReadOnly, LabelJson = "{}", IsActive = true });
        f.Db.PortalUsers.Add(new PortalUser { PortalUserId = 1, TenantId = WmsFixture.TenantId, ClientId = client.ClientId, UserId = 1, Email = "p@x.co", RoleLookupId = role == PortalRoles.ClientAdmin ? 9003 : 9004, IsActive = true });
        var loc = new Location
        {
            LocationId = 7001, PublicId = Guid.NewGuid(), TenantId = WmsFixture.TenantId, ClientId = client.ClientId, Name = "Tienda Norte", City = "Ponce", Line1 = "Calle 1",
            LocationTypeLookupId = f.LookupId(LookupDomains.LocationType, LocationTypes.Delivery), IsActive = true,
        };
        f.Db.Locations.Add(loc);
        var ownerType = f.LookupId(LookupDomains.EntityType, EntityTypes.Location);
        if (withEmail) f.Db.ContactPoints.Add(new ContactPoint { TenantId = WmsFixture.TenantId, OwnerEntityLookupId = ownerType, OwnerId = loc.LocationId, ContactTypeLookupId = 9001, Value = "tienda@norte.co", IsPrimary = true, IsActive = true });
        f.Db.ContactPoints.Add(new ContactPoint { TenantId = WmsFixture.TenantId, OwnerEntityLookupId = ownerType, OwnerId = loc.LocationId, ContactTypeLookupId = 9002, Value = "787-555-0100", IsPrimary = false, IsActive = true });
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        f.Tenant.PortalClientId = client.ClientId;
        return new World(f, email, clock, client, loc);
    }

    private static ConsigneeNoticeMarkRequest Mark(string channel = "EMAIL", bool accepted = true, bool warning = true) => new(channel, accepted, warning);

    /// <summary>Orden del cliente con su parada de entrega en el consignatario.</summary>
    private static async Task<TransportOrder> OrderAsync(World x, string number = "T-1")
    {
        var o = await x.F.AddOrderAsync(x.Client.ClientId, number, OrderStatuses.Unassigned);
        x.F.Db.OrderStops.Add(new OrderStop { OrderStopId = 8000 + o.TransportOrderId, TransportOrderId = o.TransportOrderId, StopTypeLookupId = x.F.LookupId(LookupDomains.StopType, StopTypes.Delivery), Sequence = 2, LocationId = x.Consignee.LocationId, SnapLine1 = "Calle 1", SnapCity = "Ponce", StatusCodeId = 1 });
        (await x.F.Db.TransportOrders.AsTracking().SingleAsync(t => t.TransportOrderId == o.TransportOrderId)).WaybillNumber = "W-" + number;
        await x.F.Db.SaveChangesAsync();
        x.F.Db.ChangeTracker.Clear();
        return o;
    }

    private static async Task ClearNoticesAsync(World x)
    {
        x.F.Db.ConsigneeNotices.RemoveRange(await x.F.Db.ConsigneeNotices.ToListAsync());
        await x.F.Db.SaveChangesAsync();
        x.F.Db.ChangeTracker.Clear();
    }

    private static async Task Enable(World x, string lang = "es") => await x.Svc.SetSettingsAsync(new ConsigneeNoticeSettingsRequest(true, lang), default);

    private static async Task Record(World x, TransportOrder o, string status, bool skipped = false)
    {
        await new ConsigneeNoticeOrderEffect(x.Svc).OnTransitionedAsync(new StatusTransitionContext(StatusDomains.OrderStatus, EntityTypes.TransportOrder, o.TransportOrderId, null, new StatusCode { InternalCode = status }, null, skipped), default);
        await x.F.Db.SaveChangesAsync();
        x.F.Db.ChangeTracker.Clear();
    }

    // ---------------------------------------------------------------- reglas puras

    [Theory]
    [InlineData("IN_TRANSIT", true)]
    [InlineData("FAILED", true)]
    [InlineData("DELIVERED", true)]
    [InlineData("CANCELLED", true)]
    [InlineData("RETURNED", true)]
    [InlineData("IN_WAREHOUSE", true)]
    [InlineData("PICKUP", false)]
    [InlineData("UNASSIGNED", false)]
    [InlineData("ASSIGNED", false)]
    [InlineData("SUBMITTED", false)]
    [InlineData(null, false)]
    public void Only_on_its_way_failed_attempt_and_final_statuses_are_noticed(string? status, bool expected)
        => Assert.Equal(expected, ConsigneeNoticeRules.ShouldNotify(status));

    [Fact]
    public void The_subject_follows_the_status_and_the_language()
    {
        Assert.Equal("Advance: su paquete va en camino", ConsigneeNoticeRules.Subject("IN_TRANSIT", "Advance", "es"));
        Assert.Equal("Advance: your package was delivered", ConsigneeNoticeRules.Subject("DELIVERED", "Advance", "en"));
        Assert.Equal("Acme le enviará avisos sobre sus envíos", ConsigneeNoticeRules.ConsentSubject("Acme", "es"));
    }

    // ---------------------------------------------------------------- lo que decide el cliente

    [Fact]
    public async Task The_notice_is_off_by_default_and_the_client_turns_it_on_with_its_language()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        var off = await x.Svc.GetSettingsAsync(default);
        Assert.Equal((false, "es", true, true), (off.Enabled, off.Lang, off.EmailConfigured, off.CanEdit));
        var on = await x.Svc.SetSettingsAsync(new ConsigneeNoticeSettingsRequest(true, "en"), default);
        Assert.Equal((true, "en"), (on.Enabled, on.Lang));
        var ex = await Assert.ThrowsAsync<ValidationException>(() => x.Svc.SetSettingsAsync(new ConsigneeNoticeSettingsRequest(null, "fr"), default));
        Assert.Equal(ConsigneeNoticeRules.InvalidLangMessage, ex.Message);
    }

    [Fact]
    public async Task A_read_only_portal_user_cannot_change_anything()
    {
        var x = await NewWorldAsync(PortalRoles.ClientReadOnly);
        await using var _ = x.F;
        Assert.False((await x.Svc.GetSettingsAsync(default)).CanEdit);
        Assert.Equal(ConsigneeNoticeService.ReadOnlyMessage, (await Assert.ThrowsAsync<ForbiddenException>(() => x.Svc.SetSettingsAsync(new ConsigneeNoticeSettingsRequest(true, null), default))).Message);
        Assert.Equal(ConsigneeNoticeService.ReadOnlyMessage, (await Assert.ThrowsAsync<ForbiddenException>(() => x.Svc.SetConsigneeAsync(x.Consignee.PublicId, Mark(), default))).Message);
    }

    [Fact]
    public async Task The_consignees_list_shows_the_contacts_and_what_was_accepted()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        var page = await x.Svc.ListConsigneesAsync(null, 0, 25, default);
        var c = Assert.Single(page.Items);
        Assert.Equal(("Tienda Norte", "tienda@norte.co", "787-555-0100", false, false), (c.Name, c.Email, c.Phone, c.AcceptsEmail, c.AcceptsSms));
        Assert.Empty((await x.Svc.ListConsigneesAsync("zzz", 0, 25, default)).Items);
    }

    [Fact]
    public async Task Marking_needs_the_notice_on_the_warning_accepted_and_the_contact_and_leaves_a_log_and_a_message_to_the_consignee()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        Assert.Equal(ConsigneeNoticeRules.NotEnabledMessage, (await Assert.ThrowsAsync<StatusRuleException>(() => x.Svc.SetConsigneeAsync(x.Consignee.PublicId, Mark(), default))).Message);
        await Enable(x);
        Assert.Equal(ConsigneeNoticeRules.WarningRequiredMessage, (await Assert.ThrowsAsync<ValidationException>(() => x.Svc.SetConsigneeAsync(x.Consignee.PublicId, Mark(warning: false), default))).Message);
        Assert.Equal(ConsigneeNoticeRules.InvalidChannelMessage, (await Assert.ThrowsAsync<ValidationException>(() => x.Svc.SetConsigneeAsync(x.Consignee.PublicId, Mark("PAPEL"), default))).Message);

        var marked = await x.Svc.SetConsigneeAsync(x.Consignee.PublicId, Mark(), default);
        Assert.True(marked.AcceptsEmail);
        var log = Assert.Single(await x.F.Db.ConsigneeNoticeConsents.AsNoTracking().ToListAsync());
        Assert.Equal(("EMAIL", true, "tienda@norte.co", true, "CLIENT"), (log.Channel, log.Accepted, log.Contact, log.WarningAccepted, log.Source));
        var consent = Assert.Single(await x.F.Db.ConsigneeNotices.AsNoTracking().ToListAsync());
        Assert.Equal((ConsigneeNoticeKinds.Consent, "tienda@norte.co", ConsigneeNoticeStatuses.Pending), (consent.Kind, consent.Contact, consent.Status));

        // marcar otra vez lo mismo no repite la bitácora ni el aviso
        await x.Svc.SetConsigneeAsync(x.Consignee.PublicId, Mark(), default);
        Assert.Single(await x.F.Db.ConsigneeNoticeConsents.AsNoTracking().ToListAsync());
        Assert.Single(await x.F.Db.ConsigneeNotices.AsNoTracking().ToListAsync());

        // desmarcar queda en la bitácora y no avisa
        var off = await x.Svc.SetConsigneeAsync(x.Consignee.PublicId, Mark(accepted: false, warning: false), default);
        Assert.False(off.AcceptsEmail);
        Assert.Equal(2, await x.F.Db.ConsigneeNoticeConsents.CountAsync());
        Assert.Single(await x.F.Db.ConsigneeNotices.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task A_channel_without_a_contact_cannot_be_marked_and_the_text_is_kept_but_not_sent()
    {
        var x = await NewWorldAsync(withEmail: false);
        await using var _ = x.F;
        await Enable(x);
        Assert.Equal(ConsigneeNoticeRules.NoEmailMessage, (await Assert.ThrowsAsync<ValidationException>(() => x.Svc.SetConsigneeAsync(x.Consignee.PublicId, Mark(), default))).Message);
        var sms = await x.Svc.SetConsigneeAsync(x.Consignee.PublicId, Mark("SMS"), default);
        Assert.True(sms.AcceptsSms);
        var notice = Assert.Single(await x.F.Db.ConsigneeNotices.AsNoTracking().ToListAsync());
        Assert.Equal((ConsigneeNoticeStatuses.Skipped, ConsigneeNoticeService.SmsNotConfiguredMessage), (notice.Status, notice.Message));
    }

    [Fact]
    public async Task Another_clients_consignee_is_not_found()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        await Enable(x);
        await Assert.ThrowsAsync<NotFoundException>(() => x.Svc.SetConsigneeAsync(Guid.NewGuid(), Mark(), default));
    }

    // ---------------------------------------------------------------- cuándo se avisa

    [Fact]
    public async Task On_its_way_failed_and_final_statuses_queue_a_notice_with_a_token_but_warehouse_movements_and_skipped_ones_do_not()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        await Enable(x, "en");
        await x.Svc.SetConsigneeAsync(x.Consignee.PublicId, Mark(), default);
        var o = await OrderAsync(x);
        await ClearNoticesAsync(x);   // sin el aviso de alta

        foreach (var warehouse in new[] { "PICKUP", "UNASSIGNED", "ASSIGNED" }) await Record(x, o, warehouse);
        await Record(x, o, "IN_TRANSIT", skipped: true);
        Assert.Empty(await x.F.Db.ConsigneeNotices.ToListAsync());

        await Record(x, o, "IN_TRANSIT");
        await Record(x, o, "FAILED");
        await Record(x, o, "DELIVERED");
        var notices = await x.F.Db.ConsigneeNotices.AsNoTracking().OrderBy(n => n.ConsigneeNoticeId).ToListAsync();
        Assert.Equal(new[] { "IN_TRANSIT", "FAILED", "DELIVERED" }, notices.Select(n => n.StatusCode).ToArray());
        Assert.All(notices, n => Assert.Equal(("tienda@norte.co", "en", ConsigneeNoticeStatuses.Pending), (n.Contact, n.Lang, n.Status)));
        Assert.Equal(3, notices.Select(n => n.Token).Distinct().Count());
        var token = (await x.F.Db.TransportOrders.AsNoTracking().SingleAsync(t => t.TransportOrderId == o.TransportOrderId)).TrackToken;
        Assert.True(token is { Length: >= 24 });
    }

    [Fact]
    public async Task Nothing_is_queued_if_the_client_turned_it_off_or_the_consignee_was_not_marked_or_unsubscribed()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        var o = await OrderAsync(x);
        await Record(x, o, "IN_TRANSIT");                                                        // apagado
        Assert.Empty(await x.F.Db.ConsigneeNotices.ToListAsync());

        await Enable(x);
        await Record(x, o, "IN_TRANSIT");                                                        // prendido, pero el consignatario no está marcado
        Assert.Empty(await x.F.Db.ConsigneeNotices.ToListAsync());

        await x.Svc.SetConsigneeAsync(x.Consignee.PublicId, Mark(), default);
        x.F.Db.ConsigneeUnsubscribes.Add(new ConsigneeUnsubscribe { TenantId = WmsFixture.TenantId, Channel = "EMAIL", Contact = "tienda@norte.co" });
        await x.F.Db.SaveChangesAsync();
        await ClearNoticesAsync(x);
        await Record(x, o, "IN_TRANSIT");                                                        // marcado pero dado de baja
        Assert.Empty(await x.F.Db.ConsigneeNotices.ToListAsync());
    }

    // ---------------------------------------------------------------- envío

    [Fact]
    public async Task Dispatch_sends_the_consent_and_the_status_notices_with_the_tracking_and_unsubscribe_links()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        await Enable(x);
        await x.Svc.SetConsigneeAsync(x.Consignee.PublicId, Mark(), default);
        var o = await OrderAsync(x);
        await Record(x, o, "IN_TRANSIT");

        var (sent, skipped, failed) = await x.Svc.DispatchAsync(default);
        Assert.Equal((2, 0, 0), (sent, skipped, failed));
        var consent = x.Email.Sent[0];
        Assert.Equal("tienda@norte.co", consent.To);
        Assert.Contains("le enviará avisos", consent.Subject);
        Assert.Contains($"{BaseUrl}/unsubscribe/", consent.Html);
        var status = x.Email.Sent[1];
        Assert.Contains("va en camino", status.Subject);
        var token = (await x.F.Db.TransportOrders.AsNoTracking().SingleAsync(t => t.TransportOrderId == o.TransportOrderId)).TrackToken;
        Assert.Contains($"{BaseUrl}/track/{token}", status.Html);
        Assert.Contains("W-T-1", status.Html);
        Assert.Contains($"{BaseUrl}/unsubscribe/", status.Html);
        Assert.All(await x.F.Db.ConsigneeNotices.AsNoTracking().ToListAsync(), n => Assert.Equal(ConsigneeNoticeStatuses.Sent, n.Status));
        Assert.Equal((0, 0, 0), await x.Svc.DispatchAsync(default));                            // nada más por mandar
    }

    [Fact]
    public async Task Dispatch_skips_without_mail_or_base_url_and_retries_failures_at_one_five_and_thirty_minutes_until_it_gives_up()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        await Enable(x);
        await x.Svc.SetConsigneeAsync(x.Consignee.PublicId, Mark(), default);
        x.Email.IsConfigured = false;
        Assert.Equal((0, 1, 0), await x.Svc.DispatchAsync(default));
        Assert.Equal(ConsigneeNoticeService.EmailNotConfiguredMessage, (await x.F.Db.ConsigneeNotices.AsNoTracking().SingleAsync()).Message);

        // un envío que falla se reintenta a 1, 5 y 30 minutos y al cuarto intento queda fallido
        await ClearNoticesAsync(x);
        x.Email.IsConfigured = true;
        x.Email.FailTimes = 10;
        await x.Svc.SetConsigneeAsync(x.Consignee.PublicId, Mark(accepted: false, warning: false), default);
        await x.Svc.SetConsigneeAsync(x.Consignee.PublicId, Mark(), default);
        var delays = new List<TimeSpan>();
        for (var i = 0; i < 4; i++)
        {
            Assert.Equal((0, 0, 1), await x.Svc.DispatchAsync(default));
            var n = await x.F.Db.ConsigneeNotices.AsNoTracking().SingleAsync();
            if (n.NextAttemptUtc is DateTime next) { delays.Add(next - x.Clock.Now); x.Clock.Now = next; }
        }
        Assert.Equal(new[] { TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30) }, delays);
        Assert.Equal(ConsigneeNoticeStatuses.Failed, (await x.F.Db.ConsigneeNotices.AsNoTracking().SingleAsync()).Status);
    }

    // ---------------------------------------------------------------- baja

    [Fact]
    public async Task Unsubscribing_with_the_link_blocks_that_contact_unmarks_the_consignee_logs_it_and_cannot_be_undone_by_the_client()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        await Enable(x);
        await x.Svc.SetConsigneeAsync(x.Consignee.PublicId, Mark(), default);
        var token = (await x.F.Db.ConsigneeNotices.AsNoTracking().SingleAsync()).Token;

        await x.Svc.UnsubscribeAsync(token, default);
        await x.Svc.UnsubscribeAsync(token, default);                                            // idempotente

        Assert.Single(await x.F.Db.ConsigneeUnsubscribes.AsNoTracking().ToListAsync());
        Assert.False((await x.F.Db.Locations.AsNoTracking().SingleAsync(l => l.LocationId == x.Consignee.LocationId)).NoticeEmailOptIn);
        var logs = await x.F.Db.ConsigneeNoticeConsents.AsNoTracking().OrderBy(c => c.ConsigneeNoticeConsentId).ToListAsync();
        Assert.Equal(new[] { "CLIENT", "CONSIGNEE" }, logs.Select(l => l.Source).ToArray());
        Assert.False(logs[1].Accepted);

        var ex = await Assert.ThrowsAsync<ConflictException>(() => x.Svc.SetConsigneeAsync(x.Consignee.PublicId, Mark(), default));
        Assert.Equal(ConsigneeNoticeService.CannotMarkUnsubscribedMessage, ex.Message);
        await Assert.ThrowsAsync<ConsigneeLinkNotFoundException>(() => x.Svc.UnsubscribeAsync("no-existe", default));
        await Assert.ThrowsAsync<ConsigneeLinkNotFoundException>(() => x.Svc.UnsubscribeAsync(null, default));
    }
}
