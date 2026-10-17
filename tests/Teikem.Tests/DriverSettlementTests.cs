using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Catalogs;
using Teikem.Domain.Clients;
using Teikem.Domain.Constants;
using Teikem.Domain.Fleet;
using Teikem.Domain.Orders;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

// Lote 94 (P9): liquidación a choferes — generar, revisar, ajustar, aprobar y cancelar, con el cálculo congelado y sin pagar dos veces.
public sealed class DriverSettlementTests
{
    private const int ServiceId = 1;     // el servicio con que AddOrderAsync crea las órdenes
    private const int PackageId = 777;   // tipo de paquete de la línea de carga

    private sealed class World(WmsFixture f, Client client, Driver driver, DriverSettlementService svc, DeliveryAttemptService attempts)
    {
        public WmsFixture F { get; } = f;
        public Client Client { get; } = client;
        public Driver Driver { get; } = driver;
        public DriverSettlementService Svc { get; } = svc;
        public DeliveryAttemptService Attempts { get; } = attempts;
    }

    private static DateOnly Today => TenantClock.Default.Today;

    private static async Task<World> NewWorldAsync(string formula = DriverPayoutFormulas.DeliveryPlusAttempts, decimal? deliveryRate = 10m, params (int Level, decimal Rate)[] attemptRates)
    {
        var f = await WmsFixture.CreateAsync(s =>
        {
            s.AddSingleton<ITenantClock>(TenantClock.Default);
            s.AddSingleton<DeliveryAttemptService>();
            s.AddSingleton<DriverSettlementService>();
        });
        f.SetModules(ModuleKeys.LtlGround);
        var id = 8500;
        foreach (var code in DriverPayoutRules.Formulas)
            f.Db.LookupCodes.Add(new LookupCode { LookupCodeId = id++, Entity = LookupDomains.DriverPayoutFormula, InternalCode = code, LabelJson = $"{{\"es\":\"{code}\"}}", IsActive = true });
        var driver = new Driver { PublicId = Guid.NewGuid(), TenantId = WmsFixture.TenantId, EmployeeCode = "D1", FullName = "Ana Chofer", StatusCodeId = 1, IsActive = true };
        f.Db.Set<Driver>().Add(driver);
        await f.Db.SaveChangesAsync();
        f.Lookups.Load(f.Db.LookupCodes.IgnoreQueryFilters().AsNoTracking().ToList());
        f.Db.Set<DriverPayPolicy>().Add(new DriverPayPolicy { TenantId = WmsFixture.TenantId, AttemptLevels = 2, PayoutFormulaLookupId = f.Lookups.GetIdAsync(LookupDomains.DriverPayoutFormula, formula).Result });
        var from = new DateOnly(2026, 1, 1);
        if (deliveryRate is decimal dr) f.Db.Set<DriverDeliveryRate>().Add(new DriverDeliveryRate { TenantId = WmsFixture.TenantId, DriverId = driver.DriverId, ServiceTypeLookupId = ServiceId, PackageTypeLookupId = PackageId, Rate = dr, EffectiveFrom = from });
        foreach (var (level, rate) in attemptRates)
            f.Db.Set<DriverAttemptRate>().Add(new DriverAttemptRate { TenantId = WmsFixture.TenantId, DriverId = driver.DriverId, AttemptNumber = level, Rate = rate, EffectiveFrom = from });
        await f.Db.SaveChangesAsync();
        var client = await f.AddClientAsync("C1");
        f.Db.ChangeTracker.Clear();
        return new World(f, client, driver, f.Get<DriverSettlementService>(), f.Get<DeliveryAttemptService>());
    }

    /// <summary>Una orden con su línea de carga y los intentos dados (true = entregó), registrados hoy.</summary>
    private static async Task<TransportOrder> OrderWithAttemptsAsync(World x, string number, params bool[] attempts)
    {
        var o = await x.F.AddOrderAsync(x.Client.ClientId, number, OrderStatuses.InTransit);
        x.F.Db.Set<CargoLine>().Add(new CargoLine { TransportOrderId = o.TransportOrderId, PackageTypeLookupId = PackageId, Description = "caja", Quantity = 1, IsActive = true });
        await x.F.Db.SaveChangesAsync();
        foreach (var delivered in attempts)
            await x.Attempts.RecordAsync(new DeliveryAttemptCreateRequest(o.PublicId, x.Driver.PublicId, delivered), default);
        x.F.Db.ChangeTracker.Clear();
        return o;
    }

