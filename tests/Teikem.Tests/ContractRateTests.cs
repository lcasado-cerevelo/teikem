using Teikem.Infrastructure.Clients;
using Xunit;

namespace Teikem.Tests;

/// <summary>Reglas puras de los tramos de pieza extra (Lote 2, P4).</summary>
public class RateTierRulesTests
{
    private static RateTierRules.TierRange R(int id, int from, int? to) => new(id, from, to);

    [Fact]
    public void Overlap_2_5_vs_4_7_overlaps()
        => Assert.NotNull(RateTierRules.FindOverlap(R(0, 4, 7), new[] { R(1, 2, 5) }));

    [Fact]
    public void Overlap_2_5_vs_6_open_does_not_overlap()
        => Assert.Null(RateTierRules.FindOverlap(R(0, 6, null), new[] { R(1, 2, 5) }));

    [Fact]
    public void Overlap_6_open_vs_8_10_overlaps()
        => Assert.NotNull(RateTierRules.FindOverlap(R(0, 8, 10), new[] { R(1, 6, null) }));

    [Fact]
    public void Overlap_2_5_vs_5_8_is_inclusive()
        => Assert.NotNull(RateTierRules.FindOverlap(R(0, 5, 8), new[] { R(1, 2, 5) }));

    [Fact]
    public void IsValidRange_rejects_inverted_and_piece_one()
    {
        Assert.False(RateTierRules.IsValidRange(6, 5));
        Assert.False(RateTierRules.IsValidRange(1, null)); // la pieza 1 nunca es extra
        Assert.True(RateTierRules.IsValidRange(2, 5));
        Assert.True(RateTierRules.IsValidRange(6, null));
        Assert.True(RateTierRules.IsValidRange(4, 4));
    }

    [Fact]
    public void FindOverlap_excludes_the_tier_being_edited()
    {
        var existing = new[] { R(10, 2, 5), R(11, 6, null) };
        // Editar el tramo 10 a 2–4 no debe chocar consigo mismo
        Assert.Null(RateTierRules.FindOverlap(R(10, 2, 4), existing, excludeId: 10));
        // ...pero sí con el vecino si se extiende a 2–6
        var clash = RateTierRules.FindOverlap(R(10, 2, 6), existing, excludeId: 10);
        Assert.NotNull(clash);
        Assert.Equal(11, clash!.Id);
        // Sin excluir, choca consigo mismo
        Assert.Equal(10, RateTierRules.FindOverlap(R(10, 2, 4), existing)!.Id);
    }
}

/// <summary>Motor puro de cotización por contrato (Lote 2, P4), anclado a los montos de la bitácora.</summary>
public class ContractRateResolverTests
{
    private const string Std = "STANDARD";
    private const string Box = "BOX";
    private const string Envelope = "ENVELOPE";
    private const int ContractId = 7;

    private static readonly IReadOnlyList<TierRow> NoTiers = Array.Empty<TierRow>();

    private static RateRow ContractBase(int id, decimal rate, string svc = Std, string pkg = Box)
        => new(id, ContractId, RateKinds.PerService, svc, pkg, rate, NoTiers);

    private static RateRow GenericBase(int id, decimal rate, string? svc = null, string? pkg = null)
        => new(id, null, RateKinds.PerService, svc, pkg, rate, NoTiers);

    /// <summary>Componente de pieza extra del contrato con los tramos de la bitácora: 2–5 @ 1.00 y 6+ @ 0.75.</summary>
    private static RateRow ContractExtra(int id = 20, string svc = Std, string pkg = Box)
        => new(id, ContractId, RateKinds.ExtraPiece, svc, pkg, 0m, new[] { new TierRow(201, 2, 5, 1.00m), new TierRow(202, 6, null, 0.75m) });

    /// <summary>Pieza extra genérica del tenant: un solo tramo abierto 2+ @ rate.</summary>
    private static RateRow GenericExtra(int id = 30, decimal rate = 2.00m)
        => new(id, null, RateKinds.ExtraPiece, null, null, 0m, new[] { new TierRow(301, 2, null, rate) });

    private static ContractFlags Flags(bool perService = true, bool extra = true, bool dispatch = false, bool cod = false,
        decimal? dispatchFee = null, string? codType = null, decimal? codValue = null)
        => new(perService, extra, dispatch, cod, dispatchFee, codType, codValue);

    // ---------------- BaseRate ----------------

    [Fact]
    public void BaseRate_uses_contract_row_when_component_is_on()
    {
        var rows = new[] { ContractBase(1, 6.50m), GenericBase(2, 12.00m) };
        var r = ContractRateResolver.BaseRate(rows, Flags(), Std, Box);
        Assert.Equal(6.50m, r.Amount);
        Assert.Equal(RateSources.Contract, r.Source);
        Assert.Equal(1, r.RowId);
    }

