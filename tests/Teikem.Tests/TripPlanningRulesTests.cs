using Xunit;
using DefaultDriverCandidate = Teikem.Domain.Trips.DefaultDriverCandidate;
using TripHeaderChange = Teikem.Domain.Trips.TripHeaderChange;
using TripPatchFlags = Teikem.Domain.Trips.TripPatchFlags;
using TripPlanningRules = Teikem.Domain.Trips.TripPlanningRules;

namespace Teikem.Tests;

/// <summary>
/// Lote 5 (P1): reglas puras de la cabecera de la ruta: número AAAA-####, fecha del plan, hora de salida (y su valor por
/// defecto), chofer por defecto, llaves prohibidas y flags en conflicto del PATCH y edición de una ruta despachada.
/// (Convención del lote: las pruebas no importan Teikem.Domain.Trips; se usan alias.)
/// </summary>
public class TripPlanningRulesTests
{
    private static readonly DateOnly Today = new(2026, 9, 26);

    // ---------------- Número ----------------

    [Theory]
    [InlineData(2026, 1, "2026-0001")]
    [InlineData(2026, 42, "2026-0042")]
    [InlineData(2027, 9999, "2027-9999")]
    [InlineData(2026, 12345, "2026-12345")]
    public void FormatCode_uses_plan_year_and_four_digit_sequence(int year, long seq, string expected)
        => Assert.Equal(expected, TripPlanningRules.FormatCode(new DateOnly(year, 3, 15), seq));

