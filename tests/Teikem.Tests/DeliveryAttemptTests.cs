using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Clients;
using Teikem.Domain.Constants;
using Teikem.Domain.Fleet;
using Teikem.Domain.Orders;
using Teikem.Domain.Trips;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Services;
using Xunit;
using Trip = Teikem.Domain.Trips.Trip;

namespace Teikem.Tests;

// Lote 93 (P9): intentos de entrega — la fuente de verdad de la liquidación a choferes.
public sealed class DeliveryAttemptTests
{
    [Fact]
    public void Rules_next_number_and_sequence()
    {
        Assert.Equal(1, DeliveryAttemptRules.NextNumber(Array.Empty<int>()));
        Assert.Equal(4, DeliveryAttemptRules.NextNumber(new[] { 1, 3 }));
        var failed1 = new PayoutAttempt(1, false);
        Assert.Null(DeliveryAttemptRules.ValidateSequence(Array.Empty<PayoutAttempt>(), failed1));
        Assert.Null(DeliveryAttemptRules.ValidateSequence(new[] { failed1 }, new PayoutAttempt(2, true)));
        Assert.Equal(DeliveryAttemptRules.DuplicateNumber(1), DeliveryAttemptRules.ValidateSequence(new[] { failed1 }, new PayoutAttempt(1, false)));
        Assert.Equal("Solo un intento puede ser la entrega.", DeliveryAttemptRules.ValidateSequence(new[] { new PayoutAttempt(1, true) }, new PayoutAttempt(2, true)));
        Assert.Equal("La entrega debe ser el último intento.", DeliveryAttemptRules.ValidateSequence(new[] { new PayoutAttempt(2, false) }, new PayoutAttempt(1, true)));
    }

    private sealed class World(WmsFixture f, Client client, Driver driver, DeliveryAttemptService svc)
    {
        public WmsFixture F { get; } = f;
        public Client Client { get; } = client;
        public Driver Driver { get; } = driver;
        public DeliveryAttemptService Svc { get; } = svc;
    }

    private static async Task<World> NewWorldAsync()
    {
        var f = await WmsFixture.CreateAsync(s =>
        {
            s.AddSingleton<ITenantClock>(TenantClock.Default);
            s.AddSingleton<DeliveryAttemptService>();
        });
        f.SetModules(ModuleKeys.LtlGround);
        var id = 8000;
        f.Db.LookupCodes.AddRange(
            new Teikem.Domain.Catalogs.LookupCode { LookupCodeId = id++, Entity = DeliveryAttemptCodes.FailureReasonDomain, InternalCode = "ABSENT", LabelJson = "{\"es\":\"Ausente\",\"en\":\"Absent\"}", IsActive = true },
            new Teikem.Domain.Catalogs.LookupCode { LookupCodeId = id++, Entity = DeliveryAttemptCodes.FailureReasonDomain, InternalCode = "REJECTED", LabelJson = "{\"es\":\"Rechazo\",\"en\":\"Rejected\"}", IsActive = true });
        var driver = new Driver { PublicId = Guid.NewGuid(), TenantId = WmsFixture.TenantId, EmployeeCode = "D1", FullName = "Ana Chofer", StatusCodeId = 1, IsActive = true };
        f.Db.Set<Driver>().Add(driver);
        await f.Db.SaveChangesAsync();
        f.Lookups.Load(f.Db.LookupCodes.IgnoreQueryFilters().AsNoTracking().ToList());
        var client = await f.AddClientAsync("C1");
        f.Db.ChangeTracker.Clear();
        return new World(f, client, driver, f.Get<DeliveryAttemptService>());
    }

    private static DeliveryAttemptCreateRequest Req(World x, TransportOrder o, bool delivered, int? n = null, string? reason = null)
        => new(o.PublicId, x.Driver.PublicId, delivered, n, null, reason, null);

    [Fact]
    public async Task Manual_attempts_number_themselves_and_keep_a_payable_sequence()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        var o = await x.F.AddOrderAsync(x.Client.ClientId, "O-1", OrderStatuses.InTransit);

