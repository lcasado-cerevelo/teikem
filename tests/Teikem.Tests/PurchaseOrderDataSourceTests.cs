using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Constants;
using Teikem.Domain.Sales;
using Teikem.Domain.Wms;
using Teikem.Infrastructure;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Analytics;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 246 (Lote F del plan de Solutions, P1) — fuente de datos PURCHASE_ORDER (InMemory, WmsFixture): metadatos, filas con origen, estado en QuickBooks (la MISMA función
/// del DTO), totales y unidades, atraso con el día de la compañía, respeto de q.Ids y del rango por día, y sin inactivas ni órdenes de otra compañía. Registro en el contenedor.
/// </summary>
public sealed class PurchaseOrderDataSourceTests
{
    /// <summary>2026-11-25 03:00 UTC = 2026-11-24 23:00 en Puerto Rico: el «hoy» de la compañía es el 24, no el 25.</summary>
    private static readonly ITenantClock Clock = new TenantClock(TenantClock.Default.Zone, () => new DateTime(2026, 11, 25, 3, 0, 0, DateTimeKind.Utc));

    private sealed class World(WmsFixture f, Supplier supplier, Warehouse warehouse) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => F.DisposeAsync();
        public WmsFixture F { get; } = f;
        public Supplier Supplier { get; } = supplier;
        public Warehouse Warehouse { get; } = warehouse;
        public PurchaseOrderDataSource Source => new(F.Db, F.Tenant, Clock);

        public async Task<PurchaseOrder> OrderAsync(string number, string status, string? origin = null, DateOnly? orderDate = null, DateOnly? expected = null,
            DateTime? exportedAt = null, DateTime? quickBooksAt = null, bool isActive = true, int? tenantId = null, params (decimal Ordered, decimal Received, decimal Cost)[] lines)
        {
            var po = new PurchaseOrder
            {
                PublicId = Guid.NewGuid(), TenantId = tenantId ?? WmsFixture.TenantId, SupplierId = Supplier.SupplierId, WarehouseId = Warehouse.WarehouseId, Number = number,
                OrderDate = orderDate ?? new DateOnly(2026, 11, 10), ExpectedDate = expected, StatusCodeId = F.StatusId(StatusDomains.PurchaseOrderStatus, status),
                OriginLookupId = origin is null ? null : F.LookupId(PurchaseOrderOrigins.Domain, origin), QuickBooksExportedAt = exportedAt, QuickBooksExportedBy = exportedAt is null ? null : 1,
                QuickBooksAt = quickBooksAt, QuickBooksBy = quickBooksAt is null ? null : 1, IsActive = isActive, CreatedAtUtc = DateTime.UtcNow,
            };
            var product = 1;
            foreach (var (ordered, received, cost) in lines)
                po.Lines.Add(new PurchaseOrderLine { ProductId = product++, QtyOrdered = ordered, QtyReceived = received, UnitCost = cost });
            F.Db.PurchaseOrders.Add(po);
            await F.Db.SaveChangesAsync();
            return po;
        }
    }

    private static async Task<World> WorldAsync()
    {
        var f = await WmsFixture.CreateAsync();
        var w = await f.AddWarehouseAsync("W1");
        var s = new Supplier { TenantId = WmsFixture.TenantId, Name = "Acme" };
        f.Db.Suppliers.Add(s);
        await f.Db.SaveChangesAsync();
        return new World(f, s, w);
    }

    private static Dictionary<string, DataRow> ByNumber(IEnumerable<DataRow> rows) => rows.ToDictionary(r => (string)r["Number"]!);

    [Fact]
    public void The_source_metadata()
    {
        var source = new PurchaseOrderDataSource(null!, new TenantContext());
        Assert.Equal("PURCHASE_ORDER", source.Key);
        Assert.Equal(EntityTypes.PurchaseOrder, source.Key);
        Assert.Equal(("Órdenes de compra", "Purchase orders"), (source.LabelEs, source.LabelEn));
        Assert.Equal("OrderDate", source.DateField);
        Assert.Equal("Id", source.IdField);
        Assert.Equal(ModuleKeys.Purchasing, source.TenantModule);
        Assert.Equal(BusinessModules.Warehouse, source.DefaultBusinessModule);
        Assert.Null(source.EntityTypeCode);
        Assert.Equal(PermissionCatalog.PurchasingView, PermissionCatalog.DataSourceReadPermission(source.Key));
        Assert.Equal(new[]
        {
            "Id", "PublicId", "Number", "SupplierId", "SupplierName", "WarehouseId", "WarehouseCode", "Status", "StatusCode", "IsOpen", "IsLate", "OriginCode", "Origin",
            "QuickBooksState", "IsInQuickBooks", "OrderDate", "ExpectedDate", "LineCount", "UnitsOrdered", "UnitsReceived", "Total", "QuickBooksExportedAtUtc", "QuickBooksAtUtc", "CreatedAtUtc",
        }, source.Fields.Select(x => x.Key).ToArray());
        Assert.True(source.Fields.Single(x => x.Key == "Total").IsMoney);
        var rel = Assert.Single(source.Relations);
        Assert.Equal(("Warehouse", EntityTypes.Warehouse, "WarehouseId"), (rel.Key, rel.TargetSourceKey, rel.LocalField));
    }

    [Fact]
    public async Task Rows_carry_origin_quickbooks_state_totals_and_lateness_with_the_company_day()
    {
        await using var x = await WorldAsync();
        var exported = new DateTime(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc);
        var marked = new DateTime(2026, 11, 21, 12, 0, 0, DateTimeKind.Utc);
        await x.OrderAsync("PO-00001", PurchaseOrderStatuses.Sent, null, expected: new DateOnly(2026, 11, 23), lines: new[] { (3m, 0m, 0.3333m), (2m, 1m, 10m) });
        await x.OrderAsync("PO-00002", PurchaseOrderStatuses.Partial, PurchaseOrderOrigins.Excel, expected: new DateOnly(2026, 11, 24), exportedAt: exported);
        await x.OrderAsync("PO-00003", PurchaseOrderStatuses.Received, PurchaseOrderOrigins.Teikem, expected: new DateOnly(2026, 11, 1), exportedAt: exported, quickBooksAt: marked);
        await x.OrderAsync("PO-00004", PurchaseOrderStatuses.Sent, PurchaseOrderOrigins.QuickBooks);
        await x.OrderAsync("PO-00005", PurchaseOrderStatuses.Draft, PurchaseOrderOrigins.Teikem, expected: new DateOnly(2026, 11, 1));
        await x.OrderAsync("PO-00006", PurchaseOrderStatuses.Cancelled, PurchaseOrderOrigins.Excel, exportedAt: exported);

        var rows = ByNumber(await x.Source.LoadAsync(new DataQuery(), default));
        Assert.Equal(6, rows.Count);

        var r1 = rows["PO-00001"];
        Assert.Equal((PurchaseOrderOrigins.Teikem, "TEIKEM"), ((string)r1["OriginCode"]!, (string)r1["Origin"]!));   // NULL = TEIKEM, con la etiqueta del catálogo
        Assert.Equal(QuickBooksSyncStates.Pending, r1["QuickBooksState"]);
        Assert.Equal((true, true, false), ((bool)r1["IsOpen"]!, (bool)r1["IsLate"]!, (bool)r1["IsInQuickBooks"]!));   // vencida ayer (día 23)
        Assert.Equal((2, 5m, 1m), ((int)r1["LineCount"]!, (decimal)r1["UnitsOrdered"]!, (decimal)r1["UnitsReceived"]!));
        Assert.Equal(3m * 0.3333m + 2m * 10m, (decimal)r1["Total"]!);
        Assert.Equal(("Acme", "W1", PurchaseOrderStatuses.Sent), ((string)r1["SupplierName"]!, (string)r1["WarehouseCode"]!, (string)r1["StatusCode"]!));

        var r2 = rows["PO-00002"];
        Assert.Equal(("EXCEL", QuickBooksSyncStates.Exported), ((string)r2["OriginCode"]!, (string)r2["QuickBooksState"]!));
        Assert.False((bool)r2["IsLate"]!);   // vence HOY en Puerto Rico (aunque en UTC ya sea el 25)
        Assert.Equal(exported, r2["QuickBooksExportedAtUtc"]);
        Assert.Equal((0, 0m), ((int)r2["LineCount"]!, (decimal)r2["Total"]!));

        var r3 = rows["PO-00003"];
        Assert.Equal((QuickBooksSyncStates.InQuickBooks, true, false), ((string)r3["QuickBooksState"]!, (bool)r3["IsInQuickBooks"]!, (bool)r3["IsOpen"]!));
        Assert.False((bool)r3["IsLate"]!);   // recibida: no está abierta
        Assert.Equal(marked, r3["QuickBooksAtUtc"]);

        Assert.Equal(QuickBooksSyncStates.FromQuickBooks, rows["PO-00004"]["QuickBooksState"]);
        Assert.Equal(QuickBooksSyncStates.NotExportable, rows["PO-00005"]["QuickBooksState"]);
        Assert.False((bool)rows["PO-00005"]["IsLate"]!);   // borrador: no está abierta
        Assert.Equal(QuickBooksSyncStates.Exported, rows["PO-00006"]["QuickBooksState"]);   // exportada y luego cancelada: sigue por confirmar

        // la MISMA función que el DTO
        foreach (var row in rows.Values)
            Assert.Equal(PurchaseOrderQuickBooksRules.State((string)row["OriginCode"]!, (string)row["StatusCode"]!, (DateTime?)row["QuickBooksExportedAtUtc"], (DateTime?)row["QuickBooksAtUtc"]),
                row["QuickBooksState"]);
    }

    [Fact]
    public async Task Respects_ids_and_the_day_range()
    {
        await using var x = await WorldAsync();
        var a = await x.OrderAsync("PO-00001", PurchaseOrderStatuses.Sent, orderDate: new DateOnly(2026, 11, 1));
        var b = await x.OrderAsync("PO-00002", PurchaseOrderStatuses.Sent, orderDate: new DateOnly(2026, 11, 15));
        await x.OrderAsync("PO-00003", PurchaseOrderStatuses.Sent, orderDate: new DateOnly(2026, 11, 30));

        var byIds = await x.Source.LoadAsync(new DataQuery { Ids = new[] { a.PurchaseOrderId, b.PurchaseOrderId } }, default);
        Assert.Equal(new[] { "PO-00001", "PO-00002" }, byIds.Select(r => (string)r["Number"]!).OrderBy(n => n));

        var ranged = await x.Source.LoadAsync(new DataQuery { FromDay = new DateOnly(2026, 11, 15), ToDayExclusive = new DateOnly(2026, 11, 30) }, default);
        Assert.Equal("PO-00002", (string)Assert.Single(ranged)["Number"]!);
        var fromUtc = await x.Source.LoadAsync(new DataQuery { FromUtc = new DateTime(2026, 11, 2, 0, 0, 0, DateTimeKind.Utc) }, default);
        Assert.Equal(new[] { "PO-00002", "PO-00003" }, fromUtc.Select(r => (string)r["Number"]!).OrderBy(n => n));
    }

    [Fact]
    public async Task Inactive_orders_and_other_companies_are_left_out()
    {
        await using var x = await WorldAsync();
        await x.OrderAsync("PO-00001", PurchaseOrderStatuses.Sent);
        await x.OrderAsync("PO-00002", PurchaseOrderStatuses.Sent, isActive: false);
        using (x.F.AsTenant(WmsFixture.OtherTenantId))
            await x.OrderAsync("PO-00003", PurchaseOrderStatuses.Sent, tenantId: WmsFixture.OtherTenantId);

        var rows = await x.Source.LoadAsync(new DataQuery(), default);
        Assert.Equal("PO-00001", (string)Assert.Single(rows)["Number"]!);
        Assert.Empty(await new PurchaseOrderDataSource(x.F.Db, x.F.Tenant, Clock).LoadAsync(new DataQuery { Ids = new[] { -1 } }, default));
    }

    [Fact]
    public void The_source_is_registered_in_the_container()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Teikem"] = "Server=model-only.invalid;Database=Teikem;User Id=x;Password=x;TrustServerCertificate=True",
            ["Jwt:SigningKey"] = new string('k', 64),
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(config);
        services.AddTeikemInfrastructure(config);
        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();
        var registry = scope.ServiceProvider.GetRequiredService<IDataSourceRegistry>();
        Assert.True(registry.TryGet(EntityTypes.PurchaseOrder, out var source));
        Assert.IsType<PurchaseOrderDataSource>(source);
        Assert.Equal(registry.All.Count, scope.ServiceProvider.GetServices<IDataSource>().Count());   // claves únicas
    }
}
