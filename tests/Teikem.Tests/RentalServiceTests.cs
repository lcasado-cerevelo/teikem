using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Clients;
using Teikem.Domain.Common;
using Teikem.Domain.Constants;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Clients;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Services;
using Teikem.Infrastructure.Wms;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 27 (Rentas R1) — RentalService en InMemory con el InventoryLedger, StatusService y RentalStatusEffect REALES (WmsFixture):
/// alta en Borrador con equipos por serie y tarifa, programar (reserva), despachar (TRANSFER a EN-RENTA creada a demanda, serie
/// ON_RENT, en mano igual y disponible 0), qué bloquea cada estatus con los mensajes exactos, cancelar (libera), quitar/agregar
/// equipos en Programada, extender con bitácora y tarifa nueva, filtros "por vencer"/"vencidas" con un reloj fijo y aislamiento
/// por compañía (404). El bloqueo real de SQL Server, la concurrencia y el índice UX_RentalLine_OpenSerial los cubre el smoke.
/// </summary>
public sealed class RentalServiceTests
{
    private static readonly DateOnly Today = new(2026, 10, 5);

    private sealed record World(WmsFixture F, Warehouse W, WarehouseBin A1, WarehouseBin A2, WarehouseBin Q1, Product P, Product PNone, Product POwned,
        Client C, Location L, Client C2, Location L2);

    private static async Task<World> SeedAsync()
    {
        var f = await WmsFixture.CreateAsync(s =>
        {
            s.AddSingleton<ITenantClock>(new TenantClock(LocalDay.DefaultZone, () => new DateTime(2026, 10, 5, 16, 0, 0, DateTimeKind.Utc)));
            s.AddSingleton<RentalBinResolver>();
            s.AddSingleton<RentalService>();
            s.AddSingleton<IStatusTransitionEffect, RentalStatusEffect>();
        });
        var w = await f.AddWarehouseAsync("W1");
        var pick = await f.AddZoneAsync(w, "PCK", ZoneTypes.Picking);
        var qua = await f.AddZoneAsync(w, "QUA", ZoneTypes.Quarantine);
        var a1 = await f.AddBinAsync(pick, "A-01");
        var a2 = await f.AddBinAsync(pick, "A-02");
        var q1 = await f.AddBinAsync(qua, "Q-01");
        var c = await f.AddClientAsync("CLI");
        var c2 = await f.AddClientAsync("OTRO");
        var p = await f.AddProductAsync("EQ-1", TrackingTypes.Serial, purchaseCost: 500m);
        var pNone = await f.AddProductAsync("SUM-1");
        var pOwned = await f.AddProductAsync("EQ-3PL", TrackingTypes.Serial, ownerClientId: c.ClientId);
        await f.PostAsync(
            Receive(p, w, a1, "S-1"), Receive(p, w, a1, "S-2"), Receive(p, w, a2, "S-3"), Receive(p, w, q1, "S-Q"),
            Receive(pOwned, w, a1, "O-1"),
            new InventoryPosting(InventoryTxnTypes.Receipt, pNone.ProductId, 5m, ToWarehouseId: w.WarehouseId, ToBinId: a1.WarehouseBinId));
        var l = await AddLocationAsync(f, c, "Hospital A");
        var l2 = await AddLocationAsync(f, c2, "Clínica B");
        return new World(f, w, a1, a2, q1, p, pNone, pOwned, c, l, c2, l2);
    }

    private static InventoryPosting Receive(Product p, Warehouse w, WarehouseBin b, string serial)
        => new(InventoryTxnTypes.Receipt, p.ProductId, 1m, SerialNumber: serial, ToWarehouseId: w.WarehouseId, ToBinId: b.WarehouseBinId);

    private static async Task<Location> AddLocationAsync(WmsFixture f, Client c, string name, bool isActive = true)
    {
        var l = new Location
        {
            PublicId = Guid.NewGuid(), TenantId = c.TenantId, ClientId = c.ClientId, Name = name, Line1 = "Calle 1", City = "Ponce",
            LocationTypeLookupId = f.LookupId(LookupDomains.LocationType, LocationTypes.Delivery), CountryLookupId = f.LookupId(LookupDomains.Country, "PR"),
            IsActive = isActive,
        };
        f.Db.Locations.Add(l);
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        return l;
    }

    private static async Task<T> Run<T>(World w, Func<RentalService, Task<T>> action)
    {
        try { return await action(w.F.Get<RentalService>()); }
        finally { w.F.Db.ChangeTracker.Clear(); }
    }

    private static RentalLinesAddRequest Equip(Product p, params string[] serials) => new(p.PublicId, serials);

