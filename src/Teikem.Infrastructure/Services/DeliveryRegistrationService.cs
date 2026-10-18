using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Teikem.Domain.Constants;
using Teikem.Domain.Orders;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Geocoding;
using Teikem.Infrastructure.Orders;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Trips;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 118 — registrar la entrega de una orden desde la web (dictado de Luis, 2026-10-18), sin depender de la app del chofer. Quien tiene orders.deliver la registra: la orden pasa a «Entregada» (brincando los estatus
/// que falten, registrados «brincados» como el escaneo; el efecto de los intentos anota el intento entregado) y queda el COMPROBANTE (<see cref="DeliveryProof"/>): quién recibió, cuándo, y la FIRMA AUTOMÁTICA del usuario
/// que lo registra (nunca del consignatario: el comprobante lo dice). La ubicación se busca aparte y sin hacer esperar (Google, un solo intento, 30 s): aquí solo se deja en cola; si no se logra, queda sin ubicación.
/// No hay fotos todavía (falta decidir dónde se guardan los archivos). Una entrega por orden: registrarla otra vez es 409.
/// </summary>
public sealed class DeliveryRegistrationService(TeikemDbContext db, ITenantContext tenant, StatusService statuses, IOptions<GoogleMapsOptions>? maps = null, ITenantClock? clock = null)
{
    private readonly ITenantClock _clock = clock ?? TenantClock.Default;
    private TenantContext Ctx => (TenantContext)tenant;

    public async Task<DeliveryProofDto> RegisterAsync(Guid orderPublicId, DeliveryRegisterRequest? req, CancellationToken ct)
    {
        var recipient = req?.RecipientName?.Trim();
        if (string.IsNullOrEmpty(recipient)) throw new ValidationException("recipientName", DeliveryProofRules.RecipientRequired);
        if (recipient.Length > DeliveryProofRules.MaxRecipientLength) throw new ValidationException("recipientName", DeliveryProofRules.RecipientTooLong);
        var notes = string.IsNullOrWhiteSpace(req?.Notes) ? null : req!.Notes!.Trim();
        if (notes is { Length: > DeliveryProofRules.MaxNotesLength }) throw new ValidationException("notes", DeliveryProofRules.NotesTooLong);
        var now = _clock.UtcNow;
        var at = req?.DeliveredAtUtc is DateTime given ? DateTime.SpecifyKind(given, DateTimeKind.Utc) : now;
        if (at > now + DeliveryProofRules.FutureTolerance) throw new ValidationException("deliveredAtUtc", DeliveryProofRules.FutureDelivery);
        var userId = tenant.UserId ?? throw new ForbiddenException("Hace falta una sesión de usuario para registrar la entrega.");
        var orderId = await db.TransportOrders.AsNoTracking().Where(o => o.PublicId == orderPublicId && o.IsActive).Select(o => (int?)o.TransportOrderId).FirstOrDefaultAsync(ct)
                      ?? throw new NotFoundException("Orden", feminine: true);
        var userName = await UserNameAsync(userId, ct);

        int proofId = 0;
        await db.RunInTransactionAsync(async ct2 =>
        {
            var order = await db.TransportOrders.FirstAsync(o => o.TransportOrderId == orderId, ct2);
            if (await db.Set<DeliveryProof>().AnyAsync(p => p.TransportOrderId == orderId && p.IsActive, ct2))
                throw new ConflictException(string.Format(DeliveryProofRules.AlreadyRegisteredFormat, order.OrderNumber));
            var pipeline = await statuses.GetPipelineAsync(StatusDomains.OrderStatus, includeDisabled: true, ct2);
            var current = pipeline.First(s => s.Id == order.StatusCodeId);
            if (string.Equals(current.StageKind, StageKinds.Terminal, StringComparison.OrdinalIgnoreCase))
                throw new StatusRuleException(string.Format(DeliveryProofRules.FinalFormat, order.OrderNumber, current.Label));
            if ((await ServiceTypeMarkReader.LoadAsync(db, order.ServiceTypeLookupId, ct2)).PickUp)
                throw new StatusRuleException(string.Format(DeliveryProofRules.PickUpFormat, order.OrderNumber));

            // Desde un lateral (Intento fallido) se cuenta desde «Recogida», como el escaneo; la entrega es el último paso y se brinca lo de en medio.
            var stages = pipeline.Select(s => new PipelineStage(s.Code, s.SortOrder, s.StageKind, s.IsInitial, s.IsEnabled)).ToList();
            var fromCode = string.Equals(current.StageKind, StageKinds.Lateral, StringComparison.OrdinalIgnoreCase) ? OrderStatuses.Pickup : current.Code;
            var steps = PipelinePath.StepsTo(stages, fromCode, OrderStatuses.Delivered);
            if (steps.Count == 0 || steps[^1] != OrderStatuses.Delivered) throw new StatusRuleException("El estatus «Entregada» no está habilitado para esta compañía.");

            var to = await statuses.AdvanceSkippingAsync(StatusDomains.OrderStatus, EntityTypes.TransportOrder, orderId, order.StatusCodeId, steps, string.Format(DeliveryProofRules.CommentFormat, userName), ct2);
            order.StatusCodeId = to.StatusCodeId;
            if (steps.Contains(OrderStatuses.Pickup) && order.ConfirmedAtUtc is null) order.ConfirmedAtUtc = now;
            await db.SaveGuardedAsync(DbExtensions.ConcurrencyMessage, ct2);

            var proof = new DeliveryProof
            {
                PublicId = Guid.NewGuid(), TenantId = Ctx.RequireTenantId(), TransportOrderId = orderId, DeliveredAtUtc = at, RecipientName = recipient, Notes = notes,
                Source = DeliveryProofCodes.SourceStaffWeb, RecordedBy = userId, SignedByUserId = userId, SignatureKind = DeliveryProofCodes.SignatureUser,
            };
            await PlaceAsync(proof, orderId, ct2);
            db.Set<DeliveryProof>().Add(proof);
            await db.SaveChangesAsync(ct2);
            proofId = proof.DeliveryProofId;
        }, ct);
        return await GetAsync(proofId, "Entrega registrada.", ct);
    }

    /// <summary>La ubicación del comprobante: la de la parada de entrega si ya la tenía; si no, se deja en cola para buscarla con Google (si está encendido y la parada tiene dirección); si no, sin ubicación.</summary>
    private async Task PlaceAsync(DeliveryProof proof, int orderId, CancellationToken ct)
    {
        var deliveryTypeId = await db.LookupCodes.AsNoTracking().Where(l => l.Entity == LookupDomains.StopType && l.InternalCode == StopTypes.Delivery).Select(l => l.LookupCodeId).FirstOrDefaultAsync(ct);
        var stop = await db.OrderStops.AsNoTracking().Where(s => s.TransportOrderId == orderId).OrderByDescending(s => s.StopTypeLookupId == deliveryTypeId).ThenByDescending(s => s.Sequence).FirstOrDefaultAsync(ct);
        if (stop is null) return;
        var points = await db.StopPointsAsync(new[] { stop.OrderStopId }, ct);
        if (points.TryGetValue(stop.OrderStopId, out var p))
        {
            (proof.GeoStatus, proof.Latitude, proof.Longitude, proof.GeoCheckedAtUtc) = (DeliveryProofCodes.GeoFromStop, DeliveryProofRules.Round6(p.Lat), DeliveryProofRules.Round6(p.Lng), _clock.UtcNow);
            return;
        }
        if (maps?.Value.IsUsable == true && !string.IsNullOrWhiteSpace(stop.SnapLine1)) proof.GeoStatus = DeliveryProofCodes.GeoPending;
    }

    public async Task<DeliveryProofDto?> FindByOrderAsync(Guid orderPublicId, CancellationToken ct)
    {
        var id = await (from p in db.Set<DeliveryProof>().AsNoTracking()
                        join o in db.TransportOrders.AsNoTracking() on p.TransportOrderId equals o.TransportOrderId
                        where o.PublicId == orderPublicId && p.IsActive select (int?)p.DeliveryProofId).FirstOrDefaultAsync(ct);
        return id is null ? null : await GetAsync(id.Value, null, ct);
    }

    private async Task<DeliveryProofDto> GetAsync(int proofId, string? message, CancellationToken ct)
    {
        var p = await db.Set<DeliveryProof>().AsNoTracking().FirstAsync(x => x.DeliveryProofId == proofId, ct);
        var order = await db.TransportOrders.AsNoTracking().Where(o => o.TransportOrderId == p.TransportOrderId).Select(o => new { o.PublicId, o.OrderNumber, o.StatusCodeId }).FirstAsync(ct);
        var status = await db.StatusCodes.AsNoTracking().Where(s => s.StatusCodeId == order.StatusCodeId).Select(s => new { s.InternalCode, s.LabelJson }).FirstAsync(ct);
        var names = await db.Users.AsNoTracking().Where(u => u.Id == p.RecordedBy || u.Id == p.SignedByUserId).ToDictionaryAsync(u => u.Id, u => u.FullName ?? u.Email, ct);
        return new DeliveryProofDto(p.PublicId, order.PublicId, order.OrderNumber, p.DeliveredAtUtc, p.RecipientName, p.Notes, p.Source, p.SignatureKind,
            names.GetValueOrDefault(p.RecordedBy), names.GetValueOrDefault(p.SignedByUserId), p.GeoStatus, p.GeoAccuracy, (double?)p.Latitude, (double?)p.Longitude,
            status.InternalCode, Domain.Common.MultilingualText.Resolve(status.LabelJson, tenant.Lang), message);
    }

    private async Task<string> UserNameAsync(int userId, CancellationToken ct)
        => await db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.FullName ?? u.Email).FirstOrDefaultAsync(ct) ?? $"usuario {userId}";
}
