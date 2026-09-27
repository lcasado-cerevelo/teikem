using System.Text.RegularExpressions;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 6 / P0 (D2): UNA sola vía de escritura del inventario. Por texto sobre src/:
/// - QtyOnHand y QtyReserved de StockBalance, InventoryTransaction, la ubicación actual y el estatus de InventorySerial solo se
///   escriben en Wms/InventoryLedger.cs;
/// - el ledger es de solo inserción: nadie hace Remove/Update de InventoryTransaction ni asigna su RefId después (no hay SetRef);
/// - nadie lee las columnas computadas QtyAvailable, VarianceQty ni LineTotal en la lógica (InMemory no las calcula).
/// </summary>
public class WmsWriteConfinementTests
{
    private const string LedgerFile = "InventoryLedger.cs";

    private static IEnumerable<(string Name, string Text)> SourceFiles()
    {
        var src = Path.Combine(TripCatalogTests.RepoRoot(), "src");
        return Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(f => (Path.GetFileName(f), StripComments(File.ReadAllText(f))));
    }

    /// <summary>Quita comentarios de línea y de documentación (los /// citan nombres de columnas).</summary>
    private static string StripComments(string text) => Regex.Replace(text, @"//[^\n]*", string.Empty);

    private static List<string> Offenders(Regex pattern, params string[] allowed)
        => SourceFiles().Where(f => pattern.IsMatch(f.Text)).Select(f => f.Name).Where(n => !allowed.Contains(n)).Distinct().OrderBy(n => n, StringComparer.Ordinal).ToList();

    [Fact]
    public void Stock_balance_quantities_are_written_only_by_the_ledger()
    {
        var offenders = Offenders(new Regex(@"\b(QtyOnHand|QtyReserved)\s*(\+|-)?=(?![=>])"), LedgerFile);
        Assert.True(offenders.Count == 0, "Escritura de saldos fuera del ledger: " + string.Join(", ", offenders));
        Assert.Empty(Offenders(new Regex(@"new\s+StockBalance\b"), LedgerFile));
    }

    [Fact]
    public void Inventory_transactions_are_inserted_only_by_the_ledger_and_never_updated()
    {
        Assert.Empty(Offenders(new Regex(@"new\s+InventoryTransaction\b"), LedgerFile));
        Assert.Empty(Offenders(new Regex(@"InventoryTransactions\s*\.\s*(Add|AddRange|Update|Remove|RemoveRange)\b|Set<InventoryTransaction>\(\)\s*\.\s*(Add|AddRange|Update|Remove|RemoveRange)\b"), LedgerFile));
        // Ni siquiera el ledger borra o actualiza movimientos.
        Assert.Empty(Offenders(new Regex(@"InventoryTransactions\s*\.\s*(Update|Remove|RemoveRange)\b|Set<InventoryTransaction>\(\)\s*\.\s*(Update|Remove|RemoveRange)\b")));
        Assert.Empty(Offenders(new Regex(@"\bSetRef\w*\s*\(")));
        // El RefId de un movimiento solo se fija en su construcción dentro del ledger.
        var ledger = SourceFiles().Single(f => f.Name == LedgerFile).Text;
        Assert.Single(Regex.Matches(ledger, @"\bRefId\s*=\s*p\.RefId\b"));
    }

    [Fact]
    public void Serial_location_and_status_are_written_only_by_the_ledger()
    {
        Assert.Empty(Offenders(new Regex(@"\b(CurrentBinId|CurrentWarehouseId)\s*=(?![=>])"), LedgerFile));
        Assert.Empty(Offenders(new Regex(@"new\s+InventorySerial\b|InventorySerials\s*\.\s*(Add|AddRange|Update|Remove)\b|Set<InventorySerial>\(\)\s*\.\s*(Add|AddRange|Update|Remove)\b"), LedgerFile));
        Assert.Empty(Offenders(new Regex(@"TransitionAsync\s*\(\s*StatusDomains\.SerialStatus"), LedgerFile));
    }

    [Fact]
    public void Computed_columns_are_never_read_by_the_logic()
    {
        // Solo las configuraciones EF las mencionan (HasComputedColumnSql); RateService.LineTotal es otro tipo (cotización, Lote 2).
        var offenders = Offenders(new Regex(@"\.(QtyAvailable|VarianceQty)\b"), "InventoryConfigurations.cs", "WmsDocumentConfigurations.cs");
        Assert.True(offenders.Count == 0, "Lectura de columnas computadas: " + string.Join(", ", offenders));
        var lineTotal = SourceFiles().Where(f => f.Text.Contains("PurchaseOrderLine", StringComparison.Ordinal) && Regex.IsMatch(f.Text, @"\.LineTotal\b"))
            .Select(f => f.Name).Where(n => n != "WmsDocumentConfigurations.cs").ToList();
        Assert.Empty(lineTotal);
    }
}
