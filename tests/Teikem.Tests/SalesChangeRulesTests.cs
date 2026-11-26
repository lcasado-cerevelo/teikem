using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Common;
using Teikem.Domain.Sales;
using Teikem.Domain.Tax;
using Xunit;
using R = Teikem.Domain.Sales.SalesChangeRules;

namespace Teikem.Tests;

/// <summary>Lote 249 — utilidades para armar el estado de una orden en las pruebas de las modificaciones.</summary>
internal static class ChangeStateBuilder
{
    public static ChangeLineState L(int id, decimal ordered, decimal reserved = 0m, decimal back = 0m, decimal shipped = 0m, decimal invoiced = 0m,
        bool closed = false, decimal price = 10m, string? sku = null, int? product = null, bool kit = false, string? taxReason = null, decimal returned = 0m)
        => new(id, id, product ?? 100 + id, sku ?? $"SKU-{id}", ordered, reserved, back, shipped, invoiced, returned, closed, price, kit, taxReason);

    public static Dictionary<int, decimal> U(params (int Line, decimal Units)[] units) => units.ToDictionary(x => x.Line, x => x.Units);

    public static ChangeBatchState Collected(int id, params (int Line, decimal Units)[] units)
        => new(id, $"EMP-{id:00000}", "COLLECTED", U(units));

    public static ChangeBatchState Packed(int id, (int Line, decimal Units)[] units, bool active = true, bool initial = true, bool freightInvoiced = false,
        int? group = null, string? groupNumber = null, string? statusLabel = null)
        => new(id, $"EMP-{id:00000}", "PACKED", U(units), GroupId: group, GroupNumber: groupNumber, GroupPacked: group is not null, ShipmentId: 900 + id,
            ShipmentNumber: $"ORD-{id:00000}", ShipmentActive: active, ShipmentInitial: initial, ShipmentStatusLabel: statusLabel, ShipmentInvoiced: freightInvoiced);

    public static ChangeOrderState S(ChangeLineState[] lines, ChangeBatchState[]? batches = null, string status = "REQUESTED", string mode = "DELIVERY", bool shippingInvoiced = false)
        => new(status, mode, shippingInvoiced, lines, batches ?? Array.Empty<ChangeBatchState>());

    public static ChangeOp Qty(int line, decimal q) => new(ChangeOpKinds.Qty, LineId: line, Quantity: q);
}

/// <summary>
/// Lote 249 (P2) — reglas puras de la modificación: etapa de las recolecciones, facturada, la tabla de etapas del plan (regla por regla) y la posición por línea,
/// con los mensajes exactos que citan el manual y la FAQ.
/// </summary>
public sealed class SalesChangeRulesTests
{
    private static ChangeLineState L(int id, decimal ordered, decimal reserved = 0m, decimal back = 0m, decimal shipped = 0m, decimal invoiced = 0m,
        bool closed = false, decimal price = 10m, string? sku = null, int? product = null, bool kit = false, string? taxReason = null)
        => ChangeStateBuilder.L(id, ordered, reserved, back, shipped, invoiced, closed, price, sku, product, kit, taxReason);

    private static ChangeOrderState S(ChangeLineState[] lines, ChangeBatchState[]? batches = null, string status = "REQUESTED", string mode = "DELIVERY", bool shippingInvoiced = false)
        => ChangeStateBuilder.S(lines, batches, status, mode, shippingInvoiced);

    // ---------------------------------------------------------------- etapa de las recolecciones

    [Fact]
    public void ClassifyBatch_COLLECTED_en_entrega_es_reversible()
    {
        var s = S(new[] { L(1, 5, shipped: 5) }, new[] { ChangeStateBuilder.Collected(1, (1, 5m)) });
        var c = R.ClassifyBatch(s, s.Batches[0]);
        Assert.Equal(ChangeBatchStages.Reversible, c.Stage);
        Assert.Null(c.Reason);
    }

    [Fact]
    public void ClassifyBatch_COLLECTED_de_un_recogido_ya_salio()
    {
        var s = S(new[] { L(1, 5, shipped: 5) }, new[] { ChangeStateBuilder.Collected(1, (1, 5m)) }, mode: "PICKUP");
        var c = R.ClassifyBatch(s, s.Batches[0]);
        Assert.Equal(ChangeBatchStages.Left, c.Stage);
        Assert.Equal("Es un recogido: lo recolectado ya se lo llevó el cliente.", c.Reason);
    }

