using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Constants;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Services;
using Teikem.Infrastructure.Wms;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 6 (P1) — pruebas de SERVICIO de los PATCH de zona, posición y muelle (InMemory, WmsFixture). WmsResolve con
/// track:true debe devolver la hija RASTREADA: AsTracking/AsNoTracking valen para toda la consulta, y un AsNoTracking en la
/// fuente del join con el almacén dejaba la entidad sin rastrear (200 sin UPDATE). Cada caso relee con AsNoTracking.
/// </summary>
public sealed class WarehouseLayoutServiceTests
{
    private static Task<WmsFixture> CreateAsync() => WmsFixture.CreateAsync(s => s.AddSingleton<WarehouseLayoutService>());

    [Fact]
    public async Task Zone_patch_is_persisted()
    {
        await using var f = await CreateAsync();
        var w = await f.AddWarehouseAsync("W1");
        var zone = await f.AddZoneAsync(w, "PCK", ZoneTypes.Picking);

        await f.Get<WarehouseLayoutService>().UpdateZoneAsync(w.PublicId, zone.WarehouseZoneId,
            new WarehouseZonePatchRequest(Name: "Nombre2", ZoneType: ZoneTypes.Reserve), default);
        f.Db.ChangeTracker.Clear();

        var saved = await f.Db.WarehouseZones.AsNoTracking().SingleAsync(z => z.WarehouseZoneId == zone.WarehouseZoneId);
        Assert.Equal("Nombre2", saved.Name);
        Assert.Equal(f.LookupId(LookupDomains.ZoneType, ZoneTypes.Reserve), saved.ZoneTypeLookupId);
    }

    [Fact]
    public async Task Bin_patch_is_persisted()
    {
        await using var f = await CreateAsync();
        var w = await f.AddWarehouseAsync("W1");
        var zone = await f.AddZoneAsync(w, "RSV", ZoneTypes.Reserve);
        var bin = await f.AddBinAsync(zone, "B-01", maxWeightKg: 100m);

        await f.Get<WarehouseLayoutService>().UpdateBinAsync(w.PublicId, bin.WarehouseBinId,
            new WarehouseBinPatchRequest(Aisle: "A9", MaxWeightKg: 50m), default);
        f.Db.ChangeTracker.Clear();

        var saved = await f.Db.WarehouseBins.AsNoTracking().SingleAsync(b => b.WarehouseBinId == bin.WarehouseBinId);
        Assert.Equal("A9", saved.Aisle);
        Assert.Equal(50m, saved.MaxWeightKg);
    }

    [Fact]
    public async Task Dock_patch_is_persisted()
    {
        await using var f = await CreateAsync();
        var w = await f.AddWarehouseAsync("W1");
        var dock = await f.AddDockAsync(w, "D1", DockTypes.Both);

        await f.Get<WarehouseLayoutService>().UpdateDockAsync(w.PublicId, dock.WarehouseDockId,
            new WarehouseDockPatchRequest(DockType: DockTypes.Inbound), default);
        f.Db.ChangeTracker.Clear();

        var saved = await f.Db.WarehouseDocks.AsNoTracking().SingleAsync(d => d.WarehouseDockId == dock.WarehouseDockId);
        Assert.Equal(f.LookupId(LookupDomains.DockType, DockTypes.Inbound), saved.DockTypeLookupId);
    }

    [Fact]
    public async Task Child_of_another_warehouse_is_not_found()
    {
        await using var f = await CreateAsync();
        var w1 = await f.AddWarehouseAsync("W1");
        var w2 = await f.AddWarehouseAsync("W2");
        var zone2 = await f.AddZoneAsync(w2, "PCK", ZoneTypes.Picking);

        await Assert.ThrowsAsync<NotFoundException>(() => f.Get<WarehouseLayoutService>().UpdateZoneAsync(w1.PublicId, zone2.WarehouseZoneId,
            new WarehouseZonePatchRequest(Name: "X"), default));
        f.Db.ChangeTracker.Clear();
        Assert.Equal("Zona PCK", (await f.Db.WarehouseZones.AsNoTracking().SingleAsync(z => z.WarehouseZoneId == zone2.WarehouseZoneId)).Name);
    }
    private static async Task<bool> BinActiveAsync(WmsFixture f, int binId)
        => (await f.Db.WarehouseBins.AsNoTracking().SingleAsync(b => b.WarehouseBinId == binId)).IsActive;

