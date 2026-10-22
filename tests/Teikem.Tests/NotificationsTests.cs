using System.Reflection;
using Teikem.Api.Auth;
using Teikem.Api.Controllers;
using Teikem.Domain.Common;
using Teikem.Domain.Constants;
using Teikem.Domain.Notifications;
using Teikem.Infrastructure.Notifications;
using Teikem.Infrastructure.Persistence.Scripts;
using Xunit;

namespace Teikem.Tests;

// Lote 51: avisos. Reglas puras (plantillas, validaciones, reintentos), catálogo de eventos, sección de update.sql, permiso y API por reflexión.
public class NotificationsTests
{
    private static readonly IReadOnlyDictionary<string, string> Values = new Dictionary<string, string> { ["order"] = "ORD-1", ["client"] = "Acme <SA>" };

    [Fact]
    public void Render_replaces_variables_and_a_missing_one_becomes_empty()
    {
        Assert.Equal("Orden ORD-1 de Acme <SA>", NotificationRules.Render("Orden {order} de {client}", Values));
        Assert.Equal("Orden ORD-1 de ", NotificationRules.Render("Orden {order} de {nope}", Values));
        Assert.Equal("sin variables", NotificationRules.Render("sin variables", Values));
        Assert.Equal(string.Empty, NotificationRules.Render(null, Values));
    }

    [Fact]
    public void Render_for_html_encodes_the_values()
        => Assert.Equal("Acme &lt;SA&gt;", NotificationRules.Render("{client}", Values, html: true));

    [Fact]
    public void Email_paragraphs_encode_the_text_and_keep_line_breaks()
        => Assert.Equal("<p>a &lt;b&gt;<br>c</p>", NotificationHtml.Paragraphs("a <b>\nc"));

    [Fact]
    public void VariablesOf_lists_each_variable_once()
        => Assert.Equal(new[] { "a", "b_2" }, NotificationRules.VariablesOf("{a} y {b_2} y {a} y {1x} y { z }"));

