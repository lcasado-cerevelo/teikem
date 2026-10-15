using System.Reflection;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Api.Controllers;
using Teikem.Domain.Catalogs;
using Teikem.Domain.Clients;
using Teikem.Domain.Constants;
using Teikem.Domain.Sales;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

// Lote 57 (P4, S1): órdenes de venta en borrador. Reglas puras, sección de update.sql, permisos y API por reflexión.
public class SalesRulesTests
{
    [Fact]
    public void Quantity_and_price_rules()
    {
        Assert.Null(SalesRules.QuantityError(2.5m));
        Assert.Equal(SalesRules.QuantityRequired, SalesRules.QuantityError(null));
        Assert.Equal(SalesRules.QuantityRequired, SalesRules.QuantityError(0m));
        Assert.Equal(SalesRules.QuantityDecimals, SalesRules.QuantityError(1.0005m));
        Assert.Equal(SalesRules.QuantityTooLarge, SalesRules.QuantityError(SalesRules.MaxQuantity + 1));
        Assert.Null(SalesRules.PriceError(null));
        Assert.Null(SalesRules.PriceError(0m));
        Assert.Equal(SalesRules.PriceInvalid, SalesRules.PriceError(-0.01m));
        Assert.Equal(SalesRules.PriceDecimals, SalesRules.PriceError(1.00001m));
        Assert.Equal(25.0125m, SalesRules.LineAmount(2.5m, 10.005m));
        Assert.Equal((null, null), SalesRules.NormalizeNotes("  "));
        Assert.Equal(SalesRules.NotesTooLong, SalesRules.NormalizeNotes(new string('x', 501)).Error);
        Assert.True(SalesRules.IsBackordered(1m, false));
        Assert.False(SalesRules.IsBackordered(1m, true));
        Assert.False(SalesRules.IsBackordered(0m, false));
    }

    [Fact]
    public void The_sales_order_counter_is_a_known_number_kind_the_real_numbering_service_accepts()
    {
        Assert.True(Teikem.Domain.Orders.NumberingRules.IsKnownKind(SalesCodes.NumberKind));
        Assert.Equal("OV-00007", Teikem.Domain.Clients.NumberFormat.Resolve(SalesCodes.NumberPattern, 7));
    }

    [Fact]
    public void Update_sql_section_creates_tables_status_and_permissions()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Teikem.sln"))) dir = dir.Parent;
        var sql = File.ReadAllText(Path.Combine(dir!.FullName, "Diseño", "logistica-db-update.sql"));
        var i = sql.IndexOf("2026-10-15 (k)", StringComparison.Ordinal);
        Assert.True(i > 0);
        var section = sql[i..];
        foreach (var t in new[] { "dbo.SalesOrder ", "dbo.SalesOrderLine", "dbo.SalesOrderReservation", "'SALESORDER'", "SalesOrderStatus", "'SALES_ORDER'" })
            Assert.Contains(t, section);
        foreach (var p in new[] { "sales.view", "sales.create", "sales.cancel", "sales.price", "sales.backorder" }) Assert.Contains($"'{p}'", section);
    }

    [Fact]
    public void Update_sql_section_l_adds_the_dispatch_links_as_nullable_columns_only()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Teikem.sln"))) dir = dir.Parent;
        var sql = File.ReadAllText(Path.Combine(dir!.FullName, "Diseño", "logistica-db-update.sql"));
        var i = sql.IndexOf("2026-10-15 (l)", StringComparison.Ordinal);
        Assert.True(i > 0);
        var section = SqlSections.From(sql, i);
        Assert.Contains("ADD SalesOrderId INT NULL", section);
        Assert.Contains("ADD SalesOrderLineId INT NULL", section);
        Assert.DoesNotContain("DROP ", section, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Permissions_templates_admin_has_all_sales_warehouse_operator_views_and_fulfills_and_sales_role_cannot_price()
    {
        var all = new[] { PermissionCatalog.SalesView, PermissionCatalog.SalesCreate, PermissionCatalog.SalesCancel, PermissionCatalog.SalesPrice, PermissionCatalog.SalesBackorder };
        Assert.All(all, p => Assert.Contains(p, PermissionCatalog.RoleTemplates["TenantAdmin"]));
        Assert.Contains(PermissionCatalog.SalesView, PermissionCatalog.RoleTemplates["WarehouseOperator"]);
        Assert.Contains(PermissionCatalog.SalesBackorder, PermissionCatalog.RoleTemplates["WarehouseOperator"]);
        Assert.DoesNotContain(PermissionCatalog.SalesCreate, PermissionCatalog.RoleTemplates["WarehouseOperator"]);
        var sales = PermissionCatalog.RoleTemplates["Sales"];
        Assert.Contains(PermissionCatalog.SalesCreate, sales);
        Assert.DoesNotContain(PermissionCatalog.SalesPrice, sales);
        Assert.DoesNotContain(PermissionCatalog.SalesBackorder, sales);
        Assert.Equal(("Ventas", "Sales"), PermissionCatalog.RoleTemplateLabels["Sales"]);
    }

    [Fact]
    public void Api_has_no_delete_and_no_put_orders_are_cancelled_not_deleted()
    {
        var methods = typeof(SalesOrdersController).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        var http = methods.SelectMany(m => m.GetCustomAttributes<HttpMethodAttribute>()).SelectMany(a => a.HttpMethods).ToList();
        Assert.DoesNotContain("DELETE", http);
        Assert.DoesNotContain("PUT", http);
        Assert.Contains("PATCH", http);
    }
}

