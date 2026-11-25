using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Common;
using Teikem.Domain.Identity;
using Teikem.Domain.Notifications;
using Teikem.Domain.Security;
using Teikem.Domain.Tenancy;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Notifications;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>Lote 243 — un evento de prueba con contenido armado al enviar: variable bloque {detail}, destinatarios del evento y plantilla de correo.</summary>
internal sealed class CtxEventProvider : INotificationEventProvider
{
    public const string Code = "CTX_TEST";

    public IEnumerable<NotificationEventDef> Events { get; } = new[]
    {
        new NotificationEventDef(
            Code, null, "Evento con contenido", "Content event",
            new[]
            {
                new NotificationVariable("number", "Número", "Number", "N-1"),
                new NotificationVariable("extra", "Dato del proveedor", "Provider value", "EXTRA"),
                new NotificationVariable("detail", "Detalle", "Detail", "(tabla)", IsBlock: true),
            },
            IsOptional: false, LinkTemplate: "/x?n={number}",
            "Aviso {number}", "Notice {number}", "Texto {number}", "Text {number}",
            new[]
            {
                new NotificationContextRecipientDef("SELLER", "Vendedor de la orden", "Order's sales rep", true),
                new NotificationContextRecipientDef("CLIENT", "Cliente de la orden (correo principal y contactos marcados)", "Order's customer (main email and flagged contacts)", false, NotificationAudience.Customer),
            },
            new[] { new NotificationChannelDefault("EMAIL", "Orden {number}", "Order {number}", "Se solicitó {number}.\n\n{detail}", "Requested {number}.\n\n{detail}") }),
    };
}

/// <summary>
/// Proveedor de contenido falso: destinatarios y contenido configurables; anota cada llamada y, si el procesador se lo pasa (CallerDb),
/// qué tipos rastrea el DbContext de quien lo llama en ese momento.
/// </summary>
internal sealed class FakeContentProvider : INotificationContentProvider
{
    public static readonly byte[] Signature = { 137, 80, 78, 71 };
    public List<NotificationContextPerson> Sellers { get; } = new();
    public string? SellerEmptyReason { get; set; }
    public List<NotificationContextPerson> Clients { get; } = new();
    public string? ClientEmptyReason { get; set; }
    public string? SkipReason { get; set; }
    public bool Throw { get; set; }
    public bool ReturnNull { get; set; }
    public int Resolutions;
    public List<(NotificationAudience Audience, string Lang)> Builds { get; } = new();
    public int RecipientCalls;
    public TeikemDbContext? CallerDb { get; set; }
    public List<Type> CallerTracked { get; } = new();

    public IReadOnlyCollection<string> EventCodes { get; } = new[] { CtxEventProvider.Code };
    public string? ReferencePermission => "sales.view";
    public string? ReferenceModule => "SALES_ORDERS";

    private void Snapshot()
    {
        if (CallerDb is not null) CallerTracked.AddRange(CallerDb.ChangeTracker.Entries().Select(e => e.Entity.GetType()));
    }

    public Task<IReadOnlyDictionary<string, string>> ValuesForReferenceAsync(int tenantId, string eventCode, string reference, CancellationToken ct)
        => reference == "N-REAL"
            ? Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string> { ["number"] = "N-REAL" })
            : throw new NotFoundException("Orden de venta", reference, feminine: true);

    public Task<NotificationContent?> BuildAsync(NotificationContentRequest req, CancellationToken ct)
    {
        Snapshot();
        lock (Builds) Builds.Add((req.Audience, req.Lang));
        if (Throw) throw new InvalidOperationException("la base no responde");
        if (ReturnNull) return Task.FromResult<NotificationContent?>(null);
        if (SkipReason is not null) return Task.FromResult<NotificationContent?>(NotificationContent.Skip(SkipReason));
        return Task.FromResult<NotificationContent?>(Content($"{req.Audience}-{req.Lang}", $"resumen {req.Audience} {req.Lang}", $"EXTRA-{req.Lang}"));
    }

    public Task<NotificationContextResolution> RecipientsAsync(int tenantId, string eventCode, string contextKey, IReadOnlyDictionary<string, string> values, CancellationToken ct)
    {
        Snapshot();
        Interlocked.Increment(ref RecipientCalls);
        return Task.FromResult(contextKey == "SELLER"
            ? (Sellers.Count == 0 ? NotificationContextResolution.Empty(SellerEmptyReason) : new NotificationContextResolution(Sellers.ToList(), null))
            : (Clients.Count == 0 ? NotificationContextResolution.Empty(ClientEmptyReason) : new NotificationContextResolution(Clients.ToList(), null)));
    }

    public NotificationContent Sample(string eventCode, NotificationAudience audience, string lang)
        => Content($"ejemplo-{audience}", $"ejemplo {audience}", "EXTRA-SAMPLE");

    private static NotificationContent Content(string html, string text, string extra)
        => new(new Dictionary<string, string> { ["extra"] = extra },
            new Dictionary<string, NotificationBlock> { ["detail"] = new($"<table>{html}</table>", text) },
            new[] { new EmailAttachment("firma.png", "image/png", Signature) });
}

/// <summary>
/// Lote 243 — motor de avisos con contenido armado al enviar, contra SQL Server REAL (el procesador reclama la cola con ExecuteUpdate,
/// que InMemory no soporta). Proveedor de contenido falso y FakeEmailSender: nunca correo real. Se omite sin TEIKEM_TEST_SQLSERVER.
/// </summary>
public sealed class NotificationContentSqlTests(NotificationSqlFixture fx) : IClassFixture<NotificationSqlFixture>, IAsyncLifetime
{
    private const string Cancelled = "La orden OV-00012 se canceló antes de enviar el aviso.";
    private const string NoSeller = "La orden no tiene un vendedor con usuario activo en la compañía.";
    private static readonly NotificationEventCatalog Catalog = new(new INotificationEventProvider[] { new TestEventProvider(), new CtxEventProvider() });
    private int A;

