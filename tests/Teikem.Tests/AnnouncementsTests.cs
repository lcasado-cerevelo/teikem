using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Teikem.Api.Auth;
using Teikem.Api.Controllers;
using Teikem.Domain.Common;
using Teikem.Domain.Constants;
using Teikem.Domain.Identity;
using Teikem.Domain.Notifications;
using Teikem.Domain.Security;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Messaging;
using Teikem.Infrastructure.Notifications;
using Teikem.Infrastructure.Persistence.Scripts;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

// Lote 54: avisos manuales. Texto del destino, sección de update.sql, permiso (solo el administrador), API por reflexión.
public class AnnouncementsTests
{
    [Fact]
    public void The_audience_text_shows_up_to_three_names_and_how_many_more()
    {
        Assert.Equal("—", AnnouncementService.AudienceText(Array.Empty<string>()));
        Assert.Equal("Todas las personas", AnnouncementService.AudienceText(new[] { "Todas las personas" }));
        Assert.Equal("Gerencia, Ana, Luis", AnnouncementService.AudienceText(new[] { "Gerencia", "Ana", "Luis" }));
        Assert.Equal("Gerencia, Ana, Luis, +2", AnnouncementService.AudienceText(new[] { "Gerencia", "Ana", "Luis", "Pedro", "Rosa" }));
        Assert.True(AnnouncementService.AudienceText(new[] { new string('x', 400) }).Length <= 300);
    }

    [Fact]
    public void Exact_messages()
    {
        Assert.Equal("Escriba el título del aviso.", AnnouncementService.TitleRequiredMessage);
        Assert.Equal("El título del aviso no puede pasar de 150 caracteres.", AnnouncementService.TitleTooLongMessage);
        Assert.Equal("El texto del aviso no puede pasar de 1000 caracteres.", AnnouncementService.BodyTooLongMessage);
        Assert.Equal("Escoja al menos una persona, un rol o a todos.", AnnouncementService.NeedAudienceMessage);
        Assert.Equal("No hay personas a quienes enviar el aviso.", AnnouncementService.NobodyMessage);
    }

    private static string Section()
    {
        var sql = File.ReadAllText(Path.Combine(DatabaseInitializer.ResolveRepoRoot(null), "Diseño", "logistica-db-update.sql")).Replace("\r\n", "\n");
        var start = sql.IndexOf("2026-10-15 (i) — Avisos manuales (Lote 54)", StringComparison.Ordinal);
        Assert.True(start >= 0, "Falta la sección 2026-10-15 (i).");
        return SqlSections.From(sql, start);
    }

    [Fact]
    public void Update_sql_section_creates_the_table_adds_the_column_and_the_permission_only_for_the_tenant_admin()
    {
        var s = Section();
        Assert.Contains("IF OBJECT_ID(N'dbo.Announcement', N'U') IS NULL CREATE TABLE dbo.Announcement (", s);   // IF + CREATE en la MISMA línea (FrozenSqlTests)
        Assert.Contains("IF COL_LENGTH('dbo.UserNotification', 'AnnouncementId') IS NULL", s);
        Assert.Contains("IF NOT EXISTS (SELECT 1 FROM dbo.Permission WHERE Code = 'notifications.send')", s);
        Assert.Contains("JOIN dbo.Permission p ON p.Code = 'notifications.send'\nWHERE r.Name IN ('TenantAdmin')\n  AND NOT EXISTS (SELECT 1 FROM dbo.RolePermission rp WHERE rp.RoleId = r.RoleId AND rp.PermissionId = p.PermissionId);", s);
        Assert.DoesNotContain("DELETE", s, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DROP ", s, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_permission_is_admin_only_and_not_given_to_other_roles()
    {
        var p = Assert.Single(PermissionCatalog.All, x => x.Code == "notifications.send");
        Assert.Equal("ADMIN", p.Category);   // 2026-10-22: va en la sección Sistema, no en «Acceso al menú»
        Assert.Contains(PermissionCatalog.NotificationsSend, PermissionCatalog.RoleTemplates["TenantAdmin"]);
        foreach (var role in PermissionCatalog.RoleTemplates.Keys.Where(r => r != "TenantAdmin"))
            Assert.DoesNotContain(PermissionCatalog.NotificationsSend, PermissionCatalog.RoleTemplates[role]);
    }

    [Fact]
    public void Every_announcements_endpoint_requires_notifications_send_in_the_system_module_and_never_edits_or_deletes()
    {
        Assert.Equal(ModuleKeys.System, typeof(AnnouncementsController).GetCustomAttribute<RequireModuleAttribute>()!.ModuleKey);
        var actions = typeof(AnnouncementsController).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).ToList();
        Assert.Equal(4, actions.Count);   // People, Roles, Send, List
        foreach (var m in actions)
            Assert.Equal(RequirePermissionAttribute.Prefix + PermissionCatalog.NotificationsSend, m.GetCustomAttribute<RequirePermissionAttribute>()!.Policy);
        Assert.DoesNotContain(actions, m => m.GetCustomAttributes().Any(a => a.GetType().Name is "HttpDeleteAttribute" or "HttpPatchAttribute" or "HttpPutAttribute"));
        Assert.NotNull(typeof(AnnouncementsController).GetCustomAttribute<AuthorizeAttribute>());
        foreach (var p in actions.SelectMany(m => m.GetParameters())) Assert.DoesNotContain(p.Name, new[] { "userId", "tenantId", "senderId" });   // quién envía y la compañía salen del token
    }

    [Fact]
    public void The_announcement_is_not_an_audit_entity_and_has_no_edit_fields()
    {
        Assert.Null(typeof(Announcement).GetCustomAttribute<AuditEntityAttribute>());
        Assert.Null(typeof(Announcement).GetProperty("UpdatedAtUtc"));
        Assert.True(typeof(ITenantScoped).IsAssignableFrom(typeof(Announcement)));
    }
}

/// <summary>Lote 54 — avisos manuales contra SQL Server REAL (misma base desechable que la mensajería; compañías propias por prueba).</summary>
public sealed class AnnouncementsSqlTests(MessagingSqlFixture fx) : IClassFixture<MessagingSqlFixture>, IAsyncLifetime
{
    private static int _seed = 5000;
    private static int NextId() => Interlocked.Increment(ref _seed);
    private int A;
    private int B;
    private readonly CapturingLiveNotifier _live = new();

