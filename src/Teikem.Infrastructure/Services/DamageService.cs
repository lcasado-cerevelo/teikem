using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Catalogs;
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
/// 2026-10-08 — Daños. Un reporte nace REPORTED y de inmediato pasa a QUARANTINED (la mercancía se manda a una posición de cuarentena) o a DISCARDED
/// (se desecha de una vez); desde cuarentena se desecha después o se recupera a una posición de guardado. El inventario lo mueven SIEMPRE
/// InventoryAdjustmentService/InventoryLedger (nunca esta tabla), todo en una transacción:
/// - Daño del ALMACÉN (la mercancía ya está en inventario): cuarentena = transferencia de su posición a la de cuarentena; desechar = ajuste negativo con
///   motivo DAMAGE.
/// - Daño de un RECIBO (llegó dañado; las unidades no se recibieron como buenas): cuarentena = ajuste positivo con motivo DAMAGE en la posición de
///   cuarentena; desechar de una vez = ningún movimiento (nunca entró al inventario), solo queda el reporte.
/// - Desde cuarentena: desechar = ajuste negativo DAMAGE; recuperar = transferencia a una posición de guardado.
/// La causa (vino así / accidente en el camino / accidente en el almacén / otro) es informativa: no hay reclamo a nadie. Productos con serie: por ahora
/// con un ajuste de inventario (422).
/// </summary>
public sealed class DamageService(TeikemDbContext db, ITenantContext tenant, ILookupCache lookups, StatusService statuses, InventoryAdjustmentService inventory, InventoryLedger ledger)
{
    // ================================================================ reportar

