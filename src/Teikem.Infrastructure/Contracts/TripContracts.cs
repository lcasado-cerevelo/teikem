using System.Text.Json;
using System.Text.Json.Serialization;

namespace Teikem.Infrastructure.Contracts;

// Lote 5 — Trips y rutas. Records sin lógica con firma posicional FIJA (TripContractsTests la verifica). Ninguna solicitud
// lleva TenantId ni ids internos de Trip/Route (la ruta se identifica por PublicId en la URL; las paradas por RouteStopId,
// siempre validadas contra la versión vigente de ESA ruta). Sin dinero: ningún DTO expone montos ni tarifas.

// ================================================================ comunes y ficha

/// <summary>Aviso (Blocking = false) o bloqueante de despacho (Blocking = true) de una ruta.</summary>
public sealed record TripIssueDto(string Code, string Message, bool Blocking);

public sealed record GeoPointDto(double Lat, double Lng);

/// <summary>Último ping del chofer; LinkedToTrip = false si es el respaldo (ping del chofer sin TripId desde la salida).</summary>
public sealed record DriverPingDto(GeoPointDto Point, decimal? SpeedKmh, int? HeadingDeg, DateTime CapturedAtUtc, DateTime ReceivedAtUtc, bool LinkedToTrip);

/// <summary>Listado de rutas: sin fecha = hoy en la zona de la compañía; From/To = rango; Search se aplica después de los filtros.</summary>
public sealed record TripListQuery(
    DateOnly? Date = null,
    DateOnly? From = null,
    DateOnly? To = null,
    string[]? Status = null,
    int? DispatchZoneId = null,
    Guid? DriverPublicId = null,
    string? Search = null,
    bool IncludeCancelled = false);

public sealed record TripListItemDto(
    int Id,
    Guid PublicId,
    string Code,
    DateOnly PlanDate,
    int? DispatchZoneId,
    string? ZoneCode,
    string? ZoneName,
    Guid? DriverPublicId,
    string? DriverCode,
    string? DriverName,
    Guid? VehiclePublicId,
    string? VehicleCode,
    string StatusCode,
    string Status,
    string? StatusColor,
    bool IsEditable,
    int? RouteVersion,
    string? RouteStatusCode,
    int StopCount,
    int? EffectiveMaxStops,
    bool OverStopLimit,
    decimal? TotalDistanceKm,
    int? TotalDurationMin,
    DateTime? PlannedStartUtc,
    DateTime? PlannedEndUtc,
    bool IsActive,
    string Kind = "DELIVERY");

/// <summary>Parada de la versión vigente: dirección, zona, coordenada y precisión (el mapa lo dibuja el front), ventana y ETA.</summary>
public sealed record RouteStopDto(
    int Id,
    int Sequence,
    int OrderStopId,
    Guid OrderPublicId,
    string OrderNumber,
    string PackBatchNumber,
    string ClientName,
    string? ConsigneeName,
    string Line1,
    string? Line2,
    string City,
    string? PostalCode,
    string? ZoneCode,
    GeoPointDto? Point,
    string? GeocodeAccuracyCode,
    string? GeocodeAccuracy,
    bool IsApproximate,
    DateTime? WindowStartUtc,
    DateTime? WindowEndUtc,
    int ServiceMinutes,
    DateTime? PlannedArrivalUtc,
    DateTime? PlannedDepartureUtc,
    decimal? DistanceFromPrevKm,
    int? DurationFromPrevMin,
    bool LateForWindow,
    int? Pieces,
    decimal? WeightKg,
    decimal? VolumeM3,
    string StatusCode,
    string Status,
    DateTime? ActualArrivalUtc,
    DateTime? ActualDepartureUtc,
    string StopKind = "DELIVERY",
    string? ServiceType = null,
    bool IsRoundTrip = false);

/// <summary>Orden que no cupo en la última optimización y sigue sin ruta, con el motivo.</summary>
public sealed record UnassignedStopDto(Guid OrderPublicId, string OrderNumber, string ReasonCode, string Reason);

