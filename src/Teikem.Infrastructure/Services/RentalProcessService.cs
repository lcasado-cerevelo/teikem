using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Common;
using Teikem.Domain.Constants;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Wms;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 28 (Rentas R2, plan 4.5) — proceso configurable del equipo devuelto (inspección, limpieza, pruebas, reparación…). La serie
/// queda IN_PROCESS y reservada en la posición del proceso hasta que el proceso termina.
/// - Advance: a cualquier estatus HABILITADO de RentalProcessStatus, con las reglas de StatusService (siguiente paso, laterales
///   Reparación/Esperando piezas, terminales); a SCRAPPED exige además inventory.adjust.
/// - Complete: a READY, con traslado opcional a otra posición del mismo almacén (TRANSFER que conserva la reserva y la serie
///   IN_PROCESS; la referencia es RENTAL_PROCESS); el efecto libera la reserva (IN_PROCESS → AVAILABLE).
/// - Scrap: a SCRAPPED; exige inventory.adjust además de rental.maintenance (PermissionService: un solo [RequirePermission] por
///   acción en el controlador). El efecto hace el ADJUSTMENT − con motivo DAMAGE y la serie queda SCRAPPED.
/// Los efectos de inventario están en RentalProcessStatusEffect (dependen de los terminales, no de los pasos). Un proceso terminado
/// responde 422 'El proceso ya terminó; solo se consulta.'. Todo en RunInTransactionAsync con el orden RentalProcess (U) → saldos →
/// series. El proceso se alcanza por id bajo el filtro de tenant (otra compañía → 404 'Proceso no encontrado.').
/// </summary>
public sealed class RentalProcessService(
    TeikemDbContext db,
    ITenantContext tenant,
    ILookupCache lookups,
    StatusService statuses,
    InventoryLedger ledger,
    PermissionService permissions)
{
    public const string ConcurrencyMessage = DbExtensions.ConcurrencyMessage;

    // ================================================================ lista

    public async Task<RentalProcessPageDto> ListAsync(RentalProcessQuery? q, CancellationToken ct)
    {
        q ??= new RentalProcessQuery();
        var skip = Math.Max(0, q.Skip);
        var take = q.Take <= 0 ? 100 : Math.Min(q.Take, RentalRules.MaxPageSize);
        var query = db.RentalProcesses.AsNoTracking();
        if (q.Status is { Length: > 0 })
        {
            var codes = q.Status.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim().ToUpperInvariant()).Distinct().ToList();
            var ids = await db.StatusCodes.AsNoTracking().Where(s => s.Entity == StatusDomains.RentalProcessStatus && codes.Contains(s.InternalCode))
                .Select(s => s.StatusCodeId).ToListAsync(ct);
            query = query.Where(p => ids.Contains(p.StatusCodeId));
        }
        if (q.Open is bool open) query = open ? query.Where(p => p.CompletedAtUtc == null) : query.Where(p => p.CompletedAtUtc != null);
        if (q.WarehousePublicId is Guid warehousePublicId)
        {
            var wh = await db.ResolveWarehouseAsync(warehousePublicId, false, ct);
            query = query.Where(p => p.WarehouseId == wh.WarehouseId);
        }
        if (q.ProductPublicId is Guid productPublicId)
        {
            var productId = await db.Products.AsNoTracking().Where(p => p.PublicId == productPublicId).Select(p => (int?)p.ProductId).FirstOrDefaultAsync(ct)
                            ?? throw WmsResolve.ProductNotFound();
            query = query.Where(p => p.ProductId == productId);
        }
        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var s = q.Search.Trim();
            query = query.Where(p => db.InventorySerials.Any(se => se.SerialId == p.SerialId && se.SerialNumber.Contains(s))
                || db.Products.Any(pr => pr.ProductId == p.ProductId && pr.Sku.Contains(s))
                || (from rl in db.RentalReturnLines
                    join r in db.RentalReturns on rl.RentalReturnId equals r.RentalReturnId
                    join rent in db.Rentals on r.RentalId equals rent.RentalId
                    where (int?)rl.RentalReturnLineId == p.RentalReturnLineId && (r.Number.Contains(s) || rent.Number.Contains(s))
                    select rl.RentalReturnLineId).Any());
        }
        var total = await query.CountAsync(ct);
        // Abiertos primero (los más viejos arriba: es una cola); después los terminados, los más recientes primero.
        var page = await query.OrderBy(p => p.CompletedAtUtc == null ? 0 : 1)
            .ThenBy(p => p.CompletedAtUtc == null ? p.RentalProcessId : -p.RentalProcessId)
            .Skip(skip).Take(take).ToListAsync(ct);
        return new RentalProcessPageDto(total, skip, take, await ToDtosAsync(page, ct));
    }

    public async Task<RentalProcessDto> GetAsync(int id, CancellationToken ct)
    {
        var process = await db.RentalProcesses.AsNoTracking().FirstOrDefaultAsync(p => p.RentalProcessId == id, ct)
                      ?? throw new NotFoundException(RentalRules.ProcessNotFound);
        return (await ToDtosAsync(new List<RentalProcess> { process }, ct))[0];
    }

    // ================================================================ acciones

    /// <summary>Pasa el proceso a cualquier estatus habilitado (reglas de StatusService). A SCRAPPED exige inventory.adjust.</summary>
    public async Task<RentalProcessDto> AdvanceAsync(int id, RentalProcessAdvanceRequest req, CancellationToken ct)
    {
        if (req is null) throw new ValidationException("body", "El cuerpo de la solicitud es obligatorio.");
        if (string.IsNullOrWhiteSpace(req.Status)) throw new ValidationException("status", RentalRules.ProcessStatusRequired);
        var code = req.Status.Trim().ToUpperInvariant();
        if (code == RentalProcessStatuses.Scrapped) await permissions.EnsureAsync(PermissionCatalog.InventoryAdjust, ct);
        var comment = PurchasingSupport.Comment(req.Comment);
        await TransitionAsync(id, code, comment, req.RowVersion, null, ct);
        return await GetAsync(id, ct);
    }

    /// <summary>Termina el proceso (Lista): traslado opcional a otra posición del mismo almacén y liberación de la reserva (efecto).</summary>
    public async Task<RentalProcessDto> CompleteAsync(int id, RentalProcessCompleteRequest? req, CancellationToken ct)
    {
        var comment = PurchasingSupport.Comment(req?.Comment);
        await TransitionAsync(id, RentalProcessStatuses.Ready, comment, req?.RowVersion, req?.BinId, ct);
        return await GetAsync(id, ct);
    }

    /// <summary>Da de baja el equipo (Dada de baja): exige inventory.adjust; el efecto hace el ajuste de salida con motivo DAMAGE.</summary>
    public async Task<RentalProcessDto> ScrapAsync(int id, RentalStatusRequest? req, CancellationToken ct)
    {
        await permissions.EnsureAsync(PermissionCatalog.InventoryAdjust, ct);
        var comment = PurchasingSupport.Comment(req?.Comment);
        await TransitionAsync(id, RentalProcessStatuses.Scrapped, comment, req?.RowVersion, null, ct);
        return await GetAsync(id, ct);
    }

    private async Task TransitionAsync(int id, string toCode, string? comment, string? rowVersion, int? moveToBinId, CancellationToken ct)
    {
        // Existe en la compañía (404 antes de abrir la transacción).
        if (!await db.RentalProcesses.AsNoTracking().AnyAsync(p => p.RentalProcessId == id, ct)) throw new NotFoundException(RentalRules.ProcessNotFound);
        await db.RunInTransactionAsync(async ct2 =>
        {
            var process = await db.LockRentalProcessAsync(id, ct2);
            db.ApplyRowVersion(process, rowVersion);
            var stageKind = await db.StatusCodes.AsNoTracking().Where(s => s.StatusCodeId == process.StatusCodeId)
                .Select(s => s.StageKind!.InternalCode).FirstOrDefaultAsync(ct2);
            if (RentalRules.IsProcessFinished(stageKind, process.CompletedAtUtc)) throw new StatusRuleException(RentalRules.ProcessFinished);

            if (moveToBinId is int binId && binId != process.BinId)
            {
                var bin = await (from b in db.WarehouseBins.AsNoTracking()
                                 join w in db.Warehouses.AsNoTracking() on b.WarehouseId equals w.WarehouseId
                                 join z in db.WarehouseZones.AsNoTracking() on b.WarehouseZoneId equals z.WarehouseZoneId
                                 where b.WarehouseBinId == binId
                                 select new { b.WarehouseBinId, b.WarehouseId, z.ZoneTypeLookupId }).FirstOrDefaultAsync(ct2)
                          ?? throw WmsResolve.BinNotFound();
                if (bin.WarehouseId != process.WarehouseId)
                {
                    var code = await db.Warehouses.AsNoTracking().Where(w => w.WarehouseId == process.WarehouseId).Select(w => w.Code).FirstAsync(ct2);
                    throw new ValidationException("binId", RentalRules.ProcessBinOtherWarehouse(code));
                }
                var zoneType = bin.ZoneTypeLookupId is int zt ? (await lookups.GetAsync(zt, ct2))?.InternalCode : null;
                if (zoneType == ZoneTypes.Rental) throw new ValidationException("binId", RentalRules.DestinationInRentalZone);
                var serial = await db.InventorySerials.AsNoTracking().Where(s => s.SerialId == process.SerialId && s.ProductId == process.ProductId)
                    .Select(s => new { s.SerialNumber, s.LotId }).FirstAsync(ct2);
                // Traslado con la reserva: sale de lo reservado (serie IN_PROCESS) y entra reservado; la serie sigue IN_PROCESS.
                await ledger.PostAsync(new[]
                {
                    new InventoryPosting(InventoryTxnTypes.Transfer, process.ProductId, 1m, LotId: serial.LotId, SerialNumber: serial.SerialNumber,
                        FromWarehouseId: process.WarehouseId, FromBinId: process.BinId, ToWarehouseId: process.WarehouseId, ToBinId: bin.WarehouseBinId,
                        RefEntityType: EntityTypes.RentalProcess, RefId: process.RentalProcessId, Notes: RentalRules.ProcessMoveNotes(process.RentalProcessId),
                        FromReserved: true, ExpectedSerialStatus: SerialStatuses.InProcess, ReserveAtDestination: true),
                }, ct2);
                process.BinId = bin.WarehouseBinId;
            }

            var to = await statuses.TransitionAsync(StatusDomains.RentalProcessStatus, EntityTypes.RentalProcess, process.RentalProcessId,
                process.StatusCodeId, toCode, comment, ct2);
            process.StatusCodeId = to.StatusCodeId;
            await db.SaveGuardedAsync(ConcurrencyMessage, ct2);
        }, ct);
    }

    // ================================================================ DTOs

    private async Task<IReadOnlyList<RentalProcessDto>> ToDtosAsync(List<RentalProcess> rows, CancellationToken ct)
    {
        if (rows.Count == 0) return Array.Empty<RentalProcessDto>();
        var serialIds = rows.Select(p => p.SerialId).Distinct().ToList();
        var productIds = rows.Select(p => p.ProductId).Distinct().ToList();
        var warehouseIds = rows.Select(p => p.WarehouseId).Distinct().ToList();
        var binIds = rows.Select(p => p.BinId).Distinct().ToList();
        var lineIds = rows.Where(p => p.RentalReturnLineId.HasValue).Select(p => p.RentalReturnLineId!.Value).Distinct().ToList();
        var serials = await db.InventorySerials.AsNoTracking().Where(s => serialIds.Contains(s.SerialId) && productIds.Contains(s.ProductId))
            .ToDictionaryAsync(s => s.SerialId, s => s.SerialNumber, ct);
        var products = await db.Products.AsNoTracking().Where(p => productIds.Contains(p.ProductId))
            .Select(p => new { p.ProductId, p.PublicId, p.Sku, p.Name }).ToDictionaryAsync(p => p.ProductId, ct);
        var warehouses = await db.Warehouses.AsNoTracking().Where(w => warehouseIds.Contains(w.WarehouseId))
            .Select(w => new { w.WarehouseId, w.PublicId, w.Code }).ToDictionaryAsync(w => w.WarehouseId, ct);
        var bins = await (from b in db.WarehouseBins.AsNoTracking()
                          join w in db.Warehouses.AsNoTracking() on b.WarehouseId equals w.WarehouseId
                          where binIds.Contains(b.WarehouseBinId)
                          select new { b.WarehouseBinId, b.Code }).ToDictionaryAsync(b => b.WarehouseBinId, b => b.Code, ct);
        var origins = await (from rl in db.RentalReturnLines.AsNoTracking()
                             join r in db.RentalReturns.AsNoTracking() on rl.RentalReturnId equals r.RentalReturnId
                             join rent in db.Rentals.AsNoTracking() on r.RentalId equals rent.RentalId
                             where lineIds.Contains(rl.RentalReturnLineId)
                             select new { rl.RentalReturnLineId, rl.ConditionLookupId, ReturnPublicId = r.PublicId, ReturnNumber = r.Number,
                                 RentalPublicId = rent.PublicId, RentalNumber = rent.Number }).ToDictionaryAsync(x => x.RentalReturnLineId, ct);
        var statusCodes = await db.StatusCodes.AsNoTracking().Include(s => s.StageKind).Where(s => s.Entity == StatusDomains.RentalProcessStatus).ToListAsync(ct);
        var ids = statusCodes.Select(s => s.StatusCodeId).ToList();
        var overrides = tenant.TenantId is null
            ? new Dictionary<int, (string? Label, string? Color)>()
            : await db.StatusCodeOverrides.AsNoTracking().Where(o => ids.Contains(o.StatusCodeId))
                .ToDictionaryAsync(o => o.StatusCodeId, o => (Label: o.CustomLabelJson, Color: o.CustomColorHex), ct);

        var result = new List<RentalProcessDto>(rows.Count);
        foreach (var p in rows)
        {
            var status = statusCodes.FirstOrDefault(s => s.StatusCodeId == p.StatusCodeId);
            var o = overrides.GetValueOrDefault(p.StatusCodeId);
            var label = status is null ? string.Empty : MultilingualText.Resolve(MultilingualText.Merge(status.LabelJson, o.Label), tenant.Lang);
            var product = products.GetValueOrDefault(p.ProductId);
            var warehouse = warehouses.GetValueOrDefault(p.WarehouseId);
            var origin = p.RentalReturnLineId is int lid ? origins.GetValueOrDefault(lid) : null;
            var condition = origin is null ? null : (await lookups.GetAsync(origin.ConditionLookupId, ct))?.InternalCode;
            result.Add(new RentalProcessDto(p.RentalProcessId, p.SerialId, serials.GetValueOrDefault(p.SerialId) ?? string.Empty,
                product?.PublicId ?? Guid.Empty, product?.Sku ?? string.Empty, product?.Name ?? string.Empty,
                warehouse?.PublicId ?? Guid.Empty, warehouse?.Code ?? string.Empty, p.BinId, bins.GetValueOrDefault(p.BinId) ?? string.Empty,
                status?.InternalCode ?? string.Empty, label, o.Color ?? status?.ColorHex,
                RentalRules.IsProcessFinished(status?.StageKind?.InternalCode, p.CompletedAtUtc),
                origin?.ReturnPublicId, origin?.ReturnNumber, origin?.RentalPublicId, origin?.RentalNumber, condition,
                p.StartedAtUtc, p.CompletedAtUtc, p.Notes, Convert.ToBase64String(p.RowVersion ?? Array.Empty<byte>())));
        }
        return result;
    }
}
