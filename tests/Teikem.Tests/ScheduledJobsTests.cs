using System.Reflection;
using Teikem.Api.Auth;
using Teikem.Api.Controllers;
using Teikem.Domain.Common;
using Teikem.Domain.Constants;
using Teikem.Domain.Jobs;
using Teikem.Infrastructure.Jobs;
using Teikem.Infrastructure.Persistence.Scripts;
using Xunit;

namespace Teikem.Tests;

// Lote 50 (P2.2): trabajos programados por compañía. Reglas puras del horario, sección de update.sql, permiso y API por reflexión.
public class ScheduledJobsTests
{
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.CreateCustomTimeZone("t", TimeSpan.FromHours(-4), "t", "t");
    private static readonly DateTime Now = new(2026, 10, 14, 12, 0, 0, DateTimeKind.Utc);   // 08:00 en la zona de prueba

    // ---- horario ----

    [Theory]
    [InlineData("INTERVAL", 60, null, "INTERVAL")]
    [InlineData("interval", 604800, null, "INTERVAL")]
    [InlineData("DAILY", null, "03:00", "DAILY")]
    [InlineData(" daily ", null, "3:05", "DAILY")]
    public void Valid_schedules_are_accepted(string kind, int? interval, string? dailyAt, string expectedKind)
    {
        var r = JobSchedule.Validate(kind, interval, dailyAt);
        Assert.Null(r.Error);
        Assert.Equal(expectedKind, r.Kind);
    }

    [Theory]
    [InlineData("INTERVAL", null, null, "intervalSeconds", "El intervalo debe estar entre 60 segundos y 7 días (604800).")]
    [InlineData("INTERVAL", 59, null, "intervalSeconds", "El intervalo debe estar entre 60 segundos y 7 días (604800).")]
    [InlineData("INTERVAL", 604801, null, "intervalSeconds", "El intervalo debe estar entre 60 segundos y 7 días (604800).")]
    [InlineData("DAILY", null, null, "dailyAt", "Indique la hora diaria (HH:mm).")]
    [InlineData("DAILY", null, "  ", "dailyAt", "Indique la hora diaria (HH:mm).")]
    [InlineData("DAILY", null, "25:00", "dailyAt", "La hora diaria no es válida; use HH:mm (24 horas).")]
    [InlineData("DAILY", null, "abc", "dailyAt", "La hora diaria no es válida; use HH:mm (24 horas).")]
    [InlineData("WEEKLY", null, null, "scheduleKind", "Tipo de horario desconocido: 'WEEKLY'. Use INTERVAL o DAILY.")]
    public void Invalid_schedules_use_the_exact_messages(string kind, int? interval, string? dailyAt, string field, string message)
    {
        var r = JobSchedule.Validate(kind, interval, dailyAt);
        Assert.Equal(field, r.Field);
        Assert.Equal(message, r.Error);
    }

    [Fact]
    public void Interval_counts_from_the_given_instant()
        => Assert.Equal(Now.AddSeconds(900), JobSchedule.Next("INTERVAL", 900, null, Now, Zone));

    [Fact]
    public void Daily_runs_today_if_the_hour_has_not_passed_and_tomorrow_otherwise()
    {
        // son las 08:00 locales: las 09:00 son hoy (13:00 UTC) y las 07:00 son mañana (11:00 UTC del día 15)
        Assert.Equal(new DateTime(2026, 10, 14, 13, 0, 0), JobSchedule.Next("DAILY", null, new TimeSpan(9, 0, 0), Now, Zone));
        Assert.Equal(new DateTime(2026, 10, 15, 11, 0, 0), JobSchedule.Next("DAILY", null, new TimeSpan(7, 0, 0), Now, Zone));
        // exactamente la hora de ahora: ya pasó, es mañana
        Assert.Equal(new DateTime(2026, 10, 15, 12, 0, 0), JobSchedule.Next("DAILY", null, new TimeSpan(8, 0, 0), Now, Zone));
    }

    [Fact]
    public void A_failure_retries_soon_three_times_and_then_goes_back_to_the_normal_schedule()
    {
        Assert.Equal(Now.AddMinutes(1), JobSchedule.NextAfterFailure("INTERVAL", 3600, null, 1, Now, Zone));
        Assert.Equal(Now.AddMinutes(2), JobSchedule.NextAfterFailure("INTERVAL", 3600, null, 2, Now, Zone));
        Assert.Equal(Now.AddMinutes(5), JobSchedule.NextAfterFailure("INTERVAL", 3600, null, 3, Now, Zone));
        Assert.Equal(Now.AddSeconds(3600), JobSchedule.NextAfterFailure("INTERVAL", 3600, null, 4, Now, Zone));
    }

    // ---- SQL, permiso y API ----

    private static string Section()
    {
        var sql = File.ReadAllText(Path.Combine(DatabaseInitializer.ResolveRepoRoot(null), "Diseño", "logistica-db-update.sql")).Replace("\r\n", "\n");
        var start = sql.IndexOf("2026-10-14 (f) — Trabajos programados por compañía (Lote 50, P2.2)", StringComparison.Ordinal);
        Assert.True(start > sql.IndexOf("2026-10-14 (e)", StringComparison.Ordinal), "Falta la sección 2026-10-14 (f) después de la (e).");
        return sql[start..];
    }

