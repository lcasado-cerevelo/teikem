using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Teikem.Domain.Catalogs;
using Teikem.Domain.Clients;
using Teikem.Domain.Constants;
using Teikem.Domain.Files;
using Teikem.Domain.Notifications;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Notifications;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 248 — el escaneo de vencimientos contra SQL Server REAL (se omite sin TEIKEM_TEST_SQLSERVER), sobre la base desechable de <see cref="NotificationSqlFixture"/>
/// (creada desde el modelo EF, sin FK): dos corridas en paralelo de la misma compañía dejan UNA fila de NotificationOutbox por clave y ninguna falla; y la violación real
/// del índice único UX_NotificationOutbox_Dedup (2601) se suelta y cuenta como ya avisado.
/// </summary>
public sealed class DocumentExpirySqlTests(NotificationSqlFixture fx) : IClassFixture<NotificationSqlFixture>
{
    private static DateOnly Today => TenantClock.Default.Today;

    private static TenantContext Ctx(int tenantId) => new() { TenantId = tenantId };

    /// <summary>Una compañía con <paramref name="count"/> clientes, cada uno con un documento que vence en 3 días (umbral 5).</summary>
    private async Task<int> WorldAsync(string name, int count)
    {
        var tenantId = await fx.NewTenantAsync(name);
        await using var db = fx.NewDb(Ctx(tenantId));
        var type = new LookupCode
        {
            Entity = ClientDocumentCodes.TypeDomain, InternalCode = "IVU_X" + tenantId, TenantId = tenantId, LabelJson = "{\"es\":\"Estatal\",\"en\":\"State\"}",
            ExtraJson = "{\"requiresValidTo\":true,\"isTax\":true}", IsActive = true,
        };
        db.LookupCodes.Add(type);
        await db.SaveChangesAsync();
        for (var i = 0; i < count; i++)
        {
            var client = new Client { PublicId = Guid.NewGuid(), TenantId = tenantId, Code = "C" + Guid.NewGuid().ToString("N")[..8], Name = $"Farmacia {i}", StatusCodeId = 1, IsActive = true, CreatedAtUtc = DateTime.UtcNow };
            var file = new StoredFile
            {
                PublicId = Guid.NewGuid(), TenantId = tenantId, OwnerEntityLookupId = 1, OwnerId = 0, FileName = "c.pdf", ContentType = "application/pdf", SizeBytes = 10,
                Sha256 = "x", StorageKey = "k" + Guid.NewGuid().ToString("N"), IsActive = true,
            };
            db.Clients.Add(client);
            db.Set<StoredFile>().Add(file);
            await db.SaveChangesAsync();
            db.Set<ClientDocument>().Add(new ClientDocument
            {
                PublicId = Guid.NewGuid(), TenantId = tenantId, ClientId = client.ClientId, StoredFileId = file.StoredFileId, DocumentTypeLookupId = type.LookupCodeId,
                DocumentNumber = "E-" + i, ValidFrom = Today.AddYears(-1), ValidTo = Today.AddDays(3), IsActive = true, CreatedAtUtc = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }
        return tenantId;
    }

    /// <summary>Choca de verdad con otra corrida: antes del primer guardado, otra conexión anota la misma clave (el guardado recibe el 2601 real de SQL Server).</summary>
    private sealed class RacedScan(NotificationSqlFixture fx, TeikemDbContext db, ITenantContext tenant, ITenantClock clock, INotificationPublisher publisher, ModuleService modules,
        IEnumerable<IExpiryNoticeSource> sources) : DocumentExpiryScanService(db, tenant, clock, publisher, modules, sources)
    {
        private bool _raced;

        protected override async Task SaveAsync(CancellationToken ct)
        {
            if (!_raced)
            {
                _raced = true;
                var pending = db.ChangeTracker.Entries<NotificationOutbox>().Where(e => e.State == EntityState.Added).Select(e => e.Entity).Single();
                await using var other = fx.NewDb(new TenantContext { TenantId = pending.TenantId });
                other.NotificationOutbox.Add(new NotificationOutbox
                {
                    TenantId = pending.TenantId, EventCode = pending.EventCode, PayloadJson = pending.PayloadJson, DedupKey = pending.DedupKey,
                    NextAttemptUtc = DateTime.UtcNow, CreatedAtUtc = DateTime.UtcNow,
                });
                await other.SaveChangesAsync(ct);
            }
            await base.SaveAsync(ct);
        }
    }

    private static (ITenantContext Ctx, INotificationPublisher Publisher, ModuleService Modules, IExpiryNoticeSource[] Sources) Parts(TeikemDbContext db, TenantContext ctx)
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        cache.Set($"modules:{ctx.TenantId}", new HashSet<string>(new[] { ModuleKeys.Catalog }, StringComparer.OrdinalIgnoreCase));
        var catalog = new NotificationEventCatalog(new INotificationEventProvider[] { new ClientDocumentEventProvider() });
        return (ctx, new NotificationPublisher(db, ctx, catalog), new ModuleService(db, ctx, cache),
            new IExpiryNoticeSource[] { new ClientDocumentExpirySource(db, ctx, new TripTestLookups()) });
    }

    private async Task<DocumentExpiryScanResult> ScanAsync(int tenantId)
    {
        var ctx = Ctx(tenantId);
        await using var db = fx.NewDb(ctx);
        var p = Parts(db, ctx);
        return await new DocumentExpiryScanService(db, p.Ctx, TenantClock.Default, p.Publisher, p.Modules, p.Sources).ScanAsync(default);
    }

    private async Task<List<NotificationOutbox>> OutboxAsync(int tenantId)
    {
        await using var db = fx.NewDb(Ctx(tenantId));
        return await db.NotificationOutbox.AsNoTracking().Where(o => o.TenantId == tenantId).ToListAsync();
    }

    [SqlServerFact]
    public async Task Dos_corridas_en_paralelo_dejan_una_fila_por_clave_y_ninguna_falla()
    {
        const int docs = 40;
        var tenantId = await WorldAsync("Vencimientos en paralelo", docs);

        var results = await Task.WhenAll(ScanAsync(tenantId), ScanAsync(tenantId), ScanAsync(tenantId));

        var rows = await OutboxAsync(tenantId);
        Assert.Equal(docs, rows.Count);
        Assert.Equal(docs, rows.Select(r => r.DedupKey).Distinct().Count());
        Assert.All(rows, r => Assert.Equal(ClientDocumentEventCodes.Expiring, r.EventCode));
        Assert.Equal(docs, results.Sum(r => r.New));                         // cada clave salió una sola vez entre las tres
        Assert.All(results, r => Assert.Equal(docs, r.New + r.AlreadySent));  // lo demás contó como ya avisado

        // una corrida más no agrega nada
        Assert.Equal(new DocumentExpiryScanResult(0, 0, docs, 0), await ScanAsync(tenantId));
    }

    [SqlServerFact]
    public async Task La_violacion_real_del_indice_unico_se_suelta_y_cuenta_como_ya_avisado()
    {
        var tenantId = await WorldAsync("Vencimientos con carrera", 3);
        var ctx = Ctx(tenantId);
        DocumentExpiryScanResult result;
        await using (var db = fx.NewDb(ctx))
        {
            var p = Parts(db, ctx);
            var scan = new RacedScan(fx, db, p.Ctx, TenantClock.Default, p.Publisher, p.Modules, p.Sources);
            result = await scan.ScanAsync(default);
            Assert.Empty(db.ChangeTracker.Entries<NotificationOutbox>().Where(e => e.State == EntityState.Added));
        }
        Assert.Equal(new DocumentExpiryScanResult(2, 0, 1, 0), result);
        var rows = await OutboxAsync(tenantId);
        Assert.Equal(3, rows.Count);
        Assert.Equal(3, rows.Select(r => r.DedupKey).Distinct().Count());
    }
}
