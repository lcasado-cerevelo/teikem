using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Constants;
using Teikem.Domain.Orders;
using Teikem.Domain.Sales;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 62 (P4, S2) — la ENTREGA de un despacho de una orden de venta: empaca la recolección (la misma operación del empaque de hoy: crea la orden de transporte real,
/// con servicio, paquetes y COD) y la enlaza con la orden de venta (TransportOrder.SalesOrderId). El cliente y el consignatario salen de la orden de venta si no se
/// mandan. El estado de la entrega vive en la orden de transporte. Un retiro no necesita nada: el cliente recoge y ahí termina.
/// </summary>
public sealed class SalesDeliveryService(TeikemDbContext db, PickBatchService pickBatches, SalesOrderService sales)
{
    /// <summary>Despacha y entrega en UNA transacción (si la entrega falla, no sale el inventario).</summary>
    public async Task<SalesDispatchResultDto> DispatchAndDeliverAsync(Guid publicId, SalesDispatchRequest req, CancellationToken ct)
    {
        if (req.Delivery is null) return await sales.DispatchAsync(publicId, req, ct);
        SalesDispatchResultDto? dispatched = null;
        await db.RunInTransactionAsync(async ct2 =>
        {
            dispatched = await sales.DispatchAsync(publicId, req with { Delivery = null }, ct2);
            await CreateAsync(publicId, new SalesDeliveryRequest(dispatched.PickBatchPublicId, req.Delivery), ct2);
        }, ct);
        return new SalesDispatchResultDto(dispatched!.PickBatchPublicId, dispatched.PickBatchNumber, await sales.GetAsync(publicId, ct));
    }

    public async Task<SalesOrderDto> CreateAsync(Guid publicId, SalesDeliveryRequest? req, CancellationToken ct)
    {
        if (req?.PickBatchPublicId is null) throw new ValidationException("pickBatchPublicId", SalesRules.DeliveryBatchRequired);
        if (req.Order is null) throw new ValidationException("order", SalesRules.DeliveryOrderRequired);

        var order = await db.Set<SalesOrder>().AsNoTracking().Where(o => o.PublicId == publicId)
            .Select(o => new { o.SalesOrderId, o.ClientId, o.ConsigneeId }).FirstOrDefaultAsync(ct) ?? throw new NotFoundException("Orden de venta", feminine: true);
        var batch = await db.Set<PickBatch>().AsNoTracking().Where(b => b.PublicId == req.PickBatchPublicId && b.SalesOrderId == order.SalesOrderId)
            .Select(b => new { b.PublicId, b.Number, b.StatusCodeId, b.IsActive }).FirstOrDefaultAsync(ct) ?? throw new NotFoundException(SalesRules.DeliveryBatchNotFound);
        var status = await db.StatusCodes.AsNoTracking().Where(s => s.StatusCodeId == batch.StatusCodeId).Select(s => s.InternalCode).FirstAsync(ct);
        if (!batch.IsActive || !string.Equals(status, PickBatchStatuses.Collected, StringComparison.OrdinalIgnoreCase))
            throw new StatusRuleException(SalesRules.DeliveryNotCollected(batch.Number));

        var buyer = await db.Clients.AsNoTracking().Where(c => c.ClientId == order.ClientId).Select(c => c.PublicId).FirstAsync(ct);
        Guid? consignee = order.ConsigneeId is int cid ? await db.Set<Teikem.Domain.Clients.Location>().AsNoTracking().Where(l => l.LocationId == cid).Select(l => (Guid?)l.PublicId).FirstOrDefaultAsync(ct) : null;
        var data = req.Order with
        {
            ClientPublicId = req.Order.ClientPublicId ?? buyer,
            ConsigneeLocationPublicId = req.Order.ConsigneeLocationPublicId ?? (req.Order.NewConsignee is null ? consignee : null),
        };

        // el empaque y el enlace con la orden de venta, juntos
        await db.RunInTransactionAsync(async ct2 =>
        {
            var packed = await pickBatches.PackAsync(batch.PublicId, new PickBatchPackRequest(data), ct2);
            var transport = await db.Set<TransportOrder>().AsTracking().FirstAsync(t => t.PublicId == packed.Order.PublicId, ct2);
            transport.SalesOrderId = order.SalesOrderId;
            await db.SaveGuardedAsync(SalesRules.Concurrency, ct2);
        }, ct);
        return await sales.GetAsync(publicId, ct);
    }
}