    private static RentalCreateRequest NewRental(World w, params RentalLinesAddRequest[] lines)
        => new(w.C.PublicId, w.L.PublicId, Today, Today.AddDays(30), Lines: lines, ContractNumber: "CT-77", ContractSignedOn: Today.AddDays(-1),
            EstimatedDeliveryCost: 45m, TransportCurrency: "USD");

    private static Task<RentalDto> CreateAsync(World w, params RentalLinesAddRequest[] lines) => Run(w, s => s.CreateAsync(NewRental(w, lines), default));

    private static async Task<(decimal OnHand, decimal Reserved)> BalanceAsync(World w, int binId)
    {
        var b = await w.F.BalanceAsync(w.P.ProductId, binId);
        return (b?.QtyOnHand ?? 0m, b?.QtyReserved ?? 0m);
    }

    private static async Task<string> SerialStatusAsync(World w, string serial)
        => w.F.StatusCodeOf((await w.F.SerialAsync(w.P.ProductId, serial)).StatusCodeId!.Value);

    // ================================================================ flujo completo

    [Fact]
    public async Task Create_schedule_and_dispatch_keep_the_equipment_on_hand_in_EN_RENTA_with_zero_available()
    {
        var w = await SeedAsync();
        var created = await CreateAsync(w, Equip(w.P, "S-1", "S-3") with { Rate = new RentalLineRateInput("monthly", 120m) });
        Assert.Equal("REN-00001", created.Rental.Number);
        Assert.Equal(RentalStatuses.Draft, created.Rental.StatusCode);
        Assert.Equal((2, "CT-77", 45m, "USD"), (created.Rental.Units, created.Rental.ContractNumber, created.EstimatedDeliveryCost!.Value, created.TransportCurrencyCode));
        Assert.Equal((Today.AddDays(30), Today.AddDays(30), 30, false), (created.Rental.PickupDate, created.Rental.OriginalPickupDate, created.Rental.DaysToPickup, created.Rental.IsOverdue));
        Assert.Equal(new[] { ("S-1", "A-01"), ("S-3", "A-02") }, created.Lines.Select(l => (l.SerialNumber, l.FromBinCode)).ToArray());
        Assert.All(created.Lines, l => Assert.Equal(("MONTHLY", 120m, "USD", Today, (DateOnly?)null), (l.Rate!.FrequencyCode, l.Rate.Amount, l.Rate.CurrencyCode, l.Rate.EffectiveFrom, l.Rate.EffectiveTo)));
        Assert.True(created.CanSchedule && created.CanEdit && created.CanCancel && !created.CanDispatch && !created.CanExtend);
        Assert.Null(created.DeliveryShipmentId);
        Assert.Null(created.InvoiceId);
        // Borrador no reserva.
        Assert.Equal((2m, 0m), await BalanceAsync(w, w.A1.WarehouseBinId));
        Assert.Equal(SerialStatuses.Available, await SerialStatusAsync(w, "S-1"));

        var scheduled = await Run(w, s => s.ScheduleAsync(created.Rental.PublicId, new RentalStatusRequest("Confirmado por teléfono"), default));
        Assert.Equal(RentalStatuses.Scheduled, scheduled.Rental.StatusCode);
        Assert.Equal((2m, 1m), await BalanceAsync(w, w.A1.WarehouseBinId));
        Assert.Equal((1m, 1m), await BalanceAsync(w, w.A2.WarehouseBinId));
        Assert.Equal(SerialStatuses.Reserved, await SerialStatusAsync(w, "S-3"));
        Assert.True(scheduled.CanDispatch && scheduled.CanExtend && scheduled.CanCancel && !scheduled.CanSchedule);

        var dispatched = await Run(w, s => s.DispatchAsync(created.Rental.PublicId, null, default));
        Assert.Equal(RentalStatuses.OnRent, dispatched.Rental.StatusCode);
        Assert.NotNull(dispatched.Rental.DispatchedAtUtc);
        Assert.False(dispatched.CanEdit || dispatched.CanCancel || dispatched.CanDispatch);
        Assert.True(dispatched.CanExtend);
        Assert.All(dispatched.Lines, l => Assert.NotNull(l.DispatchTxnId));

        // Zona RENT (tipo RENTAL) y posición EN-RENTA creadas a demanda; ahí está lo rentado, en mano y reservado.
        var zone = await w.F.Db.WarehouseZones.AsNoTracking().SingleAsync(z => z.WarehouseId == w.W.WarehouseId && z.Code == "RENT");
        Assert.Equal(w.F.LookupId(LookupDomains.ZoneType, ZoneTypes.Rental), zone.ZoneTypeLookupId);
        var rentBin = await w.F.Db.WarehouseBins.AsNoTracking().SingleAsync(b => b.WarehouseId == w.W.WarehouseId && b.Code == "EN-RENTA");
        Assert.Equal(zone.WarehouseZoneId, rentBin.WarehouseZoneId);
        Assert.Equal((2m, 2m), await BalanceAsync(w, rentBin.WarehouseBinId));
        Assert.Equal((1m, 0m), await BalanceAsync(w, w.A1.WarehouseBinId));   // S-2 sigue disponible
        Assert.Equal((0m, 0m), await BalanceAsync(w, w.A2.WarehouseBinId));
        // En mano total igual (4: S-1, S-2, S-3 y S-Q); disponible = 2 (S-2 y S-Q).
        var all = await w.F.Db.StockBalances.AsNoTracking().Where(b => b.ProductId == w.P.ProductId).ToListAsync();
        Assert.Equal((4m, 2m), (all.Sum(b => b.QtyOnHand), all.Sum(b => b.QtyOnHand - b.QtyReserved)));
        foreach (var serial in new[] { "S-1", "S-3" })
        {
            var s = await w.F.SerialAsync(w.P.ProductId, serial);
            Assert.Equal((SerialStatuses.OnRent, rentBin.WarehouseBinId), (w.F.StatusCodeOf(s.StatusCodeId!.Value), s.CurrentBinId!.Value));
        }
        var txns = (await w.F.TransactionsAsync()).TakeLast(2).ToList();
        Assert.All(txns, t => Assert.Equal((w.F.LookupId(LookupDomains.InventoryTxnType, InventoryTxnTypes.Transfer), w.F.LookupId(LookupDomains.EntityType, EntityTypes.Rental),
            created.Rental.Id, "Renta REN-00001"), (t.TxnTypeLookupId, t.RefEntityLookupId!.Value, t.RefId!.Value, t.Notes)));
        Assert.Equal(dispatched.Lines.Select(l => l.DispatchTxnId!.Value).OrderBy(x => x), txns.Select(t => t.InventoryTransactionId).OrderBy(x => x));
        Assert.Equal(new[] { RentalStatuses.Draft, RentalStatuses.Scheduled, RentalStatuses.OnRent },
            await w.F.HistoryCodesAsync(EntityTypes.Rental, created.Rental.Id));

        // Despachada: no se modifica ni se cancela (mensajes exactos).
        var add = await Assert.ThrowsAsync<StatusRuleException>(() => Run(w, s => s.AddLinesAsync(created.Rental.PublicId, Equip(w.P, "S-2"), default)));
        Assert.Equal("La renta REN-00001 ya fue despachada; no se puede modificar.", add.Message);
        var patch = await Assert.ThrowsAsync<StatusRuleException>(() => Run(w, s => s.UpdateAsync(created.Rental.PublicId, new RentalPatchRequest(Notes: "x"), default)));
        Assert.Equal(add.Message, patch.Message);
        var rate = await Assert.ThrowsAsync<StatusRuleException>(() => Run(w, s => s.SetLineRateAsync(created.Rental.PublicId, created.Lines[0].Id, new RentalLineRateRequest("DAILY", 5m), default)));
        Assert.Equal(add.Message, rate.Message);
        var cancel = await Assert.ThrowsAsync<StatusRuleException>(() => Run(w, s => s.CancelAsync(created.Rental.PublicId, null, default)));
        Assert.Equal("Solo se cancela una renta en Borrador o Programada; para terminarla registre la devolución.", cancel.Message);
        await Assert.ThrowsAsync<StatusRuleException>(() => Run(w, s => s.DispatchAsync(created.Rental.PublicId, null, default)));
    }

