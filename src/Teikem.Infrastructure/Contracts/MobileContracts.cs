namespace Teikem.Infrastructure.Contracts;

/// <summary>
/// Lote 214 — órdenes en la app móvil del personal. Una fila del buscador: lo mínimo para reconocer la orden en la calle (número, conduce, empaque, cliente, consignatario y estatus).
/// </summary>
public sealed record MobileOrderRowDto(Guid PublicId, string OrderNumber, string? WaybillNumber, string PackBatchNumber, string ClientName, string ConsigneeName, string ConsigneeCity,
    string StatusCode, string Status, bool IsInitialStatus, int TotalPieces, decimal? CodAmount);

/// <summary>Resultado del buscador: `MatchedBy` dice si hubo coincidencia EXACTA (WAYBILL, ORDER_NUMBER, PACK_BATCH, INVOICE, PACKAGE: lo que lee el escáner) o null si fue búsqueda libre.</summary>
public sealed record MobileOrderSearchDto(string Query, string? MatchedBy, IReadOnlyList<MobileOrderRowDto> Items);

/// <summary>Un intento de entrega ya anotado (los fallidos y el bueno), con su motivo resuelto en el idioma del usuario y quién lo hizo.</summary>
public sealed record MobileAttemptDto(int Number, DateTime AttemptedAtUtc, bool Delivered, string? Reason, string? Notes, string Source, string? Driver, int? DistanceFromStopMeters);

/// <summary>El comprobante de entrega, resumido: quién recibió, cuándo, desde dónde (STAFF_WEB o DRIVER_APP) y a cuántos metros de la dirección.</summary>
public sealed record MobileProofDto(DateTime DeliveredAtUtc, string RecipientName, string? Notes, string Source, int? DistanceFromStopMeters, bool HasSignature, int Photos);

/// <summary>
/// La ficha de la orden para el personal en la calle: la misma ficha «en la puerta» del chofer (`Shipment`, con su teléfono, piezas, cobro pendiente y exigencias del cliente) más la ruta vigente y su chofer,
/// el comprobante y los intentos. `CanDeliver`/`CanFail`/`CanPickup` dicen qué acción cabe por el estatus y el tipo (no por permiso: el permiso lo evalúa la app con `orders.deliver`).
/// Sin ruta vigente, una orden en su etapa inicial se trata como recogida y las demás como entrega.
/// </summary>
public sealed record MobileOrderDto(DriverShipmentDetailDto Shipment, Guid ClientPublicId, string PackBatchNumber, string? ClientInvoiceNumber, string ServiceTypeLabel, DateTime CreatedAtUtc,
    bool IsInitialStatus, string? TripCode, string? TripStatus, string? DriverName, double? Latitude, double? Longitude,
    bool CanDeliver, bool CanFail, bool CanPickup, MobileProofDto? Proof, IReadOnlyList<MobileAttemptDto> Attempts);

// ---------------------------------------------------------------------------
// Lote 215 — asignar en la calle (permiso trips.streetassign). Rutas de hoy se lee con los endpoints de siempre (GET /trips, GET /trips/{id}, trips.view).
// ---------------------------------------------------------------------------

/// <summary>
/// Una ruta a la que se puede mandar la orden (PLANNED, DISPATCHED o IN_PROGRESS, del mismo tipo que la orden: entrega o recogida), con su chofer y, si lo mandó el teléfono, su ÚLTIMA ubicación conocida y la distancia en línea recta
/// al domicilio de la orden (`DistanceKm`: null si no hay ubicación o la dirección no tiene coordenadas). `IsCurrent`: es la ruta donde ya está la orden.
/// </summary>
public sealed record StreetTripDto(Guid PublicId, string Code, string Kind, string StatusCode, string Status, bool Accepted, Guid? DriverPublicId, string? DriverName, string? VehicleCode, int StopCount,
    bool IsCurrent, double? DriverLat, double? DriverLng, DateTime? DriverPingAtUtc, double? DistanceKm);

public sealed record StreetDriverDto(Guid PublicId, string Name, bool HasOpenTrip);

public sealed record StreetVehicleDto(Guid PublicId, string Code, string? Plate);

/// <summary>
/// Dónde se puede mandar una orden: las rutas abiertas de hoy (ordenadas por cercanía al domicilio), y para armar una ruta rápida los choferes y vehículos activos.
/// `Kind` es DELIVERY o PICKUP (una orden en su etapa inicial se recoge). `Movable`: false si el estatus de la orden ya no se puede mover (entregada, fallida…): `Reason` dice por qué.
/// </summary>
public sealed record StreetTargetsDto(Guid OrderPublicId, string OrderNumber, string Kind, string StatusCode, string Status, bool Movable, string? Reason,
    Guid? CurrentTripPublicId, string? CurrentTripCode, double? AddressLat, double? AddressLng,
    IReadOnlyList<StreetTripDto> Trips, IReadOnlyList<StreetDriverDto> Drivers, IReadOnlyList<StreetVehicleDto> Vehicles);

public sealed record StreetAssignRequest(Guid OrderPublicId, Guid TripPublicId);
public sealed record StreetUnassignRequest(Guid OrderPublicId);
public sealed record StreetQuickRouteRequest(IReadOnlyList<Guid>? OrderPublicIds, Guid DriverPublicId, Guid VehiclePublicId);

/// <summary>`Action`: ASSIGNED (de sin asignar a una ruta), MOVED (de una ruta a otra), UNASSIGNED (de una ruta a «Sin asignar») o QUICK_ROUTE. `DriverNotified`: se le dejó el aviso al chofer (le llega al teléfono en menos de un minuto).</summary>
public sealed record StreetResultDto(string Action, Guid? OrderPublicId, string? OrderNumber, string? OrderStatusCode, string? OrderStatus, Guid? TripPublicId, string? TripCode, bool DriverNotified);
