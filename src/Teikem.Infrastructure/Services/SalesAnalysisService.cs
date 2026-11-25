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
using RR = Teikem.Domain.Sales.SalesRouteRules;
using K = Teikem.Domain.Sales.SalesRouteCodes;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 241 — vistas del «Análisis de ventas» (sales.analysis.view, módulo SALES_ORDERS): «Por representante», transacciones del mes, «Movimiento de productos», series para
/// gráficos y estado del historial. Lee las líneas con <see cref="SalesAnalysisLineReader"/> (historial antes de la fecha de corte, facturas de Teikem desde ella) y agrega en
/// memoria (un año de facturas y 13 mil líneas de historial son decenas de miles de filas proyectadas: pocas décimas).
/// - «Por representante» (Lote 241, se conserva igual): la venta es del representante ASIGNADO AL CLIENTE hoy (Client.SalesRepId). Fecha de factura, por mes. «Venta» =
///   producto + descuentos (la venta neta); envío y otros cargos van aparte en la conciliación.
/// - Línea de un producto = la de su marca en la tabla marca → línea; marca sin línea = su propia marca; sin marca = «Sin marca».
/// <para>
/// Lote 244 (Lote A del plan de rutas) — «Por ruta», transacciones, gráficos, la fuente SALES_ANALYSIS_LINE y el Pulso usan la ATRIBUCIÓN de
/// <see cref="SalesRouteRules.Attribute"/>: ruta = la copia de la orden (congelada al solicitar) → la ruta propia del cliente → la de su representante; persona = el
/// vendedor copiado → el representante heredado (activo y de la misma ruta) → el titular vigente EL DÍA DE LA LÍNEA → «Sin vendedor». Un usuario que es representante
/// cuenta como ese representante (con duplicados, el de menor id y un aviso). Sin copias y recién migrado da lo mismo que «Por representante» (paridad).
/// </para>
/// </summary>
public sealed class SalesAnalysisService(TeikemDbContext db, ITenantClock clock, SalesAnalysisLineReader reader, ITenantContext tenant)
{
    private int TenantId => ((TenantContext)tenant).RequireTenantId();

    /// <summary>Lote 244 — clave del grupo «sin ruta y sin vendedor» en la vista por ruta.</summary>
    private const string UnassignedKey = "UNASSIGNED";

    // ================================================================ contexto (representantes, rutas, cuotas, líneas)

    private sealed record RepInfo(int Id, string Name, int? RouteId, bool IsActive, int? UserId);
    private sealed record RouteInfo(int Id, string Name, int SortOrder, bool IsActive);
    /// <summary>Lote 244 — la ruta propia del cliente y su representante.</summary>
    private sealed record ClientInfo(int? RouteId, int? RepId);
    /// <summary>Lote 244 — la copia de ruta y vendedor de una orden solicitada (SalesCopyAtUtc puesto).</summary>
    private sealed record OrderCopy(int? RouteId, int? SellerUserId);

    /// <summary>Lote 244 — una línea de venta con su ruta y su persona atribuidas.</summary>
    private sealed record Attributed(SalesAnalysisLine L, int? RouteId, SalesPersonRef? Person)
    {
        public string PersonKey => Person is null || Person.IsEmpty ? R.NoPersonKey : Person.Key;
        public bool IsUnassigned => RouteId is null && PersonKey == R.NoPersonKey;
    }

    private sealed class Ctx
    {
        public Dictionary<int, RepInfo> Reps { get; init; } = new();
        public Dictionary<int, RouteInfo> Routes { get; init; } = new();
        public Dictionary<int, ClientInfo> Clients { get; init; } = new();
        public Dictionary<int, List<(DateOnly Month, decimal Amount)>> Quotas { get; init; } = new();
        public Dictionary<int, List<(DateOnly Month, decimal Amount)>> RouteQuotas { get; init; } = new();
        public Dictionary<string, string> BrandLine { get; init; } = new(StringComparer.Ordinal);
        public Dictionary<int, SalesAnalysisProduct> Products { get; init; } = new();
        public SalesAnalysisSetting Settings { get; init; } = new();
        public SalesPrimaryTimeline Timeline { get; init; } = SalesPrimaryTimeline.Empty;
        /// <summary>Usuario → su representante activo (con duplicados, el de menor id).</summary>
        public Dictionary<int, int> UserToRep { get; init; } = new();
        public Dictionary<int, string> UserNames { get; init; } = new();
        /// <summary>Copias de las órdenes de las líneas cargadas (se llena con <see cref="AttributeAsync"/>).</summary>
        public Dictionary<int, OrderCopy> Orders { get; } = new();
        /// <summary>Avisos de la atribución (usuario en varios representantes activos).</summary>
        public List<string> RouteWarnings { get; init; } = new();

