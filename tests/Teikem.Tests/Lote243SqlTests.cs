using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Teikem.Domain.Notifications;
using Teikem.Infrastructure;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Notifications;
using Teikem.Infrastructure.Persistence.Scripts;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 243 (Lote G del plan de Solutions) — la sección de logistica-db-update.sql (después de la del 241; se lee solo hasta la siguiente): solo AGREGA (sin DELETE, sin permisos, sin datos),
/// amplía CK_NotificationRuleRecipient_Kind para el Kind CONTEXT con el patrón de CK_NumberSequence_Kind (DROP protegido con NOT LIKE '%CONTEXT%',
/// ADD con IF NOT EXISTS) y agrega ClientContact.ReceivesEmailNotifications con COL_LENGTH y DEFAULT 0. Contra SQL Server real (si hay
/// TEIKEM_TEST_SQLSERVER) la sección corre DOS veces sobre una base desechable con el CHECK viejo y sin la columna, y el CHECK nuevo admite
/// CONTEXT y sigue rechazando lo mal formado.
/// </summary>
public sealed class Lote243SqlTests(NotificationSqlFixture fx) : IClassFixture<NotificationSqlFixture>
{
    private const string Marker = "2026-11-24 — Lote 243 (Lote G del plan de Solutions): correo con la orden de venta al solicitarla";

    private static string UpdateSql()
        => File.ReadAllText(Path.Combine(DatabaseInitializer.ResolveRepoRoot(null), "Diseño", "logistica-db-update.sql")).Replace("\r\n", "\n");

    /// <summary>
    /// La sección completa: desde su comentario de encabezado hasta el de la siguiente (Lote 244 en adelante) o el final del archivo. Así el fixture contra
    /// SQL Server solo corre los lotes de G.
    /// </summary>
    private static string Section()
    {
        var sql = UpdateSql();
        var at = sql.IndexOf(Marker, StringComparison.Ordinal);
        Assert.True(at > 0, "Falta la sección del Lote 243 en logistica-db-update.sql.");
        var header = sql.LastIndexOf("/* ----", at, StringComparison.Ordinal);
        return SqlSections.From(sql, header);
    }

    /// <summary>Las sentencias de la sección sin los comentarios (lo que de verdad corre).</summary>
    private static string Statements(string section)
        => string.Join("\n", Regex.Replace(section, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline)
            .Split('\n').Where(l => !l.TrimStart().StartsWith("--", StringComparison.Ordinal)));

    private static IEnumerable<string> Batches(string section)
        => Regex.Split(section, @"^\s*GO\s*$", RegexOptions.Multiline).Select(b => b.Trim()).Where(b => Statements(b).Trim().Length > 0);

    [Fact]
    public void The_section_goes_after_lote_241_and_says_it_only_adds_and_is_idempotent()
    {
        var sql = UpdateSql();
        var at = sql.IndexOf(Marker, StringComparison.Ordinal);
        Assert.True(at > sql.IndexOf("2026-11-12 — Lote 241: «Análisis de ventas»", StringComparison.Ordinal));
        // Lote 244: ya no es la última; la sección se acota a su propio encabezado (Section)
        var section = Section();
        Assert.Contains("(docs/lote243-decisiones.md)", section);
        Assert.Contains("Solo AGREGA. Idempotente.", section);
        Assert.EndsWith("GO", section.TrimEnd());
    }

