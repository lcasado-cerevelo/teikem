using System.Text.RegularExpressions;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 5 / P0: el SQL crudo está confinado. FromSql*, SqlQuery* y ExecuteSql* solo aparecen en los cuatro archivos que lo
/// necesitan (bloqueos de fila, contadores, GEOGRAPHY y pings), y cada sentencia de TripQueries filtra explícitamente por
/// 'TenantId =' (el SQL crudo no pasa por el filtro global cuando no se compone sobre un DbSet).
/// </summary>
public class RawSqlConfinementTests
{
    private static readonly string[] Allowed = { "FleetQueries.cs", "NumberSequenceService.cs", "DriverPayPolicyService.cs", "TripQueries.cs" };

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
}
