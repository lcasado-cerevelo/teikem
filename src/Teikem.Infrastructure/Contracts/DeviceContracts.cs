namespace Teikem.Infrastructure.Contracts;

// ======================================================================================================================
// Lote 8A — aparatos de confianza de la app de almacén (UserDevice), PIN por usuario y login por aparato. Firma posicional
// FIJA (el cliente del aparato se genera desde openapi.json). Nunca viajan hashes: el código de registro y el secreto del
// aparato salen en claro UNA sola vez (DeviceCreatedDto.EnrollCode y DeviceEnrolledDto.DeviceSecret).
// ======================================================================================================================

/// <summary>
/// Alta de un aparato (devices.manage). Code: obligatorio, hasta 30, único por compañía (409 'Ya existe un aparato con ese
/// código.'). DefaultWarehousePublicId: almacén por defecto (opcional). Theme: LIGHT | DARK (LIGHT si no se indica).
/// </summary>
public sealed record DeviceCreateRequest(string? Code, string? Name, string? Model, Guid? DefaultWarehousePublicId, string? Theme);

/// <summary>
/// Aparato para la pantalla de administración. IsEnrolled = ya se registró en el aparato (tiene secreto).
/// EnrollCodeExpiresUtc = vencimiento del código de registro pendiente (null si no hay). Platform y Theme son códigos
/// (ANDROID; LIGHT | DARK). RowVersion en base64 para el PATCH con concurrencia optimista.
/// </summary>
public sealed record DeviceDto(
    Guid PublicId, string Code, string? Name, string? Model, string? Platform, string? AppVersion, bool IsEnrolled, DateTime? EnrolledAtUtc,
    DateTime? EnrollCodeExpiresUtc, DateTime? LastSeenUtc, int? LastUserId, string? LastUserName, Guid? DefaultWarehousePublicId,
    string? DefaultWarehouseCode, string? Theme, DateTime RegisteredAtUtc, bool IsActive, string? RowVersion);

/// <summary>Aparato recién dado de alta (o con código regenerado) y su código de registro de 8 caracteres: se muestra UNA vez, vence en 24 h.</summary>
public sealed record DeviceCreatedDto(DeviceDto Device, string EnrollCode);

/// <summary>
/// Edición del aparato (PATCH): null = sin cambio. Name "" = quitar el nombre. DefaultWarehousePublicId fija el almacén
/// por defecto; ClearDefaultWarehouse = true lo quita. Theme: LIGHT | DARK. RowVersion opcional (409 si cambió).
/// </summary>
public sealed record DevicePatchRequest(string? Name, Guid? DefaultWarehousePublicId, bool ClearDefaultWarehouse = false, string? Theme = null, string? RowVersion = null);

/// <summary>
/// Registro en el aparato (anónimo) con el código de un solo uso; Model y AppVersion informativos. Los campos de entrada de
/// estos contratos son anulables a propósito: si faltan, responde el servicio con su mensaje en español (p. ej. 401 'El código
/// de registro no es válido o venció.'), no el 400 genérico de MVC en inglés.
/// </summary>
/// <remarks>2026-09-30: RegisteredDevicePublicIds = registros que el teléfono ya tiene (uno por compañía). Si el código es de
/// una compañía que ya está entre ellos, se rechaza ANTES de consumir el código (409) para que el administrador dé otro.</remarks>
public sealed record DeviceEnrollRequest(string? EnrollCode, string? Model, string? AppVersion, IReadOnlyList<Guid>? RegisteredDevicePublicIds = null);

/// <summary>
/// Aparato registrado: su PublicId y su secreto (se muestra UNA sola vez; el aparato lo guarda cifrado), compañía, almacén y
/// tema. Lote 16: DefaultWarehouseReceivingMode = modo de recepción del almacén por defecto (PUTAWAY | DIRECT; null sin almacén).
/// </summary>
public sealed record DeviceEnrolledDto(Guid DevicePublicId, string DeviceSecret, string TenantName, Guid? DefaultWarehousePublicId, string? Theme,
    string? DefaultWarehouseReceivingMode = null);

/// <summary>Lista de usuarios que pueden entrar en el aparato (anónimo: se autentica con aparato + secreto).</summary>
public sealed record DeviceUsersRequest(Guid DevicePublicId, string? DeviceSecret);

/// <summary>Usuario elegible en el aparato: activo en la compañía, con PIN definido y con inventory.view.</summary>
public sealed record DeviceUserDto(int UserId, string FullName, string Initials);

/// <summary>Login por aparato: aparato + secreto + usuario elegido + PIN (401 'PIN incorrecto.'; 423 'PIN bloqueado por 15 minutos.').</summary>
public sealed record DeviceLoginRequest(Guid DevicePublicId, string? DeviceSecret, int UserId, string? Pin);

/// <summary>Mi cuenta: definir o cambiar el PIN propio; exige la contraseña actual (400 'La contraseña actual es incorrecta.').</summary>
public sealed record PinSetRequest(string? CurrentPassword, string? Pin);

/// <summary>Administración (devices.manage o admin.users): asignar o restablecer el PIN de otro usuario de la compañía.</summary>
public sealed record PinAdminSetRequest(string? Pin);

/// <summary>Estado del PIN (nunca el PIN): definido, bloqueado hasta (si está bloqueado) y última actualización.</summary>
public sealed record PinStatusDto(bool HasPin, DateTime? LockedUntilUtc, DateTime? UpdatedAtUtc);

/// <summary>Latido del aparato (anónimo): aparato + secreto y versión de la app.</summary>
public sealed record HeartbeatRequest(Guid DevicePublicId, string? DeviceSecret, string? AppVersion);

/// <summary>
/// Respuesta del heartbeat: el aparato desactivado recibe IsActive = false (la app bloquea la entrada). Lote 16:
/// DefaultWarehouseReceivingMode = modo de recepción del almacén por defecto (PUTAWAY | DIRECT; null sin almacén).
/// </summary>
public sealed record DeviceHeartbeatDto(bool IsActive, Guid? DefaultWarehousePublicId, string? Theme, DateTime ServerTimeUtc,
    string? DefaultWarehouseReceivingMode = null);