    [Fact]
    public void ClassifyBatch_compania_enlazada_y_despacho_manual_ya_salieron()
    {
        var linked = ChangeStateBuilder.Collected(1, (1, 5m)) with { TenantLinked = true };
        var manual = ChangeStateBuilder.Collected(2, (1, 5m)) with { IsManual = true };
        var s = S(new[] { L(1, 10, shipped: 10) }, new[] { linked, manual });
        Assert.Equal((ChangeBatchStages.Left, R.ReasonTenantLinked), (R.ClassifyBatch(s, linked).Stage, R.ClassifyBatch(s, linked).Reason));
        Assert.Equal(ChangeBatchStages.Left, R.ClassifyBatch(s, manual).Stage);
        // la compañía enlazada manda también sobre un empaque reabrible
        var linkedPacked = ChangeStateBuilder.Packed(3, new[] { (1, 5m) }) with { TenantLinked = true };
        Assert.Equal(ChangeBatchStages.Left, R.ClassifyBatch(s, linkedPacked).Stage);
    }

    [Fact]
    public void ClassifyBatch_PACKED_con_envio_inicial_y_sin_facturar_es_reabrible()
    {
        var s = S(new[] { L(1, 5, shipped: 5) }, new[] { ChangeStateBuilder.Packed(1, new[] { (1, 5m) }) });
        Assert.Equal(ChangeBatchStages.Reopenable, R.ClassifyBatch(s, s.Batches[0]).Stage);
    }

    [Theory]
    [InlineData(false, true, false, "El envío ORD-00001 está anulado.")]
    [InlineData(true, false, false, "El envío ORD-00001 ya está «En ruta».")]
    [InlineData(true, true, true, "El flete del envío ORD-00001 ya está facturado.")]
    public void ClassifyBatch_PACKED_con_envio_inactivo_avanzado_o_flete_facturado_ya_salio(bool active, bool initial, bool freight, string reason)
    {
        var b = ChangeStateBuilder.Packed(1, new[] { (1, 5m) }, active, initial, freight, statusLabel: "En ruta");
        var s = S(new[] { L(1, 5, shipped: 5) }, new[] { b });
        var c = R.ClassifyBatch(s, b);
        Assert.Equal(ChangeBatchStages.Left, c.Stage);
        Assert.Equal(reason, c.Reason);
    }

    [Fact]
    public void ClassifyBatch_PACKED_con_producto_facturado_ya_salio()
    {
        var b = ChangeStateBuilder.Packed(1, new[] { (1, 5m) });
        var s = S(new[] { L(1, 5, shipped: 5, invoiced: 5) }, new[] { b });
        var c = R.ClassifyBatch(s, b);
        Assert.Equal(ChangeBatchStages.Left, c.Stage);
        Assert.Equal("Tiene productos ya facturados.", c.Reason);
    }

    [Fact]
    public void ClassifyBatch_miembro_COLLECTED_de_una_maestra_empacada_sigue_a_la_maestra()
    {
        var b = ChangeStateBuilder.Packed(1, new[] { (1, 5m) }, group: 7, groupNumber: "EMP-00070") with { StatusCode = "COLLECTED" };
        var s = S(new[] { L(1, 5, shipped: 5) }, new[] { b });
        Assert.Equal(ChangeBatchStages.Reopenable, R.ClassifyBatch(s, b).Stage);
        Assert.Equal("EMP-00070", b.PackNumber);
    }

    // ---------------------------------------------------------------- facturada

    [Theory]
    [InlineData("QTY")]
    [InlineData("PRICE")]
    [InlineData("TAX")]
    [InlineData("TERMS")]
    [InlineData("NOTES")]
    [InlineData("ADD")]
    [InlineData("REMOVE")]
    public void Facturada_bloquea_toda_operacion_aunque_solo_este_recolectada(string kind)
    {
        // ON_DISPATCH: se facturó al recolectar; la recolección sigue COLLECTED
        var s = S(new[] { L(1, 10, shipped: 4, reserved: 6, invoiced: 4), L(2, 3, reserved: 3) }, new[] { ChangeStateBuilder.Collected(1, (1, 4m)) }, status: "PARTIAL");
        var op = new ChangeOp(kind, LineId: 2, ProductId: 555, Quantity: 2m, UnitPrice: 9m, TaxPct: 1m, Note: "x");
        var plan = R.Plan(s, new[] { op }, reopenRequested: true);
        Assert.Equal(new[] { "La orden ya está facturada: solo se le puede añadir. Quitar o bajar se hace con una nota de crédito." }, plan.Blockers);
        Assert.True(R.IsBilled(s));
    }

