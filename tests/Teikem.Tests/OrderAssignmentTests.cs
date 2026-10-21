using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Catalogs;
using Teikem.Domain.Constants;
using Teikem.Domain.Sales;
using Teikem.Domain.Security;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 195 — asignar la orden de venta a quien la va a despachar (y «tomarla»): solo solicitada o parcial, solo miembros activos, una asignada a otra persona no se toma y la lista filtra
/// «asignadas a mí». (La orden de compra usa la misma regla; ver PurchaseOrderService.AssignAsync.)
/// </summary>
public sealed class OrderAssignmentTests
{
    private const int Me = 1;
    private const int Ana = 2;

    private static async Task<WmsFixture> CreateAsync()
    {
        var f = await WmsFixture.CreateAsync(s =>
        {
            s.AddSingleton<ITenantClock>(TenantClock.Default);
            s.AddSingleton<TaxService>();
            s.AddSingleton<FakeBalance>();
            s.AddSingleton<Teikem.Infrastructure.Orders.IClientBalanceProvider>(sp => sp.GetRequiredService<FakeBalance>());
            s.AddSingleton<SalesOrderService>();
        });
        f.SetModules(ModuleKeys.SalesOrders);
        f.Db.StatusCodes.Add(new StatusCode { StatusCodeId = 900, Entity = StatusDomains.MembershipStatus, InternalCode = MembershipStatuses.Active, LabelJson = "{\"es\":\"Activo\"}", IsActive = true });
        f.Db.UserTenants.AddRange(
            new UserTenant { UserId = Me, TenantId = WmsFixture.TenantId, StatusCodeId = 900 },
            new UserTenant { UserId = Ana, TenantId = WmsFixture.TenantId, StatusCodeId = 900 });
        await f.Db.SaveChangesAsync();
        return f;
    }

    private static async Task<SalesOrderDto> NewOrderAsync(WmsFixture f, string status)
    {
        var w = await f.AddWarehouseAsync("W1");
        var c = await f.AddClientAsync("C1");
        var p = await f.AddProductAsync("MED");
        var row = await f.Db.Set<Product>().AsTracking().SingleAsync(x => x.ProductId == p.ProductId);
        row.SalePrice = 10m;
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        var o = await f.Get<SalesOrderService>().CreateAsync(new SalesOrderRequest(w.PublicId, c.PublicId, Lines: new[] { new SalesOrderLineRequest(p.PublicId, 2m, null) }), default);
        if (status != SalesOrderStatuses.Draft)
        {
            var statusId = (await f.Db.StatusCodes.AsNoTracking().FirstAsync(s => s.Entity == SalesCodes.StatusDomain && s.InternalCode == status)).StatusCodeId;
            var order = await f.Db.Set<SalesOrder>().AsTracking().FirstAsync(x => x.PublicId == o.PublicId);
            order.StatusCodeId = statusId;
            await f.Db.SaveChangesAsync();
            f.Db.ChangeTracker.Clear();
        }
        return o;
    }

    [Fact]
    public async Task A_supervisor_assigns_and_clears_an_open_order_and_the_list_filters_mine()
    {
        await using var f = await CreateAsync();
        var o = await NewOrderAsync(f, SalesOrderStatuses.Requested);
        var svc = f.Get<SalesOrderService>();
        Assert.Null(o.AssignedToUserId);

        var assigned = await svc.AssignAsync(o.PublicId, new TaskAssignRequest(Ana), default);
        Assert.Equal(Ana, assigned.AssignedToUserId);
        var list = await svc.ListAsync(null, null, null, false, null, null, 0, 50, default, assignedUserId: Ana);
        Assert.Single(list.Items);
        Assert.Empty((await svc.ListAsync(null, null, null, false, null, null, 0, 50, default, assignedToMe: true)).Items);   // yo soy el usuario 1 y no es mía

        Assert.Null((await svc.AssignAsync(o.PublicId, new TaskAssignRequest(null), default)).AssignedToUserId);
    }

    [Fact]
    public async Task Taking_an_unassigned_order_works_but_one_assigned_to_someone_else_is_refused()
    {
        await using var f = await CreateAsync();
        var o = await NewOrderAsync(f, SalesOrderStatuses.Partial);
        var svc = f.Get<SalesOrderService>();
        Assert.Equal(Me, (await svc.TakeAsync(o.PublicId, null, default)).AssignedToUserId);
        await svc.AssignAsync(o.PublicId, new TaskAssignRequest(Ana), default);
        var e = await Assert.ThrowsAsync<ConflictException>(() => svc.TakeAsync(o.PublicId, null, default));
        Assert.Equal(DocumentAssignmentRules.AlreadyAssigned, e.Message);
    }

    [Fact]
    public async Task Only_open_orders_are_assigned_and_only_active_members()
    {
        await using var f = await CreateAsync();
        var draft = await NewOrderAsync(f, SalesOrderStatuses.Draft);
        var svc = f.Get<SalesOrderService>();
        var e = await Assert.ThrowsAsync<StatusRuleException>(() => svc.AssignAsync(draft.PublicId, new TaskAssignRequest(Ana), default));
        Assert.Equal(DocumentAssignmentRules.SalesOrderNotOpen, e.Message);
        await Assert.ThrowsAsync<ValidationException>(() => svc.AssignAsync(draft.PublicId, new TaskAssignRequest(99), default));
    }
}
