using Teikem.Domain.Constants;
using Xunit;
using MonitorRules = Teikem.Domain.Trips.MonitorRules;
using MonitorStopInput = Teikem.Domain.Trips.MonitorStopInput;
using MonitorTripFacts = Teikem.Domain.Trips.MonitorTripFacts;

namespace Teikem.Tests;

/// <summary>
/// Lote 5 / P7: reglas puras del monitor de rutas. Progreso por estatus de parada, próxima ETA entre las paradas no
/// terminales, pines aproximados, totales calculados ANTES de la búsqueda (buscar nunca cambia los contadores, L650) y
/// búsqueda sobre la lista ya filtrada, sin distinguir mayúsculas ni acentos.
/// (Se importan los tipos con alias, sin el namespace completo de Trips: convención del lote por el choque de Route.)
/// </summary>
public class MonitorRulesTests
{
    private static readonly DateTime T0 = new(2026, 9, 26, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Progress_counts_completed_failed_and_pending()
    {
        var p = MonitorRules.Progress(new[]
        {
            RouteStopStatuses.Completed, RouteStopStatuses.Completed, RouteStopStatuses.Failed,
            RouteStopStatuses.Pending, RouteStopStatuses.OnTheWay, RouteStopStatuses.Arrived, null,
        });
        Assert.Equal(7, p.Total);
        Assert.Equal(2, p.Completed);
        Assert.Equal(1, p.Failed);
        Assert.Equal(4, p.Pending); // PENDING, ON_THE_WAY, ARRIVED y un código desconocido siguen pendientes

        var empty = MonitorRules.Progress(Array.Empty<string?>());
        Assert.Equal(0, empty.Total);
        Assert.Equal(0, empty.Pending);

        // sin distinguir mayúsculas
        Assert.Equal(1, MonitorRules.Progress(new string?[] { "completed" }).Completed);
    }

    [Fact]
    public void Next_eta_is_the_earliest_planned_arrival_among_non_terminal_stops()
    {
        var stops = new[]
        {
            new MonitorStopInput(RouteStopStatuses.Completed, T0, true, GeocodeAccuracies.Exact),              // terminal: no cuenta
            new MonitorStopInput(RouteStopStatuses.Failed, T0.AddMinutes(5), true, GeocodeAccuracies.Exact),   // terminal: no cuenta
            new MonitorStopInput(RouteStopStatuses.Pending, T0.AddMinutes(40), true, GeocodeAccuracies.Exact),
            new MonitorStopInput(RouteStopStatuses.OnTheWay, T0.AddMinutes(20), true, GeocodeAccuracies.Exact),
            new MonitorStopInput(RouteStopStatuses.Pending, null, true, GeocodeAccuracies.Exact),              // sin ETA: se ignora
        };
        Assert.Equal(T0.AddMinutes(20), MonitorRules.NextEta(stops));

        Assert.Null(MonitorRules.NextEta(Array.Empty<MonitorStopInput>()));
        Assert.Null(MonitorRules.NextEta(new[] { new MonitorStopInput(RouteStopStatuses.Completed, T0, true, null) }));
        Assert.Null(MonitorRules.NextEta(new[] { new MonitorStopInput(RouteStopStatuses.Pending, null, true, null) }));
    }

    [Fact]
    public void Approximate_count_uses_point_and_accuracy()
    {
        var stops = new[]
        {
            new MonitorStopInput(RouteStopStatuses.Pending, null, true, GeocodeAccuracies.Exact),         // exacta
            new MonitorStopInput(RouteStopStatuses.Pending, null, true, GeocodeAccuracies.Manual),        // pin manual
            new MonitorStopInput(RouteStopStatuses.Pending, null, true, GeocodeAccuracies.ZipCentroid),   // aproximada
            new MonitorStopInput(RouteStopStatuses.Pending, null, true, GeocodeAccuracies.CityCentroid),  // aproximada
            new MonitorStopInput(RouteStopStatuses.Pending, null, true, null),                            // sin precisión
            new MonitorStopInput(RouteStopStatuses.Pending, null, false, GeocodeAccuracies.Exact),        // sin coordenada
        };
        Assert.Equal(4, MonitorRules.ApproximateCount(stops));
        Assert.Equal(0, MonitorRules.ApproximateCount(Array.Empty<MonitorStopInput>()));
        Assert.False(MonitorRules.IsApproximate(true, "manual"));
    }

    private sealed record Item(string Code, string? Zone, string? Driver, string? Vehicle, string Status, MonitorTripFacts Facts);

    private static IEnumerable<string?> Texts(Item i) => new[] { i.Code, i.Zone, i.Driver, i.Vehicle, i.Status };

    private static readonly Item[] Day =
    {
        new("2026-0001", "Z1", "José Pérez", "V1", "En curso", new MonitorTripFacts(3, 1, 0, 2, false, true)),
        new("2026-0002", "Z2", "Ana Díaz", "V2", "Despachada", new MonitorTripFacts(5, 0, 1, 4, true, false)),
        new("2026-0003", "Z1", null, null, "Despachada", new MonitorTripFacts(0, 0, 0, 0, false, false)),
    };

    [Fact]
    public void Totals_are_computed_over_the_date_and_do_not_change_with_search()
    {
        var totals = MonitorRules.Totals(Day.Select(i => i.Facts));
        Assert.Equal(3, totals.Trips);
        Assert.Equal(8, totals.TotalStops);
        Assert.Equal(1, totals.CompletedStops);
        Assert.Equal(1, totals.FailedStops);
        Assert.Equal(6, totals.PendingStops);
        Assert.Equal(1, totals.OverStopLimitTrips);
        Assert.Equal(2, totals.TripsWithoutPing);

        // La búsqueda reduce la lista, pero los totales se calculan antes y no cambian.
        // Compose es lo que usa TripMonitorService: si los totales se calcularan sobre la lista filtrada, esto fallaría.
        var (t2, visible) = MonitorRules.Compose(Day, i => i.Facts, "2026-0001", Texts);
        Assert.Single(visible);
        Assert.Equal(totals, t2);
        Assert.NotEqual(t2, MonitorRules.Totals(visible.Select(i => i.Facts)));

        var empty = MonitorRules.Totals(Array.Empty<MonitorTripFacts>());
        Assert.Equal(0, empty.Trips);
        Assert.Equal(0, empty.TripsWithoutPing);
    }

    [Fact]
    public void Search_applies_over_the_filtered_list_case_and_accent_insensitive()
    {
        // Sobre la lista ya filtrada por zona Z1: la búsqueda no puede traer rutas de otra zona.
        var zoneOne = Day.Where(i => i.Zone == "Z1").ToList();
        Assert.Empty(MonitorRules.ApplySearch(zoneOne, "ana", Texts));

        Assert.Equal(new[] { "2026-0002" }, MonitorRules.ApplySearch(Day, "ANA DIAZ", Texts).Select(i => i.Code));
        Assert.Equal(new[] { "2026-0001" }, MonitorRules.ApplySearch(Day, "jose", Texts).Select(i => i.Code));
        Assert.Equal(new[] { "2026-0002", "2026-0003" }, MonitorRules.ApplySearch(Day, "despachada", Texts).Select(i => i.Code));
        Assert.Equal(new[] { "2026-0001", "2026-0003" }, MonitorRules.ApplySearch(Day, "z1", Texts).Select(i => i.Code));

        // Sin consulta: la lista tal cual y en el mismo orden.
        Assert.Equal(Day.Select(i => i.Code), MonitorRules.ApplySearch(Day, null, Texts).Select(i => i.Code));
        Assert.Equal(Day.Select(i => i.Code), MonitorRules.ApplySearch(Day, "   ", Texts).Select(i => i.Code));
        Assert.Empty(MonitorRules.ApplySearch(Day, "NOEXISTE", Texts));
    }
}
