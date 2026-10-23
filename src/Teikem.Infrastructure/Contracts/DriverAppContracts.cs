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
    string? ContactPerson, string? Phone, string Address, string? City, string? PostalCode, int? Pieces, decimal? CodAmount, string? Notes, string? ClientName, bool CanClose,
    bool SignatureRequired = false, bool PhotoRequired = false, decimal? CodPending = null);

/// <summary>Un cobro contra entrega hecho al entregar. `ClientId` lo genera la app (un reintento sin señal no cobra dos veces); `Method` es CASH, CHECK, COMPANY_CHECK, ATH_MOVIL o CARD; los cheques llevan `CheckNumber`.</summary>
public sealed record DriverCodDto(Guid ClientId, string? Method, decimal? Amount, string? CheckNumber);

/// <summary>
/// Entregar un envío. `RecipientName` quien recibió (obligatorio); `SignaturePngBase64` la firma de quien recibió y `PhotosBase64` fotos (JPG, hasta 3) son opcionales; `DeliveredAtUtc` cuándo (la app lo manda porque pudo entregar sin señal);
/// `Latitude`/`Longitude` del teléfono al entregar.
/// </summary>
public sealed record DriverDeliverRequest(string? RecipientName, string? Notes, DateTime? DeliveredAtUtc, double? Latitude, double? Longitude, string? SignaturePngBase64, IReadOnlyList<string>? PhotosBase64, IReadOnlyList<DriverCodDto>? Cods = null);

/// <summary>No entregar un envío: `ReasonCode` de la lista «Razón de fallo» (obligatorio), nota y foto opcionales.</summary>
public sealed record DriverFailRequest(string? ReasonCode, string? Notes, DateTime? AttemptedAtUtc, string? PhotoBase64, double? Latitude = null, double? Longitude = null);

/// <summary>`AlreadyDone`: el envío ya estaba cerrado por la app (un reintento de la cola sin señal): no se repite ni se duplica nada.</summary>
public sealed record DriverShipmentResultDto(Guid ShipmentPublicId, string StatusCode, string Status, bool AlreadyDone, int? DistanceFromStopMeters = null);

// ---- seguimiento de la ruta (GPS) ----

/// <summary>Los valores del seguimiento, globales (appsettings `DriverApp:Tracking`): la app los pide al arrancar y se ajustan sin publicar una versión nueva.</summary>
public sealed record DriverTrackingConfigDto(int MinDistanceMeters, int MaxIntervalSeconds, int UploadEverySeconds, int RetentionDays);

/// <summary>Un punto de ubicación. La hora es la de captura en el teléfono.</summary>
public sealed record DriverTrackingPointDto(double Latitude, double Longitude, DateTime CapturedAtUtc, decimal? SpeedKmh, int? HeadingDeg);

/// <summary>Un aviso del seguimiento (STARTED, STOPPED_BY_DRIVER, PERMISSION_LOST, RESUMED) y cuándo ocurrió.</summary>
public sealed record DriverTrackingEventDto(string Type, DateTime OccurredAtUtc);

public sealed record DriverTrackingUploadRequest(IReadOnlyList<DriverTrackingPointDto>? Points, IReadOnlyList<DriverTrackingEventDto>? Events);

/// <summary>`RouteActive` = el chofer tiene una ruta EN CURSO en esta compañía (si no, los puntos no se guardan). `Accepted`: puntos nuevos guardados; `Skipped`: repetidos o descartados (fuera de rango, futuros o muy viejos).</summary>
public sealed record DriverTrackingUploadResultDto(bool RouteActive, int Accepted, int Skipped, int Events);

public sealed record TripTrackPointDto(double Latitude, double Longitude, decimal? SpeedKmh, DateTime CapturedAtUtc);
public sealed record TripTrackingDto(Guid TripPublicId, string TripCode, IReadOnlyList<TripTrackPointDto> Points, IReadOnlyList<DriverTrackingEventDto> Events);

// ---- teléfono y avisos emergentes ----
/// <summary>Registra el teléfono del chofer para los avisos emergentes: `Platform` ANDROID o IOS, `PushToken` el token de FCM.</summary>
public sealed record DriverDeviceRegisterRequest(string? Platform, string? PushToken, string? AppVersion);

// ---- avisos ----
public sealed record DriverNoticeDto(Guid PublicId, string Kind, string Title, string? Body, DateTime CreatedAtUtc, bool Read, Guid? TripPublicId);
public sealed record DriverNoticesDto(int Unread, IReadOnlyList<DriverNoticeDto> Items);
/// <summary>Marcar leídos: los de `Ids`, o todos con `All` = true.</summary>
public sealed record DriverNoticesReadRequest(IReadOnlyList<Guid>? Ids, bool? All);
/// <summary>La oficina escribe un aviso a un chofer.</summary>
public sealed record DriverNoticeSendRequest(Guid? DriverPublicId, string? Title, string? Body);
