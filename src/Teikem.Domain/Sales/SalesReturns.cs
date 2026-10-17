using Teikem.Domain.Common;

namespace Teikem.Domain.Sales;

/// <summary>Constantes de las devoluciones de venta (Lote 81, P6). Viven aquí para que el lote se pueda pasar solo a otra rama.</summary>
public static class ReturnCodes
{
    /// <summary>EntityType de la bitácora y del historial de estatus de la devolución (sembrado en la sección 2026-10-16 (w) de update.sql).</summary>
    public const string EntityType = "SALES_RETURN";
    public const string StatusDomain = "SalesReturnStatus";
    public const string ReasonDomain = "ReturnReason";
    /// <summary>Contador de números de devolución (NumberSequence.Kind), uno por compañía.</summary>
    public const string NumberKind = "SALESRETURN";
    public const string NumberPattern = "DV-#####";
    /// <summary>Días de la ventana de devolución por omisión (0 = sin ventana).</summary>
    public const int DefaultWindowDays = 30;
    public const int MaxWindowDays = 3650;
}

public static class SalesReturnStatuses
{
    public const string Requested = "REQUESTED";
    public const string Authorized = "AUTHORIZED";
    public const string Received = "RECEIVED";
    public const string Credited = "CREDITED";
    public const string Rejected = "REJECTED";
    public const string Cancelled = "CANCELLED";
}

public static class ReturnReasons
{
    public const string Damaged = "DAMAGED";
    public const string WrongItem = "WRONG_ITEM";
    public const string NotNeeded = "NOT_NEEDED";
    public const string Expired = "EXPIRED";
    public const string Other = "OTHER";
    public static readonly string[] All = { Damaged, WrongItem, NotNeeded, Expired, Other };
}

/// <summary>
/// Devolución de venta (DV-#####): el cliente devuelve parte de lo que se le vendió. REQUESTED (ventas la pide) → AUTHORIZED (un supervisor la autoriza) → RECEIVED (el almacén la recibe, en partes)
/// → CREDITED (tiene su nota de crédito); REJECTED o CANCELLED. Recibirla reutiliza el recibo existente (tipo RETURN) con lo bueno y lo dañado por línea; el inventario lo mueve ese recibo.
/// </summary>
[AuditEntity(ReturnCodes.EntityType)]
public class SalesReturn : ITenantScoped, IHasStatus, IAuditStamped
{
    public int SalesReturnId { get; set; }
    [NotAudited] public Guid PublicId { get; set; }
    public int TenantId { get; set; }
    public string Number { get; set; } = string.Empty;
    public int SalesOrderId { get; set; }
    public int ClientId { get; set; }
    /// <summary>Almacén que la recibe (el de la orden de venta).</summary>
    public int WarehouseId { get; set; }
    public int ReasonLookupId { get; set; }
    public int StatusCodeId { get; set; }
    public string? Note { get; set; }
    public int? RequestedBy { get; set; }
    public DateTime RequestedAtUtc { get; set; } = DateTime.UtcNow;
    /// <summary>La pidió fuera de la ventana de devolución (solo la autoriza quien tiene sales.return.authorize, que lo confirma al autorizar).</summary>
    public bool OutsideWindow { get; set; }
    public int? AuthorizedBy { get; set; }
    public DateTime? AuthorizedAtUtc { get; set; }
    /// <summary>Cómo regresa la mercancía (el cliente la lleva o se recoge) y cualquier nota de quien autoriza o rechaza.</summary>
    public string? AuthorizationNote { get; set; }
    public DateTime? ReceivedAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public int? CreatedBy { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
    public int? UpdatedBy { get; set; }
    [NotAudited] public byte[]? RowVersion { get; set; }
    public List<SalesReturnLine> Lines { get; set; } = new();
}

/// <summary>Línea de la devolución: lo pedido, lo recibido (bueno + dañado, QtyReceived), de eso lo dañado (QtyDamaged) y lo ya acreditado con notas de crédito (QtyCredited).</summary>
public class SalesReturnLine
{
    public int SalesReturnLineId { get; set; }
    public int SalesReturnId { get; set; }
    public int SalesOrderLineId { get; set; }
    public int ProductId { get; set; }
    public decimal QtyRequested { get; set; }
    public decimal QtyReceived { get; set; }
    public decimal QtyDamaged { get; set; }
    public decimal QtyCredited { get; set; }
}

/// <summary>Reglas puras de las devoluciones de venta (sin EF; SalesReturnService las traduce a 400/404/409/422). Los mensajes son públicos: el manual (capítulo 20) y la FAQ los citan.</summary>
public static class SalesReturnRules
{
    public const int MaxNoteLength = 500;
    public const int MaxLines = 200;

