using System.Text;
using Teikem.Api.Middleware;
using Teikem.Domain.Security;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 8A / P0 — reglas puras de la idempotencia del API (IdempotencyRules): a qué aplica, validación de la clave, huella
/// SHA-256 de método + ruta + cuerpo, decisión repetir / 409 otro contenido / 409 en vuelo / vencido, y qué respuestas se
/// guardan (menores a 500 salvo acceso, bloqueo y saturación).
/// </summary>
public class IdempotencyRulesTests
{
    private static readonly DateTime Now = new(2026, 9, 27, 12, 0, 0, DateTimeKind.Utc);
    private static byte[] B(string s) => Encoding.UTF8.GetBytes(s);

    [Fact]
    public void Header_names_messages_and_limits()
    {
        Assert.Equal("Idempotency-Key", IdempotencyRules.HeaderName);
        Assert.Equal("Idempotent-Replayed", IdempotencyRules.ReplayedHeader);
        Assert.Equal(80, IdempotencyRules.MaxKeyLength);
        Assert.Equal(TimeSpan.FromDays(7), IdempotencyRules.Retention);
        Assert.Equal("La clave de idempotencia no es válida.", IdempotencyRules.InvalidKeyMessage);
        Assert.Equal("La clave de idempotencia ya se usó con otro contenido.", IdempotencyRules.BodyMismatchMessage);
        Assert.Equal("La operación con esta clave todavía se está procesando.", IdempotencyRules.InFlightMessage);
    }

    [Theory]
    [InlineData("POST", true)]
    [InlineData("put", true)]
    [InlineData("PATCH", true)]
    [InlineData("DELETE", true)]
    [InlineData("GET", false)]
    [InlineData("HEAD", false)]
    [InlineData("OPTIONS", false)]
    [InlineData(null, false)]
    public void Applies_only_to_writes(string? method, bool applies) => Assert.Equal(applies, IdempotencyRules.AppliesToMethod(method));

