using System.Globalization;
using Teikem.Domain.Clients;
using Teikem.Domain.Constants;

namespace Teikem.Domain.Trips;

/// <summary>Candidato a chofer por defecto de una ruta: chofer activo con la zona como primaria y su disponibilidad en PlanDate.</summary>
public sealed record DefaultDriverCandidate(int DriverId, bool Available);

/// <summary>
/// Qué cambia un PATCH de cabecera respecto de la ruta actual (true = el valor final difiere del actual). Lo arma TripService
/// y lo evalúa <see cref="TripPlanningRules.ValidateDispatchedPatch"/> sin BD.
/// </summary>
public sealed record TripHeaderChange(bool PlanDate, bool Zone, bool Driver, bool Vehicle, bool PlannedStart);

/// <summary>
/// Flags del PATCH de cabecera tal como llegan: valor indicado (Has*) y 'quitar' (Clear*). Lo evalúa
/// <see cref="TripPlanningRules.ValidatePatchFlags"/> sin depender del contrato HTTP.
/// </summary>
public sealed record TripPatchFlags(
    bool HasDriver, bool ClearDriver,
    bool HasVehicle, bool ClearVehicle,
    bool HasZone, bool ClearZone,
    bool HasPlannedStart, bool ClearPlannedStart);

/// <summary>
/// Lote 5 (P1) — reglas puras de la cabecera de una ruta (Trip): número automático AAAA-####, fecha del plan, hora de
/// salida (y su valor por defecto 12:00 UTC = 08:00 AST), chofer por defecto por zona primaria, llaves prohibidas y flags
/// en conflicto del PATCH, y qué se puede cambiar de una ruta ya despachada (capacidad EDIT_TRIP). Los mensajes son
/// constantes públicas porque el manual y la FAQ los citan tal cual. Números y fechas en cultura invariante.
/// </summary>
public static class TripPlanningRules
{
    // ---------------- Mensajes ----------------

    public const string PlanDateRequiredMessage = "Indique la fecha de la ruta.";
    public const string PlanDateRangeMessage = "La fecha de la ruta no puede ser anterior a ayer ni posterior a 60 días.";
    public const string PlannedStartRangeMessage = "La hora de salida debe caer en la fecha de la ruta (±12 h por zona horaria).";
    public const string CodeImmutableMessage = "El número de la ruta se fija al crearlo; no se puede cambiar.";
    public const string StatusImmutableMessage = "El estatus de la ruta cambia con sus acciones (optimizar, despachar, eliminar).";
    public const string FieldImmutableMessage = "Ese campo no se puede modificar.";
    public const string DriverConflictMessage = "Indique el chofer o quítelo, no ambos.";
    public const string VehicleConflictMessage = "Indique el vehículo o quítelo, no ambos.";
    public const string ZoneConflictMessage = "Indique la zona o quítela, no ambas.";
    public const string PlannedStartConflictMessage = "Indique la hora de salida o quítela, no ambas.";
    public const string DispatchedHeaderMessage = "La fecha y la zona de una ruta despachada no se cambian.";
    public const string DispatchedRequiredMessage = "Una ruta despachada debe conservar chofer, vehículo y hora de salida; cámbielos en lugar de quitarlos.";
    public const string DuplicateCodeMessage = "Ya existe una ruta con ese número.";
    public const string DefaultDeleteComment = "Ruta eliminada";

    // Reasignación en bloque por zona
    public const string ZonesRequiredMessage = "Indique al menos una zona.";
    public const string MaxReassignZonesMessage = "Máximo 50 zonas por reasignación.";
    public const string DriverRequiredMessage = "Indique el chofer.";
    public const int MaxReassignZones = 50;

    // Disponibilidad (Lote 4) al asignar: 409 con los motivos bloqueantes
    public const string DriverNotAvailablePrefix = "El chofer no está disponible para despacho: ";
    public const string VehicleNotAvailablePrefix = "El vehículo no está disponible para despacho: ";

    public const string DateRangeMessage = "El rango de fechas es inválido.";

