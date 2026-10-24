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