    public async Task<DamageReportDto> ReportAsync(DamageReportRequest req, CancellationToken ct)
    {
        if (req is null) throw new ValidationException("body", DamageRules.BodyRequired);
        var errors = new Dictionary<string, string[]>();
        var (origin, originError) = DamageRules.ParseOrigin(req.Origin);
        origin ??= req.ReceiptPublicId is not null ? DamageOrigins.Receipt : DamageOrigins.Warehouse;
        if (originError is not null) errors["origin"] = new[] { originError };
        var (cause, causeError) = DamageRules.ParseCause(req.Cause);
        if (causeError is not null) errors["cause"] = new[] { causeError };
        var (disposition, dispositionError) = DamageRules.ParseDisposition(req.Disposition);
        if (dispositionError is not null) errors["disposition"] = new[] { dispositionError };
        var (notes, notesError) = DamageRules.NormalizeNotes(req.Notes);
        if (notesError is not null) errors["notes"] = new[] { notesError };
        if (req.ProductPublicId is null) errors["productPublicId"] = new[] { DamageRules.ProductRequired };
        if (req.Quantity is null or <= 0m) errors["quantity"] = new[] { DamageRules.QuantityInvalid };
        if (originError is null && DamageRules.ValidateSource(origin, req.ReceiptPublicId is not null, req.FromBinId is not null) is { } src) errors[src.Field] = new[] { src.Message };
        if (req.LotId is not null && req.Lot is not null) errors["lot"] = new[] { AdjustmentRules.LotAmbiguous };
        // 2026-10-08: al darle salida de una vez se puede decir a dónde va (opcional: sin dato queda vacío = tirado)
        int? reportFinalDestinationId = null;
        if (disposition == DamageDispositions.Discard && !string.IsNullOrWhiteSpace(req.FinalDestination))
        {
            reportFinalDestinationId = await lookups.TryGetIdAsync(LookupDomains.DamageFinalDestination, req.FinalDestination.Trim().ToUpperInvariant(), ct);
            if (reportFinalDestinationId is null) errors["finalDestination"] = new[] { DamageRules.UnknownFinalDestination(req.FinalDestination.Trim()) };
        }
        if (errors.Count > 0) throw new ValidationException(errors);

        var id = await db.RunInTransactionAsync(async ct2 =>
        {
            var tenantId = ((TenantContext)tenant).RequireTenantId();
            var product = await db.Set<Product>().AsNoTracking().FirstOrDefaultAsync(p => p.PublicId == req.ProductPublicId!.Value, ct2) ?? throw WmsResolve.ProductNotFound();
            var tracking = await lookups.GetAsync(product.TrackingTypeLookupId, ct2);
            if (tracking?.InternalCode == TrackingTypes.Serial) throw new StatusRuleException(DamageRules.SerialNotSupported);
            var warehouse = await db.ResolveWarehouseOrDefaultAsync(req.WarehousePublicId, ct2);

            int? receiptId = null;
            if (origin == DamageOrigins.Receipt)
            {
                receiptId = await db.Set<ReceiptHeader>().AsNoTracking()
                    .Where(r => r.PublicId == req.ReceiptPublicId!.Value && r.WarehouseId == warehouse.WarehouseId)
                    .Select(r => (int?)r.ReceiptHeaderId).FirstOrDefaultAsync(ct2) ?? throw new NotFoundException("Recibo");
            }
            WarehouseBin? fromBin = null;
            if (origin == DamageOrigins.Warehouse) fromBin = await ResolveBinAsync(warehouse.WarehouseId, req.FromBinId!.Value, ct2);
            int? lotId = null;
            if (req.LotId is int requestedLot)
                lotId = await db.Set<InventoryLot>().AsNoTracking().Where(l => l.LotId == requestedLot && l.ProductId == product.ProductId)
                    .Select(l => (int?)l.LotId).FirstOrDefaultAsync(ct2) ?? throw WmsResolve.LotNotFound();

            // El lote también puede venir por su número (la app lo teclea o escanea): un daño del almacén exige que exista; uno de recibo que se
            // desecha de una vez solo lo anota si existe; uno de recibo que entra a cuarentena lo da de alta al ajustar (más abajo).
            if (lotId is null && req.Lot?.Number is string lotNumber && !string.IsNullOrWhiteSpace(lotNumber) && !(origin == DamageOrigins.Receipt && disposition == DamageDispositions.Quarantine))
            {
                var wanted = lotNumber.Trim();
                lotId = await db.Set<InventoryLot>().AsNoTracking().Where(l => l.ProductId == product.ProductId && l.LotNumber == wanted).Select(l => (int?)l.LotId).FirstOrDefaultAsync(ct2);
                if (lotId is null && origin == DamageOrigins.Warehouse) throw WmsResolve.LotNotFound();
            }

            WarehouseBin? quarantine = null;
            if (disposition == DamageDispositions.Quarantine) quarantine = await ResolveQuarantineBinAsync(warehouse.WarehouseId, req.QuarantineBinId, ct2);

            var initial = await InitialStatusAsync(ct2);
            var d = new DamageReport
            {
                TenantId = tenantId, WarehouseId = warehouse.WarehouseId, ProductId = product.ProductId, LotId = lotId, FromBinId = fromBin?.WarehouseBinId,
                QuarantineBinId = quarantine?.WarehouseBinId, Quantity = req.Quantity!.Value, OriginLookupId = await lookups.GetIdAsync(LookupDomains.DamageOrigin, origin, ct2),
                CauseLookupId = await lookups.GetIdAsync(LookupDomains.DamageCause, cause!, ct2), ReceiptHeaderId = receiptId, Notes = notes, FinalDestinationLookupId = reportFinalDestinationId,
                StatusCodeId = initial.StatusCodeId, ReportedAtUtc = DateTime.UtcNow, ReportedBy = tenant.UserId, IsActive = true,
            };
            db.Set<DamageReport>().Add(d);
            await db.SaveGuardedAsync(DbExtensions.ConcurrencyMessage, ct2);
            // historial de estatus desde el principio: (nada) → REPORTED
            await statuses.TransitionAsync(StatusDomains.DamageStatus, EntityTypes.DamageReport, d.DamageReportId, null, DamageStatuses.Reported, null, ct2);
            await db.SaveGuardedAsync(DbExtensions.ConcurrencyMessage, ct2);

            var causeLabel = await CauseLabelAsync(d.CauseLookupId, ct2);
            var note = DamageRules.MovementNote(d.DamageReportId, causeLabel, notes);
            var damageId = d.DamageReportId;
            var damageQty = d.Quantity;
            if (DamageRules.MovesInventory(origin, disposition!))
            {
                if (origin == DamageOrigins.Warehouse && disposition == DamageDispositions.Quarantine)
                {
                    // dejarlo en la misma posición donde estaba no mueve nada (queda reservado más abajo si no es de cuarentena)
                    if (fromBin!.WarehouseBinId != quarantine!.WarehouseBinId)
                        await inventory.TransferAsync(new TransferRequest(product.PublicId, fromBin.WarehouseBinId, quarantine.WarehouseBinId, damageQty, warehouse.PublicId, null, lotId, null, note), ct2);
                }
                else if (origin == DamageOrigins.Warehouse)
                    await inventory.AdjustAsync(new AdjustmentRequest(product.PublicId, warehouse.PublicId, fromBin!.WarehouseBinId, -damageQty, AdjustmentReasons.Damage, note, lotId), ct2);
                else
                    await inventory.AdjustAsync(new AdjustmentRequest(product.PublicId, warehouse.PublicId, quarantine!.WarehouseBinId, damageQty, AdjustmentReasons.Damage, note, lotId, req.Lot), ct2);
            }
            // El ledger puede limpiar el seguimiento del contexto: el reporte se vuelve a leer antes de seguir.
            d = await db.Set<DamageReport>().FirstAsync(x => x.DamageReportId == damageId, ct2);
            // un lote nuevo (daño de recibo con Lot) se crea al ajustar: se vuelve a leer el LotId de la entrada para el reporte
            if (lotId is null && req.Lot is not null && DamageRules.MovesInventory(origin, disposition!))
                d.LotId = await db.Set<InventoryLot>().AsNoTracking().Where(l => l.ProductId == product.ProductId && l.LotNumber == req.Lot.Number)
                    .Select(l => (int?)l.LotId).FirstOrDefaultAsync(ct2);

            var target = disposition == DamageDispositions.Quarantine ? DamageStatuses.Quarantined : DamageStatuses.Discarded;
            if (target == DamageStatuses.Quarantined) await ReserveIfNeededAsync(damageId, product.ProductId, warehouse.WarehouseId, quarantine!, ct2);
            d = await db.Set<DamageReport>().FirstAsync(x => x.DamageReportId == damageId, ct2);
            await TransitionAsync(d, initial.StatusCodeId, target, null, ct2);
            if (target == DamageStatuses.Discarded) { d.ResolvedAtUtc = DateTime.UtcNow; d.ResolvedBy = tenant.UserId; }
            await db.SaveGuardedAsync(DbExtensions.ConcurrencyMessage, ct2);
            return d.DamageReportId;
        }, ct);
        return await GetAsync(id, ct);
    }

