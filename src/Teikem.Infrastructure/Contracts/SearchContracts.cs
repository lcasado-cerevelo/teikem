namespace Teikem.Infrastructure.Contracts;

/// <summary>Un resultado de la búsqueda global: tipo (ORDER, CLIENT…), id (publicId o número interno), título y subtítulo, el campo por el que salió, si el valor fue exacto y claves extra para llegar a la pantalla (p. ej. warehouse y zone de una posición).</summary>
public sealed record SearchHitDto(string Type, string Id, string Title, string? Subtitle, string MatchedBy, bool Exact, IReadOnlyDictionary<string, string>? Keys = null);

/// <summary>Resultado de GET /api/v1/search: lo buscado (recortado) y los resultados (lo exacto primero).</summary>
public sealed record SearchResultDto(string Query, IReadOnlyList<SearchHitDto> Hits);
