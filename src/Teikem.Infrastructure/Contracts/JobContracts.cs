namespace Teikem.Infrastructure.Contracts;

// ======================================================================================================================
// Lote 50 (P2.2) — trabajos programados de la compañía (Sistema → Trabajos programados). Permiso jobs.manage.
// ======================================================================================================================

/// <summary>
/// Un trabajo programado de la compañía. ScheduleKind: INTERVAL (IntervalSeconds) | DAILY (DailyAt = «HH:mm» hora local);
/// LastStatus: NEVER | OK | FAILED | RUNNING; IsRunning = tiene un reclamo vigente; ManualRequested = «Ejecutar ahora» pedido y
/// aún no tomado. Name/Description en español y NameEn/DescriptionEn en inglés. RowVersion en base64 para el PATCH.
/// </summary>
public sealed record ScheduledJobDto(
    string Key,
    string Name,
    string NameEn,
    string Description,
    string DescriptionEn,
    bool IsEnabled,
    string ScheduleKind,
    int? IntervalSeconds,
    string? DailyAt,
    DateTime? NextRunUtc,
    bool ManualRequested,
    DateTime? LastStartedUtc,
    DateTime? LastFinishedUtc,
    string LastStatus,
    string? LastMessage,
    string? LastError,
    int ConsecutiveFailures,
    bool IsRunning,
    string RowVersion);

/// <summary>
/// Cambio del interruptor y/o del horario (PATCH). null = sin cambio. Si viene ScheduleKind se valida el horario completo
/// (INTERVAL pide IntervalSeconds 60..604800; DAILY pide DailyAt «HH:mm»). RowVersion opcional (409 si cambió).
/// </summary>
public sealed record ScheduledJobRequest(
    bool? IsEnabled = null,
    string? ScheduleKind = null,
    int? IntervalSeconds = null,
    string? DailyAt = null,
    string? RowVersion = null);

/// <summary>Una corrida del historial. TriggeredBy: SCHEDULE | MANUAL; Status: RUNNING | OK | FAILED.</summary>
public sealed record ScheduledJobRunDto(
    long Id,
    DateTime StartedUtc,
    DateTime? FinishedUtc,
    string Status,
    string TriggeredBy,
    string? Message,
    string? Error);