    [Theory]
    [InlineData("ana@correo.com", true)]
    [InlineData(" ana@correo.com ", true)]
    [InlineData("ana@correo", false)]
    [InlineData("ana correo@x.com", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Email_validation(string? v, bool ok) => Assert.Equal(ok, NotificationRules.IsValidEmail(v));

    [Theory]
    [InlineData("787-555-0101", true)]
    [InlineData("+1 (787) 555-0101", true)]
    [InlineData("12345", false)]
    [InlineData("787-555-ABCD", false)]
    [InlineData("1234567890123456", false)]
    [InlineData(null, false)]
    public void Phone_validation(string? v, bool ok) => Assert.Equal(ok, NotificationRules.IsValidPhone(v));

    [Fact]
    public void Channels_and_kinds_are_normalized_or_rejected()
    {
        Assert.Equal("EMAIL", NotificationRules.NormalizeChannel(" email "));
        Assert.Null(NotificationRules.NormalizeChannel("FAX"));
        Assert.Equal("ROLE", NotificationRules.NormalizeKind("role"));
        Assert.Null(NotificationRules.NormalizeKind("GROUP"));
    }

    [Fact]
    public void Retries_are_one_five_and_thirty_minutes_and_then_it_fails_for_good()
    {
        Assert.Equal((true, TimeSpan.FromMinutes(1)), NotificationRules.AfterFailure(1));
        Assert.Equal((true, TimeSpan.FromMinutes(5)), NotificationRules.AfterFailure(2));
        Assert.Equal((true, TimeSpan.FromMinutes(30)), NotificationRules.AfterFailure(3));
        Assert.False(NotificationRules.AfterFailure(4).Retry);
    }

    [Fact]
    public void Exact_messages()
    {
        Assert.Equal("Evento de aviso desconocido: 'X'.", NotificationRules.UnknownEventMessage("X"));
        Assert.Equal("Canal desconocido: 'FAX'. Use EMAIL, SMS, APP o PUSH.", NotificationRules.UnknownChannelMessage("FAX"));
        Assert.Equal("El canal SMS todavía no está configurado en Teikem.", NotificationRules.NotConfiguredMessage("SMS"));
        Assert.Equal("El canal EMAIL necesita un asunto.", NotificationRules.SubjectRequiredMessage("EMAIL"));
        Assert.Equal("La plantilla usa una variable que el evento no tiene: {zzz}.", NotificationRules.UnknownVariableMessage("zzz"));
        Assert.Equal("Tipo de destinatario desconocido: 'GROUP'. Use ROLE, USER, EMAIL o PHONE.", NotificationRules.UnknownRecipientKindMessage("GROUP"));
        Assert.Equal("Correo no válido: 'x'.", NotificationRules.InvalidEmailMessage("x"));
        Assert.Equal("Teléfono no válido: 'x'.", NotificationRules.InvalidPhoneMessage("x"));
    }

    // ---- catálogo de eventos ----

    [Fact]
    public void The_catalog_has_the_test_event_and_rejects_a_repeated_code()
    {
        var catalog = new NotificationEventCatalog(new INotificationEventProvider[] { new TestEventProvider() });
        var test = Assert.IsType<NotificationEventDef>(catalog.Find("test"));
        Assert.Equal("TEST", test.Code);
        Assert.Equal(new[] { "message", "user" }, test.Variables.Select(v => v.Name));
        Assert.Null(catalog.Find("NOPE"));
        Assert.Throws<InvalidOperationException>(() => new NotificationEventCatalog(new INotificationEventProvider[] { new TestEventProvider(), new TestEventProvider() }));
    }

    [Fact]
    public void The_default_texts_of_every_event_only_use_the_events_own_variables()
    {
        var catalog = new NotificationEventCatalog(new INotificationEventProvider[] { new TestEventProvider() });
        foreach (var e in catalog.All)
        {
            var allowed = e.Variables.Select(v => v.Name).ToHashSet();
            foreach (var text in new[] { e.DefaultSubjectEs, e.DefaultSubjectEn, e.DefaultBodyEs, e.DefaultBodyEn, e.LinkTemplate })
                Assert.All(NotificationRules.VariablesOf(text), v => Assert.Contains(v, allowed));
        }
    }

    // ---- SQL, permiso, auditoría y API ----

    private static string Section()
    {
        var sql = File.ReadAllText(Path.Combine(DatabaseInitializer.ResolveRepoRoot(null), "Diseño", "logistica-db-update.sql")).Replace("\r\n", "\n");
        var start = sql.IndexOf("2026-10-14 (g) — Avisos / notificaciones (Lote 51)", StringComparison.Ordinal);
        Assert.True(start >= 0, "Falta la sección 2026-10-14 (g).");
        return SqlSections.From(sql, start);
    }

    [Fact]
    public void Update_sql_section_creates_the_seven_tables_idempotently_with_the_guards()
    {
        var s = Section();
        foreach (var table in new[] { "NotificationRule", "NotificationRuleChannel", "NotificationRuleRecipient", "NotificationOutbox", "NotificationLog", "UserNotification", "UserNotificationPref" })
            Assert.Contains($"IF OBJECT_ID(N'dbo.{table}', N'U') IS NULL CREATE TABLE dbo.{table} (", s);   // IF + CREATE en la MISMA línea (FrozenSqlTests)
        Assert.Contains("CREATE UNIQUE INDEX UX_NotificationLog_Delivery ON dbo.NotificationLog(NotificationOutboxId, NotificationRuleId, Channel, Recipient);", s);
        Assert.Contains("CREATE UNIQUE INDEX UX_NotificationOutbox_Dedup ON dbo.NotificationOutbox(TenantId, EventCode, DedupKey) WHERE DedupKey IS NOT NULL;", s);
        Assert.Contains("CHECK (Channel IN ('EMAIL', 'SMS', 'APP', 'PUSH'))", s);
        Assert.Contains("CHECK (Status IN ('PENDING', 'SENT', 'FAILED', 'SKIPPED'))", s);
        Assert.Contains("N'NOTIFICATION_RULE'", s);
        Assert.DoesNotContain("DELETE", s, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DROP ", s, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Update_sql_section_adds_the_permission_to_the_template_and_the_cloned_roles()
    {
        var s = Section();
        Assert.Contains("IF NOT EXISTS (SELECT 1 FROM dbo.Permission WHERE Code = 'notifications.manage')", s);
        Assert.Contains("JOIN dbo.Permission p ON p.Code = 'notifications.manage'\nWHERE r.Name IN ('TenantAdmin')\n  AND NOT EXISTS (SELECT 1 FROM dbo.RolePermission rp WHERE rp.RoleId = r.RoleId AND rp.PermissionId = p.PermissionId);", s);
    }

    [Fact]
    public void Permission_is_admin_only_for_tenant_admin()
    {
        var p = Assert.Single(PermissionCatalog.All, x => x.Code == "notifications.manage");
        Assert.Equal("ADMIN", p.Category);
        Assert.Contains(PermissionCatalog.NotificationsManage, PermissionCatalog.RoleTemplates["TenantAdmin"]);
        foreach (var role in PermissionCatalog.RoleTemplates.Keys.Where(r => r != "TenantAdmin"))
            Assert.DoesNotContain(PermissionCatalog.NotificationsManage, PermissionCatalog.RoleTemplates[role]);
    }

    [Fact]
    public void The_rule_is_audited_and_the_queue_and_log_are_not_audit_entities()
    {
        Assert.Equal("NOTIFICATION_RULE", typeof(NotificationRule).GetCustomAttribute<AuditEntityAttribute>()!.EntityTypeCode);
        foreach (var t in new[] { typeof(NotificationOutbox), typeof(NotificationLog), typeof(UserNotification), typeof(UserNotificationPref), typeof(NotificationRuleChannel), typeof(NotificationRuleRecipient) })
            Assert.Null(t.GetCustomAttribute<AuditEntityAttribute>());
    }

    [Fact]
    public void Every_notifications_endpoint_requires_the_permission_in_the_system_module_and_the_inbox_is_open_to_any_signed_in_user()
    {
        Assert.Equal(ModuleKeys.System, typeof(NotificationsController).GetCustomAttribute<RequireModuleAttribute>()!.ModuleKey);
        var actions = typeof(NotificationsController).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).ToList();
        Assert.Equal(9, actions.Count);   // Events, Channels, List, Create, Get, Update, Test, TestEvent, Log
        foreach (var m in actions)
        {
            // 2026-10-22: las lecturas (eventos, canales, reglas, bitácora) las acepta también «entrar y ver»; crear, cambiar y probar exigen gestionar
            var policy = m.GetCustomAttribute<RequirePermissionAttribute>()!.Policy;
            var isRead = m.GetCustomAttributes().Any(a => a.GetType().Name == "HttpGetAttribute");
            Assert.Equal(RequirePermissionAttribute.Prefix + PermissionCatalog.NotificationsManage + (isRead ? "|notifications.view" : string.Empty), policy);
        }
        Assert.DoesNotContain(actions, m => m.GetCustomAttributes().Any(a => a.GetType().Name == "HttpDeleteAttribute"));   // nunca DELETE

        var inbox = typeof(InboxController).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).ToList();
        Assert.Equal(6, inbox.Count);
        Assert.All(inbox, m => Assert.Null(m.GetCustomAttribute<RequirePermissionAttribute>()));
        Assert.NotNull(typeof(InboxController).GetCustomAttribute<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>());
        // la bandeja nunca recibe el usuario ni la compañía del request
        foreach (var p in inbox.SelectMany(m => m.GetParameters()))
            Assert.DoesNotContain(p.Name, new[] { "userId", "tenantId" });
    }
}
