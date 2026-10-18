using Teikem.Domain.Catalogs;
using Teikem.Domain.Clients;
using Teikem.Domain.Common;

namespace Teikem.Domain.Orders;

/// <summary>
/// Orden de transporte (Lote 3). Cabecera con los cuatro identificadores (número de orden = consecutivo interno del cliente,
/// único por (Tenant, Cliente) entre las activas; factura del cliente siempre con valor; empaque EMP-##### único por tenant),
/// totales de carga, COD (CodTypeLookupId queda NULL hasta la entrega), cotización congelada al confirmar (QuotedAmount /
/// QuotedAtUtc / ContractId), entrega especial (IsSpecialDelivery + SpecialServiceId) y origen RefEntity/RefId
/// (SourceEntityTypeLookupId / SourceEntityId: lo llenará Recolección y empaque; hoy el importador de órdenes).
/// Las hijas (OrderStop, CargoLine, OrderReference) no llevan TenantId: se alcanzan SOLO a través de la orden filtrada.
/// </summary>
[AuditEntity(Constants.EntityTypes.TransportOrder)]
public class TransportOrder : ITenantScoped, ISoftDeletable, IHasStatus, IAuditStamped
{
    public int TransportOrderId { get; set; }
    [NotAudited] public Guid PublicId { get; set; }
    public int TenantId { get; set; }
    public int ClientId { get; set; }
    /// <summary>Contrato vigente en la fecha de confirmación (se congela al confirmar, DECISIÓN 17).</summary>
    public int? ContractId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    /// <summary>Número de factura del cliente: lo asigna el cliente o Teikem si queda en blanco (siempre con valor, L240).</summary>
    public string ClientInvoiceNumber { get; set; } = string.Empty;
    /// <summary>true si la factura se tecleó al crear la orden; false si la generó Teikem. R36 solo aplica a facturas tecleadas (L1124).</summary>
    public bool ClientInvoiceNumberTyped { get; set; }
    /// <summary>Lote 62: la orden de venta de la que nació esta entrega; NULL = orden de transporte normal.</summary>
    public int? SalesOrderId { get; set; }
    /// <summary>Número de empaque (EMP-##### fijo o id del lote de Recolección y empaque); identificador de escaneo único por tenant.</summary>
    public string PackBatchNumber { get; set; } = string.Empty;
    /// <summary>Lote 97: número de conduce, único por orden en la compañía (CND-#######); lo genera Teikem.</summary>
    public string WaybillNumber { get; set; } = string.Empty;
    /// <summary>Lote 112: si es una orden de vuelta, la orden que se devolvió (que quedó «Devuelta»); NULL en las demás.</summary>
    public int? ReturnOfOrderId { get; set; }
    public int ServiceTypeLookupId { get; set; }
    public int StatusCodeId { get; set; }
    public int? PriorityLookupId { get; set; }
    public DateTime? RequestedDate { get; set; }
    public DateTime? PromisedDate { get; set; }
    public decimal? TotalWeightKg { get; set; }
    public decimal? TotalVolumeM3 { get; set; }
    public int? TotalPieces { get; set; }
    public decimal? QuotedAmount { get; set; }
    public DateTime? QuotedAtUtc { get; set; }
    /// <summary>Lote 74: el flete de la orden ya está en una factura (no anulada) de la facturación; se reclama al generar la corrida y se libera al cancelarla o anular la factura.</summary>
    [NotAudited] public bool FreightInvoiced { get; set; }
    public int? CurrencyLookupId { get; set; }
    /// <summary>Siempre NULL en este lote: el tipo de COD se define al entregar (11B/POD).</summary>
    public int? CodTypeLookupId { get; set; }
    public decimal? CodAmount { get; set; }
    public int? CodCurrencyLookupId { get; set; }
    /// <summary>StatusCode del dominio CodStatus; NULL = sin COD. Su historial vive bajo EntityType ORDER_COD.</summary>
    public int? CodStatusCodeId { get; set; }
    public bool IsSpecialDelivery { get; set; }
    public int? SpecialServiceId { get; set; }
    /// <summary>Origen de la orden (LookupCode EntityType) + id: patrón RefEntity/RefId.</summary>
    public int? SourceEntityTypeLookupId { get; set; }
    public int? SourceEntityId { get; set; }
    public DateTime? ConfirmedAtUtc { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; }
    public int? CreatedBy { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
    public int? UpdatedBy { get; set; }
    [NotAudited] public byte[]? RowVersion { get; set; }

    public Client? Client { get; set; }
    public Contract? Contract { get; set; }
    public LookupCode? ServiceType { get; set; }
    public StatusCode? Status { get; set; }
    public StatusCode? CodStatus { get; set; }
    public SpecialService? SpecialService { get; set; }
    public ICollection<OrderStop> Stops { get; set; } = new List<OrderStop>();
    public ICollection<CargoLine> CargoLines { get; set; } = new List<CargoLine>();
    public ICollection<OrderReference> References { get; set; } = new List<OrderReference>();
}
