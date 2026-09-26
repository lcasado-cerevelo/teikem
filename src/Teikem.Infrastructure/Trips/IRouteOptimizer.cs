using Teikem.Domain.Trips;

namespace Teikem.Infrastructure.Trips;

/// <summary>
/// Lote 5 (P0) — costura del motor de optimización (HEURISTIC en este lote; VROOM / OR-Tools después).
/// Contrato: la implementación NO toca la BD ni retiene bloqueos; RouteOptimizationService la llama FUERA de transacción
/// (FASE 2), con un token que combina la cancelación del cliente y el tiempo máximo, y valida su resultado
/// (RouteOptimizationRules.ValidateResult) antes de escribirlo.
/// </summary>
public interface IRouteOptimizer
{
    /// <summary>Código del motor en el catálogo OptimizerEngine (OptimizationRun.EngineLookupId).</summary>
    string EngineCode { get; }

    Task<RouteOptimizationResult> OptimizeAsync(RouteOptimizationRequest request, CancellationToken ct);
}
