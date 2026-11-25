using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Common;
using Teikem.Domain.Constants;
using Teikem.Domain.Sales;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Analytics;
using Xunit;
using K = Teikem.Domain.Sales.SalesRouteCodes;

namespace Teikem.Tests;

/// <summary>
/// Lote 244 (Lote A del plan de rutas) — fuentes de datos de las rutas de venta en la base en memoria con dos compañías (datos INVENTADOS): SALES_ROUTE (titular, apoyos,
/// clientes por ruta efectiva y cuota de ruta vigente, sin filas de otra compañía), SALES_ROUTE_ASSIGNMENT (rango por ValidFrom, sin anuladas por omisión, la anulada
/// solo por id) y SALES_ORDER con la ruta y el vendedor de la COPIA (vacíos en el borrador aunque tenga elección).
/// </summary>
public sealed class SalesRouteDataSourceTests
{
    private const int Other = WmsFixture.OtherTenantId;
    private static readonly DateTime Now = new(2026, 11, 25, 16, 0, 0, DateTimeKind.Utc);
    private static readonly ITenantClock Clock = new TenantClock(LocalDay.DefaultZone, () => Now);

    [Fact]
    public async Task Sales_route_source_has_primary_supports_clients_and_current_quota_of_its_company_only()
    {
        await using var f = await WmsFixture.CreateAsync();
        await f.AddActiveMemberAsync(201, "Ana Titular");
        await f.AddActiveMemberAsync(202, "Beto Apoyo");
        await f.AddActiveMemberAsync(203, "Carla Apoyo");
        await f.AddSuspendedMemberAsync(204, "Dino Suspendido");
        var norte = await f.AddRouteAsync("Norte", sortOrder: 1);
        var sur = await f.AddRouteAsync("Sur", sortOrder: 2);
        var vieja = await f.AddRouteAsync("Vieja", isActive: false, sortOrder: 3);
        var ajena = await f.AddRouteAsync("Ajena", tenantId: Other);

        var d = new DateOnly(2026, 10, 1);
        await f.AddAssignmentAsync(norte.SalesRouteId, 201, true, d);
        await f.AddAssignmentAsync(norte.SalesRouteId, 202, false, d);
        await f.AddAssignmentAsync(norte.SalesRouteId, 203, false, d);
        await f.AddAssignmentAsync(norte.SalesRouteId, 204, false, d, validTo: new DateOnly(2026, 10, 20));   // apoyo cerrado: no cuenta
        await f.AddAssignmentAsync(norte.SalesRouteId, 202, true, d.AddDays(-30), isActive: false);           // anulada: no cuenta
        await f.AddAssignmentAsync(sur.SalesRouteId, 204, true, d);                                            // titular suspendido

        // Clientes: dos con ruta propia Norte, uno heredado por su representante (Norte), uno inactivo y uno de Sur
        var rep = await f.AddRepAsync("Rep Norte", norte.SalesRouteId, null);
        var c1 = await f.AddClientAsync("C1");
        var c2 = await f.AddClientAsync("C2");
        var c3 = await f.AddClientAsync("C3");
        var c4 = await f.AddClientAsync("C4");
        var c5 = await f.AddClientAsync("C5");
        await f.SetClientSalesAsync(c1.ClientId, routeId: norte.SalesRouteId);
        await f.SetClientSalesAsync(c2.ClientId, routeId: norte.SalesRouteId, repId: rep.SalesRepId);
        await f.SetClientSalesAsync(c3.ClientId, repId: rep.SalesRepId);
        await f.SetClientSalesAsync(c4.ClientId, routeId: norte.SalesRouteId);
        await f.SetClientSalesAsync(c5.ClientId, routeId: sur.SalesRouteId);
        var inactive = await f.Db.Clients.AsTracking().SingleAsync(c => c.ClientId == c4.ClientId);
        inactive.IsActive = false;

        // Cuotas de ruta: Norte la hereda de octubre en noviembre; la de diciembre (futura) y la quitada no cuentan; una de representante no es de ruta
        f.Db.Set<SalesQuota>().AddRange(
            new SalesQuota { TenantId = WmsFixture.TenantId, SalesRouteId = norte.SalesRouteId, Month = new DateOnly(2026, 9, 1), Amount = 900m },
            new SalesQuota { TenantId = WmsFixture.TenantId, SalesRouteId = norte.SalesRouteId, Month = new DateOnly(2026, 10, 1), Amount = 1500m },
            new SalesQuota { TenantId = WmsFixture.TenantId, SalesRouteId = norte.SalesRouteId, Month = new DateOnly(2026, 11, 1), Amount = 7777m, IsActive = false },
            new SalesQuota { TenantId = WmsFixture.TenantId, SalesRouteId = norte.SalesRouteId, Month = new DateOnly(2026, 12, 1), Amount = 9999m },
            new SalesQuota { TenantId = WmsFixture.TenantId, SalesRepId = rep.SalesRepId, Month = new DateOnly(2026, 11, 1), Amount = 300m },
            new SalesQuota { TenantId = Other, SalesRouteId = ajena.SalesRouteId, Month = new DateOnly(2026, 11, 1), Amount = 50m });
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();

        var source = new SalesRouteDataSource(f.Db, f.Tenant, Clock);
        Assert.Equal((K.RouteDataSourceKey, (string?)null, ModuleKeys.SalesOrders), (source.Key, source.DateField, source.TenantModule));
        var rows = (await source.LoadAsync(new DataQuery(), default)).ToDictionary(r => (int)r["Id"]!);
        Assert.Equal(new[] { norte.SalesRouteId, sur.SalesRouteId, vieja.SalesRouteId }, rows.Keys.OrderBy(k => k).ToArray());   // sin la de otra compañía

        var n = rows[norte.SalesRouteId];
        Assert.Equal(("Norte", true, "Ana Titular", true, true), ((string)n["Name"]!, (bool)n["IsActive"]!, (string?)n["PrimaryName"], (bool)n["HasPrimary"]!, (bool)n["PrimaryIsActiveMember"]!));
        Assert.Equal((2, "Beto Apoyo, Carla Apoyo", 3, (decimal?)1500m), ((int)n["SupportCount"]!, (string?)n["SupportNames"], (int)n["ClientCount"]!, (decimal?)n["CurrentQuota"]));

        var s = rows[sur.SalesRouteId];
        Assert.Equal(("Dino Suspendido", true, false, 0, 1, (decimal?)null),
            ((string?)s["PrimaryName"], (bool)s["HasPrimary"]!, (bool)s["PrimaryIsActiveMember"]!, (int)s["SupportCount"]!, (int)s["ClientCount"]!, (decimal?)s["CurrentQuota"]));

        var v = rows[vieja.SalesRouteId];
        Assert.Equal((false, false, (string?)null, 0, (string?)null), ((bool)v["IsActive"]!, (bool)v["HasPrimary"]!, (string?)v["PrimaryName"], (int)v["ClientCount"]!, (string?)v["SupportNames"]));

        // Por ids (resolver la relación desde las personas de la ruta)
        var byId = await source.LoadAsync(new DataQuery { Ids = new[] { sur.SalesRouteId, ajena.SalesRouteId } }, default);
        Assert.Equal(new[] { sur.SalesRouteId }, byId.Select(r => (int)r["Id"]!).ToArray());
    }

