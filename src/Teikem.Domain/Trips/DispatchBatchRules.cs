using System.Globalization;

namespace Teikem.Domain.Trips;

/// <summary>Resumen de un despacho en lote: cuántas rutas se pidieron, cuántas se despacharon y cuántas no.</summary>
public sealed record DispatchBatchSummary(int Requested, int Dispatched, int Failed);

/// <summary>
/// Lote 5 (P4) — reglas puras del despacho y la salida de rutas. Los mensajes exactos viven aquí como 'public const' o
/// funciones puras porque el manual funcional y la FAQ los citan.
/// - Validate: selección del despacho en lote (al menos una ruta, máximo 50, sin repetidos).
/// - Summarize: totales del resultado del lote.
/// - Mensajes de salida (TripLifecycleService), del pipeline incompleto y de los efectos de TripStatus/OrderStatus.
/// </summary>
public static class DispatchBatchRules
{
    /// <summary>Tope técnico de rutas por despacho en lote.</summary>
    public const int MaxTripsPerBatch = 50;

    public const string EmptySelectionMessage = "Seleccione al menos una ruta.";
    public const string TooManyTripsMessage = "Máximo 50 rutas por despacho.";

    /// <summary>Ítem del lote cuyo id no es una ruta del tenant (mismo texto que el 404 de la ficha).</summary>
    public const string TripNotFoundMessage = "Ruta no encontrada.";

    /// <summary>Comentario por defecto de la salida (TripStatus → IN_PROGRESS).</summary>
    public const string DefaultStartComment = "Salida registrada";

    /// <summary>Comentario por defecto del despacho (TripStatus → DISPATCHED).</summary>
    public const string DefaultDispatchComment = "Ruta despachada";

    /// <summary>Comentario de los pasos intermedios del Trip al despachar (p. ej. DRAFT → PLANNED).</summary>
    public const string DispatchStepComment = "Paso automático al despachar";

    /// <summary>Comentario de la versión de ruta al despacharse (→ ACTIVE) cuando ya estaba optimizada.</summary>
    public const string RouteDispatchedComment = "Despachada";

    /// <summary>Comentario de la versión de ruta al despacharse sin haberse optimizado nunca (DRAFT → OPTIMIZED → ACTIVE).</summary>
    public const string ManualSequenceComment = "Secuencia manual confirmada al despachar";

    /// <summary>Comentario de la versión de ruta archivada al eliminar la ruta.</summary>
    public const string RouteArchivedOnDeleteComment = "Ruta eliminada";

    // Invariantes de última línea del efecto de despacho (el servicio ya los reporta como bloqueantes antes de transicionar).
    public const string NoDriverMessage = "La ruta no tiene chofer asignado.";
    public const string NoVehicleMessage = "La ruta no tiene vehículo asignado.";
    public const string NoStopsMessage = "La ruta no tiene paradas.";

    /// <summary>
    /// Selección del despacho en lote: colapsa duplicados conservando el orden de llegada. Error si está vacía o si pasa de 50.
    /// </summary>
    public static (IReadOnlyList<Guid> Ids, string? Error) Validate(IEnumerable<Guid>? ids)
    {
        var distinct = (ids ?? Array.Empty<Guid>()).Where(g => g != Guid.Empty).Distinct().ToList();
        if (distinct.Count == 0) return (distinct, EmptySelectionMessage);
        if (distinct.Count > MaxTripsPerBatch) return (distinct, TooManyTripsMessage);
        return (distinct, null);
    }

    /// <summary>Totales del lote a partir del resultado por ruta (true = despachada).</summary>
    public static DispatchBatchSummary Summarize(IEnumerable<bool> dispatched)
    {
        var list = (dispatched ?? Array.Empty<bool>()).ToList();
        var ok = list.Count(d => d);
        return new DispatchBatchSummary(list.Count, ok, list.Count - ok);
    }

    /// <summary>'La ruta {código} no está despachada; despáchela antes de registrar su salida.'</summary>
    public static string NotDispatchedForStart(string tripCode)
        => $"La ruta {tripCode} no está despachada; despáchela antes de registrar su salida.";

    /// <summary>'La ruta {código} ya salió.'</summary>
    public static string AlreadyStarted(string tripCode) => $"La ruta {tripCode} ya salió.";

    /// <summary>'El pipeline de rutas de esta compañía no tiene habilitada la etapa {stage}.'</summary>
    public static string PipelineMissing(string stage) => $"El pipeline de rutas de esta compañía no tiene habilitada la etapa {stage}.";

    /// <summary>'La ruta {código} no se puede despachar: {m1}; {m2}.' (cada motivo sin su punto final).</summary>
    public static string DispatchBlocked(string tripCode, IEnumerable<string> messages)
    {
        var parts = (messages ?? Array.Empty<string>())
            .Where(m => !string.IsNullOrWhiteSpace(m))
            .Select(m => m.Trim().TrimEnd('.'))
            .Distinct()
            .ToList();
        return string.Create(CultureInfo.InvariantCulture, $"La ruta {tripCode} no se puede despachar: {string.Join("; ", parts)}.");
    }

    /// <summary>'Orden {número}: {motivo}' (orden no elegible al despachar).</summary>
    public static string OrderNotEligible(string orderNumber, string reason) => $"Orden {orderNumber}: {reason}";

    /// <summary>Comentario del historial de cada orden al despacharse la ruta.</summary>
    public static string OrderDispatchedComment(string tripCode) => $"Despachada en la ruta {tripCode}";

    /// <summary>Comentario del historial de cada orden al salir la ruta.</summary>
    public static string OrderStartedComment(string tripCode) => $"Salió en la ruta {tripCode}";

    /// <summary>'Eliminar ruta' desde una etapa distinta de DRAFT/PLANNED.</summary>
    public static string CannotDeleteDispatched(string tripCode) => $"La ruta {tripCode} ya fue despachada; no se puede eliminar.";

    /// <summary>Cancelar una orden que va en una ruta despachada o en curso.</summary>
    public static string CannotCancelOrderInDispatchedTrip(string tripCode)
        => $"La orden va en la ruta {tripCode} ya despachada; no se puede cancelar mientras la ruta esté en curso.";
}
