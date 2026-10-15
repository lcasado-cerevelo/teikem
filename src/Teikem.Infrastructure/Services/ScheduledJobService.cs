using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Constants;
using Teikem.Domain.Jobs;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Jobs;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 50 (P2.2) — trabajos programados de la compañía: lista, interruptor y horario, «Ejecutar ahora» e historial.
/// - Al listar se crean las filas que falten (compañía nueva o trabajo nuevo), con el horario por omisión.
/// - Prender un trabajo calcula su próxima corrida desde ahora; apagarlo la borra. Cambiar el horario de uno prendido la recalcula.
/// - «Ejecutar ahora» NO corre dentro de la petición: pide la corrida (ManualRequested) y el worker la toma en unos segundos
///   (también con el trabajo apagado). 409 si ya se está ejecutando.
/// - Solo se ven y se tocan los trabajos que existen en esta versión del sistema (una fila huérfana se ignora).
/// </summary>
public sealed class ScheduledJobService(TeikemDbContext db, ITenantContext tenant, ScheduledJobRunner runner)
{
    public async Task<IReadOnlyList<ScheduledJobDto>> ListAsync(CancellationToken ct)
    {
        var tenantId = ((TenantContext)tenant).RequireTenantId();
        await runner.EnsureTenantRowsAsync(tenantId, DateTime.UtcNow, ct);
        var rows = await db.ScheduledJobs.AsNoTracking().ToListAsync(ct);
        return rows.Where(r => runner.Find(r.JobKey) is not null)
            .OrderBy(r => runner.Find(r.JobKey)!.NameEs, StringComparer.CurrentCultureIgnoreCase)
            .Select(ToDto).ToList();
    }

    public async Task<ScheduledJobDto> GetAsync(string key, CancellationToken ct) => ToDto(await LoadAsync(key, ct, track: false));

    public async Task<ScheduledJobDto> UpdateAsync(string key, ScheduledJobRequest req, CancellationToken ct)
    {
        var job = await LoadAsync(key, ct);
        db.ApplyRowVersion(job, req.RowVersion);
        var now = DateTime.UtcNow;

        var scheduleChanged = false;
        if (req.ScheduleKind is not null)
        {
            var (kind, interval, dailyAt, field, error) = JobSchedule.Validate(req.ScheduleKind, req.IntervalSeconds, req.DailyAt);
            if (error is not null) throw new ValidationException(field!, error);
            job.ScheduleKind = kind!;
            job.IntervalSeconds = interval;
            job.DailyAt = dailyAt;
            scheduleChanged = true;
        }
        var wasEnabled = job.IsEnabled;
        if (req.IsEnabled is bool enabled) job.IsEnabled = enabled;

        if (!job.IsEnabled) job.NextRunUtc = null;
        else if (!wasEnabled || scheduleChanged) job.NextRunUtc = JobSchedule.Next(job.ScheduleKind, job.IntervalSeconds, job.DailyAt, now);

        await db.SaveGuardedAsync(DbExtensions.ConcurrencyMessage, ct);
        return ToDto(job);
    }

    public async Task<ScheduledJobDto> RunNowAsync(string key, CancellationToken ct)
    {
        var job = await LoadAsync(key, ct, track: false);
        var now = DateTime.UtcNow;
        var asked = await db.ScheduledJobs.Where(j => j.ScheduledJobId == job.ScheduledJobId && (j.LockedUntilUtc == null || j.LockedUntilUtc < now))
            .ExecuteUpdateAsync(s => s.SetProperty(j => j.ManualRequested, true), ct);
        if (asked == 0) throw new ConflictException(JobSchedule.AlreadyRunningMessage);
        return ToDto(await LoadAsync(key, ct, track: false));
    }

    public async Task<IReadOnlyList<ScheduledJobRunDto>> RunsAsync(string key, int take, CancellationToken ct)
    {
        await LoadAsync(key, ct, track: false);
        take = Math.Clamp(take <= 0 ? 50 : take, 1, 100);
        var rows = await db.ScheduledJobRuns.AsNoTracking().Where(r => r.JobKey == key)
            .OrderByDescending(r => r.ScheduledJobRunId).Take(take).ToListAsync(ct);
        return rows.Select(r => new ScheduledJobRunDto(r.ScheduledJobRunId, r.StartedUtc, r.FinishedUtc, r.Status, r.TriggeredBy, r.Message, r.Error)).ToList();
    }

    private async Task<ScheduledJob> LoadAsync(string key, CancellationToken ct, bool track = true)
    {
        var k = (key ?? string.Empty).Trim().ToUpperInvariant();
        if (runner.Find(k) is null) throw new NotFoundException("Trabajo");
        var q = track ? db.ScheduledJobs : db.ScheduledJobs.AsNoTracking();
        var row = await q.FirstOrDefaultAsync(j => j.JobKey == k, ct);
        if (row is null)
        {
            // La compañía todavía no tiene la fila (primera visita): se crea con el horario por omisión.
            await runner.EnsureTenantRowsAsync(((TenantContext)tenant).RequireTenantId(), DateTime.UtcNow, ct);
            row = await q.FirstOrDefaultAsync(j => j.JobKey == k, ct) ?? throw new NotFoundException("Trabajo");
        }
        return row;
    }

    private ScheduledJobDto ToDto(ScheduledJob j)
    {
        var def = runner.Find(j.JobKey)!;
        var running = j.LockedBy is not null && j.LockedUntilUtc > DateTime.UtcNow;
        return new ScheduledJobDto(j.JobKey, def.NameEs, def.NameEn, def.DescriptionEs, def.DescriptionEn, j.IsEnabled, j.ScheduleKind,
            j.IntervalSeconds, j.DailyAt?.ToString(@"hh\:mm"), j.NextRunUtc, j.ManualRequested, j.LastStartedUtc, j.LastFinishedUtc,
            j.LastStatus, j.LastMessage, j.LastError, j.ConsecutiveFailures, running, Convert.ToBase64String(j.RowVersion ?? Array.Empty<byte>()));
    }
}
