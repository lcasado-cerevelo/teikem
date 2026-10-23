using Teikem.Domain.Common;

namespace Teikem.Domain.Fleet;

/// <summary>Constantes del registro de intentos de entrega (Lote 93, P9).</summary>
public static class DeliveryAttemptCodes
{
    /// <summary>EntityType de la bitácora del intento (sembrado en la sección 2026-10-17 (ad) de update.sql).</summary>
    public const string EntityType = "DELIVERY_ATTEMPT";
    /// <summary>Catálogo de razones de fallo (ya existe en el seed original).</summary>
    public const string FailureReasonDomain = "DeliveryFailureReason";
}

/// <summary>De dónde salió el intento: del cambio de estatus de la orden (Entregada o Fallida), escrito a mano por la oficina o, más adelante, de la app del chofer.</summary>
public static class DeliveryAttemptSources
{
    public const string Status = "STATUS";
    public const string Manual = "MANUAL";
    public const string App = "APP";
    public static readonly string[] All = { Status, Manual, App };
}

/// <summary>
/// Intento de entrega de una orden: una fila por intento (1, 2, 3…), con el chofer que lo hizo y si se entregó. Es la fuente de verdad de la liquidación a choferes (y, a futuro, del cargo por intento
/// al cliente): nada se paga fuera de este registro. A lo sumo un intento entregado por orden y debe ser el último. Un intento no se borra: se anula (IsActive 0, con motivo) y solo mientras no esté en
/// una liquidación (SettledRunId nulo).
/// </summary>
[AuditEntity(DeliveryAttemptCodes.EntityType)]
public class DeliveryAttempt : ITenantScoped
{
    public int DeliveryAttemptId { get; set; }
    [NotAudited] public Guid PublicId { get; set; }
    public int TenantId { get; set; }
    public int TransportOrderId { get; set; }
    public int AttemptNumber { get; set; }
    public int? DriverId { get; set; }
    public int? TripId { get; set; }
    public DateTime AttemptedAtUtc { get; set; }
    public bool Delivered { get; set; }
    public int? FailureReasonLookupId { get; set; }
    public string? Notes { get; set; }
    /// <summary>App del chofer: dónde estaba el teléfono al marcar «no entregado» y a cuántos metros de la dirección (null = sin GPS). Sirve para ver si el chofer marcó la excepción lejos del lugar.</summary>
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }
    public int? DistanceFromStopMeters { get; set; }
    public string Source { get; set; } = DeliveryAttemptSources.Manual;
    public int? RecordedBy { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime? VoidedAtUtc { get; set; }
    public int? VoidedBy { get; set; }
    public string? VoidReason { get; set; }
    /// <summary>La corrida de liquidación que lo pagó (Lote 94); nulo = todavía sin liquidar.</summary>
    public int? SettledRunId { get; set; }
    [NotAudited] public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>Reglas puras de los intentos de entrega.</summary>
public static class DeliveryAttemptRules
{
    public const int MaxNotesLength = 400;
    public const int MaxVoidReasonLength = 400;

    public const string DriverRequired = "Indique el chofer del intento.";
    public const string OrderRequired = "Indique el envío del intento.";
    public const string NotesTooLong = "La nota no puede pasar de 400 caracteres.";
    public const string ReasonRequired = "Indique el motivo de la anulación.";
    public const string ReasonTooLong = "El motivo no puede pasar de 400 caracteres.";
    public const string DeliveredWithReason = "Un intento entregado no lleva razón de fallo.";
    public const string FutureAttempt = "La fecha del intento no puede ser futura.";
    public const string SpecialDelivery = "Es una entrega especial: se paga por viaje, no por intentos.";
    public const string AlreadySettled = "El intento ya está en una liquidación y no se puede anular.";
    public const string AlreadyVoided = "El intento ya está anulado.";
    public static string DuplicateNumber(int n) => $"El envío ya tiene un intento número {n}.";
    public static string UnknownReason(string code) => $"La razón de fallo '{code}' no existe.";

    /// <summary>El número del siguiente intento: el mayor vigente más uno (1 si no hay).</summary>
    public static int NextNumber(IEnumerable<int> activeNumbers)
    {
        var max = 0;
        foreach (var n in activeNumbers) if (n > max) max = n;
        return max + 1;
    }

    /// <summary>
    /// Valida la secuencia que quedaría con el intento nuevo (los números vigentes de la orden más el nuevo): sin repetir, a lo sumo un entregado y que sea el último. Devuelve el mensaje o null.
    /// Es la misma regla del motor de pago (DriverPayoutRules.ValidateAttempts), para que lo que se registre siempre se pueda liquidar.
    /// </summary>
    public static string? ValidateSequence(IReadOnlyCollection<PayoutAttempt> existing, PayoutAttempt added)
    {
        if (existing.Any(a => a.Number == added.Number)) return DuplicateNumber(added.Number);
        return DriverPayoutRules.ValidateAttempts(existing.Append(added).ToList());
    }
}
