using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Catalogs;
using Teikem.Domain.Constants;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Wms;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 28 (Rentas R2) — proceso del equipo devuelto con RentalProcessService y RentalProcessStatusEffect REALES (RentalReturnWorld):
/// - READY libera la reserva (serie IN_PROCESS → AVAILABLE, el disponible vuelve), con traslado opcional dentro del almacén;
/// - SCRAPPED hace el ADJUSTMENT − con motivo DAMAGE y la referencia RENTAL_PROCESS (serie SCRAPPED, sin ubicación) y exige
///   inventory.adjust además de rental.maintenance;
/// - el efecto depende de los TERMINALES, no de los pasos: con un paso desactivado por la compañía y llegando a READY por "avanzar"
///   (sin "terminar") el efecto es el mismo; un proceso terminado responde 422.
/// </summary>
public sealed class RentalProcessEffectTests
{
    /// <summary>Renta de S-3 despachada y devuelta anticipada por daño a Q-01, con proceso; devuelve el id del proceso.</summary>
    private static async Task<int> ReturnedForProcessAsync(RentalReturnWorld w)
    {
        var rental = await w.DispatchedAsync("S-3");
        var ret = await w.Returns(s => s.CreateAsync(rental.Rental.PublicId, new RentalReturnCreateRequest("EARLY_DAMAGE",
            new[] { RentalReturnWorld.Line("S-3", "DAMAGED", w.Q1.WarehouseBinId) }), default));
        return Assert.Single(ret.Lines).ProcessId!.Value;
    }

    [Fact]
    public async Task Ready_releases_the_unit_and_the_available_comes_back_with_an_optional_move()
    {
        var w = await RentalReturnWorld.CreateAsync();
        var id = await ReturnedForProcessAsync(w);
        Assert.Equal((3m, 2m), await w.TotalsAsync());   // S-3 en proceso: en mano, no disponible

        // Pasos configurables: Inspección → Reparación (lateral) → de vuelta a Inspección → Limpieza → Pruebas.
        foreach (var step in new[] { RentalProcessStatuses.Inspection, RentalProcessStatuses.Repair, RentalProcessStatuses.Inspection,
                     RentalProcessStatuses.Cleaning, RentalProcessStatuses.Testing })
        {
            var advanced = await w.Processes(s => s.AdvanceAsync(id, new RentalProcessAdvanceRequest(step.ToLowerInvariant(), "paso"), default));
            Assert.Equal((step, false), (advanced.StatusCode, advanced.IsFinished));
        }
        // Los pasos intermedios no tocan el inventario.
        Assert.Equal((SerialStatuses.InProcess, (int?)w.Q1.WarehouseBinId), await w.SerialAsync("S-3"));
        Assert.Equal((1m, 1m), await w.BalanceAsync(w.Q1.WarehouseBinId));
        // Salto ilegal de StatusService (de Pruebas no se regresa a Pendiente).
        await Assert.ThrowsAsync<StatusRuleException>(() => w.Processes(s => s.AdvanceAsync(id, new RentalProcessAdvanceRequest("PENDING"), default)));

        // Terminar con traslado a A-02 (mismo almacén): TRANSFER con la reserva y luego READY libera.
        var ready = await w.Processes(s => s.CompleteAsync(id, new RentalProcessCompleteRequest(w.A2.WarehouseBinId, "Listo para rentar"), default));
        Assert.Equal((RentalProcessStatuses.Ready, true, "A-02"), (ready.StatusCode, ready.IsFinished, ready.BinCode));
        Assert.NotNull(ready.CompletedAtUtc);
        Assert.Equal((SerialStatuses.Available, (int?)w.A2.WarehouseBinId), await w.SerialAsync("S-3"));
        Assert.Equal((0m, 0m), await w.BalanceAsync(w.Q1.WarehouseBinId));
        Assert.Equal((1m, 0m), await w.BalanceAsync(w.A2.WarehouseBinId));
        Assert.Equal((3m, 3m), await w.TotalsAsync());   // el disponible vuelve
        var move = (await w.F.TransactionsAsync()).Last();
        Assert.Equal((w.F.LookupId(LookupDomains.InventoryTxnType, InventoryTxnTypes.Transfer), w.F.LookupId(LookupDomains.EntityType, EntityTypes.RentalProcess), id, $"Proceso #{id}"),
            (move.TxnTypeLookupId, move.RefEntityLookupId!.Value, move.RefId!.Value, move.Notes));
        Assert.Equal(new[] { RentalProcessStatuses.Pending, RentalProcessStatuses.Inspection, RentalProcessStatuses.Repair, RentalProcessStatuses.Inspection,
            RentalProcessStatuses.Cleaning, RentalProcessStatuses.Testing, RentalProcessStatuses.Ready }, await w.F.HistoryCodesAsync(EntityTypes.RentalProcess, id));

        // Terminado: solo se consulta (422) y la serie se puede volver a rentar.
        var finished = await Assert.ThrowsAsync<StatusRuleException>(() => w.Processes(s => s.AdvanceAsync(id, new RentalProcessAdvanceRequest("CLEANING"), default)));
        Assert.Equal("El proceso ya terminó; solo se consulta.", finished.Message);
        await Assert.ThrowsAsync<StatusRuleException>(() => w.Processes(s => s.ScrapAsync(id, null, default)));
        Assert.Equal(1, (await w.Processes(s => s.ListAsync(new RentalProcessQuery(Open: false), default))).Total);
        Assert.Equal(0, (await w.Processes(s => s.ListAsync(new RentalProcessQuery(Open: true), default))).Total);
        var again = await w.DispatchedAsync("S-3");
        Assert.Equal(RentalStatuses.OnRent, again.Rental.StatusCode);
    }

