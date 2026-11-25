using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Clients;
using Teikem.Domain.Constants;
using Teikem.Domain.Contacts;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Clients;
using Teikem.Infrastructure.Contracts;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 243 (G3) — consultas de los contactos del cliente para los avisos (ClientContactQueries): la tarjeta (dirección CORPORATE antes que BILLING, teléfono
/// y correo del contacto principal primero, como QuickBooks) y los correos de la copia al cliente (principal + marcados, sin repetir, con el del cliente de respaldo).
/// Siempre a través del cliente de la compañía.
/// </summary>
public sealed class ClientContactQueriesTests
{
    private const int Tenant = WmsFixture.TenantId;

    private static ContactPointUpsertRequest Email(string v, bool primary = true) => new("EMAIL", v, null, null, primary);
    private static ContactPointUpsertRequest Phone(string v, bool primary = true) => new("PHONE", v, null, null, primary);

    private static async Task AddressAsync(SalesVendorWorld w, Client c, string type, string line1)
    {
        w.F.Db.Locations.Add(new Location
        {
            TenantId = c.TenantId, ClientId = c.ClientId, PublicId = Guid.NewGuid(), Name = type, Line1 = line1, City = "Ponce", State = "PR", PostalCode = "00716", IsActive = true,
            LocationTypeLookupId = w.Lookup(LookupDomains.LocationType, type), CountryLookupId = w.Lookup(LookupDomains.Country, "PR"), CreatedAtUtc = DateTime.UtcNow,
        });
        await w.F.Db.SaveChangesAsync();
        w.F.Db.ChangeTracker.Clear();
    }

    private static async Task ClientEmailAsync(SalesVendorWorld w, Client c, string email, bool active = true)
    {
        w.F.Db.ContactPoints.Add(new ContactPoint
        {
            TenantId = c.TenantId, OwnerEntityLookupId = w.Lookup(LookupDomains.EntityType, EntityTypes.Client), OwnerId = c.ClientId,
            ContactTypeLookupId = w.Lookup(LookupDomains.ContactType, "EMAIL"), Value = email, IsPrimary = true, IsActive = active,
        });
        await w.F.Db.SaveChangesAsync();
        w.F.Db.ChangeTracker.Clear();
    }

    private static ILookupCache Lookups(SalesVendorWorld w) => w.F.Get<ILookupCache>();

    [Fact]
    public async Task La_tarjeta_toma_la_direccion_CORPORATE_antes_que_BILLING_y_el_telefono_del_contacto_principal()
    {
        await using var w = await SalesVendorWorld.CreateAsync();
        var c = await w.ClientAsync("C1");
        await AddressAsync(w, c, LocationTypes.Billing, "PO Box 55");
        await AddressAsync(w, c, LocationTypes.Corporate, "Calle Sol 10");
        await w.Clients.AddContactAsync(c.PublicId, new ClientContactCreateRequest("Otro", null, false, new[] { Phone("7875550000"), Email("otro@farmacia.test") }), default);
        await w.Clients.AddContactAsync(c.PublicId, new ClientContactCreateRequest("Ana Rivera", null, true, new[] { Phone("7875551234"), Email("ana@farmacia.test") }), default);

        var card = await ClientContactQueries.CardAsync(w.F.Db, Lookups(w), Tenant, c.ClientId, default, d => $"[{d}]");
        Assert.Equal(new[] { "Calle Sol 10", "Ponce PR 00716" }, card.Address);
        Assert.Equal("[7875551234]", card.Phone);
        Assert.Equal("ana@farmacia.test", card.Email);
        Assert.Equal("Ana Rivera", card.ContactName);
    }

    [Fact]
    public async Task Sin_CORPORATE_la_tarjeta_usa_la_BILLING_y_un_cliente_de_otra_compania_da_la_tarjeta_vacia()
    {
        await using var w = await SalesVendorWorld.CreateAsync();
        var c = await w.ClientAsync("C1");
        await AddressAsync(w, c, LocationTypes.Billing, "PO Box 55");
        Assert.Equal("PO Box 55", (await ClientContactQueries.CardAsync(w.F.Db, Lookups(w), Tenant, c.ClientId, default)).Address[0]);

        var other = await w.F.AddClientAsync("AJENO", tenantId: WmsFixture.OtherTenantId);
        Assert.Same(ClientCard.Empty, await ClientContactQueries.CardAsync(w.F.Db, Lookups(w), Tenant, other.ClientId, default));
    }

    [Fact]
    public async Task Los_correos_de_aviso_no_se_repiten_sin_distinguir_mayusculas()
    {
        await using var w = await SalesVendorWorld.CreateAsync();
        var c = await w.ClientAsync("C1");
        await w.Clients.AddContactAsync(c.PublicId, new ClientContactCreateRequest("Principal", null, true, new[] { Email("Compras@Farmacia.test") }, ReceivesEmailNotifications: true), default);
        var copy = await w.Clients.AddContactAsync(c.PublicId, new ClientContactCreateRequest("Copia", null, false, null, ReceivesEmailNotifications: true), default);
        // el servicio guarda los correos en minúsculas; uno cargado de otra forma (migración) con mayúsculas tampoco se repite
        w.F.Db.ContactPoints.Add(new ContactPoint
        {
            TenantId = Tenant, OwnerEntityLookupId = w.Lookup(LookupDomains.EntityType, EntityTypes.ClientContact), OwnerId = copy.Id,
            ContactTypeLookupId = w.Lookup(LookupDomains.ContactType, "EMAIL"), Value = "COMPRAS@FARMACIA.TEST", IsPrimary = true, IsActive = true,
        });
        await w.F.Db.SaveChangesAsync();
        w.F.Db.ChangeTracker.Clear();
        await w.Clients.AddContactAsync(c.PublicId, new ClientContactCreateRequest("Marcado", null, false, new[] { Email("marcado@farmacia.test") }, ReceivesEmailNotifications: true), default);
        await w.Clients.AddContactAsync(c.PublicId, new ClientContactCreateRequest("No marcado", null, false, new[] { Email("nomarcado@farmacia.test") }), default);
        await ClientEmailAsync(w, c, "cliente@farmacia.test");   // el contacto principal tiene correo: el del cliente no se usa

        var emails = await ClientContactQueries.NoticeEmailsAsync(w.F.Db, Lookups(w), Tenant, c.ClientId, default);
        Assert.Equal(new[] { "compras@farmacia.test", "marcado@farmacia.test" }, emails.Select(e => e.Email).ToArray());
        Assert.Equal(new[] { "Principal", "Marcado" }, emails.Select(e => e.Name).ToArray());
    }

    [Fact]
    public async Task Sin_correo_del_contacto_principal_se_usa_el_del_cliente_de_respaldo()
    {
        await using var w = await SalesVendorWorld.CreateAsync();
        var c = await w.ClientAsync("C1");
        await w.Clients.AddContactAsync(c.PublicId, new ClientContactCreateRequest("Principal sin correo", null, true, new[] { Phone("7875551234") }), default);
        await ClientEmailAsync(w, c, "viejo@farmacia.test", active: false);
        await ClientEmailAsync(w, c, "cliente@farmacia.test");

        var emails = await ClientContactQueries.NoticeEmailsAsync(w.F.Db, Lookups(w), Tenant, c.ClientId, default);
        Assert.Equal("cliente@farmacia.test", Assert.Single(emails).Email);
    }

    [Fact]
    public async Task Nunca_lee_contactos_de_otra_compania()
    {
        await using var w = await SalesVendorWorld.CreateAsync();
        var mine = await w.ClientAsync("C1");
        var other = await w.F.AddClientAsync("AJENO", tenantId: WmsFixture.OtherTenantId);
        // un contacto marcado del cliente ajeno, con un correo de la otra compañía
        w.F.Db.ClientContacts.Add(new ClientContact { ClientId = other.ClientId, FullName = "Ajeno", IsPrimary = true, IsActive = true, ReceivesEmailNotifications = true });
        await w.F.Db.SaveChangesAsync();
        var contactId = await w.F.Db.ClientContacts.AsNoTracking().Where(x => x.ClientId == other.ClientId).Select(x => x.ClientContactId).SingleAsync();
        w.F.Db.ContactPoints.Add(new ContactPoint
        {
            TenantId = WmsFixture.OtherTenantId, OwnerEntityLookupId = w.Lookup(LookupDomains.EntityType, EntityTypes.ClientContact), OwnerId = contactId,
            ContactTypeLookupId = w.Lookup(LookupDomains.ContactType, "EMAIL"), Value = "ajeno@otra.test", IsPrimary = true, IsActive = true,
        });
        await w.F.Db.SaveChangesAsync();
        w.F.Db.ChangeTracker.Clear();

        Assert.Empty(await ClientContactQueries.NoticeEmailsAsync(w.F.Db, Lookups(w), Tenant, other.ClientId, default));
        Assert.Empty(await ClientContactQueries.NoticeEmailsAsync(w.F.Db, Lookups(w), Tenant, mine.ClientId, default));
        Assert.Empty(await ClientContactQueries.NoticeEmailsAsync(w.F.Db, Lookups(w), Tenant, 987654, default));
    }

    [Fact]
    public void Las_lineas_de_una_direccion()
    {
        Assert.Empty(ClientContactQueries.AddressLines(null));
        Assert.Equal(new[] { "Calle 1", "Apt 2", "Ponce PR 00716" },
            ClientContactQueries.AddressLines(new Location { Line1 = "Calle 1", Line2 = " Apt 2 ", City = "Ponce", State = "PR", PostalCode = "00716" }));
        Assert.Equal(new[] { "Calle 1", "Ponce" }, ClientContactQueries.AddressLines(new Location { Line1 = "Calle 1", City = "Ponce" }));
    }
}
