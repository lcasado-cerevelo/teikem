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
/// La estación de escaneo con acción escogida (ver <see cref="ScanActions"/>), Lotes 107 y 110. Lo que se escanea son las PIEZAS (su número único): el número de la orden no se escanea («no se encontró la pieza»). El usuario escoge la acción
/// y cada escaneo la aplica a la pieza y a su orden:
/// - La pieza se registra con esa acción (una vez por pieza y acción: repetirla es un DUPLICADO, sin cambios). El primer escaneo de una acción mueve la orden a ese estatus de una vez, brincando los de en medio (una fila de historial por estatus,
///   todas con la misma fecha y hora y las de en medio «brincadas», sin avisos al cliente); los siguientes de la misma acción solo cuentan («4 de 5»).
/// - Estatus final → se ignora (FINAL). Desde Intento fallido, «Llegó» la devuelve a Sin asignar y las demás acciones pasan por Sin asignar (brincado).
/// - Enrutar además pone la orden en la ruta abierta de su zona (el escaneo Outbound de siempre); si no hay ruta que la reciba no cambia nada (NO_ROUTE).
/// - Faltantes: cuando la orden pasa a la acción siguiente sin alguna pieza (o al cerrar el conteo), esa pieza queda «Por verificar». Si aparece (se escanea después) deja de ser faltante.
/// Nada de esto revisa tarifa, crédito ni cliente: eso se revisó al someter la orden. Silencioso: nunca pregunta ni interrumpe; cada escaneo contesta con su resultado y su palabra (Sí / No / Ok).
/// </summary>
public sealed class ScanActionService(TeikemDbContext db, ITenantContext tenant, StatusService statuses, OutboundScanService outbound, ReappearedOrderService reappeared, ReturnOrderService returns, PermissionService? permissions = null)
{
    private readonly Dictionary<string, string> labels = new(StringComparer.OrdinalIgnoreCase);

    private sealed record OrderFacts(Guid PublicId, string OrderNumber, string? PackBatchNumber, string? WaybillNumber);

