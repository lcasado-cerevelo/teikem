using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Catalogs;
using Teikem.Domain.Clients;
using Teikem.Domain.Constants;
using Teikem.Domain.Contacts;
using Teikem.Domain.Files;
using Teikem.Domain.Notifications;
using Teikem.Domain.Tax;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Jobs;
using Teikem.Infrastructure.Notifications;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>Lote 248 — reloj de prueba con la hora que se pida (la zona de la compañía es la de omisión, Puerto Rico).</summary>
internal sealed class ExpiryTestClock(DateTime nowUtc) : ITenantClock
{
    public DateTime Now { get; set; } = nowUtc;
    public TimeZoneInfo Zone => TenantClock.Default.Zone;
    public DateTime UtcNow => Now;
    public DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(Now, Zone));
    public DateTime StartOfDayUtc(DateOnly day) => TenantClock.Default.StartOfDayUtc(day);
    public DateOnly DayOf(DateTime utc) => TenantClock.Default.DayOf(utc);
    public (DateTime? FromUtc, DateTime? ToUtcExclusive) UtcRange(DateOnly? from, DateOnly? to) => TenantClock.Default.UtcRange(from, to);

    public void NextDay(int days = 1) => Now = Now.AddDays(days);
}

/// <summary>
/// Lote 248 — el mundo de las pruebas de los avisos de vencimiento sobre <see cref="WmsFixture"/> (InMemory, datos INVENTADOS): tipos de documento con marcas, el catálogo
/// de eventos con los de documentos, el publicador real, la fuente CLIENT_DOCUMENT, el escaneo y el contenido; módulo de catálogo encendido. Hoy = 2026-11-27.
/// </summary>
internal static class DocumentExpiryTestSeed
{
    public const int StateType = 9301, OtherType = 9302, ContactEntity = 9310, EmailType = 9311;
    public static readonly DateTime NowUtc = new(2026, 11, 27, 16, 0, 0, DateTimeKind.Utc);
    public static readonly DateOnly Today = new(2026, 11, 27);
    private static int _nextFile = 80000;

    public static async Task<WmsFixture> CreateAsync(ExpiryTestClock clock, Action<IServiceCollection>? extra = null)
    {
        var f = await WmsFixture.CreateAsync(s =>
        {
            s.AddSingleton<ITenantClock>(clock);
            s.AddSingleton<INotificationEventProvider, ClientDocumentEventProvider>();
            s.AddSingleton<NotificationEventCatalog>();
            s.AddSingleton<INotificationPublisher, NotificationPublisher>();
            s.AddSingleton<IExpiryNoticeSource, ClientDocumentExpirySource>();
            s.AddSingleton<DocumentExpiryScanService>();
            s.AddSingleton<ClientDocumentNoticeContent>();
            extra?.Invoke(s);
        });
        f.SetModules(ModuleKeys.Catalog);
        f.Db.LookupCodes.AddRange(
            new LookupCode { LookupCodeId = StateType, Entity = ClientDocumentCodes.TypeDomain, InternalCode = ClientDocumentCodes.IvuExemptionState,
                LabelJson = "{\"es\":\"Exención estatal\",\"en\":\"State exemption\"}", ExtraJson = "{\"requiresValidTo\":true,\"isTax\":true}", IsActive = true },
            new LookupCode { LookupCodeId = OtherType, Entity = ClientDocumentCodes.TypeDomain, InternalCode = ClientDocumentCodes.Other, LabelJson = "{\"es\":\"Otro\",\"en\":\"Other\"}", IsActive = true },
            new LookupCode { LookupCodeId = ContactEntity, Entity = LookupDomains.EntityType, InternalCode = EntityTypes.ClientContact, LabelJson = "{\"es\":\"Contacto\"}", IsActive = true },
            new LookupCode { LookupCodeId = EmailType, Entity = LookupDomains.ContactType, InternalCode = "EMAIL", LabelJson = "{\"es\":\"Correo\"}", IsActive = true });
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        f.Lookups.Load(await f.Db.LookupCodes.AsNoTracking().ToListAsync());
        return f;
    }

