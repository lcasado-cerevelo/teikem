using Teikem.Domain.ExitNotices;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 173 — la consulta de «Recolección y empaque por documento» contra SQL Server REAL: la unión de avisos de salida, órdenes de venta y envíos con sus subconsultas se traduce y devuelve
/// lo mismo que en memoria (InMemory no prueba la traducción a SQL). Base desechable (NotificationSqlFixture); se omite sin TEIKEM_TEST_SQLSERVER.
/// </summary>
public sealed class PickBatchDocumentSqlTests(NotificationSqlFixture fx) : IClassFixture<NotificationSqlFixture>
{
    [Fact]
    public async Task The_union_translates_to_sql_and_returns_progress_and_movements()
    {
        if (fx.ConnectionString is null) return;
        var tenantId = await fx.NewTenantAsync("docs-sql");
        await using (var seed = fx.NewDb(tenantId))
        {
            var notice = new ExitNotice
            {
                PublicId = Guid.NewGuid(), TenantId = tenantId, Number = "SAL-00001", WarehouseId = 1, ClientId = 1, RequestedDate = new DateOnly(2026, 10, 20), StatusCodeId = 1,
                CreatedAtUtc = new DateTime(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc),
            };
            notice.Lines.Add(new ExitNoticeLine { TenantId = tenantId, LineNo = 1, ProductId = 1, QtyOrdered = 10m, QtyReserved = 4m, QtyShipped = 6m });
            seed.Set<ExitNotice>().Add(notice);
            await seed.SaveChangesAsync();
            var batch = new PickBatch { PublicId = Guid.NewGuid(), TenantId = tenantId, WarehouseId = 1, Number = "EMP-00001", StatusCodeId = 1, CollectedAtUtc = new DateTime(2026, 10, 20, 15, 0, 0, DateTimeKind.Utc), ExitNoticeId = notice.ExitNoticeId };
            batch.Lines.Add(new PickBatchLine { ProductId = 1, FromBinId = 1, Quantity = 6m, IssueTxnId = 1 });
            seed.Set<PickBatch>().Add(batch);
            await seed.SaveChangesAsync();
        }

        var tenant = new Teikem.Infrastructure.Abstractions.TenantContext { TenantId = tenantId };
        await using var db = fx.NewDb(tenant);
        var svc = new PickBatchDocumentService(db, tenant);
        var page = await svc.ListAsync(new PickDocumentQuery(Search: "SAL-0000"), default);
        var doc = Assert.Single(page.Items);
        Assert.Equal(("SAL-00001", 10m, 4m, 6m, 1), (doc.Number, doc.Ordered, doc.Reserved, doc.Shipped, doc.BatchCount));
        Assert.Equal("EMP-00001", Assert.Single(doc.Batches).Number);
        Assert.Equal(1, (await svc.ListAsync(new PickDocumentQuery(PendingOnly: true), default)).Total);
        Assert.Empty((await svc.ListAsync(new PickDocumentQuery(Kind: PickBatchDocumentService.KindOrder), default)).Items);
    }
}
