using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Teikem.Domain.Catalogs;
using Teikem.Domain.Constants;
using Teikem.Domain.Identity;
using Teikem.Domain.Security;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// 2026-10-01 (Luis): al crear un usuario se puede agregar también a otras compañías (las mismas credenciales, una membresía y los
/// mismos roles por nombre en cada una). Solo a compañías donde quien lo crea administra usuarios.
/// </summary>
public class UserMultiCompanyTests
{
    private const int Other = WmsFixture.OtherTenantId;
    private const int Here = WmsFixture.TenantId;

    private static async Task<WmsFixture> FixtureAsync(bool rolesInOther = true)
    {
        var f = await WmsFixture.CreateAsync(s =>
        {
            s.AddIdentityCore<ApplicationUser>().AddRoles<ApplicationRole>().AddEntityFrameworkStores<TeikemDbContext>().AddDefaultTokenProviders();
            s.AddSingleton<ISecurityEventWriter>(_ => new ReceivingNullSecurityEventWriter());
            s.AddSingleton(new JwtTokenService(Options.Create(new JwtOptions { SigningKey = new string('k', 64) })));
            s.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
            s.AddSingleton<IPasswordBreachChecker, NoOpPasswordBreachChecker>();
            s.AddSingleton<DeviceService>();
            s.AddSingleton<PinService>();
            s.AddSingleton<IOptions<OnboardingOptions>>(Options.Create(new OnboardingOptions { Enabled = false }));
            s.AddSingleton<ITransactionalEmailSender, NoEmailSender>();
            s.AddSingleton<AuthService>();
            s.AddSingleton<UserAdminService>();
        });
        f.Db.StatusCodes.Add(new StatusCode { StatusCodeId = 900, Entity = StatusDomains.MembershipStatus, InternalCode = MembershipStatuses.Active, LabelJson = "{\"es\":\"Activo\"}", IsActive = true });
        f.Db.LookupCodes.Add(new LookupCode { LookupCodeId = 960, Entity = LookupDomains.UserKind, InternalCode = UserKinds.Internal, LabelJson = "{\"es\":\"Interno\"}", IsActive = true });
        // Quien crea el usuario: miembro activo de las dos compañías.
        var me = new ApplicationUser { Id = 1, UserName = "yo@t.local", Email = "yo@t.local", NormalizedEmail = "YO@T.LOCAL", FullName = "Yo", SecurityStamp = "s1", IsActive = true };
        f.Db.Users.Add(me);
        f.Db.AppRoles.Add(new Role { TenantId = Here, Name = "Operador de almacén", IsActive = true });
        if (rolesInOther) f.Db.AppRoles.Add(new Role { TenantId = Other, Name = "Operador de almacén", IsActive = true });
        await f.Db.SaveChangesAsync();
        f.Db.UserTenants.AddRange(
            new UserTenant { UserId = 1, TenantId = Here, StatusCodeId = 900, IsDefault = true },
            new UserTenant { UserId = 1, TenantId = Other, StatusCodeId = 900 });
        await f.Db.SaveChangesAsync();
        f.Lookups.Load(f.Db.LookupCodes.IgnoreQueryFilters().AsNoTracking().ToList());
        f.Db.ChangeTracker.Clear();
        return f;
    }

    private static UserCreateRequest Req(string email, IList<int>? also, params string[] roles)
        => new(email, "Nueva Persona", "Clave-Temporal-2026", roles, UserKinds.Internal, also);

    [Fact]
    public async Task Platform_admin_can_assign_to_every_other_active_company()
    {
        await using var f = await FixtureAsync();
        var list = await f.Get<UserAdminService>().GetAssignableCompaniesAsync(default);
        Assert.Equal([Other], list.Select(c => c.TenantId));
    }