    // ================================================================ daño declarado en la línea de un recibo que se confirma

    /// <summary>
    /// 2026-10-08 — Llamado por ReceiptService al confirmar un recibo cuya línea declara unidades dañadas. A diferencia del daño de recibo reportado
    /// después (ReportAsync), aquí las unidades YA entraron al inventario con el resto de la línea (la recepción asienta todo lo recibido en la posición
    /// donde aterrizó, <paramref name="landingBinId"/>), así que:
    /// - desechar = ajuste negativo DAMAGE desde donde aterrizó (el reporte nace DISCARDED, sin posición);
    /// - cuarentena = transferencia desde donde aterrizó a la posición indicada (si es la misma, no hay movimiento: las unidades se quedan y el reporte
    ///   queda QUARANTINED ahí). Sin posición indicada: la primera de cuarentena activa del almacén por código o, si no hay, donde aterrizó.
    /// Corre dentro de la transacción de la confirmación. Devuelve el id del reporte.
    /// </summary>
    public async Task<int> ReportReceiptLineAsync(int warehouseId, int receiptId, int productId, int? lotId, decimal quantity, int causeLookupId, string? notes,
        int? landingBinId, int? damageBinId, bool discard, CancellationToken ct)
    {
        return await db.RunInTransactionAsync(async ct2 =>
        {
            var tenantId = ((TenantContext)tenant).RequireTenantId();
            var product = await db.Set<Product>().AsNoTracking().FirstAsync(p => p.ProductId == productId, ct2);
            var warehouse = await db.Set<Warehouse>().AsNoTracking().FirstAsync(w => w.WarehouseId == warehouseId, ct2);
            WarehouseBin? destination = null;
            if (!discard)
            {
                if (damageBinId is int explicitBin) destination = await ResolveBinAsync(warehouseId, explicitBin, ct2);
                else
                {
                    try { destination = await ResolveQuarantineBinAsync(warehouseId, null, ct2); }
                    catch (StatusRuleException) { destination = landingBinId is int lb ? await ResolveBinAsync(warehouseId, lb, ct2) : null; }
                }
                if (destination is null) throw new StatusRuleException(DamageRules.NoQuarantineBin);
            }

            var initial = await InitialStatusAsync(ct2);
            var d = new DamageReport
            {
                TenantId = tenantId, WarehouseId = warehouseId, ProductId = productId, LotId = lotId, FromBinId = landingBinId,
                QuarantineBinId = destination?.WarehouseBinId, Quantity = quantity,
                OriginLookupId = await lookups.GetIdAsync(LookupDomains.DamageOrigin, DamageOrigins.Receipt, ct2), CauseLookupId = causeLookupId,
                ReceiptHeaderId = receiptId, Notes = notes, StatusCodeId = initial.StatusCodeId, ReportedAtUtc = DateTime.UtcNow, ReportedBy = tenant.UserId, IsActive = true,
            };
            db.Set<DamageReport>().Add(d);
            await db.SaveGuardedAsync(DbExtensions.ConcurrencyMessage, ct2);
            await statuses.TransitionAsync(StatusDomains.DamageStatus, EntityTypes.DamageReport, d.DamageReportId, null, DamageStatuses.Reported, null, ct2);
            await db.SaveGuardedAsync(DbExtensions.ConcurrencyMessage, ct2);

            var note = DamageRules.MovementNote(d.DamageReportId, await CauseLabelAsync(causeLookupId, ct2), notes);
            var damageId = d.DamageReportId;
            if (landingBinId is int from)
            {
                if (discard)
                    await inventory.AdjustAsync(new AdjustmentRequest(product.PublicId, warehouse.PublicId, from, -quantity, AdjustmentReasons.Damage, note, lotId), ct2);
                else if (destination!.WarehouseBinId != from)
                    await inventory.TransferAsync(new TransferRequest(product.PublicId, from, destination.WarehouseBinId, quantity, warehouse.PublicId, null, lotId, null, note), ct2);
            }
            if (!discard) await ReserveIfNeededAsync(damageId, productId, warehouseId, destination!, ct2);
            d = await db.Set<DamageReport>().FirstAsync(x => x.DamageReportId == damageId, ct2);
            var target = discard ? DamageStatuses.Discarded : DamageStatuses.Quarantined;
            await TransitionAsync(d, initial.StatusCodeId, target, null, ct2);
            if (discard) { d.ResolvedAtUtc = DateTime.UtcNow; d.ResolvedBy = tenant.UserId; }
            await db.SaveGuardedAsync(DbExtensions.ConcurrencyMessage, ct2);
            return damageId;
        }, ct);
    }

