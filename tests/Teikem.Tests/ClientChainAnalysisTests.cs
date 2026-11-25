using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Clients;
using Teikem.Domain.Common;
using Teikem.Domain.Constants;
using Teikem.Domain.Sales;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Analytics;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Migration;
using Teikem.Infrastructure.Services;
using Xunit;
using C = Teikem.Domain.Sales.SalesAnalysisCodes;
using R = Teikem.Domain.Sales.SalesAnalysisRules;

namespace Teikem.Tests;

/// <summary>
/// Lote 245 (Lote B del plan) — análisis de ventas por cadena en la base en memoria (datos INVENTADOS): historial importado de verdad con SalesHistoryImportService más
/// una factura de Teikem y una línea del historial sin emparejar. «Por cadena» cuadra al centavo y concilia con la venta neta del mes; lo sin emparejar cae en «Sin
/// cadena»; al mover un cliente de cadena su historial se reagrupa; con chainPublicId solo esa cadena; la serie de 12 meses y los clientes principales; los mensajes
/// exactos y el 404 de una cadena de otra compañía; y las fuentes de datos CLIENT_CHAIN, CLIENT, SALES_ANALYSIS_LINE y SALES_ORDER con la cadena.
/// </summary>
public sealed class ClientChainAnalysisTests
{
    private static readonly DateTime Now = new(2026, 11, 5, 16, 0, 0, DateTimeKind.Utc);
    private static readonly ITenantClock Clock = new TenantClock(LocalDay.DefaultZone, () => Now);

    // septiembre: Esquina 200; octubre: Esquina 504 + 7.50 − 0.15 (descuento) y 12 de envío, Cañón −42 (nota de crédito)
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

    private sealed record World(WmsFixture F, ClientChainAnalysisService Chains, SalesAnalysisService Analysis, int Esquina, int Canon, int Otra, int Strips,
        ClientChain Walgreens, ClientChain Cvs, ClientChain Old, ClientChain Foreign);

