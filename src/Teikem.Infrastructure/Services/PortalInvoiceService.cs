using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Billing;
using Teikem.Domain.Common;
using Teikem.Domain.Constants;
using Teikem.Domain.Orders;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 119 (P10) — las facturas del cliente en su portal, SOLO LECTURA (dictado de Luis, 2026-10-18: se muestran; no se pueden marcar como pagadas). Mismas reglas que las demás lecturas del portal: todo se limita al cliente de
/// la sesión (el filtro global por cliente del contexto y la condición explícita aquí) y una factura de otro cliente responde 404 igual que una que no existe. Nunca se muestran los borradores (una corrida sin aprobar) ni las notas
/// internas ni el detalle de un ajuste de la oficina; los pagos anulados tampoco. Exige el módulo BILLING encendido (además del portal).
/// </summary>
public sealed class PortalInvoiceService(TeikemDbContext db, ITenantContext tenant, ILookupCache lookups, ModuleService modules)
{
    public const int MaxTake = 100;
    private TenantContext T => (TenantContext)tenant;
    private int ClientId => T.PortalClientId ?? throw new ForbiddenException(AuthService.PortalNoAccessMessage);

    public async Task<PagedResult<PortalInvoiceListItemDto>> ListAsync(string? status, int skip, int take, CancellationToken ct)
    {
        await modules.EnsureEnabledAsync(ModuleKeys.Billing, ct);
        var cid = ClientId;
        take = Math.Clamp(take <= 0 ? 25 : take, 1, MaxTake);
        skip = Math.Max(0, skip);
        var map = await StatusMapAsync(ct);
        var visible = map.Where(kv => kv.Value.Code != InvoiceStatuses.Draft).Select(kv => kv.Key).ToList();
        var query = db.Set<Invoice>().AsNoTracking().Where(i => i.IsActive && i.ClientId == cid && visible.Contains(i.StatusCodeId));
        if (!string.IsNullOrWhiteSpace(status))
        {
            var ids = map.Where(kv => kv.Value.Code.Equals(status.Trim(), StringComparison.OrdinalIgnoreCase)).Select(kv => kv.Key).ToList();
            query = query.Where(i => ids.Contains(i.StatusCodeId));
        }
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(i => i.IssueDate).ThenByDescending(i => i.InvoiceId).Skip(skip).Take(take).ToListAsync(ct);
        var ids2 = rows.Select(r => r.InvoiceId).ToList();
        var paid = await PaidAsync(ids2, ct);
        var credits = await db.AppliedByInvoiceAsync(ids2, ct);
        var items = new List<PortalInvoiceListItemDto>(rows.Count);
        foreach (var i in rows)
        {
            var t = i.Subtotal + i.TaxAmount;
            var p = paid.GetValueOrDefault(i.InvoiceId);
            items.Add(new PortalInvoiceListItemDto(i.PublicId, i.InvoiceNumber, map[i.StatusCodeId].Code, map[i.StatusCodeId].Label, i.IssueDate, i.DueDate, await CurrencyAsync(i.CurrencyLookupId, ct), t, p, t - p - credits.GetValueOrDefault(i.InvoiceId)));
        }
        return new PagedResult<PortalInvoiceListItemDto>(items, total, skip, take);
    }

    public async Task<PortalInvoiceDto> GetAsync(Guid publicId, CancellationToken ct)
    {
        await modules.EnsureEnabledAsync(ModuleKeys.Billing, ct);
        var cid = ClientId;
        var map = await StatusMapAsync(ct);
        var i = await db.Set<Invoice>().AsNoTracking().Include(x => x.Lines).FirstOrDefaultAsync(x => x.PublicId == publicId && x.IsActive && x.ClientId == cid, ct);
        if (i is null || map[i.StatusCodeId].Code == InvoiceStatuses.Draft) throw new NotFoundException("Factura", feminine: true);   // de otro cliente, borrador o inexistente: la misma respuesta
        var paid = (await PaidAsync(new[] { i.InvoiceId }, ct)).GetValueOrDefault(i.InvoiceId);
        var credited = (await db.AppliedByInvoiceAsync(new[] { i.InvoiceId }, ct)).GetValueOrDefault(i.InvoiceId);
        var transportIds = i.Lines.Where(l => l.TransportOrderId != null).Select(l => l.TransportOrderId!.Value).Distinct().ToList();
        var orderNumbers = transportIds.Count == 0 ? new Dictionary<int, string>()
            : await db.Set<TransportOrder>().AsNoTracking().Where(o => transportIds.Contains(o.TransportOrderId)).ToDictionaryAsync(o => o.TransportOrderId, o => o.OrderNumber, ct);
        var lines = i.Lines.OrderBy(l => l.InvoiceLineId).Select(l => new PortalInvoiceLineDto(l.Description, l.Quantity, l.UnitAmount, Math.Round(l.Quantity * l.UnitAmount, 4, MidpointRounding.AwayFromZero), l.TaxAmount,
            l.TransportOrderId is int tid ? orderNumbers.GetValueOrDefault(tid) : null)).ToList();
        var payments = new List<PortalInvoicePaymentDto>();
        foreach (var p in await db.Set<Payment>().AsNoTracking().Where(p => p.InvoiceId == i.InvoiceId && p.VoidedAtUtc == null).OrderBy(p => p.PaymentId).ToListAsync(ct))
        {
            var m = await lookups.GetAsync(p.PaymentMethodLookupId, ct);
            payments.Add(new PortalInvoicePaymentDto(DateOnly.FromDateTime(p.PaidAtUtc), p.Amount, MultilingualText.Resolve(m?.LabelJson, tenant.Lang) ?? m?.InternalCode ?? string.Empty, p.Reference));
        }
        var total = i.Subtotal + i.TaxAmount;
        return new PortalInvoiceDto(i.PublicId, i.InvoiceNumber, map[i.StatusCodeId].Code, map[i.StatusCodeId].Label, i.IssueDate, i.DueDate, await CurrencyAsync(i.CurrencyLookupId, ct), i.Subtotal, i.TaxAmount, total, paid, credited,
            total - paid - credited, lines, payments);
    }

    private async Task<string> CurrencyAsync(int lookupId, CancellationToken ct) => (await lookups.GetAsync(lookupId, ct))?.InternalCode ?? "USD";

    private async Task<Dictionary<int, (string Code, string Label)>> StatusMapAsync(CancellationToken ct)
        => (await db.StatusCodes.AsNoTracking().Where(s => s.Entity == BillingCodes.InvoiceStatusDomain).ToListAsync(ct)).ToDictionary(s => s.StatusCodeId, s => (s.InternalCode, MultilingualText.Resolve(s.LabelJson, tenant.Lang) ?? s.InternalCode));

    private async Task<Dictionary<int, decimal>> PaidAsync(IEnumerable<int> invoiceIds, CancellationToken ct)
    {
        var list = invoiceIds.Distinct().ToList();
        return (await db.Set<Payment>().AsNoTracking().Where(p => list.Contains(p.InvoiceId) && p.VoidedAtUtc == null).ToListAsync(ct)).GroupBy(p => p.InvoiceId).ToDictionary(g => g.Key, g => g.Sum(p => p.Amount));
    }
}
