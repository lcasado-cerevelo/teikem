using System.Globalization;
using Xunit;
using OutboundScanRules = Teikem.Domain.Trips.OutboundScanRules;
using ScanFacts = Teikem.Domain.Trips.ScanFacts;
using ScanOutcomes = Teikem.Domain.Trips.ScanOutcomes;
using ScanReasonCodes = Teikem.Domain.Trips.ScanReasonCodes;
using ScanVoices = Teikem.Domain.Trips.ScanVoices;
using ScanZone = Teikem.Domain.Trips.ScanZone;

namespace Teikem.Tests;

/// <summary>
/// Lote 5 (P6): reglas puras de la estación de escaneo Outbound: normalización del código, palabra de voz y decisión con
/// precedencia fija (mensajes exactos). (Convención del lote: las pruebas no importan Teikem.Domain.Trips; se usan alias.)
/// </summary>
public class OutboundScanRulesTests
{
    private static readonly DateOnly PlanDate = new(2026, 9, 26);
    private static readonly ScanZone Z1 = new("Z1", false, Array.Empty<string>());

    /// <summary>Hechos de una orden encontrada, elegible, con zona Z1 y ruta abierta 2026-0001 (el caso feliz).</summary>
    private static ScanFacts Ok() => new(1, "PACK_BATCH", "ORD-1", null, null, Z1, "2026-0001", PlanDate);

    // ---------------- NormalizeCode ----------------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NormalizeCode_rejects_empty(string? raw)
    {
        var (code, error) = OutboundScanRules.NormalizeCode(raw);
        Assert.Null(code);
        Assert.Equal("Escanee o escriba un código.", error);
    }

    [Fact]
    public void NormalizeCode_trims_and_caps_at_40()
    {
        Assert.Equal(("PB-0001", (string?)null), OutboundScanRules.NormalizeCode("  PB-0001 \t"));

        var forty = new string('A', 40);
        Assert.Equal((forty, (string?)null), OutboundScanRules.NormalizeCode(" " + forty + " "));

        var (code, error) = OutboundScanRules.NormalizeCode(new string('A', 41));
        Assert.Null(code);
        Assert.Equal("El código no puede exceder 40 caracteres.", error);
    }

    // ---------------- Voice ----------------

    [Theory]
    [InlineData("FOUND_ASSIGNED", "found")]
    [InlineData("FOUND_UNASSIGNED", "found")]
    [InlineData("ALREADY_ASSIGNED", "dup")]
    [InlineData("NOT_FOUND", "notfound")]
    [InlineData("NOT_ELIGIBLE", "notfound")]
    public void Voice_maps_each_outcome(string outcome, string voice)
        => Assert.Equal(voice, OutboundScanRules.Voice(outcome));

    [Fact]
    public void Outcome_and_voice_constants_match_the_contract()
    {
        Assert.Equal("FOUND_ASSIGNED", ScanOutcomes.FoundAssigned);
        Assert.Equal("FOUND_UNASSIGNED", ScanOutcomes.FoundUnassigned);
        Assert.Equal("ALREADY_ASSIGNED", ScanOutcomes.AlreadyAssigned);
        Assert.Equal("NOT_FOUND", ScanOutcomes.NotFound);
        Assert.Equal("NOT_ELIGIBLE", ScanOutcomes.NotEligible);
        Assert.Equal("found", ScanVoices.Found);
        Assert.Equal("dup", ScanVoices.Duplicate);
        Assert.Equal("notfound", ScanVoices.NotFound);
    }

    // ---------------- Decide: precedencia ----------------

    [Fact]
    public void No_match_is_not_found()
    {
        var d = OutboundScanRules.Decide(new ScanFacts(0, null, null, null, null, null, null, PlanDate));
        Assert.Equal(ScanOutcomes.NotFound, d.Outcome);
        Assert.Equal(ScanReasonCodes.NoMatch, d.ReasonCode);
        Assert.Equal("No se encontró la orden.", d.Message);
        Assert.Equal("notfound", d.Voice);
    }

    [Fact]
    public void Several_matches_is_not_found_and_asks_for_the_pack_batch()
    {
        var d = OutboundScanRules.Decide(Ok() with { MatchCount = 2, MatchedBy = "INVOICE" });
        Assert.Equal(ScanOutcomes.NotFound, d.Outcome);
        Assert.Equal(ScanReasonCodes.MultipleMatches, d.ReasonCode);
        Assert.Equal("Hay varias órdenes con ese código; escanee el empaque.", d.Message);
        Assert.Equal("notfound", d.Voice);
    }

    [Fact]
    public void Same_order_number_of_two_clients_is_not_found_even_if_one_is_already_in_a_route()
    {
        // La numeración es por cliente (consolidación multi-cliente, maestro L263): dos clientes pueden repetir el número.
        // La ambigüedad gana a todo lo demás: no se adivina la orden ni se dice 'Ya'.
        var d = OutboundScanRules.Decide(Ok() with { MatchCount = 2, MatchedBy = "ORDER_NUMBER", CurrentTripCode = "2026-0001" });
        Assert.Equal(ScanOutcomes.NotFound, d.Outcome);
        Assert.Equal(ScanReasonCodes.MultipleMatches, d.ReasonCode);
        Assert.Equal("Hay varias órdenes con ese código; escanee el empaque.", d.Message);
        Assert.Equal("notfound", d.Voice);
    }

