using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Teikem.Domain.Catalogs;
using Teikem.Domain.Clients;
using Teikem.Domain.Constants;
using Teikem.Domain.Identity;
using Teikem.Domain.Messaging;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 117b — mensajes del consignatario con su cliente: una conversación por persona del consignatario (nadie ve la de otro, ni de otro consignatario ni de otro cliente), la abre el consignatario, la contestan los administradores
/// y operadores del cliente (solo consulta lee), lo no leído de cada lado, validaciones y el tope por hora.
/// </summary>
public class ConsigneeMessagingTests
{
    private const int ClientA = 501, ClientB = 502;
    private const int AdminA = 41, OperatorA = 42, ReadOnlyA = 43, AdminB = 44;       // personas de los clientes
    private const int TiendaUser = 51, ColmadoUser = 52, OtroUser = 53;              // personas de los consignatarios
    private const int LocTienda = 7001, LocColmado = 7002, LocOtro = 7003;

    private sealed record World(WmsFixture F, ConsigneeMessagingService Svc, NoEmailSender Mail) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => F.DisposeAsync();

        public void AsClientPerson(int userId, int clientId)
        {
            F.Tenant.IsPlatformAdmin = false;
            F.Tenant.UserId = userId; F.Tenant.IsPortal = true; F.Tenant.PortalClientId = clientId; F.Tenant.PortalLocationId = null; F.Tenant.ScopedClientIds = new[] { clientId };
            F.Db.ChangeTracker.Clear();
        }

