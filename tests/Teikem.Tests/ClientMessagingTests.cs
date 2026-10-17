using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Catalogs;
using Teikem.Domain.Clients;
using Teikem.Domain.Constants;
using Teikem.Domain.Identity;
using Teikem.Domain.Messaging;
using Teikem.Domain.Security;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 104 — mensajes del portal con el equipo de servicio al cliente: una conversación POR PERSONA (nadie ve la de otro, ni de su empresa ni de otro cliente), el equipo por cliente (o, sin equipo, quien tenga el permiso),
/// contestar, escribirle primero a una persona, lo no leído de cada lado, validaciones y el tope por hora.
/// </summary>
public class ClientMessagingTests
{
    private const int ClientA = 501, ClientB = 502;
    private const int Staff1 = 31, Staff2 = 32, Boss = 33;      // equipo de la compañía
    private const int AnaA = 41, BetoA = 42, CarlaB = 43;       // personas del cliente A (Ana, Beto) y del B (Carla)

    private sealed record World(WmsFixture F, ClientMessagingService Svc) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => F.DisposeAsync();

        /// <summary>La sesión de una persona del portal (su cliente sale del claim, nunca del request).</summary>
        public void AsPortal(int userId, int clientId)
        {
            F.Tenant.IsPlatformAdmin = false;
            F.Tenant.UserId = userId;
            F.Tenant.IsPortal = true;
            F.Tenant.PortalClientId = clientId;
            F.Tenant.ScopedClientIds = new[] { clientId };
            F.Db.ChangeTracker.Clear();
        }