    /// <summary>Días hacia adelante que admite la fecha del plan (desde hoy).</summary>
    public const int MaxDaysAhead = 60;
    /// <summary>Días hacia atrás que admite la fecha del plan (ayer).</summary>
    public const int MaxDaysBack = 1;
    /// <summary>Margen de la hora de salida alrededor de la fecha del plan, en horas (zona horaria).</summary>
    public const int PlannedStartMarginHours = 12;
    /// <summary>Hora de salida por defecto, en UTC (12:00 UTC = 08:00 AST, Puerto Rico).</summary>
    public const int DefaultStartHourUtc = 12;

    public static string UnknownStatusMessage(string code) => $"Estatus de ruta desconocido: '{code}'.";

    public static string DriverNotAvailableMessage(IEnumerable<string> blockingMessages) => NotAvailable(DriverNotAvailablePrefix, blockingMessages);
    public static string VehicleNotAvailableMessage(IEnumerable<string> blockingMessages) => NotAvailable(VehicleNotAvailablePrefix, blockingMessages);

    private static string NotAvailable(string prefix, IEnumerable<string> messages)
        => prefix + string.Join("; ", (messages ?? Array.Empty<string>()).Select(m => m.Trim().TrimEnd('.')).Where(m => m.Length > 0)) + ".";

    // ---------------- Número ----------------

    /// <summary>Número de ruta 'AAAA-####' con el año de la fecha del plan: (2026-09-26, 1) → '2026-0001'. No se reinicia por año.</summary>
    public static string FormatCode(DateOnly planDate, long seq)
        => NumberFormat.Resolve(planDate.Year.ToString("0000", CultureInfo.InvariantCulture) + "-####", seq);

    // ---------------- Fechas ----------------

    /// <summary>Fecha del plan obligatoria y entre ayer y hoy + 60 días (inclusive). null si es válida.</summary>
    public static string? ValidatePlanDate(DateOnly? planDate, DateOnly today)
    {
        if (planDate is not DateOnly d) return PlanDateRequiredMessage;
        if (d < today.AddDays(-MaxDaysBack) || d > today.AddDays(MaxDaysAhead)) return PlanDateRangeMessage;
        return null;
    }

