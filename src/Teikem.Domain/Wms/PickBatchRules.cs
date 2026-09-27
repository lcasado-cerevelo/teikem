using System.Globalization;
using Teikem.Domain.Constants;

namespace Teikem.Domain.Wms;

/// <summary>Saldo candidato para recolectar (una fila de StockBalance del producto en el almacén), con su posición y zona.</summary>
/// <param name="Available">Disponible = en mano − reservado, calculado en código (nunca la columna computada QtyAvailable).</param>
public sealed record PickCandidate(int BinId, string BinCode, string? ZoneTypeCode, bool BinActive, int? LotId, DateOnly? ExpiryDate, decimal Available);

/// <summary>Porción asignada de una línea de recolección: posición, lote y cantidad (magnitud; el ledger pone el signo).</summary>
public sealed record PickAllocation(int BinId, int? LotId, decimal Quantity);

/// <summary>Resultado de asignar una línea: porciones en orden FEFO y el faltante (0 si alcanzó).</summary>
public sealed record PickAllocationResult(IReadOnlyList<PickAllocation> Allocations, decimal Shortfall)
{
    public bool IsComplete => Shortfall <= 0m;
    public decimal Allocated => Allocations.Sum(a => a.Quantity);
}

/// <summary>Fila ligera del listado de recolecciones: lo necesario para los filtros de texto y la búsqueda final.</summary>
public sealed record PickBatchListRow(
    int PickBatchId,
    string Number,
    string? OrderNumber,
    string? PackBatchNumber,
    string? ClientInvoiceNumber,
    string? ClientName,
    IReadOnlyCollection<string> Skus);

/// <summary>
/// Lote 6 (P7) — reglas puras de Recolección y empaque ad hoc (R13, R34-R43; D10-D15, D37-D39, D48). Sin EF ni servicios:
/// PickBatchService las traduce a 400/404/409/422. Los mensajes son públicos porque el manual (docs/manual/06) y la FAQ los
/// citan tal cual.
/// - Una recolección lleva productos de UN solo dueño (D15); si el dueño es un cliente 3PL, la orden del empaque es de ese
///   cliente.
/// - Asignación FEFO (D14): vencimiento ascendente (sin fecha al final), luego zona PICKING &lt; RESERVE &lt; REFRIGERATED &lt;
///   STAGING, luego código de posición, id de posición y lote. Excluye QUARANTINE, CROSSDOCK, posiciones inactivas y
///   disponible ≤ 0 (lo reservado no se recolecta). Es el mismo orden determinista que StockAllocator.
/// - Eliminar: una recolección COLLECTED se elimina siempre; una PACKED solo si su orden sigue activa y en la etapa inicial.
/// - Listado: primero los filtros (orden, factura) y DESPUÉS la búsqueda libre sobre el resultado filtrado.
/// </summary>
public static class PickBatchRules
{
    public const int MaxLines = 100;
    public const int MaxSerialsPerLine = 500;
    public const int MaxSerialLength = 80;
    public const int DefaultPageSize = 100;
    public const int MaxPageSize = 200;
    public const decimal MaxQuantity = 9_999_999_999_999.999m;

    // ---------------------------------------------------------------- mensajes (manual 06 y FAQ)

    public const string SingleOwner = "Una recolección solo puede tener productos de un mismo dueño.";
    public const string PackSpecialNotAllowed = "Un empaque no puede ser una entrega especial ni llevar chofer.";
    public const string LinesRequired = "Indique al menos una línea a recolectar.";
    /// <summary>Lote 8A: 'pack' en POST /api/v1/pick-batches (la variante atómica tiene su propia ruta y su propio tipo).</summary>
    public const string PackUseCollectAndPack = "Para recolectar y empacar en una llamada use POST /api/v1/pick-batches/collect-and-pack.";
    public const string TooManyLines = "La recolección admite como máximo 100 líneas.";
    public const string ProductRequired = "Indique el producto.";
    public const string QuantityRequired = "La cantidad debe ser mayor que cero.";
    public const string QuantityDecimals = "La cantidad admite como máximo 3 decimales.";
    public const string QuantityTooLarge = "La cantidad excede el máximo permitido.";
    public const string SerialQuantityMismatch = "En productos con serie la cantidad debe ser igual al número de series escaneadas.";
    public const string SerialTooLong = "Cada número de serie admite como máximo 80 caracteres.";
    public const string TooManySerials = "Una línea admite como máximo 500 series.";
    public const string OrderRequired = "Indique los datos de la orden que se crea al empacar.";
    public const string WarehouseRequired = "Indique el almacén: la compañía tiene más de uno.";
    public const string NoActiveWarehouse = "La compañía no tiene almacenes activos.";
    public const string WarehouseInactive = "El almacén está inactivo; no se puede recolectar.";
    public const string NumberTaken = "Ya existe una recolección con ese número; intente de nuevo.";
    public const string OrderTaken = "La orden ya está ligada a otra recolección.";

