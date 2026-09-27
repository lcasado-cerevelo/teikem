using Teikem.Domain.Constants;

namespace Teikem.Domain.Wms;

/// <summary>
/// Lote 6 (P9) — reglas puras de las citas de muelle (R19b, D30). Los mensajes son públicos porque el manual y la FAQ los
/// citan literalmente.
/// - Solapamiento SEMIABIERTO por muelle: [inicio, fin) — una cita que termina a las 10:00 no choca con otra que empieza a
///   las 10:00. Una cita sin fin ocupa 60 minutos.
/// - Dirección compatible con el tipo de muelle: INBOUND solo recibe INBOUND, OUTBOUND solo OUTBOUND, BOTH ambas.
/// - Ventana: el fin es posterior al inicio. Horizonte: el inicio va de ayer (00:00 UTC) a +90 días.
/// - Una cita se enlaza a un aviso de llegada (ASN) o a un viaje, nunca a ambos; un ASN es siempre de entrada.
/// - Solo las citas SCHEDULED y ARRIVED ocupan agenda; ARRIVED ocupa el muelle y COMPLETED/NO_SHOW/CANCELLED lo liberan.
/// </summary>
public static class DockScheduleRules
{
    /// <summary>Duración asumida de una cita sin fin.</summary>
    public const int DefaultDurationMinutes = 60;

    /// <summary>Días hacia adelante en que se admite agendar.</summary>
    public const int HorizonDays = 90;

    public const string Overlap = "El muelle ya tiene una cita que se solapa con ese horario.";
    public const string Window = "El fin de la cita debe ser posterior a su inicio.";
    public const string Horizon = "La cita debe agendarse entre ayer y los próximos 90 días.";
    public const string StartRequired = "Indique el inicio de la cita.";
    public const string AsnOrTrip = "Una cita se enlaza a un aviso de llegada o a un viaje, no a ambos.";
    public const string AsnInboundOnly = "Una cita de un aviso de llegada debe ser de entrada (INBOUND).";
    public const string DockInactive = "El muelle está inactivo; reactívelo o elija otro.";
    public const string NotReschedulable = "La cita solo se reprograma mientras está agendada.";
    public const string AsnNotActive = "El aviso de llegada está cancelado o dado de baja.";

    public static string Incompatible(string dockType, string direction)
        => $"El muelle es de tipo {dockType}; no admite citas de {direction}.";

    public static string UnknownDirection(string? value)
        => $"Dirección desconocida: '{value}'. Use INBOUND u OUTBOUND.";

    public static string UnknownStatus(string? value)
        => $"Estatus de cita desconocido: '{value}'. Use SCHEDULED, ARRIVED, COMPLETED, NO_SHOW o CANCELLED.";

    /// <summary>Estatus que ocupan agenda (entran en la verificación de solapamiento).</summary>
    public static readonly IReadOnlyList<string> ActiveStatuses = new[] { AppointmentStatuses.Scheduled, AppointmentStatuses.Arrived };

    /// <summary>Estatus que se fijan a mano desde la pantalla (la transición la valida StatusService).</summary>
    public static readonly IReadOnlyList<string> ManualStatuses = new[]
    {
        AppointmentStatuses.Scheduled, AppointmentStatuses.Arrived, AppointmentStatuses.Completed,
        AppointmentStatuses.NoShow, AppointmentStatuses.Cancelled,
    };

    /// <summary>Direcciones válidas de una cita.</summary>
    public static readonly IReadOnlyList<string> Directions = new[] { DockDirections.Inbound, DockDirections.Outbound };

    public static bool IsActive(string? statusCode) => ActiveStatuses.Any(s => Eq(s, statusCode));

    /// <summary>ARRIVED ocupa el muelle.</summary>
    public static bool OccupiesDock(string? statusCode) => Eq(statusCode, AppointmentStatuses.Arrived);

    /// <summary>COMPLETED, NO_SHOW y CANCELLED liberan el muelle (si no queda otra llegada en curso).</summary>
    public static bool ReleasesDock(string? statusCode)
        => Eq(statusCode, AppointmentStatuses.Completed) || Eq(statusCode, AppointmentStatuses.NoShow) || Eq(statusCode, AppointmentStatuses.Cancelled);

    /// <summary>Fin efectivo: el indicado o inicio + 60 minutos.</summary>
    public static DateTime EffectiveEnd(DateTime start, DateTime? end) => end ?? start.AddMinutes(DefaultDurationMinutes);

    /// <summary>Solapamiento semiabierto de [aStart, aEnd) y [bStart, bEnd) con el fin efectivo.</summary>
    public static bool Overlaps(DateTime aStart, DateTime? aEnd, DateTime bStart, DateTime? bEnd)
        => aStart < EffectiveEnd(bStart, bEnd) && bStart < EffectiveEnd(aStart, aEnd);

    /// <summary>¿El muelle de este tipo admite citas de esta dirección? BOTH admite ambas.</summary>
    public static bool Compatible(string? dockType, string? direction)
    {
        if (Eq(dockType, DockTypes.Both)) return Eq(direction, DockDirections.Inbound) || Eq(direction, DockDirections.Outbound);
        if (Eq(dockType, DockTypes.Inbound)) return Eq(direction, DockDirections.Inbound);
        if (Eq(dockType, DockTypes.Outbound)) return Eq(direction, DockDirections.Outbound);
        return false;
    }

    /// <summary>Normaliza la dirección (INBOUND/OUTBOUND) o null si no es válida.</summary>
    public static string? NormalizeDirection(string? value)
    {
        var v = value?.Trim().ToUpperInvariant();
        return Directions.FirstOrDefault(d => d == v);
    }

    /// <summary>Normaliza un estatus manual o null si no es válido.</summary>
    public static string? NormalizeStatus(string? value)
    {
        var v = value?.Trim().ToUpperInvariant();
        return ManualStatuses.FirstOrDefault(s => s == v);
    }

    /// <summary>
    /// Ventana y horizonte: fin &gt; inicio (Window) e inicio entre ayer 00:00 UTC y ahora + 90 días (Horizon).
    /// Devuelve el mensaje de error o null.
    /// </summary>
    public static string? ValidateWindow(DateTime start, DateTime? end, DateTime nowUtc)
    {
        if (end is DateTime e && e <= start) return Window;
        var earliest = nowUtc.Date.AddDays(-1);
        var latest = nowUtc.AddDays(HorizonDays);
        if (start < earliest || start > latest) return Horizon;
        return null;
    }

    /// <summary>ASN o viaje, nunca ambos; un ASN solo con dirección INBOUND. Devuelve el mensaje de error o null.</summary>
    public static string? ValidateReferences(bool hasAsn, bool hasTrip, string direction)
    {
        if (hasAsn && hasTrip) return AsnOrTrip;
        if (hasAsn && !Eq(direction, DockDirections.Inbound)) return AsnInboundOnly;
        return null;
    }

    /// <summary>¿La nueva ventana choca con alguna de las citas activas existentes del muelle?</summary>
    public static bool OverlapsAny(DateTime start, DateTime? end, IEnumerable<(DateTime Start, DateTime? End)> existing)
        => existing.Any(x => Overlaps(start, end, x.Start, x.End));

    /// <summary>Fecha en UTC: Unspecified se toma como UTC; Local se convierte.</summary>
    public static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };

    private static bool Eq(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
