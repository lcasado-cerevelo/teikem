using Microsoft.Extensions.Logging;
using Teikem.Infrastructure.Abstractions;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 194 — al confirmar un recibo (o una tanda) se revisa de inmediato si hay pendiente (backorder) de avisos de salida que ya se puede reservar, sin esperar al trabajo
/// EXIT_NOTICE_BACKORDER_SCAN (cada 10 minutos, que sigue como red de seguridad). Un fallo aquí se registra y se ignora: el recibo ya quedó confirmado.
/// </summary>
public sealed class ExitNoticeBackorderHook(ExitNoticeService notices, ILogger<ExitNoticeBackorderHook> logger) : IStockArrivalHook
{
    public async Task OnStockArrivedAsync(CancellationToken ct)
    {
        try
        {
            await notices.ReserveBackordersAsync(ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogWarning(e, "No se pudo reservar el pendiente de los avisos de salida después del recibo; lo hará el trabajo programado.");
        }
    }
}
