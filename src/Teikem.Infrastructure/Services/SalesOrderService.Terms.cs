using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Clients;
using Teikem.Domain.Common;
using Teikem.Domain.Constants;
using Teikem.Domain.Files;
using Teikem.Domain.Sales;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 222 (P2 del pedido de vendedores) — los datos nuevos de la orden de venta:
/// - Urgente, entrega o recogido, nombre de quien autoriza.
/// - Forma de pago de ESTE pedido (SalesPaymentMethod) y término del crédito: sin forma al crear, se prellena con la del cliente (su término de pago, SalesPaymentRules.Prefill);
///   si la del pedido es otra, la orden queda marcada PaymentChanged. Consignación solo con la forma Consignación.
/// - Cargo de envío (ShippingRules: la regla del cliente si tiene una propia, si no la de la compañía; solo en entrega; el mínimo se mide con el total de los productos
///   después del IVU). Se recalcula al guardar el borrador, al solicitar y al modificar una línea; una vez facturado ya no cambia. Su IVU, si la compañía lo cobra con IVU
///   (Ajustes → Ventas) y el módulo de impuestos está encendido, con la tasa de la venta de producto (respeta la exención del cliente).
/// </summary>
public sealed partial class SalesOrderService
{
    private sealed record TermsPlan(bool? IsUrgent, bool DeliveryModeGiven, string? DeliveryMode, bool PaymentGiven, int? PaymentMethodId, string? PaymentMethod,
        int? PaymentTermId, string? PaymentTerm, bool PaymentChanged, bool? IsConsignment, bool AuthorizedGiven, string? AuthorizedByName);

    /// <summary>
    /// Valida y resuelve (fuera de la transacción) lo que el pedido trae de nuevo. <paramref name="current"/> = la orden como está (null al crear); <paramref name="clientChanged"/> =
    /// el PATCH cambió el cliente (la forma de pago se vuelve a prellenar si no la mandan).
    /// </summary>
    private async Task<TermsPlan> PlanTermsAsync(SalesOrderRequest req, Client client, SalesOrder? current, bool clientChanged, CancellationToken ct)
    {
        string? deliveryMode = null;
        if (req.DeliveryMode is not null)
        {
            var (m, e) = ShippingRules.ParseDeliveryMode(req.DeliveryMode);
            if (e is not null) throw new ValidationException("deliveryMode", e);
            deliveryMode = m;
        }
        string? authorized = null;
        if (req.AuthorizedByName is not null)
        {
            var (n, e) = SalesOrderExtraRules.NormalizeAuthorizedBy(req.AuthorizedByName);
            if (e is not null) throw new ValidationException("authorizedByName", e);
            authorized = n;
        }

        // forma de pago: la que mandan, o la de la orden, o la prellenada con el cliente
        var clientTerm = client.PaymentTermLookupId is int ctId ? (await lookups.GetAsync(ctId, ct))?.InternalCode : null;
        var currentMethod = current?.PaymentMethodLookupId is int cm ? (await lookups.GetAsync(cm, ct))?.InternalCode : null;
        var currentTerm = current?.PaymentTermLookupId is int cterm ? (await lookups.GetAsync(cterm, ct))?.InternalCode : null;
        string? method, term;
        var paymentGiven = req.PaymentMethod is not null || req.PaymentTerm is not null || current is null || clientChanged;
        if (req.PaymentMethod is null && req.PaymentTerm is null && (current is null || clientChanged))
            (method, term) = SalesPaymentRules.Prefill(clientTerm);
        else
        {
            method = req.PaymentMethod is null ? currentMethod : (req.PaymentMethod.Trim().Length == 0 ? null : req.PaymentMethod.Trim().ToUpperInvariant());
            term = req.PaymentTerm is not null ? (req.PaymentTerm.Trim().Length == 0 ? null : req.PaymentTerm.Trim().ToUpperInvariant())
                : method == SalesPaymentMethods.Credit ? currentTerm : null;   // cambiar a otra forma quita el término del crédito
        }
        var error = SalesPaymentRules.ValidateMethodAndTerm(method, term);
        if (error is not null) throw new ValidationException(req.PaymentTerm is not null && req.PaymentMethod is null ? "paymentTerm" : "paymentMethod", error);
        int? methodId = null, termId = null;
        if (method is not null)
            methodId = await lookups.TryGetIdAsync(SalesPaymentMethods.Domain, method, ct) ?? throw new ValidationException("paymentMethod", SalesPaymentRules.MethodUnknown);
        if (term is not null)
            termId = await lookups.TryGetIdAsync(LookupDomains.PaymentTerm, term, ct) ?? throw new ValidationException("paymentTerm", SalesPaymentRules.TermNotCredit);

        // consignación: la que mandan; al crear, la trae la forma Consignación; cambiar a otra forma la quita
        bool? consignment = req.IsConsignment;
        if (consignment is null && current is null) consignment = method == SalesPaymentMethods.Consignment;
        else if (consignment is null && current is not null && current.IsConsignment && method != SalesPaymentMethods.Consignment) consignment = false;
        var consignmentError = SalesPaymentRules.ValidateConsignment(consignment ?? current?.IsConsignment ?? false, method);
        if (consignmentError is not null) throw new ValidationException("isConsignment", consignmentError);

        return new TermsPlan(req.IsUrgent, req.DeliveryMode is not null, deliveryMode, paymentGiven, methodId, method, termId, term,
            SalesPaymentRules.IsChange(clientTerm, method, term), consignment, req.AuthorizedByName is not null, authorized);
    }