        /// <summary>La sesión de alguien del equipo con esos permisos.</summary>
        public void AsStaff(int userId, params string[] permissions)
        {
            F.Tenant.IsPortal = false;
            F.Tenant.PortalClientId = null;
            F.Tenant.ScopedClientIds = null;
            F.Tenant.UserId = userId;
            F.SetPermissions(permissions);
            F.Db.ChangeTracker.Clear();
        }
    }

    private static async Task<World> WorldAsync()
    {
        var f = await WmsFixture.CreateAsync();
        var db = f.Db;
        db.StatusCodes.Add(new StatusCode { StatusCodeId = 901, Entity = StatusDomains.PortalUserStatus, InternalCode = PortalUserStatuses.Active, LabelJson = "{\"es\":\"Activo\"}", IsActive = true });
        db.StatusCodes.Add(new StatusCode { StatusCodeId = 902, Entity = StatusDomains.PortalUserStatus, InternalCode = PortalUserStatuses.Suspended, LabelJson = "{\"es\":\"Suspendido\"}", IsActive = true });
        db.StatusCodes.Add(new StatusCode { StatusCodeId = 900, Entity = StatusDomains.MembershipStatus, InternalCode = MembershipStatuses.Active, LabelJson = "{\"es\":\"Activo\"}", IsActive = true });
        db.LookupCodes.Add(new LookupCode { LookupCodeId = 951, Entity = LookupDomains.UserKind, InternalCode = UserKinds.Portal, LabelJson = "{\"es\":\"Portal\"}", IsActive = true });
        db.LookupCodes.Add(new LookupCode { LookupCodeId = 952, Entity = LookupDomains.UserKind, InternalCode = UserKinds.Internal, LabelJson = "{\"es\":\"Interno\"}", IsActive = true });
        db.Clients.AddRange(
            new Client { ClientId = ClientA, PublicId = Guid.NewGuid(), TenantId = WmsFixture.TenantId, Code = "A", Name = "Cliente A", IsActive = true, StatusCodeId = 900 },
            new Client { ClientId = ClientB, PublicId = Guid.NewGuid(), TenantId = WmsFixture.TenantId, Code = "B", Name = "Cliente B", IsActive = true, StatusCodeId = 900 });
        ApplicationUser U(int id, string name, int kind) => new() { Id = id, UserName = name + "@x.co", NormalizedUserName = (name + "@x.co").ToUpperInvariant(), Email = name + "@x.co", FullName = name, SecurityStamp = "s" + id, IsActive = true, UserKindLookupId = kind };
        db.Users.AddRange(U(Staff1, "Servicio Uno", 952), U(Staff2, "Servicio Dos", 952), U(Boss, "Jefa", 952), U(AnaA, "Ana", 951), U(BetoA, "Beto", 951), U(CarlaB, "Carla", 951));
        foreach (var id in new[] { Staff1, Staff2, Boss }) db.UserTenants.Add(new UserTenant { UserId = id, TenantId = WmsFixture.TenantId, StatusCodeId = 900, IsDefault = true });
        int pu = 1;
        foreach (var (client, user) in new[] { (ClientA, AnaA), (ClientA, BetoA), (ClientB, CarlaB) })
            db.PortalUsers.Add(new PortalUser { PortalUserId = pu++, TenantId = WmsFixture.TenantId, ClientId = client, UserId = user, Email = $"u{user}@x.co", RoleLookupId = 950, StatusCodeId = 901, IsActive = true });
        await db.SaveChangesAsync();
        f.Lookups.Load(db.LookupCodes.IgnoreQueryFilters().AsNoTracking().ToList());
        db.ChangeTracker.Clear();
        return new World(f, new ClientMessagingService(db, f.Tenant, f.Get<PermissionService>()));
    }

    private static string Reply => PermissionCatalog.ClientMessagesReply;
    private static string Manage => PermissionCatalog.ClientMessagesManage;

    // ---------------------------------------------------------------- el portal

    [Fact]
    public async Task The_first_message_creates_the_conversation_and_the_next_ones_go_to_the_same_with_their_unread_counts()
    {
        await using var w = await WorldAsync();
        w.AsPortal(AnaA, ClientA);
        var empty = await w.Svc.GetMineAsync(null, 30, default);
        Assert.Null(empty.ConversationPublicId);
        Assert.Empty(empty.Messages);
        Assert.Equal(0, (await w.Svc.UnreadMineAsync(default)).Unread);

        var first = await w.Svc.SendAsPortalAsync(new ClientMessageSendRequest("  Hola, ¿dónde está mi orden?  "), default);
        await w.Svc.SendAsPortalAsync(new ClientMessageSendRequest("Es la CND-0000415"), default);
        Assert.Equal(("Ana", ClientMessageSenders.Client, "Hola, ¿dónde está mi orden?", true), (first.SenderName, first.SenderKind, first.Body, first.Mine));
        Assert.Single(w.F.Db.ClientConversations.AsNoTracking());
        var mine = await w.Svc.GetMineAsync(null, 30, default);
        Assert.Equal(new[] { "Hola, ¿dónde está mi orden?", "Es la CND-0000415" }, mine.Messages.Select(m => m.Body));
        Assert.Equal(0, mine.Unread);   // lo que escribo ya lo leí

        // el equipo contesta: a la persona le llega sin leer, y al leer queda en 0
        w.AsStaff(Staff1, Reply);
        var conv = (await w.Svc.ListAsync(null, false, null, 0, 25, default)).Items.Single();
        Assert.Equal((2, "Ana", "Cliente A", ClientMessageSenders.Client), (conv.Unread, conv.PersonName, conv.ClientName, conv.LastSenderKind));
        await w.Svc.ReplyAsync(conv.PublicId, new ClientMessageSendRequest("La estamos revisando"), default);
        w.AsPortal(AnaA, ClientA);
        Assert.Equal(1, (await w.Svc.UnreadMineAsync(default)).Unread);
        var seen = await w.Svc.GetMineAsync(null, 30, default);
        Assert.Equal("Servicio Uno", seen.Messages.Last().SenderName);
        Assert.False(seen.Messages.Last().Mine);
        await w.Svc.MarkReadAsPortalAsync(default);
        Assert.Equal(0, (await w.Svc.UnreadMineAsync(default)).Unread);
    }

    [Fact]
    public async Task Each_person_has_their_own_conversation_and_nobody_sees_anothers_not_even_in_the_same_company()
    {
        await using var w = await WorldAsync();
        w.AsPortal(AnaA, ClientA);
        await w.Svc.SendAsPortalAsync(new ClientMessageSendRequest("secreto de Ana"), default);
        w.AsPortal(BetoA, ClientA);                       // otra persona del MISMO cliente
        Assert.Empty((await w.Svc.GetMineAsync(null, 30, default)).Messages);
        await w.Svc.SendAsPortalAsync(new ClientMessageSendRequest("mensaje de Beto"), default);
        Assert.Equal(new[] { "mensaje de Beto" }, (await w.Svc.GetMineAsync(null, 30, default)).Messages.Select(m => m.Body));
        w.AsPortal(CarlaB, ClientB);                      // otro cliente
        Assert.Empty((await w.Svc.GetMineAsync(null, 30, default)).Messages);
        w.AsPortal(AnaA, ClientA);
        Assert.Equal(new[] { "secreto de Ana" }, (await w.Svc.GetMineAsync(null, 30, default)).Messages.Select(m => m.Body));
        Assert.Equal(2, w.F.Db.ClientConversations.AsNoTracking().Count());
    }

    [Fact]
    public async Task Empty_or_too_long_messages_are_refused_and_too_many_in_an_hour_get_429()
    {
        await using var w = await WorldAsync();
        w.AsPortal(AnaA, ClientA);
        var empty = await Assert.ThrowsAsync<ValidationException>(() => w.Svc.SendAsPortalAsync(new ClientMessageSendRequest("   "), default));
        Assert.Equal(ClientMessageRules.EmptyMessage, empty.Errors!["body"].Single());
        var big = await Assert.ThrowsAsync<ValidationException>(() => w.Svc.SendAsPortalAsync(new ClientMessageSendRequest(new string('x', 2001)), default));
        Assert.Equal(ClientMessageRules.TooLongMessage, big.Errors!["body"].Single());
        await w.Svc.SendAsPortalAsync(new ClientMessageSendRequest(new string('x', 2000)), default);   // el máximo sí
        for (var i = 1; i < ClientMessageRules.MaxPerHour; i++) await w.Svc.SendAsPortalAsync(new ClientMessageSendRequest("m" + i), default);
        var many = await Assert.ThrowsAsync<TooManyMessagesException>(() => w.Svc.SendAsPortalAsync(new ClientMessageSendRequest("uno más"), default));
        Assert.Equal((429, ClientMessageRules.TooManyMessage), (many.StatusCode, many.Message));
        // otra persona no se ve afectada
        w.AsPortal(BetoA, ClientA);
        await w.Svc.SendAsPortalAsync(new ClientMessageSendRequest("hola"), default);
    }

    [Fact]
    public async Task A_session_without_a_client_cannot_use_the_portal_side()
    {
        await using var w = await WorldAsync();
        w.AsStaff(Staff1, Reply);
        await Assert.ThrowsAsync<ForbiddenException>(() => w.Svc.GetMineAsync(null, 30, default));
        await Assert.ThrowsAsync<ForbiddenException>(() => w.Svc.SendAsPortalAsync(new ClientMessageSendRequest("hola"), default));
    }

    [Fact]
    public async Task Messages_are_paged_from_the_newest_in_chronological_order()
    {
        await using var w = await WorldAsync();
        w.AsPortal(AnaA, ClientA);
        for (var i = 1; i <= 7; i++) await w.Svc.SendAsPortalAsync(new ClientMessageSendRequest("m" + i), default);
        var last = await w.Svc.GetMineAsync(null, 3, default);
        Assert.Equal(new[] { "m5", "m6", "m7" }, last.Messages.Select(m => m.Body));
        Assert.True(last.HasMore);
        var older = await w.Svc.GetMineAsync(last.Messages.First().Id, 3, default);
        Assert.Equal(new[] { "m2", "m3", "m4" }, older.Messages.Select(m => m.Body));
        Assert.True(older.HasMore);
        var first = await w.Svc.GetMineAsync(older.Messages.First().Id, 3, default);
        Assert.Equal(new[] { "m1" }, first.Messages.Select(m => m.Body));
        Assert.False(first.HasMore);
    }

    // ---------------------------------------------------------------- el equipo

    private static async Task SeedConversationsAsync(World w)
    {
        w.AsPortal(AnaA, ClientA);
        await w.Svc.SendAsPortalAsync(new ClientMessageSendRequest("de Ana (cliente A)"), default);
        w.AsPortal(CarlaB, ClientB);
        await w.Svc.SendAsPortalAsync(new ClientMessageSendRequest("de Carla (cliente B)"), default);
    }

    [Fact]
    public async Task Without_a_team_whoever_has_the_permission_sees_every_client_and_with_a_team_only_its_members_and_the_managers()
    {
        await using var w = await WorldAsync();
        await SeedConversationsAsync(w);

        // sin equipo en ningún cliente: cualquiera con el permiso atiende los dos
        w.AsStaff(Staff2, Reply);
        Assert.Equal(2, (await w.Svc.ListAsync(null, false, null, 0, 25, default)).Total);
        Assert.Equal(2, (await w.Svc.UnreadAsync(default)).Conversations);
        var team = await w.Svc.GetTeamAsync(w.F.Db.Clients.Single(c => c.ClientId == ClientA).PublicId, default);
        Assert.True(team.UsesFallback);

        // se designa a Servicio Uno para el cliente A (lo hace quien administra)
        w.AsStaff(Boss, Reply, Manage);
        var clientA = w.F.Db.Clients.AsNoTracking().Single(c => c.ClientId == ClientA).PublicId;
        var set = await w.Svc.SetTeamAsync(clientA, new ClientServiceTeamRequest(new[] { Staff1 }), default);
        Assert.False(set.UsesFallback);
        Assert.Equal(new[] { Staff1 }, set.Members.Select(m => m.UserId));

        // Servicio Dos ya NO ve la de Ana (404, igual que si no existiera), pero sí la de Carla (cliente B sin equipo)
        w.AsStaff(Staff2, Reply);
        var list = await w.Svc.ListAsync(null, false, null, 0, 25, default);
        Assert.Equal(new[] { "Carla" }, list.Items.Select(i => i.PersonName));
        var anaConv = w.F.Db.ClientConversations.IgnoreQueryFilters().AsNoTracking().Single(c => c.ClientId == ClientA).PublicId;
        await Assert.ThrowsAsync<NotFoundException>(() => w.Svc.GetAsync(anaConv, null, 30, default));
        await Assert.ThrowsAsync<NotFoundException>(() => w.Svc.ReplyAsync(anaConv, new ClientMessageSendRequest("intruso"), default));
        await Assert.ThrowsAsync<NotFoundException>(() => w.Svc.MarkReadAsync(anaConv, default));
        // Servicio Uno sí la ve (y la de Carla, porque el cliente B no tiene equipo)
        w.AsStaff(Staff1, Reply);
        Assert.Equal(2, (await w.Svc.ListAsync(null, false, null, 0, 25, default)).Total);
        // y quien administra las ve todas
        w.AsStaff(Boss, Reply, Manage);
        Assert.Equal(2, (await w.Svc.ListAsync(null, false, null, 0, 25, default)).Total);
    }

    [Fact]
    public async Task Any_member_of_the_team_replies_and_that_marks_the_conversation_read_for_the_whole_team()
    {
        await using var w = await WorldAsync();
        await SeedConversationsAsync(w);
        w.AsStaff(Boss, Reply, Manage);
        var clientA = w.F.Db.Clients.AsNoTracking().Single(c => c.ClientId == ClientA).PublicId;
        await w.Svc.SetTeamAsync(clientA, new ClientServiceTeamRequest(new[] { Staff1, Staff2 }), default);

        w.AsStaff(Staff2, Reply);
        var conv = (await w.Svc.ListAsync(clientA, true, null, 0, 25, default)).Items.Single();   // solo con mensajes sin leer, de ese cliente
        Assert.Equal(1, conv.Unread);
        await w.Svc.ReplyAsync(conv.PublicId, new ClientMessageSendRequest("Ya lo veo, Ana."), default);
        w.AsStaff(Staff1, Reply);
        Assert.Empty((await w.Svc.ListAsync(clientA, true, null, 0, 25, default)).Items);          // para su compañero ya no está sin leer
        var detail = await w.Svc.GetAsync(conv.PublicId, null, 30, default);
        Assert.Equal(new[] { "de Ana (cliente A)", "Ya lo veo, Ana." }, detail.Messages.Select(m => m.Body));
        Assert.Equal(("Ana", "Cliente A"), (detail.PersonName, detail.ClientName));
    }

    [Fact]
    public async Task Marking_read_and_searching_the_list()
    {
        await using var w = await WorldAsync();
        await SeedConversationsAsync(w);
        w.AsStaff(Staff1, Reply);
        var conv = (await w.Svc.ListAsync(null, false, "Carla", 0, 25, default)).Items.Single();
        Assert.Equal("Cliente B", conv.ClientName);
        await w.Svc.MarkReadAsync(conv.PublicId, default);
        Assert.Equal(1, (await w.Svc.UnreadAsync(default)).Conversations);   // queda la de Ana
        Assert.Empty((await w.Svc.ListAsync(null, true, "Carla", 0, 25, default)).Items);
        Assert.Single((await w.Svc.ListAsync(null, false, "Cliente A", 0, 25, default)).Items, i => i.PersonName == "Ana");   // por cliente, persona, correo o texto
    }

    [Fact]
    public async Task The_team_can_write_first_to_an_active_person_of_the_client_and_only_to_them()
    {
        await using var w = await WorldAsync();
        w.AsStaff(Staff1, Reply);
        var clientA = w.F.Db.Clients.AsNoTracking().Single(c => c.ClientId == ClientA).PublicId;
        Assert.Equal(new[] { "Ana", "Beto" }, (await w.Svc.PeopleAsync(clientA, default)).Select(p => p.FullName));
        var started = await w.Svc.StartAsync(new ClientConversationStartRequest(clientA, AnaA, "Hola Ana, hay un problema con la CND-0000415"), default);
        Assert.Equal(new[] { "Hola Ana, hay un problema con la CND-0000415" }, started.Messages.Select(m => m.Body));
        Assert.Equal(ClientMessageSenders.Staff, w.F.Db.ClientConversations.AsNoTracking().Single().StartedBy);

        // a la persona le llega sin leer en su portal
        w.AsPortal(AnaA, ClientA);
        Assert.Equal(1, (await w.Svc.UnreadMineAsync(default)).Unread);
        await w.Svc.SendAsPortalAsync(new ClientMessageSendRequest("Gracias, lo reviso"), default);
        Assert.Single(w.F.Db.ClientConversations.AsNoTracking());   // la misma conversación

        // una persona de OTRO cliente (o suspendida) no es válida para ese cliente
        w.AsStaff(Staff1, Reply);
        var nf = await Assert.ThrowsAsync<ValidationException>(() => w.Svc.StartAsync(new ClientConversationStartRequest(clientA, CarlaB, "hola"), default));
        Assert.Equal(ClientMessageRules.PersonNotFound, nf.Errors!["personId"].Single());
        var pu = await w.F.Db.PortalUsers.SingleAsync(p => p.UserId == BetoA);
        pu.StatusCodeId = 902;   // suspendido
        await w.F.Db.SaveChangesAsync();
        w.F.Db.ChangeTracker.Clear();
        await Assert.ThrowsAsync<ValidationException>(() => w.Svc.StartAsync(new ClientConversationStartRequest(clientA, BetoA, "hola"), default));
        Assert.DoesNotContain(await w.Svc.PeopleAsync(clientA, default), p => p.PersonId == BetoA);
    }

    [Fact]
    public async Task Someone_outside_the_clients_team_cannot_start_a_conversation_or_list_its_people()
    {
        await using var w = await WorldAsync();
        w.AsStaff(Boss, Reply, Manage);
        var clientA = w.F.Db.Clients.AsNoTracking().Single(c => c.ClientId == ClientA).PublicId;
        await w.Svc.SetTeamAsync(clientA, new ClientServiceTeamRequest(new[] { Staff1 }), default);
        w.AsStaff(Staff2, Reply);
        await Assert.ThrowsAsync<NotFoundException>(() => w.Svc.PeopleAsync(clientA, default));
        await Assert.ThrowsAsync<NotFoundException>(() => w.Svc.StartAsync(new ClientConversationStartRequest(clientA, AnaA, "hola"), default));
    }

    [Fact]
    public async Task The_team_is_replaced_by_the_new_list_and_only_active_internal_users_can_be_in_it()
    {
        await using var w = await WorldAsync();
        w.AsStaff(Boss, Reply, Manage);
        var clientA = w.F.Db.Clients.AsNoTracking().Single(c => c.ClientId == ClientA).PublicId;
        var candidates = await w.Svc.CandidatesAsync(default);
        Assert.Equal(new[] { "Jefa", "Servicio Dos", "Servicio Uno" }, candidates.Select(c => c.FullName));   // sin las personas de portal
        await w.Svc.SetTeamAsync(clientA, new ClientServiceTeamRequest(new[] { Staff1, Staff2 }), default);
        var changed = await w.Svc.SetTeamAsync(clientA, new ClientServiceTeamRequest(new[] { Staff2, Boss, Boss }), default);   // se repite uno: cuenta una vez
        Assert.Equal(new[] { "Jefa", "Servicio Dos" }, changed.Members.Select(m => m.FullName));
        var bad = await Assert.ThrowsAsync<ValidationException>(() => w.Svc.SetTeamAsync(clientA, new ClientServiceTeamRequest(new[] { AnaA }), default));   // una persona de portal
        Assert.Equal(ClientMessageRules.TeamUserInvalid, bad.Errors!["userIds"].Single());
        await Assert.ThrowsAsync<ValidationException>(() => w.Svc.SetTeamAsync(clientA, new ClientServiceTeamRequest(new[] { 9999 }), default));
        Assert.Equal(2, (await w.Svc.GetTeamAsync(clientA, default)).Members.Count);   // nada cambió
        var none = await w.Svc.SetTeamAsync(clientA, new ClientServiceTeamRequest(Array.Empty<int>()), default);
        Assert.True(none.UsesFallback);
    }

    [Fact]
    public async Task The_fallback_count_is_whoever_holds_the_reply_permission_through_a_role()
    {
        await using var w = await WorldAsync();
        w.F.Db.Permissions.Add(new Permission { PermissionId = 990, Code = PermissionCatalog.ClientMessagesReply, CategoryLookupId = 951 });
        w.F.Db.AppRoles.Add(new Role { RoleId = 880, TenantId = WmsFixture.TenantId, Name = "Servicio al cliente", IsActive = true, Permissions = { new RolePermission { PermissionId = 990 } } });
        w.F.Db.AppUserRoles.AddRange(new UserRole { UserId = Staff1, RoleId = 880, TenantId = WmsFixture.TenantId }, new UserRole { UserId = Staff2, RoleId = 880, TenantId = WmsFixture.TenantId });
        await w.F.Db.SaveChangesAsync();
        w.AsStaff(Boss, Reply, Manage);
        var team = await w.Svc.GetTeamAsync(w.F.Db.Clients.AsNoTracking().Single(c => c.ClientId == ClientA).PublicId, default);
        Assert.Equal((true, 2), (team.UsesFallback, team.FallbackCount));
    }

    [Fact]
    public async Task A_member_with_a_client_data_scope_only_sees_the_conversations_of_the_clients_in_it()
    {
        await using var w = await WorldAsync();
        await SeedConversationsAsync(w);
        w.AsStaff(Staff1, Reply);
        w.F.Tenant.ScopedClientIds = new[] { ClientB };   // el alcance de datos de ese usuario (UserDataScope)
        var items = (await w.Svc.ListAsync(null, false, null, 0, 25, default)).Items;
        Assert.Equal(new[] { "Carla" }, items.Select(i => i.PersonName));
    }
}