    [Fact]
    public async Task Assignment_source_filters_by_valid_from_and_leaves_voided_out_by_default()
    {
        await using var f = await WmsFixture.CreateAsync();
        await f.AddActiveMemberAsync(301, "Ana");
        await f.AddSuspendedMemberAsync(302, "Beto");
        await f.AddActiveMemberAsync(303, "Otra", tenantId: Other);
        var norte = await f.AddRouteAsync("Norte");
        var ajena = await f.AddRouteAsync("Ajena", tenantId: Other);

        var sep = await f.AddAssignmentAsync(norte.SalesRouteId, 301, true, new DateOnly(2026, 9, 1), validTo: new DateOnly(2026, 10, 5));
        var oct = await f.AddAssignmentAsync(norte.SalesRouteId, 302, true, new DateOnly(2026, 10, 5));
        var sup = await f.AddAssignmentAsync(norte.SalesRouteId, 301, false, new DateOnly(2026, 10, 10));
        var voided = await f.AddAssignmentAsync(norte.SalesRouteId, 301, false, new DateOnly(2026, 10, 12), isActive: false);
        await f.AddAssignmentAsync(ajena.SalesRouteId, 303, true, new DateOnly(2026, 10, 1), tenantId: Other);

        var source = new SalesRouteAssignmentDataSource(f.Db, f.Tenant);
        Assert.Equal((K.AssignmentDataSourceKey, "ValidFrom"), (source.Key, source.DateField));
        Assert.Contains(source.Relations, r => r.TargetSourceKey == K.RouteDataSourceKey && r.LocalField == "RouteId");

        var all = (await source.LoadAsync(new DataQuery(), default)).ToDictionary(r => (int)r["Id"]!);
        Assert.Equal(new[] { sep.SalesRouteAssignmentId, oct.SalesRouteAssignmentId, sup.SalesRouteAssignmentId }, all.Keys.OrderBy(k => k).ToArray());

        var o = all[oct.SalesRouteAssignmentId];
        Assert.Equal(("Norte", "Beto", K.RolePrimary, "Titular", true, false, false),
            ((string?)o["RouteName"], (string?)o["UserName"], (string)o["Role"]!, (string)o["RoleLabel"]!, (bool)o["IsCurrent"]!, (bool)o["IsVoided"]!, (bool)o["UserIsActiveMember"]!));
        var closed = all[sep.SalesRouteAssignmentId];
        Assert.Equal((false, (DateOnly?)new DateOnly(2026, 10, 5), true), ((bool)closed["IsCurrent"]!, (DateOnly?)closed["ValidTo"], (bool)closed["UserIsActiveMember"]!));
        Assert.Equal((K.RoleSupport, "Apoyo"), ((string)all[sup.SalesRouteAssignmentId]["Role"]!, (string)all[sup.SalesRouteAssignmentId]["RoleLabel"]!));

        // Rango por ValidFrom en días de la compañía (desde inclusivo, hasta exclusivo)
        var octOnly = await source.LoadAsync(new DataQuery { FromDay = new DateOnly(2026, 10, 1), ToDayExclusive = new DateOnly(2026, 10, 10) }, default);
        Assert.Equal(new[] { oct.SalesRouteAssignmentId }, octOnly.Select(r => (int)r["Id"]!).ToArray());

        // La anulada solo sale al pedirla por id
        var v = Assert.Single(await source.LoadAsync(new DataQuery { Ids = new[] { voided.SalesRouteAssignmentId } }, default));
        Assert.Equal((true, false), ((bool)v["IsVoided"]!, (bool)v["IsCurrent"]!));
    }

