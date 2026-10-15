using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Teikem.Domain.Constants;
using Teikem.Domain.Jobs;
using Teikem.Domain.Tenancy;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Jobs;
using Teikem.Infrastructure.Persistence;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 50 (P2.2) — el ejecutor de trabajos contra SQL Server REAL (el reclamo es un UPDATE atómico; InMemory no lo soporta). Base
/// DESECHABLE propia (Teikem_jobstest_&lt;guid&gt;, se borra al terminar), creada desde el modelo EF. Se omite sin TEIKEM_TEST_SQLSERVER.
/// </summary>
public sealed class ScheduledJobRunnerFixture : IAsyncLifetime
{
    public string? ConnectionString { get; private set; }
    public int TenantA { get; private set; }
    public int TenantB { get; private set; }
    private string? _database;

    public async Task InitializeAsync()
    {
        var server = Environment.GetEnvironmentVariable(TenantIsolationSqlServerFixture.EnvVar);
        if (string.IsNullOrWhiteSpace(server)) return;
        _database = "Teikem_jobstest_" + Guid.NewGuid().ToString("N")[..12];
        ConnectionString = new SqlConnectionStringBuilder(server) { InitialCatalog = _database }.ConnectionString;
        try
        {
            await using var db = NewDb(new TenantContext());
            await db.Database.EnsureCreatedAsync();
            var a = new Tenant { Name = "A" };
            var b = new Tenant { Name = "B" };
            db.Tenants.AddRange(a, b);
            await db.SaveChangesAsync();
            (TenantA, TenantB) = (a.TenantId, b.TenantId);
        }
        catch
        {
            await DisposeAsync();
            throw;
        }
    }

    public TeikemDbContext NewDb(ITenantContext tenant) => new(new DbContextOptionsBuilder<TeikemDbContext>()
        .UseSqlServer(ConnectionString)
        .ConfigureWarnings(w => w.Ignore(CoreEventId.PossibleIncorrectRequiredNavigationWithQueryFilterInteractionWarning)).Options, tenant);

    /// <summary>Un «API» completo: su propio proveedor de DI, su propio ejecutor (otro InstanceId) y los trabajos indicados.</summary>
    public ScheduledJobRunner NewRunner(params IScheduledJob[] jobs)
    {
        var services = new ServiceCollection();
        services.AddScoped<TenantContext>();
        services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());
        services.AddScoped(sp => NewDb(sp.GetRequiredService<ITenantContext>()));
        var provider = services.BuildServiceProvider();
        return new ScheduledJobRunner(provider.GetRequiredService<IServiceScopeFactory>(), jobs, NullLogger<ScheduledJobRunner>.Instance);
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

public sealed class ScheduledJobRunnerSqlTests(ScheduledJobRunnerFixture fx) : IClassFixture<ScheduledJobRunnerFixture>
{
    private sealed class FakeJob(string key, Func<JobRunContext, Task<string?>> run, bool enabled = true, TimeSpan? maxRuntime = null) : IScheduledJob
    {
        public int Runs;
        public string Key => key;
        public string NameEs => key; public string NameEn => key; public string DescriptionEs => key; public string DescriptionEn => key;
        public string DefaultKind => JobScheduleKinds.Interval;
        public int? DefaultIntervalSeconds => 3600;
        public TimeSpan? DefaultDailyAt => null;
        public bool DefaultEnabled => enabled;
        public TimeSpan MaxRuntime => maxRuntime ?? TimeSpan.FromMinutes(1);
        public async Task<string?> RunAsync(JobRunContext context, CancellationToken ct)
        {
            Interlocked.Increment(ref Runs);
            return await run(context);
        }
    }

    private static string NewKey() => "T_" + Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();

    private async Task<ScheduledJob> RowAsync(int tenantId, string key)
    {
        var t = new TenantContext { TenantId = tenantId };
        await using var db = fx.NewDb(t);
        return await db.ScheduledJobs.AsNoTracking().SingleAsync(j => j.JobKey == key);
    }