    // ================================================================ desechar / recuperar lo que está en cuarentena

    public async Task<DamageReportDto> DiscardAsync(int id, DamageResolveRequest? req, CancellationToken ct)
    {
        var (notes, notesError) = DamageRules.NormalizeNotes(req?.Notes);
        if (notesError is not null) throw new ValidationException("notes", notesError);
        var (destinationCode, destinationError) = DamageRules.ParseFinalDestination(req?.FinalDestination);
        if (destinationError is not null) throw new ValidationException("finalDestination", destinationError);
        var finalDestinationId = await lookups.TryGetIdAsync(LookupDomains.DamageFinalDestination, destinationCode!, ct)
            ?? throw new ValidationException("finalDestination", DamageRules.UnknownFinalDestination(req!.FinalDestination!.Trim()));
        await db.RunInTransactionAsync(async ct2 =>
        {
            var d = await LoadQuarantinedAsync(id, ct2);
            var (product, warehouse, lotId) = await RefsAsync(d, ct2);
            await ReleaseIfReservedAsync(d, ct2);
            var destinationLabel = LabelOf(await lookups.GetAsync(finalDestinationId, ct2));
            var note = DamageRules.MovementNote(d.DamageReportId, string.IsNullOrEmpty(destinationLabel) ? "Salida" : $"Salida: {destinationLabel}", notes ?? d.Notes);
            await inventory.AdjustAsync(new AdjustmentRequest(product.PublicId, warehouse.PublicId, d.QuarantineBinId, -d.Quantity, AdjustmentReasons.Damage, note, lotId), ct2);
            d = await ReloadAsync(id, ct2);
            d.FinalDestinationLookupId = finalDestinationId;
            await TransitionAsync(d, d.StatusCodeId, DamageStatuses.Discarded, notes, ct2);
            Resolve(d, notes);
            await db.SaveGuardedAsync(DbExtensions.ConcurrencyMessage, ct2);
        }, ct);
        return await GetAsync(id, ct);
    }

