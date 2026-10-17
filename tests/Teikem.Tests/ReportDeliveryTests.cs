using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Analytics;
using Teikem.Domain.Billing;
using Teikem.Domain.Catalogs;
using Teikem.Domain.Clients;
using Teikem.Domain.Constants;
using Teikem.Domain.Identity;
using Teikem.Domain.Security;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Analytics;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Jobs;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

// Lote 92 (P12): informes programados por correo — reglas de la programación, envío de los que ya tocan (como su dueño), «Enviar ahora» y el trabajo REPORT_DELIVERY.
public sealed class ReportDeliveryTests
{
    private static readonly TimeZoneInfo Pr = TenantClock.Default.Zone;   // Puerto Rico: UTC−4 todo el año

    // ---------------------------------------------------------------- reglas puras

    [Fact]
    public void Cron_parsing_accepts_only_daily_weekly_and_monthly_forms()
    {
        Assert.Equal(new ReportSchedule("DAILY", 7, 0, 0, 0), ReportScheduleRules.TryParse("0 7 * * *"));
        Assert.Equal(new ReportSchedule("WEEKLY", 18, 30, 1, 0), ReportScheduleRules.TryParse(" 30 18 * * 1 "));
        Assert.Equal(0, ReportScheduleRules.TryParse("0 7 * * 7")!.DayOfWeek);   // 7 también es domingo
        Assert.Equal(new ReportSchedule("MONTHLY", 6, 15, 0, 28), ReportScheduleRules.TryParse("15 6 28 * *"));
        foreach (var bad in new[] { null, "", "  ", "0 7 * *", "60 7 * * *", "0 24 * * *", "0 7 29 * *", "0 7 0 * *", "0 7 1 * 1", "0 7 * 1 *", "0 7 * * 8", "*/5 * * * *", "daily" })
            Assert.Null(ReportScheduleRules.TryParse(bad));
        foreach (var s in new[] { "0 7 * * *", "30 18 * * 1", "15 6 28 * *" })
            Assert.Equal(s, ReportScheduleRules.Build(ReportScheduleRules.TryParse(s)!));
    }

    [Fact]
    public void Emails_are_split_deduplicated_validated_and_limited()
    {
        Assert.Equal(new[] { "a@b.co", "c@d.org" }, ReportScheduleRules.ParseEmails("a@b.co; c@d.org, A@B.CO"));
        Assert.Empty(ReportScheduleRules.ParseEmails("  "));
        Assert.Equal(ReportScheduleRules.InvalidEmailMessage("nope"), Assert.Throws<FormatException>(() => ReportScheduleRules.ParseEmails("a@b.co, nope")).Message);
        var eleven = string.Join(",", Enumerable.Range(1, 11).Select(i => $"u{i}@x.co"));
        Assert.Equal(ReportScheduleRules.TooManyRecipientsMessage, Assert.Throws<FormatException>(() => ReportScheduleRules.ParseEmails(eleven)).Message);
    }

    [Fact]
    public void Saving_requires_schedule_and_recipients_together_and_a_valid_range()
    {
        Assert.Equal(("0 7 * * *", "a@b.co", "LAST30"), ReportScheduleRules.ValidateForSave(" 0 7 * * * ", "a@b.co", "last30"));
        Assert.Equal(("0 7 * * *", "a@b.co", "LAST7"), ReportScheduleRules.ValidateForSave("0 7 * * *", "a@b.co", null));   // período por omisión: últimos 7 días
        Assert.Equal((null, null, null), ReportScheduleRules.ValidateForSave(null, null, "LAST30"));
        Assert.Equal(ReportScheduleRules.InvalidScheduleMessage, Assert.Throws<FormatException>(() => ReportScheduleRules.ValidateForSave("cada rato", "a@b.co", null)).Message);
        Assert.Equal(ReportScheduleRules.ScheduleNeedsEmailsMessage, Assert.Throws<FormatException>(() => ReportScheduleRules.ValidateForSave("0 7 * * *", null, null)).Message);
        Assert.Equal(ReportScheduleRules.EmailsNeedScheduleMessage, Assert.Throws<FormatException>(() => ReportScheduleRules.ValidateForSave(null, "a@b.co", null)).Message);
        Assert.Equal(ReportScheduleRules.InvalidRangeMessage, Assert.Throws<FormatException>(() => ReportScheduleRules.ValidateForSave("0 7 * * *", "a@b.co", "CUSTOM")).Message);
    }

