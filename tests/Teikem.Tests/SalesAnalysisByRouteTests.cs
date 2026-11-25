using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Billing;
using Teikem.Domain.Catalogs;
using Teikem.Domain.Common;
using Teikem.Domain.Constants;
using Teikem.Domain.Sales;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;
using Xunit;
using C = Teikem.Domain.Sales.SalesAnalysisCodes;
using K = Teikem.Domain.Sales.SalesRouteCodes;
using R = Teikem.Domain.Sales.SalesAnalysisRules;
using RR = Teikem.Domain.Sales.SalesRouteRules;

namespace Teikem.Tests;

/// <summary>
/// Lote 244 (Lote A del plan de rutas) — la vista «Por ruta» con DINERO, en la base en memoria (datos INVENTADOS):
/// - conciliación (rutas + sin ruta ni vendedor + envío y otros = total del mes, diferencia 0) en un mundo mixto: cliente con ruta propia, cliente solo con representante,
///   cliente sin nada, facturas con copia distinta de la ruta de hoy, orden con copia de vendedor vacía, nota de crédito, kit repartido y envío;
/// - la suma de las personas = la fila de su ruta; la copia gana a la ruta de hoy; titular por la fecha de la línea (cambio a mitad de mes);
/// - REP (cumplimiento = venta total de la persona en todas las rutas ÷ su cuota; en el total la cuota cuenta una vez) y ROUTE (cuota en la fila de la ruta); cambiar el
///   ajuste no cambia ninguna venta;
/// - PARIDAD EXACTA con «Por representante» (Lote 241) recién migrado y sin copias, con una ruta con un representante con usuario (titular) y otro sin usuario;
/// - un usuario en dos representantes (aviso y el de menor id); SalesOrderId en las líneas sin multiplicar filas; transacciones por ruta.
/// </summary>
public sealed class SalesAnalysisByRouteTests
{
    private const int T = WmsFixture.TenantId;
    private static readonly DateTime Now = new(2026, 11, 25, 16, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Jan = new(2026, 1, 1);
    private const int Ana = 301, Beto = 302, Carla = 303;

    private sealed record World(WmsFixture F, int Norte, int Metro, int RepAna, int RepSin, int C1, int C2, int C3, int C4, int C5, int P1, int P2)
    {
        public SalesAnalysisService Analysis => F.Get<SalesAnalysisService>();
        public SalesAnalysisSetupService Setup => F.Get<SalesAnalysisSetupService>();
    }

    private static async Task<WmsFixture> FixtureAsync()
        => await WmsFixture.CreateAsync(s =>
        {
            s.AddSingleton<ITenantClock>(new TenantClock(LocalDay.DefaultZone, () => Now));
            // transitorios: el lector guarda los ajustes en memoria (en la aplicación es por solicitud)
            s.AddTransient<SalesAnalysisLineReader>();
            s.AddTransient<SalesAnalysisService>();
            s.AddTransient<SalesAnalysisSetupService>();
        });

    /// <summary>
    /// Norte (titular Ana, representantes Ana con usuario y «Rep Sin Usuario» heredado) y Metro (titular Beto hasta el 16-oct, luego Carla). Clientes: C1 (Ana, ruta Norte),
    /// C2 (Rep Sin Usuario, ruta Norte), C3 (ruta Metro, sin representante), C4 (nada), C5 (Ana, sin ruta propia). Historial de octubre (y C1 en septiembre).
    /// </summary>
    private static async Task<World> WorldAsync(bool withMetroClient = true)
    {
        var f = await FixtureAsync();
        await f.AddActiveMemberAsync(Ana, "Ana Norte");
        await f.AddActiveMemberAsync(Beto, "Beto Metro");
        await f.AddActiveMemberAsync(Carla, "Carla Metro");
        var norte = await f.AddRouteAsync("Norte", sortOrder: 1);
        var metro = await f.AddRouteAsync("Metro", sortOrder: 2);
        var repAna = await f.AddRepAsync("Ana Norte", norte.SalesRouteId, Ana);
        var repSin = await f.AddRepAsync("Rep Sin Usuario", norte.SalesRouteId, null);
        await f.AddAssignmentAsync(norte.SalesRouteId, Ana, true, Jan);
        await f.AddAssignmentAsync(metro.SalesRouteId, Beto, true, Jan, new DateOnly(2026, 10, 16));
        await f.AddAssignmentAsync(metro.SalesRouteId, Carla, true, new DateOnly(2026, 10, 16));

        var c1 = await f.AddClientAsync("C1");
        var c2 = await f.AddClientAsync("C2");
        var c3 = await f.AddClientAsync("C3");
        var c4 = await f.AddClientAsync("C4");
        var c5 = await f.AddClientAsync("C5");
        await f.SetClientSalesAsync(c1.ClientId, norte.SalesRouteId, repAna.SalesRepId);
        await f.SetClientSalesAsync(c2.ClientId, norte.SalesRouteId, repSin.SalesRepId);
        if (withMetroClient) await f.SetClientSalesAsync(c3.ClientId, metro.SalesRouteId);
        await f.SetClientSalesAsync(c5.ClientId, null, repAna.SalesRepId);

        var p1 = await f.AddProductAsync("P1");
        var p2 = await f.AddProductAsync("P2");
        var rows = await f.Db.Products.AsTracking().Where(p => p.ProductId == p1.ProductId || p.ProductId == p2.ProductId).ToListAsync();
        foreach (var p in rows) { p.Brand = "Marca"; p.SalePrice = p.ProductId == p1.ProductId ? 6m : 2m; }
        var batch = new SalesHistoryBatch { TenantId = T, Kind = C.KindSales, FileName = "h.csv", FileHash = new string('a', 64), PublicId = Guid.NewGuid() };
        f.Db.Set<SalesHistoryBatch>().Add(batch);
        await f.Db.SaveChangesAsync();
        var n = 0;
        void Hist(int day, int? clientId, decimal amount, string cls = C.ClassProduct, int month = 10)
            => f.Db.Set<SalesHistoryLine>().Add(new SalesHistoryLine
            {
                TenantId = T, SalesHistoryBatchId = batch.SalesHistoryBatchId, SourceLine = ++n, TxnDate = new DateOnly(2026, month, day), TxnType = "Invoice",
                DocNumber = "H-" + n, ClientNameRaw = clientId is null ? "SIN EMPAREJAR" : "X", ClientId = clientId, ItemRaw = "P1", ItemSku = "P1",
                ProductId = cls == C.ClassProduct ? p1.ProductId : null, Quantity = 1m, Amount = amount, LineClass = cls,
            });
        Hist(3, c1.ClientId, 100m);
        Hist(3, c1.ClientId, -5m, C.ClassDiscount);
        Hist(3, c1.ClientId, 8m, C.ClassShipping);
        Hist(4, c2.ClientId, 50m);
        if (withMetroClient)
        {
            Hist(5, c3.ClientId, 30m);    // titular Beto
            Hist(20, c3.ClientId, 70m);   // titular Carla (desde el 16)
        }
        Hist(6, c4.ClientId, 20m);
        Hist(7, c5.ClientId, 10m);
        Hist(10, c1.ClientId, 40m, month: 9);
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        return new World(f, norte.SalesRouteId, metro.SalesRouteId, repAna.SalesRepId, repSin.SalesRepId, c1.ClientId, c2.ClientId, c3.ClientId, c4.ClientId,
            c5.ClientId, p1.ProductId, p2.ProductId);
    }

    /// <summary>Una orden solicitada con su copia de ruta y vendedor, y su factura emitida (líneas: producto, kit y envío, cada una opcional).</summary>
    private static async Task<int> OrderInvoiceAsync(World w, int orderId, int clientId, int? routeId, int? sellerUserId, DateOnly issue,
        (int ProductId, decimal Qty, decimal Price)? product = null, decimal? kitAmount = null, decimal? shipping = null)
    {
        var f = w.F;
        var order = new SalesOrder
        {
            SalesOrderId = orderId, PublicId = Guid.NewGuid(), TenantId = T, Number = "OV-" + orderId, WarehouseId = 1, ClientId = clientId, RequestedDate = issue,
            StatusCodeId = 1, CreatedAtUtc = Now, SalesRouteId = routeId, SellerUserId = sellerUserId, SalesCopyAtUtc = Now.AddMonths(-2),
        };
        var invoice = new Invoice
        {
            PublicId = Guid.NewGuid(), TenantId = T, ClientId = clientId, InvoiceNumber = "FAC-" + orderId, IssueDate = issue, CurrencyLookupId = 1,
            StatusCodeId = f.StatusId(BillingCodes.InvoiceStatusDomain, InvoiceStatuses.Issued), IsActive = true,
        };
        if (product is { } pr)
        {
            var (pid, qty, price) = pr;
            order.Lines.Add(new SalesOrderLine { SalesOrderLineId = orderId * 10 + 1, TenantId = T, LineNo = 1, ProductId = pid, QtyOrdered = qty, UnitPrice = price });
            invoice.Lines.Add(new InvoiceLine { InvoiceLineId = orderId * 10 + 1, ProductId = pid, SalesOrderLineId = orderId * 10 + 1, Description = "P", Quantity = qty, UnitAmount = price });
        }
        if (kitAmount is decimal ka)
        {
            var kit = await f.AddProductAsync("KIT-" + orderId);
            f.Db.Attach(kit).Entity.IsOfferKit = true;
            order.Lines.Add(new SalesOrderLine { SalesOrderLineId = orderId * 10 + 2, TenantId = T, LineNo = 2, ProductId = kit.ProductId, QtyOrdered = 1m, UnitPrice = ka });
            f.Db.Set<SalesOrderLineComponent>().AddRange(
                new SalesOrderLineComponent { TenantId = T, SalesOrderLineId = orderId * 10 + 2, ComponentProductId = w.P1, Quantity = 1m },
                new SalesOrderLineComponent { TenantId = T, SalesOrderLineId = orderId * 10 + 2, ComponentProductId = w.P2, Quantity = 2m });
            invoice.Lines.Add(new InvoiceLine { InvoiceLineId = orderId * 10 + 2, ProductId = kit.ProductId, SalesOrderLineId = orderId * 10 + 2, Description = "KIT", Quantity = 1m, UnitAmount = ka });
        }
        if (shipping is decimal sh)
            invoice.Lines.Add(new InvoiceLine { InvoiceLineId = orderId * 10 + 3, SalesOrderId = orderId, Description = "Envío", Quantity = 1m, UnitAmount = sh });
        f.Db.Set<SalesOrder>().Add(order);
        f.Db.Set<Invoice>().Add(invoice);
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        return invoice.InvoiceId;
    }

    /// <summary>Nota de crédito aprobada que devuelve unidades de una línea de factura.</summary>
    private static async Task CreditAsync(World w, int invoiceId, int invoiceLineId, decimal qty, decimal price, DateTime approvedAtUtc)
    {
        var f = w.F;
        var approved = await f.Db.StatusCodes.AsNoTracking().FirstOrDefaultAsync(s => s.Entity == CreditCodes.StatusDomain && s.InternalCode == CreditNoteStatuses.Approved);
        if (approved is null)
        {
            approved = new StatusCode
            {
                StatusCodeId = 9901, Entity = CreditCodes.StatusDomain, InternalCode = CreditNoteStatuses.Approved, LabelJson = "{}",
                StageKindLookupId = f.LookupId(LookupDomains.StageKind, StageKinds.Pipeline), IsActive = true,
            };
            f.Db.StatusCodes.Add(approved);
        }
        var credit = new CreditNote
        {
            PublicId = Guid.NewGuid(), TenantId = T, CreditNumber = "NC-" + invoiceLineId, ClientId = w.C3, InvoiceId = invoiceId, StatusCodeId = approved.StatusCodeId,
            ApprovedAtUtc = approvedAtUtc, CurrencyLookupId = 1,
        };
        credit.Lines.Add(new CreditNoteLine { InvoiceLineId = invoiceLineId, Description = "Devolución", Quantity = qty, UnitAmount = price });
        f.Db.Set<CreditNote>().Add(credit);
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
    }

    /// <summary>
    /// El mundo mixto: además del historial, C3 (hoy en Metro) tiene una factura de una orden solicitada en NORTE por Ana (producto, kit y envío) con una nota de crédito;
    /// C4 (sin nada) una orden copiada en Metro con el vendedor vacío; y C3 otra orden copiada en Metro por Ana.
    /// </summary>
    private static async Task<World> MixedWorldAsync()
    {
        var w = await WorldAsync();
        var inv = await OrderInvoiceAsync(w, 8101, w.C3, w.Norte, Ana, new DateOnly(2026, 10, 15), (w.P1, 10m, 6m), kitAmount: 13.40m, shipping: 5m);
        await CreditAsync(w, inv, 81011, 1m, 6m, new DateTime(2026, 10, 28, 15, 0, 0, DateTimeKind.Utc));
        await OrderInvoiceAsync(w, 8102, w.C4, w.Metro, null, new DateOnly(2026, 10, 10), (w.P2, 4m, 2m));
        await OrderInvoiceAsync(w, 8103, w.C3, w.Metro, Ana, new DateOnly(2026, 10, 22), (w.P2, 5m, 2m));
        await w.Setup.SetQuotasAsync("2026-10", new SalesQuotaBulkRequest(new[] { new SalesQuotaItemRequest(w.RepAna, 200m) }), default);
        return w;
    }

    private static SalesPersonRowDto Person(SalesRouteRowDto row, string key) => row.People.Single(p => p.PersonKey == key);

    [Fact]
    public async Task Mixed_world_reconciles_and_people_add_up_to_their_route()
    {
        var w = await MixedWorldAsync();
        await using var _ = w.F;

        var v = await w.Analysis.GetByRouteAsync("2026-10", null, default);
        Assert.Equal(("2026-10", "2026-09", K.QuotaByRep), (v.Month, v.CompareMonth, v.QuotaBy));
        var rec = v.Reconciliation;
        Assert.Equal((340.40m, 20m, 13m, 373.40m, 0m), (rec.RoutesTotal, rec.Unassigned, rec.ShippingAndOther, rec.MonthTotal, rec.Difference));
        Assert.Equal(1, rec.UnassignedClients);

        var norte = v.Routes.Single(r => r.RouteId == w.Norte);
        var metro = v.Routes.Single(r => r.RouteId == w.Metro);
        Assert.Equal((222.40m, 40m), (norte.Sales, norte.CompareSales));
        Assert.Equal(118m, metro.Sales);
        foreach (var row in v.Routes) Assert.Equal(row.Sales, row.People.Sum(p => p.Sales));   // la suma de las personas = la fila de su ruta

        // la copia gana: la factura de la orden solicitada en Norte por Ana sigue en Norte aunque C3 hoy sea de Metro (producto 60 + kit 13.40 − nota 6)
        Assert.Equal(172.40m, Person(norte, "R" + w.RepAna).Sales);   // 95 + 10 (C5 por su representante) + 67.40
        Assert.Equal(50m, Person(norte, "R" + w.RepSin).Sales);       // el representante heredado va antes que el titular
        Assert.True(Person(norte, "R" + w.RepAna).IsPrimary);
        // titular por la fecha de la línea: el 5 era Beto (30 + la orden de copia con vendedor vacío del 10, 8) y el 20 Carla (70)
        Assert.Equal(38m, Person(metro, "U" + Beto).Sales);
        Assert.Equal(70m, Person(metro, "U" + Carla).Sales);
        Assert.Equal(10m, Person(metro, "R" + w.RepAna).Sales);       // la orden copiada en Metro por Ana
        Assert.True(Person(metro, "U" + Carla).IsPrimary);
        Assert.Equal((Carla, "Carla Metro"), (metro.PrimaryUserId!.Value, metro.PrimaryName));

        // REP: la cuota de Ana (200) contra su venta TOTAL en las dos rutas (182.40), en las dos filas y en «Por persona»; en el total cuenta una vez
        Assert.Equal((200m, 0.912m), (Person(norte, "R" + w.RepAna).Quota!.Value, Person(norte, "R" + w.RepAna).Compliance!.Value));
        Assert.Equal(0.912m, Person(metro, "R" + w.RepAna).Compliance);
        var ana = v.People.Single(p => p.RepId == w.RepAna);
        Assert.Equal((182.40m, 200m, 0.912m, "Norte, Metro"), (ana.Sales, ana.Quota!.Value, ana.Compliance!.Value, ana.Routes));
        Assert.Equal((340.40m, 200m, 1.702m), (v.Totals.Sales, v.Totals.Quota, v.Totals.Compliance!.Value));
        Assert.Null(norte.Quota);

        // líneas y ruta × producto por la ruta atribuida
        var line = Assert.Single(v.Lines, l => !l.IsDiscounts);
        Assert.Equal(("Ana Norte", "Norte"), (line.LeaderName, line.LeaderRoute));
        Assert.Contains(v.RouteProducts, p => p.RouteId == w.Metro && p.ProductId == w.P2 && p.Units == 9m && p.Sales == 18m);
        Assert.Contains(v.RouteProducts, p => p.RouteName == R.UnassignedName && p.ProductId == w.P1 && p.Sales == 20m);
    }

    [Fact]
    public async Task Route_quota_mode_puts_quota_on_the_route_and_does_not_change_any_sale()
    {
        var w = await MixedWorldAsync();
        await using var _ = w.F;
        var before = await w.Analysis.GetByRouteAsync("2026-10", null, default);

        await w.Setup.SetQuotasAsync("2026-09", new SalesQuotaBulkRequest(new[] { new SalesQuotaItemRequest(null, 300m, w.Norte) }), default);
        await w.Setup.SetQuotasAsync("2026-10", new SalesQuotaBulkRequest(new[] { new SalesQuotaItemRequest(null, 100m, w.Metro) }), default);
        await w.Setup.UpdateSettingsAsync(new SalesAnalysisSettingsRequest(QuotaBy: "route"), default);
        var v = await w.Analysis.GetByRouteAsync("2026-10", null, default);

        Assert.Equal(K.QuotaByRoute, v.QuotaBy);
        var norte = v.Routes.Single(r => r.RouteId == w.Norte);
        var metro = v.Routes.Single(r => r.RouteId == w.Metro);
        Assert.Equal((300m, 0.7413m), (norte.Quota!.Value, norte.Compliance!.Value));   // la de septiembre sigue valiendo en octubre
        Assert.Equal((100m, 1.18m), (metro.Quota!.Value, metro.Compliance!.Value));
        Assert.All(v.Routes.SelectMany(r => r.People), p => Assert.Null(p.Quota));
        Assert.Equal((400m, 0.851m), (v.Totals.Quota, v.Totals.Compliance!.Value));

        // cambiar el ajuste no cambia ninguna venta
        Assert.Equal(before.Reconciliation, v.Reconciliation);
        Assert.Equal(before.Routes.Select(r => (r.RouteId, r.Sales, r.CompareSales)), v.Routes.Select(r => (r.RouteId, r.Sales, r.CompareSales)));
        Assert.Equal(before.Routes.SelectMany(r => r.People).Select(p => (p.PersonKey, p.Sales)), v.Routes.SelectMany(r => r.People).Select(p => (p.PersonKey, p.Sales)));

        var charts = await w.Analysis.GetChartsAsync("2026-09", "2026-10", "2026-10", null, default);
        Assert.Equal(K.QuotaByRoute, charts.QuotaBy);
        Assert.Contains(charts.RouteVsQuota!, r => r.RouteId == w.Norte && r.Sales == 222.40m && r.Quota == 300m);
    }

    [Fact]
    public async Task Without_copies_and_freshly_migrated_it_matches_the_by_rep_view_exactly()
    {
        // recién migrado: los clientes con representante tienen la ruta de su representante; el titular de Norte es Ana (el representante con usuario) y
        // «Rep Sin Usuario» (heredado) se queda en la misma ruta; sin órdenes con copia
        var w = await WorldAsync(withMetroClient: false);
        await using var _ = w.F;
        var rep = await w.Analysis.GetByRepAsync("2026-10", null, default);
        var route = await w.Analysis.GetByRouteAsync("2026-10", null, default);

        Assert.Equal(rep.Reconciliation.RoutesTotal, route.Reconciliation.RoutesTotal);
        Assert.Equal(rep.Reconciliation.Unassigned, route.Reconciliation.Unassigned);
        Assert.Equal(rep.Reconciliation.MonthTotal, route.Reconciliation.MonthTotal);
        Assert.Equal(rep.Reconciliation.UnassignedClients, route.Reconciliation.UnassignedClients);
        Assert.Equal(0m, route.Reconciliation.Difference);
        foreach (var r in rep.Reps)
        {
            var person = route.People.Single(p => p.RepId == r.RepId);
            Assert.Equal((r.Sales, r.CompareSales), (person.Sales, person.CompareSales));
        }
        Assert.Equal((105m, 50m), (route.People.Single(p => p.RepId == w.RepAna).Sales, route.People.Single(p => p.RepId == w.RepSin).Sales));
        Assert.Equal(rep.Totals.Sales, route.Totals.Sales);
    }

    [Fact]
    public async Task A_user_in_two_reps_counts_for_the_oldest_and_warns()
    {
        var w = await WorldAsync(withMetroClient: false);
        await using var _ = w.F;
        await w.F.AddActiveMemberAsync(304, "Dora Doble");
        var first = await w.F.AddRepAsync("Dora Uno", w.Metro, 304);
        var second = await w.F.AddRepAsync("Dora Dos", w.Metro, 304);
        await OrderInvoiceAsync(w, 8201, w.C4, w.Metro, 304, new DateOnly(2026, 10, 12), (w.P1, 1m, 6m));

        var v = await w.Analysis.GetByRouteAsync("2026-10", null, default);
        var metro = v.Routes.Single(r => r.RouteId == w.Metro);
        Assert.Equal(6m, Person(metro, "R" + first.SalesRepId).Sales);
        Assert.Equal(0m, Person(metro, "R" + second.SalesRepId).Sales);
        Assert.Contains(RR.UserInSeveralReps("Dora Doble", new[] { "Dora Uno", "Dora Dos" }), v.Warnings);
    }

    [Fact]
    public async Task Lines_carry_their_order_without_multiplying_rows_and_transactions_filter_by_route()
    {
        var w = await MixedWorldAsync();
        await using var _ = w.F;

        var tx = await w.Analysis.GetTransactionsAsync("2026-10", null, null, null, 0, 0, true, default);
        // historial 8 + factura 8101 (producto, kit en 2 piezas, envío) 4 + nota 1 + facturas 8102 y 8103 1 + 1
        Assert.Equal(15, tx.Total);
        Assert.All(tx.Items.Where(t => t.Source != C.SourceHistory), t => Assert.NotNull(t.SalesOrderId));
        Assert.Equal(4, tx.Items.Count(t => t.SalesOrderId == 8101 && t.Source == C.SourceInvoice));   // producto, las 2 piezas del kit y el envío
        Assert.Equal(8101, tx.Items.Single(t => t.Source == C.SourceCredit).SalesOrderId);                // la nota, por su línea de factura
        Assert.All(tx.Items.Where(t => t.SalesOrderId == 8101 || t.Source == C.SourceCredit), t => Assert.Equal(("Norte", "Ana Norte"), (t.RouteName, t.RepName)));

        var metro = await w.Analysis.GetTransactionsAsync("2026-10", null, w.Metro, null, 0, 0, true, default);
        Assert.Equal(118m, metro.Items.Where(t => R.CountsForRep(t.LineClass)).Sum(t => t.Amount));
        var noRoute = await w.Analysis.GetTransactionsAsync("2026-10", null, 0, null, 0, 0, true, default);
        Assert.Equal(20m, Assert.Single(noRoute.Items).Amount);   // C4 sin ruta ni vendedor
        Assert.Null(noRoute.Items[0].RepName);

        var range = await w.Analysis.LinesForRangeAsync(new DateOnly(2026, 10, 1), new DateOnly(2026, 11, 1), default);
        Assert.Equal(15, range.Count);
        Assert.Contains(range, r => r.Line.RepName == "Carla Metro" && r.Line.RouteName == "Metro" && r.Line.Amount == 70m);
    }
}
