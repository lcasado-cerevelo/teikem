using Teikem.Domain.Constants;

namespace Teikem.Domain.Orders;

/// <summary>
/// Etapa de un pipeline de estatus tal como la ve el tenant: código, orden efectivo (con SortOverride), tipo de etapa
/// (PIPELINE | LATERAL | TERMINAL), si es la inicial y si está habilitada. Las deshabilitadas se incluyen solo para conocer
/// el orden del destino o del origen; nunca son un paso.
/// </summary>
public sealed record PipelineStage(string Code, int SortOrder, string Kind, bool IsInitial, bool IsEnabled = true);

/// <summary>
/// Lote 4 (P7): recorrido etapa por etapa hasta un destino del pipeline, con la MISMA regla que StatusService: desde una
/// etapa PIPELINE solo se avanza a la siguiente etapa habilitada no lateral por SortOrder (no hay saltos). Lo usa la entrega
/// especial con chofer para llegar a IN_TRANSIT con una transición (e historial) por paso.
/// </summary>
public static class PipelinePath
{
    /// <summary>
    /// Códigos a recorrer desde <paramref name="fromCode"/> (excluido) hasta <paramref name="targetCode"/> (incluido).
    /// - Salta las etapas deshabilitadas; si el destino está deshabilitado termina en la última etapa PIPELINE habilitada anterior.
    /// - Vacío si el origen ya está en el destino o después.
    /// - Se detiene antes de una etapa terminal que no sea el destino (nunca cierra la orden de paso).
    /// - InvalidOperationException si el origen es lateral o terminal, o si origen/destino no existen.
    /// </summary>
    public static IReadOnlyList<string> StepsTo(IEnumerable<PipelineStage> stages, string fromCode, string targetCode)
    {
        ArgumentNullException.ThrowIfNull(stages);
        var all = stages.ToList();
        var from = all.FirstOrDefault(s => Same(s.Code, fromCode))
                   ?? throw new InvalidOperationException($"La etapa de origen '{fromCode}' no existe en el pipeline.");
        var target = all.FirstOrDefault(s => Same(s.Code, targetCode))
                     ?? throw new InvalidOperationException($"La etapa destino '{targetCode}' no existe en el pipeline.");
        if (Same(from.Kind, StageKinds.Lateral) || Same(from.Kind, StageKinds.Terminal))
            throw new InvalidOperationException($"La etapa de origen '{from.Code}' es {from.Kind.ToLowerInvariant()}: no avanza por el pipeline.");
        if (Same(target.Kind, StageKinds.Lateral))
            throw new InvalidOperationException($"La etapa destino '{target.Code}' es lateral: no se alcanza avanzando por el pipeline.");

        var enabled = all.Where(s => s.IsEnabled).ToList();
        var steps = new List<string>();
        var current = from.SortOrder;
        while (true)
        {
            var next = enabled
                .Where(s => s.SortOrder > current && !Same(s.Kind, StageKinds.Lateral))
                .OrderBy(s => s.SortOrder)
                .FirstOrDefault();
            if (next is null || next.SortOrder > target.SortOrder) break;
            var isTarget = Same(next.Code, target.Code);
            if (!isTarget && !Same(next.Kind, StageKinds.Pipeline)) break;
            steps.Add(next.Code);
            if (isTarget) break;
            current = next.SortOrder;
        }
        return steps;
    }

    private static bool Same(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
