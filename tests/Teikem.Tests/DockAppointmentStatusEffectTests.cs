using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Catalogs;
using Teikem.Domain.Constants;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 6 (P9) — efecto del dominio AppointmentStatus sobre el muelle (R19b, D30): ARRIVED ocupa el muelle; COMPLETED,
/// NO_SHOW y CANCELLED lo liberan si no queda otra llegada; MAINTENANCE no se toca. InMemory con StatusService y el efecto
/// reales (StatusService resuelto de forma perezosa, como en producción). Pipelines y entradas laterales sembrados igual que
/// logistica-db-seed.sql (Lote 6).
/// </summary>
public class DockAppointmentStatusEffectTests
{
    private const int TenantId = 1;
    private const int WarehouseId = 10;

    [Fact]
    public async Task Arrival_occupies_a_free_dock_with_history()
    {
        await using var f = await Fixture.CreateAsync();
        var dock = await f.SeedDockAsync(DockStatuses.Free);
        var appt = await f.SeedAppointmentAsync(dock, AppointmentStatuses.Scheduled);

        await f.TransitionAsync(appt, AppointmentStatuses.Arrived);

        Assert.Equal(f.Id(StatusDomains.DockStatus, DockStatuses.Occupied), await f.DockStatusIdAsync(dock));
        Assert.Equal(new[] { DockStatuses.Occupied }, await f.HistoryCodesAsync(EntityTypes.WarehouseDock, dock));
    }

    [Fact]
    public async Task Completion_frees_the_dock()
    {
        await using var f = await Fixture.CreateAsync();
        var dock = await f.SeedDockAsync(DockStatuses.Free);
        var appt = await f.SeedAppointmentAsync(dock, AppointmentStatuses.Scheduled);

        await f.TransitionAsync(appt, AppointmentStatuses.Arrived);
        await f.TransitionAsync(appt, AppointmentStatuses.Completed);

        Assert.Equal(f.Id(StatusDomains.DockStatus, DockStatuses.Free), await f.DockStatusIdAsync(dock));
        Assert.Equal(new[] { DockStatuses.Occupied, DockStatuses.Free }, await f.HistoryCodesAsync(EntityTypes.WarehouseDock, dock));
    }

    [Fact]
    public async Task Another_active_arrival_keeps_the_dock_occupied()
    {
        await using var f = await Fixture.CreateAsync();
        var dock = await f.SeedDockAsync(DockStatuses.Free);
        var first = await f.SeedAppointmentAsync(dock, AppointmentStatuses.Scheduled);
        var second = await f.SeedAppointmentAsync(dock, AppointmentStatuses.Scheduled);

        await f.TransitionAsync(first, AppointmentStatuses.Arrived);
        await f.TransitionAsync(second, AppointmentStatuses.Arrived);   // el muelle ya está ocupado: sin otro historial
        await f.TransitionAsync(first, AppointmentStatuses.Completed);

        Assert.Equal(f.Id(StatusDomains.DockStatus, DockStatuses.Occupied), await f.DockStatusIdAsync(dock));

        // Al cerrar la última llegada el muelle se libera.
        await f.TransitionAsync(second, AppointmentStatuses.Completed);
        Assert.Equal(f.Id(StatusDomains.DockStatus, DockStatuses.Free), await f.DockStatusIdAsync(dock));
        Assert.Equal(new[] { DockStatuses.Occupied, DockStatuses.Free }, await f.HistoryCodesAsync(EntityTypes.WarehouseDock, dock));
    }

    [Fact]
    public async Task Maintenance_is_never_touched()
    {
        await using var f = await Fixture.CreateAsync();
        var dock = await f.SeedDockAsync(DockStatuses.Maintenance);
        var appt = await f.SeedAppointmentAsync(dock, AppointmentStatuses.Scheduled);

        await f.TransitionAsync(appt, AppointmentStatuses.Arrived);
        Assert.Equal(f.Id(StatusDomains.DockStatus, DockStatuses.Maintenance), await f.DockStatusIdAsync(dock));

        await f.TransitionAsync(appt, AppointmentStatuses.Completed);
        Assert.Equal(f.Id(StatusDomains.DockStatus, DockStatuses.Maintenance), await f.DockStatusIdAsync(dock));
        Assert.Empty(await f.HistoryCodesAsync(EntityTypes.WarehouseDock, dock));
    }

