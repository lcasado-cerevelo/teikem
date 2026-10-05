using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Constants;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 28 (Rentas R2) — RentalReturnService en InMemory con el ledger, StatusService y los efectos REALES (RentalReturnWorld):
/// devolución completa (TRANSFER desde EN-RENTA a la posición destino; con proceso queda reservada e IN_PROCESS, sin proceso
/// AVAILABLE; la renta pasa a Devuelta), devolución ANTICIPADA POR DAÑO parcial (la renta sigue En renta hasta que vuelve el
/// resto), validaciones con los mensajes exactos sin escribir nada, lista y ficha, y aislamiento por compañía (404). El bloqueo
/// real de SQL Server y la concurrencia los cubre el smoke.
/// </summary>
public sealed class RentalReturnServiceTests
{
    private static readonly DateOnly Today = RentalReturnWorld.Today;

    [Fact]
    public async Task Full_return_moves_each_unit_out_of_EN_RENTA_opens_a_process_when_needed_and_closes_the_rental()
    {
        var w = await RentalReturnWorld.CreateAsync();
        var rental = await w.DispatchedAsync("S-1", "S-3");
        var rentBin = await w.RentBinAsync();
        Assert.Equal((3m, 1m), await w.TotalsAsync());

        var ret = await w.Returns(s => s.CreateAsync(rental.Rental.PublicId, new RentalReturnCreateRequest(RentalReturnReasons.EndOfContract.ToLowerInvariant(),
            new[]
            {
                RentalReturnWorld.Line("S-1", requiresProcess: false),                                       // a la posición de donde salió (A-01)
                RentalReturnWorld.Line("s-3", RentalReturnConditions.Damaged, w.Q1.WarehouseBinId, notes: "Rueda floja"),   // a cuarentena, con proceso
            }, EstimatedPickupCost: 30m, TransportCurrency: "USD"), default));

        Assert.Equal("DRN-00001", ret.Return.Number);
        Assert.Equal((RentalReturnReasons.EndOfContract, Today, true, 2, 1), (ret.Return.ReasonCode, ret.Return.ReturnedOn, ret.Return.IsEarly, ret.Return.Units, ret.Return.OpenProcesses));
        Assert.Equal((RentalStatuses.Returned, 30m, "USD", (int?)null), (ret.RentalStatusCode, ret.EstimatedPickupCost!.Value, ret.TransportCurrencyCode, ret.PickupShipmentId));
        var s1 = ret.Lines.Single(l => l.SerialNumber == "S-1");
        Assert.Equal(("A-01", false, RentalReturnConditions.Good, (int?)null), (s1.ToBinCode, s1.RequiresProcess, s1.ConditionCode, s1.ProcessId));
        var s3 = ret.Lines.Single(l => l.SerialNumber == "S-3");
        Assert.Equal(("Q-01", true, RentalReturnConditions.Damaged, RentalProcessStatuses.Pending, "Rueda floja"), (s3.ToBinCode, s3.RequiresProcess, s3.ConditionCode, s3.ProcessStatusCode, s3.Notes));
        Assert.NotNull(s3.ProcessId);

        // Inventario: EN-RENTA vacía; S-1 disponible en A-01; S-3 en Q-01 en mano y reservada (en proceso: no cuenta como disponible).
        Assert.Equal((0m, 0m), await w.BalanceAsync(rentBin.WarehouseBinId));
        Assert.Equal((2m, 0m), await w.BalanceAsync(w.A1.WarehouseBinId));
        Assert.Equal((1m, 1m), await w.BalanceAsync(w.Q1.WarehouseBinId));
        Assert.Equal((3m, 2m), await w.TotalsAsync());
        Assert.Equal((SerialStatuses.Available, (int?)w.A1.WarehouseBinId), await w.SerialAsync("S-1"));
        Assert.Equal((SerialStatuses.InProcess, (int?)w.Q1.WarehouseBinId), await w.SerialAsync("S-3"));
        var history = await w.F.HistoryCodesAsync(EntityTypes.InventorySerial, (await w.F.SerialAsync(w.P.ProductId, "S-3")).SerialId);
        Assert.Equal(SerialStatuses.InProcess, history.Last());

        // Kárdex: dos TRANSFER neutras con la referencia de la devolución y su nota; enlazadas en cada equipo devuelto.
        var txns = (await w.F.TransactionsAsync()).TakeLast(2).ToList();
        Assert.All(txns, t => Assert.Equal((w.F.LookupId(LookupDomains.InventoryTxnType, InventoryTxnTypes.Transfer), w.F.LookupId(LookupDomains.EntityType, EntityTypes.RentalReturn),
            ret.Return.Id, "Devolución de renta DRN-00001 (REN-00001)", (int?)rentBin.WarehouseBinId), (t.TxnTypeLookupId, t.RefEntityLookupId!.Value, t.RefId!.Value, t.Notes, t.FromBinId)));
        Assert.Equal(ret.Lines.Select(l => l.ReturnTxnId!.Value).OrderBy(x => x), txns.Select(t => t.InventoryTransactionId).OrderBy(x => x));

        // La renta: Devuelta (terminal) con fecha de cierre; sus equipos marcados devueltos; historial con la devolución.
        var closed = await w.Rentals(s => s.GetAsync(rental.Rental.PublicId, default));
        Assert.Equal(RentalStatuses.Returned, closed.Rental.StatusCode);
        Assert.NotNull(closed.Rental.ClosedAtUtc);
        Assert.All(closed.Lines, l => Assert.NotNull(l.ReturnedAtUtc));
        Assert.False(closed.CanExtend || closed.CanEdit || closed.CanCancel);
        Assert.Equal(new[] { RentalStatuses.Draft, RentalStatuses.Scheduled, RentalStatuses.OnRent, RentalStatuses.Returned },
            await w.F.HistoryCodesAsync(EntityTypes.Rental, rental.Rental.Id));
        Assert.Equal(new[] { RentalProcessStatuses.Pending }, await w.F.HistoryCodesAsync(EntityTypes.RentalProcess, s3.ProcessId!.Value));
        var process = await w.F.Db.RentalProcesses.AsNoTracking().SingleAsync();
        Assert.Equal((s3.ProcessId.Value, w.Q1.WarehouseBinId, (int?)s3.Id, "Rueda floja"), (process.RentalProcessId, process.BinId, process.RentalReturnLineId, process.Notes));

        // Devuelta: ya no admite otra devolución (422); la serie devuelta ya no está "en renta" en ninguna renta abierta.
        var again = await Assert.ThrowsAsync<StatusRuleException>(() => w.Returns(s => s.CreateAsync(rental.Rental.PublicId,
            new RentalReturnCreateRequest("END_OF_CONTRACT", new[] { RentalReturnWorld.Line("S-1") }), default)));
        Assert.Equal("Solo se registra la devolución de una renta En renta; la renta REN-00001 no lo está.", again.Message);

        // Ficha y lista.
        var got = await w.Returns(s => s.GetAsync(ret.Return.PublicId, default));
        Assert.Equal(2, got.Lines.Count);
        var list = await w.Returns(s => s.ListAsync(new RentalReturnQuery(RentalPublicId: rental.Rental.PublicId), default));
        Assert.Equal(new[] { "DRN-00001" }, list.Items.Select(i => i.Number).ToArray());
        Assert.Equal(1, (await w.Returns(s => s.ListAsync(new RentalReturnQuery(Search: "S-3"), default))).Total);
        Assert.Equal(0, (await w.Returns(s => s.ListAsync(new RentalReturnQuery(Reason: new[] { "EARLY_DAMAGE" }), default))).Total);
    }

