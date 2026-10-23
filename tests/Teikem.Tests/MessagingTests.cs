using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Teikem.Api.Auth;
using Teikem.Api.Controllers;
using Teikem.Api.Messaging;
using Teikem.Domain.Constants;
using Teikem.Domain.Messaging;
using Teikem.Infrastructure.Messaging;
using Teikem.Infrastructure.Persistence.Scripts;
using Xunit;

namespace Teikem.Tests;

// Lote 53: mensajería. Reglas puras, sección de update.sql, permiso (todos los roles existentes, ninguno nuevo por omisión) y API/hub por reflexión.
public class MessagingTests
{
    [Fact]
    public void The_direct_key_is_the_same_whoever_writes_first()
    {
        Assert.Equal("3-7", MessagingRules.DirectKey(3, 7));
        Assert.Equal("3-7", MessagingRules.DirectKey(7, 3));
    }

    [Theory]
    [InlineData("  hola  ", "hola")]
    [InlineData("a\r\nb", "a\nb")]
    [InlineData("   ", null)]
    [InlineData(null, null)]
    public void Body_is_trimmed_and_empty_is_null(string? input, string? expected) => Assert.Equal(expected, MessagingRules.NormalizeBody(input));

    [Fact]
    public void Preview_is_one_line_and_cut_at_200()
    {
        Assert.Equal("a b c", MessagingRules.Preview("a\n  b\r\n\tc"));
        var p = MessagingRules.Preview(new string('x', 500));
        Assert.Equal(200, p.Length);
        Assert.EndsWith("…", p);
        Assert.Equal(200, MessagingRules.Preview(new string('y', 200)).Length);
    }

    [Fact]
    public void A_group_without_a_title_is_named_after_the_other_people()
    {
        Assert.Equal("Ana", MessagingRules.GroupName(null, new[] { "Ana" }));
        Assert.Equal("Ana, Luis", MessagingRules.GroupName(null, new[] { "Ana", "Luis" }));
        Assert.Equal("Ana, Luis, +2", MessagingRules.GroupName(" ", new[] { "Ana", "Luis", "Pedro", "Rosa" }));
        Assert.Equal("Gerencia", MessagingRules.GroupName(" Gerencia ", new[] { "Ana", "Luis" }));
        Assert.Equal("—", MessagingRules.GroupName(null, Array.Empty<string>()));
    }

    [Fact]
    public void One_person_without_a_role_is_a_direct_thread_otherwise_a_group()
    {
        Assert.Equal("DIRECT", MessagingRules.KindOf(1, false));
        Assert.Equal("GROUP", MessagingRules.KindOf(1, true));    // un rol, aunque hoy tenga una sola persona, es grupo
        Assert.Equal("GROUP", MessagingRules.KindOf(3, false));
    }

    [Fact]
    public void Unread_means_from_others_newer_than_last_read_and_than_the_entry()
    {
        Assert.True(MessagingRules.IsUnread(10, senderId: 2, me: 1, lastRead: 5, joinedAfter: 0));
        Assert.False(MessagingRules.IsUnread(10, senderId: 1, me: 1, lastRead: 5, joinedAfter: 0));   // lo mío nunca
        Assert.False(MessagingRules.IsUnread(5, senderId: 2, me: 1, lastRead: 5, joinedAfter: 0));    // ya leído
        Assert.False(MessagingRules.IsUnread(8, senderId: 2, me: 1, lastRead: 0, joinedAfter: 8));    // anterior a que entrara
    }

    [Fact]
    public void Online_means_seen_less_than_90_seconds_ago()
    {
        var now = new DateTime(2026, 10, 15, 12, 0, 0, DateTimeKind.Utc);
        Assert.True(MessagingRules.IsOnline(now.AddSeconds(-89), now));
        Assert.False(MessagingRules.IsOnline(now.AddSeconds(-90), now));
        Assert.False(MessagingRules.IsOnline(null, now));
    }

    [Fact]
    public void Exact_messages()
    {
        Assert.Equal("Escriba el mensaje.", MessagingRules.BodyRequiredMessage);
        Assert.Equal("El mensaje no puede pasar de 2000 caracteres.", MessagingRules.BodyTooLongMessage);
        Assert.Equal("El nombre del grupo no puede pasar de 100 caracteres.", MessagingRules.TitleTooLongMessage);
        Assert.Equal("Escoja al menos una persona o un rol.", MessagingRules.NeedRecipientsMessage);
        Assert.Equal("No puede escribirse a sí mismo.", MessagingRules.CannotWriteSelfMessage);
        Assert.Equal("Usuario no encontrado en esta compañía.", MessagingRules.UserNotFoundMessage);
        Assert.Equal("Rol no encontrado.", MessagingRules.RoleNotFoundMessage);
        Assert.Equal("Solo quien creó el grupo puede agregar personas.", MessagingRules.OnlyCreatorAddsMessage);
        Assert.Equal("Una conversación entre dos personas no se abandona.", MessagingRules.DirectNoLeaveMessage);
    }

    // ---- SQL, permiso y API ----

    private static string Section()
    {
        var sql = File.ReadAllText(Path.Combine(DatabaseInitializer.ResolveRepoRoot(null), "Diseño", "logistica-db-update.sql")).Replace("\r\n", "\n");
        var start = sql.IndexOf("2026-10-15 (h) — Mensajería interna con hilos (Lote 53)", StringComparison.Ordinal);
        Assert.True(start >= 0, "Falta la sección 2026-10-15 (h).");
        return SqlSections.From(sql, start);
    }