    private static DriverSettlementRequest Period(World x, Guid? driver = null) => new(driver, Today.AddDays(-1), Today, null);

    [Fact]
    public async Task Delivery_plus_attempts_pays_the_delivery_and_every_attempt_and_freezes_the_formula()
    {
        var x = await NewWorldAsync(DriverPayoutFormulas.DeliveryPlusAttempts, 10m, (1, 5m), (2, 3m));
        await using var _ = x.F;
        await OrderWithAttemptsAsync(x, "O-1", false, true);   // un intento fallido y la entrega

        var run = await x.Svc.GenerateAsync(Period(x), default);
        Assert.Equal(("LIQ-00001", "GENERATED", DriverPayoutFormulas.DeliveryPlusAttempts, 1, 18m), (run.Number, run.StatusCode, run.PayoutFormulaCode, run.LineCount, run.TotalAmount));
        var line = Assert.Single(run.Lines);
        Assert.Equal(("DELIVERY", "O-1", 2, 18m, 18m, false, false), (line.Kind, line.OrderNumber, line.AttemptCount, line.CalculatedAmount, line.Amount, line.Adjusted, line.RateMissing));
        Assert.Equal(new[] { ("DELIVERY", 10m), ("ATTEMPT", 5m), ("ATTEMPT", 3m) }, line.Breakdown.Select(b => (b.Kind, b.Amount)).ToArray());
        Assert.Equal(("D1", "Ana Chofer"), (line.DriverCode, line.DriverName));
        Assert.Equal((18m, 0m), (run.DeliveriesAmount, run.TripsAmount));

        // los intentos quedaron reservados
        var attempts = (await x.Attempts.ListAsync(new DeliveryAttemptListQuery(), default)).Items;
        Assert.All(attempts, a => Assert.True(a.Settled));

        // cambiar la política y las tarifas después no recalcula la corrida
        var policy = await x.F.Db.Set<DriverPayPolicy>().AsTracking().SingleAsync();
        policy.PayoutFormulaLookupId = await x.F.Lookups.GetIdAsync(LookupDomains.DriverPayoutFormula, DriverPayoutFormulas.FailedReplacesDelivery);
        foreach (var r in await x.F.Db.Set<DriverAttemptRate>().AsTracking().ToListAsync()) r.Rate = 99m;
        await x.F.Db.SaveChangesAsync();
        var again = await x.Svc.GetAsync(run.PublicId, default);
        Assert.Equal((DriverPayoutFormulas.DeliveryPlusAttempts, 18m), (again.PayoutFormulaCode, again.TotalAmount));
    }

    [Fact]
    public async Task The_other_two_formulas_pay_as_the_engine_says()
    {
        // intento fallido + entrega, con la entrega de 10 y los intentos de 5 y 3
        var failed = await NewWorldAsync(DriverPayoutFormulas.FailedReplacesDelivery, 10m, (1, 5m), (2, 3m));
        await using var _ = failed.F;
        await OrderWithAttemptsAsync(failed, "O-1", false, true);
        Assert.Equal(15m, (await failed.Svc.GenerateAsync(Period(failed), default)).TotalAmount);   // el fallido paga su tarifa (5) y la entrega 10

        var first = await NewWorldAsync(DriverPayoutFormulas.DeliveryIncludesFirst, 10m, (1, 5m), (2, 3m));
        await using var __ = first.F;
        await OrderWithAttemptsAsync(first, "O-1", false, true);
        Assert.Equal(13m, (await first.Svc.GenerateAsync(Period(first), default)).TotalAmount);   // la entrega 10 + el 2.º intento 3 (el 1.º va incluido)
    }