    [Fact]
    public void Update_sql_section_creates_both_tables_idempotently_with_the_unique_key_and_the_checks()
    {
        var s = Section();
        Assert.Contains("IF OBJECT_ID(N'dbo.ScheduledJob', N'U') IS NULL CREATE TABLE dbo.ScheduledJob (", s);   // IF + CREATE en la MISMA línea (FrozenSqlTests)
        Assert.Contains("IF OBJECT_ID(N'dbo.ScheduledJobRun', N'U') IS NULL CREATE TABLE dbo.ScheduledJobRun (", s);
        Assert.Contains("CREATE UNIQUE INDEX UX_ScheduledJob_Key ON dbo.ScheduledJob(TenantId, JobKey);", s);
        Assert.Contains("CHECK (ScheduleKind IN ('INTERVAL', 'DAILY'))", s);
        Assert.Contains("IntervalSeconds BETWEEN 60 AND 604800", s);
        Assert.Contains("CHECK (LastStatus IN ('NEVER', 'OK', 'FAILED', 'RUNNING'))", s);
        Assert.Contains("N'SCHEDULED_JOB'", s);
        Assert.DoesNotContain("DELETE", s, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DROP ", s, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("INSERT INTO dbo.ScheduledJob", s, StringComparison.OrdinalIgnoreCase);   // las filas por compañía las crea el sistema
    }

    [Fact]
    public void Update_sql_section_adds_the_permission_and_propagates_it_to_the_template_and_the_cloned_roles()
    {
        var s = Section();
        Assert.Contains("IF NOT EXISTS (SELECT 1 FROM dbo.Permission WHERE Code = 'jobs.manage')", s);
        Assert.Contains("JOIN dbo.Permission p ON p.Code = 'jobs.manage'\nWHERE r.Name IN ('TenantAdmin')\n  AND NOT EXISTS (SELECT 1 FROM dbo.RolePermission rp WHERE rp.RoleId = r.RoleId AND rp.PermissionId = p.PermissionId);", s);
    }

    [Fact]
    public void Permission_is_admin_only_for_tenant_admin()
    {
        var p = Assert.Single(PermissionCatalog.All, x => x.Code == "jobs.manage");
        Assert.Equal("ADMIN", p.Category);
        Assert.Contains(PermissionCatalog.JobsManage, PermissionCatalog.RoleTemplates["TenantAdmin"]);
        foreach (var role in PermissionCatalog.RoleTemplates.Keys.Where(r => r != "TenantAdmin"))
            Assert.DoesNotContain(PermissionCatalog.JobsManage, PermissionCatalog.RoleTemplates[role]);
    }

    [Fact]
    public void Entity_is_audited_and_the_runtime_columns_are_not()
    {
        Assert.Equal(EntityTypes.ScheduledJob, typeof(ScheduledJob).GetCustomAttribute<AuditEntityAttribute>()!.EntityTypeCode);
        foreach (var name in new[] { nameof(ScheduledJob.NextRunUtc), nameof(ScheduledJob.LastStatus), nameof(ScheduledJob.LockedBy), nameof(ScheduledJob.LockedUntilUtc), nameof(ScheduledJob.ConsecutiveFailures), nameof(ScheduledJob.ManualRequested) })
            Assert.NotNull(typeof(ScheduledJob).GetProperty(name)!.GetCustomAttribute<NotAuditedAttribute>());
        foreach (var name in new[] { nameof(ScheduledJob.IsEnabled), nameof(ScheduledJob.ScheduleKind), nameof(ScheduledJob.IntervalSeconds), nameof(ScheduledJob.DailyAt) })
            Assert.Null(typeof(ScheduledJob).GetProperty(name)!.GetCustomAttribute<NotAuditedAttribute>());
    }

    [Fact]
    public void Every_endpoint_of_the_jobs_api_requires_jobs_manage_in_the_system_module()
    {
        Assert.Equal(ModuleKeys.System, typeof(JobsController).GetCustomAttribute<RequireModuleAttribute>()!.ModuleKey);
        var actions = typeof(JobsController).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).ToList();
        Assert.Equal(5, actions.Count);   // List, Get, Update, RunNow, Runs
        foreach (var m in actions)
            Assert.Equal(RequirePermissionAttribute.Prefix + PermissionCatalog.JobsManage, m.GetCustomAttribute<RequirePermissionAttribute>()!.Policy);
        Assert.DoesNotContain(actions, m => m.GetCustomAttributes().Any(a => a.GetType().Name == "HttpDeleteAttribute"));   // nunca DELETE
    }

    [Fact]
    public void The_three_built_in_jobs_have_the_designed_defaults()
    {
        IScheduledJob purge = new JobRunPurgeJob(), sweep = new InventorySweepJob(), test = new TestJob();
        Assert.Equal(("JOB_RUN_PURGE", true, new TimeSpan(3, 0, 0)), (purge.Key, purge.DefaultEnabled, purge.DefaultDailyAt!.Value));
        Assert.Equal(("INVENTORY_SWEEP", true, new TimeSpan(2, 0, 0)), (sweep.Key, sweep.DefaultEnabled, sweep.DefaultDailyAt!.Value));
        Assert.Equal(("JOB_TEST", false), (test.Key, test.DefaultEnabled));
        Assert.Equal(100, JobRunPurgeJob.KeepPerJob);
    }
}