    [Fact]
    public void Facturada_tambien_por_el_envio_facturado()
    {
        var s = S(new[] { L(1, 10, reserved: 10) }, shippingInvoiced: true);
        Assert.True(R.IsBilled(s));
        Assert.Equal(R.Billed, Assert.Single(R.Plan(s, new[] { ChangeStateBuilder.Qty(1, 5m) }, false).Blockers));
        Assert.False(R.IsBilled(S(new[] { L(1, 10, reserved: 10) })));
    }

    // ---------------------------------------------------------------- bajar

    [Fact]
    public void Bajar_por_debajo_de_lo_salido_bloquea_con_SKU_N_y_M()
    {
        var left = ChangeStateBuilder.Packed(1, new[] { (1, 6m) }, initial: false);
        var s = S(new[] { L(1, 10, reserved: 4, shipped: 6) }, new[] { left }, status: "PARTIAL");
        var plan = R.Plan(s, new[] { ChangeStateBuilder.Qty(1, 4m) }, reopenRequested: true);
        Assert.Equal(new[] { "No se puede bajar SKU-1 a 4: ya salieron 6." }, plan.Blockers);
        Assert.Empty(plan.ReverseBatchIds);
        // bajar hasta lo salido sí se puede: solo libera reserva
        var ok = R.Plan(s, new[] { ChangeStateBuilder.Qty(1, 6m) }, false);
        Assert.False(ok.IsBlocked);
        Assert.Empty(ok.ReverseBatchIds);
    }

    [Fact]
    public void Quitar_una_linea_con_algo_salido_bloquea()
    {
        var s = S(new[] { L(1, 10, shipped: 10), L(2, 2, reserved: 2) }, new[] { ChangeStateBuilder.Collected(1, (1, 10m)) }, status: "PARTIAL", mode: "PICKUP");
        var plan = R.Plan(s, new[] { new ChangeOp(ChangeOpKinds.Remove, LineId: 1) }, true);
        Assert.Equal(new[] { "No se puede quitar SKU-1: ya salieron 10." }, plan.Blockers);
    }

    [Fact]
    public void Bajar_dentro_de_un_empaque_sin_reabrir_bloquea_y_con_reabrir_lo_reabre_y_lo_deshace()
    {
        var s = S(new[] { L(1, 10, reserved: 4, shipped: 6) }, new[] { ChangeStateBuilder.Packed(3, new[] { (1, 6m) }) }, status: "PARTIAL");
        var no = R.Plan(s, new[] { ChangeStateBuilder.Qty(1, 2m) }, reopenRequested: false);
        Assert.Equal(new[] { "SKU-1 está en el empaque EMP-00003: para cambiarla marque «Reabrir el empaque» (permiso warehouse.reopen-pack)." }, no.Blockers);
        Assert.True(no.NeedsReopen);

        var yes = R.Plan(s, new[] { ChangeStateBuilder.Qty(1, 2m) }, reopenRequested: true);
        Assert.False(yes.IsBlocked);
        Assert.Equal(new[] { 3 }, yes.ReopenBatchIds);
        Assert.Equal(new[] { 3 }, yes.ReverseBatchIds);
        Assert.Empty(yes.ReopenGroupIds);
    }

    [Fact]
    public void Bajar_dentro_de_una_maestra_empacada_reabre_la_maestra()
    {
        var b = ChangeStateBuilder.Packed(3, new[] { (1, 6m) }, group: 40, groupNumber: "EMP-00040");
        var s = S(new[] { L(1, 10, reserved: 4, shipped: 6) }, new[] { b }, status: "PARTIAL");
        var no = R.Plan(s, new[] { ChangeStateBuilder.Qty(1, 2m) }, false);
        Assert.Equal("SKU-1 está en el empaque EMP-00040: para cambiarla marque «Reabrir el empaque» (permiso warehouse.reopen-pack).", Assert.Single(no.Blockers));
        var yes = R.Plan(s, new[] { ChangeStateBuilder.Qty(1, 2m) }, true);
        Assert.Equal(new[] { 40 }, yes.ReopenGroupIds);
        Assert.Empty(yes.ReopenBatchIds);
        Assert.Equal(new[] { 3 }, yes.ReverseBatchIds);
    }

