using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Constants;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Wms;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 27 (Rentas, plan 4.1) — posición EN-RENTA de un almacén, creada a demanda: la zona RENT ('En renta', ZoneType RENTAL) y
/// su posición EN-RENTA. Ahí vive la existencia rentada (en mano y reservada; serie ON_RENT); StockAllocator, PickBatchRules,
/// ProductService, PutawayRules y ReceivingModeRules excluyen la zona.
/// - Corre dentro de la transacción del despacho, después de bloquear la renta: bloquea el almacén (orden del lote Rental &lt;
///   Warehouse &lt; saldos &lt; series) para que dos despachos simultáneos no creen la zona dos veces.
/// - Usa la primera zona RENTAL del almacén (activa primero); si no hay, crea RENT. Si el código RENT o EN-RENTA ya lo usa otra
///   zona u otra posición del almacén → 409 con el mensaje de RentalRules (hay que cambiarle el código). Una posición EN-RENTA
///   desactivada a mano no se reactiva: el ledger responde 422 al despachar.
/// - Zona y posición son hijas del almacén filtrado (sin TenantId); se auditan bajo WAREHOUSE como las que crea una persona.
/// </summary>
public sealed class RentalBinResolver(TeikemDbContext db, ILookupCache lookups)
{
    public async Task<int> ResolveAsync(int warehouseId, CancellationToken ct)
    {
        var warehouse = await db.LockWarehouseAsync(warehouseId, ct);
        var rentalTypeId = await lookups.GetIdAsync(LookupDomains.ZoneType, ZoneTypes.Rental, ct);

        var zones = await db.WarehouseZones.Where(z => z.WarehouseId == warehouseId).ToListAsync(ct);
        var zone = zones.Where(z => z.ZoneTypeLookupId == rentalTypeId)
            .OrderByDescending(z => z.IsActive).ThenBy(z => z.WarehouseZoneId).FirstOrDefault();
        if (zone is null)
        {
            if (zones.Any(z => string.Equals(z.Code, RentalRules.RentalZoneCode, StringComparison.OrdinalIgnoreCase)))
                throw new ConflictException(RentalRules.RentalZoneCodeTaken(warehouse.Code));
            zone = new WarehouseZone
            {
                WarehouseId = warehouseId, Code = RentalRules.RentalZoneCode, Name = RentalRules.RentalZoneName,
                ZoneTypeLookupId = rentalTypeId, IsActive = true,
            };
            db.WarehouseZones.Add(zone);
            await db.SaveChangesAsync(ct);
        }

        var bin = await db.WarehouseBins.FirstOrDefaultAsync(b => b.WarehouseId == warehouseId && b.Code == RentalRules.RentalBinCode, ct);
        if (bin is null)
        {
            bin = new WarehouseBin
            {
                WarehouseZoneId = zone.WarehouseZoneId, WarehouseId = warehouseId, Code = RentalRules.RentalBinCode, IsActive = true,
            };
            db.WarehouseBins.Add(bin);
            await db.SaveChangesAsync(ct);
        }
        else if (bin.WarehouseZoneId != zone.WarehouseZoneId)
        {
            throw new ConflictException(RentalRules.RentalBinCodeTaken(warehouse.Code));
        }
        return bin.WarehouseBinId;
    }
}