    [Fact]
    public async Task Early_return_for_damage_is_partial_until_the_rest_comes_back()
    {
        var w = await RentalReturnWorld.CreateAsync();
        var rental = await w.DispatchedAsync("S-1", "S-3");

        // Se dañó S-3 a mitad de la renta: vuelve sola, anticipada por daño, a cuarentena y con proceso (por defecto).
        var damaged = await w.Returns(s => s.CreateAsync(rental.Rental.PublicId, new RentalReturnCreateRequest("EARLY_DAMAGE",
            new[] { RentalReturnWorld.Line("S-3", "DAMAGED") }, ToBinId: w.Q1.WarehouseBinId, Notes: "Pantalla rota"), default));
        Assert.Equal((RentalReturnReasons.EarlyDamage, true, RentalStatuses.OnRent, 1, "Pantalla rota"),
            (damaged.Return.ReasonCode, damaged.Return.IsEarly, damaged.RentalStatusCode, damaged.Return.OpenProcesses, damaged.Notes));
        Assert.True(Assert.Single(damaged.Lines).RequiresProcess);
        Assert.Equal((SerialStatuses.InProcess, (int?)w.Q1.WarehouseBinId), await w.SerialAsync("S-3"));
        Assert.Equal((SerialStatuses.OnRent, (int?)(await w.RentBinAsync()).WarehouseBinId), await w.SerialAsync("S-1"));
        var still = await w.Rentals(s => s.GetAsync(rental.Rental.PublicId, default));
        Assert.Equal(RentalStatuses.OnRent, still.Rental.StatusCode);
        Assert.Null(still.Rental.ClosedAtUtc);
        Assert.True(still.CanExtend);

        // S-3 ya volvió: otra devolución con ella → 409 (no está en renta); el filtro de anticipadas la encuentra.
        var twice = await Assert.ThrowsAsync<ConflictException>(() => w.Returns(s => s.CreateAsync(rental.Rental.PublicId,
            new RentalReturnCreateRequest("EARLY_DAMAGE", new[] { RentalReturnWorld.Line("S-3") }), default)));
        Assert.Equal("La serie S-3 no está en renta en REN-00001.", twice.Message);
        Assert.Equal(new[] { damaged.Return.Number }, (await w.Returns(s => s.ListAsync(new RentalReturnQuery(Early: true, Reason: new[] { "early_damage" }), default)))
            .Items.Select(i => i.Number).ToArray());

        // Vuelve el resto (sin proceso): la renta pasa a Devuelta con la segunda devolución.
        var rest = await w.Returns(s => s.CreateAsync(rental.Rental.PublicId, new RentalReturnCreateRequest("END_OF_CONTRACT",
            new[] { RentalReturnWorld.Line("S-1", requiresProcess: false) }), default));
        Assert.Equal(("DRN-00002", RentalStatuses.Returned), (rest.Return.Number, rest.RentalStatusCode));
        Assert.Equal(2, (await w.Returns(s => s.ListAsync(new RentalReturnQuery(ClientPublicId: w.C.PublicId), default))).Total);
    }

