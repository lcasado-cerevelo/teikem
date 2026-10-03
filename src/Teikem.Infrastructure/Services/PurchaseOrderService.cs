using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Clients;
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
/// Lote 6 (P8) — órdenes de compra mínimas (Compras 13B, D9): borrador PO-#####, enviar, recibir parcial/completa (vía la
/// costura IPurchaseOrderReceiving que consume la Recepción), cancelar (también desde PARTIAL, con bitácora; D47) y
/// eliminar (baja lógica, sin recepciones).
/// - Editar se protege con la capacidad EDIT_PURCHASE_ORDER (D46): sembrada negada en SENT, PARTIAL, RECEIVED y
///   CANCELLED; el tenant puede habilitarla en SENT o PARTIAL. Aun así, las líneas con recepciones no se eliminan, no bajan
///   de lo recibido y no cambian de costo (PurchaseOrderRules.ReceivedLineLocked).
/// - Cancelar: no hay regla lateral sembrada para CANCELLED en PURCHASE_ORDER, así que el motor la permite desde DRAFT, SENT
///   y PARTIAL; RECEIVED es terminal. Con un recibo OPEN sobre la orden → 409.
/// - Solo productos propios (Product.ClientId NULL), sin repetir, costo congelado en la línea (D35).
/// - Orden de bloqueo del lote: PurchaseOrder (UPDLOCK) → saldos → NumberSequence al final. Toda escritura sobre una orden
///   existente bloquea su encabezado y re-verifica el estatus dentro de la transacción.
/// El TenantId sale del principal; la orden se expone por PublicId y sus líneas (sin TenantId) por id entero SIEMPRE bajo
/// su orden filtrada.
/// </summary>
public sealed class PurchaseOrderService(
    TeikemDbContext db,
    ITenantContext tenant,
    ILookupCache lookups,
    StatusService statuses,
    INumberSequenceService numbers,
    ITenantClock? clock = null)
{
    private readonly ITenantClock _clock = clock ?? TenantClock.Default;

    public const string ConcurrencyMessage = "La orden de compra fue modificada por otro usuario; recargue e intente de nuevo.";
    public const string NumberTakenMessage = "Ya existe una orden de compra con ese número; intente de nuevo.";

    /// <summary>
    /// Campos que el PATCH rechaza aunque lleguen en el cuerpo (van a Extra por no estar en el contrato). Proveedor y almacén
    /// salieron de la lista el 2026-09-30: son parte del contrato y se cambian solo en DRAFT.
    /// </summary>
    private static readonly string[] ImmutableOnPatch = { "number", "orderDate", "currency", "status", "statusCode" };

    // ================================================================ lista y ficha

    public async Task<PurchaseOrderPageDto> ListAsync(PurchaseOrderQuery? q, CancellationToken ct)
    {
        q ??= new PurchaseOrderQuery();
        var skip = Math.Max(0, q.Skip);
        var take = q.Take <= 0 ? 100 : Math.Min(q.Take, PurchaseOrderRules.MaxPageSize);

        var query = db.Set<PurchaseOrder>().AsNoTracking().Where(p => p.IsActive);
        if (q.Status is { Length: > 0 })
        {
            var codes = q.Status.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim().ToUpperInvariant()).Distinct().ToList();
            var ids = await db.StatusCodes.AsNoTracking()
                .Where(s => s.Entity == StatusDomains.PurchaseOrderStatus && codes.Contains(s.InternalCode))
                .Select(s => s.StatusCodeId).ToListAsync(ct);
            query = query.Where(p => ids.Contains(p.StatusCodeId));
        }
        // Lote 12: proveedores y almacenes múltiples, combinados con los singulares (compatibilidad).
        var supplierIds = (q.SupplierIds ?? Array.Empty<int>()).Concat(q.SupplierId is int supplierId ? new[] { supplierId } : Array.Empty<int>())
            .Distinct().ToList();
        if (supplierIds.Count > 0) query = query.Where(p => supplierIds.Contains(p.SupplierId));
        var warehousePublicIds = (q.WarehousePublicIds ?? Array.Empty<Guid>()).Concat(q.WarehousePublicId is Guid wh ? new[] { wh } : Array.Empty<Guid>())
            .Distinct().ToList();
        if (warehousePublicIds.Count > 0)
            query = query.Where(p => db.Set<Warehouse>().Any(w => w.WarehouseId == p.WarehouseId && warehousePublicIds.Contains(w.PublicId)));
        if (q.From is DateOnly from) query = query.Where(p => p.OrderDate >= from);
        if (q.To is DateOnly to) query = query.Where(p => p.OrderDate <= to);
        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var s = q.Search.Trim();
            query = query.Where(p => p.Number.Contains(s)
                || (p.Notes != null && p.Notes.Contains(s))
                || db.Set<Supplier>().Any(su => su.SupplierId == p.SupplierId && su.Name.Contains(s))
                || (from l in db.Set<PurchaseOrderLine>()
                    join pr in db.Set<Product>() on l.ProductId equals pr.ProductId
                    where l.PurchaseOrderId == p.PurchaseOrderId && (pr.Sku.Contains(s) || pr.Name.Contains(s))
                    select l.PurchaseOrderLineId).Any());
        }

        var total = await query.CountAsync(ct);
        var page = await query.OrderByDescending(p => p.OrderDate).ThenByDescending(p => p.PurchaseOrderId)
            .Skip(skip).Take(take).ToListAsync(ct);
        var dtos = await ToDtosAsync(page, ct);
        return new PurchaseOrderPageDto(total, skip, take, dtos);
    }

    public async Task<PurchaseOrderDto> GetAsync(Guid publicId, CancellationToken ct)
    {
        var po = await db.Set<PurchaseOrder>().AsNoTracking().FirstOrDefaultAsync(p => p.PublicId == publicId && p.IsActive, ct)
                 ?? throw new NotFoundException(PurchaseOrderRules.NotFound, feminine: true);
        return (await ToDtosAsync(new List<PurchaseOrder> { po }, ct))[0];
    }

    /// <summary>Ficha por id interno (uso de los servicios de compras; la orden se alcanza bajo el filtro de tenant).</summary>
    public async Task<PurchaseOrderDto> GetByIdAsync(int purchaseOrderId, CancellationToken ct)
    {
        var po = await db.Set<PurchaseOrder>().AsNoTracking().FirstOrDefaultAsync(p => p.PurchaseOrderId == purchaseOrderId, ct)
                 ?? throw new NotFoundException(PurchaseOrderRules.NotFound, feminine: true);
        return (await ToDtosAsync(new List<PurchaseOrder> { po }, ct))[0];
    }

    // ================================================================ alta

    /// <summary>
    /// Alta en DRAFT con número PO-#####: proveedor activo del tenant, almacén indicado (o el único activo), fecha de la
    /// orden (hoy en la zona de la compañía por defecto), fecha esperada ≥ fecha de la orden, moneda del catálogo y líneas válidas.
    /// </summary>
    public async Task<PurchaseOrderDto> CreateAsync(PurchaseOrderCreateRequest req, CancellationToken ct)
    {
        if (req is null) throw new ValidationException("body", "El cuerpo de la solicitud es obligatorio.");
        var errors = new Dictionary<string, string[]>();
        if (req.SupplierId is null) errors["supplierId"] = new[] { PurchaseOrderRules.SupplierRequired };
        var orderDate = req.OrderDate ?? _clock.Today;
        if (req.ExpectedDate is DateOnly expected && expected < orderDate) errors["expectedDate"] = new[] { PurchaseOrderRules.ExpectedBeforeOrder };
        var (notes, notesError) = NormalizeNotes(req.Notes);
        if (notesError is not null) errors["notes"] = new[] { notesError };
        int? currencyId = null;
        if (!string.IsNullOrWhiteSpace(req.Currency))
        {
            currencyId = await lookups.TryGetIdAsync(LookupDomains.Currency, req.Currency.Trim().ToUpperInvariant(), ct);
            if (currencyId is null) errors["currency"] = new[] { PurchaseOrderRules.UnknownCurrency(req.Currency.Trim()) };
        }
        if (errors.Count > 0) throw new ValidationException(errors);

        var supplier = await db.Set<Supplier>().AsNoTracking().FirstOrDefaultAsync(s => s.SupplierId == req.SupplierId!.Value, ct)
                       ?? throw new NotFoundException("Proveedor");
        if (!supplier.IsActive) throw new StatusRuleException(PurchaseOrderRules.SupplierInactive);
        var warehouse = await ResolveWarehouseOrDefaultAsync(req.WarehousePublicId, ct);
        if (!warehouse.IsActive) throw new StatusRuleException(PurchaseOrderRules.WarehouseInactive);
        var lines = await ValidateLinesAsync(req.Lines, ct);

        await EnsureNumberingAsync(ct);
        var poId = await db.RunInTransactionAsync(async ct2 =>
        {
            var po = await CreateDraftAsync(supplier.SupplierId, warehouse.WarehouseId, orderDate, req.ExpectedDate, currencyId, notes, lines, ct2);
            return po.PurchaseOrderId;
        }, ct);
        return await GetByIdAsync(poId, ct);
    }

    /// <summary>Asegura el contador PURCHASE (autocommit, ANTES de abrir la transacción que llamará a CreateDraftAsync).</summary>
    public Task EnsureNumberingAsync(CancellationToken ct) => numbers.EnsureAsync(NumberKinds.Purchase, null, ct);

    /// <summary>
    /// Uso interno (alta y Reordenar): crea la orden en DRAFT dentro de la transacción ambiente, con historial null → DRAFT.
    /// El número PO-##### se toma al final (NumberSequence es el último bloqueo del orden del lote). El llamador validó
    /// proveedor, almacén y líneas, y llamó antes a EnsureNumberingAsync.
    /// </summary>
    public async Task<PurchaseOrder> CreateDraftAsync(int supplierId, int warehouseId, DateOnly orderDate, DateOnly? expectedDate,
        int? currencyLookupId, string? notes, IReadOnlyList<PurchaseOrderLinePlan> lines, CancellationToken ct)
    {
        var tenantId = ((TenantContext)tenant).RequireTenantId();
        if (lines.Count == 0) throw new ValidationException("lines", PurchaseOrderRules.LinesRequired);
        var initial = await statuses.GetInitialAsync(StatusDomains.PurchaseOrderStatus, ct);

        var seq = await numbers.NextAsync(NumberKinds.Purchase, null, ct);
        var po = new PurchaseOrder
        {
            PublicId = Guid.NewGuid(), TenantId = tenantId, SupplierId = supplierId, WarehouseId = warehouseId,
            Number = NumberFormat.Resolve(PurchaseOrderRules.NumberPattern, seq), OrderDate = orderDate, ExpectedDate = expectedDate,
            StatusCodeId = initial.StatusCodeId, CurrencyLookupId = currencyLookupId, Notes = notes, IsActive = true,
            CreatedAtUtc = DateTime.UtcNow, CreatedBy = tenant.UserId,
        };
        db.Set<PurchaseOrder>().Add(po);
        await db.SaveGuardedAsync(NumberTakenMessage, ct);

        foreach (var l in lines)
            db.Set<PurchaseOrderLine>().Add(new PurchaseOrderLine
            {
                PurchaseOrderId = po.PurchaseOrderId, ProductId = l.ProductId, QtyOrdered = l.QtyOrdered, QtyReceived = 0m, UnitCost = l.UnitCost,
            });
        var born = await statuses.TransitionAsync(StatusDomains.PurchaseOrderStatus, EntityTypes.PurchaseOrder, po.PurchaseOrderId,
            null, initial.InternalCode, null, ct);
        po.StatusCodeId = born.StatusCodeId;
        await db.SaveGuardedAsync(NumberTakenMessage, ct);
        return po;
    }

    // ================================================================ edición

    /// <summary>
    /// PATCH: fecha esperada, notas ("" las quita), reemplazo completo de líneas y, solo en DRAFT, proveedor y almacén. Exige
    /// la capacidad EDIT_PURCHASE_ORDER en el estatus actual (422 fuera de DRAFT por defecto). Número, fecha y moneda no
    /// cambian (400). Con un recibo abierto no se cambian las líneas (409). rowVersion opcional (409 si cambió).
    /// Proveedor y almacén (ajuste del 2026-09-30): un valor igual al actual no es un cambio (se acepta en cualquier estatus
    /// y no se revalida); uno distinto fuera de DRAFT → 409 SupplierWarehouseOnlyDraft, antes de la capacidad. En DRAFT se
    /// validan como en el alta: almacén ajeno o inexistente 404, proveedor ajeno o inexistente 404, dados de baja 422. En
    /// DRAFT la orden no tiene avisos ni recibos (se crean al recibir, desde SENT), así que cambiar el almacén no toca nada
    /// más: las líneas no dependen del almacén.
    /// </summary>
    public async Task<PurchaseOrderDto> UpdateAsync(Guid publicId, PurchaseOrderPatchRequest req, CancellationToken ct)
    {
        if (req is null) throw new ValidationException("body", "El cuerpo de la solicitud es obligatorio.");
        if (req.Extra is not null)
            foreach (var key in req.Extra.Keys)
            {
                var hit = ImmutableOnPatch.FirstOrDefault(f => string.Equals(f, key, StringComparison.OrdinalIgnoreCase));
                if (hit is not null) throw new ValidationException(hit, PurchaseOrderRules.ImmutableField(hit));
            }
        var (notes, notesError) = NormalizeNotes(req.Notes);
        if (notesError is not null) throw new ValidationException("notes", notesError);
        var requested = req.Lines is null ? null : await ValidateLinesAsync(req.Lines, ct);
        var poId = await ResolveIdAsync(publicId, ct);

        await db.RunInTransactionAsync(async ct2 =>
        {
            var po = await PurchasingSupport.LockPurchaseOrderAsync(db, poId, ct2);
            if (!po.IsActive) throw new NotFoundException(PurchaseOrderRules.NotFound, feminine: true);
            PurchasingSupport.EnsureRowVersion(po.RowVersion, req.RowVersion, ConcurrencyMessage);

            // Proveedor y almacén: solo un valor distinto del actual es un cambio, y solo se permite en DRAFT.
            Warehouse? newWarehouse = null;
            if (req.WarehousePublicId is Guid warehousePublicId)
            {
                var wh = await ResolveWarehouseOrDefaultAsync(warehousePublicId, ct2);   // ajeno o inexistente → 404
                if (wh.WarehouseId != po.WarehouseId) newWarehouse = wh;
            }
            var newSupplierId = req.SupplierId is int requestedSupplier && requestedSupplier != po.SupplierId ? requestedSupplier : (int?)null;
            if (newWarehouse is not null || newSupplierId is not null)
            {
                var code = await PurchasingSupport.StatusCodeOfAsync(db, po.StatusCodeId, ct2);
                if (code != PurchaseOrderStatuses.Draft) throw new ConflictException(PurchaseOrderRules.SupplierWarehouseOnlyDraft);
            }

            await statuses.EnsureAllowedAsync(EntityTypes.PurchaseOrder, po.StatusCodeId, Capabilities.EditPurchaseOrder, ct2);

            // Mismas validaciones y el mismo orden que el alta (proveedor y luego almacén); se asigna solo si ambos pasan.
            if (newSupplierId is int supplierId)
            {
                var supplier = await db.Set<Supplier>().AsNoTracking().FirstOrDefaultAsync(s => s.SupplierId == supplierId, ct2)
                               ?? throw new NotFoundException("Proveedor");
                if (!supplier.IsActive) throw new StatusRuleException(PurchaseOrderRules.SupplierInactive);
            }
            if (newWarehouse is { IsActive: false }) throw new StatusRuleException(PurchaseOrderRules.WarehouseInactive);
            if (newSupplierId is int changedSupplier) po.SupplierId = changedSupplier;
            if (newWarehouse is not null) po.WarehouseId = newWarehouse.WarehouseId;

            if (req.ExpectedDate is DateOnly expected)
            {
                if (expected < po.OrderDate) throw new ValidationException("expectedDate", PurchaseOrderRules.ExpectedBeforeOrder);
                po.ExpectedDate = expected;
            }
            if (req.Notes is not null) po.Notes = notes;

            if (requested is not null)
            {
                if (await PurchasingSupport.HasOpenReceiptAsync(db, po.PurchaseOrderId, ct2))
                    throw new ConflictException(PurchaseOrderRules.HasOpenReceiptEdit);
                var current = await db.Set<PurchaseOrderLine>().Where(l => l.PurchaseOrderId == po.PurchaseOrderId).ToListAsync(ct2);
                var lineIds = current.Select(l => l.PurchaseOrderLineId).ToList();
                var referenced = (await db.Set<AsnLine>().AsNoTracking()
                        .Where(al => al.PurchaseOrderLineId != null && lineIds.Contains(al.PurchaseOrderLineId.Value))
                        .Select(al => al.PurchaseOrderLineId!.Value).ToListAsync(ct2))
                    .Concat(await db.Set<PurchaseOrderShortageResolution>().AsNoTracking()
                        .Where(r => r.PurchaseOrderId == po.PurchaseOrderId).Select(r => r.PurchaseOrderLineId).ToListAsync(ct2))
                    .ToHashSet();
                var productIds = current.Select(l => l.ProductId).Distinct().ToList();
                var skus = await db.Set<Product>().AsNoTracking().Where(p => productIds.Contains(p.ProductId))
                    .ToDictionaryAsync(p => p.ProductId, p => p.Sku, ct2);
                var existing = current.Select(l => new PurchaseOrderExistingLine(l.PurchaseOrderLineId, l.ProductId,
                    skus.GetValueOrDefault(l.ProductId, "?"), l.QtyReceived, l.UnitCost, referenced.Contains(l.PurchaseOrderLineId))).ToList();

                var (changes, errors) = PurchaseOrderRules.PlanReplacement(existing, requested);
                if (errors.Count > 0) throw new ConflictException(errors.Values.First()[0]) { Errors = errors };

                foreach (var (lineId, plan) in changes.Updates)
                {
                    var line = current.First(l => l.PurchaseOrderLineId == lineId);
                    line.QtyOrdered = plan.QtyOrdered;
                    line.UnitCost = plan.UnitCost;
                }
                foreach (var plan in changes.Additions)
                    db.Set<PurchaseOrderLine>().Add(new PurchaseOrderLine
                    {
                        PurchaseOrderId = po.PurchaseOrderId, ProductId = plan.ProductId, QtyOrdered = plan.QtyOrdered, QtyReceived = 0m, UnitCost = plan.UnitCost,
                    });
                // Solo líneas sin recepciones ni referencias (nunca tocaron el ledger ni un aviso): se quitan de la orden.
                foreach (var lineId in changes.Removals)
                    db.Set<PurchaseOrderLine>().Remove(current.First(l => l.PurchaseOrderLineId == lineId));
            }
            await db.SaveGuardedAsync(ConcurrencyMessage, ct2);
        }, ct);
        return await GetByIdAsync(poId, ct);
    }

    // ================================================================ estatus

    /// <summary>DRAFT → SENT (422 desde cualquier otro estatus), con el comentario en el historial.</summary>
    public async Task<PurchaseOrderDto> SendAsync(Guid publicId, PurchaseOrderStatusRequest? req, CancellationToken ct)
    {
        var poId = await ResolveIdAsync(publicId, ct);
        var comment = PurchasingSupport.Comment(req?.Comment);
        await db.RunInTransactionAsync(async ct2 =>
        {
            var po = await PurchasingSupport.LockPurchaseOrderAsync(db, poId, ct2);
            if (!po.IsActive) throw new NotFoundException(PurchaseOrderRules.NotFound, feminine: true);
            PurchasingSupport.EnsureRowVersion(po.RowVersion, req?.RowVersion, ConcurrencyMessage);
            var code = await PurchasingSupport.StatusCodeOfAsync(db, po.StatusCodeId, ct2);
            if (code != PurchaseOrderStatuses.Draft) throw new StatusRuleException(PurchaseOrderRules.OnlyDraftSends);
            if (!await db.Set<PurchaseOrderLine>().AnyAsync(l => l.PurchaseOrderId == po.PurchaseOrderId, ct2))
                throw new ValidationException("lines", PurchaseOrderRules.LinesRequired);
            var to = await statuses.TransitionAsync(StatusDomains.PurchaseOrderStatus, EntityTypes.PurchaseOrder, po.PurchaseOrderId,
                po.StatusCodeId, PurchaseOrderStatuses.Sent, comment, ct2);
            po.StatusCodeId = to.StatusCodeId;
            await db.SaveGuardedAsync(ConcurrencyMessage, ct2);
        }, ct);
        return await GetByIdAsync(poId, ct);
    }

    /// <summary>
    /// Cancelación con bitácora (D47, maestro L461): desde DRAFT, SENT o PARTIAL; RECEIVED → 422. Con un recibo OPEN sobre
    /// un aviso de la orden → 409 HasOpenReceipt. El comentario queda en el historial PURCHASE_ORDER. Una orden cancelada
    /// deja de mostrar su faltante.
    /// </summary>
    public async Task<PurchaseOrderDto> CancelAsync(Guid publicId, PurchaseOrderStatusRequest? req, CancellationToken ct)
    {
        var poId = await ResolveIdAsync(publicId, ct);
        var comment = PurchasingSupport.Comment(req?.Comment);
        await db.RunInTransactionAsync(async ct2 =>
        {
            var po = await PurchasingSupport.LockPurchaseOrderAsync(db, poId, ct2);
            if (!po.IsActive) throw new NotFoundException(PurchaseOrderRules.NotFound, feminine: true);
            PurchasingSupport.EnsureRowVersion(po.RowVersion, req?.RowVersion, ConcurrencyMessage);
            var code = await PurchasingSupport.StatusCodeOfAsync(db, po.StatusCodeId, ct2);
            if (PurchaseOrderRules.CancelError(code) is { } error) throw new StatusRuleException(error);
            if (await PurchasingSupport.HasOpenReceiptAsync(db, po.PurchaseOrderId, ct2))
                throw new ConflictException(PurchaseOrderRules.HasOpenReceipt);
            // Sin regla lateral sembrada para CANCELLED: el motor la permite desde DRAFT, SENT y PARTIAL.
            var to = await statuses.TransitionAsync(StatusDomains.PurchaseOrderStatus, EntityTypes.PurchaseOrder, po.PurchaseOrderId,
                po.StatusCodeId, PurchaseOrderStatuses.Cancelled, comment, ct2);
            po.StatusCodeId = to.StatusCodeId;
            await db.SaveGuardedAsync(ConcurrencyMessage, ct2);
        }, ct);
        return await GetByIdAsync(poId, ct);
    }

    /// <summary>
    /// Baja lógica (IsActive = 0): solo DRAFT, SENT o CANCELLED, sin recepciones confirmadas y sin recibo abierto (409 con
    /// el mensaje de PurchaseOrderRules.DeleteError).
    /// </summary>
    public async Task DeleteAsync(Guid publicId, CancellationToken ct)
    {
        var poId = await ResolveIdAsync(publicId, ct);
        await db.RunInTransactionAsync(async ct2 =>
        {
            var po = await PurchasingSupport.LockPurchaseOrderAsync(db, poId, ct2);
            if (!po.IsActive) throw new NotFoundException(PurchaseOrderRules.NotFound, feminine: true);
            var code = await PurchasingSupport.StatusCodeOfAsync(db, po.StatusCodeId, ct2);
            var hasReceipts = await PurchasingSupport.HasReceiptsAsync(db, po.PurchaseOrderId, ct2);
            var hasOpen = await PurchasingSupport.HasOpenReceiptAsync(db, po.PurchaseOrderId, ct2);
            if (PurchaseOrderRules.DeleteError(code, hasReceipts, hasOpen) is { } error) throw new ConflictException(error);
            po.IsActive = false;
            await db.SaveGuardedAsync(ConcurrencyMessage, ct2);
        }, ct);
    }

    // ================================================================ DTOs (compartidos con PurchaseShortageService)

    /// <summary>Fichas de órdenes sin N+1: proveedores, almacenes, líneas, resoluciones, recibos y capacidad por estatus.</summary>
    public async Task<IReadOnlyList<PurchaseOrderDto>> ToDtosAsync(List<PurchaseOrder> rows, CancellationToken ct)
    {
        if (rows.Count == 0) return Array.Empty<PurchaseOrderDto>();
        var poIds = rows.Select(p => p.PurchaseOrderId).ToList();
        var supplierIds = rows.Select(p => p.SupplierId).Distinct().ToList();
        var whIds = rows.Select(p => p.WarehouseId).Distinct().ToList();

        var suppliers = await db.Set<Supplier>().AsNoTracking().Where(s => supplierIds.Contains(s.SupplierId))
            .ToDictionaryAsync(s => s.SupplierId, s => s.Name, ct);
        var warehouses = await db.Set<Warehouse>().AsNoTracking().Where(w => whIds.Contains(w.WarehouseId))
            .Select(w => new { w.WarehouseId, w.PublicId, w.Code }).ToDictionaryAsync(w => w.WarehouseId, ct);
        var lines = await (from l in db.Set<PurchaseOrderLine>().AsNoTracking()
                           join p in db.Set<Product>().AsNoTracking() on l.ProductId equals p.ProductId
                           where poIds.Contains(l.PurchaseOrderId)
                           orderby l.PurchaseOrderLineId
                           select new { l.PurchaseOrderId, l.PurchaseOrderLineId, p.PublicId, p.Sku, p.Name, l.QtyOrdered, l.QtyReceived, l.UnitCost })
            .ToListAsync(ct);
        var resolved = await PurchasingSupport.ResolvedByLineAsync(db, poIds, ct);
        var flags = await PurchasingSupport.ReceiptFlagsAsync(db, poIds, ct);
        var statusMap = await PurchasingSupport.StatusMapAsync(db, tenant, StatusDomains.PurchaseOrderStatus, ct);

        var currencyIds = rows.Where(p => p.CurrencyLookupId != null).Select(p => p.CurrencyLookupId!.Value).Distinct().ToList();
        var currencies = new Dictionary<int, string>();
        foreach (var id in currencyIds)
            if (await lookups.GetAsync(id, ct) is { } lc) currencies[id] = lc.InternalCode;

        var editable = new Dictionary<int, bool>();
        foreach (var statusId in rows.Select(p => p.StatusCodeId).Distinct())
            editable[statusId] = await statuses.IsAllowedAsync(EntityTypes.PurchaseOrder, statusId, Capabilities.EditPurchaseOrder, ct);

        return rows.Select(po =>
        {
            var s = statusMap.GetValueOrDefault(po.StatusCodeId);
            var code = s?.Code ?? string.Empty;
            var w = warehouses.GetValueOrDefault(po.WarehouseId);
            var f = flags.GetValueOrDefault(po.PurchaseOrderId);
            var lineDtos = lines.Where(l => l.PurchaseOrderId == po.PurchaseOrderId).Select(l =>
            {
                var res = resolved.GetValueOrDefault(l.PurchaseOrderLineId);
                var pending = ShortageRules.Pending(l.QtyOrdered, l.QtyReceived, res);
                return new PurchaseOrderLineDto(l.PurchaseOrderLineId, l.PublicId, l.Sku, l.Name, l.QtyOrdered, l.QtyReceived, res, pending,
                    l.UnitCost, PurchaseOrderRules.LineAmount(l.QtyOrdered, l.UnitCost));
            }).ToList();
            var isCancelled = code == PurchaseOrderStatuses.Cancelled;
            var hasShortage = po.IsActive && !isCancelled && f.HasConfirmed && lineDtos.Any(l => l.QtyPending > 0m);
            var total = PurchaseOrderRules.Total(lineDtos.Select(l => (l.QtyOrdered, l.UnitCost)));
            return new PurchaseOrderDto(po.PurchaseOrderId, po.PublicId, po.Number, po.SupplierId, suppliers.GetValueOrDefault(po.SupplierId, ""),
                w?.PublicId ?? Guid.Empty, w?.Code ?? "", po.OrderDate, po.ExpectedDate, code, s?.Label ?? code,
                po.CurrencyLookupId is int cid ? currencies.GetValueOrDefault(cid) : null, po.Notes, total, hasShortage,
                po.IsActive && editable.GetValueOrDefault(po.StatusCodeId, true),
                po.IsActive && PurchaseOrderRules.CanCancel(code) && !f.HasOpen,
                po.IsActive && PurchaseOrderRules.IsDeletable(code, f.HasConfirmed || lineDtos.Any(l => l.QtyReceived > 0m), f.HasOpen),
                po.IsActive, lineDtos, Convert.ToBase64String(po.RowVersion ?? Array.Empty<byte>()));
        }).ToList();
    }

    // ================================================================ helpers

    private async Task<int> ResolveIdAsync(Guid publicId, CancellationToken ct)
        => await db.Set<PurchaseOrder>().AsNoTracking().Where(p => p.PublicId == publicId && p.IsActive)
               .Select(p => (int?)p.PurchaseOrderId).FirstOrDefaultAsync(ct)
           ?? throw new NotFoundException(PurchaseOrderRules.NotFound, feminine: true);

    /// <summary>El almacén indicado (404 si no es del tenant) o, si se omite, el único activo (ninguno o más de uno → 400).</summary>
    private Task<Warehouse> ResolveWarehouseOrDefaultAsync(Guid? publicId, CancellationToken ct)
        => WmsResolve.ResolveWarehouseOrDefaultAsync(db, publicId, ct); // única implementación (D26): 404 / 400 con más de uno / 422 sin ninguno

    /// <summary>
    /// Resuelve los productos de las líneas por PublicId (bajo el filtro de tenant; ausente → 400 por línea) y aplica las
    /// reglas puras (propio, activo, sin repetir, cantidad, costo con default Product.PurchaseCost, tope 200).
    /// </summary>
    private async Task<IReadOnlyList<PurchaseOrderLinePlan>> ValidateLinesAsync(IReadOnlyList<PurchaseOrderLineRequest>? requestLines, CancellationToken ct)
    {
        if (requestLines is null || requestLines.Count == 0) throw new ValidationException("lines", PurchaseOrderRules.LinesRequired);
        if (requestLines.Count > PurchaseOrderRules.MaxLines) throw new ValidationException("lines", PurchaseOrderRules.TooManyLines);

        var publicIds = requestLines.Where(l => l?.ProductPublicId != null).Select(l => l.ProductPublicId!.Value).Distinct().ToList();
        var products = await db.Set<Product>().AsNoTracking().Where(p => publicIds.Contains(p.PublicId)).ToListAsync(ct);
        var byPublicId = products.ToDictionary(p => p.PublicId);
        var trackingIds = products.Select(p => p.TrackingTypeLookupId).Distinct().ToList();
        var serialTracking = new HashSet<int>();
        foreach (var id in trackingIds)
            if ((await lookups.GetAsync(id, ct))?.InternalCode == TrackingTypes.Serial) serialTracking.Add(id);

        var errors = new Dictionary<string, string[]>();
        var inputs = new List<PurchaseOrderLineInput>();
        var inputIndex = new List<int>();
        for (var i = 0; i < requestLines.Count; i++)
        {
            var r = requestLines[i];
            if (r?.ProductPublicId is null) { errors[$"lines[{i}]"] = new[] { PurchaseOrderRules.ProductRequired }; continue; }
            if (!byPublicId.TryGetValue(r.ProductPublicId.Value, out var p)) { errors[$"lines[{i}]"] = new[] { "Producto no encontrado." }; continue; }
            if (!p.IsActive) { errors[$"lines[{i}]"] = new[] { PurchaseOrderRules.ProductInactive(p.Sku) }; continue; }
            inputs.Add(new PurchaseOrderLineInput(p.ProductId, p.Sku, p.ClientId is null, serialTracking.Contains(p.TrackingTypeLookupId),
                r.QtyOrdered, r.UnitCost, p.PurchaseCost));
            inputIndex.Add(i);
        }
        var (plans, ruleErrors) = PurchaseOrderRules.ValidateLines(inputs);
        // Las claves 'lines[k]' de la regla se refieren a la k-ésima entrada válida: se traducen al índice de la solicitud.
        foreach (var (key, messages) in ruleErrors)
        {
            var mapped = key;
            if (key.StartsWith("lines[", StringComparison.Ordinal) && int.TryParse(key[6..^1], out var k) && k < inputIndex.Count)
                mapped = $"lines[{inputIndex[k]}]";
            errors[mapped] = messages;
        }
        if (errors.Count > 0) throw new ValidationException(errors);
        return plans;
    }

    private static (string? Notes, string? Error) NormalizeNotes(string? notes)
    {
        if (string.IsNullOrWhiteSpace(notes)) return (null, null);
        var trimmed = notes.Trim();
        return trimmed.Length > PurchaseOrderRules.MaxNotesLength ? (null, PurchaseOrderRules.NotesTooLong) : (trimmed, null);
    }
}

