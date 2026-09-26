using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Clients;
using Teikem.Domain.Constants;
using Teikem.Domain.Orders;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Orders;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 3 (P4): ciclo de estatus de la orden fuera de la captura.
/// - Vista previa (PreviewQuoteAsync): cotización + chequeo de crédito sin persistir (DECISIÓN 28).
/// - Confirmar = salir de la etapa inicial hacia la primera etapa PIPELINE habilitada posterior (normalmente CONFIRMED; si
///   el tenant la deshabilita, PICKUP) vía StatusService.TransitionAsync; el efecto OrderStatusEffect cotiza, congela y
///   verifica el crédito sobre la misma entidad tracked (DECISIÓN 24). Crédito excedido: 422 credit_exceeded; con
///   overrideCredit=true y permiso orders.credit_override se confirma dejando comentario en EntityStatusHistory y
///   SecurityEvent ROLE_CHANGE {action:'credit_override'} (ajuste C). Sin el permiso: 403 + PERMISSION_DENIED.
/// - Reprecio (capacidad REPRICE), cancelación (capacidad CANCEL + entradas laterales del tenant) y estatus laterales
///   / regreso al pipeline (POST /status); los avances PICKUP…DELIVERED quedan reservados a Trips/POD (DECISIÓN 11).
/// Todo bajo OrderScope (404 fuera del scope, sin oráculo) y con RowVersion opcional (409).
/// </summary>
public sealed class OrderStatusService(
    TeikemDbContext db,
    ITenantContext tenant,
    StatusService statuses,
    OrderQuoteService quotes,
    IClientBalanceProvider balance,
    OrderReadService reader,
    IEnumerable<IStatusTransitionEffect> effects,
    PermissionService permissions,
    ISecurityEventWriter security)
{
    /// <summary>Permiso del ajuste C (sembrado por P0 en PermissionCatalog y en el seed).</summary>
    public const string CreditOverridePermission = "orders.credit_override";
    public const string CreditOverrideCommentPrefix = "Crédito excedido autorizado por ";

    public const string NoNextStageMessage = "El pipeline de órdenes no tiene una etapa siguiente a la inicial.";
    public const string NotInitialMessage = "Solo se confirma una orden en su estatus inicial.";
    public const string UseCancelMessage = "Para cancelar use la acción de cancelación.";
    public const string UseConfirmMessage = "Para confirmar use la acción de confirmación.";

    public static string ReservedAdvanceMessage(string code)
        => $"El avance a '{code}' lo realiza el módulo correspondiente (trips/entregas); desde aquí solo se registran estatus laterales.";

    // ================================================================ etapa destino de la confirmación

    /// <summary>Primera etapa PIPELINE habilitada posterior a la inicial (por SortOrder) o 422 si el pipeline no la tiene.</summary>
    public async Task<StatusDto> ConfirmTargetAsync(CancellationToken ct)
    {
        var pipeline = await statuses.GetPipelineAsync(StatusDomains.OrderStatus, includeDisabled: false, ct);
        var initial = pipeline.Where(s => s.IsInitial).OrderBy(s => s.SortOrder).FirstOrDefault()
                      ?? throw new StatusRuleException(NoNextStageMessage);
        return pipeline
                   .Where(s => !s.IsInitial && s.SortOrder > initial.SortOrder
                               && string.Equals(s.StageKind, StageKinds.Pipeline, StringComparison.OrdinalIgnoreCase))
                   .OrderBy(s => s.SortOrder).FirstOrDefault()
               ?? throw new StatusRuleException(NoNextStageMessage);
    }

    // ================================================================ vista previa

    /// <summary>Cotización y chequeo de crédito sin persistir nada; los 409/422 son los mismos que daría confirmar.</summary>
    public async Task<OrderQuotePreviewDto> PreviewQuoteAsync(Guid publicId, OrderScope scope, CancellationToken ct)
    {
        var order = await db.ResolveOrderForReadAsync(publicId, scope, ct);
        var client = await LoadClientAsync(order.ClientId, ct);
        var today = Today();
        var computation = await quotes.ComputeAsync(order, client, today, ct);
        var credit = await CheckCreditAsync(client, order.TransportOrderId, computation.Total, ct);
        return new OrderQuotePreviewDto(computation.Total, computation.Lines, computation.DispatchFee, computation.CodFee,
            computation.ContractPublicId, today, order.IsSpecialDelivery, computation.SpecialServiceName, credit);
    }

    // ================================================================ confirmar

    /// <summary>Confirma la orden tracked dentro de la transacción del llamador (OrderService con confirmNow, ConfirmAsync).</summary>
    public Task ConfirmTrackedAsync(TransportOrder order, CancellationToken ct) => ConfirmTrackedAsync(order, overrideCredit: false, ct);

    /// <summary>
    /// Confirma la orden tracked: valida etapa inicial, resuelve la etapa destino y transiciona (el efecto cotiza, congela y
    /// verifica el crédito). Con overrideCredit=true exige orders.credit_override (403 + PERMISSION_DENIED si falta) y, solo
    /// si el crédito realmente se excede, autoriza el efecto, deja el comentario en el historial y registra el SecurityEvent.
    /// </summary>
    public async Task ConfirmTrackedAsync(TransportOrder order, bool overrideCredit, CancellationToken ct)
    {
        // Una orden eliminada en captura (IsActive=0) no vuelve al pipeline (L253); ResolveOrderForWriteAsync ya la excluye.
        if (!order.IsActive) throw new NotFoundException(OrderQueries.OrderLabel);
        if (!await db.IsInitialAsync(order.StatusCodeId, ct)) throw new StatusRuleException(NotInitialMessage);
        var target = await ConfirmTargetAsync(ct);

        string? comment = null;
        CreditCheck? overridden = null;
        if (overrideCredit)
        {
            await permissions.EnsureAsync(CreditOverridePermission, ct);
            var client = await LoadClientAsync(order.ClientId, ct);
            var computation = await quotes.ComputeAsync(order, client, Today(), ct);
            var pending = await balance.PendingBalanceAsync(client.ClientId, order.TransportOrderId, ct);
            var check = new CreditCheck(client.CreditLimit, pending, computation.Total);
            if (!CreditRules.Evaluate(check).Ok)
            {
                overridden = check;
                comment = CreditOverrideCommentPrefix + await CurrentUserNameAsync(ct)
                          + $" (límite {CreditRules.Format(check.Limit ?? 0m)}, en curso {CreditRules.Format(CreditRules.Exposure(check))}, esta orden {CreditRules.Format(check.NewAmount)})";
                var effect = effects.OfType<OrderStatusEffect>().FirstOrDefault()
                             ?? throw new InvalidOperationException("OrderStatusEffect no está registrado como IStatusTransitionEffect (scoped).");
                effect.AuthorizeCreditOverride(order.TransportOrderId);
            }
        }

        var to = await statuses.TransitionAsync(StatusDomains.OrderStatus, EntityTypes.TransportOrder, order.TransportOrderId,
            order.StatusCodeId, target.Code, comment, ct);
        order.StatusCodeId = to.StatusCodeId;
        order.ConfirmedAtUtc = DateTime.UtcNow;
        await db.SaveGuardedAsync(DbExtensions.ConcurrencyMessage, ct);

        if (overridden is not null)
            await security.WriteAsync(SecurityEventTypes.RoleChange, SecurityOutcomes.Success, tenant.UserId, tenant.TenantId,
                new
                {
                    action = "credit_override",
                    order = order.PublicId,
                    orderNumber = order.OrderNumber,
                    limit = overridden.Limit,
                    exposure = CreditRules.Exposure(overridden),
                    amount = overridden.NewAmount,
                }, ct);
    }

    public Task<OrderDetailDto> ConfirmAsync(Guid publicId, OrderConfirmRequest? req, OrderScope scope, CancellationToken ct)
        => ConfirmAsync(publicId, req, scope, overrideCredit: false, ct);

    public async Task<OrderDetailDto> ConfirmAsync(Guid publicId, OrderConfirmRequest? req, OrderScope scope, bool overrideCredit, CancellationToken ct)
    {
        await db.RunInTransactionAsync(async ct2 =>
        {
            var order = await db.ResolveOrderForWriteAsync(publicId, scope, ct2);
            db.ApplyRowVersion(order, req?.RowVersion);
            await ConfirmTrackedAsync(order, overrideCredit, ct2);
        }, ct);
        return await reader.GetAsync(publicId, scope, ct);
    }

    // ================================================================ reprecio

    /// <summary>Re-cotiza la orden tracked sin cambiar de estatus (lo usa el PATCH de P2 cuando la orden ya estaba cotizada).</summary>
    public async Task RequoteTrackedAsync(TransportOrder order, CancellationToken ct)
    {
        var client = await LoadClientAsync(order.ClientId, ct);
        await quotes.ApplyQuoteAsync(order, client, Today(), ct);
    }

    public async Task<OrderDetailDto> RepriceAsync(Guid publicId, OrderScope scope, CancellationToken ct)
    {
        await db.RunInTransactionAsync(async ct2 =>
        {
            var order = await db.ResolveOrderForWriteAsync(publicId, scope, ct2);
            await statuses.EnsureAllowedAsync(EntityTypes.TransportOrder, order.StatusCodeId, Capabilities.Reprice, ct2);
            await RequoteTrackedAsync(order, ct2);
            await db.SaveGuardedAsync(DbExtensions.ConcurrencyMessage, ct2);
        }, ct);
        return await reader.GetAsync(publicId, scope, ct);
    }

    // ================================================================ cancelar

    public async Task<OrderDetailDto> CancelAsync(Guid publicId, OrderCancelRequest? req, OrderScope scope, CancellationToken ct)
    {
        await db.RunInTransactionAsync(async ct2 =>
        {
            var order = await db.ResolveOrderForWriteAsync(publicId, scope, ct2);
            db.ApplyRowVersion(order, req?.RowVersion);
            await statuses.EnsureAllowedAsync(EntityTypes.TransportOrder, order.StatusCodeId, Capabilities.Cancel, ct2);
            var to = await statuses.TransitionAsync(StatusDomains.OrderStatus, EntityTypes.TransportOrder, order.TransportOrderId,
                order.StatusCodeId, OrderStatuses.Cancelled, req?.Comment, ct2);
            order.StatusCodeId = to.StatusCodeId;
            await db.SaveGuardedAsync(DbExtensions.ConcurrencyMessage, ct2);
        }, ct);
        return await reader.GetAsync(publicId, scope, ct);
    }

    // ================================================================ laterales y regreso

    /// <summary>
    /// Estatus laterales (ON_HOLD, PARTIAL, FAILED) y regreso al pipeline. CANCELLED y la confirmación tienen acción propia
    /// (422); cualquier otro avance PIPELINE/TERMINAL lo realiza el módulo correspondiente (422).
    /// </summary>
    public async Task<OrderDetailDto> TransitionAsync(Guid publicId, StatusChangeRequest req, OrderScope scope, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req?.ToCode)) throw new ValidationException("toCode", "Indique el estatus destino.");

        await db.RunInTransactionAsync(async ct2 =>
        {
            var order = await db.ResolveOrderForWriteAsync(publicId, scope, ct2);
            var to = await statuses.GetByCodeAsync(StatusDomains.OrderStatus, req.ToCode.Trim().ToUpperInvariant(), ct2); // 404
            var toKind = to.StageKind?.InternalCode ?? string.Empty;
            var currentKind = await db.StageKindAsync(order.StatusCodeId, ct2);

            if (string.Equals(to.InternalCode, OrderStatuses.Cancelled, StringComparison.OrdinalIgnoreCase))
                throw new StatusRuleException(UseCancelMessage);
            if (await db.IsInitialAsync(order.StatusCodeId, ct2))
            {
                var target = await ConfirmTargetAsync(ct2);
                if (target.Id == to.StatusCodeId) throw new StatusRuleException(UseConfirmMessage);
            }

            var isLateral = string.Equals(toKind, StageKinds.Lateral, StringComparison.OrdinalIgnoreCase);
            var isReturn = string.Equals(toKind, StageKinds.Pipeline, StringComparison.OrdinalIgnoreCase)
                           && string.Equals(currentKind, StageKinds.Lateral, StringComparison.OrdinalIgnoreCase);
            if (!isLateral && !isReturn) throw new StatusRuleException(ReservedAdvanceMessage(to.InternalCode));

            // Regreso desde un lateral: solo a la etapa PIPELINE desde la que se desvió la orden. StatusService admitiría también
            // la siguiente, lo que permitiría confirmar sin cotizar ni verificar crédito (DRAFT → ON_HOLD → CONFIRMED) o avanzar
            // el pipeline reservado a Trips/POD (CONFIRMED → ON_HOLD → PICKUP).
            if (isReturn)
            {
                var orderId = order.TransportOrderId;
                var lastPipelineId = await db.EntityStatusHistories.AsNoTracking()
                    .Where(h => h.EntityId == orderId
                                && h.EntityType!.Entity == LookupDomains.EntityType
                                && h.EntityType.InternalCode == EntityTypes.TransportOrder
                                && h.ToStatus!.StageKind!.InternalCode == StageKinds.Pipeline)
                    .OrderByDescending(h => h.EntityStatusHistoryId)
                    .Select(h => (int?)h.ToStatusCodeId)
                    .FirstOrDefaultAsync(ct2);
                if (lastPipelineId is not null && to.StatusCodeId != lastPipelineId)
                {
                    var target = await ConfirmTargetAsync(ct2);
                    var lastIsInitial = await db.IsInitialAsync(lastPipelineId.Value, ct2);
                    throw new StatusRuleException(lastIsInitial && target.Id == to.StatusCodeId
                        ? UseConfirmMessage
                        : ReservedAdvanceMessage(to.InternalCode));
                }
            }

            var result = await statuses.TransitionAsync(StatusDomains.OrderStatus, EntityTypes.TransportOrder, order.TransportOrderId,
                order.StatusCodeId, to.InternalCode, req.Comment, ct2);
            order.StatusCodeId = result.StatusCodeId;
            await db.SaveGuardedAsync(DbExtensions.ConcurrencyMessage, ct2);
        }, ct);
        return await reader.GetAsync(publicId, scope, ct);
    }

    // ================================================================ helpers

    private async Task<CreditCheckDto> CheckCreditAsync(Client client, int orderId, decimal orderTotal, CancellationToken ct)
    {
        var pending = await balance.PendingBalanceAsync(client.ClientId, orderId, ct);
        var check = new CreditCheck(client.CreditLimit, pending, orderTotal);
        var (ok, available) = CreditRules.Evaluate(check);
        return new CreditCheckDto(client.CreditLimit, pending, orderTotal, available, !ok);
    }

    private async Task<Client> LoadClientAsync(int clientId, CancellationToken ct)
        => await db.Clients.AsNoTracking().FirstOrDefaultAsync(c => c.ClientId == clientId, ct)
           ?? throw new NotFoundException("Cliente");

    private async Task<string> CurrentUserNameAsync(CancellationToken ct)
    {
        if (tenant.UserId is not int userId) return "sistema";
        var name = await db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.FullName ?? u.Email).FirstOrDefaultAsync(ct);
        return string.IsNullOrWhiteSpace(name) ? $"usuario {userId}" : name;
    }

    private static DateOnly Today() => DateOnly.FromDateTime(DateTime.UtcNow);
}