    [Fact]
    public async Task Return_validations_answer_with_the_exact_messages_and_write_nothing()
    {
        var w = await RentalReturnWorld.CreateAsync();
        var rental = await w.DispatchedAsync("S-1");
        var rentBin = await w.RentBinAsync();
        async Task<TException> Fails<TException>(RentalReturnCreateRequest req, Guid? rentalPublicId = null) where TException : Exception
            => await Assert.ThrowsAsync<TException>(() => w.Returns(s => s.CreateAsync(rentalPublicId ?? rental.Rental.PublicId, req, default)));

        var other = await Fails<ValidationException>(new RentalReturnCreateRequest("OTHER", new[] { RentalReturnWorld.Line("S-1") }));
        Assert.Equal("Con el motivo 'Otro' describa la devolución en las notas.", other.Message);
        var rentalZone = await Fails<ValidationException>(new RentalReturnCreateRequest("END_OF_CONTRACT", new[] { RentalReturnWorld.Line("S-1", toBinId: rentBin.WarehouseBinId) }));
        Assert.Equal("La posición de destino no puede ser de la zona En renta.", rentalZone.Message);
        var notInRental = await Fails<ConflictException>(new RentalReturnCreateRequest("END_OF_CONTRACT", new[] { RentalReturnWorld.Line("S-1"), RentalReturnWorld.Line("S-2") }));
        Assert.Equal("La serie S-2 no está en renta en REN-00001.", notInRental.Message);
        var noReason = await Fails<ValidationException>(new RentalReturnCreateRequest(null, new[] { RentalReturnWorld.Line("S-1") }));
        Assert.Equal(RentalRules.ReturnReasonRequired, noReason.Message);
        var unknownReason = await Fails<ValidationException>(new RentalReturnCreateRequest("LOST", new[] { RentalReturnWorld.Line("S-1") }));
        Assert.Equal("Motivo de devolución desconocido: 'LOST'. Use END_OF_CONTRACT, EARLY_DAMAGE, EARLY_CLIENT u OTHER.", unknownReason.Message);
        var noLines = await Fails<ValidationException>(new RentalReturnCreateRequest("END_OF_CONTRACT", Array.Empty<RentalReturnLineInput>()));
        Assert.Equal("Indique al menos una serie que se devuelve.", noLines.Message);
        var condition = await Fails<ValidationException>(new RentalReturnCreateRequest("END_OF_CONTRACT", new[] { RentalReturnWorld.Line("S-1", "BROKEN") }));
        Assert.Equal("Condición desconocida: 'BROKEN'. Use GOOD, DAMAGED o INCOMPLETE.", condition.Message);
        var duplicated = await Fails<ValidationException>(new RentalReturnCreateRequest("END_OF_CONTRACT", new[] { RentalReturnWorld.Line("S-1"), RentalReturnWorld.Line("s-1") }));
        Assert.Equal(SerialRules.Duplicated("s-1"), duplicated.Message);
        var future = await Fails<ValidationException>(new RentalReturnCreateRequest("END_OF_CONTRACT", new[] { RentalReturnWorld.Line("S-1") }, Today.AddDays(1)));
        Assert.Equal("La fecha de devolución no puede ser futura.", future.Message);
        var beforeStart = await Fails<ValidationException>(new RentalReturnCreateRequest("END_OF_CONTRACT", new[] { RentalReturnWorld.Line("S-1") }, Today.AddDays(-1)));
        Assert.Equal("La fecha de devolución no puede ser anterior al inicio de la renta (2026-10-05).", beforeStart.Message);
        var cost = await Fails<ValidationException>(new RentalReturnCreateRequest("END_OF_CONTRACT", new[] { RentalReturnWorld.Line("S-1") }, EstimatedPickupCost: -1m));
        Assert.Equal("El costo de recogido estimado no puede ser negativo.", cost.Message);
        await Fails<NotFoundException>(new RentalReturnCreateRequest("END_OF_CONTRACT", new[] { RentalReturnWorld.Line("S-1", toBinId: 99999) }));
        await Fails<NotFoundException>(new RentalReturnCreateRequest("END_OF_CONTRACT", new[] { RentalReturnWorld.Line("S-1") }), Guid.NewGuid());

        // Una renta en Borrador no se devuelve (422).
        var draft = await w.Rentals(s => s.CreateAsync(new RentalCreateRequest(w.C.PublicId, w.L.PublicId, Today, Today.AddDays(5),
            Lines: new[] { new RentalLinesAddRequest(w.P.PublicId, new[] { "S-2" }) }), default));
        var notOnRent = await Fails<StatusRuleException>(new RentalReturnCreateRequest("END_OF_CONTRACT", new[] { RentalReturnWorld.Line("S-2") }), draft.Rental.PublicId);
        Assert.Equal("Solo se registra la devolución de una renta En renta; la renta REN-00002 no lo está.", notOnRent.Message);

        // Nada se escribió: sin devoluciones ni procesos; la serie sigue en renta.
        Assert.Equal(0, await w.F.Db.RentalReturns.CountAsync());
        Assert.Equal(0, await w.F.Db.RentalProcesses.CountAsync());
        Assert.Equal((SerialStatuses.OnRent, (int?)rentBin.WarehouseBinId), await w.SerialAsync("S-1"));
    }

