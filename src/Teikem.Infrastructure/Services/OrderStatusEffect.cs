using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Constants;
using Teikem.Domain.Orders;
using Teikem.Infrastructure.Clients;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Orders;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Abstractions;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// 422 con code 'credit_exceeded' y errors {limit, exposure, newAmount, available} (ajuste C; sin orders.view_price, Lote 39:
/// mensaje sin cifras y errors vacío): el front muestra el aviso
/// y el botón "Autorizar" (overrideCredit=true, exige orders.credit_override). StatusRuleException tiene el code fijo
/// 'status_rule', por eso esta excepción propia; el middleware ya serializa code y errors.
/// </summary>
public sealed class CreditExceededException : TeikemException
{
    public CreditExceededException(CreditCheck check, decimal? available) : this(check, available, showAmounts: true) { }

    /// <summary>
    /// Lote 39: con <paramref name="showAmounts"/> = false (quien confirma no tiene orders.view_price) el aviso va SIN cifras:
    /// mensaje <see cref="CreditRules.RedactedMessage"/> y errors vacío; el código credit_exceeded y el 422 no cambian. Check y
    /// Available quedan solo del lado del servidor (no se serializan).
    /// </summary>
    public CreditExceededException(CreditCheck check, decimal? available, bool showAmounts)
        : base(showAmounts ? CreditRules.Message(check) : CreditRules.RedactedMessage, 422, "credit_exceeded")
    {
        Check = check;
        Available = available;
        ShowsAmounts = showAmounts;
        Errors = showAmounts
            ? new Dictionary<string, string[]>
            {
                ["limit"] = [check.Limit is decimal l ? CreditRules.Format(l) : string.Empty],
                ["exposure"] = [CreditRules.Format(CreditRules.Exposure(check))],
                ["newAmount"] = [CreditRules.Format(check.NewAmount)],
                ["available"] = [available is decimal a ? CreditRules.Format(a) : string.Empty],
            }
            : new Dictionary<string, string[]>();
    }

    public CreditCheck Check { get; }
    public decimal? Available { get; }
    /// <summary>Lote 39: false = aviso sin cifras (quien confirma no tiene orders.view_price).</summary>
    public bool ShowsAmounts { get; }
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
public sealed class OrderStatusEffect(TeikemDbContext db, IServiceProvider services, IClientBalanceProvider balance, ITenantClock? clock = null) : IStatusTransitionEffect
{
    private readonly ITenantClock _clock = clock ?? TenantClock.Default;

    public const string ClientSuspendedMessage = "El cliente está suspendido; no se pueden crear ni confirmar órdenes.";

    private readonly HashSet<int> _creditOverrides = new();

    public string StatusDomain => StatusDomains.OrderStatus;

    /// <summary>Autoriza (para esta unidad de trabajo) confirmar la orden aunque exceda el crédito. Solo lo llama OrderStatusService tras validar el permiso.</summary>
    public void AuthorizeCreditOverride(int orderId) => _creditOverrides.Add(orderId);

    /// <summary>Último chequeo de crédito evaluado por el efecto (lo lee OrderStatusService para la bitácora del override).</summary>
    public CreditCheck? LastCreditCheck { get; private set; }

    /// <summary>Lote 107: sin acciones. La cotización, el cliente suspendido y el crédito se revisan al someter la orden (OrderStatusService.QuoteOnSubmissionAsync); salir de Sometida ya no revisa nada.</summary>
    public Task OnTransitionedAsync(StatusTransitionContext context, CancellationToken ct) => Task.CompletedTask;

    /// <summary>
    /// Lote 39: el 422 credit_exceeded con cifras solo para quien ve precios (orders.view_price, OrderPriceVisibility del scope);
    /// sin el permiso, o sin el servicio de la decisión, el aviso va sin cifras.
    /// </summary>
    public static async Task<CreditExceededException> CreditExceededAsync(IServiceProvider services, CreditCheck check, decimal? available, CancellationToken ct)
        => new(check, available, await OrderPriceVisibility.CanViewAsync(services.GetService<OrderPriceVisibility>(), ct));
}
