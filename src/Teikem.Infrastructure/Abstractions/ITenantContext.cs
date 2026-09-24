namespace Teikem.Infrastructure.Abstractions;

/// <summary>
/// Contexto de ejecución del request: el TenantId sale del principal autenticado (JWT), NUNCA del request.
/// Lo llena TenantContextMiddleware a partir de los claims; los seeders y jobs lo fijan explícitamente.
/// </summary>
public interface ITenantContext
{
    int? TenantId { get; }
    int? UserId { get; }
    bool IsAuthenticated { get; }
    bool IsPlatformAdmin { get; }
    string Lang { get; }
    /// <summary>Solo para operaciones de plataforma (aprovisionar tenants, seeders). Apaga el filtro global.</summary>
    bool TenantFilterDisabled { get; }
    Guid CorrelationId { get; }
    string? IpAddress { get; }
    string? UserAgent { get; }
    /// <summary>Instante de la última verificación AAL2 (step-up) que trae el access token, si alguna.</summary>
    DateTime? Aal2VerifiedAtUtc { get; }
}

/// <summary>Implementación mutable (scoped). El middleware y los seeders la configuran.</summary>
public sealed class TenantContext : ITenantContext
{
    public int? TenantId { get; set; }
    public int? UserId { get; set; }
    public bool IsAuthenticated { get; set; }
    public bool IsPlatformAdmin { get; set; }
    public string Lang { get; set; } = Domain.Common.MultilingualText.DefaultLang;
    public bool TenantFilterDisabled { get; set; }
    public Guid CorrelationId { get; set; } = Guid.NewGuid();
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public DateTime? Aal2VerifiedAtUtc { get; set; }

    public int RequireTenantId() => TenantId ?? throw new Exceptions.ForbiddenException("No hay tenant activo en la sesión.");
    public int RequireUserId() => UserId ?? throw new Exceptions.UnauthorizedException();

    /// <summary>Ejecuta un bloque con el filtro de tenant apagado (uso interno de plataforma).</summary>
    public IDisposable BypassTenantFilter()
    {
        var prev = TenantFilterDisabled;
        TenantFilterDisabled = true;
        return new Restore(() => TenantFilterDisabled = prev);
    }

    /// <summary>Ejecuta un bloque como un tenant concreto (seeders / aprovisionamiento).</summary>
    public IDisposable As(int tenantId, int? userId = null)
    {
        var (t, u) = (TenantId, UserId);
        TenantId = tenantId;
        if (userId.HasValue) UserId = userId;
        return new Restore(() => { TenantId = t; UserId = u; });
    }

    private sealed class Restore(Action a) : IDisposable { public void Dispose() => a(); }
}
