namespace Teikem.Infrastructure.Contracts;

// ======================================================================================================================
// Lote 8A — contratos comunes de la sincronización por diferencia del aparato de almacén. Firma posicional FIJA. Los DTOs
// compactos de cada recurso (SyncProductDto, SyncBinDto, ...) viven junto a SyncService.
// ======================================================================================================================

/// <summary>
/// Página de sincronización: Items en orden de id ascendente; NextCursor = null cuando ya no hay más páginas;
/// ServerTimeUtc = hora del servidor al atender la página (la marca de agua de la siguiente pasada es el ServerTimeUtc de la
/// PRIMERA página menos 5 minutos, ver SyncRules.NextSince).
/// </summary>
public sealed record SyncPage<T>(IReadOnlyList<T> Items, string? NextCursor, DateTime ServerTimeUtc);

/// <summary>
/// Consulta de sincronización: Since (UTC; null = carga completa), Cursor opaco de la página anterior, Take ≤ 500
/// ('El máximo por página es 500.') y almacén opcional.
/// </summary>
public sealed record SyncQuery(DateTime? Since = null, string? Cursor = null, int Take = 500, Guid? WarehousePublicId = null);
