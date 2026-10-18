using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Constants;
using Teikem.Domain.Orders;
using Teikem.Domain.Trips;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Orders;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 107 — la estación de escaneo con acción escogida (ver <see cref="ScanActions"/>). El usuario escoge la acción y cada escaneo lleva la orden al estatus de esa acción:
/// - Estatus final → se ignora (FINAL). Ya está en ese estatus o más adelante → no se hace nada (ALREADY_BEYOND).
/// - Más atrás → se avanza de una vez: una fila de historial por estatus, todas con la misma fecha y hora y las de en medio «brincadas» (StatusService.AdvanceSkippingAsync); los avisos al cliente de los brincados no salen.
/// - Desde Intento fallido (lateral), «Llegó» la devuelve a Sin asignar y las demás acciones la llevan a ese estatus pasando por Sin asignar (brincado).
/// - Enrutar además la pone en una ruta abierta de su zona (el mismo escaneo Outbound de siempre); si no hay ruta que la reciba no cambia nada (NO_ROUTE).
/// Nada de esto revisa tarifa, crédito ni cliente: eso se revisó al someter la orden. Silencioso: nunca pregunta ni interrumpe; cada escaneo contesta con su resultado.
/// </summary>
public sealed class ScanActionService(TeikemDbContext db, ITenantContext tenant, OrderReadService orders, StatusService statuses, OutboundScanService outbound)
{
    public async Task<ScanActionResultDto> ApplyAsync(ScanActionRequest? req, CancellationToken ct)
    {
        if (tenant.TenantId is null) throw new ForbiddenException("No hay tenant activo en la sesión.");
        var action = ScanActions.Normalize(req?.Action) ?? throw new ValidationException("action", ScanActions.InvalidActionMessage);
        var (code, error) = OutboundScanRules.NormalizeCode(req?.Code);
        if (error is not null) throw new ValidationException("code", error);

        var lookup = await orders.LookupAsync(code!, OrderScope.Any, ct);
        matchedBy = lookup.MatchedBy;
        if (lookup.Matches.Count != 1)
        {
            var none = OutboundScanRules.Decide(new ScanFacts(lookup.Matches.Count, lookup.MatchedBy, null, null, null, null, null, req?.PlanDate ?? DateOnly.FromDateTime(DateTime.UtcNow)));
            return Result(action, lookup.Matches.Count > 1 ? ScanActionOutcomes.Ambiguous : ScanActionOutcomes.NotFound, none.Message, null, null, null, null, null);
        }

        var match = lookup.Matches[0];
        var targetCode = ScanActions.TargetStatus(action);
        var pipeline = await statuses.GetPipelineAsync(StatusDomains.OrderStatus, includeDisabled: true, ct);
        foreach (var s in pipeline) labels[s.Code] = s.Label;
        var stages = pipeline.Select(s => new PipelineStage(s.Code, s.SortOrder, s.StageKind, s.IsInitial, s.IsEnabled)).ToList();

        ScanActionResultDto? done = null;
        {
            await db.RunInTransactionAsync(async ct2 =>
            {
                var order = await db.ResolveOrderForWriteAsync(match.PublicId, OrderScope.Any, ct2);
                var current = pipeline.First(s => s.Id == order.StatusCodeId);

                if (string.Equals(current.StageKind, StageKinds.Terminal, StringComparison.OrdinalIgnoreCase))
                {
                    done = Result(action, ScanActionOutcomes.Final, FinalMessage(match.OrderNumber, current.Label), match, current, null, null, null);
                    return;
                }

                // Pasos desde el estatus actual: desde un lateral (Intento fallido) se cuenta desde «Recogida» (se regresa al almacén).
                var fromCode = string.Equals(current.StageKind, StageKinds.Lateral, StringComparison.OrdinalIgnoreCase) ? OrderStatuses.Pickup : current.Code;
                var steps = PipelinePath.StepsTo(stages, fromCode, targetCode);
                if (steps.Count == 0)
                {
                    // Enrutar sobre una orden que ya está Asignada o más adelante: si ya está en una ruta se dice cuál («Ya estaba en la ruta R-0001.»).
                    OutboundScanResultDto? known = null;
                    if (action == ScanActions.Route)
                    {
                        known = await outbound.ScanAsync(new OutboundScanRequest(match.OrderNumber, req?.PlanDate), ct2);
                        if (known.Outcome is not (ScanOutcomes.AlreadyAssigned or ScanOutcomes.FoundAssigned)) known = null;
                    }
                    done = Result(action, ScanActionOutcomes.AlreadyBeyond, known?.Message ?? BeyondMessage(match.OrderNumber, current.Label), match, current, null, null, known);
                    return;
                }

                var entityTypeId = await db.LookupCodes.AsNoTracking().Where(l => l.Entity == LookupDomains.EntityType && l.InternalCode == EntityTypes.TransportOrder).Select(l => l.LookupCodeId).FirstAsync(ct2);
                var orderId = order.TransportOrderId;
                var historyFloor = await db.EntityStatusHistories.AsNoTracking().Where(h => h.EntityTypeLookupId == entityTypeId && h.EntityId == orderId).MaxAsync(h => (long?)h.EntityStatusHistoryId, ct2) ?? 0L;
                var confirmedBefore = order.ConfirmedAtUtc;
                var to = await statuses.AdvanceSkippingAsync(StatusDomains.OrderStatus, EntityTypes.TransportOrder, order.TransportOrderId, order.StatusCodeId, steps, null, ct2);
                order.StatusCodeId = to.StatusCodeId;
                if (steps.Contains(OrderStatuses.Pickup) && order.ConfirmedAtUtc is null) order.ConfirmedAtUtc = DateTime.UtcNow;
                await db.SaveGuardedAsync(DbExtensions.ConcurrencyMessage, ct2);

                OutboundScanResultDto? route = null;
                if (action == ScanActions.Route)
                {
                    // Ya está Asignada: ahora a una ruta abierta de su zona. Si no hay quién la reciba se revierte todo (no queda Asignada sin ruta).
                    route = await outbound.ScanAsync(new OutboundScanRequest(match.OrderNumber, req?.PlanDate), ct2);
                    if (route.Outcome is not (ScanOutcomes.FoundAssigned or ScanOutcomes.AlreadyAssigned))
                    {
                        // Sin ruta que la reciba no queda Asignada: se deshace el avance (historial y estatus) y se contesta NO_ROUTE.
                        db.ChangeTracker.Clear();
                        var undo = await db.EntityStatusHistories.Where(h => h.EntityTypeLookupId == entityTypeId && h.EntityId == orderId && h.EntityStatusHistoryId > historyFloor).ToListAsync(ct2);
                        db.EntityStatusHistories.RemoveRange(undo);
                        var back = await db.TransportOrders.FirstAsync(o => o.TransportOrderId == orderId, ct2);
                        back.StatusCodeId = current.Id;
                        back.ConfirmedAtUtc = confirmedBefore;
                        await db.SaveChangesAsync(ct2);
                        done = Result(action, ScanActionOutcomes.NoRoute, route.Message, match, current, null, null, route);
                        return;
                    }
                }

                var message = route is not null
                    ? route.Message + (steps.Count > 1 ? $" (brincó {steps.Count - 1} estatus)" : string.Empty)   // «Asignada a la ruta 2026-0001.»
                    : AppliedMessage(match.OrderNumber, LabelOf(to.InternalCode) ?? to.InternalCode, steps.Count - 1);
                done = Result(action, ScanActionOutcomes.Applied, message, match, current,
                    to.InternalCode, steps.Take(steps.Count - 1).ToList(), route);
            }, ct);
        }
        return done!;
    }

