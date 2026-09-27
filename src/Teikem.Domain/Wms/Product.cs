using Teikem.Domain.Common;

namespace Teikem.Domain.Wms;

/// <summary>
/// Categoría jerárquica de productos (ParentId del mismo tenant por FK compuesta; sin ciclos, máximo 5 niveles). Lote 7A: se
/// audita con su propio EntityType (PRODUCT_CATEGORY) para que su baja no se confunda con la de un producto del mismo id.
/// </summary>
[AuditEntity(Constants.EntityTypes.ProductCategory)]
public class ProductCategory : ITenantScoped, ISoftDeletable
{
    public int ProductCategoryId { get; set; }
    public int TenantId { get; set; }
    public int? ParentId { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// Lote 6 — Producto (SKU). ClientId = DUEÑO DEL INVENTARIO (cliente 3PL de la tabla Client; NULL = propio del tenant); nunca
/// se confunde con TenantId, el dueño de los datos. Seguimiento (TrackingType NONE/LOT/SERIAL), UoM base y dueño son
/// inmutables tras el primer movimiento (D25). Costos y precios DECIMAL(18,4) ≥ 0; mínimos (D23) en unidad base.
/// </summary>
[AuditEntity(Constants.EntityTypes.Product)]
public class Product : ITenantScoped, ISoftDeletable
{
    public int ProductId { get; set; }
    [NotAudited] public Guid PublicId { get; set; }
    public int TenantId { get; set; }
    public int? ClientId { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int? ProductCategoryId { get; set; }
    public int BaseUomLookupId { get; set; }
    public int TrackingTypeLookupId { get; set; }
    public decimal? WeightKg { get; set; }
    public decimal? VolumeM3 { get; set; }
    public string? Barcode { get; set; }
    public decimal? PurchaseCost { get; set; }
    public decimal? SalePrice { get; set; }
    public int? PreferredWarehouseId { get; set; }
    public int? PreferredBinId { get; set; }
    public decimal? MinQty { get; set; }
    public decimal? MinPickQty { get; set; }
    public decimal? MaxPickQty { get; set; }
    public bool IsActive { get; set; } = true;
    [NotAudited] public byte[]? RowVersion { get; set; }
}

/// <summary>
/// Lote del producto (UQ_Lot por producto). Sin TenantId propio: hereda la tenencia de Product por FK; se alcanza SOLO a
/// través del producto filtrado. Un lote existente con otras fechas → 409 (D34).
/// </summary>
[AuditEntity(Constants.EntityTypes.Product)]
public class InventoryLot
{
    public int LotId { get; set; }
    public int ProductId { get; set; }
    public string LotNumber { get; set; } = string.Empty;
    public DateOnly? ManufactureDate { get; set; }
    public DateOnly? ExpiryDate { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// Serie del producto (UQ_Serial por producto). Sin AuditLog: su rastro es EntityStatusHistory (SerialStatus, EntityType
/// INVENTORY_SERIAL) más el ledger. Estatus y ubicación actual (D17) los escribe SOLO InventoryLedger. Sin TenantId propio:
/// hereda la tenencia de Product.
/// </summary>
public class InventorySerial
{
    public int SerialId { get; set; }
    public int ProductId { get; set; }
    public int? LotId { get; set; }
    public string SerialNumber { get; set; } = string.Empty;
    public int? StatusCodeId { get; set; }
    public int? CurrentWarehouseId { get; set; }
    public int? CurrentBinId { get; set; }
}