    [Fact]
    public async Task A_second_dispatch_reuses_the_EN_RENTA_bin_and_a_rented_serial_cannot_be_moved_by_hand()
    {
        var w = await SeedAsync();
        foreach (var serial in new[] { "S-1", "S-2" })
        {
            var r = await CreateAsync(w, Equip(w.P, serial));
            await Run(w, s => s.ScheduleAsync(r.Rental.PublicId, null, default));
            await Run(w, s => s.DispatchAsync(r.Rental.PublicId, null, default));
        }
        Assert.Single(await w.F.Db.WarehouseZones.AsNoTracking().Where(z => z.WarehouseId == w.W.WarehouseId && z.Code == "RENT").ToListAsync());
        var rentBin = await w.F.Db.WarehouseBins.AsNoTracking().SingleAsync(b => b.WarehouseId == w.W.WarehouseId && b.Code == "EN-RENTA");
        Assert.Equal((2m, 2m), await BalanceAsync(w, rentBin.WarehouseBinId));

        // Mover la serie rentada (transferencia manual) → 409 del ledger.
        var ex = await Assert.ThrowsAsync<ConflictException>(() => w.F.PostAsync(new InventoryPosting(InventoryTxnTypes.Transfer, w.P.ProductId, 1m, SerialNumber: "S-1",
            FromWarehouseId: w.W.WarehouseId, FromBinId: rentBin.WarehouseBinId, ToWarehouseId: w.W.WarehouseId, ToBinId: w.A2.WarehouseBinId)));
        Assert.Equal("La serie S-1 no está disponible en EN-RENTA.", ex.Message);
    }

