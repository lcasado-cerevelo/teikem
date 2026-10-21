using Teikem.Domain.ExitNotices;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 176 — la lista y la ficha de la recolección maestra contra SQL Server REAL (la búsqueda por número de documento usa subconsultas que InMemory no prueba). Base desechable
/// (NotificationSqlFixture); se omite sin TEIKEM_TEST_SQLSERVER.
/// </summary>
public sealed class PickGroupSqlTests(NotificationSqlFixture fx) : IClassFixture<NotificationSqlFixture>
{
    [Fact]
    public async Task List_and_get_translate_to_sql_and_find_a_master_by_the_number_of_one_of_its_documents()
    {
        if (fx.ConnectionString is null) return;
        var tenantId = await fx.NewTenantAsync("groups-sql");
        Guid groupId;
        await using (var seed = fx.NewDb(tenantId))
        {
            var notice = new ExitNotice
            {
                PublicId = Guid.NewGuid(), TenantId = tenantId, Number = "SAL-00077", WarehouseId = 1, ClientId = 1, RequestedDate = new DateOnly(2026, 10, 20), StatusCodeId = 1,
                CreatedAtUtc = new DateTime(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc),
            };
            notice.Lines.Add(new ExitNoticeLine { TenantId = tenantId, LineNo = 1, ProductId = 1, QtyOrdered = 3m, QtyReserved = 0m, QtyShipped = 3m });
            seed.Set<ExitNotice>().Add(notice);
            var group = new PickGroup { PublicId = Guid.NewGuid(), TenantId = tenantId, WarehouseId = 1, ClientId = 1, Number = "EMP-00099", CollectedAtUtc = new DateTime(2026, 10, 20, 15, 0, 0, DateTimeKind.Utc) };
            seed.Set<PickGroup>().Add(group);
            await seed.SaveChangesAsync();
            groupId = group.PublicId;
            var batch = new PickBatch { PublicId = Guid.NewGuid(), TenantId = tenantId, WarehouseId = 1, Number = "EMP-00098", StatusCodeId = 1, CollectedAtUtc = group.CollectedAtUtc, ExitNoticeId = notice.ExitNoticeId, PickGroupId = group.PickGroupId };
            batch.Lines.Add(new PickBatchLine { ProductId = 1, FromBinId = 1, Quantity = 3m, IssueTxnId = 1 });
            seed.Set<PickBatch>().Add(batch);
            await seed.SaveChangesAsync();
        }

        var tenant = new Teikem.Infrastructure.Abstractions.TenantContext { TenantId = tenantId };
        await using var db = fx.NewDb(tenant);
        var svc = new PickGroupService(db, tenant, null!, null!, null!, null!, null!, null!, null!, null!);
        var page = await svc.ListAsync(new PickGroupQuery(Search: "SAL-00077"), default);
        var item = Assert.Single(page.Items);
        Assert.Equal(("EMP-00099", 3m, "COLLECTED"), (item.Number, item.TotalQty, item.StatusCode));
        Assert.Equal("SAL-00077", Assert.Single(item.Documents).Number);
        Assert.Equal(1, (await svc.ListAsync(new PickGroupQuery(Status: "COLLECTED"), default)).Total);
        Assert.Empty((await svc.ListAsync(new PickGroupQuery(Search: "NO-EXISTE"), default)).Items);
        var detail = await svc.GetAsync(groupId, default);
        Assert.Equal(3m, Assert.Single(detail.Movements).Quantity);
    }
}
