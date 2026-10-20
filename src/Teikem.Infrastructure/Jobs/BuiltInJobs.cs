using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Constants;
using Teikem.Domain.Integrations;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Services;

namespace Teikem.Infrastructure.Jobs;

/// <summary>
/// Lote 50 (P2.2) — limpia el historial de corridas: conserva las últimas 100 de cada trabajo de la compañía. Cada noche a las 03:00
/// (hora local). Solo borra historial de corridas (nunca datos del negocio).
/// </summary>
public sealed class JobRunPurgeJob : IScheduledJob
{
    public const int KeepPerJob = 100;
    public string Key => JobKeys.RunPurge;
    public string NameEs => "Limpiar el historial de trabajos";
    public string NameEn => "Clean up job history";
    public string DescriptionEs => "Conserva las últimas 100 corridas de cada trabajo y borra las más viejas.";
    public string DescriptionEn => "Keeps the last 100 runs of each job and deletes the older ones.";
    public string DefaultKind => JobScheduleKinds.Daily;
    public int? DefaultIntervalSeconds => null;
    public TimeSpan? DefaultDailyAt => new(3, 0, 0);
    public bool DefaultEnabled => true;
    public TimeSpan MaxRuntime => TimeSpan.FromMinutes(5);

    public async Task<string?> RunAsync(JobRunContext context, CancellationToken ct)
    {
        var db = context.Services.GetRequiredService<TeikemDbContext>();
        var keys = await db.ScheduledJobRuns.AsNoTracking().Select(r => r.JobKey).Distinct().ToListAsync(ct);
        var deleted = 0;
        foreach (var key in keys)
        {
            var cutoff = await db.ScheduledJobRuns.AsNoTracking().Where(r => r.JobKey == key)
                .OrderByDescending(r => r.ScheduledJobRunId).Skip(KeepPerJob).Select(r => (long?)r.ScheduledJobRunId).FirstOrDefaultAsync(ct);
            if (cutoff is null) continue;
            deleted += await db.ScheduledJobRuns.Where(r => r.JobKey == key && r.ScheduledJobRunId <= cutoff).ExecuteDeleteAsync(ct);
        }
        return deleted == 0 ? "Nada que limpiar." : $"{deleted} corridas viejas borradas.";
    }
}

/// <summary>
/// Lote 50 (P2.2) — barrido completo Kárdex ↔ saldo de la compañía (el que el Lote 14 dejó pendiente; SweepAsync con SCHEDULED).
/// Cada noche a las 02:00 (hora local). Si el módulo de inventario está apagado no hace nada.
/// </summary>
public sealed class InventorySweepJob : IScheduledJob
{
    public string Key => JobKeys.InventorySweep;
    public string NameEs => "Revisión diaria de inventario";
    public string NameEn => "Daily inventory check";
    public string DescriptionEs => "Compara todo el Kárdex contra los saldos y abre descuadres si algo no cuadra.";
    public string DescriptionEn => "Compares the whole ledger against balances and opens discrepancies when something does not match.";
    public string DefaultKind => JobScheduleKinds.Daily;
    public int? DefaultIntervalSeconds => null;
    public TimeSpan? DefaultDailyAt => new(2, 0, 0);
    public bool DefaultEnabled => true;
    public TimeSpan MaxRuntime => TimeSpan.FromMinutes(30);

    public async Task<string?> RunAsync(JobRunContext context, CancellationToken ct)
    {
        var modules = context.Services.GetRequiredService<ModuleService>();
        if (!await modules.IsEnabledAsync(ModuleKeys.WmsLotSerial, ct)) return "El módulo de inventario está apagado; no se hizo nada.";
        var sweep = context.Services.GetRequiredService<InventoryReconciliationService>();
        var run = await sweep.SweepAsync(ReconciliationTriggers.Scheduled, ct);
        return $"{run.ProductsChecked} productos revisados; {run.Opened} descuadres nuevos, {run.StillOpen} siguen abiertos, {run.SelfCorrected} se corrigieron solos.";
    }
}