    public async Task<ScanActionResultDto> ApplyAsync(ScanActionRequest? req, CancellationToken ct)
    {
        if (tenant.TenantId is null) throw new ForbiddenException("No hay tenant activo en la sesión.");
        var action = ScanActions.Normalize(req?.Action) ?? throw new ValidationException("action", ScanActions.InvalidActionMessage);
        var (code, error) = OutboundScanRules.NormalizeCode(req?.Code);
        if (error is not null) throw new ValidationException("code", error);

        // Lo que se escanea es una pieza: el número de la orden (o de empaque o factura) no se encuentra.
        var piece = await db.OrderPieces.AsNoTracking().Where(p => p.IsActive && p.PackageNumber == code!).Select(p => new { p.OrderPieceId, p.TransportOrderId, p.PackageNumber }).FirstOrDefaultAsync(ct);
        if (piece is null) return Plain(action, ScanActionOutcomes.NotFound, ScanActions.PieceNotFoundMessage);
        var orderFacts = await db.TransportOrders.AsNoTracking().Where(o => o.TransportOrderId == piece.TransportOrderId && o.IsActive)
            .Select(o => new OrderFacts(o.PublicId, o.OrderNumber, o.PackBatchNumber, o.WaybillNumber)).FirstOrDefaultAsync(ct);
        if (orderFacts is null) return Plain(action, ScanActionOutcomes.NotFound, ScanActions.PieceNotFoundMessage);

        var targetCode = ScanActions.TargetStatus(action);
        var pipeline = await statuses.GetPipelineAsync(StatusDomains.OrderStatus, includeDisabled: true, ct);
        foreach (var s in pipeline) labels[s.Code] = s.Label;
        var stages = pipeline.Select(s => new PipelineStage(s.Code, s.SortOrder, s.StageKind, s.IsInitial, s.IsEnabled)).ToList();
        var routeCode = string.IsNullOrWhiteSpace(orderFacts.WaybillNumber) ? orderFacts.OrderNumber : orderFacts.WaybillNumber;   // el conduce es único; el número de orden puede repetirse entre clientes

        ScanActionResultDto? done = null;
        await db.RunInTransactionAsync(async ct2 =>
        {
            var order = await db.ResolveOrderForWriteAsync(orderFacts.PublicId, OrderScope.Any, ct2);
            var current = pipeline.First(s => s.Id == order.StatusCodeId);
            var orderId = order.TransportOrderId;

            // Lote 111: una pieza de una reclamación cerrada no se acepta; una perdida o reclamada (o «Por verificar» de una orden ya cerrada) que aparece se quita de su reclamación y espera la orden nueva
            var pieceRow = await db.OrderPieces.FirstAsync(p => p.OrderPieceId == piece.OrderPieceId, ct2);
            var orderIsFinal = string.Equals(current.StageKind, StageKinds.Terminal, StringComparison.OrdinalIgnoreCase);
            if (pieceRow.MissingStatus == PieceMissing.Resolved)
            {
                done = Plain(action, ScanActionOutcomes.NotFound, ClaimRules.ClosedClaimPieceMessage);
                return;
            }
            // Lote 112: «Devolver» (orders.return): la pieza viaja en la orden de vuelta y la orden queda «Devuelta»; no sigue el camino de las demás acciones
            if (action == ScanActions.Return)
            {
                if (permissions is not null) await permissions.EnsureAsync(Teikem.Domain.Constants.PermissionCatalog.OrdersReturn, ct2);
                if (pieceRow.MissingStatus is not null)
                {
                    done = Plain(action, ScanActionOutcomes.NotFound, string.Format(ReturnOrderService.PieceMissingMessage, piece.PackageNumber));
                    return;
                }
                if (await db.OrderPieceScans.AnyAsync(s => s.OrderPieceId == pieceRow.OrderPieceId && s.Action == action, ct2))
                {
                    done = await ResultAsync(action, ScanActionOutcomes.Duplicate, DuplicateMessage(piece.PackageNumber), orderFacts, current, null, null, null, piece.PackageNumber, orderId, ct2);
                    return;
                }
                if (orderIsFinal && current.Code != OrderStatuses.Returned)
                {
                    done = await ResultAsync(action, ScanActionOutcomes.Final, FinalMessage(orderFacts.OrderNumber, current.Label), orderFacts, current, null, null, null, piece.PackageNumber, orderId, ct2);
                    return;
                }
                var (returnOrder, createdReturn) = await returns.ReturnScannedPieceAsync(order, pieceRow, ct2);
                db.OrderPieceScans.Add(new OrderPieceScan { TenantId = order.TenantId, OrderPieceId = pieceRow.OrderPieceId, Action = action, ScannedAtUtc = DateTime.UtcNow, ScannedBy = tenant.UserId });
                await db.SaveChangesAsync(ct2);
                var text = createdReturn
                    ? string.Format(ReturnOrderService.CreatedMessageFormat, orderFacts.OrderNumber, returnOrder.OrderNumber, returnOrder.WaybillNumber, 1)
                    : string.Format(ReturnOrderService.AddedMessageFormat, piece.PackageNumber, returnOrder.OrderNumber);
                done = await ResultAsync(action, ScanActionOutcomes.Returned, text, orderFacts, current, createdReturn ? OrderStatuses.Returned : null, null, null, piece.PackageNumber, orderId, ct2);
                return;
            }

            if (pieceRow.MissingStatus is PieceMissing.Lost or PieceMissing.Claimed || pieceRow.ReappearedAtUtc is not null || (pieceRow.MissingStatus == PieceMissing.ToVerify && orderIsFinal))
            {
                if (await db.OrderPieceScans.AnyAsync(s => s.OrderPieceId == pieceRow.OrderPieceId && s.Action == action, ct2))
                {
                    done = await ResultAsync(action, ScanActionOutcomes.Duplicate, DuplicateMessage(piece.PackageNumber), orderFacts, current, null, null, null, piece.PackageNumber, orderId, ct2);
                    return;
                }
                string? claimNumber = null;
                if (pieceRow.ClaimId is int claimId)
                {
                    var claim = await db.Claims.FirstAsync(c => c.ClaimId == claimId, ct2);
                    claimNumber = claim.Number;
                    pieceRow.ClaimId = null;
                    await db.SaveChangesAsync(ct2);
                    if (!await db.OrderPieces.AnyAsync(p => p.ClaimId == claimId, ct2)) claim.IsActive = false;   // una reclamación sin cerrar que se queda sin piezas se da de baja
                }
                pieceRow.MissingStatus = null;
                pieceRow.MissingAtUtc = null;
                pieceRow.ReappearedAtUtc ??= DateTime.UtcNow;
                if (pieceRow.ReappearedAction is null || ScanActions.Rank(action) > ScanActions.Rank(pieceRow.ReappearedAction)) pieceRow.ReappearedAction = action;
                db.OrderPieceScans.Add(new OrderPieceScan { TenantId = order.TenantId, OrderPieceId = pieceRow.OrderPieceId, Action = action, ScannedAtUtc = DateTime.UtcNow, ScannedBy = tenant.UserId });
                await db.SaveChangesAsync(ct2);
                done = await ResultAsync(action, ScanActionOutcomes.Reappeared, ReappearedMessage(piece.PackageNumber, claimNumber), orderFacts, current, null, null, null, piece.PackageNumber, orderId, ct2);
                return;
            }

            if (orderIsFinal)
            {
                done = await ResultAsync(action, ScanActionOutcomes.Final, FinalMessage(orderFacts.OrderNumber, current.Label), orderFacts, current, null, null, null, piece.PackageNumber, orderId, ct2);
                return;
            }

            // la pieza ya se escaneó con esta acción: duplicado, sin cambios
            if (await db.OrderPieceScans.AnyAsync(s => s.OrderPieceId == piece.OrderPieceId && s.Action == action, ct2))
            {
                done = await ResultAsync(action, ScanActionOutcomes.Duplicate, DuplicateMessage(piece.PackageNumber), orderFacts, current, null, null, null, piece.PackageNumber, orderId, ct2);
                return;
            }

            // Pasos desde el estatus actual: desde un lateral (Intento fallido) se cuenta desde «Recogida» (se regresa al almacén).
            var fromCode = string.Equals(current.StageKind, StageKinds.Lateral, StringComparison.OrdinalIgnoreCase) ? OrderStatuses.Pickup : current.Code;
            var steps = PipelinePath.StepsTo(stages, fromCode, targetCode);
            OutboundScanResultDto? route = null;
            string? toCode = null;
            IReadOnlyList<string>? skipped = null;
            var message = CountedMessage(piece.PackageNumber, LabelOf(targetCode));

            if (steps.Count == 0)
            {
                // La orden ya está en ese estatus o más adelante: la pieza solo se cuenta. Con Enrutar, si ya está en una ruta se dice cuál.
                if (action == ScanActions.Route)
                {
                    var known = await outbound.ScanAsync(new OutboundScanRequest(routeCode, req?.PlanDate), ct2);
                    if (known.Outcome is ScanOutcomes.AlreadyAssigned or ScanOutcomes.FoundAssigned) { route = known; message = known.Message; }
                }
            }
            else
            {
                // La orden pasa a la acción siguiente: las piezas que no estuvieron en la acción en que iba quedan faltantes «Por verificar»
                var markedBefore = await MarkMissingAsync(orderId, piece.OrderPieceId, current.Code, ct2);

                var entityTypeId = await db.LookupCodes.AsNoTracking().Where(l => l.Entity == LookupDomains.EntityType && l.InternalCode == EntityTypes.TransportOrder).Select(l => l.LookupCodeId).FirstAsync(ct2);
                var historyFloor = await db.EntityStatusHistories.AsNoTracking().Where(h => h.EntityTypeLookupId == entityTypeId && h.EntityId == orderId).MaxAsync(h => (long?)h.EntityStatusHistoryId, ct2) ?? 0L;
                var confirmedBefore = order.ConfirmedAtUtc;
                var to = await statuses.AdvanceSkippingAsync(StatusDomains.OrderStatus, EntityTypes.TransportOrder, orderId, order.StatusCodeId, steps, null, ct2);
                order.StatusCodeId = to.StatusCodeId;
                if (steps.Contains(OrderStatuses.Pickup) && order.ConfirmedAtUtc is null) order.ConfirmedAtUtc = DateTime.UtcNow;
                await db.SaveGuardedAsync(DbExtensions.ConcurrencyMessage, ct2);

                if (action == ScanActions.Route)
                {
                    // Ya está Asignada: ahora a una ruta abierta de su zona. Si no hay quién la reciba se deshace todo (no queda Asignada sin ruta ni piezas marcadas por este escaneo).
                    route = await outbound.ScanAsync(new OutboundScanRequest(routeCode, req?.PlanDate), ct2);
                    if (route.Outcome is not (ScanOutcomes.FoundAssigned or ScanOutcomes.AlreadyAssigned))
                    {
                        db.ChangeTracker.Clear();
                        var undo = await db.EntityStatusHistories.Where(h => h.EntityTypeLookupId == entityTypeId && h.EntityId == orderId && h.EntityStatusHistoryId > historyFloor).ToListAsync(ct2);
                        db.EntityStatusHistories.RemoveRange(undo);
                        var back = await db.TransportOrders.FirstAsync(o => o.TransportOrderId == orderId, ct2);
                        back.StatusCodeId = current.Id;
                        back.ConfirmedAtUtc = confirmedBefore;
                        if (markedBefore.Count > 0)
                            foreach (var p in await db.OrderPieces.Where(p => markedBefore.Contains(p.OrderPieceId)).ToListAsync(ct2)) { p.MissingStatus = null; p.MissingAtUtc = null; }
                        await db.SaveChangesAsync(ct2);
                        done = await ResultAsync(action, ScanActionOutcomes.NoRoute, route.Message, orderFacts, current, null, null, route, piece.PackageNumber, orderId, ct2);
                        return;
                    }
                }

                toCode = to.InternalCode;
                skipped = steps.Take(steps.Count - 1).ToList();
                message = route is not null
                    ? route.Message + (steps.Count > 1 ? $" (brincó {steps.Count - 1} estatus)" : string.Empty)   // «Asignada a la ruta 2026-0001.»
                    : AppliedMessage(orderFacts.OrderNumber, LabelOf(to.InternalCode), steps.Count - 1);
            }

            // la pieza queda escaneada con esta acción; si estaba como faltante, apareció
            db.OrderPieceScans.Add(new OrderPieceScan { TenantId = order.TenantId, OrderPieceId = piece.OrderPieceId, Action = action, ScannedAtUtc = DateTime.UtcNow, ScannedBy = tenant.UserId });
            var tracked = await db.OrderPieces.FirstAsync(p => p.OrderPieceId == piece.OrderPieceId, ct2);
            if (tracked.MissingStatus == PieceMissing.ToVerify) { tracked.MissingStatus = null; tracked.MissingAtUtc = null; }
            await db.SaveChangesAsync(ct2);

            done = await ResultAsync(action, steps.Count > 0 ? ScanActionOutcomes.Applied : ScanActionOutcomes.Counted, message, orderFacts, current, toCode, skipped, route, piece.PackageNumber, orderId, ct2);
        }, ct);
        return done!;
    }

