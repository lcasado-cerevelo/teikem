using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Catalogs;
using Teikem.Domain.Constants;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 6 (P5) — efecto del dominio WarehouseTaskStatus (R10): la última PUTAWAY de un recibo que llega a DONE o CANCELLED
/// cierra el recibo RECEIVED → PUTAWAY. InMemory con StatusService y el efecto reales (StatusService resuelto de forma
/// perezosa, como en producción). Pipelines sembrados igual que logistica-db-seed.sql (Lote 6).
/// </summary>
public class WarehouseTaskStatusEffectTests
{
    private const int TenantId = 1;
    private const int WarehouseId = 10;

    [Fact]
    public async Task Last_putaway_done_moves_receipt_to_putaway_with_history()
    {
        await using var f = await Fixture.CreateAsync();
        var receipt = await f.SeedReceiptAsync(ReceiptStatuses.Received);
        var task = await f.SeedTaskAsync(WarehouseTaskTypes.Putaway, WarehouseTaskStatuses.InProgress, receipt);

        await f.TransitionTaskAsync(task, WarehouseTaskStatuses.Done);

        Assert.Equal(f.Id(StatusDomains.ReceiptStatus, ReceiptStatuses.Putaway), await f.ReceiptStatusIdAsync(receipt));
        Assert.Contains(ReceiptStatuses.Putaway, await f.HistoryCodesAsync(EntityTypes.Receipt, receipt));
    }

    [Fact]
    public async Task Another_open_putaway_keeps_receipt_received()
    {
        await using var f = await Fixture.CreateAsync();
        var receipt = await f.SeedReceiptAsync(ReceiptStatuses.Received);
        var task = await f.SeedTaskAsync(WarehouseTaskTypes.Putaway, WarehouseTaskStatuses.InProgress, receipt);
        var other = await f.SeedTaskAsync(WarehouseTaskTypes.Putaway, WarehouseTaskStatuses.Pending, receipt);

        await f.TransitionTaskAsync(task, WarehouseTaskStatuses.Done);
        Assert.Equal(f.Id(StatusDomains.ReceiptStatus, ReceiptStatuses.Received), await f.ReceiptStatusIdAsync(receipt));

        // Al cerrar la última (aunque sea cancelándola) el recibo pasa a PUTAWAY.
        await f.TransitionTaskAsync(other, WarehouseTaskStatuses.Cancelled);
        Assert.Equal(f.Id(StatusDomains.ReceiptStatus, ReceiptStatuses.Putaway), await f.ReceiptStatusIdAsync(receipt));
    }

    [Fact]
    public async Task Cancelled_last_putaway_is_terminal_and_closes_receipt()
    {
        await using var f = await Fixture.CreateAsync();
        var receipt = await f.SeedReceiptAsync(ReceiptStatuses.Received);
        var task = await f.SeedTaskAsync(WarehouseTaskTypes.Putaway, WarehouseTaskStatuses.Pending, receipt);

        await f.TransitionTaskAsync(task, WarehouseTaskStatuses.Cancelled);

        Assert.Equal(f.Id(StatusDomains.ReceiptStatus, ReceiptStatuses.Putaway), await f.ReceiptStatusIdAsync(receipt));
        // CANCELLED es terminal: la tarea ya no admite transiciones.
        await Assert.ThrowsAsync<Teikem.Infrastructure.Exceptions.StatusRuleException>(
            () => f.TransitionTaskAsync(task, WarehouseTaskStatuses.InProgress));
    }

    [Fact]
    public async Task Open_putaway_of_another_receipt_does_not_block()
    {
        await using var f = await Fixture.CreateAsync();
        var receipt = await f.SeedReceiptAsync(ReceiptStatuses.Received);
        var otherReceipt = await f.SeedReceiptAsync(ReceiptStatuses.Received);
        var task = await f.SeedTaskAsync(WarehouseTaskTypes.Putaway, WarehouseTaskStatuses.InProgress, receipt);
        await f.SeedTaskAsync(WarehouseTaskTypes.Putaway, WarehouseTaskStatuses.Pending, otherReceipt);

        await f.TransitionTaskAsync(task, WarehouseTaskStatuses.Done);

        Assert.Equal(f.Id(StatusDomains.ReceiptStatus, ReceiptStatuses.Putaway), await f.ReceiptStatusIdAsync(receipt));
        Assert.Equal(f.Id(StatusDomains.ReceiptStatus, ReceiptStatuses.Received), await f.ReceiptStatusIdAsync(otherReceipt));
    }