    public static string OrderClientMustBeOwner(string clientName)
        => $"La orden debe ser del cliente dueño del inventario ({clientName}).";

    public static string DeleteBlocked(string number, string status)
        => $"La orden de la recolección {number} ya avanzó a '{status}'; la recolección ya no se puede eliminar.";

    public static string NotCollected(string number) => $"La recolección {number} ya fue empacada.";

    public static string AlreadyCancelled(string number) => $"La recolección {number} fue eliminada; solo se consulta.";

    public static string SerialRequired(string sku) => $"El producto {sku} tiene serie: escanee las series a recolectar.";

    public static string SerialNotAllowed(string sku) => $"El producto {sku} no maneja serie.";

    public static string LotNotTracked(string sku) => $"El producto {sku} no maneja lote.";

    public static string SerialDuplicated(string serial) => $"La serie {serial} está repetida en la recolección.";

    public static string SerialNotAvailable(string serial)
        => $"La serie {serial} no está disponible en el almacén (no existe, ya salió o está en otra posición o lote).";

    public static string ProductInactive(string sku) => $"El producto {sku} está inactivo; no se puede recolectar.";

    public static string BinInactive(string binCode) => $"La posición {binCode} está inactiva.";

    public static string ZoneNotPickable(string binCode, string zoneType)
        => $"La posición {binCode} está en una zona {zoneType}; de ahí no se recolecta.";

    public static string ReversalBinInactive(string binCode)
        => $"La posición {binCode} de la recolección está inactiva; reactívela para eliminar la recolección y restaurar el inventario.";

    /// <summary>Mismo formato que InventoryRules.InsufficientStockMessage (409 insufficient_stock).</summary>
    public static string InsufficientStock(string sku, string where, decimal available, decimal requested)
        => $"Inventario insuficiente de {sku} en {where}: disponible {FormatQty(available)}, solicitado {FormatQty(requested)}.";

    /// <summary>'Orden: {x} · Factura: {y|—}' (R40: el número de orden y la factura final quedan a la vista en el lote).</summary>
    public static string DisplayNumbers(string? orderNumber, string? invoiceNumber)
        => $"Orden: {Dash(orderNumber)} · Factura: {Dash(invoiceNumber)}";

    // ---------------------------------------------------------------- validaciones de captura

    /// <summary>Cantidad de una línea sin serie: &gt; 0, a lo sumo 3 decimales y dentro de DECIMAL(16,3). Null = válida.</summary>
    public static string? ValidateQuantity(decimal? qty)
    {
        if (qty is not decimal q || q <= 0m) return QuantityRequired;
        if (decimal.Round(q, 3) != q) return QuantityDecimals;
        if (q > MaxQuantity) return QuantityTooLarge;
        return null;
    }

