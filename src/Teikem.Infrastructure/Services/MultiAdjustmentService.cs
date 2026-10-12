using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Constants;
using Teikem.Domain.Orders;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Orders;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Wms;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// 2026-10-12 — Ajuste múltiple AJM-###### (docs/ajuste-multiple-diseno.md): pone al día el inventario de UN almacén por producto y posición, varias a
/// la vez, en una sola transacción (todo o nada). Permiso inventory.adjust.multi en todas las rutas.
/// - Cada línea con diferencia es un movimiento Ajuste del ledger (motivo PHYSICAL_COUNT, origen MULTI_ADJUSTMENT + id del documento). No hay tipo
///   de movimiento nuevo: saldos, Kárdex y reportes lo ven como cualquier ajuste.
/// - La diferencia se calcula con el saldo BLOQUEADO (mismo orden de claves que el ledger); si lo que vio la pantalla (SystemQty) ya no es lo que
///   hay → 409 stale_system_qty con las líneas (lines[i]). Nunca se aplica en silencio sobre otro saldo.
/// - Sin serie (se ajustan con el ajuste normal); posiciones de cuarentena, en renta o cross-dock no entran (422), como el ajuste del aparato.
/// - La existencia final no puede quedar por debajo de lo reservado (409 insufficient_stock con lines[i]).
/// </summary>
public sealed class MultiAdjustmentService(TeikemDbContext db, ILookupCache lookups, InventoryLedger ledger, INumberSequenceService numbers, ITenantContext tenant)
{
    public const string Label = "Ajuste múltiple";
    public const string StaleCode = "stale_system_qty";
    public const int MaxTake = 200;
    public const string NumberTaken = "Ese número de ajuste múltiple ya existe; intente de nuevo.";

    private static readonly int? CounterClientId = null;   // un contador por compañía (ClientId NULL)

    private sealed class StaleException : TeikemException
    {
        public StaleException(string message, IDictionary<string, string[]> errors) : base(message, 409, StaleCode) { Errors = errors; }
    }

    // ================================================================ lectura para la pantalla

    /// <summary>Productos con sus posiciones con existencia en el almacén (o el único activo); sin lote = LotId null.</summary>
    public async Task<IReadOnlyList<MultiAdjustmentProductDto>> StockAsync(Guid? warehousePublicId, IReadOnlyList<Guid> productPublicIds, CancellationToken ct)
    {
        if (productPublicIds is null || productPublicIds.Count == 0) return Array.Empty<MultiAdjustmentProductDto>();
        var warehouse = await WmsResolve.ResolveWarehouseOrDefaultAsync(db, warehousePublicId, ct);
        var products = await db.Set<Product>().AsNoTracking().Where(p => productPublicIds.Contains(p.PublicId)).ToListAsync(ct);
        var productIds = products.Select(p => p.ProductId).ToList();
        var rows = await (from s in db.Set<StockBalance>().AsNoTracking()
                          join b in db.Set<WarehouseBin>().AsNoTracking() on s.WarehouseBinId equals b.WarehouseBinId
                          join z in db.Set<WarehouseZone>().AsNoTracking() on b.WarehouseZoneId equals z.WarehouseZoneId
                          where s.WarehouseId == warehouse.WarehouseId && productIds.Contains(s.ProductId) && (s.QtyOnHand != 0 || s.QtyReserved != 0)
                          select new { s.ProductId, s.LotId, s.QtyOnHand, s.QtyReserved, BinId = b.WarehouseBinId, BinCode = b.Code, ZoneCode = z.Code }).ToListAsync(ct);
        var lotIds = rows.Where(r => r.LotId.HasValue).Select(r => r.LotId!.Value).Distinct().ToList();
        var lots = lotIds.Count == 0 ? new Dictionary<int, string>()
            : await db.Set<InventoryLot>().AsNoTracking().Where(l => lotIds.Contains(l.LotId)).ToDictionaryAsync(l => l.LotId, l => l.LotNumber, ct);
        var result = new List<MultiAdjustmentProductDto>();
        foreach (var id in productPublicIds.Distinct())
        {
            var p = products.FirstOrDefault(x => x.PublicId == id);
            if (p is null) continue;
            var tracking = (await lookups.GetAsync(p.TrackingTypeLookupId, ct))?.InternalCode ?? TrackingTypes.None;
            var positions = rows.Where(r => r.ProductId == p.ProductId)
                .Select(r => new MultiAdjustmentPositionDto(r.BinId, r.BinCode, r.ZoneCode, r.LotId, r.LotId.HasValue ? lots.GetValueOrDefault(r.LotId.Value) : null, r.QtyOnHand, r.QtyReserved))
                .OrderBy(x => x.BinCode, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.LotNumber, StringComparer.OrdinalIgnoreCase)
                .ToList();
            result.Add(new MultiAdjustmentProductDto(p.PublicId, p.Sku, p.Name, tracking, p.IsActive, positions));
        }
        return result;
    }

