using System.Globalization;
using Teikem.Domain.Constants;
using Teikem.Domain.Fleet;

namespace Teikem.Domain.Trips;

/// <summary>Aviso o bloqueante de una ruta (despacho, selector, ficha). Blocking = impide despachar.</summary>
public sealed record TripIssue(string Code, string Message, bool Blocking);

/// <summary>Orden de la ruta que no es elegible, con el motivo de <see cref="TripRules.CheckEligibility"/>.</summary>
public sealed record OrderIneligibility(string OrderNumber, string Reason);

/// <summary>
/// Hechos de una orden para decidir si se puede asignar a una ruta (TripRules.CheckEligibility), sin BD:
/// - StatusCode/StatusLabel/StageKind/IsInitial/SortOrder: su estatus en el pipeline del tenant (con etapas deshabilitadas);
/// - InTransitSortOrder: el SortOrder de IN_TRANSIT en ese pipeline (la orden debe estar antes);
/// - AssignTripAllowed: la capacidad ASSIGN_TRIP de su estatus (StatusService.IsAllowedAsync);
/// - HasPendingDelivery: tiene una parada DELIVERY no terminal.
/// </summary>
public sealed record OrderEligibilityInput(
    string OrderNumber,
    bool IsActive,
    bool IsSpecialDelivery,
    string StatusCode,
    string StatusLabel,
    string StageKind,
    bool IsInitial,
    int SortOrder,
    int InTransitSortOrder,
    bool AssignTripAllowed,
    bool HasPendingDelivery);

/// <summary>
/// Hechos de una ruta para armar sus avisos y bloqueantes (TripRules.BuildIssues), sin BD. Los arma TripIssueBuilder por lote.
/// - DriverAvailability/VehicleAvailability: resultado del Lote 4 en PlanDate (null si la ruta no tiene chofer/vehículo).
/// - EffectiveMaxStops: máximo de paradas del chofer (el propio o el default del tenant); null sin chofer.
/// - Vehicle*: capacidad del vehículo (null = sin límite o sin vehículo).
/// - LateStops: paradas cuya llegada planificada pasa el fin de su ventana; ApproximatePins: sin coordenada o con
///   precisión distinta de EXACT/MANUAL.
/// - DriverOtherTrips/VehicleOtherTrips: códigos de OTRAS rutas activas y no canceladas del mismo día con el mismo recurso.
/// - IneligibleOrders: órdenes vigentes no elegibles (solo si se pidió la elegibilidad).
/// </summary>
public sealed record TripIssueInput(
    string TripCode,
    bool HasDriver,
    bool HasVehicle,
    int StopCount,
    AvailabilityResult? DriverAvailability,
    AvailabilityResult? VehicleAvailability,
    int? EffectiveMaxStops,
    int? VehicleMaxStops,
    decimal? VehicleMaxWeightKg,
    decimal? VehicleMaxVolumeM3,
    decimal TotalWeightKg,
    decimal TotalVolumeM3,
    int LateStops,
    bool HasPlannedStart,
    int ApproximatePins,
    IReadOnlyList<string> DriverOtherTrips,
    IReadOnlyList<string> VehicleOtherTrips,
    IReadOnlyList<OrderIneligibility> IneligibleOrders);

/// <summary>Códigos de los avisos y bloqueantes de una ruta (contrato estable con el front y el manual).</summary>
public static class TripIssueCodes
{
    // Bloquean el despacho
    public const string NoDriver = "NO_DRIVER";
    public const string NoVehicle = "NO_VEHICLE";
    public const string NoStops = "NO_STOPS";
    public const string DriverUnavailable = "DRIVER_UNAVAILABLE";
    public const string VehicleUnavailable = "VEHICLE_UNAVAILABLE";
    public const string OrderNotEligible = "ORDER_NOT_ELIGIBLE";
    // Solo avisan
    public const string OverStopLimit = "OVER_STOP_LIMIT";
    public const string OverVehicleStops = "OVER_VEHICLE_STOPS";
    public const string OverWeight = "OVER_WEIGHT";
    public const string OverVolume = "OVER_VOLUME";
    public const string LateWindows = "LATE_WINDOWS";
    public const string NoPlannedStart = "NO_PLANNED_START";
    public const string ApproximatePins = "APPROXIMATE_PINS";
    public const string DriverDoubleBooked = "DRIVER_DOUBLE_BOOKED";
    public const string VehicleDoubleBooked = "VEHICLE_DOUBLE_BOOKED";
}