    public async Task<DamageReportDto> RecoverAsync(int id, DamageResolveRequest? req, CancellationToken ct)
    {
        var (notes, notesError) = DamageRules.NormalizeNotes(req?.Notes);
        if (notesError is not null) throw new ValidationException("notes", notesError);
        if (req?.ToBinId is null) throw new ValidationException("toBinId", DamageRules.RecoverBinRequired);
        await db.RunInTransactionAsync(async ct2 =>
        {
            var d = await LoadQuarantinedAsync(id, ct2);
            var (product, warehouse, lotId) = await RefsAsync(d, ct2);
            var toBin = await ResolveBinAsync(d.WarehouseId, req.ToBinId.Value, ct2);
            var zoneType = await ZoneTypeOfAsync(toBin.WarehouseZoneId, ct2);
            if (DamageRules.ValidateRecoverZone(toBin.Code, zoneType) is { } zoneError) throw new ValidationException("toBinId", zoneError);
            await ReleaseIfReservedAsync(d, ct2);
            var note = DamageRules.MovementNote(d.DamageReportId, "Recuperado", notes ?? d.Notes);
            await inventory.TransferAsync(new TransferRequest(product.PublicId, d.QuarantineBinId, toBin.WarehouseBinId, d.Quantity, warehouse.PublicId, null, lotId, null, note), ct2);
            d = await ReloadAsync(id, ct2);
            await TransitionAsync(d, d.StatusCodeId, DamageStatuses.Recovered, notes, ct2);
            Resolve(d, notes);
            await db.SaveGuardedAsync(DbExtensions.ConcurrencyMessage, ct2);
        }, ct);
        return await GetAsync(id, ct);
    }

    // ================================================================ lectura