        public string QuotaBy => RR.ParseQuotaBy(Settings.QuotaBy) ?? K.QuotaByRep;

        public RepInfo? RepOf(SalesAnalysisLine l) => l.ClientId is int c && Clients.TryGetValue(c, out var ci) && ci.RepId is int rid ? Reps.GetValueOrDefault(rid) : null;
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

        /// <summary>Lote 244 — la cuota vigente de una ruta en el mes (misma herencia mensual que la del representante).</summary>
        public decimal? RouteQuota(int routeId, DateOnly month) => RouteQuotas.TryGetValue(routeId, out var q) ? R.EffectiveQuota(q, month) : null;

        /// <summary>Lote 244 — ruta y persona de una línea (la persona ya normalizada: un usuario que es representante pasa a ese representante).</summary>
        public Attributed Attribute(SalesAnalysisLine l)
        {
            OrderCopy? copy = l.SalesOrderId is int so && Orders.TryGetValue(so, out var oc) ? oc : null;
            ClientInfo? ci = l.ClientId is int c && Clients.TryGetValue(c, out var found) ? found : null;
            RepInfo? rep = ci?.RepId is int r ? Reps.GetValueOrDefault(r) : null;
            var a = RR.Attribute(new AttributionInput(copy?.RouteId, copy?.SellerUserId, ci?.RouteId, rep?.Id, rep?.IsActive ?? false, rep?.RouteId, l.Date), Timeline.On);
            return new Attributed(l, a.RouteId, RR.NormalizePerson(a.Person, UserToRep));
        }

        /// <summary>Lote 244 — el nombre de una persona: el del representante o el del usuario; sin persona = «Sin vendedor».</summary>
        public string PersonName(SalesPersonRef? p)
        {
            if (p is { RepId: int r }) return Reps.TryGetValue(r, out var rep) ? rep.Name : RR.NoSellerName;
            if (p is { UserId: int u }) return UserNames.TryGetValue(u, out var n) && !string.IsNullOrWhiteSpace(n) ? n : $"Usuario {u}";
            return RR.NoSellerName;
        }
    }

    private async Task<Ctx> ContextAsync(CancellationToken ct)
    {
        var routes = await db.Set<SalesRoute>().AsNoTracking().ToDictionaryAsync(r => r.SalesRouteId, r => new RouteInfo(r.SalesRouteId, r.Name, r.SortOrder, r.IsActive), ct);
        var reps = await db.Set<SalesRep>().AsNoTracking().ToDictionaryAsync(r => r.SalesRepId, r => new RepInfo(r.SalesRepId, r.Name, r.SalesRouteId, r.IsActive, r.UserId), ct);
        var clients = await db.Clients.AsNoTracking().Where(c => c.SalesRepId != null || c.SalesRouteId != null)
            .Select(c => new { c.ClientId, c.SalesRouteId, c.SalesRepId }).ToDictionaryAsync(c => c.ClientId, c => new ClientInfo(c.SalesRouteId, c.SalesRepId), ct);
        var allQuotas = await db.Set<SalesQuota>().AsNoTracking().Where(q => q.IsActive).Select(q => new { q.SalesRepId, q.SalesRouteId, q.Month, q.Amount }).ToListAsync(ct);
        var quotas = allQuotas.Where(q => q.SalesRepId != null).GroupBy(q => q.SalesRepId!.Value).ToDictionary(g => g.Key, g => g.Select(q => (q.Month, q.Amount)).ToList());
        var routeQuotas = allQuotas.Where(q => q.SalesRouteId != null).GroupBy(q => q.SalesRouteId!.Value)
            .ToDictionary(g => g.Key, g => g.Select(q => (q.Month, q.Amount)).ToList());
        var lines = await db.Set<ProductLine>().AsNoTracking().Where(l => l.IsActive).ToDictionaryAsync(l => l.ProductLineId, l => l.Name, ct);
        var brandLine = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var b in await db.Set<ProductLineBrand>().AsNoTracking().ToListAsync(ct))
            if (lines.TryGetValue(b.ProductLineId, out var name)) brandLine[R.NameKey(b.Brand)] = name;