    [Fact]
    public async Task A_company_admin_only_sees_companies_where_he_is_active_member_with_admin_users()
    {
        await using var f = await FixtureAsync();
        f.SetPermissions(PermissionCatalog.AdminUsers);                       // aquí sí
        Assert.Empty(await f.Get<UserAdminService>().GetAssignableCompaniesAsync(default));   // en la otra, sin admin.users
        f.Get<IMemoryCache>().Set($"perms:1:{Other}", new HashSet<string>([PermissionCatalog.AdminUsers], StringComparer.OrdinalIgnoreCase));
        Assert.Equal([Other], (await f.Get<UserAdminService>().GetAssignableCompaniesAsync(default)).Select(c => c.TenantId));
    }

    [Fact]
    public async Task Creating_with_extra_companies_adds_one_membership_and_the_same_roles_by_name_in_each()
    {
        await using var f = await FixtureAsync();
        var r = await f.Get<UserAdminService>().CreateUserCoreAsync(Req("nuevo@t.local", [Other], "Operador de almacén"), default);

        Assert.Equal(["Otro tenant"], r.AlsoAddedTo);
        Assert.Empty(r.AlreadyMemberOf!);
        f.Db.ChangeTracker.Clear();
        var uid = await f.Db.Users.Where(u => u.NormalizedEmail == "NUEVO@T.LOCAL").Select(u => u.Id).SingleAsync();
        var memberships = await f.Db.UserTenants.IgnoreQueryFilters().Where(m => m.UserId == uid).Select(m => new { m.TenantId, m.IsDefault }).ToListAsync();
        Assert.Equal(2, memberships.Count);
        Assert.True(memberships.Single(m => m.TenantId == Here).IsDefault);
        Assert.False(memberships.Single(m => m.TenantId == Other).IsDefault);
        var roleTenants = await f.Db.AppUserRoles.IgnoreQueryFilters().Where(ur => ur.UserId == uid).Select(ur => ur.TenantId).ToListAsync();
        Assert.Equivalent(new[] { Here, Other }, roleTenants);
    }

    [Fact]
    public async Task A_missing_role_in_an_extra_company_fails_before_creating_anything()
    {
        await using var f = await FixtureAsync(rolesInOther: false);
        var ex = await Assert.ThrowsAsync<ValidationException>(() => f.Get<UserAdminService>().CreateUserCoreAsync(Req("nuevo@t.local", [Other], "Operador de almacén"), default));
        Assert.Equal("En Otro tenant no existen los roles: Operador de almacén.", ex.Message);
        f.Db.ChangeTracker.Clear();
        Assert.False(await f.Db.Users.AnyAsync(u => u.NormalizedEmail == "NUEVO@T.LOCAL"));
    }

    [Fact]
    public async Task A_company_the_creator_cannot_administer_is_rejected_before_creating_anything()
    {
        await using var f = await FixtureAsync();
        f.SetPermissions(PermissionCatalog.AdminUsers);   // sin admin.users en la otra compañía
        var ex = await Assert.ThrowsAsync<ForbiddenException>(() => f.Get<UserAdminService>().CreateUserCoreAsync(Req("nuevo@t.local", [Other], "Operador de almacén"), default));
        Assert.Equal(UserAdminService.CompanyNotAssignableMessage, ex.Message);
        f.Db.ChangeTracker.Clear();
        Assert.False(await f.Db.Users.AnyAsync(u => u.NormalizedEmail == "NUEVO@T.LOCAL"));
    }

    [Fact]
    public async Task If_the_person_already_belongs_to_the_extra_company_it_is_left_untouched()
    {
        await using var f = await FixtureAsync();
        var users = f.Get<UserAdminService>();
        await users.CreateUserCoreAsync(Req("nuevo@t.local", [Other], "Operador de almacén"), default);
        f.Db.ChangeTracker.Clear();
        // Mismo correo otra vez desde la otra compañía hacia la primera: ya es miembro de ambas → "ya pertenece" (409) en la activa.
        await Assert.ThrowsAsync<ConflictException>(() => users.CreateUserCoreAsync(Req("nuevo@t.local", [Other], "Operador de almacén"), default));
    }
}
