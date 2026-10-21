using Teikem.Domain.Wms;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 182 — la lista y la ficha de la recepción maestra contra SQL Server REAL (la búsqueda por número de aviso u orden de compra de uno de sus documentos usa subconsultas anidadas que InMemory no prueba).
/// Base desechable (NotificationSqlFixture); se omite sin TEIKEM_TEST_SQLSERVER.
/// </summary>
public sealed class ReceiptGroupSqlTests(NotificationSqlFixture fx) : IClassFixture<NotificationSqlFixture>
{
    [Fact]
    public async Task List_and_get_translate_to_sql_and_find_a_group_by_the_number_of_one_of_its_documents()
    {
        if (fx.ConnectionString is null) return;
        var tenantId = await fx.NewTenantAsync("receipt-groups-sql");
        Guid groupId;
        await using (var seed = fx.NewDb(tenantId))
        {
            var asn = new Asn { TenantId = tenantId, Number = "LLE-00077", WarehouseId = 1, StatusCodeId = 1, IsActive = true, CreatedAtUtc = DateTime.UtcNow };
            seed.Set<Asn>().Add(asn);
            var group = new ReceiptGroup { PublicId = Guid.NewGuid(), TenantId = tenantId, WarehouseId = 1, Number = "RCM-00099", CreatedAtUtc = DateTime.UtcNow, IsActive = true };
            seed.Set<ReceiptGroup>().Add(group);
            await seed.SaveChangesAsync();
            groupId = group.PublicId;
            var receipt = new ReceiptHeader { PublicId = Guid.NewGuid(), TenantId = tenantId, WarehouseId = 1, AsnId = asn.AsnId, ReceiptTypeLookupId = 1, Number = "REC-00098", StatusCodeId = 1, IsActive = true, ReceiptGroupId = group.ReceiptGroupId, CreatedAtUtc = DateTime.UtcNow };
            receipt.Lines.Add(new ReceiptLine { ProductId = 1, ExpectedQty = 3m, ReceivedQty = 3m });
            seed.Set<ReceiptHeader>().Add(receipt);
            await seed.SaveChangesAsync();
        }

        var tenant = new Teikem.Infrastructure.Abstractions.TenantContext { TenantId = tenantId };
        await using var db = fx.NewDb(tenant);
        var svc = new ReceiptGroupService(db, tenant, null!, null!);
        var page = await svc.ListAsync(new ReceiptGroupQuery(Search: "LLE-00077"), default);
        var item = Assert.Single(page.Items);
        Assert.Equal(("RCM-00099", "OPEN", 1, 3m), (item.Number, item.StatusCode, item.MemberCount, item.ExpectedQty));
        Assert.Equal("LLE-00077", Assert.Single(item.Members).OriginRef);
        Assert.Equal(1, (await svc.ListAsync(new ReceiptGroupQuery(Status: "OPEN"), default)).Total);
        Assert.Equal(0, (await svc.ListAsync(new ReceiptGroupQuery(Status: "CONFIRMED"), default)).Total);
        Assert.Empty((await svc.ListAsync(new ReceiptGroupQuery(Search: "NO-EXISTE"), default)).Items);
        Assert.Equal("REC-00098", Assert.Single((await svc.GetAsync(groupId, default)).Members).ReceiptNumber);
    }
}
