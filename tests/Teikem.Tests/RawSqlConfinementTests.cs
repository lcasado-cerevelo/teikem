using System.Text.RegularExpressions;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 5 / P0: el SQL crudo está confinado. FromSql*, SqlQuery* y ExecuteSql* solo aparecen en los archivos que lo
/// necesitan (bloqueos de fila, contadores, GEOGRAPHY y pings; Lote 6: InventoryQueries con los bloqueos del inventario), y
/// cada sentencia de TripQueries e InventoryQueries filtra explícitamente por 'TenantId =' (el SQL crudo no pasa por el filtro
/// global cuando no se compone sobre un DbSet).
/// </summary>
public class RawSqlConfinementTests
{
    private static readonly string[] Allowed = { "FleetQueries.cs", "NumberSequenceService.cs", "DriverPayPolicyService.cs", "TripQueries.cs", "InventoryQueries.cs" };

    private static readonly Regex RawSqlApi = new(@"\b(FromSql\w*|SqlQuery\w*|ExecuteSql\w*)\s*[<(]", RegexOptions.Compiled);

    private static IEnumerable<string> SourceFiles()
    {
        var src = Path.Combine(TripCatalogTests.RepoRoot(), "src");
        return Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));
    }

    [Fact]
    public void Raw_sql_only_lives_in_the_allowed_files()
    {
        var offenders = SourceFiles()
            .Where(f => RawSqlApi.IsMatch(File.ReadAllText(f)))
            .Select(Path.GetFileName)
            .Where(n => !Allowed.Contains(n))
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();
        Assert.True(offenders.Count == 0, "SQL crudo fuera de los archivos permitidos: " + string.Join(", ", offenders));
    }

    [Fact]
    public void Every_trip_queries_statement_filters_by_tenant()
    {
        var file = SourceFiles().Single(f => Path.GetFileName(f) == "TripQueries.cs");
        var text = File.ReadAllText(file);
        var statements = Regex.Matches(text, "\\$\"((?:SELECT|UPDATE)[^\"]*)\"").Select(m => m.Groups[1].Value).ToList();

        // Bloqueo de Trip, órdenes y Tenant; puntos; pin; pings de ruta y de chofer.
        Assert.Equal(7, statements.Count);
        Assert.All(statements, s => Assert.Contains("TenantId = {tenantId}", s));
        Assert.Contains(statements, s => s.StartsWith("SELECT * FROM dbo.Trip WITH (UPDLOCK, ROWLOCK)", StringComparison.Ordinal));
        Assert.Contains(statements, s => s.StartsWith("SELECT * FROM dbo.TransportOrder WITH (UPDLOCK, ROWLOCK)", StringComparison.Ordinal) && s.Contains("OPENJSON"));
        Assert.Contains(statements, s => s.StartsWith("SELECT TenantId AS Value FROM dbo.Tenant WITH (UPDLOCK, ROWLOCK)", StringComparison.Ordinal));
        Assert.Contains(statements, s => s.Contains("os.GeoPoint.Lat") && s.StartsWith("SELECT", StringComparison.Ordinal));
        Assert.Contains(statements, s => s.StartsWith("UPDATE os SET GeoPoint = geography::Point(", StringComparison.Ordinal));
        Assert.Contains(statements, s => s.Contains("ROW_NUMBER() OVER (PARTITION BY p.TripId"));
        Assert.Contains(statements, s => s.Contains("p.TripId IS NULL AND p.CapturedAtUtc >= k.StartUtc"));

        // Solo interpolación parametrizada: nada de FromSqlRaw/ExecuteSqlRaw con cadenas armadas a mano.
        Assert.DoesNotContain("SqlRaw", text);
    }

    [Fact]
    public void Every_inventory_queries_statement_filters_by_tenant()
    {
        var file = SourceFiles().Single(f => Path.GetFileName(f) == "InventoryQueries.cs");
        var text = File.ReadAllText(file);
        var statements = Regex.Matches(text, "\\$\"((?:SELECT|UPDATE|IF|INSERT|MERGE)[^\"]*)\"").Select(m => m.Groups[1].Value).ToList();

        // (1) upsert, (2) bloqueo por clave, (3-5) rangos, (6-14) encabezados, (15) muelle, (16) series, (17) lote, (18) renta (Lote 27).
        Assert.Equal(18, statements.Count);
        Assert.All(statements, s => Assert.Contains("TenantId = {tenantId}", s));
        Assert.Contains(statements, s => s.StartsWith("IF NOT EXISTS (SELECT 1 FROM dbo.StockBalance WITH (UPDLOCK, HOLDLOCK)", StringComparison.Ordinal) && s.Contains("INSERT INTO dbo.StockBalance"));
        Assert.Contains(statements, s => s.StartsWith("SELECT * FROM dbo.StockBalance WITH (UPDLOCK, ROWLOCK)", StringComparison.Ordinal));
        Assert.Equal(3, statements.Count(s => s.StartsWith("SELECT * FROM dbo.StockBalance WITH (UPDLOCK, HOLDLOCK)", StringComparison.Ordinal)));
        foreach (var (table, pk) in new[] { ("PickBatch", "PickBatchId"), ("CrossDockPlan", "CrossDockPlanId"), ("CycleCount", "CycleCountId"),
                     ("ReceiptHeader", "ReceiptHeaderId"), ("Asn", "AsnId"), ("PurchaseOrder", "PurchaseOrderId"), ("Product", "ProductId"),
                     ("Warehouse", "WarehouseId"), ("WarehouseTask", "WarehouseTaskId"), ("Rental", "RentalId") })
            Assert.Contains(statements, s => s.StartsWith($"SELECT {pk} AS Value FROM dbo.{table} WITH (UPDLOCK, ROWLOCK)", StringComparison.Ordinal));
        Assert.Contains(statements, s => s.StartsWith("SELECT d.WarehouseDockId AS Value FROM dbo.WarehouseDock d WITH (UPDLOCK, ROWLOCK) JOIN dbo.Warehouse w", StringComparison.Ordinal));
        Assert.Contains(statements, s => s.StartsWith("SELECT s.* FROM dbo.InventorySerial s WITH (UPDLOCK, ROWLOCK)", StringComparison.Ordinal) && s.Contains("OPENJSON"));
        Assert.Contains(statements, s => s.StartsWith("SELECT l.LotId AS Value FROM dbo.InventoryLot l WITH (UPDLOCK, HOLDLOCK)", StringComparison.Ordinal));
        // Warehouse.GeoPoint es GEOGRAPHY: nunca 'SELECT *' sobre el almacén.
        Assert.DoesNotContain(statements, s => s.Contains("SELECT * FROM dbo.Warehouse ", StringComparison.Ordinal));
        Assert.DoesNotContain("SqlRaw", text);
    }
}
