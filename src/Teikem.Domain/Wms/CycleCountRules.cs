using System.Globalization;
using Teikem.Domain.Constants;

namespace Teikem.Domain.Wms;

/// <summary>Saldo candidato a entrar en un conteo (foto del sistema): posición, producto, lote y existencia en mano.</summary>
public sealed record CountCandidate(int BinId, string BinCode, int ProductId, string Sku, int? LotId, string? LotNumber, decimal QtyOnHand);

/// <summary>Ubicación actual de una serie conocida por el sistema (estatus, almacén, posición y lote).</summary>
public sealed record SerialLocation(string SerialNumber, string? StatusCode, int? WarehouseId, int? BinId, int? LotId);

/// <summary>Serie que se TRANSFIERE a la posición contada desde donde el sistema la tenía.</summary>
public sealed record SerialTransferMove(string SerialNumber, int FromWarehouseId, int FromBinId, int? LotId);

/// <summary>
/// Resultado del conteo por serie de una línea contra la ubicación ACTUAL (D22):
/// - Removals: esperadas en la posición y no contadas (ni contadas en otra línea del conteo) → ADJUSTMENT de salida (baja);
/// - Additions: contadas y desconocidas, despachadas o fuera de inventario → ADJUSTMENT de entrada (alta);
/// - Transfers: contadas aquí y en inventario en OTRA posición → TRANSFER hacia la posición contada.
/// </summary>
public sealed record SerialVarianceResult(
    IReadOnlyList<string> Removals,
    IReadOnlyList<string> Additions,
    IReadOnlyList<SerialTransferMove> Transfers)
{
    public bool IsEmpty => Removals.Count == 0 && Additions.Count == 0 && Transfers.Count == 0;
}

/// <summary>
/// Lote 6 (P6) — reglas puras del conteo cíclico en modo informado (R16, R33; bitácora L542: 'al confirmar, cada línea con
/// diferencia genera un ajuste'). La foto (SystemQty) es informativa: la diferencia que se ASIENTA al reconciliar es contado
/// menos el saldo en mano ACTUAL bloqueado (D22); la línea queda marcada SystemQtyChanged si el saldo se movió desde la foto.
/// Los mensajes son constantes o métodos estáticos (el manual y la FAQ los citan), en cultura invariante.
/// </summary>
public static class CycleCountRules
{
    public const int MaxLines = 1000;
    public const int MaxListRows = 200;
    public const int MaxSerialLength = 80;
    public const int MaxSerialsPerLine = 500;
    /// <summary>DECIMAL(16,3): 13 dígitos enteros.</summary>
    public const decimal MaxQuantity = 9_999_999_999_999.999m;

    // ---------------------------------------------------------------- mensajes

    public const string TooManyLines = "El conteo admite como máximo 1000 líneas; acote los filtros.";
    public const string NothingSelected = "Los filtros no seleccionan inventario en mano para contar; amplíe los filtros o agregue líneas a mano.";
    public const string CountNotOpen = "El conteo ya fue reconciliado; solo se consulta.";
    public const string CountAlreadyFinished = "El conteo ya se terminó; puede corregir la captura o reconciliarlo.";
    public const string DeleteOnlyOpen = "Solo se elimina un conteo abierto; este ya se terminó de contar.";
    public const string NoLines = "El conteo no tiene líneas.";
    public const string SerialCountedByList = "En productos con serie se capturan los números de serie, no la cantidad.";
    public const string LineDuplicated = "Esa posición, producto y lote ya están en el conteo.";
    public const string CompleteFromCountScreen = "Las tareas de conteo se completan desde Conteo cíclico.";
    public const string CountedNegative = "La cantidad contada no puede ser negativa.";
    public const string QtyDecimals = "La cantidad admite como máximo 3 decimales.";
    public const string QtyTooLarge = "La cantidad excede el máximo permitido.";
    public const string SerialInteger = "En productos con serie la cantidad debe ser entera.";
    public const string SerialsTooMany = "Una línea admite como máximo 500 números de serie.";
    public const string LineNotFoundWhat = "Línea del conteo";
    public const string BinRequired = "Indique la posición.";
    public const string ProductRequired = "Indique el producto.";
    public const string LotAmbiguous = "Indique el lote por su id o por su número, no ambos.";
    public const string LotNumberRequired = "Indique el número de lote.";
    public const string LotNumberTooLong = "El número de lote admite como máximo 60 caracteres.";
    public const string LotDates = "La fecha de fabricación no puede ser posterior al vencimiento.";
    public const string CaptureRequired = "Indique las líneas a capturar.";

