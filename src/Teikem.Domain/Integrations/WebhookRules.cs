using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Teikem.Domain.Integrations;

/// <summary>Eventos a los que se puede suscribir un webhook (Lote 136, P11). «webhook.ping» es la prueba y no se suscribe.</summary>
public static class WebhookEvents
{
    public const string OrderStatusChanged = "order.status_changed";
    public const string OrderDelivered = "order.delivered";
    public const string InvoiceIssued = "invoice.issued";
    public const string Ping = "webhook.ping";

    public static readonly IReadOnlyList<string> All = [OrderStatusChanged, OrderDelivered, InvoiceIssued];
}

/// <summary>Estatus de una entrega de webhook: PENDING (espera su primer intento o el siguiente), DELIVERED, UNDELIVERED (agotó los reintentos: va a la bandeja de salidas) y DISCARDED.</summary>
public static class WebhookDeliveryStatuses
{
    public const string Pending = "PENDING";
    public const string Delivered = "DELIVERED";
    public const string Undelivered = "UNDELIVERED";
    public const string Discarded = "DISCARDED";
    public static readonly IReadOnlyList<string> All = [Pending, Delivered, Undelivered, Discarded];
}

/// <summary>
/// Lote 136 (P11) — reglas puras de los webhooks de salida: URL permitida (https; nunca direcciones internas), eventos, firma HMAC-SHA256 con marca de tiempo contra repetición,
/// reintentos con espera creciente (1 min, 5, 30, 2 h, 12 h) y suspensión tras N entregas seguidas sin lograrse. El cuerpo es mínimo (id y estatus): el detalle se consulta por la API.
/// </summary>
public static class WebhookRules
{
    public const int UrlMax = 500;
    public const int MaxPerIntegration = 10;
    public const int SuspendAfterUndelivered = 5;
    public const int TimeoutSeconds = 10;
    public const int RetentionDays = 30;
    public const int ToleranceSeconds = 300;
    public const string SecretPrefix = "whsec_";
    public const string SignatureHeader = "X-Teikem-Signature";
    public const string TimestampHeader = "X-Teikem-Timestamp";
    public const string EventHeader = "X-Teikem-Event";
    public const string DeliveryHeader = "X-Teikem-Delivery";

    /// <summary>Espera antes de cada reintento (segundos): tras el intento 1, 2, 3, 4 y 5. El intento 6 es el último.</summary>
    public static readonly IReadOnlyList<int> BackoffSeconds = [60, 300, 1800, 7200, 43200];
    public static int MaxAttempts => BackoffSeconds.Count + 1;

    public const string UrlRequiredMessage = "Indique la URL a la que se enviarán los avisos.";
    public static string UrlMaxMessage => $"La URL no puede pasar de {UrlMax} caracteres.";
    public const string UrlInvalidMessage = "La URL no es válida: debe ser una dirección completa (https://…) sin usuario ni contraseña.";
    public const string UrlHttpsMessage = "La URL debe usar https.";
    public const string UrlInternalMessage = "La URL apunta a una dirección interna o local, que no se permite.";
    public const string EventsRequiredMessage = "Escoja al menos un evento.";
    public static string EventUnknownMessage(string e) => $"Evento desconocido: {e}. Use {string.Join(", ", WebhookEvents.All)}.";
    public static string TooManyMessage => $"Una integración puede tener hasta {MaxPerIntegration} webhooks; quite alguno.";
    public const string NeedsClientMessage = "Los webhooks necesitan una integración con cliente: solo se avisa de lo de ese cliente.";
    public const string NeedsScopeMessage = "La integración no tiene el alcance «webhooks:manage».";
    public const string NotApiMessage = "Solo las integraciones de tipo API tienen webhooks.";
    public const string RemovedMessage = "El webhook ya fue quitado.";
    public const string NotSuspendedMessage = "El webhook no está suspendido.";
    public const string NotUndeliveredMessage = "Solo se puede reintentar una salida no entregada.";
    public const string NotPendingMessage = "La salida ya no está pendiente.";
    public const string AlreadyDiscardedMessage = "La salida ya fue descartada.";

    public static string NewSecret() => SecretPrefix + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static string Last4(string secret) => secret.Length <= 4 ? secret : secret[^4..];

