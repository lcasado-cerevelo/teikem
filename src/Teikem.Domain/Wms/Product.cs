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
    /// <summary>
    /// 2026-10-09 — Empaque (opcional, UNO por producto): unidad de empaque (UnitOfMeasure: Caja, Barril, Paquete…) y cuántas unidades BASE trae.
    /// Solo para capturar y mostrar (calculadora del conteo, recibo…): el inventario SIEMPRE está en unidad base. Ambos o ninguno (CK_Product_Pack).
    /// </summary>
    public int? PackUomLookupId { get; set; }
    public decimal? PackQty { get; set; }
    public int TrackingTypeLookupId { get; set; }
    public decimal? WeightKg { get; set; }
    public decimal? VolumeM3 { get; set; }
    /// <summary>
    /// Lote 230: largo, ancho y alto de la unidad base, tal como se escribieron en <see cref="DimensionUnit"/> (IN, CM, FT, M). Con las tres, VolumeM3 se calcula de
    /// ellas (ProductDimensionRules). WeightKg y VolumeM3 siguen siendo la verdad interna en kg y m³.
    /// </summary>
    public decimal? Length { get; set; }
    public decimal? Width { get; set; }
    public decimal? Height { get; set; }
    public string? DimensionUnit { get; set; }
    /// <summary>Lote 230: unidad en que se escribió el peso (LB o KG) para mostrarlo igual; el peso se guarda en kg (WeightKg).</summary>
    public string? WeightUnit { get; set; }
    public string? Barcode { get; set; }
    public decimal? PurchaseCost { get; set; }
    public decimal? SalePrice { get; set; }
    /// <summary>Lote 55: categoría de impuesto (LookupCode TaxCategory: TAXABLE / EXEMPT / ZERO_RATE). NULL = gravable.</summary>
    public int? TaxCategoryLookupId { get; set; }
    public int? PreferredWarehouseId { get; set; }
    public int? PreferredBinId { get; set; }
    public decimal? MinQty { get; set; }
    public decimal? MinPickQty { get; set; }
    /// <summary>Lote 34: excepción de rotación de este producto ('FEFO' | 'FIFO'). NULL = el método de la compañía (Tenant.RotationMethod).</summary>
    public string? RotationMethod { get; set; }
    public decimal? MaxPickQty { get; set; }
    /// <summary>Lote 12: marca en texto libre (NVARCHAR(100), recortada; vacía = NULL). Se audita como cualquier campo.</summary>
    public string? Brand { get; set; }
    /// <summary>Lote 12: modelo en texto libre (NVARCHAR(100), recortado; vacío = NULL).</summary>
    public string? Model { get; set; }
    /// <summary>Lote 68: tipo de producto (LookupCode ProductType: STANDARD / KIT). NULL = producto normal.</summary>
    public int? ProductTypeLookupId { get; set; }
    /// <summary>Lote 68: kit «ajustable al pedir» — en una orden de venta se pueden cambiar sus piezas sin tocar el catálogo.</summary>
    public bool KitAdjustable { get; set; }
    /// <summary>
    /// Lote 221: el kit es una OFERTA (promoción) — se muestra como tarjeta con su foto en el pedido y solo se vende dentro de su vigencia
    /// (PromoFrom / PromoTo, ambos opcionales: NULL = sin límite de ese lado). Solo un kit puede serlo. Las órdenes ya hechas no cambian al vencer.
    /// </summary>
    public bool IsPromotion { get; set; }
    public DateOnly? PromoFrom { get; set; }
    public DateOnly? PromoTo { get; set; }
    /// <summary>Lote 221: la foto de la oferta (StoredFile del producto, una imagen).</summary>
    public int? PromoImageFileId { get; set; }
    /// <summary>Lote 229: la foto del producto (StoredFile del producto, JPG/PNG/WEBP). Las ofertas y listas la usan por omisión (ProductImageRules.Resolve).</summary>
    public int? ImageFileId { get; set; }
    /// <summary>Lote 229: el kit fue creado para esta oferta (Offer): solo se vende mientras ella esté publicada y vigente (OfferRules.KitSellable). NULL = producto normal.</summary>
    public int? OfferId { get; set; }
    /// <summary>Lote 225: cuándo (UTC) quedó el producto como ya mandado a QuickBooks (al marcar o confirmar la primera factura que lo lleva). NULL = nuevo para QuickBooks. Un kit no se manda: van sus componentes.</summary>
    public DateTime? QuickBooksSentAt { get; set; }
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
