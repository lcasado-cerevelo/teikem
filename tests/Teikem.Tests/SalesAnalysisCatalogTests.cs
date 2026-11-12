using Teikem.Domain.Constants;
using Teikem.Domain.Sales;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 241 — permisos y sección SQL del análisis de ventas: sales.analysis.view y sales.analysis.manage en el catálogo (BILLING), en las plantillas (TenantAdmin todo;
/// SalesSupervisor ver y administrar; SalesOffice ver), en el orden de la pantalla de roles, como lectura de la fuente SALES_ANALYSIS_LINE, y la sección «2026-11-12 —
/// Lote 241» de logistica-db-update.sql: tablas idempotentes, los permisos a los roles YA clonados una sola vez, los EntityType y la migración del campo «Representante».
/// </summary>
public sealed class SalesAnalysisCatalogTests
{
    private static string Section()
    {
        var sql = File.ReadAllText(Path.Combine(TripCatalogTests.RepoRoot(), "Diseño", "logistica-db-update.sql")).Replace("\r\n", "\n");
        var start = sql.IndexOf("2026-11-12 — Lote 241", StringComparison.Ordinal);
        Assert.True(start > 0, "falta la sección del lote 241");
        var next = sql.IndexOf("/* ------", start, StringComparison.Ordinal);
        return next < 0 ? sql[start..] : sql[start..next];
    }

    [Fact]
    public void Permissions_are_in_the_catalog_templates_display_order_and_data_source()
    {
        foreach (var code in new[] { PermissionCatalog.SalesAnalysisView, PermissionCatalog.SalesAnalysisManage })
        {
            Assert.Equal("BILLING", Assert.Single(PermissionCatalog.All, p => p.Code == code).Category);
            Assert.Contains(code, PermissionCatalog.RoleTemplates["TenantAdmin"]);
            Assert.Contains(code, PermissionCatalog.RoleTemplates["SalesSupervisor"]);
            Assert.Contains(code, PermissionDisplayOrder.Codes);
            Assert.Equal(new[] { PermissionCatalog.SalesView }, MenuPermissions.EditImplies[code]);
        }
        Assert.Contains(PermissionCatalog.SalesAnalysisView, PermissionCatalog.RoleTemplates["SalesOffice"]);
        Assert.DoesNotContain(PermissionCatalog.SalesAnalysisManage, PermissionCatalog.RoleTemplates["SalesOffice"]);
        Assert.DoesNotContain(PermissionCatalog.SalesAnalysisView, PermissionCatalog.RoleTemplates["Sales"]);
        Assert.Equal(PermissionCatalog.SalesAnalysisView, PermissionCatalog.DataSourceReadPermission(SalesAnalysisCodes.DataSourceKey));
    }

    [Fact]
    public void The_sql_section_is_idempotent_and_propagates_to_cloned_roles_once()
    {
        var s = Section();
        foreach (var table in new[] { "SalesRoute", "SalesRep", "SalesQuota", "ProductLine", "ProductLineBrand", "SalesAnalysisSetting", "SalesHistoryBatch",
                     "SalesHistoryLine", "PurchaseHistoryLine", "SalesHistoryAlias" })
            Assert.Contains($"IF OBJECT_ID(N'dbo.{table}', N'U') IS NULL CREATE TABLE dbo.{table} (", s);
        Assert.Contains("IF COL_LENGTH(N'dbo.Client', N'SalesRepId') IS NULL", s);
        Assert.Contains("IF NOT EXISTS (SELECT 1 FROM dbo.AppliedMigration WHERE Code = 'L241_SALES_ANALYSIS_PERMS')", s);
        Assert.Contains("r.Name IN ('TenantAdmin', 'SalesSupervisor')", s);
        Assert.Contains("r.Name = 'SalesOffice'", s);
        Assert.Contains("IF NOT EXISTS (SELECT 1 FROM dbo.AppliedMigration WHERE Code = 'L241_SALES_REP_FROM_FIELD')", s);
        Assert.Contains("d.FieldKey = N'sales_rep'", s);
        Assert.Contains("WHERE c.SalesRepId IS NULL", s);
        foreach (var et in new[] { SalesAnalysisCodes.RouteEntityType, SalesAnalysisCodes.RepEntityType, SalesAnalysisCodes.QuotaEntityType, SalesAnalysisCodes.LineEntityType,
                     SalesAnalysisCodes.AnalysisEntityType, SalesAnalysisCodes.DataSourceKey })
            Assert.Contains($"(N'{et}',", s);
        Assert.DoesNotContain("DROP ", s);
        Assert.DoesNotContain("DELETE ", s);
    }
}
