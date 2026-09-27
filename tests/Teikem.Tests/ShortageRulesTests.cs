using Teikem.Domain.Constants;
using Teikem.Domain.Wms;
using Xunit;

namespace Teikem.Tests;

/// <summary>Lote 6 (P8) — reglas puras de faltantes de compra: pendiente, acciones, mensajes y costo pendiente.</summary>
public class ShortageRulesTests
{
    [Fact]
    public void Pending_is_ordered_minus_received_minus_resolved_clamped()
    {
        Assert.Equal(2m, ShortageRules.Pending(10m, 8m, 0m));
        Assert.Equal(1m, ShortageRules.Pending(10m, 8m, 1m));
        Assert.Equal(0m, ShortageRules.Pending(10m, 12m, 0m));
        Assert.Equal(0m, ShortageRules.Pending(10m, 8m, 3m));
    }

    [Theory]
    [InlineData(ShortageActions.Close)]
    [InlineData(ShortageActions.Reorder)]
    public void Close_and_reorder_resolve_the_whole_pending(string action)
    {
        var v = ShortageRules.Validate(action, null, 2m);
        Assert.True(v.IsValid);
        Assert.Equal(2m, v.Quantity);
        Assert.Equal(2m, ShortageRules.Validate(action.ToLowerInvariant(), 2m, 2m).Quantity);
        var partial = ShortageRules.Validate(action, 1m, 2m);
        Assert.False(partial.IsValid);
        Assert.Equal(ShortageRules.FullOnly(2m), partial.Error);
    }

    [Fact]
    public void Manual_admits_partial_up_to_pending()
    {
        Assert.Equal(1m, ShortageRules.Validate(ShortageActions.ManualAdjustment, 1m, 2m).Quantity);
        var exceeds = ShortageRules.Validate(ShortageActions.ManualAdjustment, 3m, 2m);
        Assert.Equal("La cantidad del ajuste excede el faltante pendiente (2).", exceeds.Error);
        Assert.False(exceeds.IsConflict);
        Assert.Equal(ShortageRules.QuantityRequired, ShortageRules.Validate(ShortageActions.ManualAdjustment, null, 2m).Error);
        Assert.Equal(ShortageRules.QuantityPositive, ShortageRules.Validate(ShortageActions.ManualAdjustment, 0m, 2m).Error);
        Assert.Equal(ShortageRules.QuantityDecimals, ShortageRules.Validate(ShortageActions.ManualAdjustment, 0.0001m, 2m).Error);
        Assert.Equal("La cantidad del ajuste excede el faltante pendiente (1.5).", ShortageRules.ManualExceeds(1.5m));
    }

    [Fact]
    public void No_pending_is_a_conflict()
    {
        var v = ShortageRules.Validate(ShortageActions.Close, null, 0m);
        Assert.Equal("La línea ya no tiene faltante pendiente.", v.Error);
        Assert.True(v.IsConflict);
    }

    [Fact]
    public void Unknown_action()
    {
        Assert.Null(ShortageRules.NormalizeAction("FOO"));
        Assert.Null(ShortageRules.NormalizeAction(null));
        var v = ShortageRules.Validate("FOO", 1m, 2m);
        Assert.Equal("Acción desconocida: use CLOSE, REORDER o MANUAL_ADJUSTMENT.", v.Error);
        Assert.False(v.IsConflict);
    }

    [Fact]
    public void Resolvable_status()
    {
        Assert.Equal("La orden de compra está cancelada; su faltante ya no se resuelve.",
            ShortageRules.ResolvableError(PurchaseOrderStatuses.Cancelled, hasConfirmedReceipt: true));
        Assert.Equal("La orden de compra todavía no tiene recepciones confirmadas.",
            ShortageRules.ResolvableError(PurchaseOrderStatuses.Sent, hasConfirmedReceipt: false));
        Assert.Null(ShortageRules.ResolvableError(PurchaseOrderStatuses.Partial, hasConfirmedReceipt: true));
    }

    [Fact]
    public void Manual_reasons()
    {
        Assert.Equal(AdjustmentReasons.PoShortage, ShortageRules.NormalizeManualReason(null));
        Assert.Equal(AdjustmentReasons.Found, ShortageRules.NormalizeManualReason(" found "));
        Assert.Null(ShortageRules.NormalizeManualReason(AdjustmentReasons.Damage));
    }

    [Fact]
    public void Pending_cost_round4()
    {
        Assert.Equal(24.6912m, ShortageRules.PendingCost(2m, 12.3456m));
        Assert.Equal(0.0004m, ShortageRules.PendingCost(0.001m, 0.35m));
    }
}
