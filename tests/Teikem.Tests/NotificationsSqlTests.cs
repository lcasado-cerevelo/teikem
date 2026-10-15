using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
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

/// <summary>
/// Lote 51 — avisos contra SQL Server REAL (el reclamo de la cola es un UPDATE atómico; InMemory no lo soporta). Base DESECHABLE propia
/// (Teikem_notiftest_&lt;guid&gt;, se borra al terminar), creada desde el modelo EF y sin las llaves foráneas (se siembran usuarios, roles y
/// compañías sueltos); los índices únicos SÍ quedan (la idempotencia depende de ellos). Se omite sin TEIKEM_TEST_SQLSERVER.
/// </summary>
public sealed class NotificationSqlFixture : IAsyncLifetime
{
    public string? ConnectionString { get; private set; }
    private string? _database;

    public async Task InitializeAsync()
    {
        var server = Environment.GetEnvironmentVariable(TenantIsolationSqlServerFixture.EnvVar);
        if (string.IsNullOrWhiteSpace(server)) return;
        _database = "Teikem_notiftest_" + Guid.NewGuid().ToString("N")[..12];
        ConnectionString = new SqlConnectionStringBuilder(server) { InitialCatalog = _database }.ConnectionString;
        try
        {
            await using var db = NewDb(new TenantContext());
            await db.Database.EnsureCreatedAsync();
            await db.Database.ExecuteSqlRawAsync("""
                DECLARE @sql nvarchar(max) = N'';
                SELECT @sql += N'ALTER TABLE ' + QUOTENAME(SCHEMA_NAME(t.schema_id)) + N'.' + QUOTENAME(t.name) + N' DROP CONSTRAINT ' + QUOTENAME(f.name) + N';'
                  FROM sys.foreign_keys f JOIN sys.tables t ON t.object_id = f.parent_object_id;
                EXEC sp_executesql @sql;
                """);
        }
        catch
        {
            await DisposeAsync();
            throw;
        }
    }

    /// <summary>Una compañía nueva (cada prueba usa las suyas para que las reglas de una no avisen en otra).</summary>
    public async Task<int> NewTenantAsync(string name)
    {
        await using var db = NewDb(new TenantContext());
        var t = new Tenant { Name = name, DefaultLangCode = "es" };
        db.Tenants.Add(t);
        await db.SaveChangesAsync();
        return t.TenantId;
    }

    public TeikemDbContext NewDb(ITenantContext tenant) => new(new DbContextOptionsBuilder<TeikemDbContext>()
        .UseSqlServer(ConnectionString)
        .ConfigureWarnings(w => w.Ignore(CoreEventId.PossibleIncorrectRequiredNavigationWithQueryFilterInteractionWarning)).Options, tenant);

    public TeikemDbContext NewDb(int tenantId, int? userId = null) => NewDb(new TenantContext { TenantId = tenantId, UserId = userId });

    public static NotificationEventCatalog Catalog() => new(new INotificationEventProvider[] { new TestEventProvider() });

    /// <summary>Un procesador completo: su propio DI, la bandeja real, SMS y push «no configurado» y el correo que se le pase.</summary>
    public NotificationProcessor NewProcessor(INotificationChannelSender email)
    {
        var services = new ServiceCollection();
        services.AddScoped<TenantContext>();
        services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());
        services.AddScoped(sp => NewDb(sp.GetRequiredService<ITenantContext>()));
        services.AddSingleton(email);
        services.AddScoped<INotificationChannelSender, AppChannelSender>();
        services.AddScoped<INotificationChannelSender, SmsChannelSender>();
        services.AddScoped<INotificationChannelSender, PushChannelSender>();
        var provider = services.BuildServiceProvider();
        return new NotificationProcessor(provider.GetRequiredService<IServiceScopeFactory>(), Catalog(), NullLogger<NotificationProcessor>.Instance);
    }

    public async Task DisposeAsync()
    {
        if (_database is null) return;
        SqlConnection.ClearAllPools();
        var master = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable(TenantIsolationSqlServerFixture.EnvVar)) { InitialCatalog = "master" }.ConnectionString;
        await using var c = new SqlConnection(master);
        await c.OpenAsync();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = $"IF DB_ID('{_database}') IS NOT NULL BEGIN ALTER DATABASE [{_database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_database}]; END";
        await cmd.ExecuteNonQueryAsync();
    }
}

/// <summary>Un canal de correo falso: anota lo que «envía»; puede configurarse como caído o como no configurado.</summary>
public sealed class FakeEmailSender : INotificationChannelSender
{
    private readonly object _gate = new();
    public List<NotificationDelivery> Sent { get; } = new();
    public bool Fail { get; set; }
    public bool Configured { get; set; } = true;
    public string Channel => NotificationChannels.Email;
    public bool IsConfigured => Configured;