        public void AsConsignee(int userId, int clientId, int locationId)
        {
            F.Tenant.IsPlatformAdmin = false;
            F.Tenant.UserId = userId; F.Tenant.IsPortal = true; F.Tenant.PortalClientId = clientId; F.Tenant.PortalLocationId = locationId; F.Tenant.ScopedClientIds = new[] { clientId };
            F.Db.ChangeTracker.Clear();
        }
    }

    private static async Task<World> WorldAsync()
    {
        var f = await WmsFixture.CreateAsync();
        var db = f.Db;
        db.StatusCodes.Add(new StatusCode { StatusCodeId = 900, Entity = StatusDomains.MembershipStatus, InternalCode = MembershipStatuses.Active, LabelJson = "{\"es\":\"Activo\"}", IsActive = true });
        db.LookupCodes.AddRange(
            new LookupCode { LookupCodeId = 951, Entity = LookupDomains.UserKind, InternalCode = UserKinds.Portal, LabelJson = "{\"es\":\"Portal\"}", IsActive = true },
            new LookupCode { LookupCodeId = 960, Entity = LookupDomains.PortalRole, InternalCode = PortalRoles.ClientAdmin, LabelJson = "{\"es\":\"Admin\"}", IsActive = true },
            new LookupCode { LookupCodeId = 961, Entity = LookupDomains.PortalRole, InternalCode = PortalRoles.ClientOperator, LabelJson = "{\"es\":\"Operador\"}", IsActive = true },
            new LookupCode { LookupCodeId = 962, Entity = LookupDomains.PortalRole, InternalCode = PortalRoles.ClientReadOnly, LabelJson = "{\"es\":\"Solo lectura\"}", IsActive = true },
            new LookupCode { LookupCodeId = 963, Entity = LookupDomains.PortalRole, InternalCode = PortalRoles.Consignee, LabelJson = "{\"es\":\"Consignatario\"}", IsActive = true });
        db.Clients.AddRange(
            new Client { ClientId = ClientA, PublicId = Guid.NewGuid(), TenantId = WmsFixture.TenantId, Code = "A", Name = "Cliente A", IsActive = true, StatusCodeId = 900 },
            new Client { ClientId = ClientB, PublicId = Guid.NewGuid(), TenantId = WmsFixture.TenantId, Code = "B", Name = "Cliente B", IsActive = true, StatusCodeId = 900 });
        ApplicationUser U(int id, string name) => new() { Id = id, UserName = name + "@x.co", NormalizedUserName = (name + "@x.co").ToUpperInvariant(), Email = name + "@x.co", FullName = name, SecurityStamp = "s" + id, IsActive = true, UserKindLookupId = 951 };
        db.Users.AddRange(U(AdminA, "Ana"), U(OperatorA, "Beto"), U(ReadOnlyA, "Rita"), U(AdminB, "Carla"), U(TiendaUser, "Tienda"), U(ColmadoUser, "Colmado"), U(OtroUser, "Otro"));
        Location L(int id, int client, string name) => new()
        {
            LocationId = id, PublicId = Guid.NewGuid(), TenantId = WmsFixture.TenantId, ClientId = client, Name = name, City = "Ponce", Line1 = "Calle 1",
            LocationTypeLookupId = f.LookupId(LookupDomains.LocationType, LocationTypes.Delivery), IsActive = true,
        };
        db.Locations.AddRange(L(LocTienda, ClientA, "Tienda Norte"), L(LocColmado, ClientA, "Colmado Sur"), L(LocOtro, ClientB, "Tienda de B"));
        int pu = 1;
        void P(int client, int user, int role, int? loc = null) => db.PortalUsers.Add(new PortalUser
        {
            PortalUserId = pu++, TenantId = WmsFixture.TenantId, ClientId = client, UserId = user, Email = $"u{user}@x.co", RoleLookupId = role, LocationId = loc,
            StatusCodeId = f.StatusId(StatusDomains.PortalUserStatus, PortalUserStatuses.Active), IsActive = true,
        });
        P(ClientA, AdminA, 960); P(ClientA, OperatorA, 961); P(ClientA, ReadOnlyA, 962); P(ClientB, AdminB, 960);
        P(ClientA, TiendaUser, 963, LocTienda); P(ClientA, ColmadoUser, 963, LocColmado); P(ClientB, OtroUser, 963, LocOtro);
        await db.SaveChangesAsync();
        f.Lookups.Load(db.LookupCodes.IgnoreQueryFilters().AsNoTracking().ToList());
        db.ChangeTracker.Clear();
        var mail = new NoEmailSender();
        return new World(f, new ConsigneeMessagingService(db, f.Tenant, mail, new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { [ConsigneeMessagingService.BaseUrlKey] = "https://app.example.com" }).Build()), mail);
    }

    private static ClientMessageSendRequest Say(string? body) => new(body);

    // ---------------------------------------------------------------- el consignatario

    [Fact]
    public async Task The_consignee_opens_the_conversation_with_the_first_message_and_it_is_his_own()
    {
        await using var w = await WorldAsync();
        w.AsConsignee(TiendaUser, ClientA, LocTienda);
        var empty = await w.Svc.GetMineAsync(null, 0, default);
        Assert.Null(empty.ConversationPublicId);
        Assert.Empty(empty.Messages);

        var sent = await w.Svc.SendAsConsigneeAsync(Say("  Hola, ¿cuándo llega mi paquete?  "), default);
        Assert.Equal((ConsigneeMessageSenders.Consignee, "Hola, ¿cuándo llega mi paquete?", true), (sent.SenderKind, sent.Body, sent.Mine));
        var mine = await w.Svc.GetMineAsync(null, 0, default);
        Assert.NotNull(mine.ConversationPublicId);
        Assert.Single(mine.Messages);
        Assert.Equal(0, mine.Unread);   // lo que él escribe ya lo leyó

        // otro consignatario del mismo cliente no ve la conversación ni sus mensajes
        w.AsConsignee(ColmadoUser, ClientA, LocColmado);
        Assert.Empty((await w.Svc.GetMineAsync(null, 0, default)).Messages);
        Assert.Null((await w.Svc.GetMineAsync(null, 0, default)).ConversationPublicId);
    }

    [Fact]
    public async Task Empty_or_too_long_messages_are_rejected_with_the_exact_message()
    {
        await using var w = await WorldAsync();
        w.AsConsignee(TiendaUser, ClientA, LocTienda);
        var empty = await Assert.ThrowsAsync<ValidationException>(() => w.Svc.SendAsConsigneeAsync(Say("   "), default));
        Assert.Equal(ClientMessageRules.EmptyMessage, empty.Message);
        var big = await Assert.ThrowsAsync<ValidationException>(() => w.Svc.SendAsConsigneeAsync(Say(new string('x', 2001)), default));
        Assert.Equal(ClientMessageRules.TooLongMessage, big.Message);
        Assert.Empty(await w.F.Db.ConsigneeConversations.AsNoTracking().ToListAsync());
        await w.Svc.SendAsConsigneeAsync(Say(new string('x', 2000)), default);   // el tope exacto sí pasa
    }

    [Fact]
    public async Task More_than_thirty_messages_in_an_hour_is_429()
    {
        await using var w = await WorldAsync();
        w.AsConsignee(TiendaUser, ClientA, LocTienda);
        for (var i = 0; i < ClientMessageRules.MaxPerHour; i++) await w.Svc.SendAsConsigneeAsync(Say("m" + i), default);
        var ex = await Assert.ThrowsAsync<TooManyMessagesException>(() => w.Svc.SendAsConsigneeAsync(Say("uno más"), default));
        Assert.Equal(ClientMessageRules.TooManyMessage, ex.Message);
        Assert.Equal(429, ex.StatusCode);
        // el tope es por persona: otro consignatario sí puede
        w.AsConsignee(ColmadoUser, ClientA, LocColmado);
        await w.Svc.SendAsConsigneeAsync(Say("hola"), default);
    }

    // ---------------------------------------------------------------- el cliente

    [Fact]
    public async Task The_client_sees_the_conversations_of_its_consignees_with_what_is_unread_and_answers()
    {
        await using var w = await WorldAsync();
        w.AsConsignee(TiendaUser, ClientA, LocTienda);
        await w.Svc.SendAsConsigneeAsync(Say("Primero"), default);
        await w.Svc.SendAsConsigneeAsync(Say("Segundo"), default);
        w.AsConsignee(ColmadoUser, ClientA, LocColmado);
        await w.Svc.SendAsConsigneeAsync(Say("Del colmado"), default);

        w.AsClientPerson(AdminA, ClientA);
        var list = await w.Svc.ListAsync(false, null, 0, 25, default);
        Assert.Equal(2, list.Total);
        var tienda = list.Items.Single(i => i.ConsigneeName == "Tienda Norte");
        Assert.Equal((2, "Tienda", ConsigneeMessageSenders.Consignee), (tienda.Unread, tienda.PersonName, tienda.LastSenderKind));
        Assert.Equal(2, (await w.Svc.UnreadAsync(default)).Unread);   // dos conversaciones con algo sin leer

        var open = await w.Svc.GetAsync(tienda.PublicId, null, 0, default);
        Assert.Equal(new[] { "Primero", "Segundo" }, open.Messages.Select(m => m.Body));
        Assert.All(open.Messages, m => Assert.False(m.Mine));

        var reply = await w.Svc.ReplyAsync(tienda.PublicId, Say("Mañana en la mañana."), default);
        Assert.Equal((ConsigneeMessageSenders.Client, true), (reply.SenderKind, reply.Mine));
        w.AsClientPerson(OperatorA, ClientA);   // la lectura es compartida: la contestó Ana, el operador ya la ve leída
        Assert.Equal(0, (await w.Svc.ListAsync(false, "Tienda", 0, 25, default)).Items.Single().Unread);
        Assert.Equal(1, (await w.Svc.UnreadAsync(default)).Unread);   // queda la del colmado
        await w.Svc.MarkReadAsClientAsync((await w.Svc.ListAsync(true, null, 0, 25, default)).Items.Single().PublicId, default);
        Assert.Equal(0, (await w.Svc.UnreadAsync(default)).Unread);
        Assert.Empty((await w.Svc.ListAsync(true, null, 0, 25, default)).Items);
    }

    [Fact]
    public async Task The_consignee_sees_the_answer_and_marks_it_read()
    {
        await using var w = await WorldAsync();
        w.AsConsignee(TiendaUser, ClientA, LocTienda);
        await w.Svc.SendAsConsigneeAsync(Say("¿Dónde está?"), default);
        w.AsClientPerson(OperatorA, ClientA);
        var id = (await w.Svc.ListAsync(false, null, 0, 25, default)).Items.Single().PublicId;
        await w.Svc.ReplyAsync(id, Say("En ruta."), default);

        w.AsConsignee(TiendaUser, ClientA, LocTienda);
        Assert.Equal(1, (await w.Svc.UnreadMineAsync(default)).Unread);
        var mine = await w.Svc.GetMineAsync(null, 0, default);
        Assert.Equal(new[] { "¿Dónde está?", "En ruta." }, mine.Messages.Select(m => m.Body));
        Assert.Equal(new[] { true, false }, mine.Messages.Select(m => m.Mine));
        Assert.Equal(("Beto", ConsigneeMessageSenders.Client), (mine.Messages[1].SenderName, mine.Messages[1].SenderKind));
        await w.Svc.MarkReadAsConsigneeAsync(default);
        Assert.Equal(0, (await w.Svc.UnreadMineAsync(default)).Unread);
    }

    [Fact]
    public async Task Read_only_reads_but_does_not_answer_and_a_message_to_a_conversation_of_another_client_is_not_found()
    {
        await using var w = await WorldAsync();
        w.AsConsignee(TiendaUser, ClientA, LocTienda);
        await w.Svc.SendAsConsigneeAsync(Say("Hola"), default);
        w.AsConsignee(OtroUser, ClientB, LocOtro);
        await w.Svc.SendAsConsigneeAsync(Say("Hola de B"), default);

        w.AsClientPerson(ReadOnlyA, ClientA);
        var item = Assert.Single((await w.Svc.ListAsync(false, null, 0, 25, default)).Items);
        Assert.Equal("Tienda Norte", item.ConsigneeName);
        Assert.Single((await w.Svc.GetAsync(item.PublicId, null, 0, default)).Messages);
        var ro = await Assert.ThrowsAsync<ForbiddenException>(() => w.Svc.ReplyAsync(item.PublicId, Say("no"), default));
        Assert.Equal(ConsigneeMessagingService.ReadOnlyMessage, ro.Message);

        // la conversación de B, desde A: 404 igual que si no existiera
        w.AsClientPerson(AdminB, ClientB);
        var bId = Assert.Single((await w.Svc.ListAsync(false, null, 0, 25, default)).Items).PublicId;
        w.AsClientPerson(AdminA, ClientA);
        await Assert.ThrowsAsync<NotFoundException>(() => w.Svc.GetAsync(bId, null, 0, default));
        await Assert.ThrowsAsync<NotFoundException>(() => w.Svc.ReplyAsync(bId, Say("x"), default));
        await Assert.ThrowsAsync<NotFoundException>(() => w.Svc.MarkReadAsClientAsync(bId, default));
        Assert.DoesNotContain("Hola de B", (await w.F.Db.ConsigneeMessages.AsNoTracking().Where(m => m.SenderKind == ConsigneeMessageSenders.Client).ToListAsync()).Select(m => m.Body));
    }

    [Fact]
    public async Task A_consignee_cannot_use_the_client_side_nor_a_person_of_the_client_the_consignee_side()
    {
        await using var w = await WorldAsync();
        w.AsConsignee(TiendaUser, ClientA, LocTienda);
        await w.Svc.SendAsConsigneeAsync(Say("Hola"), default);
        w.AsConsignee(TiendaUser, ClientA, LocTienda);
        await Assert.ThrowsAsync<ForbiddenException>(() => w.Svc.ListAsync(false, null, 0, 25, default));
        await Assert.ThrowsAsync<ForbiddenException>(() => w.Svc.UnreadAsync(default));

        w.AsClientPerson(AdminA, ClientA);
        var ex = await Assert.ThrowsAsync<ForbiddenException>(() => w.Svc.SendAsConsigneeAsync(Say("soy el cliente"), default));
        Assert.Equal(ConsigneeSelfService.NotConsigneeMessage, ex.Message);
        await Assert.ThrowsAsync<ForbiddenException>(() => w.Svc.GetMineAsync(null, 0, default));
    }

    // ---------------------------------------------------------------- aviso por correo al cliente (Lote 117e)

    [Fact]
    public async Task When_the_consignee_writes_the_admins_and_operators_of_the_client_get_one_email_without_the_text()
    {
        await using var w = await WorldAsync();
        w.AsConsignee(TiendaUser, ClientA, LocTienda);
        await w.Svc.SendAsConsigneeAsync(Say("Mensaje privado del consignatario"), default);
        Assert.Equal(new[] { "u41@x.co", "u42@x.co" }, w.Mail.Sent.Select(m => m.To).OrderBy(x => x));   // administrador y operador; quien solo consulta y el otro cliente, no
        var mail = w.Mail.Sent[0];
        Assert.Contains("Tienda Norte", mail.Subject);
        Assert.Contains("https://app.example.com/consignee-messages", mail.Html);
        Assert.DoesNotContain("Mensaje privado", mail.Html);   // el texto del mensaje no viaja por correo
        Assert.DoesNotContain(w.Mail.Sent, m => m.To is "u43@x.co" or "u44@x.co");
    }

    [Fact]
    public async Task Several_messages_in_a_row_are_one_email_and_after_the_client_reads_the_next_one_notifies_again()
    {
        await using var w = await WorldAsync();
        w.AsConsignee(TiendaUser, ClientA, LocTienda);
        await w.Svc.SendAsConsigneeAsync(Say("uno"), default);
        w.AsConsignee(TiendaUser, ClientA, LocTienda);
        await w.Svc.SendAsConsigneeAsync(Say("dos"), default);
        Assert.Equal(2, w.Mail.Sent.Count);   // un correo por destinatario, no por mensaje

        w.AsClientPerson(OperatorA, ClientA);
        await w.Svc.MarkReadAsClientAsync((await w.Svc.ListAsync(false, null, 0, 25, default)).Items.Single().PublicId, default);
        w.AsConsignee(TiendaUser, ClientA, LocTienda);
        await w.Svc.SendAsConsigneeAsync(Say("tres"), default);
        Assert.Equal(4, w.Mail.Sent.Count);
    }

    [Fact]
    public async Task Without_mail_configured_or_when_it_fails_the_message_is_still_sent()
    {
        await using var w = await WorldAsync();
        w.Mail.Configured = false;
        w.AsConsignee(TiendaUser, ClientA, LocTienda);
        await w.Svc.SendAsConsigneeAsync(Say("sin correo"), default);
        Assert.Empty(w.Mail.Sent);
        Assert.Single((await w.Svc.GetMineAsync(null, 0, default)).Messages);
    }

    [Fact]
    public async Task The_client_answering_does_not_send_an_email_to_anyone()
    {
        await using var w = await WorldAsync();
        w.AsConsignee(TiendaUser, ClientA, LocTienda);
        await w.Svc.SendAsConsigneeAsync(Say("hola"), default);
        var before = w.Mail.Sent.Count;
        w.AsClientPerson(AdminA, ClientA);
        await w.Svc.ReplyAsync((await w.Svc.ListAsync(false, null, 0, 25, default)).Items.Single().PublicId, Say("respuesta"), default);
        Assert.Equal(before, w.Mail.Sent.Count);
    }

    [Fact]
    public async Task Long_conversations_page_backwards_in_chronological_order()
    {
        await using var w = await WorldAsync();
        w.AsConsignee(TiendaUser, ClientA, LocTienda);
        for (var i = 1; i <= 5; i++) await w.Svc.SendAsConsigneeAsync(Say("m" + i), default);
        var last = await w.Svc.GetMineAsync(null, 2, default);
        Assert.Equal(new[] { "m4", "m5" }, last.Messages.Select(m => m.Body));
        Assert.True(last.HasMore);
        var older = await w.Svc.GetMineAsync(last.Messages[0].Id, 10, default);
        Assert.Equal(new[] { "m1", "m2", "m3" }, older.Messages.Select(m => m.Body));
        Assert.False(older.HasMore);
    }
}
