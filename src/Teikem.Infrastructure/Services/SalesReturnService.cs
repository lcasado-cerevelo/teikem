using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Billing;
using Teikem.Domain.Clients;
using Teikem.Domain.Common;
using Teikem.Domain.Constants;
using Teikem.Domain.Sales;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Orders;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 81 (P6) — devoluciones de venta (DV-#####). REQUESTED (ventas la pide) → AUTHORIZED (un supervisor) → RECEIVED (el almacén, en partes) → CREDITED (nota de crédito); REJECTED o CANCELLED.
/// - Se devuelve de una orden de venta despachada: por línea hasta despachado − devuelto − lo que ya piden otras devoluciones abiertas; dentro de la ventana de días (SalesReturnWindowDays) del último
///   despacho o, fuera de ella, solo con sales.return.authorize y confirmándolo. Kits y productos con serie no se devuelven en esta versión.
/// - Recibirla REUTILIZA el recibo: crea y confirma un recibo tipo RETURN (ReceiptService, una sola transacción) con lo bueno y lo dañado por línea; lo dañado sigue el flujo de daños (causa «Devolución
///   de cliente», cuarentena o desecho). El inventario lo mueve ese recibo; aquí solo se anota lo devuelto (SalesOrderLine.QtyReturned, que ya descuenta lo facturable).
/// - Acreditarla crea una nota de crédito en borrador por factura con lo recibido que sí se facturó (las facturas en borrador se ajustan en su corrida).
/// </summary>
public sealed class SalesReturnService(TeikemDbContext db, ITenantContext tenant, StatusService statuses, INumberSequenceService numbers, ILookupCache lookups, ITenantClock clock,
    PermissionService permissions, ReceiptService receipts, CreditNoteService credits)
{
    public const string Concurrency = "La devolución cambió mientras se guardaba. Revise y vuelva a intentar.";
    public const int MaxPageSize = 200;
    private TenantContext T => (TenantContext)tenant;

    private static readonly string[] OpenCodes = { SalesReturnStatuses.Requested, SalesReturnStatuses.Authorized, SalesReturnStatuses.Received };

    // ================================================================ ajustes

    public async Task<SalesReturnSettingsDto> GetSettingsAsync(CancellationToken ct)
        => new((await db.Tenants.AsNoTracking().FirstAsync(t => t.TenantId == T.RequireTenantId(), ct)).SalesReturnWindowDays);

    public async Task<SalesReturnSettingsDto> UpdateSettingsAsync(SalesReturnSettingsRequest? req, CancellationToken ct)
    {
        if (req is null) throw new ValidationException("body", "El cuerpo de la solicitud es obligatorio.");
        var t = await db.Tenants.AsTracking().FirstAsync(x => x.TenantId == T.RequireTenantId(), ct);
        if (req.WindowDays is int days)
        {
            if (days < 0 || days > ReturnCodes.MaxWindowDays) throw new ValidationException("windowDays", SalesReturnRules.WindowDaysInvalid);
            t.SalesReturnWindowDays = days;
        }
        await db.SaveGuardedAsync(Concurrency, ct);
        return new SalesReturnSettingsDto(t.SalesReturnWindowDays);
    }

    // ================================================================ consultas

    private sealed record StatusInfo(string Code, string Label, string Color);

    private async Task<Dictionary<int, StatusInfo>> StatusMapAsync(CancellationToken ct)
        => (await db.StatusCodes.AsNoTracking().Where(s => s.Entity == ReturnCodes.StatusDomain).ToListAsync(ct))
            .ToDictionary(s => s.StatusCodeId, s => new StatusInfo(s.InternalCode, MultilingualText.Resolve(s.LabelJson, T.Lang) ?? s.InternalCode, s.ColorHex ?? "#9CA3AF"));

    private async Task<(string Code, string Label)> ReasonAsync(int id, CancellationToken ct)
    {
        var l = await lookups.GetAsync(id, ct);
        return (l?.InternalCode ?? string.Empty, MultilingualText.Resolve(l?.LabelJson, T.Lang) ?? l?.InternalCode ?? string.Empty);
    }

    public async Task<PagedResult<SalesReturnListItemDto>> ListAsync(string? status, Guid? clientPublicId, Guid? orderPublicId, string? q, int skip, int take, CancellationToken ct)
    {
        skip = Math.Max(0, skip);
        take = Math.Clamp(take <= 0 ? 50 : take, 1, MaxPageSize);
        var map = await StatusMapAsync(ct);
        var query = db.SalesReturns.AsNoTracking().Include(r => r.Lines).AsQueryable();
        if (!string.IsNullOrWhiteSpace(status))
        {
            var ids = map.Where(kv => kv.Value.Code.Equals(status.Trim(), StringComparison.OrdinalIgnoreCase)).Select(kv => kv.Key).ToList();
            query = query.Where(r => ids.Contains(r.StatusCodeId));
        }
        if (clientPublicId is Guid cp)
        {
            var cid = await db.Set<Teikem.Domain.Clients.Client>().AsNoTracking().Where(c => c.PublicId == cp).Select(c => (int?)c.ClientId).FirstOrDefaultAsync(ct);
            query = cid is null ? query.Where(r => false) : query.Where(r => r.ClientId == cid);
        }
        if (orderPublicId is Guid op)
        {
            var oid = await db.Set<SalesOrder>().AsNoTracking().Where(o => o.PublicId == op).Select(o => (int?)o.SalesOrderId).FirstOrDefaultAsync(ct);
            query = oid is null ? query.Where(r => false) : query.Where(r => r.SalesOrderId == oid);
        }
        if (!string.IsNullOrWhiteSpace(q))
        {
            var s = q.Trim();
            var clientIds = await db.Set<Teikem.Domain.Clients.Client>().AsNoTracking().Where(c => c.Name.Contains(s)).Select(c => c.ClientId).ToListAsync(ct);
            var orderIds = await db.Set<SalesOrder>().AsNoTracking().Where(o => o.Number.Contains(s)).Select(o => o.SalesOrderId).ToListAsync(ct);
            query = query.Where(r => r.Number.Contains(s) || clientIds.Contains(r.ClientId) || orderIds.Contains(r.SalesOrderId));
        }
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(r => r.SalesReturnId).Skip(skip).Take(take).ToListAsync(ct);
        var clients = await db.Set<Teikem.Domain.Clients.Client>().AsNoTracking().Where(c => rows.Select(r => r.ClientId).Contains(c.ClientId)).ToDictionaryAsync(c => c.ClientId, ct);
        var orders = await db.Set<SalesOrder>().AsNoTracking().Where(o => rows.Select(r => r.SalesOrderId).Contains(o.SalesOrderId)).ToDictionaryAsync(o => o.SalesOrderId, ct);
        var items = new List<SalesReturnListItemDto>();
        foreach (var r in rows)
        {
            var st = map[r.StatusCodeId];
            var reason = await ReasonAsync(r.ReasonLookupId, ct);
            items.Add(new SalesReturnListItemDto(r.PublicId, r.Number, st.Code, st.Label, st.Color, orders[r.SalesOrderId].PublicId, orders[r.SalesOrderId].Number, clients[r.ClientId].PublicId, clients[r.ClientId].Name,
                reason.Code, reason.Label, r.Lines.Sum(l => l.QtyRequested), r.Lines.Sum(l => l.QtyReceived), r.OutsideWindow, r.RequestedAtUtc));
        }
        return new PagedResult<SalesReturnListItemDto>(items, total, skip, take);
    }

    public async Task<SalesReturnDto> GetAsync(Guid publicId, CancellationToken ct)
    {
        var r = await db.SalesReturns.AsNoTracking().Include(x => x.Lines).FirstOrDefaultAsync(x => x.PublicId == publicId, ct) ?? throw new NotFoundException("Devolución", feminine: true);
        var st = (await StatusMapAsync(ct))[r.StatusCodeId];
        var order = await db.Set<SalesOrder>().AsNoTracking().Include(o => o.Lines).FirstAsync(o => o.SalesOrderId == r.SalesOrderId, ct);
        var client = await db.Set<Teikem.Domain.Clients.Client>().AsNoTracking().FirstAsync(c => c.ClientId == r.ClientId, ct);
        var warehouse = await db.Set<Warehouse>().AsNoTracking().Where(w => w.WarehouseId == r.WarehouseId).Select(w => new { w.PublicId, w.Name }).FirstAsync(ct);
        var reason = await ReasonAsync(r.ReasonLookupId, ct);
        var productIds = r.Lines.Select(l => l.ProductId).ToList();
        var products = await db.Set<Product>().AsNoTracking().Where(p => productIds.Contains(p.ProductId)).ToDictionaryAsync(p => p.ProductId, ct);
        var lines = r.Lines.OrderBy(l => l.SalesReturnLineId).Select(l =>
        {
            var p = products[l.ProductId];
            return new SalesReturnLineDto(l.SalesReturnLineId, l.SalesOrderLineId, order.Lines.First(x => x.SalesOrderLineId == l.SalesOrderLineId).LineNo, p.PublicId, p.Sku, p.Name, l.QtyRequested, l.QtyReceived,
                l.QtyDamaged, l.QtyCredited, SalesReturnRules.PendingToReceive(l.QtyRequested, l.QtyReceived));
        }).ToList();
        var rec = await db.Set<ReceiptHeader>().AsNoTracking().Where(h => h.SalesReturnId == r.SalesReturnId).OrderBy(h => h.ReceiptHeaderId).Select(h => new SalesReturnReceiptDto(h.PublicId, h.Number)).ToListAsync(ct);
        var noteStatuses = await db.StatusCodes.AsNoTracking().Where(s => s.Entity == CreditCodes.StatusDomain).ToDictionaryAsync(s => s.StatusCodeId, s => s.InternalCode, ct);
        var notes = (await db.CreditNotes.AsNoTracking().Where(n => n.SalesReturnId == r.SalesReturnId).OrderBy(n => n.CreditNoteId).ToListAsync(ct))
            .Select(n => new SalesReturnCreditDto(n.PublicId, n.CreditNumber, noteStatuses[n.StatusCodeId], n.Subtotal + n.TaxAmount)).ToList();
        return new SalesReturnDto(r.PublicId, r.Number, st.Code, st.Label, st.Color, order.PublicId, order.Number, client.PublicId, client.Name, warehouse.PublicId, warehouse.Name, reason.Code, reason.Label, r.Note,
            r.OutsideWindow, r.RequestedAtUtc, r.AuthorizedAtUtc, r.AuthorizationNote, r.ReceivedAtUtc, lines, rec, notes);
    }

    /// <summary>Lo que se puede devolver de una orden de venta: por línea lo despachado, lo devuelto, lo pendiente en devoluciones abiertas y lo que queda por devolver; y si la ventana ya pasó.</summary>
    public async Task<ReturnableDto> ReturnableAsync(Guid orderPublicId, CancellationToken ct)
    {
        var order = await db.Set<SalesOrder>().AsNoTracking().Include(o => o.Lines).FirstOrDefaultAsync(o => o.PublicId == orderPublicId, ct) ?? throw new NotFoundException("Orden de venta", feminine: true);
        var window = (await GetSettingsAsync(ct)).WindowDays;
        var last = await LastDispatchAsync(order, ct);
        var pending = await PendingByLineAsync(order.Lines.Select(l => l.SalesOrderLineId).ToList(), null, ct);
        var products = await db.Set<Product>().AsNoTracking().Where(p => order.Lines.Select(l => l.ProductId).Contains(p.ProductId)).ToDictionaryAsync(p => p.ProductId, ct);
        var kitId = await lookups.TryGetIdAsync(Teikem.Domain.Wms.KitCodes.ProductTypeDomain, Teikem.Domain.Wms.ProductTypes.Kit, ct);
        var serialId = await lookups.TryGetIdAsync(LookupDomains.TrackingType, TrackingTypes.Serial, ct);
        var lines = order.Lines.Where(l => l.QtyShipped > 0m).OrderBy(l => l.LineNo).Select(l =>
        {
            var p = products[l.ProductId];
            var pend = pending.GetValueOrDefault(l.SalesOrderLineId);
            return new ReturnableLineDto(l.SalesOrderLineId, l.LineNo, p.PublicId, p.Sku, p.Name, l.QtyShipped, l.QtyReturned, pend, SalesReturnRules.Returnable(l.QtyShipped, l.QtyReturned, pend),
                kitId is int k && p.ProductTypeLookupId == k, serialId is int s && p.TrackingTypeLookupId == s);
        }).ToList();
        return new ReturnableDto(order.PublicId, order.Number, last is null ? null : DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(last.Value, DateTimeKind.Utc), clock.Zone)), window,
            last is not null && !SalesReturnRules.WithinWindow(DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(last.Value, DateTimeKind.Utc), clock.Zone)), window, clock.Today), lines);
    }

    private async Task<DateTime?> LastDispatchAsync(SalesOrder order, CancellationToken ct)
    {
        var ids = order.Lines.Select(l => l.SalesOrderLineId).ToList();
        var dates = await (from bl in db.Set<PickBatchLine>().AsNoTracking()
                           join b in db.Set<PickBatch>().AsNoTracking() on bl.PickBatchId equals b.PickBatchId
                           where bl.SalesOrderLineId != null && ids.Contains(bl.SalesOrderLineId.Value) && b.IsActive
                           select (DateTime?)b.CollectedAtUtc).ToListAsync(ct);
        return dates.Max();
    }

    /// <summary>Lo que piden las devoluciones abiertas (solicitada, autorizada o recibida en parte) y todavía no ha llegado, por línea de orden. `exceptReturnId` la deja fuera (al revisar una devolución propia).</summary>
    private async Task<Dictionary<int, decimal>> PendingByLineAsync(IReadOnlyCollection<int> orderLineIds, int? exceptReturnId, CancellationToken ct)
    {
        var open = (await StatusMapAsync(ct)).Where(kv => OpenCodes.Contains(kv.Value.Code)).Select(kv => kv.Key).ToList();
        var rows = await (from l in db.SalesReturnLines.AsNoTracking()
                          join r in db.SalesReturns.AsNoTracking() on l.SalesReturnId equals r.SalesReturnId
                          where orderLineIds.Contains(l.SalesOrderLineId) && open.Contains(r.StatusCodeId) && r.SalesReturnId != exceptReturnId
                          select new { l.SalesOrderLineId, l.QtyRequested, l.QtyReceived }).ToListAsync(ct);
        return rows.GroupBy(x => x.SalesOrderLineId).ToDictionary(g => g.Key, g => g.Sum(x => SalesReturnRules.PendingToReceive(x.QtyRequested, x.QtyReceived)));
    }

    // ================================================================ pedir, autorizar, rechazar y cancelar

    public async Task<SalesReturnDto> CreateAsync(SalesReturnRequest? req, CancellationToken ct)
    {
        if (req is null) throw new ValidationException("body", "El cuerpo de la solicitud es obligatorio.");
        if (req.SalesOrderPublicId is not Guid op) throw new ValidationException("salesOrderPublicId", SalesReturnRules.OrderRequired);
        var reasonId = await RequireReasonAsync(req.Reason, ct);
        var note = NormalizeNote(req.Note);
        if (req.Lines is null || req.Lines.Count == 0) throw new ValidationException("lines", SalesReturnRules.LinesRequired);
        if (req.Lines.Count > SalesReturnRules.MaxLines) throw new ValidationException("lines", SalesReturnRules.TooManyLines);

        var publicId = await db.RunInTransactionAsync(async ct2 =>
        {
            var order = await db.Set<SalesOrder>().AsNoTracking().Include(o => o.Lines).FirstOrDefaultAsync(o => o.PublicId == op, ct2) ?? throw new NotFoundException("Orden de venta", feminine: true);
            if (order.Lines.All(l => l.QtyShipped <= 0m)) throw new StatusRuleException(SalesReturnRules.OrderNotReturnable);
            var window = (await GetSettingsAsync(ct2)).WindowDays;
            var last = await LastDispatchAsync(order, ct2);
            var lastDate = last is null ? order.RequestedDate : DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(last.Value, DateTimeKind.Utc), clock.Zone));
            var outside = !SalesReturnRules.WithinWindow(lastDate, window, clock.Today);
            if (outside && !(req.ConfirmOutsideWindow == true && await permissions.HasPermissionAsync(PermissionCatalog.SalesReturnAuthorize, ct2))) throw new ForbiddenException(SalesReturnRules.OutsideWindowNeedsRight);

            var pending = await PendingByLineAsync(order.Lines.Select(l => l.SalesOrderLineId).ToList(), null, ct2);
            var products = await db.Set<Product>().AsNoTracking().Where(p => order.Lines.Select(l => l.ProductId).Contains(p.ProductId)).ToDictionaryAsync(p => p.ProductId, ct2);
            var kitId = await lookups.TryGetIdAsync(Teikem.Domain.Wms.KitCodes.ProductTypeDomain, Teikem.Domain.Wms.ProductTypes.Kit, ct2);
            var serialId = await lookups.TryGetIdAsync(LookupDomains.TrackingType, TrackingTypes.Serial, ct2);
            var lines = new List<SalesReturnLine>();
            var seen = new HashSet<int>();
            for (var i = 0; i < req.Lines.Count; i++)
            {
                var r = req.Lines[i];
                var ol = order.Lines.FirstOrDefault(l => l.SalesOrderLineId == r.SalesOrderLineId) ?? throw new ValidationException($"lines[{i}]", SalesReturnRules.LineNotFound);
                if (!seen.Add(ol.SalesOrderLineId)) throw new ValidationException($"lines[{i}]", SalesReturnRules.LineRepeated);
                var qty = r.Quantity ?? 0m;
                if (!SalesReturnRules.IsValidQuantity(qty)) throw new ValidationException($"lines[{i}].quantity", SalesReturnRules.QuantityInvalid);
                var p = products[ol.ProductId];
                if (kitId is int k && p.ProductTypeLookupId == k) throw new StatusRuleException(SalesReturnRules.KitNotReturnable);
                if (serialId is int s && p.TrackingTypeLookupId == s) throw new StatusRuleException(SalesReturnRules.SerialNotReturnable);
                var returnable = SalesReturnRules.Returnable(ol.QtyShipped, ol.QtyReturned, pending.GetValueOrDefault(ol.SalesOrderLineId));
                if (qty > returnable) throw new StatusRuleException(SalesReturnRules.ExceedsReturnable(p.Sku, returnable));
                lines.Add(new SalesReturnLine { SalesOrderLineId = ol.SalesOrderLineId, ProductId = ol.ProductId, QtyRequested = qty });
            }
            await numbers.EnsureAsync(ReturnCodes.NumberKind, null, ct2);
            var seq = await numbers.NextAsync(ReturnCodes.NumberKind, null, ct2);
            var initial = await statuses.GetInitialAsync(ReturnCodes.StatusDomain, ct2);
            var ret = new SalesReturn
            {
                PublicId = Guid.NewGuid(), TenantId = T.RequireTenantId(), Number = NumberFormat.Resolve(ReturnCodes.NumberPattern, seq), SalesOrderId = order.SalesOrderId, ClientId = order.ClientId,
                WarehouseId = order.WarehouseId, ReasonLookupId = reasonId, StatusCodeId = initial.StatusCodeId, Note = note, RequestedBy = tenant.UserId, RequestedAtUtc = clock.UtcNow, OutsideWindow = outside,
            };
            ret.Lines.AddRange(lines);
            db.SalesReturns.Add(ret);
            await db.SaveGuardedAsync(Concurrency, ct2);
            await statuses.TransitionAsync(ReturnCodes.StatusDomain, ReturnCodes.EntityType, ret.SalesReturnId, null, initial.InternalCode, null, ct2);
            return ret.PublicId;
        }, ct);
        return await GetAsync(publicId, ct);
    }

    public async Task<SalesReturnDto> AuthorizeAsync(Guid publicId, string? note, CancellationToken ct)
    {
        var n = NormalizeNote(note);
        await db.RunInTransactionAsync(async ct2 =>
        {
            var r = await db.SalesReturns.AsTracking().FirstOrDefaultAsync(x => x.PublicId == publicId, ct2) ?? throw new NotFoundException("Devolución", feminine: true);
            if ((await StatusMapAsync(ct2))[r.StatusCodeId].Code != SalesReturnStatuses.Requested) throw new StatusRuleException(SalesReturnRules.AuthorizeNeedsRequested);
            var to = await statuses.TransitionAsync(ReturnCodes.StatusDomain, ReturnCodes.EntityType, r.SalesReturnId, r.StatusCodeId, SalesReturnStatuses.Authorized, n, ct2);
            (r.StatusCodeId, r.AuthorizedBy, r.AuthorizedAtUtc, r.AuthorizationNote) = (to.StatusCodeId, tenant.UserId, clock.UtcNow, n);
            await db.SaveGuardedAsync(Concurrency, ct2);
        }, ct);
        return await GetAsync(publicId, ct);
    }

    public async Task<SalesReturnDto> RejectAsync(Guid publicId, string? reason, CancellationToken ct)
    {
        var why = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        if (why is null) throw new ValidationException("reason", SalesReturnRules.RejectReasonRequired);
        if (why.Length > SalesReturnRules.MaxNoteLength) throw new ValidationException("reason", SalesReturnRules.NoteTooLong);
        await db.RunInTransactionAsync(async ct2 =>
        {
            var r = await db.SalesReturns.AsTracking().FirstOrDefaultAsync(x => x.PublicId == publicId, ct2) ?? throw new NotFoundException("Devolución", feminine: true);
            if ((await StatusMapAsync(ct2))[r.StatusCodeId].Code != SalesReturnStatuses.Requested) throw new StatusRuleException(SalesReturnRules.RejectNeedsRequested);
            var to = await statuses.TransitionAsync(ReturnCodes.StatusDomain, ReturnCodes.EntityType, r.SalesReturnId, r.StatusCodeId, SalesReturnStatuses.Rejected, why, ct2);
            (r.StatusCodeId, r.AuthorizationNote) = (to.StatusCodeId, why);
            await db.SaveGuardedAsync(Concurrency, ct2);
        }, ct);
        return await GetAsync(publicId, ct);
    }

    /// <summary>Cancela una devolución solicitada o autorizada que todavía no se ha recibido.</summary>
    public async Task<SalesReturnDto> CancelAsync(Guid publicId, string? comment, CancellationToken ct)
    {
        var why = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();
        await db.RunInTransactionAsync(async ct2 =>
        {
            var r = await db.SalesReturns.AsTracking().Include(x => x.Lines).FirstOrDefaultAsync(x => x.PublicId == publicId, ct2) ?? throw new NotFoundException("Devolución", feminine: true);
            var code = (await StatusMapAsync(ct2))[r.StatusCodeId].Code;
            if (code is not (SalesReturnStatuses.Requested or SalesReturnStatuses.Authorized) || r.Lines.Any(l => l.QtyReceived > 0m)) throw new StatusRuleException(SalesReturnRules.CancelNeedsOpen);
            var to = await statuses.TransitionAsync(ReturnCodes.StatusDomain, ReturnCodes.EntityType, r.SalesReturnId, r.StatusCodeId, SalesReturnStatuses.Cancelled, why, ct2);
            r.StatusCodeId = to.StatusCodeId;
            await db.SaveGuardedAsync(Concurrency, ct2);
        }, ct);
        return await GetAsync(publicId, ct);
    }

    // ================================================================ recibir

    /// <summary>
    /// Recibe lo que llegó (en partes): crea y confirma un recibo tipo RETURN con lo bueno y lo dañado por línea. Lo recibido (bueno + dañado) cuenta como devuelto en la línea de la orden de venta.
    /// La devolución pasa a RECIBIDA con la primera recepción; con CloseRest lo que no llegó se da por no devuelto.
    /// </summary>
    public async Task<SalesReturnDto> ReceiveAsync(Guid publicId, SalesReturnReceiveRequest? req, CancellationToken ct)
    {
        if (req is null) throw new ValidationException("body", "El cuerpo de la solicitud es obligatorio.");
        if (req.Lines is null || req.Lines.Count == 0) throw new ValidationException("lines", SalesReturnRules.ReceiveLinesRequired);
        await db.RunInTransactionAsync(async ct2 =>
        {
            var r = await db.SalesReturns.AsTracking().Include(x => x.Lines).FirstOrDefaultAsync(x => x.PublicId == publicId, ct2) ?? throw new NotFoundException("Devolución", feminine: true);
            var code = (await StatusMapAsync(ct2))[r.StatusCodeId].Code;
            if (code is not (SalesReturnStatuses.Authorized or SalesReturnStatuses.Received)) throw new StatusRuleException(SalesReturnRules.ReceiveNeedsAuthorized);
            var products = await db.Set<Product>().AsNoTracking().Where(p => r.Lines.Select(l => l.ProductId).Contains(p.ProductId)).ToDictionaryAsync(p => p.ProductId, ct2);
            var orderLines = await db.Set<SalesOrderLine>().AsTracking().Where(l => r.Lines.Select(x => x.SalesOrderLineId).Contains(l.SalesOrderLineId)).ToDictionaryAsync(l => l.SalesOrderLineId, ct2);
            var warehouse = await db.Set<Warehouse>().AsNoTracking().FirstAsync(w => w.WarehouseId == r.WarehouseId, ct2);

            var receiptLines = new List<ReceiptLineRequest>();
            var applied = new List<(SalesReturnLine Line, decimal Good, decimal Damaged)>();
            var seen = new HashSet<int>();
            for (var i = 0; i < req.Lines.Count; i++)
            {
                var x = req.Lines[i];
                var line = r.Lines.FirstOrDefault(l => l.SalesReturnLineId == x.LineId) ?? throw new ValidationException($"lines[{i}]", SalesReturnRules.ReceiveLineNotFound);
                if (!seen.Add(line.SalesReturnLineId)) throw new ValidationException($"lines[{i}]", SalesReturnRules.ReceiveLineRepeated);
                var good = x.GoodQty ?? 0m;
                var damaged = x.DamagedQty ?? 0m;
                if (good < 0m || damaged < 0m || Math.Round(good, 3) != good || Math.Round(damaged, 3) != damaged || good + damaged <= 0m) throw new ValidationException($"lines[{i}]", SalesReturnRules.ReceiveQuantityInvalid);
                var pend = SalesReturnRules.PendingToReceive(line.QtyRequested, line.QtyReceived);
                var sku = products[line.ProductId].Sku;
                if (good + damaged > pend) throw new StatusRuleException(SalesReturnRules.ExceedsPending(sku, pend));
                if (damaged > 0m && string.IsNullOrWhiteSpace(x.DamageNote)) throw new ValidationException($"lines[{i}].damageNote", SalesReturnRules.DamagedNeedsNote);
                receiptLines.Add(new ReceiptLineRequest(products[line.ProductId].PublicId, good + damaged, x.Lot, null, null, null, x.TargetBinId, null, damaged > 0m ? damaged : null,
                    damaged > 0m ? DamageCauses.CustomerReturn : null, damaged > 0m ? x.DamageNote!.Trim() : null, null, null, damaged > 0m ? x.DamageDiscard : null, null));
                applied.Add((line, good, damaged));
            }

            // el recibo tipo RETURN (se crea y se confirma en una sola transacción: mueve el inventario, genera lo dañado y las tareas de acomodo)
            var receipt = await receipts.CreateAsync(new ReceiptCreateRequest(WarehousePublicId: warehouse.PublicId, Type: ReceiptTypes.Return, Lines: receiptLines, Confirm: true, Reference: r.Number), ct2);

            // el servicio de recibos limpia lo que tenía rastreado: se vuelve a cargar lo de la devolución antes de anotar lo recibido
            var returnId = r.SalesReturnId;
            var received = applied.Select(x => (x.Line.SalesReturnLineId, x.Line.SalesOrderLineId, x.Good, x.Damaged)).ToList();
            r = await db.SalesReturns.AsTracking().Include(x => x.Lines).FirstAsync(x => x.SalesReturnId == returnId, ct2);
            orderLines = await db.Set<SalesOrderLine>().AsTracking().Where(l => r.Lines.Select(x => x.SalesOrderLineId).Contains(l.SalesOrderLineId)).ToDictionaryAsync(l => l.SalesOrderLineId, ct2);
            var header = await db.Set<ReceiptHeader>().AsTracking().FirstAsync(h => h.PublicId == receipt.Header.PublicId, ct2);
            header.SalesReturnId = returnId;

            foreach (var (lineId, orderLineId, good, damaged) in received)
            {
                var line = r.Lines.First(l => l.SalesReturnLineId == lineId);
                line.QtyReceived += good + damaged;
                line.QtyDamaged += damaged;
                orderLines[orderLineId].QtyReturned += good + damaged;
            }
            if (req.CloseRest == true)
                foreach (var l in r.Lines) l.QtyRequested = l.QtyReceived;
            if (code == SalesReturnStatuses.Authorized)
            {
                var to = await statuses.TransitionAsync(ReturnCodes.StatusDomain, ReturnCodes.EntityType, r.SalesReturnId, r.StatusCodeId, SalesReturnStatuses.Received, null, ct2);
                r.StatusCodeId = to.StatusCodeId;
            }
            r.ReceivedAtUtc = clock.UtcNow;
            await db.SaveGuardedAsync(Concurrency, ct2);
        }, ct);
        return await GetAsync(publicId, ct);
    }

    // ================================================================ acreditar

    /// <summary>
    /// Crea las notas de crédito en borrador de lo recibido que sí se facturó (una por factura emitida, vencida o pagada; la devolución pasa a ACREDITADA). Lo recibido que no se había facturado
    /// simplemente deja de ser facturable (QtyReturned). Se acredita al monto unitario de la línea facturada.
    /// </summary>
    public async Task<SalesReturnDto> CreditAsync(Guid publicId, CancellationToken ct)
    {
        await db.RunInTransactionAsync(async ct2 =>
        {
            var r = await db.SalesReturns.AsTracking().Include(x => x.Lines).FirstOrDefaultAsync(x => x.PublicId == publicId, ct2) ?? throw new NotFoundException("Devolución", feminine: true);
            if ((await StatusMapAsync(ct2))[r.StatusCodeId].Code != SalesReturnStatuses.Received) throw new StatusRuleException(SalesReturnRules.CreditNeedsReceived);
            var voidId = await db.StatusCodes.AsNoTracking().Where(s => s.Entity == BillingCodes.InvoiceStatusDomain && s.InternalCode == InvoiceStatuses.Void).Select(s => s.StatusCodeId).FirstAsync(ct2);
            var draftId = await db.StatusCodes.AsNoTracking().Where(s => s.Entity == BillingCodes.InvoiceStatusDomain && s.InternalCode == InvoiceStatuses.Draft).Select(s => s.StatusCodeId).FirstAsync(ct2);
            var orderLines = await db.Set<SalesOrderLine>().AsTracking().Where(l => r.Lines.Select(x => x.SalesOrderLineId).Contains(l.SalesOrderLineId)).ToDictionaryAsync(l => l.SalesOrderLineId, ct2);

            // lo que se acredita por factura: (línea de factura → cantidad)
            var byInvoice = new Dictionary<int, List<(int InvoiceLineId, decimal Qty)>>();
            var creditedByLine = new Dictionary<int, decimal>();
            foreach (var line in r.Lines)
            {
                var toCredit = Math.Round(line.QtyReceived - line.QtyCredited, 3);
                if (toCredit <= 0m) continue;
                var invoiced = await (from il in db.Set<InvoiceLine>().AsNoTracking()
                                      join i in db.Set<Invoice>().AsNoTracking() on il.InvoiceId equals i.InvoiceId
                                      where il.SalesOrderLineId == line.SalesOrderLineId && i.StatusCodeId != voidId && i.StatusCodeId != draftId
                                      orderby il.InvoiceLineId
                                      select new { il.InvoiceLineId, il.InvoiceId, il.Quantity }).ToListAsync(ct2);
                var remaining = toCredit;
                foreach (var il in invoiced)
                {
                    if (remaining <= 0m) break;
                    var take = Math.Min(remaining, il.Quantity);
                    if (take <= 0m) continue;
                    if (!byInvoice.TryGetValue(il.InvoiceId, out var list)) byInvoice[il.InvoiceId] = list = new List<(int, decimal)>();
                    list.Add((il.InvoiceLineId, take));
                    creditedByLine[line.SalesReturnLineId] = creditedByLine.GetValueOrDefault(line.SalesReturnLineId) + take;
                    remaining -= take;
                }
            }
            if (byInvoice.Count == 0) throw new StatusRuleException(SalesReturnRules.NothingToCredit);
            var returnId = r.SalesReturnId;

            foreach (var (invoiceId, list) in byInvoice.OrderBy(kv => kv.Key))
            {
                var invoicePublicId = await db.Set<Invoice>().AsNoTracking().Where(i => i.InvoiceId == invoiceId).Select(i => i.PublicId).FirstAsync(ct2);
                var note = await credits.CreateAsync(new CreditNoteRequest(InvoicePublicId: invoicePublicId, Reason: CreditReasons.Return, Note: $"Devolución {r.Number}",
                    Lines: list.Select(x => new CreditNoteLineRequest(InvoiceLineId: x.InvoiceLineId, Quantity: x.Qty)).ToList()), ct2);
                // se guarda en el acto: el servicio de notas puede limpiar lo rastreado (en una transacción anidada de pruebas) y lo anotado después se perdería
                var tracked = await db.CreditNotes.AsTracking().FirstAsync(n => n.PublicId == note.PublicId, ct2);
                tracked.SalesReturnId = returnId;
                await db.SaveGuardedAsync(Concurrency, ct2);
            }
            r = await db.SalesReturns.AsTracking().Include(x => x.Lines).FirstAsync(x => x.SalesReturnId == returnId, ct2);
            orderLines = await db.Set<SalesOrderLine>().AsTracking().Where(l => r.Lines.Select(x => x.SalesOrderLineId).Contains(l.SalesOrderLineId)).ToDictionaryAsync(l => l.SalesOrderLineId, ct2);
            foreach (var line in r.Lines)
                if (creditedByLine.TryGetValue(line.SalesReturnLineId, out var q))
                {
                    line.QtyCredited += q;
                    orderLines[line.SalesOrderLineId].QtyCredited += q;
                }
            var to = await statuses.TransitionAsync(ReturnCodes.StatusDomain, ReturnCodes.EntityType, r.SalesReturnId, r.StatusCodeId, SalesReturnStatuses.Credited, null, ct2);
            r.StatusCodeId = to.StatusCodeId;
            await db.SaveGuardedAsync(Concurrency, ct2);
        }, ct);
        return await GetAsync(publicId, ct);
    }

    // ================================================================ ayudas

    private static string? NormalizeNote(string? note)
    {
        var n = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (n is { Length: > SalesReturnRules.MaxNoteLength }) throw new ValidationException("note", SalesReturnRules.NoteTooLong);
        return n;
    }

    private async Task<int> RequireReasonAsync(string? reason, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(reason)) throw new ValidationException("reason", SalesReturnRules.ReasonRequired);
        var code = reason.Trim().ToUpperInvariant();
        if (!ReturnReasons.All.Contains(code)) throw new ValidationException("reason", SalesReturnRules.ReasonInvalid);
        return await lookups.TryGetIdAsync(ReturnCodes.ReasonDomain, code, ct) ?? throw new ValidationException("reason", SalesReturnRules.ReasonInvalid);
    }
}
