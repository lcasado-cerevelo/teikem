namespace Teikem.Infrastructure.Contracts;

// Lote 7A — feed "Actividad reciente" del Pulso (GET /api/v1/analytics/activity).

/// <summary>
/// Consulta del feed: módulo de negocio (WAREHOUSE…; null = el primero visible), ventana 24h (default) | 48h | today,
/// solo obligatorios y página (take ≤ 50, default 50; más → 400 'El máximo por página es 50.').
/// </summary>
public sealed record ActivityQuery(string? Module = null, string Window = "24h", bool OnlyMandatory = false, int Skip = 0, int Take = 50);

/// <summary>
/// Un evento del feed: instante UTC, código del catálogo ActivityEventType, módulo, bandera de obligatorio, etiqueta en el
/// idioma del usuario, entidad enlazada (código EntityType, id y PublicId), referencia visible (número o SKU), detalle corto y
/// quién lo hizo.
/// </summary>
public sealed record ActivityEventDto(DateTime OccurredAtUtc, string Code, string Module, bool Mandatory, string Label, string EntityType,
    int EntityId, Guid? PublicId, string Reference, string? Detail, int? UserId, string? UserName);

/// <summary>Página del feed: total en la ventana, módulos (pestañas) que el usuario ve y filas de la página.</summary>
public sealed record ActivityPageDto(int Total, IReadOnlyList<string> VisibleModules, IReadOnlyList<ActivityEventDto> Items);