    [Fact]
    public void Subir_la_cantidad_de_una_linea_empacada_tambien_exige_reabrir_pero_no_deshace()
    {
        var s = S(new[] { L(1, 6, shipped: 6) }, new[] { ChangeStateBuilder.Packed(3, new[] { (1, 6m) }) }, status: "SHIPPED");
        Assert.True(R.Plan(s, new[] { ChangeStateBuilder.Qty(1, 8m) }, false).IsBlocked);
        var yes = R.Plan(s, new[] { ChangeStateBuilder.Qty(1, 8m) }, true);
        Assert.False(yes.IsBlocked);
        Assert.Equal(new[] { 3 }, yes.ReopenBatchIds);
        Assert.Empty(yes.ReverseBatchIds);
    }

    [Fact]
    public void Bajar_lo_recolectado_escoge_la_reversa_sin_reabrir()
    {
        var s = S(new[] { L(1, 10, reserved: 4, shipped: 6) }, new[] { ChangeStateBuilder.Collected(1, (1, 4m)), ChangeStateBuilder.Collected(2, (1, 2m)) }, status: "PARTIAL");
        var plan = R.Plan(s, new[] { ChangeStateBuilder.Qty(1, 5m) }, false);
        Assert.False(plan.IsBlocked);
        Assert.Equal(new[] { 2 }, plan.ReverseBatchIds);
        Assert.False(plan.NeedsReopen);
    }

    // ---------------------------------------------------------------- precio, IVU y términos

    [Fact]
    public void Precio_o_IVU_de_una_linea_con_unidades_salidas_bloquea()
    {
        var s = S(new[] { L(1, 10, reserved: 4, shipped: 6) }, new[] { ChangeStateBuilder.Packed(1, new[] { (1, 6m) }, initial: false) }, status: "PARTIAL");
        Assert.Equal(new[] { "El precio de SKU-1 ya no se cambia: parte de la línea ya salió." },
            R.Plan(s, new[] { new ChangeOp(ChangeOpKinds.Price, LineId: 1, UnitPrice: 9m) }, true).Blockers);
        Assert.Equal(new[] { "El IVU de SKU-1 ya no se cambia: parte de la línea ya salió." },
            R.Plan(s, new[] { new ChangeOp(ChangeOpKinds.Tax, LineId: 1, TaxPct: 1m, Note: "exento") }, true).Blockers);
    }

    [Fact]
    public void Precio_de_una_linea_recolectada_se_cambia_y_se_anota()
    {
        var s = S(new[] { L(1, 10, reserved: 4, shipped: 6) }, new[] { ChangeStateBuilder.Collected(1, (1, 6m)) }, status: "PARTIAL");
        var plan = R.Plan(s, new[] { new ChangeOp(ChangeOpKinds.Price, LineId: 1, UnitPrice: 9m) }, false);
        Assert.False(plan.IsBlocked);
        Assert.Equal(new[] { 1 }, plan.PriceChangedLineIds);
        Assert.Empty(plan.ReverseBatchIds);
        // el mismo precio no es un cambio
        Assert.Empty(R.Plan(s, new[] { new ChangeOp(ChangeOpKinds.Price, LineId: 1, UnitPrice: 10m) }, false).PriceChangedLineIds);
    }

    [Fact]
    public void IVU_manual_y_quitarlo_exigen_el_permiso_de_impuesto()
    {
        var s = S(new[] { L(1, 10, reserved: 10), L(2, 3, reserved: 3, taxReason: TaxReasons.ManualOverride) });
        var plan = R.Plan(s, new[] { new ChangeOp(ChangeOpKinds.Tax, LineId: 1, TaxPct: 1m, Note: "x"), new ChangeOp(ChangeOpKinds.ClearTax, LineId: 2) }, false);
        Assert.False(plan.IsBlocked);
        Assert.True(plan.NeedsTaxPermission);
        Assert.Equal("La línea 1 (SKU-1) no tiene IVU manual.", Assert.Single(R.Plan(s, new[] { new ChangeOp(ChangeOpKinds.ClearTax, LineId: 1) }, false).Blockers));
        Assert.Equal(R.TaxPctRange, Assert.Single(R.Plan(s, new[] { new ChangeOp(ChangeOpKinds.Tax, LineId: 1, TaxPct: 101m) }, false).Blockers));
    }