        // Lote 244: titulares por fecha, usuario → representante y los nombres de los usuarios
        var timeline = await SalesSellerQueries.PrimaryTimelineAsync(db, TenantId, null, ct);
        var userToRep = new Dictionary<int, int>();
        var duplicated = new List<(int UserId, List<string> Reps)>();
        foreach (var g in reps.Values.Where(r => r.IsActive && r.UserId is not null).GroupBy(r => r.UserId!.Value))
        {
            var ordered = g.OrderBy(r => r.Id).ToList();
            userToRep[g.Key] = ordered[0].Id;
            if (ordered.Count > 1) duplicated.Add((g.Key, ordered.Select(r => r.Name).ToList()));
        }
        var userIds = reps.Values.Where(r => r.UserId is not null).Select(r => r.UserId!.Value)
            .Concat(timeline.Spans.Values.SelectMany(s => s.Select(x => x.UserId))).Distinct().ToList();
        var userNames = await AssigneeCheck.NamesAsync(db, userIds, ct);
        var warnings = duplicated.Select(d => RR.UserInSeveralReps(userNames.GetValueOrDefault(d.UserId) ?? $"Usuario {d.UserId}", d.Reps)).ToList();
        return new Ctx
        {
            Routes = routes, Reps = reps, Clients = clients, Quotas = quotas, RouteQuotas = routeQuotas, BrandLine = brandLine,
            Products = await reader.ProductsAsync(ct), Settings = await reader.SettingsAsync(ct),
            Timeline = timeline, UserToRep = userToRep, UserNames = userNames, RouteWarnings = warnings,
        };
    }

    /// <summary>
    /// Lote 244 — atribuye las líneas: carga la copia (ruta y vendedor) de las órdenes SOLICITADAS de esas líneas (SalesCopyAtUtc puesto; borradores y órdenes
    /// anteriores al lote no tienen copia) y los nombres de sus vendedores.
    /// </summary>
    private async Task<List<Attributed>> AttributeAsync(Ctx x, List<SalesAnalysisLine> lines, CancellationToken ct)
    {
        var tenantId = TenantId;
        var orderIds = lines.Where(l => l.SalesOrderId is not null).Select(l => l.SalesOrderId!.Value).Distinct().Where(id => !x.Orders.ContainsKey(id)).ToList();
        foreach (var chunk in orderIds.Chunk(2000))
        {
            var ids = chunk.ToList();
            var copies = await db.SalesOrders.AsNoTracking().Where(o => o.TenantId == tenantId && ids.Contains(o.SalesOrderId) && o.SalesCopyAtUtc != null)
                .Select(o => new { o.SalesOrderId, o.SalesRouteId, o.SellerUserId }).ToListAsync(ct);
            foreach (var o in copies) x.Orders[o.SalesOrderId] = new OrderCopy(o.SalesRouteId, o.SellerUserId);
        }
        var missing = x.Orders.Values.Where(o => o.SellerUserId is int u && !x.UserNames.ContainsKey(u)).Select(o => o.SellerUserId!.Value).Distinct().ToList();
        if (missing.Count > 0)
            foreach (var (id, name) in await AssigneeCheck.NamesAsync(db, missing, ct)) x.UserNames[id] = name;
        return lines.Select(x.Attribute).ToList();
    }

    private static string ClientKey(SalesAnalysisLine l) => l.ClientId is int c ? "C" + c : "N" + R.NameKey(l.ClientName);

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

    // ================================================================ Lote 244: «Por ruta»

    /// <summary>
    /// Vista «Por ruta» del mes (por omisión el último mes completo) contra el de comparación (por omisión el anterior), con la atribución del Lote 244 y la cuota según el
    /// ajuste QuotaBy (REP: por persona; ROUTE: por ruta).
    /// </summary>
    public async Task<SalesByRouteDto> GetByRouteAsync(string? month, string? compareMonth, CancellationToken ct)
    {
        var m = RequireMonth(month, R.LastCompleteMonth(clock.Today), "month");
        var cm = RequireMonth(compareMonth, m.AddMonths(-1), "compareMonth");
        var x = await ContextAsync(ct);
        var from = m < cm ? m : cm;
        var to = (m > cm ? m : cm).AddMonths(1);
        var lines = await AttributeAsync(x, await reader.LoadSalesAsync(from, to, ct), ct);
        var warnings = await WarningsAsync(x, from, to, ct);
        warnings.AddRange(x.RouteWarnings);
        return BuildByRoute(x, lines, m, cm, warnings);
    }

    private static readonly SalesGroupSales NoSales = new(0m, 0m);

    private static string RouteGroupKey(int? routeId, string personKey) => routeId is int r ? "T" + r : personKey == R.NoPersonKey ? UnassignedKey : "T0";

    private static SalesByRouteDto BuildByRoute(Ctx x, List<Attributed> all, DateOnly m, DateOnly cm, List<string> warnings)
    {
        var byRep = x.QuotaBy == K.QuotaByRep;
        var lastDay = m.AddMonths(1).AddDays(-1);
        var dom = all.Select(a => new AttributedLine(a.L.Date, a.L.LineClass, a.L.Amount, a.RouteId, a.PersonKey, ClientKey(a.L))).ToList();
        var byRoute = R.Aggregate(dom, l => RouteGroupKey(l.RouteId, l.PersonKey), m, cm);
        var byRoutePerson = R.Aggregate(dom, l => RouteGroupKey(l.RouteId, l.PersonKey) + "|" + l.PersonKey, m, cm);
        var byPerson = R.Aggregate(dom, l => l.PersonKey, m, cm);

        // personas conocidas: las de las líneas, los representantes activos y los titulares (para nombre, representante y usuario)
        var refs = new Dictionary<string, SalesPersonRef>(StringComparer.Ordinal);
        foreach (var a in all) if (a.Person is { IsEmpty: false } p) refs.TryAdd(p.Key, p);
        foreach (var r in x.Reps.Values.Where(r => r.IsActive)) refs.TryAdd("R" + r.Id, new SalesPersonRef(r.Id, r.UserId));
        SalesPersonRef? PrimaryOf(int routeId) => x.Timeline.On(routeId, lastDay) is int u ? RR.NormalizePerson(new SalesPersonRef(null, u), x.UserToRep) : null;
        decimal? PersonQuota(SalesPersonRef? p) => byRep && p?.RepId is int rid ? x.Quota(rid, m) : null;

        // rutas: las activas y las que vendieron en alguno de los dos meses; «Sin ruta» si hay venta con vendedor y sin ruta o representantes activos sin ruta
        var sold = all.Where(a => a.RouteId is not null && R.CountsForRep(a.L.LineClass) && (R.MonthStart(a.L.Date) == m || R.MonthStart(a.L.Date) == cm))
            .Select(a => a.RouteId!.Value);
        var routeOrder = x.Routes.Values.Where(r => r.IsActive).Select(r => r.Id).Concat(sold).Distinct()
            .OrderBy(id => x.RouteSort(id)).ThenBy(id => x.RouteName(id), StringComparer.CurrentCultureIgnoreCase).Select(id => (int?)id).ToList();
        if (byRoute.ContainsKey("T0") || x.Reps.Values.Any(r => r.IsActive && r.RouteId is null)) routeOrder.Add(null);

        var rows = new List<SalesRouteRowDto>();
        foreach (var routeId in routeOrder)
        {
            var key = routeId is int rid ? "T" + rid : "T0";
            var primary = routeId is int pr ? PrimaryOf(pr) : null;
            if (primary is not null) refs.TryAdd(primary.Key, primary);
            var prefix = key + "|";
            var personKeys = byRoutePerson.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).Select(k => k[prefix.Length..])
                .Concat(x.Reps.Values.Where(r => r.IsActive && r.RouteId == routeId).Select(r => "R" + r.Id))
                .Concat(primary is null ? Array.Empty<string>() : new[] { primary.Key })
                .Distinct(StringComparer.Ordinal).ToList();
            var rowPeople = personKeys.Select(pk =>
                {
                    var v = byRoutePerson.GetValueOrDefault(prefix + pk) ?? NoSales;
                    var p = pk == R.NoPersonKey ? null : refs.GetValueOrDefault(pk);
                    var quota = PersonQuota(p);
                    var s = M2(v.Sales);
                    var c = M2(v.CompareSales);
                    var total = M2((byPerson.GetValueOrDefault(pk) ?? NoSales).Sales);
                    return new SalesPersonRowDto(pk, p?.RepId, p?.UserId, x.PersonName(p), primary is not null && primary.Key == pk, c, s, M2(s - c), R.ChangePct(c, s),
                        quota, R.Compliance(total, quota));
                })
                .OrderByDescending(p => p.IsPrimary).ThenBy(p => p.PersonKey == R.NoPersonKey).ThenBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
            var rv = byRoute.GetValueOrDefault(key) ?? NoSales;
            var sales = M2(rv.Sales);
            var compare = M2(rv.CompareSales);
            decimal? routeQuota = !byRep && routeId is int qr ? x.RouteQuota(qr, m) : null;
            rows.Add(new SalesRouteRowDto(routeId, x.RouteName(routeId), compare, sales, M2(sales - compare), R.ChangePct(compare, sales), routeQuota,
                R.Compliance(sales, routeQuota), primary?.UserId, primary is null ? null : x.PersonName(primary), rowPeople));
        }

        // «Por persona»: la venta total de cada persona en todas las rutas (y los representantes activos aunque no hayan vendido)
        var people = byPerson.Keys.Where(k => k != R.NoPersonKey).Concat(x.Reps.Values.Where(r => r.IsActive).Select(r => "R" + r.Id)).Distinct(StringComparer.Ordinal)
            .Select(pk =>
            {
                var p = refs.GetValueOrDefault(pk);
                var v = byPerson.GetValueOrDefault(pk) ?? NoSales;
                var s = M2(v.Sales);
                var c = M2(v.CompareSales);
                var quota = PersonQuota(p);
                var routes = rows.Where(r => r.People.Any(pp => pp.PersonKey == pk && (pp.Sales != 0m || pp.CompareSales != 0m))).Select(r => r.RouteName).ToList();
                if (routes.Count == 0 && p?.RepId is int rr && x.Reps.TryGetValue(rr, out var repInfo)) routes.Add(x.RouteName(repInfo.RouteId));
                return new SalesPersonTotalDto(pk, p?.RepId, p?.UserId, x.PersonName(p), string.Join(", ", routes), c, s, M2(s - c), R.ChangePct(c, s), quota,
                    R.Compliance(s, quota));
            })
            .OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList();

        var tc = rows.Sum(r => r.CompareSales);
        var ts = rows.Sum(r => r.Sales);
        var tq = byRep ? people.Sum(p => p.Quota ?? 0m) : rows.Sum(r => r.Quota ?? 0m);   // con REP cada representante cuenta una vez
        var totals = new SalesRepTotalsDto(tc, ts, M2(ts - tc), R.ChangePct(tc, ts), tq, R.Compliance(ts, tq));

        var inM = all.Where(a => R.MonthStart(a.L.Date) == m).ToList();
        var netM = inM.Where(a => R.CountsForRep(a.L.LineClass)).ToList();
        var product = inM.Where(a => a.L.LineClass == C.ClassProduct).Sum(a => a.L.Amount);
        var discounts = inM.Where(a => a.L.LineClass == C.ClassDiscount).Sum(a => a.L.Amount);
        var shipping = inM.Where(a => a.L.LineClass == C.ClassShipping).Sum(a => a.L.Amount);
        var other = inM.Where(a => a.L.LineClass == C.ClassOther).Sum(a => a.L.Amount);
        var unassigned = byRoute.GetValueOrDefault(UnassignedKey) ?? NoSales;
        var rec = R.Reconcile(ts, M2(unassigned.Sales), M2(shipping + other), M2(inM.Sum(a => a.L.Amount)));
        var unassignedClients = netM.Where(a => a.IsUnassigned).Select(a => ClientKey(a.L)).Distinct().Count();
        var reconciliation = new SalesReconciliationDto(rec.RoutesTotal, rec.Unassigned, rec.ShippingAndOther, rec.MonthTotal, rec.Difference, M2(product), M2(discounts),
            M2(shipping), M2(other), unassignedClients, M2(unassigned.CompareSales));

        // por línea (producto) con su líder (la persona que más vendió de esa línea en una ruta) y los descuentos sin línea aparte
        var net = product + discounts;
        var lineDtos = inM.Where(a => a.L.LineClass == C.ClassProduct).GroupBy(a => x.LineOf(a.L))
            .Select(g => (Line: g.Key, Sales: g.Sum(a => a.L.Amount), Items: g.ToList()))
            .OrderByDescending(g => g.Sales).ThenBy(g => g.Line, StringComparer.CurrentCultureIgnoreCase)
            .Select(g =>
            {
                var leaders = g.Items.Where(a => a.PersonKey != R.NoPersonKey).GroupBy(a => (a.RouteId, a.PersonKey))
                    .Select(r => (r.Key.RouteId, Person: r.First().Person, Name: x.PersonName(r.First().Person), Sales: r.Sum(a => a.L.Amount)))
                    .OrderByDescending(r => r.Sales).ThenBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
                if (leaders.Count == 0) return new SalesLineShareDto(g.Line, false, M2(g.Sales), R.Share(g.Sales, net), R.Share(g.Sales, product), null, null, null, null);
                var top = leaders[0];
                return new SalesLineShareDto(g.Line, false, M2(g.Sales), R.Share(g.Sales, net), R.Share(g.Sales, product), top.Person?.RepId, top.Name,
                    x.RouteName(top.RouteId), M2(top.Sales));
            }).ToList();
        if (inM.Any(a => a.L.LineClass == C.ClassDiscount))
            lineDtos.Add(new SalesLineShareDto(R.DiscountsLine, true, M2(discounts), R.Share(discounts, net), R.Share(discounts, product), null, null, null, null));

        // ruta × producto: todos los productos vendidos en el mes para cada ruta atribuida (0 donde no se vendió) y lo sin ruta ni vendedor aparte
        var soldProducts = inM.Where(a => a.L.LineClass == C.ClassProduct).GroupBy(a => SalesAnalysisLineReader.ProductKey(a.L.ProductId, a.L.Sku))
            .Select(g => g.First().L).OrderBy(l => x.LineOf(l), StringComparer.CurrentCultureIgnoreCase).ThenBy(l => l.Sku, StringComparer.OrdinalIgnoreCase).ToList();
        var groups = rows.Select(r => (Key: r.RouteId is int id ? "T" + id : "T0", r.RouteId, Route: r.RouteName,
            People: string.Join(", ", r.People.Where(p => p.PersonKey != R.NoPersonKey).Select(p => p.Name)))).ToList();
        if (netM.Any(a => a.IsUnassigned)) groups.Add((UnassignedKey, null, R.UnassignedName, R.UnassignedName));
        var byRouteProduct = inM.Where(a => a.L.LineClass == C.ClassProduct)
            .GroupBy(a => (Route: RouteGroupKey(a.RouteId, a.PersonKey), Key: SalesAnalysisLineReader.ProductKey(a.L.ProductId, a.L.Sku)))
            .ToDictionary(g => g.Key, g => (Units: g.Sum(a => a.L.Quantity), Sales: g.Sum(a => a.L.Amount)));
        var routeProducts = new List<SalesRouteProductDto>();
        foreach (var rg in groups)
            foreach (var p in soldProducts)
            {
                var v = byRouteProduct.GetValueOrDefault((rg.Key, SalesAnalysisLineReader.ProductKey(p.ProductId, p.Sku)));
                routeProducts.Add(new SalesRouteProductDto(rg.RouteId, rg.Route, rg.People, x.LineOf(p), p.ProductId, p.Sku, p.Item, v.Units, M2(v.Sales)));
            }

        return new SalesByRouteDto(R.MonthKey(m), R.MonthKey(cm), x.Settings.CutoffDate, x.QuotaBy, warnings, rows, totals, reconciliation, people, lineDtos, routeProducts);
    }

    // ================================================================ transacciones del mes

    /// <summary>Líneas del mes (todas las clases), filtrables por representante (0 = sin representante) y texto; paginadas o completas (all = true, para exportar).</summary>
    public Task<PagedResult<SalesTransactionDto>> GetTransactionsAsync(string? month, int? repId, string? q, int skip, int take, bool all, CancellationToken ct)
        => GetTransactionsAsync(month, repId, null, q, skip, take, all, ct);

    /// <summary>
    /// Líneas del mes (todas las clases) con la persona y la ruta ATRIBUIDAS (Lote 244), filtrables por representante (repId; 0 = la persona no es un representante),
    /// por ruta (routeId; 0 = sin ruta) y texto; paginadas o completas (all = true, para exportar).
    /// </summary>
    public async Task<PagedResult<SalesTransactionDto>> GetTransactionsAsync(string? month, int? repId, int? routeId, string? q, int skip, int take, bool all,
        CancellationToken ct)
    {
        var m = RequireMonth(month, R.LastCompleteMonth(clock.Today), "month");
        if (skip < 0) throw new ValidationException("skip", R.PageInvalid);
        if (!all && (take < 1 || take > R.MaxPageSize)) throw new ValidationException("take", R.PageSizeInvalid);
        var x = await ContextAsync(ct);
        var lines = await AttributeAsync(x, await reader.LoadSalesAsync(m, m.AddMonths(1), ct), ct);
        IEnumerable<Attributed> filtered = lines;
        if (repId is int rid) filtered = filtered.Where(a => (a.Person?.RepId ?? 0) == rid);
        if (routeId is int ro) filtered = filtered.Where(a => (a.RouteId ?? 0) == ro);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var s = q.Trim();
            filtered = filtered.Where(a => a.L.ClientName.Contains(s, StringComparison.OrdinalIgnoreCase) || a.L.Sku.Contains(s, StringComparison.OrdinalIgnoreCase)
                || a.L.Item.Contains(s, StringComparison.OrdinalIgnoreCase) || (a.L.Number?.Contains(s, StringComparison.OrdinalIgnoreCase) ?? false));
        }
        var ordered = filtered.OrderBy(a => a.L.Date).ThenBy(a => a.L.Number, StringComparer.Ordinal).ThenBy(a => a.L.Sku, StringComparer.OrdinalIgnoreCase).ToList();
        var page = all ? ordered : ordered.Skip(skip).Take(take).ToList();
        var items = page.Select(a => TransactionDto(x, a)).ToList();
        return new PagedResult<SalesTransactionDto>(items, ordered.Count, all ? 0 : skip, all ? ordered.Count : take);
    }

    /// <summary>
    /// Lote 244 — una línea con su persona y ruta atribuidas: RepId = el representante (null si la persona es un usuario sin representante), RepName = el nombre de la
    /// persona; RouteName = la ruta («Sin ruta» si tiene vendedor y no ruta; null si no tiene ninguno de los dos).
    /// </summary>
    private static SalesTransactionDto TransactionDto(Ctx x, Attributed a)
    {
        var l = a.L;
        var hasPerson = a.Person is { IsEmpty: false };
        return new SalesTransactionDto(l.Date, l.Source, l.Type, l.Number, l.ClientId, l.ClientName, a.Person?.RepId, hasPerson ? x.PersonName(a.Person) : null,
            a.RouteId is int r ? x.RouteName(r) : hasPerson ? R.NoRouteName : null,
            l.ProductId, l.Sku, l.Item, l.LineClass is C.ClassProduct or C.ClassDiscount ? x.LineOf(l) : string.Empty, l.LineClass, l.Quantity, l.UnitPrice, l.Amount,
            a.RouteId, a.Person?.UserId, a.PersonKey, l.SalesOrderId);
    }

    /// <summary>Las líneas de [desde, hasta exclusivo) ya con persona y ruta atribuidas y línea (para la fuente de datos de Vistas, Indicadores y Gráficos).</summary>
    public async Task<IReadOnlyList<(SalesTransactionDto Line, string? Brand)>> LinesForRangeAsync(DateOnly from, DateOnly toExclusive, CancellationToken ct)
    {
        var x = await ContextAsync(ct);
        var lines = await AttributeAsync(x, await reader.LoadSalesAsync(from, toExclusive, ct), ct);
        return lines.OrderByDescending(a => a.L.Date)
            .Select(a => (TransactionDto(x, a), a.L.ProductId is int pid ? x.Products.GetValueOrDefault(pid)?.Brand : null)).ToList();
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

    /// <summary>
    /// Series para graficar: rango de meses (por omisión los 12 que terminan en el último completo), mes de la venta vs. cuota (por persona y, Lote 244, por ruta) y los N
    /// productos principales.
    /// </summary>
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

        // Lote 244: venta vs. cuota con la atribución nueva (por persona y por ruta; la cuota según el ajuste QuotaBy)
        var byRoute = BuildByRoute(x, await AttributeAsync(x, lines, ct), m, m.AddMonths(-1), new List<string>());
        var repVsQuota = byRoute.People.Select(p => new SalesRepQuotaPointDto(p.RepId, p.Name, p.Routes, p.Sales, p.Quota, p.Compliance)).ToList();
        var routeVsQuota = byRoute.Routes.Select(r => new SalesRouteQuotaPointDto(r.RouteId, r.RouteName, r.Sales, r.Quota, r.Compliance)).ToList();
        var lineShare = byRoute.Lines.Where(l => !l.IsDiscounts).Select(l => new SalesLineSharePointDto(l.Line, l.Sales, l.ShareOfGross)).ToList();

        var movement = BuildMovement(x, mp, lines, new List<SalesAnalysisPurchase>(), await StockAsync(ct), new Dictionary<int, decimal>(), new List<string>());
        // los N principales por venta en $ del rango (como el gráfico del dueño), con sus unidades por mes
        var unitsBySku = movement.UnitsByMonth.GroupBy(s => (s.ProductId, s.Sku)).ToDictionary(g => g.Key, g => g.First());
        var topUnits = movement.Products.Where(p => p.Sales > 0m && unitsBySku.ContainsKey((p.ProductId, p.Sku))).Take(top)
            .Select(p => unitsBySku[(p.ProductId, p.Sku)]).Select(s => new SalesChartSeriesDto(s.Sku, s.Name, s.Values)).ToList();
        var coverage = movement.Products.Where(r => r.WeeklyAverage is not null).OrderBy(r => r.CoverageWeeks ?? decimal.MaxValue).ThenBy(r => r.Sku, StringComparer.OrdinalIgnoreCase)
            .Select(r => new SalesCoveragePointDto(r.ProductId, r.Sku, r.Name, r.Abc, r.Available, r.WeeklyAverage, r.CoverageWeeks)).ToList();
        return new SalesChartsDto(months.Select(R.MonthKey).ToList(), total, byLine, R.MonthKey(m), repVsQuota, lineShare, topUnits, coverage, x.QuotaBy, routeVsQuota);
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
        var routes = await RouteStatusAsync(warnings, ct);

        return new SalesAnalysisStatusDto(settings.CutoffDate, settings.LeadTimeDays, settings.ServiceLevelZ,
            batches.Select(b => new SalesHistoryBatchDto(b.PublicId, b.Kind, b.FileName, b.DateFrom, b.DateTo, b.LineCount, b.TotalAmount, b.Source, b.ImportedAtUtc,
                b.ImportedBy is int u ? users.GetValueOrDefault(u) : null, b.IsActive, b.ReplacedAtUtc)).ToList(),
            activeClients, withoutRep, withoutLine, unmatchedClients, unmatchedItems, unmatchedPurchases, histFrom, histTo, invFrom, invTo, overlap, warnings,
            routes.ClientsWithoutRoute, routes.RoutesWithoutPrimary, routes.RoutesWithInactivePrimary, routes.RepsWithoutUser,
            RR.ParseQuotaBy(settings.QuotaBy) ?? K.QuotaByRep);
    }

    /// <summary>
    /// Lote 244 — lo que falta para vender por ruta (solo se señala, no bloquea): clientes activos sin ruta (ni propia ni de su representante), rutas activas sin titular
    /// vigente o con el titular inactivo, representantes activos sin usuario y usuarios en varios representantes activos. Agrega los avisos con conteo y nombres.
    /// </summary>
    private async Task<(int ClientsWithoutRoute, int RoutesWithoutPrimary, int RoutesWithInactivePrimary, int RepsWithoutUser)> RouteStatusAsync(List<string> warnings,
        CancellationToken ct)
    {
        var tenantId = TenantId;
        var reps = await db.Set<SalesRep>().AsNoTracking().Select(r => new { r.SalesRepId, r.Name, r.SalesRouteId, r.UserId, r.IsActive }).ToListAsync(ct);
        var routes = await db.Set<SalesRoute>().AsNoTracking().Where(r => r.IsActive).Select(r => new { r.SalesRouteId, r.Name, r.SortOrder }).ToListAsync(ct);
        var primaries = (await db.Set<SalesRouteAssignment>().AsNoTracking().Where(a => a.TenantId == tenantId && a.IsActive && a.IsPrimary && a.ValidTo == null)
                .Select(a => new { a.SalesRouteId, a.UserId }).ToListAsync(ct))
            .GroupBy(a => a.SalesRouteId).ToDictionary(g => g.Key, g => g.First().UserId);
        var active = await SalesSellerQueries.ActiveMemberIdsAsync(db, tenantId, primaries.Values, ct);
        var ordered = routes.OrderBy(r => r.SortOrder).ThenBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        var noPrimary = ordered.Where(r => !primaries.ContainsKey(r.SalesRouteId)).Select(r => r.Name).ToList();
        var inactivePrimary = ordered.Where(r => primaries.TryGetValue(r.SalesRouteId, out var u) && !active.Contains(u)).Select(r => r.Name).ToList();
        var withoutUser = reps.Where(r => r.IsActive && r.UserId is null).Select(r => r.Name).OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase).ToList();
        var routed = reps.Where(r => r.SalesRouteId is not null).Select(r => r.SalesRepId).ToList();
        var withoutRoute = await db.Clients.AsNoTracking()
            .CountAsync(c => c.IsActive && c.SalesRouteId == null && (c.SalesRepId == null || !routed.Contains(c.SalesRepId.Value)), ct);

        if (withoutRoute > 0) warnings.Add(RR.ClientsWithoutRoute(withoutRoute));
        if (noPrimary.Count > 0) warnings.Add(RR.RoutesWithoutPrimary(noPrimary.Count, noPrimary));
        if (inactivePrimary.Count > 0) warnings.Add(RR.RoutesWithInactivePrimary(inactivePrimary.Count, inactivePrimary));
        if (withoutUser.Count > 0) warnings.Add(RR.RepsWithoutUser(withoutUser.Count, withoutUser));
        var duplicated = reps.Where(r => r.IsActive && r.UserId is not null).GroupBy(r => r.UserId!.Value).Where(g => g.Count() > 1).ToList();
        if (duplicated.Count > 0)
        {
            var names = await AssigneeCheck.NamesAsync(db, duplicated.Select(g => g.Key), ct);
            foreach (var g in duplicated)
                warnings.Add(RR.UserInSeveralReps(names.GetValueOrDefault(g.Key) ?? $"Usuario {g.Key}", g.OrderBy(r => r.SalesRepId).Select(r => r.Name)));
        }
        return (withoutRoute, noPrimary.Count, inactivePrimary.Count, withoutUser.Count);
    }

    // ================================================================ panel Ventas del Pulso

    /// <summary>
    /// Venta neta del mes en curso (producto + descuentos, hasta hoy) y la suma de las cuotas vigentes (null si no hay ninguna): Lote 244, según el ajuste QuotaBy, la de
    /// los representantes activos (REP) o la de las rutas activas (ROUTE).
    /// </summary>
    public async Task<(decimal Sales, decimal? Quota)> MonthSalesVsQuotaAsync(CancellationToken ct)
    {
        var month = R.MonthStart(clock.Today);
        var x = await ContextAsync(ct);
        var lines = await reader.LoadSalesAsync(month, clock.Today.AddDays(1), ct);
        var sales = M2(lines.Where(l => R.CountsForRep(l.LineClass)).Sum(l => l.Amount));
        var quotas = (x.QuotaBy == K.QuotaByRoute
                ? x.Routes.Values.Where(r => r.IsActive).Select(r => x.RouteQuota(r.Id, month))
                : x.Reps.Values.Where(r => r.IsActive).Select(r => x.Quota(r.Id, month)))
            .Where(q => q is not null).ToList();
        return (sales, quotas.Count == 0 ? null : quotas.Sum(q => q!.Value));
    }
}