    // ================================================================ faltantes y cierre del conteo

    /// <summary>
    /// Las piezas de la orden que no se escanearon en la acción en que iba la orden (ni en una posterior) quedan «Por verificar». Solo si en esa acción se escaneó alguna pieza (si la orden brincó esa acción no hay conteo que cerrar).
    /// <paramref name="exceptPieceId"/> es la pieza que se está escaneando ahora (0 = ninguna). Devuelve los ids de las piezas marcadas.
    /// </summary>
    private async Task<List<int>> MarkMissingAsync(int orderId, int exceptPieceId, string currentStatusCode, CancellationToken ct, bool dryRun = false)
    {
        var marked = new List<int>();
        if (currentStatusCode == OrderStatuses.Returned)
        {
            // Lote 112: una orden «Devuelta» ya mandó a la vuelta lo que se escaneó con «Devolver»; lo que sigue en ella y no es faltante queda «Por verificar» (de ahí sigue en Reclamaciones)
            var left = await db.OrderPieces.Where(p => p.TransportOrderId == orderId && p.IsActive && p.MissingStatus == null && p.ReappearedAtUtc == null).ToListAsync(ct);
            foreach (var p in left)
            {
                marked.Add(p.OrderPieceId);
                if (dryRun) continue;
                p.MissingStatus = PieceMissing.ToVerify;
                p.MissingAtUtc = DateTime.UtcNow;
            }
            if (marked.Count > 0 && !dryRun) await db.SaveChangesAsync(ct);
            return marked;
        }
        var currentAction = ScanActions.ForStatus(currentStatusCode);
        if (currentAction is null) return marked;
        var pieces = await db.OrderPieces.Where(p => p.TransportOrderId == orderId && p.IsActive).ToListAsync(ct);
        var ids = pieces.Select(p => p.OrderPieceId).ToList();
        var scans = await db.OrderPieceScans.AsNoTracking().Where(s => ids.Contains(s.OrderPieceId)).Select(s => new { s.OrderPieceId, s.Action }).ToListAsync(ct);
        if (!scans.Any(s => s.Action == currentAction)) return marked;
        var rank = ScanActions.Rank(currentAction);
        var now = DateTime.UtcNow;
        foreach (var p in pieces.Where(p => p.OrderPieceId != exceptPieceId && p.MissingStatus is null))
        {
            if (scans.Any(s => s.OrderPieceId == p.OrderPieceId && ScanActions.Rank(s.Action) >= rank)) continue;
            marked.Add(p.OrderPieceId);
            if (dryRun) continue;   // la vista previa solo dice cuáles quedarían «Por verificar»
            p.MissingStatus = PieceMissing.ToVerify;
            p.MissingAtUtc = now;
        }
        if (marked.Count > 0 && !dryRun) await db.SaveChangesAsync(ct);
        return marked;
    }

