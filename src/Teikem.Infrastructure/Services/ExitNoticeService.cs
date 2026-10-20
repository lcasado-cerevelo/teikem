using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Clients;
using Teikem.Domain.Common;
using Teikem.Domain.Constants;
using Teikem.Domain.ExitNotices;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Notifications;
using Teikem.Infrastructure.Orders;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Wms;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 143 (2026-10-20) — aviso de salida (SAL-#####): el dueño de la mercancía (un cliente de almacén) avisa qué quiere que el almacén saque. No es una venta: sin precio,
/// impuesto ni crédito. Se arma sobre el mismo patrón que las órdenes de venta (reserva por saldo en el ledger, pendiente por línea, despacho que crea una recolección), con estas reglas:
/// - Solo productos activos del cliente dueño (OwnerMismatch), sin series ni kits en esta primera versión; el lote es opcional.
/// - Nace SOLICITADO y reserva en el acto lo que haya con la rotación de la compañía (FEFO/FIFO/posición); lo que falte queda PENDIENTE (backorder) y el trabajo
///   EXIT_NOTICE_BACKORDER_SCAN lo va reservando, el aviso más antiguo primero, cuando entra mercancía. Nunca se rechaza por falta de existencia.
/// - Para quien opera la reserva es «por producto y lote»; por dentro el ledger reserva por saldo (posición), y al despachar se puede sacar de otra posición del mismo lote
///   (la reserva se mueve sola) salvo que la rotación esté en «Obligar».
/// - Nunca DELETE: se cancela (libera lo reservado). El número SAL-##### se toma al final de la transacción.
/// </summary>
public sealed partial class ExitNoticeService(TeikemDbContext db, ITenantContext tenant, StatusService statuses, INumberSequenceService numbers, ITenantClock clock,
    InventoryLedger ledger, ILookupCache lookups, INotificationPublisher? notifications = null, ValuationService? valuation = null) : IExitNoticeDispatchReversal
{
    public const int MaxPageSize = 200;
    private const int MaxReserveAttempts = 3;
    private TenantContext T => (TenantContext)tenant;

    private sealed record StatusInfo(string Code, string Label, string Color);

    private static NotFoundException NotFound() => new("Aviso de salida");

    // ---------------------------------------------------------------- consulta

    public async Task<PagedResult<ExitNoticeListItemDto>> ListAsync(string? status, Guid? clientPublicId, string? q, bool backorderOnly, DateOnly? from, DateOnly? to,
        int skip, int take, CancellationToken ct)
    {
        skip = Math.Max(0, skip);
        take = Math.Clamp(take <= 0 ? 50 : take, 1, MaxPageSize);
        var map = await StatusMapAsync(ct);
        var query = db.Set<ExitNotice>().AsQueryable();
        if (!string.IsNullOrWhiteSpace(status))
        {
            var code = status.Trim().ToUpperInvariant();
            var ids = map.Where(kv => kv.Value.Code == code).Select(kv => kv.Key).ToList();
            query = query.Where(o => ids.Contains(o.StatusCodeId));
        }
        if (clientPublicId is Guid cp) query = query.Where(o => db.Set<Client>().Any(c => c.ClientId == o.ClientId && c.PublicId == cp));
        if (from is DateOnly f) query = query.Where(o => o.RequestedDate >= f);
        if (to is DateOnly t2) query = query.Where(o => o.RequestedDate <= t2);
        if (backorderOnly) query = query.Where(o => db.Set<ExitNoticeLine>().Any(l => l.ExitNoticeId == o.ExitNoticeId && l.QtyBackordered > 0 && !l.IsClosed));
        if (!string.IsNullOrWhiteSpace(q))
        {
            var s = q.Trim();
            query = query.Where(o => o.Number.Contains(s) || (o.Reference != null && o.Reference.Contains(s)) || (o.Destination != null && o.Destination.Contains(s))
                || db.Set<Client>().Any(c => c.ClientId == o.ClientId && (c.Name.Contains(s) || c.Code.Contains(s)))
                || db.Set<ExitNoticeLine>().Any(l => l.ExitNoticeId == o.ExitNoticeId && db.Set<Product>().Any(p => p.ProductId == l.ProductId && (p.Sku.Contains(s) || p.Name.Contains(s)))));
        }
        var total = await query.CountAsync(ct);
        var notices = await query.OrderByDescending(o => o.ExitNoticeId).Skip(skip).Take(take).ToListAsync(ct);
        return new PagedResult<ExitNoticeListItemDto>(await ToListItemsAsync(notices, map, ct), total, skip, take);
    }

    public async Task<ExitNoticeDto> GetAsync(Guid publicId, CancellationToken ct)
    {
        var notice = await db.Set<ExitNotice>().Include(o => o.Lines).FirstOrDefaultAsync(o => o.PublicId == publicId, ct) ?? throw NotFound();
        return (await ToDtosAsync(new[] { notice }, ct))[0];
    }

    // ---------------------------------------------------------------- alta (reserva en el acto; lo que falte queda pendiente)

    private sealed record PlannedLine(Product Product, decimal Quantity, int? LotId);

    public async Task<ExitNoticeDto> CreateAsync(ExitNoticeRequest req, CancellationToken ct)
    {
        if (req is null) throw new ValidationException("body", "El cuerpo de la solicitud es obligatorio.");
        var tenantId = T.RequireTenantId();
        var client = await ResolveClientAsync(req.ClientPublicId, ct);
        var warehouse = await ResolveWarehouseAsync(req.WarehousePublicId, ct);
        var (reference, refError) = ExitNoticeRules.NormalizeText(req.Reference, ExitNoticeRules.MaxReferenceLength, ExitNoticeRules.ReferenceTooLong);
        if (refError is not null) throw new ValidationException("reference", refError);
        var (destination, destError) = ExitNoticeRules.NormalizeText(req.Destination, ExitNoticeRules.MaxDestinationLength, ExitNoticeRules.DestinationTooLong);
        if (destError is not null) throw new ValidationException("destination", destError);
        var (notes, notesError) = ExitNoticeRules.NormalizeText(req.Notes, ExitNoticeRules.MaxNotesLength, ExitNoticeRules.NotesTooLong);
        if (notesError is not null) throw new ValidationException("notes", notesError);
        var planned = await PlanLinesAsync(req.Lines, client, ct);

        await numbers.EnsureAsync(ExitNoticeCodes.NumberKind, null, ct);
        Guid createdId = Guid.Empty;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                createdId = await db.RunInTransactionAsync(async ct2 =>
                {
                    var initial = await statuses.GetInitialAsync(ExitNoticeCodes.StatusDomain, ct2);
                    var seq = await numbers.NextAsync(ExitNoticeCodes.NumberKind, null, ct2);
                    var notice = new ExitNotice
                    {
                        PublicId = Guid.NewGuid(), TenantId = tenantId, Number = NumberFormat.Resolve(ExitNoticeCodes.NumberPattern, seq), WarehouseId = warehouse.WarehouseId,
                        ClientId = client.ClientId, Reference = reference, Destination = destination, RequestedBy = tenant.UserId, RequestedDate = clock.Today,
                        RequiredDate = req.RequiredDate, Notes = notes, StatusCodeId = initial.StatusCodeId, RequestedAtUtc = DateTime.UtcNow,
                    };
                    var no = 0;
                    foreach (var p in planned)
                        notice.Lines.Add(new ExitNoticeLine { TenantId = tenantId, LineNo = ++no, ProductId = p.Product.ProductId, LotId = p.LotId, QtyOrdered = p.Quantity, QtyBackordered = p.Quantity });
                    db.Set<ExitNotice>().Add(notice);
                    await db.SaveGuardedAsync(ExitNoticeRules.NumberTaken, ct2);

                    // reserva lo que haya con la rotación; lo que falte queda pendiente (nunca se rechaza por falta de existencia)
                    var plan = await PlanAsync(notice.WarehouseId, notice.Lines.Select(l => new Want(l.ExitNoticeLineId, l.ProductId, l.LotId, l.QtyOrdered)).ToList(), ct2);
                    var reservations = new List<StockReservation>();
                    foreach (var l in notice.Lines)
                    {
                        var a = plan[l.ExitNoticeLineId];
                        l.QtyReserved = a.Allocated;
                        l.QtyBackordered = a.Shortage;
                        AddReservations(notice, l, a.Allocations, reservations);
                    }
                    await ledger.ReserveAsync(reservations, ct2);   // el saldo se bloquea y se vuelve a verificar: una carrera lanza insufficient_stock y se reintenta
                    var born = await statuses.TransitionAsync(ExitNoticeCodes.StatusDomain, ExitNoticeCodes.EntityType, notice.ExitNoticeId, null, initial.InternalCode, null, ct2);
                    notice.StatusCodeId = born.StatusCodeId;
                    await NotifyRequestedAsync(notice, ct2);
                    await db.SaveGuardedAsync(ExitNoticeRules.Concurrency, ct2);
                    return notice.PublicId;
                }, ct);
                break;
            }
            catch (InsufficientStockException) when (attempt < MaxReserveAttempts)
            {
                db.ChangeTracker.Clear();   // otro documento reservó entre la foto y el candado: se vuelve a calcular con lo que quedó
            }
        }
        return await GetAsync(createdId, ct);
    }

    /// <summary>Valida las líneas: producto activo del cliente dueño, sin series ni kits, sin repetir, cantidad válida y lote del producto.</summary>
    private async Task<List<PlannedLine>> PlanLinesAsync(IReadOnlyList<ExitNoticeLineRequest>? lines, Client client, CancellationToken ct)
    {
        if (lines is null || lines.Count == 0) throw new ValidationException("lines", ExitNoticeRules.LinesRequired);
        if (lines.Count > ExitNoticeRules.MaxLines) throw new ValidationException("lines", ExitNoticeRules.TooManyLines);
        var ids = lines.Where(l => l.ProductPublicId is not null).Select(l => l.ProductPublicId!.Value).Distinct().ToList();
        var products = await db.Set<Product>().AsNoTracking().Where(p => ids.Contains(p.PublicId)).ToDictionaryAsync(p => p.PublicId, ct);
        var serialId = await lookups.GetIdAsync(LookupDomains.TrackingType, TrackingTypes.Serial, ct);
        var noneId = await lookups.GetIdAsync(LookupDomains.TrackingType, TrackingTypes.None, ct);
        var kitTypeId = await lookups.TryGetIdAsync(KitCodes.ProductTypeDomain, ProductTypes.Kit, ct);
        var seen = new HashSet<int>();
        var result = new List<PlannedLine>();
        for (var i = 0; i < lines.Count; i++)
        {
            var l = lines[i];
            if (l.ProductPublicId is null) throw new ValidationException($"lines[{i}].productPublicId", ExitNoticeRules.ProductRequired);
            var product = products.GetValueOrDefault(l.ProductPublicId.Value) ?? throw new NotFoundException("Producto");
            if (!product.IsActive) throw new StatusRuleException(ExitNoticeRules.ProductInactive);
            if (product.ClientId != client.ClientId) throw new ValidationException($"lines[{i}].productPublicId", ExitNoticeRules.OwnerMismatch(product.Sku));
            if (product.TrackingTypeLookupId == serialId) throw new StatusRuleException(ExitNoticeRules.SerialNotSupported);
            if (kitTypeId is not null && product.ProductTypeLookupId == kitTypeId) throw new StatusRuleException(ExitNoticeRules.KitNotSupported);
            if (!seen.Add(product.ProductId)) throw new ValidationException($"lines[{i}].productPublicId", ExitNoticeRules.ProductRepeated);
            var qtyError = ExitNoticeRules.QuantityError(l.Quantity);
            if (qtyError is not null) throw new ValidationException($"lines[{i}].quantity", qtyError);
            if (l.LotId is int lotId)
            {
                if (product.TrackingTypeLookupId == noneId) throw new ValidationException($"lines[{i}].lotId", ExitNoticeRules.LotNotTracked(product.Sku));
                if (!await db.Set<InventoryLot>().AsNoTracking().AnyAsync(x => x.LotId == lotId && x.ProductId == product.ProductId, ct)) throw new NotFoundException("Lote");
            }
            result.Add(new PlannedLine(product, l.Quantity!.Value, l.LotId));
        }
        return result;
    }

    // ---------------------------------------------------------------- piezas

    private async Task<Client> ResolveClientAsync(Guid? publicId, CancellationToken ct)
    {
        if (publicId is null) throw new ValidationException("clientPublicId", ExitNoticeRules.ClientRequired);
        var client = await db.Set<Client>().AsNoTracking().FirstOrDefaultAsync(c => c.PublicId == publicId, ct) ?? throw new NotFoundException("Cliente");
        if (!client.IsActive) throw new StatusRuleException(ExitNoticeRules.ClientInactive);
        return client;
    }

    private async Task<Warehouse> ResolveWarehouseAsync(Guid? publicId, CancellationToken ct)
    {
        if (publicId is Guid id)
        {
            var w = await db.Set<Warehouse>().AsNoTracking().FirstOrDefaultAsync(x => x.PublicId == id, ct) ?? throw new NotFoundException("Almacén");
            if (!w.IsActive) throw new StatusRuleException(ExitNoticeRules.WarehouseInactive);
            return w;
        }
        var active = await db.Set<Warehouse>().AsNoTracking().Where(w => w.IsActive).OrderBy(w => w.WarehouseId).Take(2).ToListAsync(ct);
        if (active.Count == 0) throw new StatusRuleException(ExitNoticeRules.NoActiveWarehouse);
        if (active.Count > 1) throw new ValidationException("warehousePublicId", ExitNoticeRules.WarehouseRequired);
        return active[0];
    }

    private async Task<Dictionary<int, StatusInfo>> StatusMapAsync(CancellationToken ct)
        => (await db.StatusCodes.AsNoTracking().Where(s => s.Entity == ExitNoticeCodes.StatusDomain).ToListAsync(ct))
            .ToDictionary(s => s.StatusCodeId, s => new StatusInfo(s.InternalCode, MultilingualText.Resolve(s.LabelJson, T.Lang) ?? s.InternalCode, s.ColorHex ?? "#9CA3AF"));

    private async Task<Dictionary<int, string>> UserNamesAsync(IEnumerable<int> ids, CancellationToken ct)
    {
        var list = ids.Distinct().ToList();
        if (list.Count == 0) return new();
        return await db.Users.AsNoTracking().Where(u => list.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName ?? u.Email ?? string.Empty, ct);
    }

    private async Task<List<ExitNoticeListItemDto>> ToListItemsAsync(IReadOnlyList<ExitNotice> notices, Dictionary<int, StatusInfo> map, CancellationToken ct)
    {
        var ids = notices.Select(o => o.ExitNoticeId).ToList();
        var stats = await db.Set<ExitNoticeLine>().Where(l => ids.Contains(l.ExitNoticeId)).GroupBy(l => l.ExitNoticeId)
            .Select(g => new { Id = g.Key, Count = g.Count(), Ordered = g.Sum(l => l.QtyOrdered), Shipped = g.Sum(l => l.QtyShipped), Back = g.Any(l => l.QtyBackordered > 0 && !l.IsClosed) })
            .ToDictionaryAsync(x => x.Id, ct);
        var clientIds = notices.Select(o => o.ClientId).Distinct().ToList();
        var whIds = notices.Select(o => o.WarehouseId).Distinct().ToList();
        var clients = await db.Set<Client>().Where(c => clientIds.Contains(c.ClientId)).ToDictionaryAsync(c => c.ClientId, ct);
        var warehouses = await db.Set<Warehouse>().Where(w => whIds.Contains(w.WarehouseId)).ToDictionaryAsync(w => w.WarehouseId, ct);
        var users = await UserNamesAsync(notices.Where(o => o.RequestedBy is not null).Select(o => o.RequestedBy!.Value), ct);
        return notices.Select(o =>
        {
            var s = map[o.StatusCodeId];
            var c = clients[o.ClientId];
            var w = warehouses[o.WarehouseId];
            var st = stats.GetValueOrDefault(o.ExitNoticeId);
            return new ExitNoticeListItemDto(o.PublicId, o.Number, s.Code, s.Label, s.Color, c.PublicId, c.Name, w.PublicId, w.Code, w.Name, o.Reference, o.Destination,
                o.RequestedDate, o.RequiredDate, st?.Count ?? 0, st?.Ordered ?? 0m, st?.Shipped ?? 0m, st?.Back ?? false, o.RequestedBy is int u ? users.GetValueOrDefault(u) : null, o.CreatedAtUtc);
        }).ToList();
    }

    private async Task<IReadOnlyList<ExitNoticeDto>> ToDtosAsync(IReadOnlyList<ExitNotice> notices, CancellationToken ct)
    {
        var map = await StatusMapAsync(ct);
        var productIds = notices.SelectMany(o => o.Lines.Select(l => l.ProductId)).Distinct().ToList();
        var products = await db.Set<Product>().Where(p => productIds.Contains(p.ProductId)).ToDictionaryAsync(p => p.ProductId, ct);
        var tracking = new Dictionary<int, string>();
        foreach (var id in products.Values.Select(p => p.TrackingTypeLookupId).Distinct())
            tracking[id] = (await lookups.GetAsync(id, ct))?.InternalCode ?? TrackingTypes.None;
        var lotIds = notices.SelectMany(o => o.Lines.Where(l => l.LotId != null).Select(l => l.LotId!.Value)).Distinct().ToList();
        var lots = await db.Set<InventoryLot>().Where(l => lotIds.Contains(l.LotId)).ToDictionaryAsync(l => l.LotId, l => l.LotNumber, ct);
        var clientIds = notices.Select(o => o.ClientId).Distinct().ToList();
        var whIds = notices.Select(o => o.WarehouseId).Distinct().ToList();
        var clients = await db.Set<Client>().Where(c => clientIds.Contains(c.ClientId)).ToDictionaryAsync(c => c.ClientId, ct);
        var warehouses = await db.Set<Warehouse>().Where(w => whIds.Contains(w.WarehouseId)).ToDictionaryAsync(w => w.WarehouseId, ct);
        var users = await UserNamesAsync(notices.Where(o => o.RequestedBy is not null).Select(o => o.RequestedBy!.Value), ct);
        var dispatchInfo = await DispatchesOfAsync(notices.Select(o => o.ExitNoticeId).ToList(), ct);
        return notices.Select(o =>
        {
            var s = map[o.StatusCodeId];
            var c = clients[o.ClientId];
            var w = warehouses[o.WarehouseId];
            var lines = o.Lines.OrderBy(l => l.LineNo).Select(l =>
            {
                var p = products[l.ProductId];
                return new ExitNoticeLineDto(l.ExitNoticeLineId, l.LineNo, p.PublicId, p.Sku, p.Name, tracking.GetValueOrDefault(p.TrackingTypeLookupId, TrackingTypes.None),
                    l.LotId, l.LotId is int li ? lots.GetValueOrDefault(li) : null, l.QtyOrdered, l.QtyReserved, l.QtyBackordered, l.QtyShipped, l.IsClosed);
            }).ToList();
            var open = s.Code is ExitNoticeStatuses.Requested or ExitNoticeStatuses.Partial;
            var anyShipped = o.Lines.Any(l => l.QtyShipped > 0m);
            return new ExitNoticeDto(o.PublicId, o.Number, s.Code, s.Label, s.Color, c.PublicId, c.Name, w.PublicId, w.Code, w.Name, o.Reference, o.Destination, o.RequestedDate,
                o.RequiredDate, o.Notes, o.Lines.Any(l => ExitNoticeRules.IsBackordered(l.QtyBackordered, l.IsClosed)), s.Code == ExitNoticeStatuses.Requested && !anyShipped,
                s.Code == ExitNoticeStatuses.Requested && !anyShipped, open && o.Lines.Any(l => l.QtyReserved > 0m),
                o.RequestedBy is int u ? users.GetValueOrDefault(u) : null, o.RequestedAtUtc, o.CreatedAtUtc, Convert.ToBase64String(o.RowVersion ?? Array.Empty<byte>()), lines,
                dispatchInfo.GetValueOrDefault(o.ExitNoticeId) ?? new List<ExitNoticeDispatchInfoDto>());
        }).ToList();
    }

    // ---------------------------------------------------------------- avisos

    private static string Qty(decimal v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    private async Task NotifyAsync(string eventCode, ExitNotice notice, IReadOnlyDictionary<string, string?> extra, string? dedupKey, CancellationToken ct)
    {
        if (notifications is null) return;
        var client = await db.Set<Client>().AsNoTracking().Where(c => c.ClientId == notice.ClientId).Select(c => c.Name).FirstOrDefaultAsync(ct) ?? string.Empty;
        var warehouse = await db.Set<Warehouse>().AsNoTracking().Where(w => w.WarehouseId == notice.WarehouseId).Select(w => w.Name).FirstOrDefaultAsync(ct) ?? string.Empty;
        var values = new Dictionary<string, string?> { ["number"] = notice.Number, ["client"] = client, ["warehouse"] = warehouse, ["noticeId"] = notice.PublicId.ToString("N") };
        foreach (var kv in extra) values[kv.Key] = kv.Value;
        await notifications.PublishAsync(eventCode, values, dedupKey, ct);
    }

    private Task NotifyRequestedAsync(ExitNotice notice, CancellationToken ct)
    {
        var pending = notice.Lines.Count(l => l.QtyBackordered > 0m);
        var detail = pending == 0 ? "Quedó todo reservado." : $"{pending} línea(s) con pendiente por falta de existencia.";
        return NotifyAsync(ExitNoticeEventCodes.Requested, notice,
            new Dictionary<string, string?> { ["lineCount"] = notice.Lines.Count.ToString(CultureInfo.InvariantCulture), ["detail"] = detail }, null, ct);
    }
}
