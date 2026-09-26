using System.Globalization;
using Teikem.Domain.Constants;
using Teikem.Domain.Fleet;
using Xunit;
using OrderEligibilityInput = Teikem.Domain.Trips.OrderEligibilityInput;
using OrderIneligibility = Teikem.Domain.Trips.OrderIneligibility;
using TripIssue = Teikem.Domain.Trips.TripIssue;
using TripIssueInput = Teikem.Domain.Trips.TripIssueInput;
using TripRules = Teikem.Domain.Trips.TripRules;

namespace Teikem.Tests;

/// <summary>Lote 5 / P0: reglas puras de la ruta (editabilidad, elegibilidad, secuencia, máximo de paradas, ruta abierta y avisos).</summary>
public class TripRulesTests
{
    // ---------------------------------------------------------------- editabilidad

    [Theory]
    [InlineData("DRAFT", true)]
    [InlineData("PLANNED", true)]
    [InlineData("planned", true)]
    [InlineData("DISPATCHED", false)]
    [InlineData("IN_PROGRESS", false)]
    [InlineData("COMPLETED", false)]
    [InlineData("CANCELLED", false)]
    [InlineData(null, false)]
    public void IsEditable_only_in_draft_and_planned(string? code, bool expected) => Assert.Equal(expected, TripRules.IsEditable(code));

    [Theory]
    [InlineData("DISPATCHED", "La ruta 2026-0001 ya fue despachada; no se puede editar ni eliminar.")]
    [InlineData("IN_PROGRESS", "La ruta 2026-0001 ya fue despachada; no se puede editar ni eliminar.")]
    [InlineData("COMPLETED", "La ruta 2026-0001 está cerrada; solo se consulta.")]
    [InlineData("CANCELLED", "La ruta 2026-0001 está cerrada; solo se consulta.")]
    [InlineData("DRAFT", "El estatus actual de la ruta 2026-0001 no permite editarla.")]
    public void NotEditableMessage_is_exact(string status, string expected)
        => Assert.Equal(expected, TripRules.NotEditableMessage("2026-0001", status));

    [Fact]
    public void Order_messages_are_exact()
    {
        Assert.Equal("La orden no está en esta ruta.", TripRules.NotInTripMessage);
        Assert.Equal("La orden ya está en esta ruta.", TripRules.AlreadyInTripMessage);
        Assert.Equal("La orden ya está asignada a la ruta 2026-0007.", TripRules.OrderInOtherTripMessage("2026-0007"));
        Assert.Equal("La orden ya está asignada a otra ruta.", TripRules.OrderTakenMessage);
        Assert.Equal("Hay órdenes que no se pueden asignar a la ruta.", TripRules.EligibilityHeader);
        Assert.Equal(300, TripRules.MaxStopsHardCap);
        Assert.Equal("Una ruta admite como máximo 300 paradas.", TripRules.MaxStopsHardCapMessage);
        Assert.Equal("La secuencia debe incluir exactamente las paradas de la ruta vigente, sin repetir.", TripRules.SequenceMessage);
        Assert.Equal("Parada no encontrada en esta ruta.", TripRules.StopNotInTripMessage);
    }

    // ---------------------------------------------------------------- elegibilidad

    private static OrderEligibilityInput Order(string status = "CONFIRMED", string label = "Confirmada", string kind = StageKinds.Pipeline,
        bool initial = false, int sort = 2, bool active = true, bool special = false, bool allowed = true, bool pending = true)
        => new("2026-000123", active, special, status, label, kind, initial, sort, 6, allowed, pending);

    [Fact]
    public void Confirmed_order_with_pending_delivery_is_eligible() => Assert.Null(TripRules.CheckEligibility(Order()));

    [Fact]
    public void Eligibility_messages_are_exact()
    {
        Assert.Equal("La orden está en Entrada; confírmela antes de asignarla a una ruta.",
            TripRules.CheckEligibility(Order("DRAFT", "Entrada", initial: true, sort: 1, allowed: false)));
        Assert.Equal("Las entregas especiales se asignan al chofer desde la orden; no pasan por Sala de despacho.",
            TripRules.CheckEligibility(Order(special: true)));
        Assert.Equal("La orden está en 'En espera'; regrésela al pipeline antes de asignarla a una ruta.",
            TripRules.CheckEligibility(Order("ON_HOLD", "En espera", StageKinds.Lateral, sort: 20)));
        Assert.Equal("La orden ya salió a ruta o terminó; no se puede asignar a otra ruta.",
            TripRules.CheckEligibility(Order("IN_TRANSIT", "En tránsito", sort: 6)));
        Assert.Equal("La orden ya salió a ruta o terminó; no se puede asignar a otra ruta.",
            TripRules.CheckEligibility(Order("DELIVERED", "Entregada", StageKinds.Terminal, sort: 8)));
        Assert.Equal("La orden ya salió a ruta o terminó; no se puede asignar a otra ruta.",
            TripRules.CheckEligibility(Order(active: false)));
        Assert.Equal("El estatus actual no permite la acción 'ASSIGN_TRIP'.", TripRules.CheckEligibility(Order(allowed: false)));
        Assert.Equal("La orden no tiene una parada de entrega pendiente.", TripRules.CheckEligibility(Order(pending: false)));
    }

