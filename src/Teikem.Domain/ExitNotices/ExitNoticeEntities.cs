using Teikem.Domain.Common;

namespace Teikem.Domain.ExitNotices;

/// <summary>Constantes del aviso de salida (Lote 143, 2026-10-20). Viven aquí (y no en CatalogDomains) para que el lote se pueda pasar solo a otra rama.</summary>
public static class ExitNoticeCodes
{
    /// <summary>EntityType de la bitácora y del historial de estatus.</summary>
    public const string EntityType = "EXIT_NOTICE";
    /// <summary>Dominio de estatus (StatusCode).</summary>
    public const string StatusDomain = "ExitNoticeStatus";
    /// <summary>Contador de números (NumberSequence.Kind), por compañía (ClientId NULL).</summary>
    public const string NumberKind = "EXITNOTICE";
    public const string NumberPattern = "SAL-#####";
}

/// <summary>
/// Estatus del aviso de salida: REQUESTED = solicitado (reservado lo que hay; el almacén lo ve); PARTIAL = se despachó parte; SHIPPED = todo despachado o cerrado;
/// CANCELLED. El pendiente (backorder) NO es un estatus: es la marca de que alguna línea tiene QtyBackordered &gt; 0. No hay borrador: nace solicitado y reservado.
/// </summary>
public static class ExitNoticeStatuses
{
    public const string Requested = "REQUESTED";
    public const string Partial = "PARTIAL";
    public const string Shipped = "SHIPPED";
    public const string Cancelled = "CANCELLED";
}

/// <summary>
/// Aviso de salida (SAL-#####): el dueño de la mercancía (un cliente de almacén) avisa qué quiere que el almacén saque. Sin precio: no es una venta. Reserva lo que
/// haya al crearse (por producto y lote; por dentro, en las posiciones que dicte la rotación) y lo que falte queda pendiente (backorder) hasta que entre mercancía.
/// El destino es texto libre (retiro, dirección o nota). Nunca DELETE: se cancela.
/// </summary>
[AuditEntity(ExitNoticeCodes.EntityType)]
public class ExitNotice : ITenantScoped, IHasStatus, IAuditStamped
{
    public int ExitNoticeId { get; set; }
    [NotAudited] public Guid PublicId { get; set; }
    public int TenantId { get; set; }
    public string Number { get; set; } = string.Empty;
    public int WarehouseId { get; set; }
    /// <summary>El dueño de la mercancía (cliente de almacén); solo se piden productos suyos.</summary>
    public int ClientId { get; set; }
    public string? Reference { get; set; }
    public string? Destination { get; set; }
    public int? RequestedBy { get; set; }
    /// <summary>Lote 171 (B2): a quién se le asignó surtirlo (NULL = sin asignar; lo puede tomar cualquiera con permiso de recolectar).</summary>
    public int? AssignedToUserId { get; set; }
    public DateOnly RequestedDate { get; set; }
    public DateOnly? RequiredDate { get; set; }
    public string? Notes { get; set; }
    public int StatusCodeId { get; set; }
    public DateTime? RequestedAtUtc { get; set; }
    [NotAudited] public DateTime CreatedAtUtc { get; set; }
    public int? CreatedBy { get; set; }
    [NotAudited] public DateTime? UpdatedAtUtc { get; set; }
    public int? UpdatedBy { get; set; }
    [NotAudited] public byte[]? RowVersion { get; set; }
    public List<ExitNoticeLine> Lines { get; set; } = new();
}

/// <summary>Línea de un aviso de salida. QtyReserved + QtyBackordered + lo despachado = lo pedido (salvo líneas cerradas con faltante).</summary>
public class ExitNoticeLine : ITenantScoped
{
    public int ExitNoticeLineId { get; set; }
    public int TenantId { get; set; }
    public int ExitNoticeId { get; set; }
    public int LineNo { get; set; }
    public int ProductId { get; set; }
    public int? LotId { get; set; }
    public decimal QtyOrdered { get; set; }
    public decimal QtyReserved { get; set; }
    public decimal QtyBackordered { get; set; }
    public decimal QtyShipped { get; set; }
    /// <summary>La línea se dio por terminada con faltante (cerrar con faltante): no espera más inventario.</summary>
    public bool IsClosed { get; set; }
}

/// <summary>Qué saldo (almacén, posición, lote) reservó una línea y cuánto: se libera EXACTAMENTE esto al cancelar, modificar o despachar (el ledger reserva por saldo).</summary>
public class ExitNoticeReservation : ITenantScoped
{
    public int ExitNoticeReservationId { get; set; }
    public int TenantId { get; set; }
    public int ExitNoticeLineId { get; set; }
    public int WarehouseId { get; set; }
    public int WarehouseBinId { get; set; }
    public int? LotId { get; set; }
    public decimal Quantity { get; set; }
}
