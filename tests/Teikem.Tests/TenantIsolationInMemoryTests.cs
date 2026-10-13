using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Persistence;
using Xunit;
using static Teikem.Tests.TenantFilterHarness;

namespace Teikem.Tests;

/// <summary>
/// Lote 42 — NO FILTRACIÓN entre compañías con el proveedor InMemory (corre siempre, sin servidor): para TODAS las entidades con
/// filtro de tenant (enumeradas por reflexión del modelo), con la compañía A activa no se ve nada de la B en ninguna forma de consulta;
/// el bypass ve todo SOLO dentro de su bloque; IgnoreQueryFilters sigue ignorando; As(B) cambia de compañía y vuelve; y sin compañía
/// no se ve nada (falla cerrado; las globales de las entidades opcionales sí). El mismo arnés corre contra SQL Server real en
/// TenantIsolationSqlServerTests.
/// </summary>
public sealed class TenantIsolationInMemoryTests
{
    private static async Task<(TeikemDbContext Db, TenantContext Tenant)> CreateSeededAsync()
    {
        var tenant = new TenantContext { TenantId = TenantA, UserId = 1, IsAuthenticated = true };
        var options = new DbContextOptionsBuilder<TeikemDbContext>()
            .UseInMemoryDatabase("tenant-isolation-" + Guid.NewGuid(), b => b.EnableNullChecks(false))
            .ConfigureWarnings(w => w.Ignore(CoreEventId.PossibleIncorrectRequiredNavigationWithQueryFilterInteractionWarning))
            .Options;
        var db = new TeikemDbContext(options, tenant);
        await SeedAsync(db, tenant);
        return (db, tenant);
    }

    [Fact]
    public async Task Cobertura_todas_las_entidades_con_TenantId_estan_en_la_prueba()
    {
        var (db, _) = await CreateSeededAsync();
        var entities = TenantFilteredEntities(db.Model);
        // Toda entidad del dominio con propiedad TenantId y filtro global está en la lista (la reflexión es la fuente, no una lista a mano).
        var withTenantId = db.Model.GetEntityTypes().Where(e => !e.IsOwned() && e.FindProperty("TenantId") is not null && e.GetQueryFilter() is not null).ToList();
        Assert.Equal(withTenantId.Select(e => e.ClrType.Name).OrderBy(n => n, StringComparer.Ordinal), entities.Select(e => e.ClrType.Name));
        Assert.True(entities.Length >= 70, $"Solo {entities.Length} entidades con filtro de tenant");
        Assert.Contains(entities, e => e.ClrType.Name == "TransportOrder");
        Assert.Contains(entities, e => e.ClrType.Name == "LookupCode" && IsOptional(e));   // NULL = global
        Assert.All(entities, e => Assert.NotNull(e.GetQueryFilter()));
    }

    [Fact]
    public async Task Con_la_compania_A_no_se_ve_nada_de_la_B_en_ninguna_forma_de_consulta()
    {
        var (db, tenant) = await CreateSeededAsync();
        AssertOnlyTenant(db.Model, await RunAllShapesAsync(db), TenantA);
        using (tenant.As(TenantB))
            AssertOnlyTenant(db.Model, await RunAllShapesAsync(db), TenantB);
        // al salir de As(B) vuelve a ser A
        AssertOnlyTenant(db.Model, await RunAllShapesAsync(db), TenantA);
    }

    [Fact]
    public async Task El_bypass_ve_todo_solo_dentro_de_su_bloque_e_IgnoreQueryFilters_sigue_ignorando()
    {
        var (db, tenant) = await CreateSeededAsync();
        var entities = TenantFilteredEntities(db.Model);
        Dictionary<string, int> inside;
        using (tenant.BypassTenantFilter())
            inside = await CountAllAsync(db);
        var after = await CountAllAsync(db);
        var ignored = await CountAllAsync(db, ignoreFilters: true);
        foreach (var e in entities)
        {
            Assert.True(Total(e) == inside[e.ClrType.Name], $"{e.ClrType.Name}: con bypass {inside[e.ClrType.Name]}, total {Total(e)}");
            Assert.True(ExpectedVisible(e, TenantA) == after[e.ClrType.Name], $"{e.ClrType.Name}: al salir del bypass {after[e.ClrType.Name]}");
            Assert.True(Total(e) == ignored[e.ClrType.Name], $"{e.ClrType.Name}: IgnoreQueryFilters {ignored[e.ClrType.Name]}");
        }
        Assert.False(tenant.TenantFilterDisabled);
    }

    [Fact]
    public async Task Sin_compania_activa_falla_cerrado_solo_las_globales_de_las_opcionales()
    {
        var (db, tenant) = await CreateSeededAsync();
        tenant.TenantId = null;
        Assert.Equal(-1, db.QueryFilterTenantId);
        var counts = await CountAllAsync(db);
        foreach (var e in TenantFilteredEntities(db.Model))
            Assert.True(counts[e.ClrType.Name] == (IsOptional(e) ? 1 : 0), $"{e.ClrType.Name}: sin compañía vio {counts[e.ClrType.Name]}");
    }

    [Fact]
    public void QueryFilterTenantId_es_null_solo_con_el_bypass()
    {
        var tenant = new TenantContext { TenantId = 5 };
        using var db = new TeikemDbContext(new DbContextOptionsBuilder<TeikemDbContext>().UseInMemoryDatabase("qft-" + Guid.NewGuid()).Options, tenant);
        Assert.Equal(5, db.QueryFilterTenantId);
        using (tenant.BypassTenantFilter()) Assert.Null(db.QueryFilterTenantId);
        Assert.Equal(5, db.QueryFilterTenantId);
        tenant.TenantId = null;
        Assert.Equal(-1, db.QueryFilterTenantId);
        using (tenant.As(9)) Assert.Equal(9, db.QueryFilterTenantId);
        Assert.Equal(-1, db.QueryFilterTenantId);
    }
}
