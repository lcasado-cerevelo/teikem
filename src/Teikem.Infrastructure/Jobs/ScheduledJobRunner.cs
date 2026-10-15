using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Teikem.Domain.Constants;
using Teikem.Domain.Jobs;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Jobs;

public sealed class JobsOptions
{
    public const string Section = "Jobs";
    public bool Enabled { get; set; } = true;
    public int PollSeconds { get; set; } = 30;
}

/// <summary>
/// Lote 50 (P2.2) — ejecutor de trabajos programados. NO duplica: cada trabajo de cada compañía se «reclama» con un UPDATE atómico
/// (ExecuteUpdate con la condición completa en el WHERE); si afecta una fila, esta copia del API es la dueña de la corrida, si no,
/// otra se le adelantó. El reclamo vale hasta LockedUntilUtc (= ahora + MaxRuntime del trabajo): si la copia se cae a medias, vence
/// y otra copia puede tomarlo. Cada corrida va en su propio alcance de DI, como la compañía SIN usuario («Sistema»).
/// Una falla no detiene nada: queda en la fila y en el historial, y se reintenta pronto (JobSchedule.NextAfterFailure).
/// Los métodos son públicos para las pruebas (xunit los llama sin esperar al ciclo del worker).
/// </summary>
public sealed class ScheduledJobRunner(IServiceScopeFactory scopes, IEnumerable<IScheduledJob> jobs, ILogger<ScheduledJobRunner> logger)
{
    public const int MaxMessageLength = 400;
    public const int MaxErrorLength = 1000;

    private readonly Dictionary<string, IScheduledJob> _jobs = jobs.ToDictionary(j => j.Key, StringComparer.Ordinal);

