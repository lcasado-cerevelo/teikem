using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Catalogs;
using Teikem.Domain.Clients;
using Teikem.Domain.Constants;
using Teikem.Domain.Contacts;
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
        return (f, new PortalOrderEntryService(db, f.Tenant, null!, new LocationService(db, f.Tenant, f.Lookups)));
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

    // ---------------------------------------------------------------- Lote 213: el formulario igual al del personal

    /// <summary>Catálogos de servicio y paquete (uno apagado por la compañía, uno renombrado y reordenado, uno inactivo), predeterminados y teléfonos de los consignatarios.</summary>
    private static async Task SeedCatalogsAndContactsAsync(WmsFixture f, bool clientHasServiceDefault = true)
    {
        var db = f.Db;
        db.LookupCodes.AddRange(
            new LookupCode { LookupCodeId = 980, Entity = LookupDomains.ServiceType, InternalCode = "NEXT_DAY", LabelJson = "{\"es\":\"Al día siguiente\",\"en\":\"Next day\"}", SortOrder = 10, IsActive = true },
            new LookupCode { LookupCodeId = 981, Entity = LookupDomains.ServiceType, InternalCode = "SAME_DAY", LabelJson = "{\"es\":\"Mismo día\",\"en\":\"Same day\"}", SortOrder = 20, IsActive = true },
            new LookupCode { LookupCodeId = 982, Entity = LookupDomains.ServiceType, InternalCode = "EXPRESS", LabelJson = "{\"es\":\"Expreso\"}", SortOrder = 5, IsActive = true },
            new LookupCode { LookupCodeId = 983, Entity = LookupDomains.ServiceType, InternalCode = "OLD", LabelJson = "{\"es\":\"Viejo\"}", SortOrder = 1, IsActive = false },
            new LookupCode { LookupCodeId = 985, Entity = LookupDomains.PackageType, InternalCode = "BOX", LabelJson = "{\"es\":\"Caja\",\"en\":\"Box\"}", SortOrder = 10, IsActive = true },
            new LookupCode { LookupCodeId = 986, Entity = LookupDomains.PackageType, InternalCode = "PALLET", LabelJson = "{\"es\":\"Paleta\",\"en\":\"Pallet\"}", SortOrder = 20, IsActive = true },
            new LookupCode { LookupCodeId = 990, Entity = "ContactType", InternalCode = "EMAIL", LabelJson = "{}", IsActive = true },
            new LookupCode { LookupCodeId = 991, Entity = "ContactType", InternalCode = "PHONE", LabelJson = "{}", IsActive = true },
            new LookupCode { LookupCodeId = 992, Entity = "ContactType", InternalCode = "MOBILE", LabelJson = "{}", IsActive = true });
        db.LookupCodeOverrides.AddRange(
            new LookupCodeOverride { TenantId = WmsFixture.TenantId, LookupCodeId = 982, IsEnabled = false },                                                   // la compañía apagó el expreso
            new LookupCodeOverride { TenantId = WmsFixture.TenantId, LookupCodeId = 981, IsEnabled = true, CustomLabelJson = "{\"es\":\"Mismo día (rápido)\"}", SortOverride = 1 });   // renombrado y primero
        var ownerType = f.LookupId(LookupDomains.EntityType, EntityTypes.Location);
        db.ContactPoints.AddRange(
            new ContactPoint { TenantId = WmsFixture.TenantId, OwnerEntityLookupId = ownerType, OwnerId = 1, ContactTypeLookupId = 990, Value = "tienda1@x.co", IsPrimary = true, IsActive = true },
            new ContactPoint { TenantId = WmsFixture.TenantId, OwnerEntityLookupId = ownerType, OwnerId = 1, ContactTypeLookupId = 991, Value = "7875550001", CountryCallingCode = "1", IsPrimary = false, IsActive = true },
            new ContactPoint { TenantId = WmsFixture.TenantId, OwnerEntityLookupId = ownerType, OwnerId = 1, ContactTypeLookupId = 992, Value = "7875550002", CountryCallingCode = "1", IsPrimary = true, IsActive = true },
            new ContactPoint { TenantId = WmsFixture.TenantId, OwnerEntityLookupId = ownerType, OwnerId = 2, ContactTypeLookupId = 991, Value = "7875550009", IsPrimary = false, IsActive = false });   // inactivo: no cuenta
        await db.SaveChangesAsync();
        var loc = await db.Locations.AsTracking().SingleAsync(l => l.LocationId == 1);
        loc.Code = "T-001";
        var client = await db.Clients.AsTracking().SingleAsync(c => c.ClientId == ClientA);
        client.ClientAssignsOrderNumber = true;
        client.DefaultServiceTypeLookupId = clientHasServiceDefault ? 980 : null;
        var tenantRow = await db.Tenants.AsTracking().IgnoreQueryFilters().SingleAsync(t => t.TenantId == WmsFixture.TenantId);
        tenantRow.DefaultServiceTypeLookupId = 981;
        tenantRow.DefaultPackageTypeLookupId = 986;   // el cliente no tiene tipo de paquete propio: sale el de la compañía
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
    }

    [Fact]
    public async Task The_form_brings_the_enabled_catalogs_in_spanish_the_defaults_the_order_number_flag_and_the_consignee_number_and_phone()
    {
        var (f, svc) = await SetupAsync();
        await using var _ = f;
        await SeedCatalogsAndContactsAsync(f);
        AsPortal(f, Operator, ClientA);
        f.Tenant.Lang = "es";

        var form = await svc.GetFormAsync(CancellationToken.None);

        // habilitados, en su orden (el renombrado y reordenado primero), sin el apagado ni el inactivo
        Assert.Equal(new[] { ("SAME_DAY", "Mismo día (rápido)"), ("NEXT_DAY", "Al día siguiente") }, form.ServiceTypes!.Select(s => (s.Code, s.Label)).ToArray());
        Assert.Equal(new[] { ("BOX", "Caja"), ("PALLET", "Paleta") }, form.PackageTypes!.Select(s => (s.Code, s.Label)).ToArray());
        Assert.Equal("NEXT_DAY", form.DefaultServiceType);    // el del cliente gana al de la compañía
        Assert.Equal("PALLET", form.DefaultPackageType);      // el cliente no tiene: el de la compañía
        Assert.True(form.ClientAssignsOrderNumber);
        Assert.False(form.ClientTypesInvoiceNumber);
        var t1 = form.Consignees.Single(c => c.Name == "Tienda 1");
        Assert.Equal(("T-001", "+1 7875550002"), (t1.Code, t1.Phone));                         // el móvil principal antes que el teléfono
        var t2 = form.Consignees.Single(c => c.Name == "Tienda 2");
        Assert.Equal(((string?)null, (string?)null), (t2.Code, t2.Phone));                    // sin número ni teléfono activo

        // en inglés, la etiqueta sale en inglés (la renombrada solo tiene español)
        f.Tenant.Lang = "en";
        var en = await svc.GetFormAsync(CancellationToken.None);
        Assert.Equal("Next day", en.ServiceTypes!.Single(s => s.Code == "NEXT_DAY").Label);
    }

    [Fact]
    public async Task Without_a_client_default_the_company_service_type_is_the_default_and_a_read_only_person_gets_no_catalogs()
    {
        var (f, svc) = await SetupAsync();
        await using var _ = f;
        await SeedCatalogsAndContactsAsync(f, clientHasServiceDefault: false);
        AsPortal(f, Operator, ClientA);
        Assert.Equal("SAME_DAY", (await svc.GetFormAsync(CancellationToken.None)).DefaultServiceType);

        AsPortal(f, Reader, ClientA);
        var ro = await svc.GetFormAsync(CancellationToken.None);
        Assert.False(ro.CanCreate);
        Assert.True(ro.ServiceTypes is null or { Count: 0 });
        Assert.True(ro.PackageTypes is null or { Count: 0 });
    }

    [Fact]
    public async Task A_due_date_before_the_ready_date_is_rejected_before_touching_the_orders_service()
    {
        var (f, svc) = await SetupAsync();
        await using var _ = f;
        AsPortal(f, Operator, ClientA);
        var req = new PortalOrderCreateRequest(null, Fresh, null, OnePackage, null, null, new DateTime(2026, 10, 25), null,
            ServiceType: "NEXT_DAY", OrderNumber: "PO-1", PromisedDate: new DateTime(2026, 10, 24, 23, 0, 0));
        var ex = await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(req, CancellationToken.None));
        Assert.Equal(PortalOrderEntryService.PromisedBeforeRequestedMessage, ex.Message);
        Assert.Equal("La fecha límite no puede ser anterior a la fecha lista.", ex.Message);
        Assert.True(ex.Errors!.ContainsKey("promisedDate"));
    }

    // ---------------------------------------------------------------- consignatario nuevo al momento (2026-10-23)

    [Fact]
    public async Task A_new_consignee_is_saved_right_away_for_my_client_and_shows_in_the_form()
    {
        var (f, svc) = await SetupAsync();
        await using var _ = f;
        AsPortal(f, Operator, ClientA);
        var place = await svc.CreateConsigneeAsync(new PortalConsigneeCreateRequest("  Farmacia Nueva ", " Calle 9 #4 ", null, " Caguas ", null, "00725"), CancellationToken.None);
        Assert.Equal("Farmacia Nueva", place.Name);
        Assert.Equal("Calle 9 #4", place.Line1);
        Assert.Equal("Caguas", place.City);
        var saved = await f.Db.Locations.AsNoTracking().FirstAsync(l => l.PublicId == place.PublicId);
        Assert.Equal(ClientA, saved.ClientId);                      // del cliente de la sesión, nunca compartido ni de otro
        Assert.Equal("00725", saved.PostalCode);
        var form = await svc.GetFormAsync(CancellationToken.None);
        Assert.Contains("Farmacia Nueva", form.Consignees.Select(c => c.Name));
    }

    [Fact]
    public async Task The_same_name_and_address_returns_the_existing_consignee_without_duplicating()
    {
        var (f, svc) = await SetupAsync();
        await using var _ = f;
        AsPortal(f, Admin, ClientA);
        var first = await svc.CreateConsigneeAsync(new PortalConsigneeCreateRequest("Colmado Ñandú", "Av. Principal 22", null, "Ponce", null, null), CancellationToken.None);
        var again = await svc.CreateConsigneeAsync(new PortalConsigneeCreateRequest("colmado nandu", "AV. PRINCIPAL  22", null, "Ponce", null, null), CancellationToken.None);   // otras mayúsculas, sin acento, doble espacio
        Assert.Equal(first.PublicId, again.PublicId);
        Assert.Equal(1, await f.Db.Locations.CountAsync(l => l.ClientId == ClientA && l.Name == "Colmado Ñandú"));
        // el que ya existía de antes también se reconoce
        var old = await svc.CreateConsigneeAsync(new PortalConsigneeCreateRequest("Tienda 1", "Calle 1", null, "Ponce", null, null), CancellationToken.None);
        Assert.Equal(1, await f.Db.Locations.CountAsync(l => l.ClientId == ClientA && l.Name == "Tienda 1"));
        Assert.Equal("Tienda 1", old.Name);
    }

    [Theory]
    [InlineData(null, "Calle 1", "Ponce", "name", PortalOrderEntryService.ConsigneeNameRequiredMessage)]
    [InlineData("Tienda", " ", "Ponce", "line1", PortalOrderEntryService.ConsigneeLine1RequiredMessage)]
    [InlineData("Tienda", "Calle 1", "", "city", PortalOrderEntryService.ConsigneeCityRequiredMessage)]
    public async Task A_new_consignee_needs_name_address_and_town(string? name, string? line1, string? city, string field, string message)
    {
        var (f, svc) = await SetupAsync();
        await using var _ = f;
        AsPortal(f, Operator, ClientA);
        var ex = await Assert.ThrowsAsync<ValidationException>(() => svc.CreateConsigneeAsync(new PortalConsigneeCreateRequest(name, line1, null, city, null, null), CancellationToken.None));
        Assert.Equal(message, ex.Message);
        Assert.Contains(field, ex.Errors!.Keys);
    }

    [Fact]
    public async Task A_read_only_person_cannot_create_a_consignee()
    {
        var (f, svc) = await SetupAsync();
        await using var _ = f;
        AsPortal(f, Reader, ClientA);
        var ex = await Assert.ThrowsAsync<ForbiddenException>(() => svc.CreateConsigneeAsync(new PortalConsigneeCreateRequest("Tienda", "Calle 1", null, "Ponce", null, null), CancellationToken.None));
        Assert.Equal(PortalOrderEntryService.ReadOnlyMessage, ex.Message);
        Assert.Equal(0, await f.Db.Locations.CountAsync(l => l.Name == "Tienda" && l.ClientId == ClientA));
    }
}
