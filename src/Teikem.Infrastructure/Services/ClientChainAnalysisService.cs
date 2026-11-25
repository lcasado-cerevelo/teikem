using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Clients;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Clients;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;
using CR = Teikem.Domain.Clients.ClientChainRules;
using R = Teikem.Domain.Sales.SalesAnalysisRules;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 245 (Lote B del plan de rutas, cadenas y precios) — «Análisis de ventas › Por cadena» (sales.analysis.view, módulo SALES_ORDERS). Es una vista MÁS junto a «Por
/// ruta» (la de por omisión) y «Por representante»: no toca <see cref="SalesAnalysisService"/>. Una cadena no es una ruta ni cambia la ruta del cliente.
/// - Lee las líneas con <see cref="SalesAnalysisLineReader"/> (historial antes de la fecha de corte, facturas y notas de crédito de Teikem desde ella) y agrega en memoria
///   con las reglas puras de <see cref="ClientChainRules"/>.
/// - La venta se atribuye a la cadena ACTUAL del cliente (<see cref="ClientChainQueries.ClientChainMapAsync"/>), también la del historial importado: al mover un cliente,
///   su historial se reagrupa. Venta = producto + descuentos; envío y otros cargos no cuentan. Las líneas sin cliente emparejado caen en «Sin cadena».
/// - Todo bajo el filtro de tenant (AsNoTracking): una cadena de otra compañía no existe (404 «Cadena no encontrada.»).
/// </summary>
public sealed class ClientChainAnalysisService(TeikemDbContext db, ITenantClock clock, SalesAnalysisLineReader reader)
{
    private sealed record ClientRow(int ClientId, Guid PublicId, string Code, string Name, bool IsActive);

    private static DateOnly RequireMonth(string? raw, DateOnly fallback, string field)
    {
        if (string.IsNullOrWhiteSpace(raw)) return fallback;
        return R.ParseMonth(raw) ?? throw new ValidationException(field, R.MonthInvalid);
    }

    private static NotFoundException ChainNotFound() => new("Cadena", feminine: true);   // «Cadena no encontrada.» (= ClientChainRules.NotFound)

    private async Task<ChainRef> RequireChainAsync(Guid publicId, CancellationToken ct)
        => await db.Set<ClientChain>().AsNoTracking().Where(x => x.PublicId == publicId)
               .Select(x => new ChainRef(x.ClientChainId, x.PublicId, x.Name, x.IsActive)).FirstOrDefaultAsync(ct)
           ?? throw ChainNotFound();

    /// <summary>
    /// Vista «Por cadena» del mes (por omisión el último mes completo) contra el de comparación (por omisión el anterior): una fila por cadena (las activas y las inactivas
    /// con venta) con su desglose por cliente, la fila «Sin cadena», totales y conciliación (cadenas + sin cadena = venta neta del mes). Con
    /// <paramref name="chainPublicId"/>: solo esa cadena con sus clientes, sin «Sin cadena» y con la conciliación del mes completo.
    /// </summary>
    public async Task<ClientChainSalesDto> GetSalesByChainAsync(string? month, string? compareMonth, Guid? chainPublicId, CancellationToken ct)
    {
        var m = RequireMonth(month, R.LastCompleteMonth(clock.Today), "month");
        var cm = RequireMonth(compareMonth, m.AddMonths(-1), "compareMonth");
        int? onlyChainId = chainPublicId is Guid g ? (await RequireChainAsync(g, ct)).Id : null;

        var from = m < cm ? m : cm;
        var to = (m > cm ? m : cm).AddMonths(1);
        var lines = await reader.LoadSalesAsync(from, to, ct);
        var settings = await reader.SettingsAsync(ct);

        var clientChain = await ClientChainQueries.ClientChainMapAsync(db, null, ct);
        var chains = await db.Set<ClientChain>().AsNoTracking()
            .Select(x => new ChainRef(x.ClientChainId, x.PublicId, x.Name, x.IsActive)).ToDictionaryAsync(x => x.Id, ct);
        var clients = await ClientsAsync(ct);

        var result = CR.SalesByChain(
            lines.Select(l => new ChainSaleLine(l.ClientId, l.ClientName, l.Date, l.Amount, l.LineClass)),
            clientChain,
            clients.ToDictionary(kv => kv.Key, kv => new ChainClientInfo(kv.Key, kv.Value.Name, kv.Value.IsActive)),
            chains.Values.Select(c => new ChainInfo(c.Id, c.Name, c.IsActive)),
            m, cm, onlyChainId);

        ClientChainSalesRowDto Row(ChainSalesRow r) => new(
            r.ChainId is int id && chains.TryGetValue(id, out var ch) ? (Guid?)ch.PublicId : null, r.ChainName, r.IsActive, r.ClientCount, r.ClientsWithSales,
            r.CompareSales, r.Sales, r.Change, r.ChangePct, r.ShareOfNet,
            r.Clients.Select(c => new ClientChainSalesClientDto(
                c.ClientId is int cid && clients.TryGetValue(cid, out var info) ? (Guid?)info.PublicId : null, c.ClientName, c.CompareSales, c.Sales, c.Change, c.ChangePct,
                c.ShareOfChain)).ToList());

        var t = result.Totals;
        var rec = result.Reconciliation;
        return new ClientChainSalesDto(R.MonthKey(m), R.MonthKey(cm), settings.CutoffDate, result.Rows.Select(Row).ToList(),
            result.NoChain is null ? null : Row(result.NoChain),
            new ClientChainSalesTotalsDto(t.CompareSales, t.Sales, t.Change, t.ChangePct),
            new ClientChainReconciliationDto(rec.ChainsTotal, rec.NoChain, rec.MonthNet, rec.Difference));
    }