    public Task<SendOutcome> SendAsync(NotificationDelivery d, CancellationToken ct)
    {
        if (!Configured) return Task.FromResult(SendOutcome.Skipped(NotificationRules.NotConfiguredMessage(Channel)));
        if (Fail) return Task.FromResult(SendOutcome.Failed("el correo está caído"));
        lock (_gate) Sent.Add(d);
        return Task.FromResult(SendOutcome.Sent());
    }
}

public sealed class NotificationsSqlTests(NotificationSqlFixture fx) : IClassFixture<NotificationSqlFixture>, IAsyncLifetime
{
    private int A;
    private int B;

    public async Task InitializeAsync()
    {
        if (fx.ConnectionString is null) return;   // sin SQL Server de prueba: las pruebas se omiten
        (A, B) = (await fx.NewTenantAsync("Compañía A"), await fx.NewTenantAsync("Compañía B"));
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static int _seed = 100;
    private static int NextId() => Interlocked.Increment(ref _seed);

    // ---- siembra ----

    private async Task<int> UserAsync(int tenantId, string email, bool active = true, string? phone = null, string? fullName = null)
    {
        await using var db = fx.NewDb(tenantId);
        var u = new ApplicationUser { UserName = email, Email = email, FullName = fullName ?? email, IsActive = active, PhoneNumber = phone };
        db.Users.Add(u);
        await db.SaveChangesAsync();
        db.UserTenants.Add(new UserTenant { UserId = u.Id, TenantId = tenantId, StatusCodeId = 1 });
        await db.SaveChangesAsync();
        return u.Id;
    }

    private async Task<int> RoleAsync(int tenantId, params int[] userIds)
    {
        await using var db = fx.NewDb(tenantId);
        var r = new Role { TenantId = tenantId, Name = "Rol " + NextId() };
        db.AppRoles.Add(r);
        await db.SaveChangesAsync();
        foreach (var uid in userIds) db.AppUserRoles.Add(new UserRole { UserId = uid, RoleId = r.RoleId, TenantId = tenantId });
        await db.SaveChangesAsync();
        return r.RoleId;
    }

    private async Task<Guid> RuleAsync(int tenantId, IEnumerable<NotificationRuleRecipient> recipients, bool email = true, bool app = true, bool sms = false, bool active = true, string eventCode = "TEST")
    {
        await using var db = fx.NewDb(tenantId);
        var rule = new NotificationRule { PublicId = Guid.NewGuid(), TenantId = tenantId, EventCode = eventCode, Name = "Regla " + NextId(), IsActive = active };
        foreach (var (channel, on) in new[] { ("EMAIL", email), ("SMS", sms), ("APP", app), ("PUSH", false) })
            rule.Channels.Add(new NotificationRuleChannel { TenantId = tenantId, Channel = channel, IsEnabled = on, SubjectJson = MultilingualText.Build("Asunto {message}", "Subject {message}"), BodyJson = MultilingualText.Build("Texto {message} de {user}", "Body {message} by {user}") });
        foreach (var r in recipients) { r.TenantId = tenantId; rule.Recipients.Add(r); }
        db.NotificationRules.Add(rule);
        await db.SaveChangesAsync();
        return rule.PublicId;
    }

    private static NotificationRuleRecipient Role(int id) => new() { Kind = "ROLE", RoleId = id };
    private static NotificationRuleRecipient UserR(int id) => new() { Kind = "USER", UserId = id };
    private static NotificationRuleRecipient EmailR(string v) => new() { Kind = "EMAIL", Value = v };
    private static NotificationRuleRecipient PhoneR(string v) => new() { Kind = "PHONE", Value = v };

    private async Task<long> PublishAsync(int tenantId, string? dedup = null, string message = "hola")
    {
        await using var db = fx.NewDb(tenantId, 1);
        var publisher = new NotificationPublisher(db, db.TenantForTest(), NotificationSqlFixture.Catalog());
        await publisher.PublishAsync("TEST", new Dictionary<string, string?> { ["message"] = message, ["user"] = "Ana" }, dedup, default);
        await db.SaveChangesAsync();
        return await db.NotificationOutbox.AsNoTracking().OrderByDescending(o => o.NotificationOutboxId).Select(o => o.NotificationOutboxId).FirstAsync();
    }

    private async Task<NotificationOutbox> OutboxAsync(int tenantId, long id)
    {
        await using var db = fx.NewDb(tenantId);
        return await db.NotificationOutbox.AsNoTracking().SingleAsync(o => o.NotificationOutboxId == id);
    }

    private async Task<List<NotificationLog>> LogAsync(int tenantId, long outboxId)
    {
        await using var db = fx.NewDb(tenantId);
        return await db.NotificationLogs.AsNoTracking().Where(l => l.NotificationOutboxId == outboxId).OrderBy(l => l.NotificationLogId).ToListAsync();
    }

    private async Task<List<UserNotification>> InboxOfAsync(int tenantId, int userId, long? outboxId = null)
    {
        await using var db = fx.NewDb(tenantId);
        return await db.UserNotifications.AsNoTracking().Where(n => n.UserId == userId && (outboxId == null || n.NotificationOutboxId == outboxId)).ToListAsync();
    }

    // ---- procesador ----

    [SqlServerFact]
    public async Task An_event_reaches_the_active_users_of_a_role_and_the_loose_email_and_the_not_configured_sms_is_skipped()
    {
        var u1 = await UserAsync(A, $"u{NextId()}@a.com", phone: "787-555-0101");
        var u2 = await UserAsync(A, $"u{NextId()}@a.com");
        var inactive = await UserAsync(A, $"u{NextId()}@a.com", active: false);
        var role = await RoleAsync(A, u1, u2, inactive);
        await RuleAsync(A, new[] { Role(role), EmailR("suelto@x.com"), PhoneR("787-555-0202") }, email: true, app: true, sms: true);
        var email = new FakeEmailSender();
        var processor = fx.NewProcessor(email);
        var outbox = await PublishAsync(A);

        Assert.True(await processor.TryProcessAsync(A, outbox, DateTime.UtcNow, default));

        var o = await OutboxAsync(A, outbox);
        Assert.Equal(("DONE", 1), (o.Status, o.Attempts));
        Assert.Null(o.LockedBy);
        // bandeja: los dos usuarios activos (el inactivo no)
        var n1 = Assert.Single(await InboxOfAsync(A, u1, outbox));
        Assert.Equal(("Asunto hola", "Texto hola de Ana", false), (n1.Title, n1.Body, n1.IsRead));
        Assert.Single(await InboxOfAsync(A, u2, outbox));
        Assert.Empty(await InboxOfAsync(A, inactive, outbox));
        // correo: el usuario con correo (los dos tienen) y el suelto, sin repetir
        Assert.Equal(3, email.Sent.Count);
        Assert.Contains(email.Sent, d => d.Email == "suelto@x.com" && d.Subject == "Asunto hola" && d.BodyHtml == "<p>Texto hola de Ana</p>" && d.CompanyName == "Compañía A");
        // registro: el SMS sin proveedor queda SKIPPED con su razón (a los dos que tienen teléfono: u1 y el suelto)
        var log = await LogAsync(A, outbox);
        Assert.All(log.Where(l => l.Channel == "SMS"), l => Assert.Equal(("SKIPPED", "El canal SMS todavía no está configurado en Teikem."), (l.Status, l.Error)));
        Assert.Equal(2, log.Count(l => l.Channel == "SMS"));
        Assert.Equal(3, log.Count(l => l.Channel == "EMAIL" && l.Status == "SENT"));
        Assert.Equal(2, log.Count(l => l.Channel == "APP" && l.Status == "SENT"));
    }

    [SqlServerFact]
    public async Task Processing_the_same_event_again_never_notifies_twice()
    {
        var u = await UserAsync(A, $"u{NextId()}@a.com");
        await RuleAsync(A, new[] { UserR(u), EmailR("otro@x.com") });
        var email = new FakeEmailSender();
        var processor = fx.NewProcessor(email);
        var outbox = await PublishAsync(A);
        await processor.TryProcessAsync(A, outbox, DateTime.UtcNow, default);
        Assert.Equal(2, email.Sent.Count);

        // un procesador que lo vuelve a tomar (la cola se reabre a mano) no repite nada
        await using (var db = fx.NewDb(A))
            await db.NotificationOutbox.Where(o => o.NotificationOutboxId == outbox).ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, "PENDING"));
        Assert.True(await processor.TryProcessAsync(A, outbox, DateTime.UtcNow, default));
        Assert.Equal(2, email.Sent.Count);
        Assert.Single(await InboxOfAsync(A, u, outbox));
        Assert.Equal(3, (await LogAsync(A, outbox)).Count);   // 2 correos + 1 bandeja
        // y uno que ya terminó no se toma
        Assert.False(await processor.TryProcessAsync(A, outbox, DateTime.UtcNow, default));
    }

    [SqlServerFact]
    public async Task A_failing_email_retries_only_what_failed_and_after_three_retries_it_fails_for_good_without_undoing_the_rest()
    {
        var u = await UserAsync(A, $"u{NextId()}@a.com");
        await RuleAsync(A, new[] { UserR(u) });
        var email = new FakeEmailSender { Fail = true };
        var processor = fx.NewProcessor(email);
        var outbox = await PublishAsync(A);

        await processor.TryProcessAsync(A, outbox, DateTime.UtcNow, default);
        var o = await OutboxAsync(A, outbox);
        Assert.Equal(("PENDING", 1), (o.Status, o.Attempts));
        Assert.InRange(o.NextAttemptUtc, DateTime.UtcNow.AddSeconds(30), DateTime.UtcNow.AddMinutes(2));   // 1 min
        Assert.Single(await InboxOfAsync(A, u, outbox));                                          // la bandeja sí salió
        Assert.Contains(await LogAsync(A, outbox), l => l.Channel == "EMAIL" && l.Status == "FAILED" && l.Error == "el correo está caído");

        // aún no toca: no se procesa
        Assert.False(await processor.TryProcessAsync(A, outbox, DateTime.UtcNow, default));

        // vuelve el correo: el reintento envía SOLO el correo y no repite la bandeja
        email.Fail = false;
        await processor.TryProcessAsync(A, outbox, DateTime.UtcNow.AddMinutes(2), default);
        o = await OutboxAsync(A, outbox);
        Assert.Equal(("DONE", 2), (o.Status, o.Attempts));
        Assert.Single(email.Sent);
        Assert.Single(await InboxOfAsync(A, u, outbox));

        // otro aviso con el correo siempre caído: 1 min, 5 min, 30 min y luego FAILED
        email.Fail = true;
        var second = await PublishAsync(A);
        var now = DateTime.UtcNow;
        var expected = new[] { 1, 5, 30 };
        for (var i = 0; i < 3; i++)
        {
            await processor.TryProcessAsync(A, second, now, default);
            var s = await OutboxAsync(A, second);
            Assert.Equal(("PENDING", i + 1), (s.Status, s.Attempts));
            Assert.InRange(s.NextAttemptUtc, DateTime.UtcNow.AddMinutes(expected[i]).AddSeconds(-30), DateTime.UtcNow.AddMinutes(expected[i]).AddSeconds(30));
            now = s.NextAttemptUtc.AddSeconds(1);
        }
        await processor.TryProcessAsync(A, second, now, default);
        var last = await OutboxAsync(A, second);
        Assert.Equal(("FAILED", 4), (last.Status, last.Attempts));
        Assert.Single(await InboxOfAsync(A, u, second));
    }

    [SqlServerFact]
    public async Task Eight_concurrent_workers_process_an_event_exactly_once()
    {
        var u = await UserAsync(A, $"u{NextId()}@a.com");
        await RuleAsync(A, new[] { UserR(u) });
        var email = new FakeEmailSender();
        var p1 = fx.NewProcessor(email);
        var p2 = fx.NewProcessor(email);
        var outbox = await PublishAsync(A);
        var now = DateTime.UtcNow;
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(i => Task.Run(() => (i % 2 == 0 ? p1 : p2).TryProcessAsync(A, outbox, now, default))));
        Assert.Single(results, r => r);
        Assert.Single(email.Sent);
        Assert.Single(await InboxOfAsync(A, u, outbox));
    }

    [SqlServerFact]
    public async Task A_dedup_key_never_queues_the_same_notice_twice_per_company_and_inactive_rules_do_not_fire()
    {
        var key = "orden-" + NextId();
        var first = await PublishAsync(A, key);
        await using (var db = fx.NewDb(A, 1))
        {
            var publisher = new NotificationPublisher(db, db.TenantForTest(), NotificationSqlFixture.Catalog());
            Assert.False(await publisher.PublishAsync("TEST", null, key, default));      // misma clave: no se anota
            Assert.Equal(1, await db.NotificationOutbox.CountAsync(o => o.DedupKey == key));
        }
        await using (var db = fx.NewDb(B, 1))
        {
            var publisher = new NotificationPublisher(db, db.TenantForTest(), NotificationSqlFixture.Catalog());
            Assert.True(await publisher.PublishAsync("TEST", null, key, default));        // otra compañía: sí
            await db.SaveChangesAsync();
        }
        // una regla apagada no avisa
        var u = await UserAsync(A, $"u{NextId()}@a.com");
        await RuleAsync(A, new[] { UserR(u) }, active: false);
        var email = new FakeEmailSender();
        await fx.NewProcessor(email).TryProcessAsync(A, first, DateTime.UtcNow, default);
        Assert.Empty(email.Sent);
        Assert.Empty(await InboxOfAsync(A, u, first));
        Assert.Equal("DONE", (await OutboxAsync(A, first)).Status);
        await using var db2 = fx.NewDb(A, 1);
        Assert.Throws<InvalidOperationException>(() => new NotificationPublisher(db2, db2.TenantForTest(), NotificationSqlFixture.Catalog()).PublishAsync("NO_EXISTE", null, null, default).GetAwaiter().GetResult());
    }

    [SqlServerFact]
    public async Task A_rule_of_another_company_never_fires_and_never_sees_the_other_companys_users()
    {
        var ua = await UserAsync(A, $"u{NextId()}@a.com");
        var ub = await UserAsync(B, $"u{NextId()}@b.com");
        await RuleAsync(B, new[] { UserR(ub), EmailR("b@x.com") });      // regla de B
        await RuleAsync(A, new[] { UserR(ua), UserR(ub) });              // regla de A que (mal) nombra a un usuario de B
        var email = new FakeEmailSender();
        var outbox = await PublishAsync(A);
        await fx.NewProcessor(email).TryProcessAsync(A, outbox, DateTime.UtcNow, default);
        Assert.Single(await InboxOfAsync(A, ua, outbox));
        Assert.Empty(await InboxOfAsync(B, ub, outbox));                 // el usuario de B no es de A: no se le avisa
        Assert.DoesNotContain(email.Sent, d => d.Email == "b@x.com");             // ni se usa la regla de B
        Assert.Equal(A, email.Sent.Single().TenantId);
    }

    [SqlServerFact]
    public async Task A_user_who_muted_an_optional_event_does_not_get_it_in_the_inbox()
    {
        var u = await UserAsync(A, $"u{NextId()}@a.com");
        await using (var db = fx.NewDb(A))
        {
            db.UserNotificationPrefs.Add(new UserNotificationPref { TenantId = A, UserId = u, EventCode = "TEST", IsMuted = true });
            await db.SaveChangesAsync();
        }
        await RuleAsync(A, new[] { UserR(u) });
        var outbox = await PublishAsync(A);
        await fx.NewProcessor(new FakeEmailSender()).TryProcessAsync(A, outbox, DateTime.UtcNow, default);
        Assert.Empty(await InboxOfAsync(A, u, outbox));
        Assert.Contains(await LogAsync(A, outbox), l => l.Channel == "APP" && l.Status == "SKIPPED" && l.Error == "El usuario silenció este aviso.");
    }

    [SqlServerFact]
    public async Task An_email_channel_that_is_not_configured_is_skipped_not_failed()
    {
        var u = await UserAsync(A, $"u{NextId()}@a.com");
        await RuleAsync(A, new[] { UserR(u) });
        var outbox = await PublishAsync(A);
        await fx.NewProcessor(new FakeEmailSender { Configured = false }).TryProcessAsync(A, outbox, DateTime.UtcNow, default);
        Assert.Equal(("DONE", 1), ((await OutboxAsync(A, outbox)).Status, (await OutboxAsync(A, outbox)).Attempts));
        Assert.Contains(await LogAsync(A, outbox), l => l.Channel == "EMAIL" && l.Status == "SKIPPED");
    }

    // ---- servicio de reglas ----

    private (NotificationRuleService Svc, TeikemDbContext Db) Service(int tenantId, int userId, FakeEmailSender? email = null)
    {
        var db = fx.NewDb(tenantId, userId);
        var ctx = db.TenantForTest();
        var senders = new INotificationChannelSender[] { email ?? new FakeEmailSender(), new AppChannelSender(db), new SmsChannelSender(), new PushChannelSender() };
        var catalog = NotificationSqlFixture.Catalog();
        return (new NotificationRuleService(db, ctx, catalog, senders, new NotificationPublisher(db, ctx, catalog)), db);
    }

    private static async Task<ValidationException> Invalid(Func<Task> act) => await Assert.ThrowsAsync<ValidationException>(act);

    [SqlServerFact]
    public async Task Creating_a_rule_builds_the_four_channels_off_with_the_default_texts_and_validates_with_exact_messages()
    {
        var admin = await UserAsync(A, $"admin{NextId()}@a.com");
        var role = await RoleAsync(A, admin);
        var (svc, db) = Service(A, admin);
        await using var _ = db;
        var name = "Mi regla " + NextId();

        var created = await svc.CreateRuleAsync(new NotificationRuleRequest("test", $"  {name} "), default);
        Assert.Equal((name, "TEST", false), (created.Name, created.EventCode, created.IsActive));
        Assert.Equal(new[] { "EMAIL", "SMS", "APP", "PUSH" }, created.Channels.Select(c => c.Channel));
        Assert.All(created.Channels, c => Assert.False(c.IsEnabled));
        Assert.Equal(new[] { true, false, true, false }, created.Channels.Select(c => c.IsConfigured));   // correo falso sí; SMS/push no
        Assert.Equal("{message} (disparado por {user})", created.Channels[0].BodyEs);

        // validaciones
        Assert.Equal("Indique el evento de la regla.", (await Invalid(() => svc.CreateRuleAsync(new NotificationRuleRequest(null, "x"), default))).Message);
        Assert.Equal("Evento de aviso desconocido: 'NOPE'.", (await Invalid(() => svc.CreateRuleAsync(new NotificationRuleRequest("NOPE", "x"), default))).Message);
        Assert.Equal("Escriba el nombre de la regla.", (await Invalid(() => svc.CreateRuleAsync(new NotificationRuleRequest("TEST", "  "), default))).Message);
        Assert.Equal("El nombre de la regla no puede pasar de 100 caracteres.", (await Invalid(() => svc.CreateRuleAsync(new NotificationRuleRequest("TEST", new string('n', 101)), default))).Message);
        var dup = await Assert.ThrowsAsync<ConflictException>(() => svc.CreateRuleAsync(new NotificationRuleRequest("TEST", name), default));
        Assert.Equal("Ya existe una regla con ese nombre.", dup.Message);

        var other = "Otra " + NextId();
        Assert.Equal("El canal SMS todavía no está configurado en Teikem.", (await Invalid(() => svc.CreateRuleAsync(new NotificationRuleRequest("TEST", other, null,
            new[] { new NotificationChannelRequest("SMS", true) }), default))).Message);
        Assert.Equal("Canal desconocido: 'fax'. Use EMAIL, SMS, APP o PUSH.", (await Invalid(() => svc.CreateRuleAsync(new NotificationRuleRequest("TEST", other, null,
            new[] { new NotificationChannelRequest("fax", true) }), default))).Message);
        Assert.Equal("El canal EMAIL necesita un asunto.", (await Invalid(() => svc.CreateRuleAsync(new NotificationRuleRequest("TEST", other, null,
            new[] { new NotificationChannelRequest("EMAIL", true, "", "") }), default))).Message);
        Assert.Equal("El canal APP necesita un texto.", (await Invalid(() => svc.CreateRuleAsync(new NotificationRuleRequest("TEST", other, null,
            new[] { new NotificationChannelRequest("APP", true, BodyEs: "", BodyEn: "") }), default))).Message);
        Assert.Equal("La plantilla usa una variable que el evento no tiene: {zzz}.", (await Invalid(() => svc.CreateRuleAsync(new NotificationRuleRequest("TEST", other, null,
            new[] { new NotificationChannelRequest("APP", false, BodyEs: "hola {zzz}") }), default))).Message);
        Assert.Equal("El asunto del canal EMAIL no puede pasar de 200 caracteres.", (await Invalid(() => svc.CreateRuleAsync(new NotificationRuleRequest("TEST", other, null,
            new[] { new NotificationChannelRequest("EMAIL", false, SubjectEs: new string('a', 201)) }), default))).Message);
        Assert.Equal("La regla necesita al menos un destinatario para poder activarse.", (await Invalid(() => svc.CreateRuleAsync(new NotificationRuleRequest("TEST", other, true,
            new[] { new NotificationChannelRequest("APP", true) }), default))).Message);
        Assert.Equal("La regla necesita al menos un canal encendido para poder activarse.", (await Invalid(() => svc.CreateRuleAsync(new NotificationRuleRequest("TEST", other, true,
            null, new[] { new NotificationRecipientRequest("ROLE", RoleId: role) }), default))).Message);
        Assert.Equal("Tipo de destinatario desconocido: 'GROUP'. Use ROLE, USER, EMAIL o PHONE.", (await Invalid(() => svc.CreateRuleAsync(new NotificationRuleRequest("TEST", other, null,
            null, new[] { new NotificationRecipientRequest("GROUP") }), default))).Message);
        Assert.Equal("Correo no válido: 'x@'.", (await Invalid(() => svc.CreateRuleAsync(new NotificationRuleRequest("TEST", other, null,
            null, new[] { new NotificationRecipientRequest("EMAIL", Value: "x@") }), default))).Message);
        Assert.Equal("Teléfono no válido: '12'.", (await Invalid(() => svc.CreateRuleAsync(new NotificationRuleRequest("TEST", other, null,
            null, new[] { new NotificationRecipientRequest("PHONE", Value: "12") }), default))).Message);
        Assert.Equal("Rol no encontrado.", (await Invalid(() => svc.CreateRuleAsync(new NotificationRuleRequest("TEST", other, null,
            null, new[] { new NotificationRecipientRequest("ROLE", RoleId: 999999) }), default))).Message);
        Assert.Equal("Usuario no encontrado en esta compañía.", (await Invalid(() => svc.CreateRuleAsync(new NotificationRuleRequest("TEST", other, null,
            null, new[] { new NotificationRecipientRequest("USER", UserId: 999999) }), default))).Message);
    }

    [SqlServerFact]
    public async Task A_role_or_user_of_another_company_cannot_be_a_recipient()
    {
        var adminA = await UserAsync(A, $"admin{NextId()}@a.com");
        var userB = await UserAsync(B, $"b{NextId()}@b.com");
        var roleB = await RoleAsync(B, userB);
        var (svc, db) = Service(A, adminA);
        await using var _ = db;
        Assert.Equal("Rol no encontrado.", (await Invalid(() => svc.CreateRuleAsync(new NotificationRuleRequest("TEST", "R" + NextId(), null, null, new[] { new NotificationRecipientRequest("ROLE", RoleId: roleB) }), default))).Message);
        Assert.Equal("Usuario no encontrado en esta compañía.", (await Invalid(() => svc.CreateRuleAsync(new NotificationRuleRequest("TEST", "R" + NextId(), null, null, new[] { new NotificationRecipientRequest("USER", UserId: userB) }), default))).Message);
    }

    [SqlServerFact]
    public async Task Updating_a_rule_replaces_recipients_changes_only_the_channels_sent_and_honors_the_row_version()
    {
        var admin = await UserAsync(A, $"admin{NextId()}@a.com");
        var other = await UserAsync(A, $"x{NextId()}@a.com", fullName: "Pedro Pérez");
        var (svc, db) = Service(A, admin);
        await using var _ = db;
        var rule = await svc.CreateRuleAsync(new NotificationRuleRequest("TEST", "Edit " + NextId(), true,
            new[] { new NotificationChannelRequest("APP", true, "Hola {message}", "Hi {message}", "Texto {message}", "Text {message}") },
            new[] { new NotificationRecipientRequest("USER", UserId: admin), new NotificationRecipientRequest("EMAIL", Value: "a@x.com"), new NotificationRecipientRequest("email", Value: "A@X.com") }), default);
        Assert.True(rule.IsActive);
        Assert.Equal(2, rule.Recipients.Count);   // el correo repetido (sin importar mayúsculas) se ignora

        var updated = await svc.UpdateRuleAsync(rule.PublicId, new NotificationRuleRequest(null, null, null,
            new[] { new NotificationChannelRequest("APP", null, SubjectEn: "Hey {message}") },
            new[] { new NotificationRecipientRequest("USER", UserId: other), new NotificationRecipientRequest("PHONE", Value: "787-555-0101") }), default);
        Assert.Equal(new[] { "Pedro Pérez (" + $"{await EmailOfAsync(other)})", "787-555-0101" }, updated.Recipients.Select(r => r.Label));
        var app = updated.Channels.Single(c => c.Channel == "APP");
        Assert.Equal(("Hola {message}", "Hey {message}", true), (app.SubjectEs, app.SubjectEn, app.IsEnabled));   // solo cambió el inglés del asunto

        // apagarla y la versión vieja
        var off = await svc.UpdateRuleAsync(rule.PublicId, new NotificationRuleRequest(IsActive: false), default);
        Assert.False(off.IsActive);
        var stale = await Assert.ThrowsAsync<ConflictException>(() => svc.UpdateRuleAsync(rule.PublicId, new NotificationRuleRequest(Name: "Otro nombre", RowVersion: rule.RowVersion), default));
        Assert.Equal("El registro fue modificado por otro usuario; recargue e intente de nuevo.", stale.Message);
        var nf = await Assert.ThrowsAsync<NotFoundException>(() => svc.UpdateRuleAsync(Guid.NewGuid(), new NotificationRuleRequest(), default));
        Assert.Equal("Regla de aviso no encontrada.", nf.Message);
        Assert.Equal("El evento de una regla no se cambia; cree otra regla.", (await Invalid(() => svc.UpdateRuleAsync(rule.PublicId, new NotificationRuleRequest(EventCode: "OTRO"), default))).Message);
    }

    private async Task<string> EmailOfAsync(int userId)
    {
        await using var db = fx.NewDb(A);
        return await db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.Email!).SingleAsync();
    }

    [SqlServerFact]
    public async Task The_test_send_reaches_the_requester_with_sample_values_and_reports_not_configured_channels()
    {
        var admin = await UserAsync(A, $"admin{NextId()}@a.com");
        var email = new FakeEmailSender();
        var (svc, db) = Service(A, admin, email);
        await using var _ = db;
        var rule = await svc.CreateRuleAsync(new NotificationRuleRequest("TEST", "Prueba " + NextId()), default);

        var viaEmail = await svc.TestAsync(rule.PublicId, new NotificationTestRequest("email"), default);
        Assert.Equal(("EMAIL", "SENT", null), (viaEmail.Channel, viaEmail.Status, viaEmail.Message));
        Assert.Equal("Esto es una prueba. (disparado por Administrador)", email.Sent.Single().BodyText);
        var viaApp = await svc.TestAsync(rule.PublicId, new NotificationTestRequest("APP"), default);
        Assert.Equal("SENT", viaApp.Status);
        Assert.Single(await InboxOfAsync(A, admin));
        var viaSms = await svc.TestAsync(rule.PublicId, new NotificationTestRequest("SMS"), default);
        Assert.Equal(("SKIPPED", "El canal SMS todavía no está configurado en Teikem."), (viaSms.Status, viaSms.Message));
        email.Fail = true;
        Assert.Equal("FAILED", (await svc.TestAsync(rule.PublicId, new NotificationTestRequest("EMAIL"), default)).Status);
        Assert.Equal("Indique el canal.", (await Invalid(() => svc.TestAsync(rule.PublicId, new NotificationTestRequest(null), default))).Message);
    }

    [SqlServerFact]
    public async Task The_test_event_goes_through_the_real_queue_and_the_log_is_filterable_and_per_company()
    {
        var admin = await UserAsync(A, $"admin{NextId()}@a.com");
        var (svc, db) = Service(A, admin);
        await using var _ = db;
        var created = await svc.CreateRuleAsync(new NotificationRuleRequest("TEST", "Cola " + NextId(), true,
            new[] { new NotificationChannelRequest("APP", true) }, new[] { new NotificationRecipientRequest("USER", UserId: admin) }), default);
        Assert.True((await svc.PublishTestEventAsync(new NotificationTestEventRequest("Hola desde la prueba"), default)).Queued);

        var outbox = await db.NotificationOutbox.AsNoTracking().OrderByDescending(o => o.NotificationOutboxId).Select(o => o.NotificationOutboxId).FirstAsync();
        await fx.NewProcessor(new FakeEmailSender()).TryProcessAsync(A, outbox, DateTime.UtcNow, default);
        var n = Assert.Single(await InboxOfAsync(A, admin, outbox));
        Assert.Contains("Hola desde la prueba", n.Body);

        var log = await svc.ListLogAsync("test", "app", "sent", null, null, 0, 50, default);
        var line = Assert.Single(log.Items, l => l.OutboxId == outbox);
        Assert.Equal((created.PublicId, created.Name, "APP", "SENT"), (line.RulePublicId, line.RuleName, line.Channel, line.Status));
        Assert.Empty((await svc.ListLogAsync(null, "SMS", null, null, null, 0, 50, default)).Items.Where(l => l.OutboxId == outbox));
        // otra compañía no ve nada de esto
        var (svcB, dbB) = Service(B, 1);
        await using var __ = dbB;
        Assert.DoesNotContain((await svcB.ListLogAsync(null, null, null, null, null, 0, 200, default)).Items, l => l.OutboxId == outbox);
        Assert.DoesNotContain(await svcB.ListRulesAsync(default), r => r.PublicId == created.PublicId);
        await Assert.ThrowsAsync<NotFoundException>(() => svcB.GetRuleAsync(created.PublicId, default));
    }

    // ---- bandeja ----

    [SqlServerFact]
    public async Task The_inbox_lists_counts_marks_read_and_only_touches_the_owners_own_notices()
    {
        var ana = await UserAsync(A, $"ana{NextId()}@a.com");
        var luis = await UserAsync(A, $"luis{NextId()}@a.com");
        long first, second, theirs;
        await using (var db = fx.NewDb(A))
        {
            var n1 = new UserNotification { TenantId = A, UserId = ana, EventCode = "TEST", Title = "Uno", CreatedAtUtc = DateTime.UtcNow };
            var n2 = new UserNotification { TenantId = A, UserId = ana, EventCode = "TEST", Title = "Dos", CreatedAtUtc = DateTime.UtcNow };
            var n3 = new UserNotification { TenantId = A, UserId = luis, EventCode = "TEST", Title = "De Luis", CreatedAtUtc = DateTime.UtcNow };
            db.UserNotifications.AddRange(n1, n2, n3);
            await db.SaveChangesAsync();
            (first, second, theirs) = (n1.UserNotificationId, n2.UserNotificationId, n3.UserNotificationId);
        }
        await using var adb = fx.NewDb(A, ana);
        var inbox = new NotificationInboxService(adb, adb.TenantForTest(), NotificationSqlFixture.Catalog());

        Assert.Equal(2, (await inbox.UnreadCountAsync(default)).Count);
        var list = await inbox.ListAsync(false, 0, 20, default);
        Assert.Equal(new[] { "Dos", "Uno" }, list.Items.Select(i => i.Title));   // lo más nuevo primero
        Assert.Equal(2, list.Total);

        Assert.True((await inbox.MarkReadAsync(first, default)).IsRead);
        Assert.Equal(1, (await inbox.UnreadCountAsync(default)).Count);
        Assert.Single((await inbox.ListAsync(true, 0, 20, default)).Items);
        var nf = await Assert.ThrowsAsync<NotFoundException>(() => inbox.MarkReadAsync(theirs, default));    // el aviso de otro usuario no existe para Ana
        Assert.Equal("Aviso no encontrado.", nf.Message);
        Assert.Equal(1, (await inbox.MarkAllReadAsync(default)).Marked);
        Assert.Equal(0, (await inbox.UnreadCountAsync(default)).Count);
        await using var ldb = fx.NewDb(A, luis);
        Assert.Equal(1, (await new NotificationInboxService(ldb, ldb.TenantForTest(), NotificationSqlFixture.Catalog()).UnreadCountAsync(default)).Count);   // lo de Luis intacto
        Assert.Equal(second, (await inbox.ListAsync(false, 0, 1, default)).Items.Single().Id);
    }

    [SqlServerFact]
    public async Task Muting_only_applies_to_optional_events_and_can_be_undone()
    {
        var ana = await UserAsync(A, $"ana{NextId()}@a.com");
        await using var db = fx.NewDb(A, ana);
        var inbox = new NotificationInboxService(db, db.TenantForTest(), NotificationSqlFixture.Catalog());
        var pref = Assert.Single(await inbox.PrefsAsync(default));
        Assert.Equal(("TEST", false), (pref.EventCode, pref.IsMuted));
        Assert.True((await inbox.SetMutedAsync("test", new NotificationPrefRequest(), default)).IsMuted);
        Assert.True(Assert.Single(await inbox.PrefsAsync(default)).IsMuted);
        Assert.False((await inbox.SetMutedAsync("TEST", new NotificationPrefRequest(false), default)).IsMuted);
        var nf = await Assert.ThrowsAsync<NotFoundException>(() => inbox.SetMutedAsync("NOPE", new NotificationPrefRequest(), default));
        Assert.Equal("Evento de aviso no encontrado.", nf.Message);
    }
}

internal static class NotificationTestExtensions
{
    /// <summary>El contexto de compañía del DbContext (los de prueba lo crean con TenantContext).</summary>
    public static ITenantContext TenantForTest(this TeikemDbContext db)
        => (ITenantContext)typeof(TeikemDbContext).GetField("_tenant", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(db)!;
}
