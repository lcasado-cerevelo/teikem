using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Constants;
using Teikem.Domain.Sales;
using Teikem.Infrastructure.Analytics;
using Teikem.Infrastructure.Contracts;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 249 (P5) — fuente SALES_ORDER_CHANGE: contrato (clave, campos, DateField RegisteredAtUtc, módulo y relación con SALES_ORDER), permiso de lectura sales.view,
/// filas con la diferencia y los contadores, q.Ids y el rango; y los campos nuevos de SALES_ORDER (IsAddition, ParentOrderNumber) y SALES_ORDER_LINE (IsRemoved,
/// TaxReason, HasTaxOverride).
/// </summary>
public sealed class SalesOrderChangeDataSourceTests
{
    private static SalesOrderChangeDataSource Source(SalesChangeWorld x) => new(x.F.Db, x.F.Lookups, x.F.Tenant);

    [Fact]
    public async Task Contract_date_field_module_relation_and_read_permission()
    {
        await using var x = await ChangeKit.WorldAsync();
        var s = Source(x);
        Assert.Equal((SalesChangeCodes.EntityType, (string?)null, "RegisteredAtUtc", "Id", ModuleKeys.SalesOrders), (s.Key, s.EntityTypeCode, s.DateField, s.IdField, s.TenantModule));
        Assert.Equal(new[]
        {
            "Id", "PublicId", "OrderId", "OrderNumber", "ChangeNo", "Kind", "Status", "StatusCode", "ClientName", "RegisteredByName", "AuthorizedByName", "AuthorizedByTitle",
            "Channel", "AuthorizedAtUtc", "Reason", "TotalBefore", "TotalAfter", "TotalDelta", "LinesAdded", "LinesRemoved", "LinesChanged", "HasPriceChange",
            "HasTaxOverride", "HasTermsChange", "ReopenedPack", "ReversedBatchCount", "CreditOverride", "RequiresApproval", "DecidedByName", "DecidedAtUtc",
            "AdditionOrderNumber", "RegisteredAtUtc",
        }, s.Fields.Select(f => f.Key));
        Assert.Contains(s.Fields, f => f.Key == s.DateField && f.Type == DataFieldType.Date);
        Assert.All(new[] { "TotalBefore", "TotalAfter", "TotalDelta" }, k => Assert.True(s.Fields.Single(f => f.Key == k).IsMoney));
        var rel = Assert.Single(s.Relations);
        Assert.Equal(("Order", SalesCodes.EntityType, "OrderId"), (rel.Key, rel.TargetSourceKey, rel.LocalField));
        Assert.Equal(PermissionCatalog.SalesView, PermissionCatalog.DataSourceExtraReadPermission[SalesChangeCodes.EntityType]);

        Assert.Contains(new SalesOrderDataSource(x.F.Db, x.F.Lookups, x.F.Tenant).Fields, f => f.Key == "IsAddition" && f.Type == DataFieldType.Bool);
        Assert.Contains(new SalesOrderDataSource(x.F.Db, x.F.Lookups, x.F.Tenant).Fields, f => f.Key == "ParentOrderNumber");
        var lineFields = new SalesOrderLineDataSource(x.F.Db, x.F.Tenant).Fields.Select(f => f.Key).ToList();
        Assert.Contains("IsRemoved", lineFields);
        Assert.Contains("TaxReason", lineFields);
        Assert.Contains("HasTaxOverride", lineFields);
    }

    [Fact]
    public async Task Rows_carry_the_delta_the_counters_the_labels_and_honor_ids_and_range()
    {
        await using var x = await ChangeKit.WorldAsync();
        var o = await x.RequestedAsync((x.P1, 3m), (x.P2, 2m));
        var svc = ChangeKit.Service(x);
        var change = await svc.CreateAsync(o.PublicId, ChangeKit.Req(ChangeKit.Qty(o.Lines[0].Id, 5m), new SalesOrderChangeLineOp(ChangeOpKinds.Remove, o.Lines[1].Id)), default);
        var addition = await svc.CreateAdditionAsync(o.PublicId, ChangeKit.Addition((x.P3, 2m)), default);

        var rows = await Source(x).LoadAsync(new DataQuery(), default);
        Assert.Equal(2, rows.Count);
        var r = rows.Single(row => (string?)row["PublicId"] == change.Item.PublicId.ToString());
        Assert.Equal((o.Number, 1, SalesChangeKinds.Modify, SalesOrderChangeStatuses.Applied, x.C.Name),
            ((string)r["OrderNumber"]!, (int)r["ChangeNo"]!, (string)r["Kind"]!, (string)r["StatusCode"]!, (string)r["ClientName"]!));
        Assert.Equal((46m, 50m, 4m, 0, 1, 1), ((decimal)r["TotalBefore"]!, (decimal)r["TotalAfter"]!, (decimal)r["TotalDelta"]!, (int)r["LinesAdded"]!,
            (int)r["LinesRemoved"]!, (int)r["LinesChanged"]!));
        Assert.Equal(("Por teléfono", ChangeKit.AuthorizedBy, false), ((string)r["Channel"]!, (string)r["AuthorizedByName"]!, (bool)r["RequiresApproval"]!));
        var a = rows.Single(row => (string?)row["PublicId"] == addition.Item.PublicId.ToString());
        Assert.Equal((SalesChangeKinds.Addition, addition.Item.AdditionNumber, 10m), ((string)a["Kind"]!, (string?)a["AdditionOrderNumber"], (decimal)a["TotalDelta"]!));

        var id = (int)r["Id"]!;
        Assert.Equal(id, (int)Assert.Single(await Source(x).LoadAsync(new DataQuery { Ids = new[] { id } }, default))["Id"]!);
        Assert.Empty(await Source(x).LoadAsync(new DataQuery { FromUtc = DateTime.UtcNow.AddDays(1) }, default));
        Assert.Equal(2, (await Source(x).LoadAsync(new DataQuery { FromUtc = DateTime.UtcNow.AddDays(-1), ToUtc = DateTime.UtcNow.AddDays(1) }, default)).Count);

        // SALES_ORDER marca la adición y su madre; SALES_ORDER_LINE marca la quitada
        var orders = await new SalesOrderDataSource(x.F.Db, x.F.Lookups, x.F.Tenant).LoadAsync(new DataQuery(), default);
        var child = orders.Single(row => (string)row["Number"]! == addition.Item.AdditionNumber);
        Assert.Equal((true, o.Number), ((bool)child["IsAddition"]!, (string?)child["ParentOrderNumber"]));
        Assert.False((bool)orders.Single(row => (string)row["Number"]! == o.Number)["IsAddition"]!);
        var lines = await new SalesOrderLineDataSource(x.F.Db, x.F.Tenant).LoadAsync(new DataQuery(), default);
        var removed = lines.Single(row => (int)row["Id"]! == o.Lines[1].Id);
        Assert.Equal((true, false), ((bool)removed["IsRemoved"]!, (bool)removed["HasTaxOverride"]!));
        Assert.NotNull(await x.F.Db.Set<SalesOrderChange>().AsNoTracking().FirstOrDefaultAsync());
    }
}