    private ScanActionResultDto Result(string action, string outcome, string message, OrderListItemDto? match, StatusDto? from, string? toCode, IReadOnlyList<string>? skipped, OutboundScanResultDto? route)
    {
        var toLabel = toCode is null ? null : LabelOf(toCode);
        return new ScanActionResultDto(action, outcome, outcome == ScanActionOutcomes.Applied ? ScanVoices.Found : outcome is ScanActionOutcomes.NotFound or ScanActionOutcomes.Ambiguous or ScanActionOutcomes.NoRoute ? ScanVoices.NotFound : ScanVoices.Duplicate,
            message, match?.PublicId, match?.OrderNumber, match?.PackBatchNumber, from?.Code, from?.Label, toCode, toLabel, skipped ?? Array.Empty<string>(),
            route?.ZoneCode, route?.TripPublicId, route?.TripCode, route?.ReasonCode, matchedBy);
    }

    private string? matchedBy;   // por qué se encontró (número de orden, empaque o factura)

    private string? LabelOf(string statusCode) => labels.TryGetValue(statusCode, out var l) ? l : statusCode;
    private readonly Dictionary<string, string> labels = new(StringComparer.OrdinalIgnoreCase);

    public const string FinalMessageFormat = "La orden {0} está en un estatus final ({1}): no se cambia.";
    public static string FinalMessage(string orderNumber, string label) => string.Format(FinalMessageFormat, orderNumber, label);
    public static string BeyondMessage(string orderNumber, string label) => $"La orden {orderNumber} ya está en {label} o más adelante: no se cambia.";
    public static string AppliedMessage(string orderNumber, string toLabel, int skipped)
        => skipped > 0 ? $"La orden {orderNumber} pasó a {toLabel} (brincó {skipped} estatus)." : $"La orden {orderNumber} pasó a {toLabel}.";
}