    [Fact]
    public void Terminos_con_empaque_exigen_reabrir_y_con_algo_salido_bloquean()
    {
        var packed = S(new[] { L(1, 6, shipped: 6) }, new[] { ChangeStateBuilder.Packed(5, new[] { (1, 6m) }) }, status: "SHIPPED");
        var terms = new[] { new ChangeOp(ChangeOpKinds.Terms) };
        Assert.Equal(new[] { "La orden está en el empaque EMP-00005: para cambiar el pago, la entrega o el consignatario marque «Reabrir el empaque» (permiso warehouse.reopen-pack)." },
            R.Plan(packed, terms, false).Blockers);
        var yes = R.Plan(packed, terms, true);
        Assert.False(yes.IsBlocked);
        Assert.Equal(new[] { 5 }, yes.ReopenBatchIds);
        Assert.Empty(yes.ReverseBatchIds);

        var left = S(new[] { L(1, 10, reserved: 4, shipped: 6) }, new[] { ChangeStateBuilder.Packed(5, new[] { (1, 6m) }, initial: false) }, status: "PARTIAL");
        Assert.Equal(new[] { "Ya salió parte de la orden: el pago, la entrega y el consignatario ya no se cambian." }, R.Plan(left, terms, true).Blockers);

        // sin recolecciones los términos y las notas se cambian sin más
        var free = S(new[] { L(1, 10, reserved: 10) });
        Assert.False(R.Plan(free, new[] { new ChangeOp(ChangeOpKinds.Terms), new ChangeOp(ChangeOpKinds.Notes) }, false).IsBlocked);
    }

    // ---------------------------------------------------------------- agregar y quitar

    [Fact]
    public void Agregar_no_toca_las_recolecciones_y_en_una_orden_que_salio_completa_bloquea()
    {
        var collected = S(new[] { L(1, 6, shipped: 6) }, new[] { ChangeStateBuilder.Collected(1, (1, 6m)) }, status: "SHIPPED");
        var add = new[] { new ChangeOp(ChangeOpKinds.Add, ProductId: 999, Quantity: 2m, Sku: "NEW") };
        var ok = R.Plan(collected, add, false);
        Assert.False(ok.IsBlocked);
        Assert.Empty(ok.ReverseBatchIds);
        Assert.False(ok.NeedsReopen);

        var gone = S(new[] { L(1, 6, shipped: 6) }, new[] { ChangeStateBuilder.Packed(1, new[] { (1, 6m) }, initial: false) }, status: "SHIPPED");
        Assert.Equal(new[] { "La orden ya salió completa: solo se le puede añadir con una orden interna de adición." }, R.Plan(gone, add, true).Blockers);
        Assert.True(R.LeftComplete(gone));
        Assert.False(R.LeftComplete(collected));
    }

    [Fact]
    public void Quitar_todas_las_lineas_bloquea()
    {
        var s = S(new[] { L(1, 5, reserved: 5), L(2, 3, reserved: 3), L(3, 0, closed: true) });
        var plan = R.Plan(s, new[] { new ChangeOp(ChangeOpKinds.Remove, LineId: 1), new ChangeOp(ChangeOpKinds.Remove, LineId: 2) }, false);
        Assert.Equal(new[] { "No se pueden quitar todas las líneas: para eso cancele la orden." }, plan.Blockers);
        // quitar todas y agregar otra sí se puede
        var swap = R.Plan(s, new[] { new ChangeOp(ChangeOpKinds.Remove, LineId: 1), new ChangeOp(ChangeOpKinds.Remove, LineId: 2),
            new ChangeOp(ChangeOpKinds.Add, ProductId: 777, Quantity: 1m) }, false);
        Assert.False(swap.IsBlocked);
    }

    [Theory]
    [InlineData("DRAFT", "La orden está en borrador: se cambia desde su edición, no con una modificación.")]
    [InlineData("CANCELLED", "La orden está cancelada: no se puede modificar.")]
    public void Borrador_y_cancelada_bloquean(string status, string message)
    {
        var s = S(new[] { L(1, 5, reserved: 5) }, status: status);
        Assert.Equal(new[] { message }, R.Plan(s, new[] { ChangeStateBuilder.Qty(1, 3m) }, false).Blockers);
    }

