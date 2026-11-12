using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Constants;
using Teikem.Domain.Migration;
using Teikem.Domain.Sales;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;
using R = Teikem.Domain.Sales.SalesAnalysisRules;
using C = Teikem.Domain.Sales.SalesAnalysisCodes;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 241 — vistas del «Análisis de ventas» (sales.analysis.view, módulo SALES_ORDERS): «Por representante», transacciones del mes, «Movimiento de productos», series para
/// gráficos y estado del historial. Lee las líneas con <see cref="SalesAnalysisLineReader"/> (historial antes de la fecha de corte, facturas de Teikem desde ella) y agrega en
/// memoria (un año de facturas y 13 mil líneas de historial son decenas de miles de filas proyectadas: pocas décimas).
/// - La venta es del representante ASIGNADO AL CLIENTE hoy (Client.SalesRepId): el historial no trae representante y se atribuye al actual (límite documentado). Fecha de
///   factura, por mes. «Venta» del representante = producto + descuentos (la venta neta); envío y otros cargos van aparte en la conciliación.
/// - Línea de un producto = la de su marca en la tabla marca → línea; marca sin línea = su propia marca; sin marca = «Sin marca».
/// </summary>
public sealed class SalesAnalysisService(TeikemDbContext db, ITenantClock clock, SalesAnalysisLineReader reader)
{
    // ================================================================ contexto (representantes, rutas, cuotas, líneas)

    private sealed record RepInfo(int Id, string Name, int? RouteId, bool IsActive);
    private sealed record RouteInfo(int Id, string Name, int SortOrder);

    private sealed class Ctx
    {
        public Dictionary<int, RepInfo> Reps { get; init; } = new();
        public Dictionary<int, RouteInfo> Routes { get; init; } = new();
        public Dictionary<int, int?> ClientRep { get; init; } = new();
        public Dictionary<int, List<(DateOnly Month, decimal Amount)>> Quotas { get; init; } = new();
        public Dictionary<string, string> BrandLine { get; init; } = new(StringComparer.Ordinal);
        public Dictionary<int, SalesAnalysisProduct> Products { get; init; } = new();
        public SalesAnalysisSetting Settings { get; init; } = new();

        public RepInfo? RepOf(SalesAnalysisLine l) => l.ClientId is int c && ClientRep.TryGetValue(c, out var r) && r is int rid ? Reps.GetValueOrDefault(rid) : null;
        public string RouteName(int? routeId) => routeId is int r && Routes.TryGetValue(r, out var x) ? x.Name : R.NoRouteName;
        public int RouteSort(int? routeId) => routeId is int r && Routes.TryGetValue(r, out var x) ? x.SortOrder : int.MaxValue;

        public string LineOfProduct(int? productId)
        {
            if (productId is not int pid) return R.UnmatchedLine;
            var brand = Products.GetValueOrDefault(pid)?.Brand;
            if (string.IsNullOrWhiteSpace(brand)) return R.NoBrandLine;
            return BrandLine.TryGetValue(R.NameKey(brand), out var line) ? line : brand.Trim();
        }

        public string LineOf(SalesAnalysisLine l) => l.LineClass == C.ClassDiscount ? R.DiscountsLine : LineOfProduct(l.ProductId);

        public decimal? Quota(int repId, DateOnly month) => Quotas.TryGetValue(repId, out var q) ? R.EffectiveQuota(q, month) : null;
    }

