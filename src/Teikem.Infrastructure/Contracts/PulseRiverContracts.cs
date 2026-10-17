namespace Teikem.Infrastructure.Contracts;

// Lote 88 (P12) — los dos ríos del Pulso: «Paquetes en la calle» (órdenes) y «Dinero COD».

/// <summary>Un dato secundario de un nodo. Kind = "count" (una cantidad) o "money" (un monto en la moneda de la compañía).</summary>
public sealed record PulseRiverValueDto(string Key, decimal Value, string Kind);

/// <summary>Un nodo del río: su valor principal (cantidad o monto), datos secundarios y, si la tarjeta lleva barritas, los últimos 7 días (el último es hoy).</summary>
public sealed record PulseRiverNodeDto(string Key, decimal Value, string Kind, IReadOnlyList<PulseRiverValueDto> Details, IReadOnlyList<decimal>? Spark);

/// <summary>River = "ORDERS" o "COD"; Day = el día de la compañía al que se refiere «hoy».</summary>
public sealed record PulseRiverDto(string River, DateOnly Day, IReadOnlyList<PulseRiverNodeDto> Nodes);