    /// <summary>Valida la URL: mensaje exacto del primer problema o null. Si <paramref name="allowInsecure"/> (solo desarrollo y pruebas) admite http y direcciones internas.</summary>
    public static string? ValidateUrl(string? url, bool allowInsecure)
    {
        var u = url?.Trim();
        if (string.IsNullOrEmpty(u)) return UrlRequiredMessage;
        if (u.Length > UrlMax) return UrlMaxMessage;
        if (!Uri.TryCreate(u, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Fragment) || string.IsNullOrEmpty(uri.Host)) return UrlInvalidMessage;
        if (allowInsecure) return null;
        if (uri.Scheme != Uri.UriSchemeHttps) return UrlHttpsMessage;
        if (IsInternalHost(uri.Host)) return UrlInternalMessage;
        return null;
    }

    /// <summary>¿Es un nombre o dirección interna (localhost, *.local, *.internal, o una IP privada, de bucle, de enlace local, etc.)?</summary>
    public static bool IsInternalHost(string host)
    {
        var h = host.Trim().TrimEnd('.').ToLowerInvariant();
        if (h == "localhost" || h.EndsWith(".localhost") || h.EndsWith(".local") || h.EndsWith(".internal") || h.EndsWith(".lan") || h.EndsWith(".home.arpa")) return true;
        if (!h.Contains('.') && !h.Contains(':')) return true;   // nombre sin dominio (intranet)
        return IPAddress.TryParse(h.Trim('[', ']'), out var ip) && IsInternalAddress(ip);
    }

    public static bool IsInternalAddress(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (IPAddress.IsLoopback(ip) || ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any)) return true;
        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6Multicast) return true;
            var b6 = ip.GetAddressBytes();
            return (b6[0] & 0xFE) == 0xFC;   // fc00::/7 (únicas locales)
        }
        var b = ip.GetAddressBytes();
        return b[0] == 10 || b[0] == 0 || b[0] >= 224
            || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
            || (b[0] == 192 && b[1] == 168)
            || (b[0] == 169 && b[1] == 254)
            || (b[0] == 100 && b[1] >= 64 && b[1] <= 127);   // 100.64/10 (CGNAT)
    }

    /// <summary>Valida los eventos: mensaje exacto o null. Devuelve la lista limpia (sin repetidos) en <paramref name="clean"/>.</summary>
    public static string? ValidateEvents(IEnumerable<string>? events, out List<string> clean)
    {
        clean = (events ?? []).Select(e => (e ?? string.Empty).Trim().ToLowerInvariant()).Where(e => e.Length > 0).Distinct().ToList();
        if (clean.Count == 0) return EventsRequiredMessage;
        var unknown = clean.FirstOrDefault(e => !WebhookEvents.All.Contains(e));
        return unknown is null ? null : EventUnknownMessage(unknown);
    }

    /// <summary>Firma: «sha256=» + HMAC-SHA256 (hex minúscula) de «{marca de tiempo}.{cuerpo}» con el secreto del webhook.</summary>
    public static string Sign(string secret, long timestamp, string body)
    {
        var mac = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes($"{timestamp}.{body}"));
        return "sha256=" + Convert.ToHexString(mac).ToLowerInvariant();
    }

    /// <summary>Lo que hace quien recibe: la firma coincide (tiempo constante) y la marca de tiempo está dentro de la tolerancia (contra repetición).</summary>
    public static bool Verify(string secret, long timestamp, string body, string? signature, DateTime nowUtc)
    {
        if (string.IsNullOrEmpty(signature)) return false;
        if (Math.Abs(new DateTimeOffset(nowUtc).ToUnixTimeSeconds() - timestamp) > ToleranceSeconds) return false;
        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(Sign(secret, timestamp, body)), Encoding.UTF8.GetBytes(signature));
    }

    /// <summary>Cuándo es el siguiente intento tras fallar el intento número <paramref name="attemptsMade"/> (1 = el primero); null si ya no hay más (agotó los reintentos).</summary>
    public static DateTime? NextAttemptUtc(int attemptsMade, DateTime nowUtc)
        => attemptsMade >= 1 && attemptsMade <= BackoffSeconds.Count ? nowUtc.AddSeconds(BackoffSeconds[attemptsMade - 1]) : null;

    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = false };

    /// <summary>El cuerpo mínimo del evento: id de la entrega, tipo, cuándo y los datos (id público y estatus; el detalle se consulta por la API).</summary>
    public static string BuildBody(Guid eventId, string eventType, DateTime createdAtUtc, object data)
        => JsonSerializer.Serialize(new { id = eventId, type = eventType, createdAt = DateTime.SpecifyKind(createdAtUtc, DateTimeKind.Utc), data }, Json);
}