    [Fact]
    public void BaseRate_falls_back_to_generic_when_component_is_off_or_row_is_missing()
    {
        var rows = new[] { ContractBase(1, 6.50m), GenericBase(2, 12.00m) };
        var off = ContractRateResolver.BaseRate(rows, Flags(perService: false), Std, Box);
        Assert.Equal(12.00m, off.Amount);
        Assert.Equal(RateSources.Generic, off.Source);
        Assert.Equal(2, off.RowId);

        var missing = ContractRateResolver.BaseRate(rows, Flags(), Std, Envelope);
        Assert.Equal(12.00m, missing.Amount);
        Assert.Equal(RateSources.Generic, missing.Source);
    }

    [Fact]
    public void BaseRate_without_generic_is_null_none()
    {
        var rows = new[] { ContractBase(1, 6.50m) };
        var r = ContractRateResolver.BaseRate(rows, Flags(), Std, Envelope);
        Assert.Null(r.Amount);
        Assert.Equal(RateSources.None, r.Source);
        Assert.Null(r.RowId);
    }

    [Fact]
    public void BaseRate_specificity_exact_beats_service_wildcard_beats_total_wildcard()
    {
        var rows = new[]
        {
            GenericBase(1, 10.00m),               // comodín total
            GenericBase(2, 11.00m, svc: Std),     // servicio + comodín de paquete
            GenericBase(3, 12.00m, svc: Std, pkg: Box), // exacta
            GenericBase(4, 9.00m, pkg: Envelope), // comodín de servicio + paquete
        };
        Assert.Equal(3, ContractRateResolver.BaseRate(rows, Flags(), Std, Box).RowId);
        Assert.Equal(2, ContractRateResolver.BaseRate(rows, Flags(), Std, "PALLET").RowId);
        Assert.Equal(4, ContractRateResolver.BaseRate(rows, Flags(), "EXPRESS", Envelope).RowId);
        Assert.Equal(1, ContractRateResolver.BaseRate(rows, Flags(), "EXPRESS", "PALLET").RowId);
    }

    [Fact]
    public void BaseRate_two_exact_contract_rows_violate_invariant()
    {
        var rows = new[] { ContractBase(1, 6.50m), ContractBase(2, 7.00m) };
        Assert.Throws<InvalidOperationException>(() => ContractRateResolver.BaseRate(rows, Flags(), Std, Box));
    }

    // ---------------- ExtraPieces (GRADUATED marginal) ----------------

    [Theory]
    [InlineData(1, 0.00)]
    [InlineData(2, 1.00)]
    [InlineData(5, 4.00)]
    [InlineData(8, 6.25)]
    public void ExtraPieces_graduated_marginal_matches_the_log(int pieces, decimal expected)
    {
        var rows = new[] { ContractExtra() };
        var r = ContractRateResolver.ExtraPieces(rows, Flags(), Std, Box, pieces);
        Assert.Equal(expected, r.Amount);
        Assert.Equal(pieces >= 2 ? RateSources.Contract : RateSources.None, r.Source);
    }

    [Fact]
    public void ExtraPieces_eight_pieces_is_6_25_not_flat_tier_readings()
    {
        var r = ContractRateResolver.ExtraPieces(new[] { ContractExtra() }, Flags(), Std, Box, 8);
        Assert.Equal(6.25m, r.Amount);      // 4 × 1.00 + 3 × 0.75
        Assert.NotEqual(7.00m, r.Amount);   // no son 7 piezas al tramo 1
        Assert.NotEqual(5.25m, r.Amount);   // no son 7 piezas al tramo 2
        Assert.Equal(new[] { 20 }, r.RowIds);
    }

    [Fact]
    public void ExtraPieces_component_off_uses_generic_tiers()
    {
        var rows = new[] { ContractExtra(), GenericExtra(rate: 2.00m) };
        var r = ContractRateResolver.ExtraPieces(rows, Flags(extra: false), Std, Box, 8);
        Assert.Equal(14.00m, r.Amount);     // 7 × 2.00
        Assert.Equal(RateSources.Generic, r.Source);
        Assert.Equal(new[] { 30 }, r.RowIds);
    }

    [Fact]
    public void ExtraPieces_without_generic_is_zero_none()
    {
        var rows = new[] { ContractExtra() };
        var r = ContractRateResolver.ExtraPieces(rows, Flags(extra: false), Std, Box, 8);
        Assert.Equal(0m, r.Amount);
        Assert.Equal(RateSources.None, r.Source);
        Assert.Empty(r.RowIds);
    }

