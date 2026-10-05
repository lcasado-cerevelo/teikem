using Teikem.Domain.Constants;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Wms;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 27 (Rentas R1, plan 4.1) — parámetros nuevos del ledger con el InventoryLedger REAL (WmsFixture, InMemory):
/// - InventoryPosting.TargetSerialStatus fija el estatus de la serie tras el movimiento (TRANSFER → ON_RENT) y
///   ReserveAtDestination deja lo que entra reservado en el destino (en mano y reservado suben juntos);
/// - ExpectedSerialStatus exige otro estatus al salir (ON_RENT desde EN-RENTA, para la devolución de R2);
/// - StockReservation reserva y libera con estatus esperado y destino propios (IN_PROCESS → AVAILABLE al terminar un proceso);
/// - sin los parámetros nuevos todo se comporta como antes (una serie ON_RENT no se mueve ni se recibe de nuevo).
/// </summary>
public sealed class RentalLedgerTests
{
    private sealed record World(WmsFixture F, Warehouse W, WarehouseBin Pick, WarehouseBin Rent, WarehouseBin Other, Product P);

    private static async Task<World> SeedAsync()
    {
        var f = await WmsFixture.CreateAsync();
        var w = await f.AddWarehouseAsync("W1");
        var pick = await f.AddZoneAsync(w, "PCK", ZoneTypes.Picking);
        var rentZone = await f.AddZoneAsync(w, "RENT", ZoneTypes.Rental);
        var a = await f.AddBinAsync(pick, "A-01");
        var other = await f.AddBinAsync(pick, "A-02");
        var rent = await f.AddBinAsync(rentZone, "EN-RENTA");
        var p = await f.AddProductAsync("EQ-1", TrackingTypes.Serial);
        await f.PostAsync(new InventoryPosting(InventoryTxnTypes.Receipt, p.ProductId, 1m, SerialNumber: "S-1", ToWarehouseId: w.WarehouseId, ToBinId: a.WarehouseBinId),
            new InventoryPosting(InventoryTxnTypes.Receipt, p.ProductId, 1m, SerialNumber: "S-2", ToWarehouseId: w.WarehouseId, ToBinId: a.WarehouseBinId));
        return new World(f, w, a, rent, other, p);
    }

    private static InventoryPosting Dispatch(World w, string serial, bool fromReserved = true) => new(InventoryTxnTypes.Transfer, w.P.ProductId, 1m,
        SerialNumber: serial, FromWarehouseId: w.W.WarehouseId, FromBinId: w.Pick.WarehouseBinId, ToWarehouseId: w.W.WarehouseId, ToBinId: w.Rent.WarehouseBinId,
        RefEntityType: EntityTypes.Rental, RefId: 1, FromReserved: fromReserved, TargetSerialStatus: SerialStatuses.OnRent, ReserveAtDestination: true);

    private static StockReservation Reserve(World w, WarehouseBin bin, string serial, string? expected = null, string? target = null)
        => new(w.P.ProductId, w.W.WarehouseId, bin.WarehouseBinId, null, 1m, new[] { serial }, expected, target);

    [Fact]
    public async Task Rental_dispatch_transfer_keeps_the_unit_on_hand_reserved_at_the_destination_and_marks_the_serial_on_rent()
    {
        var w = await SeedAsync();
        await w.F.ReserveAsync(Reserve(w, w.Pick, "S-1"));
        await w.F.PostAsync(Dispatch(w, "S-1"));

        var src = await w.F.BalanceAsync(w.P.ProductId, w.Pick.WarehouseBinId);
        Assert.Equal((1m, 0m), (src!.QtyOnHand, src.QtyReserved));   // S-2 sigue disponible en A-01
        var dst = await w.F.BalanceAsync(w.P.ProductId, w.Rent.WarehouseBinId);
        Assert.Equal((1m, 1m), (dst!.QtyOnHand, dst.QtyReserved));   // en mano y reservado: disponible 0
        var serial = await w.F.SerialAsync(w.P.ProductId, "S-1");
        Assert.Equal(SerialStatuses.OnRent, w.F.StatusCodeOf(serial.StatusCodeId!.Value));
        Assert.Equal((w.W.WarehouseId, w.Rent.WarehouseBinId), (serial.CurrentWarehouseId!.Value, serial.CurrentBinId!.Value));
        Assert.Equal(new[] { SerialStatuses.Available, SerialStatuses.Reserved, SerialStatuses.OnRent },
            await w.F.HistoryCodesAsync(EntityTypes.InventorySerial, serial.SerialId));
        var txn = (await w.F.TransactionsAsync()).Last();
        Assert.Equal(1m, txn.Quantity);   // TRANSFER: neutra en el Kárdex (sin filtro), con la referencia RENTAL
        Assert.Equal(w.F.LookupId(LookupDomains.EntityType, EntityTypes.Rental), txn.RefEntityLookupId);
    }

