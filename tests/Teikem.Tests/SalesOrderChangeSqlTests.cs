using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Teikem.Infrastructure.Persistence.Scripts;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 249 (P5) — la sección «Lote 249» de logistica-db-update.sql contra SQL Server REAL (se omite sin TEIKEM_TEST_SQLSERVER), sobre la base desechable de
/// <see cref="NotificationSqlFixture"/> (creada desde el modelo EF y sin FK). PrepareAsync deja la base «como antes del lote» en lo que importa (sin la tabla
/// dbo.SalesOrderChange ni los CHECK nuevos; TaxReason y las columnas del IVU manual YA existen, como si el Lote D las hubiera creado), crea lo que la sección da por
/// hecho (dbo.AppliedMigration, los DEFAULT de CreatedAtUtc del catálogo, StageKind, PermissionCategory y dos roles ya clonados) y corre DOS veces la sección
/// (acotada al siguiente encabezado). Después se prueban las FK COMPUESTAS (547), los índices únicos filtrados (2601), los CHECK y la propagación de permisos.
/// </summary>
public sealed class SalesOrderChangeSqlTests(NotificationSqlFixture fx) : IClassFixture<NotificationSqlFixture>
{
    internal const string Marker = "2026-11-27 — Lote 249: Edición de la orden de venta con autorización";
    private const string TenantAName = "Compañía A modificaciones (sección 249)";
    private const string TenantBName = "Compañía B modificaciones (sección 249)";
    private static readonly SemaphoreSlim Gate = new(1, 1);
    /// <summary>Un JSON vacío como literal SQL.</summary>
    private const string J = "N'{}'";

    private static string UpdateSql()
        => File.ReadAllText(Path.Combine(DatabaseInitializer.ResolveRepoRoot(null), "Diseño", "logistica-db-update.sql")).Replace("\r\n", "\n");

    internal static string Section()
    {
        var sql = UpdateSql();
        var at = sql.IndexOf(Marker, StringComparison.Ordinal);
        Assert.True(at > 0, "Falta la sección del Lote 249 en logistica-db-update.sql.");
        return SqlSections.From(sql, sql.LastIndexOf("/* ----", at, StringComparison.Ordinal));
    }

    private static string Statements(string section)
        => string.Join("\n", Regex.Replace(section, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline)
            .Split('\n').Where(l => !l.TrimStart().StartsWith("--", StringComparison.Ordinal)));

    private static List<string> Batches(string section)
        => Regex.Split(section, @"^\s*GO\s*$", RegexOptions.Multiline).Select(b => b.Trim()).Where(b => Statements(b).Trim().Length > 0).ToList();