        var first = await x.Svc.RecordAsync(Req(x, o, false, reason: "absent"), default);
        Assert.Equal((1, false, "ABSENT", "Ausente", DeliveryAttemptSources.Manual, "O-1", "D1"), (first.AttemptNumber, first.Delivered, first.FailureReasonCode, first.FailureReason, first.Source, first.OrderNumber, first.DriverCode));
        var second = await x.Svc.RecordAsync(Req(x, o, true), default);
        Assert.Equal((2, true, null), (second.AttemptNumber, second.Delivered, second.FailureReasonCode));

        // ya hay un intento entregado: no se acepta otro, ni uno intermedio después de él
        Assert.Equal("Solo un intento puede ser la entrega.", (await Assert.ThrowsAsync<ValidationException>(() => x.Svc.RecordAsync(Req(x, o, true), default))).Message);
        Assert.Equal(DeliveryAttemptRules.DuplicateNumber(1), (await Assert.ThrowsAsync<ValidationException>(() => x.Svc.RecordAsync(Req(x, o, false, 1), default))).Message);
        Assert.Equal("La entrega debe ser el último intento.", (await Assert.ThrowsAsync<ValidationException>(() => x.Svc.RecordAsync(Req(x, o, false, 3), default))).Message);
    }

    [Fact]
    public async Task Manual_attempts_validate_their_fields()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        var o = await x.F.AddOrderAsync(x.Client.ClientId, "O-1", OrderStatuses.InTransit);
        async Task<string> Msg(DeliveryAttemptCreateRequest r) => (await Assert.ThrowsAsync<ValidationException>(() => x.Svc.RecordAsync(r, default))).Message;
        Assert.Equal(DeliveryAttemptRules.OrderRequired, await Msg(new DeliveryAttemptCreateRequest(null, x.Driver.PublicId, false)));
        Assert.Equal(DeliveryAttemptRules.DriverRequired, await Msg(new DeliveryAttemptCreateRequest(o.PublicId, null, false)));
        Assert.Equal(DeliveryAttemptRules.DeliveredWithReason, await Msg(Req(x, o, true, reason: "ABSENT")));
        Assert.Equal(DeliveryAttemptRules.UnknownReason("NOPE"), await Msg(Req(x, o, false, reason: "nope")));
        Assert.Equal(DeliveryAttemptRules.NotesTooLong, await Msg(Req(x, o, false) with { Notes = new string('x', 401) }));
        Assert.Equal(DeliveryAttemptRules.FutureAttempt, await Msg(Req(x, o, false) with { AttemptedAtUtc = DateTime.UtcNow.AddHours(2) }));
        await Assert.ThrowsAsync<NotFoundException>(() => x.Svc.RecordAsync(new DeliveryAttemptCreateRequest(Guid.NewGuid(), x.Driver.PublicId, false), default));
        await Assert.ThrowsAsync<NotFoundException>(() => x.Svc.RecordAsync(new DeliveryAttemptCreateRequest(o.PublicId, Guid.NewGuid(), false), default));

        // entrega especial (con viaje pagado): se paga por viaje
        var special = await x.F.AddOrderAsync(x.Client.ClientId, "O-SP", OrderStatuses.InTransit);
        x.F.Db.DriverTrips.Add(new DriverTrip { PublicId = Guid.NewGuid(), TenantId = WmsFixture.TenantId, DriverId = x.Driver.DriverId, SpecialServiceTypeId = 1, TransportOrderId = special.TransportOrderId, TripDate = TenantClock.Default.Today, StatusCodeId = 1, IsActive = true });
        await x.F.Db.SaveChangesAsync();
        Assert.Equal(DeliveryAttemptRules.SpecialDelivery, (await Assert.ThrowsAsync<StatusRuleException>(() => x.Svc.RecordAsync(Req(x, special, true), default))).Message);
    }

    [Fact]
    public async Task Voiding_needs_a_reason_and_is_blocked_once_settled_and_hidden_from_the_list()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        var o = await x.F.AddOrderAsync(x.Client.ClientId, "O-1", OrderStatuses.InTransit);
        var a = await x.Svc.RecordAsync(Req(x, o, false), default);
        Assert.Equal(DeliveryAttemptRules.ReasonRequired, (await Assert.ThrowsAsync<ValidationException>(() => x.Svc.VoidAsync(a.PublicId, " ", default))).Message);
        var voided = await x.Svc.VoidAsync(a.PublicId, "Se registró por error", default);
        Assert.Equal((false, "Se registró por error"), (voided.IsActive, voided.VoidReason));
        Assert.Equal(DeliveryAttemptRules.AlreadyVoided, (await Assert.ThrowsAsync<ConflictException>(() => x.Svc.VoidAsync(a.PublicId, "otra vez", default))).Message);
        Assert.Empty((await x.Svc.ListAsync(new DeliveryAttemptListQuery(), default)).Items);
        Assert.Single((await x.Svc.ListAsync(new DeliveryAttemptListQuery(IncludeVoided: true), default)).Items);
        // el número anulado se puede volver a usar
        Assert.Equal(1, (await x.Svc.RecordAsync(Req(x, o, false), default)).AttemptNumber);

        // ya liquidado: no se anula
        var settled = await x.Svc.RecordAsync(Req(x, o, true), default);
        var row = await x.F.Db.Set<DeliveryAttempt>().AsTracking().SingleAsync(r => r.DeliveryAttemptId == settled.Id);
        row.SettledRunId = 7;
        await x.F.Db.SaveChangesAsync();
        Assert.Equal(DeliveryAttemptRules.AlreadySettled, (await Assert.ThrowsAsync<ConflictException>(() => x.Svc.VoidAsync(settled.PublicId, "tarde", default))).Message);
    }

    [Fact]
    public async Task The_list_filters_by_driver_order_result_settled_and_dates_and_never_crosses_tenants()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        var o1 = await x.F.AddOrderAsync(x.Client.ClientId, "O-1", OrderStatuses.InTransit);
        var o2 = await x.F.AddOrderAsync(x.Client.ClientId, "O-2", OrderStatuses.InTransit);
        await x.Svc.RecordAsync(Req(x, o1, false), default);
        await x.Svc.RecordAsync(Req(x, o1, true), default);
        await x.Svc.RecordAsync(Req(x, o2, true) with { AttemptedAtUtc = DateTime.UtcNow.AddDays(-3) }, default);

        Task<PagedResult<DeliveryAttemptDto>> L(DeliveryAttemptListQuery q) => x.Svc.ListAsync(q, default);
        Assert.Equal(3, (await L(new DeliveryAttemptListQuery())).Total);
        Assert.Equal(2, (await L(new DeliveryAttemptListQuery(OrderPublicId: o1.PublicId))).Total);
        Assert.Equal(3, (await L(new DeliveryAttemptListQuery(DriverPublicId: x.Driver.PublicId))).Total);
        Assert.Equal(0, (await L(new DeliveryAttemptListQuery(DriverPublicId: Guid.NewGuid()))).Total);
        Assert.Equal(2, (await L(new DeliveryAttemptListQuery(Delivered: true))).Total);
        Assert.Equal(3, (await L(new DeliveryAttemptListQuery(Settled: false))).Total);
        Assert.Equal(0, (await L(new DeliveryAttemptListQuery(Settled: true))).Total);
        Assert.Equal(2, (await L(new DeliveryAttemptListQuery(From: TenantClock.Default.Today))).Total);   // el de hace 3 días queda fuera
        Assert.Equal(0, (await L(new DeliveryAttemptListQuery(WithoutDriver: true))).Total);
        var page = await L(new DeliveryAttemptListQuery(Take: 2));
        Assert.Equal((3, 2), (page.Total, page.Items.Count));
        Assert.True(page.Items[0].AttemptedAtUtc >= page.Items[1].AttemptedAtUtc);
        using (x.F.AsTenant(WmsFixture.OtherTenantId))
            Assert.Equal(0, (await L(new DeliveryAttemptListQuery())).Total);
    }

    [Fact]
    public async Task The_order_status_effect_records_the_attempt_with_the_driver_of_the_current_route()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        var o = await x.F.AddOrderAsync(x.Client.ClientId, "O-1", OrderStatuses.InTransit);
        var trip = new Trip { PublicId = Guid.NewGuid(), TenantId = WmsFixture.TenantId, Code = "RUT-1", PlanDate = TenantClock.Default.Today, DriverId = x.Driver.DriverId, StatusCodeId = x.F.StatusId(StatusDomains.TripStatus, TripStatuses.InProgress), IsActive = true };
        x.F.Db.Set<Trip>().Add(trip);
        await x.F.Db.SaveChangesAsync();
        x.F.Db.Set<TripOrder>().Add(new TripOrder { TenantId = WmsFixture.TenantId, TripId = trip.TripId, TransportOrderId = o.TransportOrderId, IsCurrent = true, AssignedAtUtc = DateTime.UtcNow });
        await x.F.Db.SaveChangesAsync();
        x.F.Db.ChangeTracker.Clear();

        var effect = new DeliveryAttemptOrderEffect(x.F.Services);
        StatusTransitionContext Ctx(string to, string? comment = null)
            => new(StatusDomains.OrderStatus, EntityTypes.TransportOrder, o.TransportOrderId, null, new Teikem.Domain.Catalogs.StatusCode { InternalCode = to }, comment);

        await effect.OnTransitionedAsync(Ctx(OrderStatuses.Failed, "Nadie en casa"), default);
        await x.F.Db.SaveChangesAsync();
        await effect.OnTransitionedAsync(Ctx(OrderStatuses.Delivered), default);
        await x.F.Db.SaveChangesAsync();
        await effect.OnTransitionedAsync(Ctx(OrderStatuses.Delivered), default);   // una segunda entrega no suma otro intento
        await effect.OnTransitionedAsync(Ctx(OrderStatuses.InTransit), default);   // otros estatus no hacen nada
        await x.F.Db.SaveChangesAsync();

        var items = (await x.Svc.ListAsync(new DeliveryAttemptListQuery(OrderPublicId: o.PublicId), default)).Items.OrderBy(i => i.AttemptNumber).ToList();
        Assert.Equal(2, items.Count);
        Assert.Equal(new[] { (1, false, "Nadie en casa"), (2, true, (string?)null) }, items.Select(i => (i.AttemptNumber, i.Delivered, i.Notes)).ToArray());
        Assert.All(items, i => Assert.Equal(("D1", "RUT-1", DeliveryAttemptSources.Status), (i.DriverCode, i.TripCode, i.Source)));
    }

    [Fact]
    public async Task The_effect_leaves_the_driver_empty_without_a_route_and_ignores_special_deliveries()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        var plain = await x.F.AddOrderAsync(x.Client.ClientId, "O-1", OrderStatuses.InTransit);
        var special = await x.F.AddOrderAsync(x.Client.ClientId, "O-SP", OrderStatuses.InTransit);
        x.F.Db.DriverTrips.Add(new DriverTrip { PublicId = Guid.NewGuid(), TenantId = WmsFixture.TenantId, DriverId = x.Driver.DriverId, SpecialServiceTypeId = 1, TransportOrderId = special.TransportOrderId, TripDate = TenantClock.Default.Today, StatusCodeId = 1, IsActive = true });
        await x.F.Db.SaveChangesAsync();
        x.F.Db.ChangeTracker.Clear();

        var effect = new DeliveryAttemptOrderEffect(x.F.Services);
        foreach (var id in new[] { plain.TransportOrderId, special.TransportOrderId })
        {
            await effect.OnTransitionedAsync(new StatusTransitionContext(StatusDomains.OrderStatus, EntityTypes.TransportOrder, id, null, new Teikem.Domain.Catalogs.StatusCode { InternalCode = OrderStatuses.Delivered }, null), default);
            await x.F.Db.SaveChangesAsync();
        }
        var all = (await x.Svc.ListAsync(new DeliveryAttemptListQuery(), default)).Items;
        var only = Assert.Single(all);
        Assert.Equal(("O-1", null), (only.OrderNumber, only.DriverCode));
        Assert.Single((await x.Svc.ListAsync(new DeliveryAttemptListQuery(WithoutDriver: true), default)).Items);
    }
}
