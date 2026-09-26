using Teikem.Domain.Constants;
using Teikem.Domain.Fleet;

namespace Teikem.Domain.Trips;

/// <summary>Progreso de una ruta sobre las paradas de su versión vigente.</summary>
public sealed record StopProgress(int Total, int Completed, int Failed, int Pending);

/// <summary>Parada de la versión vigente vista por el monitor: estatus, llegada planificada y calidad del pin.</summary>
public sealed record MonitorStopInput(string? StatusCode, DateTime? PlannedArrivalUtc, bool HasPoint, string? GeocodeAccuracyCode);

/// <summary>Hechos de una ruta que alimentan los totales del monitor.</summary>
public sealed record MonitorTripFacts(int TotalStops, int CompletedStops, int FailedStops, int PendingStops, bool OverStopLimit, bool HasPing);

/// <summary>Totales del monitor (sobre la fecha y la zona; la búsqueda libre nunca los cambia).</summary>
public sealed record MonitorTotals(int Trips, int TotalStops, int CompletedStops, int FailedStops, int PendingStops, int OverStopLimitTrips, int TripsWithoutPing);

/// <summary>
/// Lote 5 (P7) — reglas puras del monitoreo de rutas despachadas o en curso.
/// - Progreso: COMPLETED cuenta como completada, FAILED como fallida; el resto (PENDING, ON_THE_WAY, ARRIVED o un código
///   desconocido) sigue pendiente.
/// - Próxima ETA: la menor llegada planificada entre las paradas NO terminales (ni COMPLETED ni FAILED).
/// - Pines aproximados: sin coordenada, o con precisión distinta de EXACT y MANUAL (misma regla que la ficha de la ruta).
/// - Totales: se calculan SOBRE LA FECHA Y LA ZONA, antes de la búsqueda (buscar nunca cambia los contadores, L650).
/// - Búsqueda: se aplica DESPUÉS de los filtros, sobre los textos visibles (código, zona, chofer, vehículo, estatus), sin
///   distinguir mayúsculas ni acentos (FleetRules.MatchesSearch).
/// </summary>
public static class MonitorRules
{
    /// <summary>¿La parada ya terminó (completada o fallida)?</summary>
    public static bool IsTerminalStop(string? statusCode)
        => string.Equals(statusCode, RouteStopStatuses.Completed, StringComparison.OrdinalIgnoreCase)
           || string.Equals(statusCode, RouteStopStatuses.Failed, StringComparison.OrdinalIgnoreCase);

    /// <summary>Total, completadas, fallidas y pendientes a partir de los códigos de estatus de las paradas.</summary>
    public static StopProgress Progress(IEnumerable<string?> stopStatusCodes)
    {
        int total = 0, completed = 0, failed = 0;
        foreach (var code in stopStatusCodes ?? Array.Empty<string?>())
        {
            total++;
            if (string.Equals(code, RouteStopStatuses.Completed, StringComparison.OrdinalIgnoreCase)) completed++;
            else if (string.Equals(code, RouteStopStatuses.Failed, StringComparison.OrdinalIgnoreCase)) failed++;
        }
        return new StopProgress(total, completed, failed, total - completed - failed);
    }

    /// <summary>Menor llegada planificada entre las paradas no terminales; null si no hay ninguna con ETA.</summary>
    public static DateTime? NextEta(IEnumerable<MonitorStopInput> stops)
    {
        DateTime? next = null;
        foreach (var s in stops ?? Array.Empty<MonitorStopInput>())
        {
            if (IsTerminalStop(s.StatusCode) || s.PlannedArrivalUtc is not DateTime eta) continue;
            if (next is null || eta < next.Value) next = eta;
        }
        return next;
    }

    /// <summary>¿El pin es aproximado? Sin coordenada, o con precisión distinta de EXACT y MANUAL.</summary>
    public static bool IsApproximate(bool hasPoint, string? accuracyCode)
        => !hasPoint
           || !(string.Equals(accuracyCode, GeocodeAccuracies.Exact, StringComparison.OrdinalIgnoreCase)
                || string.Equals(accuracyCode, GeocodeAccuracies.Manual, StringComparison.OrdinalIgnoreCase));

    /// <summary>Cantidad de paradas con pin aproximado.</summary>
    public static int ApproximateCount(IEnumerable<MonitorStopInput> stops)
        => (stops ?? Array.Empty<MonitorStopInput>()).Count(s => IsApproximate(s.HasPoint, s.GeocodeAccuracyCode));

    /// <summary>Totales sobre TODAS las rutas de la fecha y la zona (llamar antes de ApplySearch).</summary>
    public static MonitorTotals Totals(IEnumerable<MonitorTripFacts> trips)
    {
        int count = 0, total = 0, completed = 0, failed = 0, pending = 0, over = 0, withoutPing = 0;
        foreach (var t in trips ?? Array.Empty<MonitorTripFacts>())
        {
            count++;
            total += t.TotalStops;
            completed += t.CompletedStops;
            failed += t.FailedStops;
            pending += t.PendingStops;
            if (t.OverStopLimit) over++;
            if (!t.HasPing) withoutPing++;
        }
        return new MonitorTotals(count, total, completed, failed, pending, over, withoutPing);
    }

    /// <summary>
    /// Composición del monitor: totales sobre TODAS las rutas de la fecha y la zona, y la lista visible después de la
    /// búsqueda. Es la única forma en que TripMonitorService arma la respuesta, así el orden (totales antes de buscar) queda
    /// fijado por una regla pura y probada.
    /// </summary>
    public static (MonitorTotals Totals, IReadOnlyList<T> Visible) Compose<T>(
        IReadOnlyList<T> all, Func<T, MonitorTripFacts> facts, string? query, Func<T, IEnumerable<string?>> visibleTexts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        var list = all ?? Array.Empty<T>();
        return (Totals(list.Select(facts)), ApplySearch(list, query, visibleTexts));
    }

    /// <summary>
    /// Búsqueda libre sobre la lista YA filtrada por fecha y zona: cada palabra debe aparecer en alguno de los textos
    /// visibles de la ruta (sin distinguir mayúsculas ni acentos). Consulta vacía = la lista tal cual, en el mismo orden.
    /// </summary>
    public static IReadOnlyList<T> ApplySearch<T>(IEnumerable<T> trips, string? query, Func<T, IEnumerable<string?>> visibleTexts)
    {
        var list = (trips ?? Array.Empty<T>()).ToList();
        if (string.IsNullOrWhiteSpace(query)) return list;
        return list.Where(t => FleetRules.MatchesSearch(query, visibleTexts(t).ToArray())).ToList();
    }
}
