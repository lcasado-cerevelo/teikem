using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Common;
using Teikem.Domain.Constants;
using Teikem.Domain.Orders;
using Teikem.Domain.Trips;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Orders;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 216 (2026-10-24, Luis) — la descarga de la estación de escaneo para trabajar sin señal. Todos los envíos que no están completados (cualquier estatus que no sea final, incluidos los «En tránsito» y los fallidos),
/// los <see cref="Limit"/> más recientes (por fecha de creación: los viejos casi siempre son un error), con sus piezas y lo ya escaneado; más el pipeline de estatus y las rutas abiertas para Enrutar.
/// Por páginas (<see cref="MaxPage"/>): el teléfono las pide una tras otra hasta completar el total. Solo lee.
/// </summary>
public sealed class ScanSnapshotService(TeikemDbContext db, ITenantContext tenant, StatusService statuses, ITenantClock? clock = null, int? limitOverride = null)
{
    public const int DefaultLimit = 5000;
    public const int MaxPage = 500;
    private readonly ITenantClock _clock = clock ?? TenantClock.Default;
    private int Limit => limitOverride ?? DefaultLimit;

    public async Task<ScanSnapshotPageDto> PageAsync(int skip, int take, CancellationToken ct)
    {
        if (tenant.TenantId is null) throw new ForbiddenException("No hay tenant activo en la sesión.");
        skip = Math.Max(0, skip);
        take = Math.Clamp(take <= 0 ? MaxPage : take, 1, MaxPage);

        var open = from o in db.TransportOrders.AsNoTracking()
                   join st in db.StatusCodes.AsNoTracking() on o.StatusCodeId equals st.StatusCodeId
                   where o.IsActive && st.StageKind!.InternalCode != StageKinds.Terminal
                   select new { o.TransportOrderId, o.PublicId, o.OrderNumber, o.WaybillNumber, o.ServiceTypeLookupId, o.CreatedAtUtc, StatusCode = st.InternalCode };
        var available = await open.CountAsync(ct);
        var total = Math.Min(available, Limit);
        var room = Math.Max(0, Math.Min(take, total - skip));

        var page = room == 0
            ? new()
            : await open.OrderByDescending(o => o.CreatedAtUtc).ThenByDescending(o => o.TransportOrderId).Skip(skip).Take(room).ToListAsync(ct);
        var ids = page.Select(o => o.TransportOrderId).ToList();

        var pieces = ids.Count == 0 ? new() : await db.OrderPieces.AsNoTracking().Where(p => p.IsActive && ids.Contains(p.TransportOrderId))
            .OrderBy(p => p.TransportOrderId).ThenBy(p => p.Sequence).Select(p => new { p.OrderPieceId, p.TransportOrderId, p.PackageNumber, p.MissingStatus }).ToListAsync(ct);
        var pieceIds = pieces.Select(p => p.OrderPieceId).ToList();
        var scans = pieceIds.Count == 0 ? new() : await db.OrderPieceScans.AsNoTracking().Where(s => pieceIds.Contains(s.OrderPieceId)).Select(s => new { s.OrderPieceId, s.Action }).Distinct().ToListAsync(ct);
        var actionsByPiece = scans.GroupBy(s => s.OrderPieceId).ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(x => x.Action).OrderBy(a => ScanActions.Rank(a)).ToList());
        var trips = ids.Count == 0 ? new() : await (from l in db.TripOrders.AsNoTracking() join t in db.Trips.AsNoTracking() on l.TripId equals t.TripId
                                                   join d in db.Drivers.AsNoTracking() on t.DriverId equals d.DriverId into ds from d in ds.DefaultIfEmpty()
                                                   where l.IsCurrent && t.IsActive && ids.Contains(l.TransportOrderId)
                                                   select new { l.TransportOrderId, t.PublicId, t.Code, DriverPublicId = d != null ? (Guid?)d.PublicId : null }).ToListAsync(ct);
        // El pueblo: el de la parada de entrega (la de mayor secuencia); si el envío no tiene entrega, el de cualquier parada.
        var stops = ids.Count == 0 ? new() : await db.OrderStops.AsNoTracking().Where(st => ids.Contains(st.TransportOrderId)).Select(st => new { st.TransportOrderId, st.Sequence, st.SnapCity, IsDelivery = st.StopType != null && st.StopType.InternalCode == StopTypes.Delivery }).ToListAsync(ct);
        var cityByOrder = stops.Where(st => !string.IsNullOrWhiteSpace(st.SnapCity)).GroupBy(st => st.TransportOrderId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.IsDelivery).ThenByDescending(x => x.Sequence).First().SnapCity!.Trim());
        var tripByOrder = trips.GroupBy(t => t.TransportOrderId).ToDictionary(g => g.Key, g => g.First());
        var pickUpByType = new Dictionary<int, bool>();
        foreach (var typeId in page.Select(o => o.ServiceTypeLookupId).Distinct())
            pickUpByType[typeId] = (await ServiceTypeMarkReader.LoadAsync(db, typeId, ct)).PickUp;

        var piecesByOrder = pieces.GroupBy(p => p.TransportOrderId).ToDictionary(g => g.Key, g => g.ToList());
        var orders = page.Select(o =>
        {
            tripByOrder.TryGetValue(o.TransportOrderId, out var trip);
            var ps = piecesByOrder.GetValueOrDefault(o.TransportOrderId) ?? new();
            return new ScanSnapshotOrderDto(o.PublicId, o.OrderNumber, o.WaybillNumber, o.StatusCode, trip?.Code, trip?.PublicId, pickUpByType.GetValueOrDefault(o.ServiceTypeLookupId),
                ps.Select(p => new ScanSnapshotPieceDto(p.PackageNumber, actionsByPiece.GetValueOrDefault(p.OrderPieceId) ?? Array.Empty<string>(), p.MissingStatus)).ToList(),
                cityByOrder.GetValueOrDefault(o.TransportOrderId), trip?.DriverPublicId);
        }).ToList();

        IReadOnlyList<ScanSnapshotStatusDto> statusList = Array.Empty<ScanSnapshotStatusDto>();
        IReadOnlyList<ScanSnapshotRouteDto> routes = Array.Empty<ScanSnapshotRouteDto>();
        IReadOnlyList<ScanSnapshotDriverDto> drivers = Array.Empty<ScanSnapshotDriverDto>();
        if (skip == 0)
        {
            drivers = await db.Drivers.AsNoTracking().Where(d => d.IsActive).OrderBy(d => d.FullName).Select(d => new ScanSnapshotDriverDto(d.PublicId, d.FullName)).ToListAsync(ct);
            var pipeline = await statuses.GetPipelineAsync(StatusDomains.OrderStatus, includeDisabled: true, ct);
            statusList = pipeline.Select(s => new ScanSnapshotStatusDto(s.Code, s.Label, s.SortOrder, s.StageKind, s.IsInitial, s.IsEnabled)).ToList();
            routes = await OpenRoutesAsync(ct);
        }
        return new ScanSnapshotPageDto(total, Limit, available > Limit, skip, DateTime.UtcNow, orders, statusList, routes, drivers);
    }

    /// <summary>Las rutas que se pueden escoger para Enrutar: las planeadas de hoy y las despachadas o en curso. Solo lectura.</summary>
    public async Task<IReadOnlyList<ScanSnapshotRouteDto>> OpenRoutesAsync(CancellationToken ct)
    {
        var today = _clock.Today;
        var openCodes = new[] { TripStatuses.Draft, TripStatuses.Planned, TripStatuses.Dispatched, TripStatuses.InProgress };
        var rows = await (from t in db.Trips.AsNoTracking()
                          join st in db.StatusCodes.AsNoTracking() on t.StatusCodeId equals st.StatusCodeId
                          join d in db.Drivers.AsNoTracking() on t.DriverId equals d.DriverId into ds from d in ds.DefaultIfEmpty()
                          where t.IsActive && openCodes.Contains(st.InternalCode)
                                && (t.PlanDate == today || st.InternalCode == TripStatuses.Dispatched || st.InternalCode == TripStatuses.InProgress)
                          orderby t.Code
                          select new { t.TripId, t.PublicId, t.Code, t.Kind, StatusCode = st.InternalCode, st.LabelJson, DriverName = d != null ? d.FullName : null }).ToListAsync(ct);
        var ids = rows.Select(r => r.TripId).ToList();
        var counts = ids.Count == 0 ? new() : await db.TripOrders.AsNoTracking().Where(l => l.IsCurrent && ids.Contains(l.TripId)).GroupBy(l => l.TripId).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N, ct);
        return rows.Select(r => new ScanSnapshotRouteDto(r.PublicId, r.Code, r.Kind, r.StatusCode, MultilingualText.Resolve(r.LabelJson, tenant.Lang), r.DriverName, counts.GetValueOrDefault(r.TripId))).ToList();
    }
}
