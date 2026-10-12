using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Catalogs;
using Teikem.Domain.Constants;
using Teikem.Domain.Identity;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// 2026-10-12 — filtro Usuario del Kárdex (docs/kardex-filtro-usuario-diseno.md): UserIds + IncludeSystem en la consulta compartida (lista,
/// resumen y exportación) y las opciones del filtro (usuarios con movimientos, inactivos marcados, «Sistema» solo si hay movimientos sin usuario).
/// </summary>
public class KardexUserFilterTests
{
    private const int Ana = InventoryServiceFixture.UserId; // la del fixture: «Ana Operadora»
    private const int Beto = 2;
    private const int Carla = 3; // dada de baja, con historial
    private const int ClientA = 50;

    private sealed record World(InventoryServiceFixture F, Product P, Product PClient);

    /// <summary>
    /// Cuatro movimientos del producto propio: 1 de Ana (RECEIPT), 2 de Beto (ADJUSTMENT), 3 de Carla (ADJUSTMENT, inactiva), 4 sin usuario
    /// (RECEIPT, p. ej. un cierre automático); y uno de Beto sobre el producto del cliente A.
    /// </summary>
    private static async Task<World> SeedAsync(bool withSystem = true)
    {
        var f = await InventoryServiceFixture.CreateAsync();
        f.Db.Users.Add(new ApplicationUser { Id = Beto, UserName = "beto", Email = "beto@teikem.test", FullName = "Beto Supervisor" });
        f.Db.Users.Add(new ApplicationUser { Id = Carla, UserName = "carla", Email = "carla@teikem.test", FullName = "Carla Baja", IsActive = false });
        await f.Db.SaveChangesAsync();
        await f.AddClientAsync(ClientA, "Cliente A");
        var w = await f.AddWarehouseAsync("ALM-01");
        var z = await f.AddZoneAsync(w, "PCK", "PICKING");
        var bin = await f.AddBinAsync(z, "A-01");
        var p = await f.AddProductAsync("P1", "NONE");
        var pa = await f.AddProductAsync("PA", "NONE", ClientA);

        var t0 = new DateTime(2026, 9, 20, 14, 0, 0, DateTimeKind.Utc);
        await f.AddTxnAsync("RECEIPT", p, 10m, to: (w, bin), at: t0);                                // Ana (la del fixture)
        var byBeto = await f.AddTxnAsync("ADJUSTMENT", p, -1m, from: (w, bin), at: t0.AddHours(1), reason: "DAMAGE");
        var byCarla = await f.AddTxnAsync("ADJUSTMENT", p, 2m, to: (w, bin), at: t0.AddHours(2), reason: "FOUND");
        var bySystem = await f.AddTxnAsync("RECEIPT", p, 3m, to: (w, bin), at: t0.AddHours(3));
        var clientTxn = await f.AddTxnAsync("ADJUSTMENT", pa, 4m, to: (w, bin), at: t0.AddHours(4), reason: "FOUND");
        await SetUserAsync(f, byBeto, Beto);
        await SetUserAsync(f, byCarla, Carla);
        await SetUserAsync(f, clientTxn, Beto);
        await SetUserAsync(f, bySystem, withSystem ? null : Ana);
        return new World(f, p, pa);
    }

    /// <summary>Cambia quién hizo un movimiento ya sembrado (el fixture los siembra todos a nombre de Ana).</summary>
    private static async Task SetUserAsync(InventoryServiceFixture f, InventoryTransaction txn, int? userId)
    {
        var tracked = await f.Db.Set<InventoryTransaction>().AsTracking().SingleAsync(t => t.InventoryTransactionId == txn.InventoryTransactionId);
        tracked.CreatedBy = userId;
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
    }

    private static async Task<int[]> RowUsersAsync(InventoryServiceFixture f, KardexQuery q)
    {
        var page = await f.Get<InventoryReadService>().KardexAsync(q, InventoryScope.Any, default);
        return page.Items.Select(i => i.UserId ?? 0).OrderBy(x => x).ToArray();
    }

    // ---------------------------------------------------------------- el filtro

    [Fact]
    public async Task Without_user_filter_everything_comes()
    {
        var w = await SeedAsync();
        Assert.Equal(new[] { 0, Ana, Beto, Beto, Carla }, await RowUsersAsync(w.F, new KardexQuery()));
    }

    [Fact]
    public async Task One_user_and_several_users_filter_the_movements_they_made()
    {
        var w = await SeedAsync();
        Assert.Equal(new[] { Beto, Beto }, await RowUsersAsync(w.F, new KardexQuery(UserIds: new[] { Beto })));
        Assert.Equal(new[] { Ana, Carla }, await RowUsersAsync(w.F, new KardexQuery(UserIds: new[] { Ana, Carla })));
        // un usuario repetido no duplica filas
        Assert.Equal(new[] { Ana }, await RowUsersAsync(w.F, new KardexQuery(UserIds: new[] { Ana, Ana })));
    }

