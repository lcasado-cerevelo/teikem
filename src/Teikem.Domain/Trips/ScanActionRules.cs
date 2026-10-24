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
    /// <summary>Lote 112: Devolver (permiso orders.return): la pieza escaneada viaja en la orden de vuelta y la orden queda «Devuelta». No está en el camino de las cuatro acciones (no tiene lugar en el orden).</summary>
    public const string Return = "RETURN";

    public static readonly string[] All = { Pickup, Arrive, Route, Depart };

    public const string PieceNotFoundMessage = "No se encontró la pieza.";   // el número del envío no se escanea: lo que se escanea son las piezas
    public static string BeyondMessage(string piece, string order, string currentLabel, string targetLabel)
        => $"La pieza {piece}: el envío {order} ya está en «{currentLabel}», que va después de «{targetLabel}». Si sigue, el envío regresará a «{targetLabel}».";
    public const string ScopeRoute = "ROUTE";
    public const string ScopeCity = "CITY";
    public const string ScopeDriver = "DRIVER";
    public static string NotInScopeMessage(string piece, string order, string mode)
        => mode == ScopeRoute ? $"La pieza {piece} (envío {order}) no es de las rutas escogidas."
         : mode == ScopeCity ? $"La pieza {piece} (envío {order}) no es de las ciudades escogidas."
         : $"La pieza {piece} (envío {order}) no es de los choferes escogidos.";
    public static string MovedBackMessage(string piece, string order, string fromLabel, string toLabel)
        => $"La pieza {piece} regresó el envío {order} de «{fromLabel}» a «{toLabel}».";
    public static string MovedBackComment(string piece, string toLabel) => $"Regresado a «{toLabel}» al escanear la pieza {piece}";
    public const string InvalidActionMessage = "Escoja la acción: recoger, llegó, enrutar, salir o devolver.";

    /// <summary>Acción normalizada (sin distinguir mayúsculas) o null si no es una de las cuatro.</summary>
    public static string? Normalize(string? raw)
    {
        var a = raw?.Trim().ToUpperInvariant();
        return a is not null && (All.Contains(a) || a == Return) ? a : null;
    }

    /// <summary>Orden de las acciones en el camino de la orden (Recoger 1, Llegó 2, Enrutar 3, Salir 4).</summary>
    public static int Rank(string action) => Array.IndexOf(All, action) + 1;

    /// <summary>La acción cuyo estatus es ese (Recogida → Recoger…); null si el estatus no es el de ninguna acción.</summary>
    public static string? ForStatus(string? statusCode)
        => string.Equals(statusCode, OrderStatuses.InWarehouse, StringComparison.OrdinalIgnoreCase)
            ? Arrive   // Lote 114: «En almacén» (el final del pick up) es el estatus de «Llegó» para el conteo
            : All.FirstOrDefault(a => string.Equals(TargetStatus(a), statusCode, StringComparison.OrdinalIgnoreCase));

    /// <summary>Estatus de la orden al que lleva cada acción.</summary>
    public static string TargetStatus(string action) => action switch
    {
        Pickup => OrderStatuses.Pickup,
        Arrive => OrderStatuses.Unassigned,
        Route => OrderStatuses.Assigned,
        Depart => OrderStatuses.InTransit,
        Return => OrderStatuses.Pickup,   // el estatus de la ORDEN DE VUELTA que nace con lo escaneado (la original queda Devuelta)
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
    /// <summary>Lote 110: la pieza se contó para esa acción sin mover la orden (la orden ya estaba en ese estatus o más adelante).</summary>
    public const string Counted = "COUNTED";
    /// <summary>Lote 110: esa pieza ya se había escaneado con esa acción.</summary>
    public const string Duplicate = "DUPLICATE";
    /// <summary>Lote 111: una pieza perdida o reclamada apareció: se quitó de su reclamación y su orden nueva se crea al cerrar el conteo.</summary>
    public const string Reappeared = "REAPPEARED";
    /// <summary>
    /// Lote 216: el envío de la pieza ya va MÁS ADELANTE que el estatus de la acción. El sistema PARA y advierte (no cuenta nada); si la persona confirma (ConfirmBeyond) el envío REGRESA a ese estatus —escanear hacia atrás es la
    /// única forma de regresarlo—: sale de la ruta si el estatus es anterior a Asignada y se borran los escaneos de acciones posteriores de sus piezas, para poder volver a avanzar escaneando.
    /// </summary>
    public const string Beyond = "BEYOND";
    /// <summary>Lote 216: la pieza existe pero su envío no es de lo escogido (otra ruta, otra ciudad u otro chofer): «No».</summary>
    public const string NotInScope = "NOT_IN_SCOPE";
    /// <summary>Lote 112: la pieza escaneada con «Devolver» viaja en la orden de vuelta (la orden queda «Devuelta»).</summary>
    public const string Returned = "RETURNED";
}
