using Teikem.Domain.Clients;
using Teikem.Domain.Orders;
using Xunit;

namespace Teikem.Tests;

/// <summary>Lote 3 (P2): reglas puras de la captura de órdenes (paquetes, totales, resumen, R36, campos fijos, snapshot).</summary>
public class OrderRulesTests
{
    // ---------------------------------------------------------------- ValidatePackages

    [Fact]
    public void ValidatePackages_requires_at_least_one_line_for_a_normal_order()
    {
        var errors = OrderRules.ValidatePackages(Array.Empty<PackageLineInput>(), isSpecialDelivery: false);
        Assert.Equal("Indique al menos una línea de paquete.", errors["packages"]);

        var nullErrors = OrderRules.ValidatePackages(null, isSpecialDelivery: false);
        Assert.Equal("Indique al menos una línea de paquete.", nullErrors["packages"]);
    }

    [Fact]
    public void ValidatePackages_special_delivery_rejects_lines_and_cod()
    {
        var withLines = OrderRules.ValidatePackages(new[] { new PackageLineInput("Caja", 1) }, isSpecialDelivery: true);
        Assert.Equal("Una entrega especial no lleva líneas de paquete ni COD.", withLines["packages"]);

        var withCod = OrderRules.ValidatePackages(Array.Empty<PackageLineInput>(), isSpecialDelivery: true, codAmount: 50m);
        Assert.Equal("Una entrega especial no lleva líneas de paquete ni COD.", withCod["codAmount"]);

        var clean = OrderRules.ValidatePackages(Array.Empty<PackageLineInput>(), isSpecialDelivery: true, codAmount: null);
        Assert.Empty(clean);
    }

    [Fact]
    public void ValidatePackages_flags_pieces_description_weight_and_volume_by_line()
    {
        var lines = new[]
        {
            new PackageLineInput("Caja", 0),
            new PackageLineInput(new string('x', 251), 1),
            new PackageLineInput("Sobre", 1, WeightKg: -1m),
            new PackageLineInput("Sobre", 1, VolumeM3: -0.5m),
        };
        var errors = OrderRules.ValidatePackages(lines, isSpecialDelivery: false);
        Assert.Equal("La cantidad de piezas debe ser al menos 1.", errors["packages[0].pieces"]);
        Assert.Equal("Máximo 250 caracteres.", errors["packages[1].description"]);
        Assert.True(errors.ContainsKey("packages[2].weightKg"));
        Assert.True(errors.ContainsKey("packages[3].volumeM3"));
        Assert.Equal(4, errors.Count);
    }

    [Fact]
    public void ValidatePackages_accepts_a_valid_line()
    {
        Assert.Empty(OrderRules.ValidatePackages(new[] { new PackageLineInput(null, 2, 1.5m, 0.2m) }, isSpecialDelivery: false));
    }

    // ---------------------------------------------------------------- Totals

    [Fact]
    public void Totals_sums_pieces_and_weight_only_when_some_line_has_it()
    {
        var (pieces, weight, volume) = OrderRules.Totals(new[] { new PackageLineInput("A", 2, WeightKg: 3m), new PackageLineInput("B", 1) });
        Assert.Equal(3, pieces);
        Assert.Equal(3m, weight);
        Assert.Null(volume);

        var (_, noWeight, _) = OrderRules.Totals(new[] { new PackageLineInput("A", 1), new PackageLineInput("B", 1) });
        Assert.Null(noWeight);
    }

    // ---------------------------------------------------------------- PackagesSummary

    [Fact]
    public void PackagesSummary_consolidates_by_label_in_order_of_appearance()
    {
        Assert.Equal("Caja ×3 + Sobre ×1", OrderRules.PackagesSummary(new[] { ("Caja", 2), ("Caja", 1), ("Sobre", 1) }));
        Assert.Equal("Caja ×3", OrderRules.PackagesSummary(new[] { ("Caja", 3) }));
        Assert.Equal("", OrderRules.PackagesSummary(Array.Empty<(string, int)>()));
    }

    // ---------------------------------------------------------------- DecideDuplicateInvoice