/// <summary>Lote 50 (P2.2) — trabajo de prueba (apagado): permite probar «Ejecutar ahora» y el historial sin tocar datos.</summary>
public sealed class TestJob : IScheduledJob
{
    public string Key => JobKeys.Test;
    public string NameEs => "Trabajo de prueba";
    public string NameEn => "Test job";
    public string DescriptionEs => "No hace nada: sirve para probar «Ejecutar ahora» y el historial.";
    public string DescriptionEn => "Does nothing: use it to try \"Run now\" and the history.";
    public string DefaultKind => JobScheduleKinds.Interval;
    public int? DefaultIntervalSeconds => 3600;
    public TimeSpan? DefaultDailyAt => null;
    public bool DefaultEnabled => false;
    public TimeSpan MaxRuntime => TimeSpan.FromMinutes(1);

    public Task<string?> RunAsync(JobRunContext context, CancellationToken ct) => Task.FromResult<string?>("Prueba completada.");
}

/// <summary>
/// Lote 66 (P4 S3) — avisa cuando un backorder de una orden de venta ya se puede surtir (entró inventario por cualquier vía). Cada 10 minutos. Si el módulo de órdenes
/// de venta está apagado no hace nada. Solo avisa (no surte solo: el modo automático de backorder queda para después).
/// </summary>
public sealed class SalesBackorderScanJob : IScheduledJob
{
    public string Key => JobKeys.SalesBackorderScan;
    public string NameEs => "Avisar backorders que ya se pueden surtir";
    public string NameEn => "Notify backorders ready to fulfill";
    public string DescriptionEs => "Revisa las órdenes de venta con backorder y avisa (evento «Backorder que ya se puede surtir») cuando hay inventario disponible para surtirlas.";
    public string DescriptionEn => "Checks sales orders with backorder and notifies (event \"Backorder ready to fulfill\") when stock is available to fulfill them.";
    public string DefaultKind => JobScheduleKinds.Interval;
    public int? DefaultIntervalSeconds => 600;
    public TimeSpan? DefaultDailyAt => null;
    public bool DefaultEnabled => true;
    public TimeSpan MaxRuntime => TimeSpan.FromMinutes(5);

    public async Task<string?> RunAsync(JobRunContext context, CancellationToken ct)
    {
        var modules = context.Services.GetRequiredService<ModuleService>();
        if (!await modules.IsEnabledAsync(ModuleKeys.SalesOrders, ct)) return "El módulo de órdenes de venta está apagado; no se hizo nada.";
        var sales = context.Services.GetRequiredService<SalesOrderService>();
        var sent = await sales.NotifyFulfillableBackordersAsync(ct);
        return sent == 0 ? "Ningún backorder nuevo por avisar." : $"{sent} orden(es) con backorder surtible avisadas.";
    }
}

/// <summary>
/// Lote 143 (2026-10-20) — reserva el pendiente (backorder) de los avisos de salida cuando entra mercancía (por cualquier vía), el aviso más antiguo primero, y avisa
/// (evento «Pendiente de un aviso de salida ya reservado»). Cada 10 minutos. Si el módulo de inventario está apagado no hace nada.
/// </summary>
public sealed class ExitNoticeBackorderScanJob : IScheduledJob
{
    public string Key => JobKeys.ExitNoticeBackorderScan;
    public string NameEs => "Reservar el pendiente de los avisos de salida";
    public string NameEn => "Reserve exit notice backorders";
    public string DescriptionEs => "Reserva el pendiente de los avisos de salida cuando entra mercancía, el aviso más antiguo primero, y avisa (evento «Pendiente de un aviso de salida ya reservado»).";
    public string DescriptionEn => "Reserves the pending part of exit notices when stock arrives, oldest notice first, and notifies (event \"Exit notice backorder reserved\").";
    public string DefaultKind => JobScheduleKinds.Interval;
    public int? DefaultIntervalSeconds => 600;
    public TimeSpan? DefaultDailyAt => null;
    public bool DefaultEnabled => true;
    public TimeSpan MaxRuntime => TimeSpan.FromMinutes(5);

    public async Task<string?> RunAsync(JobRunContext context, CancellationToken ct)
    {
        var modules = context.Services.GetRequiredService<ModuleService>();
        if (!await modules.IsEnabledAsync(ModuleKeys.WmsLotSerial, ct)) return "El módulo de inventario está apagado; no se hizo nada.";
        var n = await context.Services.GetRequiredService<ExitNoticeService>().ReserveBackordersAsync(ct);
        return n == 0 ? "Ningún pendiente nuevo por reservar." : $"{n} aviso(s) de salida con pendiente reservado.";
    }
}

