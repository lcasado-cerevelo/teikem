using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Constants;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Wms;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 6 (P7) — costura IOrderInventoryLines (D45): líneas de PRODUCTO de cada orden, tomadas de las recolecciones
/// empacadas. Las líneas de la orden (CargoLine) son paquetes sin ProductId (D11), así que el producto vendido sale de aquí.
/// - Solo recolecciones activas en PACKED ligadas a esas órdenes y líneas sin reversa (ReversalTxnId NULL).
/// - Producto, lote, serie, cantidad, costo CONGELADO de la línea (D35) y el precio de venta VIGENTE del producto (el Lote 10
///   decide al facturar).
/// - Una sola consulta bajo el filtro de tenant (PickBatch y Product son ITenantScoped; líneas, lotes y series se alcanzan
///   por ellos): una orden de otro tenant no devuelve nada. Las asignaciones de cruce de muelle no se incluyen (mercancía de
///   terceros).
/// - El diccionario solo trae las órdenes que tienen líneas; el resto no aparece.
/// Insumo de PRODUCT_SALE y de la Contabilización de despachos (Lote 10).
/// </summary>
public sealed class OrderInventoryLinesProvider(TeikemDbContext db) : IOrderInventoryLines
{
    public async Task<IReadOnlyDictionary<int, IReadOnlyList<OrderInventoryLine>>> GetAsync(IReadOnlyCollection<int> transportOrderIds, CancellationToken ct)
    {
        var ids = (transportOrderIds ?? Array.Empty<int>()).Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<int, IReadOnlyList<OrderInventoryLine>>();

        var rows = await (from b in db.Set<PickBatch>().AsNoTracking()
                          join s in db.StatusCodes.AsNoTracking() on b.StatusCodeId equals s.StatusCodeId
                          join l in db.Set<PickBatchLine>().AsNoTracking() on b.PickBatchId equals l.PickBatchId
                          join p in db.Set<Product>().AsNoTracking() on l.ProductId equals p.ProductId
                          join lot in db.Set<InventoryLot>().AsNoTracking() on l.LotId equals (int?)lot.LotId into lj
                          from lot in lj.DefaultIfEmpty()
                          join ser in db.Set<InventorySerial>().AsNoTracking() on l.SerialId equals (int?)ser.SerialId into sj
                          from ser in sj.DefaultIfEmpty()
                          where b.IsActive && b.TransportOrderId != null && ids.Contains(b.TransportOrderId!.Value)
                                && s.Entity == StatusDomains.PickBatchStatus && s.InternalCode == PickBatchStatuses.Packed
                                && l.ReversalTxnId == null
                          orderby b.PickBatchId, l.PickBatchLineId
                          select new
                          {
                              TransportOrderId = b.TransportOrderId!.Value, b.PickBatchId, b.Number,
                              p.ProductId, p.PublicId, p.Sku, p.Name,
                              l.LotId, LotNumber = lot == null ? null : lot.LotNumber,
                              l.SerialId, SerialNumber = ser == null ? null : ser.SerialNumber,
                              l.Quantity, l.UnitCost, p.SalePrice,
                          }).ToListAsync(ct);

        return rows
            .GroupBy(r => r.TransportOrderId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<OrderInventoryLine>)g.Select(r => new OrderInventoryLine(r.TransportOrderId, r.PickBatchId, r.Number,
                        r.ProductId, r.PublicId, r.Sku, r.Name, r.LotId, r.LotNumber, r.SerialId, r.SerialNumber, r.Quantity, r.UnitCost,
                        r.SalePrice))
                    .ToList());
    }
}
