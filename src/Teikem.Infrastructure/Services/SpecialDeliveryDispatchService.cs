using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Constants;
using Teikem.Domain.Fleet;
using Teikem.Domain.Orders;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Fleet;
using Teikem.Infrastructure.Orders;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Chofer, disponibilidad y tarifa ya verificados ANTES de sacar números (PrepareAsync). No es un DTO HTTP: vive aquí y no
/// en Contracts. La tarifa por viaje resuelta se congela tal cual en el DriverTrip.
/// </summary>
public sealed record PreparedSpecialDelivery(int DriverId, string DriverCode, string DriverName, DriverRateResolution TripRate);

/// <summary>
/// Lote 4 (P7): entrega especial con chofer (completa el pendiente del Lote 3).
/// - PrepareAsync (sin escribir nada): módulo CATALOG encendido (el endpoint vive en LTL_GROUND pero usa choferes y tarifas),
///   permiso trips.dispatch, chofer del tenant (404), disponibilidad para despacho (409 con los motivos bloqueantes) y tarifa
///   por viaje vigente. OrderService lo llama antes del bucle de numeración: un rechazo no consume NumberSequence.
/// - AssignTrackedAsync (dentro de la transacción del llamador): confirma si la orden está en la etapa inicial (cotiza y
///   verifica crédito como el Lote 3), exige ASSIGN_TRIP, cancela el viaje vigente de otro chofer (reasignación), avanza
///   etapa por etapa hasta IN_TRANSIT (PipelinePath, una transición e historial por paso) y crea el DriverTrip con el monto
///   congelado. UX_DriverTrip_Order hace chocar dos asignaciones concurrentes (409).
/// - AssignAsync: POST /orders/{publicId}/driver (asignar o reasignar sobre una orden ya creada).
/// La orden expone solo la identidad del chofer asignado (OrderReadService), nunca montos.
/// </summary>
public sealed class SpecialDeliveryDispatchService(
    TeikemDbContext db,
    ITenantContext tenant,
    StatusService statuses,
    OrderStatusService orderStatuses,
    DriverTripService trips,
    IFleetAvailabilityService availability,
    IDriverRateResolver rates,
    PermissionService permissions,
    ModuleService modules,
    OrderReadService reader)
{
    public const string NotSpecialMessage = "Solo las entregas especiales se asignan a un chofer desde aquí; las demás órdenes pasan por Sala de despacho.";
    public const string PastTransitMessage = "La entrega especial ya llegó a destino o terminó; no se puede asignar ni reasignar el chofer.";
    public const string AlreadyAssignedMessage = "La orden ya está asignada a ese chofer.";
    public const string DriverNotAvailablePrefix = "El chofer no está disponible para despacho: ";

    public static string DriverNotAvailableMessage(IEnumerable<string> blockingMessages)
        => DriverNotAvailablePrefix + string.Join("; ", blockingMessages.Select(m => m.Trim().TrimEnd('.'))) + ".";

    public static string AssignedComment(string driverCode, string driverName) => $"Entrega especial asignada a {driverCode} {driverName}";
    public static string ReassignedComment(string driverCode, string driverName) => $"Reasignada a {driverCode} {driverName}";

    // ================================================================ preparar (sin escribir, antes de numerar)

    public async Task<PreparedSpecialDelivery> PrepareAsync(Guid driverPublicId, int specialServiceTypeId, DateOnly date, CancellationToken ct)
    {
        await modules.EnsureEnabledAsync(ModuleKeys.Catalog, ct);               // 403 module_disabled
        await permissions.EnsureAsync(PermissionCatalog.TripsDispatch, ct);      // 403 'Falta el permiso 'trips.dispatch'.'
        var driver = await db.ResolveDriverAsync(driverPublicId, false, ct);     // 404 'Chofer no encontrado.'

        var check = await availability.CheckDriverAsync(driver.DriverId, date, ct);
        if (!check.Available)
        {
            var blocking = check.Issues.Where(i => i.Blocking).Select(i => i.Message).ToList();
            if (blocking.Count == 0) blocking = check.Issues.Select(i => i.Message).ToList();
            throw new ConflictException(DriverNotAvailableMessage(blocking));
        }

        var tripRate = await rates.TripRateAsync(driver.DriverId, specialServiceTypeId, date, ct);
        return new PreparedSpecialDelivery(driver.DriverId, driver.EmployeeCode, driver.FullName, tripRate);
    }

    // ================================================================ asignar (dentro de la transacción del llamador)

    public async Task AssignTrackedAsync(TransportOrder order, PreparedSpecialDelivery p, bool overrideCredit, string? comment, CancellationToken ct)
    {
        // 1. Solo entregas especiales
        if (!order.IsSpecialDelivery || order.SpecialServiceId is not int specialServiceId) throw new StatusRuleException(NotSpecialMessage);

        // 2. En la etapa inicial se confirma (cotiza, congela y verifica crédito; 422 credit_exceeded como el Lote 3)
        if (await db.IsInitialAsync(order.StatusCodeId, ct))
            await orderStatuses.ConfirmTrackedAsync(order, overrideCredit, ct);

        // 3. Capacidad por estatus
        await statuses.EnsureAllowedAsync(EntityTypes.TransportOrder, order.StatusCodeId, Capabilities.AssignTrip, ct);

        // 4. Lateral, terminal o ya después de IN_TRANSIT
        var pipeline = await statuses.GetPipelineAsync(StatusDomains.OrderStatus, includeDisabled: true, ct);
        var current = pipeline.FirstOrDefault(s => s.Id == order.StatusCodeId);
        var target = pipeline.FirstOrDefault(s => string.Equals(s.Code, OrderStatuses.InTransit, StringComparison.OrdinalIgnoreCase))
                     ?? throw new InvalidOperationException("El pipeline de órdenes no tiene la etapa IN_TRANSIT.");
        if (current is null
            || !string.Equals(current.StageKind, StageKinds.Pipeline, StringComparison.OrdinalIgnoreCase)
            || current.SortOrder > target.SortOrder)
            throw new StatusRuleException(PastTransitMessage);

        // 5-6. Viaje vigente: mismo chofer → 409; otro chofer → se cancela (y se guarda) antes de insertar el nuevo
        var orderId = order.TransportOrderId;
        var existing = await db.DriverTrips.FirstOrDefaultAsync(t => t.TransportOrderId == orderId && t.IsActive, ct);
        if (existing is not null)
        {
            if (existing.DriverId == p.DriverId) throw new ConflictException(AlreadyAssignedMessage);
            await trips.CancelTrackedAsync(existing, ReassignedComment(p.DriverCode, p.DriverName), ct);
        }

        // 7. Avance etapa por etapa hasta IN_TRANSIT (StatusService no permite saltos; se omiten las deshabilitadas)
        var stages = pipeline.Select(s => new PipelineStage(s.Code, s.SortOrder, s.StageKind, s.IsInitial, s.IsEnabled));
        var stepComment = AssignedComment(p.DriverCode, p.DriverName);
        foreach (var code in PipelinePath.StepsTo(stages, current.Code, OrderStatuses.InTransit))
        {
            var to = await statuses.TransitionAsync(StatusDomains.OrderStatus, EntityTypes.TransportOrder, orderId,
                order.StatusCodeId, code, stepComment, ct);
            order.StatusCodeId = to.StatusCodeId;
        }

        // 8. Viaje con el monto congelado (tipo = el del servicio especial de la orden; fecha = hoy)
        var typeId = await db.SpecialServices.AsNoTracking()
                         .Where(s => s.SpecialServiceId == specialServiceId)
                         .Select(s => (int?)s.SpecialServiceTypeId)
                         .FirstOrDefaultAsync(ct)
                     ?? throw new InvalidOperationException($"La orden {order.OrderNumber} apunta a un servicio especial inexistente.");
        await trips.CreateTrackedAsync(p.DriverId, typeId, Today(), orderId, comment, p.TripRate, ct);

        // 9. Historial y orden; dos asignaciones concurrentes chocan en UX_DriverTrip_Order
        await db.SaveGuardedAsync(DriverTripService.OrderTripTakenMessage, ct);
    }

    // ================================================================ POST /orders/{publicId}/driver

    public async Task<OrderDetailDto> AssignAsync(Guid orderPublicId, SpecialDeliveryAssignRequest req, OrderScope scope, CancellationToken ct)
    {
        ((TenantContext)tenant).RequireTenantId();
        if (req is null) throw new ValidationException("driverPublicId", "Indique el chofer.");
        if (DriverTripRules.ValidateNotes(req.Comment) is string commentError) throw new ValidationException("comment", commentError);

        // Orden y su tipo de viaje, sin tracking (404 fuera del tenant o del scope; 422 si no es entrega especial)
        var head = await db.ScopedOrders(scope).AsNoTracking()
                       .Where(o => o.PublicId == orderPublicId && o.IsActive)
                       .Select(o => new { o.IsSpecialDelivery, o.SpecialServiceId })
                       .FirstOrDefaultAsync(ct)
                   ?? throw new NotFoundException(OrderQueries.OrderLabel);
        if (!head.IsSpecialDelivery || head.SpecialServiceId is not int specialServiceId) throw new StatusRuleException(NotSpecialMessage);
        var typeId = await db.SpecialServices.AsNoTracking()
                         .Where(s => s.SpecialServiceId == specialServiceId)
                         .Select(s => (int?)s.SpecialServiceTypeId)
                         .FirstOrDefaultAsync(ct)
                     ?? throw new StatusRuleException(NotSpecialMessage);

        if (req.OverrideCredit) await permissions.EnsureAsync(PermissionCatalog.OrdersCreditOverride, ct);
        var prepared = await PrepareAsync(req.DriverPublicId, typeId, Today(), ct);

        var comment = string.IsNullOrWhiteSpace(req.Comment) ? null : req.Comment.Trim();
        await db.RunInTransactionAsync(async ct2 =>
        {
            var order = await db.ResolveOrderForWriteAsync(orderPublicId, scope, ct2);
            db.ApplyRowVersion(order, req.RowVersion);
            await AssignTrackedAsync(order, prepared, req.OverrideCredit, comment, ct2);
        }, ct);
        return await reader.GetAsync(orderPublicId, scope, ct);
    }

    private static DateOnly Today() => DateOnly.FromDateTime(DateTime.UtcNow);
}
