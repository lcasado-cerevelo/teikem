using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Analytics;
using Teikem.Domain.Audit;
using Teikem.Domain.Catalogs;
using Teikem.Domain.Identity;
using Teikem.Domain.Billing;
using Teikem.Domain.Clients;
using Teikem.Domain.Cod;
using Teikem.Domain.Constants;
using Teikem.Domain.Orders;
using Teikem.Domain.Sales;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Analytics;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;
using Xunit;
using Trip = Teikem.Domain.Trips.Trip;

namespace Teikem.Tests;

// Lote 91 (P12): fuentes COD_COLLECTION e INVOICE, el contenido de fábrica que las usa y la «Actividad reciente» de Operación y Contabilidad.
public sealed class FinanceAnalyticsTests
{
    private sealed class World(WmsFixture f, CodService cod, Client client)
    {
        public WmsFixture F { get; } = f;
        public CodService Cod { get; } = cod;
        public Client Client { get; } = client;
    }

    private static async Task<World> NewWorldAsync()
    {
        var f = await WmsFixture.CreateAsync(s =>
        {
            s.AddSingleton<ITenantClock>(TenantClock.Default);
            s.AddSingleton<CodService>();
        });
        f.SetModules(ModuleKeys.LtlGround, ModuleKeys.Cod, ModuleKeys.Billing);
        return new World(f, f.Get<CodService>(), await f.AddClientAsync("C1"));
    }

    private static async Task<TransportOrder> CodOrderAsync(World x, string number, decimal cod, string invoice, string status = OrderStatuses.Delivered)
    {
        var o = await x.F.AddOrderAsync(x.Client.ClientId, number, status);
        var t = await x.F.Db.TransportOrders.AsTracking().SingleAsync(r => r.TransportOrderId == o.TransportOrderId);
        (t.CodAmount, t.CodStatusCodeId, t.ClientInvoiceNumber) = (cod, x.F.StatusId(StatusDomains.CodStatus, CodStatuses.Pending), invoice);
        await x.F.Db.SaveChangesAsync();
        x.F.Db.ChangeTracker.Clear();
        return t;
    }

    private static async Task<Invoice> InvoiceAsync(World x, string number, string status, decimal subtotal, DateOnly? issue, DateOnly? due, decimal paid = 0m)
    {
        var inv = new Invoice
        {
            PublicId = Guid.NewGuid(), TenantId = WmsFixture.TenantId, ClientId = x.Client.ClientId, InvoiceNumber = number,
            StatusCodeId = x.F.StatusId(BillingCodes.InvoiceStatusDomain, status), IssueDate = issue, DueDate = due,
            CurrencyLookupId = 1, Subtotal = subtotal, TaxAmount = Math.Round(subtotal * 0.1m, 2), IsActive = true,
        };
        x.F.Db.Set<Invoice>().Add(inv);
        await x.F.Db.SaveChangesAsync();
        if (paid > 0m)
        {
            x.F.Db.Set<Payment>().Add(new Payment { TenantId = WmsFixture.TenantId, InvoiceId = inv.InvoiceId, PaymentMethodLookupId = x.F.LookupId(BillingCodes.PaymentMethodDomain, "CASH"), Amount = paid, PaidAtUtc = DateTime.UtcNow });
            await x.F.Db.SaveChangesAsync();
        }
        x.F.Db.ChangeTracker.Clear();
        return inv;
    }

    [Fact]
    public async Task The_cod_source_returns_one_row_per_collection_with_method_status_order_and_client()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        var a = await CodOrderAsync(x, "O-1", 100m, "FAC-1");
        var b = await CodOrderAsync(x, "O-2", 50m, "FAC-2");
        await x.Cod.CollectAsync(a.PublicId, new CodCollectionRequest("CASH", 100m), default);
        await x.Cod.CollectAsync(b.PublicId, new CodCollectionRequest("CHECK", 50m, CheckNumber: "777"), default);