    [Fact]
    public void ExtraPieces_piece_outside_contract_tiers_falls_to_generic_per_piece()
    {
        // Contrato solo cubre 2–5; la genérica cubre 2+ @ 2.00. Con 8 piezas: 4 × 1.00 (contrato) + 3 × 2.00 (genérica) = 10.00
        var contract = new RateRow(20, ContractId, RateKinds.ExtraPiece, Std, Box, 0m, new[] { new TierRow(201, 2, 5, 1.00m) });
        var r = ContractRateResolver.ExtraPieces(new[] { contract, GenericExtra() }, Flags(), Std, Box, 8);
        Assert.Equal(10.00m, r.Amount);
        Assert.Equal(RateSources.Contract, r.Source);
        Assert.Equal(new[] { 20, 30 }, r.RowIds);
    }

    // ---------------- Cargos por orden ----------------

    [Fact]
    public void CodFee_off_is_zero()
        => Assert.Equal(0m, ContractRateResolver.CodFee(Flags(cod: false, codType: "FIXED", codValue: 2.00m), 200m));

    [Fact]
    public void CodFee_fixed_returns_value()
        => Assert.Equal(2.00m, ContractRateResolver.CodFee(Flags(cod: true, codType: "FIXED", codValue: 2.00m), 200m));

    [Fact]
    public void CodFee_percent_is_over_cod_amount()
    {
        Assert.Equal(5.00m, ContractRateResolver.CodFee(Flags(cod: true, codType: "PERCENT", codValue: 2.5m), 200m));
        Assert.Equal(0.8333m, ContractRateResolver.CodFee(Flags(cod: true, codType: "PERCENT", codValue: 2.5m), 33.33m));
        Assert.Equal(0m, ContractRateResolver.CodFee(Flags(cod: true, codType: "PERCENT", codValue: 2.5m), 0m));
    }

    [Fact]
    public void DispatchFee_off_is_zero_on_is_amount()
    {
        Assert.Equal(0m, ContractRateResolver.DispatchFee(Flags(dispatch: false, dispatchFee: 3.00m)));
        Assert.Equal(3.00m, ContractRateResolver.DispatchFee(Flags(dispatch: true, dispatchFee: 3.00m)));
    }

    [Fact]
    public void Money_round4_is_away_from_zero()
    {
        Assert.Equal(0.8333m, Money.Round4(0.83325m));
        Assert.Equal(1.0001m, Money.Round4(1.00005m));
        Assert.Equal(-1.0001m, Money.Round4(-1.00005m));
    }

    // ---------------- Líneas repetidas (documento L1096) ----------------

    [Fact]
    public void FindDuplicateLine_flags_the_second_line_with_the_same_service_and_package()
    {
        // Dos líneas STANDARD/BOX de 1 pieza cobrarían dos bases (6.50 + 6.50) en vez de base + pieza extra (6.50 + 1.00):
        // el servicio las rechaza con 400 antes de cotizar.
        var lines = new[] { new QuoteLine(Std, Box, 1), new QuoteLine(" standard ", "box", 1) };
        Assert.Equal(1, ContractRateResolver.FindDuplicateLine(lines));
        Assert.Null(ContractRateResolver.FindDuplicateLine(new[] { new QuoteLine(Std, Box, 2), new QuoteLine(Std, Envelope, 1) }));
        Assert.Null(ContractRateResolver.FindDuplicateLine(Array.Empty<QuoteLine>()));
    }

    // ---------------- QuoteOrder (bitácora) ----------------

    [Fact]
    public void QuoteOrder_L1094_mixed_order_with_generics_totals_25()
    {
        // Contrato sin tarifas propias: base genérica BOX 12, ENVELOPE 8, pieza extra genérica 2.00, COD FIXED 3 (encendido)
        var rows = new[] { GenericBase(1, 12.00m, pkg: Box), GenericBase(2, 8.00m, pkg: Envelope), GenericExtra(30, 2.00m) };
        var flags = Flags(perService: true, extra: true, cod: true, codType: "FIXED", codValue: 3m);
        var lines = new[] { new QuoteLine(Std, Box, 2), new QuoteLine(Std, Envelope, 1) };

        var q = ContractRateResolver.QuoteOrder(rows, flags, lines, codAmount: 100m);

        Assert.Equal(2, q.Lines.Count);
        Assert.Equal(12.00m, q.Lines[0].BaseRate);
        Assert.Equal(2.00m, q.Lines[0].ExtraPieces);
        Assert.Equal(17.00m, q.Lines[0].LineTotal);   // 12 + 2 + COD 3 (solo en la primera)
        Assert.Equal(8.00m, q.Lines[1].BaseRate);
        Assert.Equal(0m, q.Lines[1].ExtraPieces);
        Assert.Equal(8.00m, q.Lines[1].LineTotal);
        Assert.Equal(3.00m, q.CodFee);
        Assert.Equal(0m, q.DispatchFee);
        Assert.Equal(25.00m, q.Total);
        Assert.All(q.Lines, l => Assert.Equal(RateSources.Generic, l.BaseSource));
    }

