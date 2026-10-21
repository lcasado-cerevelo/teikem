using Teikem.Domain.Common;

namespace Teikem.Domain.Accounting;

/// <summary>
/// Lote 181 (H1) — Contabilización de compras y de despachos: el costo ya congelado de lo que entró (recibos confirmados) o salió (despachos) del inventario PROPIO, asentado en el sistema contable de la compañía.
/// Corridas más ligeras que la facturación: GENERADA → ACEPTADA → EXPORTADA (CANCELADA desde generada o aceptada); no hay nada que ajustar línea por línea, solo se junta, se acepta y se exporta.
/// </summary>
public static class AccountingCodes
{
    /// <summary>EntityType de la bitácora y del historial de estatus de la corrida (sembrado en la sección 2026-10-21 (Lote 181) de update.sql).</summary>
    public const string EntityType = "ACCOUNTING_RUN";
    public const string StatusDomain = "AccountingRunStatus";

    /// <summary>De qué es la corrida: las compras (lo que entró) o los despachos (lo que salió).</summary>
    public const string KindPurchases = "PURCHASES";
    public const string KindDispatches = "DISPATCHES";
    public static readonly string[] Kinds = { KindPurchases, KindDispatches };

    /// <summary>Contadores por compañía (NumberSequence.Kind, ClientId NULL) y sus patrones.</summary>
    public const string NumberKindPurchases = "ACCTPURCH";
    public const string NumberKindDispatches = "ACCTDISP";
    public const string NumberPatternPurchases = "CMP-#####";
    public const string NumberPatternDispatches = "DSP-#####";

    /// <summary>De dónde sale cada línea: la línea del recibo o la línea de la recolección (o despacho).</summary>
    public const string SourceReceiptLine = "RECEIPT_LINE";
    public const string SourcePickLine = "PICK_LINE";

    public static string NumberKindOf(string kind) => kind == KindDispatches ? NumberKindDispatches : NumberKindPurchases;
    public static string NumberPatternOf(string kind) => kind == KindDispatches ? NumberPatternDispatches : NumberPatternPurchases;
    public static string SourceOf(string kind) => kind == KindDispatches ? SourcePickLine : SourceReceiptLine;
    public static string ScopeOf(string kind) => kind == KindDispatches ? Export.ExportCodes.ScopeDispatchAccounting : Export.ExportCodes.ScopePurchaseAccounting;
}

/// <summary>Dominio AccountingRunStatus.</summary>
public static class AccountingRunStatuses
{
    public const string Generated = "GENERATED";
    public const string Accepted = "ACCEPTED";
    public const string Exported = "EXPORTED";
    public const string Cancelled = "CANCELLED";
}

/// <summary>
/// Corrida de contabilización (CMP-##### de compras, DSP-##### de despachos). Junta las líneas elegibles del período (día de la compañía) que aún no están en otra corrida viva, con el costo que ya
/// traían congelado (la capa del recibo; el costo de la salida): lo que se asienta no se recalcula si después cambia un costo. Aceptarla la congela; exportarla arma el archivo con la plantilla.
/// </summary>
[AuditEntity(AccountingCodes.EntityType)]
public class AccountingRun : ITenantScoped, IHasStatus
{
    public int AccountingRunId { get; set; }
    [NotAudited] public Guid PublicId { get; set; }
    public int TenantId { get; set; }
    public string Kind { get; set; } = AccountingCodes.KindPurchases;
    public string Number { get; set; } = string.Empty;
    public DateOnly PeriodStart { get; set; }
    public DateOnly PeriodEnd { get; set; }
    public int StatusCodeId { get; set; }
    public int CurrencyLookupId { get; set; }
    public int LineCount { get; set; }
    /// <summary>Suma de los totales de las líneas con costo.</summary>
    public decimal TotalAmount { get; set; }
    /// <summary>Líneas sin costo congelado (la capa no tenía costo): van a la corrida con costo en blanco para que contabilidad las vea.</summary>
    public int UnpricedLineCount { get; set; }
    public string? Note { get; set; }
    public DateTime GeneratedAtUtc { get; set; } = DateTime.UtcNow;
    public int? GeneratedBy { get; set; }
    public DateTime? AcceptedAtUtc { get; set; }
    public int? AcceptedBy { get; set; }
    public DateTime? ExportedAtUtc { get; set; }
    public int? ExportedBy { get; set; }
    [NotAudited] public byte[]? RowVersion { get; set; }