    public static async Task<ClientDocument> DocAsync(WmsFixture f, Client client, DateOnly? validTo, string? number = "E-1", int typeId = StateType)
    {
        var file = new StoredFile
        {
            StoredFileId = Interlocked.Increment(ref _nextFile), PublicId = Guid.NewGuid(), TenantId = client.TenantId, OwnerEntityLookupId = 1, OwnerId = client.ClientId,
            FileName = "c.pdf", ContentType = "application/pdf", SizeBytes = 10, Sha256 = "x", StorageKey = "k", IsActive = true,
        };
        var doc = new ClientDocument
        {
            PublicId = Guid.NewGuid(), TenantId = client.TenantId, ClientId = client.ClientId, StoredFileId = file.StoredFileId, DocumentTypeLookupId = typeId,
            DocumentNumber = number, ValidFrom = validTo?.AddYears(-1), ValidTo = validTo, IsActive = true, CreatedAtUtc = DateTime.UtcNow,
        };
        f.Db.Set<StoredFile>().Add(file);
        f.Db.Set<ClientDocument>().Add(doc);
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        return doc;
    }

    public static async Task<ClientTaxExemption> ExemptionAsync(WmsFixture f, ClientDocument doc)
    {
        var e = new ClientTaxExemption
        {
            PublicId = Guid.NewGuid(), TenantId = doc.TenantId, ClientId = doc.ClientId, ExemptionType = "Estatal", CertificateNumber = doc.DocumentNumber ?? "-",
            ValidFrom = doc.ValidFrom ?? Today, ValidTo = doc.ValidTo, IsActive = true, ClientDocumentId = doc.ClientDocumentId, CreatedAtUtc = DateTime.UtcNow,
        };
        f.Db.Set<ClientTaxExemption>().Add(e);
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        return e;
    }

    public static async Task UpdateDocAsync(WmsFixture f, ClientDocument doc, Action<ClientDocument> change)
    {
        var row = await f.Db.Set<ClientDocument>().IgnoreQueryFilters().AsTracking().SingleAsync(d => d.ClientDocumentId == doc.ClientDocumentId);
        change(row);
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
    }

    public static async Task UpdateClientAsync(WmsFixture f, Client client, Action<Client> change)
    {
        var row = await f.Db.Clients.IgnoreQueryFilters().AsTracking().SingleAsync(c => c.ClientId == client.ClientId);
        change(row);
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
    }

    public static async Task TenantAsync(WmsFixture f, int tenantId, string? thresholds = null, bool? daily = null, bool? notifyClient = null, string? lang = null)
    {
        var t = await f.Db.Tenants.IgnoreQueryFilters().AsTracking().SingleAsync(x => x.TenantId == tenantId);
        if (thresholds is not null) t.DocExpiryThresholdDays = thresholds;
        if (daily is bool d) t.DocExpiryDailyAfterExpiry = d;
        if (notifyClient is bool n) t.DocExpiryNotifyClient = n;
        if (lang is not null) t.DefaultLangCode = lang;
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
    }

    /// <summary>Un contacto del cliente con su correo (principal o marcado «Recibe avisos por correo»).</summary>
    public static async Task ContactAsync(WmsFixture f, Client client, string name, string? email, bool primary = false, bool flagged = false, bool active = true)
    {
        var c = new ClientContact { ClientId = client.ClientId, FullName = name, IsPrimary = primary, IsActive = active, ReceivesEmailNotifications = flagged };
        f.Db.ClientContacts.Add(c);
        await f.Db.SaveChangesAsync();
        if (email is not null)
        {
            f.Db.ContactPoints.Add(new ContactPoint
            {
                TenantId = client.TenantId, OwnerEntityLookupId = ContactEntity, OwnerId = c.ClientContactId, ContactTypeLookupId = EmailType, Value = email, IsPrimary = true, IsActive = true,
            });
            await f.Db.SaveChangesAsync();
        }
        f.Db.ChangeTracker.Clear();
    }

    public static Task<List<NotificationOutbox>> OutboxAsync(WmsFixture f)
        => f.Db.NotificationOutbox.IgnoreQueryFilters().AsNoTracking().OrderBy(o => o.NotificationOutboxId).ToListAsync();
}