    [Theory]
    [InlineData("NO_SHOW")]
    [InlineData("CANCELLED")]
    public async Task No_show_and_cancel_free_an_occupied_dock_without_arrivals(string code)
    {
        await using var f = await Fixture.CreateAsync();
        var dock = await f.SeedDockAsync(DockStatuses.Occupied);
        var appt = await f.SeedAppointmentAsync(dock, AppointmentStatuses.Scheduled);

        await f.TransitionAsync(appt, code);

        Assert.Equal(f.Id(StatusDomains.DockStatus, DockStatuses.Free), await f.DockStatusIdAsync(dock));
    }

    [Fact]
    public async Task Cancel_does_not_free_while_another_appointment_is_arrived()
    {
        await using var f = await Fixture.CreateAsync();
        var dock = await f.SeedDockAsync(DockStatuses.Free);
        var arrived = await f.SeedAppointmentAsync(dock, AppointmentStatuses.Scheduled);
        var other = await f.SeedAppointmentAsync(dock, AppointmentStatuses.Scheduled);
        await f.TransitionAsync(arrived, AppointmentStatuses.Arrived);

        await f.TransitionAsync(other, AppointmentStatuses.Cancelled);

        Assert.Equal(f.Id(StatusDomains.DockStatus, DockStatuses.Occupied), await f.DockStatusIdAsync(dock));
    }

    [Fact]
    public async Task Arrival_in_another_dock_does_not_block_release()
    {
        await using var f = await Fixture.CreateAsync();
        var dock = await f.SeedDockAsync(DockStatuses.Free);
        var otherDock = await f.SeedDockAsync(DockStatuses.Free);
        var appt = await f.SeedAppointmentAsync(dock, AppointmentStatuses.Scheduled);
        var elsewhere = await f.SeedAppointmentAsync(otherDock, AppointmentStatuses.Scheduled);
        await f.TransitionAsync(appt, AppointmentStatuses.Arrived);
        await f.TransitionAsync(elsewhere, AppointmentStatuses.Arrived);

        await f.TransitionAsync(appt, AppointmentStatuses.Completed);

        Assert.Equal(f.Id(StatusDomains.DockStatus, DockStatuses.Free), await f.DockStatusIdAsync(dock));
        Assert.Equal(f.Id(StatusDomains.DockStatus, DockStatuses.Occupied), await f.DockStatusIdAsync(otherDock));
    }

    [Fact]
    public async Task No_show_after_arrival_is_rejected_by_the_lateral_rules()
    {
        await using var f = await Fixture.CreateAsync();
        var dock = await f.SeedDockAsync(DockStatuses.Free);
        var appt = await f.SeedAppointmentAsync(dock, AppointmentStatuses.Scheduled);
        await f.TransitionAsync(appt, AppointmentStatuses.Arrived);

        await Assert.ThrowsAsync<StatusRuleException>(() => f.TransitionAsync(appt, AppointmentStatuses.NoShow));
        Assert.Equal(f.Id(StatusDomains.DockStatus, DockStatuses.Occupied), await f.DockStatusIdAsync(dock));
    }

    // ================================================================ fixture

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly Dictionary<string, int> _statusIds = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _lookupIds = new(StringComparer.OrdinalIgnoreCase);
        private int _nextDockId = 100;
        private int _nextAppointmentId = 1000;

        private Fixture(TeikemDbContext db, ServiceProvider services, TripTestLookups lookups)
        {
            Db = db;
            Services = services;
            Lookups = lookups;
        }

        public TeikemDbContext Db { get; }
        public ServiceProvider Services { get; }
        public TripTestLookups Lookups { get; }
        public StatusService Statuses => Services.GetRequiredService<StatusService>();

        public int Id(string domain, string code) => _statusIds[domain + "|" + code];
        private int LookupId(string domain, string code) => _lookupIds[domain + "|" + code];

