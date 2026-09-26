using System.Globalization;
using Teikem.Domain.Constants;

namespace Teikem.Domain.Wms;

/// <summary>
/// Lote 6 (P5) — reglas puras de la cola unificada de tareas de almacén (R17, D41). Los mensajes son públicos porque el
/// manual y la FAQ los citan literalmente.
/// El permiso por tipo YA NO vive aquí: lo declara cada IWarehouseTaskHandler (RequiredPermission), y un tipo sin handler
/// (PICK, PACK, LOAD) responde 422 NoHandler hasta que su lote registre el suyo.
/// </summary>
public static class WarehouseTaskRules
{
    /// <summary>Tope de filas por página de la cola (igual que InventoryRules.MaxPageSize).</summary>
    public const int MaxPageSize = 200;
    public const int DefaultPageSize = 100;
    public const int MaxSuggestions = 10;
    public const int DefaultSuggestions = 3;

    public static string NoHandler(string taskType) => $"Las tareas de tipo {taskType} no se completan desde la cola.";
    public const string TaskNotOpen = "La tarea ya fue completada o cancelada.";
    public const string AssigneeNotMember = "El usuario no es miembro activo de la compañía.";
    public const string QtyExceeds = "La cantidad excede la de la tarea.";
    public const string DestinationRequired = "Indique la posición de destino.";

    // Mensajes adicionales de la cola (también citados por el manual).
    public const string QtyPositive = "La cantidad debe ser mayor que cero.";
    public const string QtyDecimals = "La cantidad admite como máximo 3 decimales.";
    public const string SameBin = "La posición de destino debe ser distinta de la de origen.";
    public const string OriginMissing = "La tarea no tiene posición de origen; no se puede completar.";
    public const string QuantityMissing = "La tarea no tiene cantidad; no se puede completar desde la cola.";
    public const string SerialQtyInteger = "En productos con serie la cantidad debe ser entera.";
    public static string SerialCountMismatch(decimal qty) =>
        $"Indique exactamente {FormatQty(qty)} número(s) de serie.";
    public const string SerialTooLong = "Cada número de serie admite como máximo 80 caracteres.";
    public const string SerialDuplicated = "Hay números de serie repetidos.";
    public const string SerialNotOfTask = "La tarea es de una serie específica; no indique otras series.";
    public const string AlreadyInProgress = "La tarea ya está en proceso.";
    public static string CancelNotFromQueue(string taskType) =>
        $"Las tareas de tipo {taskType} se cancelan desde su pantalla, no desde la cola.";
    public const string SuggestionInput = "Indique la tarea (taskId) o el producto (productPublicId) para sugerir la posición.";

    /// <summary>Estatus abiertos de una tarea (los que admiten asignar, iniciar, completar y cancelar).</summary>
    public static readonly IReadOnlyList<string> OpenStatuses = new[] { WarehouseTaskStatuses.Pending, WarehouseTaskStatuses.InProgress };