    [Fact]
    public async Task Sales_order_source_exposes_route_and_seller_from_the_copy_only()
    {
        await using var f = await WmsFixture.CreateAsync();
        await f.AddActiveMemberAsync(401, "Ana Vendedora");
        var norte = await f.AddRouteAsync("Norte");
        var w = await f.AddWarehouseAsync("WH1");
        var client = await f.AddClientAsync("C1");

        var copied = await OrderAsync(f, client.ClientId, w.WarehouseId, norte.SalesRouteId, 401, Now);
        var draft = await OrderAsync(f, client.ClientId, w.WarehouseId, null, 401, null);       // borrador con elección: no es la copia
        var oldOne = await OrderAsync(f, client.ClientId, w.WarehouseId, null, null, null);      // solicitada antes del lote
        var noSeller = await OrderAsync(f, client.ClientId, w.WarehouseId, norte.SalesRouteId, null, Now);   // copia sin vendedor

        var source = new SalesOrderDataSource(f.Db, f.Lookups, f.Tenant, Clock);
        foreach (var key in new[] { "SalesRouteId", "SalesRouteName", "SellerUserId", "SellerName" })
            Assert.Contains(source.Fields, x => x.Key == key);
        var rows = (await source.LoadAsync(new DataQuery(), default)).ToDictionary(r => (int)r["Id"]!);

        var c = rows[copied];
        Assert.Equal(((int?)norte.SalesRouteId, "Norte", (int?)401, "Ana Vendedora"), ((int?)c["SalesRouteId"], (string?)c["SalesRouteName"], (int?)c["SellerUserId"], (string?)c["SellerName"]));
        foreach (var id in new[] { draft, oldOne })
            Assert.Equal(((object?)null, (object?)null, (object?)null, (object?)null), (rows[id]["SalesRouteId"], rows[id]["SalesRouteName"], rows[id]["SellerUserId"], rows[id]["SellerName"]));
        Assert.Equal(("Norte", (string?)null), ((string?)rows[noSeller]["SalesRouteName"], (string?)rows[noSeller]["SellerName"]));
    }

    private static async Task<int> OrderAsync(WmsFixture f, int clientId, int warehouseId, int? routeId, int? seller, DateTime? copyAt)
    {
        var o = new SalesOrder
        {
            PublicId = Guid.NewGuid(), TenantId = WmsFixture.TenantId, Number = "OV-" + Guid.NewGuid().ToString("N")[..6], WarehouseId = warehouseId, ClientId = clientId,
            RequestedDate = new DateOnly(2026, 11, 20), StatusCodeId = 1, CreatedAtUtc = Now, SalesRouteId = routeId, SellerUserId = seller, SalesCopyAtUtc = copyAt,
        };
        f.Db.SalesOrders.Add(o);
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        return o.SalesOrderId;
    }
}
