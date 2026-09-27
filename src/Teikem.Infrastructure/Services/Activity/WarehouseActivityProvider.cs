using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Clients;
using Teikem.Domain.Common;
using Teikem.Domain.Constants;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 7A (P1) — proveedor de eventos de la pestaña Almacén del feed "Actividad reciente" (inventory.view). Solo lectura
/// (AsNoTracking) bajo el filtro global de tenant, desde fromUtc. Fuentes (maestro, 'Catálogo inicial — Almacén'):
/// (a) EntityStatusHistory de RECEIPT, ASN, CYCLE_COUNT, PICK_BATCH, PURCHASE_ORDER, CROSSDOCK_PLAN (solo con el módulo
///     CROSSDOCK) y WAREHOUSE, con su documento para la referencia (número) y el detalle (almacén, líneas, diferencia, cliente);
/// (b) WarehouseTask → DONE/CANCELLED: las tareas escriben historial (WarehouseTaskService, handlers y efectos van por
///     StatusService.TransitionAsync), así que se leen del mismo historial con el tipo de la tarea;
/// (c) ledger: ADJUSTMENT agrupado por operación (Ref + instante + usuario; los manuales además por producto) y TRANSFER
///     manual (sin Ref: los de putaway, reabasto y conteo ya son PUTAWAY_DONE, REPLENISH_DONE y COUNT_RECONCILED);
/// (d) baja de producto: AuditLog PRODUCT con acción DELETE (el interceptor clasifica IsActive 1 → 0 como DELETE); la
///     categoría se audita como PRODUCT_CATEGORY y el lote nunca genera DELETE, así que cada fila es la baja de un producto
///     (aunque se haya reactivado después); faltante resuelto: cada fila de PurchaseOrderShortageResolution (la resolución es un INSERT
///     auditado bajo PURCHASE_ORDER, no un UPDATE de la línea).
/// La etiqueta y la bandera de obligatorio salen del catálogo ActivityEventType (ILookupCache); un evento opcional apagado
/// por defecto (BIN_MOVED) o excluido por 'solo obligatorios' ni se consulta.
/// </summary>
public sealed class WarehouseActivityProvider(TeikemDbContext db, ILookupCache lookups) : IActivityEventProvider
{
    public string Module => BusinessModules.Warehouse;
    public string RequiredPermission => PermissionCatalog.InventoryView;

    private sealed record CatalogEntry(string Label, bool Mandatory, bool DefaultOn);

    /// <summary>Transiciones que producen evento: tipo de entidad → (dominio de estatus, estatus destino).</summary>
    private static readonly (string EntityType, string Domain, string[] ToStatuses)[] StatusSources =
    {
        (EntityTypes.Receipt, StatusDomains.ReceiptStatus, new[] { ReceiptStatuses.Received, ReceiptStatuses.Putaway }),
        (EntityTypes.Asn, StatusDomains.AsnStatus, new[] { AsnStatuses.Cancelled }),
        (EntityTypes.CycleCount, StatusDomains.CycleCountStatus, new[] { CycleCountStatuses.Counted, CycleCountStatuses.Reconciled }),
        (EntityTypes.PickBatch, StatusDomains.PickBatchStatus, new[] { PickBatchStatuses.Collected, PickBatchStatuses.Packed, PickBatchStatuses.Cancelled }),
        (EntityTypes.PurchaseOrder, StatusDomains.PurchaseOrderStatus, new[] { PurchaseOrderStatuses.Sent, PurchaseOrderStatuses.Received, PurchaseOrderStatuses.Cancelled }),
        (EntityTypes.CrossDockPlan, StatusDomains.CrossDockStatus, new[] { CrossDockStatuses.Completed }),
        (EntityTypes.Warehouse, StatusDomains.WarehouseStatus, new[] { WarehouseStatuses.Inactive }),
        (EntityTypes.WarehouseTask, StatusDomains.WarehouseTaskStatus, new[] { WarehouseTaskStatuses.Done, WarehouseTaskStatuses.Cancelled }),
    };

    public async Task<IReadOnlyList<ActivityEventDto>> ReadAsync(ActivityScope scope, DateTime fromUtc, bool onlyMandatory, CancellationToken ct)
    {
        var catalog = await CatalogAsync(scope.Lang, ct);
        bool Wanted(string code)
        {
            var e = catalog.GetValueOrDefault(code);
            return e is not null && ActivityRules.IsShown(e.Mandatory, e.DefaultOn, onlyMandatory);
        }
        var ctx = new ReadContext(scope, fromUtc, catalog, Wanted);

        await ReadStatusHistoryAsync(ctx, ct);
        await ReadAdjustmentsAsync(ctx, ct);
        await ReadTransfersAsync(ctx, ct);
        await ReadDeactivatedProductsAsync(ctx, ct);
        await ReadShortageResolutionsAsync(ctx, ct);
        return ctx.Events;
    }