    [Fact]
    public async Task Last_putaway_of_a_receipt_completed_with_variance_moves_it_to_putaway()
    {
        // Lote 13: RECEIVED_VARIANCE (lateral) → PUTAWAY por la entrada lateral sembrada.
        await using var f = await Fixture.CreateAsync();
        var receipt = await f.SeedReceiptAsync(ReceiptStatuses.ReceivedWithVariance);
        var task = await f.SeedTaskAsync(WarehouseTaskTypes.Putaway, WarehouseTaskStatuses.InProgress, receipt);

        await f.TransitionTaskAsync(task, WarehouseTaskStatuses.Done);

        Assert.Equal(f.Id(StatusDomains.ReceiptStatus, ReceiptStatuses.Putaway), await f.ReceiptStatusIdAsync(receipt));
        Assert.Equal(new[] { ReceiptStatuses.Putaway }, await f.HistoryCodesAsync(EntityTypes.Receipt, receipt));
    }

    [Theory]
    [InlineData(ReceiptStatuses.Expected)]
    [InlineData(ReceiptStatuses.Receiving)]
    [InlineData(ReceiptStatuses.Discrepancy)]
    public async Task Receipt_outside_received_is_not_changed(string open)
    {
        await using var f = await Fixture.CreateAsync();
        var receipt = await f.SeedReceiptAsync(open);
        var task = await f.SeedTaskAsync(WarehouseTaskTypes.Putaway, WarehouseTaskStatuses.InProgress, receipt);

        await f.TransitionTaskAsync(task, WarehouseTaskStatuses.Done);

        Assert.Equal(f.Id(StatusDomains.ReceiptStatus, open), await f.ReceiptStatusIdAsync(receipt));
        Assert.Empty(await f.HistoryCodesAsync(EntityTypes.Receipt, receipt));
    }

    [Fact]
    public async Task Replenish_task_has_no_effect()
    {
        await using var f = await Fixture.CreateAsync();
        var receipt = await f.SeedReceiptAsync(ReceiptStatuses.Received);
        var task = await f.SeedTaskAsync(WarehouseTaskTypes.Replenish, WarehouseTaskStatuses.InProgress, receipt);

        await f.TransitionTaskAsync(task, WarehouseTaskStatuses.Done);

        Assert.Equal(f.Id(StatusDomains.ReceiptStatus, ReceiptStatuses.Received), await f.ReceiptStatusIdAsync(receipt));
    }

    [Fact]
    public async Task Start_of_putaway_has_no_effect()
    {
        await using var f = await Fixture.CreateAsync();
        var receipt = await f.SeedReceiptAsync(ReceiptStatuses.Received);
        var task = await f.SeedTaskAsync(WarehouseTaskTypes.Putaway, WarehouseTaskStatuses.Pending, receipt);

        await f.TransitionTaskAsync(task, WarehouseTaskStatuses.InProgress);

        Assert.Equal(f.Id(StatusDomains.ReceiptStatus, ReceiptStatuses.Received), await f.ReceiptStatusIdAsync(receipt));
    }

    // ================================================================ fixture

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly Dictionary<string, int> _statusIds = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _lookupIds = new(StringComparer.OrdinalIgnoreCase);
        private int _nextReceiptId = 100;
        private int _nextTaskId = 1000;

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
                .UseInMemoryDatabase("wh-task-effect-" + Guid.NewGuid(), b => b.EnableNullChecks(false))
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options;
            var db = new TeikemDbContext(options, tenant);
            var lookups = new TripTestLookups();

