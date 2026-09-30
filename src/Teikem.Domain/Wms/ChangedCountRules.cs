using System.Globalization;
using Teikem.Domain.Common;

namespace Teikem.Domain.Wms;

/// <summary>
/// Forma de un movimiento del Kárdex para "lo cambiado": sus dos lados (almacén y posición), el producto, el lote y si viene de un
/// conteo (Ref CYCLE_COUNT: no genera otro conteo, D3).
/// </summary>
public sealed record ChangedMovement(int? FromWarehouseId, int? FromBinId, int? ToWarehouseId, int? ToBinId, int ProductId, int? LotId,
    bool FromCycleCount = false);

/// <summary>Clave (posición, producto, lote) que se movió en la ventana.</summary>
public sealed record MovedKey(int BinId, int ProductId, int? LotId);

/// <summary>
/// Lote 14 (D2, D3, D4 y la hora de Puerto Rico) — reglas puras de "Conteo de lo cambiado":
/// - Ventana [desde, hasta): por defecto desde el último ChangesToUtc de "lo cambiado" en ese almacén; la primera vez, desde las
///   00:00 LOCALES de hoy (zona de la compañía, ITenantClock); nunca más de <see cref="MaxWindowDays"/> días atrás. Las fechas
///   se pueden editar antes de crear; 'hasta' no pasa de ahora.
/// - Posiciones: los lados de cada movimiento que caen en el almacén, sin los movimientos que vienen de un conteo.
/// - Líneas de una posición: TODO lo que tiene en mano (saldo &gt; 0) y, si se piden las vaciadas, las claves que se movieron en la
///   ventana y hoy están en 0 (SystemQty 0: se confirma que de verdad no hay nada).
/// - Tope: <see cref="MaxPositions"/> posiciones (conteos) por generación.
/// Los mensajes son constantes o métodos estáticos (el manual y la FAQ los citan), en cultura invariante.
/// </summary>
public static class ChangedCountRules
{
    public const int MaxPositions = 200;
    public const int MaxWindowDays = 31;

    // ---------------------------------------------------------------- mensajes

    public const string WindowTooLong = "El rango de \"lo cambiado\" admite como máximo 31 días.";

    public static string NoMovements(string warehouse, DateTime fromUtc, DateTime toUtc, TimeZoneInfo zone)
        => $"No hubo movimientos en {warehouse} entre {FormatLocal(fromUtc, zone)} y {FormatLocal(toUtc, zone)}; no hay posiciones que contar.";

    public static string AllHaveOpenCount(int positions)
        => $"Las {positions.ToString(CultureInfo.InvariantCulture)} posiciones con cambios ya tienen un conteo pendiente.";

    public static string TooManyPositions(int positions)
        => $"Hay {positions.ToString(CultureInfo.InvariantCulture)} posiciones con cambios; se generan como máximo 200 a la vez. Acote el rango de fechas o las zonas.";

