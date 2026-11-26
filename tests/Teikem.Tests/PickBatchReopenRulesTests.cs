using Teikem.Domain.Constants;
using Teikem.Domain.Wms;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 249 — reglas puras de «Reabrir el empaque» (PACKED → COLLECTED) y de «Unir al envío de la orden»: mensajes exactos (manual y FAQ) y el orden de las guardas.
/// </summary>
public sealed class PickBatchReopenRulesTests
{
    private static ReopenPackCheck Ok(string number = "EMP-00031") => new(number, "ORD-01234", true, true, "Capturada");

    [Fact]
    public void Exact_messages()
    {
        Assert.Equal("Indique el motivo para reabrir el empaque.", PickBatchRules.ReopenReasonRequired);
        Assert.Equal("Reabrir el empaque exige el permiso warehouse.reopen-pack.", PickBatchRules.ReopenPermission);
        Assert.Equal("La recolección EMP-1 no está empacada.", PickBatchRules.ReopenNotPacked("EMP-1"));
        Assert.Equal("El envío ORD-9 ya está «En ruta»: el empaque EMP-1 no se puede reabrir.", PickBatchRules.ReopenBlocked("EMP-1", "ORD-9", "En ruta"));
        Assert.Equal("El flete del envío ORD-9 ya está facturado: el empaque EMP-1 no se reabre.", PickBatchRules.ReopenFreightInvoiced("EMP-1", "ORD-9"));
        Assert.Equal("El empaque EMP-1 tiene productos ya facturados (FAC-00012): no se reabre. Para añadir, use una orden interna de adición.",
            PickBatchRules.ReopenInvoiced("EMP-1", "FAC-00012"));
        Assert.Equal("La recolección EMP-1 no es de la misma orden ni de una adición de ella: no se une a ese envío.", PickGroupRules.JoinNotFamily("EMP-1"));
        Assert.Equal("La recolección EMP-1 no está recolectada (está «Empacada»).", PickGroupRules.JoinNotCollected("EMP-1", "Empacada"));
        Assert.Equal("La recolección EMP-1 ya está en la maestra EMP-7.", PickGroupRules.JoinAlreadyInGroup("EMP-1", "EMP-7"));
        Assert.Equal("Escoja con qué recolección de OV-00040 sale (hay varias).", PickGroupRules.JoinChooseTarget("OV-00040"));
        Assert.Equal("El envío de EMP-1 ya salió («En ruta»): la recolección sale en su propio envío.", PickGroupRules.JoinTargetLeft("EMP-1", "En ruta"));
        Assert.Equal("EMP-1 ya está empacada: marque «Reabrir el empaque» (permiso warehouse.reopen-pack) para unir esta recolección a su envío.",
            PickGroupRules.JoinNeedsReopen("EMP-1"));
    }

    [Theory]
    [InlineData(null, PickBatchRules.ReopenReasonRequired)]
    [InlineData("   ", PickBatchRules.ReopenReasonRequired)]
    public void Reason_is_required(string? reason, string error)
        => Assert.Equal((null, error), PickBatchRules.NormalizeReopenReason(reason));

    [Fact]
    public void Reason_is_trimmed_and_limited_to_500()
    {
        Assert.Equal(("Cliente pidió 5 más", (string?)null), PickBatchRules.NormalizeReopenReason("  Cliente pidió 5 más "));
        Assert.Equal(PickBatchRules.ReopenReasonTooLong, PickBatchRules.NormalizeReopenReason(new string('x', 501)).Error);
        Assert.Null(PickBatchRules.NormalizeReopenReason(new string('x', 500)).Error);
    }

    [Fact]
    public void History_comment_fits_in_500_characters()
    {
        Assert.Equal("Empaque reabierto: Falta una caja (envío ORD-01234 anulado)", PickBatchRules.ReopenHistoryComment("Falta una caja", "ORD-01234"));
        var long500 = PickBatchRules.ReopenHistoryComment(new string('x', 500), "ORD-01234");
        Assert.Equal(500, long500.Length);
        Assert.StartsWith("Empaque reabierto: xxx", long500);
        Assert.EndsWith("… (envío ORD-01234 anulado)", long500);
    }

