using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Clients;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Orders;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Wms;

/// <summary>
/// Lote 184 — «recibir juntos» desde el aparato: los recibos que llegan con la misma llave (GroupKey, la del camión que armó el operador) entran a la misma recepción maestra, que nace con el primero.
/// Cada documento sigue su recibo de siempre; aquí solo se liga el recibo a la maestra y se cuida que sea del mismo dueño y almacén. Lo usan ReceiptService (al abrir el recibo) y, por él, la cola del aparato.
/// </summary>
internal static class ReceiptGroupJoin
{
    /// <summary>Liga el recibo (rastreado) a la maestra de la llave; la crea si no existe. El llamador guarda el recibo. El contador RECEIPTGROUP debe haberse asegurado antes de la transacción externa.</summary>
    public static async Task JoinAsync(TeikemDbContext db, INumberSequenceService numbers, ITenantContext tenant, Guid key, ReceiptHeader receipt, int? ownerClientId, CancellationToken ct)
    {
        var group = await db.Set<ReceiptGroup>().AsTracking().FirstOrDefaultAsync(g => g.ClientKey == key, ct);
        if (group is null)
        {
            var seq = await numbers.NextAsync(ReceiptGroupCodes.NumberKind, null, ct);
            group = new ReceiptGroup
            {
                PublicId = Guid.NewGuid(), TenantId = ((TenantContext)tenant).RequireTenantId(), WarehouseId = receipt.WarehouseId, ClientId = ownerClientId, ClientKey = key,
                Number = NumberFormat.Resolve(ReceiptGroupCodes.NumberPattern, seq), CreatedAtUtc = DateTime.UtcNow, CreatedBy = tenant.UserId, IsActive = true,
            };
            db.Set<ReceiptGroup>().Add(group);
            await db.SaveGuardedAsync(ReceiptGroupRules.NumberTaken, ct);
        }
        else
        {
            if (!group.IsActive) throw new ConflictException(ReceiptGroupRules.GroupGone);
            if (group.ClientId != ownerClientId) throw new ValidationException("groupKey", ReceiptGroupRules.JoinOtherOwner(group.Number));
            if (group.WarehouseId != receipt.WarehouseId) throw new ValidationException("groupKey", ReceiptGroupRules.JoinOtherWarehouse(group.Number));
        }
        receipt.ReceiptGroupId = group.ReceiptGroupId;
    }
}
