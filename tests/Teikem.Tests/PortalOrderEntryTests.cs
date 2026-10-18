using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Catalogs;
using Teikem.Domain.Clients;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 105 — entrada de órdenes del cliente en su portal: solo los roles CLIENT_ADMIN y CLIENT_OPERATOR crean (CLIENT_READONLY → 403), el formulario ofrece solo los consignatarios y puntos de recogida del cliente
/// de la sesión, y las validaciones responden antes de tocar el servicio de órdenes (que es el mismo de la web de la compañía).
/// </summary>
public class PortalOrderEntryTests
{
    private const int ClientA = 601, ClientB = 602;
    private const int Admin = 61, Operator = 62, Reader = 63;

    private static async Task<(WmsFixture F, PortalOrderEntryService Svc)> SetupAsync()
    {
        var f = await WmsFixture.CreateAsync();
        var db = f.Db;
        db.LookupCodes.AddRange(
            new LookupCode { LookupCodeId = 960, Entity = LookupDomains.PortalRole, InternalCode = PortalRoles.ClientAdmin, LabelJson = "{\"es\":\"Admin\"}", IsActive = true },
            new LookupCode { LookupCodeId = 961, Entity = LookupDomains.PortalRole, InternalCode = PortalRoles.ClientOperator, LabelJson = "{\"es\":\"Operador\"}", IsActive = true },
            new LookupCode { LookupCodeId = 962, Entity = LookupDomains.PortalRole, InternalCode = PortalRoles.ClientReadOnly, LabelJson = "{\"es\":\"Lectura\"}", IsActive = true },
            new LookupCode { LookupCodeId = 970, Entity = LookupDomains.LocationType, InternalCode = LocationTypes.Delivery, LabelJson = "{\"es\":\"Entrega\"}", IsActive = true },
            new LookupCode { LookupCodeId = 971, Entity = LookupDomains.LocationType, InternalCode = LocationTypes.Pickup, LabelJson = "{\"es\":\"Recogida\"}", IsActive = true },
            new LookupCode { LookupCodeId = 972, Entity = LookupDomains.LocationType, InternalCode = LocationTypes.Billing, LabelJson = "{\"es\":\"Facturación\"}", IsActive = true });
        db.Clients.AddRange(
            new Client { ClientId = ClientA, PublicId = Guid.NewGuid(), TenantId = WmsFixture.TenantId, Code = "A", Name = "Cliente A", IsActive = true, StatusCodeId = 1 },
            new Client { ClientId = ClientB, PublicId = Guid.NewGuid(), TenantId = WmsFixture.TenantId, Code = "B", Name = "Cliente B", IsActive = true, StatusCodeId = 1 });
        int pu = 1;
        foreach (var (user, role) in new[] { (Admin, 960), (Operator, 961), (Reader, 962) })
            db.PortalUsers.Add(new PortalUser { PortalUserId = pu++, TenantId = WmsFixture.TenantId, ClientId = ClientA, UserId = user, Email = $"u{user}@x.co", RoleLookupId = role, StatusCodeId = 1, IsActive = true });
        Location L(int id, int? client, string name, int type, bool active = true) => new() { LocationId = id, PublicId = Guid.NewGuid(), TenantId = WmsFixture.TenantId, ClientId = client, Name = name, LocationTypeLookupId = type, Line1 = "Calle " + id, City = "Ponce", IsActive = active };
        db.Locations.AddRange(L(1, ClientA, "Tienda 1", 970), L(2, ClientA, "Tienda 2", 970), L(3, ClientA, "Tienda dada de baja", 970, false), L(4, ClientA, "Bodega A", 971), L(5, ClientA, "Facturación A", 972), L(6, ClientB, "Tienda de B", 970));
        await db.SaveChangesAsync();
        f.Lookups.Load(db.LookupCodes.IgnoreQueryFilters().AsNoTracking().ToList());
        db.ChangeTracker.Clear();
        return (f, new PortalOrderEntryService(db, f.Tenant, null!));
    }

    private static void AsPortal(WmsFixture f, int userId, int clientId)
    {
        f.Tenant.IsPlatformAdmin = false;
        f.Tenant.UserId = userId;
        f.Tenant.IsPortal = true;
        f.Tenant.PortalClientId = clientId;
        f.Tenant.ScopedClientIds = new[] { clientId };
        f.Db.ChangeTracker.Clear();
    }