    // ================================================================ validaciones del alta

    [Fact]
    public async Task Create_validations_answer_with_the_exact_messages_and_write_nothing()
    {
        var w = await SeedAsync();
        async Task<TException> Fails<TException>(RentalCreateRequest req) where TException : Exception
            => await Assert.ThrowsAsync<TException>(() => Run(w, s => s.CreateAsync(req, default)));

        var noLocation = await Fails<ValidationException>(NewRental(w) with { LocationPublicId = null });
        Assert.Equal(new[] { "Indique la localidad del cliente donde estará el equipo." }, noLocation.Errors!["locationPublicId"]);
        var otherClient = await Fails<ValidationException>(NewRental(w) with { LocationPublicId = w.L2.PublicId });
        Assert.Equal("La localidad no pertenece al cliente de la renta.", otherClient.Message);
        var dates = await Fails<ValidationException>(NewRental(w) with { PickupDate = Today.AddDays(-1) });
        Assert.Equal(new[] { "La fecha de recogido no puede ser anterior a la de inicio." }, dates.Errors!["pickupDate"]);
        var owned = await Fails<ValidationException>(NewRental(w, Equip(w.POwned, "O-1")));
        Assert.Equal("Solo se rentan equipos propios; EQ-3PL pertenece a un cliente.", owned.Message);
        var notSerial = await Fails<ValidationException>(NewRental(w, Equip(w.PNone, "X-1")));
        Assert.Equal("El producto SUM-1 no se controla por serie; solo se rentan equipos con número de serie.", notSerial.Message);
        var unknown = await Fails<ConflictException>(NewRental(w, Equip(w.P, "NO-EXISTE")));
        Assert.Equal("La serie NO-EXISTE no está disponible en W1.", unknown.Message);
        var quarantine = await Fails<ConflictException>(NewRental(w, Equip(w.P, "S-Q")));
        Assert.Equal("La serie S-Q no está disponible en Q-01.", quarantine.Message);
        var negative = await Fails<ValidationException>(NewRental(w, Equip(w.P, "S-1") with { Rate = new RentalLineRateInput("DAILY", -1m) }));
        Assert.Equal("La tarifa no puede ser negativa.", negative.Message);
        var duplicated = await Fails<ValidationException>(NewRental(w, Equip(w.P, "S-1", "s-1")));
        Assert.Equal(SerialRules.Duplicated("s-1"), duplicated.Message);
        Assert.Equal(0, await w.F.Db.Rentals.CountAsync());

        // Cliente dado de baja: 409 (mensaje reutilizado del Lote 3).
        var client = await w.F.Db.Clients.SingleAsync(c => c.ClientId == w.C.ClientId);
        client.IsActive = false;
        await w.F.Db.SaveChangesAsync();
        w.F.Db.ChangeTracker.Clear();
        var inactive = await Fails<ConflictException>(NewRental(w));
        Assert.Equal("El cliente está dado de baja; solo se consulta su historial.", inactive.Message);
    }

    [Fact]
    public async Task A_serial_can_be_in_only_one_open_rental()
    {
        var w = await SeedAsync();
        await CreateAsync(w, Equip(w.P, "S-1"));
        var ex = await Assert.ThrowsAsync<ConflictException>(() => CreateAsync(w, Equip(w.P, "S-2", "S-1")));
        Assert.Equal("La serie S-1 ya está en la renta REN-00001.", ex.Message);
        Assert.Equal(1, await w.F.Db.Rentals.CountAsync());
    }

    // ================================================================ estatus

