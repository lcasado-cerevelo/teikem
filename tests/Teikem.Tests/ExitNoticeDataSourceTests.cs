using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Constants;
using Teikem.Domain.ExitNotices;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Analytics;
using Xunit;

namespace Teikem.Tests;

/// <summary>Menores (2026-10-21, autónomo) — EXIT_NOTICE como fuente de datos de Vistas, indicadores y gráficos: una fila por aviso con su avance, atraso y cumplimiento; lectura con inventory.view.</summary>
public sealed class ExitNoticeDataSourceTests
{
    [Fact]
    public void The_source_is_keyed_by_the_entity_type_needs_inventory_view_and_its_module()
    {
        Assert.Equal("EXIT_NOTICE", ExitNoticeCodes.EntityType);
        Assert.Equal(PermissionCatalog.InventoryView, PermissionCatalog.DataSourceReadPermission(ExitNoticeCodes.EntityType));
    }

    [Fact]
    public async Task One_row_per_notice_with_progress_late_and_fill_rate_only_when_shipped()
    {
        await using var f = await WmsFixture.CreateAsync(s => s.AddSingleton<ITenantClock>(new TenantClock(Teikem.Domain.Common.LocalDay.DefaultZone, () => new DateTime(2026, 10, 20, 15, 0, 0, DateTimeKind.Utc))));
        var w = await f.AddWarehouseAsync("W1");
        var client = await f.AddClientAsync("ASICS");
        var p = await f.AddProductAsync("MED", ownerClientId: client.ClientId);
        ExitNotice Notice(string number, string status, DateOnly? required, decimal ordered, decimal reserved, decimal shipped, decimal backordered, int? assignedTo = null)
        {
            var n = new ExitNotice
            {
                PublicId = Guid.NewGuid(), TenantId = WmsFixture.TenantId, Number = number, WarehouseId = w.WarehouseId, ClientId = client.ClientId, RequestedDate = new DateOnly(2026, 10, 1), RequiredDate = required,
                StatusCodeId = f.StatusId(ExitNoticeCodes.StatusDomain, status), AssignedToUserId = assignedTo, CreatedAtUtc = new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc),
            };
            n.Lines.Add(new ExitNoticeLine { TenantId = WmsFixture.TenantId, LineNo = 1, ProductId = p.ProductId, QtyOrdered = ordered, QtyReserved = reserved, QtyShipped = shipped, QtyBackordered = backordered });
            return n;
        }
        f.Db.Set<ExitNotice>().Add(Notice("SAL-00001", ExitNoticeStatuses.Partial, new DateOnly(2026, 10, 10), 10m, 3m, 5m, 2m, assignedTo: 7));   // abierto, atrasado, con pendiente
        f.Db.Set<ExitNotice>().Add(Notice("SAL-00002", ExitNoticeStatuses.Shipped, new DateOnly(2026, 10, 10), 8m, 0m, 6m, 0m));                    // despachado: no está atrasado, cumplimiento 75
        await f.Db.SaveChangesAsync();

        var src = new ExitNoticeDataSource(f.Db, f.Tenant, f.Get<ITenantClock>());
        var rows = (await src.LoadAsync(new DataQuery(), default)).OrderBy(r => (string)r["Number"]!).ToList();

        Assert.Equal(2, rows.Count);
        var open = rows[0];
        Assert.Equal(("SAL-00001", "Cliente ASICS", "W1", true, true, true, true), ((string)open["Number"]!, (string)open["ClientName"]!, (string)open["WarehouseCode"]!, (bool)open["IsOpen"]!, (bool)open["HasBackorder"]!, (bool)open["IsLate"]!, (bool)open["IsAssigned"]!));
        Assert.Equal((10m, 3m, 5m, 2m), ((decimal)open["UnitsOrdered"]!, (decimal)open["UnitsReserved"]!, (decimal)open["UnitsShipped"]!, (decimal)open["UnitsBackordered"]!));
        Assert.Null(open["FillRatePct"]);
        var shipped = rows[1];
        Assert.Equal((false, false, false), ((bool)shipped["IsOpen"]!, (bool)shipped["IsLate"]!, (bool)shipped["IsAssigned"]!));
        Assert.Equal(75m, (decimal)shipped["FillRatePct"]!);
        // q.Ids respeta el filtro
        var one = await src.LoadAsync(new DataQuery { Ids = new[] { (int)open["Id"]! } }, default);
        Assert.Single(one);
    }
}
