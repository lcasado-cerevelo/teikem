using Teikem.Domain.Constants;

namespace Teikem.Domain.Trips;

/// <summary>
/// Lote 107 — la estación de escaneo con ACCIÓN escogida por el usuario (decidido con Luis, 2026-10-17): el usuario escoge qué va a hacer (recoger, llegó, enrutar o salir) y esa acción decide a qué estatus lleva lo que
/// se escanea; no se escanea varias veces para recorrer los estatus: el estatus se brinca y los de en medio quedan registrados «brincados» con la misma fecha y hora. Si la orden ya está más adelante no se hace nada; en un estatus final
/// tampoco (reabrir exige el permiso orders.reopen). Nada de esto revisa tarifa, crédito ni cliente: eso se revisó al someter la orden.
/// </summary>
public static class ScanActions
{
    public const string Pickup = "PICKUP";   // Recoger: quien recoge (chofer o guarda) → Recogida
    public const string Arrive = "ARRIVE";   // Llegó: entró al almacén (guarda) → Sin asignar
    public const string Route = "ROUTE";     // Enrutar: puesta en una ruta (guarda) → Asignada
    public const string Depart = "DEPART";   // Salir: el chofer la acepta y sale (chofer) → En tránsito

    public static readonly string[] All = { Pickup, Arrive, Route, Depart };

    public const string InvalidActionMessage = "Escoja la acción: recoger, llegó, enrutar o salir.";

    /// <summary>Acción normalizada (sin distinguir mayúsculas) o null si no es una de las cuatro.</summary>
    public static string? Normalize(string? raw)
    {
        var a = raw?.Trim().ToUpperInvariant();
        return a is not null && All.Contains(a) ? a : null;
    }

    /// <summary>Estatus de la orden al que lleva cada acción.</summary>
    public static string TargetStatus(string action) => action switch
    {
        Pickup => OrderStatuses.Pickup,
        Arrive => OrderStatuses.Unassigned,
        Route => OrderStatuses.Assigned,
        Depart => OrderStatuses.InTransit,
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Acción de escaneo desconocida."),
    };
}

/// <summary>Resultado de aplicar una acción a lo escaneado.</summary>
public static class ScanActionOutcomes
{
    public const string Applied = "APPLIED";                 // el estatus cambió (con los de en medio brincados, si los hubo)
    public const string AlreadyBeyond = "ALREADY_BEYOND";    // la orden ya está en ese estatus o más adelante: no se hace nada
    public const string Final = "FINAL";                     // estatus final (entregada, cancelada…): se ignora; reabrir exige permiso
    public const string NoRoute = "NO_ROUTE";                // Enrutar sin una ruta abierta que reciba la orden: no se cambia nada
    public const string NotFound = "NOT_FOUND";
    public const string Ambiguous = "AMBIGUOUS";
}
