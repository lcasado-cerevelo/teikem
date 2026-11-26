using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Teikem.Domain.Catalogs;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 249 (P1) — StatusService.StepBackAsync: un paso ATRÁS en el pipeline, solo de una etapa PIPELINE habilitada a la PIPELINE habilitada inmediatamente anterior
/// por SortOrder (con los reordenamientos y las etapas apagadas de la compañía). Nunca sale de un lateral ni de un terminal, nunca salta etapas, valida el comentario,
/// escribe EntityStatusHistory y corre los efectos del dominio. Lo usa «Reabrir empaque» (PickBatchStatus PACKED → COLLECTED). InMemory con el StatusService real.
/// </summary>
public class StatusStepBackTests
{
    private const int TenantId = 1;
    private const int EntityId = 77;
    private const string Demo = "DemoStatus";
    private const string DemoEntity = "DEMO_ENTITY";

    [Fact]
    public async Task Packed_steps_back_to_collected_with_history_comment_and_effects()
    {
        await using var f = await Fixture.CreateAsync();
        var to = await f.Service.StepBackAsync(StatusDomains.PickBatchStatus, EntityTypes.PickBatch, EntityId,
            f.Id(StatusDomains.PickBatchStatus, PickBatchStatuses.Packed), PickBatchStatuses.Collected, "Empaque reabierto: el cliente agregó 5 cajas", default);
        await f.Db.SaveChangesAsync();

        Assert.Equal(PickBatchStatuses.Collected, to.InternalCode);
        var h = Assert.Single(await f.Db.EntityStatusHistories.AsNoTracking().ToListAsync());
        Assert.Equal(f.Id(StatusDomains.PickBatchStatus, PickBatchStatuses.Packed), h.FromStatusCodeId);
        Assert.Equal(to.StatusCodeId, h.ToStatusCodeId);
        Assert.Equal(EntityId, h.EntityId);
        Assert.Equal(TenantId, h.TenantId);
        Assert.Equal(5, h.ChangedBy);
        Assert.Equal("Empaque reabierto: el cliente agregó 5 cajas", h.Comment);
        Assert.False(h.IsSkipped);
        var ctx = Assert.Single(f.Effects.Seen);   // solo el efecto de su dominio
        Assert.Equal(PickBatchStatuses.Packed, ctx.From!.InternalCode);
        Assert.Equal(PickBatchStatuses.Collected, ctx.To.InternalCode);
        Assert.Equal(StatusDomains.PickBatchStatus, ctx.StatusDomain);
    }

    [Fact]
    public async Task It_never_jumps_stages()
    {
        await using var f = await Fixture.CreateAsync();
        var ex = await Assert.ThrowsAsync<StatusRuleException>(() => f.Service.StepBackAsync(Demo, DemoEntity, EntityId, f.Id(Demo, "C"), "A", null, default));
        Assert.Equal("Solo se puede regresar de 'C' a la etapa inmediatamente anterior, 'B'.", ex.Message);
        Assert.Empty(await f.Db.EntityStatusHistories.AsNoTracking().ToListAsync());
        Assert.Empty(f.Effects.Seen);
    }

    [Fact]
    public async Task It_never_goes_forward_nor_stays()
    {
        await using var f = await Fixture.CreateAsync();
        await Assert.ThrowsAsync<StatusRuleException>(() => f.Service.StepBackAsync(Demo, DemoEntity, EntityId, f.Id(Demo, "A"), "B", null, default));
        var same = await Assert.ThrowsAsync<StatusRuleException>(() => f.Service.StepBackAsync(Demo, DemoEntity, EntityId, f.Id(Demo, "B"), "B", null, default));
        Assert.Equal("El registro ya está en ese estatus.", same.Message);
        var first = await Assert.ThrowsAsync<StatusRuleException>(() => f.Service.StepBackAsync(StatusDomains.PickBatchStatus, EntityTypes.PickBatch, EntityId,
            f.Id(StatusDomains.PickBatchStatus, PickBatchStatuses.Collected), PickBatchStatuses.Packed, null, default));
        Assert.Equal("'COLLECTED' es la primera etapa del pipeline: no hay a dónde regresar.", first.Message);
    }

