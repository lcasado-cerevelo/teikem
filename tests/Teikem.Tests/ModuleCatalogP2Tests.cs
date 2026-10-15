using Teikem.Domain.Constants;
using Teikem.Infrastructure.Persistence.Scripts;
using Xunit;

namespace Teikem.Tests;

// Lote 49 (P2.3): módulos SALES_ORDERS, TAX y BILLING. Constantes y sección de update.sql (idempotente, solo los define; nada encendido).
public class ModuleCatalogP2Tests
{
    private static string Sql() => File.ReadAllText(Path.Combine(DatabaseInitializer.ResolveRepoRoot(null), "Diseño", "logistica-db-update.sql")).Replace("\r\n", "\n");

    [Fact]
    public void The_module_keys_exist_with_the_exact_codes()
    {
        Assert.Equal("SALES_ORDERS", ModuleKeys.SalesOrders);
        Assert.Equal("TAX", ModuleKeys.Tax);
        Assert.Equal("BILLING", ModuleKeys.Billing);
    }

    [Fact]
    public void Update_sql_defines_the_three_modules_with_their_dependencies_in_a_new_idempotent_section_at_the_end()
    {
        var sql = Sql();
        var start = sql.IndexOf("2026-10-14 (e) — Módulos nuevos (Lote 49, P2.3)", StringComparison.Ordinal);
        Assert.True(start > sql.IndexOf("2026-10-14 (d)", StringComparison.Ordinal), "Falta la sección 2026-10-14 (e) después de la (d).");
        var section = SqlSections.From(sql, start);
        Assert.Contains("MERGE dbo.ModuleDefinition AS t", section);
        Assert.Contains("('SALES_ORDERS', N'Órdenes de venta', N'Sales orders'", section);
        Assert.Contains("'WMS_LOTSERIAL', 100)", section); // las ventas descuentan inventario
        Assert.Contains("('TAX', N'Impuestos (IVU)', N'Taxes (IVU)'", section);
        Assert.Contains(", NULL, 110)", section);          // los impuestos no dependen de nadie
        Assert.Contains("('BILLING', N'Facturación', N'Billing'", section);
        Assert.Contains("'SALES_ORDERS', 120)", section);   // la facturación depende de las ventas
        // solo define: no enciende nada por compañía ni toca el núcleo
        Assert.DoesNotContain("dbo.TenantModule", section, StringComparison.Ordinal);
        Assert.DoesNotContain("DELETE", section, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DROP ", section, StringComparison.OrdinalIgnoreCase);
    }
}
