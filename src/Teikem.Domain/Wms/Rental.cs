using Teikem.Domain.Catalogs;
using Teikem.Domain.Common;

namespace Teikem.Domain.Wms;

/// <summary>
/// Lote 27 (Rentas R1) — renta REN-##### de equipos propios con número de serie (D1, D2, D6): un cliente, una localidad del cliente
/// y varios equipos que salen del almacén de origen. El equipo rentado SIGUE en el inventario (posición EN-RENTA de la zona
/// RENTAL, reservado, serie ON_RENT): no cuenta como disponible. Contrato = número y fecha dentro de la renta (D6). Estatus
/// RentalStatus por StatusService: DRAFT → SCHEDULED (reserva las series) → ON_RENT (despacho) → RETURNED; CANCELLED solo antes
/// del despacho. PickupDate es la fecha de recogido vigente (cambia con cada extensión); OriginalPickupDate, la pactada al
/// inicio. DeliveryShipmentId e InvoiceId son enlaces nulos preparados para Envíos y Facturación (D2, D3); sin cálculo ni cobro.
/// </summary>
[AuditEntity(Constants.EntityTypes.Rental)]
public class Rental : ITenantScoped, IHasStatus, IAuditStamped
{
    public int RentalId { get; set; }
    [NotAudited] public Guid PublicId { get; set; }
    public int TenantId { get; set; }
    public string Number { get; set; } = string.Empty;
    public int ClientId { get; set; }
    public int LocationId { get; set; }
    public int? ClientContactId { get; set; }
    /// <summary>Almacén de origen: las series salen de sus posiciones y quedan en su posición EN-RENTA.</summary>
    public int WarehouseId { get; set; }
    public DateOnly StartDate { get; set; }
    /// <summary>Fecha de recogido vigente (fin de la renta); la mueve hacia adelante cada extensión.</summary>
    public DateOnly PickupDate { get; set; }
    /// <summary>Fecha de recogido pactada al inicio (no cambia con las extensiones).</summary>
    public DateOnly OriginalPickupDate { get; set; }
    public string? ContractNumber { get; set; }
    public DateOnly? ContractSignedOn { get; set; }
    /// <summary>Costo de transporte estimado de la entrega (solo dato; sin cálculo).</summary>
    public decimal? EstimatedDeliveryCost { get; set; }
    public int? TransportCurrencyLookupId { get; set; }
    /// <summary>Envío futuro que hará la entrega (sin FK hasta que exista el módulo de Envíos).</summary>
    public int? DeliveryShipmentId { get; set; }
    /// <summary>Factura futura (FK a dbo.Invoice, no mapeada todavía).</summary>
    public int? InvoiceId { get; set; }
    public int StatusCodeId { get; set; }
    public DateTime? DispatchedAtUtc { get; set; }
    public DateTime? ClosedAtUtc { get; set; }
    public string? Notes { get; set; }
    [NotAudited] public DateTime CreatedAtUtc { get; set; }
    public int? CreatedBy { get; set; }
    [NotAudited] public DateTime? UpdatedAtUtc { get; set; }
    public int? UpdatedBy { get; set; }
    [NotAudited] public byte[]? RowVersion { get; set; }

    public StatusCode? Status { get; set; }
}

/// <summary>
/// Equipo de la renta: una serie de un producto propio con seguimiento SERIAL. Sin TenantId: se alcanza por su renta filtrada.
/// Una serie solo puede estar en UNA línea abierta (UX_RentalLine_OpenSerial: no devuelta y activa). FromBinId = posición de
/// donde sale (se fija al agregar y se actualiza al programar); DispatchTxnId = movimiento TRANSFER del despacho. Quitar un
/// equipo (antes del despacho) o cancelar la renta lo desactiva (IsActive = 0), nunca se borra.
/// </summary>
[AuditEntity(Constants.EntityTypes.Rental)]
public class RentalLine : ISoftDeletable
{
    public int RentalLineId { get; set; }
    public int RentalId { get; set; }
    public int ProductId { get; set; }
    public int SerialId { get; set; }
    public int? LotId { get; set; }
    public int FromBinId { get; set; }
    public long? DispatchTxnId { get; set; }
    public DateTime? DispatchedAtUtc { get; set; }
    public DateTime? ReturnedAtUtc { get; set; }
    public bool IsActive { get; set; } = true;
    [NotAudited] public DateTime CreatedAtUtc { get; set; }
}

