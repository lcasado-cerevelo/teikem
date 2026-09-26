using Teikem.Domain.Catalogs;
using Teikem.Domain.Common;
using Teikem.Domain.Identity;

namespace Teikem.Domain.Fleet;

/// <summary>
/// Chofer (Lote 4, maestro 'Choferes y tarifas'). EmployeeCode es el 'Código': obligatorio, único por compañía e inmutable
/// (UQ_Driver_EmployeeCode, sin filtro). UserId vincula un usuario INTERNAL (único por compañía, UX_Driver_User). Estatus
/// DriverStatus vía StatusService: ACTIVE ↔ UNAVAILABLE (lateral), INACTIVE terminal ('Eliminar chofer', sin borrado
/// físico). Es el agregado raíz de sus tarifas (DriverDeliveryRate/DriverAttemptRate/DriverTripRate) y viajes (DriverTrip).
/// MaxStopsPerRoute NULL = usa Tenant.MaxStopsPerRouteDefault.
/// </summary>
[AuditEntity(Constants.EntityTypes.Driver)]
public class Driver : ITenantScoped, ISoftDeletable, IHasStatus, IAuditStamped
{
    public int DriverId { get; set; }
    [NotAudited] public Guid PublicId { get; set; }
    public int TenantId { get; set; }
    public string EmployeeCode { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public int? UserId { get; set; }
    public DateOnly? HireDate { get; set; }
    public int? HomeWarehouseId { get; set; }
    public int StatusCodeId { get; set; }
    public int? MaxStopsPerRoute { get; set; }
    public bool IsActive { get; set; } = true;
    [NotAudited] public DateTime CreatedAtUtc { get; set; }
    public int? CreatedBy { get; set; }
    [NotAudited] public DateTime? UpdatedAtUtc { get; set; }
    public int? UpdatedBy { get; set; }
    [NotAudited] public byte[]? RowVersion { get; set; }

    public StatusCode? Status { get; set; }
    public ApplicationUser? User { get; set; }
    public ICollection<DriverLicense> Licenses { get; set; } = new List<DriverLicense>();
    public ICollection<DriverCertification> Certifications { get; set; } = new List<DriverCertification>();
    public ICollection<DriverZone> Zones { get; set; } = new List<DriverZone>();
    public ICollection<DriverDevice> Devices { get; set; } = new List<DriverDevice>();
}

/// <summary>Licencia de conducir del chofer. Sin TenantId: se alcanza a través del chofer; auditada bajo DRIVER.</summary>
[AuditEntity(Constants.EntityTypes.Driver)]
public class DriverLicense : ISoftDeletable
{
    public int DriverLicenseId { get; set; }
    public int DriverId { get; set; }
    public int LicenseClassLookupId { get; set; }
    public string LicenseNumber { get; set; } = string.Empty;
    public DateOnly? IssuedDate { get; set; }
    public DateOnly? ExpiryDate { get; set; }
    public bool IsActive { get; set; } = true;

    public Driver? Driver { get; set; }
    public LookupCode? LicenseClass { get; set; }
}

/// <summary>Certificación del chofer (HazMat, refrigerado, montacargas). Sin TenantId; auditada bajo DRIVER.</summary>
[AuditEntity(Constants.EntityTypes.Driver)]
public class DriverCertification : ISoftDeletable
{
    public int DriverCertificationId { get; set; }
    public int DriverId { get; set; }
    public int CertTypeLookupId { get; set; }
    public string? CertNumber { get; set; }
    public DateOnly? IssuedDate { get; set; }
    public DateOnly? ExpiryDate { get; set; }
    public bool IsActive { get; set; } = true;

    public Driver? Driver { get; set; }
    public LookupCode? CertType { get; set; }
}

/// <summary>
/// Dispositivo de la app del chofer (lo registra la app del Lote 7; aquí solo se lista y se desactiva). PushToken es un
/// secreto: nunca sale en un DTO ni en la bitácora ([SensitiveData]).
/// </summary>
[AuditEntity(Constants.EntityTypes.Driver)]
public class DriverDevice : ITenantScoped, ISoftDeletable
{
    public int DriverDeviceId { get; set; }
    public int TenantId { get; set; }
    public int DriverId { get; set; }
    public int PlatformLookupId { get; set; }
    [SensitiveData] public string? PushToken { get; set; }
    public string? AppVersion { get; set; }
    [NotAudited] public DateTime? LastSeenUtc { get; set; }
    public bool IsActive { get; set; } = true;

    public Driver? Driver { get; set; }
    public LookupCode? Platform { get; set; }
}

/// <summary>
/// Zona de despacho que cubre el chofer (asignación estándar). PK (DriverId, DispatchZoneId); IsPrimary marca la zona
/// primaria, cuyo nombre es el 'Área' del chofer. Sin TenantId: se alcanza a través del chofer o de la zona.
/// </summary>
[AuditEntity(Constants.EntityTypes.Driver)]
public class DriverZone
{
    public int DriverId { get; set; }
    public int DispatchZoneId { get; set; }
    public bool IsPrimary { get; set; } = true;

    public Driver? Driver { get; set; }
    public DispatchZone? Zone { get; set; }
}