/// <summary>
/// Lote 248 — DocumentExpiryScanService y el trabajo DOCUMENT_EXPIRY_SCAN (InMemory): un aviso por documento y umbral que no se repite, el umbral 0 no se repite como
/// vencido, corregir el «hasta» vuelve a avisar, renovados y clientes dados de baja no avisan, aviso diario, tope de 500, módulo apagado, aislamiento por compañía,
/// violación única simulada y el aviso anotado aunque no haya regla.
/// </summary>
public sealed class DocumentExpiryScanServiceTests
{
    private const int T1 = WmsFixture.TenantId;
    private const int T2 = WmsFixture.OtherTenantId;
    private static readonly DateOnly Today = DocumentExpiryTestSeed.Today;

    private static async Task<(WmsFixture F, ExpiryTestClock Clock)> WorldAsync()
    {
        var clock = new ExpiryTestClock(DocumentExpiryTestSeed.NowUtc);
        return (await DocumentExpiryTestSeed.CreateAsync(clock), clock);
    }

    private static Task<DocumentExpiryScanResult> ScanAsync(WmsFixture f) => f.Get<DocumentExpiryScanService>().ScanAsync(default);

    [Fact]
    public async Task Publica_un_aviso_por_documento_y_umbral_y_la_segunda_corrida_no_repite()
    {
        var (f, _) = await WorldAsync();
        await using var _f = f;
        var a = await f.AddClientAsync("A");
        var b = await f.AddClientAsync("B");
        var d20 = await DocumentExpiryTestSeed.DocAsync(f, a, Today.AddDays(20), "E-20");
        var d3 = await DocumentExpiryTestSeed.DocAsync(f, b, Today.AddDays(3), "E-3");
        await DocumentExpiryTestSeed.ExemptionAsync(f, d3);
        await DocumentExpiryTestSeed.DocAsync(f, a, Today.AddDays(40), "E-40", DocumentExpiryTestSeed.OtherType);   // fuera de los 30 días: nada

        var r1 = await ScanAsync(f);
        Assert.Equal(new DocumentExpiryScanResult(2, 0, 0, 0), r1);
        var rows = await DocumentExpiryTestSeed.OutboxAsync(f);
        Assert.Equal(new[] { $"CD:{d3.ClientDocumentId}:{Today.AddDays(3):yyyyMMdd}:T5", $"CD:{d20.ClientDocumentId}:{Today.AddDays(20):yyyyMMdd}:T30" },
            rows.Select(r => r.DedupKey).ToArray());
        Assert.All(rows, r => Assert.Equal((ClientDocumentEventCodes.Expiring, T1), (r.EventCode, r.TenantId)));
        var v = NotificationRules.ParseValues(rows[0].PayloadJson);
        Assert.Equal(("Cliente B", "B", "Exención estatal", "E-3", "3", "5"), (v["client"], v["clientCode"], v["documentType"], v["documentNumber"], v["days"], v["threshold"]));
        Assert.Equal((d3.PublicId.ToString("N"), b.PublicId.ToString("N"), "2026-11-30"), (v["documentPublicId"], v["clientPublicId"], v["validToIso"]));
        Assert.Equal("Respalda una exención de IVU: al vencer, las ventas vuelven a llevar IVU.", v["taxNote"]);
        Assert.Equal(string.Empty, NotificationRules.ParseValues(rows[1].PayloadJson)["taxNote"]);

        var r2 = await ScanAsync(f);
        Assert.Equal(new DocumentExpiryScanResult(0, 0, 2, 0), r2);
        Assert.Equal(2, (await DocumentExpiryTestSeed.OutboxAsync(f)).Count);
        Assert.Empty(f.Db.ChangeTracker.Entries<NotificationOutbox>().Where(e => e.State == EntityState.Added));
    }

    [Fact]
    public async Task El_umbral_cero_no_se_repite_como_vencido_al_dia_siguiente()
    {
        var (f, clock) = await WorldAsync();
        await using var _f = f;
        await DocumentExpiryTestSeed.TenantAsync(f, T1, thresholds: "30,15,5,0");
        var a = await f.AddClientAsync("A");
        var doc = await DocumentExpiryTestSeed.DocAsync(f, a, Today);

        Assert.Equal(new DocumentExpiryScanResult(1, 0, 0, 0), await ScanAsync(f));
        var row = Assert.Single(await DocumentExpiryTestSeed.OutboxAsync(f));
        Assert.Equal((ClientDocumentEventCodes.Expiring, $"CD:{doc.ClientDocumentId}:{Today:yyyyMMdd}:T0"), (row.EventCode, row.DedupKey));

        clock.NextDay();
        Assert.Equal(new DocumentExpiryScanResult(0, 0, 1, 0), await ScanAsync(f));
        Assert.Single(await DocumentExpiryTestSeed.OutboxAsync(f));
    }

