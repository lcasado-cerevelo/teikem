using Teikem.Domain.Constants;
using Teikem.Domain.Trips;

namespace Teikem.Infrastructure.Trips;

/// <summary>
/// Lote 5 (P3) — motor 'HEURISTIC' detrás de la costura IRouteOptimizer: delega en la regla pura y determinista
/// <see cref="HeuristicRoutePlanner"/> (zona y ventana, respetando paradas, peso y volumen del vehículo). Contrato de la
/// costura: NO toca la BD ni hace llamadas externas; RouteOptimizationService lo invoca FUERA de la transacción (FASE 2),
/// con un token combinado con RouteEditRules.EngineTimeout, y valida su resultado antes de escribirlo.
/// </summary>
public sealed class HeuristicRouteOptimizer : IRouteOptimizer
{
    public string EngineCode => OptimizerEngines.Heuristic;

    public Task<RouteOptimizationResult> OptimizeAsync(RouteOptimizationRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(HeuristicRoutePlanner.Plan(request));
    }
}
