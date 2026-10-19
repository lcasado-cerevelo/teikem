using Teikem.Domain.Integrations;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

// Lote 135 (P11): reglas puras de la API de los clientes — rutas por alcance, llaves, secretos y límite de uso.
public sealed class ApiAccessRulesTests
{
    [Fact]
    public void A_route_is_open_only_to_keys_with_its_scope()
    {
        Assert.True(ApiAccessRules.IsAllowed("GET", "/api/v1/orders", [ApiScopes.OrdersRead], out _));
        Assert.True(ApiAccessRules.IsAllowed("get", "/api/v1/orders/abc/", [ApiScopes.OrdersRead], out _));
        Assert.False(ApiAccessRules.IsAllowed("POST", "/api/v1/orders", [ApiScopes.OrdersRead], out var needed));
        Assert.Equal(ApiScopes.OrdersCreate, needed);
    }

    [Fact]
    public void Routes_outside_the_list_are_never_allowed_and_deletes_are_never_opened()
    {
        Assert.False(ApiAccessRules.IsAllowed("GET", "/api/v1/users", [.. ApiAccessRules.Routes.Select(r => r.Scope)], out var needed));
        Assert.Null(needed);
        Assert.DoesNotContain(ApiAccessRules.Routes, r => r.Method == "DELETE");
        Assert.False(ApiAccessRules.IsAllowed("DELETE", "/api/v1/locations/abc", [ApiScopes.ConsigneesWrite], out _));
    }

    [Fact]
    public void Every_key_can_ask_who_it_is()
    {
        Assert.True(ApiAccessRules.IsAllowed("GET", "/api/v1/api-auth/me", [], out _));
    }

    [Fact]
    public void Secrets_are_stored_hashed_and_compared_exactly()
    {
        var secret = ApiKeyRules.NewSecret();
        var hash = ApiKeyRules.HashSecret(secret);
        Assert.StartsWith(ApiKeyRules.SecretPrefix, secret);
        Assert.StartsWith(ApiKeyRules.KeyPrefix, ApiKeyRules.NewKeyId());
        Assert.True(ApiKeyRules.VerifySecret(secret, hash));
        Assert.False(ApiKeyRules.VerifySecret(secret + "x", hash));
        Assert.False(ApiKeyRules.VerifySecret(null, hash));
        Assert.DoesNotContain(secret, hash);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(6000, true)]
    [InlineData(6001, false)]
    public void The_rate_limit_goes_from_1_to_6000(int limit, bool ok) => Assert.Equal(ok, ApiKeyRules.IsValidRateLimit(limit));

    [Fact]
    public void The_limiter_blocks_past_the_limit_and_frees_after_a_minute()
    {
        var limiter = new ApiRateLimiter();
        var t0 = new DateTime(2026, 10, 19, 10, 0, 0, DateTimeKind.Utc);
        Assert.True(limiter.TryAcquire(1, 2, t0).Allowed);
        Assert.True(limiter.TryAcquire(1, 2, t0.AddSeconds(1)).Allowed);
        var blocked = limiter.TryAcquire(1, 2, t0.AddSeconds(2));
        Assert.False(blocked.Allowed);
        Assert.InRange(blocked.RetryAfterSeconds, 1, 60);
        Assert.True(limiter.TryAcquire(2, 2, t0.AddSeconds(2)).Allowed);   // otra llave, otra cuenta
        Assert.True(limiter.TryAcquire(1, 2, t0.AddSeconds(61)).Allowed);
    }
}
