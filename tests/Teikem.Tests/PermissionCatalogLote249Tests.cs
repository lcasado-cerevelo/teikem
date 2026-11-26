using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Clients;
using Teikem.Domain.Constants;
using Teikem.Domain.Sales;
using Teikem.Domain.Tenancy;
using Teikem.Infrastructure.Notifications;
using Teikem.Infrastructure.Persistence.Scripts;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 249 (Lote E del plan: modificaciones de órdenes de venta) — la base compartida del lote: los cuatro permisos nuevos (catálogo, plantillas, orden de la pantalla de
/// roles, «editar ⇒ ver», propagación del sembrador y lectura de la fuente SALES_ORDER_CHANGE), los eventos de aviso del lote, el mapeo EF de las columnas nuevas de
/// SalesOrder, SalesOrderLine, Tenant y Client, y los textos de su sección de logistica-db-update.sql, leída SOLO hasta la siguiente (SqlSections.From).
/// </summary>
public sealed class PermissionCatalogLote249Tests
{
    internal const string Marker = "2026-11-27 — Lote 249: Edición de la orden de venta con autorización";

    private static readonly string[] News = { "sales.modify", "sales.tax", "sales.modify.approve", "warehouse.reopen-pack" };

    private static string UpdateSql()
        => File.ReadAllText(Path.Combine(DatabaseInitializer.ResolveRepoRoot(null), "Diseño", "logistica-db-update.sql")).Replace("\r\n", "\n");

    internal static string Section()
    {
        var sql = UpdateSql();
        var at = sql.IndexOf(Marker, StringComparison.Ordinal);
        Assert.True(at > 0, "Falta la sección del Lote 249 en logistica-db-update.sql.");
        return SqlSections.From(sql, sql.LastIndexOf("/* ----", at, StringComparison.Ordinal));
    }

    internal static string Statements(string section)
        => string.Join("\n", Regex.Replace(section, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline)
            .Split('\n').Where(l => !l.TrimStart().StartsWith("--", StringComparison.Ordinal)));

    // ---------------------------------------------------------------- permisos

    [Fact]
    public void The_four_permissions_exist_with_their_category_and_labels()
    {
        Assert.Equal(News, new[] { PermissionCatalog.SalesModify, PermissionCatalog.SalesTax, PermissionCatalog.SalesModifyApprove, PermissionCatalog.WarehouseReopenPack });
        void Def(string code, string category, string es, string en)
        {
            var d = Assert.Single(PermissionCatalog.All, p => p.Code == code);
            Assert.Equal((category, es, en), (d.Category, d.LabelEs, d.LabelEn));
        }
        Def("sales.modify", "BILLING", "Modificar órdenes de venta ya solicitadas (con quién autorizó y cómo)", "Modify requested sales orders (with who authorized and how)");
        Def("sales.tax", "BILLING", "Cambiar el impuesto de una línea de venta (con motivo)", "Change the tax of a sales line (with a reason)");
        Def("sales.modify.approve", "BILLING", "Dar la segunda aprobación a modificaciones de órdenes de venta", "Give the second approval to sales order changes");
        Def("warehouse.reopen-pack", "WAREHOUSE", "Reabrir un empaque (anula su envío en la etapa inicial)", "Reopen a pack (voids its shipment in the initial stage)");
        Assert.Equal(167, PermissionCatalog.All.Count);
    }

    [Fact]
    public void The_templates_get_exactly_what_the_plan_says()
    {
        var t = PermissionCatalog.RoleTemplates;
        foreach (var code in News) Assert.Contains(code, t["TenantAdmin"]);
        foreach (var code in new[] { "sales.modify", "sales.tax", "sales.modify.approve" }) Assert.Contains(code, t["SalesSupervisor"]);
        Assert.DoesNotContain("warehouse.reopen-pack", t["SalesSupervisor"]);
        Assert.Contains("sales.modify", t["SalesOffice"]);
        foreach (var code in new[] { "sales.tax", "sales.modify.approve", "warehouse.reopen-pack" }) Assert.DoesNotContain(code, t["SalesOffice"]);
        // nadie más los trae por plantilla (el vendedor y el operario de almacén, por ejemplo, no)
        foreach (var (role, codes) in t.Where(kv => kv.Key is not ("TenantAdmin" or "SalesSupervisor" or "SalesOffice")))
            foreach (var code in News) Assert.DoesNotContain(code, codes);
    }

    [Fact]
    public void The_seeder_propagates_only_to_the_roles_that_have_them_in_their_template()
    {
        var fresh = News.ToHashSet();
        var none = new HashSet<string>();
        Assert.Equal(News.OrderBy(c => c), PermissionCatalog.CodesToPropagate("TenantAdmin", fresh, none).OrderBy(c => c));
        Assert.Equal(new[] { "sales.modify", "sales.modify.approve", "sales.tax" }, PermissionCatalog.CodesToPropagate("SalesSupervisor", fresh, none).OrderBy(c => c));
        Assert.Equal(new[] { "sales.modify" }, PermissionCatalog.CodesToPropagate("SalesOffice", fresh, none));
        Assert.Empty(PermissionCatalog.CodesToPropagate("Sales", fresh, none));
        Assert.Empty(PermissionCatalog.CodesToPropagate("WarehouseOperator", fresh, none));
        Assert.Empty(PermissionCatalog.CodesToPropagate("SalesSupervisor", fresh, fresh));   // ya los tiene: solo agrega
    }

    [Fact]
    public void They_sit_in_their_screen_in_the_roles_page_and_editing_implies_entering()
    {
        foreach (var code in News) Assert.Contains(code, PermissionDisplayOrder.Codes);
        Assert.True(PermissionDisplayOrder.IndexOf("sales.price") < PermissionDisplayOrder.IndexOf("sales.modify"));
        Assert.Equal(PermissionDisplayOrder.IndexOf("sales.modify") + 1, PermissionDisplayOrder.IndexOf("sales.tax"));
        Assert.Equal(PermissionDisplayOrder.IndexOf("sales.tax") + 1, PermissionDisplayOrder.IndexOf("sales.modify.approve"));
        Assert.Equal(PermissionDisplayOrder.IndexOf("warehouse.issue") + 1, PermissionDisplayOrder.IndexOf("warehouse.reopen-pack"));
        foreach (var code in new[] { "sales.modify", "sales.tax", "sales.modify.approve" })
            Assert.Equal(new[] { PermissionCatalog.SalesView }, MenuPermissions.EditImplies[code]);
        Assert.Equal(new[] { "pickbatches.view" }, MenuPermissions.EditImplies["warehouse.reopen-pack"]);
    }

    [Fact]
    public void The_change_data_source_reads_with_sales_view_like_the_sales_orders()
    {
        Assert.Equal(PermissionCatalog.SalesView, PermissionCatalog.DataSourceReadPermission("SALES_ORDER_CHANGE"));
        // sin permiso de dueño (no tiene rutas polimórficas ni resolver de pertenencia), como SALES_ORDER
        Assert.False(PermissionCatalog.OwnerReadPermission.ContainsKey("SALES_ORDER_CHANGE"));
    }

    // ---------------------------------------------------------------- avisos

    [Fact]
    public void The_four_events_build_the_catalog_with_their_recipients()
    {
        var catalog = new NotificationEventCatalog(new INotificationEventProvider[] { new SalesChangeEventProvider(), new SalesEventProvider() });
        Assert.Equal(new[] { "SALES_ORDER_CHANGED", "SALES_ORDER_ADDITION_CREATED", "SALES_PACK_REOPENED", "SALES_CHANGE_APPROVAL_PENDING" }, SalesChangeEventCodes.All);
        foreach (var code in SalesChangeEventCodes.All)
        {
            var e = catalog.Find(code)!;
            Assert.NotNull(e);
            Assert.True(code.Length <= 60);
            Assert.Equal((ModuleKeys.SalesOrders, "/sales-orders?order={orderPublicId}"), (e.ModuleKey, e.LinkTemplate));
            Assert.Equal(NotificationEventCategories.Sales, NotificationEventCategories.Of(code));
            Assert.Empty(e.BlockNames);
            Assert.Contains("orderPublicId", e.Variables.Select(v => v.Name));
            var declared = e.Variables.Select(v => v.Name).ToHashSet();
            foreach (var text in new[] { e.DefaultSubjectEs, e.DefaultSubjectEn, e.DefaultBodyEs, e.DefaultBodyEn })
                Assert.All(NotificationRules.VariablesOf(text), name => Assert.Contains(name, declared));
            Assert.All(e.ContextRecipients ?? Array.Empty<NotificationContextRecipientDef>(), r =>
            {
                Assert.True(NotificationEventDefRules.IsValidContextKey(r.Key));
                Assert.Equal(NotificationAudience.Staff, r.Audience);   // nunca al cliente
            });
        }
        string[] Defaults(string code) => catalog.Find(code)!.ContextRecipients?.Where(r => r.IsDefault).Select(r => r.Key).OrderBy(k => k).ToArray() ?? Array.Empty<string>();
        Assert.Equal(new[] { "ASSIGNEE", "REGISTRANT", "SELLER" }, Defaults(SalesChangeEventCodes.OrderChanged));
        Assert.Equal(new[] { "ASSIGNEE", "SELLER" }, Defaults(SalesChangeEventCodes.AdditionCreated));
        Assert.Equal(new[] { "ASSIGNEE" }, Defaults(SalesChangeEventCodes.PackReopened));
        Assert.Empty(Defaults(SalesChangeEventCodes.ApprovalPending));   // la reciben quienes el administrador ponga (sales.modify.approve)
        Assert.False(catalog.Find(SalesChangeEventCodes.ApprovalPending)!.IsOptional);
        Assert.True(catalog.Find(SalesChangeEventCodes.OrderChanged)!.IsOptional);
        Assert.Equal(("SELLER", "REGISTRANT", "ASSIGNEE"), (SalesChangeContextRecipients.Seller, SalesChangeContextRecipients.Registrant, SalesChangeContextRecipients.Assignee));
    }

    // ---------------------------------------------------------------- modelo EF

    [Fact]
    public void The_ef_model_maps_the_new_columns_with_their_precision()
    {
        using var db = TenantIsolationModelTests.CreateSqlServerModelContext();
        var order = db.Model.FindEntityType(typeof(SalesOrder))!;
        Assert.True(order.FindProperty(nameof(SalesOrder.ParentSalesOrderId))!.IsNullable);
        var parent = Assert.Single(order.GetForeignKeys(), f => f.Properties.Single().Name == nameof(SalesOrder.ParentSalesOrderId));
        Assert.Equal(typeof(SalesOrder), parent.PrincipalEntityType.ClrType);
        var ix = Assert.Single(order.GetIndexes(), i => i.GetDatabaseName() == "IX_SalesOrder_Parent");
        Assert.Equal("[ParentSalesOrderId] IS NOT NULL", ix.GetFilter());

        var line = db.Model.FindEntityType(typeof(SalesOrderLine))!;
        Assert.Equal(30, line.FindProperty(nameof(SalesOrderLine.TaxReason))!.GetMaxLength());
        Assert.Equal(250, line.FindProperty(nameof(SalesOrderLine.TaxOverrideNote))!.GetMaxLength());
        Assert.Equal("decimal(9,4)", line.FindProperty(nameof(SalesOrderLine.TaxComputedRatePct))!.GetColumnType());
        Assert.Equal("decimal(18,4)", line.FindProperty(nameof(SalesOrderLine.TaxComputedAmount))!.GetColumnType());
        foreach (var p in new[] { nameof(SalesOrderLine.TaxReason), nameof(SalesOrderLine.TaxOverrideNote), nameof(SalesOrderLine.TaxComputedRatePct),
                     nameof(SalesOrderLine.TaxComputedAmount), nameof(SalesOrderLine.TaxOverrideBy), nameof(SalesOrderLine.TaxOverrideAtUtc) })
            Assert.True(line.FindProperty(p)!.IsNullable, p);

        var tenant = db.Model.FindEntityType(typeof(Tenant))!;
        Assert.Equal(12, tenant.FindProperty(nameof(Tenant.SalesChangeApprovalMode))!.GetMaxLength());
        Assert.False(tenant.FindProperty(nameof(Tenant.SalesChangeApprovalMode))!.IsNullable);
        Assert.Equal("decimal(18,2)", tenant.FindProperty(nameof(Tenant.SalesChangeApprovalAmount))!.GetColumnType());
        Assert.Equal("NONE", new Tenant().SalesChangeApprovalMode);
        Assert.False(new Tenant().SalesChangeAttachmentRequired);

        var client = db.Model.FindEntityType(typeof(Client))!;
        Assert.Equal(12, client.FindProperty(nameof(Client.SalesChangeApprovalMode))!.GetMaxLength());
        foreach (var p in new[] { nameof(Client.SalesChangeApprovalMode), nameof(Client.SalesChangeApprovalAmount), nameof(Client.SalesChangeAttachmentRequired) })
            Assert.True(client.FindProperty(p)!.IsNullable, p);   // NULL = hereda de la compañía
        Assert.Equal("decimal(18,2)", client.FindProperty(nameof(Client.SalesChangeApprovalAmount))!.GetColumnType());
    }

    // ---------------------------------------------------------------- sección SQL

    [Fact]
    public void The_section_goes_after_lote_248_names_its_decisions_and_says_what_it_does()
    {
        var sql = UpdateSql();
        var at = sql.IndexOf(Marker, StringComparison.Ordinal);
        Assert.True(at > sql.IndexOf("2026-11-27 — Lote 248 (Lote D del plan", StringComparison.Ordinal));
        Assert.True(sql.IndexOf("2026-11-27 — Lote 248", StringComparison.Ordinal) > 0);
        var section = Section();
        Assert.Contains("(docs/lote249-decisiones.md)", section);
        Assert.Contains("Solo AGREGA (y relaja o amplía dos CHECK). Idempotente.", section);
        Assert.EndsWith("GO", section.TrimEnd());
    }

    [Fact]
    public void Every_structure_change_has_its_guard_and_the_fks_are_composite()
    {
        var code = Statements(Section());
        foreach (Match m in Regex.Matches(code, @"ALTER TABLE dbo\.(\w+) ADD (?!CONSTRAINT\b)(\w+) "))
            Assert.Contains($"IF COL_LENGTH(N'dbo.{m.Groups[1].Value}', N'{m.Groups[2].Value}') IS NULL\n    ALTER TABLE dbo.{m.Groups[1].Value} ADD {m.Groups[2].Value} ", code);
        var tables = Regex.Matches(code, @"CREATE TABLE dbo\.(\w+)").Select(m => m.Groups[1].Value).ToList();
        Assert.Equal(new[] { "SalesOrderChange" }, tables);
        Assert.Contains("IF OBJECT_ID(N'dbo.SalesOrderChange', N'U') IS NULL CREATE TABLE dbo.SalesOrderChange (", code);
        var indexes = Regex.Matches(code, @"CREATE (?:UNIQUE )?INDEX (\w+) ON dbo\.(\w+)").Select(m => (m.Groups[1].Value, m.Groups[2].Value)).ToList();
        Assert.Equal(new[] { "IX_SalesOrder_Parent", "UX_SalesOrderChange_PublicId", "UX_SalesOrderChange_Pending", "IX_SalesOrderChange_PendingTenant",
            "IX_SalesOrderChange_Registered", "UX_SalesOrderChange_Attachment", "IX_SalesOrderChange_Addition" }, indexes.Select(i => i.Item1));
        foreach (var (name, table) in indexes)
            Assert.Contains($"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = '{name}' AND object_id = OBJECT_ID('dbo.{table}'))", code);
        Assert.Contains("CREATE UNIQUE INDEX UX_SalesOrderChange_Pending ON dbo.SalesOrderChange(SalesOrderId) WHERE IsPending = 1;", code);
        Assert.Contains("IF NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = 'UQ_SalesOrder_IdTenant' AND parent_object_id = OBJECT_ID('dbo.SalesOrder'))", code);
        foreach (var fk in new[] { "FK_SalesOrder_Parent FOREIGN KEY (ParentSalesOrderId, TenantId) REFERENCES dbo.SalesOrder(SalesOrderId, TenantId)",
                     "FK_SalesOrderChange_Order FOREIGN KEY (SalesOrderId, TenantId) REFERENCES dbo.SalesOrder(SalesOrderId, TenantId)",
                     "FK_SalesOrderChange_Addition FOREIGN KEY (AdditionSalesOrderId, TenantId) REFERENCES dbo.SalesOrder(SalesOrderId, TenantId)" })
            Assert.Contains(fk, code);
        foreach (var ck in new[] { "CK_SalesOrder_ParentNotSelf", "CK_SalesOrderLine_TaxOverride", "CK_Tenant_SalesChangeApproval", "CK_Client_SalesChangeApproval" })
            Assert.Contains($"IF OBJECT_ID(N'dbo.{ck}', N'C') IS NULL", code);
    }

    [Fact]
    public void The_two_checks_are_rebuilt_only_while_they_are_the_old_ones()
    {
        var code = Statements(Section());
        // la línea quitada: el DROP solo si el CHECK todavía no conoce IsClosed; el ADD solo si no existe
        var drop = code.IndexOf("ALTER TABLE dbo.SalesOrderLine DROP CONSTRAINT CK_SalesOrderLine_Qty;", StringComparison.Ordinal);
        var add = code.IndexOf("ALTER TABLE dbo.SalesOrderLine ADD CONSTRAINT CK_SalesOrderLine_Qty", StringComparison.Ordinal);
        Assert.True(drop > 0 && add > drop);
        Assert.Contains("AND definition NOT LIKE '%IsClosed%')", code);
        Assert.Contains("(QtyOrdered > 0 OR (QtyOrdered = 0 AND IsClosed = 1 AND QtyReserved = 0 AND QtyBackordered = 0 AND QtyShipped = 0))", code);
        // ON_REQUEST en la base de facturación de las órdenes de venta
        Assert.Contains("AND definition NOT LIKE '%ON_REQUEST%')", code);
        Assert.Contains("CHECK (SalesBillingTrigger IN (''ON_DISPATCH'',''ON_DELIVERY'',''ON_REQUEST''))", code);
        // no se borra nada más
        Assert.Equal(2, Regex.Matches(code, @"\bDROP\b").Count);
        foreach (var word in new[] { "DELETE ", "DROP TABLE", "DROP COLUMN", "ALTER COLUMN", "UPDATE dbo." })
            Assert.DoesNotContain(word, code, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_section_seeds_the_catalogs_the_permissions_and_their_propagation_once()
    {
        var code = Statements(Section());
        foreach (var s in new[] { "(N'PENDING_APPROVAL', N'{\"es\":\"Pendiente de aprobar\",\"en\":\"Pending approval\"}', '#F59E0B', 1, 1, 'PIPELINE')",
                     "(N'APPLIED',          N'{\"es\":\"Aplicada\",\"en\":\"Applied\"}',                     '#10B981', 2, 0, 'TERMINAL')",
                     "(N'REJECTED',         N'{\"es\":\"Rechazada\",\"en\":\"Rejected\"}',                   '#EF4444', 3, 0, 'TERMINAL')",
                     "(N'WITHDRAWN',        N'{\"es\":\"Retirada\",\"en\":\"Withdrawn\"}',                   '#6B7280', 4, 0, 'TERMINAL')" })
            Assert.Contains(s, code);
        Assert.Contains("lat.InternalCode IN (N'REJECTED', N'WITHDRAWN')", code);
        Assert.Contains("frm.InternalCode = N'PENDING_APPROVAL'", code);
        foreach (var ch in new[] { "PHONE", "EMAIL", "IN_PERSON", "MESSAGE" }) Assert.Contains($"(N'{ch}',", code);
        Assert.Contains("(N'SALES_ORDER_CHANGE', N'{\"es\":\"Modificación de orden de venta\",\"en\":\"Sales order change\"}', 201)", code);
        foreach (var def in PermissionCatalog.All.Where(p => News.Contains(p.Code)))
        {
            Assert.Contains($"IF NOT EXISTS (SELECT 1 FROM dbo.Permission WHERE Code = '{def.Code}')", code);
            Assert.Contains($"N'{{\"es\":\"{def.LabelEs}\",\"en\":\"{def.LabelEn}\"}}'", code);
        }
        Assert.Contains("IF NOT EXISTS (SELECT 1 FROM dbo.AppliedMigration WHERE Code = 'L249_SALES_CHANGE_PERMS')", code);
        Assert.Contains("INSERT INTO dbo.AppliedMigration (Code) VALUES ('L249_SALES_CHANGE_PERMS');", code);
        Assert.Contains("WHERE r.IsActive = 1 AND r.TenantId IS NOT NULL", code);
        Assert.Contains("NOT EXISTS (SELECT 1 FROM dbo.RolePermission x WHERE x.RoleId = r.RoleId AND x.PermissionId = pm.PermissionId)", code);
        // la propagación del SQL es la misma que la de las plantillas
        var pairs = Regex.Matches(code, @"\('([a-z.\-]+)',\s+'(\w+)'\)").Select(m => (Code: m.Groups[1].Value, Role: m.Groups[2].Value)).ToList();
        var expected = News.SelectMany(c => new[] { "TenantAdmin", "SalesSupervisor", "SalesOffice" }
            .Where(r => PermissionCatalog.RoleTemplates[r].Contains(c)).Select(r => (Code: c, Role: r))).ToList();
        Assert.Equal(expected.OrderBy(p => p.Code).ThenBy(p => p.Role), pairs.OrderBy(p => p.Code).ThenBy(p => p.Role));
    }
}