    /// <summary>
    /// Series de una línea: trim, sin vacíos, máximo 80 caracteres, sin repetidas (sin distinguir mayúsculas; se conserva
    /// la primera escritura) y máximo 500. Devuelve la lista o el mensaje de error.
    /// </summary>
    public static (IReadOnlyList<string> Serials, string? Error) NormalizeSerials(IEnumerable<string?>? serials)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in serials ?? Array.Empty<string?>())
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var s = raw.Trim();
            if (s.Length > MaxSerialLength) return (Array.Empty<string>(), SerialTooLong);
            if (!seen.Add(s)) return (Array.Empty<string>(), SerialDuplicated(s));
            result.Add(s);
        }
        if (result.Count > MaxSerialsPerLine) return (Array.Empty<string>(), TooManySerials);
        return (result, null);
    }

    /// <summary>Cantidad de una línea con serie: la de las series; si se indicó cantidad, debe coincidir (entera).</summary>
    public static (decimal Quantity, string? Error) SerialQuantity(decimal? requested, int serialCount)
    {
        if (requested is decimal q && q != serialCount) return (0m, SerialQuantityMismatch);
        return (serialCount, null);
    }

    /// <summary>Un solo dueño (D15): los ClientId de los productos (NULL = propio del tenant) deben ser todos iguales.</summary>
    public static bool IsSingleOwner(IEnumerable<int?> ownerClientIds)
        => (ownerClientIds ?? Array.Empty<int?>()).Distinct().Count() <= 1;

    /// <summary>
    /// Cliente de la orden del empaque (D15): con dueño 3PL la orden debe ser de ese cliente; con inventario propio (NULL)
    /// cualquier cliente sirve.
    /// </summary>
    public static bool OrderClientAllowed(int? ownerClientId, int? orderClientId)
        => ownerClientId is null || orderClientId == ownerClientId;

    /// <summary>Un empaque crea una orden normal: sin entrega especial y sin chofer.</summary>
    public static bool PackRequestAllowed(bool isSpecialDelivery, bool hasDriver) => !isSpecialDelivery && !hasDriver;

    /// <summary>¿Se puede empacar? Solo una recolección activa en COLLECTED.</summary>
    public static bool CanPack(string? statusCode, bool isActive)
        => isActive && string.Equals(statusCode, PickBatchStatuses.Collected, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// ¿Se puede eliminar? COLLECTED siempre; PACKED solo si su orden sigue activa y en la etapa inicial (se elimina con ella);
    /// CANCELLED nunca. El llamador verifica además que la recolección siga activa.
    /// </summary>
    public static bool CanDelete(string? statusCode, bool orderIsActive, bool orderIsInitial)
    {
        if (string.Equals(statusCode, PickBatchStatuses.Collected, StringComparison.OrdinalIgnoreCase)) return true;
        if (string.Equals(statusCode, PickBatchStatuses.Packed, StringComparison.OrdinalIgnoreCase)) return orderIsActive && orderIsInitial;
        return false;
    }

    // ---------------------------------------------------------------- asignación FEFO (D14)

    /// <summary>¿Se recolecta de una posición de este tipo de zona? QUARANTINE y CROSSDOCK nunca.</summary>
    public static bool IsPickableZone(string? zoneTypeCode)
        => !string.Equals(zoneTypeCode, ZoneTypes.Quarantine, StringComparison.OrdinalIgnoreCase)
           && !string.Equals(zoneTypeCode, ZoneTypes.CrossDock, StringComparison.OrdinalIgnoreCase);

    /// <summary>Rango de zona para el desempate FEFO: PICKING 0, RESERVE 1, REFRIGERATED 2, STAGING 3, otra o sin tipo 4.</summary>
    public static int ZoneRank(string? zoneTypeCode) => zoneTypeCode?.ToUpperInvariant() switch
    {
        ZoneTypes.Picking => 0,
        ZoneTypes.Reserve => 1,
        ZoneTypes.Refrigerated => 2,
        ZoneTypes.Staging => 3,
        _ => 4,
    };

    /// <summary>Candidatos elegibles en orden FEFO determinista, opcionalmente restringidos a una posición y/o un lote.</summary>
    public static IReadOnlyList<PickCandidate> Eligible(IEnumerable<PickCandidate> candidates, int? binId = null, int? lotId = null)
        => (candidates ?? Array.Empty<PickCandidate>())
            .Where(c => c.BinActive && IsPickableZone(c.ZoneTypeCode) && c.Available > 0m)
            .Where(c => binId is null || c.BinId == binId)
            .Where(c => lotId is null || c.LotId == lotId)
            .OrderBy(c => c.ExpiryDate is null ? 1 : 0)
            .ThenBy(c => c.ExpiryDate)
            .ThenBy(c => ZoneRank(c.ZoneTypeCode))
            .ThenBy(c => c.BinCode, StringComparer.Ordinal)
            .ThenBy(c => c.BinId)
            .ThenBy(c => c.LotId ?? 0)
            .ToList();

    /// <summary>
    /// Asigna qty sobre los candidatos (FEFO, D14): toma de cada saldo elegible hasta cubrir. Si no alcanza, devuelve lo
    /// asignado y el faltante (el servicio responde 409 insufficient_stock sin escribir nada).
    /// </summary>
    public static PickAllocationResult Allocate(decimal qty, IEnumerable<PickCandidate> candidates, int? binId = null, int? lotId = null)
    {
        var result = new List<PickAllocation>();
        var remaining = qty;
        foreach (var c in Eligible(candidates, binId, lotId))
        {
            if (remaining <= 0m) break;
            var take = Math.Min(remaining, c.Available);
            if (take <= 0m) continue;
            result.Add(new PickAllocation(c.BinId, c.LotId, take));
            remaining -= take;
        }
        return new PickAllocationResult(result, Math.Max(0m, remaining));
    }

    // ---------------------------------------------------------------- listado: filtros y después búsqueda final

    /// <summary>
    /// Filtros de texto del listado (se aplican ANTES de la búsqueda): número de orden y número de factura, sin distinguir
    /// mayúsculas y por contenido. Un filtro vacío no filtra. Una recolección sin orden no pasa un filtro de orden.
    /// </summary>
    public static bool MatchesFilters(PickBatchListRow row, string? orderNumber, string? invoiceNumber)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (!string.IsNullOrWhiteSpace(orderNumber) && !Contains(row.OrderNumber, orderNumber.Trim())) return false;
        if (!string.IsNullOrWhiteSpace(invoiceNumber) && !Contains(row.ClientInvoiceNumber, invoiceNumber.Trim())) return false;
        return true;
    }

    /// <summary>
    /// Búsqueda final sobre el resultado ya filtrado: número de la recolección, número de empaque, orden, factura, cliente o
    /// SKU de sus líneas (contenido, sin distinguir mayúsculas). Vacía = todo.
    /// </summary>
    public static bool MatchesSearch(PickBatchListRow row, string? search)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (string.IsNullOrWhiteSpace(search)) return true;
        var s = search.Trim();
        return Contains(row.Number, s) || Contains(row.PackBatchNumber, s) || Contains(row.OrderNumber, s)
               || Contains(row.ClientInvoiceNumber, s) || Contains(row.ClientName, s)
               || (row.Skus ?? Array.Empty<string>()).Any(sku => Contains(sku, s));
    }

    /// <summary>Aplica en orden los filtros y luego la búsqueda (R41: la búsqueda nunca amplía lo que los filtros acotaron).</summary>
    public static IReadOnlyList<PickBatchListRow> FilterThenSearch(IEnumerable<PickBatchListRow> rows, string? orderNumber, string? invoiceNumber, string? search)
        => (rows ?? Array.Empty<PickBatchListRow>())
            .Where(r => MatchesFilters(r, orderNumber, invoiceNumber))
            .Where(r => MatchesSearch(r, search))
            .ToList();

    /// <summary>Paginación del listado: skip ≥ 0; take por defecto 100 y como máximo 200.</summary>
    public static (int Skip, int Take) NormalizePaging(int skip, int take)
        => (Math.Max(0, skip), take <= 0 ? DefaultPageSize : Math.Min(take, MaxPageSize));

    // ---------------------------------------------------------------- dinero y formato

    /// <summary>Costo total: Σ cantidad × costo congelado (D35), Round4 AwayFromZero; null si ninguna línea tiene costo.</summary>
    public static decimal? TotalCost(IEnumerable<(decimal Quantity, decimal? UnitCost)> lines)
    {
        var withCost = (lines ?? Array.Empty<(decimal, decimal?)>()).Where(l => l.UnitCost is not null).ToList();
        if (withCost.Count == 0) return null;
        return Round4(withCost.Sum(l => l.Quantity * l.UnitCost!.Value));
    }

    public static decimal Round4(decimal value) => Math.Round(value, 4, MidpointRounding.AwayFromZero);

    /// <summary>Cantidad para mensajes: cultura invariante, sin ceros sobrantes (8, 2.5, 0.125).</summary>
    public static string FormatQty(decimal qty) => qty.ToString("0.###", CultureInfo.InvariantCulture);

    private static string Dash(string? value) => string.IsNullOrWhiteSpace(value) ? "—" : value.Trim();

    private static bool Contains(string? haystack, string needle)
        => haystack is not null && haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);
}
