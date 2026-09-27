using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Constants;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Wms;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 6 (P8) — costura IPurchaseOrderReceiving: lo que la Recepción (P4) necesita de Compras, siempre DENTRO de la
/// transacción de la recepción.
/// - LockForReceiptAsync: bloquea la orden (UPDLOCK), exige SENT o PARTIAL (422) y algún pendiente (422 NothingPending),
///   y devuelve las líneas con pendiente = ordenado − recibido − resuelto (los faltantes ya resueltos no se vuelven a pedir).
/// - ApplyReceiptAsync: bloquea la orden (orden del lote: ReceiptHeader &lt; Asn &lt; PurchaseOrder), suma lo recibido por
///   línea y la avanza de forma escalonada a PARTIAL o RECEIVED (RECEIVED cuando ninguna línea queda con pendiente).
/// La orden se alcanza bajo el filtro de tenant: de otro tenant → 404.
/// </summary>
public sealed class PurchaseOrderReceivingService(TeikemDbContext db, StatusService statuses) : IPurchaseOrderReceiving
{
    public const string QuantityNegative = "La cantidad recibida contra la orden de compra no puede ser negativa.";

    public async Task<PurchaseOrderForReceipt> LockForReceiptAsync(Guid purchaseOrderPublicId, CancellationToken ct)
    {
        var poId = await db.Set<PurchaseOrder>().AsNoTracking()
                       .Where(p => p.PublicId == purchaseOrderPublicId && p.IsActive)
                       .Select(p => (int?)p.PurchaseOrderId).FirstOrDefaultAsync(ct)
                   ?? throw new NotFoundException(PurchaseOrderRules.NotFound, feminine: true);

        var po = await PurchasingSupport.LockPurchaseOrderAsync(db, poId, ct);
        if (!po.IsActive) throw new NotFoundException(PurchaseOrderRules.NotFound, feminine: true);
        var code = await PurchasingSupport.StatusCodeOfAsync(db, po.StatusCodeId, ct);
        EnsureReceivable(code);

        var lines = await db.Set<PurchaseOrderLine>().AsNoTracking().Where(l => l.PurchaseOrderId == po.PurchaseOrderId)
            .OrderBy(l => l.PurchaseOrderLineId).ToListAsync(ct);
        var resolved = await PurchasingSupport.ResolvedByLineAsync(db, new[] { po.PurchaseOrderId }, ct);
        var pending = lines
            .Select(l => new PurchaseOrderPendingLine(l.PurchaseOrderLineId, l.ProductId,
                ShortageRules.Pending(l.QtyOrdered, l.QtyReceived, resolved.GetValueOrDefault(l.PurchaseOrderLineId)), l.UnitCost))
            .Where(l => l.QtyPending > 0m)
            .ToList();
        if (pending.Count == 0) throw new StatusRuleException(PurchaseStatusRules.NothingPending);
        return new PurchaseOrderForReceipt(po.PurchaseOrderId, po.Number, po.WarehouseId, pending);
    }

    public async Task ApplyReceiptAsync(int purchaseOrderId, IReadOnlyList<PurchaseOrderReceiptQty> quantities, CancellationToken ct)
    {
        var po = await PurchasingSupport.LockPurchaseOrderAsync(db, purchaseOrderId, ct);
        if (!po.IsActive) throw new NotFoundException(PurchaseOrderRules.NotFound, feminine: true);
        var code = await PurchasingSupport.StatusCodeOfAsync(db, po.StatusCodeId, ct);
        EnsureReceivable(code);

        var lines = await db.Set<PurchaseOrderLine>().Where(l => l.PurchaseOrderId == po.PurchaseOrderId).ToListAsync(ct);
        foreach (var q in quantities ?? Array.Empty<PurchaseOrderReceiptQty>())
        {
            if (q.Quantity < 0m) throw new ValidationException("quantity", QuantityNegative);
            // La línea se busca DENTRO de la orden bloqueada: una línea de otra orden es 404.
            var line = lines.FirstOrDefault(l => l.PurchaseOrderLineId == q.PurchaseOrderLineId)
                       ?? throw new NotFoundException(ShortageRules.LineNotFound, feminine: true);
            line.QtyReceived += q.Quantity;
        }

        var resolved = await PurchasingSupport.ResolvedByLineAsync(db, new[] { po.PurchaseOrderId }, ct);
        var target = PurchaseOrderRules.TargetStatus(lines.Select(l => (l.QtyOrdered, l.QtyReceived, resolved.GetValueOrDefault(l.PurchaseOrderLineId))));
        await PurchasingSupport.AdvanceAsync(db, statuses, po, target, null, ct);
        await db.SaveGuardedAsync(PurchaseOrderService.ConcurrencyMessage, ct);
    }

    // ---------------------------------------------------------------- misma regla que PurchaseStatusRules.IsReceivable (P0)

    /// <summary>SENT o PARTIAL; si no → 422 'La orden de compra debe estar enviada o recibida parcial para recibir contra ella.'</summary>
    private static void EnsureReceivable(string statusCode)
    {
        if (!PurchaseOrderRules.IsReceivable(statusCode)) throw new StatusRuleException(PurchaseOrderRules.NotReceivable);
    }
}