/// <summary>Saldo pendiente del cliente por transporte (lo fija la prueba).</summary>
internal sealed class FakeBalance : Teikem.Infrastructure.Orders.IClientBalanceProvider
{
    public decimal Pending { get; set; }
    public Task<decimal> PendingBalanceAsync(int clientId, int? excludeOrderId, CancellationToken ct) => Task.FromResult(Pending);
}

public sealed class SalesOrderServiceTests
{
    private static Task<WmsFixture> CreateAsync()
        => WmsFixture.CreateAsync(s =>
        {
            s.AddSingleton<ITenantClock>(TenantClock.Default);
            s.AddSingleton<TaxService>();
            s.AddSingleton<FakeBalance>();
            s.AddSingleton<Teikem.Infrastructure.Orders.IClientBalanceProvider>(sp => sp.GetRequiredService<FakeBalance>());
            s.AddSingleton<SalesOrderService>();
        });

    private static async Task<Product> SellableAsync(WmsFixture f, string sku, decimal? price, int? ownerClientId = null)
    {
        var p = await f.AddProductAsync(sku, ownerClientId: ownerClientId);
        var row = await f.Db.Set<Product>().AsTracking().SingleAsync(x => x.ProductId == p.ProductId);
        row.SalePrice = price;
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        return p;
    }

    private static SalesOrderLineRequest L(Product product, decimal qty, decimal? price = null) => new(product.PublicId, qty, price);

    private static SalesOrderRequest Order(Client c, Warehouse w, params SalesOrderLineRequest[] lines) => new(w.PublicId, c.PublicId, Lines: lines);

    private static async Task<string> MsgOf(Func<Task> act) => (await Assert.ThrowsAnyAsync<TeikemException>(act)).Message;

    [Fact]
    public async Task Creates_a_draft_with_number_list_price_and_totals()
    {
        await using var f = await CreateAsync();
        f.SetModules(ModuleKeys.SalesOrders, ModuleKeys.WmsLotSerial);
        var w = await f.AddWarehouseAsync("W1");
        var c = await f.AddClientAsync("C1");
        var meter = await SellableAsync(f, "MED", 12.5m);
        var strips = await SellableAsync(f, "TIRAS", 3m);
        var svc = f.Get<SalesOrderService>();

        var o = await svc.CreateAsync(Order(c, w, L(meter, 4m), L(strips, 10m)), default);
        Assert.Equal("OV-00001", o.Number);
        Assert.Equal("DRAFT", o.Status);
        Assert.Equal(c.PublicId, o.ClientPublicId);
        Assert.Equal(2, o.Lines.Count);
        Assert.Equal(12.5m, o.Lines[0].UnitPrice);
        Assert.Equal(50m, o.Lines[0].LineSubtotal);
        Assert.Equal(80m, o.Subtotal);
        Assert.Equal(0m, o.TaxAmount);   // sin el módulo TAX el impuesto es cero
        Assert.Equal(80m, o.Total);
        Assert.All(o.Lines, l => Assert.Equal(0m, l.QtyReserved));
        Assert.Equal("OV-00002", (await svc.CreateAsync(Order(c, w, L(meter, 1m)), default)).Number);
        var list = await svc.ListAsync(null, null, "TIRAS", false, null, null, 0, 50, default);
        Assert.Single(list.Items);
        Assert.Equal(2, list.Items[0].LineCount);
    }

