using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Billing;
using Teikem.Domain.Clients;
using Teikem.Domain.Cod;
using Teikem.Domain.Constants;
using Teikem.Domain.Orders;
using Teikem.Domain.Trips;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Jobs;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

// Lote 98 (P10): avisos al cliente — configuración por cliente (la decide la compañía), hechos, resúmenes por ruta y envíos con reintentos.
public sealed class ClientNotificationTests
{
    [Fact]
    public void Rules_parse_emails_and_phones_with_exact_messages()
    {
        Assert.Equal(new[] { "a@b.co", "c@d.org" }, ClientNotificationRules.ParseEmails("a@b.co; c@d.org, A@B.CO"));
        Assert.Equal(ClientNotificationRules.InvalidEmail("nope"), Assert.Throws<FormatException>(() => ClientNotificationRules.ParseEmails("a@b.co, nope")).Message);
        Assert.Equal(ClientNotificationRules.TooManyRecipients, Assert.Throws<FormatException>(() => ClientNotificationRules.ParseEmails(string.Join(",", Enumerable.Range(1, 11).Select(i => $"u{i}@x.co")))).Message);
        Assert.Equal(new[] { "787-555-0100", "+1 (939) 555 0101" }, ClientNotificationRules.ParsePhones("787-555-0100, +1 (939) 555 0101"));
        Assert.Equal(ClientNotificationRules.InvalidPhone("12"), Assert.Throws<FormatException>(() => ClientNotificationRules.ParsePhones("12")).Message);
        Assert.True(ClientNotificationKinds.IsKnown(" delivered "));
        Assert.False(ClientNotificationKinds.IsKnown("X"));
    }

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

    private sealed class World(WmsFixture f, FakeEmail email, MutableClock clock, Client client)
    {
        public WmsFixture F { get; } = f;
        public FakeEmail Email { get; } = email;
        public MutableClock Clock { get; } = clock;
        public Client Client { get; } = client;
        public ClientNotificationService Svc => F.Get<ClientNotificationService>();
    }

    private static async Task<World> NewWorldAsync()
    {
        var email = new FakeEmail();
        var clock = new MutableClock(new DateTime(2026, 10, 17, 15, 0, 0, DateTimeKind.Utc));
        var f = await WmsFixture.CreateAsync(s =>
        {
            s.AddSingleton<ITenantClock>(clock);
            s.AddSingleton<ITransactionalEmailSender>(email);
            s.AddSingleton<ClientNotificationService>();
        });
        var client = await f.AddClientAsync("C1");
        f.Db.ChangeTracker.Clear();
        return new World(f, email, clock, client);
    }

    private static ClientNotificationSettingsRequest On(string kind, bool email = true, bool app = true, bool sms = false, string? extraEmails = null, string? extraPhones = null)
        => new(new List<ClientNotificationSettingDto> { new(kind, email, app, sms, extraEmails, extraPhones) });

    private static async Task<TransportOrder> OrderAsync(World x, string number, int pieces, Client? client = null)
    {
        var o = await x.F.AddOrderAsync((client ?? x.Client).ClientId, number, OrderStatuses.InTransit);
        var t = await x.F.Db.TransportOrders.AsTracking().SingleAsync(r => r.TransportOrderId == o.TransportOrderId);
        t.TotalPieces = pieces;
        await x.F.Db.SaveChangesAsync();
        x.F.Db.ChangeTracker.Clear();
        return t;
    }

    private static async Task<Trip> TripWithAsync(World x, string code, params TransportOrder[] orders)
    {
        var trip = new Trip { PublicId = Guid.NewGuid(), TenantId = WmsFixture.TenantId, Code = code, PlanDate = x.Clock.Today, StatusCodeId = x.F.StatusId(StatusDomains.TripStatus, TripStatuses.InProgress), IsActive = true };
        x.F.Db.Set<Trip>().Add(trip);
        await x.F.Db.SaveChangesAsync();
        foreach (var o in orders) x.F.Db.Set<TripOrder>().Add(new TripOrder { TenantId = WmsFixture.TenantId, TripId = trip.TripId, TransportOrderId = o.TransportOrderId, IsCurrent = true, AssignedAtUtc = x.Clock.Now });
        await x.F.Db.SaveChangesAsync();
        x.F.Db.ChangeTracker.Clear();
        return trip;
    }

