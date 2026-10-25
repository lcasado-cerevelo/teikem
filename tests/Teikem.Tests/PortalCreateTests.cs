using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Catalogs;
using Teikem.Domain.Clients;
using Teikem.Domain.Constants;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Notifications;
using Teikem.Infrastructure.Services;
using Teikem.Infrastructure.Wms;
using Xunit;

namespace Teikem.Tests;

/// <summary>Lote 156 — lo que el cliente crea desde su portal: la bandera por cliente manda, el solo lectura no puede, todo sale a nombre del cliente de la sesión y lo ajeno no existe.</summary>
public sealed class PortalCreateTests
{
    private sealed record World(WmsFixture F, Client Mine, Client Other, Warehouse W, Product P, Product Foreign, PortalCreateService Svc);

    private static async Task<World> WorldAsync(string role = PortalRoles.ClientAdmin, bool products = true, bool arrivals = true, bool exits = true)
    {
        var f = await WmsFixture.CreateAsync(s =>
        {
            s.AddSingleton<ITenantClock>(TenantClock.Default);
            s.AddSingleton<INotificationEventProvider, ExitNoticeEventProvider>();
            s.AddSingleton<INotificationEventProvider, AsnEventProvider>();
            s.AddSingleton<NotificationEventCatalog>();
            s.AddSingleton<INotificationPublisher, NotificationPublisher>();
            s.AddSingleton<ExitNoticeService>();
            s.AddSingleton<ProductService>();
            s.AddSingleton<AsnService>();
            s.AddSingleton<InventoryReadService>();
            s.AddSingleton<ValuationService>();
            s.AddSingleton<PortalWarehouseService>();
            s.AddSingleton<PortalCreateService>();
        });
        var w = await f.AddWarehouseAsync("W1");
        var bin = await f.AddBinAsync(await f.AddZoneAsync(w, "PZ", ZoneTypes.Picking), "P-01");
        var mine = await f.AddClientAsync("MIO");
        var other = await f.AddClientAsync("OTRO");
        var p = await f.AddProductAsync("MED", ownerClientId: mine.ClientId, purchaseCost: 5m);
        var foreign = await f.AddProductAsync("AJENO", ownerClientId: other.ClientId, purchaseCost: 9m);
        await f.PostAsync(new InventoryPosting(InventoryTxnTypes.Receipt, p.ProductId, 10m, ToWarehouseId: w.WarehouseId, ToBinId: bin.WarehouseBinId));
        var roles = new[] { PortalRoles.ClientAdmin, PortalRoles.ClientOperator, PortalRoles.ClientReadOnly }
            .Select((c, n) => new LookupCode { LookupCodeId = 9000 + n, Entity = LookupDomains.PortalRole, InternalCode = c, LabelJson = "{}", IsActive = true }).ToList();
        f.Db.LookupCodes.AddRange(roles);
        f.Db.PortalUsers.Add(new PortalUser { TenantId = WmsFixture.TenantId, ClientId = mine.ClientId, UserId = 1, Email = "a@b.c", RoleLookupId = roles.Single(r => r.InternalCode == role).LookupCodeId, StatusCodeId = 1, IsActive = true });
        var c = await f.Db.Set<Client>().SingleAsync(x => x.ClientId == mine.ClientId);
        (c.PortalCanCreateProducts, c.PortalCanCreateArrivals, c.PortalCanCreateExits) = (products, arrivals, exits);
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        f.Tenant.PortalClientId = mine.ClientId;
        return new World(f, mine, other, w, p, foreign, f.Get<PortalCreateService>());
    }

    [Fact]
    public void Only_admin_and_operator_roles_can_create()
    {
        Assert.True(PortalCreateRules.RoleCanCreate(PortalRoles.ClientAdmin));
        Assert.True(PortalCreateRules.RoleCanCreate(PortalRoles.ClientOperator));
        Assert.False(PortalCreateRules.RoleCanCreate(PortalRoles.ClientReadOnly));
        Assert.False(PortalCreateRules.RoleCanCreate(null));
    }

