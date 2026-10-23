using Teikem.Domain.Common;

namespace Teikem.Domain.Trips;

/// <summary>
/// 2026-10-23 (app del chofer) — un envío que el chofer NO aceptó al aceptar su manifiesto, con el motivo (lista «Motivos de no aceptación», editable en Listas) y una nota
/// opcional. El envío sale del manifiesto y vuelve a «Sin asignar»; la oficina lo ve aquí para volver a asignarlo. Solo se agrega (historial).
/// </summary>
[AuditEntity(TripOrderDeclineCodes.EntityType)]
public class TripOrderDecline : ITenantScoped
{
    public int TripOrderDeclineId { get; set; }
    [NotAudited] public Guid PublicId { get; set; } = Guid.NewGuid();
    public int TenantId { get; set; }
    public int TripId { get; set; }
    public int TransportOrderId { get; set; }
    public int? DriverId { get; set; }
    public int ReasonLookupId { get; set; }
    public string? Note { get; set; }
    public DateTime DeclinedAtUtc { get; set; } = DateTime.UtcNow;
    public int? DeclinedBy { get; set; }
}

public static class TripOrderDeclineCodes
{
    /// <summary>EntityType de la bitácora (sembrado en la sección 2026-10-23 de update.sql).</summary>
    public const string EntityType = "TRIP_ORDER_DECLINE";
    public const string ReasonDomain = "TripDeclineReason";
    public const int NoteMaxLength = 300;
}

/// <summary>Mensajes de la app del chofer (aceptar manifiesto). Los usa DriverAppService y se repiten tal cual en el manual y el FAQ.</summary>
public static class DriverAppRules
{
    public const string NotADriverMessage = "Su usuario no está vinculado a un chofer activo.";
    public const string ReasonRequiredMessage = "Indique el motivo de cada envío que no acepta.";
    public const string ReasonInvalidMessage = "El motivo no es válido.";
    public const string NoteTooLongMessage = "La nota no puede pasar de 300 caracteres.";
    public const string DuplicateShipmentMessage = "Un envío aparece más de una vez en la lista de los que no acepta.";
    public const string ShipmentNotInManifestMessage = "El envío no está en este manifiesto.";
    public static string AlreadyAcceptedMessage(string code) => $"El manifiesto {code} ya fue aceptado.";
    public static string NotReadyMessage(string code) => $"El manifiesto {code} no está listo para aceptar: debe estar despachado.";
    public static string DeclinedComment(string reason) => $"No aceptado por el chofer: {reason}";
    public const string AcceptedComment = "Aceptada por el chofer";

    // ---- entregar / no entregar desde la app ----
    public const int MaxPhotos = 3;
    public const string NotAcceptedMessage = "Acepte primero el manifiesto: todavía no ha salido.";
    public const string ShipmentNotYoursMessage = "El envío no está en ninguno de sus manifiestos.";
    public const string FailureReasonRequiredMessage = "Indique el motivo por el que no se entregó.";
    public const string FailureReasonInvalidMessage = "El motivo no es válido.";
    public const string FailureNoteTooLongMessage = "La nota no puede pasar de 400 caracteres.";
    public const string SignatureInvalidMessage = "La firma no es una imagen válida.";
    public const string PhotoInvalidMessage = "Una foto no es una imagen válida.";
    public static string TooManyPhotosMessage => $"Máximo {MaxPhotos} fotos por entrega.";
    public static string DeliveredComment(string driver) => $"Entregado desde la app por {driver}.";
    public static string FailedComment(string reason) => $"No entregado desde la app: {reason}";
}