/// <summary>
/// Condiciones de cobro de un equipo, efectivo-fechadas como RateComponent (D3: solo se guardan; sin cálculo ni cobro): fija
/// (ONE_TIME) o por tiempo (DAILY/WEEKLY/MONTHLY), tarifa ≥ 0 y moneda. EffectiveTo es EXCLUSIVO (como RateComponent). Antes del
/// despacho la versión vigente se edita en su lugar; una extensión con tarifa nueva cierra la vigente y abre otra desde el día
/// siguiente al recogido anterior (EffectiveTo de la vieja = EffectiveFrom de la nueva) con RentalExtensionId. Sin TenantId: por
/// su línea y su renta filtradas.
/// </summary>
[AuditEntity(Constants.EntityTypes.Rental)]
public class RentalLineRate : IEffectiveDated
{
    public int RentalLineRateId { get; set; }
    public int RentalLineId { get; set; }
    public int BillingFrequencyLookupId { get; set; }
    public decimal RateAmount { get; set; }
    public int CurrencyLookupId { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public int? RentalExtensionId { get; set; }
    [NotAudited] public DateTime CreatedAtUtc { get; set; }
    public int? CreatedBy { get; set; }
}

/// <summary>
/// Extensión de la renta (D4): bitácora de SOLO INSERCIÓN con la fecha de recogido anterior y la nueva (siempre posterior,
/// CK_RentalExtension_Dates) y el motivo. Sin aprobación de un segundo usuario: basta el permiso rental.extend.
/// </summary>
[AuditEntity(Constants.EntityTypes.Rental)]
public class RentalExtension
{
    public int RentalExtensionId { get; set; }
    public int RentalId { get; set; }
    public DateOnly PreviousPickupDate { get; set; }
    public DateOnly NewPickupDate { get; set; }
    public string Reason { get; set; } = string.Empty;
    [NotAudited] public DateTime CreatedAtUtc { get; set; }
    public int? CreatedBy { get; set; }
}

/// <summary>
/// Devolución de renta DRN-##### (bloque R2): fecha, motivo por devolución (RentalReturnReason), notas, costo de recogido
/// estimado y enlace nulo al envío futuro de recogido. Mapeada desde R1 (el esquema de rentas se crea completo).
/// </summary>
[AuditEntity(Constants.EntityTypes.RentalReturn)]
public class RentalReturn : ITenantScoped
{
    public int RentalReturnId { get; set; }
    [NotAudited] public Guid PublicId { get; set; }
    public int TenantId { get; set; }
    public string Number { get; set; } = string.Empty;
    public int RentalId { get; set; }
    public DateOnly ReturnedOn { get; set; }
    public int ReasonLookupId { get; set; }
    public string? Notes { get; set; }
    public decimal? EstimatedPickupCost { get; set; }
    public int? TransportCurrencyLookupId { get; set; }
    /// <summary>Envío futuro del recogido (sin FK hasta que exista el módulo de Envíos).</summary>
    public int? PickupShipmentId { get; set; }
    [NotAudited] public DateTime CreatedAtUtc { get; set; }
    public int? CreatedBy { get; set; }
}

/// <summary>Equipo devuelto (bloque R2): condición, destino, si pasa por proceso y el movimiento de la devolución.</summary>
[AuditEntity(Constants.EntityTypes.RentalReturn)]
public class RentalReturnLine
{
    public int RentalReturnLineId { get; set; }
    public int RentalReturnId { get; set; }
    public int RentalLineId { get; set; }
    public int ConditionLookupId { get; set; }
    public int ToWarehouseId { get; set; }
    public int ToBinId { get; set; }
    public bool RequiresProcess { get; set; }
    public long? ReturnTxnId { get; set; }
    public string? Notes { get; set; }
}

/// <summary>
/// Proceso de un equipo devuelto (bloque R2): inspección, limpieza, pruebas, reparación… con estatus RentalProcessStatus
/// configurable; la serie queda IN_PROCESS y reservada hasta READY (o SCRAPPED).
/// </summary>
[AuditEntity(Constants.EntityTypes.RentalProcess)]
public class RentalProcess : ITenantScoped, IHasStatus
{
    public int RentalProcessId { get; set; }
    public int TenantId { get; set; }
    public int SerialId { get; set; }
    public int ProductId { get; set; }
    public int WarehouseId { get; set; }
    public int BinId { get; set; }
    public int? RentalReturnLineId { get; set; }
    public int StatusCodeId { get; set; }
    public DateTime StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public string? Notes { get; set; }
    [NotAudited] public byte[]? RowVersion { get; set; }

    public StatusCode? Status { get; set; }
}