/// <summary>Ficha de la ruta (Trip + su versión vigente). RowVersion en base64 para el control optimista.</summary>
public sealed record TripDetailDto(
    int Id,
    Guid PublicId,
    string Code,
    DateOnly PlanDate,
    int? DispatchZoneId,
    string? ZoneCode,
    string? ZoneName,
    Guid? DriverPublicId,
    string? DriverCode,
    string? DriverName,
    Guid? VehiclePublicId,
    string? VehicleCode,
    string? VehiclePlate,
    string StatusCode,
    string Status,
    bool IsEditable,
    bool CanEditHeader,
    bool IsTerminal,
    int? RouteId,
    int? RouteVersion,
    string? RouteStatusCode,
    string? RouteStatus,
    int StopCount,
    int? EffectiveMaxStops,
    bool OverStopLimit,
    decimal TotalWeightKg,
    decimal TotalVolumeM3,
    decimal? TotalDistanceKm,
    int? TotalDurationMin,
    DateTime? PlannedStartUtc,
    DateTime? PlannedEndUtc,
    DateTime? ActualStartUtc,
    DateTime? ActualEndUtc,
    IReadOnlyList<RouteStopDto> Stops,
    IReadOnlyList<TripIssueDto> Issues,
    IReadOnlyList<UnassignedStopDto> LastRunUnassigned,
    DriverPingDto? LastPing,
    bool IsActive,
    DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc,
    string RowVersion,
    string Kind = "DELIVERY");

// ================================================================ planificación y órdenes

/// <summary>Alta de ruta. Sin chofer y con zona: chofer por defecto de la zona. Sin salida: 12:00 UTC de la fecha.</summary>
public sealed record TripCreateRequest(
    DateOnly? PlanDate,
    int? DispatchZoneId = null,
    Guid? DriverPublicId = null,
    Guid? VehiclePublicId = null,
    DateTime? PlannedStartUtc = null,
    string? Kind = null);

/// <summary>Edición de la cabecera (null = sin cambio; Clear* quita el dato).</summary>
public sealed record TripPatchRequest(
    DateOnly? PlanDate = null,
    int? DispatchZoneId = null,
    bool? ClearZone = null,
    Guid? DriverPublicId = null,
    bool? ClearDriver = null,
    Guid? VehiclePublicId = null,
    bool? ClearVehicle = null,
    DateTime? PlannedStartUtc = null,
    bool? ClearPlannedStart = null,
    string? RowVersion = null)
{
    /// <summary>Llaves del JSON que no corresponden a ninguna propiedad (el servicio rechaza las prohibidas con 400).</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? Extra { get; set; }
}

/// <summary>'Eliminar ruta' (DELETE con cuerpo opcional).</summary>
public sealed record TripCancelRequest(string? Comment = null, string? RowVersion = null);

/// <summary>Reasignación en bloque del chofer de las rutas abiertas de esas zonas en esa fecha.</summary>
public sealed record ZoneReassignRequest(DateOnly? PlanDate, IReadOnlyList<int>? DispatchZoneIds, Guid? DriverPublicId);

public sealed record ZoneReassignResultDto(
    Guid DriverPublicId,
    string DriverCode,
    string DriverName,
    int TripsUpdated,
    IReadOnlyList<TripListItemDto> Trips,
    IReadOnlyList<TripIssueDto> Issues);

/// <summary>'Planificar el día': zonas null o vacías = todas las zonas activas; CreateEmptyTrips crea la ruta aunque no haya órdenes.</summary>
public sealed record PlanDayRequest(DateOnly? PlanDate, IReadOnlyList<int>? DispatchZoneIds = null, bool CreateEmptyTrips = false);

public sealed record PlanDayZoneResultDto(
    int DispatchZoneId,
    string ZoneCode,
    Guid? TripPublicId,
    string? TripCode,
    bool TripCreated,
    int OrdersAssigned,
    int OrdersSkipped,
    IReadOnlyList<TripIssueDto> Issues);

public sealed record PlanDayResultDto(
    DateOnly PlanDate,
    int TripsCreated,
    int OrdersAssigned,
    int OrdersSkipped,
    int OrdersWithoutZone,
    IReadOnlyList<PlanDayZoneResultDto> Zones);