    [Fact]
    public async Task A_client_creates_its_own_product_owned_by_itself()
    {
        var x = await WorldAsync();
        await using var _ = x.F;
        var created = await x.Svc.CreateProductAsync(new PortalProductCreateRequest("NUEVO-1", "Producto nuevo", PurchaseCost: 4m), default);
        Assert.Equal("NUEVO-1", created.Sku);
        var stored = await x.F.Db.Set<Product>().AsNoTracking().SingleAsync(p => p.Sku == "NUEVO-1");
        Assert.Equal((x.Mine.ClientId, 4m), (stored.ClientId, stored.PurchaseCost));
    }

    [Fact]
    public async Task A_client_edits_its_own_product_but_not_another_clients_and_not_with_an_empty_body()
    {
        var x = await WorldAsync();
        await using var _ = x.F;
        var edited = await x.Svc.UpdateProductAsync(x.P.PublicId, new PortalProductPatchRequest(Name: "Nombre nuevo", Brand: "Marca X", PurchaseCost: 7m), default);
        Assert.Equal(("Nombre nuevo", "Marca X"), (edited.Name, edited.Brand));
        var stored = await x.F.Db.Set<Product>().AsNoTracking().SingleAsync(p => p.ProductId == x.P.ProductId);
        Assert.Equal((x.P.Sku, 7m), (stored.Sku, stored.PurchaseCost));
        await Assert.ThrowsAsync<NotFoundException>(() => x.Svc.UpdateProductAsync(x.Foreign.PublicId, new PortalProductPatchRequest(Name: "Robado"), default));
        Assert.NotEqual("Robado", (await x.F.Db.Set<Product>().AsNoTracking().SingleAsync(p => p.ProductId == x.Foreign.ProductId)).Name);
        await Assert.ThrowsAsync<ValidationException>(() => x.Svc.UpdateProductAsync(x.P.PublicId, new PortalProductPatchRequest(), default));
    }

    [Fact]
    public async Task Editing_a_product_needs_the_flag_and_a_writing_role()
    {
        var off = await WorldAsync(products: false);
        await using (off.F)
            Assert.Equal(PortalCreateRules.ProductsOff, (await Assert.ThrowsAsync<ForbiddenException>(() => off.Svc.UpdateProductAsync(off.P.PublicId, new PortalProductPatchRequest(Name: "X"), default))).Message);
        var ro = await WorldAsync(PortalRoles.ClientReadOnly);
        await using (ro.F)
            Assert.Equal(PortalCreateRules.ReadOnlyForbidden, (await Assert.ThrowsAsync<ForbiddenException>(() => ro.Svc.UpdateProductAsync(ro.P.PublicId, new PortalProductPatchRequest(Name: "X"), default))).Message);
    }

    [Fact]
    public async Task The_flag_off_or_a_read_only_access_is_forbidden_with_its_message()
    {
        var off = await WorldAsync(products: false, arrivals: false, exits: false);
        await using (off.F)
        {
            Assert.Equal(PortalCreateRules.ProductsOff, (await Assert.ThrowsAsync<ForbiddenException>(() => off.Svc.CreateProductAsync(new PortalProductCreateRequest("X", "X"), default))).Message);
            Assert.Equal(PortalCreateRules.ArrivalsOff, (await Assert.ThrowsAsync<ForbiddenException>(() => off.Svc.CreateArrivalAsync(new PortalArrivalRequest(), default))).Message);
            Assert.Equal(PortalCreateRules.ExitsOff, (await Assert.ThrowsAsync<ForbiddenException>(() => off.Svc.CreateExitAsync(new PortalExitRequest(), default))).Message);
        }
        var ro = await WorldAsync(PortalRoles.ClientReadOnly);
        await using (ro.F)
            Assert.Equal(PortalCreateRules.ReadOnlyForbidden, (await Assert.ThrowsAsync<ForbiddenException>(() => ro.Svc.CreateProductAsync(new PortalProductCreateRequest("X", "X"), default))).Message);
    }