/// <summary>
/// Lote 6 (P8) — apoyo compartido de Compras (PurchaseOrderService, PurchaseShortageService y PurchaseOrderReceivingService):
/// bloqueo de la orden, estatus con etiqueta del tenant, banderas de recibos, resuelto por línea y la transición
/// escalonada. Toda consulta corre bajo el filtro global de tenant; las hijas se alcanzan por su orden filtrada.
/// </summary>
public static class PurchasingSupport
{
    public sealed record StatusInfo(string Code, string Label);

    /// <summary>¿La orden tiene recibos? HasOpen = recibo activo OPEN; HasConfirmed = recibo activo RECEIVED o PUTAWAY.</summary>
    public readonly record struct ReceiptFlags(bool HasOpen, bool HasConfirmed);

    // ---------------------------------------------------------------- adaptador a la costura de P0 (InventoryQueries)

    /// <summary>
    /// Encabezado PurchaseOrder con UPDLOCK, tracked (orden del lote: ReceiptHeader &lt; Asn &lt; PurchaseOrder &lt; saldos
    /// &lt; NumberSequence). De otro tenant → 404. Con InMemory carga tracked sin bloqueo.
    /// </summary>
    public static Task<PurchaseOrder> LockPurchaseOrderAsync(TeikemDbContext db, int purchaseOrderId, CancellationToken ct)
        => db.LockPurchaseOrderAsync(purchaseOrderId, ct);