    [Fact]
    public async Task Ready_depends_on_the_terminal_not_on_the_steps_and_complete_guards_the_destination()
    {
        var w = await RentalReturnWorld.CreateAsync();
        var id = await ReturnedForProcessAsync(w);

        // La compañía desactiva "Inspección": ya no se avanza a ese paso; los demás siguen.
        w.F.Db.StatusCodeOverrides.Add(new StatusCodeOverride
        {
            TenantId = WmsFixture.TenantId, StatusCodeId = w.F.StatusId(StatusDomains.RentalProcessStatus, RentalProcessStatuses.Inspection), IsEnabled = false,
        });
        await w.F.Db.SaveChangesAsync();
        w.F.Db.ChangeTracker.Clear();
        var disabled = await Assert.ThrowsAsync<StatusRuleException>(() => w.Processes(s => s.AdvanceAsync(id, new RentalProcessAdvanceRequest("INSPECTION"), default)));
        Assert.Equal("El estatus 'INSPECTION' no existe o no está habilitado para esta compañía.", disabled.Message);
        Assert.Equal(RentalProcessStatuses.Cleaning, (await w.Processes(s => s.AdvanceAsync(id, new RentalProcessAdvanceRequest("CLEANING"), default))).StatusCode);

        // Destino del traslado: no de la zona En renta (400) ni de otro almacén (400); vacío = 400.
        var rentBin = await w.RentBinAsync();
        var inRental = await Assert.ThrowsAsync<ValidationException>(() => w.Processes(s => s.CompleteAsync(id, new RentalProcessCompleteRequest(rentBin.WarehouseBinId), default)));
        Assert.Equal("La posición de destino no puede ser de la zona En renta.", inRental.Message);
        var w2 = await w.F.AddWarehouseAsync("W2");
        var otherBin = await w.F.AddBinAsync(await w.F.AddZoneAsync(w2, "PCK", ZoneTypes.Picking), "B-01");
        var otherWarehouse = await Assert.ThrowsAsync<ValidationException>(() => w.Processes(s => s.CompleteAsync(id, new RentalProcessCompleteRequest(otherBin.WarehouseBinId), default)));
        Assert.Equal("La posición de destino debe ser del almacén W1 del proceso.", otherWarehouse.Message);
        var noStatus = await Assert.ThrowsAsync<ValidationException>(() => w.Processes(s => s.AdvanceAsync(id, new RentalProcessAdvanceRequest(" "), default)));
        Assert.Equal("Indique el estatus al que pasa el proceso.", noStatus.Message);
        Assert.Equal((SerialStatuses.InProcess, (int?)w.Q1.WarehouseBinId), await w.SerialAsync("S-3"));

        // READY por "avanzar" (sin "terminar" ni traslado): el mismo efecto, en la posición del proceso.
        var ready = await w.Processes(s => s.AdvanceAsync(id, new RentalProcessAdvanceRequest("READY"), default));
        Assert.True(ready.IsFinished);
        Assert.Equal((SerialStatuses.Available, (int?)w.Q1.WarehouseBinId), await w.SerialAsync("S-3"));
        Assert.Equal((1m, 0m), await w.BalanceAsync(w.Q1.WarehouseBinId));
    }