    public List<AccountingRunLine> Lines { get; set; } = new();
}

/// <summary>
/// Línea de la corrida: una línea de recibo (SourceType RECEIPT_LINE) o una línea de recolección/despacho (PICK_LINE), con los datos que el archivo necesita copiados al generar. IsActive = 0 cuando la
/// corrida se cancela: la línea queda libre para otra corrida (índice único filtrado por origen entre las vivas).
/// </summary>
public class AccountingRunLine : ITenantScoped
{
    public int AccountingRunLineId { get; set; }
    public int TenantId { get; set; }
    public int AccountingRunId { get; set; }
    public string SourceType { get; set; } = AccountingCodes.SourceReceiptLine;
    public int SourceId { get; set; }
    public DateOnly DocDate { get; set; }
    /// <summary>Número del recibo (REC-#####) o de la recolección/despacho (EMP-##### / DMA-#####).</summary>
    public string DocumentNumber { get; set; } = string.Empty;
    /// <summary>Compras: la orden de compra o el aviso de llegada de origen. Despachos: la orden de venta o el aviso de salida.</summary>
    public string? ReferenceNumber { get; set; }
    /// <summary>Compras: el proveedor (o el cliente del aviso). Despachos: el cliente del documento.</summary>
    public string? PartyName { get; set; }
    public string? WarehouseCode { get; set; }
    public int ProductId { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string? LotNumber { get; set; }
    public decimal Quantity { get; set; }
    public decimal? UnitCost { get; set; }
    public decimal? TotalCost { get; set; }
    /// <summary>Compras: de dónde salió el costo de la capa (PO, ASN o PRODUCT). Despachos: null.</summary>
    public string? CostSource { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>Reglas puras y mensajes (públicos: el manual y la FAQ los citan).</summary>
public static class AccountingRules
{
    public const int MaxNoteLength = 500;

    public const string Concurrency = "La corrida cambió mientras se guardaba. Revise y vuelva a intentar.";
    public const string KindInvalid = "El tipo de corrida debe ser PURCHASES o DISPATCHES.";
    public const string PeriodRequired = "Indique el período (desde y hasta).";
    public const string PeriodInvalid = "El período no es válido: la fecha final no puede ser anterior a la inicial.";
    public const string NoteTooLong = "La nota no puede pasar de 500 caracteres.";
    public const string NothingToAccount = "No hay nada por contabilizar en el período.";
    public const string AcceptNeedsGenerated = "Solo se acepta una corrida generada.";
    public const string CancelNeedsOpen = "Solo se cancela una corrida generada o aceptada.";
    public const string ExportNeedsAccepted = "Solo se exporta una corrida aceptada o exportada.";

    /// <summary>Costo total de una línea: cantidad × costo unitario a 4 decimales; sin costo, sin total.</summary>
    public static decimal? LineTotal(decimal quantity, decimal? unitCost)
        => unitCost is decimal c ? Math.Round(quantity * c, 4, MidpointRounding.AwayFromZero) : null;

    /// <summary>
    /// ¿Se contabiliza? Por dueño del inventario: lo PROPIO (producto sin cliente dueño) sí; lo de un cliente 3PL no (no es de la compañía). Sin importar el tipo de documento de origen.
    /// </summary>
    public static bool IsAccountable(int? productOwnerClientId) => productOwnerClientId is null;

    public static bool IsKnownKind(string? kind) => kind is AccountingCodes.KindPurchases or AccountingCodes.KindDispatches;
}