/// <summary>
/// Lote 141 (2026-10-20) — revisa el inventario cada día y publica los avisos de inventario: lotes por vencer o vencidos, productos bajo el mínimo, sin existencia y sin
/// movimiento (<see cref="Services.InventoryAlertService"/>). Un aviso por evento con el conjunto completo; no repite el mismo conjunto en la misma semana. Si el módulo
/// de inventario está apagado no hace nada.
/// </summary>
public sealed class InventoryAlertScanJob : IScheduledJob
{
    public string Key => JobKeys.InventoryAlertScan;
    public string NameEs => "Avisos de inventario";
    public string NameEn => "Inventory alerts";
    public string DescriptionEs => "Revisa el inventario y avisa (eventos «Lotes por vencer», «Lotes vencidos», «Productos bajo el mínimo», «Sin existencia» y «Sin movimiento») según las reglas de aviso de la compañía.";
    public string DescriptionEn => "Checks the inventory and notifies (events \"Lots about to expire\", \"Expired lots\", \"Products below minimum\", \"Out of stock\" and \"No movement\") according to the company's notification rules.";
    public string DefaultKind => JobScheduleKinds.Daily;
    public int? DefaultIntervalSeconds => null;
    public TimeSpan? DefaultDailyAt => new(6, 0, 0);
    public bool DefaultEnabled => true;
    public TimeSpan MaxRuntime => TimeSpan.FromMinutes(10);

    public async Task<string?> RunAsync(JobRunContext context, CancellationToken ct)
    {
        var modules = context.Services.GetRequiredService<ModuleService>();
        if (!await modules.IsEnabledAsync(ModuleKeys.WmsLotSerial, ct)) return "El módulo de inventario está apagado; no se hizo nada.";
        var r = await context.Services.GetRequiredService<InventoryAlertService>().ScanAsync(ct);
        return r.Total == 0
            ? "Nada nuevo que avisar."
            : $"Avisos nuevos: {r.Expiring} por vencer, {r.Expired} vencidos, {r.Low} bajo el mínimo, {r.Out} sin existencia, {r.NoMovement} sin movimiento.";
    }
}

/// <summary>
/// Lote 75 (P5) — pasa a Vencida (OVERDUE) lo facturado cuyo vencimiento ya pasó y todavía tiene saldo. Cada hora. Si el módulo de facturación está apagado no hace nada.
/// </summary>
public sealed class BillingOverdueScanJob : IScheduledJob
{
    public string Key => JobKeys.BillingOverdueScan;
    public string NameEs => "Marcar facturas vencidas";
    public string NameEn => "Mark overdue invoices";
    public string DescriptionEs => "Pasa a «Vencida» las facturas emitidas cuya fecha de vencimiento ya pasó y todavía tienen saldo.";
    public string DescriptionEn => "Marks as \"Overdue\" the issued invoices whose due date has passed and still have a balance.";
    public string DefaultKind => JobScheduleKinds.Interval;
    public int? DefaultIntervalSeconds => 3600;
    public TimeSpan? DefaultDailyAt => null;
    public bool DefaultEnabled => true;
    public TimeSpan MaxRuntime => TimeSpan.FromMinutes(5);

    public async Task<string?> RunAsync(JobRunContext context, CancellationToken ct)
    {
        var modules = context.Services.GetRequiredService<ModuleService>();
        if (!await modules.IsEnabledAsync(ModuleKeys.Billing, ct)) return "El módulo de facturación está apagado; no se hizo nada.";
        var payments = context.Services.GetRequiredService<PaymentService>();
        var n = await payments.MarkOverdueAsync(ct);
        return n == 0 ? "Ninguna factura nueva por vencer." : $"{n} factura(s) marcadas como vencidas.";
    }
}