    private async Task SetAsync(int tenantId, string key, Action<ScheduledJob> change)
    {
        var t = new TenantContext { TenantId = tenantId };
        await using var db = fx.NewDb(t);
        var row = await db.ScheduledJobs.SingleAsync(j => j.JobKey == key);
        change(row);
        await db.SaveChangesAsync();
    }

    [SqlServerFact]
    public async Task EnsureRows_creates_the_rows_per_company_with_the_default_schedule_and_never_overwrites_the_admins_changes()
    {
        var job = new FakeJob(NewKey(), _ => Task.FromResult<string?>("ok"));
        var runner = fx.NewRunner(job);
        var now = DateTime.UtcNow;
        Assert.True(await runner.EnsureTenantRowsAsync(fx.TenantA, now, default) >= 1);
        var row = await RowAsync(fx.TenantA, job.Key);
        Assert.True(row.IsEnabled);
        Assert.Equal(("INTERVAL", 3600), (row.ScheduleKind, row.IntervalSeconds));
        Assert.Equal(now.AddSeconds(3600), row.NextRunUtc!.Value, TimeSpan.FromSeconds(1));
        Assert.Equal("NEVER", row.LastStatus);

        await SetAsync(fx.TenantA, job.Key, r => { r.IsEnabled = false; r.IntervalSeconds = 120; r.NextRunUtc = null; });
        Assert.Equal(0, await runner.EnsureTenantRowsAsync(fx.TenantA, now, default));   // ya existe: no la toca
        var again = await RowAsync(fx.TenantA, job.Key);
        Assert.False(again.IsEnabled);
        Assert.Equal(120, again.IntervalSeconds);

        // la otra compañía no recibe lo de A: cada una tiene su fila
        await runner.EnsureTenantRowsAsync(fx.TenantB, now, default);
        Assert.True((await RowAsync(fx.TenantB, job.Key)).IsEnabled);
    }

    [SqlServerFact]
    public async Task Two_api_copies_racing_for_the_same_job_run_it_exactly_once()
    {
        var gate = new TaskCompletionSource();
        var job = new FakeJob(NewKey(), async _ => { await gate.Task; return "listo"; });
        var runnerA = fx.NewRunner(job);
        var runnerB = fx.NewRunner(job);
        var now = DateTime.UtcNow;
        await runnerA.EnsureTenantRowsAsync(fx.TenantA, now, default);
        await SetAsync(fx.TenantA, job.Key, r => r.NextRunUtc = now.AddMinutes(-1));
        var id = (await RowAsync(fx.TenantA, job.Key)).ScheduledJobId;

        var attempts = Enumerable.Range(0, 8)
            .Select(i => Task.Run(() => (i % 2 == 0 ? runnerA : runnerB).TryRunAsync(fx.TenantA, id, job.Key, now, default))).ToList();
        await Task.Delay(500);          // los perdedores ya volvieron; el ganador sigue dentro del trabajo
        gate.SetResult();
        var results = await Task.WhenAll(attempts);

        Assert.Single(results, r => r);
        Assert.Equal(1, job.Runs);
        var row = await RowAsync(fx.TenantA, job.Key);
        Assert.Equal(("OK", "listo", 0), (row.LastStatus, row.LastMessage, row.ConsecutiveFailures));
        Assert.Null(row.LockedBy);
        Assert.True(row.NextRunUtc > DateTime.UtcNow.AddMinutes(50));   // la siguiente: dentro de una hora
        var t = new TenantContext { TenantId = fx.TenantA };
        await using var db = fx.NewDb(t);
        var run = await db.ScheduledJobRuns.AsNoTracking().SingleAsync(r => r.JobKey == job.Key);
        Assert.Equal(("OK", "SCHEDULE"), (run.Status, run.TriggeredBy));
        Assert.NotNull(run.FinishedUtc);
    }