    /// <summary>«Cerrar conteo» de una orden: las piezas que no se escanearon en la acción en que va quedan «Por verificar» (la orden no cambia de estatus).</summary>
    public async Task<ScanCloseCountResultDto> CloseCountAsync(ScanCloseCountRequest? req, CancellationToken ct)
    {
        if (tenant.TenantId is null) throw new ForbiddenException("No hay tenant activo en la sesión.");
        if (req?.OrderPublicId is not Guid publicId) throw new ValidationException("orderPublicId", "Indique la orden cuyo conteo se cierra.");
        var order = await db.TransportOrders.AsNoTracking().Include(o => o.Status).Where(o => o.PublicId == publicId && o.IsActive).FirstOrDefaultAsync(ct) ?? throw new NotFoundException("Orden", feminine: true);
        // Lote 111b: antes de cerrar se le dice a quien escanea qué va a pasar: las piezas que no se escanearon (quedarán «Por verificar») y la orden nueva de las que aparecieron. Sin confirmar no se hace nada.
        var preview = await reappeared.PreviewAsync(order.TransportOrderId, ct);
        var wouldMark = await MarkMissingAsync(order.TransportOrderId, 0, order.Status!.InternalCode, ct, dryRun: true);
        if (preview is not null || wouldMark.Count > 0)
        {
            var notScanned = await db.OrderPieces.AsNoTracking().Where(p => p.TransportOrderId == order.TransportOrderId && p.IsActive && (wouldMark.Contains(p.OrderPieceId) || p.MissingStatus == PieceMissing.ToVerify))
                .OrderBy(p => p.Sequence).Select(p => p.PackageNumber).ToListAsync(ct);
            if (req.Confirm != true)
            {
                var clientName = preview?.ClientName ?? await db.Clients.AsNoTracking().Where(c => c.ClientId == order.ClientId).Select(c => c.Name).FirstOrDefaultAsync(ct) ?? string.Empty;
                var text = $"Va a cerrar el conteo de la orden {order.OrderNumber}.";
                if (notScanned.Count > 0) text += $" No se escanearon {notScanned.Count} pieza(s) y quedarán «Por verificar»: {string.Join(", ", notScanned)}.";
                if (preview is not null) text += $" Aparecieron {preview.Numbers.Count} pieza(s) y se creará una orden nueva con ellas (mismo cliente y consignatario, estatus «{preview.StatusLabel}»).";
                return new ScanCloseCountResultDto(text, order.PublicId, order.OrderNumber, 0, 0, notScanned.Count, notScanned, true,
                    new ScanClosePreviewDto(order.OrderNumber, clientName, preview?.ConsigneeName, preview?.StatusLabel, preview?.Numbers ?? Array.Empty<string>(), notScanned));
            }
        }
        if (preview is not null) await reappeared.EnsureSequencesAsync(order.TransportOrderId, ct);
        ScanCloseCountResultDto? done = null;
        await db.RunInTransactionAsync(async ct2 =>
        {
            var created = preview is null ? null : await reappeared.CreateAsync(order.TransportOrderId, req.PlanDate, ct2);   // primero se mudan las piezas que aparecieron: ya no cuentan como faltantes de esta orden
            await MarkMissingAsync(order.TransportOrderId, 0, order.Status!.InternalCode, ct2);
            var pieces = await db.OrderPieces.AsNoTracking().Where(p => p.TransportOrderId == order.TransportOrderId && p.IsActive).OrderBy(p => p.Sequence).ToListAsync(ct2);
            var missing = pieces.Where(p => p.MissingStatus == PieceMissing.ToVerify).Select(p => p.PackageNumber).ToList();
            var action = ScanActions.ForStatus(order.Status.InternalCode);
            var ids = pieces.Select(p => p.OrderPieceId).ToList();
            var scanned = action is null ? 0 : await db.OrderPieceScans.AsNoTracking().CountAsync(s => ids.Contains(s.OrderPieceId) && s.Action == action, ct2);
            var message = missing.Count == 0 ? $"Conteo cerrado: la orden {order.OrderNumber} está completa." : $"Conteo cerrado: a la orden {order.OrderNumber} le faltan {missing.Count} de {pieces.Count} piezas («Por verificar»).";
            if (created is not null) message += " " + string.Format(ReappearedOrderService.CreatedMessageFormat, created.OrderNumber, created.WaybillNumber, created.Pieces, created.OriginalOrderNumber);
            done = new ScanCloseCountResultDto(message, order.PublicId, order.OrderNumber, scanned, pieces.Count, missing.Count, missing, false, null, created?.PublicId, created?.OrderNumber, created?.WaybillNumber);
        }, ct);
        return done!;
    }