    public static string CountIncomplete(int pending)
        => $"Faltan {pending.ToString(CultureInfo.InvariantCulture)} línea(s) por contar.";

    public static string ReservedAboveCount(string sku, string bin, decimal counted, decimal reserved)
        => $"El conteo de {sku} en {bin} ({FormatQty(counted)}) es menor que lo reservado ({FormatQty(reserved)}); libere la reserva antes de reconciliar.";

    public static string SerialNotAllowed(string sku) => $"El producto {sku} no se controla por serie; no capture números de serie.";
    public static string LotRequired(string sku) => $"El producto {sku} se controla por lote; indique el lote.";
    public static string LotNotAllowed(string sku) => $"El producto {sku} no se controla por lote; no indique lote.";
    public static string ProductInactive(string sku) => $"El producto {sku} está inactivo; no admite entradas.";
    public static string SerialTooLong(string serial) => $"El número de serie {serial} excede {MaxSerialLength.ToString(CultureInfo.InvariantCulture)} caracteres.";
    public static string SerialDuplicated(string serial) => $"El número de serie {serial} está repetido.";
    public static string SerialCountedTwice(string serial) => $"La serie {serial} está capturada en más de una línea del conteo.";
    public static string LotExistsWithOtherDates(string lotNumber)
        => $"El lote {lotNumber} ya existe con otras fechas; corrija las fechas o use otro número de lote.";

    // ---------------------------------------------------------------- selección (foto)

