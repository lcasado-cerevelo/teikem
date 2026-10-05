using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Orden de salida del inventario de un almacén: por producto, las existencias DISPONIBLES (en mano − reservado) en el orden en que la
/// recolección las asigna (<see cref="PickBatchRules.Eligible"/>: vence primero, luego tipo de zona y código de posición; sin cuarentena ni
/// cruce de muelle ni posiciones inactivas). Es la ÚNICA implementación de esa regla para "de dónde debe salir": la app de almacén la baja
/// al aparato (para despachar sin señal) y la consulta en línea; ninguna pantalla vuelve a ordenar. Solo lectura, bajo el filtro de tenant;
/// la posición y la zona (sin TenantId) se alcanzan por el almacén filtrado.
/// </summary>
public sealed class StockExitService(TeikemDbContext db, ILookupCache lookups)
{
    public const int DefaultTake = 500;
    public const int MaxTake = 500;

    /// <param name="warehousePublicId">Almacén (obligatorio: 404 si no existe o no es de la compañía).</param>
    /// <param name="productPublicIds">Solo esos productos (vacío = todos los del almacén con existencia disponible).</param>
    public async Task<StockExitPageDto> ListAsync(Guid? warehousePublicId, Guid[]? productPublicIds, int skip, int take, CancellationToken ct)
    {
        if (warehousePublicId is null) throw new ValidationException("warehousePublicId", "Indique el almacén.");
        var warehouseId = await db.Set<Warehouse>().AsNoTracking().Where(w => w.PublicId == warehousePublicId.Value)
                              .Select(w => (int?)w.WarehouseId).FirstOrDefaultAsync(ct)
                          ?? throw new NotFoundException("Almacén");
        skip = Math.Max(0, skip);
        take = take <= 0 ? DefaultTake : Math.Min(take, MaxTake);

        var rows = from b in db.Set<StockBalance>().AsNoTracking()
                   join bin in db.Set<WarehouseBin>().AsNoTracking() on b.WarehouseBinId equals (int?)bin.WarehouseBinId
                   join z in db.Set<WarehouseZone>().AsNoTracking() on bin.WarehouseZoneId equals z.WarehouseZoneId
                   join p in db.Set<Product>().AsNoTracking() on b.ProductId equals p.ProductId
                   join l in db.Set<InventoryLot>().AsNoTracking() on b.LotId equals (int?)l.LotId into lots
                   from lot in lots.DefaultIfEmpty()
                   where b.WarehouseId == warehouseId && bin.WarehouseId == warehouseId && bin.IsActive && p.IsActive
                         && b.QtyOnHand - b.QtyReserved > 0m
                   select new
                   {
                       p.ProductId, ProductPublicId = p.PublicId, bin.WarehouseBinId, BinCode = bin.Code, ZoneCode = z.Code, z.ZoneTypeLookupId,
                       b.LotId, LotNumber = lot != null ? lot.LotNumber : null, Expiry = lot != null ? lot.ExpiryDate : null,
                       Available = b.QtyOnHand - b.QtyReserved,
                   };
        if (productPublicIds is { Length: > 0 })
        {
            var wanted = productPublicIds.Distinct().ToList();
            rows = rows.Where(r => wanted.Contains(r.ProductPublicId));
        }
        var list = await rows.ToListAsync(ct);

        var zoneTypes = new Dictionary<int, string?>();
        foreach (var id in list.Where(r => r.ZoneTypeLookupId != null).Select(r => r.ZoneTypeLookupId!.Value).Distinct())
            zoneTypes[id] = (await lookups.GetAsync(id, ct))?.InternalCode;

        var ranked = new List<StockExitOptionDto>();
        foreach (var g in list.GroupBy(r => (r.ProductId, r.ProductPublicId)).OrderBy(g => g.Key.ProductId))
        {
            var byKey = g.ToDictionary(r => (r.WarehouseBinId, r.LotId));
            var candidates = g.Select(r => new PickCandidate(r.WarehouseBinId, r.BinCode, r.ZoneTypeLookupId is int zt ? zoneTypes.GetValueOrDefault(zt) : null,
                BinActive: true, r.LotId, r.Expiry, r.Available));
            var rank = 0;
            foreach (var c in PickBatchRules.Eligible(candidates))
            {
                var r = byKey[(c.BinId, c.LotId)];
                ranked.Add(new StockExitOptionDto(g.Key.ProductPublicId, c.BinId, c.BinCode, r.ZoneCode, c.ZoneTypeCode, c.LotId, r.LotNumber, c.ExpiryDate,
                    InventoryRules.Round4(c.Available), ++rank));
            }
        }
        return new StockExitPageDto(ranked.Count, skip, take, DateTime.UtcNow, ranked.Skip(skip).Take(take).ToList());
    }
}