    [Fact]
    public async Task Schedule_needs_equipment_and_dispatch_needs_a_scheduled_rental()
    {
        var w = await SeedAsync();
        var empty = await CreateAsync(w);
        var noLines = await Assert.ThrowsAsync<StatusRuleException>(() => Run(w, s => s.ScheduleAsync(empty.Rental.PublicId, null, default)));
        Assert.Equal("La renta no tiene equipos; agregue al menos uno.", noLines.Message);
        var notScheduled = await Assert.ThrowsAsync<StatusRuleException>(() => Run(w, s => s.DispatchAsync(empty.Rental.PublicId, null, default)));
        Assert.Equal("Solo se despacha una renta Programada; programe la renta REN-00001 primero.", notScheduled.Message);

        // La serie se movió después de agregarla: se reserva donde está hoy (y FromBin se actualiza).
        await Run(w, s => s.AddLinesAsync(empty.Rental.PublicId, Equip(w.P, "S-1"), default));
        await w.F.PostAsync(new InventoryPosting(InventoryTxnTypes.Transfer, w.P.ProductId, 1m, SerialNumber: "S-1",
            FromWarehouseId: w.W.WarehouseId, FromBinId: w.A1.WarehouseBinId, ToWarehouseId: w.W.WarehouseId, ToBinId: w.A2.WarehouseBinId));
        var scheduled = await Run(w, s => s.ScheduleAsync(empty.Rental.PublicId, null, default));
        Assert.Equal("A-02", Assert.Single(scheduled.Lines).FromBinCode);
        Assert.Equal((2m, 1m), await BalanceAsync(w, w.A2.WarehouseBinId));
        var again = await Assert.ThrowsAsync<StatusRuleException>(() => Run(w, s => s.ScheduleAsync(empty.Rental.PublicId, null, default)));
        Assert.Equal("Solo se programa una renta en Borrador; la renta REN-00001 no lo está.", again.Message);
    }

    [Fact]
    public async Task Cancel_a_scheduled_rental_releases_the_reservation_and_frees_the_serial()
    {
        var w = await SeedAsync();
        var r = await CreateAsync(w, Equip(w.P, "S-1", "S-2"));
        await Run(w, s => s.ScheduleAsync(r.Rental.PublicId, null, default));
        Assert.Equal((2m, 2m), await BalanceAsync(w, w.A1.WarehouseBinId));

        var cancelled = await Run(w, s => s.CancelAsync(r.Rental.PublicId, new RentalStatusRequest("El cliente desistió"), default));
        Assert.Equal(RentalStatuses.Cancelled, cancelled.Rental.StatusCode);
        Assert.NotNull(cancelled.Rental.ClosedAtUtc);
        Assert.Equal(2, cancelled.Lines.Count);                 // los equipos que tenía, inactivos
        Assert.All(cancelled.Lines, l => Assert.False(l.IsActive));
        Assert.Equal(0, cancelled.Rental.Units);
        Assert.Equal((2m, 0m), await BalanceAsync(w, w.A1.WarehouseBinId));
        Assert.Equal(SerialStatuses.Available, await SerialStatusAsync(w, "S-1"));
        var history = await w.F.Db.EntityStatusHistories.AsNoTracking()
            .Where(h => h.EntityTypeLookupId == w.F.LookupId(LookupDomains.EntityType, EntityTypes.Rental) && h.EntityId == r.Rental.Id)
            .OrderBy(h => h.EntityStatusHistoryId).ToListAsync();
        Assert.Equal("El cliente desistió", history.Last().Comment);

        // La serie queda libre para otra renta; la cancelada ya no se cancela ni se edita.
        var other = await CreateAsync(w, Equip(w.P, "S-1"));
        Assert.Equal("REN-00002", other.Rental.Number);
        var again = await Assert.ThrowsAsync<StatusRuleException>(() => Run(w, s => s.CancelAsync(r.Rental.PublicId, null, default)));
        Assert.Equal(RentalRules.CancelNotAllowed, again.Message);
        var edit = await Assert.ThrowsAsync<StatusRuleException>(() => Run(w, s => s.UpdateAsync(r.Rental.PublicId, new RentalPatchRequest(Notes: "x"), default)));
        Assert.Equal("La renta REN-00001 está cancelada; solo se consulta.", edit.Message);
    }

    [Fact]
    public async Task Cancel_a_draft_rental_has_nothing_to_release()
    {
        var w = await SeedAsync();
        var r = await CreateAsync(w, Equip(w.P, "S-1"));
        var cancelled = await Run(w, s => s.CancelAsync(r.Rental.PublicId, null, default));
        Assert.Equal(RentalStatuses.Cancelled, cancelled.Rental.StatusCode);
        Assert.Equal((2m, 0m), await BalanceAsync(w, w.A1.WarehouseBinId));
    }

