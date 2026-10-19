using System.Net;
using System.Text.Json;
using Teikem.Domain.Integrations;
using Xunit;

namespace Teikem.Tests;

// Lote 136 (P11): reglas puras de los webhooks de salida — URL permitida, eventos, firma HMAC con marca de tiempo, reintentos con espera creciente, cuerpo mínimo y la ruta del API del cliente.
public sealed class WebhookRulesTests
{
    [Theory]
    [InlineData("https://cliente.example.com/hooks", null)]
    [InlineData("", "Indique la URL a la que se enviarán los avisos.")]
    [InlineData("   ", "Indique la URL a la que se enviarán los avisos.")]
    [InlineData("cliente.example.com/hooks", "La URL no es válida: debe ser una dirección completa (https://…) sin usuario ni contraseña.")]
    [InlineData("ftp://cliente.example.com/hooks", "La URL no es válida: debe ser una dirección completa (https://…) sin usuario ni contraseña.")]
    [InlineData("https://user:pass@cliente.example.com/", "La URL no es válida: debe ser una dirección completa (https://…) sin usuario ni contraseña.")]
    [InlineData("http://cliente.example.com/hooks", "La URL debe usar https.")]
    [InlineData("https://localhost/hooks", "La URL apunta a una dirección interna o local, que no se permite.")]
    [InlineData("https://127.0.0.1/hooks", "La URL apunta a una dirección interna o local, que no se permite.")]
    [InlineData("https://10.1.2.3/hooks", "La URL apunta a una dirección interna o local, que no se permite.")]
    [InlineData("https://172.20.0.5/hooks", "La URL apunta a una dirección interna o local, que no se permite.")]
    [InlineData("https://192.168.1.10/hooks", "La URL apunta a una dirección interna o local, que no se permite.")]
    [InlineData("https://169.254.169.254/latest/meta-data", "La URL apunta a una dirección interna o local, que no se permite.")]
    [InlineData("https://[::1]/hooks", "La URL apunta a una dirección interna o local, que no se permite.")]
    [InlineData("https://intranet/hooks", "La URL apunta a una dirección interna o local, que no se permite.")]
    [InlineData("https://servidor.local/hooks", "La URL apunta a una dirección interna o local, que no se permite.")]
    [InlineData("https://8.8.8.8/hooks", null)]
    public void The_url_must_be_https_and_public(string url, string? expected) => Assert.Equal(expected, WebhookRules.ValidateUrl(url, allowInsecure: false));

    [Fact]
    public void In_development_http_and_internal_addresses_are_allowed_but_a_bad_url_is_not()
    {
        Assert.Null(WebhookRules.ValidateUrl("http://127.0.0.1:5999/hook", allowInsecure: true));
        Assert.Equal(WebhookRules.UrlInvalidMessage, WebhookRules.ValidateUrl("no es url", allowInsecure: true));
        Assert.Equal(WebhookRules.UrlMaxMessage, WebhookRules.ValidateUrl("https://a.example.com/" + new string('x', 500), allowInsecure: false));
    }

    [Theory]
    [InlineData("8.8.8.8", false)]
    [InlineData("100.64.0.1", true)]
    [InlineData("172.15.0.1", false)]
    [InlineData("172.32.0.1", false)]
    [InlineData("224.0.0.1", true)]
    [InlineData("fe80::1", true)]
    [InlineData("fc00::1", true)]
    [InlineData("::ffff:10.0.0.1", true)]
    [InlineData("2606:4700:4700::1111", false)]
    public void Internal_addresses_are_recognised(string ip, bool internalAddress) => Assert.Equal(internalAddress, WebhookRules.IsInternalAddress(IPAddress.Parse(ip)));

    [Fact]
    public void Events_are_required_known_and_deduplicated()
    {
        Assert.Equal(WebhookRules.EventsRequiredMessage, WebhookRules.ValidateEvents([], out _));
        Assert.Equal(WebhookRules.EventsRequiredMessage, WebhookRules.ValidateEvents(null, out _));
        Assert.Equal("Evento desconocido: order.created. Use order.status_changed, order.delivered, invoice.issued.", WebhookRules.ValidateEvents(["order.created"], out _));
        Assert.Null(WebhookRules.ValidateEvents([" Order.Delivered ", "order.delivered", "invoice.issued"], out var clean));
        Assert.Equal(["order.delivered", "invoice.issued"], clean);
        Assert.Equal(WebhookEvents.All.Count, WebhookEvents.All.Distinct().Count());
        Assert.DoesNotContain(WebhookEvents.Ping, WebhookEvents.All);
    }