    [Fact]
    public async Task Price_other_than_the_list_price_needs_sales_price_and_a_product_without_price_needs_one_typed()
    {
        await using var f = await CreateAsync();
        f.SetModules(ModuleKeys.SalesOrders);
        var w = await f.AddWarehouseAsync("W1");
        var c = await f.AddClientAsync("C1");
        var p = await SellableAsync(f, "P1", 10m);
        var noPrice = await SellableAsync(f, "P2", null);
        var svc = f.Get<SalesOrderService>();

        f.SetPermissions(PermissionCatalog.SalesCreate);
        var same = await svc.CreateAsync(Order(c, w, L(p, 1m, 10m)), default);   // el mismo precio de lista no exige permiso
        Assert.Equal(10m, same.Lines[0].UnitPrice);
        var denied = await Assert.ThrowsAsync<ForbiddenException>(() => svc.CreateAsync(Order(c, w, L(p, 1m, 9m)), default));
        Assert.Equal(SalesRules.PriceNeedsPermission, denied.Message);
        Assert.Equal(SalesRules.NoPrice("P2"), await MsgOf(() => svc.CreateAsync(Order(c, w, L(noPrice, 1m)), default)));

        f.SetPermissions(PermissionCatalog.SalesCreate, PermissionCatalog.SalesPrice);
        var cheaper = await svc.CreateAsync(Order(c, w, L(p, 2m, 8.25m), L(noPrice, 1m, 5m)), default);
        Assert.Equal(8.25m, cheaper.Lines[0].UnitPrice);
        Assert.Equal(21.5m, cheaper.Subtotal);
    }

    [Fact]
    public async Task All_validations_return_the_exact_message_and_nothing_is_saved()
    {
        await using var f = await CreateAsync();
        f.SetModules(ModuleKeys.SalesOrders);
        var w = await f.AddWarehouseAsync("W1");
        var c = await f.AddClientAsync("C1");
        var p = await SellableAsync(f, "P1", 10m);
        var own3pl = await SellableAsync(f, "P3", 10m, ownerClientId: c.ClientId);
        var inactive = await f.AddProductAsync("P4", isActive: false);
        var svc = f.Get<SalesOrderService>();
        Task<string> Msg(SalesOrderRequest r) => MsgOf(() => svc.CreateAsync(r, default));

        Assert.Equal(SalesRules.ClientRequired, await Msg(new SalesOrderRequest(w.PublicId, null, Lines: new[] { L(p, 1m) })));
        Assert.Equal("Cliente no encontrado.", await Msg(new SalesOrderRequest(w.PublicId, Guid.NewGuid(), Lines: new[] { L(p, 1m) })));
        Assert.Equal(SalesRules.LinesRequired, await Msg(Order(c, w)));
        Assert.Equal(SalesRules.ProductRequired, await Msg(Order(c, w, new SalesOrderLineRequest(null, 1m))));
        Assert.Equal("Producto no encontrado.", await Msg(Order(c, w, new SalesOrderLineRequest(Guid.NewGuid(), 1m))));
        Assert.Equal(SalesRules.QuantityRequired, await Msg(Order(c, w, L(p, 0m))));
        Assert.Equal(SalesRules.QuantityDecimals, await Msg(Order(c, w, L(p, 1.0001m))));
        Assert.Equal(SalesRules.PriceInvalid, await Msg(Order(c, w, L(p, 1m, -1m))));
        Assert.Equal(SalesRules.ProductRepeated, await Msg(Order(c, w, L(p, 1m), L(p, 2m))));
        Assert.Equal(SalesRules.ProductNotOwn, await Msg(Order(c, w, L(own3pl, 1m))));
        Assert.Equal(SalesRules.ProductInactive, await Msg(Order(c, w, L(inactive, 1m, 1m))));
        Assert.Equal(SalesRules.NotesTooLong, await Msg(new SalesOrderRequest(w.PublicId, c.PublicId, Notes: new string('n', 501), Lines: new[] { L(p, 1m) })));
        Assert.Equal("Almacén no encontrado.", await Msg(new SalesOrderRequest(Guid.NewGuid(), c.PublicId, Lines: new[] { L(p, 1m) })));
        Assert.Empty((await svc.ListAsync(null, null, null, false, null, null, 0, 50, default)).Items);   // ningún intento dejó una orden
    }