    [SqlServerFact]
    public async Task A_failure_does_not_stop_the_cycle_is_recorded_and_retried_soon_and_a_success_clears_it()
    {
        var fail = true;
        var bad = new FakeJob(NewKey(), _ => fail ? throw new InvalidOperationException("se cayó") : Task.FromResult<string?>("ya sirve"));
        var good = new FakeJob(NewKey(), _ => Task.FromResult<string?>("ok"));
        var runner = fx.NewRunner(bad, good);
        var now = DateTime.UtcNow;
        await runner.EnsureTenantRowsAsync(fx.TenantA, now, default);
        foreach (var k in new[] { bad.Key, good.Key }) await SetAsync(fx.TenantA, k, r => r.NextRunUtc = now.AddMinutes(-1));

        await runner.RunDueAsync(now, default);   // el que falla no impide al otro
        var b1 = await RowAsync(fx.TenantA, bad.Key);
        Assert.Equal(("FAILED", "se cayó", 1), (b1.LastStatus, b1.LastError, b1.ConsecutiveFailures));
        Assert.InRange(b1.NextRunUtc!.Value, DateTime.UtcNow.AddSeconds(30), DateTime.UtcNow.AddMinutes(2));   // reintento a ~1 min
        Assert.Equal("OK", (await RowAsync(fx.TenantA, good.Key)).LastStatus);

        fail = false;
        await SetAsync(fx.TenantA, bad.Key, r => r.NextRunUtc = DateTime.UtcNow.AddMinutes(-1));
        await runner.RunDueAsync(DateTime.UtcNow, default);
        var b2 = await RowAsync(fx.TenantA, bad.Key);
        Assert.Equal(("OK", 0, "ya sirve"), (b2.LastStatus, b2.ConsecutiveFailures, b2.LastMessage));
        Assert.Null(b2.LastError ?? null);
    }

    [SqlServerFact]
    public async Task A_lease_that_expired_can_be_taken_by_another_copy_but_a_live_one_cannot()
    {
        var job = new FakeJob(NewKey(), _ => Task.FromResult<string?>("tomado"));
        var runner = fx.NewRunner(job);
        var now = DateTime.UtcNow;
        await runner.EnsureTenantRowsAsync(fx.TenantA, now, default);
        var id = (await RowAsync(fx.TenantA, job.Key)).ScheduledJobId;

        await SetAsync(fx.TenantA, job.Key, r => { r.NextRunUtc = now.AddMinutes(-1); r.LockedBy = "otra-copia"; r.LockedUntilUtc = now.AddMinutes(5); });
        Assert.False(await runner.TryRunAsync(fx.TenantA, id, job.Key, now, default));   // la otra copia sigue viva
        Assert.Equal(0, job.Runs);

        await SetAsync(fx.TenantA, job.Key, r => r.LockedUntilUtc = now.AddMinutes(-1));  // se cayó y su reclamo venció
        Assert.True(await runner.TryRunAsync(fx.TenantA, id, job.Key, now, default));
        Assert.Equal(1, job.Runs);
        Assert.Null((await RowAsync(fx.TenantA, job.Key)).LockedBy);
    }

    [SqlServerFact]
    public async Task A_disabled_job_does_not_run_by_schedule_but_runs_once_when_asked_and_stays_without_next_run()
    {
        var job = new FakeJob(NewKey(), _ => Task.FromResult<string?>("a pedido"), enabled: false);
        var runner = fx.NewRunner(job);
        var now = DateTime.UtcNow;
        await runner.EnsureTenantRowsAsync(fx.TenantA, now, default);
        var id = (await RowAsync(fx.TenantA, job.Key)).ScheduledJobId;
        Assert.Null((await RowAsync(fx.TenantA, job.Key)).NextRunUtc);

        Assert.False(await runner.TryRunAsync(fx.TenantA, id, job.Key, now, default));   // apagado y sin pedido
        await SetAsync(fx.TenantA, job.Key, r => r.ManualRequested = true);               // «Ejecutar ahora»
        Assert.True(await runner.RunDueAsync(now, default) >= 1);
        Assert.Equal(1, job.Runs);

        var row = await RowAsync(fx.TenantA, job.Key);
        Assert.False(row.ManualRequested);
        Assert.Null(row.NextRunUtc);
        var t = new TenantContext { TenantId = fx.TenantA };
        await using var db = fx.NewDb(t);
        Assert.Equal("MANUAL", (await db.ScheduledJobRuns.AsNoTracking().SingleAsync(r => r.JobKey == job.Key)).TriggeredBy);
    }

