using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Constants;
using Teikem.Domain.Integrations;
using Teikem.Domain.Orders;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Security;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Cierre de una orden por documento (2026-10-23): lleva la orden a «Recogida» (PICKUP) o a «Entregada» (DELIVERED) por los mismos caminos que ya existen (el pipeline de estatus con sus saltos y, para la entrega, el comprobante
/// de la entrega). Devuelve false si la orden YA estaba ahí (no se hace nada); si no se puede, lanza la excepción de dominio con el motivo.
/// </summary>
public sealed class OrderCompletionApplier(TeikemDbContext db, StatusService statuses, DeliveryRegistrationService delivery)
{
    public static string StageNotEnabledMessage(string label) => $"El estatus «{label}» no está habilitado para esta compañía.";
    public static string CannotFromMessage(string order, string status, string target) => $"La orden {order} está en «{status}» y no se puede llevar a «{target}» desde ahí.";
    public static string CommentFor(string document) => $"Cierre por documento: {document}.";
    public const string UnknownTargetMessage = "El estatus de cierre debe ser PICKUP o DELIVERED.";

    public async Task<bool> ApplyAsync(int orderId, string toStatus, string document, CancellationToken ct)
    {
        var target = (toStatus ?? string.Empty).Trim().ToUpperInvariant();
        if (target == CompletionTargets.Pickup) return await PickupAsync(orderId, document, ct);
        if (target == CompletionTargets.Delivered) return await DeliverAsync(orderId, document, ct);
        throw new ValidationException("toStatus", UnknownTargetMessage);
    }

    private async Task<bool> PickupAsync(int orderId, string document, CancellationToken ct)
    {
        var changed = false;
        await db.RunInTransactionAsync(async ct2 =>
        {
            var order = await db.TransportOrders.FirstAsync(o => o.TransportOrderId == orderId, ct2);
            var pipeline = await statuses.GetPipelineAsync(StatusDomains.OrderStatus, includeDisabled: true, ct2);
            var current = pipeline.First(s => s.Id == order.StatusCodeId);
            var pickup = pipeline.FirstOrDefault(s => s.Code == OrderStatuses.Pickup && s.IsEnabled) ?? throw new StatusRuleException(StageNotEnabledMessage("Recogido"));
            if (!current.IsInitial)
            {
                // ya recogida o más adelante (en curso o final): no hay nada que hacer; un lateral (intento fallido) o un estatus raro no se toca
                if (!string.Equals(current.StageKind, StageKinds.Lateral, StringComparison.OrdinalIgnoreCase) && current.SortOrder >= pickup.SortOrder) return;
                throw new StatusRuleException(CannotFromMessage(order.OrderNumber, current.Label, pickup.Label));
            }
            var stages = pipeline.Select(s => new PipelineStage(s.Code, s.SortOrder, s.StageKind, s.IsInitial, s.IsEnabled)).ToList();
            var steps = PipelinePath.StepsTo(stages, current.Code, OrderStatuses.Pickup);
            if (steps.Count == 0 || steps[^1] != OrderStatuses.Pickup) throw new StatusRuleException(StageNotEnabledMessage("Recogido"));
            var to = await statuses.AdvanceSkippingAsync(StatusDomains.OrderStatus, EntityTypes.TransportOrder, orderId, order.StatusCodeId, steps, CommentFor(document), ct2);
            order.StatusCodeId = to.StatusCodeId;
            order.ConfirmedAtUtc ??= DateTime.UtcNow;
            await db.SaveGuardedAsync(DbExtensions.ConcurrencyMessage, ct2);
            changed = true;
        }, ct);
        return changed;
    }

    private async Task<bool> DeliverAsync(int orderId, string document, CancellationToken ct)
    {
        var order = await db.TransportOrders.AsNoTracking().Where(o => o.TransportOrderId == orderId).Select(o => new { o.PublicId, o.StatusCodeId }).FirstAsync(ct);
        var done = await db.StatusCodes.AsNoTracking().Where(s => s.StatusCodeId == order.StatusCodeId).Select(s => s.InternalCode).FirstAsync(ct);
        if (done == OrderStatuses.Delivered) return false;
        try
        {
            await delivery.RegisterAsync(order.PublicId, new DeliveryRegisterRequest(Truncate($"Según documento: {document}", DeliveryProofRules.MaxRecipientLength), null, null), ct,
                new DeliveryRegisterOptions(DeliveryProofCodes.SourceStaffWeb, DeliveryProofCodes.SignatureUser, CommentFor(document)));
            return true;
        }
        catch (ConflictException) { return false; }   // ya tenía su entrega registrada
    }

    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..max];
}

