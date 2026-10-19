using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Integrations;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 135 (P11) — límite de uso por llave: ventana deslizante de 60 s en memoria (por instancia del servidor). Devuelve cuántas solicitudes quedan y, si se pasó, en cuántos segundos vuelve a servir.
/// </summary>
public sealed class ApiRateLimiter
{
    private readonly ConcurrentDictionary<int, Queue<DateTime>> _hits = new();

    public (bool Allowed, int Remaining, int RetryAfterSeconds) TryAcquire(int keyId, int limitPerMinute, DateTime? nowUtc = null)
    {
        var now = nowUtc ?? DateTime.UtcNow;
        var queue = _hits.GetOrAdd(keyId, _ => new Queue<DateTime>());
        lock (queue)
        {
            while (queue.Count > 0 && now - queue.Peek() >= TimeSpan.FromMinutes(1)) queue.Dequeue();
            if (queue.Count >= limitPerMinute)
                return (false, 0, Math.Max(1, (int)Math.Ceiling((queue.Peek().AddMinutes(1) - now).TotalSeconds)));
            queue.Enqueue(now);
            return (true, limitPerMinute - queue.Count, 0);
        }
    }

    /// <summary>Para las pruebas: olvida lo contado de una llave.</summary>
    public void Reset(int keyId) => _hits.TryRemove(keyId, out _);
}

/// <summary>
/// Lote 135 (P11) — llaves de API de las integraciones de tipo API: crear (el secreto se muestra UNA vez y solo se guarda su hash SHA-256), listar, cambiar el límite de uso, revocar y autenticar
/// (llave + secreto → access token de 15 minutos con los alcances de la integración, a nombre de su cuenta de servicio y, si tiene cliente, limitado a él).
/// </summary>
public sealed class ApiKeyService(TeikemDbContext db, ITenantContext tenant, JwtTokenService jwt, ApiRateLimiter limiter, Microsoft.Extensions.Options.IOptions<JwtOptions> jwtOptions)
{
    // ---------------------------------------------------------------- administración (integrations.manage)

    public async Task<IReadOnlyList<ApiKeyDto>> ListAsync(Guid integrationPublicId, CancellationToken ct)
    {
        var integration = await LoadApiIntegrationAsync(integrationPublicId, ct);
        var keys = await db.IntegrationApiKeys.AsNoTracking().Where(k => k.IntegrationId == integration.IntegrationId).OrderByDescending(k => k.IntegrationApiKeyId).ToListAsync(ct);
        return keys.Select(ToDto).ToList();
    }

    public async Task<ApiKeyCreatedDto> CreateAsync(Guid integrationPublicId, ApiKeyCreateRequest req, CancellationToken ct)
    {
        var tenantId = ((TenantContext)tenant).RequireTenantId();
        var integration = await LoadApiIntegrationAsync(integrationPublicId, ct);
        if (!integration.IsActive) throw new StatusRuleException(IntegrationRules.InactiveMessage);
        var label = req.Label?.Trim();
        if (string.IsNullOrEmpty(label)) throw new ValidationException("label", ApiKeyRules.LabelRequiredMessage);
        if (label.Length > ApiKeyRules.LabelMax) throw new ValidationException("label", ApiKeyRules.LabelMaxMessage);
        var limit = req.RateLimitPerMinute ?? ApiKeyRules.DefaultRateLimit;
        if (!ApiKeyRules.IsValidRateLimit(limit)) throw new ValidationException("rateLimitPerMinute", ApiKeyRules.RateLimitMessage);
        if (req.ExpiresAtUtc is DateTime e && e <= DateTime.UtcNow) throw new ValidationException("expiresAtUtc", "La fecha de vencimiento debe ser futura.");
        if (await db.IntegrationApiKeys.CountAsync(k => k.IntegrationId == integration.IntegrationId && k.IsActive, ct) >= ApiKeyRules.MaxKeysPerIntegration) throw new ConflictException(ApiKeyRules.TooManyKeysMessage);

        var secret = ApiKeyRules.NewSecret();
        var key = new IntegrationApiKey
        {
            TenantId = tenantId, IntegrationId = integration.IntegrationId, KeyId = ApiKeyRules.NewKeyId(), SecretHash = ApiKeyRules.HashSecret(secret), SecretLast4 = ApiKeyRules.Last4(secret),
            Label = label, RateLimitPerMinute = limit, ExpiresAtUtc = req.ExpiresAtUtc, CreatedBy = tenant.UserId,
        };
        db.IntegrationApiKeys.Add(key);
        await db.SaveChangesAsync(ct);
        return new ApiKeyCreatedDto(ToDto(key), secret);   // el secreto sale aquí y en ningún otro lado
    }

