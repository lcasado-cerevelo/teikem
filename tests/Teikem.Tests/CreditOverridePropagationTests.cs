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
/// `orders.credit_override` (Lote 3, ajuste C) y los roles ya clonados de cada compañía (cierra el pendiente P0.3 «propagación sin prueba»): el permiso entra a las
/// plantillas TenantAdmin y Billing; una compañía que ya tenía esos roles clonados de la versión anterior lo recibe cuando corre el PermissionSeeder (db-init / db-update),
/// y un rol propio de la compañía o con otro nombre (Dispatcher, almacén…) NO lo recibe. Solo agrega: lo que el rol ya tenía no se toca.
/// </summary>
public class CreditOverridePropagationTests
{
    private const string Credit = PermissionCatalog.OrdersCreditOverride;

    private static IReadOnlySet<string> Set(params string[] codes) => new HashSet<string>(codes, StringComparer.OrdinalIgnoreCase);

    [Theory]
    [InlineData("TenantAdmin", true)]
    [InlineData("Billing", true)]
    [InlineData("Dispatcher", false)]
    [InlineData("WarehouseOperator", false)]
    [InlineData("Driver", false)]
    [InlineData("ReadOnly", false)]
    [InlineData("Contador de la compañía", false)]   // rol propio: no es plantilla
    public void Only_the_templates_that_include_it_receive_it_as_a_new_code(string role, bool receives)
        => Assert.Equal(receives, PermissionCatalog.CodesToPropagate(role, Set(Credit), Set()).Contains(Credit));

    [Theory]
    [InlineData("TenantAdmin")]
    [InlineData("Billing")]
    public void A_role_that_already_has_it_is_left_alone(string role)
        => Assert.Empty(PermissionCatalog.CodesToPropagate(role, Set(Credit), Set(Credit)));

    private static Role MakeRole(int? tenantId, string name, IEnumerable<string> codes, IReadOnlyList<Permission> perms)
    {
        var role = new Role { TenantId = tenantId, Name = name, DescriptionJson = "{}", IsSystem = tenantId is null, IsActive = true };
        foreach (var code in codes) role.Permissions.Add(new RolePermission { PermissionId = perms.Single(p => p.Code == code).PermissionId });
        return role;
    }

    [Fact]
    public async Task The_real_seeder_adds_it_to_cloned_TenantAdmin_and_Billing_roles_of_every_company_and_to_no_other_role()
    {
        var tenant = new TenantContext { TenantId = null, UserId = null };
        var options = new DbContextOptionsBuilder<TeikemDbContext>()
            .UseInMemoryDatabase("credit-override-" + Guid.NewGuid(), b => b.EnableNullChecks(false))
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

        // La plataforma de la versión anterior: todos los permisos MENOS orders.credit_override, y las plantillas sin él.
        var old = PermissionCatalog.All.Where(p => p.Code != Credit)
            .Select(p => new Permission { Code = p.Code, CategoryLookupId = categories.First(c => c.InternalCode == p.Category).LookupCodeId, LabelJson = "{}" }).ToList();
        db.Permissions.AddRange(old);
        await db.SaveChangesAsync();
        foreach (var (name, templateCodes) in PermissionCatalog.RoleTemplates)
            db.AppRoles.Add(MakeRole(null, name, templateCodes.Where(c => c != Credit), old));
        // Roles ya clonados por dos compañías (más un rol propio de la compañía 1 con el mismo permiso base que Billing).
        foreach (var companyId in new[] { 1, 2 })
        {
            db.AppRoles.Add(MakeRole(companyId, "TenantAdmin", PermissionCatalog.RoleTemplates["TenantAdmin"].Where(c => c != Credit), old));
            db.AppRoles.Add(MakeRole(companyId, "Billing", PermissionCatalog.RoleTemplates["Billing"].Where(c => c != Credit), old));
            db.AppRoles.Add(MakeRole(companyId, "Dispatcher", PermissionCatalog.RoleTemplates["Dispatcher"].Where(c => c != Credit), old));
        }
        db.AppRoles.Add(MakeRole(1, "Contador de la compañía", new[] { PermissionCatalog.OrdersView }, old));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        await new PermissionSeeder(db, tenant, lookups, NullLogger<PermissionSeeder>.Instance).SeedAsync(default);
        db.ChangeTracker.Clear();

        var codes = await db.Permissions.AsNoTracking().ToDictionaryAsync(p => p.PermissionId, p => p.Code);
        var roles = await db.AppRoles.IgnoreQueryFilters().AsNoTracking().Include(r => r.Permissions).ToListAsync();
        bool Has(int? tenantId, string name) => roles.Single(r => r.TenantId == tenantId && r.Name == name).Permissions.Any(rp => codes[rp.PermissionId] == Credit);

        Assert.True(Has(null, "TenantAdmin"));
        Assert.True(Has(null, "Billing"));
        Assert.False(Has(null, "Dispatcher"));
        foreach (var companyId in new[] { 1, 2 })
        {
            Assert.True(Has(companyId, "TenantAdmin"), $"TenantAdmin de la compañía {companyId}");
            Assert.True(Has(companyId, "Billing"), $"Billing de la compañía {companyId}");
            Assert.False(Has(companyId, "Dispatcher"), $"Dispatcher de la compañía {companyId}");
        }
        Assert.False(Has(1, "Contador de la compañía"));

        // Una segunda corrida no duplica nada.
        await new PermissionSeeder(db, tenant, lookups, NullLogger<PermissionSeeder>.Instance).SeedAsync(default);
        db.ChangeTracker.Clear();
        var again = await db.AppRoles.IgnoreQueryFilters().AsNoTracking().Include(r => r.Permissions).Where(r => r.TenantId == 1 && r.Name == "Billing").SingleAsync();
        Assert.Equal(1, again.Permissions.Count(rp => codes[rp.PermissionId] == Credit));
    }
}