    [Fact]
    public async Task Bin_with_stock_cannot_be_deactivated()
    {
        await using var f = await CreateAsync();
        var w = await f.AddWarehouseAsync("W1");
        var bin = await f.AddBinAsync(await f.AddZoneAsync(w, "PCK", ZoneTypes.Picking), "P-01");
        var p = await f.AddProductAsync("PN");
        await f.PostAsync(new InventoryPosting(InventoryTxnTypes.Receipt, p.ProductId, 3m, ToWarehouseId: w.WarehouseId, ToBinId: bin.WarehouseBinId));

        var ex = await Assert.ThrowsAsync<ConflictException>(() => f.Get<WarehouseLayoutService>().SetBinActiveAsync(w.PublicId, bin.WarehouseBinId, false, default));
        Assert.Equal(WarehouseRules.BinNotEmpty("P-01"), ex.Message);
        Assert.Equal("La posición P-01 tiene inventario; no se puede desactivar.", ex.Message);
        f.Db.ChangeTracker.Clear();
        Assert.True(await BinActiveAsync(f, bin.WarehouseBinId));
    }

    [Fact]
    public async Task Bin_used_by_an_open_task_cannot_be_deactivated()
    {
        await using var f = await CreateAsync();
        var w = await f.AddWarehouseAsync("W1");
        var zone = await f.AddZoneAsync(w, "RSV", ZoneTypes.Reserve);
        var from = await f.AddBinAsync(zone, "R-01");
        var to = await f.AddBinAsync(zone, "R-02");
        var p = await f.AddProductAsync("PN");
        await f.AddTaskAsync(new WarehouseTaskSpec(WarehouseTaskTypes.Putaway, w.WarehouseId, p.ProductId, 1m, FromBinId: from.WarehouseBinId, ToBinId: to.WarehouseBinId));

        var ex = await Assert.ThrowsAsync<ConflictException>(() => f.Get<WarehouseLayoutService>().SetBinActiveAsync(w.PublicId, to.WarehouseBinId, false, default));
        Assert.Equal(WarehouseRules.BinHasOpenTasks, ex.Message);
        f.Db.ChangeTracker.Clear();
        Assert.True(await BinActiveAsync(f, to.WarehouseBinId));
    }

    [Fact]
    public async Task Empty_bin_is_deactivated_and_is_not_reactivated_under_an_inactive_zone()
    {
        await using var f = await CreateAsync();
        var w = await f.AddWarehouseAsync("W1");
        var zone = await f.AddZoneAsync(w, "RSV", ZoneTypes.Reserve);
        var bin = await f.AddBinAsync(zone, "R-01");
        var layout = f.Get<WarehouseLayoutService>();

        var dto = await layout.SetBinActiveAsync(w.PublicId, bin.WarehouseBinId, false, default);
        Assert.False(dto.IsActive);
        f.Db.ChangeTracker.Clear();
        Assert.False(await BinActiveAsync(f, bin.WarehouseBinId));

        var z = await f.Db.WarehouseZones.SingleAsync(x => x.WarehouseZoneId == zone.WarehouseZoneId);
        z.IsActive = false;
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        var ex = await Assert.ThrowsAsync<StatusRuleException>(() => layout.SetBinActiveAsync(w.PublicId, bin.WarehouseBinId, true, default));
        Assert.Equal(WarehouseRules.ZoneInactiveMessage, ex.Message);
    }
    [Fact]
    public async Task Zone_with_an_active_bin_is_not_deactivated_until_the_bin_is()
    {
        await using var f = await CreateAsync();
        var w = await f.AddWarehouseAsync("W1");
        var zone = await f.AddZoneAsync(w, "RSV", ZoneTypes.Reserve);
        var bin = await f.AddBinAsync(zone, "R-01");
        var layout = f.Get<WarehouseLayoutService>();

        var ex = await Assert.ThrowsAsync<ConflictException>(() => layout.SetZoneActiveAsync(w.PublicId, zone.WarehouseZoneId, false, default));
        Assert.Equal(WarehouseRules.ZoneHasActiveBins, ex.Message);
        f.Db.ChangeTracker.Clear();

        await layout.SetBinActiveAsync(w.PublicId, bin.WarehouseBinId, false, default);
        f.Db.ChangeTracker.Clear();
        var off = await layout.SetZoneActiveAsync(w.PublicId, zone.WarehouseZoneId, false, default);
        Assert.False(off.IsActive);
        f.Db.ChangeTracker.Clear();
        Assert.False((await f.Db.WarehouseZones.AsNoTracking().SingleAsync(z => z.WarehouseZoneId == zone.WarehouseZoneId)).IsActive);
    }