    [Fact]
    public void A_schedule_is_due_once_per_occurrence_in_the_company_time_zone()
    {
        var daily = ReportScheduleRules.TryParse("0 7 * * *")!;
        // 07:00 locales = 11:00 UTC. Último envío ayer a las 7: a las 10:59 UTC de hoy aún no; a las 11:00 sí.
        var yesterday = new DateTime(2026, 10, 16, 11, 0, 0, DateTimeKind.Utc);
        Assert.Null(ReportScheduleRules.DueOccurrenceUtc(daily, yesterday, new DateTime(2026, 10, 17, 10, 59, 0, DateTimeKind.Utc), Pr));
        Assert.Equal(new DateTime(2026, 10, 17, 11, 0, 0, DateTimeKind.Utc), ReportScheduleRules.DueOccurrenceUtc(daily, yesterday, new DateTime(2026, 10, 17, 11, 0, 0, DateTimeKind.Utc), Pr));
        // ya enviado hoy: no se repite aunque pasen horas
        Assert.Null(ReportScheduleRules.DueOccurrenceUtc(daily, new DateTime(2026, 10, 17, 11, 0, 0, DateTimeKind.Utc), new DateTime(2026, 10, 17, 20, 0, 0, DateTimeKind.Utc), Pr));
        // sin base no toca nunca (el servicio la fija)
        Assert.Null(ReportScheduleRules.DueOccurrenceUtc(daily, null, new DateTime(2026, 10, 17, 20, 0, 0, DateTimeKind.Utc), Pr));
        // varias ocurrencias perdidas (servidor apagado) → se envía una sola vez, con la última
        Assert.Equal(new DateTime(2026, 10, 17, 11, 0, 0, DateTimeKind.Utc), ReportScheduleRules.DueOccurrenceUtc(daily, new DateTime(2026, 10, 10, 11, 0, 0, DateTimeKind.Utc), new DateTime(2026, 10, 17, 15, 0, 0, DateTimeKind.Utc), Pr));

        // semanal (lunes 8:00 locales): el 2026-10-17 es sábado; la última ocurrencia fue el lunes 12
        var weekly = ReportScheduleRules.TryParse("0 8 * * 1")!;
        Assert.Equal(new DateTime(2026, 10, 12, 12, 0, 0, DateTimeKind.Utc), ReportScheduleRules.DueOccurrenceUtc(weekly, new DateTime(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc), new DateTime(2026, 10, 17, 15, 0, 0, DateTimeKind.Utc), Pr));
        Assert.Null(ReportScheduleRules.DueOccurrenceUtc(weekly, new DateTime(2026, 10, 12, 12, 0, 0, DateTimeKind.Utc), new DateTime(2026, 10, 17, 15, 0, 0, DateTimeKind.Utc), Pr));

        // mensual (día 1 a las 6:00 locales): el 17 de octubre la última fue el 1.º de octubre; el 3 de octubre a las 5:00 locales aún es la de septiembre
        var monthly = ReportScheduleRules.TryParse("0 6 1 * *")!;
        Assert.Equal(new DateTime(2026, 10, 1, 10, 0, 0, DateTimeKind.Utc), ReportScheduleRules.DueOccurrenceUtc(monthly, new DateTime(2026, 9, 1, 10, 0, 0, DateTimeKind.Utc), new DateTime(2026, 10, 17, 15, 0, 0, DateTimeKind.Utc), Pr));
        Assert.Null(ReportScheduleRules.DueOccurrenceUtc(monthly, new DateTime(2026, 9, 1, 10, 0, 0, DateTimeKind.Utc), new DateTime(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc), Pr));   // 5:00 locales del 1.º: todavía no
    }