    private static void ApplyTerms(SalesOrder order, TermsPlan p)
    {
        if (p.IsUrgent is bool urgent) order.IsUrgent = urgent;
        if (p.DeliveryModeGiven) order.DeliveryMode = p.DeliveryMode;
        if (p.PaymentGiven)
        {
            order.PaymentMethodLookupId = p.PaymentMethodId;
            order.PaymentTermLookupId = p.PaymentTermId;
            order.PaymentChanged = p.PaymentChanged;
        }
        if (p.IsConsignment is bool consignment) order.IsConsignment = consignment;
        if (p.AuthorizedGiven) order.AuthorizedByName = p.AuthorizedByName;
    }

    /// <summary>
    /// Recalcula el cargo de envío de la orden (y su IVU) con la regla del cliente o de la compañía. Una orden con el envío ya facturado no cambia. Lo llama quien ya tiene la orden
    /// rastreada, con los totales de los productos al día.
    /// </summary>
    private async Task RecalculateShippingAsync(SalesOrder order, CancellationToken ct)
    {
        if (order.ShippingInvoiced) return;
        var client = await db.Set<Client>().AsNoTracking().FirstAsync(c => c.ClientId == order.ClientId, ct);
        var company = await db.Tenants.AsNoTracking().Where(t => t.TenantId == order.TenantId)
            .Select(t => new { t.SalesShippingCharge, t.SalesShippingFreeFrom, t.SalesShippingTaxable }).FirstOrDefaultAsync(ct);
        var result = ShippingRules.Compute(new ShippingInput(order.DeliveryMode, order.ConsigneeId is not null, order.Subtotal + order.TaxAmount,
            client.ShippingMode, client.ShippingCharge, client.ShippingFreeFrom, company?.SalesShippingCharge, company?.SalesShippingFreeFrom));
        var charge = result.Charge;
        var shippingTax = 0m;
        if (charge > 0m && company is { SalesShippingTaxable: true } && await modules.IsEnabledAsync(ModuleKeys.Tax, ct))
        {
            // con la tasa de la venta de producto: respeta la exención del cliente y si los precios incluyen el impuesto
            var calc = await tax.CalculateAsync(new TaxCalcRequest(order.ClientId, clock.Today,
                new[] { new TaxCalcLineRequest("shipping", null, Teikem.Domain.Billing.BillingCodes.ProductSale, charge) }), ct);
            charge = calc.Lines[0].Net;
            shippingTax = calc.Lines[0].TaxAmount;
        }
        order.ShippingCharge = charge;
        order.ShippingTaxAmount = shippingTax;
        order.ShippingReasonLookupId = result.Reason is null ? null : await lookups.TryGetIdAsync(ShippingReasons.Domain, result.Reason, ct);
    }

    // ---------------------------------------------------------------- ajustes de envío (Ajustes → Operación → Ventas)

    public async Task<SalesShippingSettingsDto> GetShippingSettingsAsync(CancellationToken ct)
    {
        var t = await db.Tenants.AsNoTracking().FirstAsync(x => x.TenantId == T.RequireTenantId(), ct);
        return new SalesShippingSettingsDto(t.SalesShippingCharge, t.SalesShippingFreeFrom, t.SalesShippingTaxable);
    }