/// <summary>
/// Lote 90 (P12) — la foto diaria de los indicadores: guarda en KpiDailySnapshot las cifras de hoy (órdenes y COD) para que los ríos del Pulso y los gráficos tengan historial. Cada noche a las 23:55 (hora local) para
/// quedarse con el cierre del día; «Ejecutar ahora» la repite y reemplaza la de hoy. Solo guarda los indicadores de los módulos encendidos.
/// </summary>
public sealed class KpiSnapshotJob : IScheduledJob
{
    public string Key => JobKeys.KpiSnapshot;
    public string NameEs => "Foto diaria de indicadores";
    public string NameEn => "Daily indicator snapshot";
    public string DescriptionEs => "Guarda las cifras del día (órdenes por etapa y dinero COD) para ver su historial en el Pulso y en los gráficos.";
    public string DescriptionEn => "Saves the day's figures (orders by stage and COD money) so their history shows on the Pulse and in the charts.";
    public string DefaultKind => JobScheduleKinds.Daily;
    public int? DefaultIntervalSeconds => null;
    public TimeSpan? DefaultDailyAt => new(23, 55, 0);
    public bool DefaultEnabled => true;
    public TimeSpan MaxRuntime => TimeSpan.FromMinutes(5);

    public async Task<string?> RunAsync(JobRunContext context, CancellationToken ct)
    {
        var snapshots = context.Services.GetRequiredService<KpiSnapshotService>();
        var (day, saved) = await snapshots.CaptureAsync(ct);
        return saved == 0 ? "Ningún módulo con indicadores está encendido; no se guardó nada." : $"Foto del {day:yyyy-MM-dd}: {saved} cifras guardadas.";
    }
}

/// <summary>
/// Lote 92 (P12) — envía por correo los informes programados (Vistas con programación y destinatarios) que ya tocan según la hora local de la compañía. Corre cada 5 minutos; un informe se manda una vez por ocurrencia. Cada
/// informe corre con los permisos y el alcance de datos de su dueño, y uno que falla no impide los demás.
/// </summary>
public sealed class ReportDeliveryJob : IScheduledJob
{
    public string Key => JobKeys.ReportDelivery;
    public string NameEs => "Enviar informes programados";
    public string NameEn => "Send scheduled reports";
    public string DescriptionEs => "Manda por correo los informes con programación y destinatarios cuando les toca (hora de la compañía).";
    public string DescriptionEn => "Emails the reports that have a schedule and recipients when they are due (company time).";
    public string DefaultKind => JobScheduleKinds.Interval;
    public int? DefaultIntervalSeconds => 300;
    public TimeSpan? DefaultDailyAt => null;
    public bool DefaultEnabled => true;
    public TimeSpan MaxRuntime => TimeSpan.FromMinutes(10);

    public async Task<string?> RunAsync(JobRunContext context, CancellationToken ct)
    {
        var delivery = context.Services.GetRequiredService<ReportDeliveryService>();
        var (sent, skipped, failed) = await delivery.RunDueAsync(ct);
        return sent + skipped + failed == 0 ? "Ningún informe por enviar." : $"{sent} enviado(s), {skipped} omitido(s), {failed} con error.";
    }
}

/// <summary>
/// Lote 98 (P10) — avisos al cliente: junta los hechos ya asentados (órdenes recogidas, en ruta o entregadas; remesas COD remitidas; facturas emitidas) en resúmenes por cliente y ruta, los manda por correo, los deja
/// en la bandeja del portal y reintenta los envíos fallidos. Corre cada 2 minutos; un grupo se resume cuando lleva 3 minutos sin hechos nuevos.
/// </summary>
public sealed class ClientNotificationDigestJob : IScheduledJob
{
    public string Key => JobKeys.ClientNotificationDigest;
    public string NameEs => "Avisos a los clientes";
    public string NameEn => "Client notifications";
    public string DescriptionEs => "Resume lo ocurrido con las órdenes, el COD y las facturas de cada cliente y se lo manda por los canales que la compañía encendió.";
    public string DescriptionEn => "Summarizes what happened with each client's orders, COD and invoices and sends it through the channels the company turned on.";
    public string DefaultKind => JobScheduleKinds.Interval;
    public int? DefaultIntervalSeconds => 120;
    public TimeSpan? DefaultDailyAt => null;
    public bool DefaultEnabled => true;
    public TimeSpan MaxRuntime => TimeSpan.FromMinutes(5);

