using Teikem.Domain.Sales;
using Xunit;
using I = Teikem.Domain.Sales.SalesLineInvariants;
using K = Teikem.Domain.Sales.SalesLineInvariantRules;

namespace Teikem.Tests;

/// <summary>
/// Lote 249 (P2) — verificador de invariantes: reservado + backorder + despachado = pedido, despachado ≥ facturado + devuelto, cabecera = suma de las líneas, y
/// solo cuenta lo que el cambio INTRODUCE (las incoherencias heredadas no impiden modificar).
/// </summary>
public sealed class SalesLineInvariantsTests
{
    private static InvariantLine L(int id, decimal ordered, decimal reserved = 0m, decimal back = 0m, decimal shipped = 0m, decimal invoiced = 0m,
        decimal returned = 0m, bool closed = false, decimal price = 10m, decimal tax = 0m)
        => new(id, id, ordered, reserved, back, shipped, invoiced, returned, closed, price, tax);

    [Fact]
    public void Reservado_mas_backorder_mas_despachado_igual_a_pedido()
    {
        Assert.Empty(I.Violations(L(1, 10, reserved: 4, back: 2, shipped: 4)));
        var v = Assert.Single(I.Violations(L(1, 10, reserved: 4, shipped: 4)));
        Assert.Equal(K.Balance, v.Rule);
        // cerrada con faltante: sin backorder y lo demás ≤ pedido
        Assert.Empty(I.Violations(L(1, 10, reserved: 1, shipped: 4, closed: true)));
        Assert.Equal(K.Balance, Assert.Single(I.Violations(L(1, 10, back: 1, shipped: 4, closed: true))).Rule);
        // quitada: pedido 0, cerrada y todo en 0
        Assert.Empty(I.Violations(L(1, 0, closed: true)));
        Assert.Equal(K.Balance, Assert.Single(I.Violations(L(1, 0, reserved: 1, closed: true))).Rule);
        Assert.Contains(I.Violations(L(1, 0)), x => x.Rule == K.ZeroOpen);
        Assert.Contains(I.Violations(L(1, 5, reserved: -1, shipped: 6)), x => x.Rule == K.NonNegative);
    }

    [Fact]
    public void Despachado_cubre_facturado_mas_devuelto_salvo_con_ON_REQUEST()
    {
        Assert.Empty(I.Violations(L(1, 10, shipped: 10, invoiced: 8, returned: 2)));
        Assert.Equal(K.ShippedCoversInvoiced, Assert.Single(I.Violations(L(1, 10, reserved: 4, shipped: 6, invoiced: 6, returned: 1))).Rule);
        // ON_REQUEST: lo facturado puede ser mayor que lo despachado
        Assert.Empty(I.Violations(L(1, 10, reserved: 10, invoiced: 10), shippedCoversInvoiced: false));
        Assert.NotEmpty(I.Violations(L(1, 10, reserved: 10, invoiced: 10)));
    }

    [Fact]
    public void Cabecera_igual_a_la_suma_de_las_lineas()
    {
        var lines = new[] { L(1, 3, reserved: 3, price: 8m, tax: 2.76m), L(2, 2, reserved: 2, price: 1.25m, tax: 0.29m) };
        Assert.Empty(I.HeaderViolations(new InvariantOrder(26.50m, 3.05m, lines)));
        var bad = I.HeaderViolations(new InvariantOrder(30m, 3.10m, lines));
        Assert.Equal(new[] { K.HeaderSubtotal, K.HeaderTax }, bad.Select(v => v.Rule));
    }

    [Fact]
    public void No_marca_incoherencias_heredadas_que_el_cambio_no_toco()
    {
        // la línea 1 ya estaba incoherente (reservado de más) y sigue igual; la 2 estaba bien y se rompe
        var before = new[] { L(1, 10, reserved: 11), L(2, 5, reserved: 5) };
        var after = new[] { L(1, 10, reserved: 11), L(2, 5, reserved: 3) };
        var v = Assert.Single(I.Check(before, after));
        Assert.Equal((2, K.Balance), (v.LineNo, v.Rule));
        Assert.Equal("La modificación dejaría la orden incoherente (línea 2): no se aplicó.", I.IncoherentMessage(v.LineNo));

        // la heredada que se arregla no cuenta; una regla NUEVA en la línea heredada sí
        Assert.Empty(I.Check(before, new[] { L(1, 10, reserved: 10), L(2, 5, reserved: 5) }));
        var mixed = I.Check(before, new[] { L(1, 10, reserved: 11, invoiced: 1), L(2, 5, reserved: 5) });
        Assert.Equal(K.ShippedCoversInvoiced, Assert.Single(mixed).Rule);
    }

    [Fact]
    public void Una_linea_nueva_tiene_que_nacer_coherente()
    {
        var before = new[] { L(1, 5, reserved: 5) };
        Assert.Empty(I.Check(before, new[] { L(1, 5, reserved: 5), L(0, 3, reserved: 2, back: 1) }));
        Assert.Single(I.Check(before, new[] { L(1, 5, reserved: 5), L(0, 3, reserved: 2) }));
    }

    [Fact]
    public void La_cabecera_heredada_no_cuenta_pero_la_rota_por_el_cambio_si()
    {
        var okBefore = new InvariantOrder(50m, 0m, new[] { L(1, 5, reserved: 5) });
        var badAfter = new InvariantOrder(50m, 0m, new[] { L(1, 3, reserved: 3) });
        Assert.Equal(K.HeaderSubtotal, Assert.Single(I.Check(okBefore, badAfter)).Rule);
        var wrongBefore = new InvariantOrder(99m, 0m, new[] { L(1, 5, reserved: 5) });
        Assert.Empty(I.Check(wrongBefore, badAfter));
    }

    [Fact]
    public void Desde_la_entidad()
    {
        var o = new SalesOrder { Subtotal = 24m, TaxAmount = 2.76m };
        o.Lines.Add(new SalesOrderLine { SalesOrderLineId = 7, LineNo = 1, QtyOrdered = 3m, QtyReserved = 3m, UnitPrice = 8m, TaxAmount = 2.76m });
        var inv = InvariantOrder.From(o);
        Assert.Empty(I.Check(inv, inv));
        Assert.Empty(I.HeaderViolations(inv));
        Assert.Equal(7, inv.Lines[0].LineId);
    }
}
