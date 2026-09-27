using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Constants;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Wms;

/// <summary>
/// Lote 6 (P0) — resolución de encabezados y de hijas del almacén. Los encabezados con PublicId (Warehouse, Product) se
/// resuelven bajo el filtro global de tenant; las hijas sin TenantId (zona, posición, muelle, lote) SIEMPRE a través de su
/// padre filtrado: una posición de otro almacén o de otro tenant da 404 'Posición no encontrada.' (sin oráculo).
/// </summary>
public static class WmsResolve
{
    public const string WarehouseRequiredMessage = "Indique el almacén: la compañía tiene más de uno.";
    public const string NoActiveWarehouseMessage = "La compañía no tiene almacenes activos.";

    public static NotFoundException WarehouseNotFound() => new("Almacén");
    public static NotFoundException ZoneNotFound() => new("Zona", null, true);
    public static NotFoundException BinNotFound() => new("Posición", null, true);
    public static NotFoundException DockNotFound() => new("Muelle");
    public static NotFoundException ProductNotFound() => new("Producto");
    public static NotFoundException LotNotFound() => new("Lote");

    /// <summary>Almacén del tenant por PublicId (404 'Almacén no encontrado.'). Los dados de baja también se resuelven.</summary>
    public static async Task<Warehouse> ResolveWarehouseAsync(this TeikemDbContext db, Guid publicId, bool track, CancellationToken ct)
    {
        var q = track ? db.Warehouses.AsTracking() : db.Warehouses.AsNoTracking();
        return await q.FirstOrDefaultAsync(w => w.PublicId == publicId, ct) ?? throw WarehouseNotFound();
    }

    /// <summary>
    /// Almacén por PublicId (404) o, sin él, el único almacén ACTIVO del tenant (D26): con más de uno → 400 'Indique el
    /// almacén: la compañía tiene más de uno.'; sin ninguno → 422 'La compañía no tiene almacenes activos.'.
    /// </summary>
    public static async Task<Warehouse> ResolveWarehouseOrDefaultAsync(this TeikemDbContext db, Guid? publicId, CancellationToken ct)
    {
        if (publicId is Guid id) return await db.ResolveWarehouseAsync(id, false, ct);
        var active = await db.Warehouses.AsNoTracking().Where(w => w.IsActive).OrderBy(w => w.WarehouseId).Take(2).ToListAsync(ct);
        return active.Count switch
        {
            0 => throw new StatusRuleException(NoActiveWarehouseMessage),
            1 => active[0],
            _ => throw new ValidationException("warehousePublicId", WarehouseRequiredMessage),
        };
    }

    /// <summary>Zona del almacén (resuelto bajo el filtro de tenant). 404 'Zona no encontrada.'</summary>
    public static async Task<WarehouseZone> ResolveZoneAsync(this TeikemDbContext db, int warehouseId, int zoneId, bool track, CancellationToken ct)
    {
        // AsTracking/AsNoTracking valen para TODA la consulta (gana el último): se aplican sobre el IQueryable completo,
        // no sobre la fuente del join, para que track:true devuelva la hija rastreada (el almacén no se proyecta).
        var q = from z in db.WarehouseZones
                join w in db.Warehouses on z.WarehouseId equals w.WarehouseId
                where z.WarehouseZoneId == zoneId && z.WarehouseId == warehouseId
                select z;
        return await (track ? q.AsTracking() : q.AsNoTracking()).FirstOrDefaultAsync(ct)
               ?? throw ZoneNotFound();
    }

    /// <summary>Posición del almacén (resuelto bajo el filtro de tenant). 404 'Posición no encontrada.'</summary>
    public static async Task<WarehouseBin> ResolveBinAsync(this TeikemDbContext db, int warehouseId, int binId, bool track, CancellationToken ct)
    {
        // AsTracking/AsNoTracking valen para TODA la consulta (gana el último): se aplican sobre el IQueryable completo,
        // no sobre la fuente del join, para que track:true devuelva la hija rastreada (el almacén no se proyecta).
        var q = from b in db.WarehouseBins
                join w in db.Warehouses on b.WarehouseId equals w.WarehouseId
                where b.WarehouseBinId == binId && b.WarehouseId == warehouseId
                select b;
        return await (track ? q.AsTracking() : q.AsNoTracking()).FirstOrDefaultAsync(ct)
               ?? throw BinNotFound();
    }