    [Fact]
    public async Task Equipment_added_or_removed_while_scheduled_reserves_or_releases_it()
    {
        var w = await SeedAsync();
        var r = await CreateAsync(w, Equip(w.P, "S-1"));
        await Run(w, s => s.ScheduleAsync(r.Rental.PublicId, null, default));

        var added = await Run(w, s => s.AddLinesAsync(r.Rental.PublicId, Equip(w.P, "S-3") with { Rate = new RentalLineRateInput("ONE_TIME", 300m) }, default));
        Assert.Equal(2, added.Rental.Units);
        Assert.Equal((1m, 1m), await BalanceAsync(w, w.A2.WarehouseBinId));
        Assert.Equal("ONE_TIME", added.Lines.Single(l => l.SerialNumber == "S-3").Rate!.FrequencyCode);

        var line = added.Lines.Single(l => l.SerialNumber == "S-1");
        var removed = await Run(w, s => s.RemoveLineAsync(r.Rental.PublicId, line.Id, default));
        Assert.Equal(new[] { "S-3" }, removed.Lines.Select(l => l.SerialNumber).ToArray());
        Assert.Equal((2m, 0m), await BalanceAsync(w, w.A1.WarehouseBinId));
        Assert.Equal(SerialStatuses.Available, await SerialStatusAsync(w, "S-1"));
        await Assert.ThrowsAsync<NotFoundException>(() => Run(w, s => s.RemoveLineAsync(r.Rental.PublicId, line.Id, default)));

        // Tarifa vigente editada en su lugar antes del despacho (una sola versión).
        var s3 = removed.Lines.Single();
        var repriced = await Run(w, s => s.SetLineRateAsync(r.Rental.PublicId, s3.Id, new RentalLineRateRequest("WEEKLY", 80m, "EUR"), default));
        var rate = Assert.Single(repriced.Lines.Single().RateHistory);
        Assert.Equal(("WEEKLY", 80m, "EUR", Today), (rate.FrequencyCode, rate.Amount, rate.CurrencyCode, rate.EffectiveFrom));
        var bad = await Assert.ThrowsAsync<ValidationException>(() => Run(w, s => s.SetLineRateAsync(r.Rental.PublicId, s3.Id, new RentalLineRateRequest("YEARLY", 1m), default)));
        Assert.Equal(RentalRules.UnknownFrequency("YEARLY"), bad.Message);
    }

    // ================================================================ extensiones (D4)

    [Fact]
    public async Task Extend_records_the_log_moves_the_pickup_date_and_versions_the_rate_only_if_it_changes()
    {
        var w = await SeedAsync();
        var r = await CreateAsync(w, Equip(w.P, "S-1", "S-3") with { Rate = new RentalLineRateInput("MONTHLY", 100m) });
        var draft = await Assert.ThrowsAsync<StatusRuleException>(() => Run(w, s => s.ExtendAsync(r.Rental.PublicId,
            new RentalExtendRequest(Today.AddDays(40), "más tiempo"), default)));
        Assert.Equal("Solo se extiende una renta Programada o En renta.", draft.Message);

        await Run(w, s => s.ScheduleAsync(r.Rental.PublicId, null, default));
        await Run(w, s => s.DispatchAsync(r.Rental.PublicId, null, default));
        var same = await Assert.ThrowsAsync<ValidationException>(() => Run(w, s => s.ExtendAsync(r.Rental.PublicId,
            new RentalExtendRequest(Today.AddDays(30), "más tiempo"), default)));
        Assert.Equal(new[] { "La nueva fecha de recogido debe ser posterior a la actual (2026-11-04)." }, same.Errors!["newPickupDate"]);
        var noReason = await Assert.ThrowsAsync<ValidationException>(() => Run(w, s => s.ExtendAsync(r.Rental.PublicId,
            new RentalExtendRequest(Today.AddDays(45), "  "), default)));
        Assert.Equal(new[] { "Indique el motivo de la extensión." }, noReason.Errors!["reason"]);

        var s1 = r.Lines.Single(l => l.SerialNumber == "S-1");
        var s3 = r.Lines.Single(l => l.SerialNumber == "S-3");
        var extended = await Run(w, s => s.ExtendAsync(r.Rental.PublicId, new RentalExtendRequest(Today.AddDays(45), "El hospital pidió dos semanas más",
            new[] { new RentalExtensionRateInput(s1.Id, "MONTHLY", 110m), new RentalExtensionRateInput(s3.Id, "MONTHLY", 100m) }), default));
        Assert.Equal((Today.AddDays(45), Today.AddDays(30), 1), (extended.Rental.PickupDate, extended.Rental.OriginalPickupDate, extended.Rental.ExtensionCount));
        var history1 = extended.Lines.Single(l => l.Id == s1.Id).RateHistory;
        Assert.Equal(2, history1.Count);
        Assert.Equal((Today, (DateOnly?)Today.AddDays(31)), (history1[0].EffectiveFrom, history1[0].EffectiveTo));   // EffectiveTo exclusivo
        Assert.Equal((Today.AddDays(31), (DateOnly?)null, 110m), (history1[1].EffectiveFrom, history1[1].EffectiveTo, history1[1].Amount));
        Assert.NotNull(history1[1].ExtensionId);
        Assert.Single(extended.Lines.Single(l => l.Id == s3.Id).RateHistory);   // misma tarifa: sin versión nueva

        var log = Assert.Single(await Run(w, s => s.ListExtensionsAsync(r.Rental.PublicId, default)));
        Assert.Equal((Today.AddDays(30), Today.AddDays(45), 15, "El hospital pidió dos semanas más"), (log.PreviousPickupDate, log.NewPickupDate, log.DaysAdded, log.Reason));
        Assert.Equal(new[] { "S-1" }, log.Rates.Select(x => x.SerialNumber).ToArray());

        // Una segunda extensión sin tarifas solo mueve la fecha.
        var second = await Run(w, s => s.ExtendAsync(r.Rental.PublicId, new RentalExtendRequest(Today.AddDays(60), "Otra vez"), default));
        Assert.Equal((Today.AddDays(60), 2), (second.Rental.PickupDate, second.Rental.ExtensionCount));
    }