    /// <summary>
    /// La hora de salida (UTC) debe caer en [planDate 00:00 − 12 h, planDate + 1 día 00:00 + 12 h). Una hora sin zona
    /// (Unspecified) se toma como UTC; una hora local se convierte. null si es válida.
    /// </summary>
    public static string? ValidatePlannedStart(DateTime startUtc, DateOnly planDate)
    {
        var start = AsUtc(startUtc);
        var from = planDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc).AddHours(-PlannedStartMarginHours);
        var to = planDate.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc).AddHours(PlannedStartMarginHours);
        return start >= from && start < to ? null : PlannedStartRangeMessage;
    }

    /// <summary>Hora de salida por defecto: la fecha del plan a las 12:00 UTC (08:00 AST), para que siempre haya ETA.</summary>
    public static DateTime DefaultPlannedStart(DateOnly planDate)
        => planDate.ToDateTime(new TimeOnly(DefaultStartHourUtc, 0), DateTimeKind.Utc);

    /// <summary>Normaliza a UTC: Unspecified se interpreta como UTC; Local se convierte.</summary>
    public static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };

    /// <summary>
    /// Al cambiar la fecha del plan sin indicar hora de salida, la salida conserva su hora del día y se mueve los mismos
    /// días que la fecha. null se queda null.
    /// </summary>
    public static DateTime? ShiftPlannedStart(DateTime? currentStartUtc, DateOnly oldPlanDate, DateOnly newPlanDate)
        => currentStartUtc is DateTime s ? AsUtc(s).AddDays(newPlanDate.DayNumber - oldPlanDate.DayNumber) : null;

    // ---------------- Chofer por defecto ----------------

    /// <summary>El chofer por defecto de la zona solo si hay exactamente un candidato y está disponible; si no, null.</summary>
    public static int? PickDefaultDriver(IReadOnlyCollection<DefaultDriverCandidate>? candidates)
    {
        if (candidates is null) return null;
        var distinct = candidates.GroupBy(c => c.DriverId).Select(g => g.First()).ToList();
        return distinct.Count == 1 && distinct[0].Available ? distinct[0].DriverId : null;
    }

    // ---------------- PATCH ----------------

    /// <summary>Llaves que el PATCH no acepta (llegan como propiedades desconocidas) y su mensaje.</summary>
    public static readonly IReadOnlyDictionary<string, string> ForbiddenPatchKeys =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["code"] = CodeImmutableMessage,
            ["tripCode"] = CodeImmutableMessage,
            ["status"] = StatusImmutableMessage,
            ["statusCode"] = StatusImmutableMessage,
            ["toCode"] = StatusImmutableMessage,
            ["tenantId"] = FieldImmutableMessage,
            ["version"] = FieldImmutableMessage,
            ["routeVersion"] = FieldImmutableMessage,
            ["originWarehouseId"] = FieldImmutableMessage,
        };

    /// <summary>Primera llave prohibida del PATCH (en el orden en que llegó) con su mensaje; null si no hay ninguna.</summary>
    public static (string Field, string Message)? CheckForbiddenKeys(IEnumerable<string>? keys)
    {
        if (keys is null) return null;
        foreach (var key in keys)
            if (key is not null && ForbiddenPatchKeys.TryGetValue(key, out var message)) return (key, message);
        return null;
    }

    /// <summary>Flags en conflicto (valor y 'quitar' a la vez) por campo; vacío si no hay ninguno.</summary>
    public static IReadOnlyDictionary<string, string> ValidatePatchFlags(TripPatchFlags f)
    {
        var errors = new Dictionary<string, string>();
        if (f.HasDriver && f.ClearDriver) errors["driverPublicId"] = DriverConflictMessage;
        if (f.HasVehicle && f.ClearVehicle) errors["vehiclePublicId"] = VehicleConflictMessage;
        if (f.HasZone && f.ClearZone) errors["dispatchZoneId"] = ZoneConflictMessage;
        if (f.HasPlannedStart && f.ClearPlannedStart) errors["plannedStartUtc"] = PlannedStartConflictMessage;
        return errors;
    }

    /// <summary>¿La ruta ya salió a despacho (DISPATCHED o IN_PROGRESS)?</summary>
    public static bool IsDispatchedStatus(string? statusCode)
        => string.Equals(statusCode, TripStatuses.Dispatched, StringComparison.OrdinalIgnoreCase)
           || string.Equals(statusCode, TripStatuses.InProgress, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Con la ruta despachada (y EDIT_TRIP habilitada por el tenant) solo se aceptan chofer, vehículo y hora de salida:
    /// cambiar la fecha o la zona es DispatchedHeaderMessage. null si el cambio es aceptable.
    /// </summary>
    public static string? ValidateDispatchedPatch(TripHeaderChange change)
        => change.PlanDate || change.Zone ? DispatchedHeaderMessage : null;

    /// <summary>
    /// Con la ruta despachada (y EDIT_TRIP habilitada) el chofer y el vehículo se CAMBIAN, nunca se quitan: el despacho los
    /// exige y el chofer ve la ruta en la app. La hora de salida tampoco se quita si la ruta la tenía (congela las ETAs).
    /// Recibe los valores FINALES del PATCH y la hora de salida actual. DispatchedRequiredMessage o null si es aceptable.
    /// </summary>
    public static string? ValidateDispatchedValues(int? driverId, int? vehicleId, DateTime? plannedStartUtc, DateTime? currentPlannedStartUtc)
        => driverId is null || vehicleId is null || (plannedStartUtc is null && currentPlannedStartUtc is not null)
            ? DispatchedRequiredMessage
            : null;

    // ---------------- Pines ----------------

    /// <summary>
    /// ¿El pin de la parada es aproximado? Sin coordenada, o con precisión distinta de EXACT y MANUAL (centroide de CP o de
    /// pueblo, o sin precisión registrada).
    /// </summary>
    public static bool IsApproximatePin(bool hasPoint, string? accuracyCode)
        => !hasPoint
           || !(string.Equals(accuracyCode, GeocodeAccuracies.Exact, StringComparison.OrdinalIgnoreCase)
                || string.Equals(accuracyCode, GeocodeAccuracies.Manual, StringComparison.OrdinalIgnoreCase));

    /// <summary>¿La llegada planificada pasa el fin de la ventana de la parada?</summary>
    public static bool LateForWindow(DateTime? plannedArrivalUtc, DateTime? windowEndUtc)
        => plannedArrivalUtc is DateTime a && windowEndUtc is DateTime e && AsUtc(a) > AsUtc(e);
}
