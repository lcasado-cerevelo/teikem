using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Services;

namespace Teikem.Infrastructure.Jobs;

/// <summary>
/// Lote 227 (P7 del pedido de vendedores) — el puente entre compañías, del lado de la compañía cliente: cada 2 minutos manda a la compañía enlazada las entregas que quedaron
/// pendientes (sin señal, una falla pasajera o creadas dentro de otra transacción) y trae de vuelta el estatus, quién recibió y cuándo de las que no han terminado. Corre como la
/// compañía (sin usuario); lo de la otra compañía se hace en su propio contexto acotado. Si el módulo de compañías enlazadas está apagado no hace nada.
/// </summary>
public sealed class TenantLinkSyncJob : IScheduledJob
{
    public string Key => JobKeys.IntercompanySync;
    public string NameEs => "Entregas por compañía enlazada";
    public string NameEn => "Deliveries by linked company";
    public string DescriptionEs => "Manda a la compañía enlazada las entregas pendientes y trae de vuelta su estatus y su entrega.";
    public string DescriptionEn => "Sends pending deliveries to the linked company and brings back their status and delivery.";
    public string DefaultKind => JobScheduleKinds.Interval;
    public int? DefaultIntervalSeconds => 120;
    public TimeSpan? DefaultDailyAt => null;
    public bool DefaultEnabled => true;
    public TimeSpan MaxRuntime => TimeSpan.FromMinutes(5);

    public async Task<string?> RunAsync(JobRunContext context, CancellationToken ct)
    {
        var modules = context.Services.GetRequiredService<ModuleService>();
        if (!await modules.IsEnabledAsync(ModuleKeys.Intercompany, ct)) return "El módulo de compañías enlazadas está apagado; no se hizo nada.";
        return await context.Services.GetRequiredService<TenantLinkBridge>().RunDueAsync(context.NowUtc, ct);
    }
}
