namespace Teikem.Infrastructure.Contracts;

// Orden de salida del inventario (pedido del dueño 2026-10-05): de dónde debe salir un producto, en el MISMO orden en que lo asigna la
// recolección (PickBatchRules.Eligible, FEFO D14). Una sola regla, en el servidor: la app y las pantallas solo la siguen.

/// <summary>
/// Una existencia de la que puede salir el producto: posición (con su zona y tipo de zona), lote (con vencimiento) y lo DISPONIBLE (en mano −
/// reservado). Rank = lugar en el orden de salida del producto en ese almacén (1 = sale primero). Solo posiciones activas y recolectables
/// (sin cuarentena ni cruce de muelle) con disponible mayor que cero, de productos activos.
/// </summary>
public sealed record StockExitOptionDto(Guid ProductPublicId, int BinId, string BinCode, string? ZoneCode, string? ZoneTypeCode, int? LotId,
    string? LotNumber, DateOnly? ExpiryDate, decimal Available, int Rank);

/// <summary>Página de opciones de salida, por producto y luego por Rank. ServerTimeUtc = instante en que se calculó (la app lo guarda).</summary>
public sealed record StockExitPageDto(int Total, int Skip, int Take, DateTime ServerTimeUtc, IReadOnlyList<StockExitOptionDto> Items);
