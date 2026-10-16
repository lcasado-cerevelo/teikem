using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Constants;
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
