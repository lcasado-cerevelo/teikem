namespace Teikem.Infrastructure.Contracts;

// Lote 2 (P5): servicios especiales por cliente (tarifa efectivo-fechada) y tipos compartidos del tenant.

/// <summary>Tipo de servicio especial del tenant (compartido por todos sus clientes). ClientsUsing = clientes con una tarifa abierta de este tipo.</summary>
public sealed record SpecialServiceTypeDto(int Id, string Name, int ClientsUsing, bool IsActive);

/// <summary>Tarifa de un servicio especial del cliente. IsCurrent = vigente en la fecha consultada (EffectiveTo exclusivo).</summary>
public sealed record SpecialServiceDto(int Id, int TypeId, string TypeName, decimal Rate, DateOnly EffectiveFrom, DateOnly? EffectiveTo, bool IsCurrent);

/// <summary>
/// Servicios especiales del cliente en la fecha AsOf (o el historial completo con includeHistory).
/// ComponentEnabled = BillSpecialServices del contrato vigente (false si no hay contrato). Las filas se devuelven aunque el
/// componente esté apagado (R7b: no se pierden; la UI oculta el panel).
/// </summary>
public sealed record SpecialServiceListDto(Guid ClientPublicId, Guid? ContractPublicId, bool ComponentEnabled, DateOnly AsOf, IReadOnlyList<SpecialServiceDto> Items);

/// <summary>
/// Alta: exactamente uno de TypeId (tipo existente del tenant) o NewTypeName (se normaliza y, si ya existe un tipo con el
/// mismo nombre normalizado, se reutiliza; si no, se crea y queda disponible para todos los clientes). Rate >= 0.
/// EffectiveFrom vacío = hoy.
/// </summary>
public sealed record SpecialServiceCreateRequest(int? TypeId, string? NewTypeName, decimal Rate, DateOnly? EffectiveFrom = null);

/// <summary>Nueva versión de la tarifa: cierra la fila abierta en EffectiveFrom y abre otra con Rate (R28). El tipo es inmutable. EffectiveFrom vacío = hoy.</summary>
public sealed record SpecialServiceRateUpdateRequest(decimal Rate, DateOnly? EffectiveFrom = null);

/// <summary>Cierre (quitar conservando historial). EffectiveTo vacío = hoy.</summary>
public sealed record SpecialServiceCloseRequest(DateOnly? EffectiveTo = null);
