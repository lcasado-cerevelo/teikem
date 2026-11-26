using Teikem.Domain.Tax;
using Teikem.Infrastructure.Tax;
using Xunit;

namespace Teikem.Tests;

// Lote 248 (dinero): la vigencia EFECTIVA de una exención del cliente sale del documento que la respalda; las viejas sin documento siguen igual.
public sealed class ClientExemptionBackingTests
{
    private static readonly DateOnly From = new(2026, 1, 1);
    private static readonly DateOnly Today = new(2026, 11, 27);

    [Fact]
    public void A_linked_exemption_takes_dates_and_number_from_its_document()
    {
        var doc = new ExemptionBackingDocument(true, new DateOnly(2026, 3, 1), new DateOnly(2027, 2, 28), " CERT-DOC ");
        var r = ClientExemptionBacking.Effective(7, From, new DateOnly(2026, 12, 31), "CERT-COPIA", doc);
        Assert.Equal((true, new DateOnly(2026, 3, 1), (DateOnly?)new DateOnly(2027, 2, 28), "CERT-DOC"), (r.Applies, r.From, r.To, r.Certificate));
        // el documento sin «desde» deja el de la exención; sin número, la copia de la exención
        var bare = ClientExemptionBacking.Effective(7, From, new DateOnly(2026, 12, 31), "CERT-COPIA", new ExemptionBackingDocument(true, null, new DateOnly(2027, 1, 31), null));
        Assert.Equal((From, (DateOnly?)new DateOnly(2027, 1, 31), "CERT-COPIA"), (bare.From, bare.To, bare.Certificate));
        // nunca queda abierta por un hueco del documento: sin «hasta» en el documento manda la copia
        Assert.Equal(new DateOnly(2026, 12, 31), ClientExemptionBacking.Effective(7, From, new DateOnly(2026, 12, 31), "C", new ExemptionBackingDocument(true, null, null, "C")).To);
    }

    [Fact]
    public void With_its_document_removed_or_missing_a_linked_exemption_does_not_apply()
    {
        Assert.False(ClientExemptionBacking.Effective(7, From, null, "C", new ExemptionBackingDocument(false, From, new DateOnly(2030, 1, 1), "C")).Applies);
        Assert.False(ClientExemptionBacking.Effective(7, From, null, "C", null).Applies);
    }

    [Fact]
    public void A_legacy_exemption_without_document_keeps_working_the_same_even_without_an_end_date()
    {
        var r = ClientExemptionBacking.Effective(null, From, null, "VIEJA-1", null);
        Assert.Equal((true, From, (DateOnly?)null, "VIEJA-1"), (r.Applies, r.From, r.To, r.Certificate));
        // y el cálculo la sigue aplicando: exención total vigente hoy, monto en 0
        var outcome = TaxRules.ResolveExemptions(new[] { new TaxExemptionInfo(TaxExemptionScopes.All, Array.Empty<string>(), r.Certificate, r.From, r.To) }, Today);
        Assert.Equal("VIEJA-1", outcome.Full?.CertificateNumber);
        var rate = new TaxRateInfo(1, "IVU", "IVU", 11.5m, new[] { new TaxComponentInfo("STATE", "Estatal", 10.5m), new TaxComponentInfo("CITY", "Municipal", 1m) });
        var calc = TaxRules.Calculate(new[] { new TaxLineInput("a", 100m, TaxTreatment.ExemptClient) }, rate, false, TaxRoundingModes.Line);
        Assert.Equal(0m, calc.TaxAmount);
    }

    [Fact]
    public void Backing_document_rules_return_the_exact_messages_in_order()
    {
        Assert.Equal(ClientExemptionRules.DocumentInactive, ClientExemptionRules.ValidateBackingDocument(false, false, null, null, Today).Error);
        Assert.Equal(ClientExemptionRules.DocumentNotTax, ClientExemptionRules.ValidateBackingDocument(true, false, "N", Today, Today).Error);
        Assert.Equal(ClientExemptionRules.DocumentWithoutNumber, ClientExemptionRules.ValidateBackingDocument(true, true, " ", Today, Today).Error);
        Assert.Equal(ClientExemptionRules.DocumentWithoutValidTo, ClientExemptionRules.ValidateBackingDocument(true, true, "N", null, Today).Error);
        Assert.Equal(ClientExemptionRules.DocumentExpired, ClientExemptionRules.ValidateBackingDocument(true, true, "N", Today.AddDays(-1), Today).Error);
        Assert.Equal((null, null), ClientExemptionRules.ValidateBackingDocument(true, true, "N", Today, Today));   // el último día todavía sirve
        Assert.Equal("La exención necesita el documento que la respalda.", ClientExemptionRules.DocumentRequired);
        Assert.Equal("Ese documento ya respalda otra exención activa.", ClientExemptionRules.DocumentAlreadyBacks);
    }

    [Fact]
    public void The_exemption_type_defaults_to_the_document_type_label_cut_to_sixty()
    {
        Assert.Equal("Gobierno", ClientExemptionRules.DefaultExemptionType(" Gobierno ", "Certificado"));
        Assert.Equal("Certificado de exención de IVU estatal", ClientExemptionRules.DefaultExemptionType(null, "Certificado de exención de IVU estatal"));
        Assert.Equal(60, ClientExemptionRules.DefaultExemptionType("", new string('x', 80)).Length);
    }
}
