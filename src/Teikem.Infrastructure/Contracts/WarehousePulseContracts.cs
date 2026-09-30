using Teikem.Domain.Wms;

namespace Teikem.Infrastructure.Contracts;

/// <summary>
/// Lote 15 (P3) — consulta de la franja "Almacén hoy" del Pulso del día: almacenes por PublicId (vacío = todos; uno que no existe
/// o es de otra compañía no cae en "todos": da cifras en cero, como el Kárdex) y número de días locales que terminan hoy (1–14;
/// por defecto 7, D1).
/// </summary>
public sealed record WarehousePulseQuery(Guid[]? WarehousePublicIds = null, int Days = WarehousePulseRules.DefaultDays);

/// <summary>
/// Un día local de la franja (hora de la compañía): unidades recibidas (neto de recepción + diferencias de recepción) y sus
/// movimientos del Kárdex; unidades de salida (recolección + cruce de muelle − recolecciones eliminadas ese día; puede ser
/// negativa si ese día solo se eliminaron recolecciones) y sus movimientos; conteos cerrados ese día en Diferencia.
/// </summary>
public sealed record WarehousePulseDayDto(DateOnly Date, decimal ReceivedUnits, int ReceivedMovements, decimal OutboundUnits,
    int OutboundMovements, int CountsWithVariance);

/// <summary>
/// Franja "Almacén hoy" (D1–D3): zona horaria (IANA), hoy local, rango UTC cubierto [FromUtc, ToUtc), la serie por día (de la
/// más vieja a hoy, siempre Days elementos), los números de HOY y los totales de los días, "Productos bajo mínimo" en este
/// momento (solo el número, sin serie: el mismo cálculo que GET /products?belowMin=true) y el tono naranja de D3.
/// </summary>
public sealed record WarehousePulseDaysDto(string TimeZone, DateOnly Today, DateTime FromUtc, DateTime ToUtc,
    IReadOnlyList<WarehousePulseDayDto> Days,
    decimal ReceivedToday, decimal ReceivedTotal, decimal OutboundToday, decimal OutboundTotal,
    int CountsWithVarianceToday, int CountsWithVarianceTotal, int BelowMinProducts, bool CountsAlert, bool BelowMinAlert);