    public async Task<ApiKeyDto> UpdateAsync(Guid integrationPublicId, string keyId, ApiKeyPatchRequest req, CancellationToken ct)
    {
        var integration = await LoadApiIntegrationAsync(integrationPublicId, ct);
        var key = await db.IntegrationApiKeys.FirstOrDefaultAsync(k => k.IntegrationId == integration.IntegrationId && k.KeyId == keyId, ct) ?? throw new NotFoundException("Llave de API");
        if (!key.IsActive) throw new ConflictException(ApiKeyRules.RevokedMessage);
        if (req.Label is not null)
        {
            var l = req.Label.Trim();
            if (l.Length == 0) throw new ValidationException("label", ApiKeyRules.LabelRequiredMessage);
            if (l.Length > ApiKeyRules.LabelMax) throw new ValidationException("label", ApiKeyRules.LabelMaxMessage);
            key.Label = l;
        }
        if (req.RateLimitPerMinute is int r)
        {
            if (!ApiKeyRules.IsValidRateLimit(r)) throw new ValidationException("rateLimitPerMinute", ApiKeyRules.RateLimitMessage);
            key.RateLimitPerMinute = r;
        }
        await db.SaveChangesAsync(ct);
        return ToDto(key);
    }

    public async Task<ApiKeyDto> RevokeAsync(Guid integrationPublicId, string keyId, CancellationToken ct)
    {
        var integration = await LoadApiIntegrationAsync(integrationPublicId, ct);
        var key = await db.IntegrationApiKeys.FirstOrDefaultAsync(k => k.IntegrationId == integration.IntegrationId && k.KeyId == keyId, ct) ?? throw new NotFoundException("Llave de API");
        if (!key.IsActive) throw new ConflictException(ApiKeyRules.RevokedMessage);
        key.IsActive = false;
        key.RevokedAtUtc = DateTime.UtcNow;
        key.RevokedBy = tenant.UserId;
        await db.SaveChangesAsync(ct);
        limiter.Reset(key.IntegrationApiKeyId);
        return ToDto(key);
    }

    /// <summary>Bitácora reciente de las solicitudes de una llave (para soporte).</summary>
    public async Task<IReadOnlyList<ApiLogDto>> LogAsync(Guid integrationPublicId, string keyId, int take, CancellationToken ct)
    {
        var integration = await LoadApiIntegrationAsync(integrationPublicId, ct);
        var key = await db.IntegrationApiKeys.AsNoTracking().FirstOrDefaultAsync(k => k.IntegrationId == integration.IntegrationId && k.KeyId == keyId, ct) ?? throw new NotFoundException("Llave de API");
        return await db.IntegrationApiLogs.AsNoTracking().Where(l => l.IntegrationApiKeyId == key.IntegrationApiKeyId).OrderByDescending(l => l.IntegrationApiLogId).Take(Math.Clamp(take, 1, 200))
            .Select(l => new ApiLogDto(l.CreatedAtUtc, l.Method, l.Path, l.StatusCode, l.DurationMs)).ToListAsync(ct);
    }

    private async Task<Integration> LoadApiIntegrationAsync(Guid publicId, CancellationToken ct)
    {
        var integration = await db.Integrations.AsNoTracking().FirstOrDefaultAsync(i => i.PublicId == publicId, ct) ?? throw new NotFoundException(IntegrationService.NotFoundLabel);
        if (integration.ConnectorKind != IntegrationConnectorKinds.Api) throw new ValidationException("connectorKind", ApiKeyRules.NotAnApiIntegrationMessage);
        return integration;
    }

    private static ApiKeyDto ToDto(IntegrationApiKey k)
        => new(k.IntegrationApiKeyId, k.KeyId, k.Label, k.SecretLast4, k.RateLimitPerMinute, k.IsActive, k.ExpiresAtUtc, k.LastUsedUtc, k.CreatedAtUtc, k.RevokedAtUtc);

    // ---------------------------------------------------------------- autenticar (anónimo: llave + secreto → token)

    public sealed record Authenticated(IntegrationApiKey Key, Integration Integration, Teikem.Domain.Identity.ApplicationUser ServiceUser, int? ClientId, string? ClientPublicId);

