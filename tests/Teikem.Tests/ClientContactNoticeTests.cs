using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Clients;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 243 (G2, adelantada del Lote D, plan §3.4): marca «Recibe avisos por correo» (ClientContact.ReceivesEmailNotifications)
/// y HasEmail en el contacto del cliente. Siempre a través del cliente del tenant: un contacto de otro cliente o de otra compañía da 404.
/// </summary>
public sealed class ClientContactNoticeTests
{
    private static ContactPointUpsertRequest Email(string v) => new("EMAIL", v, null, null, true);
    private static ContactPointUpsertRequest Phone(string v) => new("PHONE", v, null, null, true);

    [Fact]
    public async Task Alta_por_omision_no_recibe_avisos_y_sin_correo_HasEmail_es_falso()
    {
        await using var w = await SalesVendorWorld.CreateAsync();
        var c = await w.ClientAsync("CN1");

        var dto = await w.Clients.AddContactAsync(c.PublicId, new ClientContactCreateRequest("Ana Rivera", null, false, new[] { Phone("7875551234") }), default);

        Assert.False(dto.ReceivesEmailNotifications);
        Assert.False(dto.HasEmail);
        var row = await w.F.Db.ClientContacts.AsNoTracking().SingleAsync(x => x.ClientContactId == dto.Id);
        Assert.False(row.ReceivesEmailNotifications);
    }

    [Fact]
    public async Task Alta_marcada_con_correo_se_guarda_y_HasEmail_es_verdadero()
    {
        await using var w = await SalesVendorWorld.CreateAsync();
        var c = await w.ClientAsync("CN2");

        var dto = await w.Clients.AddContactAsync(c.PublicId,
            new ClientContactCreateRequest("Luis Pérez", "Compras", false, new[] { Email("luis@farmacia.com") }, ReceivesEmailNotifications: true), default);

        Assert.True(dto.ReceivesEmailNotifications);
        Assert.True(dto.HasEmail);
        Assert.True((await w.F.Db.ClientContacts.AsNoTracking().SingleAsync(x => x.ClientContactId == dto.Id)).ReceivesEmailNotifications);
        var listed = Assert.Single(await w.Clients.GetContactsAsync(c.PublicId, false, default));
        Assert.True(listed.ReceivesEmailNotifications);
        Assert.True(listed.HasEmail);
    }

    [Fact]
    public async Task Marcar_un_contacto_sin_correo_no_se_bloquea()
    {
        await using var w = await SalesVendorWorld.CreateAsync();
        var c = await w.ClientAsync("CN3");

        var dto = await w.Clients.AddContactAsync(c.PublicId, new ClientContactCreateRequest("Sin Correo", null, false, null, true), default);

        Assert.True(dto.ReceivesEmailNotifications);
        Assert.False(dto.HasEmail); // la web avisa «marcado para avisos pero sin correo»
    }

    [Fact]
    public async Task Patch_cambia_la_marca_y_null_la_deja_igual()
    {
        await using var w = await SalesVendorWorld.CreateAsync();
        var c = await w.ClientAsync("CN4");
        var dto = await w.Clients.AddContactAsync(c.PublicId, new ClientContactCreateRequest("Ana", null, false, new[] { Email("ana@x.com") }), default);

        var on = await w.Clients.UpdateContactAsync(c.PublicId, dto.Id, new ClientContactUpdateRequest(null, null, null, null, true), default);
        Assert.True(on.ReceivesEmailNotifications);

        var same = await w.Clients.UpdateContactAsync(c.PublicId, dto.Id, new ClientContactUpdateRequest("Ana María", null, null, null), default);
        Assert.True(same.ReceivesEmailNotifications);
        Assert.Equal("Ana María", same.FullName);

        var off = await w.Clients.UpdateContactAsync(c.PublicId, dto.Id, new ClientContactUpdateRequest(null, null, null, null, false), default);
        Assert.False(off.ReceivesEmailNotifications);
        Assert.False((await w.F.Db.ClientContacts.AsNoTracking().SingleAsync(x => x.ClientContactId == dto.Id)).ReceivesEmailNotifications);
    }

    [Fact]
    public async Task Inactivar_el_contacto_conserva_la_marca()
    {
        await using var w = await SalesVendorWorld.CreateAsync();
        var c = await w.ClientAsync("CN5");
        var dto = await w.Clients.AddContactAsync(c.PublicId, new ClientContactCreateRequest("Ana", null, false, new[] { Email("ana@x.com") }, true), default);

        var off = await w.Clients.UpdateContactAsync(c.PublicId, dto.Id, new ClientContactUpdateRequest(null, null, null, false), default);

        // La marca se queda; quien arma los avisos (G3) ignora a los inactivos
        Assert.False(off.IsActive);
        Assert.True(off.ReceivesEmailNotifications);
        Assert.Empty(await w.Clients.GetContactsAsync(c.PublicId, false, default));
        Assert.True(Assert.Single(await w.Clients.GetContactsAsync(c.PublicId, true, default)).ReceivesEmailNotifications);
    }

