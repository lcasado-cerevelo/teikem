namespace Teikem.Domain.Orders;

/// <summary>Un estatus de orden tal como lo ve la compañía: código, texto (con su nombre propio si lo cambió), tipo de etapa (PIPELINE, LATERAL, TERMINAL), posición y si está habilitado.</summary>
public sealed record ProgressStatus(string Code, string Label, string Kind, int SortOrder, bool Enabled);

/// <summary>El avance de una orden: paso actual de Total (0 = ninguno), estado, y el estatus actual con su texto.</summary>
public sealed record OrderProgressResult(int Step, int Total, string State, string StatusCode, string StatusLabel);

/// <summary>
/// Lote 103 — el avance de una orden para quien la espera: la <b>barra de progreso</b> del portal del cliente, la del consignatario y la página pública «¿Cómo va mi orden?». Decidido con Luis (2026-10-17): la barra tiene
/// <b>tantas etapas como estatus tenga el camino normal de las órdenes de la compañía</b> (con los nombres que ella les puso), no un número fijo. Las etapas son los estatus de tipo PIPELINE más el primer estatus final
/// (el de «entregada»: el terminal de menor posición), en orden. Los estatus laterales (en espera, parcial, fallida…) y el terminal de cancelación no son etapas: no avanzan la barra, la dejan donde iba y avisan.
/// </summary>
public static class OrderProgress
{
    /// <summary>En marcha por el camino normal.</summary>
    public const string InProgress = "IN_PROGRESS";
    /// <summary>Llegó a la última etapa (entregada): barra completa.</summary>
    public const string Completed = "COMPLETED";
    /// <summary>Estatus lateral (en espera, parcial, fallida…): la barra se queda donde iba y se muestra el aviso.</summary>
    public const string Alert = "ALERT";
    /// <summary>Cancelada (terminal que no es la entrega): barra vacía.</summary>
    public const string Cancelled = "CANCELLED";

    private const string PipelineKind = "PIPELINE";
    private const string TerminalKind = "TERMINAL";

    /// <summary>Las etapas del camino normal: los PIPELINE habilitados y el primer terminal habilitado (el de menor posición = «entregada»), por posición.</summary>
    public static IReadOnlyList<ProgressStatus> StepsOf(IEnumerable<ProgressStatus> all)
    {
        var enabled = all.Where(s => s.Enabled).ToList();
        var steps = enabled.Where(s => s.Kind == PipelineKind).ToList();
        var done = enabled.Where(s => s.Kind == TerminalKind).OrderBy(s => s.SortOrder).FirstOrDefault();
        if (done is not null) steps.Add(done);
        return steps.OrderBy(s => s.SortOrder).ToList();
    }

    /// <summary>
    /// El avance de una orden con ese estatus. <paramref name="previousCodes"/> son los estatus anteriores de su historial, del más reciente al más antiguo: sirven cuando el estatus actual no es una etapa
    /// (lateral o cancelado) para dejar la barra donde iba. Sin historial una orden detenida queda en la primera etapa.
    /// </summary>
    public static OrderProgressResult Compute(IReadOnlyList<ProgressStatus> steps, string? currentCode, string currentLabel, string? currentKind, IEnumerable<string>? previousCodes)
    {
        var total = steps.Count;
        var code = (currentCode ?? string.Empty).Trim();
        var idx = IndexOf(steps, code);
        if (idx >= 0) return new OrderProgressResult(idx + 1, total, idx == total - 1 ? Completed : InProgress, code, currentLabel);
        // no es una etapa: cancelada (terminal) o detenida (lateral o desconocida)
        if (string.Equals(currentKind, TerminalKind, StringComparison.OrdinalIgnoreCase)) return new OrderProgressResult(0, total, Cancelled, code, currentLabel);
        foreach (var previous in previousCodes ?? Array.Empty<string>())
        {
            var p = IndexOf(steps, previous);
            if (p >= 0) return new OrderProgressResult(p + 1, total, Alert, code, currentLabel);
        }
        return new OrderProgressResult(total == 0 ? 0 : 1, total, Alert, code, currentLabel);
    }

    private static int IndexOf(IReadOnlyList<ProgressStatus> steps, string? code)
    {
        for (var i = 0; i < steps.Count; i++)
            if (string.Equals(steps[i].Code, code?.Trim(), StringComparison.OrdinalIgnoreCase)) return i;
        return -1;
    }
}