/// <summary>
/// Lote 5 (P0) — reglas puras de la ruta: editabilidad, elegibilidad de órdenes, secuencia, máximo de paradas, ruta abierta,
/// avisos/bloqueantes del despacho y mensajes exactos (el manual y la FAQ los citan). Números en cultura invariante.
/// </summary>
public static class TripRules
{
    // ---------------- Editabilidad ----------------

    /// <summary>Estatus en los que el CONTENIDO de la ruta (órdenes, paradas, secuencia) se puede cambiar.</summary>
    public static readonly IReadOnlyList<string> EditableStatuses = new[] { TripStatuses.Draft, TripStatuses.Planned };

    public static bool IsEditable(string? statusCode)
        => EditableStatuses.Contains(statusCode ?? string.Empty, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 422 de una ruta no editable: DISPATCHED/IN_PROGRESS → 'La ruta {código} ya fue despachada; no se puede editar ni
    /// eliminar.'; COMPLETED/CANCELLED → 'La ruta {código} está cerrada; solo se consulta.'; otro estatus (el tenant negó
    /// EDIT_TRIP) → 'El estatus actual de la ruta {código} no permite editarla.'
    /// </summary>
    public static string NotEditableMessage(string tripCode, string? statusCode)
    {
        if (Same(statusCode, TripStatuses.Dispatched) || Same(statusCode, TripStatuses.InProgress))
            return $"La ruta {tripCode} ya fue despachada; no se puede editar ni eliminar.";
        if (Same(statusCode, TripStatuses.Completed) || Same(statusCode, TripStatuses.Cancelled))
            return $"La ruta {tripCode} está cerrada; solo se consulta.";
        return $"El estatus actual de la ruta {tripCode} no permite editarla.";
    }

    // ---------------- Mensajes de órdenes en la ruta ----------------

    public const string NotInTripMessage = "La orden no está en esta ruta.";
    public const string AlreadyInTripMessage = "La orden ya está en esta ruta.";
    public const string OrderTakenMessage = "La orden ya está asignada a otra ruta.";
    public const string EligibilityHeader = "Hay órdenes que no se pueden asignar a la ruta.";

    /// <summary>409 'La orden ya está asignada a la ruta {código}.'</summary>
    public static string OrderInOtherTripMessage(string tripCode) => $"La orden ya está asignada a la ruta {tripCode}.";

    // ---------------- Límites y secuencia ----------------

    /// <summary>Tope técnico de paradas por ruta (DECISIÓN: límites técnicos duros).</summary>
    public const int MaxStopsHardCap = 300;
    public const string MaxStopsHardCapMessage = "Una ruta admite como máximo 300 paradas.";
    public const string SequenceMessage = "La secuencia debe incluir exactamente las paradas de la ruta vigente, sin repetir.";
    public const string StopNotInTripMessage = "Parada no encontrada en esta ruta.";

    // ---------------- Elegibilidad ----------------

    public const string DraftOrderMessage = "La orden está en Entrada; confírmela antes de asignarla a una ruta.";
    public const string SpecialDeliveryMessage = "Las entregas especiales se asignan al chofer desde la orden; no pasan por Sala de despacho.";
    public const string AlreadyOutMessage = "La orden ya salió a ruta o terminó; no se puede asignar a otra ruta.";
    public const string AssignTripNotAllowedMessage = "El estatus actual no permite la acción 'ASSIGN_TRIP'.";
    public const string NoPendingDeliveryMessage = "La orden no tiene una parada de entrega pendiente.";

    /// <summary>'La orden está en '{estatus}'; regrésela al pipeline antes de asignarla a una ruta.' (laterales)</summary>
    public static string LateralOrderMessage(string statusLabel) => $"La orden está en '{statusLabel}'; regrésela al pipeline antes de asignarla a una ruta.";

    /// <summary>
    /// ¿La orden se puede asignar a una ruta? null si sí; si no, el motivo exacto. Precedencia:
    /// 1. inactiva o terminal → ya salió o terminó; 2. entrega especial; 3. etapa inicial (Entrada); 4. lateral;
    /// 5. en IN_TRANSIT o después → ya salió; 6. sin la capacidad ASSIGN_TRIP; 7. sin parada DELIVERY pendiente.
    /// </summary>
    public static string? CheckEligibility(OrderEligibilityInput o)
    {
        ArgumentNullException.ThrowIfNull(o);
        if (!o.IsActive || Same(o.StageKind, StageKinds.Terminal)) return AlreadyOutMessage;
        if (o.IsSpecialDelivery) return SpecialDeliveryMessage;
        if (o.IsInitial) return DraftOrderMessage;
        if (Same(o.StageKind, StageKinds.Lateral))
            return LateralOrderMessage(string.IsNullOrWhiteSpace(o.StatusLabel) ? o.StatusCode : o.StatusLabel);
        if (o.SortOrder >= o.InTransitSortOrder) return AlreadyOutMessage;
        if (!o.AssignTripAllowed) return AssignTripNotAllowedMessage;
        if (!o.HasPendingDelivery) return NoPendingDeliveryMessage;
        return null;
    }

    /// <summary>
    /// La secuencia pedida debe ser una permutación exacta de las paradas de la versión vigente (mismos ids, sin repetir,
    /// sin faltantes ni ajenos). null si es válida; si no, <see cref="SequenceMessage"/>.
    /// </summary>
    public static string? ValidateSequence(IReadOnlyCollection<int> current, IReadOnlyList<int>? requested)
    {
        if (requested is null || current is null) return SequenceMessage;
        if (requested.Count != current.Count) return SequenceMessage;
        var set = new HashSet<int>(current);
        var seen = new HashSet<int>();
        foreach (var id in requested)
            if (!set.Contains(id) || !seen.Add(id)) return SequenceMessage;
        return null;
    }

    /// <summary>¿La ruta pasa el máximo de paradas del chofer? Estrictamente mayor; sin máximo, nunca.</summary>
    public static bool OverStopLimit(int stopCount, int? effectiveMaxStops)
        => effectiveMaxStops is int max && stopCount > max;

    /// <summary>
    /// 'Ruta abierta' de una zona y fecha: entre las candidatas (activas, DRAFT/PLANNED) gana la de menor TripId. null si no
    /// hay. La comparten el escaneo Outbound y 'Planificar el día'.
    /// </summary>
    public static int? PickOpenTrip(IEnumerable<int>? candidateTripIds)
    {
        int? best = null;
        foreach (var id in candidateTripIds ?? Array.Empty<int>())
            if (best is null || id < best.Value) best = id;
        return best;
    }

    // ---------------- Avisos y bloqueantes ----------------

    public const string NoDriverMessage = "La ruta no tiene chofer asignado.";
    public const string NoVehicleMessage = "La ruta no tiene vehículo asignado.";
    public const string NoStopsMessage = "La ruta no tiene paradas.";
    public const string NoPlannedStartMessage = "La ruta no tiene hora de salida; no se calculan las ETAs.";
    public const string DriverNotAvailablePrefix = "El chofer no está disponible para despacho: ";
    public const string VehicleNotAvailablePrefix = "El vehículo no está disponible para despacho: ";

    public static string DriverUnavailableMessage(IEnumerable<string> reasons) => JoinReasons(DriverNotAvailablePrefix, reasons);
    public static string VehicleUnavailableMessage(IEnumerable<string> reasons) => JoinReasons(VehicleNotAvailablePrefix, reasons);

    /// <summary>'Orden {número}: {motivo}'</summary>
    public static string OrderNotEligibleMessage(string orderNumber, string reason) => $"Orden {orderNumber}: {reason}";

    /// <summary>'La ruta tiene {n} paradas y el máximo del chofer es {m}.'</summary>
    public static string OverStopLimitMessage(int stops, int max)
        => string.Create(CultureInfo.InvariantCulture, $"La ruta tiene {stops} paradas y el máximo del chofer es {max}.");

    /// <summary>'La ruta tiene {n} paradas y el vehículo admite {m}.'</summary>
    public static string OverVehicleStopsMessage(int stops, int max)
        => string.Create(CultureInfo.InvariantCulture, $"La ruta tiene {stops} paradas y el vehículo admite {max}.");

    /// <summary>'La carga ({x} kg) excede la capacidad de peso del vehículo ({y} kg).'</summary>
    public static string OverWeightMessage(decimal total, decimal max)
        => string.Create(CultureInfo.InvariantCulture, $"La carga ({Num(total)} kg) excede la capacidad de peso del vehículo ({Num(max)} kg).");

    /// <summary>'El volumen ({x} m³) excede la capacidad de volumen del vehículo ({y} m³).'</summary>
    public static string OverVolumeMessage(decimal total, decimal max)
        => string.Create(CultureInfo.InvariantCulture, $"El volumen ({Num(total)} m³) excede la capacidad de volumen del vehículo ({Num(max)} m³).");

    /// <summary>'1 parada llega después del fin de su ventana.' / '{n} paradas llegan después del fin de su ventana.'</summary>
    public static string LateWindowsMessage(int count)
        => count == 1
            ? "1 parada llega después del fin de su ventana."
            : string.Create(CultureInfo.InvariantCulture, $"{count} paradas llegan después del fin de su ventana.");

    /// <summary>'1 parada tiene ubicación aproximada o sin coordenadas.' / '{n} paradas tienen ubicación aproximada o sin coordenadas.'</summary>
    public static string ApproximatePinsMessage(int count)
        => count == 1
            ? "1 parada tiene ubicación aproximada o sin coordenadas."
            : string.Create(CultureInfo.InvariantCulture, $"{count} paradas tienen ubicación aproximada o sin coordenadas.");

    /// <summary>'El chofer también está en la ruta {códigos} ese mismo día.'</summary>
    public static string DriverDoubleBookedMessage(IEnumerable<string> otherTrips)
        => $"El chofer también está en la ruta {string.Join(", ", Codes(otherTrips))} ese mismo día.";

    /// <summary>'El vehículo también está en la ruta {códigos} ese mismo día.'</summary>
    public static string VehicleDoubleBookedMessage(IEnumerable<string> otherTrips)
        => $"El vehículo también está en la ruta {string.Join(", ", Codes(otherTrips))} ese mismo día.";

    /// <summary>
    /// Avisos y bloqueantes de una ruta, en orden estable: primero los bloqueantes (NO_DRIVER, DRIVER_UNAVAILABLE, NO_VEHICLE,
    /// VEHICLE_UNAVAILABLE, NO_STOPS, ORDER_NOT_ELIGIBLE por orden) y luego los avisos (avisos de disponibilidad no
    /// bloqueantes tal cual, OVER_STOP_LIMIT, OVER_VEHICLE_STOPS, OVER_WEIGHT, OVER_VOLUME, LATE_WINDOWS, NO_PLANNED_START,
    /// APPROXIMATE_PINS, DRIVER_DOUBLE_BOOKED, VEHICLE_DOUBLE_BOOKED). La capacidad y el máximo de paradas solo avisan.
    /// </summary>
    public static IReadOnlyList<TripIssue> BuildIssues(TripIssueInput i)
    {
        ArgumentNullException.ThrowIfNull(i);
        var blocking = new List<TripIssue>();
        var warnings = new List<TripIssue>();

        if (!i.HasDriver) blocking.Add(new TripIssue(TripIssueCodes.NoDriver, NoDriverMessage, true));
        else if (i.DriverAvailability is { } da)
        {
            if (!da.Available)
                blocking.Add(new TripIssue(TripIssueCodes.DriverUnavailable, DriverUnavailableMessage(BlockingReasons(da)), true));
            warnings.AddRange(da.Issues.Where(x => !x.Blocking).Select(x => new TripIssue(x.Code, x.Message, false)));
        }

        if (!i.HasVehicle) blocking.Add(new TripIssue(TripIssueCodes.NoVehicle, NoVehicleMessage, true));
        else if (i.VehicleAvailability is { } va)
        {
            if (!va.Available)
                blocking.Add(new TripIssue(TripIssueCodes.VehicleUnavailable, VehicleUnavailableMessage(BlockingReasons(va)), true));
            warnings.AddRange(va.Issues.Where(x => !x.Blocking).Select(x => new TripIssue(x.Code, x.Message, false)));
        }

        if (i.StopCount <= 0) blocking.Add(new TripIssue(TripIssueCodes.NoStops, NoStopsMessage, true));

        foreach (var o in i.IneligibleOrders ?? Array.Empty<OrderIneligibility>())
            blocking.Add(new TripIssue(TripIssueCodes.OrderNotEligible, OrderNotEligibleMessage(o.OrderNumber, o.Reason), true));

        if (OverStopLimit(i.StopCount, i.EffectiveMaxStops))
            warnings.Add(new TripIssue(TripIssueCodes.OverStopLimit, OverStopLimitMessage(i.StopCount, i.EffectiveMaxStops!.Value), false));
        if (i.HasVehicle && i.VehicleMaxStops is int vMax && i.StopCount > vMax)
            warnings.Add(new TripIssue(TripIssueCodes.OverVehicleStops, OverVehicleStopsMessage(i.StopCount, vMax), false));
        if (i.HasVehicle && i.VehicleMaxWeightKg is decimal wMax && i.TotalWeightKg > wMax)
            warnings.Add(new TripIssue(TripIssueCodes.OverWeight, OverWeightMessage(i.TotalWeightKg, wMax), false));
        if (i.HasVehicle && i.VehicleMaxVolumeM3 is decimal vol && i.TotalVolumeM3 > vol)
            warnings.Add(new TripIssue(TripIssueCodes.OverVolume, OverVolumeMessage(i.TotalVolumeM3, vol), false));
        if (i.LateStops > 0) warnings.Add(new TripIssue(TripIssueCodes.LateWindows, LateWindowsMessage(i.LateStops), false));
        if (!i.HasPlannedStart && i.StopCount > 0) warnings.Add(new TripIssue(TripIssueCodes.NoPlannedStart, NoPlannedStartMessage, false));
        if (i.ApproximatePins > 0) warnings.Add(new TripIssue(TripIssueCodes.ApproximatePins, ApproximatePinsMessage(i.ApproximatePins), false));
        if (i.HasDriver && i.DriverOtherTrips is { Count: > 0 })
            warnings.Add(new TripIssue(TripIssueCodes.DriverDoubleBooked, DriverDoubleBookedMessage(i.DriverOtherTrips), false));
        if (i.HasVehicle && i.VehicleOtherTrips is { Count: > 0 })
            warnings.Add(new TripIssue(TripIssueCodes.VehicleDoubleBooked, VehicleDoubleBookedMessage(i.VehicleOtherTrips), false));

        blocking.AddRange(warnings);
        return blocking;
    }

    /// <summary>'La ruta {código} no se puede despachar: {m1}; {m2}.' con los mensajes de los bloqueantes (sin su punto final).</summary>
    public static string DispatchBlockingMessage(string tripCode, IEnumerable<TripIssue> issues)
    {
        var parts = (issues ?? Array.Empty<TripIssue>())
            .Where(x => x.Blocking && !string.IsNullOrWhiteSpace(x.Message))
            .Select(x => x.Message.Trim().TrimEnd('.'))
            .Distinct()
            .ToList();
        return $"La ruta {tripCode} no se puede despachar: {string.Join("; ", parts)}.";
    }

    // ---------------- helpers ----------------

    private static IEnumerable<string> BlockingReasons(AvailabilityResult r)
    {
        var blocking = r.Issues.Where(x => x.Blocking).Select(x => x.Message).ToList();
        return blocking.Count > 0 ? blocking : r.Issues.Select(x => x.Message);
    }

    private static string JoinReasons(string prefix, IEnumerable<string> reasons)
        => prefix + string.Join("; ", (reasons ?? Array.Empty<string>())
            .Where(m => !string.IsNullOrWhiteSpace(m)).Select(m => m.Trim().TrimEnd('.'))) + ".";

    private static IEnumerable<string> Codes(IEnumerable<string> codes)
        => (codes ?? Array.Empty<string>()).Where(c => !string.IsNullOrWhiteSpace(c)).Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(c => c, StringComparer.Ordinal);

    private static string Num(decimal value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    private static bool Same(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
