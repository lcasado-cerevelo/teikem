using System.Reflection;
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

    [Fact]
    public void A_later_section_removes_the_dependency_of_billing_on_sales_orders_so_a_transport_company_can_turn_billing_on_alone()
    {
        var sql = Sql();
        var start = sql.IndexOf("2026-10-26 — Facturación (BILLING) deja de depender de Órdenes de venta", StringComparison.Ordinal);
        Assert.True(start > sql.IndexOf("2026-10-14 (e) — Módulos nuevos (Lote 49, P2.3)", StringComparison.Ordinal), "La sección debe ir DESPUÉS de la que define el módulo (esa vuelve a ponerle la dependencia en cada corrida).");
        var section = SqlSections.From(sql, start);
        Assert.Contains("UPDATE dbo.ModuleDefinition SET DependsOnModuleKey = NULL WHERE ModuleKey = N'BILLING' AND DependsOnModuleKey IS NOT NULL;", section);
        // solo cambia la definición: no toca lo encendido por compañía ni borra nada
        Assert.DoesNotContain("dbo.TenantModule", section, StringComparison.Ordinal);
        Assert.DoesNotContain("DELETE", section, StringComparison.OrdinalIgnoreCase);
        // las ventas siguen necesitando el inventario: la sección no toca a SALES_ORDERS
        Assert.DoesNotContain("ModuleKey = N'SALES_ORDERS'", section, StringComparison.Ordinal);
    }

    [Fact]
    public void A_later_section_makes_transport_optional_and_makes_cod_depend_on_it_after_the_seed_that_marks_it_as_core()
    {
        var sql = Sql();
        var start = sql.IndexOf("2026-10-26 (b) — El módulo de Transporte (LTL_GROUND", StringComparison.Ordinal);
        Assert.True(start > sql.IndexOf("2026-10-26 — Facturación (BILLING) deja de depender", StringComparison.Ordinal), "La sección debe ir al final del archivo.");
        var section = SqlSections.From(sql, start);
        Assert.Contains("UPDATE dbo.ModuleDefinition SET IsCore = 0, Name = N'Transporte (última milla terrestre)', NameEn = N'Transport (ground last mile)' WHERE ModuleKey = N'LTL_GROUND';", section);
        Assert.Contains("UPDATE dbo.ModuleDefinition SET DependsOnModuleKey = N'LTL_GROUND' WHERE ModuleKey = N'COD'", section);
        // solo cambia definiciones: no apaga nada por compañía ni borra
        Assert.DoesNotContain("dbo.TenantModule", section, StringComparison.Ordinal);
        Assert.DoesNotContain("DELETE", section, StringComparison.OrdinalIgnoreCase);
        // el seed (congelado) lo sigue marcando núcleo: por eso esta sección tiene que ir después
        Assert.Contains("IN ('LTL_GROUND','SYSTEM') THEN 1", File.ReadAllText(Path.Combine(DatabaseInitializer.ResolveRepoRoot(null), "Diseño", "logistica-db-seed.sql")));
    }

    [Fact]
    public void The_transport_only_catalog_controllers_follow_the_transport_module_so_a_company_without_it_does_not_see_them()
    {
        foreach (var t in new[] { typeof(Teikem.Api.Controllers.DriversController), typeof(Teikem.Api.Controllers.FleetController), typeof(Teikem.Api.Controllers.VehiclesController), typeof(Teikem.Api.Controllers.DispatchZonesController) })
            Assert.Equal(new[] { ModuleKeys.LtlGround }, t.GetCustomAttributes<Teikem.Api.Auth.RequireModuleAttribute>(inherit: true).Select(a => a.ModuleKey));
    }
}