    [Fact]
    public async Task Dock_with_a_scheduled_appointment_is_not_deactivated()
    {
        await using var f = await CreateAsync();
        var w = await f.AddWarehouseAsync("W1");
        var dock = await f.AddDockAsync(w, "D1", DockTypes.Both);
        var appt = new DockAppointment
        {
            TenantId = WmsFixture.TenantId, WarehouseId = w.WarehouseId, WarehouseDockId = dock.WarehouseDockId,
            DirectionLookupId = f.LookupId(LookupDomains.DockDirection, DockDirections.Inbound), ScheduledStartUtc = DateTime.UtcNow.AddDays(1),
            StatusCodeId = f.StatusId(StatusDomains.AppointmentStatus, AppointmentStatuses.Scheduled), CreatedAtUtc = DateTime.UtcNow,
        };
        f.Db.DockAppointments.Add(appt);
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        var layout = f.Get<WarehouseLayoutService>();

        var ex = await Assert.ThrowsAsync<ConflictException>(() => layout.SetDockActiveAsync(w.PublicId, dock.WarehouseDockId, false, default));
        Assert.Equal(WarehouseRules.DockHasAppointments, ex.Message);
        f.Db.ChangeTracker.Clear();

        var cancelled = await f.Db.DockAppointments.SingleAsync(a => a.DockAppointmentId == appt.DockAppointmentId);
        cancelled.StatusCodeId = f.StatusId(StatusDomains.AppointmentStatus, AppointmentStatuses.Cancelled);
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        Assert.False((await layout.SetDockActiveAsync(w.PublicId, dock.WarehouseDockId, false, default)).IsActive);
    }

    [Fact]
    public async Task Manual_dock_status_is_written_with_history_and_rejected_when_unknown_or_inactive()
    {
        // Maestro L317: muelles con estatus de ocupación; el cambio manual deja historial WAREHOUSE_DOCK con el comentario.
        await using var f = await CreateAsync();
        var w = await f.AddWarehouseAsync("W1");
        var dock = await f.AddDockAsync(w, "D1", DockTypes.Both);
        var layout = f.Get<WarehouseLayoutService>();

        var dto = await layout.SetDockStatusAsync(w.PublicId, dock.WarehouseDockId, new WarehouseDockStatusRequest("maintenance", "Rampa dañada"), default);
        Assert.Equal(DockStatuses.Maintenance, dto.StatusCode);
        var dockType = f.LookupId(LookupDomains.EntityType, EntityTypes.WarehouseDock);
        var history = await f.Db.EntityStatusHistories.AsNoTracking()
            .SingleAsync(h => h.EntityTypeLookupId == dockType && h.EntityId == dock.WarehouseDockId);
        Assert.Equal((f.StatusId(StatusDomains.DockStatus, DockStatuses.Maintenance), "Rampa dañada"), (history.ToStatusCodeId, history.Comment));

        var unknown = await Assert.ThrowsAsync<ValidationException>(() =>
            layout.SetDockStatusAsync(w.PublicId, dock.WarehouseDockId, new WarehouseDockStatusRequest("FOO"), default));
        Assert.Equal(WarehouseRules.DockStatusNotManual("FOO"), Assert.Single(unknown.Errors!["status"]));

        f.Db.ChangeTracker.Clear();
        await layout.SetDockActiveAsync(w.PublicId, dock.WarehouseDockId, false, default);
        f.Db.ChangeTracker.Clear();
        var inactive = await Assert.ThrowsAsync<StatusRuleException>(() =>
            layout.SetDockStatusAsync(w.PublicId, dock.WarehouseDockId, new WarehouseDockStatusRequest(DockStatuses.Free), default));
        Assert.Equal(WarehouseRules.DockInactiveMessage, inactive.Message);
    }
}