    /// <summary>Identificador de esta copia del API en LockedBy y en el historial.</summary>
    public string InstanceId { get; } = $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid().ToString("N")[..6]}";

    public IReadOnlyCollection<IScheduledJob> Registry => _jobs.Values;
    public IScheduledJob? Find(string key) => _jobs.GetValueOrDefault(key);

    /// <summary>Crea, para las compañías activas, la fila de cada trabajo que falte (con su horario por omisión). Nunca pisa una existente.</summary>
    public async Task<int> EnsureRowsAsync(DateTime nowUtc, CancellationToken ct)
    {
        var created = 0;
        List<int> tenants;
        await using (var scope = scopes.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TeikemDbContext>();
            tenants = await db.Tenants.AsNoTracking().Where(t => t.IsActive).Select(t => t.TenantId).ToListAsync(ct);
        }
        foreach (var tenantId in tenants)
            created += await EnsureTenantRowsAsync(tenantId, nowUtc, ct);
        return created;
    }

    /// <summary>Lo mismo para una sola compañía (también lo usa la pantalla al listar).</summary>
    public async Task<int> EnsureTenantRowsAsync(int tenantId, DateTime nowUtc, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TenantContext>();
        using var asTenant = context.AsAnonymous(tenantId);
        var db = scope.ServiceProvider.GetRequiredService<TeikemDbContext>();
        var have = await db.ScheduledJobs.AsNoTracking().Select(j => j.JobKey).ToListAsync(ct);
        var created = 0;
        foreach (var job in _jobs.Values.Where(j => !have.Contains(j.Key)))
        {
            db.ScheduledJobs.Add(new ScheduledJob
            {
                TenantId = tenantId,
                JobKey = job.Key,
                IsEnabled = job.DefaultEnabled,
                ScheduleKind = job.DefaultKind,
                IntervalSeconds = job.DefaultIntervalSeconds,
                DailyAt = job.DefaultDailyAt,
                NextRunUtc = job.DefaultEnabled ? JobSchedule.Next(job.DefaultKind, job.DefaultIntervalSeconds, job.DefaultDailyAt, nowUtc) : null,
            });
            created++;
        }
        if (created == 0) return 0;
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            // Otra copia del API creó la misma fila a la vez (UX_ScheduledJob_Key): no es un error.
            logger.LogDebug("Trabajos de la compañía {TenantId} creados por otra copia del API.", tenantId);
            return 0;
        }
        return created;
    }

    /// <summary>Corre los trabajos que tocan (de todas las compañías). Devuelve cuántos corrió esta copia.</summary>
    public async Task<int> RunDueAsync(DateTime nowUtc, CancellationToken ct)
    {
        List<(int TenantId, int Id, string Key)> due;
        await using (var scope = scopes.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TeikemDbContext>();
            var rows = await db.ScheduledJobs.AsNoTracking().IgnoreQueryFilters()
                .Where(j => ((j.IsEnabled && j.NextRunUtc <= nowUtc) || j.ManualRequested) && (j.LockedUntilUtc == null || j.LockedUntilUtc < nowUtc))
                .OrderBy(j => j.NextRunUtc).Select(j => new { j.TenantId, j.ScheduledJobId, j.JobKey }).ToListAsync(ct);
            due = rows.Select(r => (r.TenantId, r.ScheduledJobId, r.JobKey)).ToList();
        }

        var ran = 0;
        foreach (var (tenantId, id, key) in due)
        {
            ct.ThrowIfCancellationRequested();
            if (await TryRunAsync(tenantId, id, key, nowUtc, ct)) ran++;
        }
        return ran;
    }

    /// <summary>Reclama y corre un trabajo; false si otra copia lo reclamó primero (o ya no toca).</summary>
    public async Task<bool> TryRunAsync(int tenantId, int scheduledJobId, string key, DateTime nowUtc, CancellationToken ct)
    {
        var job = Find(key);
        await using var scope = scopes.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TenantContext>();
        using var asTenant = context.AsAnonymous(tenantId);
        var db = scope.ServiceProvider.GetRequiredService<TeikemDbContext>();

        var manual = await db.ScheduledJobs.AsNoTracking().Where(j => j.ScheduledJobId == scheduledJobId).Select(j => j.ManualRequested).FirstOrDefaultAsync(ct);
        var maxRuntime = job?.MaxRuntime ?? TimeSpan.FromMinutes(5);
        var lockedUntil = nowUtc + maxRuntime;

        // Reclamo atómico: toda la condición va en el WHERE; solo una copia afecta la fila.
        var claimed = await db.ScheduledJobs.IgnoreQueryFilters()
            .Where(j => j.ScheduledJobId == scheduledJobId && j.TenantId == tenantId
                && ((j.IsEnabled && j.NextRunUtc <= nowUtc) || j.ManualRequested)
                && (j.LockedUntilUtc == null || j.LockedUntilUtc < nowUtc))
            .ExecuteUpdateAsync(s => s
                .SetProperty(j => j.LockedBy, InstanceId)
                .SetProperty(j => j.LockedUntilUtc, lockedUntil)
                .SetProperty(j => j.ManualRequested, false)
                .SetProperty(j => j.LastStatus, JobStatuses.Running)
                .SetProperty(j => j.LastStartedUtc, nowUtc), ct);
        if (claimed == 0) return false;

        var trigger = manual ? JobTriggers.Manual : JobTriggers.Schedule;
        var run = new ScheduledJobRun { TenantId = tenantId, JobKey = key, StartedUtc = nowUtc, Status = JobStatuses.Running, TriggeredBy = trigger, InstanceId = InstanceId };
        db.ScheduledJobRuns.Add(run);
        await db.SaveChangesAsync(ct);

        string status;
        string? message = null, error = null;
        try
        {
            if (job is null) throw new InvalidOperationException($"El trabajo '{key}' ya no existe en esta versión del sistema.");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(maxRuntime);
            message = await job.RunAsync(new JobRunContext(tenantId, trigger, scope.ServiceProvider, nowUtc), timeout.Token);
            status = JobStatuses.Ok;
        }
        catch (Exception ex)
        {
            status = JobStatuses.Failed;
            error = ex is OperationCanceledException ? "El trabajo superó su tiempo máximo o se interrumpió." : ex.Message;
            logger.LogError(ex, "Trabajo programado {JobKey} (compañía {TenantId}) falló.", key, tenantId);
        }

        await FinishAsync(db, tenantId, scheduledJobId, run.ScheduledJobRunId, status, Trim(message, MaxMessageLength), Trim(error, MaxErrorLength));
        return true;
    }

    private async Task FinishAsync(TeikemDbContext db, int tenantId, int scheduledJobId, long runId, string status, string? message, string? error)
    {
        var finished = DateTime.UtcNow;   // el fin se anota aunque la aplicación se esté apagando (no se usa el token del ciclo)
        var row = await db.ScheduledJobs.AsNoTracking().IgnoreQueryFilters().Where(j => j.ScheduledJobId == scheduledJobId && j.LockedBy == InstanceId).FirstOrDefaultAsync();
        if (row is not null)
        {
            var failures = status == JobStatuses.Ok ? 0 : row.ConsecutiveFailures + 1;
            DateTime? next = null;
            if (row.IsEnabled)
                next = status == JobStatuses.Ok
                    ? JobSchedule.Next(row.ScheduleKind, row.IntervalSeconds, row.DailyAt, finished)
                    : JobSchedule.NextAfterFailure(row.ScheduleKind, row.IntervalSeconds, row.DailyAt, failures, finished);
            await db.ScheduledJobs.IgnoreQueryFilters().Where(j => j.ScheduledJobId == scheduledJobId && j.LockedBy == InstanceId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(j => j.LastFinishedUtc, finished)
                    .SetProperty(j => j.LastStatus, status)
                    .SetProperty(j => j.LastMessage, message)
                    .SetProperty(j => j.LastError, error)
                    .SetProperty(j => j.ConsecutiveFailures, failures)
                    .SetProperty(j => j.NextRunUtc, next)
                    .SetProperty(j => j.LockedBy, (string?)null)
                    .SetProperty(j => j.LockedUntilUtc, (DateTime?)null));
        }
        await db.ScheduledJobRuns.IgnoreQueryFilters().Where(r => r.ScheduledJobRunId == runId && r.TenantId == tenantId)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.FinishedUtc, finished).SetProperty(r => r.Status, status)
                .SetProperty(r => r.Message, message).SetProperty(r => r.Error, error));
    }

    private static string? Trim(string? text, int max) => text is null ? null : text.Length <= max ? text : text[..max];
}
