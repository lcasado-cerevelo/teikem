using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Common;
using Teikem.Domain.Constants;
using Teikem.Domain.ExitNotices;
using Teikem.Domain.Orders;
using Teikem.Domain.Sales;
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
/// Lote 176 — recolección maestra (decisión del dueño, 2026-10-21): se eligen varios documentos (órdenes de venta y/o avisos de salida) DEL MISMO CLIENTE y almacén y se recolecta todo junto, en UNA
/// transacción: cada documento hace su despacho de siempre (su recolección con sus movimientos, sus reservas, su estatus) y la maestra los reúne bajo un número (EMP-#####). Al terminar se le
/// crea UN envío a la maestra (el empaque): un solo destino; para otro destino, otra recolección. No se suman documentos después. Eliminar la maestra deshace todas sus recolecciones.
/// La maestra no hace nada nuevo con el inventario: cada despacho lo hace el servicio de su documento (mismo ledger, mismas reservas, mismas reglas de rotación).
/// </summary>
public sealed class PickGroupService(
    TeikemDbContext db,
    ITenantContext tenant,
    StatusService statuses,
    INumberSequenceService numbers,
    PermissionService permissions,
    ModuleService modules,
    OrderService orders,
    SalesOrderService sales,
    ExitNoticeService exits,
    PickBatchService batches)
{
    private sealed record DocInfo(string Kind, Guid PublicId, int Id, string Number, int ClientId, int WarehouseId, string ClientName, string WarehouseCode);

    // ================================================================ recolectar

    public async Task<PickGroupDto> CreateAsync(PickGroupCreateRequest? req, CancellationToken ct)
    {
        var requested = req?.Documents ?? Array.Empty<PickGroupDocumentRequest>();
        var bad = PickGroupRules.ValidateSelection(requested.Select(d => (d.Kind, d.PublicId)).ToList());
        if (bad is { } b) throw new ValidationException(b.Field, b.Message);
        var (note, noteError) = PickBatchRules.NormalizeNote(req?.Note);
        if (noteError is not null) throw new ValidationException("note", noteError);

        var kinds = requested.Select(d => PickGroupRules.NormalizeKind(d.Kind)!).ToList();
        if (kinds.Contains(PickGroupRules.KindSalesOrder)) await modules.EnsureEnabledAsync(ModuleKeys.SalesOrders, ct);

        // Los documentos, bajo el filtro de tenant (otro tenant o inexistente → 404), del mismo cliente y almacén.
        var infos = new List<DocInfo>();
        for (var i = 0; i < requested.Count; i++)
            infos.Add(await ResolveDocAsync(kinds[i], requested[i].PublicId!.Value, ct));
        for (var i = 1; i < infos.Count; i++)
        {
            if (infos[i].ClientId != infos[0].ClientId) throw new ValidationException($"documents[{i}]", PickGroupRules.OtherClient(infos[0].Number + " (" + infos[0].ClientName + ")", infos[i].Number + " (" + infos[i].ClientName + ")"));
            if (infos[i].WarehouseId != infos[0].WarehouseId) throw new ValidationException($"documents[{i}]", PickGroupRules.OtherWarehouse(infos[0].Number + " (" + infos[0].WarehouseCode + ")", infos[i].Number + " (" + infos[i].WarehouseCode + ")"));
        }

        // El contador de empaque en autocommit ANTES de la transacción externa (patrón de NumberSequenceService); los despachos anidados lo encuentran y no insertan nada.
        await numbers.EnsureAsync(NumberKinds.PackBatch, null, ct);
        var tenantId = ((TenantContext)tenant).RequireTenantId();
        var groupPublicId = await db.RunInTransactionAsync(async ct2 =>
        {
            var batchIds = new List<Guid>();
            for (var i = 0; i < requested.Count; i++)
            {
                var info = infos[i];
                var lines = requested[i].Lines;
                batchIds.Add(info.Kind == PickGroupRules.KindSalesOrder
                    ? (await sales.DispatchAsync(info.PublicId, new SalesDispatchRequest(await SalesLinesAsync(info, lines, ct2), note), ct2)).PickBatchPublicId
                    : (await exits.DispatchAsync(info.PublicId, new ExitNoticeDispatchRequest(await ExitLinesAsync(info, lines, ct2), note), ct2)).PickBatchPublicId);
            }

            var seq = await numbers.NextAsync(NumberKinds.PackBatch, null, ct2);
            var group = new PickGroup
            {
                PublicId = Guid.NewGuid(), TenantId = tenantId, WarehouseId = infos[0].WarehouseId, ClientId = infos[0].ClientId,
                Number = NumberingRules.Resolve(NumberingRules.PackBatchPattern, seq), Note = note, CollectedAtUtc = DateTime.UtcNow, CollectedBy = tenant.UserId, IsActive = true,
            };
            db.Set<PickGroup>().Add(group);
            await db.SaveGuardedAsync(PickGroupRules.NumberTaken, ct2);
            var members = await db.Set<PickBatch>().AsTracking().Where(x => batchIds.Contains(x.PublicId)).ToListAsync(ct2);
            foreach (var m in members) m.PickGroupId = group.PickGroupId;
            await db.SaveGuardedAsync(PickGroupRules.NumberTaken, ct2);
            return group.PublicId;
        }, ct);
        return await GetAsync(groupPublicId, ct);
    }

    private async Task<DocInfo> ResolveDocAsync(string kind, Guid publicId, CancellationToken ct)
    {
        if (kind == PickGroupRules.KindSalesOrder)
        {
            var o = await (from s in db.Set<SalesOrder>().AsNoTracking()
                           join c in db.Clients.AsNoTracking() on s.ClientId equals c.ClientId
                           join w in db.Set<Warehouse>().AsNoTracking() on s.WarehouseId equals w.WarehouseId
                           where s.PublicId == publicId
                           select new DocInfo(PickGroupRules.KindSalesOrder, s.PublicId, s.SalesOrderId, s.Number, s.ClientId, s.WarehouseId, c.Name, w.Code)).FirstOrDefaultAsync(ct);
            return o ?? throw new NotFoundException("Orden de venta", feminine: true);
        }
        var n = await (from s in db.Set<ExitNotice>().AsNoTracking()
                       join c in db.Clients.AsNoTracking() on s.ClientId equals c.ClientId
                       join w in db.Set<Warehouse>().AsNoTracking() on s.WarehouseId equals w.WarehouseId
                       where s.PublicId == publicId
                       select new DocInfo(PickGroupRules.KindExitNotice, s.PublicId, s.ExitNoticeId, s.Number, s.ClientId, s.WarehouseId, c.Name, w.Code)).FirstOrDefaultAsync(ct);
        return n ?? throw new NotFoundException("Aviso de salida");
    }

    /// <summary>Sin Lines se recolecta todo lo reservado del documento (nada reservado → 422).</summary>
    private async Task<IReadOnlyList<SalesDispatchLineRequest>> SalesLinesAsync(DocInfo d, IReadOnlyList<PickGroupLineRequest>? lines, CancellationToken ct)
    {
        if (lines is { Count: > 0 }) return lines.Select(l => new SalesDispatchLineRequest(l.LineId, l.Quantity, l.BinId, l.LotId)).ToList();
        var reserved = await db.Set<SalesOrderLine>().AsNoTracking().Where(l => l.SalesOrderId == d.Id && l.QtyReserved > 0m).OrderBy(l => l.LineNo)
            .Select(l => new SalesDispatchLineRequest(l.SalesOrderLineId, l.QtyReserved, null, null)).ToListAsync(ct);
        return reserved.Count == 0 ? throw new StatusRuleException(PickGroupRules.NothingReserved(d.Number)) : reserved;
    }

    private async Task<IReadOnlyList<ExitNoticeDispatchLineRequest>> ExitLinesAsync(DocInfo d, IReadOnlyList<PickGroupLineRequest>? lines, CancellationToken ct)
    {
        if (lines is { Count: > 0 }) return lines.Select(l => new ExitNoticeDispatchLineRequest(l.LineId, l.Quantity, l.BinId, l.LotId)).ToList();
        var reserved = await db.Set<ExitNoticeLine>().AsNoTracking().Where(l => l.ExitNoticeId == d.Id && l.QtyReserved > 0m).OrderBy(l => l.LineNo)
            .Select(l => new ExitNoticeDispatchLineRequest(l.ExitNoticeLineId, l.QtyReserved, null, null)).ToListAsync(ct);
        return reserved.Count == 0 ? throw new StatusRuleException(PickGroupRules.NothingReserved(d.Number)) : reserved;
    }

    // ================================================================ leer

    public async Task<PickGroupDto> GetAsync(Guid publicId, CancellationToken ct)
    {
        var g = await db.Set<PickGroup>().AsNoTracking().FirstOrDefaultAsync(x => x.PublicId == publicId, ct) ?? throw NotFound();
        return (await BuildAsync(new List<PickGroup> { g }, withMovements: true, ct))[0];
    }

    public async Task<PickGroupPageDto> ListAsync(PickGroupQuery? q, CancellationToken ct)
    {
        q ??= new PickGroupQuery();
        var skip = Math.Max(0, q.Skip);
        var take = Math.Clamp(q.Take <= 0 ? 50 : q.Take, 1, PickBatchRules.MaxPageSize);
        var query = db.Set<PickGroup>().AsNoTracking();
        var status = string.IsNullOrWhiteSpace(q.Status) ? null : q.Status.Trim().ToUpperInvariant();
        if (status == "CANCELLED") query = query.Where(g => !g.IsActive);
        else if (status == "PACKED") query = query.Where(g => g.IsActive && g.TransportOrderId != null);
        else if (status == "COLLECTED") query = query.Where(g => g.IsActive && g.TransportOrderId == null);
        var search = q.Search?.Trim();
        if (!string.IsNullOrEmpty(search))
        {
            var like = search;
            query = query.Where(g => g.Number.Contains(like)
                || db.Clients.Any(c => c.ClientId == g.ClientId && c.Name.Contains(like))
                || db.Set<PickBatch>().Any(b => b.PickGroupId == g.PickGroupId
                    && (db.Set<SalesOrder>().Any(s => s.SalesOrderId == b.SalesOrderId && s.Number.Contains(like))
                        || db.Set<ExitNotice>().Any(n => n.ExitNoticeId == b.ExitNoticeId && n.Number.Contains(like)))));
        }
        var total = await query.CountAsync(ct);
        var page = await query.OrderByDescending(g => g.CollectedAtUtc).ThenByDescending(g => g.PickGroupId).Skip(skip).Take(take).ToListAsync(ct);
        return new PickGroupPageDto(total, skip, take, await BuildAsync(page, withMovements: false, ct));
    }

    private async Task<List<PickGroupDto>> BuildAsync(List<PickGroup> groups, bool withMovements, CancellationToken ct)
    {
        if (groups.Count == 0) return new List<PickGroupDto>();
        var ids = groups.Select(g => g.PickGroupId).ToList();
        var members = await db.Set<PickBatch>().AsNoTracking().Where(b => b.PickGroupId != null && ids.Contains(b.PickGroupId.Value)).OrderBy(b => b.PickBatchId).ToListAsync(ct);
        // activas, salvo en una maestra eliminada (se ve lo que tenía)
        var activeGroups = groups.Where(g => g.IsActive).Select(g => g.PickGroupId).ToHashSet();
        members = members.Where(b => b.IsActive || !activeGroups.Contains(b.PickGroupId!.Value)).ToList();
        var batchIds = members.Select(b => b.PickBatchId).ToList();
        var lines = await (from l in db.Set<PickBatchLine>().AsNoTracking()
                           join p in db.Set<Product>().AsNoTracking() on l.ProductId equals (int?)p.ProductId into pj
                           from p in pj.DefaultIfEmpty()
                           join lot in db.Set<InventoryLot>().AsNoTracking() on l.LotId equals (int?)lot.LotId into lj
                           from lot in lj.DefaultIfEmpty()
                           where batchIds.Contains(l.PickBatchId)
                           orderby l.PickBatchLineId
                           select new
                           {
                               Line = l, PublicId = p == null ? Guid.Empty : p.PublicId, Sku = p == null ? string.Empty : p.Sku, Name = p == null ? string.Empty : p.Name,
                               LotNumber = lot == null ? null : lot.LotNumber,
                           }).ToListAsync(ct);
        var binIds = lines.Select(x => x.Line.FromBinId).Distinct().ToList();
        var binCodes = await db.Set<WarehouseBin>().AsNoTracking().Where(b => binIds.Contains(b.WarehouseBinId)).ToDictionaryAsync(b => b.WarehouseBinId, b => b.Code, ct);

        var salesIds = members.Where(b => b.SalesOrderId != null).Select(b => b.SalesOrderId!.Value).Distinct().ToList();
        var exitIds = members.Where(b => b.ExitNoticeId != null).Select(b => b.ExitNoticeId!.Value).Distinct().ToList();
        var salesNumbers = await db.Set<SalesOrder>().AsNoTracking().Where(s => salesIds.Contains(s.SalesOrderId)).ToDictionaryAsync(s => s.SalesOrderId, s => (s.PublicId, s.Number), ct);
        var exitNumbers = await db.Set<ExitNotice>().AsNoTracking().Where(n => exitIds.Contains(n.ExitNoticeId)).ToDictionaryAsync(n => n.ExitNoticeId, n => (n.PublicId, n.Number), ct);

        var whIds = groups.Select(g => g.WarehouseId).Distinct().ToList();
        var warehouses = await db.Set<Warehouse>().AsNoTracking().Where(w => whIds.Contains(w.WarehouseId)).ToDictionaryAsync(w => w.WarehouseId, w => (w.PublicId, w.Code), ct);
        var clientIds = groups.Select(g => g.ClientId).Distinct().ToList();
        var clients = await db.Clients.AsNoTracking().Where(c => clientIds.Contains(c.ClientId)).ToDictionaryAsync(c => c.ClientId, c => (c.PublicId, c.Name), ct);
        var userIds = groups.Where(g => g.CollectedBy != null).Select(g => g.CollectedBy!.Value).Distinct().ToList();
        var users = await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => string.IsNullOrWhiteSpace(u.FullName) ? u.UserName : u.FullName, ct);
        var orderIds = groups.Where(g => g.TransportOrderId != null).Select(g => g.TransportOrderId!.Value).Distinct().ToList();
        var orderRows = orderIds.Count == 0
            ? new Dictionary<int, (Guid PublicId, string OrderNumber, string? Invoice, bool IsActive, bool IsInitial)>()
            : (await (from o in db.TransportOrders.AsNoTracking()
                      join s in db.StatusCodes.AsNoTracking() on o.StatusCodeId equals s.StatusCodeId
                      where orderIds.Contains(o.TransportOrderId)
                      select new { o.TransportOrderId, o.PublicId, o.OrderNumber, o.ClientInvoiceNumber, o.IsActive, s.IsInitial }).ToListAsync(ct))
                .ToDictionary(o => o.TransportOrderId, o => (o.PublicId, o.OrderNumber, o.ClientInvoiceNumber, o.IsActive, o.IsInitial));

        var result = new List<PickGroupDto>();
        foreach (var g in groups)
        {
            var mine = members.Where(b => b.PickGroupId == g.PickGroupId).ToList();
            (string Kind, Guid PublicId, string Number) Doc(PickBatch b)
                => b.SalesOrderId is int s && salesNumbers.TryGetValue(s, out var so) ? (PickGroupRules.KindSalesOrder, so.PublicId, so.Number)
                 : b.ExitNoticeId is int e && exitNumbers.TryGetValue(e, out var en) ? (PickGroupRules.KindExitNotice, en.PublicId, en.Number)
                 : ("", Guid.Empty, "");
            var qtyByBatch = lines.Where(x => mine.Any(b => b.PickBatchId == x.Line.PickBatchId)).GroupBy(x => x.Line.PickBatchId)
                .ToDictionary(x => x.Key, x => x.Sum(y => y.Line.KitProductId == null ? y.Line.Quantity : (y.Line.KitQuantity ?? 0m)));
            var docs = mine.Select(b => { var d = Doc(b); return new PickGroupDocumentDto(d.Kind, d.PublicId, d.Number, b.PublicId, b.Number, qtyByBatch.GetValueOrDefault(b.PickBatchId)); }).ToList();
            var moves = withMovements
                ? lines.Where(x => mine.Any(b => b.PickBatchId == x.Line.PickBatchId)).Select(x =>
                {
                    var b = mine.First(m => m.PickBatchId == x.Line.PickBatchId);
                    var d = Doc(b);
                    return new PickGroupMovementDto(d.Kind, d.Number, b.Number, x.PublicId, x.Sku, x.Name, x.Line.Quantity, x.Line.FromBinId,
                        binCodes.GetValueOrDefault(x.Line.FromBinId) ?? string.Empty, x.Line.LotId, x.LotNumber);
                }).ToList()
                : new List<PickGroupMovementDto>();
            var total = lines.Where(x => mine.Any(b => b.PickBatchId == x.Line.PickBatchId)).Sum(x => x.Line.Quantity);
            var order = g.TransportOrderId is int oid ? (orderRows.TryGetValue(oid, out var o) ? o : default) : default;
            var hasOrder = g.TransportOrderId is int oid2 && orderRows.ContainsKey(oid2);
            var canDelete = g.IsActive && (g.TransportOrderId is null || (hasOrder && order.IsActive && order.IsInitial));
            var (wh, cl) = (warehouses.GetValueOrDefault(g.WarehouseId), clients.GetValueOrDefault(g.ClientId));
            result.Add(new PickGroupDto(g.PickGroupId, g.PublicId, g.Number, wh.PublicId, wh.Code ?? string.Empty, cl.PublicId, cl.Name ?? string.Empty,
                PickGroupRules.Status(g.IsActive, g.TransportOrderId), g.CollectedAtUtc, g.CollectedBy is int u ? users.GetValueOrDefault(u) : null, g.PackedAtUtc,
                hasOrder ? order.PublicId : null, hasOrder ? order.OrderNumber : null, hasOrder ? order.Item3 : null, g.Note, total,
                g.IsActive && g.TransportOrderId is null && mine.Count > 0, canDelete, docs, moves, g.IsActive, Convert.ToBase64String(g.RowVersion ?? Array.Empty<byte>())));
        }
        return result;
    }

    // ================================================================ empacar: UN envío para toda la maestra

    public async Task<PickGroupPackResultDto> PackAsync(Guid publicId, PickBatchPackRequest? req, CancellationToken ct)
    {
        await permissions.EnsureAsync(PermissionCatalog.OrdersCreate, ct);
        if (req?.Order is null) throw new ValidationException("order", PickBatchRules.OrderRequired);
        if (!PickBatchRules.PackRequestAllowed(req.Order.IsSpecialDelivery, req.Order.DriverPublicId is not null))
            throw new ValidationException("order", PickBatchRules.PackSpecialNotAllowed);
        var orderRequest = req.Order;
        var current = await db.Set<PickGroup>().AsNoTracking().FirstOrDefaultAsync(x => x.PublicId == publicId, ct) ?? throw NotFound();

        var order = await db.RunInTransactionAsync(async ct2 =>
        {
            var group = await db.Set<PickGroup>().AsTracking().FirstOrDefaultAsync(x => x.PickGroupId == current.PickGroupId, ct2) ?? throw NotFound();
            if (!group.IsActive || group.TransportOrderId is not null) throw new StatusRuleException(PickGroupRules.NotPackable(group.Number));
            EnsureRowVersion(group, req.RowVersion);
            var members = await db.Set<PickBatch>().AsTracking().Where(b => b.PickGroupId == group.PickGroupId && b.IsActive).OrderBy(b => b.PickBatchId).ToListAsync(ct2);
            if (members.Count == 0) throw new StatusRuleException(PickGroupRules.NotPackable(group.Number));
            var collected = await db.StatusIdAsync(StatusDomains.PickBatchStatus, PickBatchStatuses.Collected, ct2);
            if (members.Any(b => b.StatusCodeId != collected)) throw new StatusRuleException(PickGroupRules.NotPackable(group.Number));
            var memberIds = members.Select(b => b.PickBatchId).ToList();

            // Dueño del inventario (D15): con cliente 3PL, el envío debe ser de ese cliente.
            var ownerId = await (from l in db.Set<PickBatchLine>().AsNoTracking()
                                 join p in db.Set<Product>().AsNoTracking() on l.ProductId equals p.ProductId
                                 where memberIds.Contains(l.PickBatchId)
                                 select p.ClientId).FirstOrDefaultAsync(ct2);
            if (ownerId is int ownerClientId)
            {
                var owner = await db.Clients.AsNoTracking().Where(c => c.ClientId == ownerClientId).Select(c => new { c.PublicId, c.Name }).FirstAsync(ct2);
                if (orderRequest.ClientPublicId != owner.PublicId)
                    throw new ValidationException("order.clientPublicId", PickBatchRules.OrderClientMustBeOwner(owner.Name));
            }

            // El envío REAL, en esta transacción: número de empaque = el de la maestra, origen PICK_BATCH (la primera recolección: solo se borra desde Recolección y empaque).
            var created = await orders.CreateAsync(orderRequest, OrderScope.Any, new OrderCreationOptions(group.Number, EntityTypes.PickBatch, members[0].PickBatchId), ct2);
            // La creación del envío puede limpiar el seguimiento del contexto: se vuelve a leer lo que se va a cambiar.
            group = await db.Set<PickGroup>().AsTracking().FirstAsync(x => x.PickGroupId == group.PickGroupId, ct2);
            members = await db.Set<PickBatch>().AsTracking().Where(m => m.PickGroupId == group.PickGroupId && m.IsActive).OrderBy(m => m.PickBatchId).ToListAsync(ct2);
            var now = DateTime.UtcNow;
            foreach (var b in members)
            {
                var packed = await statuses.TransitionAsync(StatusDomains.PickBatchStatus, EntityTypes.PickBatch, b.PickBatchId, b.StatusCodeId, PickBatchStatuses.Packed, null, ct2);
                b.StatusCodeId = packed.StatusCodeId;
                b.TransportOrderId = created.Id;
                b.ClientInvoiceNumber = created.ClientInvoiceNumber;
                b.PackedAtUtc = now;
                b.PackedBy = tenant.UserId;
            }
            group.TransportOrderId = created.Id;
            group.PackedAtUtc = now;
            group.PackedBy = tenant.UserId;
            await db.SaveGuardedAsync(PickGroupRules.OrderTaken, ct2);
            return created;
        }, ct);
        return new PickGroupPackResultDto(await GetAsync(publicId, ct), order);
    }

    // ================================================================ eliminar: deshace todas las recolecciones de la maestra

    public async Task DeleteAsync(Guid publicId, PickBatchDeleteRequest? req, CancellationToken ct)
    {
        var current = await db.Set<PickGroup>().AsNoTracking().FirstOrDefaultAsync(x => x.PublicId == publicId && x.IsActive, ct) ?? throw NotFound();
        var comment = string.IsNullOrWhiteSpace(req?.Comment) ? null : req!.Comment!.Trim();
        await db.RunInTransactionAsync(async ct2 =>
        {
            var group = await db.Set<PickGroup>().AsTracking().FirstOrDefaultAsync(x => x.PickGroupId == current.PickGroupId, ct2) ?? throw NotFound();
            if (!group.IsActive) throw NotFound();
            EnsureRowVersion(group, req?.RowVersion);
            var orderHandled = false;
            if (group.TransportOrderId is int orderId)
            {
                await permissions.EnsureAsync(PermissionCatalog.OrdersCancel, ct2);
                var order = await (from o in db.TransportOrders.AsNoTracking()
                                   join s in db.StatusCodes.AsNoTracking() on o.StatusCodeId equals s.StatusCodeId
                                   where o.TransportOrderId == orderId
                                   select new { o.PublicId, o.IsActive, s.IsInitial, s.LabelJson }).FirstOrDefaultAsync(ct2);
                if (order is null || !order.IsActive || !order.IsInitial)
                    throw new StatusRuleException(PickGroupRules.DeleteBlocked(group.Number, order is null ? "—" : MultilingualText.Resolve(order.LabelJson, tenant.Lang)));
                await orders.DeleteAsync(order.PublicId, OrderScope.Any, new OrderDeletionOptions(EntityTypes.PickBatch), ct2);
                orderHandled = true;
            }
            var members = await db.Set<PickBatch>().AsNoTracking().Where(b => b.PickGroupId == group.PickGroupId && b.IsActive).OrderBy(b => b.PickBatchId).Select(b => b.PublicId).ToListAsync(ct2);
            foreach (var id in members) await batches.DeleteCoreAsync(id, new PickBatchDeleteRequest(comment), orderHandled, ct2);
            // cada eliminación puede limpiar el seguimiento del contexto: la maestra se vuelve a leer para marcarla
            group = await db.Set<PickGroup>().AsTracking().FirstAsync(x => x.PickGroupId == group.PickGroupId, ct2);
            group.IsActive = false;
            await db.SaveGuardedAsync(DbExtensions.ConcurrencyMessage, ct2);
        }, ct);
    }

    /// <summary>La ficha que vio quien actúa sigue siendo la vigente (409 si cambió). Se compara aquí porque el seguimiento del contexto se limpia en medio de la operación.</summary>
    private static void EnsureRowVersion(PickGroup group, string? rowVersion)
    {
        if (string.IsNullOrWhiteSpace(rowVersion)) return;
        if (rowVersion != Convert.ToBase64String(group.RowVersion ?? Array.Empty<byte>())) throw new ConflictException(DbExtensions.ConcurrencyMessage);
    }

    private static NotFoundException NotFound() => new(PickGroupRules.Label, null, true);
}
