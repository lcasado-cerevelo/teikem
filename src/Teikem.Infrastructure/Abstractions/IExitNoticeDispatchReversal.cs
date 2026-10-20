namespace Teikem.Infrastructure.Abstractions;

/// <summary>Una línea de recolección que se acaba de revertir y que atendía una línea de aviso de salida (Lote 143).</summary>
public sealed record ExitNoticeReversedLine(int ExitNoticeLineId, int ProductId, int BinId, int? LotId, decimal Quantity);

/// <summary>
/// Lote 143 — gancho que llama PickBatchService al ELIMINAR (revertir) una recolección que atendía un aviso de salida, dentro de la misma transacción y DESPUÉS de devolver la
/// existencia a su posición: baja lo despachado de la línea y vuelve a reservar si el aviso no está cancelado. Lo implementa ExitNoticeService (igual que el de ventas).
/// </summary>
public interface IExitNoticeDispatchReversal
{
    Task OnPickBatchReversedAsync(int exitNoticeId, string pickBatchNumber, IReadOnlyList<ExitNoticeReversedLine> lines, CancellationToken ct);
}
