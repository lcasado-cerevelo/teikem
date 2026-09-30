using Teikem.Domain.Catalogs;
using Teikem.Domain.Common;

namespace Teikem.Domain.Wms;

/// <summary>
/// Lote 6 — Almacén del tenant (R6). Code único por compañía (UQ_Warehouse_Code) e inmutable. Estatus WarehouseStatus vía
/// StatusService: ACTIVE → INACTIVE (terminal, baja definitiva con IsActive = 0, D26). GeoPoint (GEOGRAPHY) NO se mapea: los
/// bloqueos del encabezado usan 'SELECT WarehouseId AS Value', nunca 'SELECT *'.
/// </summary>
[AuditEntity(Constants.EntityTypes.Warehouse)]
public class Warehouse : ITenantScoped, ISoftDeletable, IHasStatus
{
    public int WarehouseId { get; set; }
    [NotAudited] public Guid PublicId { get; set; }
    public int TenantId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Line1 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public int CountryLookupId { get; set; }
    public int StatusCodeId { get; set; }
    public bool IsActive { get; set; } = true;
    [NotAudited] public byte[]? RowVersion { get; set; }

    public StatusCode? Status { get; set; }
    public ICollection<WarehouseZone> Zones { get; set; } = new List<WarehouseZone>();
    public ICollection<WarehouseDock> Docks { get; set; } = new List<WarehouseDock>();
}

/// <summary>
/// Zona tipada del almacén (ZoneType: PICKING, RESERVE, REFRIGERATED, QUARANTINE, CROSSDOCK, STAGING). Sin TenantId: se
/// alcanza SOLO a través de su almacén filtrado (WmsResolve). Se audita bajo WAREHOUSE.
/// </summary>
[AuditEntity(Constants.EntityTypes.Warehouse)]
public class WarehouseZone : ISoftDeletable
{
    public int WarehouseZoneId { get; set; }
    public int WarehouseId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int? ZoneTypeLookupId { get; set; }
    public bool IsActive { get; set; } = true;

    public Warehouse? Warehouse { get; set; }
    public ICollection<WarehouseBin> Bins { get; set; } = new List<WarehouseBin>();
}

/// <summary>
/// Posición ('Ubicación') pasillo-rack-nivel-posición. Lleva WarehouseId (D18): FK compuesta (WarehouseZoneId, WarehouseId)
/// y código único por almacén (UQ_WarehouseBin_WhCode). Sin TenantId: se alcanza SOLO a través de su almacén filtrado.
/// </summary>
[AuditEntity(Constants.EntityTypes.Warehouse)]
public class WarehouseBin
{
    public int WarehouseBinId { get; set; }
    public int WarehouseZoneId { get; set; }
    public int WarehouseId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string? Aisle { get; set; }
    public string? Rack { get; set; }
    public string? Level { get; set; }
    public string? Position { get; set; }
    public decimal? MaxWeightKg { get; set; }
    /// <summary>
    /// Cupo máximo de la posición en unidades de producto (null = sin configurar). La capacidad de una zona NO se guarda: es
    /// la suma del cupo de sus posiciones activas con cupo (CK_WarehouseBin_MaxCapacityQty: null o &gt; 0).
    /// </summary>
    public int? MaxCapacityQty { get; set; }
    public bool IsActive { get; set; } = true;

    public WarehouseZone? Zone { get; set; }
}

/// <summary>Muelle tipado (DockType) con estatus DockStatus (FREE/OCCUPIED/MAINTENANCE). Sin TenantId: por su almacén.</summary>
[AuditEntity(Constants.EntityTypes.WarehouseDock)]
public class WarehouseDock : IHasStatus
{
    public int WarehouseDockId { get; set; }
    public int WarehouseId { get; set; }
    public string Code { get; set; } = string.Empty;
    public int DockTypeLookupId { get; set; }
    public int StatusCodeId { get; set; }
    public bool IsActive { get; set; } = true;

    public StatusCode? Status { get; set; }
}