    // ================================================================ edición

    [Fact]
    public async Task Update_moves_dates_and_rates_together_and_guards_client_warehouse_and_extended_dates()
    {
        var w = await SeedAsync();
        var r = await CreateAsync(w, Equip(w.P, "S-1") with { Rate = new RentalLineRateInput("DAILY", 10m) });
        var moved = await Run(w, s => s.UpdateAsync(r.Rental.PublicId, new RentalPatchRequest(StartDate: Today.AddDays(2), PickupDate: Today.AddDays(20),
            ContractNumber: "", Notes: "Con cargador"), default));
        Assert.Equal((Today.AddDays(2), Today.AddDays(20), Today.AddDays(20)), (moved.Rental.StartDate, moved.Rental.PickupDate, moved.Rental.OriginalPickupDate));
        Assert.Equal(Today.AddDays(2), moved.Lines.Single().Rate!.EffectiveFrom);
        Assert.Null(moved.Rental.ContractNumber);
        Assert.Equal("Con cargador", moved.Notes);

        var bad = await Assert.ThrowsAsync<ValidationException>(() => Run(w, s => s.UpdateAsync(r.Rental.PublicId, new RentalPatchRequest(PickupDate: Today), default)));
        Assert.Equal(RentalRules.PickupBeforeStart, bad.Message);
        var client = await Assert.ThrowsAsync<ValidationException>(() => Run(w, s => s.UpdateAsync(r.Rental.PublicId,
            new RentalPatchRequest { Extra = new Dictionary<string, System.Text.Json.JsonElement> { ["clientPublicId"] = default } }, default)));
        Assert.Equal("El cliente de la renta no se cambia; cancele la renta y cree otra.", client.Message);
        var location = await Assert.ThrowsAsync<ValidationException>(() => Run(w, s => s.UpdateAsync(r.Rental.PublicId, new RentalPatchRequest(LocationPublicId: w.L2.PublicId), default)));
        Assert.Equal(RentalRules.LocationNotOfClient, location.Message);
        var w2 = await w.F.AddWarehouseAsync("W2");
        var warehouse = await Assert.ThrowsAsync<ConflictException>(() => Run(w, s => s.UpdateAsync(r.Rental.PublicId, new RentalPatchRequest(WarehousePublicId: w2.PublicId), default)));
        Assert.Equal("Quite los equipos de la renta antes de cambiar el almacén de origen.", warehouse.Message);

        await Run(w, s => s.ScheduleAsync(r.Rental.PublicId, null, default));
        await Run(w, s => s.ExtendAsync(r.Rental.PublicId, new RentalExtendRequest(Today.AddDays(25), "Ajuste"), default));
        var locked = await Assert.ThrowsAsync<StatusRuleException>(() => Run(w, s => s.UpdateAsync(r.Rental.PublicId, new RentalPatchRequest(PickupDate: Today.AddDays(28)), default)));
        Assert.Equal("La renta ya tiene extensiones; la fecha de recogido se cambia con una extensión.", locked.Message);
    }

    // ================================================================ lista

