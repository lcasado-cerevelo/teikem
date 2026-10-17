using Teikem.Domain.Common;

namespace Teikem.Domain.Fleet;

/// <summary>Constantes de la liquidación a choferes (Lote 94, P9).</summary>
public static class SettlementCodes
{
    /// <summary>EntityType de la bitácora y del historial de estatus de la corrida (sembrado en la sección 2026-10-17 (ae) de update.sql).</summary>
    public const string EntityType = "DRIVER_SETTLEMENT";
    public const string StatusDomain = "DriverSettlementStatus";
    /// <summary>Contador de números de liquidación (NumberSequence.Kind), uno por compañía.</summary>
    public const string NumberKind = "DRIVERSETTLE";
    public const string NumberPattern = "LIQ-#####";
    public const string LineDelivery = "DELIVERY";
    public const string LineTrip = "TRIP";
}

/// <summary>Dominio DriverSettlementStatus: GENERADA → REVISADA → APROBADA → EXPORTADA (terminal); CANCELADA (terminal) desde generada o revisada.</summary>
public static class DriverSettlementStatuses
{
    public const string Generated = "GENERATED";
    public const string Reviewed = "REVIEWED";
    public const string Approved = "APPROVED";
    public const string Exported = "EXPORTED";
    public const string Cancelled = "CANCELLED";
}

/// <summary>
/// Corrida de liquidación a choferes (LIQ-#####): junta lo que hay por pagar de un chofer (o de todos) en un período —entregas con sus intentos y viajes— y calcula el pago con las tarifas vigentes.
/// El cálculo se CONGELA al generar (fórmula incluida): si la política o las tarifas cambian después, la corrida no se recalcula. Generar → revisar → aprobar (AAL2) → exportar; "Teikem calcula y
/// exporta; no emite el pago". DriverId nulo = todos los choferes con algo por pagar.
/// </summary>
[AuditEntity(SettlementCodes.EntityType)]
public class DriverSettlementRun : ITenantScoped, IHasStatus
{
    public int DriverSettlementRunId { get; set; }
    [NotAudited] public Guid PublicId { get; set; }
    public int TenantId { get; set; }
    public string Number { get; set; } = string.Empty;
    public int? DriverId { get; set; }
    public DateOnly PeriodStart { get; set; }
    public DateOnly PeriodEnd { get; set; }
    public int StatusCodeId { get; set; }
    /// <summary>Fórmula de pago vigente al generar (DriverPayoutFormula), congelada.</summary>
    public string PayoutFormulaCode { get; set; } = string.Empty;
    public int CurrencyLookupId { get; set; }
    public int LineCount { get; set; }
    public decimal TotalAmount { get; set; }
    /// <summary>Intentos del período que quedaron fuera por no tener chofer (la oficina los completa y vuelve a generar).</summary>
    public int SkippedNoDriver { get; set; }
    public string? Note { get; set; }
    public DateTime GeneratedAtUtc { get; set; } = DateTime.UtcNow;
    public int? GeneratedBy { get; set; }
    public int? ReviewedBy { get; set; }
    public DateTime? ReviewedAtUtc { get; set; }
    public int? ApprovedBy { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }
    public int? ExportedBy { get; set; }
    public DateTime? ExportedAtUtc { get; set; }
    [NotAudited] public byte[]? RowVersion { get; set; }

    public List<DriverSettlementLine> Lines { get; set; } = new();
}

/// <summary>
/// Línea de la corrida: la entrega de una orden con sus intentos (Kind DELIVERY) o un viaje pagado (Kind TRIP). CalculatedAmount es lo que calculó el motor; Amount es lo que se paga (igual, o el ajuste
/// manual con su motivo). BreakdownJson guarda el desglose del motor (entrega y cada intento, con la nota «sin tarifa configurada» si faltaba).
/// </summary>
public class DriverSettlementLine : ITenantScoped
{
    public int DriverSettlementLineId { get; set; }
    public int TenantId { get; set; }
    public int DriverSettlementRunId { get; set; }
    public int DriverId { get; set; }
    public string Kind { get; set; } = SettlementCodes.LineDelivery;
    public DateOnly ServiceDate { get; set; }
    public int? TransportOrderId { get; set; }
    public int? DriverTripId { get; set; }
    public string Description { get; set; } = string.Empty;
    public int AttemptCount { get; set; }
    public string? BreakdownJson { get; set; }
    public bool RateMissing { get; set; }
    public decimal CalculatedAmount { get; set; }
    public decimal Amount { get; set; }
    public string? AdjustmentNote { get; set; }
    public int? AdjustedBy { get; set; }
    public DateTime? AdjustedAtUtc { get; set; }
}

/// <summary>Reglas y mensajes de la liquidación a choferes.</summary>
public static class SettlementRules
{
    public const int MaxNoteLength = 500;
    public const int MaxAdjustNoteLength = 250;
    public const decimal MaxLineAmount = 1_000_000m;

    public const string Concurrency = "La liquidación cambió mientras se guardaba. Revise y vuelva a intentar.";
    public const string PeriodRequired = "Indique el período (desde y hasta).";
    public const string PeriodInvalid = "El período no es válido: la fecha final no puede ser anterior a la inicial.";
    public const string DriverInvalid = "El chofer no existe.";
    public const string NoteTooLong = "La nota no puede pasar de 500 caracteres.";
    public const string NothingToSettle = "No hay nada por liquidar en el período.";
    public const string ReviewNeedsGenerated = "Solo se revisa una liquidación generada.";
    public const string ApproveNeedsReviewed = "Solo se aprueba una liquidación revisada.";
    public const string CancelNeedsOpen = "Solo se cancela una liquidación generada o revisada.";
    public const string AdjustNeedsOpen = "Solo se ajusta una liquidación generada o revisada.";
    public const string ExportNeedsApproved = "Solo se exporta una liquidación aprobada o exportada.";
    public const string AdjustAmountRequired = "Indique el monto del ajuste.";
    public const string AdjustAmountInvalid = "El monto debe estar entre 0 y 1,000,000.00 con dos decimales como máximo.";
    public const string AdjustNoteRequired = "Indique el motivo del ajuste.";
    public const string AdjustNoteTooLong = "El motivo no puede pasar de 250 caracteres.";

    public static bool IsValidAmount(decimal amount) => amount >= 0m && amount <= MaxLineAmount && decimal.Round(amount, 2) == amount;
}