    [Fact]
    public async Task An_arrival_is_created_for_my_client_and_only_my_products_are_accepted()
    {
        var x = await WorldAsync();
        await using var _ = x.F;
        var arrival = await x.Svc.CreateArrivalAsync(new PortalArrivalRequest(x.W.PublicId, "OC-1", Lines: new[] { new PortalArrivalLineRequest(x.P.PublicId, 6m, UnitPrice: 5m) }), default);
        Assert.Equal(("OC-1", true), (arrival.Reference, arrival.CanCancel));
        Assert.Equal(6m, Assert.Single(arrival.Lines).ExpectedQty);
        Assert.Single(await x.Svc.ListArrivalsAsync(null, null, null, default));

        // el producto de otro cliente no se acepta
        await Assert.ThrowsAsync<ValidationException>(() => x.Svc.CreateArrivalAsync(new PortalArrivalRequest(x.W.PublicId, Lines: new[] { new PortalArrivalLineRequest(x.Foreign.PublicId, 1m) }), default));

        var cancelled = await x.Svc.CancelArrivalAsync(arrival.Id, default);
        Assert.False(cancelled.CanCancel);
    }

    [Fact]
    public async Task An_exit_notice_is_created_listed_and_cancelled_only_for_my_client()
    {
        var x = await WorldAsync();
        await using var _ = x.F;
        var exit = await x.Svc.CreateExitAsync(new PortalExitRequest(x.W.PublicId, "PED-9", "Retiro", Lines: new[] { new PortalExitLineRequest(x.P.PublicId, 4m) }), default);
        Assert.Equal(4m, Assert.Single(exit.Lines).QtyReserved);
        Assert.Equal(1, (await x.Svc.ListExitsAsync(null, null, null, null, 0, 25, default)).Total);

        await Assert.ThrowsAsync<ValidationException>(() => x.Svc.CreateExitAsync(new PortalExitRequest(x.W.PublicId, Lines: new[] { new PortalExitLineRequest(x.Foreign.PublicId, 1m) }), default));

        var cancelled = await x.Svc.CancelExitAsync(exit.PublicId, null, default);
        Assert.False(cancelled.CanCancel);
    }

    [Fact]
    public async Task Arrivals_and_exits_can_be_listed_by_a_date_range()
    {
        var x = await WorldAsync();
        await using var _ = x.F;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var soon = today.AddDays(3);
        await x.Svc.CreateArrivalAsync(new PortalArrivalRequest(x.W.PublicId, "OC-FECHA", soon, Lines: new[] { new PortalArrivalLineRequest(x.P.PublicId, 2m) }), default);
        Assert.Single(await x.Svc.ListArrivalsAsync(null, soon, soon, default));                  // el día exacto
        Assert.Single(await x.Svc.ListArrivalsAsync(null, today, soon.AddDays(10), default));     // un rango que lo incluye
        Assert.Empty(await x.Svc.ListArrivalsAsync(null, soon.AddDays(1), null, default));        // desde después de su fecha
        Assert.Empty(await x.Svc.ListArrivalsAsync(null, null, today, default));                  // hasta antes de su fecha

        await x.Svc.CreateExitAsync(new PortalExitRequest(x.W.PublicId, "PED-FECHA", "Retiro", Lines: new[] { new PortalExitLineRequest(x.P.PublicId, 1m) }), default);
        var day = (await x.Svc.ListExitsAsync(null, null, null, null, 0, 25, default)).Items.Single().RequestedDate;
        Assert.Equal(1, (await x.Svc.ListExitsAsync(null, null, day, day, 0, 25, default)).Total);
        Assert.Equal(0, (await x.Svc.ListExitsAsync(null, null, day.AddDays(1), null, 0, 25, default)).Total);
        Assert.Equal(0, (await x.Svc.ListExitsAsync(null, null, null, day.AddDays(-1), 0, 25, default)).Total);
    }