        var source = new CodCollectionDataSource(x.F.Db, x.F.Lookups, x.F.Tenant);
        Assert.Equal((FinanceAnalyticsRules.CodSource, "CollectedAtUtc", ModuleKeys.Cod), (source.Key, source.DateField, source.TenantModule));
        Assert.Equal(PermissionCatalog.CodView, PermissionCatalog.DataSourceReadPermission(source.Key));
        var rows = await source.LoadAsync(new DataQuery(), default);
        Assert.Equal(2, rows.Count);
        var cash = rows.Single(r => (string?)r["MethodCode"] == "CASH");
        Assert.Equal((100m, "COLLECTED", "O-1", "FAC-1", "Cliente C1"), ((decimal)cash["Amount"]!, (string?)cash["StatusCode"], (string?)cash["OrderNumber"], (string?)cash["InvoiceNumber"], (string?)cash["ClientName"]));
        Assert.Equal("777", (string?)rows.Single(r => (string?)r["MethodCode"] == "CHECK")["CheckNumber"]);

        // rango: ayer no tiene nada; hoy tiene los dos
        var from = DateTime.UtcNow.AddDays(-2);
        Assert.Empty(await source.LoadAsync(new DataQuery { FromUtc = from, ToUtc = DateTime.UtcNow.AddDays(-1) }, default));
        Assert.Equal(2, (await source.LoadAsync(new DataQuery { FromUtc = from, ToUtc = DateTime.UtcNow.AddMinutes(5) }, default)).Count);
        Assert.Single(await source.LoadAsync(new DataQuery { Ids = new[] { (int)(long)cash["Id"]! } }, default));
        using (x.F.AsTenant(WmsFixture.OtherTenantId))
            Assert.Empty(await source.LoadAsync(new DataQuery(), default));
    }

    [Fact]
    public async Task The_invoice_source_computes_open_balance_days_overdue_and_respects_the_issue_date_range()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        var today = TenantClock.Default.Today;
        await InvoiceAsync(x, "FAC-1", InvoiceStatuses.Issued, 100m, today.AddDays(-5), today.AddDays(10), paid: 30m);      // total 110, pagó 30 → saldo 80
        await InvoiceAsync(x, "FAC-2", InvoiceStatuses.Overdue, 200m, today.AddDays(-40), today.AddDays(-10));              // vencida hace 10 días, saldo 220
        await InvoiceAsync(x, "FAC-3", InvoiceStatuses.Paid, 50m, today.AddDays(-3), today.AddDays(27), paid: 55m);         // pagada: sin saldo
        await InvoiceAsync(x, "FAC-4", InvoiceStatuses.Draft, 10m, null, null);                                              // borrador: sin fecha

        var source = new InvoiceDataSource(x.F.Db, x.F.Tenant, TenantClock.Default);
        Assert.Equal((FinanceAnalyticsRules.InvoiceSource, "IssueDate", ModuleKeys.Billing), (source.Key, source.DateField, source.TenantModule));
        Assert.Equal(PermissionCatalog.BillingView, PermissionCatalog.DataSourceReadPermission(source.Key));
        var rows = (await source.LoadAsync(new DataQuery(), default)).ToDictionary(r => (string)r["Number"]!);
        Assert.Equal(4, rows.Count);
        Assert.Equal((true, 80m, 0), ((bool)rows["FAC-1"]["IsOpen"]!, (decimal)rows["FAC-1"]["Balance"]!, (int)rows["FAC-1"]["DaysOverdue"]!));
        Assert.Equal((true, 220m, 10), ((bool)rows["FAC-2"]["IsOpen"]!, (decimal)rows["FAC-2"]["Balance"]!, (int)rows["FAC-2"]["DaysOverdue"]!));
        Assert.Equal((false, 0m, 55m), ((bool)rows["FAC-3"]["IsOpen"]!, (decimal)rows["FAC-3"]["Balance"]!, (decimal)rows["FAC-3"]["Paid"]!));
        Assert.Equal((55m, "Cliente C1"), ((decimal)rows["FAC-3"]["Total"]!, (string?)rows["FAC-3"]["ClientName"]));

        // rango por fecha de emisión (desde inclusivo, hasta exclusivo): solo FAC-1 y FAC-3; el borrador sin fecha queda fuera
        var ranged = await source.LoadAsync(new DataQuery { FromDay = today.AddDays(-7), ToDayExclusive = today.AddDays(1) }, default);
        Assert.Equal(new[] { "FAC-1", "FAC-3" }, ranged.Select(r => (string)r["Number"]!).OrderBy(n => n).ToArray());
        using (x.F.AsTenant(WmsFixture.OtherTenantId))
            Assert.Empty(await source.LoadAsync(new DataQuery(), default));
    }

    [Fact]
    public void Factory_filters_and_fields_only_use_fields_the_sources_expose()
    {
        var cod = new CodCollectionDataSource(null!, null!, null!).Fields.Select(f => f.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var invoice = new InvoiceDataSource(null!, null!).Fields.Select(f => f.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var sales = new SalesOrderDataSource(null!, null!, null!).Fields.Select(f => f.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        static IEnumerable<string> FieldsOf(string json) => Regex.Matches(json, "\"field\":\"(\\w+)\"").Select(m => m.Groups[1].Value);
        foreach (var f in new[] { FinanceAnalyticsRules.CodLiveFilter, FinanceAnalyticsRules.CodUnreconciledFilter, FinanceAnalyticsRules.CodUnremittedFilter }.SelectMany(FieldsOf).Concat(new[] { "Amount", "Method", "CollectedAtUtc" }))
            Assert.Contains(f, cod);
        foreach (var f in new[] { FinanceAnalyticsRules.InvoiceOpenFilter, FinanceAnalyticsRules.InvoiceOverdueFilter, FinanceAnalyticsRules.InvoiceIssuedFilter }.SelectMany(FieldsOf).Concat(new[] { "Balance", "Total", "Status", "IssueDate" }))
            Assert.Contains(f, invoice);
        foreach (var f in FieldsOf(FinanceAnalyticsRules.SalesConfirmedFilter).Concat(new[] { "Total", "CreatedAtUtc" }))
            Assert.Contains(f, sales);
    }

    // ---------------------------------------------------------------- Actividad reciente

    private static async Task SeedCatalogAsync(WmsFixture f)
    {
        var existing = await f.Db.LookupCodes.AsNoTracking().ToListAsync();
        var id = 7000;
        string Module(string code) => ActivityEvents.Operations.Contains(code) ? "OPERATIONS" : "ACCOUNTING";
        var rows = ActivityEvents.Operations.Concat(ActivityEvents.Billing).Concat(ActivityEvents.Cod).Select(c => new LookupCode
        {
            LookupCodeId = id++, Entity = ActivityRules.CatalogDomain, InternalCode = c, IsActive = true, IsSystem = true,
            LabelJson = $"{{\"es\":\"es {c}\",\"en\":\"en {c}\"}}", ExtraJson = $"{{\"module\":\"{Module(c)}\",\"mandatory\":false,\"defaultOn\":true}}",
        }).ToList();
        f.Lookups.Load(existing.Concat(rows));
        f.Db.Users.Add(new ApplicationUser { Id = f.Tenant.UserId!.Value, UserName = "ana", Email = "ana@teikem.test", FullName = "Ana" });
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
    }

    private static void History(WmsFixture f, string entityType, int entityId, string domain, string toStatus, DateTime atUtc)
        => f.Db.EntityStatusHistories.Add(new EntityStatusHistory
        {
            TenantId = WmsFixture.TenantId, EntityTypeLookupId = f.LookupId(LookupDomains.EntityType, entityType), EntityId = entityId,
            ToStatusCodeId = f.StatusId(domain, toStatus), ChangedAtUtc = atUtc, ChangedBy = f.Tenant.UserId,
        });

    private static ActivityScope Scope(WmsFixture f) => new(WmsFixture.TenantId, f.Tenant.UserId, "es", new HashSet<string> { ModuleKeys.LtlGround, ModuleKeys.Cod, ModuleKeys.Billing });

    [Fact]
    public async Task The_operations_provider_reads_orders_and_trips_from_the_status_history()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        await SeedCatalogAsync(x.F);
        var delivered = await x.F.AddOrderAsync(x.Client.ClientId, "O-1", OrderStatuses.Delivered);
        var old = await x.F.AddOrderAsync(x.Client.ClientId, "O-OLD", OrderStatuses.Delivered);
        var trip = new Trip { PublicId = Guid.NewGuid(), TenantId = WmsFixture.TenantId, Code = "RUT-1", PlanDate = TenantClock.Default.Today, StatusCodeId = x.F.StatusId(StatusDomains.TripStatus, TripStatuses.Dispatched), IsActive = true };
        x.F.Db.Set<Trip>().Add(trip);
        await x.F.Db.SaveChangesAsync();
        History(x.F, EntityTypes.TransportOrder, delivered.TransportOrderId, StatusDomains.OrderStatus, OrderStatuses.Delivered, DateTime.UtcNow.AddMinutes(-5));
        History(x.F, EntityTypes.TransportOrder, old.TransportOrderId, StatusDomains.OrderStatus, OrderStatuses.Delivered, DateTime.UtcNow.AddHours(-40));
        History(x.F, EntityTypes.TransportOrder, delivered.TransportOrderId, StatusDomains.OrderStatus, OrderStatuses.Failed, DateTime.UtcNow.AddMinutes(-3));
        History(x.F, EntityTypes.Trip, trip.TripId, StatusDomains.TripStatus, TripStatuses.Dispatched, DateTime.UtcNow.AddMinutes(-1));
        await x.F.Db.SaveChangesAsync();
        x.F.Db.ChangeTracker.Clear();

        var provider = new OperationsActivityProvider(x.F.Db, x.F.Lookups);
        Assert.Equal((BusinessModules.Operations, PermissionCatalog.OrdersView, ModuleKeys.LtlGround), (provider.Module, provider.RequiredPermission, provider.TenantModule));
        var events = await provider.ReadAsync(Scope(x.F), DateTime.UtcNow.AddHours(-24), false, default);
        Assert.Equal(new[] { ActivityEvents.OrderDelivered, ActivityEvents.OrderFailed, ActivityEvents.TripDispatched }.OrderBy(c => c).ToArray(), events.Select(e => e.Code).OrderBy(c => c).ToArray());
        Assert.Equal(3, events.Count);   // la entrega de hace 40 horas queda fuera de la ventana
        var d = events.Single(e => e.Code == ActivityEvents.OrderDelivered);
        Assert.Equal(("O-1", "Cliente C1", "es " + ActivityEvents.OrderDelivered, BusinessModules.Operations), (d.Reference, d.Detail, d.Label, d.Module));
        Assert.Equal(delivered.PublicId, d.PublicId);
        Assert.Equal("RUT-1", events.Single(e => e.Code == ActivityEvents.TripDispatched).Reference);
        Assert.Empty(await provider.ReadAsync(Scope(x.F), DateTime.UtcNow.AddHours(-24), true, default));   // «solo obligatorios»: el catálogo de la prueba no marca ninguno
    }

    [Fact]
    public async Task The_billing_provider_reads_invoice_history_and_payments()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        await SeedCatalogAsync(x.F);
        var today = TenantClock.Default.Today;
        var inv = await InvoiceAsync(x, "FAC-9", InvoiceStatuses.Issued, 100m, today, today.AddDays(30));
        x.F.Db.Set<Payment>().AddRange(
            new Payment { TenantId = WmsFixture.TenantId, InvoiceId = inv.InvoiceId, PaymentMethodLookupId = x.F.LookupId(BillingCodes.PaymentMethodDomain, "CASH"), Amount = 40m, PaidAtUtc = DateTime.UtcNow.AddMinutes(-20) },
            new Payment { TenantId = WmsFixture.TenantId, InvoiceId = inv.InvoiceId, PaymentMethodLookupId = x.F.LookupId(BillingCodes.PaymentMethodDomain, "CASH"), Amount = 10m, PaidAtUtc = DateTime.UtcNow.AddHours(-30), VoidedAtUtc = DateTime.UtcNow.AddMinutes(-10), VoidReason = "error" });
        History(x.F, BillingCodes.InvoiceEntityType, inv.InvoiceId, BillingCodes.InvoiceStatusDomain, InvoiceStatuses.Issued, DateTime.UtcNow.AddMinutes(-30));
        await x.F.Db.SaveChangesAsync();
        x.F.Db.ChangeTracker.Clear();

        var provider = new BillingActivityProvider(x.F.Db, x.F.Lookups);
        Assert.Equal((BusinessModules.Accounting, PermissionCatalog.BillingView, ModuleKeys.Billing), (provider.Module, provider.RequiredPermission, provider.TenantModule));
        var events = await provider.ReadAsync(Scope(x.F), DateTime.UtcNow.AddHours(-24), false, default);
        Assert.Equal(new[] { ActivityEvents.InvoiceIssued, ActivityEvents.PaymentRecorded, ActivityEvents.PaymentVoided }, events.Select(e => e.Code).OrderBy(c => c == ActivityEvents.InvoiceIssued ? 0 : c == ActivityEvents.PaymentRecorded ? 1 : 2).ToArray());   // el pago anulado se registró hace 30 horas: solo cuenta su anulación
        Assert.All(events, e => Assert.Equal("FAC-9", e.Reference));
        Assert.Contains("Cliente C1", events.First().Detail);
        Assert.Equal(inv.PublicId, events.First().PublicId);
    }

    [Fact]
    public async Task The_cod_provider_reads_collections_reconciliations_and_remittances()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        await SeedCatalogAsync(x.F);
        var o = await CodOrderAsync(x, "O-7", 80m, "FAC-7");
        await x.Cod.CollectAsync(o.PublicId, new CodCollectionRequest("CASH", 80m), default);
        var col = await x.F.Db.Set<CodCollection>().AsTracking().SingleAsync();
        col.ReconciledAtUtc = DateTime.UtcNow.AddMinutes(-2);
        var batch = new CodRemittanceBatch
        {
            PublicId = Guid.NewGuid(), TenantId = WmsFixture.TenantId, ClientId = x.Client.ClientId, Number = "REM-00001", PeriodStart = TenantClock.Default.Today, PeriodEnd = TenantClock.Default.Today,
            StatusCodeId = x.F.StatusId(RemittanceCodes.StatusDomain, RemittanceStatuses.Remitted), GrossCollected = 80m, CurrencyLookupId = 1,
        };
        x.F.Db.Set<CodRemittanceBatch>().Add(batch);
        await x.F.Db.SaveChangesAsync();
        History(x.F, RemittanceCodes.EntityType, batch.CodRemittanceBatchId, RemittanceCodes.StatusDomain, RemittanceStatuses.Remitted, DateTime.UtcNow.AddMinutes(-1));
        await x.F.Db.SaveChangesAsync();
        x.F.Db.ChangeTracker.Clear();

        var provider = new CodActivityProvider(x.F.Db, x.F.Lookups);
        Assert.Equal((BusinessModules.Accounting, PermissionCatalog.CodView, ModuleKeys.Cod), (provider.Module, provider.RequiredPermission, provider.TenantModule));
        var events = await provider.ReadAsync(Scope(x.F), DateTime.UtcNow.AddHours(-24), false, default);
        Assert.Equal(new[] { ActivityEvents.CodCollected, ActivityEvents.CodReconciled, ActivityEvents.CodRemitted }, events.Select(e => e.Code).OrderBy(c => c).ToArray());
        Assert.Equal("O-7", events.Single(e => e.Code == ActivityEvents.CodCollected).Reference);
        var r = events.Single(e => e.Code == ActivityEvents.CodRemitted);
        Assert.Equal(("REM-00001", batch.PublicId), (r.Reference, r.PublicId));
    }

    [Fact]
    public void Every_new_activity_event_is_in_the_update_script_with_its_module()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "Teikem.sln"))) dir = Path.GetDirectoryName(dir);
        Assert.NotNull(dir);
        var sql = File.ReadAllText(Path.Combine(dir!, "Diseño", "logistica-db-update.sql"));
        var start = sql.IndexOf("(ab) Lote 91", StringComparison.Ordinal);
        Assert.True(start > 0);
        var section = sql[start..];
        foreach (var code in ActivityEvents.Operations) Assert.Matches($@"\(N'{code}', N'[^']+', N'[^']+', N'OPERATIONS',", section);
        foreach (var code in ActivityEvents.Billing.Concat(ActivityEvents.Cod)) Assert.Matches($@"\(N'{code}', N'[^']+', N'[^']+', N'ACCOUNTING',", section);
    }
}