    [Fact]
    public async Task Si_el_trabajo_no_corrio_el_dia_cero_se_recupera_como_vencido_y_lo_historico_no_avisa()
    {
        var (f, _) = await WorldAsync();
        await using var _f = f;
        await DocumentExpiryTestSeed.TenantAsync(f, T1, thresholds: "0");
        var a = await f.AddClientAsync("A");
        var late = await DocumentExpiryTestSeed.DocAsync(f, a, Today.AddDays(-3));
        await DocumentExpiryTestSeed.DocAsync(f, await f.AddClientAsync("B"), Today.AddDays(-30));   // fuera de los 7 días de recuperación

        Assert.Equal(new DocumentExpiryScanResult(0, 1, 0, 0), await ScanAsync(f));
        var row = Assert.Single(await DocumentExpiryTestSeed.OutboxAsync(f));
        Assert.Equal((ClientDocumentEventCodes.Expired, $"CD:{late.ClientDocumentId}:{Today.AddDays(-3):yyyyMMdd}:T0"), (row.EventCode, row.DedupKey));
        Assert.Equal("3", NotificationRules.ParseValues(row.PayloadJson)["days"]);
    }

    [Fact]
    public async Task Corregir_el_hasta_vuelve_a_avisar_y_renovados_o_clientes_de_baja_no_avisan()
    {
        var (f, _) = await WorldAsync();
        await using var _f = f;
        var a = await f.AddClientAsync("A");
        var b = await f.AddClientAsync("B");
        var c = await f.AddClientAsync("C");
        var doc = await DocumentExpiryTestSeed.DocAsync(f, a, Today.AddDays(10));
        var renewedOld = await DocumentExpiryTestSeed.DocAsync(f, b, Today.AddDays(4));
        await DocumentExpiryTestSeed.DocAsync(f, b, Today.AddYears(1));               // lo renueva: el viejo deja de avisar
        await DocumentExpiryTestSeed.DocAsync(f, c, Today.AddDays(2));
        await DocumentExpiryTestSeed.UpdateClientAsync(f, c, x => x.IsActive = false);   // cliente dado de baja

        Assert.Equal(new DocumentExpiryScanResult(1, 0, 0, 0), await ScanAsync(f));
        Assert.Equal(doc.ClientDocumentId.ToString(), (await DocumentExpiryTestSeed.OutboxAsync(f)).Single().DedupKey!.Split(':')[1]);
        Assert.DoesNotContain(await DocumentExpiryTestSeed.OutboxAsync(f), r => r.DedupKey!.Contains($":{renewedOld.ClientDocumentId}:"));

        await DocumentExpiryTestSeed.UpdateDocAsync(f, doc, d => d.ValidTo = Today.AddDays(12));
        Assert.Equal(new DocumentExpiryScanResult(1, 0, 0, 0), await ScanAsync(f));
        Assert.Equal(new[] { $"CD:{doc.ClientDocumentId}:{Today.AddDays(10):yyyyMMdd}:T15", $"CD:{doc.ClientDocumentId}:{Today.AddDays(12):yyyyMMdd}:T15" },
            (await DocumentExpiryTestSeed.OutboxAsync(f)).Select(r => r.DedupKey).ToArray());
    }

