using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Constants;
using Teikem.Domain.Orders;
using Teikem.Domain.Trips;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Trips;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 5 (P4) — Sala de despacho: selector de rutas no despachadas, despacho individual y despacho en lote.
/// - ListDispatchableAsync: rutas activas en DRAFT/PLANNED del día (sin fecha = hoy UTC) con sus avisos y bloqueantes
///   (TripIssueBuilder, incluida la elegibilidad de las órdenes). CanDispatch = sin bloqueantes. Orden: zona y código.
/// - DispatchAsync: CATALOG (usa chofer y vehículo), comentario ≤ 500 y, dentro de la transacción con el Trip bloqueado:
///   RowVersion (409), estatus DRAFT/PLANNED (422), bloqueantes (422 status_rule 'La ruta … no se puede despachar: …' con
///   errors {códigoIssue: [mensaje]}) y el recorrido del pipeline de TripStatus hasta DISPATCHED, una transición por paso
///   (sin DISPATCHED habilitada → 422). TripStatusEffect congela la ruta (ACTIVE) y lleva las órdenes hasta PLANNED.
/// - DispatchBatchAsync: cada ruta en su propia transacción, por TripId ascendente; siempre 200 con el resumen. Un id que no
///   es del tenant queda como ítem con 'Ruta no encontrada.'.
/// Despachar es irreversible y no crea DriverTrip (sin dinero en este lote).
/// </summary>
public sealed class TripDispatchService(
    TeikemDbContext db,
    ITenantContext tenant,
    StatusService statuses,
    ModuleService modules,
    TripIssueBuilder issueBuilder,
    TripReadService reader)
{
    // ================================================================ GET /trips/dispatchable

    public async Task<IReadOnlyList<DispatchableTripDto>> ListDispatchableAsync(DateOnly? date, CancellationToken ct)
    {
        ((TenantContext)tenant).RequireTenantId();
        var day = date ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var openIds = await OpenStatusIdsAsync(ct);

        var ids = await db.Trips.AsNoTracking()
            .Where(t => t.IsActive && t.PlanDate == day && openIds.Contains(t.StatusCodeId))
            .Select(t => t.TripId)
            .ToListAsync(ct);
        if (ids.Count == 0) return Array.Empty<DispatchableTripDto>();

        var items = await reader.ItemsAsync(ids, ct);
        var issues = await IssuesAsync(ids, ct);
        return items
            .Select(i =>
            {
                var list = issues.GetValueOrDefault(i.Id) ?? (IReadOnlyList<TripIssueDto>)Array.Empty<TripIssueDto>();
                return new DispatchableTripDto(i, !list.Any(x => x.Blocking), list);
            })
            .OrderBy(d => d.Trip.ZoneCode is null)
            .ThenBy(d => d.Trip.ZoneCode, StringComparer.Ordinal)
            .ThenBy(d => d.Trip.Code, StringComparer.Ordinal)
            .ToList();
    }

    // ================================================================ POST /trips/{publicId}/dispatch

    public async Task<TripDetailDto> DispatchAsync(Guid publicId, TripDispatchRequest? req, CancellationToken ct)
    {
        ((TenantContext)tenant).RequireTenantId();
        await modules.EnsureEnabledAsync(ModuleKeys.Catalog, ct);              // 403 module_disabled
        var comment = NormalizeComment(req?.Comment);

        await DispatchCoreAsync(publicId, comment, req?.RowVersion, ct);
        return await reader.GetAsync(publicId, ct);
    }

    // ================================================================ POST /trips/dispatch (lote)

    public async Task<TripBatchDispatchResultDto> DispatchBatchAsync(TripBatchDispatchRequest req, CancellationToken ct)
    {
        ((TenantContext)tenant).RequireTenantId();
        var (ids, error) = DispatchBatchRules.Validate(req?.TripPublicIds);
        if (error is not null) throw new ValidationException("tripPublicIds", error);
        await modules.EnsureEnabledAsync(ModuleKeys.Catalog, ct);
        var comment = NormalizeComment(req?.Comment);

        // Solo rutas del tenant (filtro global); las demás quedan como 'Ruta no encontrada.' sin decir por qué.
        var found = await db.Trips.AsNoTracking()
            .Where(t => ids.Contains(t.PublicId))
            .Select(t => new { t.PublicId, t.TripId, t.Code })
            .ToListAsync(ct);

        var results = new Dictionary<Guid, TripDispatchResultItemDto>();
        foreach (var t in found.OrderBy(x => x.TripId))   // orden de bloqueo del lote: Trips por id ascendente
        {
            try
            {
                await DispatchCoreAsync(t.PublicId, comment, null, ct);
                results[t.PublicId] = new TripDispatchResultItemDto(t.PublicId, t.Code, true, null, Array.Empty<TripIssueDto>());
            }
            catch (TeikemException ex)
            {
                // La transacción de ESTA ruta ya se revirtió; se limpia lo que haya quedado en el tracker.
                db.ChangeTracker.Clear();
                var issues = (await IssuesAsync(new[] { t.TripId }, ct)).GetValueOrDefault(t.TripId)
                             ?? (IReadOnlyList<TripIssueDto>)Array.Empty<TripIssueDto>();
                results[t.PublicId] = new TripDispatchResultItemDto(t.PublicId, t.Code, false, ex.Message, issues);
            }
        }

        var items = ids
            .Select(id => results.GetValueOrDefault(id)
                          ?? new TripDispatchResultItemDto(id, null, false, DispatchBatchRules.TripNotFoundMessage, Array.Empty<TripIssueDto>()))
            .ToList();
        var summary = DispatchBatchRules.Summarize(items.Select(i => i.Dispatched));
        return new TripBatchDispatchResultDto(summary.Requested, summary.Dispatched, items);
    }

    // ================================================================ núcleo del despacho (una ruta, una transacción)

    private async Task DispatchCoreAsync(Guid publicId, string? comment, string? rowVersion, CancellationToken ct)
    {
        await db.RunInTransactionAsync(async ct2 =>
        {
            // 1. Bloqueo del Trip (paso 2 del orden de bloqueo) y control optimista.
            var trip = await db.LockTripAsync(publicId, ct2);                   // 404 'Ruta no encontrada.'
            TripDispatchRowVersion.Ensure(db, trip, rowVersion);                 // 409 si cambió

            // 2. Solo DRAFT/PLANNED (activa).
            var pipeline = await statuses.GetPipelineAsync(StatusDomains.TripStatus, includeDisabled: true, ct2);
            var current = pipeline.FirstOrDefault(s => s.Id == trip.StatusCodeId);
            var currentCode = current?.Code ?? string.Empty;
            if (!trip.IsActive || !TripRules.IsEditable(currentCode))
                throw new StatusRuleException(TripRules.NotEditableMessage(trip.Code, trip.IsActive ? currentCode : TripStatuses.Cancelled));

            // 3. Bloqueantes (chofer/vehículo, disponibilidad, paradas y elegibilidad de las órdenes).
            var issues = (await IssuesAsync(new[] { trip.TripId }, ct2)).GetValueOrDefault(trip.TripId)
                         ?? (IReadOnlyList<TripIssueDto>)Array.Empty<TripIssueDto>();
            var blocking = issues.Where(i => i.Blocking).ToList();
            if (blocking.Count > 0)
                throw new StatusRuleException(DispatchBatchRules.DispatchBlocked(trip.Code, blocking.Select(i => i.Message)))
                {
                    Errors = blocking.GroupBy(i => i.Code, StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(g => g.Key, g => g.Select(i => i.Message).ToArray()),
                };

            // 4-5. Pipeline de TripStatus hasta DISPATCHED, una transición (e historial) por paso.
            var target = pipeline.FirstOrDefault(s => Same(s.Code, TripStatuses.Dispatched));
            if (target is null || !target.IsEnabled)
                throw new StatusRuleException(DispatchBatchRules.PipelineMissing(TripStatuses.Dispatched));
            var stages = pipeline.Select(s => new PipelineStage(s.Code, s.SortOrder, s.StageKind, s.IsInitial, s.IsEnabled)).ToList();
            var steps = PipelinePath.StepsTo(stages, currentCode, TripStatuses.Dispatched);
            if (steps.Count == 0 || !Same(steps[^1], TripStatuses.Dispatched))
                throw new StatusRuleException(DispatchBatchRules.PipelineMissing(TripStatuses.Dispatched));

            for (var i = 0; i < steps.Count; i++)
            {
                var isLast = i == steps.Count - 1;
                var stepComment = isLast ? comment ?? DispatchBatchRules.DefaultDispatchComment : DispatchBatchRules.DispatchStepComment;
                // TripStatusEffect (→ DISPATCHED) congela la ruta y avanza las órdenes; el Trip cambia DESPUÉS de la transición.
                var to = await statuses.TransitionAsync(StatusDomains.TripStatus, EntityTypes.Trip, trip.TripId, trip.StatusCodeId,
                    steps[i], stepComment, ct2);
                trip.StatusCodeId = to.StatusCodeId;
            }

            // 6. Guardar (un choque de RowVersion es 409).
            await db.SaveGuardedAsync(DbExtensions.ConcurrencyMessage, ct2);
        }, ct);
    }

    // ================================================================ helpers

    private async Task<Dictionary<int, IReadOnlyList<TripIssueDto>>> IssuesAsync(IReadOnlyCollection<int> tripIds, CancellationToken ct)
    {
        var built = await issueBuilder.BuildAsync(tripIds, true, ct);
        var result = new Dictionary<int, IReadOnlyList<TripIssueDto>>();
        foreach (var kv in built)
            result[kv.Key] = kv.Value.Select(i => new TripIssueDto(i.Code, i.Message, i.Blocking)).ToList();
        return result;
    }

    /// <summary>Ids de los estatus abiertos de TripStatus (DRAFT y PLANNED).</summary>
    private async Task<List<int>> OpenStatusIdsAsync(CancellationToken ct)
    {
        var codes = new[] { TripStatuses.Draft, TripStatuses.Planned };
        return await db.StatusCodes.AsNoTracking()
            .Where(s => s.Entity == StatusDomains.TripStatus && codes.Contains(s.InternalCode))
            .Select(s => s.StatusCodeId).ToListAsync(ct);
    }

    private static string? NormalizeComment(string? comment)
    {
        var text = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();
        if (text is { Length: > StatusService.CommentMaxLength }) throw new ValidationException("comment", StatusService.CommentTooLongMessage);
        return text;
    }

    private static bool Same(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
