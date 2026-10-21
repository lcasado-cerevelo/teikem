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
    public const string AlreadyAssigned = "Ese documento ya está asignado a otra persona; pídale a un supervisor que lo reasigne.";

    /// <summary>¿Puede «tomarlo» quien lo pide? Sin asignar, o ya asignado a esa misma persona.</summary>
    public static bool CanTake(int? assignedTo, int? me) => me is not null && (assignedTo is null || assignedTo == me);
}
