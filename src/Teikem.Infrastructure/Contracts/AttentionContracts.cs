namespace Teikem.Infrastructure.Contracts;

// Lote 14 (D6) — "Necesita tu atención" del Pulso (GET /api/v1/analytics/attention). Firma posicional: solo se agrega al final,
// con valor por defecto. El texto lo arma la web con t('analytics.attention.items.<code>.*', params).

/// <summary>
/// Una fila del panel: código del tipo de aviso (INVENTORY_DISCREPANCY), módulo de negocio (WAREHOUSE), tono (danger, warn, info),
/// cuántas cosas representa (1 = una fila por registro), parámetros para el texto (cadenas; números con punto decimal y fechas
/// ISO 8601 UTC), ruta y parámetros de "Revisar" y desde cuándo está pendiente (orden: más antiguo primero).
/// </summary>
public sealed record AttentionItemDto(string Code, string Module, string Tone, int Count, IReadOnlyDictionary<string, string> Params,
    string? Route, IReadOnlyDictionary<string, string>? Query, DateTime? SinceUtc);

/// <summary>Total por tipo de aviso con su ruta de "Ver todos (N)".</summary>
public sealed record AttentionGroupDto(string Code, string Module, int Total, string? Route, IReadOnlyDictionary<string, string>? Query);

/// <summary>
/// El panel: Total = pendientes de todos los tipos que el usuario puede ver ("Ver todos (N)"), Items = los 5 más antiguos
/// ("Revisar") y Groups = total y ruta por tipo. Sin pendientes: Total 0 y listas vacías (la web dice "Todo en orden").
/// </summary>
public sealed record AttentionDto(int Total, IReadOnlyList<AttentionItemDto> Items, IReadOnlyList<AttentionGroupDto> Groups);
