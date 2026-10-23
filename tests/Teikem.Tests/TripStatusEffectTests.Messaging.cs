using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Constants;
using Teikem.Domain.Identity;
using Teikem.Domain.Messaging;
using Teikem.Domain.Security;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Messaging;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>Lote 212: mensajería con el chofer — visibilidad por modo (FULL / OFFICE / NONE), referencia a ruta u orden, responsables y sugeridos.</summary>
public partial class TripStatusEffectTests
{
    private static async Task<(MessagingService Messages, MessageSuggestionService Suggest, ResponsibleService Resp, TenantContext Tenant)> MessagingAsync(Fixture f)
    {
        // 1 = oficina (FULL), 2 y 3 = choferes (OFFICE), 4 = sin mensajería (NONE)
        var modes = new Dictionary<int, string> { [1] = MessagingModes.Full, [2] = MessagingModes.Office, [3] = MessagingModes.Office, [4] = MessagingModes.None };
        foreach (var (id, mode) in modes)
        {
            f.Db.Users.Add(new ApplicationUser { Id = id, UserName = $"u{id}", Email = $"u{id}@teikem.test", FullName = $"Usuario {id}", IsActive = true });
            f.Db.UserTenants.Add(new UserTenant { UserId = id, TenantId = TenantId, StatusCodeId = 1, MessagingMode = mode });
        }
        await f.Db.SaveChangesAsync();
        var tenant = (TenantContext)f.Services.GetRequiredService<ITenantContext>();
        var lookups = f.Services.GetRequiredService<ILookupCache>();
        var resp = new ResponsibleService(f.Db, tenant, lookups, null!);
        var presence = new PresenceService(f.Db);
        return (new MessagingService(f.Db, tenant, presence, new CapturingLiveNotifier()), new MessageSuggestionService(f.Db, tenant, lookups, resp, presence), resp, tenant);
    }

    [Fact]
    public async Task Messaging_visibility_follows_the_mode_of_each_side()
    {
        await using var f = await Fixture.CreateAsync();
        var (msg, _, _, tenant) = await MessagingAsync(f);

        tenant.UserId = 1;   // oficina: ve a los choferes, no a quien no tiene mensajería
        Assert.Equal(new[] { 2, 3 }, (await msg.PeopleAsync(null, 20, default)).Select(p => p.Id).OrderBy(x => x));

        tenant.UserId = 2;   // chofer: solo ve a la oficina
        Assert.Equal(new[] { 1 }, (await msg.PeopleAsync(null, 20, default)).Select(p => p.Id));
        var ex = await Assert.ThrowsAsync<ValidationException>(() => msg.CreateThreadAsync(new MessageThreadRequest(UserIds: new[] { 3 }, Text: "hola"), default));
        Assert.Equal(MessagingRules.UserNotFoundMessage, ex.Message);
        var thread = await msg.CreateThreadAsync(new MessageThreadRequest(UserIds: new[] { 1 }, Text: "hola oficina"), default);
        Assert.Equal("hola oficina", thread.Messages.Single().Body);

        tenant.UserId = 4;   // sin mensajería: no ve a nadie ni puede escribir
        Assert.Empty(await msg.PeopleAsync(null, 20, default));
        var forbidden = await Assert.ThrowsAsync<ForbiddenException>(() => msg.CreateThreadAsync(new MessageThreadRequest(UserIds: new[] { 1 }, Text: "hola"), default));
        Assert.Equal(MessagingRules.NoMessagingMessage, forbidden.Message);

        tenant.UserId = 1;   // el administrador cambia el modo de un chofer a FULL: ya se ven entre choferes
        await msg.SetModeAsync(3, new MessagingModeRequest("full"), default);
        tenant.UserId = 2;
        Assert.Equal(new[] { 1, 3 }, (await msg.PeopleAsync(null, 20, default)).Select(p => p.Id).OrderBy(x => x));
        var bad = await Assert.ThrowsAsync<ValidationException>(() => msg.SetModeAsync(3, new MessagingModeRequest("otro"), default));
        Assert.Equal(MessagingModes.InvalidMessage, bad.Message);
    }

    [Fact]
    public async Task A_message_can_carry_a_route_card_and_it_shows_in_the_thread_the_preview_and_the_about_list()
    {
        await using var f = await Fixture.CreateAsync();
        var (msg, _, _, tenant) = await MessagingAsync(f);
        var (trip, _) = await f.SeedTripAsync(TripStatuses.Planned, RouteStatuses.Draft, OrderStatuses.Pickup, 1);
        tenant.UserId = 1;

        var thread = await msg.CreateThreadAsync(new MessageThreadRequest(UserIds: new[] { 2 }, Text: "¿sale hoy?", Ref: new MessageRefRequest("trip", trip.PublicId)), default);

        var card = thread.Messages.Single().Ref;
        Assert.NotNull(card);
        Assert.Equal(MessageRefs.Trip, card!.Entity);
        Assert.Equal(trip.Code, card.Title);
        Assert.False(card.CanOpen);                     // sin permiso de ver rutas solo se ve el título
        Assert.Null(card.Subtitle);
        Assert.Equal($"[{trip.Code}] ¿sale hoy?", thread.Thread.LastMessagePreview);

        var about = await msg.AboutAsync("TRIP", trip.PublicId, default);
        Assert.Equal("¿sale hoy?", Assert.Single(about).Preview);
        tenant.UserId = 3;   // quien no es parte del hilo no ve nada de esa ruta
        Assert.Empty(await msg.AboutAsync("TRIP", trip.PublicId, default));
    }

