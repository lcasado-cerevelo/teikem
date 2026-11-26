using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Services;

namespace Teikem.Infrastructure.Jobs;

/// <summary>
/// Lote 248 — revisa cada día (06:00, hora de la compañía; el administrador la cambia en Sistema → Trabajos programados) los documentos del cliente con fecha «hasta» y,
/// donde está encendido el módulo de órdenes de venta, los precios especiales, y publica los avisos «Documento del cliente por vencer» / «vencido» (y el de precio especial)
/// según los umbrales de Ajustes → Operación → Avisos de vencimiento (<see cref="DocumentExpiryScanService"/>). Un aviso por documento y umbral; no se repite. Corre en
/// toda compañía con el módulo de catálogo; si está apagado no hace nada.
/// </summary>
public sealed class DocumentExpiryScanJob : IScheduledJob
{
    /// <summary>Clave del trabajo (<see cref="JobKeys.DocumentExpiryScan"/>).</summary>
    public const string JobKey = JobKeys.DocumentExpiryScan;

    public string Key => JobKey;
    public string NameEs => "Avisos de documentos por vencer";
    public string NameEn => "Expiring documents notices";
    public string DescriptionEs => "Revisa los documentos de los clientes (y los precios especiales) con fecha «hasta» y avisa (eventos «Documento del cliente por vencer», «Documento del cliente vencido» y «Precio especial por vencer») según los umbrales de la compañía.";
    public string DescriptionEn => "Checks the customers' documents (and special prices) with a valid-to date and notifies (events \"Customer document about to expire\", \"Customer document expired\" and \"Special price about to expire\") according to the company's thresholds.";
    public string DefaultKind => JobScheduleKinds.Daily;
    public int? DefaultIntervalSeconds => null;
    public TimeSpan? DefaultDailyAt => new(6, 0, 0);
    public bool DefaultEnabled => true;
    public TimeSpan MaxRuntime => TimeSpan.FromMinutes(10);

    public async Task<string?> RunAsync(JobRunContext context, CancellationToken ct)
    {
        var modules = context.Services.GetRequiredService<ModuleService>();
        if (!await modules.IsEnabledAsync(ModuleKeys.Catalog, ct)) return "El módulo de catálogo está apagado; no se hizo nada.";
        var result = await context.Services.GetRequiredService<DocumentExpiryScanService>().ScanAsync(ct);
        return DocumentExpiryScanService.Summary(result);
    }
}