    // ---------------------------------------------------------------- consultas

    public static async Task<string> StatusCodeOfAsync(TeikemDbContext db, int statusCodeId, CancellationToken ct)
        => await db.StatusCodes.AsNoTracking().Where(s => s.StatusCodeId == statusCodeId).Select(s => s.InternalCode).FirstOrDefaultAsync(ct)
           ?? string.Empty;

    public static async Task<Dictionary<int, StatusInfo>> StatusMapAsync(TeikemDbContext db, ITenantContext tenant, string domain, CancellationToken ct)
    {
        var codes = await db.StatusCodes.AsNoTracking().Where(s => s.Entity == domain).ToListAsync(ct);
        var ids = codes.Select(c => c.StatusCodeId).ToList();
        var overrides = tenant.TenantId is null
            ? new Dictionary<int, string?>()
            : await db.StatusCodeOverrides.AsNoTracking().Where(o => ids.Contains(o.StatusCodeId))
                .ToDictionaryAsync(o => o.StatusCodeId, o => o.CustomLabelJson, ct);
        return codes.ToDictionary(c => c.StatusCodeId, c => new StatusInfo(c.InternalCode,
            MultilingualText.Resolve(MultilingualText.Merge(c.LabelJson, overrides.GetValueOrDefault(c.StatusCodeId)), tenant.Lang)));
    }