    [Theory]
    [InlineData(false, "ORD-1", false, false, DuplicateInvoiceDecision.Ok)]   // no tecleado: Ok aunque exista
    [InlineData(true, null, false, false, DuplicateInvoiceDecision.Ok)]       // tecleado sin existente
    [InlineData(true, "ORD-1", false, false, DuplicateInvoiceDecision.Blocked)]
    [InlineData(true, "ORD-1", false, true, DuplicateInvoiceDecision.Blocked)] // confirmado no sirve si no admite repetidas
    [InlineData(true, "ORD-1", true, false, DuplicateInvoiceDecision.NeedsConfirmation)]
    [InlineData(true, "ORD-1", true, true, DuplicateInvoiceDecision.Ok)]
    public void DecideDuplicateInvoice_matrix(bool typed, string? existing, bool allowDup, bool confirmed, DuplicateInvoiceDecision expected)
    {
        Assert.Equal(expected, OrderRules.DecideDuplicateInvoice(typed, existing, allowDup, confirmed));
    }

    // ---------------------------------------------------------------- RejectExtraFields

    [Fact]
    public void RejectExtraFields_patch_rejects_fixed_fields_with_exact_message()
    {
        var hit = OrderRules.RejectExtraFields(new[] { "notes", "orderNumber" }, OrderRules.ForbiddenOnPatch);
        Assert.NotNull(hit);
        Assert.Equal("orderNumber", hit!.Value.Field);
        Assert.Equal("El campo 'orderNumber' se fija al crear la orden y no se edita.", hit.Value.Message);
    }

    [Fact]
    public void RejectExtraFields_create_rejects_codType_and_packBatchNumber()
    {
        var cod = OrderRules.RejectExtraFields(new[] { "codType" }, OrderRules.ForbiddenOnCreate);
        Assert.Equal("El tipo de COD se registra al entregar, no en la captura.", cod!.Value.Message);

        var pack = OrderRules.RejectExtraFields(new[] { "packBatchNumber" }, OrderRules.ForbiddenOnCreate);
        Assert.Equal("El número de empaque siempre lo genera Teikem.", pack!.Value.Message);
    }

    [Fact]
    public void RejectExtraFields_is_case_insensitive_and_ignores_unknown_keys()
    {
        var upper = OrderRules.RejectExtraFields(new[] { "OrderNumber" }, OrderRules.ForbiddenOnPatch);
        Assert.Equal("orderNumber", upper!.Value.Field);

        Assert.Null(OrderRules.RejectExtraFields(new[] { "foo", "bar" }, OrderRules.ForbiddenOnPatch));
        Assert.Null(OrderRules.RejectExtraFields(null, OrderRules.ForbiddenOnPatch));
    }

    [Fact]
    public void Forbidden_sets_have_the_documented_fields()
    {
        Assert.Equal(new[] { "clientPublicId", "orderNumber", "clientInvoiceNumber", "packBatchNumber" }, OrderRules.FixedFieldsOnPatch);
        Assert.Equal(new[] { "packBatchNumber", "codType" }, OrderRules.ForbiddenOnCreate);
        Assert.Contains("codType", OrderRules.ForbiddenOnPatch);
        Assert.Contains("clientPublicId", OrderRules.ForbiddenOnPatch);
    }

    // ---------------------------------------------------------------- CanDelete / ValidateCod

    [Fact]
    public void CanDelete_only_in_initial_status_and_active()
    {
        Assert.True(OrderRules.CanDelete(true, true));
        Assert.False(OrderRules.CanDelete(false, true));
        Assert.False(OrderRules.CanDelete(true, false));
    }

    [Fact]
    public void ValidateCod_rejects_negative_and_accepts_zero_or_null()
    {
        Assert.Equal("El monto COD no puede ser negativo.", OrderRules.ValidateCod(-1m));
        Assert.Null(OrderRules.ValidateCod(0m));
        Assert.Null(OrderRules.ValidateCod(null));
        Assert.Null(OrderRules.ValidateCod(12.5m));
    }

    // ---------------------------------------------------------------- ApplySnapshot

