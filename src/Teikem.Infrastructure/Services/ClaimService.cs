using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Common;
using Teikem.Domain.Orders;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 111 — faltantes y reclamaciones. Camino de una pieza faltante (dictado por Luis): «Por verificar» (lo marca el escaneo, Lote 110) → «Perdido» (MarkLost) → «Reclamada» (Create: la reclamación REC-##### junta las piezas perdidas de una
/// orden con su valor declarado y notas) → «Resuelta» (Resolve: pagada, rechazada o cerrada). Mutaciones con claims.manage, lecturas con claims.view. El estatus de la reclamación (Reclamada → Resuelta) va por StatusService. Una pieza que aparece
/// estando perdida o reclamada se atiende en el escaneo (ScanActionService): se quita de su reclamación y, si esta se queda sin piezas, se da de baja.
/// </summary>
public sealed class ClaimService(TeikemDbContext db, ITenantContext tenant, StatusService statuses, PartialBillingService partialBilling)
{
    private TenantContext Ctx => (TenantContext)tenant;

    // ================================================================ cola de faltantes

    /// <summary>Órdenes con piezas «Por verificar» o «Perdido» que todavía no están en una reclamación, más recientes primero.</summary>
    public async Task<PagedResult<MissingOrderDto>> ToVerifyAsync(string? search, int skip, int take, CancellationToken ct)
    {
        take = Math.Clamp(take <= 0 ? 25 : take, 1, 100);
        skip = Math.Max(0, skip);
        var q = db.OrderPieces.AsNoTracking().Where(p => p.IsActive && (p.MissingStatus == PieceMissing.ToVerify || p.MissingStatus == PieceMissing.Lost));
        var orderIds = q.Select(p => p.TransportOrderId).Distinct();
        var ordersQuery = db.TransportOrders.AsNoTracking().Where(o => orderIds.Contains(o.TransportOrderId));
        var s = search?.Trim();
        if (!string.IsNullOrEmpty(s))
            ordersQuery = ordersQuery.Where(o => o.OrderNumber.Contains(s) || (o.WaybillNumber != null && o.WaybillNumber.Contains(s)) || db.OrderPieces.Any(p => p.TransportOrderId == o.TransportOrderId && p.PackageNumber.Contains(s)));
        var total = await ordersQuery.CountAsync(ct);
        var orders = await ordersQuery.OrderByDescending(o => o.TransportOrderId).Skip(skip).Take(take).Select(o => new { o.TransportOrderId, o.PublicId, o.OrderNumber, o.WaybillNumber, o.ClientId, o.StatusCodeId }).ToListAsync(ct);
        var ids = orders.Select(o => o.TransportOrderId).ToList();
        var pieces = await db.OrderPieces.AsNoTracking().Where(p => p.IsActive && ids.Contains(p.TransportOrderId)).OrderBy(p => p.Sequence).ToListAsync(ct);
        var lines = await LineDescriptionsAsync(pieces.Select(p => p.CargoLineId).Distinct().ToList(), ct);
        var clients = await ClientNamesAsync(orders.Select(o => o.ClientId).Distinct().ToList(), ct);
        var statusRows = await db.StatusCodes.AsNoTracking().Where(sc => orders.Select(o => o.StatusCodeId).Contains(sc.StatusCodeId)).ToDictionaryAsync(sc => sc.StatusCodeId, ct);
        var items = orders.Select(o =>
        {
            var mine = pieces.Where(p => p.TransportOrderId == o.TransportOrderId).ToList();
            var missing = mine.Where(p => p.MissingStatus is PieceMissing.ToVerify or PieceMissing.Lost).Select(p => ToPiece(p, lines, null)).ToList();
            var st = statusRows.GetValueOrDefault(o.StatusCodeId);
            return new MissingOrderDto(o.PublicId, o.OrderNumber, o.WaybillNumber, clients.GetValueOrDefault(o.ClientId, string.Empty), st?.InternalCode ?? string.Empty,
                MultilingualText.Resolve(st?.LabelJson, tenant.Lang), mine.Count, missing);
        }).ToList();
        return new PagedResult<MissingOrderDto>(items, total, skip, take);
    }

    // ================================================================ perdido

    /// <summary>«Por verificar» → «Perdido». Sin números marca todas las «Por verificar» de la orden.</summary>
    public async Task<MissingOrderDto> MarkLostAsync(MarkLostRequest? req, CancellationToken ct)
    {
        var orderId = await OrderIdAsync(req?.OrderPublicId, ct);
        await db.RunInTransactionAsync(async ct2 =>
        {
            var all = await db.OrderPieces.Where(p => p.TransportOrderId == orderId && p.IsActive).ToListAsync(ct2);
            var chosen = Choose(all, req?.PieceNumbers, p => p.MissingStatus == PieceMissing.ToVerify, ClaimRules.PieceNotToVerifyMessage, ClaimRules.NoPiecesMessage);
            foreach (var p in chosen) { p.MissingStatus = PieceMissing.Lost; p.MissingAtUtc ??= DateTime.UtcNow; }
            await db.SaveChangesAsync(ct2);
            await partialBilling.AdjustAsync(orderId, ct2);   // Lote 111c: por entrega no se cobra lo que se perdió
        }, ct);
        return (await MissingOrderAsync(orderId, ct))!;
    }

    // ================================================================ reclamar

    /// <summary>«Perdido» → «Reclamada»: crea la reclamación REC-##### con las piezas perdidas de la orden. Sin números usa todas las «Perdido» de la orden.</summary>
    public async Task<ClaimDto> CreateAsync(ClaimCreateRequest? req, CancellationToken ct)
    {
        var orderId = await OrderIdAsync(req?.OrderPublicId, ct);
        if (req?.DeclaredValue is < 0) throw new ValidationException("declaredValue", ClaimRules.NegativeValueMessage);
        var notes = string.IsNullOrWhiteSpace(req?.Notes) ? null : req!.Notes!.Trim();
        if (notes is { Length: > ClaimRules.NotesMax }) throw new ValidationException("notes", ClaimRules.NotesTooLongMessage);

        var tenantId = Ctx.RequireTenantId();
        Guid publicId = default;
        await db.RunInTransactionAsync(async ct2 =>
        {
            var order = await db.TransportOrders.AsNoTracking().FirstAsync(o => o.TransportOrderId == orderId, ct2);
            var all = await db.OrderPieces.Where(p => p.TransportOrderId == orderId && p.IsActive).ToListAsync(ct2);
            var chosen = Choose(all, req?.PieceNumbers, p => p.MissingStatus == PieceMissing.Lost && p.ClaimId is null, ClaimRules.PieceNotLostMessage, ClaimRules.NoLostPiecesMessage);
            var lines = await LineDescriptionsAsync(chosen.Select(p => p.CargoLineId).Distinct().ToList(), ct2);
            var description = string.Join("; ", chosen.GroupBy(p => p.CargoLineId).Select(g => $"{g.Count()} × {lines.GetValueOrDefault(g.Key, "pieza")}"));
            if (description.Length > 500) description = description[..499] + "…";
            var initial = await statuses.GetInitialAsync(ClaimStatuses.Domain, ct2);
            var claim = new Claim
            {
                PublicId = Guid.NewGuid(), TenantId = tenantId, Number = "REC-" + Guid.NewGuid().ToString("N")[..20], TransportOrderId = orderId, ClientId = order.ClientId, StatusCodeId = initial.StatusCodeId,
                Description = description, DeclaredValue = req?.DeclaredValue, Notes = notes, IsActive = true, CreatedBy = tenant.UserId,
            };
            db.Claims.Add(claim);
            await db.SaveChangesAsync(ct2);
            claim.Number = ClaimRules.NumberOf(claim.ClaimId);
            await statuses.TransitionAsync(ClaimStatuses.Domain, ClaimStatuses.EntityType, claim.ClaimId, null, initial.InternalCode, null, ct2);
            foreach (var p in chosen) { p.MissingStatus = PieceMissing.Claimed; p.ClaimId = claim.ClaimId; }
            await db.SaveChangesAsync(ct2);
            publicId = claim.PublicId;
        }, ct);
        return await GetAsync(publicId, ct);
    }

    // ================================================================ resolver

    public async Task<ClaimDto> ResolveAsync(Guid publicId, ClaimResolveRequest? req, CancellationToken ct)
    {
        var resolution = ClaimResolutions.Normalize(req?.Resolution) ?? throw new ValidationException("resolution", ClaimRules.InvalidResolutionMessage);
        if (resolution == ClaimResolutions.Paid && req?.AmountPaid is null) throw new ValidationException("amountPaid", ClaimRules.AmountRequiredMessage);
        if (req?.AmountPaid is < 0) throw new ValidationException("amountPaid", ClaimRules.NegativeValueMessage);
        var notes = string.IsNullOrWhiteSpace(req?.Notes) ? null : req!.Notes!.Trim();
        if (notes is { Length: > ClaimRules.NotesMax }) throw new ValidationException("notes", ClaimRules.NotesTooLongMessage);

        await db.RunInTransactionAsync(async ct2 =>
        {
            var claim = await db.Claims.Include(c => c.Status).FirstOrDefaultAsync(c => c.PublicId == publicId && c.IsActive, ct2) ?? throw new NotFoundException("Reclamación", feminine: true);
            db.ApplyRowVersion(claim, req?.RowVersion);
            if (string.Equals(claim.Status!.InternalCode, ClaimStatuses.Resolved, StringComparison.OrdinalIgnoreCase)) throw new ConflictException(ClaimRules.AlreadyResolvedMessage);
            var to = await statuses.TransitionAsync(ClaimStatuses.Domain, ClaimStatuses.EntityType, claim.ClaimId, claim.StatusCodeId, ClaimStatuses.Resolved, null, ct2);
            claim.StatusCodeId = to.StatusCodeId;
            claim.Resolution = resolution;
            claim.AmountPaid = resolution == ClaimResolutions.Paid ? req!.AmountPaid : null;
            claim.ResolutionNotes = notes;
            claim.ResolvedAtUtc = DateTime.UtcNow;
            claim.ResolvedBy = tenant.UserId;
            foreach (var p in await db.OrderPieces.Where(p => p.ClaimId == claim.ClaimId).ToListAsync(ct2)) p.MissingStatus = PieceMissing.Resolved;
            await db.SaveGuardedAsync(DbExtensions.ConcurrencyMessage, ct2);
        }, ct);
        return await GetAsync(publicId, ct);
    }

    // ================================================================ lecturas

    public async Task<PagedResult<ClaimListItemDto>> ListAsync(string? status, string? search, int skip, int take, CancellationToken ct)
    {
        take = Math.Clamp(take <= 0 ? 25 : take, 1, 100);
        skip = Math.Max(0, skip);
        var q = db.Claims.AsNoTracking().Include(c => c.Status).Include(c => c.Order).Where(c => c.IsActive);
        if (!string.IsNullOrWhiteSpace(status)) { var st = status.Trim().ToUpperInvariant(); q = q.Where(c => c.Status!.InternalCode == st); }
        var s = search?.Trim();
        if (!string.IsNullOrEmpty(s)) q = q.Where(c => c.Number.Contains(s) || c.Description.Contains(s) || c.Order!.OrderNumber.Contains(s) || (c.Order!.WaybillNumber != null && c.Order.WaybillNumber.Contains(s)));
        var total = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(c => c.ClaimId).Skip(skip).Take(take).ToListAsync(ct);
        var clients = await ClientNamesAsync(rows.Select(c => c.ClientId).Distinct().ToList(), ct);
        var ids = rows.Select(c => c.ClaimId).ToList();
        var counts = await db.OrderPieces.AsNoTracking().Where(p => p.ClaimId != null && ids.Contains(p.ClaimId!.Value)).GroupBy(p => p.ClaimId!.Value).Select(g => new { Id = g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Id, x => x.N, ct);
        var items = rows.Select(c => new ClaimListItemDto(c.PublicId, c.Number, c.Status!.InternalCode, MultilingualText.Resolve(c.Status.LabelJson, tenant.Lang), c.Order!.PublicId, c.Order.OrderNumber, c.Order.WaybillNumber,
            clients.GetValueOrDefault(c.ClientId, string.Empty), c.Description, counts.GetValueOrDefault(c.ClaimId), c.DeclaredValue, c.Resolution, c.AmountPaid, c.CreatedAtUtc, c.ResolvedAtUtc)).ToList();
        return new PagedResult<ClaimListItemDto>(items, total, skip, take);
    }

    public async Task<ClaimDto> GetAsync(Guid publicId, CancellationToken ct)
    {
        var c = await db.Claims.AsNoTracking().Include(x => x.Status).Include(x => x.Order).FirstOrDefaultAsync(x => x.PublicId == publicId && x.IsActive, ct) ?? throw new NotFoundException("Reclamación", feminine: true);
        var pieces = await db.OrderPieces.AsNoTracking().Where(p => p.ClaimId == c.ClaimId).OrderBy(p => p.Sequence).ToListAsync(ct);
        var lines = await LineDescriptionsAsync(pieces.Select(p => p.CargoLineId).Distinct().ToList(), ct);
        var clients = await ClientNamesAsync(new List<int> { c.ClientId }, ct);
        return new ClaimDto(c.PublicId, c.Number, c.Status!.InternalCode, MultilingualText.Resolve(c.Status.LabelJson, tenant.Lang), c.Order!.PublicId, c.Order.OrderNumber, c.Order.WaybillNumber, clients.GetValueOrDefault(c.ClientId, string.Empty),
            c.Description, c.DeclaredValue, c.Notes, c.Resolution, c.AmountPaid, c.ResolutionNotes, c.CreatedAtUtc, c.ResolvedAtUtc, pieces.Select(p => ToPiece(p, lines, c.Number)).ToList(), c.RowVersion is null ? null : Convert.ToBase64String(c.RowVersion));
    }

    // ================================================================ utilidades

    private static string StatusLabel(string? status) => status switch
    {
        PieceMissing.ToVerify => "Por verificar",
        PieceMissing.Lost => "Perdido",
        PieceMissing.Claimed => "Reclamada",
        PieceMissing.Resolved => "Resuelta",
        _ => string.Empty,
    };

    private static MissingPieceDto ToPiece(OrderPiece p, IReadOnlyDictionary<int, string> lines, string? claimNumber)
        => new(p.PackageNumber, p.MissingStatus ?? string.Empty, StatusLabel(p.MissingStatus), p.MissingAtUtc, claimNumber, lines.GetValueOrDefault(p.CargoLineId, string.Empty));

    private static List<OrderPiece> Choose(List<OrderPiece> all, IList<string>? numbers, Func<OrderPiece, bool> eligible, string notEligibleMessage, string noneMessage)
    {
        if (numbers is { Count: > 0 })
        {
            var chosen = new List<OrderPiece>();
            foreach (var n in numbers.Select(x => x?.Trim() ?? string.Empty).Where(x => x.Length > 0).Distinct())
            {
                var piece = all.FirstOrDefault(p => string.Equals(p.PackageNumber, n, StringComparison.Ordinal)) ?? throw new ValidationException("pieceNumbers", string.Format(ClaimRules.PieceNotOfOrderMessage, n));
                if (!eligible(piece)) throw new StatusRuleException(string.Format(notEligibleMessage, n));
                chosen.Add(piece);
            }
            if (chosen.Count == 0) throw new ValidationException("pieceNumbers", ClaimRules.NoPiecesMessage);
            return chosen;
        }
        var auto = all.Where(eligible).ToList();
        if (auto.Count == 0) throw new StatusRuleException(noneMessage);
        return auto;
    }

    private async Task<int> OrderIdAsync(Guid? publicId, CancellationToken ct)
    {
        if (publicId is null) throw new ValidationException("orderPublicId", "Indique el envío.");
        return await db.TransportOrders.AsNoTracking().Where(o => o.PublicId == publicId && o.IsActive).Select(o => (int?)o.TransportOrderId).FirstOrDefaultAsync(ct) ?? throw new NotFoundException("Envío");
    }

    private async Task<MissingOrderDto?> MissingOrderAsync(int orderId, CancellationToken ct)
    {
        var o = await db.TransportOrders.AsNoTracking().Where(x => x.TransportOrderId == orderId).Select(x => new { x.TransportOrderId, x.PublicId, x.OrderNumber, x.WaybillNumber, x.ClientId, x.StatusCodeId }).FirstOrDefaultAsync(ct);
        if (o is null) return null;
        var pieces = await db.OrderPieces.AsNoTracking().Where(p => p.TransportOrderId == orderId && p.IsActive).OrderBy(p => p.Sequence).ToListAsync(ct);
        var lines = await LineDescriptionsAsync(pieces.Select(p => p.CargoLineId).Distinct().ToList(), ct);
        var clients = await ClientNamesAsync(new List<int> { o.ClientId }, ct);
        var st = await db.StatusCodes.AsNoTracking().FirstOrDefaultAsync(sc => sc.StatusCodeId == o.StatusCodeId, ct);
        return new MissingOrderDto(o.PublicId, o.OrderNumber, o.WaybillNumber, clients.GetValueOrDefault(o.ClientId, string.Empty), st?.InternalCode ?? string.Empty, MultilingualText.Resolve(st?.LabelJson, tenant.Lang), pieces.Count,
            pieces.Where(p => p.MissingStatus is PieceMissing.ToVerify or PieceMissing.Lost).Select(p => ToPiece(p, lines, null)).ToList());
    }

    private async Task<Dictionary<int, string>> LineDescriptionsAsync(List<int> lineIds, CancellationToken ct)
        => lineIds.Count == 0 ? new Dictionary<int, string>() : await db.CargoLines.AsNoTracking().Where(l => lineIds.Contains(l.CargoLineId)).ToDictionaryAsync(l => l.CargoLineId, l => l.Description, ct);

    private async Task<Dictionary<int, string>> ClientNamesAsync(List<int> ids, CancellationToken ct)
        => ids.Count == 0 ? new Dictionary<int, string>() : await db.Clients.AsNoTracking().Where(c => ids.Contains(c.ClientId)).ToDictionaryAsync(c => c.ClientId, c => c.Name, ct);
}