/// <summary>Agregar órdenes a la ruta (atómico; máximo 200 por solicitud).</summary>
public sealed record TripOrdersAddRequest(IReadOnlyList<Guid>? OrderPublicIds, string? RowVersion = null);

public sealed record TripOrderRemoveRequest(string? RowVersion = null);

/// <summary>Lista 'Sin asignar': dispatchZoneId y noZone son excluyentes; Search se aplica después de los filtros.</summary>
public sealed record UnassignedOrdersQuery(
    int? DispatchZoneId = null,
    bool NoZone = false,
    string? PostalCode = null,
    string? City = null,
    Guid? ClientPublicId = null,
    DateOnly? RequestedFrom = null,
    DateOnly? RequestedTo = null,
    string? Search = null,
    int Skip = 0,
    int Take = 100,
    /// <summary>DELIVERY (por omisión): envíos por entregar. PICKUP: envíos «Sometidos» por recoger (rutas de recogida).</summary>
    string? Kind = null);

public sealed record UnassignedOrderDto(
    int Id,
    Guid PublicId,
    string OrderNumber,
    string PackBatchNumber,
    string ClientInvoiceNumber,
    string ClientName,
    string? ConsigneeName,
    string City,
    string? PostalCode,
    int? DispatchZoneId,
    string? ZoneCode,
    bool ZoneAmbiguous,
    string StatusCode,
    string Status,
    DateTime? RequestedDate,
    DateTime? WindowStartUtc,
    DateTime? WindowEndUtc,
    int? Pieces,
    decimal? WeightKg,
    decimal? VolumeM3,
    GeoPointDto? Point,
    string? GeocodeAccuracyCode,
    string StopKind = "DELIVERY",
    string? ServiceType = null,
    bool IsRoundTrip = false);

public sealed record UnassignedOrderPageDto(int Total, int Skip, int Take, IReadOnlyList<UnassignedOrderDto> Items);

// ================================================================ optimización, secuencia y pin

/// <summary>Reordenamiento manual: permutación exacta de las paradas de la versión vigente.</summary>
public sealed record RouteSequenceRequest(IReadOnlyList<int>? RouteStopIds, string? RowVersion = null);

/// <summary>Pin manual de una parada (precisión MANUAL).</summary>
public sealed record StopLocationRequest(double? Lat, double? Lng, string? RowVersion = null);

public sealed record OptimizeRequest(string? RowVersion = null);

public sealed record OptimizationResultDto(
    int RunId,
    string EngineCode,
    string StatusCode,
    int RouteVersion,
    int AssignedCount,
    int UnassignedCount,
    IReadOnlyList<UnassignedStopDto> Unassigned,
    string? Polyline,
    TripDetailDto Trip);

public sealed record OptimizationRunDto(
    int Id,
    string EngineCode,
    string Engine,
    string StatusCode,
    string Status,
    int? RouteId,
    int? RouteVersion,
    int? UnassignedCount,
    decimal? TotalDistanceKm,
    int? TotalDurationMin,
    string? ErrorMessage,
    DateTime StartedAtUtc,
    DateTime? CompletedAtUtc,
    string? StartedBy);

// ================================================================ despacho, salida, monitoreo y escaneo

/// <summary>Ruta del selector de despacho con sus avisos y bloqueantes; CanDispatch = sin bloqueantes.</summary>
public sealed record DispatchableTripDto(TripListItemDto Trip, bool CanDispatch, IReadOnlyList<TripIssueDto> Issues);

public sealed record TripDispatchRequest(string? Comment = null, string? RowVersion = null);

/// <summary>Despacho en lote (máximo 50 rutas; cada una en su propia transacción).</summary>
public sealed record TripBatchDispatchRequest(IReadOnlyList<Guid>? TripPublicIds, string? Comment = null);

public sealed record TripDispatchResultItemDto(Guid TripPublicId, string? Code, bool Dispatched, string? Error, IReadOnlyList<TripIssueDto> Issues);

public sealed record TripBatchDispatchResultDto(int Requested, int Dispatched, IReadOnlyList<TripDispatchResultItemDto> Items);

