using Teikem.Domain.Clients;
using Teikem.Domain.Constants;
using Teikem.Domain.Orders;
using Xunit;

namespace Teikem.Tests;

/// <summary>Lote 3 (P1): reglas puras de numeración de los cuatro identificadores de la orden.</summary>
public class NumberingRulesTests
{
    private static readonly NumberingSettings NoPatterns = new(false, false, null, null, null);
    private static readonly NumberingSettings ClientPatterns = new(true, true, "T-#####", "F-####", "P-###");

    [Fact]
    public void EffectivePattern_uses_client_pattern_when_present()
    {
        Assert.Equal("T-#####", NumberingRules.EffectivePattern(NumberKinds.Order, ClientPatterns));
        Assert.Equal("F-####", NumberingRules.EffectivePattern(NumberKinds.Invoice, ClientPatterns));
        Assert.Equal("P-###", NumberingRules.EffectivePattern(NumberKinds.Package, ClientPatterns));
    }

    [Fact]
    public void EffectivePattern_falls_back_to_system_defaults()
    {
        Assert.Equal("ORD-#####", NumberingRules.EffectivePattern(NumberKinds.Order, NoPatterns));
        Assert.Equal("FAC-#####", NumberingRules.EffectivePattern(NumberKinds.Invoice, NoPatterns));
        Assert.Equal("PQT-#####", NumberingRules.EffectivePattern(NumberKinds.Package, NoPatterns));
        Assert.Equal(NumberFormat.Defaults.Order, NumberingRules.EffectivePattern(NumberKinds.Order, new(false, false, "   ", null, null)));
    }

    [Fact]
    public void PackBatch_pattern_is_always_EMP_even_with_client_patterns()
    {
        Assert.Equal("EMP-#####", NumberingRules.PackBatchPattern);
        Assert.Equal("EMP-#####", NumberingRules.EffectivePattern(NumberKinds.PackBatch, ClientPatterns));
        Assert.Equal("EMP-#####", NumberingRules.EffectivePattern(NumberKinds.PackBatch, NoPatterns));
    }