    [Theory]
    [InlineData("/api/v1/auth/device/login", true)]
    [InlineData("/api/v1/auth", true)]
    [InlineData("/API/V1/ME/pin", true)]
    [InlineData("/api/v1/devices", true)]
    [InlineData("/api/v1/devices/enroll", true)]
    [InlineData("/api/v1/platform/tenants", true)]
    [InlineData("/api/v1/users", true)]
    [InlineData("/api/v1/users/12/pin", true)]
    [InlineData("/api/v1/usersx", false)]
    [InlineData("/api/v1/receipts", false)]
    [InlineData("/api/v1/cycle-counts/5/lines/batch", false)]
    [InlineData("/api/v1/devicesx", false)]
    [InlineData("/api/v1/metrics", false)]
    [InlineData("/api/v1/clients/0b3a4c52-6c1e-4f7a-9f0e-2d7f1c9e8a11/portal-users/invite", true)]
    [InlineData("/api/v1/clients/0b3a4c52-6c1e-4f7a-9f0e-2d7f1c9e8a11/portal-users/7/resend-invite", true)]
    [InlineData("/API/V1/CLIENTS/x/PORTAL-USERS/INVITE/", true)]
    [InlineData("/api/v1/clients/x/portal-users", false)]
    [InlineData("/api/v1/clients/x/portal-users/7/suspend", false)]
    [InlineData("/api/v1/clients", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Routes_that_return_credentials_are_excluded(string? path, bool excluded) => Assert.Equal(excluded, IdempotencyRules.IsExcludedPath(path));

    [Theory]
    [InlineData("a")]
    [InlineData("3f2b7c1e-7d7a-4c38-9d35-0f7b4fd1c9a1")]
    [InlineData("  smoke-123-rec  ")]
    public void Valid_keys_are_trimmed(string raw)
    {
        var (key, error) = IdempotencyRules.ValidateKey(raw);
        Assert.Null(error);
        Assert.Equal(raw.Trim(), key);
    }

    [Fact]
    public void Key_of_exactly_80_is_valid_and_81_is_not()
    {
        Assert.Null(IdempotencyRules.ValidateKey(new string('k', 80)).Error);
        Assert.Equal(IdempotencyRules.InvalidKeyMessage, IdempotencyRules.ValidateKey(new string('k', 81)).Error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc\u0001def")]
    public void Empty_or_control_keys_are_invalid(string? raw)
    {
        var (key, error) = IdempotencyRules.ValidateKey(raw);
        Assert.Null(key);
        Assert.Equal("La clave de idempotencia no es válida.", error);
    }

    [Fact]
    public void Repeated_header_is_invalid()
    {
        Assert.Equal(IdempotencyRules.InvalidKeyMessage, IdempotencyRules.ValidateKey(new[] { "a", "b" }).Error);
        Assert.Equal(IdempotencyRules.InvalidKeyMessage, IdempotencyRules.ValidateKey(Array.Empty<string?>()).Error);
        Assert.Equal(IdempotencyRules.InvalidKeyMessage, IdempotencyRules.ValidateKey((IReadOnlyList<string?>?)null).Error);
    }

    [Fact]
    public void Hash_is_sha256_hex_and_depends_on_method_path_and_body()
    {
        var h = IdempotencyRules.ComputeHash("POST", "/api/v1/receipts", B("{\"a\":1}"));
        Assert.Equal(64, h.Length);
        Assert.Matches("^[0-9a-f]{64}$", h);
        Assert.Equal(h, IdempotencyRules.ComputeHash("post", "/api/v1/receipts", B("{\"a\":1}")));
        Assert.NotEqual(h, IdempotencyRules.ComputeHash("POST", "/api/v1/receipts", B("{\"a\":2}")));
        Assert.NotEqual(h, IdempotencyRules.ComputeHash("PUT", "/api/v1/receipts", B("{\"a\":1}")));
        Assert.NotEqual(h, IdempotencyRules.ComputeHash("POST", "/api/v1/pick-batches", B("{\"a\":1}")));
        Assert.NotEqual(h, IdempotencyRules.ComputeHash("POST", "/api/v1/receipts?x=1", B("{\"a\":1}")));
        // SHA-256 de la cadena vacía con su encabezado: estable entre corridas.
        Assert.Equal(IdempotencyRules.ComputeHash("DELETE", "/x", ReadOnlySpan<byte>.Empty), IdempotencyRules.ComputeHash("DELETE", "/x", Array.Empty<byte>()));
    }

    [Fact]
    public void No_record_proceeds()
        => Assert.Equal(IdempotencyDecision.Proceed, IdempotencyRules.Decide(null, null, null, "h", Now));

    [Fact]
    public void Same_hash_with_response_replays()
        => Assert.Equal(IdempotencyDecision.Replay, IdempotencyRules.Decide("abc", 200, Now.AddMinutes(-3), "ABC", Now));

    [Fact]
    public void A_stored_business_rejection_also_replays()
        => Assert.Equal(IdempotencyDecision.Replay, IdempotencyRules.Decide("abc", 409, Now.AddDays(-6), "abc", Now));

    [Fact]
    public void Different_hash_is_a_conflict_even_while_in_flight()
    {
        Assert.Equal(IdempotencyDecision.BodyMismatch, IdempotencyRules.Decide("abc", 200, Now.AddMinutes(-1), "def", Now));
        Assert.Equal(IdempotencyDecision.BodyMismatch, IdempotencyRules.Decide("abc", null, Now.AddSeconds(-1), "def", Now));
    }

    [Fact]
    public void Same_hash_without_response_is_in_flight_until_10_minutes()
    {
        Assert.Equal(IdempotencyDecision.InFlight, IdempotencyRules.Decide("abc", null, Now.AddSeconds(-5), "abc", Now));
        Assert.Equal(IdempotencyDecision.InFlight, IdempotencyRules.Decide("abc", null, Now.AddMinutes(-10), "abc", Now));
        Assert.Equal(IdempotencyDecision.Expired, IdempotencyRules.Decide("abc", null, Now.AddMinutes(-10).AddSeconds(-1), "abc", Now));
    }

    [Fact]
    public void Records_older_than_7_days_do_not_count()
    {
        Assert.Equal(IdempotencyDecision.Replay, IdempotencyRules.Decide("abc", 200, Now.AddDays(-7), "abc", Now));
        Assert.Equal(IdempotencyDecision.Expired, IdempotencyRules.Decide("abc", 200, Now.AddDays(-7).AddSeconds(-1), "abc", Now));
        Assert.Equal(IdempotencyDecision.Expired, IdempotencyRules.Decide("abc", 200, Now.AddDays(-8), "otro", Now));
        Assert.Equal(Now.AddDays(-7), IdempotencyRules.PurgeBefore(Now));
        Assert.True(IdempotencyRules.IsExpired(Now.AddDays(-7).AddTicks(-1), Now));
        Assert.False(IdempotencyRules.IsExpired(Now.AddDays(-7), Now));
    }

    [Theory]
    [InlineData(200, true)]
    [InlineData(201, true)]
    [InlineData(204, true)]
    [InlineData(400, true)]
    [InlineData(404, true)]
    [InlineData(409, true)]
    [InlineData(422, true)]
    [InlineData(401, false)]
    [InlineData(403, false)]
    [InlineData(408, false)]
    [InlineData(423, false)]
    [InlineData(429, false)]
    [InlineData(500, false)]
    [InlineData(502, false)]
    [InlineData(503, false)]
    public void Responses_below_500_are_stored_except_access_lock_and_throttle(int status, bool stored)
        => Assert.Equal(stored, IdempotencyRules.ShouldStore(status));

    [Fact]
    public void Middleware_reuses_the_problem_details_shape_of_the_exception_middleware()
    {
        var ex = new Teikem.Infrastructure.Exceptions.ConflictException(IdempotencyRules.BodyMismatchMessage);
        var body = System.Text.Json.JsonSerializer.Serialize(ExceptionHandlingMiddleware.ProblemBody(ex, Guid.Empty),
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        Assert.Contains("\"title\":\"La clave de idempotencia ya se us", body);
        Assert.Contains("\"status\":409", body);
        Assert.Contains("\"code\":\"conflict\"", body);
    }
}