    [SqlServerFact]
    public async Task The_job_runs_as_its_own_company_without_a_user_and_never_sees_another_companys_rows()
    {
        int? seenTenant = null; int? seenUser = -1; int rowsSeen = -1;
        var key = NewKey();
        var job = new FakeJob(key, async ctx =>
        {
            var tc = ctx.Services.GetRequiredService<TenantContext>();
            (seenTenant, seenUser) = (tc.TenantId, tc.UserId);
            rowsSeen = await ctx.Services.GetRequiredService<TeikemDbContext>().ScheduledJobs.CountAsync(j => j.JobKey == key);
            return null;
        });
        var runner = fx.NewRunner(job);
        var now = DateTime.UtcNow;
        await runner.EnsureTenantRowsAsync(fx.TenantA, now, default);
        await runner.EnsureTenantRowsAsync(fx.TenantB, now, default);
        await SetAsync(fx.TenantB, job.Key, r => r.NextRunUtc = now.AddMinutes(1));   // B no le toca todavía
        await SetAsync(fx.TenantA, job.Key, r => r.NextRunUtc = now.AddMinutes(-1));
        await runner.RunDueAsync(now, default);

        Assert.Equal((fx.TenantA, (int?)null, 1), (seenTenant, seenUser, rowsSeen));   // solo ve su propia fila
        Assert.Equal("NEVER", (await RowAsync(fx.TenantB, job.Key)).LastStatus);          // la de B no corrió
    }

    [SqlServerFact]
    public async Task The_purge_job_keeps_the_last_100_runs_of_each_job_of_the_company_only()
    {
        var runner = fx.NewRunner(new JobRunPurgeJob());
        var tA = new TenantContext { TenantId = fx.TenantA };
        var tB = new TenantContext { TenantId = fx.TenantB };
        await using (var db = fx.NewDb(tA))
        {
            for (var i = 0; i < 105; i++) db.ScheduledJobRuns.Add(new ScheduledJobRun { TenantId = fx.TenantA, JobKey = "PURGA_X", StartedUtc = DateTime.UtcNow, Status = "OK", InstanceId = "t" });
            for (var i = 0; i < 3; i++) db.ScheduledJobRuns.Add(new ScheduledJobRun { TenantId = fx.TenantA, JobKey = "PURGA_Y", StartedUtc = DateTime.UtcNow, Status = "OK", InstanceId = "t" });
            await db.SaveChangesAsync();
        }
        await using (var db = fx.NewDb(tB))
        {
            for (var i = 0; i < 105; i++) db.ScheduledJobRuns.Add(new ScheduledJobRun { TenantId = fx.TenantB, JobKey = "PURGA_X", StartedUtc = DateTime.UtcNow, Status = "OK", InstanceId = "t" });
            await db.SaveChangesAsync();
        }

        var message = await RunPurgeAsync(runner, fx.TenantA);
        Assert.Equal("5 corridas viejas borradas.", message);
        await using var check = fx.NewDb(new TenantContext { TenantId = fx.TenantA, TenantFilterDisabled = true });
        Assert.Equal(100, await check.ScheduledJobRuns.CountAsync(r => r.TenantId == fx.TenantA && r.JobKey == "PURGA_X"));
        Assert.Equal(3, await check.ScheduledJobRuns.CountAsync(r => r.TenantId == fx.TenantA && r.JobKey == "PURGA_Y"));
        Assert.Equal(105, await check.ScheduledJobRuns.CountAsync(r => r.TenantId == fx.TenantB && r.JobKey == "PURGA_X"));   // la otra compañía no se toca
        Assert.Equal("Nada que limpiar.", await RunPurgeAsync(runner, fx.TenantA));
    }

    private async Task<string?> RunPurgeAsync(ScheduledJobRunner runner, int tenantId)
    {
        var tc = new TenantContext { TenantId = tenantId };
        await using var db = fx.NewDb(tc);
        var services = new ServiceCollection().AddSingleton(db).BuildServiceProvider();
        return await new JobRunPurgeJob().RunAsync(new JobRunContext(tenantId, "MANUAL", services, DateTime.UtcNow), default);
    }
}