    [Fact]
    public async Task Scrapped_writes_the_unit_off_with_damage_and_needs_inventory_adjust()
    {
        var w = await RentalReturnWorld.CreateAsync();
        var id = await ReturnedForProcessAsync(w);
        await w.Processes(s => s.AdvanceAsync(id, new RentalProcessAdvanceRequest("REPAIR"), default));
        var before = (await w.F.TransactionsAsync()).Count;

        // Con rental.maintenance (lo que pide el controlador) pero sin inventory.adjust → 403, por "dar de baja" y por "avanzar".
        w.F.SetPermissions(PermissionCatalog.RentalView, PermissionCatalog.RentalMaintenance);
        var scrap = await Assert.ThrowsAsync<ForbiddenException>(() => w.Processes(s => s.ScrapAsync(id, null, default)));
        Assert.Equal($"Falta el permiso '{PermissionCatalog.InventoryAdjust}'.", scrap.Message);
        await Assert.ThrowsAsync<ForbiddenException>(() => w.Processes(s => s.AdvanceAsync(id, new RentalProcessAdvanceRequest("SCRAPPED"), default)));
        // Los pasos sí los avanza quien tiene rental.maintenance.
        Assert.Equal(RentalProcessStatuses.Inspection, (await w.Processes(s => s.AdvanceAsync(id, new RentalProcessAdvanceRequest("INSPECTION"), default))).StatusCode);
        Assert.Equal(before, (await w.F.TransactionsAsync()).Count);

        w.F.SetPermissions(PermissionCatalog.RentalView, PermissionCatalog.RentalMaintenance, PermissionCatalog.InventoryAdjust);
        var scrapped = await w.Processes(s => s.ScrapAsync(id, new RentalStatusRequest("No tiene arreglo"), default));
        Assert.Equal((RentalProcessStatuses.Scrapped, true), (scrapped.StatusCode, scrapped.IsFinished));
        Assert.NotNull(scrapped.CompletedAtUtc);

        // ADJUSTMENT − (salida) con motivo DAMAGE y la referencia del proceso; la serie queda dada de baja y sin ubicación.
        var txn = (await w.F.TransactionsAsync()).Last();
        Assert.Equal((w.F.LookupId(LookupDomains.InventoryTxnType, InventoryTxnTypes.Adjustment), -1m, w.F.LookupId(LookupDomains.AdjustmentReason, AdjustmentReasons.Damage),
                w.F.LookupId(LookupDomains.EntityType, EntityTypes.RentalProcess), id, $"Baja del proceso #{id}", (int?)w.Q1.WarehouseBinId),
            (txn.TxnTypeLookupId, txn.Quantity, txn.ReasonLookupId!.Value, txn.RefEntityLookupId!.Value, txn.RefId!.Value, txn.Notes, txn.FromBinId));
        Assert.Equal((SerialStatuses.Scrapped, (int?)null), await w.SerialAsync("S-3"));
        Assert.Equal((0m, 0m), await w.BalanceAsync(w.Q1.WarehouseBinId));
        Assert.Equal((2m, 2m), await w.TotalsAsync());   // salió una unidad: en mano 2 (S-1 y S-2)
        Assert.Equal(RentalProcessStatuses.Scrapped, (await w.F.HistoryCodesAsync(EntityTypes.RentalProcess, id)).Last());

        // Dada de baja: no se rentan ni se recibe de nuevo; el proceso solo se consulta.
        var finished = await Assert.ThrowsAsync<StatusRuleException>(() => w.Processes(s => s.CompleteAsync(id, null, default)));
        Assert.Equal(RentalRules.ProcessFinished, finished.Message);
        await Assert.ThrowsAsync<ConflictException>(() => w.F.PostAsync(new InventoryPosting(InventoryTxnTypes.Receipt, w.P.ProductId, 1m, SerialNumber: "S-3",
            ToWarehouseId: w.W.WarehouseId, ToBinId: w.A1.WarehouseBinId)));
        Assert.Equal(1, await w.F.Db.RentalProcesses.CountAsync(p => p.CompletedAtUtc != null));
    }
}