    private static async Task ExecAsync(SqlConnection c, string sql)
    {
        await using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<object?> ScalarAsync(SqlConnection c, string sql)
    {
        await using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        var v = await cmd.ExecuteScalarAsync();
        return v is DBNull ? null : v;
    }

    private static async Task<int> SqlErrorAsync(SqlConnection c, string sql)
    {
        var e = await Assert.ThrowsAsync<SqlException>(() => ExecAsync(c, sql));
        return e.Number;
    }

    private async Task<SqlConnection> OpenAsync()
    {
        var c = new SqlConnection(fx.ConnectionString);
        await c.OpenAsync();
        return c;
    }

    private static async Task<int> TenantIdAsync(SqlConnection c, string name)
        => (int)(await ScalarAsync(c, $"SELECT TenantId FROM dbo.Tenant WHERE Name = N'{name}'"))!;

    /// <summary>Una orden de venta escrita con el modelo EF (llena todas las columnas obligatorias). parent = la orden madre (ParentSalesOrderId).</summary>
    private async Task<int> InsertOrderAsync(int tenantId, string number, int? parent = null)
    {
        await using var db = fx.NewDb(tenantId);
        var o = new Teikem.Domain.Sales.SalesOrder
        {
            PublicId = Guid.NewGuid(), TenantId = tenantId, Number = number, WarehouseId = 1, ClientId = 1, StatusCodeId = 1, RequestedDate = new DateOnly(2026, 11, 27),
            ParentSalesOrderId = parent, CreatedAtUtc = DateTime.UtcNow,
        };
        db.Set<Teikem.Domain.Sales.SalesOrder>().Add(o);
        await db.SaveChangesAsync();
        return o.SalesOrderId;
    }

    /// <summary>Guarda con el modelo EF y devuelve el número del error de SQL Server (0 = se guardó).</summary>
    private async Task<int> SaveErrorAsync(int tenantId, Action<Teikem.Infrastructure.Persistence.TeikemDbContext> add)
    {
        await using var db = fx.NewDb(tenantId);
        add(db);
        try { await db.SaveChangesAsync(); return 0; }
        catch (Microsoft.EntityFrameworkCore.DbUpdateException e) when (e.InnerException is SqlException sql) { return sql.Number; }
    }

    private static async Task<int> StatusIdAsync(SqlConnection c, string code)
        => (int)(await ScalarAsync(c, $"SELECT StatusCodeId FROM dbo.StatusCode WHERE Entity = N'SalesOrderChangeStatus' AND InternalCode = N'{code}'"))!;

    private static async Task<int> ChannelIdAsync(SqlConnection c)
        => (int)(await ScalarAsync(c, "SELECT LookupCodeId FROM dbo.LookupCode WHERE Entity = N'SalesChangeChannel' AND InternalCode = N'PHONE'"))!;

    private static string InsertChange(int tenantId, int orderId, int changeNo, int statusId, int channelId, bool pending, string attachment = "NULL", string kind = "MODIFY",
        string addition = "NULL")
        => $"""
            INSERT dbo.SalesOrderChange (TenantId, SalesOrderId, ChangeNo, Kind, StatusCodeId, IsPending, RegisteredBy, RegisteredAtUtc, AuthorizedByName,
                AuthorizationChannelLookupId, AuthorizedAtUtc, Reason, AttachmentFileId, RequestJson, BeforeJson, AfterJson, TotalBefore, TotalAfter, TotalDelta,
                BaseFingerprint, AdditionSalesOrderId)
            VALUES ({tenantId}, {orderId}, {changeNo}, N'{kind}', {statusId}, {(pending ? 1 : 0)}, 1, SYSUTCDATETIME(), N'María', {channelId}, SYSUTCDATETIME(), N'Motivo',
                {attachment}, {J}, {J}, {J}, 0, 0, 0, REPLICATE('0', 64), {addition});
            """;

    /// <summary>Idempotente: la primera vez deja la base como antes del lote, siembra lo que la sección necesita y corre la sección dos veces.</summary>
    private async Task PrepareAsync()
    {
        await Gate.WaitAsync();
        try
        {
            await using var c = await OpenAsync();
            if ((int)(await ScalarAsync(c, "SELECT COUNT(*) FROM sys.key_constraints WHERE name = 'UQ_SalesOrder_IdTenant'"))! > 0) return;

            await fx.NewTenantAsync(TenantAName);
            await fx.NewTenantAsync(TenantBName);
            var a = await TenantIdAsync(c, TenantAName);

            // como antes del lote: sin la tabla de la modificación (EnsureCreated la hizo sin las FK compuestas ni los CHECK) ni los CHECK nuevos
            await ExecAsync(c, """
                IF OBJECT_ID(N'dbo.SalesOrderChange', N'U') IS NOT NULL DROP TABLE dbo.SalesOrderChange;
                IF OBJECT_ID(N'dbo.CK_SalesOrderLine_Qty', N'C') IS NOT NULL ALTER TABLE dbo.SalesOrderLine DROP CONSTRAINT CK_SalesOrderLine_Qty;
                ALTER TABLE dbo.SalesOrderLine ADD CONSTRAINT CK_SalesOrderLine_Qty CHECK (QtyOrdered > 0 AND QtyReserved >= 0 AND QtyBackordered >= 0 AND QtyShipped >= 0);
                IF OBJECT_ID(N'dbo.AppliedMigration', N'U') IS NULL CREATE TABLE dbo.AppliedMigration (Code VARCHAR(60) NOT NULL PRIMARY KEY, AppliedAtUtc DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME());
                DECLARE @sql NVARCHAR(MAX) = N'';
                SELECT @sql += N'ALTER TABLE dbo.' + QUOTENAME(t.name) + N' ADD CONSTRAINT ' + QUOTENAME(N'DF_' + t.name + N'_CreatedAtUtc_L249Test') + N' DEFAULT SYSUTCDATETIME() FOR CreatedAtUtc;'
                  FROM sys.tables t JOIN sys.columns col ON col.object_id = t.object_id AND col.name = N'CreatedAtUtc'
                  WHERE t.name IN (N'LookupCode', N'StatusCode', N'CatalogDomain', N'Permission', N'StatusLateralEntry')
                    AND NOT EXISTS (SELECT 1 FROM sys.default_constraints d WHERE d.parent_object_id = t.object_id AND d.parent_column_id = col.column_id);
                EXEC sp_executesql @sql;
                IF NOT EXISTS (SELECT 1 FROM dbo.LookupCode WHERE Entity = N'StageKind' AND InternalCode = N'PIPELINE')
                    INSERT dbo.LookupCode (Entity, InternalCode, LabelJson, SortOrder, IsSystem, IsActive) VALUES (N'StageKind', N'PIPELINE', N'{"es":"En curso"}', 1, 1, 1);
                IF NOT EXISTS (SELECT 1 FROM dbo.LookupCode WHERE Entity = N'StageKind' AND InternalCode = N'TERMINAL')
                    INSERT dbo.LookupCode (Entity, InternalCode, LabelJson, SortOrder, IsSystem, IsActive) VALUES (N'StageKind', N'TERMINAL', N'{"es":"Final"}', 3, 1, 1);
                IF NOT EXISTS (SELECT 1 FROM dbo.LookupCode WHERE Entity = N'PermissionCategory' AND InternalCode = N'BILLING')
                    INSERT dbo.LookupCode (Entity, InternalCode, LabelJson, SortOrder, IsSystem, IsActive) VALUES (N'PermissionCategory', N'BILLING', N'{"es":"Facturación"}', 1, 1, 1);
                IF NOT EXISTS (SELECT 1 FROM dbo.LookupCode WHERE Entity = N'PermissionCategory' AND InternalCode = N'WAREHOUSE')
                    INSERT dbo.LookupCode (Entity, InternalCode, LabelJson, SortOrder, IsSystem, IsActive) VALUES (N'PermissionCategory', N'WAREHOUSE', N'{"es":"Almacén"}', 2, 1, 1);
                """);
            // dos roles YA clonados de la compañía A (y una plantilla sin compañía, que no se toca)
            await ExecAsync(c, $"""
                INSERT dbo.Role (TenantId, Name, IsActive, IsSystem) VALUES ({a}, N'TenantAdmin', 1, 1), ({a}, N'SalesOffice', 1, 1), (NULL, N'TenantAdmin', 1, 1);
                """);

            foreach (var _ in new[] { 1, 2 })
                foreach (var batch in Batches(Section()))
                    await ExecAsync(c, batch);
        }
        finally { Gate.Release(); }
    }

    [SqlServerFact]
    public async Task Section_runs_twice_and_leaves_the_structure_and_the_data()
    {
        await PrepareAsync();
        await using var c = await OpenAsync();
        foreach (var batch in Batches(Section())) await ExecAsync(c, batch);   // una tercera corrida tampoco falla

        Assert.NotNull(await ScalarAsync(c, "SELECT OBJECT_ID(N'dbo.SalesOrderChange', N'U')"));
        foreach (var fk in new[] { "FK_SalesOrderChange_Order", "FK_SalesOrderChange_Addition", "FK_SalesOrder_Parent" })
            Assert.Equal(2, await ScalarAsync(c, $"SELECT COUNT(*) FROM sys.foreign_key_columns WHERE constraint_object_id = OBJECT_ID('dbo.{fk}')"));
        foreach (var ux in new[] { "UX_SalesOrderChange_Pending", "UX_SalesOrderChange_Attachment" })
            Assert.Equal(1, await ScalarAsync(c, $"SELECT COUNT(*) FROM sys.indexes WHERE name = '{ux}' AND is_unique = 1 AND has_filter = 1"));
        foreach (var ck in new[] { "CK_SalesOrderLine_Qty", "CK_SalesOrderLine_TaxOverride", "CK_Tenant_SalesChangeApproval", "CK_Client_SalesChangeApproval",
                     "CK_SalesOrder_ParentNotSelf", "CK_SalesOrderChange_Kind", "CK_SalesOrderChange_Addition", "CK_SalesOrderChange_Text" })
            Assert.Equal(1, await ScalarAsync(c, $"SELECT COUNT(*) FROM sys.check_constraints WHERE name = '{ck}'"));
        Assert.Contains("IsClosed", (string)(await ScalarAsync(c, "SELECT definition FROM sys.check_constraints WHERE name = 'CK_SalesOrderLine_Qty'"))!);

        Assert.Equal(4, await ScalarAsync(c, "SELECT COUNT(*) FROM dbo.StatusCode WHERE Entity = N'SalesOrderChangeStatus'"));
        Assert.Equal(1, await ScalarAsync(c, "SELECT COUNT(*) FROM dbo.StatusCode WHERE Entity = N'SalesOrderChangeStatus' AND IsInitial = 1 AND InternalCode = N'PENDING_APPROVAL'"));
        Assert.Equal(4, await ScalarAsync(c, "SELECT COUNT(*) FROM dbo.LookupCode WHERE Entity = N'SalesChangeChannel'"));
        Assert.Equal(1, await ScalarAsync(c, "SELECT COUNT(*) FROM dbo.LookupCode WHERE Entity = N'EntityType' AND InternalCode = N'SALES_ORDER_CHANGE'"));
        Assert.Equal(2, await ScalarAsync(c, "SELECT COUNT(*) FROM dbo.StatusLateralEntry WHERE TenantId IS NULL AND LateralStatusCodeId IN (SELECT StatusCodeId FROM dbo.StatusCode WHERE Entity = N'SalesOrderChangeStatus')"));
        foreach (var p in new[] { "sales.modify", "sales.tax", "sales.modify.approve", "warehouse.reopen-pack" })
            Assert.Equal(1, await ScalarAsync(c, $"SELECT COUNT(*) FROM dbo.Permission WHERE Code = '{p}'"));
        // las compañías que ya existían quedan con los valores por omisión
        Assert.Equal(0, await ScalarAsync(c, $"SELECT COUNT(*) FROM dbo.Tenant WHERE Name = N'{TenantAName}' AND (SalesChangeApprovalMode <> N'NONE' OR SalesChangeAttachmentRequired <> 0)"));
    }

    [SqlServerFact]
    public async Task Permissions_reach_the_cloned_roles_once_and_only_those()
    {
        await PrepareAsync();
        await using var c = await OpenAsync();
        var a = await TenantIdAsync(c, TenantAName);
        string Perms(string role) => $"""
            SELECT STRING_AGG(p.Code, ',') WITHIN GROUP (ORDER BY p.Code) FROM dbo.RolePermission rp JOIN dbo.Permission p ON p.PermissionId = rp.PermissionId
            JOIN dbo.Role r ON r.RoleId = rp.RoleId WHERE r.TenantId = {a} AND r.Name = N'{role}' AND p.Code IN ('sales.modify','sales.tax','sales.modify.approve','warehouse.reopen-pack')
            """;
        Assert.Equal("sales.modify,sales.modify.approve,sales.tax,warehouse.reopen-pack", await ScalarAsync(c, Perms("TenantAdmin")));
        Assert.Equal("sales.modify", await ScalarAsync(c, Perms("SalesOffice")));
        Assert.Equal(0, await ScalarAsync(c, "SELECT COUNT(*) FROM dbo.RolePermission rp JOIN dbo.Role r ON r.RoleId = rp.RoleId WHERE r.TenantId IS NULL"));
        Assert.Equal(1, await ScalarAsync(c, "SELECT COUNT(*) FROM dbo.AppliedMigration WHERE Code = 'L249_SALES_CHANGE_PERMS'"));
        // sin filas repetidas (dos corridas)
        Assert.Equal(0, await ScalarAsync(c, "SELECT COUNT(*) FROM (SELECT RoleId, PermissionId FROM dbo.RolePermission GROUP BY RoleId, PermissionId HAVING COUNT(*) > 1) x"));
    }

    [SqlServerFact]
    public async Task The_composite_foreign_keys_refuse_a_parent_or_a_change_of_another_company()
    {
        await PrepareAsync();
        await using var c = await OpenAsync();
        var a = await TenantIdAsync(c, TenantAName);
        var b = await TenantIdAsync(c, TenantBName);
        var orderA = await InsertOrderAsync(a, "OV-A" + Guid.NewGuid().ToString("N")[..6]);
        var orderB = await InsertOrderAsync(b, "OV-B" + Guid.NewGuid().ToString("N")[..6]);
        var pending = await StatusIdAsync(c, "PENDING_APPROVAL");
        var channel = await ChannelIdAsync(c);

        // una adición de la misma compañía sí; de otra compañía no (547)
        var child = await InsertOrderAsync(a, "OV-H" + Guid.NewGuid().ToString("N")[..6], orderA);
        Assert.True(child > 0);
        Assert.Equal(547, await SqlErrorAsync(c, $"UPDATE dbo.SalesOrder SET ParentSalesOrderId = {orderB} WHERE SalesOrderId = {orderA}"));
        Assert.Equal(547, await SqlErrorAsync(c, $"UPDATE dbo.SalesOrder SET ParentSalesOrderId = {orderA} WHERE SalesOrderId = {orderA}"));   // ni de sí misma

        Assert.Equal(547, await SqlErrorAsync(c, InsertChange(a, orderB, 1, pending, channel, pending: false)));
        Assert.Equal(547, await SqlErrorAsync(c, InsertChange(a, orderA, 1, pending, channel, pending: false, kind: "ADDITION", addition: orderB.ToString())));
        Assert.Equal(547, await SqlErrorAsync(c, InsertChange(a, orderA, 1, pending, channel, pending: false, kind: "MODIFY", addition: child.ToString())));   // solo ADDITION lleva hija
        await ExecAsync(c, InsertChange(a, orderA, 1, pending, channel, pending: false, kind: "ADDITION", addition: child.ToString()));
        Assert.Equal(547, await SqlErrorAsync(c, InsertChange(a, orderA, 2, pending, channel, pending: false, kind: "OTHER")));
        Assert.Equal(2627, await SqlErrorAsync(c, InsertChange(a, orderA, 1, pending, channel, pending: false)));   // UQ_SalesOrderChange_No
    }

    [SqlServerFact]
    public async Task One_pending_change_per_order_and_one_change_per_attachment()
    {
        await PrepareAsync();
        await using var c = await OpenAsync();
        var a = await TenantIdAsync(c, TenantAName);
        var order = await InsertOrderAsync(a, "OV-P" + Guid.NewGuid().ToString("N")[..6]);
        var other = await InsertOrderAsync(a, "OV-Q" + Guid.NewGuid().ToString("N")[..6]);
        var pending = await StatusIdAsync(c, "PENDING_APPROVAL");
        var applied = await StatusIdAsync(c, "APPLIED");
        var channel = await ChannelIdAsync(c);

        await ExecAsync(c, InsertChange(a, order, 1, pending, channel, pending: true));
        Assert.Equal(2601, await SqlErrorAsync(c, InsertChange(a, order, 2, pending, channel, pending: true)));
        await ExecAsync(c, InsertChange(a, order, 2, applied, channel, pending: false));   // aplicadas, las que sean

        var fileRow = new Teikem.Domain.Files.StoredFile
        {
            PublicId = Guid.NewGuid(), TenantId = a, OwnerEntityLookupId = 1, OwnerId = order, Kind = "DOCUMENT", FileName = "captura.png", ContentType = "image/png",
            SizeBytes = 8, Sha256 = new string('a', 64), StorageKey = "k", IsActive = true,
        };
        Assert.Equal(0, await SaveErrorAsync(a, db => db.Set<Teikem.Domain.Files.StoredFile>().Add(fileRow)));
        var file = fileRow.StoredFileId;
        await ExecAsync(c, InsertChange(a, order, 3, applied, channel, pending: false, attachment: file.ToString()));
        Assert.Equal(2601, await SqlErrorAsync(c, InsertChange(a, other, 1, applied, channel, pending: false, attachment: file.ToString())));
        Assert.Equal(547, await SqlErrorAsync(c, $"""
            INSERT dbo.SalesOrderChange (TenantId, SalesOrderId, ChangeNo, Kind, StatusCodeId, RegisteredBy, RegisteredAtUtc, AuthorizedByName, AuthorizationChannelLookupId,
                AuthorizedAtUtc, Reason, RequestJson, BeforeJson, AfterJson, TotalBefore, TotalAfter, TotalDelta, BaseFingerprint)
            VALUES ({a}, {other}, 5, N'MODIFY', {applied}, 1, SYSUTCDATETIME(), N'   ', {channel}, SYSUTCDATETIME(), N'Motivo', {J}, {J}, {J}, 0, 0, 0, REPLICATE('0', 64));
            """));   // CK_SalesOrderChange_Text: quién autorizó no puede ir en blanco
    }

    [SqlServerFact]
    public async Task The_new_checks_accept_the_removed_line_and_refuse_incoherent_rows()
    {
        await PrepareAsync();
        await using var c = await OpenAsync();
        var a = await TenantIdAsync(c, TenantAName);
        var order = await InsertOrderAsync(a, "OV-L" + Guid.NewGuid().ToString("N")[..6]);
        Task<int> Line(int no, decimal qty, bool closed, decimal reserved, Action<Teikem.Domain.Sales.SalesOrderLine>? set = null)
            => SaveErrorAsync(a, db =>
            {
                var l = new Teikem.Domain.Sales.SalesOrderLine
                {
                    TenantId = a, SalesOrderId = order, LineNo = no, ProductId = 1, QtyOrdered = qty, QtyReserved = reserved, IsClosed = closed, UnitPrice = 10m,
                };
                set?.Invoke(l);
                db.Set<Teikem.Domain.Sales.SalesOrderLine>().Add(l);
            });

        Assert.Equal(0, await Line(1, 0m, closed: true, reserved: 0m));   // quitada: pedido 0, cerrada, sin reserva
        Assert.Equal(547, await Line(2, 0m, closed: false, reserved: 0m));   // pedido 0 sin cerrar
        Assert.Equal(547, await Line(3, 0m, closed: true, reserved: 1m));    // pedido 0 con reserva
        // IVU manual: exige la nota y lo que daba el cálculo
        Assert.Equal(547, await Line(4, 1m, false, 0m, l => l.TaxReason = "MANUAL_OVERRIDE"));
        Assert.Equal(0, await Line(5, 1m, false, 0m, l => (l.TaxReason, l.TaxOverrideNote, l.TaxComputedRatePct, l.TaxComputedAmount) = ("MANUAL_OVERRIDE", "Acuerdo", 11.5m, 1.15m)));

        var t = await TenantIdAsync(c, TenantBName);
        // (ojo: con el monto NULL el CHECK queda en UNKNOWN y SQL lo deja pasar; lo frena el servicio con SettingsAmountRequired)
        Assert.Equal(547, await SqlErrorAsync(c, $"UPDATE dbo.Tenant SET SalesChangeApprovalMode = N'ABOVE_AMOUNT', SalesChangeApprovalAmount = 0 WHERE TenantId = {t}"));
        Assert.Equal(547, await SqlErrorAsync(c, $"UPDATE dbo.Tenant SET SalesChangeApprovalMode = N'SOMETIMES' WHERE TenantId = {t}"));
        await ExecAsync(c, $"UPDATE dbo.Tenant SET SalesChangeApprovalMode = N'ABOVE_AMOUNT', SalesChangeApprovalAmount = 500 WHERE TenantId = {t}");
        var clientRow = new Teikem.Domain.Clients.Client
        {
            PublicId = Guid.NewGuid(), TenantId = a, Code = "C" + Guid.NewGuid().ToString("N")[..6], Name = "Cliente", StatusCodeId = 1, IsActive = true, CreatedAtUtc = DateTime.UtcNow,
        };
        Assert.Equal(0, await SaveErrorAsync(a, db => db.Clients.Add(clientRow)));
        var client = clientRow.ClientId;
        Assert.Equal(547, await SqlErrorAsync(c, $"UPDATE dbo.Client SET SalesChangeApprovalMode = N'ABOVE_AMOUNT', SalesChangeApprovalAmount = 0 WHERE ClientId = {client}"));
        await ExecAsync(c, $"UPDATE dbo.Client SET SalesChangeApprovalMode = NULL, SalesChangeApprovalAmount = NULL, SalesChangeAttachmentRequired = NULL WHERE ClientId = {client}");
        await ExecAsync(c, $"UPDATE dbo.Client SET SalesChangeApprovalMode = N'ALWAYS' WHERE ClientId = {client}");
    }
}
