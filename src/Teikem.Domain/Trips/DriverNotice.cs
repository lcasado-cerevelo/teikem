using Teikem.Domain.Common;

namespace Teikem.Domain.Trips;

public static class DriverNoticeCodes
{
    /// <summary>EntityType de la bitácora (sembrado en la sección 2026-10-23 (d) de update.sql).</summary>
    public const string EntityType = "DRIVER_NOTICE";
    public const string ManifestDispatched = "MANIFEST_DISPATCHED";
    public const string Message = "MESSAGE";
    /// <summary>Lote 215: la oficina le agregó o le quitó una parada a una ruta ya despachada (asignar en la calle).</summary>
    public const string StopAdded = "STOP_ADDED";
    public const string StopRemoved = "STOP_REMOVED";
    public const int TitleMaxLength = 120;
    public const int BodyMaxLength = 1000;
    /// <summary>Los avisos se conservan este tiempo y luego se purgan.</summary>
    public const int RetentionDays = 60;
    public const int MaxListed = 100;
}

/// <summary>
/// Un aviso para el chofer en su app (bandeja de avisos): lo escribe la oficina o lo genera el sistema (p. ej. «Nuevo manifiesto» al despacharle una ruta). Se marca leído desde la app.
/// El aviso emergente (push) llega más adelante; esta bandeja es la fuente.
/// </summary>
[AuditEntity(DriverNoticeCodes.EntityType)]
public class DriverNotice : ITenantScoped
{
    public int DriverNoticeId { get; set; }
    [NotAudited] public Guid PublicId { get; set; } = Guid.NewGuid();
    public int TenantId { get; set; }
    public int DriverId { get; set; }
    public string Kind { get; set; } = DriverNoticeCodes.Message;
    public string Title { get; set; } = string.Empty;
    public string? Body { get; set; }
    public int? TripId { get; set; }
    public int? CreatedBy { get; set; }
    [NotAudited] public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    [NotAudited] public DateTime? ReadAtUtc { get; set; }
    /// <summary>Cuándo se mandó el aviso emergente al teléfono (null = pendiente de mandar).</summary>
    [NotAudited] public DateTime? PushedAtUtc { get; set; }
}

public static class DriverNoticeRules
{
    public const string TitleRequiredMessage = "Indique el título del aviso.";
    public const string TitleTooLongMessage = "El título no puede pasar de 120 caracteres.";
    public const string BodyTooLongMessage = "El texto no puede pasar de 1000 caracteres.";
    public static string ManifestDispatchedTitle(string code) => $"Nuevo manifiesto {code}";
    public static string PickupDispatchedTitle(string code) => $"Nueva ruta de recogida {code}";
    public static string StopAddedTitle(string tripCode) => $"Nueva parada en {tripCode}";
    public static string StopAddedBody(string order, string? consignee) => string.IsNullOrWhiteSpace(consignee) ? $"Se agregó el envío {order} a su ruta. Revísela en la app." : $"Se agregó el envío {order} ({consignee}) a su ruta. Revísela en la app.";
    public static string StopRemovedTitle(string tripCode) => $"Parada quitada de {tripCode}";
    public static string StopRemovedBody(string order) => $"Se sacó el envío {order} de su ruta. Ya no tiene que atenderlo.";
    public const string PickupDispatchedBody = "Se le despachó una ruta de recogida. Ábrala en la app y acéptela para salir.";
    public const string ManifestDispatchedBody = "Se le despachó un manifiesto. Ábralo en la app y acéptelo para salir.";
}

/// <summary>Lote 215 — reglas y mensajes de «asignar en la calle».</summary>
public static class StreetAssignRules
{
    public const string OrderRequiredMessage = "Indique la orden.";
    public const string TripRequiredMessage = "Indique la ruta.";
    public const string DriverRequiredMessage = "Indique el chofer.";
    public const string VehicleRequiredMessage = "Indique el vehículo.";
    public const string MixedKindsMessage = "No se pueden juntar entregas y recogidas en la misma ruta.";
    public const string QuickRouteComment = "Ruta rápida armada desde la calle (app móvil)";
    public static string NotMovableMessage(string order, string status) => $"La orden {order} está «{status}»: ya no se puede mover de ruta.";
    public static string NotInAnyTripMessage(string order) => $"La orden {order} no está en ninguna ruta.";
    public static string WrongKindMessage(string order, string kind) => kind == TripKinds.Pickup
        ? $"La orden {order} es de recogida: elija una ruta de recogida."
        : $"La orden {order} es de entrega: elija una ruta de entrega.";
    public static string PipelineMissingMessage(string status) => $"El estatus «{status}» no está habilitado para esta compañía.";
    public static string AssignedComment(string trip) => $"Asignada desde la calle a la ruta {trip}";
    public static string MovedComment(string from, string to) => $"Pasada desde la calle de la ruta {from} a la {to}";
    public static string UnassignedComment(string trip) => $"Sacada desde la calle de la ruta {trip}";
    public static string StopRemovedTitle(string tripCode) => DriverNoticeRules.StopRemovedTitle(tripCode);
    public static string StopRemovedBody(string order) => DriverNoticeRules.StopRemovedBody(order);
}
