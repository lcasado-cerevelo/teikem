using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Constants;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Wms;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 6 (P5) — reabasto bajo demanda de las posiciones de picking (R15, D23). Una corrida por almacén (el indicado o el
/// único activo):
/// - bloquea el encabezado del almacén (InventoryQueries): dos corridas concurrentes del mismo almacén se serializan y la
///   segunda ve las REPLENISH que creó la primera (idempotencia: OPEN_TASK);
/// - evalúa los productos activos SIN serie con MinPickQty y posición preferida activa en una zona PICKING activa de ese
///   almacén; el plan lo decide ReplenishmentRules (objetivo MaxPickQty o 2×MinPickQty, origen FEFO restringido a RESERVE
///   sobre el disponible = en mano − reservado);
/// - crea una REPLENISH por asignación (posición de reserva + lote) con WarehouseTaskWriter (historial null → PENDING),
///   Ref PRODUCT + id. No mueve inventario: el TRANSFER ocurre al completar la tarea desde la cola (ReplenishTaskHandler),
///   donde el ledger re-verifica el disponible bajo bloqueo.
/// El permiso (warehouse.pick) y el módulo los exige el controlador.
/// </summary>
public sealed class ReplenishmentService(
    TeikemDbContext db,
    ITenantContext tenant,
    ILookupCache lookups,
    WarehouseTaskWriter taskWriter,
    IEnumerable<IWarehouseTaskHandler> handlers)
{
    public const string WarehouseInactive = "El almacén está inactivo.";

    public async Task<ReplenishmentResultDto> RunAsync(ReplenishmentRunRequest? req, CancellationToken ct)
    {
        var warehouse = await WarehouseTaskService.ResolveWarehouseOrDefaultAsync(db, req?.WarehousePublicId, ct);
        if (!warehouse.IsActive) throw new StatusRuleException(WarehouseInactive);
        var warehouseId = warehouse.WarehouseId;

        var evaluated = 0;
        var skippedOpen = 0;
        var skippedNoReserve = 0;
        var createdIds = new List<int>();

        await db.RunInTransactionAsync(async ct2 =>
        {
            // La estrategia de reintentos puede repetir el delegado: los contadores arrancan de cero en cada intento.
            evaluated = 0; skippedOpen = 0; skippedNoReserve = 0; createdIds.Clear();

            await LockWarehouseAsync(warehouseId, ct2);

            var serialId = await lookups.TryGetIdAsync(LookupDomains.TrackingType, TrackingTypes.Serial, ct2) ?? -1;
            var zoneTypes = (await lookups.GetDomainAsync(LookupDomains.ZoneType, ct2))
                .ToDictionary(l => l.LookupCodeId, l => l.InternalCode);

            // Posiciones del almacén con el tipo de su zona (zona inactiva = posición no elegible).
            var bins = await (from b in db.Set<WarehouseBin>().AsNoTracking()
                              join z in db.Set<WarehouseZone>().AsNoTracking() on b.WarehouseZoneId equals z.WarehouseZoneId
                              where b.WarehouseId == warehouseId
                              select new { b.WarehouseBinId, b.Code, Active = b.IsActive && z.IsActive, z.ZoneTypeLookupId })
                .ToDictionaryAsync(b => b.WarehouseBinId, ct2);
            string? ZoneTypeOf(int binId) => bins.TryGetValue(binId, out var b) && b.ZoneTypeLookupId is int zt ? zoneTypes.GetValueOrDefault(zt) : null;

            var products = await db.Set<Product>().AsNoTracking()
                .Where(p => p.IsActive && p.PreferredWarehouseId == warehouseId && p.PreferredBinId != null
                            && p.MinPickQty != null && p.MinPickQty > 0m && p.TrackingTypeLookupId != serialId)
                .Select(p => new { p.ProductId, p.Sku, PickBinId = p.PreferredBinId!.Value, p.MinPickQty, p.MaxPickQty })
                .OrderBy(p => p.Sku).ThenBy(p => p.ProductId)
                .ToListAsync(ct2);
            products = products.Where(p => bins.TryGetValue(p.PickBinId, out var b) && b.Active
                                           && string.Equals(ZoneTypeOf(p.PickBinId), ZoneTypes.Picking, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (products.Count == 0) return;

            var productIds = products.Select(p => p.ProductId).ToList();
            var balances = await db.Set<StockBalance>().AsNoTracking()
                .Where(b => b.WarehouseId == warehouseId && productIds.Contains(b.ProductId))
                .Select(b => new { b.ProductId, b.WarehouseBinId, b.LotId, b.QtyOnHand, b.QtyReserved })
                .ToListAsync(ct2);
            var lotIds = balances.Where(b => b.LotId != null).Select(b => b.LotId!.Value).Distinct().ToList();
            var expiries = await (from l in db.Set<InventoryLot>().AsNoTracking()
                                  join p in db.Set<Product>().AsNoTracking() on l.ProductId equals p.ProductId
                                  where lotIds.Contains(l.LotId)
                                  select new { l.LotId, l.ExpiryDate }).ToDictionaryAsync(l => l.LotId, l => l.ExpiryDate, ct2);

            // REPLENISH abiertas del almacén (idempotencia: una por producto y posición de picking).
            var replenishTypeId = await lookups.GetIdAsync(LookupDomains.WarehouseTaskType, WarehouseTaskTypes.Replenish, ct2);
            var openCodes = WarehouseTaskRules.OpenStatuses.ToList();
            var openStatusIds = await db.StatusCodes.AsNoTracking()
                .Where(s => s.Entity == StatusDomains.WarehouseTaskStatus && openCodes.Contains(s.InternalCode))
                .Select(s => s.StatusCodeId).ToListAsync(ct2);
            var open = await db.Set<WarehouseTask>().AsNoTracking()
                .Where(t => t.WarehouseId == warehouseId && t.TaskTypeLookupId == replenishTypeId && openStatusIds.Contains(t.StatusCodeId)
                            && t.ProductId != null && productIds.Contains(t.ProductId.Value))
                .Select(t => new { t.ProductId, t.ToBinId }).ToListAsync(ct2);

            foreach (var p in products)
            {
                evaluated++;
                var own = balances.Where(b => b.ProductId == p.ProductId).ToList();
                var candidate = new ReplenishmentProduct(p.ProductId, p.Sku, p.PickBinId, p.MinPickQty, p.MaxPickQty,
                    own.Where(b => b.WarehouseBinId == p.PickBinId).Sum(b => ReplenishmentRules.Available(b.QtyOnHand, b.QtyReserved)),
                    open.Any(t => t.ProductId == p.ProductId && t.ToBinId == p.PickBinId));
                var sources = own.Where(b => b.WarehouseBinId is int binId && bins.ContainsKey(binId))
                    .Select(b =>
                    {
                        var bin = bins[b.WarehouseBinId!.Value];
                        return new ReplenishmentSource(bin.WarehouseBinId, bin.Code, ZoneTypeOf(bin.WarehouseBinId), bin.Active, b.LotId,
                            b.LotId is int lid ? expiries.GetValueOrDefault(lid) : null, b.QtyOnHand, b.QtyReserved);
                    });

                var plan = ReplenishmentRules.Plan(candidate, sources);
                switch (plan.SkipReason)
                {
                    case ReplenishmentRules.SkipOpenTask: skippedOpen++; continue;
                    case ReplenishmentRules.SkipNoReserve: skippedNoReserve++; continue;
                    case ReplenishmentRules.SkipNotBelowMin: continue;
                }
                foreach (var move in plan.Moves)
                {
                    var task = await WarehouseTaskWrites.CreateAsync(db, taskWriter, new WarehouseTaskSpec(
                        TaskType: WarehouseTaskTypes.Replenish, WarehouseId: warehouseId, ProductId: p.ProductId, Quantity: move.Quantity,
                        LotId: move.LotId, FromBinId: move.FromBinId, ToBinId: p.PickBinId,
                        RefEntityType: EntityTypes.Product, RefId: p.ProductId), ct2);
                    await db.SaveChangesAsync(ct2);
                    createdIds.Add(task.WarehouseTaskId);
                }
            }
        }, ct);

        var created = createdIds.Count == 0
            ? new List<WarehouseTask>()
            : await db.Set<WarehouseTask>().AsNoTracking().Where(t => createdIds.Contains(t.WarehouseTaskId))
                .OrderBy(t => t.WarehouseTaskId).ToListAsync(ct);
        var dtos = await WarehouseTaskReads.BuildAsync(db, tenant, lookups, handlers, created, ct);
        return new ReplenishmentResultDto(evaluated, createdIds.Count, skippedOpen, skippedNoReserve, dtos);
    }

    // ================================================================ adaptador a la costura de P0 (InventoryQueries)

    /// <summary>Encabezado Warehouse con UPDLOCK (serializa las corridas del mismo almacén).</summary>
    private async Task LockWarehouseAsync(int warehouseId, CancellationToken ct) => await db.LockWarehouseAsync(warehouseId, ct);
}
