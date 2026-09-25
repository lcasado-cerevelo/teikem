using Teikem.Domain.Catalogs;
using Teikem.Domain.Common;

namespace Teikem.Domain.Orders;

/// <summary>
/// Línea de carga = paquete de la orden (DECISIÓN 14): tipo de paquete del catálogo (NULL solo en la línea de una entrega
/// especial), número de paquete por línea (PQT-##### del patrón del cliente si se deja en blanco), Quantity = piezas.
/// PATCH reemplaza las líneas dando de baja las anteriores (IsActive=0) para conservar rastro. ProductId/LotId/SerialId se
/// conservan como columnas (sin navegación: Product/Inventario no se mapean en este lote).
/// No lleva TenantId: se alcanza SOLO a través de su TransportOrder; se audita bajo TRANSPORT_ORDER.
/// </summary>
[AuditEntity(Constants.EntityTypes.TransportOrder)]
public class CargoLine : ISoftDeletable
{
    public int CargoLineId { get; set; }
    public int TransportOrderId { get; set; }
    public int? PickupStopId { get; set; }
    public int? DeliveryStopId { get; set; }
    public int? ProductId { get; set; }
    public int? PackageTypeLookupId { get; set; }
    public string? PackageNumber { get; set; }
    public string Description { get; set; } = string.Empty;
    /// <summary>Piezas de la línea (DECIMAL(14,3) en SQL; este lote escribe enteros).</summary>
    public decimal Quantity { get; set; } = 1;
    public int? UomLookupId { get; set; }
    public decimal? WeightKg { get; set; }
    public decimal? VolumeM3 { get; set; }
    public int? LotId { get; set; }
    public int? SerialId { get; set; }
    public int HandlingFlags { get; set; }
    public bool IsActive { get; set; } = true;

    public TransportOrder? Order { get; set; }
    public LookupCode? PackageType { get; set; }
}
