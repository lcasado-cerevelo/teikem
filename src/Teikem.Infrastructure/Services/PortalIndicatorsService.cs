using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Billing;
using Teikem.Domain.Constants;
using Teikem.Domain.Fleet;
using Teikem.Domain.Orders;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 120 (P10) — indicadores del cliente en su portal (de los últimos 7, 30 o 90 días). Todo se limita al cliente de la sesión (filtro global y condición explícita). «A tiempo» cuenta solo las órdenes entregadas que tenían fecha
/// prometida y se entregaron (día de la compañía) hasta ese día; si ninguna tenía fecha no hay porcentaje. Las facturas y el dinero de COD solo se calculan si la compañía tiene esos módulos encendidos; si no, vienen nulos.
/// Nunca trae datos de otros clientes ni de choferes.
/// </summary>
public sealed class PortalIndicatorsService(TeikemDbContext db, ITenantContext tenant, ILookupCache lookups, ModuleService modules, ITenantClock? clock = null)
{
    public static readonly int[] AllowedDays = { 7, 30, 90 };
    public const string InvalidDaysMessage = "Los días deben ser 7, 30 o 90.";
    private readonly ITenantClock _clock = clock ?? TenantClock.Default;
    private TenantContext T => (TenantContext)tenant;
    private int ClientId => T.PortalClientId ?? throw new ForbiddenException(AuthService.PortalNoAccessMessage);

    private static readonly string[] Closed = { OrderStatuses.Delivered, OrderStatuses.Cancelled, OrderStatuses.Returned, OrderStatuses.InWarehouse };

    public async Task<PortalIndicatorsDto> GetAsync(int? days, CancellationToken ct)
    {
        var d = days ?? 30;
        if (!AllowedDays.Contains(d)) throw new ValidationException("days", InvalidDaysMessage);
        var cid = ClientId;
        var now = _clock.UtcNow;
        var since = now.AddDays(-d);
        var orders = db.TransportOrders.AsNoTracking().Where(o => o.IsActive && o.ClientId == cid);
        var statuses = await db.StatusCodes.AsNoTracking().Where(s => s.Entity == StatusDomains.OrderStatus).ToDictionaryAsync(s => s.StatusCodeId, s => s.InternalCode, ct);
        var closedIds = statuses.Where(kv => Closed.Contains(kv.Value)).Select(kv => kv.Key).ToList();
        var transitId = statuses.Where(kv => kv.Value == OrderStatuses.InTransit).Select(kv => (int?)kv.Key).FirstOrDefault();
        var deliveredId = statuses.Where(kv => kv.Value == OrderStatuses.Delivered).Select(kv => (int?)kv.Key).FirstOrDefault();

        var created = await orders.CountAsync(o => o.CreatedAtUtc >= since, ct);
        var open = await orders.CountAsync(o => !closedIds.Contains(o.StatusCodeId), ct);
        var inTransit = transitId is null ? 0 : await orders.CountAsync(o => o.StatusCodeId == transitId, ct);

        // entregadas en el período: la fecha es la del cambio a «Entregada» en el historial
        int delivered = 0, withPromise = 0, onTime = 0;
        double? avgHours = null;
        if (deliveredId is not null)
        {
            var typeId = await lookups.GetIdAsync(LookupDomains.EntityType, EntityTypes.TransportOrder, ct);
            var rows = await (from h in db.EntityStatusHistories.AsNoTracking()
                              join o in orders on h.EntityId equals o.TransportOrderId
                              where h.EntityTypeLookupId == typeId && h.ToStatusCodeId == deliveredId && h.ChangedAtUtc >= since
                              select new { h.EntityId, h.ChangedAtUtc, o.CreatedAtUtc, o.PromisedDate }).ToListAsync(ct);
            var once = rows.GroupBy(r => r.EntityId).Select(g => g.OrderByDescending(r => r.ChangedAtUtc).First()).ToList();
            delivered = once.Count;
            var promised = once.Where(r => r.PromisedDate != null).ToList();
            withPromise = promised.Count;
            onTime = promised.Count(r => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(r.ChangedAtUtc, DateTimeKind.Utc), _clock.Zone)) <= DateOnly.FromDateTime(r.PromisedDate!.Value));
            if (once.Count > 0) avgHours = Math.Round(once.Average(r => (r.ChangedAtUtc - r.CreatedAtUtc).TotalHours), 1);
        }
        var failed = await db.Set<DeliveryAttempt>().AsNoTracking().CountAsync(a => a.IsActive && !a.Delivered && a.AttemptedAtUtc >= since && orders.Any(o => o.TransportOrderId == a.TransportOrderId), ct);

        decimal? invoiceBalance = null;
        int? overdue = null;
        if (await modules.IsEnabledAsync(ModuleKeys.Billing, ct))
        {
            var map = await db.StatusCodes.AsNoTracking().Where(s => s.Entity == BillingCodes.InvoiceStatusDomain).ToDictionaryAsync(s => s.StatusCodeId, s => s.InternalCode, ct);
            var openIds = map.Where(kv => kv.Value is InvoiceStatuses.Issued or InvoiceStatuses.Overdue).Select(kv => kv.Key).ToList();
            var invoices = await db.Set<Invoice>().AsNoTracking().Where(i => i.IsActive && i.ClientId == cid && openIds.Contains(i.StatusCodeId)).ToListAsync(ct);
            var ids = invoices.Select(i => i.InvoiceId).ToList();
            var paid = (await db.Set<Payment>().AsNoTracking().Where(p => ids.Contains(p.InvoiceId) && p.VoidedAtUtc == null).ToListAsync(ct)).GroupBy(p => p.InvoiceId).ToDictionary(g => g.Key, g => g.Sum(p => p.Amount));
            var credits = await db.AppliedByInvoiceAsync(ids, ct);
            invoiceBalance = invoices.Sum(i => Math.Max(0m, i.Subtotal + i.TaxAmount - paid.GetValueOrDefault(i.InvoiceId) - credits.GetValueOrDefault(i.InvoiceId)));
            overdue = invoices.Count(i => map[i.StatusCodeId] == InvoiceStatuses.Overdue);
        }

        decimal? codToRemit = null;
        if (await modules.IsEnabledAsync(ModuleKeys.Cod, ct))
        {
            var codIds = await db.StatusCodes.AsNoTracking().Where(s => s.Entity == StatusDomains.CodStatus && (s.InternalCode == CodStatuses.Collected || s.InternalCode == CodStatuses.Reconciled)).Select(s => s.StatusCodeId).ToListAsync(ct);
            codToRemit = await orders.Where(o => o.CodStatusCodeId != null && codIds.Contains(o.CodStatusCodeId.Value)).SumAsync(o => o.CodAmount ?? 0m, ct);
        }
        return new PortalIndicatorsDto(d, created, delivered, withPromise, onTime, withPromise == 0 ? null : Math.Round(100.0 * onTime / withPromise, 1), avgHours, open, inTransit, failed, invoiceBalance, overdue, codToRemit);
    }
}