    [Fact]
    public async Task A_draft_is_edited_by_replacing_its_lines_and_only_while_draft()
    {
        await using var f = await CreateAsync();
        f.SetModules(ModuleKeys.SalesOrders);
        var w = await f.AddWarehouseAsync("W1");
        var c = await f.AddClientAsync("C1");
        var p1 = await SellableAsync(f, "P1", 10m);
        var p2 = await SellableAsync(f, "P2", 4m);
        var svc = f.Get<SalesOrderService>();
        var o = await svc.CreateAsync(Order(c, w, L(p1, 2m)), default);

        var edited = await svc.UpdateAsync(o.PublicId, new SalesOrderRequest(Notes: "Entregar el viernes", RequiredDate: new DateOnly(2026, 11, 1), Lines: new[] { L(p2, 5m), L(p1, 1m) }), default);
        Assert.Equal(new[] { "P2", "P1" }, edited.Lines.Select(l => l.Sku).ToArray());
        Assert.Equal(new[] { 1, 2 }, edited.Lines.Select(l => l.LineNo).ToArray());
        Assert.Equal(30m, edited.Subtotal);
        Assert.Equal("Entregar el viernes", edited.Notes);
        Assert.Equal(new DateOnly(2026, 11, 1), edited.RequiredDate);
        Assert.Equal(o.Number, edited.Number);
        // sin líneas en el PATCH, las líneas no cambian; ClearRequiredDate la quita
        var cleared = await svc.UpdateAsync(o.PublicId, new SalesOrderRequest(ClearRequiredDate: true), default);
        Assert.Null(cleared.RequiredDate);
        Assert.Equal(2, cleared.Lines.Count);

        // fuera de borrador no se edita
        var row = await f.Db.Set<SalesOrder>().AsTracking().SingleAsync(x => x.PublicId == o.PublicId);
        row.StatusCodeId = f.StatusId(SalesCodes.StatusDomain, SalesOrderStatuses.Requested);
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        Assert.Equal(SalesRules.OnlyDraftEditable, await MsgOf(() => svc.UpdateAsync(o.PublicId, new SalesOrderRequest(Notes: "x"), default)));
    }

    [Fact]
    public async Task Cancelling_a_draft_goes_through_the_status_service_and_it_cannot_be_cancelled_twice()
    {
        await using var f = await CreateAsync();
        f.SetModules(ModuleKeys.SalesOrders);
        var w = await f.AddWarehouseAsync("W1");
        var c = await f.AddClientAsync("C1");
        var p = await SellableAsync(f, "P1", 10m);
        var svc = f.Get<SalesOrderService>();
        var o = await svc.CreateAsync(Order(c, w, L(p, 1m)), default);

        var cancelled = await svc.CancelAsync(o.PublicId, "El cliente ya no lo quiere", default);
        Assert.Equal("CANCELLED", cancelled.Status);
        var id = (await f.Db.Set<SalesOrder>().SingleAsync(x => x.PublicId == o.PublicId)).SalesOrderId;
        var history = await f.Db.Set<EntityStatusHistory>().Where(h => h.EntityId == id).ToListAsync();
        Assert.Contains(history, h => h.Comment == "El cliente ya no lo quiere");
        Assert.Equal(SalesRules.AlreadyCancelled, await MsgOf(() => svc.CancelAsync(o.PublicId, null, default)));
        Assert.Equal(SalesRules.OnlyDraftEditable, await MsgOf(() => svc.UpdateAsync(o.PublicId, new SalesOrderRequest(Notes: "x"), default)));
        Assert.Equal("Orden de venta no encontrada.", await MsgOf(() => svc.CancelAsync(Guid.NewGuid(), null, default)));
    }