    private static byte[] Cp1252(string s)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(1252).GetBytes(s);
    }

    private static async Task<World> WorldAsync()
    {
        var f = await WmsFixture.CreateAsync(s =>
        {
            s.AddSingleton<ITenantClock>(Clock);
            s.AddSingleton<SalesAnalysisLineReader>();
            s.AddSingleton<SalesAnalysisService>();
            s.AddSingleton<SalesAnalysisSetupService>();
            s.AddSingleton<SalesHistoryImportService>();
            s.AddSingleton<ClientChainAnalysisService>();
        });
        var esquina = await f.AddClientAsync("C1");
        var canon = await f.AddClientAsync("C2");
        var otra = await f.AddClientAsync("C3");
        var strips = await f.AddProductAsync("AB-100");
        await f.AddProductAsync("LN28");
        foreach (var (c, name) in new[] { (esquina, "FARMACIA LA ESQUINA"), (canon, "Farmacia Cañon"), (otra, "Farmacia Otra") }) f.Db.Attach(c).Entity.Name = name;
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();

        ClientChain Chain(string name, bool active = true, int tenantId = WmsFixture.TenantId)
        {
            var ch = new ClientChain { PublicId = Guid.NewGuid(), TenantId = tenantId, Name = name, IsActive = active, CreatedAtUtc = Now };
            f.Db.Set<ClientChain>().Add(ch);
            return ch;
        }
        var walgreens = Chain("Walgreens");
        var cvs = Chain("CVS");
        var old = Chain("Cadena vieja", active: false);
        var foreign = Chain("Walgreens ajena", tenantId: WmsFixture.OtherTenantId);
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        await SetChainAsync(f, esquina.ClientId, walgreens.ClientChainId);
        await SetChainAsync(f, canon.ClientId, cvs.ClientChainId);

        // el historial de QuickBooks, importado de verdad
        await f.Get<SalesHistoryImportService>().ImportAsync("Prueba", new[] { new SalesHistoryFile("SalesByItemDetail.CSV", Cp1252(SalesCsv), C.KindSales) },
            false, null, false, C.ImportApi, default);
        // una línea del historial de octubre que no se emparejó con ningún cliente (30)
        var batch = await f.Db.Set<SalesHistoryBatch>().AsNoTracking().FirstAsync();
        f.Db.Set<SalesHistoryLine>().Add(new SalesHistoryLine
        {
            TenantId = WmsFixture.TenantId, SalesHistoryBatchId = batch.SalesHistoryBatchId, TxnDate = new DateOnly(2026, 10, 20), TxnType = "Invoice", DocNumber = "2025-0100",
            ClientNameRaw = "Farmacia Sin Emparejar", ItemRaw = "AB-100 (TIRAS)", ItemSku = "AB-100", ProductId = strips.ProductId, Quantity = 10m, Amount = 30m,
            LineClass = C.ClassProduct,
        });
        // una factura de Teikem de octubre a «Farmacia Otra» (sin cadena): 10 tiras a 4.00 y 5 de envío (el envío no cuenta)
        var invoice = new Teikem.Domain.Billing.Invoice
        {
            PublicId = Guid.NewGuid(), TenantId = WmsFixture.TenantId, ClientId = otra.ClientId, InvoiceNumber = "FAC-00001", IssueDate = new DateOnly(2026, 10, 21),
            CurrencyLookupId = 1, IsActive = true,
            StatusCodeId = f.StatusId(Teikem.Domain.Billing.BillingCodes.InvoiceStatusDomain, Teikem.Domain.Billing.InvoiceStatuses.Issued),
        };
        invoice.Lines.Add(new Teikem.Domain.Billing.InvoiceLine { InvoiceLineId = 7001, ProductId = strips.ProductId, SalesOrderLineId = 9001, Description = "AB-100", Quantity = 10m, UnitAmount = 4m });
        invoice.Lines.Add(new Teikem.Domain.Billing.InvoiceLine { InvoiceLineId = 7002, SalesOrderId = 8001, Description = "Envío", Quantity = 1m, UnitAmount = 5m });
        f.Db.Set<Teikem.Domain.Billing.Invoice>().Add(invoice);
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        return new World(f, f.Get<ClientChainAnalysisService>(), f.Get<SalesAnalysisService>(), esquina.ClientId, canon.ClientId, otra.ClientId, strips.ProductId,
            walgreens, cvs, old, foreign);
    }

    private static async Task SetChainAsync(WmsFixture f, int clientId, int? chainId)
    {
        var c = await f.Db.Clients.AsTracking().SingleAsync(x => x.ClientId == clientId);
        c.ChainId = chainId;
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
    }

    [Fact]
    public async Task By_chain_with_imported_history_reconciles_to_the_cent_and_regroups_when_a_client_moves()
    {
        var w = await WorldAsync();
        await using var _ = w.F;

        var v = await w.Chains.GetSalesByChainAsync(null, null, null, default);   // por omisión: octubre contra septiembre (hoy es 5-nov)
        Assert.Equal(("2026-10", "2026-09"), (v.Month, v.CompareMonth));
        Assert.Equal(new[] { "Walgreens", "CVS" }, v.Rows.Select(r => r.ChainName).ToArray());   // la inactiva sin venta no sale; orden por venta
        var wal = v.Rows[0];
        Assert.Equal((w.Walgreens.PublicId, true, 1, 1), (wal.ChainPublicId!.Value, wal.IsActive, wal.ClientCount, wal.ClientsWithSales));
        Assert.Equal((200m, 511.35m, 311.35m, 1.5568m), (wal.CompareSales, wal.Sales, wal.Change, wal.ChangePct));   // producto + descuento; el envío no
        var esquina = Assert.Single(wal.Clients);
        Assert.Equal(("FARMACIA LA ESQUINA", 511.35m, (decimal?)1m), (esquina.ClientName, esquina.Sales, esquina.ShareOfChain));
        Assert.NotNull(esquina.ClientPublicId);
        var cvs = v.Rows[1];
        Assert.Equal((0m, -42m, (decimal?)null), (cvs.CompareSales, cvs.Sales, cvs.ChangePct));

        // «Sin cadena»: el cliente sin cadena (factura de Teikem) y el nombre del historial sin emparejar
        var none = Assert.IsType<Teikem.Infrastructure.Contracts.ClientChainSalesRowDto>(v.NoChain);
        Assert.Equal((ClientChainCodes.NoChainName, (Guid?)null, 70m), (none.ChainName, none.ChainPublicId, none.Sales));
        Assert.Contains(none.Clients, c => c.ClientName == "Farmacia Otra" && c.ClientPublicId != null && c.Sales == 40m);
        Assert.Contains(none.Clients, c => c.ClientName == "Farmacia Sin Emparejar" && c.ClientPublicId == null && c.Sales == 30m);

        // conciliación: cadenas + sin cadena = venta neta del mes, al centavo
        Assert.Equal((469.35m, 70m, 539.35m, 0m), (v.Reconciliation.ChainsTotal, v.Reconciliation.NoChain, v.Reconciliation.MonthNet, v.Reconciliation.Difference));
        Assert.Equal((200m, 539.35m), (v.Totals.CompareSales, v.Totals.Sales));
        Assert.Equal(v.Rows.Sum(r => r.Sales), v.Rows.Sum(r => r.Clients.Sum(c => c.Sales)));

        // con chainPublicId: solo esa cadena con sus clientes, sin «Sin cadena», y la conciliación del mes completo
        var one = await w.Chains.GetSalesByChainAsync("2026-10", "2026-09", w.Walgreens.PublicId, default);
        Assert.Equal("Walgreens", Assert.Single(one.Rows).ChainName);
        Assert.Null(one.NoChain);
        Assert.Equal((511.35m, 539.35m, 0m), (one.Totals.Sales, one.Reconciliation.MonthNet, one.Reconciliation.Difference));

        // mover a Esquina de Walgreens a CVS: su historial se va con ella (la venta es de la cadena ACTUAL)
        await SetChainAsync(w.F, w.Esquina, w.Cvs.ClientChainId);
        var moved = await w.Chains.GetSalesByChainAsync("2026-10", null, null, default);
        Assert.Equal(new[] { "CVS", "Walgreens" }, moved.Rows.Select(r => r.ChainName).ToArray());
        Assert.Equal((200m, 469.35m), (moved.Rows[0].CompareSales, moved.Rows[0].Sales));
        Assert.Equal((0m, 0m, 0), (moved.Rows[1].Sales, moved.Rows[1].CompareSales, moved.Rows[1].ClientCount));
        Assert.Equal(0m, moved.Reconciliation.Difference);

        // una cadena inactiva con venta sí sale
        await SetChainAsync(w.F, w.Otra, w.Old.ClientChainId);
        var withOld = await w.Chains.GetSalesByChainAsync("2026-10", null, null, default);
        var oldRow = withOld.Rows.Single(r => r.ChainName == "Cadena vieja");
        Assert.Equal((false, 40m), (oldRow.IsActive, oldRow.Sales));
        Assert.Equal(30m, withOld.NoChain!.Sales);
    }

    [Fact]
    public async Task By_chain_rejects_a_bad_month_and_a_chain_of_another_company()
    {
        var w = await WorldAsync();
        await using var _ = w.F;

        var bad = await Assert.ThrowsAsync<ValidationException>(() => w.Chains.GetSalesByChainAsync("10-2026", null, null, default));
        Assert.Equal("El mes debe tener la forma AAAA-MM (por ejemplo 2026-10).", bad.Message);
        var badCompare = await Assert.ThrowsAsync<ValidationException>(() => w.Chains.GetSalesByChainAsync("2026-10", "2026-13", null, default));
        Assert.Equal(R.MonthInvalid, badCompare.Message);

        var foreign = await Assert.ThrowsAsync<NotFoundException>(() => w.Chains.GetSalesByChainAsync("2026-10", null, w.Foreign.PublicId, default));
        Assert.Equal(ClientChainRules.NotFound, foreign.Message);
        await Assert.ThrowsAsync<NotFoundException>(() => w.Chains.GetSalesByChainAsync(null, null, Guid.NewGuid(), default));
        // la otra compañía no ve la venta ni las cadenas de esta
        using (w.F.AsTenant(WmsFixture.OtherTenantId))
        {
            var other = await w.Chains.GetSalesByChainAsync("2026-10", null, null, default);
            Assert.Equal(new[] { "Walgreens ajena" }, other.Rows.Select(r => r.ChainName).ToArray());
            Assert.Equal((0m, 0m), (other.Reconciliation.MonthNet, other.Totals.Sales));
            await Assert.ThrowsAsync<NotFoundException>(() => w.Chains.GetSalesByChainAsync("2026-10", null, w.Walgreens.PublicId, default));
        }
    }

    [Fact]
    public async Task Series_gives_twelve_months_and_the_top_clients_with_their_share()
    {
        var w = await WorldAsync();
        await using var _ = w.F;
        await SetChainAsync(w.F, w.Otra, w.Walgreens.ClientChainId);   // Walgreens: Esquina (200 + 511.35) y Otra (40)

        var s = await w.Chains.GetChainSeriesAsync(w.Walgreens.PublicId, null, null, default);
        Assert.Equal((w.Walgreens.PublicId, "Walgreens", "2025-11", "2026-10"), (s.ChainPublicId, s.ChainName, s.FromMonth, s.ToMonth));
        Assert.Equal(12, s.Months.Count);
        Assert.Equal(("2026-09", 200m), (s.Months[10].Month, s.Months[10].Sales));
        Assert.Equal(("2026-10", 551.35m), (s.Months[11].Month, s.Months[11].Sales));
        Assert.Equal(0m, s.Months[0].Sales);
        Assert.Equal(new[] { "FARMACIA LA ESQUINA", "Farmacia Otra" }, s.TopClients.Select(c => c.Name).ToArray());
        var top = s.TopClients[0];
        Assert.Equal(("C1", 711.35m, R.Share(711.35m, 751.35m)), (top.Code, top.Sales, top.Share));
        Assert.Equal(R.Share(40m, 751.35m), s.TopClients[1].Share);

        var range = await w.Chains.GetChainSeriesAsync(w.Walgreens.PublicId, "2026-09", "2026-10", default);
        Assert.Equal(new[] { "2026-09", "2026-10" }, range.Months.Select(m => m.Month).ToArray());

        var tooLong = await Assert.ThrowsAsync<ValidationException>(() => w.Chains.GetChainSeriesAsync(w.Walgreens.PublicId, "2023-01", "2026-10", default));
        Assert.Equal("El rango no puede pasar de 36 meses.", tooLong.Message);
        var noChain = await Assert.ThrowsAsync<ValidationException>(() => w.Chains.GetChainSeriesAsync(null, null, null, default));
        Assert.Equal("Indique la cadena (chainPublicId).", noChain.Message);
        var reversed = await Assert.ThrowsAsync<ValidationException>(() => w.Chains.GetChainSeriesAsync(w.Walgreens.PublicId, "2026-10", "2026-09", default));
        Assert.Equal(R.MonthRangeInvalid, reversed.Message);
        var foreign = await Assert.ThrowsAsync<NotFoundException>(() => w.Chains.GetChainSeriesAsync(w.Foreign.PublicId, null, null, default));
        Assert.Equal(ClientChainRules.NotFound, foreign.Message);

        // una cadena sin clientes: meses en 0 y sin clientes principales
        var empty = await w.Chains.GetChainSeriesAsync(w.Old.PublicId, null, null, default);
        Assert.All(empty.Months, m => Assert.Equal(0m, m.Sales));
        Assert.Empty(empty.TopClients);
    }

    [Fact]
    public async Task Data_sources_expose_the_current_chain()
    {
        var w = await WorldAsync();
        await using var _ = w.F;
        var inactive = await w.F.AddClientAsync("C9");
        await SetChainAsync(w.F, inactive.ClientId, w.Walgreens.ClientChainId);
        var tracked = await w.F.Db.Clients.AsTracking().SingleAsync(c => c.ClientId == inactive.ClientId);
        tracked.IsActive = false;
        await w.F.Db.SaveChangesAsync();
        w.F.Db.ChangeTracker.Clear();

        // CLIENT_CHAIN: sus campos, clientes activos e inactivos, sin las de otra compañía, y por ids
        var chains = new ClientChainDataSource(w.F.Db);
        Assert.Equal((ClientChainCodes.DataSourceKey, (string?)null, (string?)null, ModuleKeys.SalesOrders),
            (chains.Key, chains.DateField, chains.EntityTypeCode, chains.TenantModule));
        Assert.Equal(new[] { "Id", "PublicId", "Name", "Notes", "IsActive", "ClientCount", "InactiveClientCount", "CreatedAtUtc" }, chains.Fields.Select(x => x.Key).ToArray());
        var chainRows = (await chains.LoadAsync(new DataQuery(), default)).ToDictionary(r => (int)r["Id"]!);
        Assert.Equal(new[] { w.Walgreens.ClientChainId, w.Cvs.ClientChainId, w.Old.ClientChainId }.OrderBy(x => x), chainRows.Keys.OrderBy(x => x));
        Assert.Equal(("Walgreens", true, 1, 1), ((string)chainRows[w.Walgreens.ClientChainId]["Name"]!, (bool)chainRows[w.Walgreens.ClientChainId]["IsActive"]!,
            (int)chainRows[w.Walgreens.ClientChainId]["ClientCount"]!, (int)chainRows[w.Walgreens.ClientChainId]["InactiveClientCount"]!));
        Assert.Equal((false, 0), ((bool)chainRows[w.Old.ClientChainId]["IsActive"]!, (int)chainRows[w.Old.ClientChainId]["ClientCount"]!));
        var byIds = await chains.LoadAsync(new DataQuery { Ids = new[] { w.Cvs.ClientChainId, w.Foreign.ClientChainId } }, default);
        Assert.Equal(new[] { w.Cvs.ClientChainId }, byIds.Select(r => (int)r["Id"]!).ToArray());
        Assert.Equal(PermissionCatalog.ClientsRead, PermissionCatalog.DataSourceReadPermission(ClientChainCodes.DataSourceKey));

        // CLIENT: ChainId y ChainName al final y la relación Chain → CLIENT_CHAIN
        var clients = new ClientDataSource(w.F.Db, w.F.Lookups, w.F.Tenant, Clock);
        Assert.Equal(new[] { "ChainId", "ChainName" }, clients.Fields.TakeLast(2).Select(x => x.Key).ToArray());
        var rel = Assert.Single(clients.Relations, r => r.Key == "Chain");
        Assert.Equal((ClientChainCodes.DataSourceKey, "ChainId"), (rel.TargetSourceKey, rel.LocalField));
        var clientRows = (await clients.LoadAsync(new DataQuery(), default)).ToDictionary(r => (int)r["Id"]!);
        Assert.Equal(((int?)w.Walgreens.ClientChainId, "Walgreens"), ((int?)clientRows[w.Esquina]["ChainId"], (string?)clientRows[w.Esquina]["ChainName"]));
        Assert.Equal(((object?)null, (object?)null), (clientRows[w.Otra]["ChainId"], clientRows[w.Otra]["ChainName"]));

        // SALES_ANALYSIS_LINE: ChainName nativo, «Sin cadena» si no hay
        var lines = new SalesAnalysisLineDataSource(w.Analysis, w.F.Db, Clock);
        Assert.Contains(lines.Fields, x => x.Key == "ChainName");
        var lineRows = await lines.LoadAsync(new DataQuery { FromDay = new DateOnly(2026, 10, 1), ToDayExclusive = new DateOnly(2026, 11, 1) }, default);
        Assert.All(lineRows.Where(r => (int?)r["ClientId"] == w.Esquina), r => Assert.Equal("Walgreens", r["ChainName"]));
        Assert.All(lineRows.Where(r => (int?)r["ClientId"] == w.Canon), r => Assert.Equal("CVS", r["ChainName"]));
        Assert.Contains(lineRows, r => (int?)r["ClientId"] == w.Otra && (string?)r["ChainName"] == ClientChainCodes.NoChainName);
        Assert.Contains(lineRows, r => r["ClientId"] is null && (string?)r["ChainName"] == ClientChainCodes.NoChainName);

        // SALES_ORDER: ChainId y ChainName EN VIVO desde el cliente
        var wh = await w.F.AddWarehouseAsync("WH1");
        var order = new SalesOrder
        {
            PublicId = Guid.NewGuid(), TenantId = WmsFixture.TenantId, Number = "OV-CH1", WarehouseId = wh.WarehouseId, ClientId = w.Esquina,
            RequestedDate = new DateOnly(2026, 11, 2), StatusCodeId = 1, CreatedAtUtc = Now,
        };
        var noChainOrder = new SalesOrder
        {
            PublicId = Guid.NewGuid(), TenantId = WmsFixture.TenantId, Number = "OV-CH2", WarehouseId = wh.WarehouseId, ClientId = w.Otra,
            RequestedDate = new DateOnly(2026, 11, 2), StatusCodeId = 1, CreatedAtUtc = Now,
        };
        w.F.Db.SalesOrders.AddRange(order, noChainOrder);
        await w.F.Db.SaveChangesAsync();
        w.F.Db.ChangeTracker.Clear();
        var orders = new SalesOrderDataSource(w.F.Db, w.F.Lookups, w.F.Tenant, Clock);
        Assert.Equal(new[] { "ChainId", "ChainName" }, orders.Fields.TakeLast(2).Select(x => x.Key).ToArray());
        var orderRows = (await orders.LoadAsync(new DataQuery(), default)).ToDictionary(r => (int)r["Id"]!);
        Assert.Equal(((int?)w.Walgreens.ClientChainId, "Walgreens"), ((int?)orderRows[order.SalesOrderId]["ChainId"], (string?)orderRows[order.SalesOrderId]["ChainName"]));
        Assert.Equal(((object?)null, (object?)null), (orderRows[noChainOrder.SalesOrderId]["ChainId"], orderRows[noChainOrder.SalesOrderId]["ChainName"]));
        await SetChainAsync(w.F, w.Esquina, w.Cvs.ClientChainId);
        var after = (await orders.LoadAsync(new DataQuery { Ids = new[] { order.SalesOrderId } }, default)).Single();
        Assert.Equal("CVS", after["ChainName"]);   // sin copia en la orden: sale con la cadena nueva

        // las fuentes que se construyen con null! (solo para leer sus campos) siguen funcionando
        Assert.Contains(new SalesOrderDataSource(null!, null!, null!).Fields, x => x.Key == "ChainName");
    }
}