    [Fact]
    public void FormatCode_is_culture_invariant()
    {
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("th-TH"); // calendario budista
            Assert.Equal("2026-0007", TripPlanningRules.FormatCode(Today, 7));
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = previous; }
    }

    // ---------------- Fecha del plan ----------------

    [Fact]
    public void PlanDate_is_required()
        => Assert.Equal("Indique la fecha de la ruta.", TripPlanningRules.ValidatePlanDate(null, Today));

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(60)]
    public void PlanDate_from_yesterday_to_sixty_days_is_valid(int offset)
        => Assert.Null(TripPlanningRules.ValidatePlanDate(Today.AddDays(offset), Today));

    [Theory]
    [InlineData(-2)]
    [InlineData(-3)]
    [InlineData(61)]
    public void PlanDate_outside_range_has_exact_message(int offset)
        => Assert.Equal("La fecha de la ruta no puede ser anterior a ayer ni posterior a 60 días.",
            TripPlanningRules.ValidatePlanDate(Today.AddDays(offset), Today));

    // ---------------- Hora de salida ----------------

    [Fact]
    public void DefaultPlannedStart_is_noon_utc_of_plan_date()
    {
        var start = TripPlanningRules.DefaultPlannedStart(new DateOnly(2026, 9, 26));
        Assert.Equal(new DateTime(2026, 9, 26, 12, 0, 0, DateTimeKind.Utc), start);
        Assert.Equal(DateTimeKind.Utc, start.Kind);
        Assert.Null(TripPlanningRules.ValidatePlannedStart(start, new DateOnly(2026, 9, 26)));
    }

    [Theory]
    [InlineData("2026-09-25T12:00:00Z")] // límite inferior incluido (fecha − 12 h)
    [InlineData("2026-09-26T00:00:00Z")]
    [InlineData("2026-09-26T23:59:59Z")]
    [InlineData("2026-09-27T11:59:59Z")] // justo antes del límite superior
    public void PlannedStart_within_plan_date_plus_minus_12h_is_valid(string iso)
        => Assert.Null(TripPlanningRules.ValidatePlannedStart(ParseUtc(iso), Today));

    [Theory]
    [InlineData("2026-09-25T11:59:59Z")]
    [InlineData("2026-09-27T12:00:00Z")] // límite superior excluido
    [InlineData("2026-10-01T08:00:00Z")]
    public void PlannedStart_outside_window_has_exact_message(string iso)
        => Assert.Equal("La hora de salida debe caer en la fecha de la ruta (±12 h por zona horaria).",
            TripPlanningRules.ValidatePlannedStart(ParseUtc(iso), Today));

    [Fact]
    public void PlannedStart_unspecified_kind_is_taken_as_utc()
    {
        var unspecified = new DateTime(2026, 9, 27, 12, 0, 0, DateTimeKind.Unspecified);
        Assert.NotNull(TripPlanningRules.ValidatePlannedStart(unspecified, Today));
        Assert.Equal(DateTimeKind.Utc, TripPlanningRules.AsUtc(unspecified).Kind);
    }

    [Fact]
    public void ShiftPlannedStart_keeps_time_of_day_and_moves_by_days()
    {
        var start = new DateTime(2026, 9, 26, 13, 30, 0, DateTimeKind.Utc);
        Assert.Equal(new DateTime(2026, 9, 28, 13, 30, 0, DateTimeKind.Utc),
            TripPlanningRules.ShiftPlannedStart(start, Today, Today.AddDays(2)));
        Assert.Null(TripPlanningRules.ShiftPlannedStart(null, Today, Today.AddDays(2)));
    }

    // ---------------- Chofer por defecto ----------------

    [Fact]
    public void PickDefaultDriver_single_available_candidate_is_chosen()
        => Assert.Equal(7, TripPlanningRules.PickDefaultDriver(new[] { new DefaultDriverCandidate(7, true) }));

    [Fact]
    public void PickDefaultDriver_single_unavailable_candidate_is_null()
        => Assert.Null(TripPlanningRules.PickDefaultDriver(new[] { new DefaultDriverCandidate(7, false) }));

    [Fact]
    public void PickDefaultDriver_none_or_several_candidates_is_null()
    {
        Assert.Null(TripPlanningRules.PickDefaultDriver(Array.Empty<DefaultDriverCandidate>()));
        Assert.Null(TripPlanningRules.PickDefaultDriver(null));
        Assert.Null(TripPlanningRules.PickDefaultDriver(new[] { new DefaultDriverCandidate(7, true), new DefaultDriverCandidate(8, true) }));
    }

    [Fact]
    public void PickDefaultDriver_duplicated_candidate_counts_once()
        => Assert.Equal(7, TripPlanningRules.PickDefaultDriver(new[] { new DefaultDriverCandidate(7, true), new DefaultDriverCandidate(7, true) }));

    // ---------------- PATCH: llaves prohibidas ----------------

    [Theory]
    [InlineData("code", "El número de la ruta se fija al crearlo; no se puede cambiar.")]
    [InlineData("tripCode", "El número de la ruta se fija al crearlo; no se puede cambiar.")]
    [InlineData("CODE", "El número de la ruta se fija al crearlo; no se puede cambiar.")]
    [InlineData("status", "El estatus de la ruta cambia con sus acciones (optimizar, despachar, eliminar).")]
    [InlineData("statusCode", "El estatus de la ruta cambia con sus acciones (optimizar, despachar, eliminar).")]
    [InlineData("toCode", "El estatus de la ruta cambia con sus acciones (optimizar, despachar, eliminar).")]
    [InlineData("tenantId", "Ese campo no se puede modificar.")]
    [InlineData("version", "Ese campo no se puede modificar.")]
    [InlineData("routeVersion", "Ese campo no se puede modificar.")]
    [InlineData("originWarehouseId", "Ese campo no se puede modificar.")]
    public void Forbidden_patch_keys_have_exact_messages(string key, string message)
    {
        var hit = TripPlanningRules.CheckForbiddenKeys(new[] { "notes", key });
        Assert.NotNull(hit);
        Assert.Equal(key, hit!.Value.Field);
        Assert.Equal(message, hit.Value.Message);
    }

    [Fact]
    public void Unknown_but_allowed_keys_are_not_forbidden()
    {
        Assert.Null(TripPlanningRules.CheckForbiddenKeys(new[] { "notes", "foo" }));
        Assert.Null(TripPlanningRules.CheckForbiddenKeys(null));
    }

    // ---------------- PATCH: flags en conflicto ----------------

    [Fact]
    public void Conflicting_flags_have_exact_messages_per_field()
    {
        var errors = TripPlanningRules.ValidatePatchFlags(new TripPatchFlags(true, true, true, true, true, true, true, true));
        Assert.Equal("Indique el chofer o quítelo, no ambos.", errors["driverPublicId"]);
        Assert.Equal("Indique el vehículo o quítelo, no ambos.", errors["vehiclePublicId"]);
        Assert.Equal("Indique la zona o quítela, no ambas.", errors["dispatchZoneId"]);
        Assert.Equal("Indique la hora de salida o quítela, no ambas.", errors["plannedStartUtc"]);
    }

    [Fact]
    public void Value_or_clear_alone_is_not_a_conflict()
    {
        Assert.Empty(TripPlanningRules.ValidatePatchFlags(new TripPatchFlags(true, false, false, true, true, false, false, true)));
        Assert.Empty(TripPlanningRules.ValidatePatchFlags(new TripPatchFlags(false, false, false, false, false, false, false, false)));
    }

    // ---------------- Ruta despachada (EDIT_TRIP) ----------------

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void Dispatched_patch_rejects_plan_date_or_zone(bool planDate, bool zone)
        => Assert.Equal("La fecha y la zona de una ruta despachada no se cambian.",
            TripPlanningRules.ValidateDispatchedPatch(new TripHeaderChange(planDate, zone, true, true, true)));

    [Fact]
    public void Dispatched_patch_accepts_driver_vehicle_and_planned_start()
    {
        Assert.Null(TripPlanningRules.ValidateDispatchedPatch(new TripHeaderChange(false, false, true, false, false)));
        Assert.Null(TripPlanningRules.ValidateDispatchedPatch(new TripHeaderChange(false, false, false, true, false)));
        Assert.Null(TripPlanningRules.ValidateDispatchedPatch(new TripHeaderChange(false, false, false, false, true)));
        Assert.Null(TripPlanningRules.ValidateDispatchedPatch(new TripHeaderChange(false, false, true, true, true)));
    }

    private static readonly DateTime Start = new(2026, 9, 26, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Dispatched_values_accept_changing_driver_vehicle_and_start()
    {
        Assert.Null(TripPlanningRules.ValidateDispatchedValues(1, 2, Start, Start));
        Assert.Null(TripPlanningRules.ValidateDispatchedValues(3, 4, Start.AddHours(1), Start));
        // Sin hora de salida previa, seguir sin ella no es quitarla.
        Assert.Null(TripPlanningRules.ValidateDispatchedValues(1, 2, null, null));
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, true, true)]
    public void Dispatched_values_reject_clearing_driver_vehicle_or_start(bool clearDriver, bool clearVehicle, bool clearStart)
        => Assert.Equal("Una ruta despachada debe conservar chofer, vehículo y hora de salida; cámbielos en lugar de quitarlos.",
            TripPlanningRules.ValidateDispatchedValues(clearDriver ? null : 1, clearVehicle ? null : 2, clearStart ? null : Start, Start));

    [Theory]
    [InlineData("DISPATCHED", true)]
    [InlineData("IN_PROGRESS", true)]
    [InlineData("in_progress", true)]
    [InlineData("DRAFT", false)]
    [InlineData("PLANNED", false)]
    [InlineData("COMPLETED", false)]
    [InlineData(null, false)]
    public void IsDispatchedStatus_only_dispatched_and_in_progress(string? code, bool expected)
        => Assert.Equal(expected, TripPlanningRules.IsDispatchedStatus(code));

    // ---------------- Disponibilidad, estatus y pines ----------------

    [Fact]
    public void Not_available_messages_join_reasons_without_double_periods()
    {
        Assert.Equal("El chofer no está disponible para despacho: La licencia venció; El chofer está inactivo.",
            TripPlanningRules.DriverNotAvailableMessage(new[] { "La licencia venció.", "El chofer está inactivo." }));
        Assert.Equal("El vehículo no está disponible para despacho: El marbete venció.",
            TripPlanningRules.VehicleNotAvailableMessage(new[] { "El marbete venció." }));
    }

    [Fact]
    public void Unknown_status_message_is_exact()
        => Assert.Equal("Estatus de ruta desconocido: 'FOO'.", TripPlanningRules.UnknownStatusMessage("FOO"));

    [Fact]
    public void Reassign_messages_are_exact()
    {
        Assert.Equal("Indique al menos una zona.", TripPlanningRules.ZonesRequiredMessage);
        Assert.Equal("Máximo 50 zonas por reasignación.", TripPlanningRules.MaxReassignZonesMessage);
        Assert.Equal("Indique el chofer.", TripPlanningRules.DriverRequiredMessage);
        Assert.Equal(50, TripPlanningRules.MaxReassignZones);
    }

    [Theory]
    [InlineData(true, "EXACT", false)]
    [InlineData(true, "MANUAL", false)]
    [InlineData(true, "ZIP_CENTROID", true)]
    [InlineData(true, "CITY_CENTROID", true)]
    [InlineData(true, null, true)]
    [InlineData(false, "EXACT", true)]
    [InlineData(false, null, true)]
    public void IsApproximatePin_only_exact_or_manual_points_are_precise(bool hasPoint, string? accuracy, bool expected)
        => Assert.Equal(expected, TripPlanningRules.IsApproximatePin(hasPoint, accuracy));

    [Fact]
    public void LateForWindow_when_arrival_after_window_end()
    {
        var end = new DateTime(2026, 9, 26, 15, 0, 0, DateTimeKind.Utc);
        Assert.True(TripPlanningRules.LateForWindow(end.AddMinutes(1), end));
        Assert.False(TripPlanningRules.LateForWindow(end, end));
        Assert.False(TripPlanningRules.LateForWindow(null, end));
        Assert.False(TripPlanningRules.LateForWindow(end.AddHours(1), null));
    }

    private static DateTime ParseUtc(string iso)
        => DateTime.Parse(iso, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal);
}
