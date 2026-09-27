using Teikem.Domain.Constants;
using Teikem.Domain.Wms;
using Xunit;

namespace Teikem.Tests;

/// <summary>Lote 6 (P8) — reglas puras de la orden de compra: cancelar, eliminar, líneas, guardas de edición y totales.</summary>
public class PurchaseOrderRulesTests
{
    [Theory]
    [InlineData(PurchaseOrderStatuses.Draft)]
    [InlineData(PurchaseOrderStatuses.Sent)]
    [InlineData(PurchaseOrderStatuses.Partial)]
    public void Cancel_is_allowed_from_draft_sent_and_partial(string status)
    {
        Assert.True(PurchaseOrderRules.CanCancel(status));
        Assert.Null(PurchaseOrderRules.CancelError(status));
    }

    [Fact]
    public void Received_is_not_cancelled()
    {
        Assert.False(PurchaseOrderRules.CanCancel(PurchaseOrderStatuses.Received));
        Assert.Equal("Una orden de compra recibida completa no se cancela.", PurchaseOrderRules.CancelError(PurchaseOrderStatuses.Received));
        Assert.Equal(PurchaseOrderRules.AlreadyCancelled, PurchaseOrderRules.CancelError(PurchaseOrderStatuses.Cancelled));
    }

    [Fact]
    public void Open_receipt_message_is_exact()
        => Assert.Equal("La orden de compra tiene un recibo abierto; confírmelo o elimínelo antes de cancelar.", PurchaseOrderRules.HasOpenReceipt);

    [Theory]
    [InlineData(PurchaseOrderStatuses.Draft)]
    [InlineData(PurchaseOrderStatuses.Sent)]
    [InlineData(PurchaseOrderStatuses.Cancelled)]
    public void Deletable_without_receipts(string status)
        => Assert.True(PurchaseOrderRules.IsDeletable(status, hasReceipts: false, hasOpenReceipt: false));

    [Fact]
    public void Not_deletable_with_receipts()
    {
        Assert.Equal("Una orden de compra con recepciones no se elimina; cancélela.",
            PurchaseOrderRules.DeleteError(PurchaseOrderStatuses.Sent, hasReceipts: true, hasOpenReceipt: false));
        Assert.Equal(PurchaseOrderRules.DeleteWithReceipts,
            PurchaseOrderRules.DeleteError(PurchaseOrderStatuses.Partial, hasReceipts: true, hasOpenReceipt: false));
        Assert.Equal("Una orden de compra cancelada con recepciones se conserva con su bitácora; no se elimina.",
            PurchaseOrderRules.DeleteError(PurchaseOrderStatuses.Cancelled, hasReceipts: true, hasOpenReceipt: false));
        Assert.Equal(PurchaseOrderRules.DeleteReceived,
            PurchaseOrderRules.DeleteError(PurchaseOrderStatuses.Received, hasReceipts: true, hasOpenReceipt: false));
    }

    [Fact]
    public void Not_deletable_with_open_receipt()
    {
        Assert.False(PurchaseOrderRules.IsDeletable(PurchaseOrderStatuses.Sent, hasReceipts: false, hasOpenReceipt: true));
        Assert.Equal(PurchaseOrderRules.HasOpenReceiptDelete,
            PurchaseOrderRules.DeleteError(PurchaseOrderStatuses.Sent, hasReceipts: false, hasOpenReceipt: true));
    }

    [Theory]
    [InlineData(PurchaseOrderStatuses.Sent, true)]
    [InlineData(PurchaseOrderStatuses.Partial, true)]
    [InlineData(PurchaseOrderStatuses.Draft, false)]
    [InlineData(PurchaseOrderStatuses.Received, false)]
    [InlineData(PurchaseOrderStatuses.Cancelled, false)]
    public void Receivable_only_sent_or_partial(string status, bool expected)
        => Assert.Equal(expected, PurchaseOrderRules.IsReceivable(status));

    [Fact]
    public void Target_status_after_receipt_or_resolution()
    {
        Assert.Equal(PurchaseOrderStatuses.Partial, PurchaseOrderRules.TargetStatus(new[] { (10m, 8m, 0m), (5m, 5m, 0m) }));
        Assert.Equal(PurchaseOrderStatuses.Received, PurchaseOrderRules.TargetStatus(new[] { (10m, 8m, 2m), (5m, 5m, 0m) }));
        Assert.Equal(PurchaseOrderStatuses.Received, PurchaseOrderRules.TargetStatus(new[] { (10m, 12m, 0m) }));
    }

    [Fact]
    public void Path_is_stepwise_from_sent_to_received()
    {
        Assert.Equal(new[] { PurchaseOrderStatuses.Partial, PurchaseOrderStatuses.Received },
            PurchaseOrderRules.Path(PurchaseOrderStatuses.Sent, PurchaseOrderStatuses.Received));
        Assert.Equal(new[] { PurchaseOrderStatuses.Received },
            PurchaseOrderRules.Path(PurchaseOrderStatuses.Sent, PurchaseOrderStatuses.Received, partialEnabled: false));
        Assert.Equal(new[] { PurchaseOrderStatuses.Received },
            PurchaseOrderRules.Path(PurchaseOrderStatuses.Partial, PurchaseOrderStatuses.Received));
        Assert.Empty(PurchaseOrderRules.Path(PurchaseOrderStatuses.Partial, PurchaseOrderStatuses.Partial));
    }

    private static PurchaseOrderLineInput Line(int id, string sku, decimal? qty, decimal? cost = null, decimal? defaultCost = 2.5m,
        bool own = true, bool serial = false)
        => new(id, sku, own, serial, qty, cost, defaultCost);