    [Fact]
    public void The_signature_covers_timestamp_and_body_and_is_verified_in_constant_time_within_five_minutes()
    {
        var now = new DateTime(2026, 10, 19, 12, 0, 0, DateTimeKind.Utc);
        var ts = new DateTimeOffset(now).ToUnixTimeSeconds();
        var secret = WebhookRules.NewSecret();
        var body = "{\"id\":\"1\"}";
        var sig = WebhookRules.Sign(secret, ts, body);
        Assert.StartsWith(WebhookRules.SecretPrefix, secret);
        Assert.StartsWith("sha256=", sig);
        Assert.Equal(7 + 64, sig.Length);
        Assert.True(WebhookRules.Verify(secret, ts, body, sig, now));
        Assert.False(WebhookRules.Verify(secret, ts, body + " ", sig, now));                 // cuerpo alterado
        Assert.False(WebhookRules.Verify(secret, ts + 1, body, sig, now));                   // marca de tiempo alterada
        Assert.False(WebhookRules.Verify(secret + "x", ts, body, sig, now));                 // otro secreto
        Assert.False(WebhookRules.Verify(secret, ts, body, null, now));
        Assert.True(WebhookRules.Verify(secret, ts, body, sig, now.AddSeconds(299)));
        Assert.False(WebhookRules.Verify(secret, ts, body, sig, now.AddSeconds(301)));       // repetición tardía
        Assert.Equal("sha256=" + Convert.ToHexString(System.Security.Cryptography.HMACSHA256.HashData("k"u8.ToArray(), "5.x"u8.ToArray())).ToLowerInvariant(), WebhookRules.Sign("k", 5, "x"));
    }

    [Fact]
    public void Retries_wait_1_min_5_min_30_min_2_h_and_12_h_then_give_up()
    {
        var now = new DateTime(2026, 10, 19, 12, 0, 0, DateTimeKind.Utc);
        Assert.Equal(6, WebhookRules.MaxAttempts);
        Assert.Equal(now.AddMinutes(1), WebhookRules.NextAttemptUtc(1, now));
        Assert.Equal(now.AddMinutes(5), WebhookRules.NextAttemptUtc(2, now));
        Assert.Equal(now.AddMinutes(30), WebhookRules.NextAttemptUtc(3, now));
        Assert.Equal(now.AddHours(2), WebhookRules.NextAttemptUtc(4, now));
        Assert.Equal(now.AddHours(12), WebhookRules.NextAttemptUtc(5, now));
        Assert.Null(WebhookRules.NextAttemptUtc(6, now));                                    // agotado: no entregada
        Assert.Null(WebhookRules.NextAttemptUtc(0, now));
    }

    [Fact]
    public void The_body_is_minimal_camel_case_with_id_type_date_and_data()
    {
        var id = Guid.NewGuid();
        var json = WebhookRules.BuildBody(id, WebhookEvents.OrderDelivered, new DateTime(2026, 10, 19, 12, 0, 0, DateTimeKind.Utc), new { orderId = Guid.Empty, status = "DELIVERED" });
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal(id.ToString(), root.GetProperty("id").GetString());
        Assert.Equal("order.delivered", root.GetProperty("type").GetString());
        Assert.Equal("2026-10-19T12:00:00Z", root.GetProperty("createdAt").GetString());
        Assert.Equal("DELIVERED", root.GetProperty("data").GetProperty("status").GetString());
    }

    [Fact]
    public void Webhook_routes_belong_to_the_webhooks_scope_and_the_only_delete_is_the_webhook_removal()
    {
        Assert.True(ApiAccessRules.IsAllowed("DELETE", "/api/v1/webhooks/abc", [ApiScopes.WebhooksManage], out _));
        Assert.False(ApiAccessRules.IsAllowed("DELETE", "/api/v1/webhooks/abc", [ApiScopes.OrdersRead], out var needed));
        Assert.Equal(ApiScopes.WebhooksManage, needed);
        Assert.All(ApiAccessRules.Routes.Where(r => r.Method == "DELETE"), r => Assert.Equal(ApiScopes.WebhooksManage, r.Scope));
        Assert.True(IntegrationScopes.AnyNeedsClient([ApiScopes.WebhooksManage]));
        Assert.True(IntegrationScopes.IsApiScope(ApiScopes.WebhooksManage));
    }
}