    public async Task InitializeAsync()
    {
        if (fx.ConnectionString is null) return;   // sin SQL Server de prueba: las pruebas se omiten
        A = await fx.NewTenantAsync("Compañía Contenido");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static int _seed = 5000;
    private static int NextId() => Interlocked.Increment(ref _seed);

    // ---- siembra ----

    private async Task<int> UserAsync(int tenantId, string email, string? fullName = null)
    {
        await using var db = fx.NewDb(tenantId);
        var u = new ApplicationUser { UserName = email, Email = email, FullName = fullName ?? email, IsActive = true };
        db.Users.Add(u);
        await db.SaveChangesAsync();
        db.UserTenants.Add(new UserTenant { UserId = u.Id, TenantId = tenantId, StatusCodeId = await fx.MembershipStatusIdAsync() });
        await db.SaveChangesAsync();
        return u.Id;
    }

    private sealed record Ch(string Channel, string? SubjectEs = null, string? BodyEs = null, string? SubjectEn = null, string? BodyEn = null);

    /// <summary>Regla activa del evento con los canales indicados encendidos (sin texto = la plantilla por omisión del evento para el canal).</summary>
    private async Task<int> RuleAsync(int tenantId, IEnumerable<NotificationRuleRecipient> recipients, params Ch[] channels)
        => await RuleForAsync(tenantId, CtxEventProvider.Code, recipients, channels);

    private async Task<int> RuleForAsync(int tenantId, string eventCode, IEnumerable<NotificationRuleRecipient> recipients, params Ch[] channels)
    {
        var def = Catalog.Find(eventCode)!;
        await using var db = fx.NewDb(tenantId);
        var rule = new NotificationRule { PublicId = Guid.NewGuid(), TenantId = tenantId, EventCode = eventCode, Name = "Regla " + NextId(), IsActive = true };
        foreach (var channel in NotificationChannels.All)
        {
            var on = channels.FirstOrDefault(c => c.Channel == channel);
            var cd = def.ChannelDefaultFor(channel);
            rule.Channels.Add(new NotificationRuleChannel
            {
                TenantId = tenantId, Channel = channel, IsEnabled = on is not null,
                SubjectJson = MultilingualText.Build(on?.SubjectEs ?? cd?.SubjectEs ?? def.DefaultSubjectEs, on?.SubjectEn ?? cd?.SubjectEn ?? def.DefaultSubjectEn),
                BodyJson = MultilingualText.Build(on?.BodyEs ?? cd?.BodyEs ?? def.DefaultBodyEs, on?.BodyEn ?? cd?.BodyEn ?? def.DefaultBodyEn),
            });
        }
        foreach (var r in recipients) { r.TenantId = tenantId; rule.Recipients.Add(r); }
        db.NotificationRules.Add(rule);
        await db.SaveChangesAsync();
        return rule.NotificationRuleId;
    }

    private static NotificationRuleRecipient UserR(int id) => new() { Kind = "USER", UserId = id };
    private static NotificationRuleRecipient EmailR(string v) => new() { Kind = "EMAIL", Value = v };
    private static NotificationRuleRecipient Ctx(string key) => new() { Kind = "CONTEXT", Value = key };

    private async Task<long> PublishAsync(int tenantId, string eventCode = CtxEventProvider.Code)
    {
        await using var db = fx.NewDb(tenantId, 1);
        var publisher = new NotificationPublisher(db, db.TenantForTest(), Catalog);
        var values = eventCode == CtxEventProvider.Code
            ? new Dictionary<string, string?> { ["number"] = "N-1" }
            : new Dictionary<string, string?> { ["message"] = "hola", ["user"] = "Ana" };
        await publisher.PublishAsync(eventCode, values, null, default);
        await db.SaveChangesAsync();
        return await db.NotificationOutbox.AsNoTracking().OrderByDescending(o => o.NotificationOutboxId).Select(o => o.NotificationOutboxId).FirstAsync();
    }

    private NotificationProcessor Processor(FakeEmailSender email, FakeContentProvider provider)
        => fx.NewProcessor(email, Catalog, s => s.AddScoped<INotificationContentProvider>(sp =>
        {
            Interlocked.Increment(ref provider.Resolutions);
            provider.CallerDb = sp.GetRequiredService<TeikemDbContext>();   // el DbContext del procesador (mismo scope)
            return provider;
        }));

    private async Task<NotificationOutbox> OutboxAsync(long id)
    {
        await using var db = fx.NewDb(A);
        return await db.NotificationOutbox.AsNoTracking().SingleAsync(o => o.NotificationOutboxId == id);
    }

    private async Task<List<NotificationLog>> LogAsync(long outboxId)
    {
        await using var db = fx.NewDb(A);
        return await db.NotificationLogs.AsNoTracking().Where(l => l.NotificationOutboxId == outboxId).OrderBy(l => l.NotificationLogId).ToListAsync();
    }

    private async Task<List<UserNotification>> InboxAsync(int userId, long outboxId)
    {
        await using var db = fx.NewDb(A);
        return await db.UserNotifications.AsNoTracking().Where(n => n.UserId == userId && n.NotificationOutboxId == outboxId).ToListAsync();
    }

    private static NotificationContextPerson Staff(int? userId, string? email, string? phone = null) => new(userId, "Vendedor", email, phone, NotificationAudience.Staff, null);

    // ---- procesador ----

    [SqlServerFact]
    public async Task The_seller_comes_from_the_provider_a_user_and_the_seller_with_the_same_email_get_one_and_the_email_carries_the_block_and_the_attachment()
    {
        var email1 = $"ana{NextId()}@a.com";
        var ana = await UserAsync(A, email1);
        var provider = new FakeContentProvider();
        provider.Sellers.Add(Staff(ana, email1.ToUpperInvariant()));   // mismo correo, otra capitalización
        await RuleAsync(A, new[] { UserR(ana), Ctx("SELLER"), EmailR("almacen@x.com") }, new Ch("EMAIL"), new Ch("APP", "Aviso {number}", "Texto {number}: {detail}"));
        // una segunda regla con el cliente (sin correos): el proveedor se llama cuando el procesador ya rastrea registros de la primera
        await RuleAsync(A, new[] { EmailR("otro@x.com"), Ctx("CLIENT") }, new Ch("EMAIL"));
        var email = new FakeEmailSender();
        var outbox = await PublishAsync(A);

        Assert.True(await Processor(email, provider).TryProcessAsync(A, outbox, DateTime.UtcNow, default));

        Assert.Equal("DONE", (await OutboxAsync(outbox)).Status);
        Assert.Equal(3, email.Sent.Count);   // Ana una sola vez (USER y SELLER con el mismo correo), el buzón y el de la otra regla
        Assert.Single(email.Sent, d => string.Equals(d.Email, email1, StringComparison.OrdinalIgnoreCase));
        var d = email.Sent.Single(x => x.Email == "almacen@x.com");
        Assert.Equal("Orden N-1", d.Subject);
        Assert.Equal("<p style=\"font-family:Arial,Helvetica,sans-serif;font-size:14px;color:#111827\">Se solicit&#243; N-1.</p><table>Staff-es</table>", d.BodyHtml);
        Assert.Equal("Se solicitó N-1.\n\nresumen Staff es", d.BodyText);
        Assert.Equal("/x?n=N-1", d.LinkUrl);
        var att = Assert.Single(d.Attachments!);
        Assert.Equal(("firma.png", "image/png", 4), (att.FileName, att.ContentType, att.Content.Length));
        // la campanita: el bloque sale como su línea de resumen
        var n = Assert.Single(await InboxAsync(ana, outbox));
        Assert.Equal(("Aviso N-1", "Texto N-1: resumen Staff es"), (n.Title, n.Body));
        // el contenido se armó UNA vez (Staff, es) para todo el aviso, y el proveedor se resolvió una vez
        Assert.Equal(new[] { (NotificationAudience.Staff, "es") }, provider.Builds);
        Assert.Equal(1, provider.Resolutions);
        // el DbContext del procesador solo rastrea lo suyo cuando llama al proveedor: el registro de avisos (y la bandeja, que escribe el
        // canal APP en el mismo DbContext); nada de lo que lee el proveedor
        Assert.Contains(typeof(NotificationLog), provider.CallerTracked);
        Assert.All(provider.CallerTracked, t => Assert.Contains(t, new[] { typeof(NotificationLog), typeof(UserNotification) }));
        var log = await LogAsync(outbox);
        Assert.Equal(4, log.Count(l => l.Status == "SENT"));   // 3 correos + 1 bandeja
        var skipped = Assert.Single(log, l => l.Status != "SENT");
        Assert.Equal(("EMAIL", "(Cliente de la orden)", "SKIPPED", "«Cliente de la orden» no tiene correo."), (skipped.Channel, skipped.Recipient, skipped.Status, skipped.Error));
    }

    [SqlServerFact]
    public async Task The_customer_copy_comes_in_its_language_with_its_block_and_an_empty_seller_leaves_one_skipped_row_with_the_reason()
    {
        var provider = new FakeContentProvider { SellerEmptyReason = NoSeller };
        provider.Clients.Add(new NotificationContextPerson(null, "Cliente", "cliente@x.com", null, NotificationAudience.Customer, "EN"));
        await RuleAsync(A, new[] { Ctx("SELLER"), Ctx("CLIENT") }, new Ch("EMAIL"), new Ch("APP", "Aviso {number}", "Texto {number}"));
        var email = new FakeEmailSender();
        var outbox = await PublishAsync(A);

        await Processor(email, provider).TryProcessAsync(A, outbox, DateTime.UtcNow, default);

        var d = Assert.Single(email.Sent);
        Assert.Equal(("cliente@x.com", "Order N-1"), (d.Email, d.Subject));
        Assert.Equal("<p style=\"font-family:Arial,Helvetica,sans-serif;font-size:14px;color:#111827\">Requested N-1.</p><table>Customer-en</table>", d.BodyHtml);
        Assert.Equal(new[] { (NotificationAudience.Customer, "en") }, provider.Builds);
        var log = await LogAsync(outbox);
        Assert.Equal(3, log.Count);
        Assert.Contains(log, l => l.Channel == "EMAIL" && l.Recipient == "cliente@x.com" && l.Status == "SENT");
        // el vendedor sin usuario: UNA fila por canal con el motivo del proveedor; el cliente no aplica a la campanita (no deja fila)
        Assert.Contains(log, l => l.Channel == "EMAIL" && l.Recipient == "(Vendedor de la orden)" && l.Status == "SKIPPED" && l.Error == NoSeller);
        Assert.Contains(log, l => l.Channel == "APP" && l.Recipient == "(Vendedor de la orden)" && l.Status == "SKIPPED" && l.Error == NoSeller);
        Assert.Equal("DONE", (await OutboxAsync(outbox)).Status);   // SKIPPED no es una falla

        // un reintento no repite nada (ni el correo ni las filas de diagnóstico)
        await using (var db = fx.NewDb(A))
            await db.NotificationOutbox.Where(o => o.NotificationOutboxId == outbox).ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, "PENDING"));
        await Processor(email, provider).TryProcessAsync(A, outbox, DateTime.UtcNow, default);
        Assert.Single(email.Sent);
        Assert.Equal(3, (await LogAsync(outbox)).Count);
    }

    [SqlServerFact]
    public async Task Without_a_reason_from_the_provider_the_skipped_row_says_what_is_missing_per_channel()
    {
        var provider = new FakeContentProvider();
        provider.Sellers.Add(Staff(987654, null));   // usuario sin correo ni teléfono
        await RuleAsync(A, new[] { Ctx("SELLER"), Ctx("CLIENT") }, new Ch("EMAIL"), new Ch("SMS", null, "Texto {number}"), new Ch("APP", "Aviso {number}", "Texto {number}"));
        var outbox = await PublishAsync(A);

        await Processor(new FakeEmailSender(), provider).TryProcessAsync(A, outbox, DateTime.UtcNow, default);

        var log = await LogAsync(outbox);
        Assert.Contains(log, l => l.Channel == "EMAIL" && l.Recipient == "(Vendedor de la orden)" && l.Status == "SKIPPED" && l.Error == "«Vendedor de la orden» no tiene correo.");
        Assert.Contains(log, l => l.Channel == "SMS" && l.Recipient == "(Vendedor de la orden)" && l.Status == "SKIPPED" && l.Error == "«Vendedor de la orden» no tiene teléfono.");
        Assert.Contains(log, l => l.Channel == "EMAIL" && l.Recipient == "(Cliente de la orden)" && l.Status == "SKIPPED" && l.Error == "«Cliente de la orden» no tiene correo.");
        Assert.Contains(log, l => l.Channel == "SMS" && l.Recipient == "(Cliente de la orden)" && l.Status == "SKIPPED" && l.Error == "«Cliente de la orden» no tiene teléfono.");
        // en la campanita el vendedor sí llega (tiene usuario) y el cliente no deja fila
        Assert.Contains(log, l => l.Channel == "APP" && l.Recipient == "usuario 987654" && l.Status == "SENT");
        Assert.DoesNotContain(log, l => l.Channel == "APP" && l.Recipient.StartsWith('('));
        Assert.Equal(5, log.Count);
    }

    [SqlServerFact]
    public async Task Attachments_go_only_by_email_and_only_when_the_body_used_the_block_and_a_missing_variable_asks_the_provider()
    {
        var provider = new FakeContentProvider();
        await RuleAsync(A, new[] { EmailR("con@x.com") }, new Ch("EMAIL"));
        await RuleAsync(A, new[] { EmailR("sin@x.com") }, new Ch("EMAIL", "Orden {number}", "Solo {number} y {extra}"));
        var email = new FakeEmailSender();
        var outbox = await PublishAsync(A);

        await Processor(email, provider).TryProcessAsync(A, outbox, DateTime.UtcNow, default);

        Assert.Single(email.Sent.Single(d => d.Email == "con@x.com").Attachments!);
        var sin = email.Sent.Single(d => d.Email == "sin@x.com");
        Assert.Null(sin.Attachments);
        Assert.Equal("<p>Solo N-1 y EXTRA-es</p>", sin.BodyHtml);   // {extra} no venía en el aviso: lo puso el proveedor
    }

    [SqlServerFact]
    public async Task A_provider_error_retries_the_notice_and_the_retry_does_not_repeat_what_was_sent()
    {
        var u = await UserAsync(A, $"u{NextId()}@a.com");
        var provider = new FakeContentProvider { Throw = true };
        await RuleAsync(A, new[] { UserR(u) }, new Ch("APP", "Aviso {number}", "Texto {number}"));   // no necesita el proveedor: sale
        await RuleAsync(A, new[] { EmailR("bloque@x.com") }, new Ch("EMAIL"));                         // necesita el bloque: el proveedor falla
        var email = new FakeEmailSender();
        var outbox = await PublishAsync(A);

        await Processor(email, provider).TryProcessAsync(A, outbox, DateTime.UtcNow, default);
        var o = await OutboxAsync(outbox);
        Assert.Equal(("PENDING", 1, "la base no responde"), (o.Status, o.Attempts, o.LastError));
        Assert.InRange(o.NextAttemptUtc, DateTime.UtcNow.AddSeconds(30), DateTime.UtcNow.AddMinutes(2));   // 1 minuto
        Assert.Empty(email.Sent);
        Assert.Single(await InboxAsync(u, outbox));

        provider.Throw = false;
        await Processor(email, provider).TryProcessAsync(A, outbox, DateTime.UtcNow.AddMinutes(2), default);
        Assert.Equal("DONE", (await OutboxAsync(outbox)).Status);
        Assert.Single(email.Sent);
        Assert.Single(await InboxAsync(u, outbox));   // la campanita no se repite
    }

    [SqlServerFact]
    public async Task A_null_content_sends_the_notice_with_the_payload_and_the_block_empty()
    {
        var provider = new FakeContentProvider { ReturnNull = true };
        await RuleAsync(A, new[] { EmailR("x@x.com") }, new Ch("EMAIL"));
        var email = new FakeEmailSender();
        var outbox = await PublishAsync(A);

        await Processor(email, provider).TryProcessAsync(A, outbox, DateTime.UtcNow, default);

        var d = Assert.Single(email.Sent);
        Assert.Equal(("Orden N-1", NotificationHtml.Paragraphs("Se solicitó N-1.\n\n")), (d.Subject, d.BodyHtml));
        Assert.Null(d.Attachments);
    }

    [SqlServerFact]
    public async Task A_skip_reason_leaves_every_recipient_of_the_rule_skipped_with_that_reason()
    {
        var provider = new FakeContentProvider { SkipReason = Cancelled };
        provider.Sellers.Add(Staff(null, "vendedor@x.com"));
        await RuleAsync(A, new[] { EmailR("almacen@x.com"), Ctx("SELLER") }, new Ch("EMAIL"));
        var email = new FakeEmailSender();
        var outbox = await PublishAsync(A);

        await Processor(email, provider).TryProcessAsync(A, outbox, DateTime.UtcNow, default);

        Assert.Empty(email.Sent);
        var log = await LogAsync(outbox);
        Assert.Equal(2, log.Count);
        Assert.All(log, l => Assert.Equal(("SKIPPED", Cancelled), (l.Status, l.Error)));
        Assert.Equal("DONE", (await OutboxAsync(outbox)).Status);
    }

    [SqlServerFact]
    public async Task A_rule_without_context_or_blocks_never_resolves_the_provider()
    {
        var u = await UserAsync(A, $"u{NextId()}@a.com");
        var provider = new FakeContentProvider();
        await RuleAsync(A, new[] { UserR(u), EmailR("simple@x.com") }, new Ch("EMAIL", "Aviso {number}", "Texto {number}"), new Ch("APP", "Aviso {number}", "Texto {number}"));
        var email = new FakeEmailSender();
        var outbox = await PublishAsync(A);

        await Processor(email, provider).TryProcessAsync(A, outbox, DateTime.UtcNow, default);

        Assert.Equal(0, provider.Resolutions);
        Assert.Empty(provider.Builds);
        Assert.Equal(2, email.Sent.Count);
        Assert.Contains(email.Sent, d => d.Email == "simple@x.com" && d.BodyHtml == "<p>Texto N-1</p>" && d.Attachments is null);

        // tampoco en un evento que no tiene proveedor (el aviso de prueba de siempre)
        await RuleForAsync(A, NotificationCodes.TestEvent, new[] { EmailR("test@x.com") }, new Ch("EMAIL", "Asunto {message}", "Texto {message} de {user}"));
        var test = await PublishAsync(A, NotificationCodes.TestEvent);
        await Processor(email, provider).TryProcessAsync(A, test, DateTime.UtcNow, default);
        Assert.Equal(0, provider.Resolutions);
        Assert.Contains(email.Sent, d => d.Email == "test@x.com" && d.BodyHtml == "<p>Texto hola de Ana</p>");
    }

    // ---- servicio de reglas ----

    private (NotificationRuleService Svc, TeikemDbContext Db) Service(int userId, FakeEmailSender email, FakeContentProvider? provider)
    {
        var db = fx.NewDb(A, userId);
        var ctx = db.TenantForTest();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var senders = new INotificationChannelSender[] { email, new AppChannelSender(db), new SmsChannelSender(), new PushChannelSender() };
        var svc = new NotificationRuleService(db, ctx, Catalog, senders, new NotificationPublisher(db, ctx, Catalog),
            provider is null ? null : new INotificationContentProvider[] { provider },
            new PermissionService(db, ctx, cache, null!, new ReceivingNullSecurityEventWriter()), new ModuleService(db, ctx, cache));
        return (svc, db);
    }

    private static async Task<ValidationException> Invalid(Func<Task> act) => await Assert.ThrowsAsync<ValidationException>(act);

    private static string FieldOf(ValidationException e) => e.Errors!.Keys.Single();

    private async Task GrantAsync(int userId, string code)
    {
        await using var db = fx.NewDb(A);
        var perm = await db.Permissions.FirstOrDefaultAsync(p => p.Code == code);
        if (perm is null)
        {
            perm = new Permission { Code = code, CategoryLookupId = 1, LabelJson = "{}" };
            db.Permissions.Add(perm);
            await db.SaveChangesAsync();
        }
        db.UserPermissions.Add(new UserPermission { UserId = userId, TenantId = A, PermissionId = perm.PermissionId });
        await db.SaveChangesAsync();
    }

    private async Task EnableModuleAsync(string key)
    {
        await using var db = fx.NewDb(A);
        if (!await db.ModuleDefinitions.AnyAsync(m => m.ModuleKey == key))
            db.ModuleDefinitions.Add(new ModuleDefinition { ModuleKey = key, Name = key, NameEn = key, Category = "Operacion", IsActive = true });
        db.TenantModules.Add(new TenantModule { TenantId = A, ModuleKey = key, IsEnabled = true });
        await db.SaveChangesAsync();
    }

    [SqlServerFact]
    public async Task A_new_rule_takes_the_email_template_and_the_default_seller_and_can_be_activated_with_only_context_recipients()
    {
        var admin = await UserAsync(A, $"admin{NextId()}@a.com");
        var (svc, db) = Service(admin, new FakeEmailSender(), new FakeContentProvider());
        await using var _ = db;

        var created = await svc.CreateRuleAsync(new NotificationRuleRequest(CtxEventProvider.Code, "Correo " + NextId()), default);
        var r = Assert.Single(created.Recipients);
        Assert.Equal(("CONTEXT", "SELLER", "Vendedor de la orden"), (r.Kind, r.Value, r.Label));
        var mail = created.Channels.Single(c => c.Channel == "EMAIL");
        Assert.Equal(("Orden {number}", "Se solicitó {number}.\n\n{detail}", "Requested {number}.\n\n{detail}"), (mail.SubjectEs, mail.BodyEs, mail.BodyEn));
        Assert.Equal(("Aviso {number}", "Texto {number}"), (created.Channels.Single(c => c.Channel == "APP").SubjectEs, created.Channels.Single(c => c.Channel == "APP").BodyEs));

        var on = await svc.UpdateRuleAsync(created.PublicId, new NotificationRuleRequest(IsActive: true, Channels: new[] { new NotificationChannelRequest("EMAIL", true) }), default);
        Assert.True(on.IsActive);

        // con destinatarios explícitos no se agregan los de omisión; la clave se guarda como la declara el evento y no se repite
        var explicitRule = await svc.CreateRuleAsync(new NotificationRuleRequest(CtxEventProvider.Code, "Cliente " + NextId(), null, null,
            new[] { new NotificationRecipientRequest("context", Value: "client"), new NotificationRecipientRequest("CONTEXT", Value: "CLIENT") }), default);
        var c = Assert.Single(explicitRule.Recipients);
        Assert.Equal(("CONTEXT", "CLIENT", "Cliente de la orden (correo principal y contactos marcados)"), (c.Kind, c.Value, c.Label));
        // y en el evento de prueba (sin destinatarios del evento) la regla nace sin destinatarios, como siempre
        Assert.Empty((await svc.CreateRuleAsync(new NotificationRuleRequest("TEST", "Prueba " + NextId()), default)).Recipients);
    }

    [SqlServerFact]
    public async Task Context_recipients_and_blocks_are_validated_with_exact_messages()
    {
        var admin = await UserAsync(A, $"admin{NextId()}@a.com");
        var (svc, db) = Service(admin, new FakeEmailSender(), new FakeContentProvider());
        await using var _ = db;
        Assert.Equal("Este evento no tiene el destinatario 'FOO'.", (await Invalid(() => svc.CreateRuleAsync(new NotificationRuleRequest(CtxEventProvider.Code, "R" + NextId(), null, null,
            new[] { new NotificationRecipientRequest("CONTEXT", Value: "FOO") }), default))).Message);
        Assert.Equal("Este evento no tiene el destinatario 'SELLER'.", (await Invalid(() => svc.CreateRuleAsync(new NotificationRuleRequest("TEST", "R" + NextId(), null, null,
            new[] { new NotificationRecipientRequest("CONTEXT", Value: "SELLER") }), default))).Message);
        Assert.Equal("Este evento no tiene el destinatario ''.", (await Invalid(() => svc.CreateRuleAsync(new NotificationRuleRequest(CtxEventProvider.Code, "R" + NextId(), null, null,
            new[] { new NotificationRecipientRequest("CONTEXT", Value: null) }), default))).Message);
        var block = await Invalid(() => svc.CreateRuleAsync(new NotificationRuleRequest(CtxEventProvider.Code, "R" + NextId(), null,
            new[] { new NotificationChannelRequest("EMAIL", false, SubjectEn: "Order {detail}") }), default));
        Assert.Equal(("channels", "La variable {detail} es un bloque y solo puede ir en el texto, no en el asunto."), (FieldOf(block), block.Message));
        // también en el PATCH de una regla que ya existe
        var rule = await svc.CreateRuleAsync(new NotificationRuleRequest(CtxEventProvider.Code, "R" + NextId()), default);
        Assert.Equal("La variable {detail} es un bloque y solo puede ir en el texto, no en el asunto.", (await Invalid(() => svc.UpdateRuleAsync(rule.PublicId,
            new NotificationRuleRequest(Channels: new[] { new NotificationChannelRequest("APP", null, "Aviso {detail}") }), default))).Message);
    }

    [SqlServerFact]
    public async Task The_events_and_channels_show_blocks_context_recipients_defaults_and_the_local_sink()
    {
        var admin = await UserAsync(A, $"admin{NextId()}@a.com");
        var (svc, db) = Service(admin, new FakeEmailSender { Local = true }, null);
        await using var _ = db;
        var e = svc.ListEvents().Single(x => x.Code == CtxEventProvider.Code);
        Assert.True(e.Variables.Single(v => v.Name == "detail").IsBlock);
        Assert.False(e.Variables.Single(v => v.Name == "number").IsBlock);
        Assert.Equal(new[] { ("SELLER", true, "STAFF"), ("CLIENT", false, "CUSTOMER") }, e.ContextRecipients!.Select(r => (r.Key, r.IsDefault, r.Audience)));
        var cd = Assert.Single(e.ChannelDefaults!);
        Assert.Equal(("EMAIL", "Orden {number}", "Se solicitó {number}.\n\n{detail}"), (cd.Channel, cd.Subject, cd.Body));
        var test = svc.ListEvents().Single(x => x.Code == "TEST");
        Assert.Empty(test.ContextRecipients!);
        Assert.Empty(test.ChannelDefaults!);
        var channels = svc.ListChannels();
        Assert.Equal((true, true), (channels.Single(c => c.Channel == "EMAIL").IsConfigured, channels.Single(c => c.Channel == "EMAIL").IsLocal));
        Assert.All(channels.Where(c => c.Channel != "EMAIL"), c => Assert.False(c.IsLocal));
        var (svc2, db2) = Service(admin, new FakeEmailSender(), null);
        await using var __ = db2;
        Assert.False(svc2.ListChannels().Single(c => c.Channel == "EMAIL").IsLocal);
    }

    [SqlServerFact]
    public async Task The_preview_picks_the_template_validates_it_and_never_sends_or_logs()
    {
        var admin = await UserAsync(A, $"admin{NextId()}@a.com");
        var email = new FakeEmailSender();
        var (svc, db) = Service(admin, email, new FakeContentProvider());
        await using var _ = db;
        var outboxBefore = await db.NotificationOutbox.CountAsync();
        var logBefore = await db.NotificationLogs.CountAsync();

        // sin regla activa: la plantilla por omisión del canal, con el ejemplo del proveedor
        var p = await svc.PreviewAsync(new NotificationPreviewRequest(CtxEventProvider.Code, "email"), default);
        Assert.Equal(("EMAIL", "Orden N-1", "DEFAULT", true), (p.Channel, p.Subject, p.TemplateSource, p.UsedSample));
        Assert.Equal("<p style=\"font-family:Arial,Helvetica,sans-serif;font-size:14px;color:#111827\">Se solicit&#243; N-1.</p><table>ejemplo-Staff</table>", p.BodyHtml);
        Assert.Equal("Se solicitó N-1.\n\nejemplo Staff", p.BodyText);
        var att = Assert.Single(p.Attachments);
        Assert.Equal(("firma.png", "image/png", 4L), (att.FileName, att.ContentType, att.SizeBytes));
        // en inglés y para el cliente
        var en = await svc.PreviewAsync(new NotificationPreviewRequest(CtxEventProvider.Code, "EMAIL", Lang: "EN", Audience: "customer"), default);
        Assert.Equal(("Order N-1", "<p style=\"font-family:Arial,Helvetica,sans-serif;font-size:14px;color:#111827\">Requested N-1.</p><table>ejemplo-Customer</table>"), (en.Subject, en.BodyHtml));
        // la campanita: sin HTML y el bloque como su línea; un canal sin bloque en el texto no lleva adjuntos
        var app = await svc.PreviewAsync(new NotificationPreviewRequest(CtxEventProvider.Code, "APP", "Aviso {number}", "Texto {detail}"), default);
        Assert.Equal(("Aviso N-1", "Texto ejemplo Staff", (string?)null, "REQUEST"), (app.Subject, app.BodyText, app.BodyHtml, app.TemplateSource));
        Assert.Empty(app.Attachments);
        var sms = await svc.PreviewAsync(new NotificationPreviewRequest(CtxEventProvider.Code, "SMS"), default);
        Assert.Null(sms.Subject);
        var noBlock = await svc.PreviewAsync(new NotificationPreviewRequest(CtxEventProvider.Code, "EMAIL", "Orden {number}", "Solo {extra}"), default);
        Assert.Equal(("<p>Solo EXTRA-SAMPLE</p>", 0), (noBlock.BodyHtml, noBlock.Attachments.Count));

        // con una regla activa con el canal encendido: su plantilla
        await RuleAsync(A, new[] { EmailR("a@x.com") }, new Ch("EMAIL", "De la regla {number}", "Cuerpo de la regla {detail}"));
        var fromRule = await svc.PreviewAsync(new NotificationPreviewRequest(CtxEventProvider.Code, "EMAIL"), default);
        Assert.Equal(("RULE", "De la regla N-1"), (fromRule.TemplateSource, fromRule.Subject));

        // validaciones (como al guardar)
        Assert.Equal("Indique el evento de la regla.", (await Invalid(() => svc.PreviewAsync(new NotificationPreviewRequest(null, "EMAIL"), default))).Message);
        Assert.Equal("Evento de aviso desconocido: 'NOPE'.", (await Invalid(() => svc.PreviewAsync(new NotificationPreviewRequest("NOPE", "EMAIL"), default))).Message);
        Assert.Equal("Indique el canal.", (await Invalid(() => svc.PreviewAsync(new NotificationPreviewRequest(CtxEventProvider.Code, null), default))).Message);
        Assert.Equal("Canal desconocido: 'FAX'. Use EMAIL, SMS, APP o PUSH.", (await Invalid(() => svc.PreviewAsync(new NotificationPreviewRequest(CtxEventProvider.Code, "FAX"), default))).Message);
        Assert.Equal("La variable {detail} es un bloque y solo puede ir en el texto, no en el asunto.", (await Invalid(() => svc.PreviewAsync(
            new NotificationPreviewRequest(CtxEventProvider.Code, "EMAIL", "Orden {detail}", "x"), default))).Message);
        Assert.Equal("La plantilla usa una variable que el evento no tiene: {zzz}.", (await Invalid(() => svc.PreviewAsync(
            new NotificationPreviewRequest(CtxEventProvider.Code, "EMAIL", "Orden", "x {zzz}"), default))).Message);
        Assert.Equal("El texto del canal EMAIL no puede pasar de 1000 caracteres.", (await Invalid(() => svc.PreviewAsync(
            new NotificationPreviewRequest(CtxEventProvider.Code, "EMAIL", "Orden", new string('x', 1001)), default))).Message);
        Assert.Equal("Destinatario de la vista previa desconocido: 'OTRO'. Use STAFF o CUSTOMER.", (await Invalid(() => svc.PreviewAsync(
            new NotificationPreviewRequest(CtxEventProvider.Code, "EMAIL", Audience: "OTRO"), default))).Message);
        Assert.Equal("Idioma no válido: 'fr'. Use es o en.", (await Invalid(() => svc.PreviewAsync(new NotificationPreviewRequest(CtxEventProvider.Code, "EMAIL", Lang: "fr"), default))).Message);

        // nada se envió ni se anotó
        Assert.Empty(email.Sent);
        Assert.Equal(outboxBefore, await db.NotificationOutbox.CountAsync());
        Assert.Equal(logBefore, await db.NotificationLogs.CountAsync());
    }

    [SqlServerFact]
    public async Task The_preview_with_a_real_document_requires_the_providers_permission_and_module_and_reports_not_found()
    {
        var admin = await UserAsync(A, $"admin{NextId()}@a.com");
        var provider = new FakeContentProvider();
        var withRef = new NotificationPreviewRequest(CtxEventProvider.Code, "EMAIL", Reference: "N-REAL");

        // un evento sin proveedor no admite documento real
        var (svc0, db0) = Service(admin, new FakeEmailSender(), provider);
        await using (db0)
        {
            var nr = await Invalid(() => svc0.PreviewAsync(new NotificationPreviewRequest("TEST", "EMAIL", Reference: "OV-1"), default));
            Assert.Equal(("reference", "Este evento no admite vista previa con un documento real."), (FieldOf(nr), nr.Message));
            // sin sales.view: 403
            var forbidden = await Assert.ThrowsAsync<ForbiddenException>(() => svc0.PreviewAsync(withRef, default));
            Assert.Equal("Falta el permiso 'sales.view'.", forbidden.Message);
        }

        // con el permiso pero el módulo apagado
        await GrantAsync(admin, "sales.view");
        var (svc1, db1) = Service(admin, new FakeEmailSender(), provider);
        await using (db1)
            Assert.Equal("El módulo 'SALES_ORDERS' no está habilitado para esta compañía.", (await Assert.ThrowsAsync<ModuleDisabledException>(() => svc1.PreviewAsync(withRef, default))).Message);

        await EnableModuleAsync("SALES_ORDERS");
        var (svc, db) = Service(admin, new FakeEmailSender(), provider);
        await using var _ = db;
        var nf = await Assert.ThrowsAsync<NotFoundException>(() => svc.PreviewAsync(withRef with { Reference = "OV-99999" }, default));
        Assert.Equal("Orden de venta 'OV-99999' no encontrada.", nf.Message);

        var real = await svc.PreviewAsync(withRef, default);
        Assert.Equal(("Orden N-REAL", "<p style=\"font-family:Arial,Helvetica,sans-serif;font-size:14px;color:#111827\">Se solicit&#243; N-REAL.</p><table>Staff-es</table>", false), (real.Subject, real.BodyHtml, real.UsedSample));
        var customer = await svc.PreviewAsync(withRef with { Audience = "CUSTOMER", Lang = "en" }, default);
        Assert.Equal("<p style=\"font-family:Arial,Helvetica,sans-serif;font-size:14px;color:#111827\">Requested N-REAL.</p><table>Customer-en</table>", customer.BodyHtml);

        // un documento que ya no se debe avisar (p. ej. cancelado): 400 con el motivo del proveedor
        provider.SkipReason = Cancelled;
        var skipped = await Invalid(() => svc.PreviewAsync(withRef, default));
        Assert.Equal(("reference", Cancelled), (FieldOf(skipped), skipped.Message));
    }

    [SqlServerFact]
    public async Task The_test_send_uses_the_sample_or_the_real_document_and_carries_the_attachment_only_when_the_body_uses_the_block()
    {
        var admin = await UserAsync(A, $"admin{NextId()}@a.com");
        await GrantAsync(admin, "sales.view");
        await EnableModuleAsync("SALES_ORDERS");
        var email = new FakeEmailSender();
        var provider = new FakeContentProvider();
        var (svc, db) = Service(admin, email, provider);
        await using var _ = db;
        var rule = await svc.CreateRuleAsync(new NotificationRuleRequest(CtxEventProvider.Code, "Prueba " + NextId()), default);

        Assert.Equal("SENT", (await svc.TestAsync(rule.PublicId, new NotificationTestRequest("EMAIL"), default)).Status);
        var sample = email.Sent.Last();
        Assert.Equal(("Orden N-1", "<p style=\"font-family:Arial,Helvetica,sans-serif;font-size:14px;color:#111827\">Se solicit&#243; N-1.</p><table>ejemplo-Staff</table>"), (sample.Subject, sample.BodyHtml));
        Assert.Single(sample.Attachments!);

        Assert.Equal("SENT", (await svc.TestAsync(rule.PublicId, new NotificationTestRequest("EMAIL", "N-REAL"), default)).Status);
        var real = email.Sent.Last();
        Assert.Equal(("Orden N-REAL", "<p style=\"font-family:Arial,Helvetica,sans-serif;font-size:14px;color:#111827\">Se solicit&#243; N-REAL.</p><table>Staff-es</table>"), (real.Subject, real.BodyHtml));
        Assert.Single(real.Attachments!);
        await Assert.ThrowsAsync<NotFoundException>(() => svc.TestAsync(rule.PublicId, new NotificationTestRequest("EMAIL", "OV-99999"), default));

        // un correo sin el bloque: sin adjuntos
        await svc.UpdateRuleAsync(rule.PublicId, new NotificationRuleRequest(Channels: new[] { new NotificationChannelRequest("EMAIL", null, BodyEs: "Solo {number}") }), default);
        await svc.TestAsync(rule.PublicId, new NotificationTestRequest("EMAIL"), default);
        Assert.Null(email.Sent.Last().Attachments);

        // el documento ya no se debe avisar: SKIPPED con el motivo, sin enviar
        provider.SkipReason = Cancelled;
        var count = email.Sent.Count;
        var skipped = await svc.TestAsync(rule.PublicId, new NotificationTestRequest("EMAIL", "N-REAL"), default);
        Assert.Equal(("SKIPPED", Cancelled), (skipped.Status, skipped.Message));
        Assert.Equal(count, email.Sent.Count);
    }
}