    [Fact]
    public void The_check_is_widened_to_context_with_the_guarded_drop_and_add()
    {
        var code = Statements(Section());
        var drop = code.IndexOf("ALTER TABLE dbo.NotificationRuleRecipient DROP CONSTRAINT CK_NotificationRuleRecipient_Kind;", StringComparison.Ordinal);
        var add = code.IndexOf("ALTER TABLE dbo.NotificationRuleRecipient ADD CONSTRAINT CK_NotificationRuleRecipient_Kind CHECK (", StringComparison.Ordinal);
        Assert.True(drop > 0 && add > drop);
        // el DROP solo si la definición vigente NO menciona CONTEXT
        var dropGuard = code[..drop];
        Assert.Contains("IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_NotificationRuleRecipient_Kind' AND parent_object_id = OBJECT_ID('dbo.NotificationRuleRecipient')", dropGuard);
        Assert.Contains("AND definition NOT LIKE '%CONTEXT%')", dropGuard);
        // el ADD solo si el CHECK no existe, en otro lote (GO de por medio)
        var between = code[drop..add];
        Assert.Matches(new Regex(@"^\s*GO\s*$", RegexOptions.Multiline), between);
        Assert.Contains("IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_NotificationRuleRecipient_Kind' AND parent_object_id = OBJECT_ID('dbo.NotificationRuleRecipient'))", between);
        var check = code[add..];
        Assert.Contains("(Kind = 'ROLE' AND RoleId IS NOT NULL AND UserId IS NULL AND Value IS NULL) OR", check);
        Assert.Contains("(Kind = 'USER' AND UserId IS NOT NULL AND RoleId IS NULL AND Value IS NULL) OR", check);
        Assert.Contains("(Kind IN ('EMAIL', 'PHONE', 'CONTEXT') AND Value IS NOT NULL AND RoleId IS NULL AND UserId IS NULL));", check);
        // los Kind del CHECK son exactamente los del dominio (CONTEXT incluido) y caben en VARCHAR(10)
        Assert.Contains(NotificationRecipientKinds.Context, NotificationRecipientKinds.All);
        Assert.All(NotificationRecipientKinds.All, k => Assert.Contains($"'{k}'", check));
        Assert.All(NotificationRecipientKinds.All, k => Assert.True(k.Length <= 10, k));
    }

    [Fact]
    public void The_contact_flag_is_added_with_col_length_and_default_zero()
    {
        var code = Statements(Section());
        Assert.Contains("IF COL_LENGTH(N'dbo.ClientContact', N'ReceivesEmailNotifications') IS NULL\n"
            + "    ALTER TABLE dbo.ClientContact ADD ReceivesEmailNotifications BIT NOT NULL CONSTRAINT DF_ClientContact_ReceivesEmailNotifications DEFAULT 0;", code);
    }