    [Fact]
    public async Task A_missing_rate_leaves_the_line_at_zero_with_a_note_and_never_invents_an_amount()
    {
        var x = await NewWorldAsync(DriverPayoutFormulas.DeliveryPlusAttempts, deliveryRate: null);
        await using var _ = x.F;
        await OrderWithAttemptsAsync(x, "O-1", true);
        var run = await x.Svc.GenerateAsync(Period(x), default);
        var line = Assert.Single(run.Lines);
        Assert.Equal((0m, true), (line.Amount, line.RateMissing));
        Assert.All(line.Breakdown, b => Assert.Equal(DriverPayoutRules.NoRateNote, b.Note));
    }

    [Fact]
    public async Task Trips_are_paid_with_their_frozen_amount_and_become_settled_when_the_run_is_approved()
    {
        var x = await NewWorldAsync(attemptRates: new[] { (1, 5m) });
        await using var _ = x.F;
        var type = new SpecialServiceType { TenantId = WmsFixture.TenantId, Name = "Traer vagón" };
        x.F.Db.Set<SpecialServiceType>().Add(type);
        await x.F.Db.SaveChangesAsync();
        var openId = x.F.StatusId(StatusDomains.DriverTripStatus, DriverTripStatuses.Open);
        var trip = new DriverTrip { PublicId = Guid.NewGuid(), TenantId = WmsFixture.TenantId, DriverId = x.Driver.DriverId, SpecialServiceTypeId = type.SpecialServiceTypeId, TripDate = Today, Amount = 40m, StatusCodeId = openId, IsActive = true };
        var noRate = new DriverTrip { PublicId = Guid.NewGuid(), TenantId = WmsFixture.TenantId, DriverId = x.Driver.DriverId, SpecialServiceTypeId = type.SpecialServiceTypeId, TripDate = Today, Amount = 0m, RateMissing = true, StatusCodeId = openId, IsActive = true };
        var old = new DriverTrip { PublicId = Guid.NewGuid(), TenantId = WmsFixture.TenantId, DriverId = x.Driver.DriverId, SpecialServiceTypeId = type.SpecialServiceTypeId, TripDate = Today.AddDays(-30), Amount = 99m, StatusCodeId = openId, IsActive = true };
        x.F.Db.DriverTrips.AddRange(trip, noRate, old);
        await x.F.Db.SaveChangesAsync();
        x.F.Db.ChangeTracker.Clear();

        var run = await x.Svc.GenerateAsync(Period(x), default);
        Assert.Equal((2, 40m, 40m, 0m), (run.LineCount, run.TotalAmount, run.TripsAmount, run.DeliveriesAmount));   // el viaje de hace 30 días queda fuera
        Assert.Equal(new[] { 0m, 40m }, run.Lines.Select(l => l.Amount).OrderBy(a => a).ToArray());
        Assert.True(run.Lines.Single(l => l.Amount == 0m).RateMissing);
        Assert.All(run.Lines, l => Assert.Equal(("TRIP", true), (l.Kind, l.TripPublicId != null)));

        await x.Svc.ReviewAsync(run.PublicId, default);
        var approved = await x.Svc.ApproveAsync(run.PublicId, default);
        Assert.Equal("APPROVED", approved.StatusCode);
        var settledId = x.F.StatusId(StatusDomains.DriverTripStatus, DriverTripStatuses.Settled);
        Assert.Equal(new[] { settledId, settledId, openId }, (await x.F.Db.DriverTrips.AsNoTracking().OrderBy(t => t.DriverTripId).Select(t => t.StatusCodeId).ToListAsync()).ToArray());
    }