    [Fact]
    public async Task HasEmail_solo_cuenta_un_correo_activo()
    {
        await using var w = await SalesVendorWorld.CreateAsync();
        var c = await w.ClientAsync("CN6");
        var dto = await w.Clients.AddContactAsync(c.PublicId, new ClientContactCreateRequest("Ana", null, false, new[] { Email("ana@x.com"), Phone("7875551234") }, true), default);
        Assert.True(dto.HasEmail);

        var mail = dto.ContactPoints.Single(p => p.ContactType == "EMAIL");
        await w.F.Get<ContactPointService>().DeactivateAsync(mail.Id, default);
        w.F.Db.ChangeTracker.Clear();

        var after = Assert.Single(await w.Clients.GetContactsAsync(c.PublicId, false, default));
        Assert.False(after.HasEmail);           // el teléfono no cuenta
        Assert.True(after.ReceivesEmailNotifications);
    }

    [Fact]
    public async Task Contacto_de_otro_cliente_da_404()
    {
        await using var w = await SalesVendorWorld.CreateAsync();
        var a = await w.ClientAsync("CNA");
        var b = await w.ClientAsync("CNB");
        var dto = await w.Clients.AddContactAsync(a.PublicId, new ClientContactCreateRequest("Ana", null, false, null), default);

        var ex = await Assert.ThrowsAsync<NotFoundException>(() =>
            w.Clients.UpdateContactAsync(b.PublicId, dto.Id, new ClientContactUpdateRequest(null, null, null, null, true), default));

        Assert.Equal($"Contacto '{dto.Id}' no encontrado.", ex.Message);
        Assert.False((await w.F.Db.ClientContacts.AsNoTracking().SingleAsync(x => x.ClientContactId == dto.Id)).ReceivesEmailNotifications);
    }

    [Fact]
    public async Task Cliente_de_otra_compania_da_404_al_marcar_y_al_listar()
    {
        await using var w = await SalesVendorWorld.CreateAsync();
        var other = await w.F.AddClientAsync("OTRO", WmsFixture.OtherTenantId);
        var contact = new ClientContact { ClientId = other.ClientId, FullName = "Ajeno", IsActive = true };
        w.F.Db.ClientContacts.Add(contact);
        await w.F.Db.SaveChangesAsync();
        w.F.Db.ChangeTracker.Clear();

        var upd = await Assert.ThrowsAsync<NotFoundException>(() =>
            w.Clients.UpdateContactAsync(other.PublicId, contact.ClientContactId, new ClientContactUpdateRequest(null, null, null, null, true), default));
        Assert.Equal("Cliente no encontrado.", upd.Message);

        var add = await Assert.ThrowsAsync<NotFoundException>(() =>
            w.Clients.AddContactAsync(other.PublicId, new ClientContactCreateRequest("Intruso", null, false, null, true), default));
        Assert.Equal("Cliente no encontrado.", add.Message);

        var list = await Assert.ThrowsAsync<NotFoundException>(() => w.Clients.GetContactsAsync(other.PublicId, true, default));
        Assert.Equal("Cliente no encontrado.", list.Message);

        Assert.False((await w.F.Db.ClientContacts.AsNoTracking().SingleAsync(x => x.ClientContactId == contact.ClientContactId)).ReceivesEmailNotifications);
        Assert.Equal(1, await w.F.Db.ClientContacts.AsNoTracking().CountAsync());
    }

    [Fact]
    public async Task Sin_permiso_de_escritura_de_clientes_no_se_agrega_un_contacto_con_correo()
    {
        await using var w = await SalesVendorWorld.CreateAsync();
        var c = await w.ClientAsync("CN7");
        w.F.SetPermissions(PermissionCatalog.ClientsRead);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            w.Clients.AddContactAsync(c.PublicId, new ClientContactCreateRequest("Ana", null, false, new[] { Email("ana@x.com") }, true), default));
    }

    [Fact]
    public void Los_contratos_nacen_sin_marca()
    {
        var dto = new ClientContactDto(1, "Ana", null, false, true, Array.Empty<ContactPointDto>());
        Assert.False(dto.ReceivesEmailNotifications);
        Assert.False(dto.HasEmail);
        Assert.Null(new ClientContactCreateRequest("Ana", null, false, null).ReceivesEmailNotifications);
        Assert.Null(new ClientContactUpdateRequest(null, null, null, null).ReceivesEmailNotifications);
    }
}
