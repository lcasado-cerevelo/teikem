namespace Teikem.Infrastructure.Abstractions;

/// <summary>Una línea de recolección que se acaba de revertir y que atendía una línea de orden de venta (Lote 62).</summary>
public sealed record SalesReversedLine(int SalesOrderLineId, int ProductId, int BinId, int? LotId, decimal Quantity);

/// <summary>
/// Lote 62 — gancho que llama PickBatchService al ELIMINAR (revertir) una recolección que atendía una orden de venta, dentro de la misma transacción y DESPUÉS de
/// devolver la existencia a su posición: baja lo despachado de la línea y vuelve a reservar si la orden no está cancelada. Lo implementa SalesOrderService.
/// </summary>
public interface ISalesDispatchReversal
{
    Task OnPickBatchReversedAsync(int salesOrderId, string pickBatchNumber, IReadOnlyList<SalesReversedLine> lines, CancellationToken ct);
}
