using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Teikem.Domain.Orders;
using Teikem.Domain.Trips;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Persistence.Scripts;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 44 (F-G2) — el label de cada paquete lleva en el código de barras el NÚMERO DEL PAQUETE: GET /orders/lookup y la estación de
/// escaneo lo buscan como cuarta precedencia (después de número de orden, empaque y factura), solo en líneas activas y dentro de la
/// compañía; un número repetido entre órdenes (de clientes distintos) es ambiguo con su mensaje propio. Índice IX_CargoLine_PackageNumber
/// en la sección «2026-10-12 (e)» y espejo en EF. La lista de órdenes trae la dirección de entrega para los labels.
/// </summary>
public sealed class PackageLookupTests
{
    private static async Task SetPackageAsync(OrderReadFixture f, int orderId, string number, bool activeOnly = true)
    {
        var line = await f.Db.Set<CargoLine>().IgnoreQueryFilters().FirstAsync(l => l.TransportOrderId == orderId && (!activeOnly || l.IsActive));
        line.PackageNumber = number;
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
    }

    [Fact]
    public async Task Lookup_finds_the_order_by_package_number_as_fourth_precedence()
    {
        await using var f = await OrderReadFixture.CreateAsync();
        await SetPackageAsync(f, 1, "PQT-00012");
        var r = await f.Reader.LookupAsync(" PQT-00012 ", OrderScope.Any, default);
        Assert.Equal(OrderReadService.MatchedByPackage, r.MatchedBy);
        Assert.Equal(f.O1.PublicId, Assert.Single(r.Matches).PublicId);

        // Un número de orden, empaque o factura gana al número de paquete.
        await SetPackageAsync(f, 3, "2026-1");
        var byOrder = await f.Reader.LookupAsync("2026-1", OrderScope.Any, default);
        Assert.Equal(OrderReadService.MatchedByOrderNumber, byOrder.MatchedBy);
        Assert.Equal(f.O1.PublicId, Assert.Single(byOrder.Matches).PublicId);
    }

    [Fact]
    public async Task Inactive_lines_and_other_companies_do_not_match()
    {
        await using var f = await OrderReadFixture.CreateAsync();
        var inactive = await f.Db.Set<CargoLine>().FirstAsync(l => l.TransportOrderId == 2 && !l.IsActive);
        inactive.PackageNumber = "PQT-VIEJA";
        var foreign = await f.Db.Set<CargoLine>().IgnoreQueryFilters().FirstAsync(l => l.TransportOrderId == 5);
        foreign.PackageNumber = "PQT-AJENA";
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        Assert.Empty((await f.Reader.LookupAsync("PQT-VIEJA", OrderScope.Any, default)).Matches);
        Assert.Empty((await f.Reader.LookupAsync("PQT-AJENA", OrderScope.Any, default)).Matches);
    }

    [Fact]
    public async Task The_same_package_number_in_two_orders_is_ambiguous_with_its_own_message()
    {
        await using var f = await OrderReadFixture.CreateAsync();
        await SetPackageAsync(f, 1, "PQT-00001");
        await SetPackageAsync(f, 3, "PQT-00001");   // otro cliente con el mismo consecutivo
        var r = await f.Reader.LookupAsync("PQT-00001", OrderScope.Any, default);
        Assert.Equal(OrderReadService.MatchedByPackage, r.MatchedBy);
        Assert.Equal(2, r.Matches.Count);

        var d = OutboundScanRules.Decide(new ScanFacts(2, r.MatchedBy, null, null, null, null, null, new DateOnly(2026, 10, 12)));
        Assert.Equal(ScanOutcomes.NotFound, d.Outcome);
        Assert.Equal(ScanReasonCodes.MultipleMatches, d.ReasonCode);
        Assert.Equal("El número de paquete está en 2 órdenes (de clientes distintos o repetido); escanee el empaque o el número de orden.", d.Message);
        // con los demás códigos el mensaje de siempre
        Assert.Equal(OutboundScanRules.MultipleMatchesMessage,
            OutboundScanRules.Decide(new ScanFacts(2, OrderReadService.MatchedByInvoice, null, null, null, null, null, new DateOnly(2026, 10, 12))).Message);
    }

    [Fact]
    public async Task List_items_bring_the_delivery_address_for_labels()
    {
        await using var f = await OrderReadFixture.CreateAsync();
        var r = await f.Reader.LookupAsync("EMP-1", OrderScope.Any, default);
        var item = Assert.Single(r.Matches);
        Assert.Equal("Calle 1", item.ConsigneeLine1);
        Assert.Null(item.ConsigneeLine2);
        var tail = typeof(OrderListItemDto).GetConstructors().Single().GetParameters().SkipLast(2).TakeLast(3).Select(p => p.Name).ToArray();   // Lote 97: WaybillNumber va al final; Lote 128: InvoiceProcessedAtUtc después
        Assert.Equal(new[] { "ConsigneeLine1", "ConsigneeLine2", "ConsigneeState" }, tail);
    }

    [Fact]
    public void Update_sql_adds_the_package_number_index_in_a_new_section_and_ef_mirrors_it()
    {
        var sql = File.ReadAllText(Path.Combine(DatabaseInitializer.ResolveRepoRoot(null), "Diseño", "logistica-db-update.sql")).Replace("\r\n", "\n");
        var start = sql.IndexOf("2026-10-12 (e) — Escaneo por número de paquete (Lote 44, F-G2).", StringComparison.Ordinal);
        Assert.True(start > sql.IndexOf("2026-10-12 (d) — Impresoras y plantillas de label", StringComparison.Ordinal), "La sección (e) debe ir después de la (d).");
        // solo esta sección: otras posteriores (de cualquier rama) pueden llevar UNIQUE
        var next = sql.IndexOf("\n/* ----", start, StringComparison.Ordinal);
        var section = next < 0 ? sql[start..] : sql[start..next];
        Assert.Contains("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_CargoLine_PackageNumber' AND object_id = OBJECT_ID('dbo.CargoLine'))\n"
                        + "    CREATE INDEX IX_CargoLine_PackageNumber ON dbo.CargoLine(PackageNumber) INCLUDE (TransportOrderId)\n"
                        + "        WHERE IsActive = 1 AND PackageNumber IS NOT NULL;\nGO", section);
        Assert.DoesNotContain("DELETE", section, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UNIQUE", section, StringComparison.Ordinal);

        using var db = TenantIsolationModelTests.CreateSqlServerModelContext();
        var index = db.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(CargoLine))!.GetIndexes().Single(i => i.GetDatabaseName() == "IX_CargoLine_PackageNumber");
        Assert.False(index.IsUnique);
        Assert.Equal(new[] { nameof(CargoLine.PackageNumber) }, index.Properties.Select(p => p.Name));
        Assert.Equal("[IsActive] = 1 AND [PackageNumber] IS NOT NULL", index.GetFilter());
        Assert.Equal(new[] { nameof(CargoLine.TransportOrderId) }, index.GetIncludeProperties());
    }
}
