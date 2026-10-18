using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Teikem.Domain.Constants;
using Teikem.Domain.Orders;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Geocoding;

/// <summary>
/// Lote 118 — busca la ubicación de los comprobantes de entrega que quedaron PENDING: UNA consulta a Google por comprobante, con tiempo máximo (30 s), fuera de la petición del usuario (nadie espera). Cada comprobante se «reclama»
/// con un UPDATE atómico (PENDING → RUNNING: varias copias del API no lo buscan dos veces) y se resuelve como la compañía SIN usuario. Si el resultado es OK se guardan las coordenadas; NOT_FOUND o FAILED lo dejan sin ubicación y
/// no se reintenta (un solo intento). Un RUNNING de hace más de dos minutos (la copia se cayó a medias) se da por FAILED. Los métodos son públicos para las pruebas con SQL real.
/// </summary>
public sealed class DeliveryGeocodeProcessor(IServiceScopeFactory scopes, ILogger<DeliveryGeocodeProcessor> logger)
{
    public static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(2);
    public const int BatchSize = 20;

    public async Task<int> ProcessPendingAsync(CancellationToken ct)
    {
        List<(int TenantId, int Id)> due;
        await using (var scope = scopes.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TeikemDbContext>();
            var stale = DateTime.UtcNow - StaleAfter;
            await db.Set<DeliveryProof>().IgnoreQueryFilters().Where(p => p.GeoStatus == DeliveryProofCodes.GeoRunning && p.GeoCheckedAtUtc < stale)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.GeoStatus, DeliveryProofCodes.GeoFailed), ct);
            var rows = await db.Set<DeliveryProof>().AsNoTracking().IgnoreQueryFilters().Where(p => p.GeoStatus == DeliveryProofCodes.GeoPending)
                .OrderBy(p => p.DeliveryProofId).Take(BatchSize).Select(p => new { p.TenantId, p.DeliveryProofId }).ToListAsync(ct);
            due = rows.Select(r => (r.TenantId, r.DeliveryProofId)).ToList();
        }
        var taken = 0;
        foreach (var (tenantId, id) in due)
        {
            ct.ThrowIfCancellationRequested();
            if (await TryProcessAsync(tenantId, id, ct)) taken++;
        }
        return taken;
    }

    public async Task<bool> TryProcessAsync(int tenantId, int proofId, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TenantContext>();
        using var asTenant = context.AsAnonymous(tenantId);
        var db = scope.ServiceProvider.GetRequiredService<TeikemDbContext>();
        var now = DateTime.UtcNow;
        var claimed = await db.Set<DeliveryProof>().IgnoreQueryFilters().Where(p => p.DeliveryProofId == proofId && p.TenantId == tenantId && p.GeoStatus == DeliveryProofCodes.GeoPending)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.GeoStatus, DeliveryProofCodes.GeoRunning).SetProperty(p => p.GeoCheckedAtUtc, now), ct);
        if (claimed == 0) return false;

        GeocodeOutcome outcome;
        try
        {
            var orderId = await db.Set<DeliveryProof>().AsNoTracking().Where(p => p.DeliveryProofId == proofId).Select(p => p.TransportOrderId).FirstAsync(ct);
            var deliveryTypeId = await db.LookupCodes.AsNoTracking().Where(l => l.Entity == LookupDomains.StopType && l.InternalCode == StopTypes.Delivery).Select(l => l.LookupCodeId).FirstOrDefaultAsync(ct);
            var stop = await db.OrderStops.AsNoTracking().Where(s => s.TransportOrderId == orderId).OrderByDescending(s => s.StopTypeLookupId == deliveryTypeId).ThenByDescending(s => s.Sequence).FirstOrDefaultAsync(ct);
            var address = stop is null ? string.Empty : DeliveryProofRules.BuildAddress(stop.SnapLine1, stop.SnapLine2, stop.SnapCity, stop.SnapState, stop.SnapPostalCode);
            outcome = string.IsNullOrWhiteSpace(address) ? GeocodeOutcome.NotFound : await scope.ServiceProvider.GetRequiredService<GoogleGeocoder>().GeocodeAsync(address, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            logger.LogError(ex, "La ubicación del comprobante {ProofId} (compañía {TenantId}) falló.", proofId, tenantId);
            outcome = GeocodeOutcome.Failed;
        }

        var done = DateTime.UtcNow;
        await db.Set<DeliveryProof>().IgnoreQueryFilters().Where(p => p.DeliveryProofId == proofId && p.TenantId == tenantId)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.GeoStatus, outcome.Status).SetProperty(p => p.GeoAccuracy, outcome.Confidence)
                .SetProperty(p => p.Latitude, outcome.Lat).SetProperty(p => p.Longitude, outcome.Lng).SetProperty(p => p.GeoCheckedAtUtc, done), ct);
        return true;
    }
}

/// <summary>Despierta cada pocos segundos al procesador de ubicaciones (solo si Google está encendido y con llave).</summary>
public sealed class DeliveryGeocodeWorker(DeliveryGeocodeProcessor processor, IOptions<GoogleMapsOptions> options, ILogger<DeliveryGeocodeWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (options.Value.IsUsable) await processor.ProcessPendingAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "El ciclo de ubicaciones de entregas falló."); }
            try { await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(options.Value.PollSeconds, 1, 300)), stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}
