using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Common;
using Teikem.Domain.Constants;
using Teikem.Domain.Orders;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 117d — los envíos del consignatario con acceso propio: <b>solo ve</b> las órdenes de su cliente cuya entrega es en SU consignatario (decidido con Luis, 2026-10-18: el consignatario NO crea órdenes; las crean el cliente y
/// el personal de la compañía). El cliente y el consignatario salen de la sesión (<c>cid</c> y <c>loc</c>), nunca del request; una orden de otro consignatario, de otro cliente o de otra compañía responde 404 igual que una que no
/// existe. DTOs propios y mínimos: sin precios, factura, número de orden del cliente, COD ni notas.
/// </summary>
public sealed class ConsigneeOrdersService(TeikemDbContext db, ITenantContext tenant, ILookupCache lookups, OrderProgressService progress)
{
    public const int MaxTake = 100;
    public const string OrderNotFound = "Envío";

    private TenantContext T => (TenantContext)tenant;
    private int ClientId => T.PortalClientId ?? throw new ForbiddenException(ConsigneeSelfService.NotConsigneeMessage);
    private int LocationId => T.PortalLocationId ?? throw new ForbiddenException(ConsigneeSelfService.NotConsigneeMessage);

    /// <summary>Las órdenes del cliente con una parada de entrega en mi consignatario.</summary>
    private async Task<IQueryable<TransportOrder>> MineAsync(CancellationToken ct)
    {
        var cid = ClientId; var lid = LocationId;
        var deliveryId = await lookups.GetIdAsync(LookupDomains.StopType, StopTypes.Delivery, ct);
        return db.TransportOrders.AsNoTracking().Where(o => o.IsActive && o.ClientId == cid && o.Stops.Any(s => s.LocationId == lid && s.StopTypeLookupId == deliveryId));
    }

    public async Task<PagedResult<ConsigneeOrderListItemDto>> ListAsync(string? search, int skip, int take, CancellationToken ct)
    {
        take = Math.Clamp(take <= 0 ? 25 : take, 1, MaxTake);
        skip = Math.Max(0, skip);
        var q = await MineAsync(ct);
        var term = search?.Trim();
        if (!string.IsNullOrEmpty(term)) q = q.Where(o => o.WaybillNumber.Contains(term));
        var total = await q.CountAsync(ct);
        var rows = await q.Include(o => o.Status).OrderByDescending(o => o.CreatedAtUtc).ThenByDescending(o => o.TransportOrderId).Skip(skip).Take(take).ToListAsync(ct);
        var avance = await progress.ComputeAsync(rows.Select(o => new OrderProgressInput(o.TransportOrderId, o.Status?.InternalCode ?? string.Empty)), ct);
        var items = new List<ConsigneeOrderListItemDto>(rows.Count);
        foreach (var o in rows)
        {
            var a = avance[o.TransportOrderId];
            items.Add(new ConsigneeOrderListItemDto(o.PublicId, o.WaybillNumber, await ServiceLabelAsync(o.ServiceTypeLookupId, ct), o.TotalPieces ?? 0, a.StatusCode, a.StatusLabel, o.CreatedAtUtc, o.PromisedDate,
                new PortalProgressDto(a.Step, a.Total, a.State, a.StatusCode, a.StatusLabel), o.TrackToken));
        }
        return new PagedResult<ConsigneeOrderListItemDto>(items, total, skip, take);
    }

    public async Task<ConsigneeOrderDto> GetAsync(Guid publicId, CancellationToken ct)
    {
        var q = await MineAsync(ct);
        var o = await q.Include(x => x.Status).Include(x => x.CargoLines).Include(x => x.Stops).FirstOrDefaultAsync(x => x.PublicId == publicId, ct) ?? throw new NotFoundException(OrderNotFound);
        var a = (await progress.ComputeAsync(new[] { new OrderProgressInput(o.TransportOrderId, o.Status?.InternalCode ?? string.Empty) }, ct))[o.TransportOrderId];
        var lid = LocationId;
        var stop = o.Stops.Where(s => s.LocationId == lid).OrderByDescending(s => s.Sequence).FirstOrDefault();
        var typeId = await lookups.GetIdAsync(LookupDomains.EntityType, EntityTypes.TransportOrder, ct);
        var history = await db.EntityStatusHistories.AsNoTracking().Include(h => h.ToStatus)
            .Where(h => h.EntityTypeLookupId == typeId && h.EntityId == o.TransportOrderId).OrderBy(h => h.ChangedAtUtc).ThenBy(h => h.EntityStatusHistoryId).ToListAsync(ct);
        var timeline = history.Select(h => new PortalOrderEventDto(h.ToStatus?.InternalCode ?? string.Empty, MultilingualText.Resolve(h.ToStatus?.LabelJson, tenant.Lang), h.ChangedAtUtc)).ToList();
        var packages = o.CargoLines.Where(l => l.IsActive).Select(l => new PortalOrderPackageDto(l.Description, (int)l.Quantity, null)).ToList();
        var steps = (await progress.StepsAsync(ct)).Select(s => s.Label).ToList();
        return new ConsigneeOrderDto(o.PublicId, o.WaybillNumber, await ServiceLabelAsync(o.ServiceTypeLookupId, ct), a.StatusCode, a.StatusLabel, o.TotalPieces ?? 0, o.PromisedDate, o.CreatedAtUtc,
            stop is null ? null : new PortalOrderStopDto(stop.SnapName, stop.SnapLine1, stop.SnapLine2, stop.SnapCity, stop.SnapState, stop.SnapPostalCode),
            packages, timeline, new PortalProgressDto(a.Step, a.Total, a.State, a.StatusCode, a.StatusLabel), steps);
    }

    private async Task<string> ServiceLabelAsync(int lookupId, CancellationToken ct)
        => MultilingualText.Resolve((await lookups.GetAsync(lookupId, ct))?.LabelJson, tenant.Lang);
}