    [Fact]
    public async Task A_serial_on_rent_cannot_be_moved_issued_or_received_again_without_the_rental_flow()
    {
        var w = await SeedAsync();
        await w.F.ReserveAsync(Reserve(w, w.Pick, "S-1"));
        await w.F.PostAsync(Dispatch(w, "S-1"));

        // Mover o despachar desde EN-RENTA como siempre (AVAILABLE esperado) → 409; con FromReserved (RESERVED esperado) → 409.
        var move = await Assert.ThrowsAsync<ConflictException>(() => w.F.PostAsync(new InventoryPosting(InventoryTxnTypes.Transfer, w.P.ProductId, 1m, SerialNumber: "S-1",
            FromWarehouseId: w.W.WarehouseId, FromBinId: w.Rent.WarehouseBinId, ToWarehouseId: w.W.WarehouseId, ToBinId: w.Other.WarehouseBinId)));
        Assert.Equal("La serie S-1 no está disponible en EN-RENTA.", move.Message);
        var reserved = await Assert.ThrowsAsync<ConflictException>(() => w.F.PostAsync(new InventoryPosting(InventoryTxnTypes.Issue, w.P.ProductId, 1m, SerialNumber: "S-1",
            FromWarehouseId: w.W.WarehouseId, FromBinId: w.Rent.WarehouseBinId, FromReserved: true)));
        Assert.Equal("La serie S-1 no está reservada en EN-RENTA.", reserved.Message);
        // Recibirla de nuevo (devolución normal) → 409: sigue en inventario.
        var receipt = await Assert.ThrowsAsync<ConflictException>(() => w.F.PostAsync(new InventoryPosting(InventoryTxnTypes.Receipt, w.P.ProductId, 1m, SerialNumber: "S-1",
            ToWarehouseId: w.W.WarehouseId, ToBinId: w.Other.WarehouseBinId)));
        Assert.Equal(SerialRules.AlreadyInStock("S-1"), receipt.Message);
        // Reservarla otra vez tampoco: EN-RENTA no tiene disponible (409 insufficient_stock).
        await Assert.ThrowsAsync<InsufficientStockException>(() => w.F.ReserveAsync(Reserve(w, w.Rent, "S-1")));
        Assert.Equal((1m, 1m), ((await w.F.BalanceAsync(w.P.ProductId, w.Rent.WarehouseBinId))!.QtyOnHand, (await w.F.BalanceAsync(w.P.ProductId, w.Rent.WarehouseBinId))!.QtyReserved));
    }

    [Fact]
    public async Task Expected_serial_status_lets_the_return_move_an_on_rent_serial_and_target_in_process_keeps_it_reserved()
    {
        var w = await SeedAsync();
        await w.F.ReserveAsync(Reserve(w, w.Pick, "S-1"));
        await w.F.PostAsync(Dispatch(w, "S-1"));

        // Devolución con proceso (como la hará R2): desde EN-RENTA, consumiendo lo reservado, a otra posición, reservada y EN PROCESO.
        await w.F.PostAsync(new InventoryPosting(InventoryTxnTypes.Transfer, w.P.ProductId, 1m, SerialNumber: "S-1",
            FromWarehouseId: w.W.WarehouseId, FromBinId: w.Rent.WarehouseBinId, ToWarehouseId: w.W.WarehouseId, ToBinId: w.Other.WarehouseBinId,
            FromReserved: true, ExpectedSerialStatus: SerialStatuses.OnRent, TargetSerialStatus: SerialStatuses.InProcess, ReserveAtDestination: true));
        Assert.Equal((0m, 0m), ((await w.F.BalanceAsync(w.P.ProductId, w.Rent.WarehouseBinId))!.QtyOnHand, (await w.F.BalanceAsync(w.P.ProductId, w.Rent.WarehouseBinId))!.QtyReserved));
        var back = await w.F.BalanceAsync(w.P.ProductId, w.Other.WarehouseBinId);
        Assert.Equal((1m, 1m), (back!.QtyOnHand, back.QtyReserved));
        Assert.Equal(SerialStatuses.InProcess, w.F.StatusCodeOf((await w.F.SerialAsync(w.P.ProductId, "S-1")).StatusCodeId!.Value));

        // Fin del proceso: liberar IN_PROCESS → AVAILABLE (el disponible vuelve).
        await w.F.ReleaseAsync(Reserve(w, w.Other, "S-1", SerialStatuses.InProcess, SerialStatuses.Available));
        back = await w.F.BalanceAsync(w.P.ProductId, w.Other.WarehouseBinId);
        Assert.Equal((1m, 0m), (back!.QtyOnHand, back.QtyReserved));
        Assert.Equal(SerialStatuses.Available, w.F.StatusCodeOf((await w.F.SerialAsync(w.P.ProductId, "S-1")).StatusCodeId!.Value));
    }