    [Fact]
    public void The_email_formats_values_and_escapes_html()
    {
        var cols = new[]
        {
            new ReportColumn("Name", "Cliente <b>", DataFieldType.Text, false), new ReportColumn("Total", "Total", DataFieldType.Number, true),
            new ReportColumn("Qty", "Cantidad", DataFieldType.Number, false), new ReportColumn("Open", "Abierta", DataFieldType.Bool, false), new ReportColumn("At", "Fecha", DataFieldType.Date, false),
        };
        var rows = new List<IDictionary<string, object?>>
        {
            new Dictionary<string, object?> { ["Name"] = "A & <script>", ["Total"] = 1234.5m, ["Qty"] = 3.5m, ["Open"] = true, ["At"] = new DateTime(2026, 10, 17, 14, 30, 0, DateTimeKind.Utc) },
        };
        var html = ReportEmailBuilder.Html("Mi <Compañía>", "Informe & más", DateRangeModes.Last30, new ReportRunResultDto(cols, rows, new Dictionary<string, object?> { ["Total"] = 1234.5m }, 5), Pr);
        Assert.Contains("A &amp; &lt;script&gt;", html);
        Assert.DoesNotContain("<script>", html);
        Assert.Contains("Cliente &lt;b&gt;", html);
        Assert.Contains("$1,234.50", html);
        Assert.Contains(">3.5<", html);
        Assert.Contains(">Sí<", html);
        Assert.Contains("2026-10-17 10:30", html);   // 14:30 UTC = 10:30 en Puerto Rico
        Assert.Contains("Últimos 30 días", html);
        Assert.Contains("Se muestran 1 de 5 filas", html);
        Assert.Contains("No hay datos en el período.", ReportEmailBuilder.Html("C", "R", DateRangeModes.All, new ReportRunResultDto(cols, new List<IDictionary<string, object?>>(), null, 0), Pr));
    }

    // ---------------------------------------------------------------- servicio

    private sealed class FakeEmail : ITransactionalEmailSender
    {
        public bool IsConfigured { get; set; } = true;
        public HashSet<string> Fail { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<(string To, string Subject, string Html, string? Company)> Sent { get; } = new();
        public Task SendAsync(string toEmail, string? toName, string subject, string htmlContent, CancellationToken ct, string? companyName = null)
        {
            if (Fail.Contains(toEmail)) throw new InvalidOperationException("rechazado");
            Sent.Add((toEmail, subject, htmlContent, companyName));
            return Task.CompletedTask;
        }
    }

