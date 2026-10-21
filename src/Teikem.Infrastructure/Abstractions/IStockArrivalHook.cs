namespace Teikem.Infrastructure.Abstractions;

/// <summary>
/// Lote 194 — algo que debe revisarse cuando entra mercancía al inventario por un recibo confirmado (completo o por tandas): hoy, reservar el pendiente (backorder) de los avisos de salida.
/// Se llama DESPUÉS de confirmar el recibo y nunca puede romperlo: quien lo implementa atrapa sus propios errores.
/// </summary>
public interface IStockArrivalHook
{
    Task OnStockArrivedAsync(CancellationToken ct);
}
