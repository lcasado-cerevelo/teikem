namespace Teikem.Infrastructure.Contracts;

// Lote 112 — devolución: el botón «Devolver» de la ficha de la orden.

/// <summary>Devolver una orden: las piezas que viajan en la orden de vuelta (sin números, todas las que no son faltantes) y una nota opcional.</summary>
public sealed record ReturnOrderRequest(IList<string>? PieceNumbers, string? Notes);

/// <summary>La orden de vuelta (nueva, o a la que se sumaron las piezas) y cuántas piezas viajan.</summary>
public sealed record ReturnOrderResultDto(Guid ReturnOrderPublicId, string ReturnOrderNumber, string ReturnWaybillNumber, string OriginalOrderNumber, int Pieces, string Message);

/// <summary>Lote 113 — con cuántos intentos fallidos se sugiere devolver una orden (0 = sin sugerencia). Solo una sugerencia: nunca bloquea.</summary>
public sealed record ReturnSettingsDto(int SuggestAfterAttempts);

public sealed record ReturnSettingsRequest(int? SuggestAfterAttempts);