    [Fact]
    public void Update_sql_section_creates_the_four_tables_idempotently_with_the_unique_keys()
    {
        var s = Section();
        foreach (var table in new[] { "MessageThread", "MessageThreadMember", "Message", "UserPresence" })
            Assert.Contains($"IF OBJECT_ID(N'dbo.{table}', N'U') IS NULL CREATE TABLE dbo.{table} (", s);   // IF + CREATE en la MISMA línea (FrozenSqlTests)
        Assert.Contains("CREATE UNIQUE INDEX UX_MessageThread_Direct ON dbo.MessageThread(TenantId, DirectKey) WHERE DirectKey IS NOT NULL;", s);
        Assert.Contains("CREATE UNIQUE INDEX UX_MessageThreadMember ON dbo.MessageThreadMember(MessageThreadId, UserId);", s);
        Assert.Contains("CHECK ((Kind = 'DIRECT' AND DirectKey IS NOT NULL AND Title IS NULL) OR (Kind = 'GROUP' AND DirectKey IS NULL))", s);
        Assert.DoesNotContain("DELETE", s, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DROP ", s, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Update_sql_section_adds_the_permission_to_every_existing_role_and_only_adds()
    {
        var s = Section();
        Assert.Contains("IF NOT EXISTS (SELECT 1 FROM dbo.Permission WHERE Code = 'messages.use')", s);
        Assert.Contains("JOIN dbo.Permission p ON p.Code = 'messages.use'\nWHERE NOT EXISTS (SELECT 1 FROM dbo.RolePermission rp WHERE rp.RoleId = r.RoleId AND rp.PermissionId = p.PermissionId);", s);
    }

    [Fact]
    public void The_permission_is_in_every_role_template_but_a_new_role_starts_without_it()
    {
        Assert.Contains(PermissionCatalog.All, p => p.Code == "messages.use");
        foreach (var (role, perms) in PermissionCatalog.RoleTemplates)
            Assert.Contains(PermissionCatalog.MessagesUse, perms);
        // el catálogo de permisos es lo que se ofrece al armar un rol nuevo: no se marca solo (el portal de clientes lo recibirá solo si el dueño lo asigna)
        Assert.Equal(8, PermissionCatalog.RoleTemplates.Count);   // Lote 57: + Ventas; Lote 104: + Servicio al cliente
    }

    [Fact]
    public void The_domain_entities_are_not_audit_entities_and_messages_have_no_edit_fields()
    {
        foreach (var t in new[] { typeof(MessageThread), typeof(MessageThreadMember), typeof(Message), typeof(UserPresence) })
            Assert.Null(t.GetCustomAttribute<Teikem.Domain.Common.AuditEntityAttribute>());
        Assert.Null(typeof(Message).GetProperty("UpdatedAtUtc"));
        Assert.Null(typeof(Message).GetProperty("IsDeleted"));
    }

    [Fact]
    public void Every_messages_endpoint_requires_messages_use_and_presence_only_a_session()
    {
        var actions = typeof(MessagesController).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).ToList();
        Assert.Equal(15, actions.Count);   // People, Roles, Threads, Create, Get, Send, Read, AddMembers, Leave, Mute, Unread + (Lote 212) Suggested, About, Responsibles, SetResponsibles
        foreach (var m in actions)
            Assert.Equal(RequirePermissionAttribute.Prefix + PermissionCatalog.MessagesUse, m.GetCustomAttribute<RequirePermissionAttribute>()!.Policy);
        Assert.DoesNotContain(actions, m => m.GetCustomAttributes().Any(a => a.GetType().Name == "HttpDeleteAttribute"));   // los mensajes no se borran
        Assert.DoesNotContain(actions, m => m.GetCustomAttributes().Any(a => a.GetType().Name is "HttpPatchAttribute"));    // ni se editan
        foreach (var p in actions.SelectMany(m => m.GetParameters())) Assert.DoesNotContain(p.Name, new[] { "userId", "tenantId", "senderId" });   // quién escribe y la compañía salen del token
        Assert.NotNull(typeof(MessagesController).GetCustomAttribute<AuthorizeAttribute>());
        var ping = Assert.Single(typeof(PresenceController).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly));
        Assert.Null(ping.GetCustomAttribute<RequirePermissionAttribute>());
        Assert.NotNull(typeof(PresenceController).GetCustomAttribute<AuthorizeAttribute>());
    }

    [Fact]
    public void The_messaging_mode_endpoints_are_user_administration_not_messaging()
    {
        var actions = typeof(MessagingModesController).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).ToList();
        Assert.Equal(2, actions.Count);   // List, Set
        foreach (var m in actions)
            Assert.Equal(RequirePermissionAttribute.Prefix + PermissionCatalog.AdminUsers, m.GetCustomAttribute<RequirePermissionAttribute>()!.Policy);
        Assert.NotNull(typeof(MessagingModesController).GetCustomAttribute<AuthorizeAttribute>());
    }

    [Fact]
    public void The_live_hub_requires_a_session_and_cannot_receive_messages_from_clients()
    {
        Assert.NotNull(typeof(MessagesHub).GetCustomAttribute<AuthorizeAttribute>());
        // el hub solo tiene Ping (presencia): escribir es por la API, donde se aplican permisos y filtros
        var methods = typeof(MessagesHub).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Select(m => m.Name).OrderBy(n => n).ToList();
        Assert.Equal(new[] { "OnConnectedAsync", "OnDisconnectedAsync", "Ping" }, methods);
        Assert.Equal("t7:u3", MessagesHub.Group(7, 3));
        Assert.True(typeof(Hub).IsAssignableFrom(typeof(MessagesHub)));
    }
}
