using Teikem.Domain.Constants;

namespace Teikem.Infrastructure.Jobs;

/// <summary>
/// Lote 50 (P2.2) — un trabajo programado en código. Se registra en DI (AddScheduledJob) y el ejecutor crea su fila por compañía
/// con el horario por omisión (sin pisar lo que el administrador cambie). RunAsync corre ya como la compañía (sin usuario).
/// </summary>
public interface IScheduledJob
{
    string Key { get; }
    string NameEs { get; }
    string NameEn { get; }
    string DescriptionEs { get; }
    string DescriptionEn { get; }
    string DefaultKind { get; }
    int? DefaultIntervalSeconds { get; }
    TimeSpan? DefaultDailyAt { get; }
    bool DefaultEnabled { get; }
    /// <summary>Tiempo máximo de una corrida; pasado este plazo se da por perdida y otra copia puede tomarla.</summary>
    TimeSpan MaxRuntime { get; }
    /// <summary>Devuelve un resumen corto (texto) de lo que hizo; una excepción cuenta como falla.</summary>
    Task<string?> RunAsync(JobRunContext context, CancellationToken ct);
}

/// <summary>Lo que recibe un trabajo al correr: la compañía y desde dónde se llamó.</summary>
public sealed record JobRunContext(int TenantId, string TriggeredBy, IServiceProvider Services, DateTime NowUtc);

public static class JobKeys
{
    public const string RunPurge = "JOB_RUN_PURGE";
    public const string InventorySweep = "INVENTORY_SWEEP";
    public const string Test = "JOB_TEST";
    public const string SalesBackorderScan = "SALES_BACKORDER_SCAN";
    /// <summary>Lote 75: pasa a Vencida lo facturado cuyo plazo ya pasó.</summary>
    public const string BillingOverdueScan = "BILLING_OVERDUE_SCAN";
    /// <summary>Lote 90: guarda la foto diaria de los indicadores (ríos del Pulso y Radar).</summary>
    public const string KpiSnapshot = "KPI_SNAPSHOT";
    /// <summary>Lote 92: envía por correo los informes programados que ya tocan.</summary>
    public const string ReportDelivery = "REPORT_DELIVERY";
    /// <summary>Lote 98: resume los hechos de los clientes y manda sus avisos.</summary>
    public const string ClientNotificationDigest = "CLIENT_NOTIFICATION_DIGEST";
    /// <summary>Lote 116: manda los avisos pendientes a los consignatarios.</summary>
    public const string ConsigneeNoticeDispatch = "CONSIGNEE_NOTICE_DISPATCH";
}
