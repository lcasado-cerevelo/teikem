using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Common;
using Teikem.Domain.Constants;
using Teikem.Domain.Sales;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Migration;
using Teikem.Infrastructure.Services;
using Xunit;
using C = Teikem.Domain.Sales.SalesAnalysisCodes;
using R = Teikem.Domain.Sales.SalesAnalysisRules;

namespace Teikem.Tests;

/// <summary>
/// Lote 241 — análisis de ventas de punta a punta en la base en memoria (datos INVENTADOS): rutas, representantes, cuotas y líneas; importar el historial de QuickBooks
/// (simulación sin escribir, de verdad, reimportar el mismo archivo sin duplicar, emparejar cliente por nombre normalizado e ítem por SKU sin espacios); la vista «Por
/// representante» (venta del representante del cliente, cuota vigente, conciliación con lo sin representante, líneas por marca y líder); transacciones; «Movimiento de
/// productos» (estado y ABC); enlazar a mano un nombre sin emparejar; mensajes exactos.
/// </summary>
public sealed class SalesAnalysisServiceTests
{
    private static readonly DateTime Now = new(2026, 11, 5, 16, 0, 0, DateTimeKind.Utc);

    private const string SalesCsv =
        ",\"Type\",\"Date\",\"Num\",\"Memo\",\"Name\",\"Qty\",\"Sales Price\",\"Amount\",\"Balance\"\r\n" +
        "\"Inventory\",,,,,,,,,\"\"\r\n" +
        "\"AB-100 (TIRAS DE PRUEBA 1/50CT.)\",,,,,,,,,\"\"\r\n" +
        ",\"Invoice\",\"09/15/2026\",\"2025-0000\",\"TIRAS\",\"FARMACIA LA ESQUINA\",50,4.00,200.00,200.00\r\n" +
        ",\"Invoice\",\"10/01/2026\",\"2025-0001\",\"TIRAS\",\"FARMACIA LA ESQUINA\",120,4.20,504.00,704.00\r\n" +
        ",\"Credit Memo\",\"10/08/2026\",\"2025-0002\",\"TIRAS\",\"FARMACIA CAÑÓN\",-10,4.20,-42.00,662.00\r\n" +
        "\"Total AB-100 (TIRAS DE PRUEBA 1/50CT.)\",,,,,,160,,662.00,662.00\r\n" +
        "\"LN 28 (LANCETAS \"\"28\"\")\",,,,,,,,,\"\"\r\n" +
        ",\"Invoice\",\"10/02/2026\",\"2025-0003\",\"LANCETAS\",\"FARMACIA LA ESQUINA\",6,1.25,7.50,7.50\r\n" +
        "\"Total LN 28 (LANCETAS \"\"28\"\")\",,,,,,6,,7.50,7.50\r\n" +
        "\"Total Inventory\",,,,,,166,,669.50,669.50\r\n" +
        "\"Service\",,,,,,,,,\"\"\r\n" +
        "\"1000 (SHIPPING AND DELIVERY)\",,,,,,,,,\"\"\r\n" +
        ",\"Invoice\",\"10/02/2026\",\"2025-0003\",\"ENVIO\",\"FARMACIA LA ESQUINA\",1,12.00,12.00,12.00\r\n" +
        "\"Total 1000 (SHIPPING AND DELIVERY)\",,,,,,1,,12.00,12.00\r\n" +
        "\"Total Service\",,,,,,1,,12.00,12.00\r\n" +
        "\"Discounts\",,,,,,,,,\"\"\r\n" +
        "\"Discount 2%\",,,,,,,,,\"\"\r\n" +
        ",\"Invoice\",\"10/02/2026\",\"2025-0003\",\"DESCUENTO\",\"FARMACIA LA ESQUINA\",\"\",\"-2.00%\",-0.15,-0.15\r\n" +
        "\"Total Discount 2%\",,,,,,0,,-0.15,-0.15\r\n" +
        "\"Total Discounts\",,,,,,0,,-0.15,-0.15\r\n" +
        "\"TOTAL\",,,,,,167,,681.35,681.35\r\n";

