using Teikem.Domain.Orders;
using Teikem.Infrastructure.Persistence.Scripts;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 38 (F-B): el espejo SQL de la importación de consignatarios vive en una sección nueva al final de
/// logistica-db-update.sql (después de la «(e)» de rotación): ImportBatch.ClientId pasa a NULL con guarda, el índice
/// IX_ImportBatch_Client se recrea si falta y CK_ImportBatch_OrderClient conserva la regla de órdenes. El modelo EF coincide.
/// </summary>
public sealed class ConsigneeImportSqlTests
{
    [Fact]
    public void Update_sql_makes_import_batch_client_nullable_in_a_new_idempotent_section_at_the_end()
    {
        var sql = File.ReadAllText(Path.Combine(DatabaseInitializer.ResolveRepoRoot(null), "Diseño", "logistica-db-update.sql")).Replace("\r\n", "\n");
        var start = sql.IndexOf("2026-10-11 (f) — Importación CSV de consignatarios (Lote 38, F-B)", StringComparison.Ordinal);
        Assert.True(start > 0);
        Assert.True(start > sql.IndexOf("2026-10-11 (e) — Rotacion del inventario FEFO / FIFO configurable", StringComparison.Ordinal));
        var section = UpdateSqlSection.From(sql, start);

        Assert.Contains("IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.ImportBatch') AND name = 'ClientId' AND is_nullable = 0)", section);
        Assert.Contains("DROP INDEX IX_ImportBatch_Client ON dbo.ImportBatch;", section);
        Assert.Contains("ALTER TABLE dbo.ImportBatch ALTER COLUMN ClientId INT NULL;", section);
        Assert.Contains("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ImportBatch_Client' AND object_id = OBJECT_ID('dbo.ImportBatch'))\n    CREATE INDEX IX_ImportBatch_Client ON dbo.ImportBatch(TenantId, ClientId);", section);
        Assert.Contains("IF OBJECT_ID('dbo.CK_ImportBatch_OrderClient', 'C') IS NULL", section);
        Assert.Contains("CHECK (Kind <> 'ORDER' OR ClientId IS NOT NULL);", section);
        Assert.DoesNotContain("DELETE", section, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Ef_model_maps_import_batch_client_as_optional()
    {
        await using var f = await WmsFixture.CreateAsync();
        var entity = f.Db.Model.FindEntityType(typeof(ImportBatch))!;
        Assert.True(entity.FindProperty(nameof(ImportBatch.ClientId))!.IsNullable);
        var fk = entity.GetForeignKeys().Single(k => k.Properties.Single().Name == nameof(ImportBatch.ClientId));
        Assert.False(fk.IsRequired);
        Assert.Equal(ImportKinds.Consignee, "CONSIGNEE");
    }
}