    private async Task<Ctx> ContextAsync(CancellationToken ct)
    {
        var routes = await db.Set<SalesRoute>().AsNoTracking().ToDictionaryAsync(r => r.SalesRouteId, r => new RouteInfo(r.SalesRouteId, r.Name, r.SortOrder), ct);
        var reps = await db.Set<SalesRep>().AsNoTracking().ToDictionaryAsync(r => r.SalesRepId, r => new RepInfo(r.SalesRepId, r.Name, r.SalesRouteId, r.IsActive), ct);
        var clientRep = await db.Clients.AsNoTracking().Where(c => c.SalesRepId != null).Select(c => new { c.ClientId, c.SalesRepId }).ToDictionaryAsync(c => c.ClientId, c => c.SalesRepId, ct);
        var quotas = (await db.Set<SalesQuota>().AsNoTracking().Where(q => q.IsActive).Select(q => new { q.SalesRepId, q.Month, q.Amount }).ToListAsync(ct))
            .GroupBy(q => q.SalesRepId).ToDictionary(g => g.Key, g => g.Select(q => (q.Month, q.Amount)).ToList());
        var lines = await db.Set<ProductLine>().AsNoTracking().Where(l => l.IsActive).ToDictionaryAsync(l => l.ProductLineId, l => l.Name, ct);
        var brandLine = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var b in await db.Set<ProductLineBrand>().AsNoTracking().ToListAsync(ct))
            if (lines.TryGetValue(b.ProductLineId, out var name)) brandLine[R.NameKey(b.Brand)] = name;
        return new Ctx
        {
            Routes = routes, Reps = reps, ClientRep = clientRep, Quotas = quotas, BrandLine = brandLine,
            Products = await reader.ProductsAsync(ct), Settings = await reader.SettingsAsync(ct),
        };
    }

    private static decimal M2(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);

    private static DateOnly RequireMonth(string? raw, DateOnly fallback, string field)
    {
        if (string.IsNullOrWhiteSpace(raw)) return fallback;
        return R.ParseMonth(raw) ?? throw new ValidationException(field, R.MonthInvalid);
    }

    // ================================================================ «Por representante»

    /// <summary>Vista «Por representante» del mes (por omisión el último mes completo) contra el de comparación (por omisión el anterior).</summary>
    public async Task<SalesByRepDto> GetByRepAsync(string? month, string? compareMonth, CancellationToken ct)
    {
        var m = RequireMonth(month, R.LastCompleteMonth(clock.Today), "month");
        var cm = RequireMonth(compareMonth, m.AddMonths(-1), "compareMonth");
        var x = await ContextAsync(ct);
        var from = m < cm ? m : cm;
        var to = (m > cm ? m : cm).AddMonths(1);
        var lines = await reader.LoadSalesAsync(from, to, ct);
        return BuildByRep(x, lines, m, cm, await WarningsAsync(x, from, to, ct));
    }

    private static SalesByRepDto BuildByRep(Ctx x, List<SalesAnalysisLine> all, DateOnly m, DateOnly cm, List<string> warnings)
    {
        var inM = all.Where(l => R.MonthStart(l.Date) == m).ToList();
        var inC = all.Where(l => R.MonthStart(l.Date) == cm).ToList();
        var netM = inM.Where(l => R.CountsForRep(l.LineClass)).ToList();
        var netC = inC.Where(l => R.CountsForRep(l.LineClass)).ToList();
        int RepKey(SalesAnalysisLine l) => x.RepOf(l)?.Id ?? 0;
        var salesM = netM.GroupBy(RepKey).ToDictionary(g => g.Key, g => g.Sum(l => l.Amount));
        var salesC = netC.GroupBy(RepKey).ToDictionary(g => g.Key, g => g.Sum(l => l.Amount));

        var repIds = x.Reps.Values.Where(r => r.IsActive).Select(r => r.Id).Concat(salesM.Keys).Concat(salesC.Keys).Where(id => id > 0).Distinct()
            .Select(id => x.Reps[id]).OrderBy(r => x.RouteSort(r.RouteId)).ThenBy(r => x.RouteName(r.RouteId), StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        var rows = repIds.Select(r =>
        {
            var s = salesM.GetValueOrDefault(r.Id);
            var c = salesC.GetValueOrDefault(r.Id);
            var quota = x.Quota(r.Id, m);
            return new SalesRepRowDto(r.RouteId, x.RouteName(r.RouteId), r.Id, r.Name, M2(c), M2(s), M2(s - c), R.ChangePct(c, s), quota, R.Compliance(s, quota));
        }).ToList();
        var tc = rows.Sum(r => r.CompareSales);
        var ts = rows.Sum(r => r.Sales);
        var tq = rows.Sum(r => r.Quota ?? 0m);
        var totals = new SalesRepTotalsDto(tc, ts, M2(ts - tc), R.ChangePct(tc, ts), tq, R.Compliance(ts, tq));

        var product = inM.Where(l => l.LineClass == C.ClassProduct).Sum(l => l.Amount);
        var discounts = inM.Where(l => l.LineClass == C.ClassDiscount).Sum(l => l.Amount);
        var shipping = inM.Where(l => l.LineClass == C.ClassShipping).Sum(l => l.Amount);
        var other = inM.Where(l => l.LineClass == C.ClassOther).Sum(l => l.Amount);
        var unassigned = salesM.GetValueOrDefault(0);
        var rec = R.Reconcile(ts, M2(unassigned), M2(shipping + other), M2(inM.Sum(l => l.Amount)));
        var unassignedClients = netM.Where(l => RepKey(l) == 0).Select(l => l.ClientId is int c ? "C" + c : "N" + R.NameKey(l.ClientName)).Distinct().Count();
        var reconciliation = new SalesReconciliationDto(rec.RoutesTotal, rec.Unassigned, rec.ShippingAndOther, rec.MonthTotal, rec.Difference, M2(product), M2(discounts),
            M2(shipping), M2(other), unassignedClients, M2(salesC.GetValueOrDefault(0)));

        // por línea (producto) y los descuentos sin línea aparte
        var net = product + discounts;
        var byLine = inM.Where(l => l.LineClass == C.ClassProduct).GroupBy(x.LineOf).Select(g => (Line: g.Key, Sales: g.Sum(l => l.Amount), Lines: g.ToList()))
            .OrderByDescending(g => g.Sales).ThenBy(g => g.Line, StringComparer.CurrentCultureIgnoreCase).ToList();
        var lineDtos = byLine.Select(g =>
        {
            var leader = g.Lines.Where(l => x.RepOf(l) is not null).GroupBy(l => x.RepOf(l)!.Id).Select(r => (Rep: x.Reps[r.Key], Sales: r.Sum(l => l.Amount)))
                .OrderByDescending(r => r.Sales).ThenBy(r => r.Rep.Name, StringComparer.CurrentCultureIgnoreCase).Select(r => ((RepInfo, decimal)?)r).FirstOrDefault();
            return new SalesLineShareDto(g.Line, false, M2(g.Sales), R.Share(g.Sales, net), R.Share(g.Sales, product), leader?.Item1.Id, leader?.Item1.Name,
                leader is null ? null : x.RouteName(leader.Value.Item1.RouteId), leader is null ? null : M2(leader.Value.Item2));
        }).ToList();
        if (inM.Any(l => l.LineClass == C.ClassDiscount))
            lineDtos.Add(new SalesLineShareDto(R.DiscountsLine, true, M2(discounts), R.Share(discounts, net), R.Share(discounts, product), null, null, null, null));
        var lineNames = lineDtos.Select(l => (l.Line, l.IsDiscounts)).ToList();

        // grupos de representante (las filas y, si vendió, «Sin representante»)
        var groups = repIds.Select(r => (Key: r.Id, Route: x.RouteName(r.RouteId), RouteId: r.RouteId, Name: r.Name)).ToList();
        if (netM.Any(l => RepKey(l) == 0)) groups.Add((0, R.NoRepName, null, R.NoRepName));

        var repLines = new List<SalesRepLineDto>();
        foreach (var g in groups)
        {
            var mine = netM.Where(l => RepKey(l) == g.Key).ToList();
            var total = mine.Sum(l => l.Amount);
            foreach (var (line, isDisc) in lineNames)
            {
                var s = mine.Where(l => x.LineOf(l) == line).Sum(l => l.Amount);
                repLines.Add(new SalesRepLineDto(g.Route, g.Key == 0 ? null : g.Key, g.Name, line, isDisc, M2(s), R.Share(s, total)));
            }
        }

        var repProducts = netM.GroupBy(l => (Rep: RepKey(l), Key: SalesAnalysisLineReader.ProductKey(l.ProductId, l.Sku), l.LineClass))
            .Select(g =>
            {
                var first = g.First();
                var rep = g.Key.Rep == 0 ? null : x.Reps[g.Key.Rep];
                return new SalesRepProductDto(rep is null ? R.NoRepName : x.RouteName(rep.RouteId), rep?.Id, rep?.Name ?? R.NoRepName, x.LineOf(first), first.ProductId,
                    first.Sku, first.Item, g.Key.LineClass, g.Sum(l => l.Quantity), M2(g.Sum(l => l.Amount)));
            })
            .OrderBy(r => groups.FindIndex(gr => gr.Key == (r.RepId ?? 0))).ThenBy(r => r.LineClass == C.ClassDiscount).ThenBy(r => r.Sku, StringComparer.OrdinalIgnoreCase).ToList();

        // ruta × producto: todos los productos vendidos en el mes para cada ruta (0 donde no se vendió)
        int RouteKey(SalesAnalysisLine l) => x.RepOf(l) is RepInfo r ? r.RouteId ?? 0 : -1;
        var soldProducts = inM.Where(l => l.LineClass == C.ClassProduct).GroupBy(l => SalesAnalysisLineReader.ProductKey(l.ProductId, l.Sku))
            .Select(g => g.First()).OrderBy(l => x.LineOf(l), StringComparer.CurrentCultureIgnoreCase).ThenBy(l => l.Sku, StringComparer.OrdinalIgnoreCase).ToList();
        var routeGroups = groups.Where(g => g.Key != 0).GroupBy(g => g.RouteId ?? 0).Select(g => (Key: g.Key, RouteId: g.First().RouteId, Route: g.First().Route,
            Reps: string.Join(", ", g.Select(r => r.Name)))).ToList();
        if (netM.Any(l => RouteKey(l) == -1)) routeGroups.Add((-1, null, R.NoRepName, R.NoRepName));
        var byRouteProduct = inM.Where(l => l.LineClass == C.ClassProduct)
            .GroupBy(l => (Route: RouteKey(l), Key: SalesAnalysisLineReader.ProductKey(l.ProductId, l.Sku)))
            .ToDictionary(g => g.Key, g => (Units: g.Sum(l => l.Quantity), Sales: g.Sum(l => l.Amount)));
        var routeProducts = new List<SalesRouteProductDto>();
        foreach (var rg in routeGroups)
            foreach (var p in soldProducts)
            {
                var v = byRouteProduct.GetValueOrDefault((rg.Key, SalesAnalysisLineReader.ProductKey(p.ProductId, p.Sku)));
                routeProducts.Add(new SalesRouteProductDto(rg.RouteId, rg.Route, rg.Reps, x.LineOf(p), p.ProductId, p.Sku, p.Item, v.Units, M2(v.Sales)));
            }

        return new SalesByRepDto(R.MonthKey(m), R.MonthKey(cm), x.Settings.CutoffDate, warnings, rows, totals, reconciliation, lineDtos, repLines, repProducts, routeProducts);
    }

    // ================================================================ transacciones del mes

    /// <summary>Líneas del mes (todas las clases), filtrables por representante (0 = sin representante) y texto; paginadas o completas (all = true, para exportar).</summary>
    public async Task<PagedResult<SalesTransactionDto>> GetTransactionsAsync(string? month, int? repId, string? q, int skip, int take, bool all, CancellationToken ct)
    {
        var m = RequireMonth(month, R.LastCompleteMonth(clock.Today), "month");
        if (skip < 0) throw new ValidationException("skip", R.PageInvalid);
        if (!all && (take < 1 || take > R.MaxPageSize)) throw new ValidationException("take", R.PageSizeInvalid);
        var x = await ContextAsync(ct);
        var lines = await reader.LoadSalesAsync(m, m.AddMonths(1), ct);
        IEnumerable<SalesAnalysisLine> filtered = lines;
        if (repId is int rid) filtered = filtered.Where(l => (x.RepOf(l)?.Id ?? 0) == rid);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var s = q.Trim();
            filtered = filtered.Where(l => l.ClientName.Contains(s, StringComparison.OrdinalIgnoreCase) || l.Sku.Contains(s, StringComparison.OrdinalIgnoreCase)
                || l.Item.Contains(s, StringComparison.OrdinalIgnoreCase) || (l.Number?.Contains(s, StringComparison.OrdinalIgnoreCase) ?? false));
        }
        var ordered = filtered.OrderBy(l => l.Date).ThenBy(l => l.Number, StringComparer.Ordinal).ThenBy(l => l.Sku, StringComparer.OrdinalIgnoreCase).ToList();
        var page = all ? ordered : ordered.Skip(skip).Take(take).ToList();
        var items = page.Select(l =>
        {
            var rep = x.RepOf(l);
            return new SalesTransactionDto(l.Date, l.Source, l.Type, l.Number, l.ClientId, l.ClientName, rep?.Id, rep?.Name, rep is null ? null : x.RouteName(rep.RouteId),
                l.ProductId, l.Sku, l.Item, l.LineClass is C.ClassProduct or C.ClassDiscount ? x.LineOf(l) : string.Empty, l.LineClass, l.Quantity, l.UnitPrice, l.Amount);
        }).ToList();
        return new PagedResult<SalesTransactionDto>(items, ordered.Count, all ? 0 : skip, all ? ordered.Count : take);
    }

    /// <summary>Las líneas de [desde, hasta exclusivo) ya con representante, ruta y línea (para la fuente de datos de Vistas, Indicadores y Gráficos).</summary>
    public async Task<IReadOnlyList<(SalesTransactionDto Line, string? Brand)>> LinesForRangeAsync(DateOnly from, DateOnly toExclusive, CancellationToken ct)
    {
        var x = await ContextAsync(ct);
        var lines = await reader.LoadSalesAsync(from, toExclusive, ct);
        return lines.OrderByDescending(l => l.Date).Select(l =>
        {
            var rep = x.RepOf(l);
            var dto = new SalesTransactionDto(l.Date, l.Source, l.Type, l.Number, l.ClientId, l.ClientName, rep?.Id, rep?.Name, rep is null ? null : x.RouteName(rep.RouteId),
                l.ProductId, l.Sku, l.Item, l.LineClass is C.ClassProduct or C.ClassDiscount ? x.LineOf(l) : string.Empty, l.LineClass, l.Quantity, l.UnitPrice, l.Amount);
            return (dto, l.ProductId is int pid ? x.Products.GetValueOrDefault(pid)?.Brand : null);
        }).ToList();
    }

    // ================================================================ «Movimiento de productos»

    private sealed record MovementParams(DateOnly From, DateOnly To, DateOnly AsOf, int LeadTime, decimal Z, int TopClients);

    private async Task<MovementParams> MovementParamsAsync(string? fromMonth, string? toMonth, DateOnly? asOf, int? leadTimeDays, decimal? z, int? topClients, CancellationToken ct)
    {
        var settings = await reader.SettingsAsync(ct);
        var day = asOf ?? clock.Today;
        var to = RequireMonth(toMonth, R.MonthStart(day), "toMonth");
        var from = RequireMonth(fromMonth, to.AddMonths(-11), "fromMonth");
        if (to < from) throw new ValidationException("toMonth", R.MonthRangeInvalid);
        if (R.Months(from, to).Count > R.MaxMonthsRange) throw new ValidationException("fromMonth", R.MonthRangeTooLong);
        var lead = leadTimeDays ?? settings.LeadTimeDays;
        if (lead < 1 || lead > R.MaxLeadTimeDays) throw new ValidationException("leadTimeDays", R.LeadTimeInvalid);
        var zz = z ?? settings.ServiceLevelZ;
        if (zz <= 0m || zz > R.MaxServiceLevelZ) throw new ValidationException("serviceLevelZ", R.ServiceLevelInvalid);
        return new MovementParams(from, to, day, lead, zz, Math.Clamp(topClients ?? 5, 1, 50));
    }

    /// <summary>Vista «Movimiento de productos» de [desde, hasta] (meses; por omisión los 12 meses que terminan en el del día de corte), al día de corte (por omisión hoy).</summary>
    public async Task<ProductMovementDto> GetMovementAsync(string? fromMonth, string? toMonth, DateOnly? asOf, int? leadTimeDays, decimal? serviceLevelZ, int? topClients,
        CancellationToken ct)
    {
        var p = await MovementParamsAsync(fromMonth, toMonth, asOf, leadTimeDays, serviceLevelZ, topClients, ct);
        var x = await ContextAsync(ct);
        var (wFrom, _) = R.WeekWindow(p.AsOf);
        var dataTo = MinDate(p.To.AddMonths(1), p.AsOf.AddDays(1));
        var loadFrom = MinDate(p.From, wFrom);
        var lines = await reader.LoadSalesAsync(loadFrom, dataTo, ct);
        var purchases = await reader.LoadPurchasesAsync(p.From, dataTo, ct);
        var stock = await StockAsync(ct);
        var pending = await PendingPurchasesAsync(ct);
        return BuildMovement(x, p, lines, purchases, stock, pending, await WarningsAsync(x, loadFrom, dataTo, ct));
    }

    private static DateOnly MinDate(DateOnly a, DateOnly b) => a < b ? a : b;

    private async Task<Dictionary<int, (decimal OnHand, decimal Reserved)>> StockAsync(CancellationToken ct)
        => (await db.StockBalances.AsNoTracking().GroupBy(b => b.ProductId)
                .Select(g => new { ProductId = g.Key, OnHand = g.Sum(b => b.QtyOnHand), Reserved = g.Sum(b => b.QtyReserved) }).ToListAsync(ct))
            .ToDictionary(r => r.ProductId, r => (r.OnHand, r.Reserved));

    /// <summary>Pendiente por recibir: lo pedido menos lo recibido de las órdenes de compra enviadas o recibidas en parte (activas).</summary>
    private async Task<Dictionary<int, decimal>> PendingPurchasesAsync(CancellationToken ct)
    {
        var open = await db.StatusCodes.AsNoTracking()
            .Where(s => s.Entity == StatusDomains.PurchaseOrderStatus && (s.InternalCode == PurchaseOrderStatuses.Sent || s.InternalCode == PurchaseOrderStatuses.Partial))
            .Select(s => s.StatusCodeId).ToListAsync(ct);
        return (await (from l in db.PurchaseOrderLines.AsNoTracking()
                       join o in db.PurchaseOrders.AsNoTracking() on l.PurchaseOrderId equals o.PurchaseOrderId
                       where o.IsActive && open.Contains(o.StatusCodeId) && l.QtyOrdered > l.QtyReceived
                       group l by l.ProductId into g
                       select new { ProductId = g.Key, Qty = g.Sum(l => l.QtyOrdered - l.QtyReceived) }).ToListAsync(ct))
            .ToDictionary(r => r.ProductId, r => r.Qty);
    }

    private sealed class ProductAgg
    {
        public int? ProductId;
        public string Sku = string.Empty;
        public string Name = string.Empty;
        public Dictionary<DateOnly, decimal> Units = new();
        public Dictionary<DateOnly, decimal> Sales = new();
        public decimal[] Weekly = new decimal[R.WeeksWindow];
        public decimal TotalUnits, TotalSales;
        public DateOnly? LastSale, FirstSaleMonth;
        public HashSet<string> Clients = new(StringComparer.Ordinal);
        public HashSet<string> Invoices = new(StringComparer.Ordinal);
        public Dictionary<string, (string Name, int? ClientId, decimal Units)> ByClient = new(StringComparer.Ordinal);
        public decimal GrossUnits;
    }

    private static ProductMovementDto BuildMovement(Ctx x, MovementParams p, List<SalesAnalysisLine> lines, List<SalesAnalysisPurchase> purchases,
        Dictionary<int, (decimal OnHand, decimal Reserved)> stock, Dictionary<int, decimal> pending, List<string> warnings)
    {
        var months = R.Months(p.From, p.To);
        var rangeFrom = p.From;
        var rangeTo = MinDate(p.To.AddMonths(1), p.AsOf.AddDays(1));
        var lastComplete = R.LastCompleteMonth(p.AsOf);
        var (wFrom, wTo) = R.WeekWindow(p.AsOf);
        var aggs = new Dictionary<string, ProductAgg>(StringComparer.Ordinal);

        ProductAgg Agg(int? productId, string sku, string name)
        {
            var key = SalesAnalysisLineReader.ProductKey(productId, sku);
            if (!aggs.TryGetValue(key, out var a)) aggs[key] = a = new ProductAgg { ProductId = productId, Sku = sku, Name = name };
            return a;
        }

        // universo: los productos propios activos que no son kits (un kit se reparte entre sus piezas)
        foreach (var prod in x.Products.Values.Where(pr => pr.IsActive && !pr.IsKit)) Agg(prod.ProductId, prod.Sku, prod.Name);

        foreach (var l in lines.Where(l => l.LineClass == C.ClassProduct))
        {
            var a = Agg(l.ProductId, l.Sku, l.Item);
            if (l.Date >= wFrom && l.Date < wTo) a.Weekly[(l.Date.DayNumber - wFrom.DayNumber) / 7] += l.Quantity;
            if (l.Date < rangeFrom || l.Date >= rangeTo) continue;
            var month = R.MonthStart(l.Date);
            a.Units[month] = a.Units.GetValueOrDefault(month) + l.Quantity;
            a.Sales[month] = a.Sales.GetValueOrDefault(month) + l.Amount;
            a.TotalUnits += l.Quantity;
            a.TotalSales += l.Amount;
            var ck = l.ClientId is int c ? "C" + c : "N" + R.NameKey(l.ClientName);
            if (l.Type.Equals("Invoice", StringComparison.OrdinalIgnoreCase))
            {
                // facturas y clientes: toda factura que lleva el producto (aunque la línea venga en 0), como la hoja «Resumen» del dueño
                a.Clients.Add(ck);
                if (l.Number is not null) a.Invoices.Add(l.Source + "|" + l.Number);
            }
            if (l.Quantity > 0m)
            {
                if (a.LastSale is null || l.Date > a.LastSale) a.LastSale = l.Date;
                if (a.FirstSaleMonth is null || month < a.FirstSaleMonth) a.FirstSaleMonth = month;
                var prev = a.ByClient.GetValueOrDefault(ck, (l.ClientName, l.ClientId, 0m));
                a.ByClient[ck] = (prev.Item1, prev.Item2, prev.Item3 + l.Quantity);
                a.GrossUnits += l.Quantity;
            }
        }

        var purchByKey = purchases.Where(pu => pu.Date >= rangeFrom && pu.Date < rangeTo).GroupBy(pu => SalesAnalysisLineReader.ProductKey(pu.ProductId, pu.Sku))
            .ToDictionary(g => g.Key, g => g.ToList());
        foreach (var pu in purchases.Where(pu => pu.ProductId is null && pu.Date >= rangeFrom && pu.Date < rangeTo)) Agg(null, pu.Sku, pu.Item);

        var abc = R.Abc(aggs.Select(kv => (kv.Key, kv.Value.TotalSales)));
        var totalSales = aggs.Values.Sum(a => a.TotalSales);
        var totalUnits = aggs.Values.Sum(a => a.TotalUnits);

        var rows = new List<ProductMovementRowDto>();
        var unitSeries = new List<ProductMonthSeriesDto>();
        var salesSeries = new List<ProductMonthSeriesDto>();
        var purchaseSeries = new List<ProductMonthSeriesDto>();
        var topClients = new List<ProductClientDto>();
        foreach (var (key, a) in aggs.OrderByDescending(kv => kv.Value.TotalSales).ThenByDescending(kv => kv.Value.TotalUnits).ThenBy(kv => kv.Value.Sku, StringComparer.OrdinalIgnoreCase))
        {
            var prod = a.ProductId is int pid ? x.Products.GetValueOrDefault(pid) : null;
            var (onHand, reserved) = a.ProductId is int sp ? stock.GetValueOrDefault(sp) : (0m, 0m);
            var available = onHand - reserved;
            var stats = R.MonthStats(a.Units, p.From, lastComplete);
            var week = R.WeekStats(a.Weekly);
            var trend = R.Trend(a.Units.GetValueOrDefault(lastComplete), a.Units.GetValueOrDefault(lastComplete.AddMonths(-1)), a.Units.GetValueOrDefault(lastComplete.AddMonths(-2)));
            var status = R.ProductStatus(a.LastSale, a.FirstSaleMonth, p.AsOf, trend);
            var price = prod?.Price;
            var cost = prod?.Cost;
            decimal? margin = price is decimal pr && pr > 0m && cost is decimal co ? Math.Round((pr - co) / pr, 4) : null;
            var top = a.ByClient.Values.OrderByDescending(v => v.Units).ThenBy(v => v.Name, StringComparer.CurrentCultureIgnoreCase).Select(v => ((string, int?, decimal)?)v).FirstOrDefault();
            rows.Add(new ProductMovementRowDto(a.ProductId, prod?.Sku ?? a.Sku, prod?.Name ?? a.Name, prod?.Category, prod?.Brand, x.LineOfProduct(a.ProductId), a.ProductId is not null,
                abc[key], status, price, cost, margin, onHand, reserved, available, a.TotalUnits, M2(a.TotalSales), R.Share(a.TotalSales, totalSales),
                stats.Average, stats.Min, stats.Max, stats.BestMonth is DateOnly bm ? R.MonthKey(bm) : null, stats.MonthsUsed,
                week.Average, week.StdDev, week.WeeksUsed, week.Confidence, R.CoverageWeeks(available, week.Average), R.CoverageMonths(available, week.Average),
                R.ProjectedBalance(available, week.Average, 1), R.ProjectedBalance(available, week.Average, 2), R.ProjectedBalance(available, week.Average, 3),
                prod?.MinQty, R.SuggestedReorderPoint(week.Average, week.StdDev, p.LeadTime, p.Z), trend,
                a.Clients.Count, a.Invoices.Count, top?.Item1, top?.Item3, a.LastSale,
                a.ProductId is int pp ? pending.GetValueOrDefault(pp) : 0m, a.Units.GetValueOrDefault(lastComplete)));

            var name = prod?.Name ?? a.Name;
            var sku = prod?.Sku ?? a.Sku;
            if (a.Units.Count > 0)
                unitSeries.Add(new ProductMonthSeriesDto(a.ProductId, sku, name, months.Select(mo => a.Units.GetValueOrDefault(mo)).ToList(), a.TotalUnits, stats.Min, stats.Max, stats.Average));
            if (a.Sales.Count > 0)
                salesSeries.Add(new ProductMonthSeriesDto(a.ProductId, sku, name, months.Select(mo => M2(a.Sales.GetValueOrDefault(mo))).ToList(), M2(a.TotalSales), null, null, null));
            if (purchByKey.TryGetValue(key, out var pl))
            {
                var byMonth = pl.GroupBy(pu => R.MonthStart(pu.Date)).ToDictionary(g => g.Key, g => g.Sum(pu => pu.Quantity));
                purchaseSeries.Add(new ProductMonthSeriesDto(a.ProductId, sku, name, months.Select(mo => byMonth.GetValueOrDefault(mo)).ToList(), pl.Sum(pu => pu.Quantity), null, null, null));
            }
            var rank = 0;
            foreach (var c in a.ByClient.Values.OrderByDescending(v => v.Units).ThenBy(v => v.Name, StringComparer.CurrentCultureIgnoreCase).Take(p.TopClients))
                topClients.Add(new ProductClientDto(a.ProductId, sku, name, ++rank, c.ClientId, c.Name, c.Units, R.Share(c.Units, a.GrossUnits)));
        }

        return new ProductMovementDto(R.MonthKey(p.From), R.MonthKey(p.To), p.AsOf, R.MonthKey(lastComplete), wFrom, wTo.AddDays(-1), p.LeadTime, p.Z,
            x.Settings.CutoffDate, months.Select(R.MonthKey).ToList(), warnings, totalUnits, M2(totalSales), rows, unitSeries, salesSeries, purchaseSeries, topClients);
    }

    // ================================================================ gráficos

    /// <summary>Series para graficar: rango de meses (por omisión los 12 que terminan en el último completo), mes de la vista por representante y los N productos principales.</summary>
    public async Task<SalesChartsDto> GetChartsAsync(string? fromMonth, string? toMonth, string? month, int? topProducts, CancellationToken ct)
    {
        var today = clock.Today;
        var to = RequireMonth(toMonth, R.LastCompleteMonth(today), "toMonth");
        var from = RequireMonth(fromMonth, to.AddMonths(-11), "fromMonth");
        if (to < from) throw new ValidationException("toMonth", R.MonthRangeInvalid);
        if (R.Months(from, to).Count > R.MaxMonthsRange) throw new ValidationException("fromMonth", R.MonthRangeTooLong);
        var m = RequireMonth(month, to, "month");
        var top = Math.Clamp(topProducts ?? 6, 1, 20);
        var x = await ContextAsync(ct);
        var settings = x.Settings;
        var mp = new MovementParams(from, to, today, settings.LeadTimeDays, settings.ServiceLevelZ, 1);
        var (wFrom, _) = R.WeekWindow(today);
        var loadFrom = new[] { from, m.AddMonths(-1), wFrom }.Min();
        var loadTo = new[] { to.AddMonths(1), m.AddMonths(1), today.AddDays(1) }.Max();
        var lines = await reader.LoadSalesAsync(loadFrom, loadTo, ct);
        var months = R.Months(from, to);
        var inRange = lines.Where(l => l.Date >= from && l.Date < to.AddMonths(1)).ToList();

        var total = new SalesChartSeriesDto("TOTAL", "Venta neta", months.Select(mo => M2(inRange.Where(l => R.MonthStart(l.Date) == mo && R.CountsForRep(l.LineClass)).Sum(l => l.Amount))).ToList());
        var byLine = inRange.Where(l => R.CountsForRep(l.LineClass)).GroupBy(x.LineOf)
            .Select(g => (Line: g.Key, Total: g.Sum(l => l.Amount), ByMonth: g.GroupBy(l => R.MonthStart(l.Date)).ToDictionary(mg => mg.Key, mg => mg.Sum(l => l.Amount))))
            .OrderByDescending(g => g.Total).Select(g => new SalesChartSeriesDto(g.Line, g.Line, months.Select(mo => M2(g.ByMonth.GetValueOrDefault(mo))).ToList())).ToList();

        var byRep = BuildByRep(x, lines, m, m.AddMonths(-1), new List<string>());
        var repVsQuota = byRep.Reps.Select(r => new SalesRepQuotaPointDto(r.RepId, r.RepName, r.RouteName, r.Sales, r.Quota, r.Compliance)).ToList();
        var lineShare = byRep.Lines.Where(l => !l.IsDiscounts).Select(l => new SalesLineSharePointDto(l.Line, l.Sales, l.ShareOfGross)).ToList();

        var movement = BuildMovement(x, mp, lines, new List<SalesAnalysisPurchase>(), await StockAsync(ct), new Dictionary<int, decimal>(), new List<string>());
        // los N principales por venta en $ del rango (como el gráfico del dueño), con sus unidades por mes
        var unitsBySku = movement.UnitsByMonth.GroupBy(s => (s.ProductId, s.Sku)).ToDictionary(g => g.Key, g => g.First());
        var topUnits = movement.Products.Where(p => p.Sales > 0m && unitsBySku.ContainsKey((p.ProductId, p.Sku))).Take(top)
            .Select(p => unitsBySku[(p.ProductId, p.Sku)]).Select(s => new SalesChartSeriesDto(s.Sku, s.Name, s.Values)).ToList();
        var coverage = movement.Products.Where(r => r.WeeklyAverage is not null).OrderBy(r => r.CoverageWeeks ?? decimal.MaxValue).ThenBy(r => r.Sku, StringComparer.OrdinalIgnoreCase)
            .Select(r => new SalesCoveragePointDto(r.ProductId, r.Sku, r.Name, r.Abc, r.Available, r.WeeklyAverage, r.CoverageWeeks)).ToList();
        return new SalesChartsDto(months.Select(R.MonthKey).ToList(), total, byLine, R.MonthKey(m), repVsQuota, lineShare, topUnits, coverage);
    }

    // ================================================================ estado

    /// <summary>Avisos de las fuentes: sin fecha de corte y con historial y facturas de Teikem en el mismo período (se suman las dos).</summary>
    private async Task<List<string>> WarningsAsync(Ctx x, DateOnly from, DateOnly toExclusive, CancellationToken ct)
    {
        var warnings = new List<string>();
        if (x.Settings.CutoffDate is not null) return warnings;
        var histMax = await db.Set<SalesHistoryLine>().AsNoTracking().Where(l => l.TxnDate >= from && l.TxnDate < toExclusive).MaxAsync(l => (DateOnly?)l.TxnDate, ct);
        if (histMax is null) return warnings;
        var invMin = await FirstInvoiceDateAsync(from, ct);
        if (invMin is DateOnly i && i <= histMax && i < toExclusive) warnings.Add(OverlapWarning(i, histMax.Value));
        return warnings;
    }

    public static string OverlapWarning(DateOnly firstInvoice, DateOnly lastHistory) =>
        $"No hay fecha de corte y el historial importado (hasta {lastHistory:yyyy-MM-dd}) se traslapa con las facturas de Teikem (desde {firstInvoice:yyyy-MM-dd}): " +
        "las ventas de esos días se pueden estar sumando dos veces. Ponga la fecha de corte en Ajustes del análisis.";

    private async Task<DateOnly?> FirstInvoiceDateAsync(DateOnly? from, CancellationToken ct)
    {
        var issued = await db.StatusCodes.AsNoTracking()
            .Where(s => s.Entity == Teikem.Domain.Billing.BillingCodes.InvoiceStatusDomain
                        && (s.InternalCode == Teikem.Domain.Billing.InvoiceStatuses.Issued || s.InternalCode == Teikem.Domain.Billing.InvoiceStatuses.Overdue
                            || s.InternalCode == Teikem.Domain.Billing.InvoiceStatuses.Paid))
            .Select(s => s.StatusCodeId).ToListAsync(ct);
        var q = db.Invoices.AsNoTracking().Where(i => i.IsActive && issued.Contains(i.StatusCodeId) && i.IssueDate != null && i.Lines.Any(l => l.SalesOrderLineId != null));
        if (from is DateOnly f) q = q.Where(i => i.IssueDate >= f);
        return await q.MinAsync(i => i.IssueDate, ct);
    }

    /// <summary>Estado del análisis: ajustes, lotes importados, clientes sin representante, marcas sin línea, nombres sin emparejar y traslape de fuentes.</summary>
    public async Task<SalesAnalysisStatusDto> GetStatusAsync(CancellationToken ct)
    {
        var settings = await reader.SettingsAsync(ct);
        var batches = await db.Set<SalesHistoryBatch>().AsNoTracking().OrderByDescending(b => b.ImportedAtUtc).Take(50).ToListAsync(ct);
        var userIds = batches.Where(b => b.ImportedBy is not null).Select(b => b.ImportedBy!.Value).Distinct().ToList();
        var users = userIds.Count == 0 ? new Dictionary<int, string>()
            : await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName ?? u.Email ?? string.Empty, ct);
        var activeClients = await db.Clients.AsNoTracking().CountAsync(c => c.IsActive, ct);
        var withoutRep = await db.Clients.AsNoTracking().CountAsync(c => c.IsActive && c.SalesRepId == null, ct);

        var mapped = (await db.Set<ProductLineBrand>().AsNoTracking().Select(b => b.Brand).ToListAsync(ct)).Select(R.NameKey).ToHashSet(StringComparer.Ordinal);
        var brands = await db.Products.AsNoTracking().Where(p => p.IsActive && p.ClientId == null && p.Brand != null && p.Brand != "").Select(p => p.Brand!).Distinct().ToListAsync(ct);
        var withoutLine = brands.Select(b => b.Trim()).Where(b => !mapped.Contains(R.NameKey(b))).Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(b => b, StringComparer.CurrentCultureIgnoreCase).ToList();

        var unmatchedClients = (await db.Set<SalesHistoryLine>().AsNoTracking().Where(l => l.ClientId == null)
                .GroupBy(l => l.ClientNameRaw).Select(g => new { Raw = g.Key, Lines = g.Count(), Amount = g.Sum(l => l.Amount), First = g.Min(l => l.TxnDate), Last = g.Max(l => l.TxnDate) })
                .ToListAsync(ct))
            .OrderByDescending(u => Math.Abs(u.Amount)).Select(u => new SalesUnmatchedDto(u.Raw, u.Lines, M2(u.Amount), u.First, u.Last)).ToList();
        var unmatchedItems = (await db.Set<SalesHistoryLine>().AsNoTracking().Where(l => l.ProductId == null && l.LineClass == C.ClassProduct)
                .GroupBy(l => l.ItemRaw).Select(g => new { Raw = g.Key, Lines = g.Count(), Amount = g.Sum(l => l.Amount), First = g.Min(l => l.TxnDate), Last = g.Max(l => l.TxnDate) })
                .ToListAsync(ct))
            .OrderByDescending(u => Math.Abs(u.Amount)).Select(u => new SalesUnmatchedDto(u.Raw, u.Lines, M2(u.Amount), u.First, u.Last)).ToList();
        var unmatchedPurchases = (await db.Set<PurchaseHistoryLine>().AsNoTracking().Where(l => l.ProductId == null)
                .GroupBy(l => l.ItemRaw).Select(g => new { Raw = g.Key, Lines = g.Count(), Amount = g.Sum(l => l.Amount), First = g.Min(l => l.TxnDate), Last = g.Max(l => l.TxnDate) })
                .ToListAsync(ct))
            .OrderByDescending(u => Math.Abs(u.Amount)).Select(u => new SalesUnmatchedDto(u.Raw, u.Lines, M2(u.Amount), u.First, u.Last)).ToList();

        var histFrom = await db.Set<SalesHistoryLine>().AsNoTracking().MinAsync(l => (DateOnly?)l.TxnDate, ct);
        var histTo = await db.Set<SalesHistoryLine>().AsNoTracking().MaxAsync(l => (DateOnly?)l.TxnDate, ct);
        var invFrom = await FirstInvoiceDateAsync(null, ct);
        var issued = await db.StatusCodes.AsNoTracking()
            .Where(s => s.Entity == Teikem.Domain.Billing.BillingCodes.InvoiceStatusDomain
                        && (s.InternalCode == Teikem.Domain.Billing.InvoiceStatuses.Issued || s.InternalCode == Teikem.Domain.Billing.InvoiceStatuses.Overdue
                            || s.InternalCode == Teikem.Domain.Billing.InvoiceStatuses.Paid))
            .Select(s => s.StatusCodeId).ToListAsync(ct);
        var invTo = await db.Invoices.AsNoTracking().Where(i => i.IsActive && issued.Contains(i.StatusCodeId) && i.IssueDate != null && i.Lines.Any(l => l.SalesOrderLineId != null))
            .MaxAsync(i => i.IssueDate, ct);
        var overlap = settings.CutoffDate is null && histTo is DateOnly ht && invFrom is DateOnly inf && inf <= ht;
        var warnings = new List<string>();
        if (overlap) warnings.Add(OverlapWarning(invFrom!.Value, histTo!.Value));
        if (settings.CutoffDate is DateOnly cut)
        {
            var ignoredHistory = await db.Set<SalesHistoryLine>().AsNoTracking().CountAsync(l => l.TxnDate >= cut, ct);
            if (ignoredHistory > 0) warnings.Add($"{ignoredHistory} línea(s) del historial importado son del {cut:yyyy-MM-dd} o después: no cuentan (desde la fecha de corte cuentan las facturas de Teikem).");
        }
        if (withoutRep > 0) warnings.Add($"{withoutRep} cliente(s) activo(s) sin representante: su venta sale aparte («Sin representante»).");
        if (withoutLine.Count > 0) warnings.Add($"{withoutLine.Count} marca(s) sin línea: cada una cuenta como su propia línea.");
        if (unmatchedClients.Count > 0) warnings.Add($"{unmatchedClients.Count} cliente(s) del historial sin emparejar: enlácelos a mano para atribuir su venta.");
        if (unmatchedItems.Count > 0) warnings.Add($"{unmatchedItems.Count} ítem(s) del historial sin emparejar con un producto.");

        return new SalesAnalysisStatusDto(settings.CutoffDate, settings.LeadTimeDays, settings.ServiceLevelZ,
            batches.Select(b => new SalesHistoryBatchDto(b.PublicId, b.Kind, b.FileName, b.DateFrom, b.DateTo, b.LineCount, b.TotalAmount, b.Source, b.ImportedAtUtc,
                b.ImportedBy is int u ? users.GetValueOrDefault(u) : null, b.IsActive, b.ReplacedAtUtc)).ToList(),
            activeClients, withoutRep, withoutLine, unmatchedClients, unmatchedItems, unmatchedPurchases, histFrom, histTo, invFrom, invTo, overlap, warnings);
    }

    // ================================================================ panel Ventas del Pulso

    /// <summary>Venta neta del mes en curso (producto + descuentos, hasta hoy) y la suma de las cuotas vigentes de los representantes activos (null si no hay ninguna).</summary>
    public async Task<(decimal Sales, decimal? Quota)> MonthSalesVsQuotaAsync(CancellationToken ct)
    {
        var month = R.MonthStart(clock.Today);
        var x = await ContextAsync(ct);
        var lines = await reader.LoadSalesAsync(month, clock.Today.AddDays(1), ct);
        var sales = M2(lines.Where(l => R.CountsForRep(l.LineClass)).Sum(l => l.Amount));
        var quotas = x.Reps.Values.Where(r => r.IsActive).Select(r => x.Quota(r.Id, month)).Where(q => q is not null).ToList();
        return (sales, quotas.Count == 0 ? null : quotas.Sum(q => q!.Value));
    }
}
