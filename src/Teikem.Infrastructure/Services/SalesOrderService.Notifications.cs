using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Clients;
using Teikem.Domain.Sales;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Notifications;
using Teikem.Infrastructure.Persistence;
using Teikem.Domain.Orders;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 66 (P4 S3) — avisos de las órdenes de venta. PublishAsync solo anota en la cola de avisos dentro de la transacción de quien llama: un aviso nunca deshace la operación
/// y una operación revertida no avisa nada. Sin publicador registrado (pruebas, importaciones) no hace nada.
/// </summary>
public sealed partial class SalesOrderService
{
    private async Task NotifyOrderAsync(string eventCode, SalesOrder order, IReadOnlyDictionary<string, string?> extra, string? dedupKey, CancellationToken ct)
    {
        if (notifications is null) return;
        var client = await db.Set<Client>().AsNoTracking().Where(c => c.ClientId == order.ClientId).Select(c => c.Name).FirstOrDefaultAsync(ct) ?? string.Empty;
        var warehouse = await db.Set<Warehouse>().AsNoTracking().Where(w => w.WarehouseId == order.WarehouseId).Select(w => w.Name).FirstOrDefaultAsync(ct) ?? string.Empty;
        var values = new Dictionary<string, string?> { ["orderNumber"] = order.Number, ["client"] = client, ["warehouse"] = warehouse };
        foreach (var kv in extra) values[kv.Key] = kv.Value;
        await notifications.PublishAsync(eventCode, values, dedupKey, ct);
    }

    private static string Qty(decimal v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>Aviso de orden solicitada (dentro de la transacción de Solicitar).</summary>
    private Task NotifyRequestedAsync(SalesOrder order, CancellationToken ct)
    {
        var backordered = order.Lines.Count(l => l.QtyBackordered > 0m);
        var detail = backordered == 0 ? "Quedó todo reservado." : $"{backordered} línea(s) con faltante en backorder.";
        return NotifyOrderAsync(SalesEventCodes.OrderRequested, order,
            new Dictionary<string, string?> { ["lineCount"] = order.Lines.Count.ToString(CultureInfo.InvariantCulture), ["detail"] = detail }, null, ct);
    }

    /// <summary>Aviso de crédito excedido: quien intentó solicitar no tiene (o no usó) la autorización. Una vez por orden y situación (no se repite por cada intento igual).</summary>
    private async Task NotifyCreditNeededAsync(SalesOrder order, CreditCheck check, CancellationToken ct)
    {
        if (notifications is null) return;
        var key = $"CR-{order.PublicId:N}-{Qty(check.Limit ?? 0m)}-{Qty(CreditRules.Exposure(check))}-{Qty(check.NewAmount)}";
        await NotifyOrderAsync(SalesEventCodes.CreditOverrideNeeded, order, new Dictionary<string, string?>
        {
            ["user"] = await CurrentUserNameAsync(ct),
            ["limit"] = CreditRules.Format(check.Limit ?? 0m),
            ["exposure"] = CreditRules.Format(CreditRules.Exposure(check)),
            ["amount"] = CreditRules.Format(check.NewAmount),
        }, key, ct);
        await db.SaveGuardedAsync(SalesRules.Concurrency, ct);   // aquí no hay transacción de negocio: el aviso se guarda solo, antes de rechazar la solicitud
    }

    /// <summary>
    /// Backorder que ya se puede surtir: por cada orden con alguna línea surtible (hay disponible para vender), un aviso con los productos. Lo corre el trabajo
    /// SALES_BACKORDER_SCAN cada pocos minutos, así que cubre TODAS las formas en que entra inventario (recibo, ajuste, transferencia, liberar cuarentena, cancelación de
    /// otra orden). No repite: la clave cambia solo si cambia lo surtible. Devuelve cuántas órdenes se avisaron.
    /// </summary>
    public async Task<int> NotifyFulfillableBackordersAsync(CancellationToken ct)
    {
        if (notifications is null) return 0;
        var lines = await BackordersAsync(null, ct);
        var sent = 0;
        foreach (var g in lines.Where(l => l.Fulfillable > 0m).GroupBy(l => l.OrderPublicId))
        {
            var first = g.First();
            var items = g.OrderBy(l => l.LineNo).ToList();
            var key = $"BO-{g.Key:N}-{items.Count}-{Qty(items.Sum(l => l.Fulfillable))}";
            var values = new Dictionary<string, string?>
            {
                ["orderNumber"] = first.OrderNumber, ["client"] = first.ClientName, ["warehouse"] = first.WarehouseName,
                ["lines"] = string.Join(", ", items.Select(l => $"{l.Sku} × {Qty(l.Fulfillable)}")),
                ["lineCount"] = items.Count.ToString(CultureInfo.InvariantCulture),
            };
            if (await notifications.PublishAsync(SalesEventCodes.BackorderAvailable, values, key, ct)) sent++;
        }
        if (sent > 0) await db.SaveGuardedAsync(SalesRules.Concurrency, ct);
        return sent;
    }
}