    /// <summary>Σ resuelto (PurchaseOrderShortageResolution.Quantity) por línea de las órdenes indicadas.</summary>
    public static async Task<Dictionary<int, decimal>> ResolvedByLineAsync(TeikemDbContext db, IReadOnlyCollection<int> poIds, CancellationToken ct)
    {
        var ids = poIds.ToList();
        var rows = await db.Set<PurchaseOrderShortageResolution>().AsNoTracking().Where(r => ids.Contains(r.PurchaseOrderId))
            .Select(r => new { r.PurchaseOrderLineId, r.Quantity }).ToListAsync(ct);
        return rows.GroupBy(r => r.PurchaseOrderLineId).ToDictionary(g => g.Key, g => g.Sum(r => r.Quantity));
    }

    /// <summary>
    /// Banderas de recibos por orden: recibo activo abierto (EXPECTED, RECEIVING, DISCREPANCY) y recibo activo confirmado
    /// (RECEIVED, RECEIVED_VARIANCE o PUTAWAY).
    /// </summary>
    public static async Task<Dictionary<int, ReceiptFlags>> ReceiptFlagsAsync(TeikemDbContext db, IReadOnlyCollection<int> poIds, CancellationToken ct)
    {
        var ids = poIds.ToList();
        var openIds = await db.StatusIdsAsync(StatusDomains.ReceiptStatus, ReceiptStatuses.OpenCodes, ct);
        var confirmedIds = await db.StatusIdsAsync(StatusDomains.ReceiptStatus, ReceiptStatuses.ConfirmedCodes.Append(ReceiptStatuses.Putaway), ct);
        var rows = await (from r in db.Set<ReceiptHeader>().AsNoTracking()
                          join a in db.Set<Asn>().AsNoTracking() on r.AsnId equals (int?)a.AsnId
                          where r.IsActive && a.PurchaseOrderId != null && ids.Contains(a.PurchaseOrderId.Value)
                          select new { PurchaseOrderId = a.PurchaseOrderId!.Value, r.StatusCodeId }).ToListAsync(ct);
        return rows.GroupBy(r => r.PurchaseOrderId).ToDictionary(g => g.Key, g => new ReceiptFlags(
            g.Any(r => openIds.Contains(r.StatusCodeId)),
            g.Any(r => confirmedIds.Contains(r.StatusCodeId))));
    }