    [Fact]
    public async Task It_never_leaves_a_terminal_nor_a_lateral_and_never_lands_on_them()
    {
        await using var f = await Fixture.CreateAsync();
        var terminal = await Assert.ThrowsAsync<StatusRuleException>(() => f.Service.StepBackAsync(StatusDomains.PickBatchStatus, EntityTypes.PickBatch, EntityId,
            f.Id(StatusDomains.PickBatchStatus, PickBatchStatuses.Cancelled), PickBatchStatuses.Packed, null, default));
        Assert.Equal("'CANCELLED' no es una etapa en curso del pipeline: no se puede regresar.", terminal.Message);
        var lateral = await Assert.ThrowsAsync<StatusRuleException>(() => f.Service.StepBackAsync(Demo, DemoEntity, EntityId, f.Id(Demo, "HOLD"), "B", null, default));
        Assert.Equal("'HOLD' no es una etapa en curso del pipeline: no se puede regresar.", lateral.Message);
        var toLateral = await Assert.ThrowsAsync<StatusRuleException>(() => f.Service.StepBackAsync(Demo, DemoEntity, EntityId, f.Id(Demo, "C"), "HOLD", null, default));
        Assert.Equal("Solo se regresa a una etapa en curso del pipeline; 'HOLD' no lo es.", toLateral.Message);
        Assert.Empty(await f.Db.EntityStatusHistories.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task A_stage_the_company_turned_off_is_skipped_and_cannot_be_the_target()
    {
        await using var f = await Fixture.CreateAsync();
        f.Db.StatusCodeOverrides.Add(new StatusCodeOverride { TenantId = TenantId, StatusCodeId = f.Id(Demo, "B"), IsEnabled = false });
        await f.Db.SaveChangesAsync();

        var off = await Assert.ThrowsAsync<StatusRuleException>(() => f.Service.StepBackAsync(Demo, DemoEntity, EntityId, f.Id(Demo, "C"), "B", null, default));
        Assert.Equal("El estatus 'B' no existe o no está habilitado para esta compañía.", off.Message);
        var to = await f.Service.StepBackAsync(Demo, DemoEntity, EntityId, f.Id(Demo, "C"), "A", null, default);   // la anterior HABILITADA es A
        Assert.Equal("A", to.InternalCode);
        var fromOff = await Assert.ThrowsAsync<StatusRuleException>(() => f.Service.StepBackAsync(Demo, DemoEntity, EntityId, f.Id(Demo, "B"), "A", null, default));
        Assert.Equal("El estatus actual 'B' no es una etapa habilitada de 'DemoStatus': no se puede regresar.", fromOff.Message);
    }

    [Fact]
    public async Task The_company_order_decides_which_stage_is_the_previous_one()
    {
        await using var f = await Fixture.CreateAsync();
        // la compañía pone B después de C: el orden queda A, C, B
        f.Db.StatusCodeOverrides.Add(new StatusCodeOverride { TenantId = TenantId, StatusCodeId = f.Id(Demo, "B"), IsEnabled = true, SortOverride = 35 });
        await f.Db.SaveChangesAsync();

        Assert.Equal("C", (await f.Service.StepBackAsync(Demo, DemoEntity, EntityId, f.Id(Demo, "B"), "C", null, default)).InternalCode);
        await Assert.ThrowsAsync<StatusRuleException>(() => f.Service.StepBackAsync(Demo, DemoEntity, EntityId, f.Id(Demo, "C"), "B", null, default));
    }

    [Fact]
    public async Task A_comment_longer_than_500_is_rejected_before_anything_is_written()
    {
        await using var f = await Fixture.CreateAsync();
        var ex = await Assert.ThrowsAsync<ValidationException>(() => f.Service.StepBackAsync(StatusDomains.PickBatchStatus, EntityTypes.PickBatch, EntityId,
            f.Id(StatusDomains.PickBatchStatus, PickBatchStatuses.Packed), PickBatchStatuses.Collected, new string('x', 501), default));
        Assert.Contains(StatusService.CommentTooLongMessage, ex.Message);
        Assert.Empty(f.Effects.Seen);
        // 500 exactos sí entran
        await f.Service.StepBackAsync(StatusDomains.PickBatchStatus, EntityTypes.PickBatch, EntityId,
            f.Id(StatusDomains.PickBatchStatus, PickBatchStatuses.Packed), PickBatchStatuses.Collected, new string('x', 500), default);
    }

    // ================================================================ fixture

    private sealed class RecordingEffect : IStatusTransitionEffect
    {
        public string StatusDomain => StatusDomains.PickBatchStatus;
        public List<StatusTransitionContext> Seen { get; } = new();
        public Task OnTransitionedAsync(StatusTransitionContext context, CancellationToken ct)
        {
            Seen.Add(context);
            return Task.CompletedTask;
        }
    }

    private sealed class OtherDomainEffect : IStatusTransitionEffect
    {
        public string StatusDomain => "OtherStatus";
        public Task OnTransitionedAsync(StatusTransitionContext context, CancellationToken ct) => throw new InvalidOperationException("No debe correr: es de otro dominio.");
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly Dictionary<string, int> _ids = new(StringComparer.OrdinalIgnoreCase);

        private Fixture(TeikemDbContext db, StatusService service, RecordingEffect effects)
        {
            Db = db;
            Service = service;
            Effects = effects;
        }

        public TeikemDbContext Db { get; }
        public StatusService Service { get; }
        public RecordingEffect Effects { get; }

        public int Id(string domain, string code) => _ids[domain + "|" + code];

        public static async Task<Fixture> CreateAsync()
        {
            var tenant = new TenantContext { TenantId = TenantId, UserId = 5, IsAuthenticated = true };
            var options = new DbContextOptionsBuilder<TeikemDbContext>()
                .UseInMemoryDatabase("status-stepback-" + Guid.NewGuid(), b => b.EnableNullChecks(false))
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options;
            var db = new TeikemDbContext(options, tenant);
            var lookups = new TripTestLookups();
            var effects = new RecordingEffect();
            // PermissionService solo lo usa GetHistoryAsync (no se usa aquí).
            var service = new StatusService(db, tenant, lookups, new IStatusTransitionEffect[] { effects, new OtherDomainEffect() }, null!);
            var f = new Fixture(db, service, effects);
            await f.SeedAsync(lookups);
            return f;
        }

        private async Task SeedAsync(TripTestLookups lookups)
        {
            var id = 1;
            var all = new List<LookupCode>();
            LookupCode L(string entity, string code)
            {
                var l = new LookupCode { LookupCodeId = id++, Entity = entity, InternalCode = code, LabelJson = $"{{\"es\":\"{code}\"}}", IsActive = true };
                all.Add(l);
                return l;
            }
            var pipe = L(LookupDomains.StageKind, StageKinds.Pipeline);
            var lat = L(LookupDomains.StageKind, StageKinds.Lateral);
            var term = L(LookupDomains.StageKind, StageKinds.Terminal);
            L(LookupDomains.EntityType, EntityTypes.PickBatch);
            L(LookupDomains.EntityType, DemoEntity);
            Db.LookupCodes.AddRange(all);
            lookups.Load(all);

            var sid = 100;
            void S(string domain, string code, LookupCode kind, int sort, bool initial = false)
            {
                var s = new StatusCode { StatusCodeId = sid++, Entity = domain, InternalCode = code, LabelJson = $"{{\"es\":\"{code}\"}}", StageKindLookupId = kind.LookupCodeId, SortOrder = sort, IsInitial = initial, IsActive = true };
                Db.StatusCodes.Add(s);
                _ids[domain + "|" + code] = s.StatusCodeId;
            }
            // como logistica-db-seed.sql: COLLECTED (inicial) → PACKED; CANCELLED terminal
            S(StatusDomains.PickBatchStatus, PickBatchStatuses.Collected, pipe, 1, true);
            S(StatusDomains.PickBatchStatus, PickBatchStatuses.Packed, pipe, 2);
            S(StatusDomains.PickBatchStatus, PickBatchStatuses.Cancelled, term, 3);
            // dominio de prueba: A → B → C → D (terminal) con un lateral HOLD
            S(Demo, "A", pipe, 10, true);
            S(Demo, "B", pipe, 20);
            S(Demo, "C", pipe, 30);
            S(Demo, "HOLD", lat, 40);
            S(Demo, "D", term, 50);
            await Db.SaveChangesAsync();
            Db.ChangeTracker.Clear();
        }

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }
}