    private const string UnknownCsv =
        ",\"Type\",\"Date\",\"Num\",\"Memo\",\"Name\",\"Qty\",\"Sales Price\",\"Amount\",\"Balance\"\n" +
        "\"Inventory\",,,,,,,,,\"\"\n" +
        "\"ZZ 9 (PRODUCTO RARO)\",,,,,,,,,\"\"\n" +
        ",\"Invoice\",\"08/03/2026\",\"2025-9000\",\"RARO\",\"FARMACIA NUEVA\",2,10.00,20.00,20.00\n" +
        "\"Total ZZ 9 (PRODUCTO RARO)\",,,,,,2,,20.00,20.00\n" +
        "\"Total Inventory\",,,,,,2,,20.00,20.00\n" +
        "\"TOTAL\",,,,,,2,,20.00,20.00\n";

    private static byte[] Cp1252(string s)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(1252).GetBytes(s);
    }

    private sealed record World(WmsFixture F, SalesAnalysisSetupService Setup, SalesAnalysisService Analysis, SalesHistoryImportService Import,
        int Ana, int Esquina, int Canon, int Strips, int Lancets, int Raro, int Nueva);

    private static async Task<World> WorldAsync()
    {
        var f = await WmsFixture.CreateAsync(s =>
        {
            s.AddSingleton<ITenantClock>(new TenantClock(LocalDay.DefaultZone, () => Now));
            s.AddSingleton<SalesAnalysisLineReader>();
            s.AddSingleton<SalesAnalysisService>();
            s.AddSingleton<SalesAnalysisSetupService>();
            s.AddSingleton<SalesHistoryImportService>();
        });
        var esquina = await f.AddClientAsync("C1");
        var canon = await f.AddClientAsync("C2");
        var nueva = await f.AddClientAsync("C3");
        var strips = await f.AddProductAsync("AB-100");
        var lancets = await f.AddProductAsync("LN28");
        var raro = await f.AddProductAsync("RARO-1");
        foreach (var (c, name) in new[] { (esquina, "FARMACIA LA ESQUINA"), (canon, "Farmacia Cañon"), (nueva, "Farmacia Otra") }) f.Db.Attach(c).Entity.Name = name;
        f.Db.Attach(strips).Entity.Brand = "MARCA A";
        strips.SalePrice = 4.20m; strips.PurchaseCost = 3m;
        f.Db.Attach(lancets).Entity.Brand = "Marca B";
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();

        var setup = f.Get<SalesAnalysisSetupService>();
        var norte = await setup.CreateRouteAsync(new SalesRouteRequest("Norte", 1), default);
        var ana = await setup.CreateRepAsync(new SalesRepRequest("Ana Pérez", norte.Id), default);
        await setup.AssignClientAsync(esquina.ClientId, new SalesClientRepRequest(ana.Id), default);
        await setup.SetQuotasAsync("2026-09", new SalesQuotaBulkRequest(new[] { new SalesQuotaItemRequest(ana.Id, 400m) }), default);
        var global = await setup.CreateLineAsync(new ProductLineRequest("Global Imports"), default);
        await setup.SetLineBrandsAsync(global.Id, new ProductLineBrandsRequest(new[] { "Marca A" }), default);
        return new World(f, setup, f.Get<SalesAnalysisService>(), f.Get<SalesHistoryImportService>(), ana.Id, esquina.ClientId, canon.ClientId,
            strips.ProductId, lancets.ProductId, raro.ProductId, nueva.ClientId);
    }

    private static Task<SalesHistoryImportReportDto> ImportAsync(World w, string csv, bool dryRun, string file = "SalesByItemDetail.CSV")
        => w.Import.ImportAsync("Prueba", new[] { new SalesHistoryFile(file, Cp1252(csv), C.KindSales) }, dryRun, null, false, C.ImportApi, default);

    [Fact]
    public async Task Import_simulates_saves_and_reimports_the_same_file_without_duplicating()
    {
        var w = await WorldAsync();
        await using var _ = w.F;

        var dry = await ImportAsync(w, SalesCsv, dryRun: true);
        var f = Assert.Single(dry.Files);
        Assert.True(dry.DryRun);
        Assert.Equal((6, 6, SalesHistoryImportService.ActionNew), (f.LinesRead, f.LinesSaved, f.Action));
        Assert.Equal(4, f.LinesByClass[C.ClassProduct]);
        Assert.Equal(1, f.LinesByClass[C.ClassDiscount]);
        Assert.Equal(1, f.LinesByClass[C.ClassShipping]);
        Assert.Equal(681.35m, f.TotalAmount);
        Assert.Equal(681.35m, f.ReportTotal);
        Assert.Empty(f.UnmatchedClients);              // «FARMACIA CAÑÓN» = «Farmacia Cañon» (sin acentos ni mayúsculas)
        Assert.Empty(f.UnmatchedItems);                // «LN 28» = LN28 (sin espacios)
        Assert.Equal(new[] { "2026-09", "2026-10" }, f.ByMonth.Select(m => m.Month).ToArray());
        Assert.Equal(0, await w.F.Db.Set<SalesHistoryLine>().CountAsync());

        var real = await ImportAsync(w, SalesCsv, dryRun: false);
        Assert.Equal(SalesHistoryImportService.ActionNew, real.Files[0].Action);
        Assert.Equal(6, await w.F.Db.Set<SalesHistoryLine>().CountAsync());
        var lancetLine = await w.F.Db.Set<SalesHistoryLine>().SingleAsync(l => l.ItemSku == "LN 28");
        Assert.Equal((w.Lancets, w.Esquina), (lancetLine.ProductId!.Value, lancetLine.ClientId!.Value));
        Assert.Equal(w.Canon, (await w.F.Db.Set<SalesHistoryLine>().SingleAsync(l => l.TxnType == "Credit Memo")).ClientId);

        var again = await ImportAsync(w, SalesCsv, dryRun: false);
        Assert.Equal((SalesHistoryImportService.ActionReplaceSameFile, 6), (again.Files[0].Action, again.Files[0].ReplacedLines));
        Assert.Equal(6, await w.F.Db.Set<SalesHistoryLine>().CountAsync());
        Assert.Equal(1, await w.F.Db.Set<SalesHistoryBatch>().CountAsync(b => b.IsActive));
    }

    [Fact]
    public async Task By_rep_view_attributes_the_sale_to_the_client_rep_and_reconciles_the_month()
    {
        var w = await WorldAsync();
        await using var _ = w.F;
        await ImportAsync(w, SalesCsv, dryRun: false);

        var v = await w.Analysis.GetByRepAsync("2026-10", null, default);
        Assert.Equal(("2026-10", "2026-09"), (v.Month, v.CompareMonth));
        var ana = Assert.Single(v.Reps);
        Assert.Equal(("Norte", "Ana Pérez", 200m, 511.35m, 311.35m), (ana.RouteName, ana.RepName, ana.CompareSales, ana.Sales, ana.Change));
        Assert.Equal(1.5568m, ana.ChangePct);
        Assert.Equal(400m, ana.Quota);                   // la de septiembre sigue valiendo en octubre
        Assert.Equal(1.2784m, ana.Compliance);

        var rec = v.Reconciliation;
        Assert.Equal((511.35m, -42m, 12m, 481.35m, 0m), (rec.RoutesTotal, rec.Unassigned, rec.ShippingAndOther, rec.MonthTotal, rec.Difference));
        Assert.Equal((469.50m, -0.15m, 12m, 1), (rec.ProductSales, rec.Discounts, rec.Shipping, rec.UnassignedClients));

        var global = v.Lines.Single(l => l.Line == "Global Imports");
        Assert.Equal((462m, "Ana Pérez", "Norte", 504m), (global.Sales, global.LeaderName, global.LeaderRoute, global.LeaderSales));
        Assert.Contains(v.Lines, l => l.Line == "Marca B" && l.Sales == 7.50m);          // marca sin línea = su propia línea
        Assert.Contains(v.Lines, l => l.IsDiscounts && l.Sales == -0.15m);
        Assert.Contains(v.RepLines, l => l.RepName == R.NoRepName && l.Line == "Global Imports" && l.Sales == -42m);
        Assert.Contains(v.RepProducts, p => p.RepId == w.Ana && p.ProductId == w.Strips && p.Units == 120m && p.Sales == 504m);
        // ruta × producto: cada producto vendido en el mes para cada ruta (y lo sin representante aparte)
        Assert.Equal(4, v.RouteProducts.Count);
        Assert.Contains(v.RouteProducts, p => p.RouteName == R.NoRepName && p.ProductId == w.Lancets && p.Units == 0m);

        var tx = await w.Analysis.GetTransactionsAsync("2026-10", null, null, 0, 2, false, default);
        Assert.Equal((5, 2), (tx.Total, tx.Items.Count));
        var all = await w.Analysis.GetTransactionsAsync("2026-10", 0, null, 0, 0, true, default);
        Assert.Equal(-42m, Assert.Single(all.Items).Amount);   // repId 0 = sin representante

        var bad = await Assert.ThrowsAsync<ValidationException>(() => w.Analysis.GetByRepAsync("10-2026", null, default));
        Assert.Equal(R.MonthInvalid, bad.Message);
    }

    [Fact]
    public async Task Movement_gives_status_abc_and_monthly_matrices()
    {
        var w = await WorldAsync();
        await using var _ = w.F;
        await ImportAsync(w, SalesCsv, dryRun: false);

        var m = await w.Analysis.GetMovementAsync("2025-12", "2026-11", new DateOnly(2026, 11, 5), null, null, 3, default);
        Assert.Equal(("2026-10", 12), (m.LastCompleteMonth, m.Months.Count));
        var strips = m.Products.Single(p => p.ProductId == w.Strips);
        Assert.Equal(("A", R.StatusNew, 160m, 662m, "Global Imports"), (strips.Abc, strips.Status, strips.Units, strips.Sales, strips.Line));   // empezó en septiembre
        Assert.Equal((4.20m, 3m, 0.2857m), (strips.Price, strips.Cost, strips.MarginPct));
        Assert.Equal((1, 2, "FARMACIA LA ESQUINA", 170m), (strips.Clients, strips.Invoices, strips.TopClient, strips.TopClientUnits));
        Assert.Equal(R.StatusNoSales, m.Products.Single(p => p.ProductId == w.Raro).Status);
        var units = m.UnitsByMonth.Single(s => s.ProductId == w.Strips);
        Assert.Equal(50m, units.Values[m.Months.ToList().IndexOf("2026-09")]);
        Assert.Equal(110m, units.Values[m.Months.ToList().IndexOf("2026-10")]);
        Assert.Contains(m.TopClients, c => c.ProductId == w.Strips && c.Rank == 1 && c.Client == "FARMACIA LA ESQUINA");
    }

    [Fact]
    public async Task With_a_cutoff_history_counts_before_it_and_teikem_invoices_from_it_with_kits_split_and_credits_subtracted()
    {
        var w = await WorldAsync();
        await using var _ = w.F;
        await ImportAsync(w, SalesCsv, dryRun: false);
        // una línea del historial DESPUÉS del corte: no cuenta
        var batch = await w.F.Db.Set<SalesHistoryBatch>().AsNoTracking().FirstAsync();
        w.F.Db.Set<SalesHistoryLine>().Add(new SalesHistoryLine
        {
            TenantId = WmsFixture.TenantId, SalesHistoryBatchId = batch.SalesHistoryBatchId, TxnDate = new DateOnly(2026, 11, 2), TxnType = "Invoice", DocNumber = "2025-9999",
            ClientNameRaw = "FARMACIA LA ESQUINA", ClientId = w.Esquina, ItemRaw = "AB-100 (TIRAS)", ItemSku = "AB-100", ProductId = w.Strips, Quantity = 1000m, Amount = 4000m,
            LineClass = C.ClassProduct,
        });
        var lancets = await w.F.Db.Products.AsTracking().SingleAsync(p => p.ProductId == w.Lancets);
        lancets.SalePrice = 1.25m;
        var kit = await w.F.AddProductAsync("KIT-OFERTA");
        w.F.Db.Attach(kit).Entity.IsOfferKit = true;
        // la factura de Teikem del 3-nov: 10 tiras a 4.00, 1 kit (1 tira + 2 lancetas) a 13.40 y el envío de la orden
        w.F.Db.Set<SalesOrderLineComponent>().AddRange(
            new SalesOrderLineComponent { TenantId = WmsFixture.TenantId, SalesOrderLineId = 9002, ComponentProductId = w.Strips, Quantity = 1m },
            new SalesOrderLineComponent { TenantId = WmsFixture.TenantId, SalesOrderLineId = 9002, ComponentProductId = w.Lancets, Quantity = 2m });
        var invoice = new Teikem.Domain.Billing.Invoice
        {
            PublicId = Guid.NewGuid(), TenantId = WmsFixture.TenantId, ClientId = w.Esquina, InvoiceNumber = "FAC-00001", IssueDate = new DateOnly(2026, 11, 3), CurrencyLookupId = 1,
            StatusCodeId = w.F.StatusId(Teikem.Domain.Billing.BillingCodes.InvoiceStatusDomain, Teikem.Domain.Billing.InvoiceStatuses.Issued), IsActive = true,
        };
        invoice.Lines.Add(new Teikem.Domain.Billing.InvoiceLine { InvoiceLineId = 7001, ProductId = w.Strips, SalesOrderLineId = 9001, Description = "AB-100", Quantity = 10m, UnitAmount = 4m });
        invoice.Lines.Add(new Teikem.Domain.Billing.InvoiceLine { InvoiceLineId = 7002, ProductId = kit.ProductId, SalesOrderLineId = 9002, Description = "KIT", Quantity = 1m, UnitAmount = 13.40m });
        invoice.Lines.Add(new Teikem.Domain.Billing.InvoiceLine { InvoiceLineId = 7003, SalesOrderId = 8001, Description = "Envío", Quantity = 1m, UnitAmount = 5m });
        w.F.Db.Set<Teikem.Domain.Billing.Invoice>().Add(invoice);
        // la nota de crédito aprobada el 4-nov: devuelve 2 de las 10 tiras
        var approved = new Teikem.Domain.Catalogs.StatusCode
        {
            StatusCodeId = 9901, Entity = Teikem.Domain.Billing.CreditCodes.StatusDomain, InternalCode = Teikem.Domain.Billing.CreditNoteStatuses.Approved, LabelJson = "{}",
            StageKindLookupId = w.F.LookupId(LookupDomains.StageKind, StageKinds.Pipeline), IsActive = true,
        };
        w.F.Db.StatusCodes.Add(approved);
        var credit = new Teikem.Domain.Billing.CreditNote
        {
            PublicId = Guid.NewGuid(), TenantId = WmsFixture.TenantId, CreditNumber = "NC-00001", ClientId = w.Esquina, InvoiceId = invoice.InvoiceId, StatusCodeId = approved.StatusCodeId,
            ApprovedAtUtc = new DateTime(2026, 11, 4, 15, 0, 0, DateTimeKind.Utc), CurrencyLookupId = 1,
        };
        credit.Lines.Add(new Teikem.Domain.Billing.CreditNoteLine { InvoiceLineId = 7001, Description = "Devolución", Quantity = 2m, UnitAmount = 4m });
        w.F.Db.Set<Teikem.Domain.Billing.CreditNote>().Add(credit);
        await w.F.Db.SaveChangesAsync();
        w.F.Db.ChangeTracker.Clear();
        await w.Setup.UpdateSettingsAsync(new SalesAnalysisSettingsRequest(CutoffDate: new DateOnly(2026, 11, 1)), default);

        var nov = await w.Analysis.GetByRepAsync("2026-11", null, default);
        var ana = Assert.Single(nov.Reps);
        Assert.Equal(45.40m, ana.Sales);                                   // 40 + 13.40 − 8; la línea del historial del 2-nov no cuenta
        Assert.Equal((5m, 50.40m, 0m), (nov.Reconciliation.Shipping, nov.Reconciliation.MonthTotal, nov.Reconciliation.Difference));
        var tx = await w.Analysis.GetTransactionsAsync("2026-11", null, null, 0, 0, true, default);
        Assert.Contains(tx.Items, t => t.Source == C.SourceInvoice && t.ProductId == w.Strips && t.Quantity == 1m && t.Amount == 8.40m);     // la tira del kit
        Assert.Contains(tx.Items, t => t.Source == C.SourceInvoice && t.ProductId == w.Lancets && t.Quantity == 2m && t.Amount == 5.00m);   // las 2 lancetas del kit
        Assert.Contains(tx.Items, t => t.Source == C.SourceCredit && t.ProductId == w.Strips && t.Quantity == -2m && t.Amount == -8m);
        Assert.DoesNotContain(tx.Items, t => t.ProductId == kit.ProductId);

        var oct = await w.Analysis.GetByRepAsync("2026-10", null, default);
        Assert.Equal(511.35m, oct.Reps.Single().Sales);                    // antes del corte sigue el historial
        var m = await w.Analysis.GetMovementAsync("2026-10", "2026-11", new DateOnly(2026, 11, 5), null, null, null, default);
        var strips = m.UnitsByMonth.Single(s => s.ProductId == w.Strips);
        Assert.Equal(new[] { 110m, 9m }, strips.Values.ToArray());           // noviembre: 10 + 1 del kit − 2 de la nota
        Assert.Empty((await w.Analysis.GetStatusAsync(default)).Warnings.Where(x => x.Contains("traslapa")));
    }

    [Fact]
    public async Task Unmatched_names_are_reported_and_linked_by_hand()
    {
        var w = await WorldAsync();
        await using var _ = w.F;
        var dry = await ImportAsync(w, UnknownCsv, dryRun: true, file: "otro.csv");
        Assert.Equal("FARMACIA NUEVA", Assert.Single(dry.Files[0].UnmatchedClients).Raw);
        Assert.Equal("ZZ 9 (PRODUCTO RARO)", Assert.Single(dry.Files[0].UnmatchedItems).Raw);
        await ImportAsync(w, UnknownCsv, dryRun: false, file: "otro.csv");

        var linkedClient = await w.Setup.LinkAsync(new SalesAliasRequest("CLIENT", "Farmacia Nueva", ClientId: w.Nueva), default);
        Assert.Equal(1, linkedClient.Relinked);
        var linkedItem = await w.Setup.LinkAsync(new SalesAliasRequest("PRODUCT", "ZZ 9 (PRODUCTO RARO)", ProductId: w.Raro), default);
        Assert.Equal(1, linkedItem.Relinked);
        var line = await w.F.Db.Set<SalesHistoryLine>().AsNoTracking().SingleAsync();
        Assert.Equal((w.Nueva, w.Raro), (line.ClientId!.Value, line.ProductId!.Value));

        // la próxima importación ya empareja sola con los enlaces
        var again = await ImportAsync(w, UnknownCsv, dryRun: true, file: "otro.csv");
        Assert.Empty(again.Files[0].UnmatchedClients);
        Assert.Empty(again.Files[0].UnmatchedItems);

        var bad = await Assert.ThrowsAsync<ValidationException>(() => w.Setup.LinkAsync(new SalesAliasRequest("SUPPLIER", "x", 1), default));
        Assert.Equal(R.AliasKindInvalid, bad.Message);
    }

    [Fact]
    public async Task Setup_rules_and_exact_messages()
    {
        var w = await WorldAsync();
        await using var _ = w.F;
        var dup = await Assert.ThrowsAsync<ConflictException>(() => w.Setup.CreateRepAsync(new SalesRepRequest("Ana Pérez"), default));
        Assert.Equal(R.RepNameTaken, dup.Message);
        var busy = await Assert.ThrowsAsync<ConflictException>(() => w.Setup.SetRepActiveAsync(w.Ana, false, default));
        Assert.Equal(R.RepHasClients, busy.Message);
        var routes = await w.Setup.ListRoutesAsync(false, default);
        var hasReps = await Assert.ThrowsAsync<ConflictException>(() => w.Setup.SetRouteActiveAsync(routes[0].Id, false, default));
        Assert.Equal(R.RouteHasReps, hasReps.Message);
        var other = await w.Setup.CreateLineAsync(new ProductLineRequest("Otra"), default);
        var taken = await Assert.ThrowsAsync<ConflictException>(() => w.Setup.SetLineBrandsAsync(other.Id, new ProductLineBrandsRequest(new[] { "MARCA A" }), default));
        Assert.Equal(R.BrandTaken("Marca A", "Global Imports"), taken.Message);
        var quota = await Assert.ThrowsAsync<ValidationException>(() => w.Setup.SetQuotasAsync("2026-10",
            new SalesQuotaBulkRequest(new[] { new SalesQuotaItemRequest(w.Ana, 10.555m) }), default));
        Assert.Equal(R.QuotaInvalid, quota.Message);
        var notFound = await Assert.ThrowsAsync<NotFoundException>(() => w.Setup.UpdateRouteAsync(999, new SalesRouteRequest("X"), default));
        Assert.Equal("Ruta no encontrada.", notFound.Message);

        // cuota: quitar la de un mes vuelve a la anterior
        await w.Setup.SetQuotasAsync("2026-10", new SalesQuotaBulkRequest(new[] { new SalesQuotaItemRequest(w.Ana, 500m) }), default);
        Assert.Equal(500m, (await w.Setup.GetQuotasAsync("2026-10", default)).Rows.Single().Effective);
        await w.Setup.SetQuotasAsync("2026-10", new SalesQuotaBulkRequest(new[] { new SalesQuotaItemRequest(w.Ana, null) }), default);
        var row = (await w.Setup.GetQuotasAsync("2026-10", default)).Rows.Single();
        Assert.Equal(((decimal?)null, (decimal?)400m, "2026-09"), (row.Amount, row.Effective, row.EffectiveFrom));

        var settings = await w.Setup.UpdateSettingsAsync(new SalesAnalysisSettingsRequest(CutoffDate: new DateOnly(2026, 11, 6), LeadTimeDays: 7), default);
        Assert.Equal((new DateOnly(2026, 11, 6), 7, 1.645m), (settings.CutoffDate!.Value, settings.LeadTimeDays, settings.ServiceLevelZ));
        var lead = await Assert.ThrowsAsync<ValidationException>(() => w.Setup.UpdateSettingsAsync(new SalesAnalysisSettingsRequest(LeadTimeDays: 0), default));
        Assert.Equal(R.LeadTimeInvalid, lead.Message);
    }
}