    /// <summary>Valida la llave y el secreto sin revelar cuál de los dos falló (siempre «Credenciales inválidas.») y emite el access token. Respeta el límite de uso de la llave.</summary>
    public async Task<(ApiTokenDto Token, int KeyId)> IssueTokenAsync(ApiTokenRequest req, CancellationToken ct)
    {
        var keyId = req.ClientId?.Trim() ?? string.Empty;
        var key = await db.IntegrationApiKeys.IgnoreQueryFilters().FirstOrDefaultAsync(k => k.KeyId == keyId, ct);
        // el hash se compara siempre (con un secreto cualquiera si la llave no existe) para no dar pistas por el tiempo de respuesta
        var secretOk = ApiKeyRules.VerifySecret(req.ClientSecret, key?.SecretHash ?? ApiKeyRules.HashSecret("sin-llave"));
        if (key is null || !secretOk || !key.IsActive || (key.ExpiresAtUtc is DateTime exp && exp <= DateTime.UtcNow)) throw new UnauthorizedException(ApiKeyRules.InvalidCredentialsMessage);
        var integration = await db.Integrations.IgnoreQueryFilters().FirstOrDefaultAsync(i => i.IntegrationId == key.IntegrationId, ct);
        if (integration is null || !integration.IsActive || integration.ServiceUserId is null) throw new UnauthorizedException(ApiKeyRules.InvalidCredentialsMessage);
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == integration.ServiceUserId, ct);
        if (user is null || !user.IsActive) throw new UnauthorizedException(ApiKeyRules.InvalidCredentialsMessage);
        var gate = limiter.TryAcquire(key.IntegrationApiKeyId, key.RateLimitPerMinute);
        if (!gate.Allowed) throw new RateLimitedException(ApiKeyRules.RateLimitedMessage(gate.RetryAfterSeconds), gate.RetryAfterSeconds);
        string? clientPublicId = null;
        if (integration.ClientId is int cid) clientPublicId = (await db.Clients.IgnoreQueryFilters().AsNoTracking().Where(c => c.ClientId == cid).Select(c => (Guid?)c.PublicId).FirstOrDefaultAsync(ct))?.ToString();
        var scopes = IntegrationService.ParseStrings(integration.ScopesJson);
        var (token, expires) = jwt.CreateApiToken(user, integration.TenantId, integration.ClientId, scopes, key.KeyId);
        await db.IntegrationApiKeys.IgnoreQueryFilters().Where(k => k.IntegrationApiKeyId == key.IntegrationApiKeyId).ExecuteUpdateAsync(s => s.SetProperty(k => k.LastUsedUtc, DateTime.UtcNow), ct);
        return (new ApiTokenDto(token, "Bearer", (int)(expires - DateTime.UtcNow).TotalSeconds, scopes, clientPublicId), key.IntegrationApiKeyId);
    }

    /// <summary>Lo que el cliente de la API puede saber de su propia llave (GET /api-auth/me).</summary>
    public async Task<ApiInfoDto> InfoAsync(string keyId, CancellationToken ct)
    {
        var key = await db.IntegrationApiKeys.AsNoTracking().FirstOrDefaultAsync(k => k.KeyId == keyId, ct) ?? throw new NotFoundException("Llave de API");
        var integration = await db.Integrations.AsNoTracking().FirstAsync(i => i.IntegrationId == key.IntegrationId, ct);
        string? clientName = null;
        Guid? clientPublic = null;
        if (integration.ClientId is int cid)
        {
            var c = await db.Clients.AsNoTracking().Where(x => x.ClientId == cid).Select(x => new { x.PublicId, x.Name }).FirstOrDefaultAsync(ct);
            (clientPublic, clientName) = (c?.PublicId, c?.Name);
        }
        return new ApiInfoDto(integration.Name, key.KeyId, key.Label, IntegrationService.ParseStrings(integration.ScopesJson), clientPublic, clientName, key.RateLimitPerMinute, key.ExpiresAtUtc);
    }

    /// <summary>Anota una solicitud de la API en la bitácora de la llave (los fallos de escritura nunca rompen la respuesta).</summary>
    public async Task LogRequestAsync(int tenantId, int apiKeyId, string method, string path, int status, int ms, CancellationToken ct)
    {
        db.IntegrationApiLogs.Add(new IntegrationApiLog { TenantId = tenantId, IntegrationApiKeyId = apiKeyId, Method = method.ToUpperInvariant(), Path = path.Length > 300 ? path[..300] : path, StatusCode = status, DurationMs = ms });
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Resuelve la llave de un token (claim akid) con su límite de uso: para el control de cada solicitud.</summary>
    public async Task<(int Id, int RateLimit, bool Active)?> ResolveKeyAsync(string keyId, CancellationToken ct)
    {
        var k = await db.IntegrationApiKeys.IgnoreQueryFilters().AsNoTracking().Where(x => x.KeyId == keyId).Select(x => new { x.IntegrationApiKeyId, x.RateLimitPerMinute, x.IsActive, x.ExpiresAtUtc }).FirstOrDefaultAsync(ct);
        return k is null ? null : (k.IntegrationApiKeyId, k.RateLimitPerMinute, k.IsActive && (k.ExpiresAtUtc is null || k.ExpiresAtUtc > DateTime.UtcNow));
    }

    public ApiRateLimiter Limiter => limiter;
    public int TokenMinutes => jwtOptions.Value.AccessTokenMinutes;
}
