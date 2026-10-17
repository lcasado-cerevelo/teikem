namespace Teikem.Infrastructure.Contracts;

// Lote 86 (P8) — remesa COD al cliente: bruto cobrado, comisión y neto a remitir.

public sealed record CodSettingsDto(decimal DefaultCommissionPct);

public sealed record CodSettingsRequest(decimal? DefaultCommissionPct = null);

/// <summary>POST /cod/remittances: el cliente y el período (por la fecha del cobro, en la zona de la compañía). CommissionPct = la comisión de esta remesa; sin ella, la del contrato del cliente o la de la compañía.</summary>
public sealed record CodRemittanceRequest(Guid? ClientPublicId = null, DateOnly? PeriodStart = null, DateOnly? PeriodEnd = null, decimal? CommissionPct = null);

public sealed record CodRemittanceLineDto(long Id, long CollectionId, Guid OrderPublicId, string OrderNumber, string? InvoiceNumber, DateTime CollectedAtUtc, string Method, string MethodLabel, string? CheckNumber,
    string? Reference, decimal AmountCollected, decimal CommissionAmount, decimal NetAmount);

public sealed record CodRemittanceListItemDto(Guid PublicId, string Number, string Status, string StatusLabel, string StatusColor, Guid ClientPublicId, string ClientName, DateOnly PeriodStart, DateOnly PeriodEnd,
    decimal GrossCollected, decimal? CommissionPct, decimal CommissionAmount, decimal NetRemitted, int LineCount, DateTime GeneratedAtUtc, DateTime? RemittedAtUtc);

public sealed record CodRemittanceDto(Guid PublicId, string Number, string Status, string StatusLabel, string StatusColor, Guid ClientPublicId, string ClientName, DateOnly PeriodStart, DateOnly PeriodEnd,
    decimal GrossCollected, decimal? CommissionPct, decimal CommissionAmount, decimal NetRemitted, string Currency, DateTime GeneratedAtUtc, DateTime? ApprovedAtUtc, string? ApprovedByName, DateTime? RemittedAtUtc,
    string? ExportRef, IReadOnlyList<CodRemittanceLineDto> Lines);

/// <summary>Lo que entraría en una remesa (sin crearla): los cobros cuadrados del cliente en el período y la comisión que se aplicaría.</summary>
public sealed record CodRemittanceCandidatesDto(Guid ClientPublicId, string ClientName, DateOnly PeriodStart, DateOnly PeriodEnd, decimal CommissionPct, string CommissionSource, decimal GrossCollected,
    decimal CommissionAmount, decimal NetRemitted, IReadOnlyList<CodRemittanceLineDto> Lines);

public sealed record CodRemitRequest(string? ExportRef = null);