    [Fact]
    public void Planned_orders_before_in_transit_are_still_eligible()
        => Assert.Null(TripRules.CheckEligibility(Order("PLANNED", "Planificada", sort: 5)));

    // ---------------------------------------------------------------- secuencia, máximo, ruta abierta

    [Fact]
    public void ValidateSequence_requires_an_exact_permutation()
    {
        var current = new[] { 10, 11, 12 };
        Assert.Null(TripRules.ValidateSequence(current, new[] { 12, 10, 11 }));
        Assert.Equal(TripRules.SequenceMessage, TripRules.ValidateSequence(current, new[] { 12, 10 }));          // falta
        Assert.Equal(TripRules.SequenceMessage, TripRules.ValidateSequence(current, new[] { 12, 10, 10 }));      // repetido
        Assert.Equal(TripRules.SequenceMessage, TripRules.ValidateSequence(current, new[] { 12, 10, 99 }));      // ajeno
        Assert.Equal(TripRules.SequenceMessage, TripRules.ValidateSequence(current, new[] { 12, 10, 11, 99 }));  // sobra
        Assert.Equal(TripRules.SequenceMessage, TripRules.ValidateSequence(current, null));
    }

    [Theory]
    [InlineData(2, 1, true)]
    [InlineData(1, 1, false)]
    [InlineData(0, 1, false)]
    [InlineData(40, null, false)]
    public void OverStopLimit_is_strictly_greater(int stops, int? max, bool expected) => Assert.Equal(expected, TripRules.OverStopLimit(stops, max));

    [Fact]
    public void PickOpenTrip_returns_the_lowest_id()
    {
        Assert.Equal(3, TripRules.PickOpenTrip(new[] { 7, 3, 12 }));
        Assert.Null(TripRules.PickOpenTrip(Array.Empty<int>()));
        Assert.Null(TripRules.PickOpenTrip(null));
    }

    // ---------------------------------------------------------------- avisos y bloqueantes

    private static TripIssueInput Input(
        bool driver = true, bool vehicle = true, int stops = 2,
        AvailabilityResult? driverAvailability = null, AvailabilityResult? vehicleAvailability = null,
        int? maxStops = 25, int? vehicleMaxStops = null, decimal? maxWeight = null, decimal? maxVolume = null,
        decimal weight = 0m, decimal volume = 0m, int late = 0, bool start = true, int approximate = 0,
        string[]? driverOther = null, string[]? vehicleOther = null, OrderIneligibility[]? ineligible = null)
        => new("2026-0001", driver, vehicle, stops,
            driver ? driverAvailability ?? Ok : null, vehicle ? vehicleAvailability ?? Ok : null,
            maxStops, vehicleMaxStops, maxWeight, maxVolume, weight, volume, late, start, approximate,
            driverOther ?? Array.Empty<string>(), vehicleOther ?? Array.Empty<string>(), ineligible ?? Array.Empty<OrderIneligibility>());

    private static readonly AvailabilityResult Ok = new(true, Array.Empty<AvailabilityIssue>());

    [Fact]
    public void A_complete_route_has_no_issues() => Assert.Empty(TripRules.BuildIssues(Input()));

