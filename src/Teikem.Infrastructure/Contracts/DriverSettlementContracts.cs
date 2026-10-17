namespace Teikem.Infrastructure.Contracts;

// Lote 94 (P9) — liquidación a choferes. Records sin lógica; firma posicional fija.

/// <summary>Generar (o ver la vista previa de) una liquidación. DriverPublicId vacío = todos los choferes con algo por pagar.</summary>
public sealed record DriverSettlementRequest(Guid? DriverPublicId = null, DateOnly? PeriodStart = null, DateOnly? PeriodEnd = null, string? Note = null);

/// <summary>Ajuste manual del monto de una línea (con motivo). Un monto igual al calculado quita el ajuste.</summary>
public sealed record DriverSettlementAdjustRequest(decimal? Amount = null, string? Note = null);

public sealed record DriverSettlementCancelRequest(string? Comment = null);

public sealed record DriverSettlementListItemDto(
    Guid PublicId,
    string Number,
    string StatusCode,
    string Status,
    string StatusColor,
    Guid? DriverPublicId,
    string? DriverCode,
    string? DriverName,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    int LineCount,
    decimal TotalAmount,
    string PayoutFormulaCode,
    DateTime GeneratedAtUtc);

/// <summary>Una línea de la liquidación: la entrega de una orden con sus intentos (DELIVERY) o un viaje pagado (TRIP). Breakdown = el desglose que calculó el motor.</summary>
public sealed record DriverSettlementLineDto(
    int Id,
    Guid DriverPublicId,
    string DriverCode,
    string DriverName,
    string Kind,
    DateOnly ServiceDate,
    Guid? OrderPublicId,
    string? OrderNumber,
    Guid? TripPublicId,
    string Description,
    int AttemptCount,
    IReadOnlyList<PayoutLineDto> Breakdown,
    bool RateMissing,
    decimal CalculatedAmount,
    decimal Amount,
    bool Adjusted,
    string? AdjustmentNote);

public sealed record DriverSettlementDto(
    Guid PublicId,
    string Number,
    string StatusCode,
    string Status,
    string StatusColor,
    Guid? DriverPublicId,
    string? DriverCode,
    string? DriverName,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    string PayoutFormulaCode,
    string Currency,
    int LineCount,
    decimal TotalAmount,
    decimal DeliveriesAmount,
    decimal TripsAmount,
    int SkippedNoDriver,
    string? Note,
    DateTime GeneratedAtUtc,
    string? ReviewedBy,
    DateTime? ReviewedAtUtc,
    string? ApprovedBy,
    DateTime? ApprovedAtUtc,
    DateTime? ExportedAtUtc,
    string RowVersion,
    IReadOnlyList<DriverSettlementLineDto> Lines);

/// <summary>Lo que entraría en la liquidación, sin guardar nada.</summary>
public sealed record DriverSettlementPreviewDto(
    string PayoutFormulaCode,
    int LineCount,
    decimal TotalAmount,
    int SkippedNoDriver,
    IReadOnlyList<DriverSettlementLineDto> Lines);