    private static StatusTransitionContext Ctx(string domain, string entityType, int id, string to)
        => new(domain, entityType, id, null, new Teikem.Domain.Catalogs.StatusCode { InternalCode = to }, null);

    [Fact]
    public async Task The_company_decides_per_client_and_everything_is_off_by_default()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        var dto = await x.Svc.GetSettingsAsync(x.Client.PublicId, default);
        Assert.Equal(ClientNotificationKinds.All, dto.Settings.Select(s => s.Kind).ToArray());
        Assert.All(dto.Settings, s => Assert.False(s.EmailEnabled || s.AppEnabled || s.SmsEnabled));

        var saved = await x.Svc.SaveSettingsAsync(x.Client.PublicId, On("delivered", extraEmails: "ana@x.co; luis@x.co", sms: true, extraPhones: "787-555-0100"), default);
        var d = saved.Settings.Single(s => s.Kind == "DELIVERED");
        Assert.Equal((true, true, true, "ana@x.co, luis@x.co", "787-555-0100"), (d.EmailEnabled, d.AppEnabled, d.SmsEnabled, d.ExtraEmails, d.ExtraPhones));
        Assert.False(saved.Settings.Single(s => s.Kind == "PICKED_UP").EmailEnabled);
        // apagarlo todo deja la configuración en blanco; otro cliente no cambia
        var off = await x.Svc.SaveSettingsAsync(x.Client.PublicId, On("DELIVERED", false, false, false), default);
        Assert.False(off.Settings.Single(s => s.Kind == "DELIVERED").EmailEnabled);
        var other = await x.F.AddClientAsync("C2");
        Assert.All((await x.Svc.GetSettingsAsync(other.PublicId, default)).Settings, s => Assert.False(s.EmailEnabled));
        // otra compañía no ve el cliente
        using (x.F.AsTenant(WmsFixture.OtherTenantId))
            await Assert.ThrowsAsync<NotFoundException>(() => x.Svc.GetSettingsAsync(x.Client.PublicId, default));
    }

    [Fact]
    public async Task Saving_validates_the_kind_duplicates_and_recipients_with_exact_messages()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        async Task<string> Msg(ClientNotificationSettingsRequest r) => (await Assert.ThrowsAsync<ValidationException>(() => x.Svc.SaveSettingsAsync(x.Client.PublicId, r, default))).Message;
        Assert.Equal(ClientNotificationRules.KindInvalid, await Msg(On("NOPE")));
        Assert.Equal(ClientNotificationRules.DuplicateKind, await Msg(new ClientNotificationSettingsRequest(new List<ClientNotificationSettingDto> { new("DELIVERED", true, false, false, null, null), new("delivered", false, true, false, null, null) })));
        Assert.Equal(ClientNotificationRules.InvalidEmail("nope"), await Msg(On("DELIVERED", extraEmails: "nope")));
        Assert.Equal(ClientNotificationRules.InvalidPhone("12"), await Msg(On("DELIVERED", extraPhones: "12")));
        Assert.Equal(ClientNotificationRules.ExtraEmailsTooLong, await Msg(On("DELIVERED", extraEmails: new string('a', 401))));
        Assert.Equal("El cuerpo de la solicitud es obligatorio.", (await Assert.ThrowsAsync<ValidationException>(() => x.Svc.SaveSettingsAsync(x.Client.PublicId, null, default))).Message);
        await Assert.ThrowsAsync<NotFoundException>(() => x.Svc.SaveSettingsAsync(Guid.NewGuid(), On("DELIVERED"), default));
        Assert.Empty(await x.F.Db.Set<ClientNotificationSetting>().ToListAsync());   // nada se guardó a medias
    }

    [Fact]
    public async Task Events_are_recorded_only_for_clients_with_the_notice_on_and_for_the_right_statuses()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        var o = await OrderAsync(x, "O-1", 3);
        var effect = new ClientNotificationOrderEffect(x.Svc);
        await effect.OnTransitionedAsync(Ctx(StatusDomains.OrderStatus, EntityTypes.TransportOrder, o.TransportOrderId, OrderStatuses.Delivered), default);
        await x.F.Db.SaveChangesAsync();
        Assert.Empty(await x.F.Db.Set<ClientNotificationEvent>().ToListAsync());   // apagado: no se anota nada

        await x.Svc.SaveSettingsAsync(x.Client.PublicId, new ClientNotificationSettingsRequest(new List<ClientNotificationSettingDto>
        {
            new("PICKED_UP", true, false, false, null, null), new("DELIVERED", false, true, false, null, null),
        }), default);
        foreach (var to in new[] { OrderStatuses.Pickup, OrderStatuses.InTransit, OrderStatuses.Delivered, OrderStatuses.Failed })
            await effect.OnTransitionedAsync(Ctx(StatusDomains.OrderStatus, EntityTypes.TransportOrder, o.TransportOrderId, to), default);
        await x.F.Db.SaveChangesAsync();
        var kinds = (await x.F.Db.Set<ClientNotificationEvent>().ToListAsync()).Select(e => e.Kind).OrderBy(k => k).ToArray();
        Assert.Equal(new[] { "DELIVERED", "PICKED_UP" }, kinds);   // EN_RUTA está apagado y Fallida no avisa
    }

    [Fact]
    public async Task A_route_event_becomes_one_summary_per_client_and_route_after_it_settles()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        await x.Svc.SaveSettingsAsync(x.Client.PublicId, On("OUT_FOR_DELIVERY", extraEmails: "extra@x.co"), default);
        var o1 = await OrderAsync(x, "O-1", 3);
        var o2 = await OrderAsync(x, "O-2", 5);
        var o3 = await OrderAsync(x, "O-3", 2);
        await TripWithAsync(x, "RUT-1", o1, o2);
        await TripWithAsync(x, "RUT-2", o3);
        var effect = new ClientNotificationOrderEffect(x.Svc);
        foreach (var o in new[] { o1, o2, o3 })
            await effect.OnTransitionedAsync(Ctx(StatusDomains.OrderStatus, EntityTypes.TransportOrder, o.TransportOrderId, OrderStatuses.InTransit), default);
        await x.F.Db.SaveChangesAsync();
        // portal activo del cliente
        x.F.Db.Set<PortalUser>().Add(new PortalUser { TenantId = WmsFixture.TenantId, ClientId = x.Client.ClientId, Email = "portal@cliente.co", FullName = "Pat", StatusCodeId = x.F.StatusId(StatusDomains.PortalUserStatus, PortalUserStatuses.Active), IsActive = true });
        x.F.Db.Set<PortalUser>().Add(new PortalUser { TenantId = WmsFixture.TenantId, ClientId = x.Client.ClientId, Email = "invitado@cliente.co", StatusCodeId = x.F.StatusId(StatusDomains.PortalUserStatus, PortalUserStatuses.Invited), IsActive = true });
        await x.F.Db.SaveChangesAsync();

        // todavía no se asentó: no sale nada
        x.Clock.Now = x.Clock.Now.AddMinutes(1);
        Assert.Equal((0, 0, 0), await x.Svc.DigestAsync(default));
        Assert.Empty(x.Email.Sent);

        x.Clock.Now = x.Clock.Now.AddMinutes(ClientNotificationRules.SettleMinutes);
        var (summaries, sent, failed) = await x.Svc.DigestAsync(default);
        Assert.Equal((2, 4, 0), (summaries, sent, failed));   // dos rutas; cada una a portal@ y extra@ (el invitado no recibe)
        var log = await x.Svc.ListAsync(x.Client.PublicId, 0, 50, default);
        Assert.Equal(2, log.Total);
        var r1 = log.Items.Single(n => n.BodyText.Contains("RUT-1"));
        Assert.Contains("8 caja(s) de 2 orden(es)", r1.BodyText);
        Assert.Contains("RUT-2", log.Items.Single(n => n.BodyText.Contains("2 caja(s) de 1 orden(es)")).BodyText);
        Assert.True(r1.InPortal);
        Assert.Equal(new[] { "extra@x.co", "portal@cliente.co" }, r1.Deliveries.Select(d => d.Recipient).OrderBy(d => d).ToArray());
        Assert.All(r1.Deliveries, d => Assert.Equal(("EMAIL", "SENT"), (d.Channel, d.Status)));
        Assert.Contains("Sus paquetes salieron a entrega", x.Email.Sent[0].Subject);
        Assert.Equal(new[] { "extra@x.co", "portal@cliente.co" }, x.Email.Sent.Select(s => s.To).Distinct().OrderBy(s => s).ToArray());

        // los hechos ya resumidos no se vuelven a mandar
        Assert.Equal((0, 0, 0), await x.Svc.DigestAsync(default));
        Assert.Equal(4, x.Email.Sent.Count);
    }

    [Fact]
    public async Task Delivered_summary_adds_the_cod_collected_and_switching_it_off_meanwhile_drops_the_events()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        await x.Svc.SaveSettingsAsync(x.Client.PublicId, On("DELIVERED", extraEmails: "extra@x.co"), default);
        var o = await OrderAsync(x, "O-1", 4);
        x.F.Db.Set<CodCollection>().Add(new CodCollection { TenantId = WmsFixture.TenantId, TransportOrderId = o.TransportOrderId, MethodLookupId = x.F.LookupId(CodCodes.MethodDomain, "CASH"), AmountCollected = 75.5m, CollectedAtUtc = x.Clock.Now, StatusCodeId = x.F.StatusId(CodCodes.CollectionStatusDomain, CodCollectionStatuses.Collected) });
        await x.F.Db.SaveChangesAsync();
        await new ClientNotificationOrderEffect(x.Svc).OnTransitionedAsync(Ctx(StatusDomains.OrderStatus, EntityTypes.TransportOrder, o.TransportOrderId, OrderStatuses.Delivered), default);
        await x.F.Db.SaveChangesAsync();
        x.Clock.Now = x.Clock.Now.AddMinutes(10);
        await x.Svc.DigestAsync(default);
        var n = (await x.Svc.ListAsync(x.Client.PublicId, 0, 10, default)).Items.Single();
        Assert.Contains("Entregado: 4 caja(s) de 1 orden(es)", n.BodyText);
        Assert.Contains("COD cobrado: $75.50", n.BodyText);

        // lo apagan antes de que se resuma: los hechos se descartan
        await new ClientNotificationOrderEffect(x.Svc).OnTransitionedAsync(Ctx(StatusDomains.OrderStatus, EntityTypes.TransportOrder, o.TransportOrderId, OrderStatuses.Delivered), default);
        await x.F.Db.SaveChangesAsync();
        await x.Svc.SaveSettingsAsync(x.Client.PublicId, On("DELIVERED", false, false, false), default);
        x.Clock.Now = x.Clock.Now.AddMinutes(10);
        Assert.Equal((0, 0, 0), await x.Svc.DigestAsync(default));
        Assert.Empty(await x.F.Db.Set<ClientNotificationEvent>().Where(e => e.NotificationId == null).ToListAsync());
        Assert.Equal(1, (await x.Svc.ListAsync(x.Client.PublicId, 0, 10, default)).Total);
    }

    [Fact]
    public async Task Cod_processed_and_invoice_issued_have_their_own_summaries()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        await x.Svc.SaveSettingsAsync(x.Client.PublicId, new ClientNotificationSettingsRequest(new List<ClientNotificationSettingDto>
        {
            new("COD_PROCESSED", true, true, false, "extra@x.co", null), new("INVOICE_ISSUED", true, false, false, "extra@x.co", null),
        }), default);
        var order = await OrderAsync(x, "O-1", 1);
        var batch = new CodRemittanceBatch
        {
            PublicId = Guid.NewGuid(), TenantId = WmsFixture.TenantId, ClientId = x.Client.ClientId, Number = "REM-00001", PeriodStart = x.Clock.Today, PeriodEnd = x.Clock.Today,
            StatusCodeId = x.F.StatusId(RemittanceCodes.StatusDomain, RemittanceStatuses.Remitted), GrossCollected = 100m, CommissionAmount = 8m, CurrencyLookupId = 1,
        };
        batch.Lines.Add(new CodRemittanceLine { CodCollectionId = 1, TransportOrderId = order.TransportOrderId, AmountCollected = 100m, CommissionAmount = 8m });
        x.F.Db.Set<CodRemittanceBatch>().Add(batch);
        var invoice = new Invoice { PublicId = Guid.NewGuid(), TenantId = WmsFixture.TenantId, ClientId = x.Client.ClientId, InvoiceNumber = "FAC-00009", StatusCodeId = x.F.StatusId(BillingCodes.InvoiceStatusDomain, InvoiceStatuses.Issued), IssueDate = x.Clock.Today, DueDate = x.Clock.Today.AddDays(30), CurrencyLookupId = 1, Subtotal = 200m, TaxAmount = 20m, IsActive = true };
        x.F.Db.Set<Invoice>().Add(invoice);
        await x.F.Db.SaveChangesAsync();
        await new ClientNotificationRemittanceEffect(x.F.Db, x.Svc).OnTransitionedAsync(Ctx(RemittanceCodes.StatusDomain, RemittanceCodes.EntityType, batch.CodRemittanceBatchId, RemittanceStatuses.Remitted), default);
        await new ClientNotificationInvoiceEffect(x.F.Db, x.Svc).OnTransitionedAsync(Ctx(BillingCodes.InvoiceStatusDomain, BillingCodes.InvoiceEntityType, invoice.InvoiceId, InvoiceStatuses.Issued), default);
        await x.F.Db.SaveChangesAsync();
        x.Clock.Now = x.Clock.Now.AddMinutes(10);
        Assert.Equal((2, 2, 0), await x.Svc.DigestAsync(default));
        var items = (await x.Svc.ListAsync(x.Client.PublicId, 0, 10, default)).Items;
        Assert.Contains("Remesa de COD REM-00001: cobrado $100.00, comisión $8.00, neto $92.00 (1 orden(es))", items.Single(i => i.Kind == "COD_PROCESSED").BodyText);
        Assert.Contains("Factura FAC-00009 por $220.00, vence el 2026-11-16", items.Single(i => i.Kind == "INVOICE_ISSUED").BodyText);
        Assert.True(items.Single(i => i.Kind == "COD_PROCESSED").InPortal);
        Assert.False(items.Single(i => i.Kind == "INVOICE_ISSUED").InPortal);
    }

    [Fact]
    public async Task Failed_sends_are_retried_then_fail_and_missing_channels_are_skipped_with_the_reason()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        await x.Svc.SaveSettingsAsync(x.Client.PublicId, On("PICKED_UP", true, true, true, "extra@x.co", "787-555-0100"), default);
        var o = await OrderAsync(x, "O-1", 1);
        await new ClientNotificationOrderEffect(x.Svc).OnTransitionedAsync(Ctx(StatusDomains.OrderStatus, EntityTypes.TransportOrder, o.TransportOrderId, OrderStatuses.Pickup), default);
        await x.F.Db.SaveChangesAsync();
        x.Email.FailTimes = 100;
        x.Clock.Now = x.Clock.Now.AddMinutes(10);
        Assert.Equal((1, 0, 1), await x.Svc.DigestAsync(default));   // 1.er intento falla
        var delivery = () => x.F.Db.Set<ClientNotificationDelivery>().AsNoTracking().SingleAsync(d => d.Channel == "EMAIL");
        Assert.Equal(("PENDING", 1), ((await delivery()).Status, (await delivery()).Attempts));
        x.Clock.Now = x.Clock.Now.AddMinutes(2);
        await x.Svc.DigestAsync(default);   // 2.º
        x.Clock.Now = x.Clock.Now.AddMinutes(6);
        await x.Svc.DigestAsync(default);   // 3.º
        x.Clock.Now = x.Clock.Now.AddMinutes(31);
        await x.Svc.DigestAsync(default);   // 4.º: se agotó
        var last = await delivery();
        Assert.Equal(("FAILED", 4, "rechazado"), (last.Status, last.Attempts, last.Message));
        // el SMS se guardó como preferencia pero no sale
        var sms = await x.F.Db.Set<ClientNotificationDelivery>().AsNoTracking().SingleAsync(d => d.Channel == "SMS");
        Assert.Equal(("SKIPPED", "787-555-0100", "El envío de SMS todavía no está configurado."), (sms.Status, sms.Recipient, sms.Message));

        // correo no configurado
        var x2 = await NewWorldAsync();
        await using var __ = x2.F;
        x2.Email.IsConfigured = false;
        await x2.Svc.SaveSettingsAsync(x2.Client.PublicId, On("PICKED_UP", true, false, false, "extra@x.co"), default);
        var o2 = await OrderAsync(x2, "O-1", 1);
        await new ClientNotificationOrderEffect(x2.Svc).OnTransitionedAsync(Ctx(StatusDomains.OrderStatus, EntityTypes.TransportOrder, o2.TransportOrderId, OrderStatuses.Pickup), default);
        await x2.F.Db.SaveChangesAsync();
        x2.Clock.Now = x2.Clock.Now.AddMinutes(10);
        await x2.Svc.DigestAsync(default);
        var skipped = await x2.F.Db.Set<ClientNotificationDelivery>().AsNoTracking().SingleAsync();
        Assert.Equal(("SKIPPED", "El envío de correo no está configurado."), (skipped.Status, skipped.Message));

        // sin destinatarios: se explica
        var x3 = await NewWorldAsync();
        await using var ___ = x3.F;
        await x3.Svc.SaveSettingsAsync(x3.Client.PublicId, On("PICKED_UP", true, true, false), default);
        var o3 = await OrderAsync(x3, "O-1", 1);
        await new ClientNotificationOrderEffect(x3.Svc).OnTransitionedAsync(Ctx(StatusDomains.OrderStatus, EntityTypes.TransportOrder, o3.TransportOrderId, OrderStatuses.Pickup), default);
        await x3.F.Db.SaveChangesAsync();
        x3.Clock.Now = x3.Clock.Now.AddMinutes(10);
        await x3.Svc.DigestAsync(default);
        var none = (await x3.Svc.ListAsync(x3.Client.PublicId, 0, 10, default)).Items.Single();
        Assert.True(none.InPortal);   // igual queda en la bandeja del portal
        Assert.StartsWith("No hay destinatarios", Assert.Single(none.Deliveries).Message);
    }

    [Fact]
    public async Task The_job_runs_the_digest_and_reports_what_it_did()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        var job = new ClientNotificationDigestJob();
        Assert.Equal(("CLIENT_NOTIFICATION_DIGEST", true, 120), (job.Key, job.DefaultEnabled, job.DefaultIntervalSeconds));
        var ctx = new JobRunContext(WmsFixture.TenantId, "test", x.F.Services, x.Clock.Now);
        Assert.Equal("Ningún aviso por mandar.", await job.RunAsync(ctx, default));
    }
}
