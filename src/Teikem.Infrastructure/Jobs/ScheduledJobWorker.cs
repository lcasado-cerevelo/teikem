using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Teikem.Infrastructure.Jobs;

/// <summary>
/// Lote 50 (P2.2) — ciclo de fondo de los trabajos programados: al arrancar y cada 10 minutos crea las filas que falten
/// (compañías nuevas o trabajos nuevos) y cada PollSeconds (30 por omisión) corre lo que toca. Los comandos de consola no llegan a
/// app.Run(): este servicio no arranca. Jobs:Enabled = false lo apaga. Un error nunca detiene el ciclo.
/// </summary>
public sealed class ScheduledJobWorker(ScheduledJobRunner runner, IOptions<JobsOptions> options, ILogger<ScheduledJobWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opts = options.Value;
        if (!opts.Enabled)
        {
            logger.LogInformation("Trabajos programados apagados (Jobs:Enabled = false).");
            return;
        }
        var poll = TimeSpan.FromSeconds(Math.Max(5, opts.PollSeconds));
        var nextEnsure = DateTime.MinValue;
        logger.LogInformation("Trabajos programados iniciados (cada {Seconds} s, copia {Instance}).", poll.TotalSeconds, runner.InstanceId);
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var now = DateTime.UtcNow;
                    if (now >= nextEnsure)
                    {
                        await runner.EnsureRowsAsync(now, stoppingToken);
                        nextEnsure = now.AddMinutes(10);
                    }
                    await runner.RunDueAsync(DateTime.UtcNow, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { throw; }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error en el ciclo de trabajos programados; se continúa.");
                }
                await Task.Delay(poll, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
