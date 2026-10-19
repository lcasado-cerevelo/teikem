using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Constants;
using Teikem.Domain.Orders;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 122 (P12) — fila de «Necesita tu atención»: órdenes abiertas que están a punto de pasar su SLA (en riesgo) o que ya lo pasaron (vencidas), según el nivel de servicio de su contrato (reloj desde que se recoge). Fila AGREGADA (una
/// sola; `Summary`). Módulo LTL_GROUND y orders.view. Tono: warn; danger si hay alguna ya vencida. «Revisar» abre Órdenes. Params (cadenas): atRisk, breached y minHours (las horas que le quedan a la más apretada; negativo = lleva ese
/// tiempo vencida). Solo evalúa las órdenes en curso ya recogidas (las que tienen reloj).
/// </summary>
public sealed class SlaAtRiskAttentionProvider(TeikemDbContext db, SlaService sla) : IAttentionItemProvider
{
    public const string ItemCode = "SLA_AT_RISK";
    public const string Route = "/orders";
    public const int MaxOrders = 2000;

    public string Code => ItemCode;
    public string BusinessModule => BusinessModules.Operations;
    public string? TenantModule => ModuleKeys.LtlGround;
    public string RequiredPermission => PermissionCatalog.OrdersView;

    public async Task<AttentionProviderResult> ReadAsync(AttentionScope scope, int take, CancellationToken ct)
    {
        if (take <= 0) return new AttentionProviderResult(0, Array.Empty<AttentionItemDto>(), null);
        var running = await db.StatusCodes.AsNoTracking().Where(s => s.Entity == StatusDomains.OrderStatus && SlaRules.RunningStatuses.Contains(s.InternalCode)).ToDictionaryAsync(s => s.StatusCodeId, s => s.InternalCode, ct);
        var ids = running.Keys.ToList();
        var orders = await db.TransportOrders.AsNoTracking().Where(o => o.IsActive && ids.Contains(o.StatusCodeId) && o.ContractId != null).OrderBy(o => o.TransportOrderId).Take(MaxOrders)
            .Select(o => new SlaOrderInput(o.TransportOrderId, o.ContractId, o.ServiceTypeLookupId, o.CreatedAtUtc, o.ConfirmedAtUtc, null)).ToListAsync(ct);
        if (orders.Count == 0) return new AttentionProviderResult(0, Array.Empty<AttentionItemDto>(), null);
        var statusOf = await db.TransportOrders.AsNoTracking().Where(o => orders.Select(x => x.OrderId).Contains(o.TransportOrderId)).Select(o => new { o.TransportOrderId, o.StatusCodeId }).ToDictionaryAsync(o => o.TransportOrderId, o => running[o.StatusCodeId], ct);
        var results = await sla.EvaluateAsync(orders.Select(o => o with { StatusCode = statusOf.GetValueOrDefault(o.OrderId) }).ToList(), DateTime.UtcNow, ct);
        var risky = results.Values.Where(r => r.State is SlaStates.AtRisk or SlaStates.Breached).ToList();
        if (risky.Count == 0) return new AttentionProviderResult(0, Array.Empty<AttentionItemDto>(), null);
        var breached = risky.Count(r => r.State == SlaStates.Breached);
        var minHours = risky.Min(r => r.HoursLeft ?? 0);
        var oldestDue = risky.Where(r => r.DueUtc != null).Min(r => r.DueUtc);
        var parameters = new Dictionary<string, string>
        {
            ["atRisk"] = (risky.Count - breached).ToString(CultureInfo.InvariantCulture),
            ["breached"] = breached.ToString(CultureInfo.InvariantCulture),
            ["minHours"] = minHours.ToString("0.#", CultureInfo.InvariantCulture),
        };
        var item = new AttentionItemDto(ItemCode, BusinessModules.Operations, breached > 0 ? "danger" : "warn", risky.Count, parameters, Route, null, oldestDue, Summary: true);
        return new AttentionProviderResult(1, new[] { item }, new AttentionGroupDto(ItemCode, BusinessModules.Operations, 1, Route, null));
    }
}