    public static async Task<bool> HasOpenReceiptAsync(TeikemDbContext db, int poId, CancellationToken ct)
        => (await ReceiptFlagsAsync(db, new[] { poId }, ct)).GetValueOrDefault(poId).HasOpen;

    public static async Task<bool> HasConfirmedReceiptAsync(TeikemDbContext db, int poId, CancellationToken ct)
        => (await ReceiptFlagsAsync(db, new[] { poId }, ct)).GetValueOrDefault(poId).HasConfirmed;

    /// <summary>¿La orden ya tiene recepciones? (recibo confirmado o alguna línea con cantidad recibida).</summary>
    public static async Task<bool> HasReceiptsAsync(TeikemDbContext db, int poId, CancellationToken ct)
        => await HasConfirmedReceiptAsync(db, poId, ct)
           || await db.Set<PurchaseOrderLine>().AsNoTracking().AnyAsync(l => l.PurchaseOrderId == poId && l.QtyReceived > 0m, ct);

    // ---------------------------------------------------------------- escritura

    /// <summary>
    /// Transición escalonada de la orden hasta el estatus destino (SENT → PARTIAL → RECEIVED si PARTIAL está habilitado
    /// para el tenant), con historial por paso; el comentario va en el último paso. Sin cambio si ya está en el destino.
    /// </summary>
    public static async Task AdvanceAsync(TeikemDbContext db, StatusService statuses, PurchaseOrder po, string targetCode, string? comment, CancellationToken ct)
    {
        var current = await StatusCodeOfAsync(db, po.StatusCodeId, ct);
        var partialId = await db.StatusIdAsync(StatusDomains.PurchaseOrderStatus, PurchaseOrderStatuses.Partial, ct);
        var partialEnabled = !await db.StatusCodeOverrides.AsNoTracking().AnyAsync(o => o.StatusCodeId == partialId && !o.IsEnabled, ct);
        var path = PurchaseOrderRules.Path(current, targetCode, partialEnabled);
        for (var i = 0; i < path.Count; i++)
        {
            var to = await statuses.TransitionAsync(StatusDomains.PurchaseOrderStatus, EntityTypes.PurchaseOrder, po.PurchaseOrderId,
                po.StatusCodeId, path[i], i == path.Count - 1 ? comment : null, ct);
            po.StatusCodeId = to.StatusCodeId;
        }
    }