    // ================================================================ aplicar

    private sealed record Prepared(int Index, MultiAdjustmentLineInput Input, Product Product, WarehouseBin Bin, BalanceKey Key, decimal Counted);

    public async Task<MultiAdjustmentDetailDto> CreateAsync(MultiAdjustmentRequest req, CancellationToken ct)
    {
        if (req is null) throw new ValidationException("body", "El cuerpo de la solicitud es obligatorio.");
        var errors = new Dictionary<string, string[]>();
        var (notes, notesError) = AdjustmentRules.NormalizeNotes(req.Notes, required: true);
        if (notesError is not null) errors["notes"] = new[] { notesError };
        var lines = req.Lines ?? Array.Empty<MultiAdjustmentLineInput>();
        var seen = new HashSet<(Guid, int, int?)>();
        for (var i = 0; i < lines.Count; i++)
        {
            var l = lines[i];
            if (l is null) { errors[$"lines[{i}]"] = new[] { MultiAdjustmentRules.LinesRequired }; continue; }
            if (l.CountedQty is null) continue;   // «Hay ahora» vacío = no se toca
            if (l.ProductPublicId is null) errors[$"lines[{i}].productPublicId"] = new[] { AdjustmentRules.ProductRequired };
            if (l.BinId is null) errors[$"lines[{i}].binId"] = new[] { AdjustmentRules.BinRequired };
            if (l.CountedQty < 0) errors[$"lines[{i}].countedQty"] = new[] { MultiAdjustmentRules.CountedNegative };
            if (l.SystemQty is null) errors[$"lines[{i}].systemQty"] = new[] { MultiAdjustmentRules.SystemQtyRequired };
            if (l.ProductPublicId is Guid pid && l.BinId is int bid && !seen.Add((pid, bid, l.LotId)))
                errors[$"lines[{i}]"] = new[] { MultiAdjustmentRules.Duplicated };
        }
        if (errors.Count == 0 && !lines.Any(l => l?.CountedQty is not null)) errors["lines"] = new[] { MultiAdjustmentRules.LinesRequired };
        if (errors.Count > 0) throw new ValidationException(errors);

        await numbers.EnsureAsync(NumberKinds.MultiAdjust, CounterClientId, ct);
        var tenantId = ((TenantContext)tenant).RequireTenantId();

        var id = await db.RunInTransactionAsync(async ct2 =>
        {
            var warehouse = await WmsResolve.ResolveWarehouseOrDefaultAsync(db, req.WarehousePublicId, ct2);
            if (!warehouse.IsActive) throw new StatusRuleException(InventoryAdjustmentService.WarehouseInactive);

            var blocked = new List<int>();
            foreach (var code in new[] { ZoneTypes.Quarantine, ZoneTypes.Rental, ZoneTypes.CrossDock })
                if (await lookups.TryGetIdAsync(LookupDomains.ZoneType, code, ct2) is int zid) blocked.Add(zid);

            // (1) resolución sin bloqueo: producto, posición, lote y reglas de la línea
            var counted = lines.Select((l, i) => (l, i)).Where(x => x.l?.CountedQty is not null).ToList();
            var publicIds = counted.Select(x => x.l.ProductPublicId!.Value).Distinct().ToList();
            var products = await db.Set<Product>().AsNoTracking().Where(p => publicIds.Contains(p.PublicId)).ToDictionaryAsync(p => p.PublicId, ct2);
            var binIds = counted.Select(x => x.l.BinId!.Value).Distinct().ToList();
            var bins = await (from b in db.Set<WarehouseBin>().AsNoTracking()
                              join z in db.Set<WarehouseZone>().AsNoTracking() on b.WarehouseZoneId equals z.WarehouseZoneId
                              where b.WarehouseId == warehouse.WarehouseId && binIds.Contains(b.WarehouseBinId)
                              select new { Bin = b, ZoneType = z.ZoneTypeLookupId }).ToDictionaryAsync(x => x.Bin.WarehouseBinId, ct2);

            var prepared = new List<Prepared>();
            var lineErrors = new Dictionary<string, string[]>();
            foreach (var (l, i) in counted)
            {
                if (!products.TryGetValue(l.ProductPublicId!.Value, out var product)) throw new NotFoundException("Producto");
                if (!bins.TryGetValue(l.BinId!.Value, out var bin)) throw new NotFoundException("Posición", feminine: true);
                if (bin.ZoneType is int zt && blocked.Contains(zt)) throw new StatusRuleException(AdjustmentRules.AdjustZoneNotAllowed(bin.Bin.Code));
                var tracking = (await lookups.GetAsync(product.TrackingTypeLookupId, ct2))?.InternalCode ?? TrackingTypes.None;
                if (tracking == TrackingTypes.Serial) lineErrors[$"lines[{i}].productPublicId"] = new[] { MultiAdjustmentRules.SerialNotSupported };
                else if (tracking == TrackingTypes.Lot && l.LotId is null) lineErrors[$"lines[{i}].lotId"] = new[] { MultiAdjustmentRules.LotRequired };
                else if (tracking == TrackingTypes.None && l.LotId is not null) lineErrors[$"lines[{i}].lotId"] = new[] { MultiAdjustmentRules.LotNotAllowed };
                else if (l.LotId is int lotId
                         && !await db.Set<InventoryLot>().AsNoTracking().AnyAsync(x => x.LotId == lotId && x.ProductId == product.ProductId, ct2))
                    throw new NotFoundException("Lote");
                prepared.Add(new Prepared(i, l, product, bin.Bin, new BalanceKey(product.ProductId, warehouse.WarehouseId, bin.Bin.WarehouseBinId, l.LotId), l.CountedQty!.Value));
            }
            if (lineErrors.Count > 0) throw new ValidationException(lineErrors);

            // (2) saldos bloqueados en el orden de claves del ledger; diferencia contra lo que hay AHORA
            var stale = new Dictionary<string, string[]>();
            var below = new Dictionary<string, string[]>();
            var diffs = new List<(Prepared P, decimal System, decimal Diff)>();
            foreach (var p in prepared.OrderBy(x => x.Key))
            {
                await db.UpsertBalanceAsync(p.Key, ct2);
                var balance = await db.LockBalanceAsync(p.Key, ct2);
                var current = balance?.QtyOnHand ?? 0m;
                if (p.Input.SystemQty != current) stale[$"lines[{p.Index}]"] = new[] { MultiAdjustmentRules.Stale(p.Product.Sku, p.Bin.Code, current) };
                var diff = MultiAdjustmentRules.Difference(p.Counted, current);
                if (diff == 0) continue;
                var reserved = balance?.QtyReserved ?? 0m;
                if (p.Counted < reserved) below[$"lines[{p.Index}]"] = new[] { MultiAdjustmentRules.BelowReserved(p.Product.Sku, p.Bin.Code, reserved) };
                if (diff > 0 && !p.Product.IsActive) throw new StatusRuleException(InventoryAdjustmentService.ProductInactive(p.Product.Sku));
                diffs.Add((p, current, diff));
            }
            if (stale.Count > 0) throw new StaleException(stale.Values.First()[0], stale);
            if (below.Count > 0) throw new InsufficientStockException(below.Values.First()[0], below);
            if (diffs.Count == 0) throw new ValidationException("lines", MultiAdjustmentRules.NothingToAdjust);

            // (3) cabecera ANTES de los movimientos: el origen (MULTI_ADJUSTMENT + id) va en el INSERT del ledger
            var seq = await numbers.NextAsync(NumberKinds.MultiAdjust, CounterClientId, ct2);
            var (lineCount, productCount, up, down) = MultiAdjustmentRules.Summarize(diffs.Select(d => (d.P.Product.ProductId, d.Diff)));
            var doc = new MultiAdjustment
            {
                PublicId = Guid.NewGuid(), TenantId = tenantId, WarehouseId = warehouse.WarehouseId, Number = WmsNumbering.Format(NumberKinds.MultiAdjust, seq),
                Notes = notes!, LineCount = lineCount, ProductCount = productCount, UnitsUp = up, UnitsDown = down, CreatedAtUtc = DateTime.UtcNow, CreatedBy = tenant.UserId,
            };
            foreach (var d in diffs.OrderBy(d => d.P.Index))
                doc.Lines.Add(new MultiAdjustmentLine
                {
                    ProductId = d.P.Product.ProductId, WarehouseBinId = d.P.Bin.WarehouseBinId, LotId = d.P.Input.LotId,
                    SystemQty = d.System, CountedQty = d.P.Counted, Difference = d.Diff,
                });
            db.Add(doc);
            await db.SaveGuardedAsync(NumberTaken, ct2);

            // (4) movimientos: uno por línea con diferencia, en el orden de las líneas del documento
            var ordered = doc.Lines.ToList();
            var postings = ordered.Select(l => l.Difference > 0
                ? new InventoryPosting(InventoryTxnTypes.Adjustment, l.ProductId, l.Difference, LotId: l.LotId, ToWarehouseId: warehouse.WarehouseId, ToBinId: l.WarehouseBinId,
                    RefEntityType: EntityTypes.MultiAdjustment, RefId: doc.MultiAdjustmentId, ReasonCode: AdjustmentReasons.PhysicalCount, Notes: notes)
                : new InventoryPosting(InventoryTxnTypes.Adjustment, l.ProductId, -l.Difference, LotId: l.LotId, FromWarehouseId: warehouse.WarehouseId, FromBinId: l.WarehouseBinId,
                    RefEntityType: EntityTypes.MultiAdjustment, RefId: doc.MultiAdjustmentId, ReasonCode: AdjustmentReasons.PhysicalCount, Notes: notes)).ToList();
            var txnIds = (await ledger.PostAsync(postings, ct2)).ToList();
            for (var i = 0; i < ordered.Count; i++) ordered[i].InventoryTransactionId = txnIds[i];
            await db.SaveGuardedAsync(NumberTaken, ct2);
            return doc.MultiAdjustmentId;
        }, ct);

        return await DetailByIdAsync(id, ct);
    }