    [Fact]
    public async Task Aviso_diario_despues_de_vencido_uno_por_dia()
    {
        var (f, clock) = await WorldAsync();
        await using var _f = f;
        await DocumentExpiryTestSeed.TenantAsync(f, T1, daily: true);
        var doc = await DocumentExpiryTestSeed.DocAsync(f, await f.AddClientAsync("A"), Today.AddDays(-40));   // muy viejo: con el diario no hay límite hacia atrás

        Assert.Equal(new DocumentExpiryScanResult(0, 1, 0, 0), await ScanAsync(f));
        Assert.Equal(new DocumentExpiryScanResult(0, 0, 1, 0), await ScanAsync(f));
        clock.NextDay();
        Assert.Equal(new DocumentExpiryScanResult(0, 1, 0, 0), await ScanAsync(f));
        var rows = await DocumentExpiryTestSeed.OutboxAsync(f);
        Assert.Equal(new[] { $"CD:{doc.ClientDocumentId}:{Today.AddDays(-40):yyyyMMdd}:D{Today:yyyyMMdd}", $"CD:{doc.ClientDocumentId}:{Today.AddDays(-40):yyyyMMdd}:D{Today.AddDays(1):yyyyMMdd}" },
            rows.Select(r => r.DedupKey).ToArray());
        Assert.All(rows, r => Assert.Equal(ClientDocumentEventCodes.Expired, r.EventCode));
        Assert.Equal(string.Empty, NotificationRules.ParseValues(rows[0].PayloadJson)["threshold"]);
    }

    [Fact]
    public async Task Tope_de_500_lo_demas_queda_para_la_siguiente_corrida()
    {
        var (f, _) = await WorldAsync();
        await using var _f = f;
        for (var i = 0; i < DocumentExpiryCodes.MaxNoticesPerRun + 2; i++)
            await DocumentExpiryTestSeed.DocAsync(f, await f.AddClientAsync("K" + i), Today.AddDays(3));

        var r1 = await ScanAsync(f);
        Assert.Equal(new DocumentExpiryScanResult(500, 0, 0, 2), r1);
        Assert.Equal("500 aviso(s) nuevo(s): 500 por vencer y 0 vencido(s); 0 ya se habían avisado. 2 quedaron para la siguiente corrida (tope de 500).",
            DocumentExpiryScanService.Summary(r1));
        Assert.Equal(new DocumentExpiryScanResult(2, 0, 500, 0), await ScanAsync(f));
        Assert.Equal(502, (await DocumentExpiryTestSeed.OutboxAsync(f)).Count);
    }

    [Fact]
    public async Task La_fuente_de_un_modulo_apagado_no_corre_y_el_trabajo_no_hace_nada()
    {
        var (f, _) = await WorldAsync();
        await using var _f = f;
        await DocumentExpiryTestSeed.DocAsync(f, await f.AddClientAsync("A"), Today.AddDays(3));
        f.SetModules(ModuleKeys.SalesOrders);

        Assert.Equal(new DocumentExpiryScanResult(0, 0, 0, 0), await ScanAsync(f));
        var job = new DocumentExpiryScanJob();
        Assert.Equal("El módulo de catálogo está apagado; no se hizo nada.", await job.RunAsync(new JobRunContext(T1, "TEST", f.Services, DocumentExpiryTestSeed.NowUtc), default));
        Assert.Empty(await DocumentExpiryTestSeed.OutboxAsync(f));

        f.SetModules(ModuleKeys.Catalog);
        Assert.Equal("1 aviso(s) nuevo(s): 1 por vencer y 0 vencido(s); 0 ya se habían avisado.",
            await job.RunAsync(new JobRunContext(T1, "TEST", f.Services, DocumentExpiryTestSeed.NowUtc), default));
    }

    [Fact]
    public async Task Cada_compania_publica_solo_lo_suyo()
    {
        var (f, _) = await WorldAsync();
        await using var _f = f;
        var mine = await DocumentExpiryTestSeed.DocAsync(f, await f.AddClientAsync("A"), Today.AddDays(3));
        var other = await DocumentExpiryTestSeed.DocAsync(f, await f.AddClientAsync("Z", T2), Today.AddDays(3));

        Assert.Equal(new DocumentExpiryScanResult(1, 0, 0, 0), await ScanAsync(f));
        var row = Assert.Single(await DocumentExpiryTestSeed.OutboxAsync(f));
        Assert.Equal((T1, mine.ClientDocumentId.ToString()), (row.TenantId, row.DedupKey!.Split(':')[1]));

        using (f.AsTenant(T2))
        {
            f.SetModules(ModuleKeys.Catalog);
            Assert.Equal(new DocumentExpiryScanResult(1, 0, 0, 0), await ScanAsync(f));
        }
        var t2 = (await DocumentExpiryTestSeed.OutboxAsync(f)).Single(r => r.TenantId == T2);
        Assert.Equal(other.ClientDocumentId.ToString(), t2.DedupKey!.Split(':')[1]);
    }

