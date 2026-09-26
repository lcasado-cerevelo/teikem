using Teikem.Domain.Constants;

namespace Teikem.Domain.Trips;

/// <summary>
/// Lote 5 (P0) — banderas de despacho de una orden para indicadores y fuentes de datos (DECISIÓN V5):
/// - 'En excepción' = ON_HOLD, PARTIAL o FAILED (CANCELLED no cuenta);
/// - 'Pendiente de despacho' = CONFIRMED, PICKUP, INBOUND o PLANNED (base de 'Órdenes sin chofer asignado').
/// </summary>
public static class OrderDispatchFlags
{
    public static readonly IReadOnlyList<string> ExceptionStatuses = new[] { OrderStatuses.OnHold, OrderStatuses.Partial, OrderStatuses.Failed };

    public static readonly IReadOnlyList<string> PendingDispatchStatuses = new[]
    {
        OrderStatuses.Confirmed, OrderStatuses.Pickup, OrderStatuses.Inbound, OrderStatuses.Planned,
    };

    public static bool IsException(string? statusCode)
        => ExceptionStatuses.Contains(statusCode ?? string.Empty, StringComparer.OrdinalIgnoreCase);

    public static bool IsPendingDispatch(string? statusCode)
        => PendingDispatchStatuses.Contains(statusCode ?? string.Empty, StringComparer.OrdinalIgnoreCase);
}