    public const string OrderRequired = "Indique la orden de venta de la devolución.";
    public const string OrderNotReturnable = "Solo se devuelve de una orden de venta despachada.";
    public const string ReasonRequired = "Indique el motivo de la devolución.";
    public const string ReasonInvalid = "El motivo de la devolución no existe.";
    public const string LinesRequired = "Agregue al menos una línea a la devolución.";
    public const string TooManyLines = "La devolución no puede tener más de 200 líneas.";
    public const string NoteTooLong = "La nota no puede pasar de 500 caracteres.";
    public const string LineNotFound = "La línea no es de esa orden de venta.";
    public const string LineRepeated = "Una línea de la orden está repetida en la devolución.";
    public const string QuantityInvalid = "La cantidad debe ser mayor que cero (hasta 3 decimales).";
    public const string KitNotReturnable = "Las líneas de kit no se devuelven en esta versión.";
    public const string SerialNotReturnable = "Un producto con serie no se devuelve en esta versión.";
    public const string OutsideWindowNeedsRight = "La devolución está fuera de la ventana de devolución: la autoriza quien tiene permiso para ello.";
    public const string NotFound = "Devolución no encontrada.";
    public const string AuthorizeNeedsRequested = "Solo se autoriza una devolución solicitada.";
    public const string RejectNeedsRequested = "Solo se rechaza una devolución solicitada.";
    public const string RejectReasonRequired = "Escriba el motivo para rechazar la devolución.";
    public const string CancelNeedsOpen = "Solo se cancela una devolución solicitada o autorizada que no se haya recibido.";
    public const string ReceiveNeedsAuthorized = "Solo se recibe una devolución autorizada.";
    public const string ReceiveLinesRequired = "Indique qué línea se recibe y cuánto.";
    public const string ReceiveLineNotFound = "La línea no es de esa devolución.";
    public const string ReceiveLineRepeated = "Una línea de la devolución está repetida en lo que se recibe.";
    public const string ReceiveQuantityInvalid = "Las cantidades buenas y dañadas deben ser cero o más (hasta 3 decimales) y al menos una mayor que cero.";
    public const string DamagedNeedsNote = "Describa el daño de lo que llegó dañado.";
    public const string CreditNeedsReceived = "Solo se acredita una devolución recibida.";
    public const string NothingToCredit = "No hay nada por acreditar: lo devuelto no se había facturado (ya no se facturará) o ya se acreditó.";
    public const string WindowDaysInvalid = "Los días de la ventana de devolución deben estar entre 0 y 3650.";

    public static string ExceedsReturnable(string sku, decimal returnable) => $"De {sku} solo se pueden devolver {returnable:0.###} más.";
    public static string ExceedsPending(string sku, decimal pending) => $"De {sku} solo faltan por recibir {pending:0.###}.";

    /// <summary>Lo que todavía se puede devolver de una línea: despachado − devuelto − lo que ya piden otras devoluciones abiertas (nunca negativo).</summary>
    public static decimal Returnable(decimal shipped, decimal returned, decimal pendingInOpenReturns) => Math.Max(0m, Math.Round(shipped - returned - pendingInOpenReturns, 3, MidpointRounding.AwayFromZero));

    /// <summary>Dentro de la ventana: sin ventana (0) siempre; si no, hasta N días después del último despacho de la orden.</summary>
    public static bool WithinWindow(DateOnly lastDispatch, int windowDays, DateOnly today) => windowDays <= 0 || today.DayNumber - lastDispatch.DayNumber <= windowDays;

    /// <summary>Lo que falta por recibir de una línea de la devolución.</summary>
    public static decimal PendingToReceive(decimal requested, decimal received) => Math.Max(0m, Math.Round(requested - received, 3, MidpointRounding.AwayFromZero));

    public static bool IsValidQuantity(decimal q) => q > 0m && Math.Round(q, 3) == q;
}
