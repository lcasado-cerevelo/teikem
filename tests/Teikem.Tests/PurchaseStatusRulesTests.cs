using Teikem.Domain.Constants;
using Teikem.Domain.Wms;
using Xunit;

namespace Teikem.Tests;

/// <summary>Lote 6 / P0 — estatus de la orden de compra frente a la recepción y los faltantes.</summary>
public class PurchaseStatusRulesTests
{
    [Fact]
    public void Pending_is_clamped_at_zero()
    {
        Assert.Equal(3m, PurchaseStatusRules.Pending(10m, 5m, 2m));
        Assert.Equal(0m, PurchaseStatusRules.Pending(10m, 12m, 0m));
    }

    [Fact]
    public void Receivable_only_sent_or_partial_with_exact_messages()
    {
        Assert.True(PurchaseStatusRules.IsReceivable(PurchaseOrderStatuses.Sent));
        Assert.True(PurchaseStatusRules.IsReceivable(PurchaseOrderStatuses.Partial));
        Assert.False(PurchaseStatusRules.IsReceivable(PurchaseOrderStatuses.Draft));
        Assert.False(PurchaseStatusRules.IsReceivable(PurchaseOrderStatuses.Cancelled));
        Assert.Equal("La orden de compra debe estar enviada o recibida parcial para recibir contra ella.", PurchaseStatusRules.NotReceivable);
        Assert.Equal("La orden de compra no tiene cantidades pendientes de recibir.", PurchaseStatusRules.NothingPending);
    }

    [Fact]
    public void Status_after_receipt_and_resolution()
    {
        Assert.Equal(PurchaseOrderStatuses.Partial, PurchaseStatusRules.StatusAfterReceipt(PurchaseOrderStatuses.Sent, new[] { (10m, 8m, 0m), (5m, 5m, 0m) }));
        Assert.Equal(PurchaseOrderStatuses.Received, PurchaseStatusRules.StatusAfterReceipt(PurchaseOrderStatuses.Partial, new[] { (10m, 10m, 0m), (5m, 5m, 0m) }));
        Assert.Equal(PurchaseOrderStatuses.Sent, PurchaseStatusRules.StatusAfterReceipt(PurchaseOrderStatuses.Sent, new[] { (10m, 0m, 0m) }));
        Assert.Equal(PurchaseOrderStatuses.Received, PurchaseStatusRules.StatusAfterResolution(PurchaseOrderStatuses.Partial, new[] { (10m, 8m, 2m) }));
        Assert.Equal(PurchaseOrderStatuses.Partial, PurchaseStatusRules.StatusAfterResolution(PurchaseOrderStatuses.Partial, new[] { (10m, 8m, 1m) }));
        Assert.Equal(PurchaseOrderStatuses.Cancelled, PurchaseStatusRules.StatusAfterResolution(PurchaseOrderStatuses.Cancelled, new[] { (10m, 8m, 2m) }));
    }
}
