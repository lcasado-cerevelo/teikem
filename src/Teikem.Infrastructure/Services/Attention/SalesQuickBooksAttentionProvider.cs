using System.Globalization;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 225 (plan §1.7) — UNA fila de «Necesita tu atención»: «Facturas por pasar a QuickBooks: N» para quien tiene sales.quickbooks.view, con el módulo de órdenes de venta.
/// Una fila resumen (Summary). Tono warn; danger si la más vieja lleva más de 7 días esperando. «Revisar» abre Órdenes de venta en la pestaña QuickBooks.
/// Params: count, exported (cuántas ya salieron en un archivo sin confirmar), oldest (AAAA-MM-DD), days.
/// </summary>
public sealed class SalesQuickBooksAttentionProvider(QuickBooksService quickBooks, ITenantClock clock) : IAttentionItemProvider
{
    public const string ItemCode = "SALES_QUICKBOOKS_PENDING";
    public const string Route = "/sales-orders";
    public const int LongWaitDays = 7;

    public string Code => ItemCode;
    public string BusinessModule => BusinessModules.Accounting;
    public string? TenantModule => ModuleKeys.SalesOrders;
    public string RequiredPermission => PermissionCatalog.SalesQuickBooksView;

    public async Task<AttentionProviderResult> ReadAsync(AttentionScope scope, int take, CancellationToken ct)
    {
        var (count, exported, oldest) = await quickBooks.CountPendingAsync(ct);
        if (count == 0) return new AttentionProviderResult(0, Array.Empty<AttentionItemDto>(), null);
        var days = oldest is DateOnly o ? Math.Max(0, clock.Today.DayNumber - o.DayNumber) : 0;
        var query = new Dictionary<string, string> { ["tab"] = "quickbooks" };
        var items = take <= 0 ? Array.Empty<AttentionItemDto>() : new[]
        {
            new AttentionItemDto(ItemCode, BusinessModules.Accounting, days > LongWaitDays ? "danger" : "warn", count,
                new Dictionary<string, string>
                {
                    ["count"] = count.ToString(CultureInfo.InvariantCulture), ["exported"] = exported.ToString(CultureInfo.InvariantCulture),
                    ["oldest"] = oldest?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty, ["days"] = days.ToString(CultureInfo.InvariantCulture),
                },
                Route, query, oldest is DateOnly od ? clock.StartOfDayUtc(od) : clock.UtcNow, Summary: true),
        };
        return new AttentionProviderResult(1, items, new AttentionGroupDto(ItemCode, BusinessModules.Accounting, 1, Route, query));
    }
}
