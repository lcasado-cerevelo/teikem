using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Services;

namespace Teikem.Infrastructure.Jobs;

/// <summary>
/// Lote 225 (P5 del pedido de vendedores) — el aviso de la mañana «Facturas por pasar a QuickBooks: N» (evento SALES_QUICKBOOKS_PENDING): cada día a las 07:00 (hora de la
/// compañía; el administrador la cambia en Sistema → Trabajos programados), uno al día y solo si hay algo pendiente. A quién llega y por qué canal (campana, correo) lo
/// dicen las reglas de aviso. Si el módulo de órdenes de venta está apagado no hace nada.
/// </summary>
public sealed class SalesQuickBooksPendingJob : IScheduledJob
{
    public string Key => JobKeys.SalesQuickBooksPending;
    public string NameEs => "Aviso de facturas por pasar a QuickBooks";
    public string NameEn => "Invoices to pass to QuickBooks notice";
    public string DescriptionEs => "Cada mañana avisa (evento «Facturas por pasar a QuickBooks») cuántas facturas de venta faltan por pasar a QuickBooks, si hay alguna.";
    public string DescriptionEn => "Every morning notifies (event \"Invoices to pass to QuickBooks\") how many sales invoices are still to be passed to QuickBooks, if any.";
    public string DefaultKind => JobScheduleKinds.Daily;
    public int? DefaultIntervalSeconds => null;
    public TimeSpan? DefaultDailyAt => new(7, 0, 0);
    public bool DefaultEnabled => true;
    public TimeSpan MaxRuntime => TimeSpan.FromMinutes(5);

    public async Task<string?> RunAsync(JobRunContext context, CancellationToken ct)
    {
        var modules = context.Services.GetRequiredService<ModuleService>();
        if (!await modules.IsEnabledAsync(ModuleKeys.SalesOrders, ct)) return "El módulo de órdenes de venta está apagado; no se hizo nada.";
        var qb = context.Services.GetRequiredService<QuickBooksService>();
        var (count, _, _) = await qb.CountPendingAsync(ct);
        if (count == 0) return "No hay facturas por pasar a QuickBooks.";
        return await qb.NotifyPendingAsync(ct) == 1 ? $"Aviso del día: {count} factura(s) por pasar a QuickBooks." : "El aviso de hoy ya había salido.";
    }
}
