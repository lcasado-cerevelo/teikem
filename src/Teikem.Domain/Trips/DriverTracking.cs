using Teikem.Domain.Common;

namespace Teikem.Domain.Trips;

/// <summary>Constantes y reglas puras del seguimiento de ruta del chofer (app del chofer, 2026-10-23).</summary>
public static class DriverTrackingCodes
{
    /// <summary>Por qué se dejó de enviar la ubicación (o volvió): lo manda la app y la oficina lo ve en el monitor.</summary>
    public const string Started = "STARTED";
    public const string StoppedByDriver = "STOPPED_BY_DRIVER";
    public const string PermissionLost = "PERMISSION_LOST";
    public const string Resumed = "RESUMED";
    public static readonly string[] EventTypes = { Started, StoppedByDriver, PermissionLost, Resumed };
}

/// <summary>
/// Un aviso del seguimiento: el chofer apagó el envío de su ubicación, perdió el permiso o lo reanudó. Es lo que distingue «el chofer lo apagó» de «sin señal» en el monitor.
/// Se guarda por compañía (si el chofer trabaja para dos con rutas en curso, el aviso se guarda en las dos). Sin bitácora de auditoría: es un registro técnico de alto volumen y se purga con los puntos.
/// </summary>
public class DriverTrackingEvent : ITenantScoped
{
    public long DriverTrackingEventId { get; set; }
    public int TenantId { get; set; }
    public int DriverId { get; set; }
    public int? TripId { get; set; }
    public string EventType { get; set; } = DriverTrackingCodes.Started;
    public DateTime OccurredAtUtc { get; set; }
    public DateTime ReceivedAtUtc { get; set; } = DateTime.UtcNow;
}

public static class DriverTrackingRules
{
    public const int MaxPointsPerBatch = 500;
    public const int MaxEventsPerBatch = 50;
    public static readonly TimeSpan FutureTolerance = TimeSpan.FromMinutes(5);

    public const string TooManyPointsMessage = "Máximo 500 puntos por envío.";
    public const string TooManyEventsMessage = "Máximo 50 avisos por envío.";
    public const string EventTypeInvalidMessage = "El tipo de aviso no es válido.";

    /// <summary>Latitud y longitud en rango y no (0,0): un teléfono sin fix a veces manda ceros.</summary>
    public static bool ValidCoordinate(double lat, double lng) => lat is >= -90 and <= 90 && lng is >= -180 and <= 180 && !(lat == 0 && lng == 0) && !double.IsNaN(lat) && !double.IsNaN(lng);

    /// <summary>¿El punto sirve? Hora no futura (con tolerancia del reloj) ni más vieja que la retención, y coordenada válida.</summary>
    public static bool Acceptable(double lat, double lng, DateTime capturedAtUtc, DateTime nowUtc, int retentionDays)
        => ValidCoordinate(lat, lng) && capturedAtUtc <= nowUtc + FutureTolerance && capturedAtUtc >= nowUtc.AddDays(-Math.Max(1, retentionDays));

    public static bool ValidEventType(string? type) => type is not null && DriverTrackingCodes.EventTypes.Contains(type.Trim().ToUpperInvariant());
}
