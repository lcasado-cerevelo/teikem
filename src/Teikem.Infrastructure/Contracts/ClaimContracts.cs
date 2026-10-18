namespace Teikem.Infrastructure.Contracts;

// Lote 111 — faltantes y reclamaciones (REC-#####). Ver docs/p11-piezas-y-reclamaciones-diseno.md.

/// <summary>Una pieza faltante: «Por verificar» (TO_VERIFY), «Perdido» (LOST), «Reclamada» (CLAIMED, con su reclamación) o «Resuelta» (RESOLVED).</summary>
public sealed record MissingPieceDto(string PackageNumber, string Status, string StatusLabel, DateTime? MissingAtUtc, string? ClaimNumber, string LineDescription);

/// <summary>Una orden con piezas faltantes por atender (Por verificar o Perdido, todavía sin reclamación).</summary>
public sealed record MissingOrderDto(Guid OrderPublicId, string OrderNumber, string? WaybillNumber, string ClientName, string OrderStatus, string OrderStatusLabel, int TotalPieces, IReadOnlyList<MissingPieceDto> Pieces);

/// <summary>Marcar como perdidas piezas «Por verificar» de una orden (sin números: todas las «Por verificar» de esa orden).</summary>
public sealed record MarkLostRequest(Guid? OrderPublicId, IList<string>? PieceNumbers);

/// <summary>Crear la reclamación de piezas perdidas de una orden (sin números: todas las «Perdido» de esa orden).</summary>
public sealed record ClaimCreateRequest(Guid? OrderPublicId, IList<string>? PieceNumbers, decimal? DeclaredValue, string? Notes);

/// <summary>Resolver: Resolution = PAID (con AmountPaid), REJECTED o CLOSED.</summary>
public sealed record ClaimResolveRequest(string? Resolution, decimal? AmountPaid, string? Notes, string? RowVersion);

public sealed record ClaimListItemDto(Guid PublicId, string Number, string Status, string StatusLabel, Guid OrderPublicId, string OrderNumber, string? WaybillNumber, string ClientName, string Description,
    int Pieces, decimal? DeclaredValue, string? Resolution, decimal? AmountPaid, DateTime CreatedAtUtc, DateTime? ResolvedAtUtc);

public sealed record ClaimDto(Guid PublicId, string Number, string Status, string StatusLabel, Guid OrderPublicId, string OrderNumber, string? WaybillNumber, string ClientName, string Description,
    decimal? DeclaredValue, string? Notes, string? Resolution, decimal? AmountPaid, string? ResolutionNotes, DateTime CreatedAtUtc, DateTime? ResolvedAtUtc, IReadOnlyList<MissingPieceDto> Pieces, string? RowVersion);