    // ================================================================ historial

    public async Task<MultiAdjustmentPageDto> ListAsync(MultiAdjustmentQuery q, CancellationToken ct)
    {
        q ??= new MultiAdjustmentQuery();
        var take = Math.Clamp(q.Take <= 0 ? 50 : q.Take, 1, MaxTake);
        var skip = Math.Max(0, q.Skip);
        var query = db.Set<MultiAdjustment>().AsNoTracking().AsQueryable();
        if (q.WarehousePublicIds is { Length: > 0 } whs)
        {
            var ids = await db.Set<Warehouse>().AsNoTracking().Where(w => whs.Contains(w.PublicId)).Select(w => w.WarehouseId).ToListAsync(ct);
            query = query.Where(a => ids.Contains(a.WarehouseId));
        }
        if (q.From is DateOnly from) { var f = from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc); query = query.Where(a => a.CreatedAtUtc >= f); }
        if (q.To is DateOnly to) { var t = to.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc); query = query.Where(a => a.CreatedAtUtc < t); }
        if (q.UserIds is { Length: > 0 } us) query = query.Where(a => a.CreatedBy.HasValue && us.Contains(a.CreatedBy.Value));
        var total = await query.CountAsync(ct);
        var docs = await query.OrderByDescending(a => a.CreatedAtUtc).ThenByDescending(a => a.MultiAdjustmentId).Skip(skip).Take(take).ToListAsync(ct);
        return new MultiAdjustmentPageDto(total, skip, take, await ToDtosAsync(docs, ct));
    }

    public async Task<MultiAdjustmentDetailDto> GetAsync(Guid publicId, CancellationToken ct)
    {
        var id = await db.Set<MultiAdjustment>().AsNoTracking().Where(a => a.PublicId == publicId).Select(a => (int?)a.MultiAdjustmentId).FirstOrDefaultAsync(ct)
                 ?? throw new NotFoundException(Label);
        return await DetailByIdAsync(id, ct);
    }

    /// <summary>Por id interno del documento (el origen del Kárdex trae ese id).</summary>
    public async Task<MultiAdjustmentDetailDto> GetByIdAsync(int id, CancellationToken ct)
    {
        if (!await db.Set<MultiAdjustment>().AsNoTracking().AnyAsync(a => a.MultiAdjustmentId == id, ct)) throw new NotFoundException(Label);
        return await DetailByIdAsync(id, ct);
    }

    private async Task<MultiAdjustmentDetailDto> DetailByIdAsync(int id, CancellationToken ct)
    {
        var doc = await db.Set<MultiAdjustment>().AsNoTracking().FirstAsync(a => a.MultiAdjustmentId == id, ct);
        var header = (await ToDtosAsync(new[] { doc }, ct))[0];
        var rows = await (from l in db.Set<MultiAdjustmentLine>().AsNoTracking()
                          join p in db.Set<Product>().AsNoTracking() on l.ProductId equals p.ProductId
                          join b in db.Set<WarehouseBin>().AsNoTracking() on l.WarehouseBinId equals b.WarehouseBinId
                          join z in db.Set<WarehouseZone>().AsNoTracking() on b.WarehouseZoneId equals z.WarehouseZoneId
                          where l.MultiAdjustmentId == id
                          orderby l.MultiAdjustmentLineId
                          select new { l, p.PublicId, p.Sku, p.Name, BinCode = b.Code, ZoneCode = z.Code }).ToListAsync(ct);
        var lotIds = rows.Where(r => r.l.LotId.HasValue).Select(r => r.l.LotId!.Value).Distinct().ToList();
        var lots = lotIds.Count == 0 ? new Dictionary<int, string>()
            : await db.Set<InventoryLot>().AsNoTracking().Where(x => lotIds.Contains(x.LotId)).ToDictionaryAsync(x => x.LotId, x => x.LotNumber, ct);
        return new MultiAdjustmentDetailDto(header, rows.Select(r => new MultiAdjustmentLineDto(r.PublicId, r.Sku, r.Name, r.l.WarehouseBinId, r.BinCode, r.ZoneCode,
            r.l.LotId, r.l.LotId.HasValue ? lots.GetValueOrDefault(r.l.LotId.Value) : null, r.l.SystemQty, r.l.CountedQty, r.l.Difference, r.l.InventoryTransactionId)).ToList());
    }

    private async Task<IReadOnlyList<MultiAdjustmentDto>> ToDtosAsync(IReadOnlyList<MultiAdjustment> docs, CancellationToken ct)
    {
        var whIds = docs.Select(d => d.WarehouseId).Distinct().ToList();
        var warehouses = await db.Set<Warehouse>().AsNoTracking().Where(w => whIds.Contains(w.WarehouseId)).ToDictionaryAsync(w => w.WarehouseId, ct);
        var userIds = docs.Where(d => d.CreatedBy.HasValue).Select(d => d.CreatedBy!.Value).Distinct().ToList();
        var users = userIds.Count == 0 ? new Dictionary<int, string>()
            : await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).Select(u => new { u.Id, Name = u.FullName ?? u.Email ?? u.UserName ?? "" })
                .ToDictionaryAsync(u => u.Id, u => u.Name, ct);
        return docs.Select(d => new MultiAdjustmentDto(d.MultiAdjustmentId, d.PublicId, d.Number, warehouses[d.WarehouseId].PublicId, warehouses[d.WarehouseId].Code, d.Notes,
            d.LineCount, d.ProductCount, d.UnitsUp, d.UnitsDown, d.CreatedAtUtc, d.CreatedBy, d.CreatedBy is int u ? users.GetValueOrDefault(u) : null)).ToList();
    }
}