    [Fact]
    public async Task System_alone_and_system_plus_users()
    {
        var w = await SeedAsync();
        Assert.Equal(new[] { 0 }, await RowUsersAsync(w.F, new KardexQuery(IncludeSystem: true)));
        Assert.Equal(new[] { 0, Beto, Beto }, await RowUsersAsync(w.F, new KardexQuery(UserIds: new[] { Beto }, IncludeSystem: true)));
    }

    [Fact]
    public async Task A_user_that_does_not_exist_returns_nothing()
    {
        var w = await SeedAsync();
        Assert.Empty(await RowUsersAsync(w.F, new KardexQuery(UserIds: new[] { 9999 })));
    }

    [Fact]
    public async Task The_user_filter_combines_with_the_other_filters_and_the_summary_uses_the_same_query()
    {
        var w = await SeedAsync();
        var reads = w.F.Get<InventoryReadService>();
        var q = new KardexQuery(UserIds: new[] { Beto, Carla }, Types: new[] { "ADJUSTMENT" }, Reasons: new[] { "FOUND" });
        var page = await reads.KardexAsync(q, InventoryScope.Any, default);
        Assert.Equal(new[] { Beto, Carla }, page.Items.Select(i => i.UserId!.Value).OrderBy(x => x).ToArray()); // los dos FOUND (Carla sobre P1, Beto sobre PA)
        var summary = await reads.KardexSummaryAsync(q, InventoryScope.Any, default);
        Assert.Equal(page.Total, summary.Movements);
        // con un producto concreto se acota más
        var onlyP1 = await reads.KardexAsync(q with { ProductPublicIds = new[] { w.P.PublicId } }, InventoryScope.Any, default);
        Assert.Equal(Carla, Assert.Single(onlyP1.Items).UserId);
        // el resumen también respeta «Sistema»
        var sys = await reads.KardexSummaryAsync(new KardexQuery(IncludeSystem: true), InventoryScope.Any, default);
        Assert.Equal(1, sys.Movements);
    }

    // ---------------------------------------------------------------- las opciones

    [Fact]
    public async Task Options_are_system_first_then_the_users_with_movements_by_name_with_inactive_marked()
    {
        var w = await SeedAsync();
        var options = await w.F.Get<InventoryReadService>().KardexUsersAsync(InventoryScope.Any, default);
        Assert.Equal(new[] { "Sistema (sin usuario)", "Ana Operadora", "Beto Supervisor", "Carla Baja" }, options.Select(o => o.Name).ToArray());
        Assert.Equal(new bool?[] { true, false, false, false }, options.Select(o => (bool?)o.IsSystem).ToArray());
        Assert.Null(options[0].UserId);
        Assert.Equal(new int?[] { null, Ana, Beto, Carla }, options.Select(o => o.UserId).ToArray());
        Assert.Equal(new[] { true, true, true, false }, options.Select(o => o.IsActive).ToArray()); // Carla: dada de baja, con historial
    }

    [Fact]
    public async Task System_is_only_offered_when_there_are_movements_without_a_user_and_it_speaks_the_company_language()
    {
        var none = await SeedAsync(withSystem: false);
        var options = await none.F.Get<InventoryReadService>().KardexUsersAsync(InventoryScope.Any, default);
        Assert.DoesNotContain(options, o => o.IsSystem);
        Assert.Equal(new[] { "Ana Operadora", "Beto Supervisor", "Carla Baja" }, options.Select(o => o.Name).ToArray());

        var withSystem = await SeedAsync();
        withSystem.F.Tenant.Lang = "en";
        var en = await withSystem.F.Get<InventoryReadService>().KardexUsersAsync(InventoryScope.Any, default);
        Assert.Equal("System (no user)", en[0].Name);
    }

    [Fact]
    public async Task Users_without_movements_are_not_offered_and_the_scope_limits_the_options()
    {
        var w = await SeedAsync();
        w.F.Db.Users.Add(new ApplicationUser { Id = 4, UserName = "dora", Email = "dora@teikem.test", FullName = "Dora Sin Movimientos" });
        await w.F.Db.SaveChangesAsync();
        var reads = w.F.Get<InventoryReadService>();
        Assert.DoesNotContain(await reads.KardexUsersAsync(InventoryScope.Any, default), o => o.Name.StartsWith("Dora"));
        // un usuario con scope de dueño (portal) solo ve a quienes movieron productos de ese cliente
        var scoped = await reads.KardexUsersAsync(new InventoryScope(ClientA), default);
        Assert.Equal("Beto Supervisor", Assert.Single(scoped).Name);
    }

    // ---------------------------------------------------------------- traducción a SQL Server (sin BD)

    [Fact]
    public async Task The_user_filter_translates_to_sql_server()
    {
        using var db = TenantIsolationModelTests.CreateSqlServerModelContext();
        var lookups = new TripTestLookups();
        var reads = new InventoryReadService(db, new TenantContext { TenantId = 1, UserId = 1 }, lookups, TenantClock.Default);
        var (query, _) = await reads.BuildKardexQueryAsync(new KardexQuery(UserIds: new[] { 1, 2 }, IncludeSystem: true), InventoryScope.Any, default);
        var sql = query.ToQueryString();
        Assert.Contains("[CreatedBy] IS NULL", sql);
        Assert.Contains("[CreatedBy] IN (", sql);
    }
}