    public async Task InitializeAsync()
    {
        if (fx.ConnectionString is null) return;
        (A, B) = (await fx.NewTenantAsync("Compañía A"), await fx.NewTenantAsync("Compañía B"));
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<int> UserAsync(int tenantId, string name, bool active = true)
    {
        await using var db = fx.NewDb(new TenantContext { TenantId = tenantId });
        var mail = $"{name.ToLowerInvariant().Replace(' ', '.')}{NextId()}@x.com";
        var u = new ApplicationUser { UserName = mail, Email = mail, FullName = name, IsActive = active };
        db.Users.Add(u);
        await db.SaveChangesAsync();
        db.UserTenants.Add(new UserTenant { UserId = u.Id, TenantId = tenantId, StatusCodeId = 1 });
        await db.SaveChangesAsync();
        return u.Id;
    }

    private async Task<int> RoleAsync(int tenantId, string name, params int[] userIds)
    {
        await using var db = fx.NewDb(new TenantContext { TenantId = tenantId });
        var r = new Role { TenantId = tenantId, Name = name + NextId() };
        db.AppRoles.Add(r);
        await db.SaveChangesAsync();
        foreach (var uid in userIds) db.AppUserRoles.Add(new UserRole { UserId = uid, RoleId = r.RoleId, TenantId = tenantId });
        await db.SaveChangesAsync();
        return r.RoleId;
    }

    private (AnnouncementService Svc, NotificationInboxService Inbox, Teikem.Infrastructure.Persistence.TeikemDbContext Db) As(int tenantId, int userId)
    {
        var db = fx.NewDb(new TenantContext { TenantId = tenantId, UserId = userId });
        var ctx = db.TenantOf();
        var messaging = new MessagingService(db, ctx, new PresenceService(db), _live);
        return (new AnnouncementService(db, ctx, messaging, _live), new NotificationInboxService(db, ctx, new NotificationEventCatalog(new INotificationEventProvider[] { new TestEventProvider() })), db);
    }

    private async Task<List<UserNotification>> BellOf(int tenantId, int userId)
    {
        await using var db = fx.NewDb(new TenantContext { TenantId = tenantId });
        return await db.UserNotifications.AsNoTracking().Where(n => n.UserId == userId).ToListAsync();
    }

    private static async Task<ValidationException> Invalid(Func<Task> act) => await Assert.ThrowsAsync<ValidationException>(act);

    [SqlServerFact]
    public async Task An_announcement_to_several_people_reaches_each_one_in_the_bell_from_the_sender_and_nobody_else()
    {
        var ana = await UserAsync(A, "Ana Rivera");
        var luis = await UserAsync(A, "Luis Casado");
        var pedro = await UserAsync(A, "Pedro Pérez");
        var otro = await UserAsync(A, "Otro");
        var (svc, inbox, db) = As(A, ana);
        await using var _ = db;
        _live.Pushes.Clear();
        var sent = await svc.SendAsync(new AnnouncementRequest(new[] { luis, pedro, luis, ana }, null, null, "  Cierre mañana  ", "Traigan sus reportes."), default);
        Assert.Equal(("Cierre mañana", "Traigan sus reportes.", "Ana Rivera", false, 2, 0), (sent.Title, sent.Body, sent.SentByName, sent.ToAll, sent.RecipientCount, sent.ReadCount));   // ella misma no se avisa; el repetido cuenta una vez
        Assert.Equal("Luis Casado, Pedro Pérez", sent.AudienceText);

        var n = Assert.Single(await BellOf(A, luis));
        Assert.Equal(("ANNOUNCEMENT", "Cierre mañana", "Traigan sus reportes.", false), (n.EventCode, n.Title, n.Body, n.IsRead));
        Assert.Single(await BellOf(A, pedro));
        Assert.Empty(await BellOf(A, ana));
        Assert.Empty(await BellOf(A, otro));
        // en vivo, a las dos personas y después de guardar
        var push = Assert.Single(_live.Pushes, p => p.Event == "NoticeReceived");
        Assert.Equal(A, push.TenantId);
        Assert.Equal(new[] { luis, pedro }.OrderBy(i => i), push.UserIds);
        Assert.Equal("Ana Rivera", push.Payload.SenderName);

        // la campanita de Luis dice «De Ana Rivera»
        var (_, luisInbox, ldb) = As(A, luis);
        await using var _l = ldb;
        var item = Assert.Single((await luisInbox.ListAsync(false, 0, 20, default)).Items);
        Assert.Equal(("Cierre mañana", "Ana Rivera"), (item.Title, item.FromName));
        await luisInbox.MarkReadAsync(item.Id, default);
        Assert.Equal(1, (await svc.ListAsync(0, 25, default)).Items.Single().ReadCount);   // el historial cuenta quién lo leyó
    }

    [SqlServerFact]
    public async Task To_roles_and_to_all_the_active_people_are_resolved_today_without_repeats()
    {
        var ana = await UserAsync(A, "Ana");
        var g1 = await UserAsync(A, "Gerente Uno");
        var g2 = await UserAsync(A, "Gerente Dos");
        var inactivo = await UserAsync(A, "Inactivo", active: false);
        var deB = await UserAsync(B, "De otra compañía");
        var gerencia = await RoleAsync(A, "Gerencia", g1, g2, inactivo, ana);
        var ventas = await RoleAsync(A, "Ventas", g1);   // g1 está en los dos roles: un solo aviso
        var (svc, _, db) = As(A, ana);
        await using var _ = db;

        var byRole = await svc.SendAsync(new AnnouncementRequest(null, new[] { gerencia, ventas }, null, "A los roles", null), default);
        Assert.Equal(2, byRole.RecipientCount);   // g1 y g2 (Ana no se avisa, el inactivo no)
        Assert.StartsWith("Gerencia", byRole.AudienceText);
        Assert.Single(await BellOf(A, g1));
        Assert.Empty(await BellOf(A, inactivo));

        var toAll = await svc.SendAsync(new AnnouncementRequest(new[] { g1 }, null, true, "A todos", "Para todas las personas"), default);   // All pisa a UserIds
        Assert.Equal(("Todas las personas", true, 2), (toAll.AudienceText, toAll.ToAll, toAll.RecipientCount));
        Assert.Equal(2, (await BellOf(A, g1)).Count);
        Assert.Empty(await BellOf(B, deB));   // otra compañía: nada
        Assert.Empty(await BellOf(A, ana));
    }

    [SqlServerFact]
    public async Task Validations_use_the_exact_messages_and_nothing_is_saved_on_error()
    {
        var ana = await UserAsync(A, "Ana");
        var luis = await UserAsync(A, "Luis");
        var deB = await UserAsync(B, "De B");
        var rolB = await RoleAsync(B, "Rol de B", deB);
        var vacio = await RoleAsync(A, "Vacío");
        var (svc, _, db) = As(A, ana);
        await using var _ = db;
        AnnouncementRequest R(int[]? users = null, int[]? roles = null, bool? all = null, string? title = "T", string? text = null) => new(users, roles, all, title, text);

        Assert.Equal("Escriba el título del aviso.", (await Invalid(() => svc.SendAsync(R(new[] { luis }, title: "  "), default))).Message);
        Assert.Equal("El título del aviso no puede pasar de 150 caracteres.", (await Invalid(() => svc.SendAsync(R(new[] { luis }, title: new string('t', 151)), default))).Message);
        Assert.Equal("El texto del aviso no puede pasar de 1000 caracteres.", (await Invalid(() => svc.SendAsync(R(new[] { luis }, text: new string('x', 1001)), default))).Message);
        Assert.Equal("Escoja al menos una persona, un rol o a todos.", (await Invalid(() => svc.SendAsync(R(), default))).Message);
        Assert.Equal("Escoja al menos una persona, un rol o a todos.", (await Invalid(() => svc.SendAsync(R(Array.Empty<int>(), Array.Empty<int>()), default))).Message);
        Assert.Equal("Usuario no encontrado en esta compañía.", (await Invalid(() => svc.SendAsync(R(new[] { deB }), default))).Message);
        Assert.Equal("Usuario no encontrado en esta compañía.", (await Invalid(() => svc.SendAsync(R(new[] { 999999 }), default))).Message);
        Assert.Equal("Rol no encontrado.", (await Invalid(() => svc.SendAsync(R(null, new[] { rolB }), default))).Message);
        Assert.Equal("No hay personas a quienes enviar el aviso.", (await Invalid(() => svc.SendAsync(R(null, new[] { vacio }), default))).Message);
        Assert.Equal("No hay personas a quienes enviar el aviso.", (await Invalid(() => svc.SendAsync(R(new[] { ana }), default))).Message);   // solo ella misma
        await using var check = fx.NewDb(new TenantContext { TenantId = A });
        Assert.Equal(0, await check.Announcements.CountAsync());
        Assert.Equal(0, await check.UserNotifications.CountAsync(n => n.AnnouncementId != null));
        Assert.Equal(0, _live.Pushes.Count(p => p.Event == "NoticeReceived" && p.TenantId == A));
    }

    [SqlServerFact]
    public async Task The_history_is_per_company_newest_first_and_paged()
    {
        var ana = await UserAsync(A, "Ana");
        var luis = await UserAsync(A, "Luis");
        var deB = await UserAsync(B, "De B");
        var (svc, _, db) = As(A, ana);
        await using var _ = db;
        for (var i = 1; i <= 3; i++) await svc.SendAsync(new AnnouncementRequest(new[] { luis }, null, null, $"Aviso {i}", null), default);
        var (svcB, _, dbB) = As(B, deB);
        await using var _b = dbB;
        Assert.Empty((await svcB.ListAsync(0, 25, default)).Items);   // otra compañía no ve nada
        var page = await svc.ListAsync(0, 2, default);
        Assert.Equal(3, page.Total);
        Assert.Equal(new[] { "Aviso 3", "Aviso 2" }, page.Items.Select(a => a.Title));
        Assert.Equal(new[] { "Aviso 1" }, (await svc.ListAsync(2, 2, default)).Items.Select(a => a.Title));
    }

    [SqlServerFact]
    public async Task The_people_directory_for_announcements_filters_pages_and_reports_the_total_without_the_sender_or_inactive_people()
    {
        var ana = await UserAsync(A, "Ana Rivera");
        var ids = new List<int>();
        for (var i = 0; i < 7; i++) ids.Add(await UserAsync(A, $"Persona {i:00}"));
        var inactivo = await UserAsync(A, "Persona Inactiva", active: false);
        var deB = await UserAsync(B, "Persona de B");
        var (svc, _, db) = As(A, ana);
        await using var _ = db;
        var all = await svc.PeopleAsync(null, 0, 500, default);
        Assert.Equal(7, all.Total);
        Assert.DoesNotContain(all.Items, p => p.Id == ana || p.Id == inactivo || p.Id == deB);
        Assert.All(all.Items, p => Assert.False(string.IsNullOrEmpty(p.Email)));
        var filtered = await svc.PeopleAsync("Persona 0", 0, 500, default);
        Assert.Equal(7, filtered.Total);
        Assert.Equal(new[] { "Persona 00", "Persona 01", "Persona 02" }, (await svc.PeopleAsync("Persona", 0, 3, default)).Items.Select(p => p.Name));
        Assert.Equal(new[] { "Persona 03", "Persona 04" }, (await svc.PeopleAsync("Persona", 3, 2, default)).Items.Select(p => p.Name));
        Assert.Equal(1, (await svc.PeopleAsync("Persona 05", 0, 50, default)).Total);
        Assert.Empty((await svc.PeopleAsync("zzzz", 0, 50, default)).Items);
    }
}