    [Fact]
    public void Batch_guard_needs_a_packed_loose_batch()
    {
        Assert.Null(PickBatchRules.ReopenBatchError("EMP-1", PickBatchStatuses.Packed, false, null));
        Assert.Equal(PickBatchRules.ReopenNotPacked("EMP-1"), PickBatchRules.ReopenBatchError("EMP-1", PickBatchStatuses.Collected, false, null));
        Assert.Equal(PickBatchRules.ReopenNotPacked("EMP-1"), PickBatchRules.ReopenBatchError("EMP-1", PickBatchStatuses.Cancelled, false, null));
        Assert.Equal(PickBatchRules.ReopenNotPacked("DMA-1"), PickBatchRules.ReopenBatchError("DMA-1", PickBatchStatuses.Packed, true, null));   // un despacho manual nunca está empacado
        Assert.Equal(PickBatchRules.ReopenUseGroup("EMP-1", "EMP-7"), PickBatchRules.ReopenBatchError("EMP-1", PickBatchStatuses.Packed, false, "EMP-7"));
    }

    [Fact]
    public void Shipment_guards_in_order()
    {
        Assert.Null(PickBatchRules.ReopenShipmentError(Ok()));
        Assert.Equal(PickBatchRules.ReopenLinked("EMP-2"), PickBatchRules.ReopenShipmentError(Ok() with { LinkedBatchNumber = "EMP-2", ShipmentInitial = false }));
        Assert.Equal(PickBatchRules.ReopenBlocked("EMP-00031", "ORD-01234", "En ruta"),
            PickBatchRules.ReopenShipmentError(Ok() with { ShipmentInitial = false, ShipmentStatusLabel = "En ruta", FreightInvoiced = true }));
        Assert.Equal(PickBatchRules.ReopenBlocked("EMP-00031", "ORD-01234", "Capturada"), PickBatchRules.ReopenShipmentError(Ok() with { ShipmentActive = false }));
        Assert.Equal(PickBatchRules.ReopenBlocked("EMP-00031", "—", "—"), PickBatchRules.ReopenShipmentError(new ReopenPackCheck("EMP-00031", null, false, false, null)));
        Assert.Equal(PickBatchRules.ReopenFreightInvoiced("EMP-00031", "ORD-01234"), PickBatchRules.ReopenShipmentError(Ok() with { FreightInvoiced = true, HasInvoicedProduct = true }));
        Assert.Equal(PickBatchRules.ReopenFreightInvoiced("EMP-00031", "ORD-01234"), PickBatchRules.ReopenShipmentError(Ok() with { ReturnFeeInvoiced = true }));
        Assert.Equal(PickBatchRules.ReopenFreightInvoiced("EMP-00031", "ORD-01234"), PickBatchRules.ReopenShipmentError(Ok() with { HasLiveInvoiceLine = true }));
        Assert.Equal(PickBatchRules.ReopenInvoiceProcessed("EMP-00031", "ORD-01234"), PickBatchRules.ReopenShipmentError(Ok() with { InvoiceProcessed = true }));
        Assert.Equal(PickBatchRules.ReopenInvoiced("EMP-00031", "FAC-00012, FAC-00015"),
            PickBatchRules.ReopenShipmentError(Ok() with { HasInvoicedProduct = true, ProductInvoiceNumbers = new[] { "FAC-00012", "FAC-00015" } }));
        Assert.Equal(PickBatchRules.ReopenInvoiced("EMP-00031", "—"), PickBatchRules.ReopenShipmentError(Ok() with { HasInvoicedProduct = true }));
    }

    [Fact]
    public void Can_reopen_combines_both_guards()
    {
        Assert.True(PickBatchRules.CanReopenPack("EMP-1", PickBatchStatuses.Packed, false, null, Ok("EMP-1")));
        Assert.False(PickBatchRules.CanReopenPack("EMP-1", PickBatchStatuses.Collected, false, null, Ok("EMP-1")));
        Assert.False(PickBatchRules.CanReopenPack("EMP-1", PickBatchStatuses.Packed, false, null, Ok("EMP-1") with { HasInvoicedProduct = true }));
    }

    [Fact]
    public void Family_root_is_the_parent_or_the_order_itself()
    {
        Assert.Equal(40, PickGroupRules.RootOf(40, null));
        Assert.Equal(40, PickGroupRules.RootOf(45, 40));
    }
}