    [Fact]
    public void The_section_only_adds_no_delete_no_permissions_no_data_no_seeded_rules()
    {
        var code = Statements(Section());
        Assert.DoesNotContain("DELETE", code, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("INSERT", code, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("MERGE", code, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE ", code, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("dbo.Permission", code, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("dbo.RolePermission", code, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("dbo.NotificationRule ", code, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DROP TABLE", code, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DROP COLUMN", code, StringComparison.OrdinalIgnoreCase);
        // el único DROP es el del CHECK que se vuelve a crear
        Assert.Single(Regex.Matches(code, @"\bDROP\b", RegexOptions.IgnoreCase));
        Assert.Equal(3, Batches(Section()).Count());
    }

    // ---------------------------------------------------------------- contra SQL Server real

    private async Task ExecAsync(SqlConnection c, string sql)
    {
        await using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<object?> ScalarAsync(SqlConnection c, string sql)
    {
        await using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        return await cmd.ExecuteScalarAsync();
    }

    [SqlServerFact]
    public async Task On_sql_server_the_section_runs_twice_widens_the_old_check_and_adds_the_column_with_zero_for_existing_contacts()
    {
        await using var c = new SqlConnection(fx.ConnectionString);
        await c.OpenAsync();
        // la base como estaba en producción antes del lote: el CHECK viejo (sin CONTEXT) y sin la columna del contacto
        await ExecAsync(c, """
            IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_NotificationRuleRecipient_Kind')
                ALTER TABLE dbo.NotificationRuleRecipient DROP CONSTRAINT CK_NotificationRuleRecipient_Kind;
            ALTER TABLE dbo.NotificationRuleRecipient ADD CONSTRAINT CK_NotificationRuleRecipient_Kind CHECK (
                (Kind = 'ROLE' AND RoleId IS NOT NULL AND UserId IS NULL AND Value IS NULL) OR
                (Kind = 'USER' AND UserId IS NOT NULL AND RoleId IS NULL AND Value IS NULL) OR
                (Kind IN ('EMAIL', 'PHONE') AND Value IS NOT NULL AND RoleId IS NULL AND UserId IS NULL));
            """);
        var defaults = (string?)await ScalarAsync(c, """
            SELECT TOP 1 d.name FROM sys.default_constraints d JOIN sys.columns col ON col.object_id = d.parent_object_id AND col.column_id = d.parent_column_id
            WHERE d.parent_object_id = OBJECT_ID('dbo.ClientContact') AND col.name = 'ReceivesEmailNotifications'
            """);
        if (defaults is not null) await ExecAsync(c, $"ALTER TABLE dbo.ClientContact DROP CONSTRAINT [{defaults}];");
        if (await ScalarAsync(c, "SELECT COL_LENGTH('dbo.ClientContact', 'ReceivesEmailNotifications')") is not DBNull)
            await ExecAsync(c, "ALTER TABLE dbo.ClientContact DROP COLUMN ReceivesEmailNotifications;");
        await ExecAsync(c, "INSERT INTO dbo.ClientContact (ClientId, FullName, IsPrimary, IsActive) VALUES (1, N'Contacto de antes', 0, 1);");

        // dos corridas: la segunda no hace nada ni falla
        foreach (var _ in new[] { 1, 2 })
            foreach (var batch in Batches(Section()))
                await ExecAsync(c, batch);

        var definition = (string)(await ScalarAsync(c, "SELECT definition FROM sys.check_constraints WHERE name = 'CK_NotificationRuleRecipient_Kind' AND parent_object_id = OBJECT_ID('dbo.NotificationRuleRecipient')"))!;
        Assert.Contains("CONTEXT", definition);
        Assert.Equal(1, (int)(await ScalarAsync(c, "SELECT COUNT(*) FROM sys.check_constraints WHERE name = 'CK_NotificationRuleRecipient_Kind'"))!);
        Assert.False((bool)(await ScalarAsync(c, "SELECT TOP 1 ReceivesEmailNotifications FROM dbo.ClientContact WHERE FullName = N'Contacto de antes'"))!);
        Assert.Equal("DF_ClientContact_ReceivesEmailNotifications", (string?)await ScalarAsync(c, """
            SELECT d.name FROM sys.default_constraints d JOIN sys.columns col ON col.object_id = d.parent_object_id AND col.column_id = d.parent_column_id
            WHERE d.parent_object_id = OBJECT_ID('dbo.ClientContact') AND col.name = 'ReceivesEmailNotifications'
            """));

        // el CHECK nuevo admite CONTEXT con Value y sigue rechazando lo mal formado
        await using var db = fx.NewDb(1);
        var rule = new NotificationRule { PublicId = Guid.NewGuid(), TenantId = 1, EventCode = "SALES_ORDER_REQUESTED", Name = "Regla L243", IsActive = false };
        rule.Recipients.Add(new NotificationRuleRecipient { TenantId = 1, Kind = NotificationRecipientKinds.Context, Value = "SELLER" });
        db.NotificationRules.Add(rule);
        await db.SaveChangesAsync();
        var bad = Assert.ThrowsAsync<SqlException>(() => ExecAsync(c,
            $"INSERT INTO dbo.NotificationRuleRecipient (TenantId, NotificationRuleId, Kind, RoleId, Value) VALUES (1, {rule.NotificationRuleId}, 'CONTEXT', 1, N'SELLER');"));
        Assert.Contains("CK_NotificationRuleRecipient_Kind", (await bad).Message);
        var noValue = await Assert.ThrowsAsync<SqlException>(() => ExecAsync(c,
            $"INSERT INTO dbo.NotificationRuleRecipient (TenantId, NotificationRuleId, Kind) VALUES (1, {rule.NotificationRuleId}, 'CONTEXT');"));
        Assert.Contains("CK_NotificationRuleRecipient_Kind", noValue.Message);
        await Assert.ThrowsAsync<SqlException>(() => ExecAsync(c,
            $"INSERT INTO dbo.NotificationRuleRecipient (TenantId, NotificationRuleId, Kind, Value) VALUES (1, {rule.NotificationRuleId}, 'SELLER', N'x');"));
        Assert.Equal(1, await db.Set<NotificationRuleRecipient>().AsNoTracking().CountAsync(r => r.NotificationRuleId == rule.NotificationRuleId));
    }
}

/// <summary>
/// Lote 243 — el registro en DependencyInjection: el correo transaccional sale por el buzón local (SMTP, Mailpit) SOLO en Development con
/// Email:Smtp:Host (gana sobre Brevo aunque haya llave); en cualquier otro ambiente, sin Host o sin ambiente conocido, por Brevo. El contenido de la
/// orden de venta queda registrado como proveedor de contenido del aviso. Nunca se envía nada: solo se resuelve el contenedor real.
/// </summary>
public sealed class Lote243WiringTests
{
    private sealed class Env(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "Teikem.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static ServiceProvider Container(string? environment, string? smtpHost, string? brevoKey = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Teikem"] = "Server=model-only.invalid;Database=Teikem;User Id=x;Password=x;TrustServerCertificate=True",
            ["Jwt:SigningKey"] = new string('k', 64),
            ["Email:Smtp:Host"] = smtpHost,
            ["Brevo:ApiKey"] = brevoKey,
            ["Brevo:FromEmail"] = brevoKey is null ? null : "no-responder@prueba.test",
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(config);
        if (environment is not null) services.AddSingleton<IHostEnvironment>(new Env(environment));
        services.AddTeikemInfrastructure(config);
        return services.BuildServiceProvider();
    }

    [Theory]
    [InlineData("Development", "localhost", null, true)]
    [InlineData("Development", "localhost", "llave-falsa", true)]   // en desarrollo con Host, el buzón local gana aunque haya llave de Brevo
    [InlineData("Development", null, null, false)]
    [InlineData("Development", "  ", null, false)]
    [InlineData("Production", "localhost", null, false)]
    [InlineData("Staging", "localhost", null, false)]
    [InlineData("Test", "localhost", "llave-falsa", false)]
    [InlineData(null, "localhost", null, false)]   // sin ambiente conocido: nunca el SMTP
    public void The_email_sender_is_the_local_smtp_only_in_development_with_a_host(string? environment, string? host, string? brevoKey, bool local)
    {
        using var provider = Container(environment, host, brevoKey);
        var sender = provider.GetRequiredService<ITransactionalEmailSender>();
        if (local)
        {
            Assert.IsType<SmtpEmailSender>(sender);
            Assert.True(sender.IsLocalSink);
            Assert.True(sender.IsConfigured);
        }
        else
        {
            Assert.IsType<BrevoEmailSender>(sender);
            Assert.False(sender.IsLocalSink);
            Assert.Equal(brevoKey is not null, sender.IsConfigured);
        }
    }

    [Fact]
    public void The_sales_order_content_is_registered_as_the_content_provider_of_the_requested_event()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Teikem"] = "Server=model-only.invalid;Database=Teikem;User Id=x;Password=x;TrustServerCertificate=True",
            ["Jwt:SigningKey"] = new string('k', 64),
        }).Build();
        var services = new ServiceCollection();
        services.AddTeikemInfrastructure(config);
        var d = Assert.Single(services, x => x.ServiceType == typeof(INotificationContentProvider));
        Assert.Equal((typeof(SalesOrderEmailContent), ServiceLifetime.Scoped), (d.ImplementationType, d.Lifetime));
        Assert.Single(services, x => x.ServiceType == typeof(ITransactionalEmailSender));
        Assert.Contains(services, x => x.ServiceType == typeof(SmtpEmailSender));
        Assert.Contains(services, x => x.ServiceType == typeof(BrevoEmailSender));
    }

    [Fact]
    public void The_events_catalog_and_the_provider_agree_on_the_requested_event()
    {
        using var provider = Container("Production", null);
        using var scope = provider.CreateScope();
        var content = Assert.Single(scope.ServiceProvider.GetServices<INotificationContentProvider>());
        Assert.Contains(SalesEventCodes.OrderRequested, content.EventCodes);
        var def = scope.ServiceProvider.GetRequiredService<NotificationEventCatalog>().Find(SalesEventCodes.OrderRequested)!;
        Assert.Contains(def.Variables, v => v.Name == "orderDetail" && v.IsBlock);
        Assert.NotNull(def.ContextRecipient(SalesContextRecipients.Seller));
        Assert.NotNull(def.ContextRecipient(SalesContextRecipients.Client));
    }
}
