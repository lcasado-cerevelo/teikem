using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Common;
using Teikem.Domain.Constants;
using Teikem.Domain.Orders;
using Teikem.Domain.Trips;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 216 (2026-10-24, Luis) — la lupa de la estación de escaneo: «buscar por el número de paquete lo que sea que se encuentre en la base de datos». Si escaneaste y no se encontró, aquí se ve por qué: la pieza o el envío están
/// cancelados, finales, dados de baja, en una reclamación, perdidos o «Por verificar»… Solo lee y no cambia nada. Primero por el número de la pieza (incluidas las dadas de baja); si ninguna coincide, por el número del envío,
/// el conduce, el empaque o la factura. Hasta 10 resultados (el número de pieza puede repetirse entre clientes).
/// </summary>
public sealed class ScanLookupService(TeikemDbContext db, ITenantContext tenant)
{
    public const int MaxItems = 10;

    public async Task<ScanLookupDto> LookupAsync(string? raw, CancellationToken ct)
    {
        if (tenant.TenantId is null) throw new ForbiddenException("No hay tenant activo en la sesión.");
        var (code, error) = OutboundScanRules.NormalizeCode(raw);
        if (error is not null) throw new ValidationException("code", error);

        var pieces = await db.OrderPieces.AsNoTracking().Where(p => p.PackageNumber == code!).OrderByDescending(p => p.IsActive).ThenByDescending(p => p.OrderPieceId).Take(MaxItems).ToListAsync(ct);
        string? matchedBy = null;
        var rows = new List<(OrderPiece? Piece, int OrderId)>();
        if (pieces.Count > 0)
        {
            matchedBy = "PIECE";
            rows.AddRange(pieces.Select(p => ((OrderPiece?)p, p.TransportOrderId)));
        }
        else
        {
            var c = code!;
            var orders = await db.TransportOrders.AsNoTracking().Where(o => o.OrderNumber == c || o.WaybillNumber == c || o.PackBatchNumber == c || o.ClientInvoiceNumber == c)
                .OrderByDescending(o => o.IsActive).ThenByDescending(o => o.CreatedAtUtc).Take(MaxItems)
                .Select(o => new { o.TransportOrderId, o.OrderNumber, o.WaybillNumber, o.PackBatchNumber }).ToListAsync(ct);
            if (orders.Count > 0)
            {
                var first = orders[0];
                matchedBy = string.Equals(first.WaybillNumber, c, StringComparison.OrdinalIgnoreCase) ? "WAYBILL" : string.Equals(first.OrderNumber, c, StringComparison.OrdinalIgnoreCase) ? "ORDER_NUMBER"
                    : string.Equals(first.PackBatchNumber, c, StringComparison.OrdinalIgnoreCase) ? "PACK_BATCH" : "INVOICE";
                rows.AddRange(orders.Select(o => ((OrderPiece?)null, o.TransportOrderId)));
            }
        }
        if (rows.Count == 0) return new ScanLookupDto(code!, null, Array.Empty<ScanLookupItemDto>());

        var orderIds = rows.Select(r => r.OrderId).Distinct().ToList();
        var orderRows = await (from o in db.TransportOrders.AsNoTracking()
                               join st in db.StatusCodes.AsNoTracking().Include(s => s.StageKind) on o.StatusCodeId equals st.StatusCodeId
                               join cl in db.Clients.AsNoTracking() on o.ClientId equals cl.ClientId into cls from cl in cls.DefaultIfEmpty()
                               where orderIds.Contains(o.TransportOrderId)
                               select new { o.TransportOrderId, o.PublicId, o.OrderNumber, o.WaybillNumber, o.PackBatchNumber, o.IsActive, StatusCode = st.InternalCode, st.LabelJson, Stage = st.StageKind!.InternalCode, ClientName = cl != null ? cl.Name : null })
            .ToDictionaryAsync(x => x.TransportOrderId, ct);
        var stops = await db.OrderStops.AsNoTracking().Where(s => orderIds.Contains(s.TransportOrderId)).Select(s => new { s.TransportOrderId, s.Sequence, s.SnapName, s.SnapCity, IsDelivery = s.StopType != null && s.StopType.InternalCode == StopTypes.Delivery }).ToListAsync(ct);
        var stopByOrder = stops.GroupBy(s => s.TransportOrderId).ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.IsDelivery).ThenByDescending(x => x.Sequence).First());
        var trips = await (from l in db.TripOrders.AsNoTracking() join t in db.Trips.AsNoTracking() on l.TripId equals t.TripId
                           join st in db.StatusCodes.AsNoTracking() on t.StatusCodeId equals st.StatusCodeId
                           join d in db.Drivers.AsNoTracking() on t.DriverId equals d.DriverId into ds from d in ds.DefaultIfEmpty()
                           where l.IsCurrent && orderIds.Contains(l.TransportOrderId)
                           select new { l.TransportOrderId, t.Code, StatusLabel = st.LabelJson, DriverName = d != null ? d.FullName : null }).ToListAsync(ct);
        var tripByOrder = trips.GroupBy(t => t.TransportOrderId).ToDictionary(g => g.Key, g => g.First());
        var pieceCounts = await db.OrderPieces.AsNoTracking().Where(p => p.IsActive && orderIds.Contains(p.TransportOrderId)).GroupBy(p => p.TransportOrderId).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N, ct);

        var pieceIds = rows.Where(r => r.Piece is not null).Select(r => r.Piece!.OrderPieceId).ToList();
        var scans = pieceIds.Count == 0 ? new() : await (from s in db.OrderPieceScans.AsNoTracking()
                                                         join u in db.Users.AsNoTracking() on s.ScannedBy equals (int?)u.Id into us from u in us.DefaultIfEmpty()
                                                         where pieceIds.Contains(s.OrderPieceId) orderby s.ScannedAtUtc
                                                         select new { s.OrderPieceId, s.Action, s.ScannedAtUtc, Name = u != null ? u.FullName : null }).ToListAsync(ct);
        var claimIds = rows.Where(r => r.Piece?.ClaimId != null).Select(r => r.Piece!.ClaimId!.Value).Distinct().ToList();
        var claims = claimIds.Count == 0 ? new() : await (from c in db.Claims.AsNoTracking() join st in db.StatusCodes.AsNoTracking() on c.StatusCodeId equals st.StatusCodeId where claimIds.Contains(c.ClaimId) select new { c.ClaimId, c.Number, st.LabelJson }).ToDictionaryAsync(x => x.ClaimId, ct);

        var items = new List<ScanLookupItemDto>();
        foreach (var (piece, orderId) in rows)
        {
            if (!orderRows.TryGetValue(orderId, out var o)) continue;
            stopByOrder.TryGetValue(orderId, out var stop);
            tripByOrder.TryGetValue(orderId, out var trip);
            var claim = piece?.ClaimId is int cid && claims.TryGetValue(cid, out var cl) ? cl : null;
            var flags = new List<string>();
            if (piece is { IsActive: false }) flags.Add("PIECE_INACTIVE");
            if (!o.IsActive) flags.Add("ORDER_INACTIVE");
            if (o.StatusCode == OrderStatuses.Cancelled) flags.Add("ORDER_CANCELLED");
            else if (o.StatusCode == OrderStatuses.Returned) flags.Add("ORDER_RETURNED");
            else if (string.Equals(o.Stage, StageKinds.Terminal, StringComparison.OrdinalIgnoreCase)) flags.Add("ORDER_FINAL");
            if (piece?.MissingStatus == PieceMissing.ToVerify) flags.Add("PIECE_TO_VERIFY");
            else if (piece?.MissingStatus == PieceMissing.Lost) flags.Add("PIECE_LOST");
            else if (piece?.MissingStatus == PieceMissing.Claimed) flags.Add("PIECE_CLAIMED");
            else if (piece?.MissingStatus == PieceMissing.Resolved) flags.Add("PIECE_RESOLVED");
            if (piece?.ReappearedAtUtc != null) flags.Add("REAPPEARED");
            if (trip is not null) flags.Add("IN_ROUTE");
            items.Add(new ScanLookupItemDto(piece?.PackageNumber, piece?.IsActive ?? true, piece?.MissingStatus, claim?.Number, claim is null ? null : MultilingualText.Resolve(claim.LabelJson, tenant.Lang), piece?.ReappearedAtUtc,
                piece is null ? Array.Empty<ScanLookupScanDto>() : scans.Where(s => s.OrderPieceId == piece.OrderPieceId).Select(s => new ScanLookupScanDto(s.Action, s.ScannedAtUtc, s.Name)).ToList(),
                o.PublicId, o.OrderNumber, o.WaybillNumber, o.PackBatchNumber, o.IsActive, o.StatusCode, MultilingualText.Resolve(o.LabelJson, tenant.Lang), o.Stage, o.ClientName, stop?.SnapName, stop?.SnapCity,
                trip?.Code, trip is null ? null : MultilingualText.Resolve(trip.StatusLabel, tenant.Lang), trip?.DriverName, pieceCounts.GetValueOrDefault(orderId), flags));
        }
        return new ScanLookupDto(code!, matchedBy, items);
    }
}