/// <summary>
/// Destino ORDER_COMPLETION (2026-10-23): del CSV del lector (columna 1 = la clave con que el documento nombra la orden, en CMS el Referral # = Order #; el resto de columnas es evidencia) deja UNA propuesta por orden.
/// Modo SUGGEST (por omisión): la propuesta espera a una persona. Modo AUTO: se aplica en el acto, a nombre de la cuenta de servicio. Una clave sin orden, o repetida con una propuesta viva, no crea otra.
/// </summary>
public sealed class OrderCompletionDocumentTarget(TeikemDbContext db, ITenantContext tenant, OrderCompletionApplier applier) : IDocumentTarget
{
    public string Kind => DocumentTargets.OrderCompletion;

    public static string OrderNotFoundMessage(string key, string client) => $"No hay una orden «{key}» del cliente {client}.";
    public static string AlreadyProposedMessage(string key) => $"La orden «{key}» ya tiene una propuesta de cierre viva o aplicada.";
    public static string SummaryFor(string order, string toStatus, string document) => $"Llevar la orden {order} a {toStatus} según {document}.";
    public const string NoKeysMessage = "El documento no trae ninguna orden que cerrar.";

    public async Task<FileOutcome> ProcessAsync(DocumentTargetRequest request, CancellationToken ct)
    {
        var sha = Convert.ToHexString(SHA256.HashData(request.Content)).ToLowerInvariant();
        var log = new IntegrationFileLog
        {
            TenantId = request.Integration.TenantId, IntegrationId = request.Integration.IntegrationId, IntegrationRunId = request.Run.IntegrationRunId,
            FileName = request.FileName.Length > 260 ? request.FileName[..260] : request.FileName, Sha256 = sha, SizeBytes = request.Content.Length,
        };
        if (request.CheckDuplicate && await db.IntegrationFileLogs.AsNoTracking().AnyAsync(f => f.IntegrationId == request.Integration.IntegrationId && f.Sha256 == sha && f.Result == IntegrationFileResults.Processed, ct))
        {
            log.Result = IntegrationFileResults.Duplicate;
            log.Message = IntegrationRunner.DuplicateFileMessage;
            return new FileOutcome(log, Array.Empty<Guid>(), Array.Empty<RejectedOrder>());
        }
        var mode = (request.Reader?.Mode ?? ReaderModes.Suggest).Trim().ToUpperInvariant();
        if (request.Doubtful) mode = ReaderModes.Suggest;   // lectura dudosa: aunque el lector sea automático, el cierre queda POR CONFIRMAR
        var toStatus = string.IsNullOrWhiteSpace(request.Reader?.ToStatus) ? CompletionTargets.Pickup : request.Reader!.ToStatus!.Trim().ToUpperInvariant();
        var csv = Teikem.Domain.Orders.CsvParser.Parse(IntegrationRunner.Decode(request.Content), ImportTemplateService.DelimiterOf(request.Template), request.Template.HasHeader);
        var keys = new List<(string Key, int Row, IReadOnlyList<string> Cells)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in csv)
        {
            var key = r.Cells.Count > 0 ? r.Cells[0].Trim() : string.Empty;
            if (key.Length > 0 && seen.Add(key)) keys.Add((key, r.RowNumber, r.Cells));
        }
        var created = new List<Guid>();
        var rejected = new List<RejectedOrder>();
        var applied = 0;
        var skipped = 0;
        if (keys.Count == 0) rejected.Add(new RejectedOrder(null, null, NoKeysMessage, null));
        using var asService = ((TenantContext)tenant).As(request.Integration.TenantId, request.Integration.ServiceUserId);
        foreach (var (key, row, cells) in keys)
        {
            ct.ThrowIfCancellationRequested();
            var order = await db.TransportOrders.AsNoTracking()
                .Where(o => o.ClientId == request.Client.ClientId && o.IsActive && (o.OrderNumber == key || o.WaybillNumber == key))
                .Select(o => new { o.TransportOrderId, o.OrderNumber }).FirstOrDefaultAsync(ct);
            if (order is null) { rejected.Add(new RejectedOrder(key, row, OrderNotFoundMessage(key, request.Client.Name), null)); continue; }
            if (await db.IntegrationProposals.AsNoTracking().AnyAsync(p => p.TransportOrderId == order.TransportOrderId && p.ToStatus == toStatus
                    && (p.Status == ProposalStatuses.Pending || p.Status == ProposalStatuses.Applied || p.Status == ProposalStatuses.Confirmed), ct))
            { skipped++; continue; }

            var proposal = new IntegrationProposal
            {
                PublicId = Guid.NewGuid(), TenantId = request.Integration.TenantId, IntegrationId = request.Integration.IntegrationId, IntegrationRunId = request.Run.IntegrationRunId,
                Kind = ProposalKinds.CompleteOrder, TransportOrderId = order.TransportOrderId, ReferenceKey = key, ToStatus = toStatus, Status = ProposalStatuses.Pending,
                Summary = SummaryFor(order.OrderNumber, toStatus, request.FileName),
                EvidenceJson = JsonSerializer.Serialize(new { document = request.FileName, reader = request.Reader?.Name, mode, row, cells }),
            };
            db.IntegrationProposals.Add(proposal);
            await db.SaveChangesAsync(ct);
            created.Add(proposal.PublicId);
            if (mode != ReaderModes.Auto) continue;

            try
            {
                var changed = await applier.ApplyAsync(order.TransportOrderId, toStatus, request.FileName, ct);
                proposal = await db.IntegrationProposals.FirstAsync(p => p.PublicId == proposal.PublicId, ct);   // la transacción del cierre suelta lo que se seguía en memoria
                proposal.Status = ProposalStatuses.Applied;
                proposal.Message = changed ? null : "La orden ya estaba en ese estatus.";
                applied++;
            }
            catch (TeikemException ex)
            {
                db.ChangeTracker.Clear();
                proposal = await db.IntegrationProposals.FirstAsync(p => p.PublicId == proposal.PublicId, ct);
                proposal.Status = ProposalStatuses.Failed;
                proposal.Message = ex.Message.Length > 1000 ? ex.Message[..1000] : ex.Message;
                rejected.Add(new RejectedOrder(key, row, $"Orden {order.OrderNumber}: {ex.Message}", null));
            }
            proposal.DecidedAtUtc = DateTime.UtcNow;
            proposal.DecidedBy = request.Integration.ServiceUserId;
            await db.SaveChangesAsync(ct);
        }
        log.Result = request.CheckDuplicate ? IntegrationFileResults.Processed : IntegrationFileResults.Reprocessed;
        log.OrdersCreated = created.Count;
        log.Rejected = rejected.Count;
        log.Message = request.Doubtful ? $"{created.Count} propuesta(s) por confirmar (lectura dudosa: no se aplican solas), {skipped} ya estaban propuestas, {rejected.Count} a la bandeja."
            : mode == ReaderModes.Auto
            ? $"{applied} orden(es) cerrada(s), {skipped} ya estaban propuestas, {rejected.Count} a la bandeja."
            : $"{created.Count} propuesta(s) por confirmar, {skipped} ya estaban propuestas, {rejected.Count} a la bandeja.";
        return new FileOutcome(log, created, rejected);
    }
}

