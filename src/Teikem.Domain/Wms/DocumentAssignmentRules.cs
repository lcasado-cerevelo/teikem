namespace Teikem.Domain.Wms;

/// <summary>
/// Lote 171 (B2, 2026-10-21): asignar un DOCUMENTO de almacén a una persona —el aviso de llegada (a quien lo va a recibir) y el aviso de salida (a quien lo va a surtir)—, igual
/// que ya se asignan las tareas. Asignar es de supervisión (warehouse.manage); «tomar» un documento sin asignar lo hace cualquiera con el permiso de la operación (recibir o
/// recolectar). Un documento asignado a otra persona no se toma: lo reasigna un supervisor. Solo se asigna un documento abierto.
/// </summary>
public static class DocumentAssignmentRules
{
    public const string AsnNotOpen = "Solo se puede asignar un aviso de llegada que siga esperado.";
    public const string ExitNoticeNotOpen = "Solo se puede asignar un aviso de salida abierto (solicitado o parcial).";
    public const string PurchaseOrderNotOpen = "Solo se puede asignar una orden de compra enviada o recibida en parte.";
    public const string SalesOrderNotOpen = "Solo se puede asignar una orden de venta solicitada o despachada en parte.";
    public const string AlreadyAssigned = "Ese documento ya está asignado a otra persona; pídale a un supervisor que lo reasigne.";

    /// <summary>De dónde se tomó: la app móvil manda un origen corto (MOBILE_MY_TASKS, MOBILE_RECEIVE, MOBILE_DISPATCH, MOBILE_PUTAWAY); sin origen es la web.</summary>
    public static string TakeNote(string? origin) => origin?.Trim().ToUpperInvariant() switch
    {
        "MOBILE_MY_TASKS" => "Autoasignado al tomarlo desde la app móvil (Mis tareas).",
        "MOBILE_RECEIVE" => "Autoasignado al empezar a recibirlo desde la app móvil.",
        "MOBILE_DISPATCH" => "Autoasignado al empezar a despacharlo desde la app móvil.",
        "MOBILE_PUTAWAY" => "Autoasignado al empezar a acomodarlo desde la app móvil.",
        _ => "Autoasignado al tomarlo desde la web.",
    };

    /// <summary>¿Puede «tomarlo» quien lo pide? Sin asignar, o ya asignado a esa misma persona.</summary>
    public static bool CanTake(int? assignedTo, int? me) => me is not null && (assignedTo is null || assignedTo == me);
}
