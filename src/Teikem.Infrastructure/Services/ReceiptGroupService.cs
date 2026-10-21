using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Clients;
using Teikem.Domain.Common;
using Teikem.Domain.Constants;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Orders;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 182 (C4) — recepción maestra (RCM-#####): un camión que trae varios documentos (avisos de llegada y/o órdenes de compra) DEL MISMO DUEÑO y almacén. Cada documento conserva su recibo de siempre
/// (REC-#####, con sus líneas, tandas, estatus y acomodo): la maestra los abre juntos bajo un número, con el mismo transporte y referencia, los muestra como un solo avance y los confirma juntos.
/// Se arma al abrir los recibos (no se suman documentos después); un dueño distinto = otra recepción. La maestra no hace nada nuevo con el inventario: cada recibo lo asienta el ReceiptService
/// (mismo ledger, mismas reglas de costo, de cruce de muelle y de acomodo). Mismo patrón que la recolección maestra (<see cref="PickGroupService"/>).
/// </summary>
public sealed class ReceiptGroupService(TeikemDbContext db, ITenantContext tenant, INumberSequenceService numbers, ReceiptService receipts)
{
    public const int MaxPageSize = 100;
    private TenantContext T => (TenantContext)tenant;

    private sealed record DocInfo(int? AsnId, Guid? PoPublicId, string Number, int WarehouseId, string WarehouseCode, int? OwnerClientId, string OwnerName);

    private const string OwnLabel = "la compañía";

    // ================================================================ abrir

    public async Task<ReceiptGroupDto> CreateAsync(ReceiptGroupCreateRequest? req, CancellationToken ct)
    {
        var requested = req?.Documents ?? Array.Empty<ReceiptGroupDocumentRequest>();
        var bad = ReceiptGroupRules.ValidateSelection(requested.Select(d => (d.AsnId, d.PurchaseOrderPublicId)).ToList());
        if (bad is { } b) throw new ValidationException(b.Field, b.Message);
        var note = string.IsNullOrWhiteSpace(req?.Note) ? null : req!.Note.Trim();
        if (note is { Length: > ReceiptGroupRules.MaxNoteLength }) throw new ValidationException("note", ReceiptGroupRules.NoteTooLong);

        // Los documentos, bajo el filtro de tenant (otro tenant o inexistente → 404), del mismo dueño y almacén.
        var infos = new List<DocInfo>();
        foreach (var d in requested) infos.Add(await ResolveDocAsync(d, ct));
        for (var i = 1; i < infos.Count; i++)
        {
            if (infos[i].OwnerClientId != infos[0].OwnerClientId) throw new ValidationException($"documents[{i}]", ReceiptGroupRules.OtherOwner($"{infos[0].Number} ({infos[0].OwnerName})", $"{infos[i].Number} ({infos[i].OwnerName})"));
            if (infos[i].WarehouseId != infos[0].WarehouseId) throw new ValidationException($"documents[{i}]", ReceiptGroupRules.OtherWarehouse($"{infos[0].Number} ({infos[0].WarehouseCode})", $"{infos[i].Number} ({infos[i].WarehouseCode})"));
        }

        // Los contadores en autocommit ANTES de la transacción externa (patrón de NumberSequenceService); la apertura anidada de cada recibo los encuentra y no inserta nada.
        await numbers.EnsureAsync(NumberKinds.Receipt, null, ct);
        await numbers.EnsureAsync(ReceiptGroupCodes.NumberKind, null, ct);
        var tenantId = T.RequireTenantId();
        var groupPublicId = await db.RunInTransactionAsync(async ct2 =>
        {
            var receiptIds = new List<Guid>();
            foreach (var info in infos)
            {
                var created = await receipts.CreateAsync(new ReceiptCreateRequest(AsnId: info.AsnId, PurchaseOrderPublicId: info.PoPublicId, DockId: req!.DockId, StagingBinId: req.StagingBinId,
                    Carrier: req.Carrier, Reference: req.Reference, ReceivingMode: req.ReceivingMode), ct2);
                receiptIds.Add(created.Header.PublicId);
            }
            var seq = await numbers.NextAsync(ReceiptGroupCodes.NumberKind, null, ct2);
            var group = new ReceiptGroup
            {
                PublicId = Guid.NewGuid(), TenantId = tenantId, WarehouseId = infos[0].WarehouseId, ClientId = infos[0].OwnerClientId, Number = NumberFormat.Resolve(ReceiptGroupCodes.NumberPattern, seq),
                Note = note, CreatedAtUtc = DateTime.UtcNow, CreatedBy = tenant.UserId, IsActive = true,
            };
            db.Set<ReceiptGroup>().Add(group);
            await db.SaveGuardedAsync(ReceiptGroupRules.NumberTaken, ct2);
            // los servicios anidados pueden soltar lo rastreado: los recibos se vuelven a cargar
            var members = await db.Set<ReceiptHeader>().AsTracking().Where(h => receiptIds.Contains(h.PublicId)).ToListAsync(ct2);
            foreach (var m in members) m.ReceiptGroupId = group.ReceiptGroupId;
            await db.SaveGuardedAsync(ReceiptGroupRules.Concurrency, ct2);
            return group.PublicId;
        }, ct);
        return await GetAsync(groupPublicId, ct);
    }

    private async Task<DocInfo> ResolveDocAsync(ReceiptGroupDocumentRequest d, CancellationToken ct)
    {
        if (d.AsnId is int asnId)
        {
            var a = await (from s in db.Set<Asn>().AsNoTracking()
                           join w in db.Set<Warehouse>().AsNoTracking() on s.WarehouseId equals w.WarehouseId
                           join c in db.Clients.AsNoTracking() on s.ClientId equals (int?)c.ClientId into cs
                           from c in cs.DefaultIfEmpty()
                           where s.AsnId == asnId && s.IsActive
                           select new DocInfo(s.AsnId, null, s.Number, s.WarehouseId, w.Code, s.ClientId, c == null ? OwnLabel : c.Name)).FirstOrDefaultAsync(ct);
            return a ?? throw new NotFoundException("Aviso de llegada");
        }
        var po = await (from p in db.Set<PurchaseOrder>().AsNoTracking()
                        join w in db.Set<Warehouse>().AsNoTracking() on p.WarehouseId equals w.WarehouseId
                        where p.PublicId == d.PurchaseOrderPublicId && p.IsActive
                        select new DocInfo(null, p.PublicId, p.Number, p.WarehouseId, w.Code, null, OwnLabel)).FirstOrDefaultAsync(ct);
        return po ?? throw new NotFoundException("Orden de compra", feminine: true);
    }

    // ================================================================ confirmar y eliminar

    /// <summary>Confirma todos los recibos que aún no lo están, en UNA transacción (si uno falla, ninguno se confirma). Cada confirmación es la de siempre.</summary>
    public async Task<ReceiptGroupDto> ConfirmAsync(Guid publicId, ReceiptGroupConfirmRequest? req, CancellationToken ct)
    {
        var group = await db.Set<ReceiptGroup>().AsNoTracking().FirstOrDefaultAsync(g => g.PublicId == publicId && g.IsActive, ct) ?? throw new NotFoundException(ReceiptGroupRules.Label, feminine: true);
        var open = await db.Set<ReceiptHeader>().AsNoTracking().Where(h => h.ReceiptGroupId == group.ReceiptGroupId && h.IsActive && h.ReceivedAtUtc == null).OrderBy(h => h.ReceiptHeaderId)
            .Select(h => h.PublicId).ToListAsync(ct);
        if (open.Count == 0) throw new StatusRuleException(ReceiptGroupRules.AlreadyConfirmed);
        var comment = string.IsNullOrWhiteSpace(req?.Comment) ? null : req!.Comment.Trim();
        await db.RunInTransactionAsync(async ct2 =>
        {
            foreach (var id in open) await receipts.ConfirmAsync(id, new ReceiptConfirmRequest(comment), ct2);
        }, ct);
        return await GetAsync(publicId, ct);
    }

    /// <summary>Elimina la maestra (y sus recibos) mientras ninguno esté confirmado; cada recibo se elimina con las reglas de siempre (con tandas o cruce de muelle no se puede).</summary>
    public async Task DeleteAsync(Guid publicId, CancellationToken ct)
    {
        var group = await db.Set<ReceiptGroup>().AsNoTracking().FirstOrDefaultAsync(g => g.PublicId == publicId && g.IsActive, ct) ?? throw new NotFoundException(ReceiptGroupRules.Label, feminine: true);
        var members = await db.Set<ReceiptHeader>().AsNoTracking().Where(h => h.ReceiptGroupId == group.ReceiptGroupId && h.IsActive).OrderBy(h => h.ReceiptHeaderId)
            .Select(h => new { h.PublicId, h.ReceivedAtUtc }).ToListAsync(ct);
        if (members.Any(m => m.ReceivedAtUtc != null)) throw new StatusRuleException(ReceiptGroupRules.DeleteHasConfirmed);
        await db.RunInTransactionAsync(async ct2 =>
        {
            foreach (var m in members) await receipts.DeleteAsync(m.PublicId, ct2);
            var g = await db.Set<ReceiptGroup>().AsTracking().FirstAsync(x => x.ReceiptGroupId == group.ReceiptGroupId, ct2);
            g.IsActive = false;
            await db.SaveGuardedAsync(ReceiptGroupRules.Concurrency, ct2);
        }, ct);
    }

    // ================================================================ consultas

    public async Task<ReceiptGroupPageDto> ListAsync(ReceiptGroupQuery? q, CancellationToken ct)
    {
        q ??= new ReceiptGroupQuery();
        var skip = Math.Max(0, q.Skip);
        var take = Math.Clamp(q.Take <= 0 ? 50 : q.Take, 1, MaxPageSize);
        var query = db.Set<ReceiptGroup>().AsNoTracking().AsQueryable();
        var status = q.Status?.Trim().ToUpperInvariant();
        if (status == ReceiptGroupRules.StatusCancelled) query = query.Where(g => !g.IsActive);
        else if (status == ReceiptGroupRules.StatusOpen)
            query = query.Where(g => g.IsActive && (!db.Set<ReceiptHeader>().Any(h => h.ReceiptGroupId == g.ReceiptGroupId && h.IsActive) || db.Set<ReceiptHeader>().Any(h => h.ReceiptGroupId == g.ReceiptGroupId && h.IsActive && h.ReceivedAtUtc == null)));
        else if (status == ReceiptGroupRules.StatusConfirmed)
            query = query.Where(g => g.IsActive && db.Set<ReceiptHeader>().Any(h => h.ReceiptGroupId == g.ReceiptGroupId && h.IsActive) && !db.Set<ReceiptHeader>().Any(h => h.ReceiptGroupId == g.ReceiptGroupId && h.IsActive && h.ReceivedAtUtc == null));
        else if (status is not null && status.Length > 0) throw new ValidationException("status", "El estatus debe ser OPEN, CONFIRMED o CANCELLED.");
        var s = q.Search?.Trim();
        if (!string.IsNullOrEmpty(s))
            query = query.Where(g => g.Number.Contains(s) || (g.Note != null && g.Note.Contains(s)) || db.Set<ReceiptHeader>().Any(h => h.ReceiptGroupId == g.ReceiptGroupId
                && (h.Number.Contains(s) || db.Set<Asn>().Any(a => a.AsnId == h.AsnId && (a.Number.Contains(s) || db.Set<PurchaseOrder>().Any(p => p.PurchaseOrderId == a.PurchaseOrderId && p.Number.Contains(s)))))));
        var total = await query.CountAsync(ct);
        var page = await query.OrderByDescending(g => g.CreatedAtUtc).ThenByDescending(g => g.ReceiptGroupId).Skip(skip).Take(take).ToListAsync(ct);
        return new ReceiptGroupPageDto(total, skip, take, await ToDtosAsync(page, ct));
    }

    public async Task<ReceiptGroupDto> GetAsync(Guid publicId, CancellationToken ct)
    {
        var g = await db.Set<ReceiptGroup>().AsNoTracking().FirstOrDefaultAsync(x => x.PublicId == publicId, ct) ?? throw new NotFoundException(ReceiptGroupRules.Label, feminine: true);
        return (await ToDtosAsync(new List<ReceiptGroup> { g }, ct))[0];
    }

    private async Task<IReadOnlyList<ReceiptGroupDto>> ToDtosAsync(IReadOnlyList<ReceiptGroup> groups, CancellationToken ct)
    {
        if (groups.Count == 0) return Array.Empty<ReceiptGroupDto>();
        var groupIds = groups.Select(g => g.ReceiptGroupId).ToList();
        var headers = await db.Set<ReceiptHeader>().AsNoTracking().Where(h => h.ReceiptGroupId != null && groupIds.Contains(h.ReceiptGroupId.Value)).OrderBy(h => h.ReceiptHeaderId).ToListAsync(ct);
        var headerIds = headers.Select(h => h.ReceiptHeaderId).ToList();
        var sums = await db.Set<ReceiptLine>().AsNoTracking().Where(l => headerIds.Contains(l.ReceiptHeaderId))
            .GroupBy(l => l.ReceiptHeaderId).Select(x => new { Id = x.Key, Lines = x.Count(), Expected = x.Sum(l => l.ExpectedQty ?? 0m), Received = x.Sum(l => l.ReceivedQty) }).ToDictionaryAsync(x => x.Id, ct);
        var statusIds = headers.Select(h => h.StatusCodeId).Distinct().ToList();
        var statusRows = await db.StatusCodes.AsNoTracking().Where(s => statusIds.Contains(s.StatusCodeId)).ToDictionaryAsync(s => s.StatusCodeId, ct);
        var asnIds = headers.Where(h => h.AsnId != null).Select(h => h.AsnId!.Value).Distinct().ToList();
        var asnRows = await db.Set<Asn>().AsNoTracking().Where(a => asnIds.Contains(a.AsnId)).Select(a => new { a.AsnId, a.Number, a.PurchaseOrderId }).ToDictionaryAsync(a => a.AsnId, ct);
        var poIds = asnRows.Values.Where(a => a.PurchaseOrderId != null).Select(a => a.PurchaseOrderId!.Value).Distinct().ToList();
        var poNumbers = await db.Set<PurchaseOrder>().AsNoTracking().Where(p => poIds.Contains(p.PurchaseOrderId)).ToDictionaryAsync(p => p.PurchaseOrderId, p => p.Number, ct);
        var warehouseIds = groups.Select(g => g.WarehouseId).Distinct().ToList();
        var warehouses = await db.Set<Warehouse>().AsNoTracking().Where(w => warehouseIds.Contains(w.WarehouseId)).ToDictionaryAsync(w => w.WarehouseId, ct);
        var clientIds = groups.Where(g => g.ClientId != null).Select(g => g.ClientId!.Value).Distinct().ToList();
        var clients = await db.Clients.AsNoTracking().Where(c => clientIds.Contains(c.ClientId)).ToDictionaryAsync(c => c.ClientId, ct);
        var userIds = groups.Where(g => g.CreatedBy != null).Select(g => g.CreatedBy!.Value).Distinct().ToList();
        var users = await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).Select(u => new { u.Id, Name = u.FullName ?? u.Email }).ToDictionaryAsync(u => u.Id, u => u.Name, ct);

        return groups.Select(g =>
        {
            // un grupo vigente muestra solo sus recibos vigentes; uno eliminado, los que tuvo
            var members = headers.Where(h => h.ReceiptGroupId == g.ReceiptGroupId && (!g.IsActive || h.IsActive)).Select(h =>
            {
                var sum = sums.GetValueOrDefault(h.ReceiptHeaderId);
                var st = statusRows.GetValueOrDefault(h.StatusCodeId);
                var asn = h.AsnId is int a ? asnRows.GetValueOrDefault(a) : null;
                var po = asn?.PurchaseOrderId is int pid ? poNumbers.GetValueOrDefault(pid) : null;
                return new ReceiptGroupMemberDto(h.PublicId, h.Number, st?.InternalCode ?? string.Empty, st is null ? string.Empty : MultilingualText.Resolve(st.LabelJson, T.Lang) ?? st.InternalCode,
                    po is not null ? "PO" : "ASN", po ?? asn?.Number, sum?.Lines ?? 0, sum?.Expected ?? 0m, sum?.Received ?? 0m, h.ReceivedAtUtc != null);
            }).ToList();
            var confirmed = members.Count(m => m.IsConfirmed);
            var wh = warehouses.GetValueOrDefault(g.WarehouseId);
            var owner = g.ClientId is int cid ? clients.GetValueOrDefault(cid) : null;
            var status = ReceiptGroupRules.Status(g.IsActive, members.Count, confirmed);
            return new ReceiptGroupDto(g.PublicId, g.Number, status, wh?.PublicId ?? Guid.Empty, wh?.Code ?? string.Empty, owner?.PublicId, owner?.Name, g.Note, g.CreatedAtUtc,
                g.CreatedBy is int u ? users.GetValueOrDefault(u) : null, members.Count, confirmed, members.Sum(m => m.ExpectedQty), members.Sum(m => m.ReceivedQty),
                g.IsActive && confirmed < members.Count, g.IsActive && confirmed == 0, g.IsActive, g.RowVersion is null ? string.Empty : Convert.ToBase64String(g.RowVersion), members);
        }).ToList();
    }
}