    [Fact]
    public void QuoteOrder_L1116_contract_rates_total_14_75_instead_of_29()
    {
        var contractRows = new[] { ContractBase(1, 6.50m), ContractExtra(20) };
        var genericRows = new[] { GenericBase(2, 12.00m, pkg: Box), GenericExtra(30, 2.00m) };
        var rows = contractRows.Concat(genericRows).ToList();
        var lines = new[] { new QuoteLine(Std, Box, 8) };

        var withContract = ContractRateResolver.QuoteOrder(rows, Flags(cod: true, codType: "FIXED", codValue: 2.00m), lines, 50m);
        Assert.Equal(6.50m, withContract.Lines[0].BaseRate);
        Assert.Equal(6.25m, withContract.Lines[0].ExtraPieces);
        Assert.Equal(2.00m, withContract.CodFee);
        Assert.Equal(14.75m, withContract.Total);     // 6.50 + 6.25 + 2.00

        // Con los componentes del contrato apagados y COD FIXED 3: 12 + 7 × 2 + 3 = 29.00
        var withGenerics = ContractRateResolver.QuoteOrder(rows, Flags(perService: false, extra: false, cod: true, codType: "FIXED", codValue: 3m), lines, 50m);
        Assert.Equal(29.00m, withGenerics.Total);
        Assert.Equal(RateSources.Generic, withGenerics.Lines[0].BaseSource);
        Assert.Equal(RateSources.Generic, withGenerics.Lines[0].ExtraSource);
    }

    [Fact]
    public void QuoteOrder_rate_component_ids_are_exactly_the_rows_used()
    {
        var rows = new[] { ContractBase(1, 6.50m), ContractExtra(20), GenericBase(2, 12.00m, pkg: Box), GenericExtra(30) };
        var lines = new[] { new QuoteLine(Std, Box, 8) };

        var on = ContractRateResolver.QuoteOrder(rows, Flags(), lines, 0m);
        Assert.Equal(new[] { 1, 20 }, on.RateComponentIds.OrderBy(x => x));

        // Pieza extra apagada: se usa la genérica 30 y NO la 20
        var extraOff = ContractRateResolver.QuoteOrder(rows, Flags(extra: false), lines, 0m);
        Assert.Equal(new[] { 1, 30 }, extraOff.RateComponentIds.OrderBy(x => x));
        Assert.DoesNotContain(20, extraOff.RateComponentIds);

        // Todo apagado: solo genéricas
        var allOff = ContractRateResolver.QuoteOrder(rows, Flags(perService: false, extra: false), lines, 0m);
        Assert.Equal(new[] { 2, 30 }, allOff.RateComponentIds.OrderBy(x => x));
    }

    [Fact]
    public void QuoteOrder_without_current_contract_is_generic_or_none_with_no_order_fees()
    {
        var rows = new[] { GenericBase(2, 12.00m, pkg: Box) };
        var lines = new[] { new QuoteLine(Std, Box, 3), new QuoteLine(Std, Envelope, 1) };

        var q = ContractRateResolver.QuoteOrder(rows, ContractFlags.None, lines, codAmount: 500m);

        Assert.Equal(RateSources.Generic, q.Lines[0].BaseSource);
        Assert.Equal(12.00m, q.Lines[0].BaseRate);
        Assert.Equal(RateSources.None, q.Lines[0].ExtraSource);
        Assert.Equal(0m, q.Lines[0].ExtraPieces);
        Assert.Equal(RateSources.None, q.Lines[1].BaseSource);
        Assert.Null(q.Lines[1].BaseRate);
        Assert.Equal(0m, q.DispatchFee);
        Assert.Equal(0m, q.CodFee);
        Assert.Equal(12.00m, q.Total);
        Assert.Equal(new[] { 2 }, q.RateComponentIds);
    }

    [Fact]
    public void QuoteOrder_dispatch_and_cod_are_charged_once_on_the_first_line()
    {
        var rows = new[] { ContractBase(1, 7.00m, pkg: Box), ContractBase(3, 5.00m, pkg: Envelope) };
        var flags = Flags(dispatch: true, dispatchFee: 3m, cod: true, codType: "PERCENT", codValue: 2.5m);
        var lines = new[] { new QuoteLine(Std, Box, 1), new QuoteLine(Std, Envelope, 1) };

        var q = ContractRateResolver.QuoteOrder(rows, flags, lines, codAmount: 200m);

        Assert.Equal(3m, q.DispatchFee);
        Assert.Equal(5m, q.CodFee);
        Assert.Equal(15.00m, q.Lines[0].LineTotal);   // 7 + 3 + 5
        Assert.Equal(5.00m, q.Lines[1].LineTotal);
        Assert.Equal(20.00m, q.Total);
    }
}