    [Fact]
    public void Current_trip_wins_over_not_eligible()
    {
        // Una orden ya despachada (no elegible) que está en su ruta dice 'Ya'.
        var d = OutboundScanRules.Decide(Ok() with
        {
            CurrentTripCode = "2026-0007",
            IneligibleReason = "La orden ya salió a ruta o terminó; no se puede asignar a otra ruta.",
        });
        Assert.Equal(ScanOutcomes.AlreadyAssigned, d.Outcome);
        Assert.Equal(ScanReasonCodes.InTrip, d.ReasonCode);
        Assert.Equal("Ya estaba en la ruta 2026-0007.", d.Message);
        Assert.Equal("dup", d.Voice);
    }

    [Fact]
    public void Not_eligible_without_trip_uses_the_trip_rules_reason()
    {
        const string reason = "La orden está en Entrada; confírmela antes de asignarla a una ruta.";
        var d = OutboundScanRules.Decide(Ok() with { IneligibleReason = reason, Zone = null, OpenTripCode = null });
        Assert.Equal(ScanOutcomes.NotEligible, d.Outcome);
        Assert.Equal(ScanReasonCodes.NotEligible, d.ReasonCode);
        Assert.Equal(reason, d.Message);
        Assert.Equal("notfound", d.Voice);
    }

    [Fact]
    public void No_zone_stays_unassigned()
    {
        var expected = "No se pudo resolver la zona de despacho por código postal ni pueblo; queda sin asignar.";
        foreach (var zone in new ScanZone?[] { null, ScanZone.None, new(" ", false, Array.Empty<string>()) })
        {
            var d = OutboundScanRules.Decide(Ok() with { Zone = zone, OpenTripCode = null });
            Assert.Equal(ScanOutcomes.FoundUnassigned, d.Outcome);
            Assert.Equal(ScanReasonCodes.NoZone, d.ReasonCode);
            Assert.Equal(expected, d.Message);
            Assert.Equal("found", d.Voice);
        }
    }

    [Fact]
    public void Ambiguous_zone_lists_the_candidate_codes()
    {
        var d = OutboundScanRules.Decide(Ok() with { Zone = new ScanZone(null, true, new[] { "Z2", "Z1" }), OpenTripCode = null });
        Assert.Equal(ScanOutcomes.FoundUnassigned, d.Outcome);
        Assert.Equal(ScanReasonCodes.AmbiguousZone, d.ReasonCode);
        Assert.Equal("El código postal o pueblo pertenece a varias zonas (Z1, Z2); queda sin asignar.", d.Message);
        Assert.Equal("found", d.Voice);
    }

    [Fact]
    public void No_open_route_names_zone_and_date()
    {
        var d = OutboundScanRules.Decide(Ok() with { OpenTripCode = null });
        Assert.Equal(ScanOutcomes.FoundUnassigned, d.Outcome);
        Assert.Equal(ScanReasonCodes.NoOpenRoute, d.ReasonCode);
        Assert.Equal("No hay ruta abierta para la zona Z1 en la fecha 2026-09-26; queda sin asignar.", d.Message);
        Assert.Equal("found", d.Voice);
    }

    [Fact]
    public void Everything_ok_is_assigned()
    {
        var d = OutboundScanRules.Decide(Ok());
        Assert.Equal(ScanOutcomes.FoundAssigned, d.Outcome);
        Assert.Null(d.ReasonCode);
        Assert.Equal("Asignada a la ruta 2026-0001.", d.Message);
        Assert.Equal("found", d.Voice);
    }

    [Fact]
    public void Not_found_wins_over_every_other_fact()
    {
        // Precedencia: la cantidad de coincidencias se evalúa antes que cualquier otro hecho.
        var d = OutboundScanRules.Decide(Ok() with { MatchCount = 0, CurrentTripCode = "2026-0002", IneligibleReason = "x" });
        Assert.Equal(ScanOutcomes.NotFound, d.Outcome);
    }

    [Fact]
    public void Not_eligible_wins_over_zone_problems()
    {
        var d = OutboundScanRules.Decide(Ok() with { IneligibleReason = "motivo", Zone = new ScanZone(null, true, new[] { "A", "B" }) });
        Assert.Equal(ScanOutcomes.NotEligible, d.Outcome);
        Assert.Equal("motivo", d.Message);
    }

    [Fact]
    public void Date_in_message_is_culture_invariant()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("th-TH");
            Assert.Equal("No hay ruta abierta para la zona Z1 en la fecha 2026-09-26; queda sin asignar.",
                OutboundScanRules.NoOpenRouteMessage("Z1", PlanDate));
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }
}