    [Fact]
    public void Blocking_issues_are_exact_and_first()
    {
        var unavailable = new AvailabilityResult(false, new[]
        {
            new AvailabilityIssue("DRIVER_STATUS", "Chofer en estatus 'No disponible'.", true),
            new AvailabilityIssue("DOC_EXPIRING", "Licencia B vence el 2026-10-01.", false),
        });
        var issues = TripRules.BuildIssues(Input(driverAvailability: unavailable, vehicle: false, stops: 0,
            ineligible: new[] { new OrderIneligibility("2026-000123", "El estatus actual no permite la acción 'ASSIGN_TRIP'.") }));

        Assert.Equal(new[] { "DRIVER_UNAVAILABLE", "NO_VEHICLE", "NO_STOPS", "ORDER_NOT_ELIGIBLE", "DOC_EXPIRING" }, issues.Select(i => i.Code));
        Assert.Equal(new[] { true, true, true, true, false }, issues.Select(i => i.Blocking));
        Assert.Equal("El chofer no está disponible para despacho: Chofer en estatus 'No disponible'.", issues[0].Message);
        Assert.Equal("La ruta no tiene vehículo asignado.", issues[1].Message);
        Assert.Equal("La ruta no tiene paradas.", issues[2].Message);
        Assert.Equal("Orden 2026-000123: El estatus actual no permite la acción 'ASSIGN_TRIP'.", issues[3].Message);
        Assert.Equal("Licencia B vence el 2026-10-01.", issues[4].Message);
    }

    [Fact]
    public void No_driver_and_vehicle_unavailable()
    {
        var workOrder = new AvailabilityResult(false, new[] { new AvailabilityIssue("WORK_ORDER_IN_PROGRESS", "Orden de trabajo OT-00001 en proceso.", true) });
        var issues = TripRules.BuildIssues(Input(driver: false, vehicleAvailability: workOrder));
        Assert.Equal(new[] { "NO_DRIVER", "VEHICLE_UNAVAILABLE" }, issues.Select(i => i.Code));
        Assert.Equal("La ruta no tiene chofer asignado.", issues[0].Message);
        Assert.Equal("El vehículo no está disponible para despacho: Orden de trabajo OT-00001 en proceso.", issues[1].Message);
    }

    [Fact]
    public void Warnings_are_exact_and_never_block()
    {
        var issues = TripRules.BuildIssues(Input(stops: 2, maxStops: 1, vehicleMaxStops: 1, maxWeight: 100m, weight: 150.5m,
            maxVolume: 2m, volume: 2.25m, late: 1, start: false, approximate: 2, driverOther: new[] { "2026-0009", "2026-0003" },
            vehicleOther: new[] { "2026-0004" }));
        Assert.All(issues, i => Assert.False(i.Blocking));
        var byCode = issues.ToDictionary(i => i.Code, i => i.Message);
        Assert.Equal("La ruta tiene 2 paradas y el máximo del chofer es 1.", byCode["OVER_STOP_LIMIT"]);
        Assert.Equal("La ruta tiene 2 paradas y el vehículo admite 1.", byCode["OVER_VEHICLE_STOPS"]);
        Assert.Equal("La carga (150.5 kg) excede la capacidad de peso del vehículo (100 kg).", byCode["OVER_WEIGHT"]);
        Assert.Equal("El volumen (2.25 m³) excede la capacidad de volumen del vehículo (2 m³).", byCode["OVER_VOLUME"]);
        Assert.Equal("1 parada llega después del fin de su ventana.", byCode["LATE_WINDOWS"]);
        Assert.Equal("La ruta no tiene hora de salida; no se calculan las ETAs.", byCode["NO_PLANNED_START"]);
        Assert.Equal("2 paradas tienen ubicación aproximada o sin coordenadas.", byCode["APPROXIMATE_PINS"]);
        Assert.Equal("El chofer también está en la ruta 2026-0003, 2026-0009 ese mismo día.", byCode["DRIVER_DOUBLE_BOOKED"]);
        Assert.Equal("El vehículo también está en la ruta 2026-0004 ese mismo día.", byCode["VEHICLE_DOUBLE_BOOKED"]);
        Assert.Equal(9, issues.Count);
    }

    [Fact]
    public void Numbers_use_the_invariant_culture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("es-PR");
            var issue = TripRules.BuildIssues(Input(maxWeight: 1000.5m, weight: 1200.75m)).Single();
            Assert.Equal("La carga (1200.75 kg) excede la capacidad de peso del vehículo (1000.5 kg).", issue.Message);
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Fact]
    public void DispatchBlockingMessage_joins_the_blocking_messages()
    {
        var issues = new[]
        {
            new TripIssue("NO_VEHICLE", "La ruta no tiene vehículo asignado.", true),
            new TripIssue("OVER_STOP_LIMIT", "La ruta tiene 2 paradas y el máximo del chofer es 1.", false),
            new TripIssue("DRIVER_UNAVAILABLE", "El chofer no está disponible para despacho: Chofer en estatus 'No disponible'.", true),
        };
        Assert.Equal("La ruta 2026-0001 no se puede despachar: La ruta no tiene vehículo asignado; El chofer no está disponible para despacho: Chofer en estatus 'No disponible'.",
            TripRules.DispatchBlockingMessage("2026-0001", issues));
    }
}
