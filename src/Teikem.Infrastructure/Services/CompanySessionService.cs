using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote F10 — sesiones activas de TODA la compañía activa (pantalla Seguridad y auditoría → Sesiones y MFA; maestro, módulo D:
/// "sesiones revocables… revocación por dispositivo o global"). `GET /auth/sessions` solo da las del propio usuario; aquí el
/// administrador ve las de todos y revoca una o todas las demás. La propia sesión nunca se revoca desde la lista (se cierra con
/// Salir), igual que en Mi cuenta. El TenantId sale del contexto: `RefreshToken` es `ITenantScoped` y además se filtra explícito.
/// Revocar marca `RevokedAtUtc` (el refresh deja de servir); el access token vivo dura lo que le quede (15 minutos como máximo):
/// no se cambia el SecurityStamp, que cerraría también las sesiones del usuario en sus otras compañías.
/// </summary>
public sealed class CompanySessionService(TeikemDbContext db, ITenantContext tenant, ISecurityEventWriter security)
{
    /// <summary>409 al intentar revocar la propia sesión desde la lista.</summary>
    public const string CurrentSessionMessage = "La sesión actual no se revoca desde la lista; use Salir.";

    public async Task<IReadOnlyList<CompanySessionDto>> ListAsync(long currentSessionId, CancellationToken ct)
    {
        var tenantId = tenant.TenantId ?? throw new ForbiddenException("Sin compañía activa.");
        var now = DateTime.UtcNow;
        var tokens = await db.RefreshTokens.AsNoTracking()
            .Where(t => t.TenantId == tenantId && t.RevokedAtUtc == null && t.ExpiresAtUtc > now)
            .OrderByDescending(t => t.IssuedAtUtc).ToListAsync(ct);
        if (tokens.Count == 0) return [];

        var userIds = tokens.Select(t => t.UserId).Distinct().ToList();
        var users = await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.FullName, u.Email }).ToDictionaryAsync(u => u.Id, ct);
        var deviceIds = tokens.Where(t => t.UserDeviceId != null).Select(t => t.UserDeviceId!.Value).Distinct().ToList();
        var devices = deviceIds.Count == 0
            ? new Dictionary<int, string>()
            : await db.UserDevices.AsNoTracking().IgnoreQueryFilters().Where(d => deviceIds.Contains(d.UserDeviceId))
                .ToDictionaryAsync(d => d.UserDeviceId, d => string.IsNullOrWhiteSpace(d.Name) ? d.Code : $"{d.Code} · {d.Name}", ct);

        return tokens.Select(t =>
        {
            users.TryGetValue(t.UserId, out var u);
            string? device = t.UserDeviceId is int did ? devices.GetValueOrDefault(did) : null;
            return new CompanySessionDto(t.RefreshTokenId, t.UserId, u?.FullName ?? u?.Email, u?.Email, t.DeviceInfo, device, t.UserDeviceId != null,
                t.IpAddress, t.IssuedAtUtc, t.ExpiresAtUtc, t.RefreshTokenId == currentSessionId);
        }).ToList();
    }

    /// <summary>Revoca UNA sesión de la compañía (de cualquier usuario). 409 si es la propia; 404 si no existe o ya no está activa.</summary>
    public async Task RevokeAsync(long sessionId, long currentSessionId, CancellationToken ct)
    {
        var tenantId = tenant.TenantId ?? throw new ForbiddenException("Sin compañía activa.");
        if (sessionId == currentSessionId) throw new ConflictException(CurrentSessionMessage);
        var now = DateTime.UtcNow;
        var rt = await db.RefreshTokens.FirstOrDefaultAsync(t => t.RefreshTokenId == sessionId && t.TenantId == tenantId
                     && t.RevokedAtUtc == null && t.ExpiresAtUtc > now, ct)
                 ?? throw new NotFoundException("Sesión", sessionId, feminine: true);
        rt.RevokedAtUtc = now;
        db.SuppressAudit = true;
        await db.SaveChangesAsync(ct);
        db.SuppressAudit = false;
        await security.WriteAsync(SecurityEventTypes.TokenRevoked, SecurityOutcomes.Success, rt.UserId, tenantId,
            new { scope = "company_session", session = sessionId, by = tenant.UserId }, ct);
    }

    /// <summary>"Cerrar las demás sesiones": revoca todas las sesiones activas de la compañía salvo la de quien lo pide.</summary>
    public async Task<RevokeSessionsResultDto> RevokeOthersAsync(long currentSessionId, CancellationToken ct)
    {
        var tenantId = tenant.TenantId ?? throw new ForbiddenException("Sin compañía activa.");
        var now = DateTime.UtcNow;
        var tokens = await db.RefreshTokens
            .Where(t => t.TenantId == tenantId && t.RevokedAtUtc == null && t.ExpiresAtUtc > now && t.RefreshTokenId != currentSessionId)
            .ToListAsync(ct);
        foreach (var t in tokens) t.RevokedAtUtc = now;
        if (tokens.Count > 0)
        {
            db.SuppressAudit = true;
            await db.SaveChangesAsync(ct);
            db.SuppressAudit = false;
        }
        await security.WriteAsync(SecurityEventTypes.TokenRevoked, SecurityOutcomes.Success, tenant.UserId, tenantId,
            new { scope = "company_others", count = tokens.Count, users = tokens.Select(t => t.UserId).Distinct().Count() }, ct);
        return new RevokeSessionsResultDto(tokens.Count);
    }
}
