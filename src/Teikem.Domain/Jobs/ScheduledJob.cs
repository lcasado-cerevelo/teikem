using Teikem.Domain.Common;

namespace Teikem.Domain.Jobs;

/// <summary>
/// Lote 50 (P2.2) — estado y horario de UN trabajo programado de UNA compañía (el trabajo en sí vive en código, IScheduledJob).
/// Una fila por (compañía, JobKey), creada por el ejecutor si falta (nunca pisa lo que el administrador cambió).
/// - Horario: INTERVAL (cada IntervalSeconds) o DAILY (a la hora local DailyAt). NextRunUtc = cuándo toca; null = sin programar.
/// - Un trabajo se «reclama» con un UPDATE atómico (LockedBy/LockedUntilUtc): dos copias del API nunca lo corren a la vez.
/// - Los campos de ejecución (último resultado, bloqueo, fallas) cambian solos y no se auditan; el interruptor y el horario sí.
/// </summary>
[AuditEntity(Constants.EntityTypes.ScheduledJob)]
public class ScheduledJob : ITenantScoped, IAuditStamped
{
    public int ScheduledJobId { get; set; }
    public int TenantId { get; set; }
    public string JobKey { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    public string ScheduleKind { get; set; } = Constants.JobScheduleKinds.Interval;
    public int? IntervalSeconds { get; set; }
    /// <summary>Hora local (HH:mm) de la corrida diaria cuando ScheduleKind = DAILY.</summary>
    public TimeSpan? DailyAt { get; set; }
    [NotAudited] public DateTime? NextRunUtc { get; set; }
    /// <summary>«Ejecutar ahora»: pide una corrida aunque el trabajo esté apagado.</summary>
    [NotAudited] public bool ManualRequested { get; set; }
    [NotAudited] public DateTime? LastStartedUtc { get; set; }
    [NotAudited] public DateTime? LastFinishedUtc { get; set; }
    [NotAudited] public string LastStatus { get; set; } = Constants.JobStatuses.Never;
    [NotAudited] public string? LastMessage { get; set; }
    [NotAudited] public string? LastError { get; set; }
    [NotAudited] public int ConsecutiveFailures { get; set; }
    [NotAudited] public string? LockedBy { get; set; }
    [NotAudited] public DateTime? LockedUntilUtc { get; set; }
    [NotAudited] public DateTime CreatedAtUtc { get; set; }
    public int? CreatedBy { get; set; }
    [NotAudited] public DateTime? UpdatedAtUtc { get; set; }
    public int? UpdatedBy { get; set; }
    [NotAudited] public byte[]? RowVersion { get; set; }
}

/// <summary>Historial de corridas de un trabajo (se conservan las últimas 100 por trabajo; lo limpia JOB_RUN_PURGE).</summary>
public class ScheduledJobRun : ITenantScoped
{
    public long ScheduledJobRunId { get; set; }
    public int TenantId { get; set; }
    public string JobKey { get; set; } = string.Empty;
    public DateTime StartedUtc { get; set; }
    public DateTime? FinishedUtc { get; set; }
    public string Status { get; set; } = Constants.JobStatuses.Running;
    /// <summary>SCHEDULE (le tocaba) o MANUAL («Ejecutar ahora»).</summary>
    public string TriggeredBy { get; set; } = Constants.JobTriggers.Schedule;
    public string? Message { get; set; }
    public string? Error { get; set; }
    public string InstanceId { get; set; } = string.Empty;
}
