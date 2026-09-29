using Teikem.Domain.Constants;
using Teikem.Domain.Migration;
using Teikem.Domain.Wms;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 10 / P0: catálogos compartidos que amplía la migración. El seed trae los siete términos de pago de QuickBooks y el
/// motivo OPENING_BALANCE con sus etiquetas exactas; OPENING_BALANCE es de sistema (un ajuste manual con él → 400) y cada
/// código que produce LegacyImportRules.MapPaymentTerm existe en el seed.
/// </summary>
public class LegacyImportCatalogTests
{
    private static readonly Lazy<string> Seed = new(() => File.ReadAllText(Path.Combine(TripCatalogTests.RepoRoot(), "Diseño", "logistica-db-seed.sql")));

    [Theory]
    [InlineData("('PaymentTerm','CHEQUE','Cheque','Check',5)")]
    [InlineData("('PaymentTerm','CASH','Efectivo','Cash',6)")]
    [InlineData("('PaymentTerm','ACH','Transferencia ACH','ACH transfer',7)")]
    [InlineData("('PaymentTerm','NET20','20 días','Net 20',8)")]
    [InlineData("('PaymentTerm','NET45','45 días','Net 45',9)")]
    [InlineData("('PaymentTerm','CONSIGNMENT','Consignación','Consignment',10)")]
    [InlineData("('PaymentTerm','PK_BY_REP','Cobra el representante','Paid via rep',11)")]
    [InlineData("('AdjustmentReason','OPENING_BALANCE','Saldo inicial (migración)','Opening balance (migration)',10)")]
    public void Seed_contains_the_new_lookup_codes_with_exact_labels(string row)
        => Assert.Contains(row, Seed.Value, StringComparison.Ordinal);

    [Fact]
    public void Seed_marks_the_lote10_block_and_keeps_it_inside_the_lookup_merge()
    {
        var seed = Seed.Value;
        var marker = seed.IndexOf("-- Lote 10 — Migración de datos", StringComparison.Ordinal);
        Assert.True(marker > 0, "Falta el comentario '-- Lote 10 — Migración de datos' en el seed.");
        var merge = seed.IndexOf("MERGE dbo.LookupCode AS t\r\nUSING #L", StringComparison.Ordinal);
        if (merge < 0) merge = seed.IndexOf("MERGE dbo.LookupCode AS t\nUSING #L", StringComparison.Ordinal);
        Assert.True(merge > marker, "El bloque del Lote 10 debe ir dentro del INSERT de #L, antes del MERGE de LookupCode.");
        // Cada código aparece una sola vez (sin duplicar filas del Lote 1: COD, NET15, NET30, NET60).
        foreach (var code in new[] { "CHEQUE", "CASH", "ACH", "NET20", "NET45", "CONSIGNMENT", "PK_BY_REP", "COD", "NET15", "NET30", "NET60" })
            Assert.Equal(1, Count(seed, $"('PaymentTerm','{code}',"));
        Assert.Equal(1, Count(seed, "('AdjustmentReason','OPENING_BALANCE',"));
    }

    [Theory]
    [InlineData("CHEQUE")]
    [InlineData("Cash")]
    [InlineData("ach")]
    [InlineData("COD")]
    [InlineData("Due on receipt")]
    [InlineData("Net 15")]
    [InlineData("Net 20")]
    [InlineData("Net 30")]
    [InlineData("Net 45")]
    [InlineData("Net 60")]
    [InlineData("Consignment")]
    [InlineData("PK BY REP")]
    public void Every_mapped_quickbooks_term_exists_in_the_seed(string qbTerm)
    {
        var code = LegacyImportRules.MapPaymentTerm(qbTerm);
        Assert.NotNull(code);
        Assert.Contains($"('PaymentTerm','{code}',", Seed.Value, StringComparison.Ordinal);
    }

    [Fact]
    public void Opening_balance_is_a_system_assigned_reason()
    {
        Assert.Equal("OPENING_BALANCE", AdjustmentReasons.OpeningBalance);
        Assert.Contains(AdjustmentReasons.OpeningBalance, AdjustmentReasons.SystemAssigned);
        Assert.Contains(AdjustmentReasons.OpeningBalance, AdjustmentRules.SystemReasons);
        Assert.Contains("opening_balance", AdjustmentRules.SystemReasons); // sin distinguir mayúsculas
        // Los tres motivos de sistema anteriores no cambian.
        Assert.Equal(new[] { AdjustmentReasons.ReceiptVariance, AdjustmentReasons.CountVariance, AdjustmentReasons.PickBatchReversal, AdjustmentReasons.OpeningBalance },
            AdjustmentReasons.SystemAssigned);
    }

    [Theory]
    [InlineData("OPENING_BALANCE")]
    [InlineData("opening_balance")]
    [InlineData("  Opening_Balance ")]
    public void A_manual_adjustment_with_opening_balance_is_rejected_with_the_exact_message(string reason)
    {
        var catalog = new[] { AdjustmentReasons.Damage, AdjustmentReasons.Other, AdjustmentReasons.OpeningBalance };
        var (code, error) = AdjustmentRules.ValidateReason(reason, catalog);
        Assert.Null(code);
        Assert.Equal("El motivo OPENING_BALANCE lo asigna el sistema.", error);
    }

    [Fact]
    public void Manual_reasons_are_still_accepted()
    {
        var catalog = new[] { AdjustmentReasons.Damage, AdjustmentReasons.Other, AdjustmentReasons.OpeningBalance };
        Assert.Equal((AdjustmentReasons.Damage, (string?)null), AdjustmentRules.ValidateReason("damage", catalog));
    }

    private static int Count(string text, string needle)
    {
        var n = 0;
        for (var i = text.IndexOf(needle, StringComparison.Ordinal); i >= 0; i = text.IndexOf(needle, i + needle.Length, StringComparison.Ordinal)) n++;
        return n;
    }
}
