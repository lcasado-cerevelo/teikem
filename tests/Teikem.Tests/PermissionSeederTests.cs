using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Teikem.Domain.Catalogs;
using Teikem.Domain.Constants;
using Teikem.Domain.Security;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Seeding;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 222 (P2 del pedido de vendedores): los permisos nuevos sales.approve_client, clients.create_basic y pulse.sales y los roles de fábrica «Supervisor de ventas» y «Oficina de
/// ventas». El sembrador real los agrega a las plantillas y a los roles YA clonados de cada compañía (TenantAdmin los tres; Ventas el alta mínima y el panel), y la sección
/// 2026-11-07 de logistica-db-update.sql los propaga también (por si el SQL corre antes que el sembrador). Solo agrega.
/// </summary>
public sealed class PermissionSeederTests
{
    private static readonly string[] News = { PermissionCatalog.SalesApproveClient, PermissionCatalog.ClientsCreateBasic, PermissionCatalog.PulseSales };

    [Fact]
    public void The_new_codes_are_in_the_catalog_with_their_categories_display_order_and_screens()
    {
        Assert.Equal("BILLING", Assert.Single(PermissionCatalog.All, p => p.Code == "sales.approve_client").Category);
        Assert.Equal("CLIENTS", Assert.Single(PermissionCatalog.All, p => p.Code == "clients.create_basic").Category);
        Assert.Equal("PULSE", Assert.Single(PermissionCatalog.All, p => p.Code == "pulse.sales").Category);
        foreach (var code in News) Assert.Contains(code, PermissionDisplayOrder.Codes);
        // quien da de alta o aprueba entra a la lista de clientes
        var basic = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { PermissionCatalog.ClientsCreateBasic };
        PermissionCatalog.ExpandImplied(basic);
        Assert.Contains(PermissionCatalog.ClientsRead, basic);
        var approve = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { PermissionCatalog.SalesApproveClient };
        PermissionCatalog.ExpandImplied(approve);
        Assert.Contains(PermissionCatalog.ClientsRead, approve);
    }

    [Fact]
    public void The_factory_roles_carry_what_the_plan_says()
    {
        var t = PermissionCatalog.RoleTemplates;
        foreach (var code in new[] { PermissionCatalog.SalesPricelist, PermissionCatalog.SalesApproveClient, PermissionCatalog.SalesView, PermissionCatalog.ClientsRead, PermissionCatalog.ClientsUpdate })
            Assert.Contains(code, t["SalesSupervisor"]);
        foreach (var code in new[] { PermissionCatalog.SalesView, PermissionCatalog.SalesCreate })
            Assert.Contains(code, t["SalesOffice"]);
        Assert.Contains(PermissionCatalog.ClientsCreateBasic, t["Sales"]);
        Assert.DoesNotContain(PermissionCatalog.ClientsCreate, t["Sales"]);   // el vendedor no hace el alta completa
        Assert.DoesNotContain(PermissionCatalog.SalesApproveClient, t["Sales"]);   // ni aprueba a sus propios clientes
        foreach (var code in News) Assert.Contains(code, t["TenantAdmin"]);
        Assert.Equal(("Supervisor de ventas", "Sales supervisor"), PermissionCatalog.RoleTemplateLabels["SalesSupervisor"]);
        Assert.Equal(("Oficina de ventas", "Sales office"), PermissionCatalog.RoleTemplateLabels["SalesOffice"]);
        Assert.All(t.Keys, k => Assert.True(PermissionCatalog.RoleTemplateLabels.ContainsKey(k), k));
    }

    private static Role MakeRole(int? tenantId, string name, IEnumerable<string> codes, IReadOnlyList<Permission> perms)
    {
        var role = new Role { TenantId = tenantId, Name = name, DescriptionJson = "{}", IsSystem = tenantId is null, IsActive = true };
        foreach (var code in codes.Distinct()) role.Permissions.Add(new RolePermission { PermissionId = perms.Single(p => p.Code == code).PermissionId });
        return role;
    }