    private sealed class ReadContext(ActivityScope scope, DateTime fromUtc, IReadOnlyDictionary<string, CatalogEntry> catalog, Func<string, bool> wanted)
    {
        public ActivityScope Scope { get; } = scope;
        public DateTime FromUtc { get; } = fromUtc;
        public string Lang => Scope.Lang;
        public Func<string, bool> Wanted { get; } = wanted;
        public List<ActivityEventDto> Events { get; } = new();

        public void Add(DateTime atUtc, string code, string entityType, int entityId, Guid? publicId, string reference, string? detail, int? userId)
        {
            var e = catalog[code];
            Events.Add(new ActivityEventDto(DateTime.SpecifyKind(atUtc, DateTimeKind.Utc), code, BusinessModules.Warehouse, e.Mandatory, e.Label,
                entityType, entityId, publicId, reference, detail, userId, null));
        }
    }

    /// <summary>Eventos activos de Almacén del catálogo (TenantId NULL, sembrados), con su etiqueta en el idioma del usuario.</summary>
    private async Task<IReadOnlyDictionary<string, CatalogEntry>> CatalogAsync(string lang, CancellationToken ct)
    {
        var map = new Dictionary<string, CatalogEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var l in await lookups.GetDomainAsync(ActivityRules.CatalogDomain, ct))
        {
            if (!l.IsActive || l.TenantId is not null) continue;
            var meta = ActivityRules.ParseMeta(l.ExtraJson);
            if (meta.Module is not null && !string.Equals(meta.Module, BusinessModules.Warehouse, StringComparison.OrdinalIgnoreCase)) continue;
            map[l.InternalCode] = new CatalogEntry(MultilingualText.Resolve(l.LabelJson, lang), meta.Mandatory, meta.DefaultOn);
        }
        return map;
    }

    // ================================================================ (a) + (b) historial de estatus

    private async Task ReadStatusHistoryAsync(ReadContext ctx, CancellationToken ct)
    {
        var crossDockOn = ctx.Scope.EnabledModules.Contains(ModuleKeys.CrossDock);
        var domains = StatusSources.Select(s => s.Domain).Distinct().ToList();
        var statusRows = await db.StatusCodes.AsNoTracking().Where(s => domains.Contains(s.Entity))
            .Select(s => new { s.StatusCodeId, s.Entity, s.InternalCode }).ToListAsync(ct);

        var typeIds = new Dictionary<int, string>();        // EntityTypeLookupId → código
        var statusCodes = new Dictionary<int, string>();    // StatusCodeId → código (del dominio de su entidad)
        foreach (var (entityType, domain, toStatuses) in StatusSources)
        {
            if (entityType == EntityTypes.CrossDockPlan && !crossDockOn) continue;
            // Solo se consulta la fuente si alguno de sus eventos se muestra.
            var codes = toStatuses.Select(s => ActivityRules.StatusEventCode(entityType, s, WarehouseTaskTypes.Putaway))
                .Concat(entityType == EntityTypes.WarehouseTask ? new[] { ActivityEvents.ReplenishDone } : Array.Empty<string>())
                .Where(c => c is not null).Select(c => c!);
            if (!codes.Any(ctx.Wanted)) continue;
            if (await lookups.TryGetIdAsync(LookupDomains.EntityType, entityType, ct) is not int typeId) continue;
            typeIds[typeId] = entityType;
            foreach (var s in statusRows.Where(r => r.Entity == domain && toStatuses.Contains(r.InternalCode, StringComparer.OrdinalIgnoreCase)))
                statusCodes[s.StatusCodeId] = s.InternalCode;
        }
        if (typeIds.Count == 0 || statusCodes.Count == 0) return;

        var typeIdList = typeIds.Keys.ToList();
        var statusIdList = statusCodes.Keys.ToList();
        var from = ctx.FromUtc;
        var rows = await db.EntityStatusHistories.AsNoTracking()
            .Where(h => h.ChangedAtUtc >= from && typeIdList.Contains(h.EntityTypeLookupId) && statusIdList.Contains(h.ToStatusCodeId))
            .Select(h => new { h.EntityTypeLookupId, h.EntityId, h.ToStatusCodeId, h.ChangedAtUtc, h.ChangedBy })
            .ToListAsync(ct);
        if (rows.Count == 0) return;

        var hits = rows.Select(r => new StatusHit(typeIds[r.EntityTypeLookupId], r.EntityId, statusCodes[r.ToStatusCodeId], r.ChangedAtUtc, r.ChangedBy)).ToList();
        IReadOnlyList<StatusHit> ByType(string t) => hits.Where(h => h.EntityType == t).ToList();

        var warehouses = new WarehouseNames(db);
        await EmitReceiptsAsync(ctx, ByType(EntityTypes.Receipt), warehouses, ct);
        await EmitAsnsAsync(ctx, ByType(EntityTypes.Asn), warehouses, ct);
        await EmitCycleCountsAsync(ctx, ByType(EntityTypes.CycleCount), warehouses, ct);
        await EmitPickBatchesAsync(ctx, ByType(EntityTypes.PickBatch), warehouses, ct);
        await EmitPurchaseOrdersAsync(ctx, ByType(EntityTypes.PurchaseOrder), warehouses, ct);
        await EmitCrossDockPlansAsync(ctx, ByType(EntityTypes.CrossDockPlan), warehouses, ct);
        await EmitWarehousesAsync(ctx, ByType(EntityTypes.Warehouse), ct);
        await EmitTasksAsync(ctx, ByType(EntityTypes.WarehouseTask), warehouses, ct);
    }

    private sealed record StatusHit(string EntityType, int EntityId, string ToStatus, DateTime AtUtc, int? UserId);

    /// <summary>Emite los hits cuyo evento se muestra y cuyo documento sigue alcanzable bajo el filtro de tenant.</summary>
    private static void Emit<TDoc>(ReadContext ctx, IEnumerable<StatusHit> hits, IReadOnlyDictionary<int, TDoc> docs,
        Func<TDoc, (Guid? PublicId, string Reference, string? Detail)> describe, Func<StatusHit, string?>? taskType = null)
    {
        foreach (var h in hits)
        {
            var code = ActivityRules.StatusEventCode(h.EntityType, h.ToStatus, taskType?.Invoke(h));
            if (code is null || !ctx.Wanted(code) || !docs.TryGetValue(h.EntityId, out var doc)) continue;
            var (publicId, reference, detail) = describe(doc);
            ctx.Add(h.AtUtc, code, h.EntityType, h.EntityId, publicId, reference, detail, h.UserId);
        }
    }

    private async Task EmitReceiptsAsync(ReadContext ctx, IReadOnlyList<StatusHit> hits, WarehouseNames warehouses, CancellationToken ct)
    {
        if (hits.Count == 0) return;
        var ids = hits.Select(h => h.EntityId).Distinct().ToList();
        var docs = await db.Set<ReceiptHeader>().AsNoTracking().Where(r => ids.Contains(r.ReceiptHeaderId))
            .Select(r => new { r.ReceiptHeaderId, r.PublicId, r.Number, r.WarehouseId, r.AsnId }).ToListAsync(ct);
        // ReceiptLine no lleva TenantId: se alcanza por su recibo filtrado.
        var lines = await (from l in db.Set<ReceiptLine>().AsNoTracking()
                           join r in db.Set<ReceiptHeader>().AsNoTracking() on l.ReceiptHeaderId equals r.ReceiptHeaderId
                           where ids.Contains(r.ReceiptHeaderId)
                           group l by l.ReceiptHeaderId into g
                           select new { Id = g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Id, x => x.Count, ct);
        var asnIds = docs.Where(d => d.AsnId.HasValue).Select(d => d.AsnId!.Value).Distinct().ToList();
        var asnClients = asnIds.Count == 0
            ? new Dictionary<int, int?>()
            : await db.Set<Asn>().AsNoTracking().Where(a => asnIds.Contains(a.AsnId)).ToDictionaryAsync(a => a.AsnId, a => a.ClientId, ct);
        var clients = await ClientNamesAsync(asnClients.Values, ct);
        await warehouses.LoadAsync(docs.Select(d => d.WarehouseId), ct);

        Emit(ctx, hits, docs.ToDictionary(d => d.ReceiptHeaderId), d =>
        {
            var clientId = d.AsnId is int a ? asnClients.GetValueOrDefault(a) : null;
            return (d.PublicId, d.Number, ActivityRules.Detail(warehouses.Code(d.WarehouseId),
                clientId is int c ? clients.GetValueOrDefault(c) : null, ActivityRules.Lines(lines.GetValueOrDefault(d.ReceiptHeaderId), ctx.Lang)));
        });
    }

    private async Task EmitAsnsAsync(ReadContext ctx, IReadOnlyList<StatusHit> hits, WarehouseNames warehouses, CancellationToken ct)
    {
        if (hits.Count == 0) return;
        var ids = hits.Select(h => h.EntityId).Distinct().ToList();
        var docs = await db.Set<Asn>().AsNoTracking().Where(a => ids.Contains(a.AsnId))
            .Select(a => new { a.AsnId, a.Reference, a.WarehouseId, a.ClientId }).ToListAsync(ct);
        var lines = await (from l in db.Set<AsnLine>().AsNoTracking()
                           join a in db.Set<Asn>().AsNoTracking() on l.AsnId equals a.AsnId
                           where ids.Contains(a.AsnId)
                           group l by l.AsnId into g
                           select new { Id = g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Id, x => x.Count, ct);
        var clients = await ClientNamesAsync(docs.Select(d => d.ClientId), ct);
        await warehouses.LoadAsync(docs.Select(d => d.WarehouseId), ct);

        Emit(ctx, hits, docs.ToDictionary(d => d.AsnId), d =>
            ((Guid?)null, string.IsNullOrWhiteSpace(d.Reference) ? $"ASN #{d.AsnId}" : d.Reference!,
             ActivityRules.Detail(warehouses.Code(d.WarehouseId), d.ClientId is int c ? clients.GetValueOrDefault(c) : null,
                 ActivityRules.Lines(lines.GetValueOrDefault(d.AsnId), ctx.Lang))));
    }

    private async Task EmitCycleCountsAsync(ReadContext ctx, IReadOnlyList<StatusHit> hits, WarehouseNames warehouses, CancellationToken ct)
    {
        if (hits.Count == 0) return;
        var ids = hits.Select(h => h.EntityId).Distinct().ToList();
        var docs = await db.Set<CycleCount>().AsNoTracking().Where(c => ids.Contains(c.CycleCountId))
            .Select(c => new { c.CycleCountId, c.Number, c.WarehouseId }).ToListAsync(ct);
        // CycleCountLine se alcanza por su conteo filtrado. La diferencia se calcula en código (VarianceQty es computada).
        var lines = await (from l in db.Set<CycleCountLine>().AsNoTracking()
                           join c in db.Set<CycleCount>().AsNoTracking() on l.CycleCountId equals c.CycleCountId
                           where ids.Contains(c.CycleCountId)
                           select new { l.CycleCountId, l.CountedQty, l.SystemQty, l.ReconciledSystemQty }).ToListAsync(ct);
        var byCount = lines.GroupBy(l => l.CycleCountId).ToDictionary(g => g.Key, g => (
            Count: g.Count(),
            Net: g.Where(l => l.CountedQty.HasValue).Sum(l => CycleCountRules.Adjustment(l.CountedQty!.Value, l.ReconciledSystemQty ?? l.SystemQty))));
        await warehouses.LoadAsync(docs.Select(d => d.WarehouseId), ct);

        var map = docs.ToDictionary(d => d.CycleCountId);
        foreach (var group in hits.GroupBy(h => h.ToStatus.ToUpperInvariant() == CycleCountStatuses.Reconciled))
        {
            var reconciled = group.Key;
            Emit(ctx, group, map, d =>
            {
                var info = byCount.GetValueOrDefault(d.CycleCountId);
                return ((Guid?)null, d.Number, ActivityRules.Detail(warehouses.Code(d.WarehouseId), ActivityRules.Lines(info.Count, ctx.Lang),
                    reconciled ? ActivityRules.NetVariance(info.Net, ctx.Lang) : null));
            });
        }
    }

    private async Task EmitPickBatchesAsync(ReadContext ctx, IReadOnlyList<StatusHit> hits, WarehouseNames warehouses, CancellationToken ct)
    {
        if (hits.Count == 0) return;
        var ids = hits.Select(h => h.EntityId).Distinct().ToList();
        var docs = await db.Set<PickBatch>().AsNoTracking().Where(b => ids.Contains(b.PickBatchId))
            .Select(b => new { b.PickBatchId, b.PublicId, b.Number, b.WarehouseId, b.ClientInvoiceNumber }).ToListAsync(ct);
        var lines = await (from l in db.Set<PickBatchLine>().AsNoTracking()
                           join b in db.Set<PickBatch>().AsNoTracking() on l.PickBatchId equals b.PickBatchId
                           where ids.Contains(b.PickBatchId)
                           group l by l.PickBatchId into g
                           select new { Id = g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Id, x => x.Count, ct);
        await warehouses.LoadAsync(docs.Select(d => d.WarehouseId), ct);

        Emit(ctx, hits, docs.ToDictionary(d => d.PickBatchId), d =>
            ((Guid?)d.PublicId, d.Number, ActivityRules.Detail(warehouses.Code(d.WarehouseId), d.ClientInvoiceNumber,
                ActivityRules.Lines(lines.GetValueOrDefault(d.PickBatchId), ctx.Lang))));
    }

    private async Task EmitPurchaseOrdersAsync(ReadContext ctx, IReadOnlyList<StatusHit> hits, WarehouseNames warehouses, CancellationToken ct)
    {
        if (hits.Count == 0) return;
        var ids = hits.Select(h => h.EntityId).Distinct().ToList();
        var docs = await db.Set<PurchaseOrder>().AsNoTracking().Where(p => ids.Contains(p.PurchaseOrderId))
            .Select(p => new { p.PurchaseOrderId, p.PublicId, p.Number, p.WarehouseId, p.SupplierId }).ToListAsync(ct);
        var lines = await (from l in db.Set<PurchaseOrderLine>().AsNoTracking()
                           join p in db.Set<PurchaseOrder>().AsNoTracking() on l.PurchaseOrderId equals p.PurchaseOrderId
                           where ids.Contains(p.PurchaseOrderId)
                           group l by l.PurchaseOrderId into g
                           select new { Id = g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Id, x => x.Count, ct);
        var suppliers = await SupplierNamesAsync(docs.Select(d => d.SupplierId), ct);
        await warehouses.LoadAsync(docs.Select(d => d.WarehouseId), ct);

        Emit(ctx, hits, docs.ToDictionary(d => d.PurchaseOrderId), d =>
            ((Guid?)d.PublicId, d.Number, ActivityRules.Detail(suppliers.GetValueOrDefault(d.SupplierId), warehouses.Code(d.WarehouseId),
                ActivityRules.Lines(lines.GetValueOrDefault(d.PurchaseOrderId), ctx.Lang))));
    }

    private async Task EmitCrossDockPlansAsync(ReadContext ctx, IReadOnlyList<StatusHit> hits, WarehouseNames warehouses, CancellationToken ct)
    {
        if (hits.Count == 0) return;
        var ids = hits.Select(h => h.EntityId).Distinct().ToList();
        var docs = await db.Set<CrossDockPlan>().AsNoTracking().Where(p => ids.Contains(p.CrossDockPlanId))
            .Select(p => new { p.CrossDockPlanId, p.Number, p.WarehouseId }).ToListAsync(ct);
        await warehouses.LoadAsync(docs.Select(d => d.WarehouseId), ct);

        Emit(ctx, hits, docs.ToDictionary(d => d.CrossDockPlanId), d =>
            ((Guid?)null, d.Number, ActivityRules.Detail(warehouses.Code(d.WarehouseId))));
    }

    private async Task EmitWarehousesAsync(ReadContext ctx, IReadOnlyList<StatusHit> hits, CancellationToken ct)
    {
        if (hits.Count == 0) return;
        var ids = hits.Select(h => h.EntityId).Distinct().ToList();
        var docs = await db.Set<Warehouse>().AsNoTracking().Where(w => ids.Contains(w.WarehouseId))
            .Select(w => new { w.WarehouseId, w.PublicId, w.Code, w.Name }).ToListAsync(ct);

        Emit(ctx, hits, docs.ToDictionary(d => d.WarehouseId), d => ((Guid?)d.PublicId, d.Code, ActivityRules.Detail(d.Name)));
    }

    private async Task EmitTasksAsync(ReadContext ctx, IReadOnlyList<StatusHit> hits, WarehouseNames warehouses, CancellationToken ct)
    {
        if (hits.Count == 0) return;
        var ids = hits.Select(h => h.EntityId).Distinct().ToList();
        var docs = await db.Set<WarehouseTask>().AsNoTracking().Where(t => ids.Contains(t.WarehouseTaskId))
            .Select(t => new { t.WarehouseTaskId, t.WarehouseId, t.TaskTypeLookupId, t.ProductId, t.Quantity }).ToListAsync(ct);
        var taskTypes = new Dictionary<int, string>();
        foreach (var typeId in docs.Select(d => d.TaskTypeLookupId).Distinct())
            taskTypes[typeId] = (await lookups.GetAsync(typeId, ct))?.InternalCode ?? string.Empty;
        var products = await ProductsAsync(docs.Where(d => d.ProductId.HasValue).Select(d => d.ProductId!.Value), ct);
        await warehouses.LoadAsync(docs.Select(d => d.WarehouseId), ct);
        var map = docs.ToDictionary(d => d.WarehouseTaskId);

        Emit(ctx, hits, map, d =>
        {
            var sku = d.ProductId is int pid ? products.GetValueOrDefault(pid)?.Sku : null;
            return ((Guid?)null, $"#{d.WarehouseTaskId}", ActivityRules.Detail(taskTypes.GetValueOrDefault(d.TaskTypeLookupId),
                warehouses.Code(d.WarehouseId), sku, d.Quantity is decimal q ? ActivityRules.Qty(q) : null));
        }, h => map.TryGetValue(h.EntityId, out var t) ? taskTypes.GetValueOrDefault(t.TaskTypeLookupId) : null);
    }

    // ================================================================ (c) ledger

    private async Task ReadAdjustmentsAsync(ReadContext ctx, CancellationToken ct)
    {
        if (!ctx.Wanted(ActivityEvents.ReceiptVariance) && !ctx.Wanted(ActivityEvents.CountVariance) && !ctx.Wanted(ActivityEvents.InventoryAdjusted)) return;
        if (await lookups.TryGetIdAsync(LookupDomains.InventoryTxnType, InventoryTxnTypes.Adjustment, ct) is not int adjustmentId) return;

        var from = ctx.FromUtc;
        var rows = await db.InventoryTransactions.AsNoTracking()
            .Where(t => t.CreatedAtUtc >= from && t.TxnTypeLookupId == adjustmentId && t.ReasonLookupId != null)
            .Select(t => new { t.ProductId, t.FromWarehouseId, t.ToWarehouseId, t.Quantity, t.RefEntityLookupId, t.RefId, ReasonId = t.ReasonLookupId!.Value, t.CreatedAtUtc, t.CreatedBy })
            .ToListAsync(ct);
        if (rows.Count == 0) return;

        var reasons = new Dictionary<int, (string Code, string Label)>();
        foreach (var id in rows.Select(r => r.ReasonId).Distinct())
        {
            var l = await lookups.GetAsync(id, ct);
            reasons[id] = (l?.InternalCode ?? string.Empty, l is null ? string.Empty : MultilingualText.Resolve(l.LabelJson, ctx.Lang));
        }

        var coded = rows
            .Select(r => new { Row = r, Code = ActivityRules.AdjustmentEventCode(reasons[r.ReasonId].Code, r.RefEntityLookupId.HasValue) })
            .Where(x => x.Code is not null && ctx.Wanted(x.Code))
            .ToList();
        if (coded.Count == 0) return;

        // Una operación = mismo evento, documento de origen, instante del asiento y usuario (el ledger fija un solo instante
        // por tanda). Los manuales (sin documento) se separan además por producto: la referencia es el producto.
        var groups = coded.GroupBy(x => new
        {
            x.Code,
            x.Row.RefEntityLookupId,
            x.Row.RefId,
            x.Row.CreatedAtUtc,
            x.Row.CreatedBy,
            ProductId = x.Code == ActivityEvents.InventoryAdjusted ? x.Row.ProductId : 0,
            ReasonId = x.Code == ActivityEvents.InventoryAdjusted ? x.Row.ReasonId : 0,
        }).ToList();

        var receiptIds = groups.Where(g => g.Key.Code == ActivityEvents.ReceiptVariance && g.Key.RefId.HasValue).Select(g => g.Key.RefId!.Value).Distinct().ToList();
        var countIds = groups.Where(g => g.Key.Code == ActivityEvents.CountVariance && g.Key.RefId.HasValue).Select(g => g.Key.RefId!.Value).Distinct().ToList();
        var receipts = receiptIds.Count == 0
            ? new Dictionary<int, (Guid PublicId, string Number)>()
            : (await db.Set<ReceiptHeader>().AsNoTracking().Where(r => receiptIds.Contains(r.ReceiptHeaderId))
                .Select(r => new { r.ReceiptHeaderId, r.PublicId, r.Number }).ToListAsync(ct))
                .ToDictionary(r => r.ReceiptHeaderId, r => (r.PublicId, r.Number));
        var counts = countIds.Count == 0
            ? new Dictionary<int, string>()
            : await db.Set<CycleCount>().AsNoTracking().Where(c => countIds.Contains(c.CycleCountId))
                .ToDictionaryAsync(c => c.CycleCountId, c => c.Number, ct);
        var products = await ProductsAsync(groups.SelectMany(g => g.Select(x => x.Row.ProductId)), ct);
        var warehouses = new WarehouseNames(db);
        await warehouses.LoadAsync(rows.SelectMany(r => new[] { r.FromWarehouseId, r.ToWarehouseId }).Where(w => w.HasValue).Select(w => w!.Value), ct);

        foreach (var g in groups)
        {
            var code = g.Key.Code!;
            var net = g.Sum(x => x.Row.Quantity);
            var productCount = g.Select(x => x.Row.ProductId).Distinct().Count();
            var whId = g.Select(x => x.Row.ToWarehouseId ?? x.Row.FromWarehouseId).FirstOrDefault(w => w.HasValue);
            var whCode = whId is int w ? warehouses.Code(w) : null;
            if (code == ActivityEvents.ReceiptVariance)
            {
                if (g.Key.RefId is not int rid || !receipts.TryGetValue(rid, out var r)) continue;
                ctx.Add(g.Key.CreatedAtUtc, code, EntityTypes.Receipt, rid, r.PublicId, r.Number,
                    ActivityRules.Detail(whCode, ProductsText(productCount, ctx.Lang), ActivityRules.NetVariance(net, ctx.Lang)), g.Key.CreatedBy);
            }
            else if (code == ActivityEvents.CountVariance)
            {
                if (g.Key.RefId is not int cid || !counts.TryGetValue(cid, out var number)) continue;
                ctx.Add(g.Key.CreatedAtUtc, code, EntityTypes.CycleCount, cid, null, number,
                    ActivityRules.Detail(whCode, ProductsText(productCount, ctx.Lang), ActivityRules.NetVariance(net, ctx.Lang)), g.Key.CreatedBy);
            }
            else
            {
                if (!products.TryGetValue(g.Key.ProductId, out var p)) continue;
                ctx.Add(g.Key.CreatedAtUtc, code, EntityTypes.Product, p.ProductId, p.PublicId, p.Sku,
                    ActivityRules.Detail(reasons[g.Key.ReasonId].Label, whCode, ActivityRules.Qty(net, signed: true)), g.Key.CreatedBy);
            }
        }
    }

    private async Task ReadTransfersAsync(ReadContext ctx, CancellationToken ct)
    {
        if (!ctx.Wanted(ActivityEvents.InventoryTransferred) && !ctx.Wanted(ActivityEvents.BinMoved)) return;
        if (await lookups.TryGetIdAsync(LookupDomains.InventoryTxnType, InventoryTxnTypes.Transfer, ct) is not int transferId) return;

        // Solo transferencias manuales (sin documento de origen): las de putaway, reabasto y conteo ya tienen su evento.
        var from = ctx.FromUtc;
        var rows = await db.InventoryTransactions.AsNoTracking()
            .Where(t => t.CreatedAtUtc >= from && t.TxnTypeLookupId == transferId && t.RefEntityLookupId == null)
            .Select(t => new { t.ProductId, t.FromWarehouseId, t.FromBinId, t.ToWarehouseId, t.ToBinId, t.Quantity, t.CreatedAtUtc, t.CreatedBy })
            .ToListAsync(ct);
        var coded = rows.Select(r => new { Row = r, Code = ActivityRules.TransferEventCode(r.FromWarehouseId, r.ToWarehouseId) })
            .Where(x => ctx.Wanted(x.Code)).ToList();
        if (coded.Count == 0) return;

        var groups = coded.GroupBy(x => new { x.Code, x.Row.ProductId, x.Row.FromWarehouseId, x.Row.FromBinId, x.Row.ToWarehouseId, x.Row.ToBinId, x.Row.CreatedAtUtc, x.Row.CreatedBy }).ToList();
        var products = await ProductsAsync(groups.Select(g => g.Key.ProductId), ct);
        var warehouses = new WarehouseNames(db);
        await warehouses.LoadAsync(groups.SelectMany(g => new[] { g.Key.FromWarehouseId, g.Key.ToWarehouseId }).Where(w => w.HasValue).Select(w => w!.Value), ct);
        var binIds = groups.SelectMany(g => new[] { g.Key.FromBinId, g.Key.ToBinId }).Where(b => b.HasValue).Select(b => b!.Value).Distinct().ToList();
        // WarehouseBin no lleva TenantId: se alcanza por su almacén filtrado.
        var bins = binIds.Count == 0
            ? new Dictionary<int, string>()
            : await (from b in db.Set<WarehouseBin>().AsNoTracking()
                     join w in db.Set<Warehouse>().AsNoTracking() on b.WarehouseId equals w.WarehouseId
                     where binIds.Contains(b.WarehouseBinId)
                     select new { b.WarehouseBinId, b.Code }).ToDictionaryAsync(b => b.WarehouseBinId, b => b.Code, ct);

        foreach (var g in groups)
        {
            if (!products.TryGetValue(g.Key.ProductId, out var p)) continue;
            var qty = ActivityRules.Qty(g.Sum(x => Math.Abs(x.Row.Quantity)));
            string? route = g.Key.Code == ActivityEvents.InventoryTransferred
                ? $"{Code(warehouses, g.Key.FromWarehouseId)} → {Code(warehouses, g.Key.ToWarehouseId)}"
                : $"{BinCode(bins, g.Key.FromBinId)} → {BinCode(bins, g.Key.ToBinId)}";
            ctx.Add(g.Key.CreatedAtUtc, g.Key.Code, EntityTypes.Product, p.ProductId, p.PublicId, p.Sku,
                ActivityRules.Detail(g.Key.Code == ActivityEvents.BinMoved ? Code(warehouses, g.Key.ToWarehouseId) : null, route, qty), g.Key.CreatedBy);
        }

        static string Code(WarehouseNames w, int? id) => id is int x ? w.Code(x) ?? "?" : "?";
        static string BinCode(IReadOnlyDictionary<int, string> b, int? id) => id is int x ? b.GetValueOrDefault(x) ?? "?" : "?";
    }

    // ================================================================ (d) auditoría y faltantes

    private async Task ReadDeactivatedProductsAsync(ReadContext ctx, CancellationToken ct)
    {
        if (!ctx.Wanted(ActivityEvents.ProductDeactivated)) return;
        if (await lookups.TryGetIdAsync(LookupDomains.EntityType, EntityTypes.Product, ct) is not int productTypeId) return;
        if (await lookups.TryGetIdAsync(LookupDomains.AuditAction, AuditActions.Delete, ct) is not int deleteId) return;

        // Bajo PRODUCT solo se auditan el producto y sus lotes (la categoría usa PRODUCT_CATEGORY desde el Lote 7A) y un lote no
        // es ISoftDeletable (nunca genera DELETE): cada fila DELETE con IsActive es la baja de un producto. No se exige que siga
        // inactivo: una baja reactivada dentro de la ventana también ocurrió.
        var from = ctx.FromUtc;
        var rows = await db.AuditLogs.AsNoTracking()
            .Where(a => a.CreatedAtUtc >= from && a.EntityTypeLookupId == productTypeId && a.ActionLookupId == deleteId
                        && a.ChangesJson != null && a.ChangesJson.Contains("\"IsActive\""))
            .Select(a => new { a.EntityId, a.CreatedAtUtc, a.UserId }).ToListAsync(ct);
        if (rows.Count == 0) return;
        var ids = rows.Select(r => r.EntityId).Distinct().ToList();
        var products = await db.Set<Product>().AsNoTracking().Where(p => ids.Contains(p.ProductId))
            .Select(p => new { p.ProductId, p.PublicId, p.Sku, p.Name }).ToDictionaryAsync(p => p.ProductId, ct);

        foreach (var r in rows)
        {
            if (!products.TryGetValue(r.EntityId, out var p)) continue;
            ctx.Add(r.CreatedAtUtc, ActivityEvents.ProductDeactivated, EntityTypes.Product, p.ProductId, p.PublicId, p.Sku,
                ActivityRules.Detail(p.Name), r.UserId);
        }
    }

    private async Task ReadShortageResolutionsAsync(ReadContext ctx, CancellationToken ct)
    {
        if (!ctx.Wanted(ActivityEvents.PoShortageResolved)) return;
        var from = ctx.FromUtc;
        var rows = await db.Set<PurchaseOrderShortageResolution>().AsNoTracking().Where(r => r.CreatedAtUtc >= from)
            .Select(r => new { r.PurchaseOrderId, r.PurchaseOrderLineId, r.ActionLookupId, r.Quantity, r.CreatedAtUtc, r.CreatedBy }).ToListAsync(ct);
        if (rows.Count == 0) return;

        var poIds = rows.Select(r => r.PurchaseOrderId).Distinct().ToList();
        var pos = await db.Set<PurchaseOrder>().AsNoTracking().Where(p => poIds.Contains(p.PurchaseOrderId))
            .Select(p => new { p.PurchaseOrderId, p.PublicId, p.Number }).ToDictionaryAsync(p => p.PurchaseOrderId, ct);
        var lineIds = rows.Select(r => r.PurchaseOrderLineId).Distinct().ToList();
        // PurchaseOrderLine se alcanza por su orden filtrada.
        var lineProducts = await (from l in db.Set<PurchaseOrderLine>().AsNoTracking()
                                  join p in db.Set<PurchaseOrder>().AsNoTracking() on l.PurchaseOrderId equals p.PurchaseOrderId
                                  where lineIds.Contains(l.PurchaseOrderLineId)
                                  select new { l.PurchaseOrderLineId, l.ProductId }).ToDictionaryAsync(x => x.PurchaseOrderLineId, x => x.ProductId, ct);
        var products = await ProductsAsync(lineProducts.Values, ct);
        var actions = new Dictionary<int, string>();
        foreach (var id in rows.Select(r => r.ActionLookupId).Distinct())
            actions[id] = await lookups.GetAsync(id, ct) is { } l ? MultilingualText.Resolve(l.LabelJson, ctx.Lang) : string.Empty;

        foreach (var r in rows)
        {
            if (!pos.TryGetValue(r.PurchaseOrderId, out var po)) continue;
            var sku = lineProducts.TryGetValue(r.PurchaseOrderLineId, out var pid) ? products.GetValueOrDefault(pid)?.Sku : null;
            ctx.Add(r.CreatedAtUtc, ActivityEvents.PoShortageResolved, EntityTypes.PurchaseOrder, po.PurchaseOrderId, po.PublicId, po.Number,
                ActivityRules.Detail(actions.GetValueOrDefault(r.ActionLookupId), sku, ActivityRules.Qty(r.Quantity)), r.CreatedBy);
        }
    }

    // ================================================================ apoyo

    private static string ProductsText(int count, string? lang)
        => ActivityRules.IsEnglish(lang) ? (count == 1 ? "1 product" : $"{count} products") : (count == 1 ? "1 producto" : $"{count} productos");

    private sealed record ProductRef(int ProductId, Guid PublicId, string Sku);

    private async Task<Dictionary<int, ProductRef>> ProductsAsync(IEnumerable<int> ids, CancellationToken ct)
    {
        var list = ids.Distinct().ToList();
        if (list.Count == 0) return new Dictionary<int, ProductRef>();
        return await db.Set<Product>().AsNoTracking().Where(p => list.Contains(p.ProductId))
            .Select(p => new ProductRef(p.ProductId, p.PublicId, p.Sku)).ToDictionaryAsync(p => p.ProductId, ct);
    }

    private async Task<Dictionary<int, string>> ClientNamesAsync(IEnumerable<int?> ids, CancellationToken ct)
    {
        var list = ids.Where(i => i.HasValue).Select(i => i!.Value).Distinct().ToList();
        if (list.Count == 0) return new Dictionary<int, string>();
        return await db.Set<Client>().AsNoTracking().Where(c => list.Contains(c.ClientId)).ToDictionaryAsync(c => c.ClientId, c => c.Name, ct);
    }

    private async Task<Dictionary<int, string>> SupplierNamesAsync(IEnumerable<int> ids, CancellationToken ct)
    {
        var list = ids.Distinct().ToList();
        if (list.Count == 0) return new Dictionary<int, string>();
        return await db.Set<Supplier>().AsNoTracking().Where(s => list.Contains(s.SupplierId)).ToDictionaryAsync(s => s.SupplierId, s => s.Name, ct);
    }

    /// <summary>Códigos de almacén cargados bajo demanda (una consulta por tanda de ids nuevos).</summary>
    private sealed class WarehouseNames(TeikemDbContext db)
    {
        private readonly Dictionary<int, string> _codes = new();

        public async Task LoadAsync(IEnumerable<int> ids, CancellationToken ct)
        {
            var missing = ids.Distinct().Where(i => !_codes.ContainsKey(i)).ToList();
            if (missing.Count == 0) return;
            foreach (var w in await db.Set<Warehouse>().AsNoTracking().Where(w => missing.Contains(w.WarehouseId))
                         .Select(w => new { w.WarehouseId, w.Code }).ToListAsync(ct))
                _codes[w.WarehouseId] = w.Code;
        }

        public string? Code(int warehouseId) => _codes.GetValueOrDefault(warehouseId);
    }
}
