using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Catalogs;
using Teikem.Domain.Constants;
using Teikem.Domain.Fleet;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 4 / P2 (hallazgo de revisión): 'Eliminar chofer' (entrada a la etapa TERMINAL de DriverStatus) deja al chofer
/// inactivo y sin usuario, desactiva SUS dispositivos (no los de otro chofer) y borra sus filas DriverZone. El smoke no
/// puede comprobar los dispositivos porque su registro es de la app del Lote 7; aquí se siembran directamente.
/// </summary>
public class DriverStatusEffectTests
{
    private const int TenantId = 1;

    private static TeikemDbContext InMemoryDb()
        => new(new DbContextOptionsBuilder<TeikemDbContext>().UseInMemoryDatabase("driver-status-effect-" + Guid.NewGuid()).Options,
            new TenantContext { TenantId = TenantId, UserId = 1 });

    private static StatusCode Stage(string stageKind)
        => new() { StageKind = new LookupCode { InternalCode = stageKind } };

    private static async Task<(TeikemDbContext Db, int DriverId, int OtherDriverId)> SeedAsync()
    {
        var db = InMemoryDb();
        var driver = new Driver { DriverId = 10, TenantId = TenantId, EmployeeCode = "D1", FullName = "Ana", UserId = 77, StatusCodeId = 1, IsActive = true };
        var other = new Driver { DriverId = 20, TenantId = TenantId, EmployeeCode = "D2", FullName = "Beto", StatusCodeId = 1, IsActive = true };
        db.Drivers.AddRange(driver, other);
        db.DispatchZones.Add(new DispatchZone { DispatchZoneId = 5, TenantId = TenantId, Code = "Z1", Name = "Zona" });
        db.DriverDevices.AddRange(
            new DriverDevice { DriverDeviceId = 1, TenantId = TenantId, DriverId = driver.DriverId, PlatformLookupId = 1, PushToken = "a", IsActive = true },
            new DriverDevice { DriverDeviceId = 2, TenantId = TenantId, DriverId = driver.DriverId, PlatformLookupId = 1, PushToken = "b", IsActive = true },
            new DriverDevice { DriverDeviceId = 3, TenantId = TenantId, DriverId = other.DriverId, PlatformLookupId = 1, PushToken = "c", IsActive = true });
        db.DriverZones.AddRange(
            new DriverZone { DriverId = driver.DriverId, DispatchZoneId = 5, IsPrimary = true },
            new DriverZone { DriverId = other.DriverId, DispatchZoneId = 5, IsPrimary = true });
        await db.SaveChangesAsync();
        return (db, driver.DriverId, other.DriverId);
    }

    private static Task TransitionAsync(TeikemDbContext db, int driverId, string stageKind)
        => new DriverStatusEffect(db).OnTransitionedAsync(
            new StatusTransitionContext(StatusDomains.DriverStatus, EntityTypes.Driver, driverId, null, Stage(stageKind), null), default);

    [Fact]
    public async Task Entering_terminal_deactivates_driver_devices_user_and_zone()
    {
        var (db, driverId, otherId) = await SeedAsync();
        using (db)
        {
            await TransitionAsync(db, driverId, StageKinds.Terminal);
            await db.SaveChangesAsync();

            var driver = await db.Drivers.SingleAsync(d => d.DriverId == driverId);
            Assert.False(driver.IsActive);
            Assert.Null(driver.UserId);

            var devices = await db.DriverDevices.Where(d => d.DriverId == driverId).ToListAsync();
            Assert.Equal(2, devices.Count);
            Assert.All(devices, d => Assert.False(d.IsActive));
            Assert.True((await db.DriverDevices.SingleAsync(d => d.DriverId == otherId)).IsActive);

            Assert.False(await db.DriverZones.AnyAsync(z => z.DriverId == driverId));
            Assert.True(await db.DriverZones.AnyAsync(z => z.DriverId == otherId));
            Assert.True((await db.Drivers.SingleAsync(d => d.DriverId == otherId)).IsActive);
        }
    }

    [Theory]
    [InlineData(StageKinds.Pipeline)]
    [InlineData(StageKinds.Lateral)]
    public async Task Non_terminal_stage_changes_nothing(string stageKind)
    {
        var (db, driverId, _) = await SeedAsync();
        using (db)
        {
            await TransitionAsync(db, driverId, stageKind);
            await db.SaveChangesAsync();

            var driver = await db.Drivers.SingleAsync(d => d.DriverId == driverId);
            Assert.True(driver.IsActive);
            Assert.Equal(77, driver.UserId);
            Assert.All(await db.DriverDevices.Where(d => d.DriverId == driverId).ToListAsync(), d => Assert.True(d.IsActive));
            Assert.True(await db.DriverZones.AnyAsync(z => z.DriverId == driverId));
        }
    }
}