    [Fact]
    public void Producto_repetido_bloquea_y_un_producto_quitado_se_reactiva()
    {
        var s = S(new[] { L(1, 5, reserved: 5, product: 501, sku: "AAA"), L(2, 0, closed: true, product: 502, sku: "BBB") });
        Assert.Equal(new[] { "El producto AAA ya está en la orden: cambie la cantidad de su línea." },
            R.Plan(s, new[] { new ChangeOp(ChangeOpKinds.Add, ProductId: 501, Quantity: 1m) }, false).Blockers);
        Assert.Equal(new[] { "El producto CCC ya está en la orden: cambie la cantidad de su línea." },
            R.Plan(s, new[] { new ChangeOp(ChangeOpKinds.Add, ProductId: 503, Quantity: 1m, Sku: "CCC"), new ChangeOp(ChangeOpKinds.Add, ProductId: 503, Quantity: 2m, Sku: "CCC") }, false).Blockers);

        var re = R.Plan(s, new[] { new ChangeOp(ChangeOpKinds.Add, ProductId: 502, Quantity: 4m) }, false);
        Assert.False(re.IsBlocked);
        Assert.Equal(new[] { 2 }, re.Reactivated);
        Assert.Equal(2, R.ReactivationLine(s, 502)!.LineNo);
        Assert.Null(R.ReactivationLine(s, 501));
        // una línea quitada no se cambia por su id
        Assert.Equal(new[] { "La línea 2 (BBB) está quitada: para volver a pedirla, agréguela de nuevo." }, R.Plan(s, new[] { ChangeStateBuilder.Qty(2, 3m) }, false).Blockers);
    }

    [Fact]
    public void Otros_bloqueos_de_las_operaciones()
    {
        var s = S(new[] { L(1, 5, reserved: 5), L(2, 4, reserved: 0, shipped: 2, closed: true, kit: true) }, new[] { ChangeStateBuilder.Collected(1, (2, 2m)) }, status: "PARTIAL");
        Assert.Equal(new[] { R.NothingToChange }, R.Plan(s, Array.Empty<ChangeOp>(), false).Blockers);
        Assert.Equal(new[] { R.OpKindInvalid }, R.Plan(s, new[] { new ChangeOp("DELETE", LineId: 1) }, false).Blockers);
        Assert.Equal(new[] { "La línea 77 no es de esta orden." }, R.Plan(s, new[] { ChangeStateBuilder.Qty(77, 1m) }, false).Blockers);
        Assert.Equal(new[] { "La línea 1 (SKU-1) viene más de una vez en la modificación." },
            R.Plan(s, new[] { ChangeStateBuilder.Qty(1, 3m), ChangeStateBuilder.Qty(1, 4m) }, false).Blockers);
        Assert.Equal(new[] { "La línea 2 (SKU-2) está cerrada: su cantidad ya no se cambia." }, R.Plan(s, new[] { ChangeStateBuilder.Qty(2, 3m) }, false).Blockers);
        Assert.Equal(new[] { "El kit SKU-2 se pide en piezas enteras." }, R.Plan(s, new[] { ChangeStateBuilder.Qty(2, 2.5m) }, false).Blockers);
        Assert.Equal(new[] { Teikem.Domain.Sales.SalesRules.QuantityRequired }, R.Plan(s, new[] { ChangeStateBuilder.Qty(1, 0m) }, false).Blockers);
        Assert.Equal(new[] { R.AddProductRequired }, R.Plan(s, new[] { new ChangeOp(ChangeOpKinds.Add, Quantity: 1m) }, false).Blockers);
        Assert.Equal(new[] { R.TermsTwice }, R.Plan(s, new[] { new ChangeOp(ChangeOpKinds.Terms), new ChangeOp(ChangeOpKinds.Terms) }, false).Blockers);
    }

    // ---------------------------------------------------------------- posición por línea