    [Fact]
    public void Valid_lines_take_default_cost()
    {
        var (lines, errors) = PurchaseOrderRules.ValidateLines(new[] { Line(1, "PN", 10m, 12.3456m), Line(2, "PL", 5m) });
        Assert.Empty(errors);
        Assert.Equal(12.3456m, lines[0].UnitCost);
        Assert.Equal(2.5m, lines[1].UnitCost);
    }

    [Fact]
    public void Line_rules_with_exact_messages()
    {
        var (_, errors) = PurchaseOrderRules.ValidateLines(new[]
        {
            Line(1, "P3", 1m, own: false),
            Line(2, "PN", 0m),
            Line(3, "PC", 1m, cost: null, defaultCost: null),
            Line(2, "PN", 1m),
            Line(4, "PS", 1.5m, serial: true),
            Line(5, "PX", 1.2345m),
            Line(6, "PY", 1m, cost: -1m),
        });
        Assert.Contains("La orden de compra solo admite productos propios; P3 pertenece a un cliente.", errors["lines[0]"]);
        Assert.Contains(PurchaseOrderRules.QtyRequired, errors["lines[1]"]);
        Assert.Contains("Indique el costo unitario de PC.", errors["lines[2]"]);
        Assert.Contains("El producto PN está repetido en la orden de compra.", errors["lines[3]"]);
        Assert.Contains(PurchaseOrderRules.SerialQtyInteger("PS"), errors["lines[4]"]);
        Assert.Contains(PurchaseOrderRules.QtyDecimals, errors["lines[5]"]);
        Assert.Contains(PurchaseOrderRules.CostNegative, errors["lines[6]"]);
    }

    [Fact]
    public void Lines_required_and_capped_at_200()
    {
        Assert.Contains(PurchaseOrderRules.LinesRequired, PurchaseOrderRules.ValidateLines(Array.Empty<PurchaseOrderLineInput>()).Errors["lines"]);
        var many = Enumerable.Range(1, 201).Select(i => Line(i, "P" + i, 1m)).ToList();
        Assert.Contains(PurchaseOrderRules.TooManyLines, PurchaseOrderRules.ValidateLines(many).Errors["lines"]);
        var max = Enumerable.Range(1, 200).Select(i => Line(i, "P" + i, 1m)).ToList();
        Assert.Empty(PurchaseOrderRules.ValidateLines(max).Errors);
    }

    [Fact]
    public void Received_line_is_locked()
    {
        var existing = new[]
        {
            new PurchaseOrderExistingLine(11, 1, "PN", 8m, 12.5m, IsReferenced: true),
            new PurchaseOrderExistingLine(12, 2, "PL", 0m, 2.5m, IsReferenced: false),
            new PurchaseOrderExistingLine(13, 3, "PZ", 0m, 1m, IsReferenced: true),
        };
        const string message = "La línea de PN ya tiene recepciones: no se elimina, no baja de lo recibido (8) y su costo no cambia.";
        Assert.Equal(message, PurchaseOrderRules.ReceivedLineLocked("PN", 8m));

        // Baja por debajo de lo recibido o cambio de costo → bloqueado.
        var (_, lower) = PurchaseOrderRules.PlanReplacement(existing, new[] { new PurchaseOrderLinePlan(1, 7m, 12.5m), new PurchaseOrderLinePlan(3, 1m, 1m) });
        Assert.Contains(message, lower["lines[0]"]);
        var (_, cost) = PurchaseOrderRules.PlanReplacement(existing, new[] { new PurchaseOrderLinePlan(1, 9m, 13m), new PurchaseOrderLinePlan(3, 1m, 1m) });
        Assert.Contains(message, cost["lines[0]"]);

        // Eliminar la línea recibida → bloqueado; la referenciada por un aviso tampoco se elimina.
        var (_, removed) = PurchaseOrderRules.PlanReplacement(existing, new[] { new PurchaseOrderLinePlan(2, 1m, 2.5m) });
        Assert.Contains(message, removed["lines"]);
        Assert.Contains(PurchaseOrderRules.LineInUse("PZ"), removed["lines"]);

        // Subir la recibida, quitar la libre y agregar una nueva → válido.
        var (changes, ok) = PurchaseOrderRules.PlanReplacement(existing,
            new[] { new PurchaseOrderLinePlan(1, 12m, 12.5m), new PurchaseOrderLinePlan(3, 2m, 1m), new PurchaseOrderLinePlan(4, 1m, 1m) });
        Assert.Empty(ok);
        Assert.Equal(new[] { 11, 13 }, changes.Updates.Select(u => u.PurchaseOrderLineId).OrderBy(x => x));
        Assert.Equal(12, Assert.Single(changes.Removals));
        Assert.Equal(4, Assert.Single(changes.Additions).ProductId);
    }

    [Fact]
    public void Totals_round4()
    {
        Assert.Equal(123.456m, PurchaseOrderRules.LineAmount(10m, 12.3456m));
        Assert.Equal(0.0001m, PurchaseOrderRules.Round4(0.00005m));
        Assert.Equal(135.956m, PurchaseOrderRules.Total(new[] { (10m, 12.3456m), (5m, 2.5m) }));
        Assert.Equal(0.3333m, PurchaseOrderRules.Total(new[] { (0.001m, 333.3333m) }));
    }

    [Fact]
    public void Cost_validation()
    {
        Assert.Null(PurchaseOrderRules.ValidateCost(0m));
        Assert.Equal(PurchaseOrderRules.CostDecimals, PurchaseOrderRules.ValidateCost(1.23456m));
        Assert.Equal("El costo unitario no puede ser negativo.", PurchaseOrderRules.ValidateCost(-0.01m));
    }
}