    private sealed class MutableClock(DateTime nowUtc) : ITenantClock
    {
        public DateTime Now { get; set; } = nowUtc;
        public TimeZoneInfo Zone => Pr;
        public DateTime UtcNow => Now;
        public DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(Now, Pr));
        public DateTime StartOfDayUtc(DateOnly day) => TenantClock.Default.StartOfDayUtc(day);
        public DateOnly DayOf(DateTime utc) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Pr));
        public (DateTime? FromUtc, DateTime? ToUtcExclusive) UtcRange(DateOnly? from, DateOnly? to) => TenantClock.Default.UtcRange(from, to);
    }

    private sealed class World(WmsFixture f, FakeEmail email, MutableClock clock, ReportDefinition report, Client client)
    {
        public WmsFixture F { get; } = f;
        public FakeEmail Email { get; } = email;
        public MutableClock Clock { get; } = clock;
        public ReportDefinition Report { get; } = report;
        public Client Client { get; } = client;
        public ReportDeliveryService Svc() => F.Get<ReportDeliveryService>();
    }

    private static readonly DateTime SaturdayNoonUtc = new(2026, 10, 17, 16, 0, 0, DateTimeKind.Utc);   // 12:00 en Puerto Rico

    private static async Task<World> NewWorldAsync(string? cron = "0 7 * * *", string? emails = "ana@x.co, luis@x.co", string range = "LAST30", bool owner = true)
    {
        var email = new FakeEmail();
        var clock = new MutableClock(SaturdayNoonUtc);
        var f = await WmsFixture.CreateAsync(s =>
        {
            s.AddSingleton<ITenantClock>(clock);
            s.AddSingleton<ITransactionalEmailSender>(email);
            s.AddSingleton<IDataSource, InvoiceDataSource>();
            s.AddSingleton<IDataSourceRegistry, DataSourceRegistry>();
            s.AddSingleton<AnalyticsEngine>();
            s.AddSingleton<AnalyticsService>();
            s.AddSingleton<ReportDeliveryService>();
        });
        f.SetModules(ModuleKeys.Billing);
        f.SetPermissions(PermissionCatalog.BillingView, PermissionCatalog.AnalyticsManage);   // el dueño (usuario 1) lee facturas
        var id = 9000;
        f.Db.LookupCodes.AddRange(new LookupCode { LookupCodeId = id++, Entity = LookupDomains.ReportVisibility, InternalCode = ReportVisibilities.Private, LabelJson = "{\"es\":\"Privado\"}", IsActive = true });
        f.Db.Users.Add(new ApplicationUser { Id = 1, UserName = "ana", Email = "ana@x.co", FullName = "Ana", IsActive = true });
        if (!await f.Db.Tenants.IgnoreQueryFilters().AnyAsync(t => t.TenantId == WmsFixture.TenantId)) f.Db.Tenants.Add(new Teikem.Domain.Tenancy.Tenant { TenantId = WmsFixture.TenantId, Name = "Advance <Test>" });
        await f.Db.SaveChangesAsync();
        f.Lookups.Load(f.Db.LookupCodes.IgnoreQueryFilters().AsNoTracking().ToList());

        var c = await f.AddClientAsync("C1");
        var report = new ReportDefinition
        {
            TenantId = WmsFixture.TenantId, BaseEntityTypeLookupId = f.LookupId(LookupDomains.EntityType, BillingCodes.InvoiceEntityType), Name = "Facturas del mes",
            VisibilityLookupId = f.Lookups.GetIdAsync(LookupDomains.ReportVisibility, ReportVisibilities.Private).Result, OwnerUserId = owner ? 1 : null,
            ColumnsJson = "[\"Number\",\"ClientName\",\"Total\"]", ScheduleCron = cron, DeliveryEmails = emails, DeliveryRangeMode = range,
            LastDeliveryUtc = new DateTime(2026, 10, 16, 11, 0, 0, DateTimeKind.Utc),   // ayer a las 7 locales
        };
        f.Db.ReportDefinitions.Add(report);
        await f.Db.SaveChangesAsync();
        // una factura emitida hoy (visible en LAST30)
        f.Db.Set<Invoice>().Add(new Invoice
        {
            PublicId = Guid.NewGuid(), TenantId = WmsFixture.TenantId, ClientId = c.ClientId, InvoiceNumber = "FAC-<1>", StatusCodeId = f.StatusId(BillingCodes.InvoiceStatusDomain, InvoiceStatuses.Issued),
            IssueDate = clock.Today, DueDate = clock.Today.AddDays(30), CurrencyLookupId = 1, Subtotal = 100m, TaxAmount = 10m, IsActive = true,
        });
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        return new World(f, email, clock, report, c);
    }

    private static async Task<ReportDefinition> ReloadAsync(World x) => await x.F.Db.ReportDefinitions.AsNoTracking().SingleAsync(r => r.ReportDefinitionId == x.Report.ReportDefinitionId);

    [Fact]
    public async Task A_due_report_is_sent_once_to_every_recipient_as_its_owner_and_records_the_result()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        var (sent, skipped, failed) = await x.Svc().RunDueAsync(default);
        Assert.Equal((1, 0, 0), (sent, skipped, failed));
        Assert.Equal(new[] { "ana@x.co", "luis@x.co" }, x.Email.Sent.Select(s => s.To).ToArray());
        var mail = x.Email.Sent[0];
        Assert.Equal(("Tenant de prueba · Facturas del mes · 2026-10-17", "Tenant de prueba"), (mail.Subject, mail.Company));
        Assert.Contains("FAC-&lt;1&gt;", mail.Html);
        Assert.Contains("Cliente C1", mail.Html);
        Assert.Contains("$110.00", mail.Html);
        Assert.Contains("Últimos 30 días", mail.Html);
        var r = await ReloadAsync(x);
        Assert.Equal((ReportScheduleRules.StatusSent, new DateTime(2026, 10, 17, 11, 0, 0, DateTimeKind.Utc)), (r.LastDeliveryStatus, r.LastDeliveryUtc));
        Assert.Equal("Enviado a 2 destinatario(s) con 1 fila(s).", r.LastDeliveryMessage);

        // la siguiente pasada del trabajo no repite
        Assert.Equal((0, 0, 0), await x.Svc().RunDueAsync(default));
        Assert.Equal(2, x.Email.Sent.Count);
        // al día siguiente, otra vez
        x.Clock.Now = SaturdayNoonUtc.AddDays(1);
        Assert.Equal((1, 0, 0), await x.Svc().RunDueAsync(default));
        Assert.Equal(4, x.Email.Sent.Count);
        // el contexto quedó como estaba (usuario y alcance)
        Assert.Equal((1, null), (x.F.Tenant.UserId, x.F.Tenant.ScopedClientIds));
    }

    [Fact]
    public async Task Reports_not_yet_due_without_a_base_or_without_owner_or_system_are_left_alone()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        x.Clock.Now = new DateTime(2026, 10, 17, 10, 0, 0, DateTimeKind.Utc);   // 6:00 locales: aún no son las 7
        Assert.Equal((0, 0, 0), await x.Svc().RunDueAsync(default));
        Assert.Empty(x.Email.Sent);

        var db = x.F.Db;
        var rep = await db.ReportDefinitions.AsTracking().SingleAsync();
        rep.LastDeliveryUtc = null;   // programación vieja sin base: se fija a ahora y no se envía
        await db.SaveChangesAsync();
        x.Clock.Now = SaturdayNoonUtc;
        Assert.Equal((0, 0, 0), await x.Svc().RunDueAsync(default));
        Assert.Equal(SaturdayNoonUtc, (await ReloadAsync(x)).LastDeliveryUtc);

        rep = await db.ReportDefinitions.AsTracking().SingleAsync();
        rep.LastDeliveryUtc = new DateTime(2026, 10, 10, 11, 0, 0, DateTimeKind.Utc);
        rep.OwnerUserId = null;
        await db.SaveChangesAsync();
        Assert.Equal((0, 0, 0), await x.Svc().RunDueAsync(default));
        rep = await db.ReportDefinitions.AsTracking().SingleAsync();
        (rep.OwnerUserId, rep.IsSystem) = (1, true);
        await db.SaveChangesAsync();
        Assert.Equal((0, 0, 0), await x.Svc().RunDueAsync(default));
        Assert.Empty(x.Email.Sent);
    }

    [Fact]
    public async Task Without_mail_configured_or_with_an_inactive_owner_the_attempt_is_skipped_and_not_repeated()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        x.Email.IsConfigured = false;
        Assert.Equal((0, 1, 0), await x.Svc().RunDueAsync(default));
        var r = await ReloadAsync(x);
        Assert.Equal((ReportScheduleRules.StatusSkipped, "El envío de correo no está configurado."), (r.LastDeliveryStatus, r.LastDeliveryMessage));
        Assert.Equal((0, 0, 0), await x.Svc().RunDueAsync(default));   // misma ocurrencia: no se reintenta

        x.Email.IsConfigured = true;
        x.Clock.Now = SaturdayNoonUtc.AddDays(1);
        var user = await x.F.Db.Users.SingleAsync(u => u.Id == 1);
        user.IsActive = false;
        await x.F.Db.SaveChangesAsync();
        Assert.Equal((0, 1, 0), await x.Svc().RunDueAsync(default));
        Assert.Equal("El dueño del informe ya no está activo.", (await ReloadAsync(x)).LastDeliveryMessage);
        Assert.Empty(x.Email.Sent);
    }

    [Fact]
    public async Task A_recipient_that_fails_is_reported_and_the_others_still_receive_it()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        x.Email.Fail.Add("ana@x.co");
        Assert.Equal((0, 0, 1), await x.Svc().RunDueAsync(default));
        Assert.Equal(new[] { "luis@x.co" }, x.Email.Sent.Select(s => s.To).ToArray());
        var r = await ReloadAsync(x);
        Assert.Equal((ReportScheduleRules.StatusFailed, "No se pudo enviar a: ana@x.co"), (r.LastDeliveryStatus, r.LastDeliveryMessage));
    }

    [Fact]
    public async Task The_report_runs_with_the_owners_permissions_and_data_scope()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        // el dueño pierde billing.view: el informe no se puede correr y se registra el error sin enviar nada
        x.F.SetPermissions(PermissionCatalog.AnalyticsManage);
        Assert.Equal((0, 0, 1), await x.Svc().RunDueAsync(default));
        Assert.StartsWith("No se pudo correr el informe:", (await ReloadAsync(x)).LastDeliveryMessage);
        Assert.Empty(x.Email.Sent);

        // alcance por cliente: el dueño solo ve a otro cliente → el correo sale sin la factura de C1
        x.F.SetPermissions(PermissionCatalog.BillingView, PermissionCatalog.AnalyticsManage);
        var other = await x.F.AddClientAsync("C2");
        x.F.Db.UserDataScopes.Add(new UserDataScope { UserId = 1, TenantId = WmsFixture.TenantId, ScopeEntityLookupId = x.F.LookupId(LookupDomains.EntityType, EntityTypes.Client), ScopeId = other.ClientId });
        await x.F.Db.SaveChangesAsync();
        x.Clock.Now = SaturdayNoonUtc.AddDays(1);
        Assert.Equal((1, 0, 0), await x.Svc().RunDueAsync(default));
        Assert.DoesNotContain("FAC-&lt;1&gt;", x.Email.Sent[0].Html);
        Assert.Contains("No hay datos en el período.", x.Email.Sent[0].Html);
        Assert.Null(x.F.Tenant.ScopedClientIds);
    }

    [Fact]
    public async Task Send_now_delivers_without_touching_the_schedule_and_checks_who_can()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        var last = (await ReloadAsync(x)).LastDeliveryUtc;
        var res = await x.Svc().SendNowAsync(x.Report.ReportDefinitionId, default);
        Assert.Equal((ReportScheduleRules.StatusSent, 1, 2), (res.Status, res.Rows, res.Recipients.Count));
        var r = await ReloadAsync(x);
        Assert.Equal(last, r.LastDeliveryUtc);   // la programación no se mueve
        Assert.StartsWith("Manual: ", r.LastDeliveryMessage);
        // y el envío programado de hoy sigue pendiente
        Assert.Equal((1, 0, 0), await x.Svc().RunDueAsync(default));

        // otro usuario que no es dueño ni editor compartido → 403; informe de sistema → 400; sin destinatarios → 400; inexistente → 404
        x.F.Tenant.UserId = 2;
        await Assert.ThrowsAsync<ForbiddenException>(() => x.Svc().SendNowAsync(x.Report.ReportDefinitionId, default));
        x.F.Tenant.UserId = 1;
        var rep = await x.F.Db.ReportDefinitions.AsTracking().SingleAsync();
        rep.IsSystem = true;
        await x.F.Db.SaveChangesAsync();
        Assert.Equal(ReportScheduleRules.SystemReportMessage, (await Assert.ThrowsAsync<ValidationException>(() => x.Svc().SendNowAsync(x.Report.ReportDefinitionId, default))).Message);
        rep.IsSystem = false;
        rep.DeliveryEmails = null;
        await x.F.Db.SaveChangesAsync();
        Assert.Equal(ReportScheduleRules.NoScheduleMessage, (await Assert.ThrowsAsync<ValidationException>(() => x.Svc().SendNowAsync(x.Report.ReportDefinitionId, default))).Message);
        await Assert.ThrowsAsync<NotFoundException>(() => x.Svc().SendNowAsync(99999, default));
    }

    [Fact]
    public async Task Saving_a_report_validates_the_schedule_and_resets_the_last_delivery_base()
    {
        var x = await NewWorldAsync(cron: null, emails: null);
        await using var _ = x.F;
        var svc = x.F.Get<AnalyticsService>();
        var req = new ReportUpsertRequest("Facturas del mes", null, null, null, new[] { "Number", "Total" }, null, null, null, null, "0 7 * * *", "ana@x.co; luis@x.co", null, DeliveryRangeMode: "THIS_MONTH");
        var saved = await svc.UpdateReportAsync(x.Report.ReportDefinitionId, req, default);
        Assert.Equal(("0 7 * * *", "ana@x.co, luis@x.co", "THIS_MONTH"), (saved.ScheduleCron, saved.DeliveryEmails, saved.DeliveryRangeMode));
        Assert.NotNull(saved.LastDeliveryUtc);   // base: no dispara ocurrencias anteriores a este guardado
        Assert.Null(saved.LastDeliveryStatus);

        var bad = await Assert.ThrowsAsync<ValidationException>(() => svc.UpdateReportAsync(x.Report.ReportDefinitionId, req with { ScheduleCron = "cada rato" }, default));
        Assert.Equal(ReportScheduleRules.InvalidScheduleMessage, bad.Message);
        bad = await Assert.ThrowsAsync<ValidationException>(() => svc.UpdateReportAsync(x.Report.ReportDefinitionId, req with { DeliveryEmails = "nope" }, default));
        Assert.Equal(ReportScheduleRules.InvalidEmailMessage("nope"), bad.Message);
        bad = await Assert.ThrowsAsync<ValidationException>(() => svc.UpdateReportAsync(x.Report.ReportDefinitionId, req with { DeliveryEmails = null }, default));
        Assert.Equal(ReportScheduleRules.ScheduleNeedsEmailsMessage, bad.Message);

        // quitar la programación limpia todo
        var cleared = await svc.UpdateReportAsync(x.Report.ReportDefinitionId, req with { ScheduleCron = null, DeliveryEmails = null, DeliveryRangeMode = null }, default);
        Assert.Equal((null, null, null, null), (cleared.ScheduleCron, cleared.DeliveryEmails, cleared.DeliveryRangeMode, cleared.LastDeliveryUtc));
    }

    [Fact]
    public async Task The_job_runs_the_delivery_and_reports_what_it_did()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        var job = new ReportDeliveryJob();
        Assert.Equal(("REPORT_DELIVERY", true, 300), (job.Key, job.DefaultEnabled, job.DefaultIntervalSeconds));
        var ctx = new JobRunContext(WmsFixture.TenantId, "test", x.F.Services, SaturdayNoonUtc);
        Assert.Equal("1 enviado(s), 0 omitido(s), 0 con error.", await job.RunAsync(ctx, default));
        Assert.Equal("Ningún informe por enviar.", await job.RunAsync(ctx, default));
    }
}