    public static bool IsOpen(string? statusCode) =>
        OpenStatuses.Any(s => string.Equals(s, statusCode, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Tipos que se cancelan desde la cola: sus tareas no arrastran efectos que haya que deshacer (la mercancía se queda
    /// donde está). COUNT se cancela al eliminar el conteo y CROSSDOCK al cancelar la asignación (libera la reserva).
    /// </summary>
    public static readonly IReadOnlyList<string> CancelableFromQueue = new[] { WarehouseTaskTypes.Putaway, WarehouseTaskTypes.Replenish };

    public static bool IsCancelableFromQueue(string? taskType) =>
        CancelableFromQueue.Any(t => string.Equals(t, taskType, StringComparison.OrdinalIgnoreCase));

    /// <summary>Remanente que queda como tarea nueva al completar por menos de lo indicado (0 si se completó todo).</summary>
    public static decimal Split(decimal taskQty, decimal completedQty) => Math.Max(0m, taskQty - completedQty);

    /// <summary>
    /// Cantidad a completar: la indicada o, si se omite, la de la tarea. Valida &gt; 0, máximo 3 decimales y que no exceda
    /// la de la tarea. Devuelve (cantidad, error) con el error listo para un 400.
    /// </summary>
    public static (decimal Quantity, string? Error) CompletionQuantity(decimal? taskQty, decimal? requested)
    {
        if (taskQty is not decimal tq || tq <= 0m) return (0m, QuantityMissing);
        var q = requested ?? tq;
        if (q <= 0m) return (0m, QtyPositive);
        if (decimal.Round(q, 3) != q) return (0m, QtyDecimals);
        if (q > tq) return (0m, QtyExceeds);
        return (q, null);
    }

    /// <summary>
    /// Series de una tarea de producto con serie: cantidad entera, tantas series como unidades (sin repetir, sin distinguir
    /// mayúsculas, máximo 80 caracteres). Si la tarea ya es de una serie, esa es la única válida (se toma aunque no se indique).
    /// </summary>
    public static (IReadOnlyList<string> Serials, string? Error) SerialsForCompletion(decimal qty, string? taskSerial, IReadOnlyList<string>? requested)
    {
        if (decimal.Truncate(qty) != qty) return (Array.Empty<string>(), SerialQtyInteger);
        var list = (requested ?? Array.Empty<string>())
            .Select(s => (s ?? string.Empty).Trim()).Where(s => s.Length > 0).ToList();
        if (list.Any(s => s.Length > 80)) return (Array.Empty<string>(), SerialTooLong);
        if (list.Distinct(StringComparer.OrdinalIgnoreCase).Count() != list.Count) return (Array.Empty<string>(), SerialDuplicated);

        if (!string.IsNullOrWhiteSpace(taskSerial))
        {
            if (list.Count == 0) list.Add(taskSerial.Trim());
            if (list.Count != 1 || !string.Equals(list[0], taskSerial.Trim(), StringComparison.OrdinalIgnoreCase))
                return (Array.Empty<string>(), SerialNotOfTask);
        }
        if (list.Count != (int)qty) return (Array.Empty<string>(), SerialCountMismatch(qty));
        return (list, null);
    }

    /// <summary>Destino de la tarea: el indicado al completar o el sugerido en la tarea; debe existir y ser distinto del origen.</summary>
    public static (int? BinId, string? Error) Destination(int? requestedBinId, int? taskToBinId, int? fromBinId)
    {
        var to = requestedBinId ?? taskToBinId;
        if (to is null) return (null, DestinationRequired);
        if (fromBinId is int f && f == to) return (null, SameBin);
        return (to, null);
    }

    /// <summary>Orden de la cola: prioridad ascendente (1 = más urgente), luego la más antigua primero; desempate por id.</summary>
    public static IOrderedEnumerable<T> QueueOrder<T>(IEnumerable<T> tasks, Func<T, int> priority, Func<T, DateTime> createdAtUtc, Func<T, int> id)
        => tasks.OrderBy(priority).ThenBy(createdAtUtc).ThenBy(id);

    /// <summary>Antigüedad en horas (a 1 decimal) hasta el cierre o, si sigue abierta, hasta ahora.</summary>
    public static decimal AgeHours(DateTime createdAtUtc, DateTime? completedAtUtc, DateTime nowUtc)
    {
        var end = completedAtUtc ?? nowUtc;
        var hours = (decimal)(end - createdAtUtc).TotalHours;
        return decimal.Round(Math.Max(0m, hours), 1, MidpointRounding.AwayFromZero);
    }

    /// <summary>Página acotada: skip ≥ 0; take 1..200 (0 o negativo = 100).</summary>
    public static (int Skip, int Take) Page(int skip, int take)
        => (Math.Max(0, skip), take <= 0 ? DefaultPageSize : Math.Min(take, MaxPageSize));

    private static string FormatQty(decimal q) => q.ToString("0.###", CultureInfo.InvariantCulture);
}
