using Teikem.Domain.Sales;
using Xunit;
using B = Teikem.Tests.ChangeStateBuilder;
using R = Teikem.Domain.Sales.SalesChangeRules;

namespace Teikem.Tests;

/// <summary>
/// Lote 249 (P2) — REVERSA MÍNIMA: se deshacen recolecciones completas, solo las necesarias, primero las recolectadas (de la más reciente a la más vieja) y las
/// empacadas solo si se pidió reabrir; una recolección escogida para una línea cuenta para las demás.
/// </summary>
public sealed class SalesChangeReversalPlanTests
{
    // línea 1: pedido 10 = recolección A (id 1, la vieja) 4 + recolección B (id 2, la nueva) 2 + reservado 4
    private static ChangeOrderState TwoBatches()
        => B.S(new[] { B.L(1, 10, reserved: 4, shipped: 6) }, new[] { B.Collected(1, (1, 4m)), B.Collected(2, (1, 2m)) }, status: "PARTIAL");

    [Fact]
    public void Bajar_a_5_deshace_solo_la_mas_reciente()
    {
        var plan = R.Plan(TwoBatches(), new[] { B.Qty(1, 5m) }, false);
        Assert.False(plan.IsBlocked);
        Assert.Equal(new[] { 2 }, plan.ReverseBatchIds);
    }

    [Fact]
    public void Bajar_a_1_deshace_las_dos_empezando_por_la_mas_reciente()
    {
        var plan = R.Plan(TwoBatches(), new[] { B.Qty(1, 1m) }, false);
        Assert.False(plan.IsBlocked);
        Assert.Equal(new[] { 2, 1 }, plan.ReverseBatchIds);
    }

    [Fact]
    public void Bajar_sin_tocar_lo_despachado_no_deshace_nada()
    {
        var plan = R.Plan(TwoBatches(), new[] { B.Qty(1, 6m) }, false);
        Assert.False(plan.IsBlocked);
        Assert.Empty(plan.ReverseBatchIds);
    }

    [Fact]
    public void Una_recoleccion_escogida_para_una_linea_cubre_la_otra()
    {
        // la recolección 2 lleva 2 de la línea 1 y 3 de la línea 2; la 1 lleva 4 de la línea 1
        var s = B.S(new[] { B.L(1, 10, reserved: 4, shipped: 6), B.L(2, 3, shipped: 3) },
            new[] { B.Collected(1, (1, 4m)), B.Collected(2, (1, 2m), (2, 3m)) }, status: "PARTIAL");
        var plan = R.Plan(s, new[] { B.Qty(1, 5m), B.Qty(2, 1m) }, false);
        Assert.False(plan.IsBlocked);
        Assert.Equal(new[] { 2 }, plan.ReverseBatchIds);

        // directo: con la 2 ya escogida, la línea 2 no necesita otra
        var classified = R.ClassifyAll(s);
        var choice = PickReversalPlan.Choose(s.Lines[1], 2m, classified, false, new HashSet<int> { 2 });
        Assert.Empty(choice.BatchIds);
        Assert.True(choice.Enough);
        Assert.Equal(3m, choice.Covered);
    }

    [Fact]
    public void Las_recolectadas_van_antes_que_las_empacadas_aunque_sean_mas_viejas()
    {
        var s = B.S(new[] { B.L(1, 6, shipped: 6) }, new[] { B.Collected(1, (1, 3m)), B.Packed(5, new[] { (1, 3m) }) }, status: "SHIPPED");
        var choice = PickReversalPlan.Choose(s.Lines[0], 2m, R.ClassifyAll(s), reopenAllowed: true);
        Assert.Equal(new[] { 1 }, choice.BatchIds);
        Assert.True(choice.Enough);
    }

    [Fact]
    public void Las_empacadas_solo_con_reabrir()
    {
        var s = B.S(new[] { B.L(1, 6, shipped: 6) }, new[] { B.Collected(1, (1, 3m)), B.Packed(5, new[] { (1, 3m) }) }, status: "SHIPPED");
        var no = PickReversalPlan.Choose(s.Lines[0], 4m, R.ClassifyAll(s), reopenAllowed: false);
        Assert.Equal(new[] { 1 }, no.BatchIds);
        Assert.False(no.Enough);
        Assert.True(no.CouldCoverWithReopen);

        var yes = PickReversalPlan.Choose(s.Lines[0], 4m, R.ClassifyAll(s), reopenAllowed: true);
        Assert.Equal(new[] { 1, 5 }, yes.BatchIds);
        Assert.True(yes.Enough);
        Assert.False(yes.CouldCoverWithReopen);

        var plan = R.Plan(s, new[] { B.Qty(1, 2m) }, true);
        Assert.False(plan.IsBlocked);
        Assert.Equal(new[] { 1, 5 }, plan.ReverseBatchIds);
        Assert.Equal(new[] { 5 }, plan.ReopenBatchIds);
    }

    [Fact]
    public void Si_no_alcanza_sin_tocar_lo_salido_bloquea()
    {
        // 4 salieron (envío avanzado) y 2 están recolectadas: bajar a 3 tocaría lo salido
        var s = B.S(new[] { B.L(1, 10, reserved: 4, shipped: 6) }, new[] { B.Packed(1, new[] { (1, 4m) }, initial: false), B.Collected(2, (1, 2m)) }, status: "PARTIAL");
        var plan = R.Plan(s, new[] { B.Qty(1, 3m) }, true);
        Assert.Equal(new[] { "No se puede bajar SKU-1 a 3: ya salieron 4." }, plan.Blockers);

        var choice = PickReversalPlan.Choose(s.Lines[0], 3m, R.ClassifyAll(s), true);
        Assert.False(choice.Enough);
        Assert.False(choice.CouldCoverWithReopen);
        Assert.Equal(2m, choice.Covered);
    }

    [Fact]
    public void Quitar_una_linea_recolectada_deshace_sus_recolecciones()
    {
        var s = B.S(new[] { B.L(1, 6, shipped: 6), B.L(2, 2, reserved: 2) }, new[] { B.Collected(1, (1, 4m)), B.Collected(2, (1, 2m)) }, status: "PARTIAL");
        var plan = R.Plan(s, new[] { new ChangeOp(ChangeOpKinds.Remove, LineId: 1) }, false);
        Assert.False(plan.IsBlocked);
        Assert.Equal(new[] { 2, 1 }, plan.ReverseBatchIds);
    }
}
