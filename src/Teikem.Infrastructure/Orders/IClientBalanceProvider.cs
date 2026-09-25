namespace Teikem.Infrastructure.Orders;

/// <summary>
/// Costura del "saldo pendiente" del cliente para el chequeo de crédito (R18, DECISIÓN 8). Hoy la implementa
/// QuotedOrdersBalanceProvider (Σ QuotedAmount de las órdenes activas en curso: no iniciales y no terminales, excluyendo
/// la orden que se está confirmando). Facturación registrará otra implementación (facturas impagas) en DI sin tocar Órdenes.
/// </summary>
public interface IClientBalanceProvider
{
    /// <summary>Saldo pendiente del cliente del tenant activo, sin contar excludeOrderId (la orden que se confirma). 0 si no hay.</summary>
    Task<decimal> PendingBalanceAsync(int clientId, int? excludeOrderId, CancellationToken ct);
}