    public async Task<DamageReportPageDto> ListAsync(string? status, string? origin, string? q, int skip, int take, CancellationToken ct)
    {
        take = Math.Clamp(take <= 0 ? 50 : take, 1, 200);
        skip = Math.Max(0, skip);
        var query = db.Set<DamageReport>().AsNoTracking().Where(d => d.IsActive);
        if (!string.IsNullOrWhiteSpace(status))
        {
            var codes = (await db.StatusCodes.AsNoTracking().Where(s => s.Entity == StatusDomains.DamageStatus).ToListAsync(ct));
            var wanted = status.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(s => s.ToUpperInvariant()).ToHashSet();
            var ids = codes.Where(c => wanted.Contains(c.InternalCode)).Select(c => c.StatusCodeId).ToList();
            query = query.Where(d => ids.Contains(d.StatusCodeId));
        }
        if (!string.IsNullOrWhiteSpace(origin))
        {
            var originId = await lookups.TryGetIdAsync(LookupDomains.DamageOrigin, origin.Trim().ToUpperInvariant(), ct);
            query = originId is int oid ? query.Where(d => d.OriginLookupId == oid) : query.Where(_ => false);
        }
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            var productIds = await db.Set<Product>().AsNoTracking().Where(p => p.Sku.Contains(term) || p.Name.Contains(term)).Select(p => p.ProductId).ToListAsync(ct);
            int? asId = null;
            if (term.StartsWith("DAN-", StringComparison.OrdinalIgnoreCase) && int.TryParse(term[4..], out var n)) asId = n;
            query = query.Where(d => productIds.Contains(d.ProductId) || (asId != null && d.DamageReportId == asId));
        }
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(d => d.DamageReportId).Skip(skip).Take(take).ToListAsync(ct);
        return new DamageReportPageDto(total, skip, take, await MapAsync(rows, ct));
    }

    public async Task<DamageReportDto> GetAsync(int id, CancellationToken ct)
    {
        var d = await db.Set<DamageReport>().AsNoTracking().FirstOrDefaultAsync(x => x.DamageReportId == id && x.IsActive, ct) ?? throw new NotFoundException("Daño", id);
        return (await MapAsync(new[] { d }, ct))[0];
    }

    private async Task<List<DamageReportDto>> MapAsync(IReadOnlyList<DamageReport> rows, CancellationToken ct)
    {
        if (rows.Count == 0) return new List<DamageReportDto>();
        var productIds = rows.Select(r => r.ProductId).Distinct().ToList();
        var products = await db.Set<Product>().AsNoTracking().Where(p => productIds.Contains(p.ProductId)).ToDictionaryAsync(p => p.ProductId, ct);
        var warehouseIds = rows.Select(r => r.WarehouseId).Distinct().ToList();
        var warehouses = await db.Set<Warehouse>().AsNoTracking().Where(w => warehouseIds.Contains(w.WarehouseId)).ToDictionaryAsync(w => w.WarehouseId, ct);
        var binIds = rows.SelectMany(r => new[] { r.FromBinId, r.QuarantineBinId }).Where(b => b != null).Select(b => b!.Value).Distinct().ToList();
        var bins = await db.Set<WarehouseBin>().AsNoTracking().Where(b => binIds.Contains(b.WarehouseBinId)).ToDictionaryAsync(b => b.WarehouseBinId, b => b.Code, ct);
        var lotIds = rows.Where(r => r.LotId != null).Select(r => r.LotId!.Value).Distinct().ToList();
        var lots = await db.Set<InventoryLot>().AsNoTracking().Where(l => lotIds.Contains(l.LotId)).ToDictionaryAsync(l => l.LotId, l => l.LotNumber, ct);
        var receiptIds = rows.Where(r => r.ReceiptHeaderId != null).Select(r => r.ReceiptHeaderId!.Value).Distinct().ToList();
        var receipts = await db.Set<ReceiptHeader>().AsNoTracking().Where(r => receiptIds.Contains(r.ReceiptHeaderId)).ToDictionaryAsync(r => r.ReceiptHeaderId, r => (r.PublicId, r.Number), ct);
        var userIds = rows.SelectMany(r => new[] { r.ReportedBy, r.ResolvedBy }).Where(u => u != null).Select(u => u!.Value).Distinct().ToList();
        var users = await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName ?? u.Email, ct);
        var statusMap = await StatusMapAsync(ct);
        var result = new List<DamageReportDto>();
        foreach (var r in rows)
        {
            var p = products[r.ProductId];
            var w = warehouses[r.WarehouseId];
            var (statusCode, statusLabel) = statusMap.GetValueOrDefault(r.StatusCodeId);
            var originLookup = await lookups.GetAsync(r.OriginLookupId, ct);
            var causeLookup = await lookups.GetAsync(r.CauseLookupId, ct);
            var finalLookup = r.FinalDestinationLookupId is int fid ? await lookups.GetAsync(fid, ct) : null;
            receipts.TryGetValue(r.ReceiptHeaderId ?? 0, out var receipt);
            result.Add(new DamageReportDto(r.DamageReportId, r.PublicId, DamageRules.Code(r.DamageReportId), originLookup?.InternalCode ?? string.Empty,
                LabelOf(originLookup), causeLookup?.InternalCode ?? string.Empty, LabelOf(causeLookup), w.PublicId, w.Code, p.PublicId, p.Sku, p.Name,
                r.LotId is int l && lots.TryGetValue(l, out var ln) ? ln : null, r.Quantity,
                r.FromBinId, r.FromBinId is int fb ? bins.GetValueOrDefault(fb) : null, r.QuarantineBinId, r.QuarantineBinId is int qb ? bins.GetValueOrDefault(qb) : null,
                r.ReceiptHeaderId is null ? null : receipt.PublicId, r.ReceiptHeaderId is null ? null : receipt.Number, r.Notes, statusCode ?? string.Empty, statusLabel ?? string.Empty,
                r.ReportedAtUtc, r.ReportedBy is int rb ? users.GetValueOrDefault(rb) : null, r.ResolvedAtUtc, r.ResolvedBy is int rs ? users.GetValueOrDefault(rs) : null, r.ResolutionNotes,
                finalLookup?.InternalCode, LabelOf(finalLookup), r.IsReserved));
        }
        return result;
    }

    // ================================================================ apoyo

    private string LabelOf(LookupCode? l) => l is null ? string.Empty : MultilingualText.Resolve(l.LabelJson, tenant.Lang);

    private async Task<string> CauseLabelAsync(int causeLookupId, CancellationToken ct) => LabelOf(await lookups.GetAsync(causeLookupId, ct));

    private async Task<Dictionary<int, (string Code, string Label)>> StatusMapAsync(CancellationToken ct)
    {
        var codes = await db.StatusCodes.AsNoTracking().Where(s => s.Entity == StatusDomains.DamageStatus).ToListAsync(ct);
        var ids = codes.Select(c => c.StatusCodeId).ToList();
        var overrides = tenant.TenantId is null
            ? new Dictionary<int, StatusCodeOverride>()
            : await db.StatusCodeOverrides.AsNoTracking().Where(o => ids.Contains(o.StatusCodeId)).ToDictionaryAsync(o => o.StatusCodeId, ct);
        return codes.ToDictionary(c => c.StatusCodeId, c => (c.InternalCode,
            MultilingualText.Resolve(MultilingualText.Merge(c.LabelJson, overrides.GetValueOrDefault(c.StatusCodeId)?.CustomLabelJson), tenant.Lang)));
    }

    private async Task<StatusCode> InitialStatusAsync(CancellationToken ct)
        => await db.StatusCodes.AsNoTracking().FirstOrDefaultAsync(s => s.Entity == StatusDomains.DamageStatus && s.IsInitial, ct)
           ?? throw new NotFoundException($"Estatus {StatusDomains.DamageStatus}", DamageStatuses.Reported);

    private async Task TransitionAsync(DamageReport d, int fromStatusId, string toCode, string? comment, CancellationToken ct)
    {
        var to = await statuses.TransitionAsync(StatusDomains.DamageStatus, EntityTypes.DamageReport, d.DamageReportId, fromStatusId, toCode, comment, ct);
        d.StatusCodeId = to.StatusCodeId;
    }

    private void Resolve(DamageReport d, string? notes)
    {
        d.ResolvedAtUtc = DateTime.UtcNow;
        d.ResolvedBy = tenant.UserId;
        d.ResolutionNotes = notes;
    }

    /// <summary>
    /// Reserva las unidades de un daño que queda EN CUARENTENA en una posición que no es de cuarentena (guardado, recepción…): el saldo conserva la existencia
    /// (se cuenta y se ve) pero no queda disponible, así que no se despacha ni se asigna. En zonas de cuarentena, cruce de muelle o renta no hace falta: ya
    /// están fuera de la asignación. Marca IsReserved para liberarlo al desechar o recuperar.
    /// </summary>
    private async Task ReserveIfNeededAsync(int damageId, int productId, int warehouseId, WarehouseBin bin, CancellationToken ct)
    {
        if (!DamageRules.NeedsReservation(await ZoneTypeOfAsync(bin.WarehouseZoneId, ct))) return;
        var d = await db.Set<DamageReport>().AsNoTracking().FirstAsync(x => x.DamageReportId == damageId, ct);
        await ledger.ReserveAsync(new[] { new StockReservation(productId, warehouseId, bin.WarehouseBinId, d.LotId, d.Quantity) }, ct);
        var fresh = await db.Set<DamageReport>().FirstAsync(x => x.DamageReportId == damageId, ct);
        fresh.IsReserved = true;
        await db.SaveGuardedAsync(DbExtensions.ConcurrencyMessage, ct);
    }

    /// <summary>Libera la reserva de un daño en cuarentena (si la tenía) ANTES de moverlo: el ajuste o la transferencia piden disponible.</summary>
    private async Task ReleaseIfReservedAsync(DamageReport d, CancellationToken ct)
    {
        if (!d.IsReserved) return;
        var id = d.DamageReportId;
        await ledger.ReleaseAsync(new[] { new StockReservation(d.ProductId, d.WarehouseId, d.QuarantineBinId!.Value, d.LotId, d.Quantity) }, ct);
        var fresh = await db.Set<DamageReport>().FirstAsync(x => x.DamageReportId == id, ct);
        fresh.IsReserved = false;
        await db.SaveGuardedAsync(DbExtensions.ConcurrencyMessage, ct);
    }

    /// <summary>Vuelve a leer el reporte con seguimiento (el ledger puede limpiar el del contexto al mover inventario).</summary>
    private async Task<DamageReport> ReloadAsync(int id, CancellationToken ct)
        => await db.Set<DamageReport>().FirstAsync(x => x.DamageReportId == id, ct);

    private async Task<DamageReport> LoadQuarantinedAsync(int id, CancellationToken ct)
    {
        var d = await db.Set<DamageReport>().FirstOrDefaultAsync(x => x.DamageReportId == id && x.IsActive, ct) ?? throw new NotFoundException("Daño", id);
        var current = await db.StatusCodes.AsNoTracking().FirstAsync(s => s.StatusCodeId == d.StatusCodeId, ct);
        if (current.InternalCode != DamageStatuses.Quarantined || d.QuarantineBinId is null) throw new StatusRuleException(DamageRules.ResolveOnlyQuarantined);
        return d;
    }

    private async Task<(Product Product, Warehouse Warehouse, int? LotId)> RefsAsync(DamageReport d, CancellationToken ct)
        => (await db.Set<Product>().AsNoTracking().FirstAsync(p => p.ProductId == d.ProductId, ct),
            await db.Set<Warehouse>().AsNoTracking().FirstAsync(w => w.WarehouseId == d.WarehouseId, ct), d.LotId);

    private async Task<WarehouseBin> ResolveBinAsync(int warehouseId, int binId, CancellationToken ct)
        => await (from b in db.Set<WarehouseBin>().AsNoTracking()
                  join w in db.Set<Warehouse>().AsNoTracking() on b.WarehouseId equals w.WarehouseId
                  where b.WarehouseBinId == binId && b.WarehouseId == warehouseId
                  select b).FirstOrDefaultAsync(ct) ?? throw WmsResolve.BinNotFound();

    private async Task<string?> ZoneTypeOfAsync(int zoneId, CancellationToken ct)
    {
        var typeId = await db.Set<WarehouseZone>().AsNoTracking().Where(z => z.WarehouseZoneId == zoneId).Select(z => z.ZoneTypeLookupId).FirstOrDefaultAsync(ct);
        return typeId is int id ? (await lookups.GetAsync(id, ct))?.InternalCode : null;
    }

    /// <summary>La posición de cuarentena indicada (debe ser de una zona QUARANTINE) o, sin ella, la primera activa del almacén por código.</summary>
    private async Task<WarehouseBin> ResolveQuarantineBinAsync(int warehouseId, int? requested, CancellationToken ct)
    {
        var quarantineTypeId = await lookups.TryGetIdAsync(LookupDomains.ZoneType, ZoneTypes.Quarantine, ct);
        if (requested is int binId)
        {
            var bin = await ResolveBinAsync(warehouseId, binId, ct);
            // 2026-10-08: la posición indicada puede ser cualquiera activa del almacén (no solo de cuarentena): "dónde se deja lo dañado".
            if (!bin.IsActive) throw new ValidationException("quarantineBinId", DamageRules.BinInactive(bin.Code));
            return bin;
        }
        var found = quarantineTypeId is int tid
            ? await (from b in db.Set<WarehouseBin>().AsNoTracking()
                     join z in db.Set<WarehouseZone>().AsNoTracking() on b.WarehouseZoneId equals z.WarehouseZoneId
                     where b.WarehouseId == warehouseId && b.IsActive && z.IsActive && z.ZoneTypeLookupId == tid
                     orderby b.Code
                     select b).FirstOrDefaultAsync(ct)
            : null;
        return found ?? throw new StatusRuleException(DamageRules.NoQuarantineBin);
    }
}
