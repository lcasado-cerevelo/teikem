using Teikem.Domain.Catalogs;
using Teikem.Domain.Common;

namespace Teikem.Domain.Fleet;

/// <summary>
/// Vehículo de la flota (Lote 4, panel Vehículos). Código fijo e irrepetible por compañía (UQ_Vehicle_Code, sin filtro: no se
/// libera tras la baja). Estatus VehicleStatus vía StatusService: ACTIVE ↔ MAINTENANCE (lateral) y INACTIVE terminal (baja
/// definitiva); el checkbox 'Activo' es IsActive reversible. El odómetro solo sube desde combustible y cierre de OT
/// (FleetQueries.LockVehicleAsync); la corrección manual queda auditada. HomeWarehouseId se mapea sin navegación
/// (Warehouse aún no está mapeado).
/// </summary>
[AuditEntity(Constants.EntityTypes.Vehicle)]
public class Vehicle : ITenantScoped, ISoftDeletable, IHasStatus, IAuditStamped
{
    public int VehicleId { get; set; }
    [NotAudited] public Guid PublicId { get; set; }
    public int TenantId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string? PlateNumber { get; set; }
    public decimal? MaxWeightKg { get; set; }
    public decimal? MaxVolumeM3 { get; set; }
    public int? MaxStops { get; set; }
    public int? VehicleTypeLookupId { get; set; }
    public int? OwnershipLookupId { get; set; }
    public int? FuelTypeLookupId { get; set; }
    public string? Make { get; set; }
    public string? Model { get; set; }
    public int? ModelYear { get; set; }
    public string? Vin { get; set; }
    public decimal? CurrentOdometerKm { get; set; }
    public int? HomeWarehouseId { get; set; }
    public int StatusCodeId { get; set; }
    public bool IsActive { get; set; } = true;
    [NotAudited] public DateTime CreatedAtUtc { get; set; }
    public int? CreatedBy { get; set; }
    [NotAudited] public DateTime? UpdatedAtUtc { get; set; }
    public int? UpdatedBy { get; set; }
    [NotAudited] public byte[]? RowVersion { get; set; }

    public LookupCode? VehicleType { get; set; }
    public LookupCode? Ownership { get; set; }
    public LookupCode? FuelType { get; set; }
    public StatusCode? Status { get; set; }
    public ICollection<VehicleDocument> Documents { get; set; } = new List<VehicleDocument>();
}

/// <summary>
/// Documento del vehículo (registro, seguro, inspección, permiso). Sin TenantId ni PublicId: se alcanza SIEMPRE a través de
/// su vehículo filtrado por tenant y se audita bajo VEHICLE. Un documento con vencimiento queda 'superado' si el mismo
/// vehículo tiene otro activo del mismo tipo que vence después (FleetDocuments.MarkSuperseded). FileName/StoragePath se
/// mapean pero no se exponen (sin proveedor de archivos).
/// </summary>
[AuditEntity(Constants.EntityTypes.Vehicle)]
public class VehicleDocument : ISoftDeletable
{
    public int VehicleDocumentId { get; set; }
    public int VehicleId { get; set; }
    public int DocTypeLookupId { get; set; }
    public string? DocNumber { get; set; }
    public DateOnly? IssuedDate { get; set; }
    public DateOnly? ExpiryDate { get; set; }
    public string? FileName { get; set; }
    public string? StoragePath { get; set; }
    public bool IsActive { get; set; } = true;

    public Vehicle? Vehicle { get; set; }
    public LookupCode? DocType { get; set; }
}
