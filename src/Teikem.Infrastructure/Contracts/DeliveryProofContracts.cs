namespace Teikem.Infrastructure.Contracts;

// Lote 118 — registrar la entrega desde la web (permiso orders.deliver).

/// <summary>Registrar la entrega: quién recibió (obligatorio), una nota opcional y, si no fue ahora, cuándo (nunca en el futuro).</summary>
public sealed record DeliveryRegisterRequest(string? RecipientName, string? Notes, DateTime? DeliveredAtUtc);

/// <summary>
/// El comprobante de entrega. <c>SignedByName</c> es el usuario que lo registró (firma automática): <c>SignatureKind</c> USER dice que NO firmó el consignatario. <c>GeoStatus</c>: NONE, FROM_STOP, PENDING,
/// RUNNING, OK, NOT_FOUND o FAILED; las coordenadas solo vienen con FROM_STOP u OK.
/// </summary>
public sealed record DeliveryProofDto(Guid PublicId, Guid OrderPublicId, string OrderNumber, DateTime DeliveredAtUtc, string RecipientName, string? Notes, string Source, string SignatureKind,
    string? RegisteredByName, string? SignedByName, string GeoStatus, string? GeoAccuracy, double? Latitude, double? Longitude, string OrderStatusCode, string OrderStatusLabel, string? Message);
