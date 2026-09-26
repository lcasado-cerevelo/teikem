namespace Teikem.Domain.Fleet;

/// <summary>Problema de disponibilidad para despacho: bloqueante (el recurso no se puede despachar) o solo aviso.</summary>
public sealed record AvailabilityIssue(string Code, string Message, bool Blocking);

/// <summary>Resultado de evaluar un chofer o vehículo: disponible si ningún problema es bloqueante.</summary>
public sealed record AvailabilityResult(bool Available, IReadOnlyList<AvailabilityIssue> Issues);

/// <summary>Documento (licencia, certificación o documento de vehículo) visto por las reglas de disponibilidad.</summary>
public sealed record AvailabilityDocSnapshot(string TypeCode, string TypeLabel, DateOnly? ExpiryDate, bool IsSuperseded);

/// <summary>Foto del chofer para evaluar su disponibilidad sin BD (la arma FleetAvailabilityService por lote).</summary>
public sealed record DriverAvailabilitySnapshot(
    int DriverId,
    bool IsActive,
    bool IsInitialStatus,
    bool IsTerminal,
    string StatusLabel,
    IReadOnlyList<AvailabilityDocSnapshot> Licenses,
    IReadOnlyList<AvailabilityDocSnapshot> Certifications);

/// <summary>Foto del vehículo para evaluar su disponibilidad sin BD.</summary>
public sealed record VehicleAvailabilitySnapshot(
    int VehicleId,
    bool IsActive,
    bool IsInitialStatus,
    bool IsTerminal,
    string StatusLabel,
    IReadOnlyList<AvailabilityDocSnapshot> Documents,
    IReadOnlyList<string> InProgressWorkOrderNumbers);

/// <summary>Códigos de los problemas de disponibilidad (contrato estable con el FE y con Despacho).</summary>
public static class AvailabilityIssueCodes
{
    // Bloquean
    public const string DriverInactive = "DRIVER_INACTIVE";
    public const string DriverStatus = "DRIVER_STATUS";
    public const string NoValidLicense = "NO_VALID_LICENSE";
    public const string VehicleInactive = "VEHICLE_INACTIVE";
    public const string VehicleStatus = "VEHICLE_STATUS";
    public const string VehicleDocExpired = "VEHICLE_DOC_EXPIRED";
    public const string WorkOrderInProgress = "WORK_ORDER_IN_PROGRESS";
    // Solo avisan
    public const string CertExpired = "CERT_EXPIRED";
    public const string DocExpiring = "DOC_EXPIRING";
    public const string VehicleNoDocuments = "VEHICLE_NO_DOCUMENTS";
}
