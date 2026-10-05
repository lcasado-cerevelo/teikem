using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Common;
using Teikem.Domain.Constants;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Clients;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Orders;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Wms;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 28 (Rentas R2, plan 4.5) — devolución de renta DRN-##### (D2: registro propio de la renta con enlace nulo al envío futuro
/// de recogido). Una devolución trae el motivo (por devolución, RentalReturnReason) y los equipos que vuelven, identificados por su
/// número de serie dentro de la renta.
/// - Solo una renta En renta; cada serie debe ser un equipo despachado y sin devolver de ESA renta (409 'La serie {s} no está en
///   renta en {n}.'). Motivo 'Otro' exige notas (400). La posición de destino no puede ser de la zona En renta (400); por defecto
///   la del encabezado y, sin ella, la posición de donde salió el equipo. Se puede devolver a otro almacén de la compañía.
/// - Por el ledger, una TRANSFER por equipo desde EN-RENTA (FromReserved, serie esperada ON_RENT) a la posición destino, con la
///   referencia RENTAL_RETURN: si el equipo pasa por proceso (por defecto sí) queda reservado y la serie IN_PROCESS (no cuenta como
///   disponible) y se abre su RentalProcess en el estatus inicial de RentalProcessStatus; si no, la serie queda AVAILABLE.
/// - Cuando vuelven todos los equipos despachados, la renta pasa a RETURNED por StatusService (RentalStatusEffect sella el cierre).
/// - Todo en RunInTransactionAsync. Orden de bloqueo: Rental (U) → NumberSequence → saldos → series (el contador RENTALRETURN solo
///   lo toma una devolución, que antes bloqueó su renta: sin ciclo).
/// El TenantId sale del principal; la devolución se expone por PublicId; los equipos y procesos se alcanzan por su renta filtrada.
/// </summary>
public sealed class RentalReturnService(
    TeikemDbContext db,
    ITenantContext tenant,
    ILookupCache lookups,
    StatusService statuses,
    INumberSequenceService numbers,
    InventoryLedger ledger,
    ITenantClock? clock = null)
{
    private readonly ITenantClock _clock = clock ?? TenantClock.Default;

    public const string ConcurrencyMessage = DbExtensions.ConcurrencyMessage;
    public const string NumberTakenMessage = "Ya existe una devolución de renta con ese número; intente de nuevo.";
    public const string LineTakenConcurrently = "Uno de los equipos se acaba de devolver en otra operación; recargue e intente de nuevo.";

    private sealed record LineRequest(string SerialNumber, int ConditionId, int? ToBinId, bool RequiresProcess, string? Notes);
    private sealed record BinTarget(int BinId, int WarehouseId, string Code, string? ZoneTypeCode);

    // ================================================================ alta

    public async Task<RentalReturnDto> CreateAsync(Guid rentalPublicId, RentalReturnCreateRequest req, CancellationToken ct)
    {
        if (req is null) throw new ValidationException("body", "El cuerpo de la solicitud es obligatorio.");

        // 1. Validación pura (400) antes de tocar la base.
        var errors = new Dictionary<string, string[]>();
        var (reasonCode, reasonError) = RentalRules.NormalizeReturnReason(req.Reason);
        if (reasonError is not null) errors["reason"] = new[] { reasonError };
        var (notes, notesError) = RentalRules.NormalizeText(req.Notes, RentalRules.MaxNotesLength, RentalRules.NotesTooLong);
        if (notesError is not null) errors["notes"] = new[] { notesError };
        else if (RentalRules.ValidateReturnNotes(reasonCode, notes) is { } otherError) errors["notes"] = new[] { otherError };
        if (req.EstimatedPickupCost is < 0m) errors["estimatedPickupCost"] = new[] { RentalRules.NegativePickupCost };
        int? currencyId = null;
        if (!string.IsNullOrWhiteSpace(req.TransportCurrency))
        {
            currencyId = await lookups.TryGetIdAsync(LookupDomains.Currency, req.TransportCurrency.Trim().ToUpperInvariant(), ct);
            if (currencyId is null) errors["transportCurrency"] = new[] { RentalRules.UnknownCurrency(req.TransportCurrency.Trim()) };
        }
        var today = _clock.Today;
        var returnedOn = req.ReturnedOn ?? today;
        if (RentalRules.ValidateReturnedOn(returnedOn, today) is { } dateError) errors["returnedOn"] = new[] { dateError };

        var lines = new List<LineRequest>();
        var conditionIds = new Dictionary<string, int>(StringComparer.Ordinal);
        if (req.Lines is not { Count: > 0 }) errors["lines"] = new[] { RentalRules.ReturnLinesRequired };
        else
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < req.Lines.Count; i++)
            {
                var l = req.Lines[i];
                var serial = l?.SerialNumber?.Trim();
                if (string.IsNullOrEmpty(serial)) { errors[$"lines[{i}].serialNumber"] = new[] { RentalRules.ReturnSerialRequired }; continue; }
                if (!seen.Add(serial)) { errors[$"lines[{i}].serialNumber"] = new[] { SerialRules.Duplicated(serial) }; continue; }
                var (conditionCode, conditionError) = RentalRules.NormalizeCondition(l!.Condition);
                if (conditionError is not null) { errors[$"lines[{i}].condition"] = new[] { conditionError }; continue; }
                var (lineNotes, lineNotesError) = RentalRules.NormalizeText(l.Notes, RentalRules.MaxReturnLineNotesLength, RentalRules.ReturnLineNotesTooLong);
                if (lineNotesError is not null) { errors[$"lines[{i}].notes"] = new[] { lineNotesError }; continue; }
                if (!conditionIds.TryGetValue(conditionCode!, out var conditionId))
                {
                    var found = await lookups.TryGetIdAsync(LookupDomains.RentalReturnCondition, conditionCode!, ct);
                    if (found is null) { errors[$"lines[{i}].condition"] = new[] { RentalRules.UnknownCondition(conditionCode!) }; continue; }
                    conditionIds[conditionCode!] = conditionId = found.Value;
                }
                lines.Add(new LineRequest(serial, conditionId, l.ToBinId ?? req.ToBinId, l.RequiresProcess ?? true, lineNotes));
            }
        }
        int? reasonId = null;
        if (reasonCode is not null)
        {
            reasonId = await lookups.TryGetIdAsync(LookupDomains.RentalReturnReason, reasonCode, ct);
            if (reasonId is null) errors["reason"] = new[] { RentalRules.UnknownReturnReason(reasonCode) };
        }
        // Un solo error: el mensaje va también como título (ProblemDetails) además de en su campo.
        if (errors.Count == 1 && errors.First().Value.Length == 1) throw new ValidationException(errors.First().Key, errors.First().Value[0]);
        if (errors.Count > 0) throw new ValidationException(errors);

        var rentalId = await db.Rentals.AsNoTracking().Where(r => r.PublicId == rentalPublicId).Select(r => (int?)r.RentalId).FirstOrDefaultAsync(ct)
                       ?? throw new NotFoundException(RentalRules.NotFound, feminine: true);
        await numbers.EnsureAsync(NumberKinds.RentalReturn, null, ct);

        var returnId = await db.RunInTransactionAsync(async ct2 =>
        {
            var tenantId = ((TenantContext)tenant).RequireTenantId();
            var rental = await db.LockRentalAsync(rentalId, ct2);
            db.ApplyRowVersion(rental, req.RowVersion);
            var code = await PurchasingSupport.StatusCodeOfAsync(db, rental.StatusCodeId, ct2);
            if (code != RentalStatuses.OnRent) throw new StatusRuleException(RentalRules.ReturnNotOnRent(rental.Number));
            if (RentalRules.ValidateReturnedOn(returnedOn, today, rental.StartDate) is { } startError)
                throw new ValidationException("returnedOn", startError);

            // 2. Equipos en renta (despachados, activos y sin devolver) de ESTA renta, por número de serie.
            // (Con seguimiento: las líneas se marcan devueltas; de la serie solo se proyectan columnas.)
            var open = await (from l in db.RentalLines
                              join s in db.InventorySerials on l.SerialId equals s.SerialId
                              where l.RentalId == rental.RentalId && l.IsActive && l.DispatchedAtUtc != null && l.ReturnedAtUtc == null
                              orderby l.RentalLineId
                              select new { Line = l, s.SerialNumber, s.CurrentWarehouseId, s.CurrentBinId }).ToListAsync(ct2);
            var bySerial = open.ToDictionary(x => x.SerialNumber, StringComparer.OrdinalIgnoreCase);
            var picked = new List<(RentalLine Line, string SerialNumber, int FromWarehouseId, int FromBinId, LineRequest Request)>();
            foreach (var r in lines)
            {
                if (!bySerial.TryGetValue(r.SerialNumber, out var x) || x.CurrentWarehouseId is not int fw || x.CurrentBinId is not int fb)
                    throw new ConflictException(RentalRules.SerialNotOnRent(r.SerialNumber, rental.Number));
                picked.Add((x.Line, x.SerialNumber, fw, fb, r));
            }

            // 3. Posiciones de destino (de la compañía; nunca de la zona En renta). Sin indicar: la posición de donde salió.
            var targetIds = picked.Select(p => p.Request.ToBinId ?? p.Line.FromBinId).Distinct().ToList();
            var targets = await TargetsAsync(targetIds, ct2);
            foreach (var id in targetIds)
            {
                if (!targets.TryGetValue(id, out var t)) throw WmsResolve.BinNotFound();
                if (t.ZoneTypeCode == ZoneTypes.Rental) throw new ValidationException("toBinId", RentalRules.DestinationInRentalZone);
            }

            // 4. Encabezado DRN-#####.
            var seq = await numbers.NextAsync(NumberKinds.RentalReturn, null, ct2);
            var now = DateTime.UtcNow;
            var ret = new RentalReturn
            {
                PublicId = Guid.NewGuid(), TenantId = tenantId, Number = WmsNumbering.Format(NumberKinds.RentalReturn, seq), RentalId = rental.RentalId,
                ReturnedOn = returnedOn, ReasonLookupId = reasonId!.Value, Notes = notes, EstimatedPickupCost = req.EstimatedPickupCost,
                TransportCurrencyLookupId = currencyId, PickupShipmentId = null, CreatedAtUtc = now, CreatedBy = tenant.UserId,
            };
            db.RentalReturns.Add(ret);
            await db.SaveGuardedAsync(NumberTakenMessage, ct2);

            // 5. Ledger: TRANSFER desde EN-RENTA (consume lo reservado; serie esperada ON_RENT) a la posición destino.
            var movementNotes = RentalRules.ReturnNotes(ret.Number, rental.Number);
            var postings = picked.Select(p =>
            {
                var target = targets[p.Request.ToBinId ?? p.Line.FromBinId];
                return new InventoryPosting(InventoryTxnTypes.Transfer, p.Line.ProductId, 1m, LotId: p.Line.LotId, SerialNumber: p.SerialNumber,
                    FromWarehouseId: p.FromWarehouseId, FromBinId: p.FromBinId, ToWarehouseId: target.WarehouseId, ToBinId: target.BinId,
                    RefEntityType: EntityTypes.RentalReturn, RefId: ret.RentalReturnId, Notes: movementNotes, FromReserved: true,
                    ExpectedSerialStatus: SerialStatuses.OnRent, TargetSerialStatus: RentalRules.ReturnSerialStatus(p.Request.RequiresProcess),
                    ReserveAtDestination: p.Request.RequiresProcess);
            }).ToList();
            var txnIds = await ledger.PostAsync(postings, ct2);

            // 6. Equipos devueltos (UQ por equipo de la renta) y la marca de devuelto en la renta.
            var returnLines = new List<(RentalReturnLine ReturnLine, RentalLine Line, LineRequest Request, BinTarget Target)>();
            for (var i = 0; i < picked.Count; i++)
            {
                var p = picked[i];
                var target = targets[p.Request.ToBinId ?? p.Line.FromBinId];
                var rl = new RentalReturnLine
                {
                    RentalReturnId = ret.RentalReturnId, RentalLineId = p.Line.RentalLineId, ConditionLookupId = p.Request.ConditionId,
                    ToWarehouseId = target.WarehouseId, ToBinId = target.BinId, RequiresProcess = p.Request.RequiresProcess, ReturnTxnId = txnIds[i],
                    Notes = p.Request.Notes,
                };
                db.RentalReturnLines.Add(rl);
                p.Line.ReturnedAtUtc = now;
                returnLines.Add((rl, p.Line, p.Request, target));
            }
            await db.SaveGuardedAsync(LineTakenConcurrently, ct2);

            // 7. Un proceso por equipo que lo requiere, en el estatus inicial (configurable) de RentalProcessStatus.
            var withProcess = returnLines.Where(x => x.Request.RequiresProcess).ToList();
            if (withProcess.Count > 0)
            {
                var initial = await statuses.GetInitialAsync(StatusDomains.RentalProcessStatus, ct2);
                var processes = withProcess.Select(x => new RentalProcess
                {
                    TenantId = tenantId, SerialId = x.Line.SerialId, ProductId = x.Line.ProductId, WarehouseId = x.Target.WarehouseId, BinId = x.Target.BinId,
                    RentalReturnLineId = x.ReturnLine.RentalReturnLineId, StatusCodeId = initial.StatusCodeId, StartedAtUtc = now, Notes = x.Request.Notes,
                }).ToList();
                db.RentalProcesses.AddRange(processes);
                await db.SaveGuardedAsync(ConcurrencyMessage, ct2);
                foreach (var process in processes)
                {
                    var born = await statuses.TransitionAsync(StatusDomains.RentalProcessStatus, EntityTypes.RentalProcess, process.RentalProcessId,
                        null, initial.InternalCode, null, ct2);
                    process.StatusCodeId = born.StatusCodeId;
                }
            }

            // 8. ¿Volvieron todos los equipos despachados? → la renta pasa a Devuelta (el efecto sella ClosedAtUtc).
            if (open.Count == picked.Count)
            {
                var to = await statuses.TransitionAsync(StatusDomains.RentalStatus, EntityTypes.Rental, rental.RentalId, rental.StatusCodeId,
                    RentalStatuses.Returned, $"Devolución {ret.Number}", ct2);
                rental.StatusCodeId = to.StatusCodeId;
            }
            else rental.UpdatedAtUtc = now;   // la ficha cambia (equipos devueltos): nueva RowVersion
            await db.SaveGuardedAsync(ConcurrencyMessage, ct2);
            return ret.RentalReturnId;
        }, ct);
        return await GetByIdAsync(returnId, ct);
    }

    // ================================================================ lista y ficha

    public async Task<RentalReturnPageDto> ListAsync(RentalReturnQuery? q, CancellationToken ct)
    {
        q ??= new RentalReturnQuery();
        var skip = Math.Max(0, q.Skip);
        var take = q.Take <= 0 ? 100 : Math.Min(q.Take, RentalRules.MaxPageSize);
        var query = db.RentalReturns.AsNoTracking();
        if (q.RentalPublicId is Guid rentalPublicId)
        {
            var rentalId = await db.Rentals.AsNoTracking().Where(r => r.PublicId == rentalPublicId).Select(r => (int?)r.RentalId).FirstOrDefaultAsync(ct)
                           ?? throw new NotFoundException(RentalRules.NotFound, feminine: true);
            query = query.Where(r => r.RentalId == rentalId);
        }
        if (q.ClientPublicId is Guid clientPublicId)
        {
            var client = await db.ResolveClientAsync(clientPublicId, ct);
            query = query.Where(r => db.Rentals.Any(x => x.RentalId == r.RentalId && x.ClientId == client.ClientId));
        }
        if (q.Reason is { Length: > 0 })
        {
            var ids = new List<int>();
            foreach (var code in q.Reason.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim().ToUpperInvariant()).Distinct())
                if (await lookups.TryGetIdAsync(LookupDomains.RentalReturnReason, code, ct) is int id) ids.Add(id);
            query = query.Where(r => ids.Contains(r.ReasonLookupId));
        }
        if (q.From is DateOnly from) query = query.Where(r => r.ReturnedOn >= from);
        if (q.To is DateOnly to) query = query.Where(r => r.ReturnedOn <= to);
        if (q.Early is bool early)
            query = early
                ? query.Where(r => db.Rentals.Any(x => x.RentalId == r.RentalId && r.ReturnedOn < x.PickupDate))
                : query.Where(r => db.Rentals.Any(x => x.RentalId == r.RentalId && r.ReturnedOn >= x.PickupDate));
        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var s = q.Search.Trim();
            query = query.Where(r => r.Number.Contains(s)
                || db.Rentals.Any(x => x.RentalId == r.RentalId && x.Number.Contains(s))
                || (from rl in db.RentalReturnLines
                    join l in db.RentalLines on rl.RentalLineId equals l.RentalLineId
                    join se in db.InventorySerials on l.SerialId equals se.SerialId
                    where rl.RentalReturnId == r.RentalReturnId && se.SerialNumber.Contains(s)
                    select rl.RentalReturnLineId).Any());
        }
        var total = await query.CountAsync(ct);
        var page = await query.OrderByDescending(r => r.RentalReturnId).Skip(skip).Take(take).ToListAsync(ct);
        return new RentalReturnPageDto(total, skip, take, await ToListItemsAsync(page, ct));
    }

    public async Task<RentalReturnDto> GetAsync(Guid publicId, CancellationToken ct)
    {
        var ret = await db.RentalReturns.AsNoTracking().FirstOrDefaultAsync(r => r.PublicId == publicId, ct)
                  ?? throw new NotFoundException(RentalRules.ReturnNotFound, feminine: true);
        return await ToDtoAsync(ret, ct);
    }

    private async Task<RentalReturnDto> GetByIdAsync(int id, CancellationToken ct)
    {
        var ret = await db.RentalReturns.AsNoTracking().FirstOrDefaultAsync(r => r.RentalReturnId == id, ct)
                  ?? throw new NotFoundException(RentalRules.ReturnNotFound, feminine: true);
        return await ToDtoAsync(ret, ct);
    }

    // ================================================================ apoyo

    /// <summary>Posiciones (de almacenes de la compañía, filtrados) con su almacén, código y tipo de zona.</summary>
    private async Task<Dictionary<int, BinTarget>> TargetsAsync(IReadOnlyCollection<int> binIds, CancellationToken ct)
    {
        var ids = binIds.ToList();
        var rows = await (from b in db.WarehouseBins.AsNoTracking()
                          join w in db.Warehouses.AsNoTracking() on b.WarehouseId equals w.WarehouseId
                          join z in db.WarehouseZones.AsNoTracking() on b.WarehouseZoneId equals z.WarehouseZoneId
                          where ids.Contains(b.WarehouseBinId)
                          select new { b.WarehouseBinId, b.WarehouseId, b.Code, z.ZoneTypeLookupId }).ToListAsync(ct);
        var result = new Dictionary<int, BinTarget>();
        foreach (var r in rows)
        {
            var zoneType = r.ZoneTypeLookupId is int zt ? (await lookups.GetAsync(zt, ct))?.InternalCode : null;
            result[r.WarehouseBinId] = new BinTarget(r.WarehouseBinId, r.WarehouseId, r.Code, zoneType);
        }
        return result;
    }

    private async Task<string> LabelAsync(int lookupId, CancellationToken ct)
    {
        var l = await lookups.GetAsync(lookupId, ct);
        return l is null ? string.Empty : MultilingualText.Resolve(l.LabelJson, tenant.Lang);
    }

    private async Task<IReadOnlyList<RentalReturnListItemDto>> ToListItemsAsync(List<RentalReturn> rows, CancellationToken ct)
    {
        if (rows.Count == 0) return Array.Empty<RentalReturnListItemDto>();
        var returnIds = rows.Select(r => r.RentalReturnId).ToList();
        var rentalIds = rows.Select(r => r.RentalId).Distinct().ToList();
        var rentals = await db.Rentals.AsNoTracking().Where(r => rentalIds.Contains(r.RentalId))
            .Select(r => new { r.RentalId, r.PublicId, r.Number, r.ClientId, r.PickupDate }).ToDictionaryAsync(r => r.RentalId, ct);
        var clientIds = rentals.Values.Select(r => r.ClientId).Distinct().ToList();
        var clients = await db.Clients.AsNoTracking().Where(c => clientIds.Contains(c.ClientId))
            .Select(c => new { c.ClientId, c.PublicId, c.Name }).ToDictionaryAsync(c => c.ClientId, ct);
        var units = await db.RentalReturnLines.AsNoTracking().Where(l => returnIds.Contains(l.RentalReturnId))
            .GroupBy(l => l.RentalReturnId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var openProcesses = await (from p in db.RentalProcesses.AsNoTracking()
                                   join l in db.RentalReturnLines.AsNoTracking() on p.RentalReturnLineId equals (int?)l.RentalReturnLineId
                                   where returnIds.Contains(l.RentalReturnId) && p.CompletedAtUtc == null
                                   group p by l.RentalReturnId into g
                                   select new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var result = new List<RentalReturnListItemDto>(rows.Count);
        foreach (var r in rows)
        {
            var rental = rentals.GetValueOrDefault(r.RentalId);
            var client = rental is null ? null : clients.GetValueOrDefault(rental.ClientId);
            var reason = await lookups.GetAsync(r.ReasonLookupId, ct);
            result.Add(new RentalReturnListItemDto(r.RentalReturnId, r.PublicId, r.Number, rental?.PublicId ?? Guid.Empty, rental?.Number ?? string.Empty,
                client?.PublicId ?? Guid.Empty, client?.Name ?? string.Empty, r.ReturnedOn, reason?.InternalCode ?? string.Empty,
                reason is null ? string.Empty : MultilingualText.Resolve(reason.LabelJson, tenant.Lang),
                rental is not null && RentalRules.IsEarlyReturn(r.ReturnedOn, rental.PickupDate), units.GetValueOrDefault(r.RentalReturnId),
                openProcesses.GetValueOrDefault(r.RentalReturnId), r.CreatedAtUtc));
        }
        return result;
    }

    private async Task<RentalReturnDto> ToDtoAsync(RentalReturn ret, CancellationToken ct)
    {
        var item = (await ToListItemsAsync(new List<RentalReturn> { ret }, ct))[0];
        var rentalStatusId = await db.Rentals.AsNoTracking().Where(r => r.RentalId == ret.RentalId).Select(r => r.StatusCodeId).FirstOrDefaultAsync(ct);
        var rentalStatus = await PurchasingSupport.StatusCodeOfAsync(db, rentalStatusId, ct);
        var rows = await (from rl in db.RentalReturnLines.AsNoTracking()
                          join l in db.RentalLines.AsNoTracking() on rl.RentalLineId equals l.RentalLineId
                          join p in db.Products.AsNoTracking() on l.ProductId equals p.ProductId
                          join s in db.InventorySerials.AsNoTracking() on l.SerialId equals s.SerialId
                          join b in db.WarehouseBins.AsNoTracking() on rl.ToBinId equals b.WarehouseBinId
                          join w in db.Warehouses.AsNoTracking() on rl.ToWarehouseId equals w.WarehouseId
                          where rl.RentalReturnId == ret.RentalReturnId
                          orderby rl.RentalReturnLineId
                          select new { Row = rl, ProductPublicId = p.PublicId, p.Sku, p.Name, l.SerialId, s.SerialNumber, BinCode = b.Code,
                              WarehousePublicId = w.PublicId, WarehouseCode = w.Code }).ToListAsync(ct);
        var lineIds = rows.Select(r => (int?)r.Row.RentalReturnLineId).ToList();
        var processes = await db.RentalProcesses.AsNoTracking().Where(p => lineIds.Contains(p.RentalReturnLineId))
            .Select(p => new { p.RentalProcessId, p.RentalReturnLineId, p.StatusCodeId }).ToListAsync(ct);
        var processStatuses = await PurchasingSupport.StatusMapAsync(db, tenant, StatusDomains.RentalProcessStatus, ct);
        var lines = new List<RentalReturnLineDto>(rows.Count);
        foreach (var x in rows)
        {
            var condition = await lookups.GetAsync(x.Row.ConditionLookupId, ct);
            var process = processes.FirstOrDefault(p => p.RentalReturnLineId == x.Row.RentalReturnLineId);
            var ps = process is null ? null : processStatuses.GetValueOrDefault(process.StatusCodeId);
            lines.Add(new RentalReturnLineDto(x.Row.RentalReturnLineId, x.Row.RentalLineId, x.ProductPublicId, x.Sku, x.Name, x.SerialId, x.SerialNumber,
                condition?.InternalCode ?? string.Empty, condition is null ? string.Empty : MultilingualText.Resolve(condition.LabelJson, tenant.Lang),
                x.WarehousePublicId, x.WarehouseCode, x.Row.ToBinId, x.BinCode, x.Row.RequiresProcess, x.Row.ReturnTxnId, x.Row.Notes,
                process?.RentalProcessId, ps?.Code, ps?.Label));
        }
        var currency = ret.TransportCurrencyLookupId is int cid ? (await lookups.GetAsync(cid, ct))?.InternalCode : null;
        string? createdByName = null;
        if (ret.CreatedBy is int uid)
            createdByName = await db.Users.AsNoTracking().Where(u => u.Id == uid).Select(u => u.FullName ?? u.Email).FirstOrDefaultAsync(ct);
        return new RentalReturnDto(item, rentalStatus, ret.Notes, ret.EstimatedPickupCost, currency, ret.PickupShipmentId, ret.CreatedBy, createdByName, lines);
    }
}
