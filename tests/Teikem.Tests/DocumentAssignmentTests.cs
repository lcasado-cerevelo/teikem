using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Catalogs;
using Teikem.Domain.Constants;
using Teikem.Domain.ExitNotices;
using Teikem.Domain.Security;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Notifications;
using Teikem.Infrastructure.Services;
using Teikem.Infrastructure.Wms;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 171 (B2, 2026-10-21) — asignar el aviso de llegada y el aviso de salida a una persona (y «tomarlo»): solo abiertos, solo miembros activos, un asignado a otra persona no se toma
/// y las listas filtran «asignados a mí».
/// </summary>
public sealed class DocumentAssignmentTests
{
    private const int Me = 1;
    private const int Ana = 2;
    private const int Outsider = 99;

    private sealed record World(WmsFixture F, Warehouse W, Teikem.Domain.Clients.Client Owner, Product P, AsnService Asns, ExitNoticeService Exits);

    private static async Task<World> WorldAsync()
    {
        var f = await WmsFixture.CreateAsync(s =>
        {
            s.AddSingleton<ITenantClock>(TenantClock.Default);
            s.AddSingleton<INotificationEventProvider, AsnEventProvider>();
            s.AddSingleton<INotificationEventProvider, ExitNoticeEventProvider>();
            s.AddSingleton<NotificationEventCatalog>();
            s.AddSingleton<INotificationPublisher, NotificationPublisher>();
            s.AddSingleton<AsnService>();
            s.AddSingleton<ExitNoticeService>();
        });
        f.Db.StatusCodes.Add(new StatusCode { StatusCodeId = 900, Entity = StatusDomains.MembershipStatus, InternalCode = MembershipStatuses.Active, LabelJson = "{\"es\":\"Activo\"}", IsActive = true });
        f.Db.UserTenants.AddRange(
            new UserTenant { UserId = Me, TenantId = WmsFixture.TenantId, StatusCodeId = 900 },
            new UserTenant { UserId = Ana, TenantId = WmsFixture.TenantId, StatusCodeId = 900 });
        await f.Db.SaveChangesAsync();
        var w = await f.AddWarehouseAsync("W1");
        var owner = await f.AddClientAsync("ASICS");
        var p = await f.AddProductAsync("MED", ownerClientId: owner.ClientId);
        await f.PostAsync(new InventoryPosting(InventoryTxnTypes.Receipt, p.ProductId, 10m, ToWarehouseId: w.WarehouseId,
            ToBinId: (await f.AddBinAsync(await f.AddZoneAsync(w, "PZ", ZoneTypes.Picking), "P-01")).WarehouseBinId));
        return new World(f, w, owner, p, f.Get<AsnService>(), f.Get<ExitNoticeService>());
    }

    private static Task<AsnDto> NewAsnAsync(World x) => x.Asns.CreateAsync(new AsnCreateRequest(x.W.PublicId, x.Owner.PublicId, "OC-1", Lines: new[] { new AsnLineRequest(x.P.PublicId, 5m) }), default);
    private static Task<ExitNoticeDto> NewNoticeAsync(World x) => x.Exits.CreateAsync(new ExitNoticeRequest(x.W.PublicId, x.Owner.PublicId, Lines: new[] { new ExitNoticeLineRequest(x.P.PublicId, 3m) }), default);

    [Fact]
    public async Task A_new_asn_is_unassigned_and_a_supervisor_assigns_and_clears_it()
    {
        var x = await WorldAsync();
        await using var _ = x.F;
        var asn = await NewAsnAsync(x);
        Assert.Null(asn.AssignedToUserId);

        var assigned = await x.Asns.AssignAsync(asn.Id, new TaskAssignRequest(Ana), default);
        Assert.Equal(Ana, assigned.AssignedToUserId);
        var cleared = await x.Asns.AssignAsync(asn.Id, new TaskAssignRequest(null), default);
        Assert.Null(cleared.AssignedToUserId);
    }

    [Fact]
    public async Task Someone_who_is_not_an_active_member_cannot_be_assigned()
    {
        var x = await WorldAsync();
        await using var _ = x.F;
        var asn = await NewAsnAsync(x);
        var ex = await Assert.ThrowsAsync<ValidationException>(() => x.Asns.AssignAsync(asn.Id, new TaskAssignRequest(Outsider), default));
        Assert.Contains(WarehouseTaskRules.AssigneeNotMember, ex.Errors.SelectMany(e => e.Value));
    }

    [Fact]
    public async Task A_cancelled_asn_is_not_assigned()
    {
        var x = await WorldAsync();
        await using var _ = x.F;
        var asn = await NewAsnAsync(x);
        await x.Asns.CancelAsync(asn.Id, default);
        await Assert.ThrowsAsync<StatusRuleException>(() => x.Asns.AssignAsync(asn.Id, new TaskAssignRequest(Ana), default));
    }

