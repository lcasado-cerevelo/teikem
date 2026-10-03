using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Common;
using Teikem.Domain.Constants;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Clients;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Wms;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 6 (P8) — faltantes de compra y su resolución: la pantalla 'Ajustes de inventario' (R14, bitácora L762; D8).
/// - Faltante de una línea = ordenado − recibido − resuelto, visible en órdenes activas NO canceladas que ya tienen un
///   recibo confirmado (RECEIVED o PUTAWAY). Una orden cancelada deja de mostrar faltantes (D47).
/// - Resolver (una fila en PurchaseOrderShortageResolution por acción):
///   · CLOSE: da por perdido el pendiente completo de la línea;
///   · REORDER (además purchasing.manage): crea una orden DRAFT nueva con el pendiente al mismo costo;
///   · MANUAL_ADJUSTMENT (además módulo WMS_LOTSERIAL): cantidad parcial ≤ pendiente que ENTRA al inventario con un
///     ADJUSTMENT de motivo PO_SHORTAGE (por defecto) o FOUND, Ref PURCHASE_ORDER + id, en una posición del almacén de la
///     orden (lote o series según el seguimiento del producto).
///   Si ninguna línea queda pendiente y la orden está SENT/PARTIAL → RECEIVED (escalonado, con historial).
/// - Concurrencia: bloqueo de la orden (UPDLOCK) y pendiente recalculado bajo el bloqueo: de dos resoluciones simultáneas
///   la segunda recibe 409 'La línea ya no tiene faltante pendiente.'. Orden del lote: PurchaseOrder → saldos (ledger) →
///   NumberSequence (el PO-##### de la reorden va al final).
/// </summary>
public sealed class PurchaseShortageService(
    TeikemDbContext db,
    ITenantContext tenant,
    ILookupCache lookups,
    StatusService statuses,
    PermissionService permissions,
    ModuleService modules,
    InventoryLedger ledger,
    PurchaseOrderService purchaseOrders,
    ITenantClock? clock = null)
{
    private readonly ITenantClock _clock = clock ?? TenantClock.Default;

    public const string AllResolvedComment = "Faltantes resueltos: la orden de compra no tiene cantidades pendientes.";
    public static string ReorderNotes(string number) => $"Reorden del faltante de la orden de compra {number}.";
    public static string LotConcurrent(string lot) => $"El lote {lot} se está registrando en otra operación; intente de nuevo.";

    // ================================================================ consultas

    /// <summary>
    /// Órdenes activas no canceladas con recibo confirmado y algún pendiente, con líneas en faltante, su costo y el almacén de
    /// la orden. El pendiente se filtra en SQL (solo viajan las líneas en faltante); la agrupación por orden es en memoria.
    /// </summary>
    public async Task<IReadOnlyList<PoShortageSummaryDto>> ListWithShortageAsync(CancellationToken ct)
    {
        // Lote 13: confirmado = RECEIVED, RECEIVED_VARIANCE o PUTAWAY.
        var confirmedIds = await db.StatusIdsAsync(StatusDomains.ReceiptStatus, ReceiptStatuses.ConfirmedCodes.Append(ReceiptStatuses.Putaway), ct);
        var cancelledId = await db.StatusIdAsync(StatusDomains.PurchaseOrderStatus, PurchaseOrderStatuses.Cancelled, ct);

        var rows = await PendingLinesQuery(cancelledId, confirmedIds).ToListAsync(ct);
        if (rows.Count == 0) return Array.Empty<PoShortageSummaryDto>();

        var supplierIds = rows.Select(r => r.SupplierId).Distinct().ToList();
        var suppliers = await db.Set<Supplier>().AsNoTracking().Where(s => supplierIds.Contains(s.SupplierId))
            .ToDictionaryAsync(s => s.SupplierId, s => s.Name, ct);
        var warehouseIds = rows.Select(r => r.WarehouseId).Distinct().ToList();
        var warehouses = await db.Set<Warehouse>().AsNoTracking().Where(w => warehouseIds.Contains(w.WarehouseId))
            .Select(w => new { w.WarehouseId, w.PublicId, w.Code }).ToDictionaryAsync(w => w.WarehouseId, ct);

        var result = new List<PoShortageSummaryDto>();
        foreach (var po in rows.GroupBy(r => r.PurchaseOrderId).OrderBy(g => g.First().OrderDate).ThenBy(g => g.Key))
        {
            // Mismas reglas puras que LinesAsync (Pending recorta a 0; PendingCost redondea a 4 decimales).
            var pending = po.Select(r => (Qty: ShortageRules.Pending(r.QtyOrdered, r.QtyReceived, r.Resolved), r.UnitCost))
                .Where(x => x.Qty > 0m).ToList();
            if (pending.Count == 0) continue;
            var head = po.First();
            var warehouse = warehouses.GetValueOrDefault(head.WarehouseId);
            result.Add(new PoShortageSummaryDto(head.PublicId, head.Number, head.SupplierId, suppliers.GetValueOrDefault(head.SupplierId, ""),
                pending.Count, pending.Sum(x => x.Qty), PurchaseOrderRules.Round4(pending.Sum(x => ShortageRules.PendingCost(x.Qty, x.UnitCost))),
                warehouse?.PublicId ?? Guid.Empty, warehouse?.Code ?? ""));
        }
        return result;
    }

    /// <summary>Fila plana de una línea de compra con faltante pendiente (ordenado − recibido − resuelto &gt; 0).</summary>
    private sealed record PendingLineRow(int PurchaseOrderId, Guid PublicId, string Number, DateOnly OrderDate, int SupplierId, int WarehouseId,
        int PurchaseOrderLineId, decimal QtyOrdered, decimal QtyReceived, decimal UnitCost, decimal Resolved);

    /// <summary>
    /// Líneas con faltante pendiente de órdenes activas NO canceladas con algún recibo activo confirmado (RECEIVED,
    /// RECEIVED_VARIANCE o PUTAWAY, el mismo criterio que ReceiptFlagsAsync). La consulta parte de la línea pero siempre unida a su orden: la línea no
    /// tiene TenantId y el filtro de tenant llega por PurchaseOrder. Lo resuelto es una subconsulta correlacionada; no se
    /// agrega (SUM/GroupBy) sobre una expresión que la contenga (SQL Server, error 130): la agrupación se hace en memoria.
    /// </summary>
    private IQueryable<PendingLineRow> PendingLinesQuery(int cancelledId, List<int> confirmedIds)
    {
        var asns = db.Set<Asn>().AsNoTracking();
        var receipts = db.Set<ReceiptHeader>().AsNoTracking();
        var resolutions = db.Set<PurchaseOrderShortageResolution>().AsNoTracking();
        return from l in db.Set<PurchaseOrderLine>().AsNoTracking()
               join p in db.Set<PurchaseOrder>().AsNoTracking() on l.PurchaseOrderId equals p.PurchaseOrderId
               where p.IsActive && p.StatusCodeId != cancelledId
                     && asns.Any(a => a.PurchaseOrderId == p.PurchaseOrderId
                                      && receipts.Any(r => r.AsnId == a.AsnId && r.IsActive
                                                           && confirmedIds.Contains(r.StatusCodeId)))
               let resolved = resolutions.Where(r => r.PurchaseOrderLineId == l.PurchaseOrderLineId).Sum(r => (decimal?)r.Quantity) ?? 0m
               where l.QtyOrdered - l.QtyReceived - resolved > 0m
               select new PendingLineRow(p.PurchaseOrderId, p.PublicId, p.Number, p.OrderDate, p.SupplierId, p.WarehouseId,
                   l.PurchaseOrderLineId, l.QtyOrdered, l.QtyReceived, l.UnitCost, resolved);
    }

    /// <summary>
    /// Líneas de la orden con faltante pendiente o con resoluciones previas (la bitácora de la pantalla). Una orden sin
    /// recibos confirmados todavía no tiene faltante: lista vacía.
    /// </summary>
    public async Task<IReadOnlyList<ShortageLineDto>> LinesAsync(Guid publicId, CancellationToken ct)
    {
        var po = await db.Set<PurchaseOrder>().AsNoTracking().FirstOrDefaultAsync(p => p.PublicId == publicId && p.IsActive, ct)
                 ?? throw new NotFoundException(PurchaseOrderRules.NotFound, feminine: true);
        var all = await LineDtosAsync(po.PurchaseOrderId, ct);
        if (!await PurchasingSupport.HasConfirmedReceiptAsync(db, po.PurchaseOrderId, ct))
            return all.Where(l => l.Resolutions.Count > 0).ToList();
        return all.Where(l => l.QtyPending > 0m || l.Resolutions.Count > 0).ToList();
    }

    // ================================================================ resolución

    public async Task<ShortageResolveResultDto> ResolveAsync(Guid publicId, int lineId, ShortageResolveRequest req, CancellationToken ct)
    {
        if (req is null) throw new ValidationException("body", "El cuerpo de la solicitud es obligatorio.");
        var tenantId = ((TenantContext)tenant).RequireTenantId();
        var action = ShortageRules.NormalizeAction(req.Action) ?? throw new ValidationException("action", ShortageRules.UnknownAction);
        var isManual = action == ShortageActions.ManualAdjustment;

        // Validación previa (sin BD) y permisos/módulos adicionales por acción.
        string? reasonCode = null;
        IReadOnlyList<string> serials = Array.Empty<string>();
        var errors = new Dictionary<string, string[]>();
        // el ajuste manual de un faltante es un ajuste manual de inventario: nota obligatoria (decisión del dueño, 2026-09-30);
        // cerrar y reordenar no mueven inventario y la dejan opcional
        var (notes, notesError) = AdjustmentRules.NormalizeNotes(req.Notes, required: isManual);
        if (notesError is not null) errors["notes"] = new[] { notesError };
        if (isManual)
        {
            reasonCode = ShortageRules.NormalizeManualReason(req.Reason);
            if (reasonCode is null) errors["reason"] = new[] { ShortageRules.ReasonNotAllowed };
            if (req.BinId is null) errors["binId"] = new[] { ShortageRules.BinRequired };
            if (req.LotId is not null && req.Lot is not null) errors["lot"] = new[] { AdjustmentRules.LotAmbiguous };
            var (normalized, serialError) = AdjustmentRules.NormalizeSerials(req.SerialNumbers);
            if (serialError is not null) errors["serialNumbers"] = new[] { serialError };
            serials = normalized;
        }
        else if (req.BinId is not null || req.LotId is not null || req.Lot is not null || req.SerialNumbers is { Count: > 0 } || !string.IsNullOrWhiteSpace(req.Reason))
            errors["action"] = new[] { ShortageRules.ManualOnlyFields };
        if (errors.Count > 0) throw new ValidationException(errors);

        if (action == ShortageActions.Reorder)
        {
            await permissions.EnsureAsync(PermissionCatalog.PurchasingManage, ct);
            await purchaseOrders.EnsureNumberingAsync(ct);
        }
        if (isManual) await modules.EnsureEnabledAsync(ModuleKeys.WmsLotSerial, ct);

        var poId = await db.Set<PurchaseOrder>().AsNoTracking().Where(p => p.PublicId == publicId && p.IsActive)
                       .Select(p => (int?)p.PurchaseOrderId).FirstOrDefaultAsync(ct)
                   ?? throw new NotFoundException(PurchaseOrderRules.NotFound, feminine: true);
        var actionLookupId = await lookups.GetIdAsync(LookupDomains.ShortageAction, action, ct);
        int? reasonLookupId = reasonCode is null ? null : await lookups.GetIdAsync(LookupDomains.AdjustmentReason, reasonCode, ct);

        var reorderId = await db.RunInTransactionAsync(async ct2 =>
        {
            // 1. Encabezado bloqueado y re-verificación bajo el bloqueo.
            var po = await PurchasingSupport.LockPurchaseOrderAsync(db, poId, ct2);
            if (!po.IsActive) throw new NotFoundException(PurchaseOrderRules.NotFound, feminine: true);
            PurchasingSupport.EnsureRowVersion(po.RowVersion, req.RowVersion, PurchaseOrderService.ConcurrencyMessage);
            var code = await PurchasingSupport.StatusCodeOfAsync(db, po.StatusCodeId, ct2);
            var hasConfirmed = await PurchasingSupport.HasConfirmedReceiptAsync(db, po.PurchaseOrderId, ct2);
            if (ShortageRules.ResolvableError(code, hasConfirmed) is { } statusError) throw new StatusRuleException(statusError);

            // 2. La línea se busca DENTRO de la orden (otra línea → 404) y el pendiente se recalcula bajo el bloqueo.
            var lines = await db.Set<PurchaseOrderLine>().AsNoTracking().Where(l => l.PurchaseOrderId == po.PurchaseOrderId).ToListAsync(ct2);
            var line = lines.FirstOrDefault(l => l.PurchaseOrderLineId == lineId)
                       ?? throw new NotFoundException(ShortageRules.LineNotFound, feminine: true);
            var resolved = await PurchasingSupport.ResolvedByLineAsync(db, new[] { po.PurchaseOrderId }, ct2);
            var pending = ShortageRules.Pending(line.QtyOrdered, line.QtyReceived, resolved.GetValueOrDefault(line.PurchaseOrderLineId));
            var validation = ShortageRules.Validate(action, req.Quantity, pending);
            if (!validation.IsValid)
            {
                if (validation.IsConflict) throw new ConflictException(validation.Error!);
                throw new ValidationException("quantity", validation.Error!);
            }
            var qty = validation.Quantity;

            // 3. Efecto de la acción.
            int? newPoId = null;
            long? txnId = null;
            if (action == ShortageActions.Reorder)
                newPoId = (await ReorderAsync(po, line, qty, ct2)).PurchaseOrderId;
            else if (isManual)
                txnId = await ManualAdjustmentAsync(po, line, qty, req, reasonCode!, serials, notes, ct2);

            // 4. Bitácora de la resolución.
            db.Set<PurchaseOrderShortageResolution>().Add(new PurchaseOrderShortageResolution
            {
                TenantId = tenantId, PurchaseOrderId = po.PurchaseOrderId, PurchaseOrderLineId = line.PurchaseOrderLineId,
                ActionLookupId = actionLookupId, Quantity = qty, ReasonLookupId = reasonLookupId, Notes = notes,
                ReorderPurchaseOrderId = newPoId, InventoryTransactionId = txnId, CreatedAtUtc = DateTime.UtcNow, CreatedBy = tenant.UserId,
            });

            // 5. Sin pendientes → RECEIVED (escalonado) si la orden seguía SENT/PARTIAL.
            resolved[line.PurchaseOrderLineId] = resolved.GetValueOrDefault(line.PurchaseOrderLineId) + qty;
            var target = PurchaseOrderRules.TargetStatus(lines.Select(l => (l.QtyOrdered, l.QtyReceived, resolved.GetValueOrDefault(l.PurchaseOrderLineId))));
            if (target == PurchaseOrderStatuses.Received && code is PurchaseOrderStatuses.Sent or PurchaseOrderStatuses.Partial)
                await PurchasingSupport.AdvanceAsync(db, statuses, po, PurchaseOrderStatuses.Received, AllResolvedComment, ct2);

            await db.SaveGuardedAsync(PurchaseOrderService.ConcurrencyMessage, ct2);
            return newPoId;
        }, ct);

        var lineDto = (await LineDtosAsync(poId, ct)).First(l => l.PurchaseOrderLineId == lineId);
        var poDto = await purchaseOrders.GetByIdAsync(poId, ct);
        var reorderDto = reorderId is int rid ? await purchaseOrders.GetByIdAsync(rid, ct) : null;
        return new ShortageResolveResultDto(lineDto, poDto, reorderDto);
    }

    // ================================================================ acciones

    /// <summary>REORDER: orden DRAFT nueva al mismo proveedor y almacén con el pendiente de la línea al costo congelado.</summary>
    private async Task<PurchaseOrder> ReorderAsync(PurchaseOrder po, PurchaseOrderLine line, decimal qty, CancellationToken ct)
    {
        var supplier = await db.Set<Supplier>().AsNoTracking().FirstAsync(s => s.SupplierId == po.SupplierId, ct);
        if (!supplier.IsActive) throw new StatusRuleException(PurchaseOrderRules.SupplierInactive);
        var warehouse = await db.Set<Warehouse>().AsNoTracking().FirstAsync(w => w.WarehouseId == po.WarehouseId, ct);
        if (!warehouse.IsActive) throw new StatusRuleException(PurchaseOrderRules.WarehouseInactive);
        var product = await db.Set<Product>().AsNoTracking().FirstAsync(p => p.ProductId == line.ProductId, ct);
        if (!product.IsActive) throw new StatusRuleException(PurchaseOrderRules.ProductInactive(product.Sku));
        return await purchaseOrders.CreateDraftAsync(po.SupplierId, po.WarehouseId, _clock.Today, null,
            po.CurrencyLookupId, ReorderNotes(po.Number), new[] { new PurchaseOrderLinePlan(line.ProductId, qty, line.UnitCost) }, ct);
    }

    /// <summary>
    /// MANUAL_ADJUSTMENT: entrada ADJUSTMENT (magnitud; el ledger guarda el signo +) a una posición del almacén de la orden,
    /// con motivo PO_SHORTAGE/FOUND y Ref PURCHASE_ORDER + id. LOT exige lote (existente o nuevo vía EnsureLot); SERIAL exige
    /// tantas series como la cantidad. Devuelve el primer movimiento.
    /// </summary>
    private async Task<long> ManualAdjustmentAsync(PurchaseOrder po, PurchaseOrderLine line, decimal qty, ShortageResolveRequest req,
        string reasonCode, IReadOnlyList<string> serials, string? notes, CancellationToken ct)
    {
        var product = await db.Set<Product>().AsNoTracking().FirstAsync(p => p.ProductId == line.ProductId, ct);
        var bin = await (from b in db.Set<WarehouseBin>().AsNoTracking()
                         join w in db.Set<Warehouse>().AsNoTracking() on b.WarehouseId equals w.WarehouseId
                         where b.WarehouseBinId == req.BinId!.Value && b.WarehouseId == po.WarehouseId
                         select b).FirstOrDefaultAsync(ct)
                  ?? throw new NotFoundException("Posición", feminine: true);
        var tracking = (await lookups.GetAsync(product.TrackingTypeLookupId, ct))?.InternalCode ?? TrackingTypes.None;

        int? lotId = null;
        if (req.LotId is int requestedLot)
            lotId = await (from l in db.Set<InventoryLot>().AsNoTracking()
                           join p in db.Set<Product>().AsNoTracking() on l.ProductId equals p.ProductId
                           where l.LotId == requestedLot && l.ProductId == product.ProductId
                           select (int?)l.LotId).FirstOrDefaultAsync(ct)
                    ?? throw new NotFoundException("Lote");
        else if (req.Lot is not null)
            lotId = await EnsureLotAsync(product.ProductId, req.Lot, ct);

        if (AdjustmentRules.ValidateTracking(tracking, product.Sku, qty, lotId is not null, serials.Count) is { } trackingError)
            throw new ValidationException(trackingError.Field, trackingError.Message);

        var postings = new List<InventoryPosting>();
        if (serials.Count > 0)
        {
            var numbers = serials.ToList();
            var known = await (from s in db.Set<InventorySerial>().AsNoTracking()
                               join p in db.Set<Product>().AsNoTracking() on s.ProductId equals p.ProductId
                               where s.ProductId == product.ProductId && numbers.Contains(s.SerialNumber)
                               select new { s.SerialId, s.SerialNumber, s.LotId }).ToListAsync(ct);
            var bySerial = known.ToDictionary(s => s.SerialNumber, StringComparer.OrdinalIgnoreCase);
            foreach (var serial in serials)
            {
                var existing = bySerial.GetValueOrDefault(serial);
                postings.Add(Entry(product.ProductId, 1m, po, bin.WarehouseBinId, existing?.LotId ?? lotId, existing?.SerialId, serial, reasonCode, notes));
            }
        }
        else postings.Add(Entry(product.ProductId, qty, po, bin.WarehouseBinId, lotId, null, null, reasonCode, notes));

        var ids = await ledger.PostAsync(postings, ct);
        return ids[0];
    }

    private static InventoryPosting Entry(int productId, decimal magnitude, PurchaseOrder po, int binId, int? lotId, int? serialId,
        string? serialNumber, string reasonCode, string? notes)
        => new(InventoryTxnTypes.Adjustment, productId, magnitude, LotId: lotId, SerialId: serialId, SerialNumber: serialNumber,
            ToWarehouseId: po.WarehouseId, ToBinId: binId, RefEntityType: EntityTypes.PurchaseOrder, RefId: po.PurchaseOrderId,
            ReasonCode: reasonCode, Notes: notes);

    /// <summary>
    /// EnsureLot de la entrada (D34): valida el lote capturado y delega en la sentencia (17) de InventoryQueries (UPDLOCK +
    /// HOLDLOCK): reutiliza el lote con ese número si las fechas capturadas coinciden (409 si difieren); si no existe, lo crea.
    /// </summary>
    private async Task<int> EnsureLotAsync(int productId, LotInput input, CancellationToken ct)
    {
        var (number, error) = AdjustmentRules.ValidateLotInput(input.Number, input.ManufactureDate, input.ExpiryDate);
        if (error is not null) throw new ValidationException("lot", error);
        return await db.EnsureLotAsync(productId, number!, input.ManufactureDate, input.ExpiryDate, ct);
    }

    // ================================================================ DTOs

    /// <summary>Todas las líneas de la orden con su pendiente, costo pendiente y bitácora de resoluciones (sin N+1).</summary>
    private async Task<IReadOnlyList<ShortageLineDto>> LineDtosAsync(int poId, CancellationToken ct)
    {
        var lines = await (from l in db.Set<PurchaseOrderLine>().AsNoTracking()
                           join p in db.Set<Product>().AsNoTracking() on l.ProductId equals p.ProductId
                           where l.PurchaseOrderId == poId
                           orderby l.PurchaseOrderLineId
                           select new { l.PurchaseOrderLineId, p.PublicId, p.Sku, p.Name, l.QtyOrdered, l.QtyReceived, l.UnitCost }).ToListAsync(ct);
        var resolutions = await db.Set<PurchaseOrderShortageResolution>().AsNoTracking().Where(r => r.PurchaseOrderId == poId)
            .OrderBy(r => r.CreatedAtUtc).ThenBy(r => r.PurchaseOrderShortageResolutionId).ToListAsync(ct);

        var reorderIds = resolutions.Where(r => r.ReorderPurchaseOrderId != null).Select(r => r.ReorderPurchaseOrderId!.Value).Distinct().ToList();
        var reorders = await db.Set<PurchaseOrder>().AsNoTracking().Where(p => reorderIds.Contains(p.PurchaseOrderId))
            .Select(p => new { p.PurchaseOrderId, p.PublicId, p.Number }).ToDictionaryAsync(p => p.PurchaseOrderId, ct);
        var userIds = resolutions.Where(r => r.CreatedBy != null).Select(r => r.CreatedBy!.Value).Distinct().ToList();
        var users = await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, Name = u.FullName ?? u.Email }).ToDictionaryAsync(u => u.Id, u => u.Name, ct);
        var labels = new Dictionary<int, (string Code, string Label)>();
        foreach (var id in resolutions.Select(r => r.ActionLookupId)
                     .Concat(resolutions.Where(r => r.ReasonLookupId != null).Select(r => r.ReasonLookupId!.Value)).Distinct())
            if (await lookups.GetAsync(id, ct) is { } lc) labels[id] = (lc.InternalCode, MultilingualText.Resolve(lc.LabelJson, tenant.Lang));

        return lines.Select(l =>
        {
            var mine = resolutions.Where(r => r.PurchaseOrderLineId == l.PurchaseOrderLineId).ToList();
            var resolvedQty = mine.Sum(r => r.Quantity);
            var pending = ShortageRules.Pending(l.QtyOrdered, l.QtyReceived, resolvedQty);
            var dtos = mine.Select(r =>
            {
                var action = labels.GetValueOrDefault(r.ActionLookupId);
                (string Code, string Label)? reason = r.ReasonLookupId is int rl && labels.TryGetValue(rl, out var x) ? x : null;
                var reorder = r.ReorderPurchaseOrderId is int ro ? reorders.GetValueOrDefault(ro) : null;
                return new ShortageResolutionDto(r.PurchaseOrderShortageResolutionId, action.Code ?? "", action.Label ?? action.Code ?? "",
                    r.Quantity, reason?.Code, reason?.Label, r.Notes, reorder?.PublicId, reorder?.Number, r.InventoryTransactionId,
                    r.CreatedAtUtc, r.CreatedBy is int u ? users.GetValueOrDefault(u) : null);
            }).ToList();
            return new ShortageLineDto(l.PurchaseOrderLineId, l.PublicId, l.Sku, l.Name, l.QtyOrdered, l.QtyReceived, resolvedQty, pending,
                l.UnitCost, ShortageRules.PendingCost(pending, l.UnitCost), dtos);
        }).ToList();
    }
}
