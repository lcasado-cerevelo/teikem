namespace Teikem.Infrastructure.Contracts;

/// <summary>
/// App del chofer (2026-10-23). Un manifiesto es una ruta (Trip) despachada al chofer. `Kind`: DELIVERY (entregas) o PICKUP (recogidas; llegan en la etapa de recogidas).
/// `Accepted` = el chofer ya lo aceptó desde la app (AcceptedAtUtc).
/// </summary>
public sealed record DriverManifestDto(Guid PublicId, string Code, string Kind, DateOnly PlanDate, string StatusCode, string Status, bool Accepted, DateTime? AcceptedAtUtc,
    int ShipmentCount, string? RowVersion);

public sealed record DriverShipmentDto(Guid PublicId, string OrderNumber, string WaybillNumber, string StatusCode, string Status, int Sequence, string? ConsigneeName,
    string Address, string? City, string? PostalCode, int? Pieces, decimal? CodAmount, string? Notes);

public sealed record DriverManifestDetailDto(Guid PublicId, string Code, string Kind, DateOnly PlanDate, string StatusCode, string Status, bool Accepted, DateTime? AcceptedAtUtc,
    string? RowVersion, IReadOnlyList<DriverShipmentDto> Shipments);

/// <summary>Un motivo de la lista «Motivos de no aceptación».</summary>
public sealed record DriverDeclineReasonDto(string Code, string Label);

/// <summary>
/// Aceptar el manifiesto: los envíos que NO acepta van en `Declines` con su motivo (código de la lista) y una nota opcional; el resto se acepta. Sin `Declines`, acepta todos.
/// </summary>
public sealed record DriverDeclineItem(Guid ShipmentPublicId, string ReasonCode, string? Note);
public sealed record DriverAcceptRequest(IReadOnlyList<DriverDeclineItem>? Declines, string? RowVersion);

/// <summary>`Started`: la ruta salió (quedó En curso y los envíos aceptados En tránsito). Si no aceptó ninguno, no sale (`Started` = false).</summary>
public sealed record DriverAcceptResultDto(Guid ManifestPublicId, int Accepted, int Declined, bool Started);

/// <summary>Un motivo de «No entregado» (lista «Razón de fallo»).</summary>
public sealed record DriverFailureReasonDto(string Code, string Label);

/// <summary>El envío con lo que el chofer necesita en la puerta: contacto, piezas y notas. `Delivered`/`Failed` dicen si ya se cerró desde la app.</summary>
public sealed record DriverShipmentDetailDto(Guid PublicId, Guid ManifestPublicId, string OrderNumber, string WaybillNumber, string StatusCode, string Status, string? ConsigneeName,
    string? ContactPerson, string? Phone, string Address, string? City, string? PostalCode, int? Pieces, decimal? CodAmount, string? Notes, string? ClientName, bool CanClose);

/// <summary>
/// Entregar un envío. `RecipientName` quien recibió (obligatorio); `SignaturePngBase64` la firma de quien recibió y `PhotosBase64` fotos (JPG, hasta 3) son opcionales; `DeliveredAtUtc` cuándo (la app lo manda porque pudo entregar sin señal);
/// `Latitude`/`Longitude` del teléfono al entregar.
/// </summary>
public sealed record DriverDeliverRequest(string? RecipientName, string? Notes, DateTime? DeliveredAtUtc, double? Latitude, double? Longitude, string? SignaturePngBase64, IReadOnlyList<string>? PhotosBase64);

/// <summary>No entregar un envío: `ReasonCode` de la lista «Razón de fallo» (obligatorio), nota y foto opcionales.</summary>
public sealed record DriverFailRequest(string? ReasonCode, string? Notes, DateTime? AttemptedAtUtc, string? PhotoBase64);

/// <summary>`AlreadyDone`: el envío ya estaba cerrado por la app (un reintento de la cola sin señal): no se repite ni se duplica nada.</summary>
public sealed record DriverShipmentResultDto(Guid ShipmentPublicId, string StatusCode, string Status, bool AlreadyDone);
