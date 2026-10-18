using Teikem.Domain.Constants;

namespace Teikem.Domain.Trips;

/// <summary>
/// Lote 5 (P0) — banderas de despacho de una orden para indicadores y fuentes de datos (DECISIÓN V5):
/// - 'En excepción' = FAILED, el intento fallido (CANCELLED no cuenta);
/// - 'Pendiente de despacho' = SUBMITTED, PICKUP, UNASSIGNED o ASSIGNED (base de 'Órdenes sin chofer asignado').
/// </summary>
public static class OrderDispatchFlags
{
    public static readonly IReadOnlyList<string> ExceptionStatuses = new[] { OrderStatuses.Failed };

    public static readonly IReadOnlyList<string> PendingDispatchStatuses = new[]
    {
        OrderStatuses.Submitted, OrderStatuses.Pickup, OrderStatuses.Unassigned, OrderStatuses.Assigned,
    };

    public static bool IsException(string? statusCode)
        => ExceptionStatuses.Contains(statusCode ?? string.Empty, StringComparer.OrdinalIgnoreCase);

    public static bool IsPendingDispatch(string? statusCode)
        => PendingDispatchStatuses.Contains(statusCode ?? string.Empty, StringComparer.OrdinalIgnoreCase);
}