/// <summary>Las propuestas de cierre: listarlas, confirmarlas (la orden cambia en ese momento, por el servicio de estatus, a nombre de quien confirma) o rechazarlas.</summary>
public sealed class IntegrationProposalService(TeikemDbContext db, ITenantContext tenant, OrderCompletionApplier applier)
{
    public const string NotFoundLabel = "Propuesta";
    public const string NotPendingMessage = "La propuesta ya se resolvió.";
    public const int MaxPage = 200;

    private TenantContext Ctx => (TenantContext)tenant;

    public async Task<ProposalPageDto> ListAsync(string? status, int skip, int take, CancellationToken ct)
    {
        var q = db.IntegrationProposals.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(status)) { var s = status.Trim().ToUpperInvariant(); q = q.Where(p => p.Status == s); }
        var total = await q.CountAsync(ct);
        var pending = await db.IntegrationProposals.AsNoTracking().CountAsync(p => p.Status == ProposalStatuses.Pending, ct);
        var page = await q.OrderByDescending(p => p.IntegrationProposalId).Skip(Math.Max(0, skip)).Take(Math.Clamp(take, 1, MaxPage)).ToListAsync(ct);
        return new ProposalPageDto(await ToDtosAsync(page, ct), total, pending);
    }

    public async Task<int> PendingCountAsync(CancellationToken ct) => await db.IntegrationProposals.AsNoTracking().CountAsync(p => p.Status == ProposalStatuses.Pending, ct);

    public async Task<ProposalDto> ConfirmAsync(Guid publicId, ProposalDecisionRequest? req, CancellationToken ct)
    {
        var proposal = await db.IntegrationProposals.FirstOrDefaultAsync(p => p.PublicId == publicId, ct) ?? throw new NotFoundException(NotFoundLabel);
        if (proposal.Status != ProposalStatuses.Pending) throw new ConflictException(NotPendingMessage);
        var document = ReadDocument(proposal.EvidenceJson) ?? proposal.ReferenceKey;
        string? message = null;
        var status = ProposalStatuses.Confirmed;
        try
        {
            if (!await applier.ApplyAsync(proposal.TransportOrderId, proposal.ToStatus, document, ct)) message = "La orden ya estaba en ese estatus.";
            proposal = await db.IntegrationProposals.FirstAsync(p => p.PublicId == publicId, ct);   // la transacción del cierre suelta lo que se seguía en memoria
        }
        catch (TeikemException ex)
        {
            // la orden no lo permitió: la propuesta queda FALLIDA con el motivo (ya no se puede confirmar otra vez) y la persona lo ve en la lista
            db.ChangeTracker.Clear();
            proposal = await db.IntegrationProposals.FirstAsync(p => p.PublicId == publicId, ct);
            status = ProposalStatuses.Failed;
            message = ex.Message.Length > 1000 ? ex.Message[..1000] : ex.Message;
        }
        proposal.Status = status;
        proposal.Message = message;
        proposal.DecidedAtUtc = DateTime.UtcNow;
        proposal.DecidedBy = Ctx.UserId;
        proposal.Note = Clean(req?.Note);
        await db.SaveChangesAsync(ct);
        return (await ToDtosAsync(new[] { proposal }, ct))[0];
    }

    public async Task<ProposalDto> RejectAsync(Guid publicId, ProposalDecisionRequest? req, CancellationToken ct)
    {
        var proposal = await db.IntegrationProposals.FirstOrDefaultAsync(p => p.PublicId == publicId, ct) ?? throw new NotFoundException(NotFoundLabel);
        if (proposal.Status != ProposalStatuses.Pending) throw new ConflictException(NotPendingMessage);
        proposal.Status = ProposalStatuses.Rejected;
        proposal.DecidedAtUtc = DateTime.UtcNow;
        proposal.DecidedBy = Ctx.UserId;
        proposal.Note = Clean(req?.Note);
        await db.SaveChangesAsync(ct);
        return (await ToDtosAsync(new[] { proposal }, ct))[0];
    }

    private static string? Clean(string? note) => string.IsNullOrWhiteSpace(note) ? null : (note.Trim().Length > 400 ? note.Trim()[..400] : note.Trim());

    private static string? ReadDocument(string? evidence)
    {
        if (string.IsNullOrWhiteSpace(evidence)) return null;
        try { return JsonDocument.Parse(evidence).RootElement.TryGetProperty("document", out var d) ? d.GetString() : null; }
        catch (JsonException) { return null; }
    }

    private async Task<IReadOnlyList<ProposalDto>> ToDtosAsync(IReadOnlyList<IntegrationProposal> rows, CancellationToken ct)
    {
        var orderIds = rows.Select(r => r.TransportOrderId).Distinct().ToList();
        var orders = await db.TransportOrders.AsNoTracking().Where(o => orderIds.Contains(o.TransportOrderId)).Select(o => new { o.TransportOrderId, o.PublicId, o.OrderNumber }).ToDictionaryAsync(o => o.TransportOrderId, ct);
        var integrationIds = rows.Select(r => r.IntegrationId).Distinct().ToList();
        var integrations = await db.Integrations.AsNoTracking().Where(i => integrationIds.Contains(i.IntegrationId)).ToDictionaryAsync(i => i.IntegrationId, i => i.Name, ct);
        var userIds = rows.Where(r => r.DecidedBy != null).Select(r => r.DecidedBy!.Value).Distinct().ToList();
        var users = userIds.Count == 0 ? new Dictionary<int, string>() : await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName ?? u.Email ?? string.Empty, ct);
        return rows.Select(r => new ProposalDto(r.PublicId, r.Kind, r.ReferenceKey, orders.TryGetValue(r.TransportOrderId, out var o) ? o.OrderNumber : r.ReferenceKey,
            orders.TryGetValue(r.TransportOrderId, out var o2) ? o2.PublicId : Guid.Empty, r.ToStatus, r.Status, r.Summary, ReadDocument(r.EvidenceJson), integrations.GetValueOrDefault(r.IntegrationId) ?? string.Empty,
            r.Message, r.CreatedAtUtc, r.DecidedAtUtc, r.DecidedBy is int u && users.TryGetValue(u, out var n) ? n : null, r.Note)).ToList();
    }
}
