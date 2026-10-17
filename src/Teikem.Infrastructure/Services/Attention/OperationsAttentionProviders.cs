using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Cod;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 89 (P12) — fila de «Necesita tu atención»: órdenes listas (en almacén o planificadas) que todavía no tienen una ruta con chofer. Es una fila AGREGADA (una sola, con la cuenta; `Summary`: va antes que las filas por registro), como en la maqueta
/// («83 órdenes sin chofer · Asignar»). Módulo LTL_GROUND y trips.dispatch (quien las asigna). Tono: warn; danger si la más antigua lleva 2 días o más. «Revisar» abre la Sala de despacho.
/// Params (cadenas): count (órdenes) y oldestDays (días de la más antigua).
/// </summary>
public sealed class OrdersNoDriverAttentionProvider(PulseRiverService rivers) : IAttentionItemProvider
{
    public const string ItemCode = "ORDERS_NO_DRIVER";
    public const string Route = "/ops/dispatch";
    public const int UrgentDays = 2;

    public string Code => ItemCode;
    public string BusinessModule => BusinessModules.Operations;
    public string? TenantModule => ModuleKeys.LtlGround;
    public string RequiredPermission => PermissionCatalog.TripsDispatch;

    public async Task<AttentionProviderResult> ReadAsync(AttentionScope scope, int take, CancellationToken ct)
    {
        var orders = await rivers.UnassignedQueryAsync(ct);
        var count = await orders.CountAsync(ct);
        if (count == 0 || take <= 0) return new AttentionProviderResult(0, Array.Empty<AttentionItemDto>(), null);
        var oldest = await orders.MinAsync(o => o.CreatedAtUtc, ct);
        var days = Math.Max(0, (int)(DateTime.UtcNow - oldest).TotalDays);
        var parameters = new Dictionary<string, string>
        {
            ["count"] = count.ToString(CultureInfo.InvariantCulture),
            ["oldestDays"] = days.ToString(CultureInfo.InvariantCulture),
        };
        var item = new AttentionItemDto(ItemCode, BusinessModules.Operations, days >= UrgentDays ? "danger" : "warn", count, parameters, Route, null, oldest, Summary: true);
        return new AttentionProviderResult(1, new[] { item }, new AttentionGroupDto(ItemCode, BusinessModules.Operations, 1, Route, null));
    }
}

/// <summary>
/// Lote 89 (P12) — fila de «Necesita tu atención»: dinero COD cobrado en la calle que todavía no se cuadra contra las hojas. Fila AGREGADA (una sola: el monto y cuántos cobros). Módulo COD y cod.reconcile.
/// Tono: warn; danger si el cobro más viejo lleva 3 días o más. «Revisar» abre COD en la pestaña Reconciliar. Params (cadenas): amount (con punto decimal), collections, orders y oldestDays.
/// </summary>
public sealed class CodUnreconciledAttentionProvider(TeikemDbContext db) : IAttentionItemProvider
{
    public const string ItemCode = "COD_UNRECONCILED";
    public const string Route = "/money/cod";
    public const int UrgentDays = 3;

    public string Code => ItemCode;
    public string BusinessModule => BusinessModules.Accounting;
    public string? TenantModule => ModuleKeys.Cod;
    public string RequiredPermission => PermissionCatalog.CodReconcile;

    public async Task<AttentionProviderResult> ReadAsync(AttentionScope scope, int take, CancellationToken ct)
    {
        if (take <= 0) return new AttentionProviderResult(0, Array.Empty<AttentionItemDto>(), null);
        var collectedId = await db.StatusCodes.AsNoTracking().Where(s => s.Entity == CodCodes.CollectionStatusDomain && s.InternalCode == CodCollectionStatuses.Collected).Select(s => (int?)s.StatusCodeId).FirstOrDefaultAsync(ct);
        if (collectedId is null) return new AttentionProviderResult(0, Array.Empty<AttentionItemDto>(), null);
        var rows = await db.Set<CodCollection>().AsNoTracking().Where(c => c.StatusCodeId == collectedId).Select(c => new { c.AmountCollected, c.CollectedAtUtc, c.TransportOrderId }).ToListAsync(ct);
        if (rows.Count == 0) return new AttentionProviderResult(0, Array.Empty<AttentionItemDto>(), null);
        var oldest = rows.Min(r => r.CollectedAtUtc);
        var days = Math.Max(0, (int)(DateTime.UtcNow - oldest).TotalDays);
        var parameters = new Dictionary<string, string>
        {
            ["amount"] = rows.Sum(r => r.AmountCollected).ToString("0.00", CultureInfo.InvariantCulture),
            ["collections"] = rows.Count.ToString(CultureInfo.InvariantCulture),
            ["orders"] = rows.Select(r => r.TransportOrderId).Distinct().Count().ToString(CultureInfo.InvariantCulture),
            ["oldestDays"] = days.ToString(CultureInfo.InvariantCulture),
        };
        var query = new Dictionary<string, string> { ["tab"] = "reconcile" };
        var item = new AttentionItemDto(ItemCode, BusinessModules.Accounting, days >= UrgentDays ? "danger" : "warn", rows.Count, parameters, Route, query, oldest, Summary: true);
        return new AttentionProviderResult(1, new[] { item }, new AttentionGroupDto(ItemCode, BusinessModules.Accounting, 1, Route, query));
    }
}