    [Fact]
    public async Task Taking_an_unassigned_asn_assigns_it_to_me_and_taking_one_of_someone_else_is_a_409()
    {
        var x = await WorldAsync();
        await using var _ = x.F;
        var asn = await NewAsnAsync(x);
        x.F.Tenant.UserId = Me;
        Assert.Equal(Me, (await x.Asns.TakeAsync(asn.Id, null, default)).AssignedToUserId);
        Assert.Equal(Me, (await x.Asns.TakeAsync(asn.Id, null, default)).AssignedToUserId);   // tomarlo otra vez es inofensivo

        await x.Asns.AssignAsync(asn.Id, new TaskAssignRequest(Ana), default);
        var ex = await Assert.ThrowsAsync<ConflictException>(() => x.Asns.TakeAsync(asn.Id, null, default));
        Assert.Equal(DocumentAssignmentRules.AlreadyAssigned, ex.Message);
    }

    [Fact]
    public void The_take_note_says_where_it_was_taken_from()
    {
        Assert.Contains("app móvil (Mis tareas)", DocumentAssignmentRules.TakeNote("mobile_my_tasks"));
        Assert.Contains("recibirlo", DocumentAssignmentRules.TakeNote("MOBILE_RECEIVE"));
        Assert.Contains("web", DocumentAssignmentRules.TakeNote(null));
    }

    [Fact]
    public async Task The_asn_list_filters_the_ones_assigned_to_me_or_to_a_person()
    {
        var x = await WorldAsync();
        await using var _ = x.F;
        var a = await NewAsnAsync(x);
        var b = await NewAsnAsync(x);
        await NewAsnAsync(x);
        x.F.Tenant.UserId = Me;
        await x.Asns.AssignAsync(a.Id, new TaskAssignRequest(Me), default);
        await x.Asns.AssignAsync(b.Id, new TaskAssignRequest(Ana), default);
        Assert.Equal(new[] { a.Id }, (await x.Asns.ListAsync(new AsnQuery(AssignedToMe: true), InventoryScope.Any, default)).Select(r => r.Id));
        Assert.Equal(new[] { b.Id }, (await x.Asns.ListAsync(new AsnQuery(AssignedUserId: Ana), InventoryScope.Any, default)).Select(r => r.Id));
    }

    [Fact]
    public async Task An_open_exit_notice_is_assigned_taken_and_listed_for_me()
    {
        var x = await WorldAsync();
        await using var _ = x.F;
        var n = await NewNoticeAsync(x);
        Assert.Null(n.AssignedToUserId);
        x.F.Tenant.UserId = Me;
        var taken = await x.Exits.TakeAsync(n.PublicId, null, default);
        Assert.Equal(Me, taken.AssignedToUserId);
        var mine = await x.Exits.ListAsync(null, null, null, false, null, null, 0, 50, default, assignedToMe: true);
        Assert.Equal(n.PublicId, Assert.Single(mine.Items).PublicId);
        Assert.Equal(Me, mine.Items[0].AssignedToUserId);

        var reassigned = await x.Exits.AssignAsync(n.PublicId, new TaskAssignRequest(Ana), default);
        Assert.Equal(Ana, reassigned.AssignedToUserId);
        await Assert.ThrowsAsync<ConflictException>(() => x.Exits.TakeAsync(n.PublicId, null, default));
        Assert.Empty((await x.Exits.ListAsync(null, null, null, false, null, null, 0, 50, default, assignedToMe: true)).Items);
    }

    [Fact]
    public async Task A_cancelled_exit_notice_is_not_assigned()
    {
        var x = await WorldAsync();
        await using var _ = x.F;
        var n = await NewNoticeAsync(x);
        await x.Exits.CancelAsync(n.PublicId, null, default);
        await Assert.ThrowsAsync<StatusRuleException>(() => x.Exits.AssignAsync(n.PublicId, new TaskAssignRequest(Ana), default));
    }

    [Fact]
    public void Rules_and_messages_are_exact()
    {
        Assert.True(DocumentAssignmentRules.CanTake(null, 5));
        Assert.True(DocumentAssignmentRules.CanTake(5, 5));
        Assert.False(DocumentAssignmentRules.CanTake(6, 5));
        Assert.False(DocumentAssignmentRules.CanTake(null, null));
        Assert.Equal("Solo se puede asignar un aviso de llegada que siga esperado.", DocumentAssignmentRules.AsnNotOpen);
        Assert.Equal("Solo se puede asignar un aviso de salida abierto (solicitado o parcial).", DocumentAssignmentRules.ExitNoticeNotOpen);
        Assert.Equal("Ese documento ya está asignado a otra persona; pídale a un supervisor que lo reasigne.", DocumentAssignmentRules.AlreadyAssigned);
    }
}