    public async Task<string?> RunAsync(JobRunContext context, CancellationToken ct)
    {
        var service = context.Services.GetRequiredService<ClientNotificationService>();
        var (summaries, sent, failed) = await service.DigestAsync(ct);
        return summaries + sent + failed == 0 ? "Ningún aviso por mandar." : $"{summaries} resumen(es), {sent} envío(s), {failed} con error.";
    }
}

/// <summary>
/// Lote 116 — avisos al consignatario: manda por correo los avisos pendientes (la orden salió En ruta, intento fallido, estatus final, o el alta del consignatario) con el enlace de seguimiento y el de baja, y reintenta los fallidos
/// a 1, 5 y 30 minutos. Corre cada minuto.
/// </summary>
public sealed class ConsigneeNoticeDispatchJob : IScheduledJob
{
    public string Key => JobKeys.ConsigneeNoticeDispatch;
    public string NameEs => "Avisos a los consignatarios";
    public string NameEn => "Consignee notices";
    public string DescriptionEs => "Manda a los consignatarios que el cliente marcó los avisos de sus paquetes (en camino, intento fallido, entregado…) con el enlace de seguimiento.";
    public string DescriptionEn => "Sends the consignees the client marked the notices about their packages (on its way, failed attempt, delivered…) with the tracking link.";
    public string DefaultKind => JobScheduleKinds.Interval;
    public int? DefaultIntervalSeconds => 60;
    public TimeSpan? DefaultDailyAt => null;
    public bool DefaultEnabled => true;
    public TimeSpan MaxRuntime => TimeSpan.FromMinutes(5);

    public async Task<string?> RunAsync(JobRunContext context, CancellationToken ct)
    {
        var service = context.Services.GetRequiredService<ConsigneeNoticeService>();
        var (sent, skipped, failed) = await service.DispatchAsync(ct);
        return sent + skipped + failed == 0 ? "Ningún aviso por mandar." : $"{sent} enviado(s), {skipped} omitido(s), {failed} con error.";
    }
}

/// <summary>
/// Lote 137b (P11) — las integraciones de SALIDA: pasa a envíos la cola de órdenes de las que mandan «al instante», manda lo que toque (primer intento o reintento con espera creciente) por SFTP/FTP o por la
/// API saliente y purga lo viejo. Corre cada minuto; las de «por horario» arman su envío cuando les toca (trabajo de integraciones por horario).
/// </summary>
public sealed class IntegrationOutputJob : IScheduledJob
{
    public string Key => JobKeys.IntegrationOutput;
    public string NameEs => "Salida de integraciones";
    public string NameEn => "Integration output";
    public string DescriptionEs => "Arma y manda las salidas de órdenes de las integraciones (SFTP, FTP y API saliente) y reintenta las fallidas.";
    public string DescriptionEn => "Builds and sends the integrations' order outputs (SFTP, FTP and outbound API) and retries failed ones.";
    public string DefaultKind => JobScheduleKinds.Interval;
    public int? DefaultIntervalSeconds => 60;
    public TimeSpan? DefaultDailyAt => null;
    public bool DefaultEnabled => true;
    public TimeSpan MaxRuntime => TimeSpan.FromMinutes(10);

    public async Task<string?> RunAsync(JobRunContext context, CancellationToken ct)
    {
        var modules = context.Services.GetRequiredService<ModuleService>();
        if (!(await modules.GetEnabledKeysAsync(context.TenantId, ct)).Contains(ModuleKeys.Integrations)) return "El módulo de integraciones está apagado.";
        var o = await context.Services.GetRequiredService<IntegrationOutputService>().ProcessAsync(context.NowUtc, ct);
        return o.Created + o.Sent + o.Retrying + o.Undelivered + o.Purged == 0
            ? "Ninguna salida por mandar."
            : $"{o.Created} envío(s) armado(s), {o.Sent} enviado(s), {o.Retrying} por reintentar, {o.Undelivered} no entregado(s), {o.Purged} purgado(s).";
    }
}

