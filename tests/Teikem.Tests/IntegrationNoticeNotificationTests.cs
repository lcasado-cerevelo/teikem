using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Constants;
using Teikem.Domain.Integrations;
using Teikem.Domain.Notifications;
using Teikem.Domain.Security;
using Teikem.Infrastructure.Notifications;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// 2026-10-24 — los avisos de llegada y de salida que crea una corrida avisan UNA vez por tipo (por mucho que sean) a los roles que tienen el permiso de crearlos: llegada = warehouse.receive, salida = exitnotice.create.
/// </summary>
public sealed class IntegrationNoticeNotificationTests
{
    private static async Task<WmsFixture> WorldAsync()
    {
        var f = await WmsFixture.CreateAsync(s =>
        {
            s.AddSingleton<INotificationEventProvider, IntegrationEventProvider>();
            s.AddSingleton<NotificationEventCatalog>();
            s.AddSingleton<INotificationPublisher, NotificationPublisher>();
            s.AddSingleton<IntegrationNotifier>();
        });
        Role Add(string name, params string[] permissions)
        {
            var role = new Role { TenantId = WmsFixture.TenantId, Name = name, IsActive = true };
            foreach (var code in permissions)
            {
                var p = f.Db.Set<Permission>().Local.FirstOrDefault(x => x.Code == code) ?? new Permission { Code = code, CategoryLookupId = 1 };
                role.Permissions.Add(new RolePermission { Permission = p });
            }
            f.Db.Set<Role>().Add(role);
            return role;
        }
        Add("Recibo", PermissionCatalog.WarehouseReceive);
        Add("Despacho", PermissionCatalog.ExitNoticeCreate);
        Add("Ambos", PermissionCatalog.WarehouseReceive, PermissionCatalog.ExitNoticeCreate);
        Add("Nada", PermissionCatalog.InventoryView);
        await f.Db.SaveChangesAsync();
        return f;
    }

    private static readonly Integration Integration = new() { IntegrationId = 7, TenantId = WmsFixture.TenantId, Name = "Axiscare avisos por correo" };
    private static readonly IntegrationRun Run = new() { IntegrationRunId = 40, TenantId = WmsFixture.TenantId, IntegrationId = 7 };

    [Fact]
    public async Task Diez_avisos_de_salida_de_una_corrida_son_una_sola_notificacion_a_los_roles_con_el_permiso()
    {
        await using var f = await WorldAsync();
        var notifier = f.Get<IntegrationNotifier>();
        var created = new List<(string Target, int Count, string Document)>
        {
            (DocumentTargets.ExitNotice, 6, "Despacho 1.pdf"), (DocumentTargets.ExitNotice, 4, "Despacho 2.pdf"), (DocumentTargets.ArrivalNotice, 1, "Envio.pdf"),
        };

        await notifier.PublishNoticesAsync(Integration, Run, created, default);

        var outbox = await f.Db.NotificationOutbox.AsNoTracking().ToListAsync();
        Assert.Equal(2, outbox.Count);   // una de salida y una de llegada, no una por aviso
        var exit = outbox.Single(o => o.EventCode == IntegrationEventCodes.ExitNotices);
        Assert.Contains("\"count\":\"10\"", exit.PayloadJson);
        Assert.Contains("Despacho 1.pdf", exit.PayloadJson);

        var rules = await f.Db.NotificationRules.Include(r => r.Recipients).Include(r => r.Channels).AsNoTracking().ToListAsync();
        var exitRule = rules.Single(r => r.ScopeKey == IntegrationNotifier.ExitScopeKey);
        var exitRoles = await f.Db.Set<Role>().Where(r => exitRule.Recipients.Select(x => x.RoleId).Contains(r.RoleId)).Select(r => r.Name).OrderBy(n => n).ToListAsync();
        Assert.Equal(new[] { "Ambos", "Despacho" }, exitRoles);   // los roles con exitnotice.create; ni «Recibo» ni «Nada»
        Assert.True(exitRule.IsActive);
        Assert.Equal(new[] { NotificationChannels.App, NotificationChannels.Email }, exitRule.Channels.Select(c => c.Channel).OrderBy(c => c).ToArray());
        var arrivalRule = rules.Single(r => r.ScopeKey == IntegrationNotifier.ArrivalScopeKey);
        var arrivalRoles = await f.Db.Set<Role>().Where(r => arrivalRule.Recipients.Select(x => x.RoleId).Contains(r.RoleId)).Select(r => r.Name).OrderBy(n => n).ToListAsync();
        Assert.Equal(new[] { "Ambos", "Recibo" }, arrivalRoles);
    }

    [Fact]
    public async Task La_misma_corrida_no_avisa_dos_veces_y_sin_avisos_creados_no_avisa()
    {
        await using var f = await WorldAsync();
        var notifier = f.Get<IntegrationNotifier>();
        var created = new List<(string Target, int Count, string Document)> { (DocumentTargets.ArrivalNotice, 2, "Envio.pdf") };

        await notifier.PublishNoticesAsync(Integration, Run, created, default);
        await notifier.PublishNoticesAsync(Integration, Run, created, default);
        await notifier.PublishNoticesAsync(Integration, new IntegrationRun { IntegrationRunId = 41, TenantId = WmsFixture.TenantId, IntegrationId = 7 }, new List<(string, int, string)>(), default);

        Assert.Equal(1, await f.Db.NotificationOutbox.AsNoTracking().CountAsync());
    }

    [Fact]
    public async Task Si_ningun_rol_tiene_el_permiso_la_regla_queda_apagada()
    {
        await using var f = await WorldAsync();
        f.Db.RolePermissions.RemoveRange(await f.Db.RolePermissions.Include(rp => rp.Permission).Where(rp => rp.Permission!.Code == PermissionCatalog.ExitNoticeCreate).ToListAsync());
        await f.Db.SaveChangesAsync();

        await f.Get<IntegrationNotifier>().PublishNoticesAsync(Integration, Run, new List<(string, int, string)> { (DocumentTargets.ExitNotice, 3, "D.pdf") }, default);

        var rule = await f.Db.NotificationRules.AsNoTracking().SingleAsync(r => r.ScopeKey == IntegrationNotifier.ExitScopeKey);
        Assert.False(rule.IsActive);
    }
}
