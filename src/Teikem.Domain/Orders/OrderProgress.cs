namespace Teikem.Domain.Orders;

/// <summary>Una etapa de la barra (código y texto en el idioma de quien mira).</summary>
public sealed record ProgressStatus(string Code, string Label, string Kind, int SortOrder, bool Enabled);

/// <summary>El avance de una orden: etapa actual (1 a 4; 0 = ninguna), total de etapas (4), estado, el código del estatus real y el texto que se muestra (Sometida, Recibida, En ruta, Intentada, Reprogramada o el estatus final).</summary>
public sealed record OrderProgressResult(int Step, int Total, string State, string StatusCode, string StatusLabel);

/// <summary>
/// La <b>barra de progreso</b> de quien espera una orden —el cliente, el consignatario y la página pública «¿Cómo va mi orden?»—. Decidido con Luis (2026-10-17, reemplaza el criterio del Lote 103 de «tantas etapas como
/// estatus»): <b>solo cuatro etapas</b> y todos los demás estatus se encajonan en una de ellas:
/// 1) <b>Sometida</b> (SUBMITTED); 2) <b>Recibida</b> (recogida, sin asignar y asignada: ya la tiene la compañía); 3) <b>En ruta</b> (En tránsito: la tiene el chofer), donde también caen <b>Intentada</b> (Intento fallido,
/// en alerta) y <b>Reprogramada</b> (volvió al almacén tras un intento y está Sin asignar o Asignada); 4) <b>Completada</b>: cualquier estatus final, y el texto es el del estatus final que sea (Entregada, Cancelada, Devuelta…).
/// Cancelada pinta la barra apagada. Los nombres de las etapas no dependen de los estatus de la compañía.
/// </summary>
public static class OrderProgress
{
    /// <summary>En marcha por el camino normal.</summary>
    public const string InProgress = "IN_PROGRESS";
    /// <summary>Llegó a la última etapa con un estatus final bueno (entregada, en almacén…): barra completa.</summary>
    public const string Completed = "COMPLETED";
    /// <summary>Intento fallido: la barra se queda en «En ruta» y se muestra el aviso.</summary>
    public const string Alert = "ALERT";
    /// <summary>Cancelada: barra apagada.</summary>
    public const string Cancelled = "CANCELLED";

    public const string StageSubmitted = "SUBMITTED";
    public const string StageReceived = "RECEIVED";
    public const string StageOnRoute = "ON_ROUTE";
    public const string StageDone = "DONE";
    public const int TotalSteps = 4;

    private const string TerminalKind = "TERMINAL";

    private static bool English(string? lang) => string.Equals(lang, "en", StringComparison.OrdinalIgnoreCase);

    /// <summary>Las cuatro etapas de la barra, con su texto en el idioma pedido (es por omisión).</summary>
    public static IReadOnlyList<ProgressStatus> Steps(string? lang = null)
    {
        var en = English(lang);
        return new[]
        {
            new ProgressStatus(StageSubmitted, en ? "Submitted" : "Sometido", "PIPELINE", 1, true),
            new ProgressStatus(StageReceived, en ? "Received" : "Recibida", "PIPELINE", 2, true),
            new ProgressStatus(StageOnRoute, en ? "On route" : "En ruta", "PIPELINE", 3, true),
            new ProgressStatus(StageDone, en ? "Completed" : "Completada", "PIPELINE", 4, true),
        };
    }

    /// <summary>
    /// El avance de una orden con ese estatus. <paramref name="hasFailedAttempt"/> = su historial tiene un Intento fallido: si ahora está Sin asignar o Asignada, es «Reprogramada» (sigue en la etapa 3).
    /// <paramref name="currentLabel"/> es el texto del estatus real (se usa para los estatus finales).
    /// </summary>
    public static OrderProgressResult Compute(string? currentCode, string currentLabel, string? currentKind, bool hasFailedAttempt, string? lang = null)
    {
        var en = English(lang);
        var code = (currentCode ?? string.Empty).Trim();
        OrderProgressResult R(int step, string state, string label) => new(step, TotalSteps, state, code, label);

        if (string.Equals(currentKind, TerminalKind, StringComparison.OrdinalIgnoreCase))
            return R(4, Same(code, "CANCELLED") ? Cancelled : Completed, currentLabel);

        if (Same(code, "SUBMITTED")) return R(1, InProgress, en ? "Submitted" : "Sometido");
        if (Same(code, "IN_TRANSIT")) return R(3, InProgress, en ? "On route" : "En ruta");
        if (Same(code, "FAILED")) return R(3, Alert, en ? "Attempted" : "Intentada");
        if (hasFailedAttempt && (Same(code, "UNASSIGNED") || Same(code, "ASSIGNED"))) return R(3, InProgress, en ? "Rescheduled" : "Reprogramada");
        if (Same(code, "PICKUP") || Same(code, "UNASSIGNED") || Same(code, "ASSIGNED")) return R(2, InProgress, en ? "Received" : "Recibida");

        // un estatus propio de la compañía (lateral u otro): la barra lo deja en «Recibida» con su aviso
        return R(2, Alert, currentLabel);
    }

    private static bool Same(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
