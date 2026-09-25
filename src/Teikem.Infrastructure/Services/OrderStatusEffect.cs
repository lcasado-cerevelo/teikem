using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Constants;
using Teikem.Domain.Orders;
using Teikem.Infrastructure.Clients;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Orders;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// 422 con code 'credit_exceeded' y errors {limit, exposure, newAmount, available} (ajuste C): el front muestra el aviso
/// y el botón "Autorizar" (overrideCredit=true, exige orders.credit_override). StatusRuleException tiene el code fijo
/// 'status_rule', por eso esta excepción propia; el middleware ya serializa code y errors.
/// </summary>
public sealed class CreditExceededException : TeikemException
{
    public CreditExceededException(CreditCheck check, decimal? available)
        : base(CreditRules.Message(check), 422, "credit_exceeded")
    {
        Check = check;
        Available = available;
        Errors = new Dictionary<string, string[]>
        {
            ["limit"] = [check.Limit is decimal l ? CreditRules.Format(l) : string.Empty],
            ["exposure"] = [CreditRules.Format(CreditRules.Exposure(check))],
            ["newAmount"] = [CreditRules.Format(check.NewAmount)],
            ["available"] = [available is decimal a ? CreditRules.Format(a) : string.Empty],
        };
    }

    public CreditCheck Check { get; }
    public decimal? Available { get; }
}

/// <summary>
/// Efecto del dominio OrderStatus (Lote 3, P4; DECISIÓN 8/24). Actúa SOLO al salir de la etapa inicial hacia una etapa
/// PIPELINE (From.IsInitial &amp;&amp; To.StageKind == PIPELINE): sea CONFIRMED o la que el tenant tenga habilitada después de
/// DRAFT. Ahí (1) exige cliente no SUSPENDED, (2) cotiza y congela (OrderQuoteService.ApplyQuoteAsync) sobre la MISMA
/// entidad tracked del llamador y (3) verifica el crédito (CreditRules) con el saldo pendiente de IClientBalanceProvider.
/// Si se excede lanza CreditExceededException (422 credit_exceeded): la transacción del llamador se revierte (ni estatus ni
/// cotización), salvo que OrderStatusService haya autorizado el override para esa orden (permiso orders.credit_override).
/// Para el resto de transiciones no hace nada (el COD pendiente lo resuelve 11B).
/// OrderQuoteService se resuelve de forma perezosa: OrderQuoteService → RateService → StatusService → efectos → este efecto
/// formaría un ciclo de construcción en DI si se inyectara en el constructor.
/// </summary>
public sealed class OrderStatusEffect(TeikemDbContext db, IServiceProvider services, IClientBalanceProvider balance) : IStatusTransitionEffect
{
    public const string ClientSuspendedMessage = "El cliente está suspendido; no se pueden crear ni confirmar órdenes.";

    private readonly HashSet<int> _creditOverrides = new();

    public string StatusDomain => StatusDomains.OrderStatus;

    /// <summary>Autoriza (para esta unidad de trabajo) confirmar la orden aunque exceda el crédito. Solo lo llama OrderStatusService tras validar el permiso.</summary>
    public void AuthorizeCreditOverride(int orderId) => _creditOverrides.Add(orderId);

    /// <summary>Último chequeo de crédito evaluado por el efecto (lo lee OrderStatusService para la bitácora del override).</summary>
    public CreditCheck? LastCreditCheck { get; private set; }

    public async Task OnTransitionedAsync(StatusTransitionContext context, CancellationToken ct)
    {
        if (!string.Equals(context.EntityTypeCode, EntityTypes.TransportOrder, StringComparison.OrdinalIgnoreCase)) return;
        if (context.From is not { IsInitial: true }) return;
        if (!string.Equals(context.To.StageKind?.InternalCode, StageKinds.Pipeline, StringComparison.OrdinalIgnoreCase)) return;

        // La entidad tracked del llamador (ResolveOrderForWriteAsync ya trae las líneas); si no está, se carga con tracking
        // para que la cotización quede en la misma unidad de trabajo. Siempre bajo el filtro global de tenant.
        var order = db.TransportOrders.Local.FirstOrDefault(o => o.TransportOrderId == context.EntityId)
                    ?? await db.TransportOrders.Include(o => o.CargoLines).FirstOrDefaultAsync(o => o.TransportOrderId == context.EntityId, ct)
                    ?? throw new NotFoundException("Orden");

        var client = await db.Clients.AsNoTracking().FirstOrDefaultAsync(c => c.ClientId == order.ClientId, ct)
                     ?? throw new NotFoundException("Cliente");
        var suspendedId = await db.StatusIdAsync(StatusDomains.ClientStatus, ClientStatuses.Suspended, ct);
        if (client.StatusCodeId == suspendedId) throw new StatusRuleException(ClientSuspendedMessage);

        var quotes = services.GetRequiredService<OrderQuoteService>();
        await quotes.ApplyQuoteAsync(order, client, DateOnly.FromDateTime(DateTime.UtcNow), ct);

        var pending = await balance.PendingBalanceAsync(client.ClientId, order.TransportOrderId, ct);
        var check = new CreditCheck(client.CreditLimit, pending, order.QuotedAmount!.Value);
        LastCreditCheck = check;
        var (ok, available) = CreditRules.Evaluate(check);
        if (ok || _creditOverrides.Contains(order.TransportOrderId)) return;
        throw new CreditExceededException(check, available);
    }
}
