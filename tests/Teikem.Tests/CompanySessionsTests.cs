using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Api.Auth;
using Teikem.Api.Controllers;
using Teikem.Domain.Audit;
using Teikem.Domain.Catalogs;
using Teikem.Domain.Constants;
using Teikem.Domain.Entities;
using Teikem.Domain.Identity;
using Teikem.Domain.Security;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote F10 — Seguridad y auditoría → Sesiones y MFA: el administrador ve las sesiones activas de TODA la compañía (no solo las
/// suyas), revoca una o "las demás", y la propia nunca se revoca desde la lista. Las de otra compañía no se ven ni se tocan
/// (TenantId del contexto). Además, la Actividad trae los códigos del tipo y del resultado para pintar Cambio / Evento / Alerta.
/// </summary>
public class CompanySessionsTests
{
    private sealed class CapturingSecurityEventWriter : ISecurityEventWriter
    {
        public List<(string EventType, string Outcome, int? UserId, string Detail)> Events { get; } = new();

        public Task WriteAsync(string eventType, string outcome, int? userId = null, int? tenantId = null, object? detail = null, CancellationToken ct = default)
        {
            Events.Add((eventType, outcome, userId, detail is null ? "" : JsonSerializer.Serialize(detail)));
            return Task.CompletedTask;
        }
    }

    private sealed record Seed(WmsFixture F, long Mine, long MineOther, long Ana, long AnaDevice, long Expired, long Revoked, long OtherTenant);

    private static async Task<Seed> SeedAsync()
    {
        var f = await WmsFixture.CreateAsync(s =>
        {
            s.AddSingleton<CapturingSecurityEventWriter>();
            s.AddSingleton<ISecurityEventWriter>(sp => sp.GetRequiredService<CapturingSecurityEventWriter>());
            s.AddSingleton<CompanySessionService>();
        });
        f.Db.Users.Add(new ApplicationUser { Id = 1, UserName = "yo@t.local", Email = "yo@t.local", FullName = "Yo Mismo", IsActive = true });
        f.Db.Users.Add(new ApplicationUser { Id = 2, UserName = "ana@t.local", Email = "ana@t.local", FullName = null, IsActive = true });
        f.Db.UserDevices.Add(new UserDevice { UserDeviceId = 7, TenantId = WmsFixture.TenantId, Code = "ZEBRA-01", Name = "Muelle 1", IsActive = true });
        var now = DateTime.UtcNow;
        RefreshToken Token(int user, int tenant, DateTime issued, DateTime? revoked = null, DateTime? expires = null, int? device = null, string? ip = null) => new()
        {
            UserId = user, TenantId = tenant, TokenHash = Guid.NewGuid().ToString("N"), IssuedAtUtc = issued, ExpiresAtUtc = expires ?? now.AddDays(30),
            RevokedAtUtc = revoked, UserDeviceId = device, IpAddress = ip, DeviceInfo = "Mozilla/5.0 (Windows NT 10.0) Chrome/120",
        };
        var mine = Token(1, WmsFixture.TenantId, now.AddMinutes(-1), ip: "10.0.0.1");
        var mineOther = Token(1, WmsFixture.TenantId, now.AddHours(-5));
        var ana = Token(2, WmsFixture.TenantId, now.AddHours(-2), ip: "10.0.0.2");
        var anaDevice = Token(2, WmsFixture.TenantId, now.AddHours(-3), device: 7);
        var expired = Token(2, WmsFixture.TenantId, now.AddDays(-40), expires: now.AddDays(-1));
        var revoked = Token(2, WmsFixture.TenantId, now.AddHours(-1), revoked: now.AddMinutes(-30));
        var other = Token(2, WmsFixture.OtherTenantId, now.AddHours(-1));
        f.Db.RefreshTokens.AddRange(mine, mineOther, ana, anaDevice, expired, revoked);
        await f.Db.SaveChangesAsync();
        using (f.AsTenant(WmsFixture.OtherTenantId))
        {
            f.Db.RefreshTokens.Add(other);
            await f.Db.SaveChangesAsync();
        }
        f.Db.ChangeTracker.Clear();
        return new Seed(f, mine.RefreshTokenId, mineOther.RefreshTokenId, ana.RefreshTokenId, anaDevice.RefreshTokenId, expired.RefreshTokenId,
            revoked.RefreshTokenId, other.RefreshTokenId);
    }

    private static bool IsRevoked(WmsFixture f, long id)
    {
        f.Db.ChangeTracker.Clear();
        return f.Db.RefreshTokens.IgnoreQueryFilters().AsNoTracking().Single(t => t.RefreshTokenId == id).RevokedAtUtc != null;
    }

    [Fact]
    public async Task List_shows_active_sessions_of_every_user_in_the_company_marking_the_current_one()
    {
        var s = await SeedAsync();
        var list = await s.F.Get<CompanySessionService>().ListAsync(s.Mine, default);

        // solo las activas de la compañía, de la más reciente a la más vieja (ni vencida, ni revocada, ni de otra compañía)
        Assert.Equal(new[] { s.Mine, s.Ana, s.AnaDevice, s.MineOther }, list.Select(x => x.Id));
        var mine = list[0];
        Assert.True(mine.IsCurrent);
        Assert.Equal("Yo Mismo", mine.UserName);
        Assert.Equal("10.0.0.1", mine.IpAddress);
        Assert.Single(list, x => x.IsCurrent);
        // sin nombre, el correo; la sesión de aparato trae código y nombre del aparato
        Assert.Equal("ana@t.local", list[1].UserName);
        Assert.False(list[1].IsDevice);
        Assert.True(list[2].IsDevice);
        Assert.Equal("ZEBRA-01 · Muelle 1", list[2].DeviceName);
    }

    [Fact]
    public async Task Revoke_one_session_of_another_user_writes_token_revoked()
    {
        var s = await SeedAsync();
        await s.F.Get<CompanySessionService>().RevokeAsync(s.Ana, s.Mine, default);

        Assert.True(IsRevoked(s.F, s.Ana));
        Assert.False(IsRevoked(s.F, s.AnaDevice));
        var ev = Assert.Single(s.F.Get<CapturingSecurityEventWriter>().Events);
        Assert.Equal(SecurityEventTypes.TokenRevoked, ev.EventType);
        Assert.Equal(2, ev.UserId);
        Assert.Contains("company_session", ev.Detail);
    }

    [Fact]
    public async Task Current_session_is_not_revoked_from_the_list_and_foreign_or_inactive_ones_are_404()
    {
        var s = await SeedAsync();
        var svc = s.F.Get<CompanySessionService>();

        var conflict = await Assert.ThrowsAsync<ConflictException>(() => svc.RevokeAsync(s.Mine, s.Mine, default));
        Assert.Equal(CompanySessionService.CurrentSessionMessage, conflict.Message);
        Assert.False(IsRevoked(s.F, s.Mine));

        await Assert.ThrowsAsync<NotFoundException>(() => svc.RevokeAsync(s.OtherTenant, s.Mine, default));
        await Assert.ThrowsAsync<NotFoundException>(() => svc.RevokeAsync(s.Expired, s.Mine, default));
        await Assert.ThrowsAsync<NotFoundException>(() => svc.RevokeAsync(s.Revoked, s.Mine, default));
        Assert.False(IsRevoked(s.F, s.OtherTenant));
    }

    [Fact]
    public async Task Revoke_others_closes_every_company_session_but_the_current_one()
    {
        var s = await SeedAsync();
        var result = await s.F.Get<CompanySessionService>().RevokeOthersAsync(s.Mine, default);

        Assert.Equal(3, result.Revoked);
        Assert.False(IsRevoked(s.F, s.Mine));
        Assert.True(IsRevoked(s.F, s.MineOther));
        Assert.True(IsRevoked(s.F, s.Ana));
        Assert.True(IsRevoked(s.F, s.AnaDevice));
        Assert.False(IsRevoked(s.F, s.OtherTenant));
        var ev = Assert.Single(s.F.Get<CapturingSecurityEventWriter>().Events);
        Assert.Equal(1, ev.UserId);
        Assert.Contains("\"count\":3", ev.Detail);
        Assert.Single(await s.F.Get<CompanySessionService>().ListAsync(s.Mine, default));
    }

    [Fact]
    public void Session_endpoints_require_admin_audit_and_admin_users_and_revoke_others_requires_aal2()
    {
        var t = typeof(AuditController);
        Assert.Equal(new[] { RequirePermissionAttribute.Prefix + PermissionCatalog.AdminAudit },
            t.GetCustomAttributes<RequirePermissionAttribute>(inherit: true).Select(a => a.Policy));
        Assert.Null(t.GetCustomAttribute<AllowAnonymousAttribute>());

        var list = t.GetMethod(nameof(AuditController.Sessions))!;
        Assert.Equal("sessions", list.GetCustomAttribute<HttpGetAttribute>()!.Template);
        Assert.Empty(list.GetCustomAttributes<RequirePermissionAttribute>());

        var revoke = t.GetMethod(nameof(AuditController.RevokeSession))!;
        Assert.Equal("sessions/{id:long}", revoke.GetCustomAttribute<HttpDeleteAttribute>()!.Template);
        Assert.Equal(new[] { RequirePermissionAttribute.Prefix + PermissionCatalog.AdminUsers },
            revoke.GetCustomAttributes<RequirePermissionAttribute>().Select(a => a.Policy));

        var others = t.GetMethod(nameof(AuditController.RevokeOtherSessions))!;
        Assert.Equal("sessions/revoke-others", others.GetCustomAttribute<HttpPostAttribute>()!.Template);
        Assert.Equal(new[] { RequirePermissionAttribute.Prefix + PermissionCatalog.AdminUsers },
            others.GetCustomAttributes<RequirePermissionAttribute>().Select(a => a.Policy));
        Assert.NotNull(others.GetCustomAttribute<RequireAal2Attribute>());
        foreach (var m in new[] { list, revoke, others })
            Assert.DoesNotContain(m.GetParameters(), p => string.Equals(p.Name, "tenantId", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Activity_rows_carry_the_type_and_outcome_codes()
    {
        var f = await WmsFixture.CreateAsync(s => s.AddSingleton<AuditQueryService>());
        LookupCode L(int id, string entity, string code, string es) => new() { LookupCodeId = id, Entity = entity, InternalCode = code, LabelJson = $"{{\"es\":\"{es}\"}}", IsActive = true };
        f.Db.LookupCodes.AddRange(
            L(9001, LookupDomains.SecurityEventType, SecurityEventTypes.PermissionDenied, "Permiso denegado"),
            L(9002, LookupDomains.SecurityOutcome, SecurityOutcomes.Blocked, "Bloqueado"),
            L(9003, LookupDomains.AuditAction, AuditActions.Update, "Modificación"),
            L(9004, LookupDomains.EntityType, "TENANT", "Compañía"));
        f.Db.Users.Add(new ApplicationUser { Id = 1, UserName = "yo@t.local", Email = "yo@t.local", FullName = "Yo Mismo", IsActive = true });
        f.Db.SecurityEvents.Add(new SecurityEvent { TenantId = WmsFixture.TenantId, UserId = 1, EventTypeLookupId = 9001, OutcomeLookupId = 9002, DetailJson = "{\"permission\":\"admin.users\"}" });
        f.Db.AuditLogs.Add(new AuditLog { TenantId = WmsFixture.TenantId, EntityTypeLookupId = 9004, EntityId = 1, ActionLookupId = 9003, UserId = 1, ChangesJson = "{\"SessionDays\":{\"from\":30,\"to\":15}}" });
        await f.Db.SaveChangesAsync();
        f.Lookups.Load(f.Db.LookupCodes.IgnoreQueryFilters().AsNoTracking().ToList());

        var page = await f.Get<AuditQueryService>().GetActivityAsync("all", null, null, null, 0, 50, default);

        var sec = Assert.Single(page.Items, r => r.Kind == "security");
        Assert.Equal(SecurityEventTypes.PermissionDenied, sec.TypeCode);
        Assert.Equal(SecurityOutcomes.Blocked, sec.OutcomeCode);
        Assert.Equal("Permiso denegado · Bloqueado", sec.Type);
        var chg = Assert.Single(page.Items, r => r.Kind == "change");
        Assert.Equal(AuditActions.Update, chg.TypeCode);
        Assert.Null(chg.OutcomeCode);
        Assert.Equal("Modificación · Compañía #1", chg.Type);
    }
}
