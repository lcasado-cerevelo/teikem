using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Billing;
using Teikem.Domain.Orders;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 111c — entrega parcial y cobro (dictado por Luis, 2026-10-18): si el contrato cobra POR ENTREGA (o mixto), se cobra lo que se entregó y lo que no se entregó no se cobra; si cobra POR RECOGIDO, se cobra todo y el cliente reclama.
/// Para una orden por entrega que todavía no se facturó, el flete se cotiza solo con las piezas que siguen en la orden y no están perdidas, reclamadas ni resueltas (las «Por verificar» cuentan: pueden aparecer). Se recalcula cada vez que
/// cambia ese conjunto (marcar perdidas, mudar piezas a la orden nueva): las líneas quedan con las piezas cobrables y la orden se vuelve a cotizar. Sin servicio de cotización (pruebas) no hace nada.
/// </summary>
public sealed class PartialBillingService(TeikemDbContext db, ILookupCache lookups, OrderQuoteService? quotes = null, ITenantClock? clock = null)
{
    private readonly ITenantClock _clock = clock ?? TenantClock.Default;

    /// <summary>true si el contrato de la orden cobra por recogido (la orden se cobra completa).</summary>
    public async Task<bool> BillsByPickupAsync(int? contractId, CancellationToken ct)
    {
        if (contractId is not int id) return false;
        var modelId = await db.Contracts.AsNoTracking().Where(c => c.ContractId == id).Select(c => c.BillingModelLookupId).FirstOrDefaultAsync(ct);
        if (modelId is not int mid) return false;
        var code = (await lookups.GetAsync(mid, ct))?.InternalCode;
        return string.Equals(code, FreightRules.BillingModelPickup, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Recalcula lo cobrable de la orden por entrega (no facturada) según sus piezas y la vuelve a cotizar. Devuelve true si cambió algo.</summary>
    public async Task<bool> AdjustAsync(int orderId, CancellationToken ct)
    {
        if (quotes is null) return false;
        var order = await db.TransportOrders.FirstOrDefaultAsync(o => o.TransportOrderId == orderId, ct);
        if (order is null || order.FreightInvoiced || order.QuotedAmount is null) return false;
        if (await BillsByPickupAsync(order.ContractId, ct)) return false;
        var client = await db.Clients.AsNoTracking().FirstOrDefaultAsync(c => c.ClientId == order.ClientId, ct);
        if (client is null) return false;

        var pieces = await db.OrderPieces.AsNoTracking().Where(p => p.TransportOrderId == orderId && p.IsActive).ToListAsync(ct);
        if (pieces.Count == 0) return false;
        var lines = await db.CargoLines.Where(l => l.TransportOrderId == orderId).ToListAsync(ct);
        var changed = false;
        foreach (var l in lines.Where(l => pieces.Any(p => p.CargoLineId == l.CargoLineId)))
        {
            var billable = pieces.Count(p => p.CargoLineId == l.CargoLineId && p.MissingStatus is null or PieceMissing.ToVerify);
            var before = l.Quantity;
            if (billable == before) continue;
            var keep = before <= 0 ? 0m : billable / before;
            l.Quantity = billable;
            if (l.WeightKg is not null) l.WeightKg *= keep;
            if (l.VolumeM3 is not null) l.VolumeM3 *= keep;
            l.IsActive = billable > 0;
            changed = true;
        }
        if (!changed) return false;
        order.TotalPieces = (int)lines.Where(l => l.IsActive).Sum(l => l.Quantity);
        await db.SaveChangesAsync(ct);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(_clock.UtcNow, _clock.Zone));
        await quotes.ApplyQuoteAsync(order, client, today, ct);
        await db.SaveChangesAsync(ct);
        return true;
    }
}