    /// <summary>
    /// Líneas del conteo a partir de los saldos candidatos: solo existencia en mano positiva, sin repetir (posición, producto,
    /// lote), en orden determinista (posición, SKU, lote). Más de 1000 → error TooManyLines; ninguna → NothingSelected.
    /// </summary>
    public static (IReadOnlyList<CountCandidate> Lines, string? Error) SelectLines(IEnumerable<CountCandidate> candidates)
    {
        var lines = candidates
            .Where(c => c.QtyOnHand > 0m)
            .GroupBy(c => (c.BinId, c.ProductId, c.LotId))
            .Select(g => new CountCandidate(g.Key.BinId, g.First().BinCode, g.Key.ProductId, g.First().Sku, g.Key.LotId, g.First().LotNumber, g.Sum(x => x.QtyOnHand)))
            .OrderBy(c => c.BinCode, StringComparer.Ordinal)
            .ThenBy(c => c.BinId)
            .ThenBy(c => c.Sku, StringComparer.Ordinal)
            .ThenBy(c => c.ProductId)
            .ThenBy(c => c.LotNumber ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(c => c.LotId ?? 0)
            .ToList();
        if (lines.Count > MaxLines) return (Array.Empty<CountCandidate>(), TooManyLines);
        if (lines.Count == 0) return (Array.Empty<CountCandidate>(), NothingSelected);
        return (lines, null);
    }

    // ---------------------------------------------------------------- foto, diferencia y ajuste

    /// <summary>La foto quedó vieja: el saldo en mano actual ya no es el de la foto.</summary>
    public static bool IsStale(decimal systemQty, decimal currentQty) => systemQty != currentQty;

    /// <summary>Diferencia INFORMATIVA contra la foto (null si la línea no se ha contado).</summary>
    public static decimal? Variance(decimal? counted, decimal systemQty) => counted is decimal c ? c - systemQty : null;

    /// <summary>Lo que se ASIENTA al reconciliar: contado menos el saldo en mano actual bloqueado (D22).</summary>
    public static decimal Adjustment(decimal counted, decimal currentAtReconcile) => counted - currentAtReconcile;

    /// <summary>Lo contado no puede quedar por debajo de lo reservado (el ajuste dejaría reservado &gt; en mano).</summary>
    public static bool CountBelowReserved(decimal counted, decimal reserved) => counted < reserved;

    /// <summary>Líneas sin capturar (CountedQty null).</summary>
    public static int PendingLines(IEnumerable<decimal?> counted) => counted.Count(c => c is null);

    // ---------------------------------------------------------------- captura

    /// <summary>Cantidad contada: ≥ 0 (cero es válido: no hay nada), máximo 3 decimales y dentro de DECIMAL(16,3).</summary>
    public static string? ValidateCountedQty(decimal counted)
    {
        if (counted < 0m) return CountedNegative;
        if (decimal.Round(counted, 3) != counted) return QtyDecimals;
        if (counted > MaxQuantity) return QtyTooLarge;
        return null;
    }

    /// <summary>
    /// Captura de una línea según el seguimiento del producto:
    /// - SERIAL: se capturan las series (la cantidad contada = número de series); una cantidad suelta → SerialCountedByList;
    /// - NONE/LOT: se captura la cantidad; series → SerialNotAllowed.
    /// Sin cantidad ni series la captura se borra (la línea vuelve a pendiente).
    /// </summary>
    public static (decimal? Counted, IReadOnlyList<string>? Serials, string? Field, string? Error) Capture(
        string trackingCode, string sku, decimal? countedQty, IEnumerable<string?>? serialNumbers)
    {
        if (trackingCode == TrackingTypes.Serial)
        {
            if (countedQty is not null && serialNumbers is null) return (null, null, "countedQty", SerialCountedByList);
            if (serialNumbers is null) return (null, null, null, null);
            var (serials, error) = NormalizeSerials(serialNumbers);
            if (error is not null) return (null, null, "serialNumbers", error);
            if (countedQty is decimal q && q != serials.Count) return (null, null, "countedQty", SerialCountedByList);
            return (serials.Count, serials, null, null);
        }
        if (serialNumbers is not null && serialNumbers.Any(s => !string.IsNullOrWhiteSpace(s)))
            return (null, null, "serialNumbers", SerialNotAllowed(sku));
        if (countedQty is not decimal counted) return (null, null, null, null);
        var e = ValidateCountedQty(counted);
        return e is null ? (counted, null, null, null) : (null, null, "countedQty", e);
    }

    /// <summary>Series capturadas: recortadas, sin vacías, máximo 80 caracteres, sin repetir (sin distinguir mayúsculas), hasta 500.</summary>
    public static (IReadOnlyList<string> Serials, string? Error) NormalizeSerials(IEnumerable<string?>? serials)
    {
        var result = new List<string>();
        if (serials is null) return (result, null);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in serials)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var s = raw.Trim();
            if (s.Length > MaxSerialLength) return (Array.Empty<string>(), SerialTooLong(s));
            if (!seen.Add(s)) return (Array.Empty<string>(), SerialDuplicated(s));
            result.Add(s);
        }
        if (result.Count > MaxSerialsPerLine) return (Array.Empty<string>(), SerialsTooMany);
        return (result, null);
    }

    /// <summary>Lote de la línea agregada a mano según el seguimiento: LOT lo exige; NONE lo prohíbe; SERIAL lo admite.</summary>
    public static string? ValidateLot(string trackingCode, string sku, bool hasLot)
    {
        if (trackingCode == TrackingTypes.Lot && !hasLot) return LotRequired(sku);
        if (trackingCode == TrackingTypes.None && hasLot) return LotNotAllowed(sku);
        return null;
    }

    /// <summary>Una fecha no capturada no se compara; las capturadas deben coincidir con las del lote existente (D34).</summary>
    public static bool LotDatesMatch(DateOnly? existingManufacture, DateOnly? existingExpiry, DateOnly? manufacture, DateOnly? expiry)
        => (manufacture is null || manufacture == existingManufacture) && (expiry is null || expiry == existingExpiry);

    // ---------------------------------------------------------------- series

    /// <summary>Estatus de serie que significan 'en inventario' (ocupa posición).</summary>
    public static bool IsInStock(string? serialStatusCode)
        => serialStatusCode is SerialStatuses.Available or SerialStatuses.Reserved;

    /// <summary>
    /// Conteo por serie de UNA línea (posición warehouseId/binId) contra la ubicación ACTUAL:
    /// - expectedAtReconcile: series en inventario hoy en esa posición (producto y lote de la línea);
    /// - counted: series capturadas en la línea;
    /// - locations: ubicación actual de las series contadas que el sistema conoce (clave sin distinguir mayúsculas);
    /// - countedElsewhere: series capturadas en OTRAS líneas del mismo conteo (no se dan de baja aquí: la otra línea las
    ///   transfiere).
    /// Bajas = esperadas no contadas (ni contadas en otra línea). Altas = contadas desconocidas o fuera de inventario
    /// (despachadas o dadas de baja; el ledger rechaza las dadas de baja, D34). TRANSFER = contadas que el sistema tiene en
    /// inventario en otra posición. Una serie en inventario en esta misma posición pero bajo otro lote no se mueve.
    /// </summary>
    public static SerialVarianceResult SerialVariance(
        IEnumerable<string> expectedAtReconcile,
        IEnumerable<string> counted,
        IReadOnlyDictionary<string, SerialLocation> locations,
        int warehouseId,
        int binId,
        IReadOnlySet<string>? countedElsewhere = null)
    {
        var expected = new HashSet<string>(expectedAtReconcile, StringComparer.OrdinalIgnoreCase);
        var countedList = counted.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var countedSet = new HashSet<string>(countedList, StringComparer.OrdinalIgnoreCase);
        var lookup = new Dictionary<string, SerialLocation>(StringComparer.OrdinalIgnoreCase);
        foreach (var (k, v) in locations) lookup[k] = v;

        var removals = expected
            .Where(s => !countedSet.Contains(s) && !(countedElsewhere?.Any(o => string.Equals(o, s, StringComparison.OrdinalIgnoreCase)) ?? false))
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var additions = new List<string>();
        var transfers = new List<SerialTransferMove>();
        foreach (var s in countedList.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            if (expected.Contains(s)) continue;
            if (!lookup.TryGetValue(s, out var loc) || !IsInStock(loc.StatusCode) || loc.WarehouseId is null || loc.BinId is null)
            {
                additions.Add(s);
                continue;
            }
            if (loc.WarehouseId == warehouseId && loc.BinId == binId) continue; // misma posición, otro lote: no se mueve
            transfers.Add(new SerialTransferMove(loc.SerialNumber, loc.WarehouseId.Value, loc.BinId.Value, loc.LotId));
        }
        return new SerialVarianceResult(removals, additions, transfers);
    }

    /// <summary>Primera serie capturada en más de una línea del conteo (sin distinguir mayúsculas), o null.</summary>
    public static string? FirstSerialCountedTwice(IEnumerable<IEnumerable<string>> countedByLine)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in countedByLine)
            foreach (var s in line.Distinct(StringComparer.OrdinalIgnoreCase))
                if (!seen.Add(s)) return s;
        return null;
    }

    public static string FormatQty(decimal qty) => qty.ToString("0.###", CultureInfo.InvariantCulture);
}