    // ================================================================ resultado

    private ScanActionResultDto Plain(string action, string outcome, string message)
        => new(action, outcome, VoiceOf(outcome), message, null, null, null, null, null, null, null, Array.Empty<string>(), null, null, null, null);

    private static string VoiceOf(string outcome) => outcome switch
    {
        ScanActionOutcomes.Applied or ScanActionOutcomes.Counted or ScanActionOutcomes.Reappeared or ScanActionOutcomes.Returned => ScanVoices.Found,                 // «Sí»
        ScanActionOutcomes.Duplicate => ScanVoices.Duplicate,                                          // «Ok»
        _ => ScanVoices.NotFound,                                                                      // «No»
    };

    private async Task<ScanActionResultDto> ResultAsync(string action, string outcome, string message, OrderFacts order, StatusDto? from, string? toCode, IReadOnlyList<string>? skipped, OutboundScanResultDto? route, string pieceNumber, int orderId, CancellationToken ct)
    {
        var pieces = await db.OrderPieces.AsNoTracking().Where(p => p.TransportOrderId == orderId && p.IsActive).Select(p => new { p.OrderPieceId, p.MissingStatus }).ToListAsync(ct);
        var ids = pieces.Select(p => p.OrderPieceId).ToList();
        var scanned = await db.OrderPieceScans.AsNoTracking().CountAsync(s => ids.Contains(s.OrderPieceId) && s.Action == action, ct);
        var missing = pieces.Count(p => p.MissingStatus == PieceMissing.ToVerify);
        return new ScanActionResultDto(action, outcome, VoiceOf(outcome), message, order.PublicId, order.OrderNumber, order.PackBatchNumber, from?.Code, from?.Label, toCode, toCode is null ? null : LabelOf(toCode),
            skipped ?? Array.Empty<string>(), route?.ZoneCode, route?.TripPublicId, route?.TripCode, route?.ReasonCode, "PIECE", scanned, pieces.Count, missing, pieceNumber);
    }

    private string LabelOf(string statusCode) => labels.TryGetValue(statusCode, out var l) ? l : statusCode;

    public const string FinalMessageFormat = "La orden {0} está en un estatus final ({1}): no se cambia.";
    public static string FinalMessage(string orderNumber, string label) => string.Format(FinalMessageFormat, orderNumber, label);
    public static string ReappearedMessage(string pieceNumber, string? claimNumber)
        => claimNumber is null ? $"La pieza {pieceNumber} apareció: su orden nueva se crea al cerrar el conteo." : $"La pieza {pieceNumber} apareció: se quitó de la reclamación {claimNumber} y su orden nueva se crea al cerrar el conteo.";
    public static string DuplicateMessage(string pieceNumber) => $"La pieza {pieceNumber} ya se había escaneado con esta acción.";
    public static string CountedMessage(string pieceNumber, string statusLabel) => $"La pieza {pieceNumber} se contó ({statusLabel}).";
    public static string AppliedMessage(string orderNumber, string toLabel, int skipped)
        => skipped > 0 ? $"La orden {orderNumber} pasó a {toLabel} (brincó {skipped} estatus)." : $"La orden {orderNumber} pasó a {toLabel}.";
}