/// <summary>Salida de la ruta (DISPATCHED → IN_PROGRESS).</summary>
public sealed record TripStartRequest(string? Comment = null, string? RowVersion = null);

/// <summary>Cierre de la ruta (IN_PROGRESS → COMPLETED). Solo si todas sus órdenes ya terminaron.</summary>
public sealed record TripCompleteRequest(string? Comment = null, string? RowVersion = null);

/// <summary>Monitor: sin fecha = hoy en la zona de la compañía; los totales no cambian con Search.</summary>
public sealed record MonitorQuery(DateOnly? Date = null, int? DispatchZoneId = null, string? Search = null, bool IncludeCompleted = true);

public sealed record MonitorTripDto(
    Guid PublicId,
    string Code,
    DateOnly PlanDate,
    string? ZoneCode,
    string? DriverCode,
    string? DriverName,
    string? VehicleCode,
    string StatusCode,
    string Status,
    int TotalStops,
    int CompletedStops,
    int FailedStops,
    int PendingStops,
    int ApproximateStops,
    DateTime? NextEtaUtc,
    DateTime? PlannedEndUtc,
    bool OverStopLimit,
    DriverPingDto? LastPing);

public sealed record MonitorTotalsDto(
    int Trips,
    int TotalStops,
    int CompletedStops,
    int FailedStops,
    int PendingStops,
    int OverStopLimitTrips,
    int TripsWithoutPing);

public sealed record MonitorDto(DateOnly Date, MonitorTotalsDto Totals, IReadOnlyList<MonitorTripDto> Trips);

/// <summary>Escaneo Outbound: número de orden, empaque o factura; sin fecha = hoy en la zona de la compañía.</summary>
/// <summary>`TripPublicId` (Lote 216, app móvil): la ruta que escogió quien escanea; sin ella se usa la ruta abierta de la zona del envío, como siempre.</summary>
public sealed record OutboundScanRequest(string? Code, DateOnly? PlanDate = null, Guid? TripPublicId = null);

/// <summary>Lote 107: escaneo con acción escogida: Action = PICKUP, ARRIVE, ROUTE o DEPART; Code = número de orden, empaque o factura.</summary>
/// <summary>`ConfirmBeyond` (Lote 216): sin él, si el envío de la pieza ya va MÁS ADELANTE que la acción, no se cuenta nada y la respuesta es `BEYOND` (voz `warn`) con el mensaje; con `true` la pieza se cuenta sin que el estatus retroceda.
/// `TripPublicId` (Lote 216): solo con ROUTE; la ruta escogida donde se pone el envío (planeada, despachada o en curso). Sin ella, la ruta abierta de su zona.</summary>
public sealed record ScanActionRequest(string? Action, string? Code, DateOnly? PlanDate = null, Guid? TripPublicId = null, bool? ConfirmBeyond = null);

/// <summary>Lote 110: cerrar el conteo de una orden (la última acción escaneada): lo que no se escaneó queda faltante «Por verificar».</summary>
/// <summary>Cerrar el conteo de una orden. Confirm: la estación (web y app) manda true después de mostrar el mensaje con las piezas sin escanear y la orden nueva de las que aparecieron; sin confirmar, si hay algo que decir, no se hace nada. PlanDate: el día de la ruta cuando la orden nueva debe enrutarse.</summary>
public sealed record ScanCloseCountRequest(Guid? OrderPublicId, bool? Confirm = null, DateOnly? PlanDate = null);

/// <summary>Lo que pasará al cerrar el conteo: las piezas que no se escanearon (quedarán «Por verificar») y, si aparecieron piezas perdidas o reclamadas, la orden nueva que se crea con ellas (mismo cliente y consignatario).</summary>
public sealed record ScanClosePreviewDto(string OrderNumber, string ClientName, string? ConsigneeName, string? NewOrderStatusLabel, IReadOnlyList<string> ReappearedNumbers, IReadOnlyList<string> MissingNumbers);

