using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Common;
using Teikem.Domain.Constants;
using Teikem.Domain.ExitNotices;
using Teikem.Domain.Orders;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Wms;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 143 — despachar contra un aviso de salida. UNA transacción: consume la reserva, saca el inventario por el ledger (movimiento Issue con la recolección como referencia),
/// crea la recolección enlazada al aviso (PickBatch.ExitNoticeId / PickBatchLine.ExitNoticeLineId), sube lo despachado, baja lo reservado y deja el aviso en parcial o despachado.
/// Por partes; nunca más de lo reservado. Sin posición se toma de lo que reservó el sistema (que ya sigue la rotación); con otra posición la reserva SE MUEVE a ella (se reserva
/// ahí y se libera lo equivalente), salvo que la compañía tenga la rotación en «Obligar»: entonces solo vale lo reservado (409 si no).
/// </summary>
public sealed partial class ExitNoticeService
{
    private sealed record Pick(int LineId, int ProductId, int BinId, int? LotId, decimal Quantity);

    public async Task<ExitNoticeDispatchResultDto> DispatchAsync(Guid publicId, ExitNoticeDispatchRequest? req, CancellationToken ct)
    {
        var requested = req?.Lines ?? Array.Empty<ExitNoticeDispatchLineRequest>();
        if (requested.Count == 0) throw new ValidationException("lines", ExitNoticeRules.DispatchLinesRequired);
        var seen = new HashSet<int>();
        for (var i = 0; i < requested.Count; i++)
        {
            var d = requested[i];
            if (d.LineId is null) throw new ValidationException($"lines[{i}].lineId", ExitNoticeRules.DispatchLineRequired);
            if (!seen.Add(d.LineId.Value)) throw new ValidationException($"lines[{i}].lineId", ExitNoticeRules.DispatchLineRepeated);
            var qe = ExitNoticeRules.QuantityError(d.Quantity);
            if (qe is not null) throw new ValidationException($"lines[{i}].quantity", qe);
            if (d.LotId is not null && d.BinId is null) throw new ValidationException($"lines[{i}].lotId", ExitNoticeRules.LotWithoutBin);
        }
        var (note, noteError) = ExitNoticeRules.NormalizeText(req?.Note, ExitNoticeRules.MaxNotesLength, ExitNoticeRules.NotesTooLong);
        if (noteError is not null) throw new ValidationException("note", noteError);

        await numbers.EnsureAsync(NumberKinds.PackBatch, null, ct);
        Guid batchId = Guid.Empty;
        string batchNumber = string.Empty;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await db.RunInTransactionAsync(async ct2 =>
                {
                    var tenantId = T.RequireTenantId();
                    var notice = await db.Set<ExitNotice>().AsTracking().Include(o => o.Lines).FirstOrDefaultAsync(o => o.PublicId == publicId, ct2) ?? throw NotFound();
                    var code = (await StatusMapAsync(ct2))[notice.StatusCodeId].Code;
                    if (code is not (ExitNoticeStatuses.Requested or ExitNoticeStatuses.Partial)) throw new StatusRuleException(ExitNoticeRules.NotDispatchable);

                    var lineIds = notice.Lines.Select(l => l.ExitNoticeLineId).ToList();
                    var rows = await db.Set<ExitNoticeReservation>().AsTracking().Where(r => lineIds.Contains(r.ExitNoticeLineId)).OrderBy(r => r.ExitNoticeReservationId).ToListAsync(ct2);
                    var productIds = notice.Lines.Select(l => l.ProductId).Distinct().ToList();
                    var products = await db.Set<Product>().AsNoTracking().Where(p => productIds.Contains(p.ProductId)).ToDictionaryAsync(p => p.ProductId, ct2);
                    var lotTracked = await RotationQueries.LotTrackedAsync(db, productIds, ct2);
                    var enforced = RotationRules.IsEnforced(await db.Tenants.AsNoTracking().Where(t => t.TenantId == db.CurrentTenantId).Select(t => t.RotationEnforcement).FirstOrDefaultAsync(ct2));

                    // 1) cantidades contra lo reservado
                    foreach (var d in requested)
                    {
                        var line = notice.Lines.FirstOrDefault(l => l.ExitNoticeLineId == d.LineId!.Value) ?? throw new NotFoundException("Línea", feminine: true);
                        if (d.Quantity!.Value > line.QtyReserved) throw new ConflictException(ExitNoticeRules.DispatchMoreThanReserved(products[line.ProductId].Sku, line.QtyReserved));
                    }

                    // 2) de dónde sale cada línea (y mover la reserva si el operario tomó de otra posición)
                    var picks = new List<Pick>();
                    foreach (var d in requested)
                    {
                        var line = notice.Lines.First(l => l.ExitNoticeLineId == d.LineId!.Value);
                        var mine = rows.Where(r => r.ExitNoticeLineId == line.ExitNoticeLineId && r.Quantity > 0m).ToList();
                        var q = d.Quantity!.Value;
                        if (d.BinId is null)
                        {
                            var left = q;
                            foreach (var r in mine)
                            {
                                if (left <= 0m) break;
                                var take = Math.Min(r.Quantity, left);
                                picks.Add(new Pick(line.ExitNoticeLineId, line.ProductId, r.WarehouseBinId, r.LotId, take));
                                left -= take;
                            }
                            continue;
                        }

                        var bin = d.BinId.Value;
                        var binCode = await EnsurePickableBinAsync(notice.WarehouseId, bin, ct2);
                        // lo que ya tiene reservado en esa posición (y lote, si se indicó)
                        var here = mine.Where(r => r.WarehouseBinId == bin && (d.LotId is null || r.LotId == d.LotId)).ToList();
                        var covered = here.Sum(r => r.Quantity);
                        var extra = q - Math.Min(q, covered);
                        if (extra > 0m)
                        {
                            if (enforced)
                            {
                                var reservedBins = await db.Set<WarehouseBin>().AsNoTracking().Where(b => mine.Select(r => r.WarehouseBinId).Contains(b.WarehouseBinId)).Select(b => b.Code).Distinct().ToListAsync(ct2);
                                throw new ConflictException(RotationRules.EnforcedMismatch(products[line.ProductId].Sku, string.Join(", ", reservedBins)));
                            }
                            if (lotTracked.Contains(line.ProductId) && d.LotId is null)
                                throw new ValidationException("lines", $"Indique el lote de donde sale {products[line.ProductId].Sku} en la posición {binCode}.");
                            // la reserva se mueve: se reserva en la posición escogida (el ledger verifica el saldo) y se libera lo equivalente de las demás
                            await ledger.ReserveAsync(new[] { new StockReservation(line.ProductId, notice.WarehouseId, bin, d.LotId, extra) }, ct2);
                            var toFree = extra;
                            var release = new List<StockReservation>();
                            foreach (var r in mine.Where(r => !(r.WarehouseBinId == bin && (d.LotId is null || r.LotId == d.LotId))).OrderByDescending(r => r.ExitNoticeReservationId))
                            {
                                if (toFree <= 0m) break;
                                var take = Math.Min(r.Quantity, toFree);
                                release.Add(new StockReservation(line.ProductId, r.WarehouseId, r.WarehouseBinId, r.LotId, take));
                                r.Quantity -= take;
                                if (r.Quantity <= 0m) db.Set<ExitNoticeReservation>().Remove(r);   // CHECK Quantity > 0: la fila vacía se borra al instante
                                toFree -= take;
                            }
                            await ledger.ReleaseAsync(release, ct2);
                            db.Set<ExitNoticeReservation>().Add(new ExitNoticeReservation { TenantId = notice.TenantId, ExitNoticeLineId = line.ExitNoticeLineId, WarehouseId = notice.WarehouseId, WarehouseBinId = bin, LotId = d.LotId, Quantity = extra });
                            await db.SaveGuardedAsync(ExitNoticeRules.Concurrency, ct2);
                            rows = await db.Set<ExitNoticeReservation>().AsTracking().Where(r => lineIds.Contains(r.ExitNoticeLineId)).OrderBy(r => r.ExitNoticeReservationId).ToListAsync(ct2);
                            here = rows.Where(r => r.ExitNoticeLineId == line.ExitNoticeLineId && r.WarehouseBinId == bin && (d.LotId is null || r.LotId == d.LotId)).ToList();
                        }
                        // sale de lo reservado en esa posición (por lote, en el orden en que se reservó)
                        var need = q;
                        foreach (var r in here)
                        {
                            if (need <= 0m) break;
                            var take = Math.Min(r.Quantity, need);
                            picks.Add(new Pick(line.ExitNoticeLineId, line.ProductId, bin, r.LotId, take));
                            need -= take;
                        }
                    }

                    // 3) se consume la reserva y sale el inventario
                    foreach (var pk in picks)
                    {
                        var left = pk.Quantity;
                        foreach (var r in rows.Where(r => r.ExitNoticeLineId == pk.LineId && r.WarehouseBinId == pk.BinId && r.LotId == pk.LotId && r.Quantity > 0m).ToList())
                        {
                            if (left <= 0m) break;
                            var take = Math.Min(r.Quantity, left);
                            r.Quantity -= take;
                            if (r.Quantity <= 0m) db.Set<ExitNoticeReservation>().Remove(r);   // nunca una fila con cantidad cero hacia la base
                            left -= take;
                        }
                    }
                    await ledger.ReleaseAsync(picks.Select(pk => new StockReservation(pk.ProductId, notice.WarehouseId, pk.BinId, pk.LotId, pk.Quantity)).ToList(), ct2);

                    var initial = await statuses.GetInitialAsync(StatusDomains.PickBatchStatus, ct2);
                    var seq = await numbers.NextAsync(NumberKinds.PackBatch, null, ct2);
                    var batch = new PickBatch
                    {
                        PublicId = Guid.NewGuid(), TenantId = tenantId, WarehouseId = notice.WarehouseId, Number = NumberingRules.Resolve(NumberingRules.PackBatchPattern, seq),
                        StatusCodeId = initial.StatusCodeId, CollectedAtUtc = DateTime.UtcNow, CollectedBy = tenant.UserId, IsActive = true, ExitNoticeId = notice.ExitNoticeId, Note = note,
                    };
                    db.Set<PickBatch>().Add(batch);
                    await db.SaveGuardedAsync(ExitNoticeRules.NumberTaken, ct2);
                    var born = await statuses.TransitionAsync(StatusDomains.PickBatchStatus, EntityTypes.PickBatch, batch.PickBatchId, null, initial.InternalCode, null, ct2);
                    batch.StatusCodeId = born.StatusCodeId;

                    var postings = picks.Select(pk => new InventoryPosting(InventoryTxnTypes.Issue, pk.ProductId, pk.Quantity, LotId: pk.LotId, FromWarehouseId: notice.WarehouseId, FromBinId: pk.BinId,
                        RefEntityType: EntityTypes.PickBatch, RefId: batch.PickBatchId, Notes: $"{batch.Number} · {notice.Number}")).ToList();
                    var txnIds = await ledger.PostAsync(postings, ct2);
                    var issueCosts = valuation is null ? null : await valuation.IssueCostsAsync(picks.Select(p => (p.ProductId, p.Quantity)).ToList(), ct2);   // Lote 148
                    for (var i = 0; i < picks.Count; i++)
                        db.Set<PickBatchLine>().Add(new PickBatchLine
                        {
                            PickBatchId = batch.PickBatchId, ProductId = picks[i].ProductId, LotId = picks[i].LotId, FromBinId = picks[i].BinId, Quantity = picks[i].Quantity,
                            UnitCost = issueCosts is null ? products[picks[i].ProductId].PurchaseCost : issueCosts[i], IssueTxnId = txnIds[i], ExitNoticeLineId = picks[i].LineId,
                        });

                    // 4) lo despachado y lo reservado de cada línea; el estatus del aviso
                    foreach (var line in notice.Lines)
                    {
                        var shipped = picks.Where(pk => pk.LineId == line.ExitNoticeLineId).Sum(pk => pk.Quantity);
                        if (shipped > 0m) (line.QtyShipped, line.QtyReserved) = (line.QtyShipped + shipped, line.QtyReserved - shipped);
                    }
                    await RefreshStatusAsync(notice, "Despacho " + batch.Number, ct2);
                    await db.SaveGuardedAsync(ExitNoticeRules.Concurrency, ct2);
                    batchId = batch.PublicId;
                    batchNumber = batch.Number;
                }, ct);
                break;
            }
            catch (InsufficientStockException) when (attempt < MaxReserveAttempts)
            {
                db.ChangeTracker.Clear();   // otro documento tomó el saldo de la posición escogida: se reintenta con la foto nueva
            }
        }
        return new ExitNoticeDispatchResultDto(batchId, batchNumber, await GetAsync(publicId, ct));
    }

    /// <summary>La posición es del almacén del aviso, está activa y es recolectable (sin cuarentena, cruce de muelle ni renta). Devuelve su código.</summary>
    private async Task<string> EnsurePickableBinAsync(int warehouseId, int binId, CancellationToken ct)
    {
        var info = await (from b in db.Set<WarehouseBin>().AsNoTracking()
                          join z in db.Set<WarehouseZone>().AsNoTracking() on b.WarehouseZoneId equals z.WarehouseZoneId
                          where b.WarehouseBinId == binId && b.WarehouseId == warehouseId
                          select new { b.Code, Active = b.IsActive && z.IsActive, z.ZoneTypeLookupId }).FirstOrDefaultAsync(ct);
        if (info is null) throw new NotFoundException("Posición", feminine: true);
        var zone = info.ZoneTypeLookupId is int zt ? (await lookups.GetAsync(zt, ct))?.InternalCode : null;
        if (!info.Active || !PickBatchRules.IsPickableZone(zone)) throw new StatusRuleException(ExitNoticeRules.BinNotPickable(info.Code));
        return info.Code;
    }

    /// <summary>Despachos (recolecciones) de cada aviso; los deshechos salen como CANCELLED.</summary>
    private async Task<Dictionary<int, List<ExitNoticeDispatchInfoDto>>> DispatchesOfAsync(IReadOnlyCollection<int> noticeIds, CancellationToken ct)
    {
        var batches = await db.Set<PickBatch>().AsNoTracking().Where(b => b.ExitNoticeId != null && noticeIds.Contains(b.ExitNoticeId.Value)).OrderBy(b => b.PickBatchId).ToListAsync(ct);
        if (batches.Count == 0) return new();
        var ids = batches.Select(b => b.PickBatchId).ToList();
        var qty = await db.Set<PickBatchLine>().AsNoTracking().Where(l => ids.Contains(l.PickBatchId)).GroupBy(l => l.PickBatchId).Select(g => new { g.Key, Total = g.Sum(l => l.Quantity) }).ToDictionaryAsync(x => x.Key, x => x.Total, ct);
        var statusIds = batches.Select(b => b.StatusCodeId).Distinct().ToList();
        var codes = await db.StatusCodes.AsNoTracking().Where(s => statusIds.Contains(s.StatusCodeId)).ToDictionaryAsync(s => s.StatusCodeId, s => s.InternalCode, ct);
        return batches.GroupBy(b => b.ExitNoticeId!.Value).ToDictionary(g => g.Key, g => g.Select(b =>
            new ExitNoticeDispatchInfoDto(b.PublicId, b.Number, codes.GetValueOrDefault(b.StatusCodeId, string.Empty), b.CollectedAtUtc, qty.GetValueOrDefault(b.PickBatchId))).ToList());
    }

    // ---------------------------------------------------------------- deshacer una recolección de un aviso (gancho de PickBatchService)

    /// <summary>
    /// Se eliminó una recolección que atendía este aviso (la existencia ya volvió a su posición, en esta misma transacción): baja lo despachado de cada línea y, si el aviso no
    /// está cancelado, VUELVE A RESERVAR esa cantidad en la misma posición y lote. Un aviso despachado o parcial se reabre (parcial / solicitado).
    /// </summary>
    public async Task OnPickBatchReversedAsync(int exitNoticeId, string pickBatchNumber, IReadOnlyList<ExitNoticeReversedLine> reversed, CancellationToken ct)
    {
        var notice = await db.Set<ExitNotice>().AsTracking().Include(o => o.Lines).FirstOrDefaultAsync(o => o.ExitNoticeId == exitNoticeId, ct);
        if (notice is null || reversed.Count == 0) return;
        var code = (await StatusMapAsync(ct))[notice.StatusCodeId].Code;
        var reserve = code != ExitNoticeStatuses.Cancelled;
        var reservations = new List<StockReservation>();
        foreach (var r in reversed)
        {
            var line = notice.Lines.FirstOrDefault(l => l.ExitNoticeLineId == r.ExitNoticeLineId);
            if (line is null) continue;
            var back = Math.Min(r.Quantity, line.QtyShipped);
            if (back <= 0m) continue;
            (line.QtyShipped, line.QtyReserved) = (line.QtyShipped - back, reserve ? line.QtyReserved + back : line.QtyReserved);
            if (!reserve) continue;
            reservations.Add(new StockReservation(r.ProductId, notice.WarehouseId, r.BinId, r.LotId, back));
            db.Set<ExitNoticeReservation>().Add(new ExitNoticeReservation { TenantId = notice.TenantId, ExitNoticeLineId = line.ExitNoticeLineId, WarehouseId = notice.WarehouseId, WarehouseBinId = r.BinId, LotId = r.LotId, Quantity = back });
        }
        await ledger.ReserveAsync(reservations, ct);
        if (reserve)
        {
            var anyShipped = notice.Lines.Any(l => l.QtyShipped > 0m);
            var anyPending = notice.Lines.Any(l => l.QtyReserved > 0m || l.QtyBackordered > 0m);
            string? target = null;
            if (code == ExitNoticeStatuses.Shipped) target = anyShipped ? ExitNoticeStatuses.Partial : ExitNoticeStatuses.Requested;
            else if (code == ExitNoticeStatuses.Partial && !anyShipped) target = ExitNoticeStatuses.Requested;
            if (target is not null && anyPending)
            {
                var to = await statuses.TransitionAsync(ExitNoticeCodes.StatusDomain, ExitNoticeCodes.EntityType, notice.ExitNoticeId, notice.StatusCodeId, target, "Se deshizo el despacho " + pickBatchNumber, ct);
                notice.StatusCodeId = to.StatusCodeId;
            }
        }
        await db.SaveGuardedAsync(ExitNoticeRules.Concurrency, ct);
    }
}