    [Fact]
    public async Task Another_company_cannot_return_see_or_process_the_equipment()
    {
        var w = await RentalReturnWorld.CreateAsync();
        var rental = await w.DispatchedAsync("S-1", "S-3");
        var ret = await w.Returns(s => s.CreateAsync(rental.Rental.PublicId, new RentalReturnCreateRequest("EARLY_CLIENT",
            new[] { RentalReturnWorld.Line("S-3") }), default));
        var processId = Assert.Single(ret.Lines).ProcessId!.Value;

        using (w.F.AsTenant(WmsFixture.OtherTenantId))
        {
            var create = await Assert.ThrowsAsync<NotFoundException>(() => w.Returns(s => s.CreateAsync(rental.Rental.PublicId,
                new RentalReturnCreateRequest("END_OF_CONTRACT", new[] { RentalReturnWorld.Line("S-1") }), default)));
            Assert.Equal("Renta no encontrada.", create.Message);
            var get = await Assert.ThrowsAsync<NotFoundException>(() => w.Returns(s => s.GetAsync(ret.Return.PublicId, default)));
            Assert.Equal("Devolución de renta no encontrada.", get.Message);
            Assert.Equal(0, (await w.Returns(s => s.ListAsync(null, default))).Total);
            Assert.Equal(0, (await w.Processes(s => s.ListAsync(null, default))).Total);
            var advance = await Assert.ThrowsAsync<NotFoundException>(() => w.Processes(s => s.AdvanceAsync(processId, new RentalProcessAdvanceRequest("INSPECTION"), default)));
            Assert.Equal("Proceso no encontrado.", advance.Message);
            await Assert.ThrowsAsync<NotFoundException>(() => w.Processes(s => s.CompleteAsync(processId, null, default)));
            await Assert.ThrowsAsync<NotFoundException>(() => w.Processes(s => s.ScrapAsync(processId, null, default)));
            Assert.False(await new RentalReturnOwnedEntityResolver(w.F.Db).ExistsInTenantAsync(ret.Return.Id, default));
            Assert.False(await new RentalProcessOwnedEntityResolver(w.F.Db).ExistsInTenantAsync(processId, default));
        }
        Assert.True(await new RentalReturnOwnedEntityResolver(w.F.Db).ExistsInTenantAsync(ret.Return.Id, default));
        Assert.True(await new RentalProcessOwnedEntityResolver(w.F.Db).ExistsInTenantAsync(processId, default));
        // Nada cambió: S-1 sigue en renta y el proceso sigue Pendiente.
        Assert.Equal(SerialStatuses.OnRent, (await w.SerialAsync("S-1")).Status);
        Assert.Equal(RentalProcessStatuses.Pending, (await w.Processes(s => s.GetAsync(processId, default))).StatusCode);
    }
}