    [Fact]
    public void LinePositions_calcula_minimos_y_bloqueos()
    {
        // línea 1: pedido 10 = 2 salidas (envío avanzado) + 3 empacadas (reabribles) + 1 recolectada + 4 reservadas
        var s = S(new[] { L(1, 10, reserved: 4, shipped: 6), L(2, 0, closed: true, sku: "Q") }, new[]
        {
            ChangeStateBuilder.Packed(1, new[] { (1, 2m) }, initial: false),
            ChangeStateBuilder.Packed(2, new[] { (1, 3m) }),
            ChangeStateBuilder.Collected(3, (1, 1m)),
        }, status: "PARTIAL");
        var pos = R.LinePositions(s);
        var p = pos[0];
        Assert.Equal((2m, 3m, 1m), (p.Out, p.InPacked, p.InCollected));
        Assert.Equal(5m, p.MinQuantity);
        Assert.Equal(2m, p.MinQuantityWithReopen);
        Assert.Null(p.LockedReason);
        Assert.Equal("El precio de SKU-1 ya no se cambia: parte de la línea ya salió.", p.PriceLockedReason);
        Assert.True(pos[1].IsRemoved);
        Assert.Equal("La línea 2 (Q) está quitada: para volver a pedirla, agréguela de nuevo.", pos[1].PriceLockedReason);

        // facturada: todas las líneas llevan el bloqueo de la orden
        var billed = S(new[] { L(1, 10, reserved: 4, shipped: 6, invoiced: 6) }, new[] { ChangeStateBuilder.Collected(1, (1, 6m)) }, status: "PARTIAL");
        var bp = Assert.Single(R.LinePositions(billed));
        Assert.Equal(R.Billed, bp.LockedReason);
        Assert.Equal(6m, bp.Invoiced);
        Assert.Equal(0m, bp.MinQuantity);   // lo recolectado sigue siendo deshacible: el bloqueo lo da la factura

        // sin recolecciones: el mínimo es 0
        var free = Assert.Single(R.LinePositions(S(new[] { L(1, 5, reserved: 5) })));
        Assert.Equal((0m, 0m, null as string), (free.MinQuantity, free.MinQuantityWithReopen, free.LockedReason));
    }

    [Fact]
    public void Lo_despachado_sin_recoleccion_viva_cuenta_como_salido()
    {
        var s = S(new[] { L(1, 10, reserved: 4, shipped: 6) }, new[] { ChangeStateBuilder.Collected(1, (1, 2m)) }, status: "PARTIAL");
        var p = Assert.Single(R.LinePositions(s));
        Assert.Equal((4m, 2m), (p.Out, p.InCollected));
    }
}

/// <summary>Lote 249 (P2) — mapeo EF de la modificación: tabla, índices filtrados y bitácora sin las fotos.</summary>
public sealed class SalesOrderChangeModelTests
{
    [Fact]
    public void La_modificacion_se_mapea_a_su_tabla_con_sus_indices()
    {
        using var db = TenantIsolationModelTests.CreateSqlServerModelContext();
        var e = db.Model.FindEntityType(typeof(SalesOrderChange))!;
        Assert.Equal("SalesOrderChange", e.GetTableName());
        Assert.NotNull(e.GetQueryFilter());
        var names = e.GetIndexes().Select(i => i.GetDatabaseName()).ToHashSet();
        foreach (var n in new[] { "UX_SalesOrderChange_PublicId", "UQ_SalesOrderChange_No", "UX_SalesOrderChange_Pending", "IX_SalesOrderChange_PendingTenant",
                     "IX_SalesOrderChange_Registered", "UX_SalesOrderChange_Attachment", "IX_SalesOrderChange_Addition" })
            Assert.Contains(n, names);
        var pending = e.GetIndexes().Single(i => i.GetDatabaseName() == "UX_SalesOrderChange_Pending");
        Assert.True(pending.IsUnique);
        Assert.Equal("[IsPending] = 1", pending.GetFilter());
        Assert.Equal("char(64)", e.FindProperty(nameof(SalesOrderChange.BaseFingerprint))!.GetColumnType());
        Assert.True(e.FindProperty(nameof(SalesOrderChange.RowVersion))!.IsConcurrencyToken);
    }

    [Fact]
    public void La_bitacora_no_copia_las_fotos()
    {
        Assert.Equal("SALES_ORDER_CHANGE", typeof(SalesOrderChange).GetCustomAttribute<AuditEntityAttribute>()!.EntityTypeCode);
        foreach (var p in new[] { "RequestJson", "BeforeJson", "AfterJson", "EffectsJson", "PublicId", "IsPending" })
            Assert.NotNull(typeof(SalesOrderChange).GetProperty(p)!.GetCustomAttribute<NotAuditedAttribute>());
        Assert.Null(typeof(SalesOrderChange).GetProperty("Reason")!.GetCustomAttribute<NotAuditedAttribute>());
        Assert.Equal("MANUAL_OVERRIDE", TaxReasons.ManualOverride);
    }
}
