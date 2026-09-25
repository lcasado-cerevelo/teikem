using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Orders;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 3 (P4): implementación de la costura IClientBalanceProvider mientras no existe Facturación (DECISIÓN 8).
/// "Saldo pendiente" = Σ QuotedAmount de las órdenes activas del cliente que están en curso: estatus no inicial y no
/// terminal (CONFIRMED…ARRIVED y laterales). Excluye DRAFT (sin cotizar), DELIVERED y CANCELLED (una orden cancelada
/// deja de consumir crédito) y la orden que se está confirmando (excludeOrderId). Facturación registrará otra
/// implementación (facturas impagas) sin tocar Órdenes. Siempre bajo el filtro global de tenant.
/// </summary>
public sealed class QuotedOrdersBalanceProvider(TeikemDbContext db) : IClientBalanceProvider
{
    public async Task<decimal> PendingBalanceAsync(int clientId, int? excludeOrderId, CancellationToken ct)
    {
        var sum = await db.TransportOrders.AsNoTracking()
            .Where(o => o.ClientId == clientId && o.IsActive && o.QuotedAmount != null
                        && (excludeOrderId == null || o.TransportOrderId != excludeOrderId)
                        && !o.Status!.IsInitial
                        && o.Status.StageKind!.InternalCode != StageKinds.Terminal)
            .SumAsync(o => o.QuotedAmount, ct);
        return sum ?? 0m;
    }
}