        public static async Task<Fixture> CreateAsync()
        {
            var tenant = new TenantContext { TenantId = TenantId, UserId = 1, IsAuthenticated = true };
            var options = new DbContextOptionsBuilder<TeikemDbContext>()
                .UseInMemoryDatabase("dock-appt-effect-" + Guid.NewGuid(), b => b.EnableNullChecks(false))
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options;
            var db = new TeikemDbContext(options, tenant);
            var lookups = new TripTestLookups();

            var services = new ServiceCollection();
            services.AddSingleton(db);
            services.AddSingleton<ITenantContext>(tenant);
            services.AddSingleton<ILookupCache>(lookups);
            services.AddSingleton<IStatusTransitionEffect>(sp => new DockAppointmentStatusEffect(db, sp));
            // PermissionService solo lo usa GetHistoryAsync (no se usa aquí).
            services.AddSingleton(sp => new StatusService(db, tenant, lookups, sp.GetServices<IStatusTransitionEffect>(), null!));
            var provider = services.BuildServiceProvider();

            var f = new Fixture(db, provider, lookups);
            await f.SeedCatalogsAsync();
            return f;
        }

        private async Task SeedCatalogsAsync()
        {
            var id = 1;
            var all = new List<LookupCode>();
            LookupCode L(string entity, string code)
            {
                var l = new LookupCode { LookupCodeId = id++, Entity = entity, InternalCode = code, LabelJson = $"{{\"es\":\"{code}\"}}", IsActive = true };
                all.Add(l);
                _lookupIds[entity + "|" + code] = l.LookupCodeId;
                return l;
            }
            var pipe = L(LookupDomains.StageKind, StageKinds.Pipeline);
            var lat = L(LookupDomains.StageKind, StageKinds.Lateral);
            var term = L(LookupDomains.StageKind, StageKinds.Terminal);
            foreach (var e in new[] { EntityTypes.Warehouse, EntityTypes.WarehouseDock, EntityTypes.DockAppointment })
                L(LookupDomains.EntityType, e);
            foreach (var c in new[] { DockTypes.Inbound, DockTypes.Outbound, DockTypes.Both })
                L(LookupDomains.DockType, c);
            foreach (var c in new[] { DockDirections.Inbound, DockDirections.Outbound })
                L(LookupDomains.DockDirection, c);
            var country = L(LookupDomains.Country, "PR");
            Db.LookupCodes.AddRange(all);
            Lookups.Load(all);

            var sid = 100;
            void S(string domain, string code, LookupCode kind, int sort, bool initial = false)
            {
                var s = new StatusCode { StatusCodeId = sid++, Entity = domain, InternalCode = code, LabelJson = $"{{\"es\":\"{code}\"}}", StageKindLookupId = kind.LookupCodeId, SortOrder = sort, IsInitial = initial, IsActive = true };
                Db.StatusCodes.Add(s);
                _statusIds[domain + "|" + code] = s.StatusCodeId;
            }
            S(StatusDomains.WarehouseStatus, WarehouseStatuses.Active, pipe, 1, true);
            S(StatusDomains.WarehouseStatus, WarehouseStatuses.Inactive, term, 2);
            S(StatusDomains.DockStatus, DockStatuses.Free, pipe, 1, true);
            S(StatusDomains.DockStatus, DockStatuses.Occupied, lat, 2);
            S(StatusDomains.DockStatus, DockStatuses.Maintenance, lat, 3);
            S(StatusDomains.AppointmentStatus, AppointmentStatuses.Scheduled, pipe, 1, true);
            S(StatusDomains.AppointmentStatus, AppointmentStatuses.Arrived, pipe, 2);
            S(StatusDomains.AppointmentStatus, AppointmentStatuses.Completed, term, 3);
            S(StatusDomains.AppointmentStatus, AppointmentStatuses.NoShow, lat, 4);
            S(StatusDomains.AppointmentStatus, AppointmentStatuses.Cancelled, term, 5);

            // 3G: DOCK_APPOINTMENT — NO_SHOW y CANCELLED solo desde SCHEDULED.
            var apptType = LookupId(LookupDomains.EntityType, EntityTypes.DockAppointment);
            var leId = 1;
            foreach (var lateral in new[] { AppointmentStatuses.NoShow, AppointmentStatuses.Cancelled })
                Db.StatusLateralEntries.Add(new StatusLateralEntry
                {
                    StatusLateralEntryId = leId++, TenantId = null, EntityTypeLookupId = apptType,
                    LateralStatusCodeId = Id(StatusDomains.AppointmentStatus, lateral),
                    FromStatusCodeId = Id(StatusDomains.AppointmentStatus, AppointmentStatuses.Scheduled), IsAllowed = true,
                });

            Db.Set<Warehouse>().Add(new Warehouse
            {
                WarehouseId = WarehouseId, PublicId = Guid.NewGuid(), TenantId = TenantId, Code = "W1", Name = "Almacén 1",
                CountryLookupId = country.LookupCodeId, StatusCodeId = Id(StatusDomains.WarehouseStatus, WarehouseStatuses.Active), IsActive = true,
            });
            await Db.SaveChangesAsync();
            Db.ChangeTracker.Clear();
        }

