using Teikem.Domain.Common;

namespace Teikem.Domain.Cod;

/// <summary>Constantes de la remesa al cliente (Lote 86, P8): el dinero cobrado en la calle y cuadrado se le devuelve al cliente menos la comisión.</summary>
public static class RemittanceCodes
{
    /// <summary>EntityType de la bitácora y del historial de estatus de la remesa (sembrado en la sección 2026-10-17 (y) de update.sql).</summary>
    public const string EntityType = "COD_REMITTANCE";
    public const string StatusDomain = "RemittanceStatus";
    /// <summary>Contador de números de remesa (NumberSequence.Kind), uno por compañía.</summary>
    public const string NumberKind = "CODREMIT";
    public const string NumberPattern = "REM-#####";
}

/// <summary>Dominio RemittanceStatus: ABIERTA → CUADRADA → APROBADA → REMITIDA (terminal). Una remesa cancelada se desactiva (IsActive = 0) y libera sus cobros.</summary>
public static class RemittanceStatuses
{
    public const string Open = "OPEN";
    public const string Reconciled = "RECONCILED";
    public const string Approved = "APPROVED";
    public const string Remitted = "REMITTED";
}

/// <summary>
/// Remesa al cliente: agrupa los cobros CUADRADOS de un cliente en un período y calcula el bruto cobrado, la comisión y el neto a remitir. La plataforma calcula y exporta; no emite el pago. Cada cobro entra
/// en una sola remesa a la vez. El neto (GrossCollected − CommissionAmount) es columna calculada de la base.
/// </summary>
[AuditEntity(RemittanceCodes.EntityType)]
public class CodRemittanceBatch : ITenantScoped, IHasStatus
{
    public int CodRemittanceBatchId { get; set; }
    [NotAudited] public Guid PublicId { get; set; }
    public int TenantId { get; set; }
    public int ClientId { get; set; }
    public string Number { get; set; } = string.Empty;
    public DateOnly PeriodStart { get; set; }
    public DateOnly PeriodEnd { get; set; }
    public int StatusCodeId { get; set; }
    public decimal GrossCollected { get; set; }
    public decimal? CommissionPct { get; set; }
    public decimal CommissionAmount { get; set; }
    /// <summary>Columna calculada (GrossCollected − CommissionAmount); solo lectura.</summary>
    public decimal NetRemitted { get; set; }
    public int CurrencyLookupId { get; set; }
    public DateTime GeneratedAtUtc { get; set; } = DateTime.UtcNow;
    public int? ApprovedBy { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }
    public DateTime? RemittedAtUtc { get; set; }
    public string? ExportRef { get; set; }
    public bool IsActive { get; set; } = true;
    [NotAudited] public byte[]? RowVersion { get; set; }
    public List<CodRemittanceLine> Lines { get; set; } = new();
}

public class CodRemittanceLine
{
    public long CodRemittanceLineId { get; set; }
    public int CodRemittanceBatchId { get; set; }
    public long CodCollectionId { get; set; }
    public int TransportOrderId { get; set; }
    public decimal AmountCollected { get; set; }
    public decimal CommissionAmount { get; set; }
    /// <summary>Columna calculada (AmountCollected − CommissionAmount); solo lectura.</summary>
    public decimal NetAmount { get; set; }
}

public static class RemittanceRules
{
    public const decimal MaxPct = 100m;
    public const int MaxExportRefLength = 80;

    public const string ClientRequired = "Indique el cliente de la remesa.";
    public const string ClientInvalid = "El cliente no existe.";
    public const string PeriodRequired = "Indique el período de la remesa (desde y hasta).";
    public const string PeriodInvalid = "El período no es válido: la fecha final no puede ser anterior a la inicial.";
    public const string PctInvalid = "La comisión debe estar entre 0 y 100 (hasta 2 decimales).";
    public const string NothingToRemit = "No hay cobros cuadrados de ese cliente en ese período para remitir.";
    public const string ExportRefTooLong = "La referencia no puede pasar de 80 caracteres.";
    public const string ReviewNeedsOpen = "Solo se cuadra una remesa abierta.";
    public const string ApproveNeedsReconciled = "Solo se aprueba una remesa cuadrada.";
    public const string RemitNeedsApproved = "Solo se remite una remesa aprobada.";
    public const string CancelNotAllowed = "Solo se cancela una remesa abierta o cuadrada (una aprobada o remitida ya no).";
    public const string ExportNeedsApproved = "Solo se exporta una remesa aprobada o remitida.";
    public const string TemplateScopeMismatch = "La plantilla no es de remesas COD.";
    public const string SettingsPctInvalid = "La comisión por omisión debe estar entre 0 y 100 (hasta 2 decimales).";

    public static string Concurrency => "La remesa cambió mientras se guardaba. Revise y vuelva a intentar.";

    public static bool IsValidPct(decimal v) => v >= 0m && v <= MaxPct && Math.Round(v, 2) == v;

    /// <summary>La comisión de una línea: monto × % / 100 a 2 decimales (hacia afuera del cero en el medio).</summary>
    public static decimal Commission(decimal amount, decimal pct) => Math.Round(amount * pct / 100m, 2, MidpointRounding.AwayFromZero);

    /// <summary>La comisión a usar: la que se pide, si no la del contrato del cliente y, si no, la de la compañía.</summary>
    public static decimal ResolvePct(decimal? requested, decimal? contract, decimal tenantDefault) => requested ?? contract ?? tenantDefault;
}