    /// <summary>
    /// Venta neta por mes de los clientes de una cadena (los que están en ella HOY, activos o no) de <paramref name="fromMonth"/> a <paramref name="toMonth"/> (por
    /// omisión los 12 meses que terminan en el último mes completo; a lo sumo 36) y sus 5 clientes principales en el rango con su participación.
    /// </summary>
    public async Task<ClientChainSeriesDto> GetChainSeriesAsync(Guid? chainPublicId, string? fromMonth, string? toMonth, CancellationToken ct)
    {
        if (chainPublicId is not Guid g) throw new ValidationException("chainPublicId", CR.ChainRequired);
        var to = RequireMonth(toMonth, R.LastCompleteMonth(clock.Today), "toMonth");
        var from = RequireMonth(fromMonth, to.AddMonths(-11), "fromMonth");
        if (to < from) throw new ValidationException("toMonth", R.MonthRangeInvalid);
        if (R.Months(from, to).Count > R.MaxMonthsRange) throw new ValidationException("fromMonth", R.MonthRangeTooLong);
        var chain = await RequireChainAsync(g, ct);

        var members = await db.Clients.AsNoTracking().Where(c => c.ChainId == chain.Id).Select(c => c.ClientId).ToListAsync(ct);
        var lines = members.Count == 0 ? new List<SalesAnalysisLine>() : await reader.LoadSalesAsync(from, to.AddMonths(1), ct);
        var series = CR.Series(lines.Select(l => new ChainSaleLine(l.ClientId, l.ClientName, l.Date, l.Amount, l.LineClass)), members.ToHashSet(), from, to);

        var topIds = series.TopClients.Select(c => c.ClientId).ToList();
        var info = topIds.Count == 0
            ? new Dictionary<int, ClientRow>()
            : await db.Clients.AsNoTracking().Where(c => topIds.Contains(c.ClientId))
                .Select(c => new ClientRow(c.ClientId, c.PublicId, c.Code, c.Name, c.IsActive)).ToDictionaryAsync(c => c.ClientId, ct);
        var top = series.TopClients.Where(c => info.ContainsKey(c.ClientId))
            .Select(c => new ClientChainTopClientDto(info[c.ClientId].PublicId, info[c.ClientId].Code, info[c.ClientId].Name, c.Sales, c.Share)).ToList();
        return new ClientChainSeriesDto(chain.PublicId, chain.Name, R.MonthKey(from), R.MonthKey(to),
            series.Months.Select(x => new ClientChainMonthDto(R.MonthKey(x.Month), x.Sales)).ToList(), top);
    }

    /// <summary>Los clientes de la compañía (activos e inactivos) con lo que la vista necesita.</summary>
    private async Task<Dictionary<int, ClientRow>> ClientsAsync(CancellationToken ct)
        => await db.Clients.AsNoTracking()
            .Select(c => new ClientRow(c.ClientId, c.PublicId, c.Code, c.Name, c.IsActive)).ToDictionaryAsync(c => c.ClientId, ct);
}