    /// <summary>
    /// Control de concurrencia optimista sobre el encabezado ya bloqueado: si el cliente mandó rowVersion y difiere del
    /// actual → 409. (Comparar bajo el bloqueo cubre también los cambios que solo tocan líneas.)
    /// </summary>
    public static void EnsureRowVersion(byte[]? current, string? rowVersionBase64, string conflictMessage)
    {
        if (string.IsNullOrWhiteSpace(rowVersionBase64) || current is null || current.Length == 0) return;
        byte[] sent;
        try { sent = Convert.FromBase64String(rowVersionBase64.Trim()); }
        catch (FormatException) { throw new ValidationException("rowVersion", "rowVersion inválido: se espera el valor base64 devuelto por la ficha."); }
        if (sent.Length > 0 && !sent.AsSpan().SequenceEqual(current)) throw new ConflictException(conflictMessage);
    }

    /// <summary>Comentario de una transición: vacío → null; más de 500 → 400 (EntityStatusHistory.Comment).</summary>
    public static string? Comment(string? comment)
    {
        if (string.IsNullOrWhiteSpace(comment)) return null;
        var c = comment.Trim();
        if (c.Length > StatusService.CommentMaxLength) throw new ValidationException("comment", StatusService.CommentTooLongMessage);
        return c;
    }
}
