using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Catalogs;
using Teikem.Domain.Clients;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 121 — la lista de TODAS las personas de portal de la compañía para su administrador (regla del dueño: portal, consignatarios y staff los controlan el administrador de la compañía y el de la plataforma): cada una con su
/// tipo (CLIENT o CONSIGNEE), su cliente y, si es un consignatario, cuál; las dadas de baja no salen salvo que se pidan; nunca las de otra compañía; solo lectura.
/// </summary>
public class PortalUserDirectoryTests
{
    private static PortalUser Person(WmsFixture f, int clientId, string email, string status, int? locationId = null, bool active = true)
        => new() { TenantId = WmsFixture.TenantId, ClientId = clientId, UserId = null, LocationId = locationId, RoleLookupId = locationId is null ? 970 : 971, Email = email, FullName = email.Split('@')[0], StatusCodeId = f.StatusId(StatusDomains.PortalUserStatus, status), IsActive = active };

    private static async Task<(WmsFixture F, int C1, int C2, int Loc)> WorldAsync()
    {
        var f = await WmsFixture.CreateAsync();
        var c1 = await f.AddClientAsync("C1");
        var c2 = await f.AddClientAsync("C2");
        f.Db.LookupCodes.AddRange(
            new LookupCode { LookupCodeId = 970, Entity = LookupDomains.PortalRole, InternalCode = PortalRoles.ClientAdmin, LabelJson = "{\"es\":\"Admin cliente\"}", IsActive = true },
            new LookupCode { LookupCodeId = 971, Entity = LookupDomains.PortalRole, InternalCode = PortalRoles.Consignee, LabelJson = "{\"es\":\"Consignatario\"}", IsActive = true });
        var loc = new Location { TenantId = WmsFixture.TenantId, ClientId = c1.ClientId, Name = "Colmado Sur", Line1 = "Calle 5", City = "Ponce", IsActive = true };
        f.Db.Set<Location>().Add(loc);
        await f.Db.SaveChangesAsync();
        f.Db.Set<PortalUser>().AddRange(
            Person(f, c1.ClientId, "ana@c1.co", PortalUserStatuses.Active),
            Person(f, c1.ClientId, "inv@c1.co", PortalUserStatuses.Invited),
            Person(f, c1.ClientId, "tienda@c1.co", PortalUserStatuses.Active, loc.LocationId),
            Person(f, c1.ClientId, "baja@c1.co", PortalUserStatuses.Disabled),
            Person(f, c2.ClientId, "sam@c2.co", PortalUserStatuses.Suspended));
        f.Db.Set<PortalUser>().Add(new PortalUser { TenantId = WmsFixture.OtherTenantId, ClientId = 999, RoleLookupId = 970, Email = "otra@otra.co", StatusCodeId = f.StatusId(StatusDomains.PortalUserStatus, PortalUserStatuses.Active), IsActive = true });
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        return (f, c1.ClientId, c2.ClientId, loc.LocationId);
    }

    private static PortalUserDirectoryService Svc(WmsFixture f) => new(f.Db, f.Tenant);

    [Fact]
    public async Task It_lists_every_person_of_every_client_and_consignee_of_the_company_with_their_kind()
    {
        var (f, _, _, _) = await WorldAsync();
        await using var _f = f;
        var all = await Svc(f).ListAsync(null, false, default);

        Assert.Equal(new[] { "ana@c1.co", "inv@c1.co", "sam@c2.co", "tienda@c1.co" }, all.Select(a => a.Email).OrderBy(e => e).ToArray());   // sin la dada de baja y sin la de otra compañía
        var shop = all.Single(a => a.Email == "tienda@c1.co");
        Assert.Equal((PortalUserDirectoryService.KindConsignee, "Colmado Sur", "C1"), (shop.Kind, shop.ConsigneeName, shop.ClientCode));
        var ana = all.Single(a => a.Email == "ana@c1.co");
        Assert.Equal((PortalUserDirectoryService.KindClient, (string?)null), (ana.Kind, ana.ConsigneeName));
        Assert.NotEqual(Guid.Empty, ana.ClientPublicId);                                                                                   // con lo que las acciones por cliente necesitan
        Assert.Equal("SUSPENDED", all.Single(a => a.Email == "sam@c2.co").Status);
    }

    [Fact]
    public async Task The_kind_filter_splits_clients_from_consignees_and_removed_people_only_appear_when_asked()
    {
        var (f, _, _, _) = await WorldAsync();
        await using var _f = f;
        Assert.Equal(new[] { "tienda@c1.co" }, (await Svc(f).ListAsync("consignee", false, default)).Select(a => a.Email).ToArray());   // sin importar mayúsculas
        Assert.Equal(new[] { "ana@c1.co", "inv@c1.co", "sam@c2.co" }, (await Svc(f).ListAsync("CLIENT", false, default)).Select(a => a.Email).OrderBy(e => e).ToArray());
        var withRemoved = await Svc(f).ListAsync("CLIENT", true, default);
        Assert.Contains(withRemoved, a => a.Email == "baja@c1.co" && a.Status == "DISABLED");
        var bad = await Assert.ThrowsAsync<ValidationException>(() => Svc(f).ListAsync("STAFF", false, default));
        Assert.Equal(PortalUserDirectoryService.InvalidKindMessage, bad.Message);
    }

    [Fact]
    public void The_service_only_reads()
        => Assert.Equal(new[] { "ListAsync" }, typeof(PortalUserDirectoryService).GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly).Select(m => m.Name).ToArray());
}
