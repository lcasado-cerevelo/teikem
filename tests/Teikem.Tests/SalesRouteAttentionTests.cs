using Teikem.Domain.Common;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Services;
using Xunit;
using K = Teikem.Domain.Sales.SalesRouteCodes;

namespace Teikem.Tests;

/// <summary>
/// Lote 244 (Lote A del plan de rutas) — «Necesita tu atención»: SALES_ROUTE_NO_PRIMARY en la base en memoria con dos compañías (datos INVENTADOS). Sale una fila por ruta
/// activa sin titular vigente (NO_PRIMARY) o con el titular que ya no es persona activa (INACTIVE_PRIMARY); no sale para una ruta inactiva, una con titular activo ni una
/// de otra compañía; tono danger si la ruta tiene clientes activos (propios o heredados del representante); exige sales.analysis.manage y el módulo SALES_ORDERS.
/// </summary>
public sealed class SalesRouteAttentionTests
{
    private const int Other = WmsFixture.OtherTenantId;
    private static readonly DateTime Now = new(2026, 11, 25, 16, 0, 0, DateTimeKind.Utc);
    private static readonly ITenantClock Clock = new TenantClock(LocalDay.DefaultZone, () => Now);
    private static AttentionScope Scope => new(WmsFixture.TenantId, 1, "es", new HashSet<string> { ModuleKeys.SalesOrders });

    [Fact]
    public async Task Provider_gate_is_sales_analysis_manage_with_the_sales_orders_module()
    {
        await using var f = await WmsFixture.CreateAsync();
        var p = new SalesRouteNoPrimaryAttentionProvider(f.Db, Clock);
        Assert.Equal((K.NoPrimaryAttentionCode, PermissionCatalog.SalesAnalysisManage, ModuleKeys.SalesOrders, BusinessModules.Accounting),
            (p.Code, p.RequiredPermission, p.TenantModule, p.BusinessModule));
        var empty = await p.ReadAsync(Scope, 5, default);
        Assert.Equal((0, 0), (empty.Total, empty.Items.Count));
        Assert.Null(empty.Group);
    }

    [Fact]
    public async Task Flags_active_routes_without_primary_or_with_an_inactive_one()
    {
        await using var f = await WmsFixture.CreateAsync();
        await f.AddActiveMemberAsync(501, "Ana Titular");
        await f.AddSuspendedMemberAsync(502, "Beto Suspendido");
        await f.AddActiveMemberAsync(503, "Carla Apoyo");
        await f.AddActiveMemberAsync(509, "Otra", tenantId: Other);
        var sinTitular = await f.AddRouteAsync("Sin titular", sortOrder: 1);    // apoyo pero sin titular; tuvo uno hasta el 2026-11-10; con clientes → danger
        var suspendido = await f.AddRouteAsync("Suspendido", sortOrder: 2);     // titular suspendido, sin clientes → warn
        var completa = await f.AddRouteAsync("Completa", sortOrder: 3);         // titular activo → no sale
        var inactiva = await f.AddRouteAsync("Inactiva", isActive: false);      // inactiva sin titular → no sale
        var vacia = await f.AddRouteAsync("Vacía", sortOrder: 0);               // nunca tuvo titular, sin clientes → warn, sin fecha
        var ajena = await f.AddRouteAsync("Ajena", tenantId: Other);           // otra compañía → no sale

        var d = new DateOnly(2026, 10, 1);
        await f.AddAssignmentAsync(sinTitular.SalesRouteId, 501, true, d, validTo: new DateOnly(2026, 11, 10));
        await f.AddAssignmentAsync(sinTitular.SalesRouteId, 503, false, d);
        await f.AddAssignmentAsync(sinTitular.SalesRouteId, 503, true, new DateOnly(2026, 11, 12), isActive: false);   // anulada: no cuenta como titular
        await f.AddAssignmentAsync(suspendido.SalesRouteId, 502, true, d);
        await f.AddAssignmentAsync(completa.SalesRouteId, 501, true, d);

        // Clientes de «Sin titular»: uno propio y uno heredado de su representante
        var rep = await f.AddRepAsync("Rep", sinTitular.SalesRouteId, null);
        var c1 = await f.AddClientAsync("C1");
        var c2 = await f.AddClientAsync("C2");
        var c3 = await f.AddClientAsync("C3");
        await f.SetClientSalesAsync(c1.ClientId, routeId: sinTitular.SalesRouteId);
        await f.SetClientSalesAsync(c2.ClientId, repId: rep.SalesRepId);
        await f.SetClientSalesAsync(c3.ClientId, routeId: completa.SalesRouteId);
        var other = await f.AddClientAsync("X1", tenantId: Other);
        await f.SetClientSalesAsync(other.ClientId, routeId: ajena.SalesRouteId);

        var p = new SalesRouteNoPrimaryAttentionProvider(f.Db, Clock);
        var result = await p.ReadAsync(Scope, 10, default);
        Assert.Equal(3, result.Total);
        // Primero las que tienen clientes, luego por orden y nombre
        Assert.Equal(new[] { "Sin titular", "Vacía", "Suspendido" }, result.Items.Select(i => i.Params["name"]).ToArray());

        var a = result.Items[0];
        Assert.Equal(("danger", "NO_PRIMARY", "2", sinTitular.SalesRouteId.ToString()), (a.Tone, a.Params["reason"], a.Params["clients"], a.Params["routeId"]));
        Assert.Equal(Clock.StartOfDayUtc(new DateOnly(2026, 11, 10)), a.SinceUtc);
        Assert.Equal("/analytics/sales?tab=setup&sec=routes", $"{a.Route}?{string.Join("&", a.Query!.Select(kv => $"{kv.Key}={kv.Value}"))}");

        var b = result.Items[1];
        Assert.Equal(("warn", "NO_PRIMARY", "0"), (b.Tone, b.Params["reason"], b.Params["clients"]));
        Assert.Null(b.SinceUtc);

        var c = result.Items[2];
        Assert.Equal(("warn", "INACTIVE_PRIMARY", suspendido.SalesRouteId.ToString()), (c.Tone, c.Params["reason"], c.Params["routeId"]));
        Assert.DoesNotContain(result.Items, i => i.Params["name"] is "Completa" or "Inactiva" or "Ajena");
        Assert.Equal((K.NoPrimaryAttentionCode, 3), (result.Group!.Code, result.Group.Total));

        // take limita las filas, no el total
        var one = await p.ReadAsync(Scope, 1, default);
        Assert.Equal((3, 1), (one.Total, one.Items.Count));

        // Al ponerle titular activo a «Vacía» deja de salir
        await f.AddAssignmentAsync(vacia.SalesRouteId, 503, true, new DateOnly(2026, 11, 20));
        var after = await p.ReadAsync(Scope, 10, default);
        Assert.Equal(new[] { "Sin titular", "Suspendido" }, after.Items.Select(i => i.Params["name"]).ToArray());
        _ = inactiva;
    }
}