    [Fact]
    public async Task List_filters_due_soon_overdue_status_client_and_search()
    {
        var w = await SeedAsync();
        // REN-1: En renta y vencida (recogido ayer); REN-2: Programada que vence en 5 días; REN-3: Borrador (no cuenta).
        var r1 = await Run(w, s => s.CreateAsync(NewRental(w, Equip(w.P, "S-1")) with { StartDate = Today.AddDays(-20), PickupDate = Today.AddDays(-1) }, default));
        await Run(w, s => s.ScheduleAsync(r1.Rental.PublicId, null, default));
        await Run(w, s => s.DispatchAsync(r1.Rental.PublicId, null, default));
        var r2 = await Run(w, s => s.CreateAsync(NewRental(w, Equip(w.P, "S-2")) with { PickupDate = Today.AddDays(5), ContractNumber = "HOSP-55" }, default));
        await Run(w, s => s.ScheduleAsync(r2.Rental.PublicId, null, default));
        await Run(w, s => s.CreateAsync(NewRental(w, Equip(w.P, "S-3")) with { PickupDate = Today.AddDays(3) }, default));

        var overdue = await Run(w, s => s.ListAsync(new RentalQuery(Overdue: true), default));
        Assert.Equal(new[] { "REN-00001" }, overdue.Items.Select(i => i.Number).ToArray());
        Assert.True(overdue.Items[0].IsOverdue);
        Assert.Equal(-1, overdue.Items[0].DaysToPickup);
        var due = await Run(w, s => s.ListAsync(new RentalQuery(DueWithinDays: 7), default));
        Assert.Equal(new[] { "REN-00002" }, due.Items.Select(i => i.Number).ToArray());
        var both = await Run(w, s => s.ListAsync(new RentalQuery(DueWithinDays: 7, Overdue: true), default));
        Assert.Equal(new[] { "REN-00001", "REN-00002" }, both.Items.Select(i => i.Number).ToArray());
        Assert.Equal(new[] { "REN-00003" }, (await Run(w, s => s.ListAsync(new RentalQuery(Status: new[] { "draft" }), default))).Items.Select(i => i.Number).ToArray());
        Assert.Equal(3, (await Run(w, s => s.ListAsync(new RentalQuery(ClientPublicId: w.C.PublicId), default))).Total);
        Assert.Equal(new[] { "REN-00002" }, (await Run(w, s => s.ListAsync(new RentalQuery(Search: "HOSP"), default))).Items.Select(i => i.Number).ToArray());
        Assert.Equal(new[] { "REN-00003" }, (await Run(w, s => s.ListAsync(new RentalQuery(Search: "S-3"), default))).Items.Select(i => i.Number).ToArray());
        var negative = await Assert.ThrowsAsync<ValidationException>(() => Run(w, s => s.ListAsync(new RentalQuery(DueWithinDays: -1), default)));
        Assert.Equal("Los días deben ser 0 o más.", negative.Message);
    }

    // ================================================================ aislamiento por compañía

    [Fact]
    public async Task Another_company_sees_nothing_and_cannot_touch_the_rental()
    {
        var w = await SeedAsync();
        var r = await CreateAsync(w, Equip(w.P, "S-1"));
        using (w.F.AsTenant(WmsFixture.OtherTenantId))
        {
            Assert.Equal(0, (await Run(w, s => s.ListAsync(null, default))).Total);
            var get = await Assert.ThrowsAsync<NotFoundException>(() => Run(w, s => s.GetAsync(r.Rental.PublicId, default)));
            Assert.Equal("Renta no encontrada.", get.Message);
            await Assert.ThrowsAsync<NotFoundException>(() => Run(w, s => s.ScheduleAsync(r.Rental.PublicId, null, default)));
            await Assert.ThrowsAsync<NotFoundException>(() => Run(w, s => s.DispatchAsync(r.Rental.PublicId, null, default)));
            await Assert.ThrowsAsync<NotFoundException>(() => Run(w, s => s.CancelAsync(r.Rental.PublicId, null, default)));
            await Assert.ThrowsAsync<NotFoundException>(() => Run(w, s => s.ExtendAsync(r.Rental.PublicId, new RentalExtendRequest(Today.AddDays(50), "x"), default)));
            await Assert.ThrowsAsync<NotFoundException>(() => Run(w, s => s.ListExtensionsAsync(r.Rental.PublicId, default)));
            // Ni su cliente, ni su localidad, ni su producto existen para la otra compañía.
            await Assert.ThrowsAsync<NotFoundException>(() => Run(w, s => s.CreateAsync(NewRental(w), default)));
            Assert.False(await new RentalOwnedEntityResolver(w.F.Db).ExistsInTenantAsync(r.Rental.Id, default));
        }
        Assert.True(await new RentalOwnedEntityResolver(w.F.Db).ExistsInTenantAsync(r.Rental.Id, default));
        Assert.Equal(RentalStatuses.Draft, (await Run(w, s => s.GetAsync(r.Rental.PublicId, default))).Rental.StatusCode);
    }
}