    [Fact]
    public async Task Nothing_to_settle_period_and_driver_are_validated_with_exact_messages()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        async Task<string> Msg<T>(Func<Task> act) where T : Exception => (await Assert.ThrowsAsync<T>(act)).Message;
        Assert.Equal(SettlementRules.NothingToSettle, await Msg<StatusRuleException>(() => x.Svc.GenerateAsync(Period(x), default)));
        Assert.Equal(SettlementRules.PeriodRequired, await Msg<ValidationException>(() => x.Svc.GenerateAsync(new DriverSettlementRequest(null, null, Today), default)));
        Assert.Equal(SettlementRules.PeriodInvalid, await Msg<ValidationException>(() => x.Svc.GenerateAsync(new DriverSettlementRequest(null, Today, Today.AddDays(-1)), default)));
        Assert.Equal(SettlementRules.NoteTooLong, await Msg<ValidationException>(() => x.Svc.GenerateAsync(new DriverSettlementRequest(null, Today, Today, new string('x', 501)), default)));
        Assert.Equal(SettlementRules.DriverInvalid, await Msg<ValidationException>(() => x.Svc.GenerateAsync(new DriverSettlementRequest(Guid.NewGuid(), Today, Today), default)));
        Assert.Equal("El cuerpo de la solicitud es obligatorio.", await Msg<ValidationException>(() => x.Svc.GenerateAsync(null, default)));
    }

    [Fact]
    public async Task Nothing_is_paid_twice_and_cancelling_releases_what_the_run_reserved()
    {
        var x = await NewWorldAsync(attemptRates: new[] { (1, 5m) });
        await using var _ = x.F;
        await OrderWithAttemptsAsync(x, "O-1", true);
        var run = await x.Svc.GenerateAsync(Period(x), default);
        Assert.Equal(SettlementRules.NothingToSettle, (await Assert.ThrowsAsync<StatusRuleException>(() => x.Svc.GenerateAsync(Period(x), default))).Message);   // ya está en la corrida

        var cancelled = await x.Svc.CancelAsync(run.PublicId, "me equivoqué de período", default);
        Assert.Equal(("CANCELLED", 0m), (cancelled.StatusCode, cancelled.TotalAmount));
        Assert.Equal(SettlementRules.CancelNeedsOpen, (await Assert.ThrowsAsync<StatusRuleException>(() => x.Svc.CancelAsync(run.PublicId, null, default))).Message);
        Assert.All((await x.Attempts.ListAsync(new DeliveryAttemptListQuery(), default)).Items, a => Assert.False(a.Settled));
        var again = await x.Svc.GenerateAsync(Period(x), default);
        Assert.Equal(("LIQ-00002", 1), (again.Number, again.LineCount));
    }

    [Fact]
    public async Task Entregas_especiales_and_attempts_without_a_driver_stay_out_and_are_counted()
    {
        var x = await NewWorldAsync(attemptRates: new[] { (1, 5m) });
        await using var _ = x.F;
        await OrderWithAttemptsAsync(x, "O-1", true);
        // un intento sin chofer (como los que anota el efecto cuando la ruta no tiene chofer)
        var noDriver = await x.F.AddOrderAsync(x.Client.ClientId, "O-2", OrderStatuses.InTransit);
        x.F.Db.Set<DeliveryAttempt>().Add(new DeliveryAttempt { PublicId = Guid.NewGuid(), TenantId = WmsFixture.TenantId, TransportOrderId = noDriver.TransportOrderId, AttemptNumber = 1, AttemptedAtUtc = DateTime.UtcNow, Delivered = true, Source = DeliveryAttemptSources.Status });
        // una entrega especial con intentos viejos: se paga por viaje, no por intentos
        var special = await x.F.AddOrderAsync(x.Client.ClientId, "O-SP", OrderStatuses.InTransit);
        x.F.Db.Set<DeliveryAttempt>().Add(new DeliveryAttempt { PublicId = Guid.NewGuid(), TenantId = WmsFixture.TenantId, TransportOrderId = special.TransportOrderId, AttemptNumber = 1, DriverId = x.Driver.DriverId, AttemptedAtUtc = DateTime.UtcNow, Delivered = true, Source = DeliveryAttemptSources.Status });
        x.F.Db.DriverTrips.Add(new DriverTrip { PublicId = Guid.NewGuid(), TenantId = WmsFixture.TenantId, DriverId = x.Driver.DriverId, SpecialServiceTypeId = 1, TransportOrderId = special.TransportOrderId, TripDate = Today, StatusCodeId = x.F.StatusId(StatusDomains.DriverTripStatus, DriverTripStatuses.Cancelled), IsActive = true });
        await x.F.Db.SaveChangesAsync();
        x.F.Db.ChangeTracker.Clear();

        var preview = await x.Svc.PreviewAsync(Period(x), default);
        Assert.Equal((1, 1), (preview.LineCount, preview.SkippedNoDriver));   // la entrega especial (con viaje vigente) no genera intentos que pagar; el intento sin chofer se cuenta aparte
    }

    [Fact]
    public async Task Preview_saves_nothing_and_the_driver_filter_narrows_the_lines()
    {
        var x = await NewWorldAsync(attemptRates: new[] { (1, 5m) });
        await using var _ = x.F;
        await OrderWithAttemptsAsync(x, "O-1", true);
        var other = new Driver { PublicId = Guid.NewGuid(), TenantId = WmsFixture.TenantId, EmployeeCode = "D2", FullName = "Beto", StatusCodeId = 1, IsActive = true };
        x.F.Db.Set<Driver>().Add(other);
        await x.F.Db.SaveChangesAsync();
        var o2 = await x.F.AddOrderAsync(x.Client.ClientId, "O-2", OrderStatuses.InTransit);
        x.F.Db.Set<CargoLine>().Add(new CargoLine { TransportOrderId = o2.TransportOrderId, PackageTypeLookupId = PackageId, Description = "caja", Quantity = 1, IsActive = true });
        await x.F.Db.SaveChangesAsync();
        await x.Attempts.RecordAsync(new DeliveryAttemptCreateRequest(o2.PublicId, other.PublicId, true), default);

        var all = await x.Svc.PreviewAsync(Period(x), default);
        Assert.Equal(2, all.LineCount);
        Assert.Empty((await x.Svc.ListAsync(null, null, null, 0, 50, default)).Items);   // la vista previa no guardó nada
        var only = await x.Svc.PreviewAsync(Period(x, other.PublicId), default);
        Assert.Equal(("Beto", 1), (Assert.Single(only.Lines).DriverName, only.LineCount));
        var run = await x.Svc.GenerateAsync(Period(x, x.Driver.PublicId), default);
        Assert.Equal((x.Driver.PublicId, "D1", 1), (run.DriverPublicId, run.DriverCode, run.LineCount));
        // lo que ya se liquidó del primero no vuelve a salir; el otro chofer sigue por pagar
        Assert.Equal(1, (await x.Svc.PreviewAsync(Period(x), default)).LineCount);
    }

    [Fact]
    public async Task The_workflow_is_generated_reviewed_approved_with_exact_messages()
    {
        var x = await NewWorldAsync(attemptRates: new[] { (1, 5m) });
        await using var _ = x.F;
        await OrderWithAttemptsAsync(x, "O-1", true);
        var run = await x.Svc.GenerateAsync(Period(x), default);
        async Task<string> Msg(Func<Task> act) => (await Assert.ThrowsAsync<StatusRuleException>(act)).Message;
        Assert.Equal(SettlementRules.ApproveNeedsReviewed, await Msg(() => x.Svc.ApproveAsync(run.PublicId, default)));
        var reviewed = await x.Svc.ReviewAsync(run.PublicId, default);
        Assert.Equal(("REVIEWED", true), (reviewed.StatusCode, reviewed.ReviewedAtUtc != null));
        Assert.Equal(SettlementRules.ReviewNeedsGenerated, await Msg(() => x.Svc.ReviewAsync(run.PublicId, default)));
        var approved = await x.Svc.ApproveAsync(run.PublicId, default);
        Assert.Equal(("APPROVED", true), (approved.StatusCode, approved.ApprovedAtUtc != null));
        Assert.Equal(SettlementRules.CancelNeedsOpen, await Msg(() => x.Svc.CancelAsync(run.PublicId, null, default)));
        Assert.Equal(SettlementRules.AdjustNeedsOpen, await Msg(() => x.Svc.AdjustLineAsync(run.PublicId, run.Lines[0].Id, new DriverSettlementAdjustRequest(1m, "tarde"), default)));
        await Assert.ThrowsAsync<NotFoundException>(() => x.Svc.GetAsync(Guid.NewGuid(), default));
    }

    [Fact]
    public async Task Manual_adjustment_keeps_the_calculated_amount_needs_a_reason_and_updates_the_total()
    {
        var x = await NewWorldAsync(attemptRates: new[] { (1, 5m) });
        await using var _ = x.F;
        await OrderWithAttemptsAsync(x, "O-1", true);   // entrega 10 + intento 5 = 15
        var run = await x.Svc.GenerateAsync(Period(x), default);
        var lineId = run.Lines[0].Id;
        async Task<string> Msg(DriverSettlementAdjustRequest r) => (await Assert.ThrowsAsync<ValidationException>(() => x.Svc.AdjustLineAsync(run.PublicId, lineId, r, default))).Message;
        Assert.Equal(SettlementRules.AdjustAmountRequired, await Msg(new DriverSettlementAdjustRequest(null, "x")));
        Assert.Equal(SettlementRules.AdjustAmountInvalid, await Msg(new DriverSettlementAdjustRequest(-1m, "x")));
        Assert.Equal(SettlementRules.AdjustAmountInvalid, await Msg(new DriverSettlementAdjustRequest(1.234m, "x")));
        Assert.Equal(SettlementRules.AdjustNoteRequired, await Msg(new DriverSettlementAdjustRequest(20m, " ")));
        Assert.Equal(SettlementRules.AdjustNoteTooLong, await Msg(new DriverSettlementAdjustRequest(20m, new string('x', 251))));

        var adjusted = await x.Svc.AdjustLineAsync(run.PublicId, lineId, new DriverSettlementAdjustRequest(20m, "Bono por zona difícil"), default);
        var line = Assert.Single(adjusted.Lines);
        Assert.Equal((15m, 20m, true, "Bono por zona difícil", 20m), (line.CalculatedAmount, line.Amount, line.Adjusted, line.AdjustmentNote, adjusted.TotalAmount));
        // volver al monto calculado quita el ajuste
        var back = await x.Svc.AdjustLineAsync(run.PublicId, lineId, new DriverSettlementAdjustRequest(15m, null), default);
        Assert.Equal((15m, false, null, 15m), (back.Lines[0].Amount, back.Lines[0].Adjusted, back.Lines[0].AdjustmentNote, back.TotalAmount));
        await Assert.ThrowsAsync<NotFoundException>(() => x.Svc.AdjustLineAsync(run.PublicId, 99999, new DriverSettlementAdjustRequest(1m, "x"), default));
    }

    [Fact]
    public async Task The_list_filters_by_status_driver_and_text_and_never_crosses_tenants()
    {
        var x = await NewWorldAsync(attemptRates: new[] { (1, 5m) });
        await using var _ = x.F;
        await OrderWithAttemptsAsync(x, "O-1", true);
        var run = await x.Svc.GenerateAsync(Period(x), default);
        Task<PagedResult<DriverSettlementListItemDto>> L(string? status = null, Guid? driver = null, string? q = null) => x.Svc.ListAsync(status, driver, q, 0, 50, default);
        Assert.Equal(1, (await L()).Total);
        Assert.Equal(1, (await L("generated")).Total);
        Assert.Equal(0, (await L("APPROVED")).Total);
        Assert.Equal(1, (await L(driver: x.Driver.PublicId)).Total);
        Assert.Equal(0, (await L(driver: Guid.NewGuid())).Total);
        Assert.Equal(1, (await L(q: "LIQ-0000")).Total);
        Assert.Equal(0, (await L(q: "nada")).Total);
        var item = (await L()).Items[0];
        Assert.Equal((run.Number, 1, 10m + 5m, "GENERATED"), (item.Number, item.LineCount, item.TotalAmount, item.StatusCode));
        using (x.F.AsTenant(WmsFixture.OtherTenantId))
        {
            Assert.Equal(0, (await L()).Total);
            await Assert.ThrowsAsync<NotFoundException>(() => x.Svc.GetAsync(run.PublicId, default));
        }
    }
}