    private static PortalOrderCreateRequest Req(Guid? consignee = null, PortalNewConsigneeRequest? fresh = null, IList<PortalPackageRequest>? packages = null, string? notes = null)
        => new(consignee, fresh, null, packages, null, null, null, notes);

    private static readonly PortalNewConsigneeRequest Fresh = new("Tienda Nueva", "Calle 5", null, "Ponce", null, null);
    private static readonly IList<PortalPackageRequest> OnePackage = new List<PortalPackageRequest> { new("Caja", 1, 2m) };

    [Theory]
    [InlineData(Admin, true)]
    [InlineData(Operator, true)]
    [InlineData(Reader, false)]
    public async Task Only_admin_and_operator_roles_can_create_orders(int user, bool expected)
    {
        var (f, svc) = await SetupAsync();
        await using var _ = f;
        AsPortal(f, user, ClientA);
        Assert.Equal(expected, await svc.CanCreateAsync(CancellationToken.None));
    }

    [Fact]
    public async Task A_read_only_person_gets_an_empty_form_and_a_403_when_creating()
    {
        var (f, svc) = await SetupAsync();
        await using var _ = f;
        AsPortal(f, Reader, ClientA);
        var form = await svc.GetFormAsync(CancellationToken.None);
        Assert.False(form.CanCreate);
        Assert.Empty(form.Consignees);
        var ex = await Assert.ThrowsAsync<ForbiddenException>(() => svc.CreateAsync(Req(fresh: Fresh, packages: OnePackage), CancellationToken.None));
        Assert.Equal(PortalOrderEntryService.ReadOnlyMessage, ex.Message);
    }

    [Fact]
    public async Task The_form_offers_only_the_active_consignees_and_pickups_of_my_client()
    {
        var (f, svc) = await SetupAsync();
        await using var _ = f;
        AsPortal(f, Operator, ClientA);
        var form = await svc.GetFormAsync(CancellationToken.None);
        Assert.True(form.CanCreate);
        Assert.Equal(new[] { "Tienda 1", "Tienda 2" }, form.Consignees.Select(c => c.Name).ToArray());   // sin la dada de baja, sin la de otro cliente, sin la de facturación
        Assert.Equal(new[] { "Bodega A" }, form.Pickups.Select(c => c.Name).ToArray());
        Assert.Equal(PortalOrderEntryService.MaxPackages, form.MaxPackages);
    }

    [Fact]
    public async Task Another_clients_session_sees_nothing_of_mine()
    {
        var (f, svc) = await SetupAsync();
        await using var _ = f;
        AsPortal(f, Operator, ClientB);               // esta persona no es de B: no puede crear en B
        Assert.False(await svc.CanCreateAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Validations_answer_before_touching_the_orders_service()
    {
        var (f, svc) = await SetupAsync();
        await using var _ = f;
        AsPortal(f, Operator, ClientA);

        var none = await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(Req(packages: OnePackage), CancellationToken.None));
        Assert.Equal(PortalOrderEntryService.ConsigneeRequiredMessage, none.Message);

        var incomplete = await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(Req(fresh: new("Solo nombre", null, null, null, null, null), packages: OnePackage), CancellationToken.None));
        Assert.Equal(PortalOrderEntryService.ConsigneeRequiredMessage, incomplete.Message);

        var both = await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(Req(Guid.NewGuid(), Fresh, OnePackage), CancellationToken.None));
        Assert.Equal(PortalOrderEntryService.ConsigneeBothMessage, both.Message);

        var noPackages = await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(Req(fresh: Fresh, packages: new List<PortalPackageRequest>()), CancellationToken.None));
        Assert.Equal(PortalOrderEntryService.PackagesRequiredMessage, noPackages.Message);

        var many = Enumerable.Range(0, PortalOrderEntryService.MaxPackages + 1).Select(_ => new PortalPackageRequest("x", 1, null)).ToList();
        var tooMany = await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(Req(fresh: Fresh, packages: many), CancellationToken.None));
        Assert.Equal(PortalOrderEntryService.TooManyPackagesMessage, tooMany.Message);

        var longNotes = await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(Req(fresh: Fresh, packages: OnePackage, notes: new string('x', PortalOrderEntryService.MaxNotes + 1)), CancellationToken.None));
        Assert.Equal(PortalOrderEntryService.NotesTooLongMessage, longNotes.Message);
    }
}
