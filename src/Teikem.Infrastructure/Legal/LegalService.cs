using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Teikem.Domain.Constants;
using Teikem.Domain.Security;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Legal;

/// <summary>403 con el código 'legal_acceptance_required': falta aceptar la versión vigente del documento legal.</summary>
public sealed class LegalAcceptanceRequiredException() : TeikemException(LegalRules.RequiredMessage, 403, LegalRules.ErrorCode);

/// <summary>
/// Lote 99: documento legal único (política de privacidad + términos de uso) y su aceptación por usuario. El documento se sirve en es/en sin sesión (también
/// lo lee quien aún no entra: invitaciones y portales). Aceptar guarda versión, idioma, IP y navegador; solo vale la versión vigente.
/// </summary>
public sealed class LegalService(TeikemDbContext db, ITenantContext tenant, IOptions<LegalOptions> options, IMemoryCache cache, ISecurityEventWriter security)
{
    public LegalDocumentDto GetCurrent(string? lang) => LegalText.Build(string.Equals(lang, "en", StringComparison.OrdinalIgnoreCase) ? "en" : "es", options.Value);

    public bool Enforced => options.Value.RequireAcceptance;

    private static string CacheKey(int userId) => $"legal:{userId}:{LegalRules.CurrentVersion}";

    /// <summary>¿Aceptó el usuario la versión vigente? Solo se recuerda el sí (una aceptación no se retira).</summary>
    public async Task<bool> HasAcceptedAsync(int userId, CancellationToken ct)
    {
        if (cache.TryGetValue(CacheKey(userId), out _)) return true;
        var ok = await db.LegalAcceptances.AsNoTracking().AnyAsync(a => a.UserId == userId && a.Version == LegalRules.CurrentVersion, ct);
        if (ok) cache.Set(CacheKey(userId), true, TimeSpan.FromMinutes(30));
        return ok;
    }

    public async Task<LegalStatusDto> StatusAsync(CancellationToken ct)
    {
        var userId = (tenant.UserId ?? throw new UnauthorizedException());
        var last = await db.LegalAcceptances.AsNoTracking().Where(a => a.UserId == userId).OrderByDescending(a => a.AcceptedAtUtc).FirstOrDefaultAsync(ct);
        var accepted = last is not null && last.Version == LegalRules.CurrentVersion;
        return new LegalStatusDto(LegalRules.CurrentVersion, Enforced && !accepted, accepted, last?.AcceptedAtUtc, last?.Version);
    }

    public async Task<LegalStatusDto> AcceptAsync(LegalAcceptRequest req, CancellationToken ct)
    {
        var userId = (tenant.UserId ?? throw new UnauthorizedException());
        if (req.Version != LegalRules.CurrentVersion) throw new ConflictException(LegalRules.VersionMismatchMessage);
        if (!await db.LegalAcceptances.AnyAsync(a => a.UserId == userId && a.Version == LegalRules.CurrentVersion, ct))
        {
            db.LegalAcceptances.Add(new LegalAcceptance
            {
                UserId = userId, Version = LegalRules.CurrentVersion, Lang = string.Equals(req.Lang, "en", StringComparison.OrdinalIgnoreCase) ? "en" : "es",
                IpAddress = Trunc(tenant.IpAddress, 64), UserAgent = Trunc(tenant.UserAgent, 400), AcceptedAtUtc = DateTime.UtcNow,
            });
            await db.SaveChangesAsync(ct);
            await security.WriteAsync(SecurityEventTypes.Login, SecurityOutcomes.Success, userId, tenant.TenantId, new { stage = "legal_accepted", version = LegalRules.CurrentVersion }, ct);
        }
        cache.Set(CacheKey(userId), true, TimeSpan.FromMinutes(30));
        return await StatusAsync(ct);
    }

    /// <summary>
    /// Quién aceptó y quién no la versión vigente, entre los usuarios de la compañía activa (con membresía). Ordenado: pendientes primero. Los usuarios
    /// de otras compañías no se ven. Paginado por el servidor; filtros: estado (accepted|pending) y búsqueda por nombre o correo.
    /// </summary>
    public async Task<PagedResult<LegalAcceptanceRowDto>> ListAsync(string? status, string? q, int skip, int take, CancellationToken ct)
    {
        var tid = tenant.TenantId ?? throw new UnauthorizedException();
        take = Math.Clamp(take <= 0 ? 25 : take, 1, 200);
        var members = db.UserTenants.AsNoTracking().IgnoreQueryFilters().Where(m => m.TenantId == tid).Select(m => m.UserId);
        var users = db.Users.AsNoTracking().Where(u => members.Contains(u.Id) && u.IsActive);
        if (!string.IsNullOrWhiteSpace(q)) { var s = q.Trim(); users = users.Where(u => (u.FullName ?? "").Contains(s) || (u.Email ?? "").Contains(s)); }
        var rows = users.Select(u => new
        {
            u.Id, u.FullName, u.Email,
            Last = db.LegalAcceptances.Where(a => a.UserId == u.Id && a.Version == LegalRules.CurrentVersion).Select(a => (DateTime?)a.AcceptedAtUtc).FirstOrDefault(),
            Any = db.LegalAcceptances.Where(a => a.UserId == u.Id).OrderByDescending(a => a.AcceptedAtUtc).Select(a => a.Version).FirstOrDefault(),
        });
        if (status == "accepted") rows = rows.Where(r => r.Last != null);
        else if (status == "pending") rows = rows.Where(r => r.Last == null);
        var total = await rows.CountAsync(ct);
        var page = await rows.OrderBy(r => r.Last != null).ThenBy(r => r.FullName).ThenBy(r => r.Id).Skip(Math.Max(0, skip)).Take(take).ToListAsync(ct);
        return new PagedResult<LegalAcceptanceRowDto>(page.Select(r => new LegalAcceptanceRowDto(r.FullName, r.Email, r.Last is not null, r.Last, r.Any)).ToList(), total, skip, take);
    }

    private static string? Trunc(string? s, int max) => s is null ? null : s.Length <= max ? s : s[..max];
}