/// <summary>
/// Lote 136 (P11) — entrega los webhooks de salida que ya tocan (primer intento o reintento con espera creciente), marca las no entregadas, suspende los webhooks que fallan seguido y purga las salidas
/// viejas. Corre cada minuto; si el módulo de integraciones está apagado, no hace nada.
/// </summary>
public sealed class WebhookDeliveryJob : IScheduledJob
{
    public string Key => JobKeys.WebhookDelivery;
    public string NameEs => "Entrega de webhooks";
    public string NameEn => "Webhook delivery";
    public string DescriptionEs => "Envía los avisos por webhook pendientes y reintenta los fallidos con espera creciente.";
    public string DescriptionEn => "Sends the pending webhook notices and retries the failed ones with growing delays.";
    public string DefaultKind => JobScheduleKinds.Interval;
    public int? DefaultIntervalSeconds => 60;
    public TimeSpan? DefaultDailyAt => null;
    public bool DefaultEnabled => true;
    public TimeSpan MaxRuntime => TimeSpan.FromMinutes(5);

    public async Task<string?> RunAsync(JobRunContext context, CancellationToken ct)
    {
        var modules = context.Services.GetRequiredService<ModuleService>();
        if (!(await modules.GetEnabledKeysAsync(context.TenantId, ct)).Contains(ModuleKeys.Integrations)) return "El módulo de integraciones está apagado.";
        var o = await context.Services.GetRequiredService<WebhookProcessor>().ProcessDueAsync(context.NowUtc, 100, ct);
        return o.Sent + o.Retrying + o.Undelivered + o.Suspended + o.Purged == 0
            ? "Ningún webhook por entregar."
            : $"{o.Sent} entregado(s), {o.Retrying} por reintentar, {o.Undelivered} no entregado(s), {o.Suspended} webhook(s) suspendido(s), {o.Purged} salida(s) purgada(s).";
    }
}

/// <summary>
/// Lote 132 (P11) — corre las integraciones de entrada que tocan por horario (cada una con su cuenta de servicio) y purga el detalle de la bandeja pasada la retención.
/// Corre cada minuto; una integración sin horario (manual) nunca corre sola.
/// </summary>
public sealed class IntegrationRunDueJob : IScheduledJob
{
    public string Key => JobKeys.IntegrationRunDue;
    public string NameEs => "Integraciones por horario";
    public string NameEn => "Scheduled integrations";
    public string DescriptionEs => "Corre las integraciones de entrada que tocan por horario y purga los datos viejos de la bandeja.";
    public string DescriptionEn => "Runs the inbound integrations that are due on their schedule and purges old inbox data.";
    public string DefaultKind => JobScheduleKinds.Interval;
    public int? DefaultIntervalSeconds => 60;
    public TimeSpan? DefaultDailyAt => null;
    public bool DefaultEnabled => true;
    public TimeSpan MaxRuntime => TimeSpan.FromMinutes(10);

    public async Task<string?> RunAsync(JobRunContext context, CancellationToken ct)
    {
        var modules = context.Services.GetRequiredService<ModuleService>();
        if (!(await modules.GetEnabledKeysAsync(context.TenantId, ct)).Contains(ModuleKeys.Integrations)) return "El módulo de integraciones está apagado.";
        var db = context.Services.GetRequiredService<TeikemDbContext>();
        var runner = context.Services.GetRequiredService<IntegrationRunner>();
        var inbox = context.Services.GetRequiredService<IntegrationInboxService>();
        var due = await db.Integrations.AsNoTracking().Where(i => i.IsActive && i.ScheduleKind == IntegrationScheduleKinds.Interval && i.NextRunUtc <= context.NowUtc
                && (i.LockedUntilUtc == null || i.LockedUntilUtc < context.NowUtc)).OrderBy(i => i.NextRunUtc).Select(i => i.PublicId).ToListAsync(ct);
        int ran = 0, failed = 0;
        foreach (var id in due)
        {
            try { await runner.RunAsync(id, IntegrationTriggers.Schedule, null, ct); ran++; }
            catch (Exception ex) when (ex is not OperationCanceledException) { failed++; }
        }
        var purged = await inbox.PurgeExpiredAsync(ct);
        return ran + failed + purged == 0 ? "Ninguna integración por correr." : $"{ran} integración(es) corrida(s), {failed} con error, {purged} ítem(s) de bandeja purgado(s).";
    }
}
