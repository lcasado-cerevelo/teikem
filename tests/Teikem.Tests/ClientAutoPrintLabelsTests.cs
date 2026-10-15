using Teikem.Domain.Clients;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Persistence.Scripts;
using Xunit;

namespace Teikem.Tests;

// Lote 48 (2026-10-14): bandera por cliente «Imprimir los labels automáticamente» (Client.AutoPrintLabels) y punto de recogido por defecto que
// puede ser la dirección física o la postal. Lógica pura y texto de la sección de update.sql; el recorrido real va en smoke.sh.
public class ClientAutoPrintLabelsTests
{
    [Fact]
    public void The_flag_is_off_by_default_in_the_entity_the_detail_and_the_profile_request()
    {
        Assert.False(new Client().AutoPrintLabels);
        var detail = typeof(ClientDetailDto).GetConstructors().Single().GetParameters().Single(p => p.Name == "AutoPrintLabels");
        Assert.True(detail.HasDefaultValue);
        Assert.Equal(false, detail.DefaultValue);
        var request = new ClientProfileUpdateRequest(null, null, null, null, null, null, false, null);
        Assert.Null(request.AutoPrintLabels); // null = no cambia
        Assert.Null(request.AutoPrintInvoice);
    }

    [Fact]
    public void Update_sql_adds_the_column_in_a_new_idempotent_section_at_the_end_with_the_flag_off_by_default()
    {
        var sql = File.ReadAllText(Path.Combine(DatabaseInitializer.ResolveRepoRoot(null), "Diseño", "logistica-db-update.sql")).Replace("\r\n", "\n");
        var start = sql.IndexOf("2026-10-14 (d) — «Imprimir los labels automáticamente» por cliente (Lote 48)", StringComparison.Ordinal);
        Assert.True(start > sql.IndexOf("2026-10-14 (b)", StringComparison.Ordinal), "Falta la sección 2026-10-14 (d) después de la (b).");
        var section = SqlSections.From(sql, start);
        Assert.Contains("IF COL_LENGTH('dbo.Client', 'AutoPrintLabels') IS NULL\n    ALTER TABLE dbo.Client ADD AutoPrintLabels BIT NOT NULL CONSTRAINT DF_Client_AutoPrintLabels DEFAULT 0;", section);
        Assert.DoesNotContain("DROP ", section, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE", section, StringComparison.OrdinalIgnoreCase);
    }
}