/// <summary>Con NeedsConfirmation = true no se hizo nada: falta la confirmación (Preview dice qué pasará). Con la orden nueva creada, NewOrder* la identifica.</summary>
public sealed record ScanCloseCountResultDto(string Message, Guid? OrderPublicId, string? OrderNumber, int Scanned, int Total, int Missing, IReadOnlyList<string> MissingNumbers,
    bool NeedsConfirmation = false, ScanClosePreviewDto? Preview = null, Guid? NewOrderPublicId = null, string? NewOrderNumber = null, string? NewWaybillNumber = null);

/// <summary>Resultado de aplicar la acción: Outcome (APPLIED, ALREADY_BEYOND, FINAL, NO_ROUTE, NOT_FOUND, AMBIGUOUS), palabra de voz, mensaje, el estatus de antes y de después y los que se brincaron; con Enrutar, la zona y la ruta.</summary>
public sealed record ScanActionResultDto(
    string Action,
    string Outcome,
    string Voice,
    string Message,
    Guid? OrderPublicId,
    string? OrderNumber,
    string? PackBatchNumber,
    string? FromStatus,
    string? FromLabel,
    string? ToStatus,
    string? ToLabel,
    IReadOnlyList<string> Skipped,
    string? ZoneCode,
    Guid? TripPublicId,
    string? TripCode,
    string? ReasonCode,
    string? MatchedBy = null,
    int? Scanned = null,      // Lote 110: piezas de la orden escaneadas con esta acción…
    int? Total = null,        // …de las que tiene la orden
    int? Missing = null,      // y las que ya quedaron faltantes «Por verificar»
    string? PieceNumber = null,
    int? FailedAttempts = null,   // Lote 113: intentos fallidos vigentes de la orden…
    bool? SuggestReturn = null);  // …y si toca SUGERIR devolverla (nunca bloquea)

/// <summary>Resultado tipado del escaneo (Outcome), palabra de voz (found/dup/notfound) y mensaje exacto.</summary>
public sealed record OutboundScanResultDto(
    string Outcome,
    string Voice,
    string Message,
    string? MatchedBy,
    Guid? OrderPublicId,
    string? OrderNumber,
    string? PackBatchNumber,
    string? ZoneCode,
    Guid? TripPublicId,
    string? TripCode,
    string? ReasonCode);

// ---------------------------------------------------------------- Lote 216: buscar una pieza (lupa de la estación de escaneo)

/// <summary>Una acción con la que se escaneó la pieza, cuándo y quién.</summary>
public sealed record ScanLookupScanDto(string Action, DateTime ScannedAtUtc, string? ByName);

/// <summary>
/// Lo que hay en la base de una pieza (o, si el código no es de una pieza, de un envío): el envío con su estatus (también cancelado, final o dado de baja), la reclamación si la hay, la ruta y el chofer, y las acciones con que se escaneó.
/// `Flags`: PIECE_INACTIVE, ORDER_INACTIVE, ORDER_CANCELLED, ORDER_RETURNED, ORDER_FINAL, PIECE_TO_VERIFY, PIECE_LOST, PIECE_CLAIMED, PIECE_RESOLVED, REAPPEARED, IN_ROUTE. `PieceNumber` null = coincidió con el envío, no con una pieza.
/// </summary>
public sealed record ScanLookupItemDto(string? PieceNumber, bool PieceActive, string? Missing, string? ClaimNumber, string? ClaimStatus, DateTime? ReappearedAtUtc, IReadOnlyList<ScanLookupScanDto> Scans,
    Guid OrderPublicId, string OrderNumber, string? WaybillNumber, string? PackBatchNumber, bool OrderActive, string StatusCode, string Status, string StageKind,
    string? ClientName, string? ConsigneeName, string? City, string? TripCode, string? TripStatus, string? DriverName, int Pieces, IReadOnlyList<string> Flags);

/// <summary>`MatchedBy`: PIECE, o el campo del envío que coincidió (ORDER_NUMBER, WAYBILL, PACK_BATCH, INVOICE); null si no hay nada. Varios resultados si el número se repite.</summary>
public sealed record ScanLookupDto(string Code, string? MatchedBy, IReadOnlyList<ScanLookupItemDto> Items);