            var services = new ServiceCollection();
            services.AddSingleton(db);
            services.AddSingleton<ITenantContext>(tenant);
            services.AddSingleton<ILookupCache>(lookups);
            services.AddSingleton<IStatusTransitionEffect>(sp => new WarehouseTaskStatusEffect(db, sp, lookups));
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
            foreach (var e in new[] { EntityTypes.Warehouse, EntityTypes.Receipt, EntityTypes.WarehouseTask, EntityTypes.Product })
                L(LookupDomains.EntityType, e);
            foreach (var c in new[] { WarehouseTaskTypes.Putaway, WarehouseTaskTypes.Replenish, WarehouseTaskTypes.Count, WarehouseTaskTypes.CrossDock })
                L(LookupDomains.WarehouseTaskType, c);
            foreach (var c in new[] { ReceiptTypes.Asn, ReceiptTypes.Blind, ReceiptTypes.Return })
                L(LookupDomains.ReceiptType, c);
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
            // Lote 13: los seis estatus del recibo y sus entradas laterales, como logistica-db-seed.sql.
            S(StatusDomains.ReceiptStatus, ReceiptStatuses.Expected, pipe, 1, true);
            S(StatusDomains.ReceiptStatus, ReceiptStatuses.Receiving, pipe, 2);
            S(StatusDomains.ReceiptStatus, ReceiptStatuses.Discrepancy, lat, 3);
            S(StatusDomains.ReceiptStatus, ReceiptStatuses.Received, pipe, 4);
            S(StatusDomains.ReceiptStatus, ReceiptStatuses.ReceivedWithVariance, lat, 5);
            S(StatusDomains.ReceiptStatus, ReceiptStatuses.Putaway, term, 6);
            S(StatusDomains.WarehouseTaskStatus, WarehouseTaskStatuses.Pending, pipe, 1, true);
            S(StatusDomains.WarehouseTaskStatus, WarehouseTaskStatuses.InProgress, pipe, 2);
            S(StatusDomains.WarehouseTaskStatus, WarehouseTaskStatuses.Done, term, 3);
            S(StatusDomains.WarehouseTaskStatus, WarehouseTaskStatuses.Cancelled, term, 4);
            ReceiptStatusSeed.AddLateralEntries(Db, LookupId(LookupDomains.EntityType, EntityTypes.Receipt), Id);

            Db.Set<Warehouse>().Add(new Warehouse
            {
                WarehouseId = WarehouseId, PublicId = Guid.NewGuid(), TenantId = TenantId, Code = "W1", Name = "Almacén 1",
                CountryLookupId = country.LookupCodeId, StatusCodeId = Id(StatusDomains.WarehouseStatus, WarehouseStatuses.Active), IsActive = true,
            });
            await Db.SaveChangesAsync();
            Db.ChangeTracker.Clear();
        }

        public async Task<int> SeedReceiptAsync(string statusCode)
        {
            var receipt = new ReceiptHeader
            {
                ReceiptHeaderId = _nextReceiptId++, PublicId = Guid.NewGuid(), TenantId = TenantId, WarehouseId = WarehouseId,
                ReceiptTypeLookupId = LookupId(LookupDomains.ReceiptType, ReceiptTypes.Blind), Number = $"REC-{_nextReceiptId:00000}",
                StatusCodeId = Id(StatusDomains.ReceiptStatus, statusCode), IsActive = true, CreatedAtUtc = DateTime.UtcNow,
            };
            Db.Set<ReceiptHeader>().Add(receipt);
            await Db.SaveChangesAsync();
            Db.ChangeTracker.Clear();
            return receipt.ReceiptHeaderId;
        }

        public async Task<int> SeedTaskAsync(string type, string statusCode, int receiptId)
        {
            var task = new WarehouseTask
            {
                WarehouseTaskId = _nextTaskId++, TenantId = TenantId, WarehouseId = WarehouseId,
                TaskTypeLookupId = LookupId(LookupDomains.WarehouseTaskType, type),
                StatusCodeId = Id(StatusDomains.WarehouseTaskStatus, statusCode), Quantity = 5m,
                RefEntityLookupId = LookupId(LookupDomains.EntityType, EntityTypes.Receipt), RefId = receiptId,
                Priority = 100, CreatedAtUtc = DateTime.UtcNow,
            };
            Db.Set<WarehouseTask>().Add(task);
            await Db.SaveChangesAsync();
            Db.ChangeTracker.Clear();
            return task.WarehouseTaskId;
        }

        /// <summary>Transición como la hace WarehouseTaskService: tarea tracked, TransitionAsync (dispara el efecto), asignar y guardar.</summary>
        public async Task TransitionTaskAsync(int taskId, string toCode)
        {
            Db.ChangeTracker.Clear();
            var task = await Db.Set<WarehouseTask>().SingleAsync(t => t.WarehouseTaskId == taskId);
            var to = await Statuses.TransitionAsync(StatusDomains.WarehouseTaskStatus, EntityTypes.WarehouseTask, taskId, task.StatusCodeId, toCode, null, default);
            task.StatusCodeId = to.StatusCodeId;
            await Db.SaveChangesAsync();
            Db.ChangeTracker.Clear();
        }

        public Task<int> ReceiptStatusIdAsync(int receiptId)
            => Db.Set<ReceiptHeader>().AsNoTracking().Where(r => r.ReceiptHeaderId == receiptId).Select(r => r.StatusCodeId).SingleAsync();

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
