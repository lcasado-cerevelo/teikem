using Teikem.Domain.Common;

namespace Teikem.Domain.Orders;

/// <summary>
/// Lote 111 — reclamación REC-#####: junta las piezas PERDIDAS de una orden (dictado por Luis, 2026-10-18). El camino de una pieza faltante: «Por verificar» (Lote 110) → «Perdido» → «Reclamada» (entra a una reclamación) → «Resuelta».
/// La reclamación lleva orden, línea(s), piezas, descripción, valor declarado y notas; se resuelve (pagada, rechazada o cerrada) y queda Resuelta. Una reclamación sin cerrar que pierde todas sus piezas (porque aparecieron) se da de baja.
/// Una vez Resuelta, una pieza suya que se escanea responde «No».
/// </summary>
[AuditEntity("CLAIM")]
public class Claim : ITenantScoped
{
    public int ClaimId { get; set; }
    [NotAudited] public Guid PublicId { get; set; }
    public int TenantId { get; set; }
    /// <summary>REC-00001 (el id con ceros): único por compañía.</summary>
    public string Number { get; set; } = string.Empty;
    public int TransportOrderId { get; set; }
    public int ClientId { get; set; }
    public int StatusCodeId { get; set; }
    /// <summary>Qué se reclama: «2 × Caja; 1 × Sobre» (de las líneas de las piezas).</summary>
    public string Description { get; set; } = string.Empty;
    public decimal? DeclaredValue { get; set; }
    public string? Notes { get; set; }
    /// <summary>PAID, REJECTED o CLOSED cuando se resuelve.</summary>
    public string? Resolution { get; set; }
    public decimal? AmountPaid { get; set; }
    public string? ResolutionNotes { get; set; }
    public bool IsActive { get; set; } = true;
    [NotAudited] public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public int? CreatedBy { get; set; }
    public DateTime? ResolvedAtUtc { get; set; }
    public int? ResolvedBy { get; set; }
    [NotAudited] public byte[]? RowVersion { get; set; }

    public TransportOrder? Order { get; set; }
    public Teikem.Domain.Catalogs.StatusCode? Status { get; set; }
}

public static class ClaimStatuses
{
    public const string Domain = "ClaimStatus";
    public const string EntityType = "CLAIM";
    public const string Claimed = "CLAIMED";
    public const string Resolved = "RESOLVED";
}

public static class ClaimResolutions
{
    public const string Paid = "PAID";
    public const string Rejected = "REJECTED";
    public const string Closed = "CLOSED";
    public static readonly string[] All = { Paid, Rejected, Closed };

    public static string? Normalize(string? raw)
    {
        var r = raw?.Trim().ToUpperInvariant();
        return r is not null && All.Contains(r) ? r : null;
    }
}

public static class ClaimRules
{
    public const int NotesMax = 1000;
    public const string NoPiecesMessage = "Indique al menos una pieza.";
    public const string PieceNotToVerifyMessage = "La pieza {0} no está «Por verificar».";
    public const string PieceNotLostMessage = "La pieza {0} no está «Perdido»: márquela como perdida antes de reclamarla.";
    public const string NoLostPiecesMessage = "El envío no tiene piezas perdidas por reclamar.";
    public const string PieceNotOfOrderMessage = "La pieza {0} no es de ese envío.";
    public const string NegativeValueMessage = "El valor declarado no puede ser negativo.";
    public const string NotesTooLongMessage = "Las notas no pueden pasar de 1000 caracteres.";
    public const string InvalidResolutionMessage = "Indique la resolución: pagada, rechazada o cerrada.";
    public const string AmountRequiredMessage = "Indique el monto pagado.";
    public const string AlreadyResolvedMessage = "La reclamación ya está resuelta.";
    public const string ClosedClaimPieceMessage = "La pieza pertenece a una reclamación cerrada.";

    /// <summary>REC-00012 a partir del id.</summary>
    public static string NumberOf(int claimId) => "REC-" + claimId.ToString("00000", System.Globalization.CultureInfo.InvariantCulture);
}
