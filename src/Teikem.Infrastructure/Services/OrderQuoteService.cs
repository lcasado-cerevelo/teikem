using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Clients;
using Teikem.Domain.Common;
using Teikem.Domain.Orders;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Clients;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Orders;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 3 (P4): cotización de una orden reutilizando el motor del Lote 2 (RateService.QuoteAsync / ContractRateResolver).
/// - ComputeAsync NO muta nada: lo usa la vista previa (GET /orders/{id}/quote) y la confirmación.
/// - Orden normal: las CargoLine activas se consolidan por tipo de paquete (OrderQuoteLines) y se cotizan contra el
///   contrato vigente del cliente + genéricas del tenant; si alguna línea sale sin tarifa base (BaseSource NONE) es 422:
///   nunca se congela un $0 silencioso (DECISIÓN 9).
/// - Entrega especial: la tarifa vigente del SpecialService del cliente (409 si se cerró), sin líneas.
/// - ApplyQuoteAsync congela QuotedAmount/QuotedAtUtc/ContractId (y la moneda si faltaba) en la entidad tracked; el
///   llamador guarda en su unidad de trabajo.
/// </summary>
public sealed class OrderQuoteService(TeikemDbContext db, ILookupCache lookups, RateService rates)
{
    public const string SpecialServiceClosedMessage = "El servicio especial ya no está vigente; elija otro antes de confirmar.";
    public const string NoLinesMessage = "La orden no tiene líneas de paquete activas que cotizar.";

    public static string MissingRateMessage(string serviceType, string packageType)
        => $"No hay tarifa vigente para {serviceType}/{packageType}; configure la tarifa en el contrato del cliente antes de confirmar.";

    /// <summary>Cotiza la orden en asOf sin persistir nada. La orden debe traer sus CargoLines (activas) cargadas.</summary>
    public async Task<OrderQuoteComputation> ComputeAsync(TransportOrder order, Client client, DateOnly asOf, CancellationToken ct)
    {
        var contract = await db.CurrentContractAsync(client.ClientId, asOf, ct);

        if (order.IsSpecialDelivery)
        {
            // Servicio especial del cliente de la orden (bajo el filtro global), activo y vigente en asOf.
            SpecialService? row = null;
            if (order.SpecialServiceId is int specialServiceId)
                row = await db.SpecialServices.AsNoTracking().Include(s => s.Type)
                    .FirstOrDefaultAsync(s => s.SpecialServiceId == specialServiceId && s.ClientId == client.ClientId, ct);
            if (row is null || !row.IsActive || !EffectiveDated.IsCurrentOn(row, asOf))
                throw new ConflictException(SpecialServiceClosedMessage);

            return new OrderQuoteComputation(Money.Round4(row.Rate), Array.Empty<QuoteLineDto>(), 0m, 0m,
                contract?.ContractId, contract?.PublicId, row.Type?.Name);
        }

        var serviceType = await lookups.GetAsync(order.ServiceTypeLookupId, ct)
                          ?? throw new StatusRuleException("El tipo de servicio de la orden no existe en el catálogo.");

        var raw = new List<(string PackageType, int Pieces)>();
        foreach (var line in order.CargoLines.Where(l => l.IsActive))
        {
            if (line.PackageTypeLookupId is not int packageTypeId) continue;
            var packageType = await lookups.GetAsync(packageTypeId, ct);
            if (packageType is null) continue;
            raw.Add((packageType.InternalCode, Math.Max(1, (int)line.Quantity)));
        }

        var lines = OrderQuoteLines.Build(serviceType.InternalCode, raw);
        if (lines.Count == 0) throw new StatusRuleException(NoLinesMessage);

        var request = new RateQuoteRequest(client.PublicId,
            lines.Select(l => new RateQuoteLineRequest(l.ServiceType, l.PackageType, l.Pieces)).ToList(),
            order.CodAmount ?? 0m, asOf);
        var quote = await rates.QuoteAsync(request, ct);

        var missing = quote.Lines.FirstOrDefault(l => string.Equals(l.BaseSource, RateSources.None, StringComparison.OrdinalIgnoreCase));
        if (missing is not null) throw new StatusRuleException(MissingRateMessage(missing.ServiceType, missing.PackageType));

        return new OrderQuoteComputation(quote.Total, quote.Lines, quote.DispatchFee, quote.CodFee,
            contract?.ContractId, contract?.PublicId, null);
    }

    /// <summary>
    /// Cotiza y congela en la entidad tracked: QuotedAmount, QuotedAtUtc, ContractId (contrato vigente en asOf) y la moneda
    /// (cliente, si no contrato) solo si estaba vacía. No guarda: lo guarda el llamador (DECISIÓN 9/17).
    /// </summary>
    public async Task<decimal> ApplyQuoteAsync(TransportOrder order, Client client, DateOnly asOf, CancellationToken ct)
    {
        var computation = await ComputeAsync(order, client, asOf, ct);
        order.QuotedAmount = computation.Total;
        order.QuotedAtUtc = DateTime.UtcNow;
        if (computation.ContractId is int contractId) order.ContractId = contractId;
        if (order.CurrencyLookupId is null)
        {
            order.CurrencyLookupId = client.CurrencyLookupId;
            if (order.CurrencyLookupId is null && computation.ContractId is int cid)
                order.CurrencyLookupId = await db.Contracts.AsNoTracking().Where(c => c.ContractId == cid)
                    .Select(c => c.CurrencyLookupId).FirstOrDefaultAsync(ct);
        }
        return computation.Total;
    }
}