    [Fact]
    public void EffectivePattern_rejects_unknown_kind()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => NumberingRules.EffectivePattern("NOPE", NoPatterns));
    }

    [Fact]
    public void ScopeClientId_is_per_client_except_packbatch()
    {
        Assert.Equal(7, NumberingRules.ScopeClientId(NumberKinds.Order, 7));
        Assert.Equal(7, NumberingRules.ScopeClientId(NumberKinds.Invoice, 7));
        Assert.Equal(7, NumberingRules.ScopeClientId(NumberKinds.Package, 7));
        Assert.Null(NumberingRules.ScopeClientId(NumberKinds.PackBatch, 7));
    }

    [Fact]
    public void DrawOrder_is_order_invoice_packbatch_package()
    {
        Assert.Equal(new[] { "ORDER", "INVOICE", "PACKBATCH", "PACKAGE" }, NumberingRules.DrawOrder);
        Assert.Equal(new[] { NumberKinds.Order, NumberKinds.Invoice, NumberKinds.PackBatch, NumberKinds.Package }, NumberingRules.DrawOrder);
    }

    [Fact]
    public void ClientAssigns_follows_the_two_independent_questions()
    {
        var onlyOrder = new NumberingSettings(true, false, null, null, null);
        Assert.True(NumberingRules.ClientAssigns(NumberKinds.Order, onlyOrder));
        Assert.False(NumberingRules.ClientAssigns(NumberKinds.Invoice, onlyOrder));
        var onlyInvoice = new NumberingSettings(false, true, null, null, null);
        Assert.False(NumberingRules.ClientAssigns(NumberKinds.Order, onlyInvoice));
        Assert.True(NumberingRules.ClientAssigns(NumberKinds.Invoice, onlyInvoice));
        // El paquete siempre puede teclearse por línea; el empaque nunca.
        Assert.True(NumberingRules.ClientAssigns(NumberKinds.Package, NoPatterns));
        Assert.False(NumberingRules.ClientAssigns(NumberKinds.PackBatch, ClientPatterns));
    }

    [Fact]
    public void EnsureTypedAllowed_order_typed_but_teikem_assigns_gives_exact_message()
    {
        var ex = Assert.Throws<ArgumentException>(() => NumberingRules.EnsureTypedAllowed(NumberKinds.Order, NoPatterns, "PO-1"));
        Assert.Equal("El número de orden lo asigna Teikem para este cliente; déjelo en blanco.", ex.Message.Split(" (Parameter")[0]);
        Assert.StartsWith("El número de orden lo asigna Teikem para este cliente; déjelo en blanco.", ex.Message);
    }

    [Fact]
    public void EnsureTypedAllowed_invoice_typed_but_teikem_assigns_gives_exact_message()
    {
        var ex = Assert.Throws<ArgumentException>(() => NumberingRules.EnsureTypedAllowed(NumberKinds.Invoice, NoPatterns, "INV-1"));
        Assert.StartsWith("El número de factura lo asigna Teikem para este cliente; déjelo en blanco.", ex.Message);
    }

    [Fact]
    public void EnsureTypedAllowed_packbatch_typed_is_always_rejected()
    {
        var ex = Assert.Throws<ArgumentException>(() => NumberingRules.EnsureTypedAllowed(NumberKinds.PackBatch, ClientPatterns, "EMP-1"));
        Assert.StartsWith("El número de empaque siempre lo genera Teikem.", ex.Message);
    }

    [Fact]
    public void EnsureTypedAllowed_returns_trimmed_value_without_changing_case_when_allowed()
    {
        Assert.Equal("po-Abc", NumberingRules.EnsureTypedAllowed(NumberKinds.Order, ClientPatterns, "  po-Abc "));
        Assert.Equal("Inv-9", NumberingRules.EnsureTypedAllowed(NumberKinds.Invoice, ClientPatterns, "Inv-9"));
        Assert.Equal("pk-1", NumberingRules.EnsureTypedAllowed(NumberKinds.Package, NoPatterns, " pk-1 "));
    }

    [Fact]
    public void EnsureTypedAllowed_blank_means_automatic_for_every_kind()
    {
        Assert.Null(NumberingRules.EnsureTypedAllowed(NumberKinds.Order, NoPatterns, null));
        Assert.Null(NumberingRules.EnsureTypedAllowed(NumberKinds.Invoice, ClientPatterns, "   "));
        Assert.Null(NumberingRules.EnsureTypedAllowed(NumberKinds.PackBatch, NoPatterns, ""));
        Assert.Null(NumberingRules.EnsureTypedAllowed(NumberKinds.Package, NoPatterns, null));
    }

    [Fact]
    public void NormalizeTyped_blank_is_null_and_41_chars_is_error()
    {
        Assert.Null(NumberingRules.NormalizeTyped(null));
        Assert.Null(NumberingRules.NormalizeTyped(""));
        Assert.Null(NumberingRules.NormalizeTyped("   "));
        Assert.Equal("ABC", NumberingRules.NormalizeTyped("  ABC  "));
        Assert.Equal(new string('X', 40), NumberingRules.NormalizeTyped(new string('X', 40)));
        Assert.Throws<ArgumentException>(() => NumberingRules.NormalizeTyped(new string('X', 41)));
    }

    [Fact]
    public void Resolve_pads_and_never_truncates()
    {
        Assert.Equal("EMP-00001", NumberingRules.Resolve("EMP-#####", 1));
        Assert.Equal("PQT-00123", NumberingRules.Resolve("PQT-#####", 123));
        Assert.Equal("ORD-123456", NumberingRules.Resolve("ORD-#####", 123456));
    }

    [Fact]
    public void MaxAutoRetries_is_five()
    {
        Assert.Equal(5, NumberingRules.MaxAutoRetries);
    }

    [Fact]
    public void CollidedKind_detects_only_automatic_numbers()
    {
        const string orderMsg = "Cannot insert duplicate key row in object 'dbo.TransportOrder' with unique index 'UX_Order_Number'.";
        const string packMsg = "Cannot insert duplicate key row in object 'dbo.TransportOrder' with unique index 'UX_Order_PackBatch'.";
        const string otherMsg = "Violation of UNIQUE KEY constraint 'UQ_NumberSequence'.";

        Assert.Equal(NumberKinds.Order, NumberingRules.CollidedKind(orderMsg, orderAuto: true, packBatchAuto: true));
        Assert.Null(NumberingRules.CollidedKind(orderMsg, orderAuto: false, packBatchAuto: true));
        Assert.Equal(NumberKinds.PackBatch, NumberingRules.CollidedKind(packMsg, orderAuto: true, packBatchAuto: true));
        Assert.Null(NumberingRules.CollidedKind(packMsg, orderAuto: true, packBatchAuto: false));
        Assert.Null(NumberingRules.CollidedKind(otherMsg, orderAuto: true, packBatchAuto: true));
        Assert.Null(NumberingRules.CollidedKind(null, orderAuto: true, packBatchAuto: true));
    }

    [Fact]
    public void FromClient_copies_the_five_settings()
    {
        var client = new Client
        {
            ClientAssignsOrderNumber = true,
            ClientAssignsInvoiceNumber = false,
            OrderNumberFormat = "AX-####",
            InvoiceNumberFormat = null,
            PackageNumberFormat = "PK-###",
        };
        var s = NumberingSettings.FromClient(client);
        Assert.Equal(new NumberingSettings(true, false, "AX-####", null, "PK-###"), s);
        Assert.Equal("AX-####", NumberingRules.EffectivePattern(NumberKinds.Order, s));
        Assert.Equal("FAC-#####", NumberingRules.EffectivePattern(NumberKinds.Invoice, s));
    }

    // ---------------------------------------------------------------- ResolveChecked (números automáticos que no caben en NVARCHAR(40))

    [Fact]
    public void ResolveChecked_returns_the_number_when_it_fits_in_40_characters()
    {
        Assert.Equal("ORD-00001", NumberingRules.ResolveChecked(NumberKinds.Order, "ORD-#####", 1));
        var pattern = new string('X', 38) + "-#"; // 40 caracteres
        Assert.Equal(new string('X', 38) + "-9", NumberingRules.ResolveChecked(NumberKinds.Order, pattern, 9));
    }

    [Theory]
    [InlineData(NumberKinds.Order, "El número de orden generado con el patrón del cliente excede 40 caracteres; acorte el patrón en la ficha del cliente.")]
    [InlineData(NumberKinds.Invoice, "El número de factura generado con el patrón del cliente excede 40 caracteres; acorte el patrón en la ficha del cliente.")]
    [InlineData(NumberKinds.Package, "El número de paquete generado con el patrón del cliente excede 40 caracteres; acorte el patrón en la ficha del cliente.")]
    public void ResolveChecked_rejects_a_generated_number_longer_than_40_characters_with_the_exact_message(string kind, string message)
    {
        var pattern = new string('X', 38) + "-#"; // 40 caracteres: el consecutivo 10 lo lleva a 41 (Resolve no trunca)
        var ex = Assert.Throws<ArgumentException>(() => NumberingRules.ResolveChecked(kind, pattern, 10));
        Assert.Equal(message, ex.Message);
        Assert.Equal(message, NumberingRules.GeneratedTooLongMessage(kind));
    }

    [Fact]
    public void Typed_number_messages_have_no_parameter_suffix()
    {
        var ex = Assert.Throws<ArgumentException>(() => NumberingRules.EnsureTypedAllowed(NumberKinds.Order, NoPatterns, "X-1"));
        Assert.Equal("El número de orden lo asigna Teikem para este cliente; déjelo en blanco.", ex.Message);
        var tooLong = Assert.Throws<ArgumentException>(() => NumberingRules.NormalizeTyped(new string('A', 41)));
        Assert.Equal("El número no puede exceder 40 caracteres.", tooLong.Message);
    }
}
