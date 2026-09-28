using Teikem.Domain.Common;

namespace Teikem.Domain.Entities;

/// <summary>
/// Lote 8A — aparato de confianza de la app de almacén (terminal con escáner). Lo registra el administrador
/// (devices.manage) con un código único por compañía; el aparato se enlaza una sola vez con el código de registro de un solo
/// uso (8 caracteres, 24 h) y desde entonces se autentica con su secreto. Código de registro y secreto se guardan SOLO como
/// hash ([SensitiveData]: nunca salen en un DTO ni en la bitácora) y se muestran en claro una sola vez.
/// DriverDevice (Lote 4, push del chofer) se conserva; 8B podrá migrarlo a esta tabla.
/// Soft delete: desactivar (IsActive = 0) revoca las sesiones emitidas al aparato (RefreshToken.UserDeviceId).
/// Los datos técnicos (último contacto, último usuario, versión de la app) viven en <see cref="UserDeviceActivity"/> para que
/// el heartbeat y el login no cambien el RowVersion de esta fila (token de concurrencia de las ediciones del administrador).
/// </summary>
[AuditEntity(Constants.EntityTypes.UserDevice)]
public class UserDevice : ITenantScoped, ISoftDeletable
{
    public int UserDeviceId { get; set; }
    [NotAudited] public Guid PublicId { get; set; } = Guid.NewGuid();
    public int TenantId { get; set; }
    /// <summary>Código visible del aparato (p. ej. ZB-01), único por compañía (UQ_UserDevice_Code), hasta 30 caracteres.</summary>
    public string Code { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string? Model { get; set; }
    /// <summary>LookupCode Entity='DevicePlatform' (ANDROID en esta versión).</summary>
    public int PlatformLookupId { get; set; }
    /// <summary>Hash del código de registro pendiente (null = sin código vigente; es de un solo uso).</summary>
    [SensitiveData] public string? EnrollCodeHash { get; set; }
    public DateTime? EnrollCodeExpiresUtc { get; set; }
    /// <summary>Hash del secreto del aparato (null = aún no se registra en el aparato).</summary>
    [SensitiveData] public string? SecretHash { get; set; }
    public DateTime? EnrolledAtUtc { get; set; }
    public int? DefaultWarehouseId { get; set; }
    /// <summary>LookupCode Entity='UiTheme' (LIGHT | DARK): tema por defecto del aparato.</summary>
    public int? ThemeLookupId { get; set; }
    public int? RegisteredBy { get; set; }
    [NotAudited] public DateTime RegisteredAtUtc { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; } = true;
    /// <summary>
    /// Sello de sesiones: los access tokens con claim `did` de este aparato emitidos (claim `iat`) antes de este instante se
    /// rechazan en OnTokenValidated. Lo fijan la baja, la reactivación y el registro (enroll); null = sin corte.
    /// </summary>
    [NotAudited] public DateTime? SessionsNotBeforeUtc { get; set; }
    [NotAudited] public byte[]? RowVersion { get; set; }
}

/// <summary>
/// Lote 8A — datos técnicos del aparato (1:1 con UserDevice, PK = UserDeviceId): último contacto (heartbeat o login con PIN),
/// último usuario que entró y versión de la app. Tabla aparte y sin RowVersion: escribirla no cambia el RowVersion de
/// UserDevice, así que el PATCH del administrador solo da 409 ante ediciones reales. No se audita (sin [AuditEntity]).
/// </summary>
public class UserDeviceActivity : ITenantScoped
{
    public int UserDeviceId { get; set; }
    public int TenantId { get; set; }
    [NotAudited] public DateTime? LastSeenUtc { get; set; }
    [NotAudited] public int? LastUserId { get; set; }
    [NotAudited] public string? AppVersion { get; set; }
}