    /// <summary>null = sin cambio; Charge 0 = sin cargo de envío; FreeFrom 0 = sin mínimo. Las órdenes ya solicitadas no cambian; los borradores toman la regla nueva al guardarse o solicitarse.</summary>
    public async Task<SalesShippingSettingsDto> UpdateShippingSettingsAsync(SalesShippingSettingsRequest? req, CancellationToken ct)
    {
        if (req is null) throw new ValidationException("body", "El cuerpo de la solicitud es obligatorio.");
        if (req.Charge is decimal c && c != 0m && !ShippingRules.IsValidCharge(c)) throw new ValidationException("charge", ShippingRules.ChargeInvalid);
        if (req.FreeFrom is decimal f && !ShippingRules.IsValidFreeFrom(f)) throw new ValidationException("freeFrom", ShippingRules.FreeFromInvalid);
        var t = await db.Tenants.AsTracking().FirstAsync(x => x.TenantId == T.RequireTenantId(), ct);
        if (req.Charge is decimal charge) t.SalesShippingCharge = charge == 0m ? null : charge;
        if (req.FreeFrom is decimal freeFrom) t.SalesShippingFreeFrom = freeFrom == 0m ? null : freeFrom;
        if (req.Taxable is bool taxable) t.SalesShippingTaxable = taxable;
        await db.SaveChangesAsync(ct);
        return new SalesShippingSettingsDto(t.SalesShippingCharge, t.SalesShippingFreeFrom, t.SalesShippingTaxable);
    }

    // ---------------------------------------------------------------- DTO

    /// <summary>Los datos nuevos de cada orden (etiquetas en el idioma de la persona) con lo que hace falta del cliente y de la firma.</summary>
    private async Task<Dictionary<int, SalesOrderTermsDto>> TermsOfAsync(IReadOnlyList<SalesOrder> orders, IReadOnlyDictionary<int, Client> clients, CancellationToken ct)
    {
        var prospectId = await db.StatusCodes.AsNoTracking().Where(s => s.Entity == StatusDomains.ClientStatus && s.InternalCode == ClientStatuses.Prospect)
            .Select(s => (int?)s.StatusCodeId).FirstOrDefaultAsync(ct);
        var signatureIds = orders.Where(o => o.SignatureFileId is not null).Select(o => o.SignatureFileId!.Value).Distinct().ToList();
        var signatures = signatureIds.Count == 0 ? new Dictionary<int, Guid>()
            : await db.Set<StoredFile>().AsNoTracking().Where(f => signatureIds.Contains(f.StoredFileId) && f.IsActive).ToDictionaryAsync(f => f.StoredFileId, f => f.PublicId, ct);
        var result = new Dictionary<int, SalesOrderTermsDto>();
        foreach (var o in orders)
        {
            var c = clients.GetValueOrDefault(o.ClientId);
            var method = o.PaymentMethodLookupId is int m ? await lookups.GetAsync(m, ct) : null;
            var term = o.PaymentTermLookupId is int t ? await lookups.GetAsync(t, ct) : null;
            var reason = o.ShippingReasonLookupId is int r ? await lookups.GetAsync(r, ct) : null;
            result[o.SalesOrderId] = new SalesOrderTermsDto(o.IsUrgent, ShippingRules.EffectiveMode(o.DeliveryMode, o.ConsigneeId is not null), o.DeliveryMode is not null,
                method?.InternalCode, method is null ? null : MultilingualText.Resolve(method.LabelJson, T.Lang),
                term?.InternalCode, term is null ? null : MultilingualText.Resolve(term.LabelJson, T.Lang), o.PaymentChanged,
                o.ShippingCharge, o.ShippingTaxAmount, reason?.InternalCode, reason is null ? null : MultilingualText.Resolve(reason.LabelJson, T.Lang),
                o.IsConsignment, o.AuthorizedByName, o.SignatureFileId is int sf && signatures.TryGetValue(sf, out var sp) ? sp : null, o.SubmittedWithAuth,
                o.AwaitingClientApproval, c is not null && prospectId is int pid && c.StatusCodeId == pid, c?.RequireOrderSignature ?? false);
        }
        return result;
    }

    /// <summary>Total de la orden con el envío (los productos, su IVU, el cargo de envío y su IVU).</summary>
    private static decimal TotalOf(SalesOrder o) => SalesOrderExtraRules.Total(o.Subtotal, o.TaxAmount, o.ShippingCharge, o.ShippingTaxAmount);
}