    /// <summary>Simula la carrera con otra corrida: el primer guardado choca con el índice único.</summary>
    private sealed class RacingScan(TeikemDbContext db, ITenantContext tenant, ITenantClock clock, INotificationPublisher publisher, ModuleService modules,
        IEnumerable<IExpiryNoticeSource> sources) : DocumentExpiryScanService(db, tenant, clock, publisher, modules, sources)
    {
        private const string Marker = "duplicado simulado";
        public int Fails { get; set; } = 1;

        protected override Task SaveAsync(CancellationToken ct)
        {
            if (Fails-- > 0) throw new DbUpdateException(Marker);
            return base.SaveAsync(ct);
        }

        protected override bool IsDuplicate(DbUpdateException ex) => ex.Message == Marker;
    }

    [Fact]
    public async Task Una_violacion_unica_simulada_no_tumba_la_corrida()
    {
        var (f, _) = await WorldAsync();
        await using var _f = f;
        await DocumentExpiryTestSeed.DocAsync(f, await f.AddClientAsync("A"), Today.AddDays(3));
        var second = await DocumentExpiryTestSeed.DocAsync(f, await f.AddClientAsync("B"), Today.AddDays(4));

        var scan = ActivatorUtilities.CreateInstance<RacingScan>(f.Services);
        Assert.Equal(new DocumentExpiryScanResult(1, 0, 1, 0), await scan.ScanAsync(default));
        var row = Assert.Single(await DocumentExpiryTestSeed.OutboxAsync(f));
        Assert.Equal(second.ClientDocumentId.ToString(), row.DedupKey!.Split(':')[1]);
        Assert.Empty(f.Db.ChangeTracker.Entries<NotificationOutbox>().Where(e => e.State == EntityState.Added));
    }

    [Fact]
    public async Task Sin_regla_el_aviso_igual_se_anota_y_crear_la_regla_despues_no_dispara_lo_ya_anotado()
    {
        var (f, _) = await WorldAsync();
        await using var _f = f;
        await DocumentExpiryTestSeed.DocAsync(f, await f.AddClientAsync("A"), Today.AddDays(3));
        Assert.Empty(await f.Db.Set<NotificationRule>().IgnoreQueryFilters().AsNoTracking().ToListAsync());

        Assert.Equal(new DocumentExpiryScanResult(1, 0, 0, 0), await ScanAsync(f));
        Assert.Single(await DocumentExpiryTestSeed.OutboxAsync(f));

        f.Db.Set<NotificationRule>().Add(new NotificationRule
        {
            PublicId = Guid.NewGuid(), TenantId = T1, EventCode = ClientDocumentEventCodes.Expiring, Name = "Documentos por vencer", IsActive = true, CreatedAtUtc = DateTime.UtcNow,
        });
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        Assert.Equal(new DocumentExpiryScanResult(0, 0, 1, 0), await ScanAsync(f));
        Assert.Single(await DocumentExpiryTestSeed.OutboxAsync(f));
    }

    [Fact]
    public void El_trabajo_clave_horario_y_resumenes()
    {
        var job = new DocumentExpiryScanJob();
        Assert.Equal(("DOCUMENT_EXPIRY_SCAN", JobScheduleKinds.Daily, (TimeSpan?)new TimeSpan(6, 0, 0), true, TimeSpan.FromMinutes(10), (int?)null),
            (job.Key, job.DefaultKind, job.DefaultDailyAt, job.DefaultEnabled, job.MaxRuntime, job.DefaultIntervalSeconds));
        Assert.Equal("Nada que avisar.", DocumentExpiryScanService.Summary(new DocumentExpiryScanResult(0, 0, 0, 0)));
        Assert.Equal("3 aviso(s) nuevo(s): 2 por vencer y 1 vencido(s); 4 ya se habían avisado.", DocumentExpiryScanService.Summary(new DocumentExpiryScanResult(2, 1, 4, 0)));
        Assert.Equal("0 aviso(s) nuevo(s): 0 por vencer y 0 vencido(s); 5 ya se habían avisado.", DocumentExpiryScanService.Summary(new DocumentExpiryScanResult(0, 0, 5, 0)));
    }
}