    [Fact]
    public async Task Another_clients_notices_do_not_exist_for_me()
    {
        var x = await WorldAsync();
        await using var _ = x.F;
        // aviso de salida de OTRO cliente creado por el personal
        x.F.Tenant.PortalClientId = null;
        var theirs = await x.F.Get<ExitNoticeService>().CreateAsync(new ExitNoticeRequest(x.W.PublicId, x.Other.PublicId, Lines: new[] { new ExitNoticeLineRequest(x.Foreign.PublicId, 1m) }), default);
        var theirArrival = await x.F.Get<AsnService>().CreateAsync(new AsnCreateRequest(x.W.PublicId, x.Other.PublicId, Lines: new[] { new AsnLineRequest(x.Foreign.PublicId, 2m) }), default);
        x.F.Tenant.PortalClientId = x.Mine.ClientId;

        await Assert.ThrowsAsync<NotFoundException>(() => x.Svc.GetExitAsync(theirs.PublicId, default));
        await Assert.ThrowsAsync<NotFoundException>(() => x.Svc.CancelExitAsync(theirs.PublicId, null, default));
        await Assert.ThrowsAsync<NotFoundException>(() => x.Svc.CancelArrivalAsync(theirArrival.Id, default));
        Assert.Empty((await x.Svc.ListExitsAsync(null, null, null, null, 0, 25, default)).Items);
        Assert.Empty(await x.Svc.ListArrivalsAsync(null, null, null, default));
    }

    [Fact]
    public async Task I_can_edit_my_arrival_and_my_exit_but_not_another_clients()
    {
        var x = await WorldAsync();
        await using var _ = x.F;
        var arrival = await x.Svc.CreateArrivalAsync(new PortalArrivalRequest(x.W.PublicId, "OC-1", Lines: new[] { new PortalArrivalLineRequest(x.P.PublicId, 6m) }), default);
        var edited = await x.Svc.UpdateArrivalAsync(arrival.Id, new PortalArrivalRequest(Reference: "OC-2", Lines: new[] { new PortalArrivalLineRequest(x.P.PublicId, 8m) }), default);
        Assert.Equal(("OC-2", 8m), (edited.Reference, Assert.Single(edited.Lines).ExpectedQty));

        var exit = await x.Svc.CreateExitAsync(new PortalExitRequest(x.W.PublicId, "PED-1", "Retiro", Lines: new[] { new PortalExitLineRequest(x.P.PublicId, 4m) }), default);
        var header = await x.Svc.UpdateExitAsync(exit.PublicId, new PortalExitHeaderRequest(Destination: "Dirección nueva", Notes: "urgente"), default);
        Assert.Equal(("PED-1", "Dirección nueva", "urgente"), (header.Reference, header.Destination, header.Notes));
        var line = await x.Svc.ModifyExitLineAsync(exit.PublicId, header.Lines[0].LineId, new PortalExitLineModifyRequest(7m), default);
        Assert.Equal(7m, Assert.Single(line.Lines).QtyOrdered);

        x.F.Tenant.PortalClientId = null;
        var theirs = await x.F.Get<ExitNoticeService>().CreateAsync(new ExitNoticeRequest(x.W.PublicId, x.Other.PublicId, Lines: new[] { new ExitNoticeLineRequest(x.Foreign.PublicId, 1m) }), default);
        x.F.Tenant.PortalClientId = x.Mine.ClientId;
        await Assert.ThrowsAsync<NotFoundException>(() => x.Svc.UpdateExitAsync(theirs.PublicId, new PortalExitHeaderRequest(Notes: "x"), default));
        await Assert.ThrowsAsync<NotFoundException>(() => x.Svc.ModifyExitLineAsync(theirs.PublicId, 1, new PortalExitLineModifyRequest(2m), default));
    }
}