    private static Location SampleLocation(TimeOnly? start = null, TimeOnly? end = null, string? notes = "Portón azul") => new()
    {
        LocationId = 77,
        Name = "Central",
        Line1 = "Calle 5 #12",
        Line2 = "Suite 2",
        City = "Ponce",
        State = "PR",
        PostalCode = "00716",
        DefaultServiceMinutes = 15,
        DefaultWindowStart = start,
        DefaultWindowEnd = end,
        DeliveryNotes = notes,
    };

    [Fact]
    public void ApplySnapshot_copies_address_minutes_notes_and_location_id()
    {
        var stop = new OrderStop();
        OrderRules.ApplySnapshot(stop, SampleLocation(), "US", null);
        Assert.Equal(77, stop.LocationId);
        Assert.Equal("Central", stop.SnapName);
        Assert.Equal("Calle 5 #12", stop.SnapLine1);
        Assert.Equal("Suite 2", stop.SnapLine2);
        Assert.Equal("Ponce", stop.SnapCity);
        Assert.Equal("PR", stop.SnapState);
        Assert.Equal("00716", stop.SnapPostalCode);
        Assert.Equal("US", stop.SnapCountryCode);
        Assert.Equal(15, stop.ServiceMinutes);
        Assert.Equal("Portón azul", stop.Notes);
        Assert.Null(stop.WindowStartUtc);
        Assert.Null(stop.WindowEndUtc);
    }

    [Fact]
    public void ApplySnapshot_defaults_country_to_PR_and_truncates_notes_to_500()
    {
        var stop = new OrderStop();
        OrderRules.ApplySnapshot(stop, SampleLocation(notes: new string('n', 600)), null, null);
        Assert.Equal("PR", stop.SnapCountryCode);
        Assert.Equal(500, stop.Notes!.Length);

        var empty = new OrderStop();
        OrderRules.ApplySnapshot(empty, SampleLocation(notes: "  "), "", null);
        Assert.Equal("PR", empty.SnapCountryCode);
        Assert.Null(empty.Notes);
    }

    [Fact]
    public void ApplySnapshot_window_is_requested_date_plus_default_window_without_timezone_conversion()
    {
        var stop = new OrderStop();
        OrderRules.ApplySnapshot(stop, SampleLocation(new TimeOnly(8, 0), new TimeOnly(12, 0)), "PR", new DateTime(2026, 10, 5, 15, 30, 0));
        Assert.Equal(new DateTime(2026, 10, 5, 8, 0, 0), stop.WindowStartUtc);
        Assert.Equal(new DateTime(2026, 10, 5, 12, 0, 0), stop.WindowEndUtc);
    }

    [Fact]
    public void ApplySnapshot_without_requested_date_leaves_window_null_even_if_location_has_one()
    {
        var stop = new OrderStop();
        OrderRules.ApplySnapshot(stop, SampleLocation(new TimeOnly(8, 0), new TimeOnly(12, 0)), "PR", null);
        Assert.Null(stop.WindowStartUtc);
        Assert.Null(stop.WindowEndUtc);
    }

    [Fact]
    public void ApplySnapshot_with_only_start_window_sets_only_start()
    {
        var stop = new OrderStop();
        OrderRules.ApplySnapshot(stop, SampleLocation(new TimeOnly(8, 0), null), "PR", new DateTime(2026, 10, 5));
        Assert.Equal(new DateTime(2026, 10, 5, 8, 0, 0), stop.WindowStartUtc);
        Assert.Null(stop.WindowEndUtc);
    }

    [Fact]
    public void ApplySnapshot_on_an_existing_stop_clears_a_previous_window_when_the_new_location_has_none()
    {
        var stop = new OrderStop { WindowStartUtc = new DateTime(2026, 1, 1, 9, 0, 0), WindowEndUtc = new DateTime(2026, 1, 1, 10, 0, 0) };
        OrderRules.ApplySnapshot(stop, SampleLocation(), "PR", new DateTime(2026, 10, 5));
        Assert.Null(stop.WindowStartUtc);
        Assert.Null(stop.WindowEndUtc);
    }
}