    [Fact]
    public async Task Release_with_the_default_expected_status_rejects_a_serial_in_process()
    {
        var w = await SeedAsync();
        await w.F.ReserveAsync(Reserve(w, w.Pick, "S-1", SerialStatuses.Available, SerialStatuses.InProcess));
        Assert.Equal(SerialStatuses.InProcess, w.F.StatusCodeOf((await w.F.SerialAsync(w.P.ProductId, "S-1")).StatusCodeId!.Value));
        var ex = await Assert.ThrowsAsync<ConflictException>(() => w.F.ReleaseAsync(Reserve(w, w.Pick, "S-1")));
        Assert.Equal("La serie S-1 no está reservada en A-01.", ex.Message);
        await w.F.ReleaseAsync(Reserve(w, w.Pick, "S-1", SerialStatuses.InProcess));   // destino por defecto: AVAILABLE
        Assert.Equal(SerialStatuses.Available, w.F.StatusCodeOf((await w.F.SerialAsync(w.P.ProductId, "S-1")).StatusCodeId!.Value));
    }

    [Fact]
    public async Task Reserve_at_destination_needs_a_destination_and_the_defaults_keep_the_old_behavior()
    {
        var w = await SeedAsync();
        var ex = await Assert.ThrowsAsync<ValidationException>(() => w.F.PostAsync(new InventoryPosting(InventoryTxnTypes.Issue, w.P.ProductId, 1m, SerialNumber: "S-1",
            FromWarehouseId: w.W.WarehouseId, FromBinId: w.Pick.WarehouseBinId, ReserveAtDestination: true)));
        Assert.Contains("postings[0]", ex.Errors!.Keys);

        // Sin parámetros nuevos: una transferencia no cambia el estatus ni reserva en el destino.
        await w.F.PostAsync(new InventoryPosting(InventoryTxnTypes.Transfer, w.P.ProductId, 1m, SerialNumber: "S-2",
            FromWarehouseId: w.W.WarehouseId, FromBinId: w.Pick.WarehouseBinId, ToWarehouseId: w.W.WarehouseId, ToBinId: w.Other.WarehouseBinId));
        var dst = await w.F.BalanceAsync(w.P.ProductId, w.Other.WarehouseBinId);
        Assert.Equal((1m, 0m), (dst!.QtyOnHand, dst.QtyReserved));
        Assert.Equal(SerialStatuses.Available, w.F.StatusCodeOf((await w.F.SerialAsync(w.P.ProductId, "S-2")).StatusCodeId!.Value));
    }

    [Fact]
    public void The_rental_zone_is_never_allocated_picked_put_away_nor_a_direct_receiving_target()
    {
        Assert.True(StockAllocator.IsExcludedZone(ZoneTypes.Rental));
        Assert.False(PickBatchRules.IsPickableZone(ZoneTypes.Rental));
        Assert.True(PickBatchRules.IsPickableZone(ZoneTypes.Picking));
        Assert.Equal(ReceivingModeRules.TargetZoneNotAllowed("EN-RENTA", ZoneTypes.Rental), ReceivingModeRules.ValidateTargetZone("EN-RENTA", ZoneTypes.Rental));
        var ranked = PutawayRules.Rank(new[]
        {
            // Aun como posición preferida del producto, EN-RENTA nunca se sugiere.
            new PutawayCandidate(1, "EN-RENTA", 10, "RENT", ZoneTypes.Rental, true, null, 0m, true, 0m, 0m, true),
            new PutawayCandidate(2, "R-01", 11, "RSV", ZoneTypes.Reserve, true, null, 0m, true, 0m, 0m, false),
        }, null, 1m, null, RotationClasses.Slow, null, 10);
        Assert.Equal(new[] { 2 }, ranked.Select(r => r.BinId).ToArray());
    }
}