    /// <summary>Posición del almacén por código (sin distinguir mayúsculas). 404 'Posición no encontrada.'</summary>
    public static async Task<WarehouseBin> ResolveBinByCodeAsync(this TeikemDbContext db, int warehouseId, string code, CancellationToken ct)
    {
        var c = (code ?? string.Empty).Trim().ToUpperInvariant();
        return await (from b in db.WarehouseBins.AsNoTracking()
                      join w in db.Warehouses.AsNoTracking() on b.WarehouseId equals w.WarehouseId
                      where b.WarehouseId == warehouseId && b.Code.ToUpper() == c
                      select b).FirstOrDefaultAsync(ct)
               ?? throw BinNotFound();
    }

    /// <summary>Muelle del almacén (resuelto bajo el filtro de tenant). 404 'Muelle no encontrado.'</summary>
    public static async Task<WarehouseDock> ResolveDockAsync(this TeikemDbContext db, int warehouseId, int dockId, bool track, CancellationToken ct)
    {
        // AsTracking/AsNoTracking valen para TODA la consulta (gana el último): se aplican sobre el IQueryable completo,
        // no sobre la fuente del join, para que track:true devuelva la hija rastreada (el almacén no se proyecta).
        var q = from d in db.WarehouseDocks
                join w in db.Warehouses on d.WarehouseId equals w.WarehouseId
                where d.WarehouseDockId == dockId && d.WarehouseId == warehouseId
                select d;
        return await (track ? q.AsTracking() : q.AsNoTracking()).FirstOrDefaultAsync(ct)
               ?? throw DockNotFound();
    }

    /// <summary>Producto del tenant por PublicId (404); requireActive → 422 si está inactivo.</summary>
    public static Task<Product> ResolveProductAsync(this TeikemDbContext db, Guid publicId, bool requireActive, CancellationToken ct)
        => db.ResolveProductAsync(publicId, requireActive, null, ct);

    /// <summary>
    /// Producto del tenant por PublicId con alcance de dueño (D44): con scope.OwnerClientId fijado, un producto de otro dueño
    /// (o propio del tenant) → 404 sin oráculo. requireActive → 422 'El producto {sku} está inactivo; …'.
    /// </summary>
    public static async Task<Product> ResolveProductAsync(this TeikemDbContext db, Guid publicId, bool requireActive, InventoryScope? scope, CancellationToken ct)
    {
        var q = db.Products.AsNoTracking().Where(p => p.PublicId == publicId);
        if (scope?.OwnerClientId is int owner) q = q.Where(p => p.ClientId == owner);
        var product = await q.FirstOrDefaultAsync(ct) ?? throw ProductNotFound();
        if (requireActive && !product.IsActive) throw new StatusRuleException(InventoryRules.ProductInactiveMessage(product.Sku));
        return product;
    }

    /// <summary>Lote del producto (el producto ya resuelto bajo el filtro de tenant). 404 'Lote no encontrado.'</summary>
    public static async Task<InventoryLot> ResolveLotAsync(this TeikemDbContext db, int productId, int lotId, CancellationToken ct)
        => await (from l in db.InventoryLots.AsNoTracking()
                  join p in db.Products.AsNoTracking() on l.ProductId equals p.ProductId
                  where l.LotId == lotId && l.ProductId == productId
                  select l).FirstOrDefaultAsync(ct)
           ?? throw LotNotFound();

    /// <summary>Código de seguimiento (NONE/LOT/SERIAL) del producto.</summary>
    public static async Task<string> TrackingOfAsync(this TeikemDbContext db, Product product, CancellationToken ct)
        => await db.LookupCodes.AsNoTracking().Where(l => l.LookupCodeId == product.TrackingTypeLookupId)
               .Select(l => l.InternalCode).FirstOrDefaultAsync(ct)
           ?? TrackingTypes.None;
}
