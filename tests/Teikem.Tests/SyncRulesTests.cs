using System.Reflection;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Api.Auth;
using Teikem.Api.Controllers;
using Teikem.Domain.Audit;
using Teikem.Domain.Constants;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Orders;
using Teikem.Infrastructure.Services;
using Teikem.Infrastructure.Wms;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 8A (P2) — sincronización por diferencia y operaciones de la cola del aparato: reglas puras de SyncRules (tope de
/// página, cursor opaco, marca de agua), seguridad del SyncController por reflexión, SyncService sobre InMemory (carga
/// completa paginada, diferencia por bitácora con bajas, aislamiento de tenant), búsqueda por código escaneado, conteo a
/// ciegas, recibo en una llamada y validaciones previas de recolectar + empacar.
/// </summary>
public sealed class SyncRulesTests
{
    // ================================================================ SyncRules (puras)

    [Theory]
    [InlineData(null, 500)]
    [InlineData(0, 500)]
    [InlineData(-3, 500)]
    [InlineData(1, 1)]
    [InlineData(500, 500)]
    public void Take_defaults_to_500_and_accepts_up_to_500(int? take, int expected)
    {
        var (resolved, error) = SyncRules.ResolveTake(take);
        Assert.Null(error);
        Assert.Equal(expected, resolved);
    }

    [Theory]
    [InlineData(501)]
    [InlineData(10_000)]
    public void Take_above_500_is_rejected_with_the_exact_message(int take)
    {
        var (_, error) = SyncRules.ResolveTake(take);
        Assert.Equal("El máximo por página es 500.", error);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(123456)]
    [InlineData(int.MaxValue)]
    public void Cursor_round_trips_and_is_opaque(int id)
    {
        var cursor = SyncRules.EncodeCursor(id);
        Assert.DoesNotContain(id.ToString(System.Globalization.CultureInfo.InvariantCulture), cursor);
        Assert.DoesNotContain("=", cursor);
        Assert.DoesNotContain("/", cursor);
        Assert.DoesNotContain("+", cursor);
        var (decoded, error) = SyncRules.DecodeCursor(cursor);
        Assert.Null(error);
        Assert.Equal(id, decoded);
    }

    [Fact]
    public void Empty_cursor_is_the_first_page()
    {
        Assert.Equal((null, null), SyncRules.DecodeCursor(null));
        Assert.Equal((null, null), SyncRules.DecodeCursor("  "));
    }

    public static IEnumerable<object[]> BadCursors() => new[]
    {
        new object[] { "123" },
        new object[] { "!!!" },
        new object[] { B64("x:1") },
        new object[] { B64("c1:") },
        new object[] { B64("c1:-1") },
        new object[] { B64("c1:1a") },
        new object[] { B64("c1:99999999999") },
        new object[] { new string('A', 80) },
    };

    [Theory]
    [MemberData(nameof(BadCursors))]
    public void Tampered_cursor_is_rejected(string cursor)
    {
        var (id, error) = SyncRules.DecodeCursor(cursor);
        Assert.Null(id);
        Assert.Equal("El cursor no es válido.", error);
    }

    [Fact]
    public void Next_since_is_server_time_minus_five_minutes()
    {
        var server = new DateTime(2026, 9, 27, 12, 0, 0, DateTimeKind.Utc);
        Assert.Equal(new DateTime(2026, 9, 27, 11, 55, 0, DateTimeKind.Utc), SyncRules.NextSince(server));
        Assert.Equal(TimeSpan.FromMinutes(5), SyncRules.SafetyWindow);
    }

    [Fact]
    public void Since_is_normalized_to_utc()
    {
        Assert.Null(SyncRules.NormalizeSince(null));
        var utc = new DateTime(2026, 9, 27, 12, 0, 0, DateTimeKind.Utc);
        Assert.Equal(utc, SyncRules.NormalizeSince(utc));
        var unspecified = SyncRules.NormalizeSince(new DateTime(2026, 9, 27, 12, 0, 0, DateTimeKind.Unspecified))!.Value;
        Assert.Equal(DateTimeKind.Utc, unspecified.Kind);
        Assert.Equal(utc, unspecified);
        var local = utc.ToLocalTime();
        Assert.Equal(utc, SyncRules.NormalizeSince(local));
    }

    [Fact]
    public void Cut_returns_a_cursor_only_when_there_is_a_next_page()
    {
        var (items, next) = SyncRules.Cut(new[] { 1, 2, 3 }, 3, x => x);
        Assert.Equal(new[] { 1, 2, 3 }, items);
        Assert.Null(next);

        (items, next) = SyncRules.Cut(new[] { 4, 7, 9 }, 2, x => x);
        Assert.Equal(new[] { 4, 7 }, items);
        Assert.Equal(7, SyncRules.DecodeCursor(next).LastId);
    }

    private static string B64(string s) => Convert.ToBase64String(Encoding.UTF8.GetBytes(s)).TrimEnd('=');

    // ================================================================ SyncController (reflexión)

    [Fact]
    public void Sync_controller_requires_wms_module_and_the_resource_permission_on_every_action()
    {
        var modules = typeof(SyncController).GetCustomAttributes<RequireModuleAttribute>(inherit: true).Select(a => a.ModuleKey).ToList();
        Assert.Equal(new[] { ModuleKeys.WmsLotSerial }, modules);
        Assert.Equal("api/v1/sync", typeof(SyncController).GetCustomAttribute<RouteAttribute>()!.Template);

        var actions = typeof(SyncController).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttributes<HttpMethodAttribute>().Any()).ToList();
        var routes = actions.Select(a => a.GetCustomAttribute<HttpGetAttribute>()!.Template).OrderBy(x => x).ToArray();
        Assert.Equal(new[] { "asns", "bins", "product-categories", "products", "purchase-orders", "warehouse-tasks" }, routes);
        foreach (var action in actions)
        {
            var policies = action.GetCustomAttributes<RequirePermissionAttribute>().Select(a => a.Policy).ToList();
            var actionModules = action.GetCustomAttributes<RequireModuleAttribute>().Select(a => a.ModuleKey).ToList();
            // Las órdenes de compra conservan la defensa del recurso nativo: módulo PURCHASING y purchasing.view.
            var isPurchaseOrders = action.GetCustomAttribute<HttpGetAttribute>()!.Template == "purchase-orders";
            Assert.Equal(new[] { RequirePermissionAttribute.Prefix + (isPurchaseOrders ? PermissionCatalog.PurchasingView : PermissionCatalog.InventoryView) }, policies);
            Assert.Equal(isPurchaseOrders ? new[] { ModuleKeys.Purchasing } : Array.Empty<string>(), actionModules);
            Assert.DoesNotContain(action.GetParameters(), p => string.Equals(p.Name, "tenantId", StringComparison.OrdinalIgnoreCase));
        }
    }

    // ================================================================ SyncService (InMemory)

    private static Task<WmsFixture> SyncFixtureAsync()
        => WmsFixture.CreateAsync(s => { s.AddSingleton<SyncService>(); s.AddSingleton<ProductService>(); });

    [Fact]
    public async Task Full_load_pages_active_products_of_the_tenant_by_cursor()
    {
        await using var f = await SyncFixtureAsync();
        var a = await f.AddProductAsync("SYN-A");
        var b = await f.AddProductAsync("SYN-B");
        await f.AddProductAsync("SYN-OFF", isActive: false);
        await f.AddProductAsync("SYN-OTHER", tenantId: WmsFixture.OtherTenantId);
        var c = await f.AddProductAsync("SYN-C");
        var sync = f.Get<SyncService>();

        var first = await sync.ProductsAsync(new SyncQuery(Take: 2), default);
        Assert.Equal(new[] { a.ProductId, b.ProductId }, first.Items.Select(i => i.Id));
        Assert.NotNull(first.NextCursor);
        Assert.Equal(DateTimeKind.Utc, first.ServerTimeUtc.Kind);

        var second = await sync.ProductsAsync(new SyncQuery(Cursor: first.NextCursor, Take: 2), default);
        var last = Assert.Single(second.Items);
        Assert.Equal(c.ProductId, last.Id);
        Assert.Equal(c.PublicId, last.PublicId);
        Assert.Equal(TrackingTypes.None, last.TrackingTypeCode);
        Assert.Equal("UN", last.BaseUomCode);
        Assert.True(last.IsActive);
        Assert.Null(second.NextCursor);
    }

    [Fact]
    public async Task Difference_returns_changed_rows_including_deactivated_ones()
    {
        await using var f = await SyncFixtureAsync();
        var old = await f.AddProductAsync("DIF-OLD");
        var gone = await f.AddProductAsync("DIF-GONE", isActive: false);
        await f.AddProductAsync("DIF-QUIET");
        var now = DateTime.UtcNow;
        var productType = f.LookupId(LookupDomains.EntityType, EntityTypes.Product);
        f.Db.AuditLogs.AddRange(
            new AuditLog { TenantId = WmsFixture.TenantId, EntityTypeLookupId = productType, EntityId = old.ProductId, CreatedAtUtc = now.AddHours(-2) },
            new AuditLog { TenantId = WmsFixture.TenantId, EntityTypeLookupId = productType, EntityId = gone.ProductId, CreatedAtUtc = now });
        await f.Db.SaveChangesAsync();

        var page = await f.Get<SyncService>().ProductsAsync(new SyncQuery(Since: now.AddMinutes(-5)), default);

        var item = Assert.Single(page.Items);
        Assert.Equal(gone.ProductId, item.Id);
        Assert.False(item.IsActive);   // el aparato lo borra
        Assert.Null(page.NextCursor);
    }

    [Fact]
    public async Task Invalid_take_or_cursor_is_400_before_reading()
    {
        await using var f = await SyncFixtureAsync();
        var sync = f.Get<SyncService>();
        var take = await Assert.ThrowsAsync<ValidationException>(() => sync.ProductsAsync(new SyncQuery(Take: 501), default));
        Assert.Equal(SyncRules.TakeTooLarge, take.Message);
        var cursor = await Assert.ThrowsAsync<ValidationException>(() => sync.BinsAsync(new SyncQuery(Cursor: "xyz"), default));
        Assert.Equal(SyncRules.InvalidCursor, cursor.Message);
    }

    [Fact]
    public async Task Bins_come_with_their_zone_and_only_from_the_requested_warehouse()
    {
        await using var f = await SyncFixtureAsync();
        var w1 = await f.AddWarehouseAsync("W1");
        var w2 = await f.AddWarehouseAsync("W2");
        var z1 = await f.AddZoneAsync(w1, "PCK", ZoneTypes.Picking);
        var bin = await f.AddBinAsync(z1, "P-01");
        await f.AddBinAsync(z1, "P-OFF", isActive: false);
        await f.AddBinAsync(await f.AddZoneAsync(w2, "RSV", ZoneTypes.Reserve), "R-01");
        var sync = f.Get<SyncService>();

        var page = await sync.BinsAsync(new SyncQuery(WarehousePublicId: w1.PublicId), default);

        var item = Assert.Single(page.Items);
        Assert.Equal(bin.WarehouseBinId, item.Id);
        Assert.Equal("PCK", item.ZoneCode);
        Assert.Equal(ZoneTypes.Picking, item.ZoneTypeCode);
        Assert.Equal(w1.PublicId, item.WarehousePublicId);
        await Assert.ThrowsAsync<NotFoundException>(() => sync.BinsAsync(new SyncQuery(WarehousePublicId: Guid.NewGuid()), default));
    }

    [Fact]
    public async Task Bins_difference_includes_deactivated_bins_and_bins_of_a_deactivated_zone_as_inactive()
    {
        await using var f = await SyncFixtureAsync();
        var w1 = await f.AddWarehouseAsync("W1");
        var z1 = await f.AddZoneAsync(w1, "PCK", ZoneTypes.Picking);
        var active = await f.AddBinAsync(z1, "P-01");
        var off = await f.AddBinAsync(z1, "P-OFF", isActive: false);
        var zOff = await f.AddZoneAsync(w1, "OLD", ZoneTypes.Reserve, isActive: false);
        var inOffZone = await f.AddBinAsync(zOff, "O-01");
        var now = DateTime.UtcNow;
        // Posición y zona se auditan bajo WAREHOUSE con su propio id (la búsqueda es amplia a propósito: Contains, no Single).
        Audit(f, EntityTypes.Warehouse, off.WarehouseBinId, now);
        Audit(f, EntityTypes.Warehouse, zOff.WarehouseZoneId, now);
        await SaveClearAsync(f);
        var sync = f.Get<SyncService>();

        var diff = await sync.BinsAsync(new SyncQuery(Since: now.AddMinutes(-5), WarehousePublicId: w1.PublicId), default);

        Assert.Contains(diff.Items, i => i.Id == off.WarehouseBinId && !i.IsActive);           // el aparato la borra
        Assert.Contains(diff.Items, i => i.Id == inOffZone.WarehouseBinId && !i.IsActive);     // zona dada de baja
        Assert.DoesNotContain(diff.Items, i => i.Id == active.WarehouseBinId && !i.IsActive);
        var full = await sync.BinsAsync(new SyncQuery(WarehousePublicId: w1.PublicId), default);
        Assert.Equal(new[] { active.WarehouseBinId }, full.Items.Select(i => i.Id));
    }

    // ---------------------------------------------------------------- órdenes de compra, avisos, tareas y categorías

    private static async Task SaveClearAsync(WmsFixture f)
    {
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
    }

    private static void Audit(WmsFixture f, string entityType, int entityId, DateTime at)
        => f.Db.AuditLogs.Add(new AuditLog
        {
            TenantId = WmsFixture.TenantId, EntityTypeLookupId = f.LookupId(LookupDomains.EntityType, entityType), EntityId = entityId, CreatedAtUtc = at,
        });

    [Fact]
    public async Task Purchase_orders_full_load_brings_only_open_ones_with_pending_and_difference_marks_closed_inactive()
    {
        await using var f = await SyncFixtureAsync();
        var w = await f.AddWarehouseAsync("W1");
        var p = await f.AddProductAsync("SKU-PO");
        var supplier = new Supplier { TenantId = WmsFixture.TenantId, Name = "Proveedor PO" };
        f.Db.Set<Supplier>().Add(supplier);
        await SaveClearAsync(f);
        var yesterday = DateTime.UtcNow.AddDays(-1);
        PurchaseOrder Po(string number, string status) => new()
        {
            PublicId = Guid.NewGuid(), TenantId = WmsFixture.TenantId, SupplierId = supplier.SupplierId, WarehouseId = w.WarehouseId, Number = number,
            OrderDate = DateOnly.FromDateTime(yesterday), StatusCodeId = f.StatusId(StatusDomains.PurchaseOrderStatus, status), IsActive = true,
            CreatedAtUtc = yesterday,
        };
        var open = Po("PO-00001", PurchaseOrderStatuses.Sent);
        var closed = Po("PO-00002", PurchaseOrderStatuses.Received);
        var draft = Po("PO-00003", PurchaseOrderStatuses.Draft);
        var partial = Po("PO-00004", PurchaseOrderStatuses.Partial);
        f.Db.Set<PurchaseOrder>().AddRange(open, closed, draft, partial);
        await SaveClearAsync(f);
        f.Db.Set<PurchaseOrderLine>().Add(new PurchaseOrderLine { PurchaseOrderId = open.PurchaseOrderId, ProductId = p.ProductId, QtyOrdered = 10m, QtyReceived = 4m, UnitCost = 1m });
        await SaveClearAsync(f);
        var sync = f.Get<SyncService>();

        // Abiertas = DRAFT, SENT y PARTIAL (en orden de id); RECEIVED no llega en la carga completa.
        var full = await sync.PurchaseOrdersAsync(new SyncQuery(), default);
        Assert.Equal(new[] { PurchaseOrderStatuses.Sent, PurchaseOrderStatuses.Draft, PurchaseOrderStatuses.Partial }, full.Items.Select(i => i.StatusCode));
        Assert.All(full.Items, i => Assert.True(i.IsActive));
        var item = Assert.Single(full.Items, i => i.PublicId == open.PublicId);
        Assert.Equal(open.PublicId, item.PublicId);
        Assert.True(item.IsActive);
        Assert.Equal(PurchaseOrderStatuses.Sent, item.StatusCode);
        Assert.Equal("Proveedor PO", item.SupplierName);
        var line = Assert.Single(item.Lines);
        Assert.Equal((10m, 4m, 6m), (line.QtyOrdered, line.QtyReceived, line.QtyPending));
        Assert.Equal(p.PublicId, line.ProductPublicId);

        var now = DateTime.UtcNow;
        Audit(f, EntityTypes.PurchaseOrder, closed.PurchaseOrderId, now);
        await SaveClearAsync(f);
        var diff = await sync.PurchaseOrdersAsync(new SyncQuery(Since: now.AddMinutes(-5)), default);
        var gone = Assert.Single(diff.Items);
        Assert.Equal(closed.PublicId, gone.PublicId);
        Assert.False(gone.IsActive);   // el aparato la borra
    }

    [Fact]
    public async Task Asns_full_load_brings_only_expected_with_lines_and_difference_marks_received_inactive()
    {
        await using var f = await SyncFixtureAsync();
        var w = await f.AddWarehouseAsync("W1");
        var p = await f.AddProductAsync("SKU-ASN");
        var yesterday = DateTime.UtcNow.AddDays(-1);
        Asn NewAsn(string reference, string status) => new()
        {
            TenantId = WmsFixture.TenantId, WarehouseId = w.WarehouseId, Reference = reference,
            StatusCodeId = f.StatusId(StatusDomains.AsnStatus, status), IsActive = true, CreatedAtUtc = yesterday,
        };
        var expected = NewAsn("ASN-EXP", AsnStatuses.Expected);
        var received = NewAsn("ASN-REC", AsnStatuses.Received);
        f.Db.Set<Asn>().AddRange(expected, received);
        await SaveClearAsync(f);
        f.Db.Set<AsnLine>().Add(new AsnLine { AsnId = expected.AsnId, ProductId = p.ProductId, ExpectedQty = 7m, LotNumber = "L-1" });
        await SaveClearAsync(f);
        var sync = f.Get<SyncService>();

        var full = await sync.AsnsAsync(new SyncQuery(), default);
        var item = Assert.Single(full.Items);
        Assert.Equal((expected.AsnId, "ASN-EXP", AsnStatuses.Expected, true), (item.Id, item.Reference, item.StatusCode, item.IsActive));
        var line = Assert.Single(item.Lines);
        Assert.Equal((p.PublicId, 7m, "L-1"), (line.ProductPublicId, line.ExpectedQty, line.LotNumber));

        var now = DateTime.UtcNow;
        Audit(f, EntityTypes.Asn, received.AsnId, now);
        await SaveClearAsync(f);
        var diff = await sync.AsnsAsync(new SyncQuery(Since: now.AddMinutes(-5)), default);
        var gone = Assert.Single(diff.Items);
        Assert.Equal(received.AsnId, gone.Id);
        Assert.False(gone.IsActive);
    }

    [Fact]
    public async Task Warehouse_tasks_full_load_brings_open_ones_of_the_warehouse_and_difference_includes_completed_as_inactive()
    {
        await using var f = await SyncFixtureAsync();
        var w1 = await f.AddWarehouseAsync("W1");
        var w2 = await f.AddWarehouseAsync("W2");
        var pending = await f.AddTaskAsync(new WarehouseTaskSpec(WarehouseTaskTypes.Count, w1.WarehouseId));
        var inProgress = await f.AddTaskAsync(new WarehouseTaskSpec(WarehouseTaskTypes.Count, w1.WarehouseId));
        var done = await f.AddTaskAsync(new WarehouseTaskSpec(WarehouseTaskTypes.Count, w1.WarehouseId));
        var other = await f.AddTaskAsync(new WarehouseTaskSpec(WarehouseTaskTypes.Count, w2.WarehouseId));
        var now = DateTime.UtcNow;
        var ip = await f.Db.WarehouseTasks.AsTracking().SingleAsync(t => t.WarehouseTaskId == inProgress.WarehouseTaskId);
        ip.StatusCodeId = f.StatusId(StatusDomains.WarehouseTaskStatus, WarehouseTaskStatuses.InProgress);
        var d = await f.Db.WarehouseTasks.AsTracking().SingleAsync(t => t.WarehouseTaskId == done.WarehouseTaskId);
        d.StatusCodeId = f.StatusId(StatusDomains.WarehouseTaskStatus, WarehouseTaskStatuses.Done);
        d.CompletedAtUtc = now;
        await SaveClearAsync(f);
        var sync = f.Get<SyncService>();

        var full = await sync.WarehouseTasksAsync(new SyncQuery(WarehousePublicId: w1.PublicId), default);
        Assert.Equal(new[] { pending.WarehouseTaskId, inProgress.WarehouseTaskId }, full.Items.Select(i => i.Id));
        Assert.All(full.Items, i => Assert.True(i.IsActive));
        Assert.All(full.Items, i => Assert.Equal(w1.PublicId, i.WarehousePublicId));

        var all = await sync.WarehouseTasksAsync(new SyncQuery(), default);
        Assert.Contains(all.Items, i => i.Id == other.WarehouseTaskId);

        var diff = await sync.WarehouseTasksAsync(new SyncQuery(Since: now.AddMinutes(-5), WarehousePublicId: w1.PublicId), default);
        Assert.Equal(new[] { pending.WarehouseTaskId, inProgress.WarehouseTaskId, done.WarehouseTaskId }, diff.Items.Select(i => i.Id));
        var closed = Assert.Single(diff.Items, i => i.Id == done.WarehouseTaskId);
        Assert.False(closed.IsActive);
        Assert.Equal(WarehouseTaskStatuses.Done, closed.StatusCode);
    }

    [Fact]
    public async Task Product_categories_full_load_brings_active_and_difference_marks_deactivated_inactive()
    {
        await using var f = await SyncFixtureAsync();
        var active = new ProductCategory { TenantId = WmsFixture.TenantId, Name = "Bebidas", IsActive = true };
        var inactive = new ProductCategory { TenantId = WmsFixture.TenantId, Name = "Descontinuada", IsActive = false };
        var foreign = new ProductCategory { TenantId = WmsFixture.OtherTenantId, Name = "Ajena", IsActive = true };
        f.Db.Set<ProductCategory>().AddRange(active, inactive, foreign);
        await SaveClearAsync(f);
        var sync = f.Get<SyncService>();

        var full = await sync.ProductCategoriesAsync(new SyncQuery(), default);
        var item = Assert.Single(full.Items);
        Assert.Equal((active.ProductCategoryId, "Bebidas", true), (item.Id, item.Name, item.IsActive));

        var now = DateTime.UtcNow;
        Audit(f, EntityTypes.ProductCategory, inactive.ProductCategoryId, now);
        await SaveClearAsync(f);
        var diff = await sync.ProductCategoriesAsync(new SyncQuery(Since: now.AddMinutes(-5)), default);
        var gone = Assert.Single(diff.Items);
        Assert.Equal(inactive.ProductCategoryId, gone.Id);
        Assert.False(gone.IsActive);
    }

    // ================================================================ código escaneado

    [Fact]
    public async Task By_barcode_finds_barcode_first_then_sku_and_only_active()
    {
        await using var f = await SyncFixtureAsync();
        var byBarcode = await f.AddProductAsync("SKU-1");
        byBarcode.Barcode = "7501234567890";
        var bySku = await f.AddProductAsync("7501234567890X");
        var skuCollision = await f.AddProductAsync("7501234567890");   // SKU = código de barras de byBarcode: gana el código de barras
        var inactive = await f.AddProductAsync("SKU-OFF", isActive: false);
        inactive.Barcode = "999";
        f.Db.Products.UpdateRange(byBarcode, inactive);   // el fixture suelta el tracker al guardar
        await f.Db.SaveChangesAsync();
        var products = f.Get<ProductService>();

        var hit = (await products.GetByBarcodeAsync("7501234567890", InventoryScope.Any, default)).Product.PublicId;
        Assert.Equal(byBarcode.PublicId, hit);
        Assert.NotEqual(skuCollision.PublicId, hit);
        Assert.Equal(bySku.PublicId, (await products.GetByBarcodeAsync(" 7501234567890X ", InventoryScope.Any, default)).Product.PublicId);
        var ex = await Assert.ThrowsAsync<ProductService.BarcodeNotFoundException>(() => products.GetByBarcodeAsync("999", InventoryScope.Any, default));
        Assert.Equal("No hay un producto con ese código.", ex.Message);
        Assert.Equal(404, ex.StatusCode);
        await Assert.ThrowsAsync<ProductService.BarcodeNotFoundException>(() => products.GetByBarcodeAsync("SKU-OFF", InventoryScope.Any, default));
    }

    // ================================================================ conteo a ciegas

    [Fact]
    public void Blind_count_hides_expected_quantities_but_keeps_what_was_counted()
    {
        var header = new CycleCountDto(1, "CC-00001", Guid.NewGuid(), "W1", CycleCountStatuses.Open, "Abierto", 1, 1, 1, 2m, DateTime.UtcNow, null, true);
        var line = new CycleCountLineDto(10, 5, "P-01", "PCK", Guid.NewGuid(), "SKU", "Producto", null, TrackingTypes.Serial, null, null,
            SystemQty: 3m, CountedQty: 5m, VarianceQty: 2m, ExpectedSerials: new[] { "S1", "S2", "S3" }, CountedSerials: new[] { "S1" },
            IsStale: true, CurrentQty: 4m, ReconciledSystemQty: 4m, SystemQtyChanged: true, AdjustedQty: 1m, AdjustmentTxnId: 77);
        var detail = new CycleCountDetailDto(header, new[] { line }, "AAAA");

        var blind = CycleCountService.Blind(detail);

        Assert.True(blind.IsBlind);
        Assert.False(detail.IsBlind);
        var l = Assert.Single(blind.Lines);
        Assert.Null(l.SystemQty);
        Assert.Null(l.VarianceQty);
        Assert.Null(l.CurrentQty);
        Assert.Null(l.ReconciledSystemQty);
        Assert.Null(l.AdjustedQty);
        Assert.Null(l.AdjustmentTxnId);
        Assert.Empty(l.ExpectedSerials);
        Assert.False(l.IsStale);
        Assert.False(l.SystemQtyChanged);
        Assert.Equal(5m, l.CountedQty);
        Assert.Equal(new[] { "S1" }, l.CountedSerials);
        Assert.Equal("SKU", l.Sku);
        // El encabezado tampoco deja deducir lo esperado (contado − diferencia neta = esperado).
        Assert.Null(blind.Count.VarianceLines);
        Assert.Null(blind.Count.NetVariance);
        Assert.Equal(1, blind.Count.CountedLines);
        Assert.Equal(1, detail.Count.VarianceLines);
        Assert.Equal(2m, detail.Count.NetVariance);
    }

    // ================================================================ recibo en una llamada

    [Fact]
    public async Task Receipt_with_confirm_is_created_and_confirmed_in_one_call()
    {
        await using var f = await ReceivingFixture.CreateAsync();
        var receipts = f.Get<ReceiptService>();

        var confirmed = await receipts.CreateAsync(new ReceiptCreateRequest(
            Lines: new[] { new ReceiptLineRequest(f.ProductNonePublicId, 6m) }, Confirm: true), default);

        Assert.Equal(ReceiptStatuses.Received, confirmed.Header.StatusCode);
        Assert.NotNull(confirmed.Header.ReceivedAtUtc);
        Assert.Equal(6m, Assert.Single(confirmed.PutawayTasks).Quantity);
        Assert.Equal(6m, Assert.Single(await f.Db.Set<InventoryTransaction>().AsNoTracking().ToListAsync()).Quantity);
        Assert.Single(await f.Db.Set<ReceiptHeader>().AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Receipt_without_confirm_still_starts_open()
    {
        await using var f = await ReceivingFixture.CreateAsync();
        var created = await f.Get<ReceiptService>().CreateAsync(new ReceiptCreateRequest(
            Lines: new[] { new ReceiptLineRequest(f.ProductNonePublicId, 2m) }), default);
        Assert.Equal(ReceiptStatuses.Open, created.Header.StatusCode);
        Assert.Empty(await f.Db.Set<InventoryTransaction>().AsNoTracking().ToListAsync());
    }

    // ================================================================ recolectar + empacar

    [Fact]
    public async Task Collect_and_pack_validates_the_pack_before_taking_any_inventory()
    {
        await using var f = await WmsFixture.CreateAsync(s => s.AddSingleton(sp => new PickBatchService(
            sp.GetRequiredService<Teikem.Infrastructure.Persistence.TeikemDbContext>(), sp.GetRequiredService<ITenantContext>(),
            sp.GetRequiredService<ILookupCache>(), sp.GetRequiredService<StatusService>(), sp.GetRequiredService<INumberSequenceService>(),
            sp.GetRequiredService<PermissionService>(), sp.GetRequiredService<InventoryLedger>(), null!)));
        var w = await f.AddWarehouseAsync("W1");
        var p = await f.AddProductAsync("SKU-PACK");
        var batches = f.Get<PickBatchService>();
        var lines = new[] { new PickBatchLineRequest(p.PublicId, 1m) };

        var noOrder = await Assert.ThrowsAsync<ValidationException>(() => batches.CollectAndPackAsync(
            new PickBatchCreateRequest(w.PublicId, lines, new PickBatchPackRequest(null)), default));
        Assert.Equal(PickBatchRules.OrderRequired, noOrder.Message);
        var noPack = await Assert.ThrowsAsync<ValidationException>(() => batches.CollectAndPackAsync(
            new PickBatchCreateRequest(w.PublicId, lines), default));
        Assert.Equal(PickBatchRules.OrderRequired, noPack.Message);

        // La variante atómica tiene su propia ruta: 'pack' en POST /pick-batches → 400 (sin tocar inventario).
        var packOnCollect = await Assert.ThrowsAsync<ValidationException>(() => batches.CollectAsync(
            new PickBatchCreateRequest(w.PublicId, lines, new PickBatchPackRequest(null)), default));
        Assert.Equal(PickBatchRules.PackUseCollectAndPack, packOnCollect.Message);

        Assert.Empty(await f.Db.Set<PickBatch>().AsNoTracking().ToListAsync());
        Assert.Empty(await f.TransactionsAsync());
    }
}
