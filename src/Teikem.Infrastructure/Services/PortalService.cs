using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Common;
using Teikem.Domain.Constants;
using Teikem.Domain.Identity;
using Teikem.Domain.Orders;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 101 — lecturas del portal del cliente. REGLA: todo se limita al cliente de la sesión (<c>cid</c>) además de la compañía (<c>tid</c>): el contexto lo fija como alcance (<c>ScopedClientIds</c>, filtro global de
/// datos) y cada lectura lo vuelve a exigir (<c>OrderScope(ClientId)</c>). Una orden de otro cliente (de la misma compañía o de otra) responde 404, igual que una que no existe (sin oráculo).
/// Se devuelven DTOs propios y mínimos (<c>PortalContracts</c>): sin precios cotizados, notas internas, rutas, choferes ni datos de otros clientes.
/// </summary>
public sealed class PortalService(TeikemDbContext db, ITenantContext tenant, UserManager<ApplicationUser> users, AuthService auth, OrderReadService orders, ILookupCache lookups, OrderProgressService progress, PortalWarehouseService? warehouse = null)
{
    public const int MaxTake = 100;

    private int ClientIdOfSession => ((TenantContext)tenant).PortalClientId ?? throw new ForbiddenException(AuthService.PortalNoAccessMessage);

    public async Task<PortalMeDto> GetMeAsync(CancellationToken ct)
    {
        var cid = ClientIdOfSession;
        var userId = ((TenantContext)tenant).RequireUserId();
        var user = await users.FindByIdAsync(userId.ToString()) ?? throw new UnauthorizedException();
        var clients = await auth.PortalClientsOfAsync(userId, ct);
        var list = clients.Select(c => new PortalClientDto(c.ClientPublicId, c.ClientName, c.TenantName, c.ClientId == cid && c.TenantId == tenant.TenantId)).ToList();
        var current = list.FirstOrDefault(c => c.IsCurrent) ?? throw new ForbiddenException(AuthService.PortalNoAccessMessage);
        return new PortalMeDto(user.FullName, user.Email, user.TwoFactorEnabled, current, list, warehouse is null ? null : await warehouse.CapabilitiesAsync(ct));   // Lote 153: qué ofrece la compañía a este acceso
    }

    public async Task<PagedResult<PortalOrderListItemDto>> ListOrdersAsync(string? search, string? status, DateTime? fromUtc, DateTime? toUtc, int skip, int take, CancellationToken ct)
    {
        var cid = ClientIdOfSession;
        take = Math.Clamp(take <= 0 ? 25 : take, 1, MaxTake);
        skip = Math.Max(0, skip);
        var q = new OrderListQuery(null, string.IsNullOrWhiteSpace(status) ? null : status.Trim(), null, null, null, null, string.IsNullOrWhiteSpace(search) ? null : search.Trim(), fromUtc, toUtc, false, skip, take);
        var page = await orders.GetListAsync(q, new OrderScope(cid), ct);
        var avance = await progress.ComputeAsync(page.Items.Select(o => new OrderProgressInput(o.Id, o.Status)), ct);
        var items = page.Items.Select(o => new PortalOrderListItemDto(o.PublicId, o.WaybillNumber ?? string.Empty, o.OrderNumber, o.ClientInvoiceNumber, o.ConsigneeName, o.ConsigneeCity, o.ConsigneeState,
            o.ServiceTypeLabel, o.TotalPieces, o.CodAmount, o.Status, o.StatusLabel, o.CreatedAtUtc, ProgressOf(avance[o.Id]))).ToList();
        return new PagedResult<PortalOrderListItemDto>(items, page.Total, skip, take);
    }

    public async Task<PortalOrderDto> GetOrderAsync(Guid publicId, CancellationToken ct)
    {
        var cid = ClientIdOfSession;
        var o = await orders.GetAsync(publicId, new OrderScope(cid), ct);   // otro cliente u otra compañía: 404
        var timeline = await TimelineAsync(o.Id, ct);
        var avance = (await progress.ComputeAsync(new[] { new OrderProgressInput(o.Id, o.Status) }, ct))[o.Id];
        var stepLabels = (await progress.StepsAsync(ct)).Select(s => s.Label).ToList();
        PortalOrderStopDto? Stop(OrderStopDto? s) => s is null ? null : new PortalOrderStopDto(s.Name, s.Line1, s.Line2, s.City, s.State, s.PostalCode);
        return new PortalOrderDto(o.PublicId, o.WaybillNumber ?? string.Empty, o.OrderNumber, o.ClientInvoiceNumber, o.ServiceTypeLabel, o.Status, o.StatusLabel, o.TotalPieces, o.TotalWeightKg, o.CodAmount,
            o.CodStatusLabel, o.RequestedDate, o.PromisedDate, o.CreatedAtUtc, Stop(o.Pickup), Stop(o.Delivery)!,
            o.Packages.Select(p => new PortalOrderPackageDto(p.Description, p.Pieces, p.WeightKg)).ToList(), timeline, ProgressOf(avance), stepLabels, await DeliveryProofAsync(o.Id, ct), o.InvoiceProcessedAtUtc);
    }

    /// <summary>Lote 119: el comprobante de entrega de la orden (si lo tiene): quién recibió y cuándo; «registrada por el personal» sin decir quién; coordenadas solo si se encontraron.</summary>
    private async Task<PortalDeliveryProofDto?> DeliveryProofAsync(int orderId, CancellationToken ct)
    {
        var p = await db.Set<Teikem.Domain.Orders.DeliveryProof>().AsNoTracking().Where(x => x.TransportOrderId == orderId && x.IsActive).FirstOrDefaultAsync(ct);
        if (p is null) return null;
        var located = p.GeoStatus is Teikem.Domain.Orders.DeliveryProofCodes.GeoOk or Teikem.Domain.Orders.DeliveryProofCodes.GeoFromStop && p.Latitude is not null && p.Longitude is not null;
        return new PortalDeliveryProofDto(p.DeliveredAtUtc, p.RecipientName, p.Source == Teikem.Domain.Orders.DeliveryProofCodes.SourceStaffWeb, p.SignatureKind == Teikem.Domain.Orders.DeliveryProofCodes.SignatureRecipient,
            located ? (double?)p.Latitude : null, located ? (double?)p.Longitude : null);
    }

    /// <summary>Inicio: total de órdenes del cliente, por estatus y las 5 más recientes.</summary>
    public async Task<PortalSummaryDto> GetSummaryAsync(CancellationToken ct)
    {
        var cid = ClientIdOfSession;
        var groups = await db.TransportOrders.AsNoTracking().Where(o => o.IsActive && o.ClientId == cid)
            .GroupBy(o => o.StatusCodeId).Select(g => new { StatusCodeId = g.Key, Count = g.Count() }).ToListAsync(ct);
        var ids = groups.Select(g => g.StatusCodeId).ToList();
        var codes = await db.StatusCodes.AsNoTracking().Where(s => ids.Contains(s.StatusCodeId)).ToDictionaryAsync(s => s.StatusCodeId, ct);
        var byStatus = groups.Select(g => new PortalStatusCountDto(codes.GetValueOrDefault(g.StatusCodeId)?.InternalCode ?? string.Empty,
                MultilingualText.Resolve(codes.GetValueOrDefault(g.StatusCodeId)?.LabelJson, tenant.Lang), g.Count))
            .OrderByDescending(s => s.Count).ThenBy(s => s.StatusLabel).ToList();
        var recent = await ListOrdersAsync(null, null, null, null, 0, 5, ct);
        return new PortalSummaryDto(groups.Sum(g => g.Count), byStatus, recent.Items);
    }

    /// <summary>Bandeja de avisos: los resúmenes de ESTE cliente marcados para el portal, los más nuevos primero.</summary>
    public async Task<PagedResult<PortalNotificationDto>> ListNotificationsAsync(int skip, int take, CancellationToken ct)
    {
        var cid = ClientIdOfSession;
        take = Math.Clamp(take <= 0 ? 25 : take, 1, MaxTake);
        skip = Math.Max(0, skip);
        var q = db.Set<Teikem.Domain.Clients.ClientNotification>().AsNoTracking().Where(n => n.ClientId == cid && n.InPortal);
        // Lote 119: lo que ESTA persona apagó en sus preferencias no se le muestra (la compañía decide qué se genera; la persona, qué quiere ver)
        var userId = ((TenantContext)tenant).UserId;
        var hidden = userId is null ? new List<string>() : await db.Set<Teikem.Domain.Clients.PortalNoticePref>().AsNoTracking()
            .Where(p => !p.AppEnabled && db.Set<Teikem.Domain.Clients.PortalUser>().Any(u => u.PortalUserId == p.PortalUserId && u.UserId == userId && u.ClientId == cid)).Select(p => p.Kind).ToListAsync(ct);
        if (hidden.Count > 0) q = q.Where(n => !hidden.Contains(n.Kind));
        var total = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(n => n.CreatedAtUtc).ThenByDescending(n => n.ClientNotificationId).Skip(skip).Take(take).ToListAsync(ct);
        return new PagedResult<PortalNotificationDto>(rows.Select(n => new PortalNotificationDto(n.PublicId, n.Kind, n.Subject, n.BodyText, n.CreatedAtUtc)).ToList(), total, skip, take);
    }

    private static PortalProgressDto ProgressOf(OrderProgressResult p) => new(p.Step, p.Total, p.State, p.StatusCode, p.StatusLabel, p.FailedAttempts);

    /// <summary>Línea de tiempo: solo el estatus y cuándo (sin comentarios internos ni quién lo cambió).</summary>
    private async Task<IReadOnlyList<PortalOrderEventDto>> TimelineAsync(int orderId, CancellationToken ct)
    {
        var typeId = await lookups.GetIdAsync(LookupDomains.EntityType, EntityTypes.TransportOrder, ct);
        var rows = await db.EntityStatusHistories.AsNoTracking().Include(h => h.ToStatus)
            .Where(h => h.EntityTypeLookupId == typeId && h.EntityId == orderId).OrderBy(h => h.ChangedAtUtc).ThenBy(h => h.EntityStatusHistoryId).ToListAsync(ct);
        return rows.Select(h => new PortalOrderEventDto(h.ToStatus?.InternalCode ?? string.Empty, MultilingualText.Resolve(h.ToStatus?.LabelJson, tenant.Lang), h.ChangedAtUtc)).ToList();
    }
}
