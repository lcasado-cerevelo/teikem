using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Constants;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Resumen de recibos por almacén en una sola llamada (2026-10-11, GET /receipts/summary-by-warehouse): recibos abiertos (EXPECTED, RECEIVING, DISCREPANCY) y por acomodar
/// (RECEIVED, RECEIVED_VARIANCE) de todos los almacenes, los mismos totales que la lista con phase=OPEN / PENDING_PUTAWAY. Reemplaza las dos consultas por almacén de Ajustes de la compañía.
/// </summary>
public class ReceiptSummaryByWarehouseTests
{
    private static async Task AddReceiptAsync(ReceivingFixture f, int warehouseId, string status, bool active = true, int tenantId = ReceivingFixture.TenantId)
    {
        var id = await f.Db.StatusCodes.AsNoTracking().Where(s => s.Entity == StatusDomains.ReceiptStatus && s.InternalCode == status).Select(s => s.StatusCodeId).SingleAsync();
        var n = await f.Db.Set<ReceiptHeader>().IgnoreQueryFilters().CountAsync() + 1;
        f.Db.Set<ReceiptHeader>().Add(new ReceiptHeader
        {
            PublicId = Guid.NewGuid(), TenantId = tenantId, WarehouseId = warehouseId, Number = $"REC-{n:00000}", StatusCodeId = id, IsActive = active, CreatedAtUtc = DateTime.UtcNow,
        });
        await f.Db.SaveChangesAsync();
    }

    [Fact]
    public async Task Counts_open_and_pending_putaway_per_warehouse_in_one_call_and_matches_the_list()
    {
        await using var f = await ReceivingFixture.CreateAsync();
        var w2 = new Warehouse { WarehouseId = 20, PublicId = Guid.NewGuid(), TenantId = ReceivingFixture.TenantId, Code = "W2", Name = "Segundo", IsActive = true };
        var w3 = new Warehouse { WarehouseId = 30, PublicId = Guid.NewGuid(), TenantId = ReceivingFixture.TenantId, Code = "W3", Name = "Sin recibos", IsActive = false };
        f.Db.Set<Warehouse>().AddRange(w2, w3);
        await f.Db.SaveChangesAsync();

        // Almacén 10: 2 abiertos (EXPECTED y RECEIVING), 1 por acomodar (RECEIVED), 1 ya acomodado (PUTAWAY: no cuenta) y 1 abierto dado de baja (no cuenta).
        await AddReceiptAsync(f, f.WarehouseId, ReceiptStatuses.Expected);
        await AddReceiptAsync(f, f.WarehouseId, ReceiptStatuses.Receiving);
        await AddReceiptAsync(f, f.WarehouseId, ReceiptStatuses.Received);
        await AddReceiptAsync(f, f.WarehouseId, ReceiptStatuses.Putaway);
        await AddReceiptAsync(f, f.WarehouseId, ReceiptStatuses.Receiving, active: false);
        // Almacén 20: 1 abierto (DISCREPANCY) y 2 por acomodar (RECEIVED_VARIANCE y RECEIVED).
        await AddReceiptAsync(f, 20, ReceiptStatuses.Discrepancy);
        await AddReceiptAsync(f, 20, ReceiptStatuses.ReceivedWithVariance);
        await AddReceiptAsync(f, 20, ReceiptStatuses.Received);
        // De otro tenant: no se ve.
        await AddReceiptAsync(f, f.WarehouseId, ReceiptStatuses.Expected, tenantId: 99);

        var svc = f.Get<ReceiptService>();
        var summary = await svc.SummaryByWarehouseAsync(default);

        Assert.Equal(3, summary.Count);   // los tres almacenes de la compañía, también el inactivo
        var main = await f.Db.Set<Warehouse>().AsNoTracking().SingleAsync(w => w.WarehouseId == f.WarehouseId);
        Assert.Equal((2, 1), (summary.Single(s => s.WarehousePublicId == main.PublicId).OpenCount, summary.Single(s => s.WarehousePublicId == main.PublicId).PendingPutawayCount));
        Assert.Equal((1, 2), (summary.Single(s => s.WarehousePublicId == w2.PublicId).OpenCount, summary.Single(s => s.WarehousePublicId == w2.PublicId).PendingPutawayCount));
        var empty = summary.Single(s => s.WarehousePublicId == w3.PublicId);
        Assert.Equal((0, 0), (empty.OpenCount, empty.PendingPutawayCount));

        // Los mismos totales que la lista, almacén por almacén (lo que hacía la pantalla antes).
        foreach (var s in summary)
        {
            var open = await svc.ListAsync(new ReceiptQuery(WarehousePublicId: s.WarehousePublicId, Phase: "OPEN", Take: 1), default);
            var pending = await svc.ListAsync(new ReceiptQuery(WarehousePublicId: s.WarehousePublicId, Phase: "PENDING_PUTAWAY", Take: 1), default);
            Assert.Equal((open.Total, pending.Total), (s.OpenCount, s.PendingPutawayCount));
        }
    }

    [Fact]
    public async Task Without_any_receipt_every_warehouse_comes_back_with_zeros()
    {
        await using var f = await ReceivingFixture.CreateAsync();
        var summary = await f.Get<ReceiptService>().SummaryByWarehouseAsync(default);
        Assert.NotEmpty(summary);
        Assert.All(summary, s => Assert.Equal((0, 0), (s.OpenCount, s.PendingPutawayCount)));
    }
}