    [Fact]
    public async Task The_real_seeder_gives_the_new_codes_to_the_cloned_roles_and_creates_the_new_factory_roles()
    {
        var tenant = new TenantContext { TenantId = null, UserId = null };
        var options = new DbContextOptionsBuilder<TeikemDbContext>()
            .UseInMemoryDatabase("l222-seeder-" + Guid.NewGuid(), b => b.EnableNullChecks(false))
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        await using var db = new TeikemDbContext(options, tenant);
        var lookups = new TripTestLookups();
        var categories = PermissionCatalog.All.Select(p => p.Category).Distinct().Select((c, n) => new LookupCode
        {
            LookupCodeId = 100 + n, Entity = LookupDomains.PermissionCategory, InternalCode = c, LabelJson = "{}", IsActive = true,
        }).ToList();
        db.LookupCodes.AddRange(categories);
        lookups.Load(categories);

        // la versión anterior de la plataforma: sin los tres códigos y sin los roles de ventas nuevos
        var old = PermissionCatalog.All.Where(p => !News.Contains(p.Code))
            .Select(p => new Permission { Code = p.Code, CategoryLookupId = categories.First(c => c.InternalCode == p.Category).LookupCodeId, LabelJson = "{}" }).ToList();
        db.Permissions.AddRange(old);
        await db.SaveChangesAsync();
        foreach (var name in new[] { "TenantAdmin", "Sales", "Dispatcher", "Billing" })
            db.AppRoles.Add(MakeRole(null, name, PermissionCatalog.RoleTemplates[name].Where(c => !News.Contains(c)), old));
        foreach (var companyId in new[] { 1, 2 })
            foreach (var name in new[] { "TenantAdmin", "Sales", "Dispatcher" })
                db.AppRoles.Add(MakeRole(companyId, name, PermissionCatalog.RoleTemplates[name].Where(c => !News.Contains(c)), old));
        db.AppRoles.Add(MakeRole(1, "Vendedores de Solutions", new[] { PermissionCatalog.SalesView }, old));   // rol propio: no es plantilla
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        await new PermissionSeeder(db, tenant, lookups, NullLogger<PermissionSeeder>.Instance).SeedAsync(default);
        db.ChangeTracker.Clear();

        var codes = await db.Permissions.AsNoTracking().ToDictionaryAsync(p => p.PermissionId, p => p.Code);
        var roles = await db.AppRoles.IgnoreQueryFilters().AsNoTracking().Include(r => r.Permissions).ToListAsync();
        HashSet<string> Of(int? tenantId, string name) => roles.Single(r => r.TenantId == tenantId && r.Name == name).Permissions.Select(rp => codes[rp.PermissionId]).ToHashSet();
        foreach (var companyId in new[] { 1, 2 })
        {
            Assert.Superset(News.ToHashSet(), Of(companyId, "TenantAdmin"));
            Assert.Contains(PermissionCatalog.ClientsCreateBasic, Of(companyId, "Sales"));
            Assert.Contains(PermissionCatalog.PulseSales, Of(companyId, "Sales"));
            Assert.DoesNotContain(PermissionCatalog.SalesApproveClient, Of(companyId, "Sales"));
            Assert.Empty(News.Intersect(Of(companyId, "Dispatcher")));
        }
        Assert.Empty(News.Intersect(Of(1, "Vendedores de Solutions")));
        // las plantillas nuevas existen (las compañías nuevas las clonan; en Solutions las crea el administrador)
        Assert.Contains(PermissionCatalog.SalesApproveClient, Of(null, "SalesSupervisor"));
        Assert.Contains(PermissionCatalog.SalesPricelist, Of(null, "SalesSupervisor"));
        Assert.Contains(PermissionCatalog.SalesCreate, Of(null, "SalesOffice"));

        // una segunda corrida no duplica nada
        await new PermissionSeeder(db, tenant, lookups, NullLogger<PermissionSeeder>.Instance).SeedAsync(default);
        db.ChangeTracker.Clear();
        var sales1 = await db.AppRoles.IgnoreQueryFilters().AsNoTracking().Include(r => r.Permissions).SingleAsync(r => r.TenantId == 1 && r.Name == "Sales");
        Assert.Equal(1, sales1.Permissions.Count(rp => codes[rp.PermissionId] == PermissionCatalog.ClientsCreateBasic));
    }

    [Fact]
    public void The_update_script_inserts_the_codes_and_propagates_them_to_the_cloned_roles_once()
    {
        var sql = File.ReadAllText(Path.Combine(RepoRoot(), "Diseño", "logistica-db-update.sql"));
        var section = sql[sql.IndexOf("Lote 222 (P2 del pedido de vendedores)", StringComparison.Ordinal)..];
        foreach (var code in News) Assert.Contains($"WHERE Code = '{code}'", section);
        Assert.Contains("L222_SALES_PROSPECT", section);
        Assert.Contains("r.Name = 'TenantAdmin'", section);
        Assert.Contains("r.Name = 'Sales'", section);
        Assert.Contains("IF OBJECT_ID(N'dbo.ClientDocument', N'U') IS NULL", section);
        Assert.Contains("COL_LENGTH(N'dbo.SalesOrder', N'AwaitingClientApproval') IS NULL", section);
        Assert.DoesNotContain("DELETE FROM dbo.RolePermission", section);   // solo agrega
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "Diseño"))) dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("No se encontró la raíz del repositorio (carpeta Diseño).");
    }
}