    [Fact]
    public async Task A_reference_must_be_a_known_entity_of_this_company()
    {
        await using var f = await Fixture.CreateAsync();
        var (msg, _, _, tenant) = await MessagingAsync(f);
        tenant.UserId = 1;
        var kind = await Assert.ThrowsAsync<ValidationException>(() => msg.CreateThreadAsync(new MessageThreadRequest(UserIds: new[] { 2 }, Text: "x", Ref: new MessageRefRequest("CLIENT", Guid.NewGuid())), default));
        Assert.Equal(MessageRefs.InvalidMessage, kind.Message);
        var missing = await Assert.ThrowsAsync<ValidationException>(() => msg.CreateThreadAsync(new MessageThreadRequest(UserIds: new[] { 2 }, Text: "x", Ref: new MessageRefRequest("TRIP", Guid.NewGuid())), default));
        Assert.Equal(MessageRefs.NotFoundMessage, missing.Message);
    }

    [Fact]
    public async Task Suggestions_go_route_then_zone_then_dispatch_roles_then_recent_without_repeats_and_respecting_visibility()
    {
        await using var f = await Fixture.CreateAsync();
        var (msg, suggest, resp, tenant) = await MessagingAsync(f);
        var (trip, _) = await f.SeedTripAsync(TripStatuses.Planned, RouteStatuses.Draft, OrderStatuses.Pickup, 1);
        f.Db.DispatchZones.Add(new Teikem.Domain.Fleet.DispatchZone { DispatchZoneId = 9, TenantId = TenantId, Code = "R-09" });
        trip.DispatchZoneId = 9;
        // roles: «Despachadores» tiene trips.dispatch; la oficina (usuario 1) lo tiene; el 4 (sin mensajería) no cuenta
        f.Db.Permissions.Add(new Permission { PermissionId = 1, Code = PermissionCatalog.TripsDispatch });
        f.Db.AppRoles.Add(new Role { RoleId = 1, TenantId = TenantId, Name = "Despachadores", IsActive = true });
        f.Db.RolePermissions.Add(new RolePermission { RoleId = 1, PermissionId = 1 });
        f.Db.AppUserRoles.Add(new UserRole { UserId = 1, RoleId = 1, TenantId = TenantId });
        await f.Db.SaveChangesAsync();
        tenant.UserId = 2;   // el chofer pide sugerencias de su ruta

        await resp.AddAsync(EntityTypes.Trip, trip.TripId, 1, default);          // responsable de la ruta: la oficina
        await resp.AddAsync(EntityTypes.DispatchZone, 9, 1, default);            // el mismo en la zona: no se repite
        await resp.AddAsync(EntityTypes.DispatchZone, 9, 3, default);            // otro chofer (OFFICE): el chofer 2 no lo ve
        await resp.AddAsync(EntityTypes.DispatchZone, 9, 4, default);            // sin mensajería: nunca se sugiere
        await f.Db.SaveChangesAsync();

        var list = await suggest.SuggestAsync("TRIP", trip.PublicId, default);

        Assert.Equal(new[] { "TRIP", "ROLE" }, list.Select(s => s.Source));
        Assert.Equal(1, list[0].UserId);
        Assert.Equal("Despachadores", list[1].Name);
        Assert.Equal(1, list[1].RoleId);

        // la oficina (que sí ve a los choferes) ve también al responsable de la zona
        tenant.UserId = 1;
        var office = await suggest.SuggestAsync("TRIP", trip.PublicId, default);
        Assert.Contains(office, s => s.Source == "ZONE" && s.UserId == 3);
        Assert.DoesNotContain(office, s => s.UserId == 4);
        Assert.DoesNotContain(office, s => s.UserId == 1);   // nunca uno mismo

        // con quien se habló hace poco aparece al final, una sola vez
        await msg.CreateThreadAsync(new MessageThreadRequest(UserIds: new[] { 2 }, Text: "hola"), default);
        var after = await suggest.SuggestAsync("TRIP", trip.PublicId, default);
        Assert.Equal(1, after.Count(s => s.UserId == 2));
        Assert.Equal("RECENT", after.Last().Source);
    }

    [Theory]
    [InlineData("FULL", "FULL", true)]
    [InlineData("FULL", "OFFICE", true)]
    [InlineData("OFFICE", "FULL", true)]
    [InlineData("OFFICE", "OFFICE", false)]
    [InlineData("FULL", "NONE", false)]
    [InlineData("NONE", "FULL", false)]
    public void CanSee_follows_the_visibility_table(string viewer, string target, bool expected) => Assert.Equal(expected, MessagingRules.CanSee(viewer, target));

    [Fact]
    public void Modes_and_refs_normalize_and_reject_the_unknown()
    {
        Assert.Equal("OFFICE", MessagingModes.Normalize(" office "));
        Assert.Null(MessagingModes.Normalize("x"));
        Assert.Equal("ORDER", MessageRefs.Normalize("order"));
        Assert.Null(MessageRefs.Normalize("CLIENT"));
        Assert.Equal("[ORD-1] hola", MessagingRules.PreviewWithRef("ORD-1", "hola"));
        Assert.Equal("hola", MessagingRules.PreviewWithRef(null, "hola"));
    }
}