    [Fact]
    public async Task With_the_tax_module_on_each_line_keeps_a_copy_of_the_tax_and_an_exempt_client_pays_none()
    {
        await using var f = await CreateAsync();
        f.SetModules(ModuleKeys.SalesOrders, ModuleKeys.Tax);
        var w = await f.AddWarehouseAsync("W1");
        var taxed = await f.AddClientAsync("C1");
        var exempt = await f.AddClientAsync("C2");
        var p = await SellableAsync(f, "P1", 100m);
        var tax = f.Get<TaxService>();
        await tax.CreateRateAsync(new TaxRateRequest("IVU", "IVU", new DateOnly(2020, 1, 1), null,
            new[] { new TaxComponentDto("STATE", "Estatal", 10.5m), new TaxComponentDto("CITY", "Municipal", 1m) }), default);
        await tax.UpdateSettingsAsync(new TaxSettingsRequest(DefaultRateCode: "IVU"), default);
        await tax.CreateExemptionAsync(new TaxExemptionRequest(exempt.ClientId, "Sin fines de lucro", "CERT-1", new DateOnly(2020, 1, 1)), default);
        var svc = f.Get<SalesOrderService>();

        var o = await svc.CreateAsync(Order(taxed, w, L(p, 2m)), default);
        Assert.Equal(200m, o.Subtotal);
        Assert.Equal(23m, o.TaxAmount);
        Assert.Equal(223m, o.Total);
        Assert.Equal(11.5m, o.Lines[0].TaxRatePct);
        Assert.False(o.Lines[0].IsTaxExempt);
        var orderId = (await f.Db.Set<SalesOrder>().SingleAsync(x => x.PublicId == o.PublicId)).SalesOrderId;
        var stored = await f.Db.Set<SalesOrderLine>().SingleAsync(l => l.SalesOrderId == orderId);
        Assert.NotNull(stored.TaxRateId);   // la copia de la tasa con que se calculó

        var ex = await svc.CreateAsync(Order(exempt, w, L(p, 2m)), default);
        Assert.Equal(0m, ex.TaxAmount);
        Assert.True(ex.Lines[0].IsTaxExempt);
        // cambiar de cliente en el borrador recalcula el impuesto
        var moved = await svc.UpdateAsync(ex.PublicId, new SalesOrderRequest(ClientPublicId: taxed.PublicId), default);
        Assert.Equal(23m, moved.TaxAmount);
        Assert.False(moved.Lines[0].IsTaxExempt);
    }

    [Fact]
    public async Task Orders_of_another_company_are_invisible()
    {
        await using var f = await CreateAsync();
        f.SetModules(ModuleKeys.SalesOrders);
        var w = await f.AddWarehouseAsync("W1");
        var c = await f.AddClientAsync("C1");
        var p = await SellableAsync(f, "P1", 10m);
        var svc = f.Get<SalesOrderService>();
        var o = await svc.CreateAsync(Order(c, w, L(p, 1m)), default);
        using (f.AsTenant(WmsFixture.OtherTenantId))
        {
            f.SetModules(ModuleKeys.SalesOrders);
            Assert.Empty((await svc.ListAsync(null, null, null, false, null, null, 0, 50, default)).Items);
            await Assert.ThrowsAsync<NotFoundException>(() => svc.GetAsync(o.PublicId, default));
            await Assert.ThrowsAsync<NotFoundException>(() => svc.CancelAsync(o.PublicId, null, default));
        }
    }

    [Fact]
    public async Task Warehouse_is_the_only_active_one_by_default_and_must_be_given_when_there_are_several()
    {
        await using var f = await CreateAsync();
        f.SetModules(ModuleKeys.SalesOrders);
        var w1 = await f.AddWarehouseAsync("W1");
        var c = await f.AddClientAsync("C1");
        var p = await SellableAsync(f, "P1", 10m);
        var svc = f.Get<SalesOrderService>();
        var one = await svc.CreateAsync(new SalesOrderRequest(null, c.PublicId, Lines: new[] { L(p, 1m) }), default);
        Assert.Equal(w1.PublicId, one.WarehousePublicId);
        await f.AddWarehouseAsync("W2");
        Assert.Equal(SalesRules.WarehouseRequired, await MsgOf(() => svc.CreateAsync(new SalesOrderRequest(null, c.PublicId, Lines: new[] { L(p, 1m) }), default)));
    }
}