    /// <summary>Fecha y hora LOCAL de la compañía (dd/MM/yyyy HH:mm) para los mensajes.</summary>
    public static string FormatLocal(DateTime utc, TimeZoneInfo zone)
        => LocalDay.ToLocal(utc, zone).ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);

    // ---------------------------------------------------------------- ventana

    /// <summary>
    /// Ventana por defecto: desde el último 'hasta' de "lo cambiado" del almacén (o, la primera vez, las 00:00 locales de hoy)
    /// hasta ahora; si ese 'desde' queda a más de MaxWindowDays días, se acota a ahora − MaxWindowDays. Un 'desde' en el
    /// futuro (reloj adelantado) se lleva a ahora.
    /// </summary>
    public static (DateTime FromUtc, DateTime ToUtc) DefaultWindow(DateTime? lastChangesToUtc, DateTime nowUtc, TimeZoneInfo zone)
    {
        var now = AsUtc(nowUtc);
        var from = lastChangesToUtc is DateTime last ? AsUtc(last) : LocalDay.StartOfDayUtc(LocalDay.Today(now, zone), zone);
        var floor = now.AddDays(-MaxWindowDays);
        if (from < floor) from = floor;
        if (from > now) from = now;
        return (from, now);
    }

    /// <summary>
    /// Ventana pedida (null = la por defecto de cada lado). 'Hasta' no pasa de ahora. Devuelve el error (campo, mensaje) si
    /// 'desde' es posterior a 'hasta' (KardexRules.RangeInverted) o si el rango pasa de MaxWindowDays días.
    /// </summary>
    public static (DateTime FromUtc, DateTime ToUtc, string? Field, string? Error) ResolveWindow(DateTime? fromUtc, DateTime? toUtc,
        DateTime? lastChangesToUtc, DateTime nowUtc, TimeZoneInfo zone)
    {
        var (defaultFrom, defaultTo) = DefaultWindow(lastChangesToUtc, nowUtc, zone);
        var from = fromUtc is DateTime f ? AsUtc(f) : defaultFrom;
        var to = toUtc is DateTime t ? AsUtc(t) : defaultTo;
        if (to > defaultTo) to = defaultTo;
        if (from > to) return (from, to, "fromUtc", KardexRules.RangeInverted);
        if (to - from > TimeSpan.FromDays(MaxWindowDays)) return (from, to, "fromUtc", WindowTooLong);
        return (from, to, null, null);
    }

    /// <summary>Las fechas de la base o del request sin Kind se tratan como UTC; las locales se convierten.</summary>
    public static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };

    // ---------------------------------------------------------------- posiciones y líneas

    /// <summary>
    /// Claves (posición, producto, lote) movidas en el almacén: el lado From si su almacén es este y el lado To si lo es; los
    /// movimientos de un conteo no cuentan (D3). Sin repetir, en orden (posición, producto, lote).
    /// </summary>
    public static IReadOnlyList<MovedKey> MovedKeys(IEnumerable<ChangedMovement> movements, int warehouseId)
    {
        var keys = new HashSet<MovedKey>();
        foreach (var m in movements)
        {
            if (m.FromCycleCount) continue;
            if (m.FromWarehouseId == warehouseId && m.FromBinId is int from) keys.Add(new MovedKey(from, m.ProductId, m.LotId));
            if (m.ToWarehouseId == warehouseId && m.ToBinId is int to) keys.Add(new MovedKey(to, m.ProductId, m.LotId));
        }
        return keys.OrderBy(k => k.BinId).ThenBy(k => k.ProductId).ThenBy(k => k.LotId ?? 0).ToList();
    }

    /// <summary>Posiciones distintas de las claves movidas, en orden.</summary>
    public static IReadOnlyList<int> Positions(IEnumerable<MovedKey> keys)
        => keys.Select(k => k.BinId).Distinct().OrderBy(b => b).ToList();

    /// <summary>
    /// Líneas de UNA posición: todo saldo en mano &gt; 0 y, con includeEmpty, las claves movidas en la ventana que hoy están en 0
    /// (SystemQty 0). Una clave movida sin saldo registrado no se agrega (el ledger siempre deja la fila). Sin repetir, en el
    /// orden del alta normal (posición, SKU, lote).
    /// </summary>
    public static IReadOnlyList<CountCandidate> SelectLines(IEnumerable<CountCandidate> balancesInBin, IEnumerable<MovedKey> movedKeys, bool includeEmpty)
    {
        var moved = includeEmpty ? movedKeys.ToHashSet() : new HashSet<MovedKey>();
        return balancesInBin
            .GroupBy(c => (c.BinId, c.ProductId, c.LotId))
            .Select(g => new CountCandidate(g.Key.BinId, g.First().BinCode, g.Key.ProductId, g.First().Sku, g.Key.LotId, g.First().LotNumber, g.Sum(x => x.QtyOnHand)))
            .Where(c => c.QtyOnHand > 0m || (c.QtyOnHand == 0m && moved.Contains(new MovedKey(c.BinId, c.ProductId, c.LotId))))
            .OrderBy(c => c.BinCode, StringComparer.Ordinal)
            .ThenBy(c => c.BinId)
            .ThenBy(c => c.Sku, StringComparer.Ordinal)
            .ThenBy(c => c.ProductId)
            .ThenBy(c => c.LotNumber ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(c => c.LotId ?? 0)
            .ToList();
    }
}