        public async Task<int> SeedDockAsync(string statusCode)
        {
            var dock = new WarehouseDock
            {
                WarehouseDockId = _nextDockId++, WarehouseId = WarehouseId, Code = $"D{_nextDockId}",
                DockTypeLookupId = LookupId(LookupDomains.DockType, DockTypes.Both),
                StatusCodeId = Id(StatusDomains.DockStatus, statusCode), IsActive = true,
            };
            Db.Set<WarehouseDock>().Add(dock);
            await Db.SaveChangesAsync();
            Db.ChangeTracker.Clear();
            return dock.WarehouseDockId;
        }

        public async Task<int> SeedAppointmentAsync(int dockId, string statusCode)
        {
            var start = DateTime.UtcNow.Date.AddDays(1).AddHours(9);
            var appt = new DockAppointment
            {
                DockAppointmentId = _nextAppointmentId++, TenantId = TenantId, WarehouseId = WarehouseId, WarehouseDockId = dockId,
                DirectionLookupId = LookupId(LookupDomains.DockDirection, DockDirections.Inbound),
                ScheduledStartUtc = start, ScheduledEndUtc = start.AddHours(1),
                StatusCodeId = Id(StatusDomains.AppointmentStatus, statusCode), CreatedAtUtc = DateTime.UtcNow,
            };
            Db.Set<DockAppointment>().Add(appt);
            await Db.SaveChangesAsync();
            Db.ChangeTracker.Clear();
            return appt.DockAppointmentId;
        }

        /// <summary>
        /// Transición como la hace DockAppointmentService: muelle cargado tracked (el "bloqueo"), cita tracked,
        /// TransitionAsync (dispara el efecto), asignar y guardar.
        /// </summary>
        public async Task TransitionAsync(int appointmentId, string toCode)
        {
            Db.ChangeTracker.Clear();
            try
            {
                var appt = await Db.Set<DockAppointment>().SingleAsync(a => a.DockAppointmentId == appointmentId);
                await Db.Set<WarehouseDock>().SingleAsync(d => d.WarehouseDockId == appt.WarehouseDockId);
                var to = await Statuses.TransitionAsync(StatusDomains.AppointmentStatus, EntityTypes.DockAppointment, appointmentId,
                    appt.StatusCodeId, toCode, null, default);
                appt.StatusCodeId = to.StatusCodeId;
                await Db.SaveChangesAsync();
            }
            finally
            {
                Db.ChangeTracker.Clear();
            }
        }

        public Task<int> DockStatusIdAsync(int dockId)
            => Db.Set<WarehouseDock>().AsNoTracking().Where(d => d.WarehouseDockId == dockId).Select(d => d.StatusCodeId).SingleAsync();

        public async Task<List<string>> HistoryCodesAsync(string entityType, int entityId)
        {
            var typeId = LookupId(LookupDomains.EntityType, entityType);
            var toIds = await Db.EntityStatusHistories.AsNoTracking()
                .Where(h => h.EntityTypeLookupId == typeId && h.EntityId == entityId)
                .OrderBy(h => h.EntityStatusHistoryId).Select(h => h.ToStatusCodeId).ToListAsync();
            var codes = await Db.StatusCodes.AsNoTracking().Where(s => toIds.Contains(s.StatusCodeId)).ToDictionaryAsync(s => s.StatusCodeId, s => s.InternalCode);
            return toIds.Select(i => codes[i]).ToList();
        }

        public async ValueTask DisposeAsync() => await Services.DisposeAsync();
    }
}
