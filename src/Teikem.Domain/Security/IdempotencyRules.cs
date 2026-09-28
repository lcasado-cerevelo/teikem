using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Teikem.Domain.Security;

/// <summary>Qué hacer con una escritura que trae Idempotency-Key, según el registro que ya existe con esa clave.</summary>
public enum IdempotencyDecision
{
    /// <summary>No hay registro: se registra la clave (en vuelo) y se ejecuta.</summary>
    Proceed,
    /// <summary>Misma clave y mismo contenido con respuesta guardada: se repite la respuesta (Idempotent-Replayed: true).</summary>
    Replay,
    /// <summary>Misma clave con otro contenido: 409 'La clave de idempotencia ya se usó con otro contenido.'.</summary>
    BodyMismatch,
    /// <summary>Misma clave y mismo contenido todavía sin respuesta: 409 'La operación con esta clave todavía se está procesando.'.</summary>
    InFlight,
    /// <summary>El registro venció (más de 7 días) o quedó abandonado en vuelo: se descarta y se ejecuta como nuevo.</summary>
    Expired,
}

/// <summary>
/// Comprobación de módulo (<see cref="IdempotencyRules.ModuleCheck"/>) o de permiso (<see cref="IdempotencyRules.PermissionCheck"/>)
/// que la operación original hizo en el servicio o en el controlador (fuera de los atributos), con su resultado.
/// </summary>
public sealed record IdempotencyCheck(string Kind, string Code, bool Result);

/// <summary>
/// Lote 8A — reglas puras de la idempotencia del API (cabecera Idempotency-Key, módulo 13 del maestro), sin BD ni HTTP:
/// - Aplica a POST, PUT, PATCH y DELETE autenticados que traen la cabecera. Clave de 1 a 80 caracteres (medidos antes de
///   recortar los espacios de los extremos); vacía, más larga, repetida o con caracteres de control → 400 'La clave de
///   idempotencia no es válida.'.
/// - Clave lógica = (TenantId, UserId, clave). Huella = SHA-256 (hex en minúsculas) de método, ruta con query y cuerpo: la
///   misma clave en otra ruta también es "otro contenido".
/// - Decisión: sin registro → ejecutar; vencido (más de 7 días) o en vuelo abandonado (más de 10 minutos sin respuesta) →
///   descartar y ejecutar; huella distinta → 409; sin respuesta → 409 en vuelo; con respuesta → repetirla.
/// - Se guardan las respuestas menores a 500, salvo 401, 403, 408, 423 y 429 (acceso, bloqueo o saturación: reintentar con la
///   misma clave debe volver a intentarlo). Los 5xx nunca se guardan.
/// - La clave distingue mayúsculas ('abc' y 'ABC' son claves distintas; la columna usa collation binaria en SQL Server).
/// - Antes de repetir se vuelven a evaluar las comprobaciones de módulo y permiso que la operación original hizo en el
///   servicio o el controlador (guardadas con la respuesta): cualquiera que cambie, en cualquier sentido (p. ej. perder o
///   ganar warehouse.count, que decide el conteo a ciegas) → 409 'La operación con esta clave ya no puede repetirse con los
///   permisos actuales.': la respuesta guardada ya no se puede servir y una llamada nueva daría otro resultado. Los
///   atributos del endpoint ([RequirePermission], [RequireModule], [RequireAal2]) siguen respondiendo su propio 403.
/// - No aplica a rutas que devuelven credenciales en claro (tokens, secretos, códigos de registro, contraseñas temporales):
///   /api/v1/auth, /api/v1/me, /api/v1/devices, /api/v1/platform y /api/v1/users (el alta de usuario devuelve la contraseña
///   temporal), además de las invitaciones de portal (/api/v1/clients/{id}/portal-users/invite y
///   .../portal-users/{id}/resend-invite devuelven el token de invitación). Guardar esas respuestas dejaría el secreto en la
///   bitácora.
/// </summary>
public static class IdempotencyRules
{
    public const string HeaderName = "Idempotency-Key";
    public const string ReplayedHeader = "Idempotent-Replayed";
    public const int MaxKeyLength = 80;
    public static readonly TimeSpan Retention = TimeSpan.FromDays(7);
    public static readonly TimeSpan InFlightTimeout = TimeSpan.FromMinutes(10);

    public const string InvalidKeyMessage = "La clave de idempotencia no es válida.";
    public const string BodyMismatchMessage = "La clave de idempotencia ya se usó con otro contenido.";
    public const string InFlightMessage = "La operación con esta clave todavía se está procesando.";
    public const string RecheckChangedMessage = "La operación con esta clave ya no puede repetirse con los permisos actuales.";

    /// <summary>Tipo de comprobación guardada: módulo encendido.</summary>
    public const string ModuleCheck = "M";
    /// <summary>Tipo de comprobación guardada: permiso efectivo.</summary>
    public const string PermissionCheck = "P";

    private static readonly string[] Methods = { "POST", "PUT", "PATCH", "DELETE" };
    private static readonly int[] NotStoredBelow500 = { 401, 403, 408, 423, 429 };

    /// <summary>Prefijos de ruta excluidos (respuestas con credenciales en claro). Comparación sin distinguir mayúsculas.</summary>
    public static readonly IReadOnlyList<string> ExcludedPathPrefixes = new[] { "/api/v1/auth", "/api/v1/me", "/api/v1/devices", "/api/v1/platform", "/api/v1/users" };

    /// <summary>¿El método es una escritura a la que aplica la idempotencia?</summary>
    public static bool AppliesToMethod(string? method)
        => method is not null && Methods.Contains(method, StringComparer.OrdinalIgnoreCase);

    /// <summary>Prefijo de las rutas de clientes bajo las que cuelgan las invitaciones de portal.</summary>
    public const string ClientsPathPrefix = "/api/v1/clients/";

    /// <summary>
    /// Sufijos excluidos bajo /api/v1/clients/: invitar y reenviar invitación de portal devuelven el token de invitación
    /// (sirve para fijar la contraseña de la cuenta de portal).
    /// </summary>
    public static readonly IReadOnlyList<string> ExcludedClientPathSuffixes = new[] { "/portal-users/invite", "/resend-invite" };

    /// <summary>
    /// ¿La ruta está excluida? (segmento completo: /api/v1/devices y /api/v1/devices/x sí; /api/v1/devicesx no). También las
    /// invitaciones de portal bajo /api/v1/clients/ (ExcludedClientPathSuffixes).
    /// </summary>
    public static bool IsExcludedPath(string? path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        foreach (var prefix in ExcludedPathPrefixes)
        {
            if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            if (path.Length == prefix.Length || path[prefix.Length] == '/') return true;
        }
        if (path.StartsWith(ClientsPathPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var trimmed = path.TrimEnd('/');
            foreach (var suffix in ExcludedClientPathSuffixes)
                if (trimmed.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    /// <summary>
    /// Clave normalizada (sin espacios al inicio y al final) o error. Los valores vienen de la cabecera: más de uno (cabecera
    /// repetida) es inválido.
    /// </summary>
    public static (string? Key, string? Error) ValidateKey(IReadOnlyList<string?>? values)
    {
        if (values is null || values.Count != 1) return (null, InvalidKeyMessage);
        // La longitud (1 a 80) se mide sobre el valor recibido, antes de recortar: 81 caracteres con espacios siguen siendo 81.
        var raw = values[0];
        if (raw is null || raw.Length > MaxKeyLength) return (null, InvalidKeyMessage);
        var key = raw.Trim();
        if (key.Length == 0) return (null, InvalidKeyMessage);
        if (key.Any(char.IsControl)) return (null, InvalidKeyMessage);
        return (key, null);
    }

    /// <summary>Atajo para una sola cadena.</summary>
    public static (string? Key, string? Error) ValidateKey(string? value) => ValidateKey(new[] { value });

    /// <summary>Huella de la petición: SHA-256 (64 caracteres hex en minúsculas) de "MÉTODO ruta?query\n" + cuerpo.</summary>
    public static string ComputeHash(string method, string pathAndQuery, ReadOnlySpan<byte> body)
    {
        var head = Encoding.UTF8.GetBytes(method.ToUpperInvariant() + " " + pathAndQuery + "\n");
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        sha.AppendData(head);
        sha.AppendData(body);
        return Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant();
    }

    /// <summary>Decide según el registro existente (null = no hay) y la huella de la petición actual.</summary>
    public static IdempotencyDecision Decide(string? storedHash, int? storedResponseCode, DateTime? storedCreatedAtUtc, string requestHash, DateTime nowUtc)
    {
        if (storedCreatedAtUtc is not DateTime created) return IdempotencyDecision.Proceed;
        if (IsExpired(created, nowUtc)) return IdempotencyDecision.Expired;
        if (!string.Equals(storedHash, requestHash, StringComparison.OrdinalIgnoreCase)) return IdempotencyDecision.BodyMismatch;
        if (storedResponseCode is null)
            return nowUtc - created > InFlightTimeout ? IdempotencyDecision.Expired : IdempotencyDecision.InFlight;
        return IdempotencyDecision.Replay;
    }

    /// <summary>¿El registro ya no cuenta? (más de 7 días).</summary>
    public static bool IsExpired(DateTime createdAtUtc, DateTime nowUtc) => nowUtc - createdAtUtc > Retention;

    /// <summary>Límite de antigüedad para el borrado perezoso: registros creados antes de esta hora se borran.</summary>
    public static DateTime PurgeBefore(DateTime nowUtc) => nowUtc - Retention;

    /// <summary>¿Se guarda la respuesta para repetirla? Menores a 500 salvo 401, 403, 408, 423 y 429.</summary>
    public static bool ShouldStore(int statusCode) => statusCode is >= 100 and < 500 && !NotStoredBelow500.Contains(statusCode);

    /// <summary>Serializa las comprobaciones de la operación original (null si no hubo ninguna).</summary>
    public static string? SerializeChecks(IReadOnlyCollection<IdempotencyCheck>? checks)
        => checks is null || checks.Count == 0 ? null : JsonSerializer.Serialize(checks);

    /// <summary>Lee las comprobaciones guardadas: vacío si no hay; null si no se pueden leer (quien repite responde 409, no se arriesga).</summary>
    public static IReadOnlyList<IdempotencyCheck>? ParseChecks(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<IdempotencyCheck>();
        try
        {
            return JsonSerializer.Deserialize<List<IdempotencyCheck>>(json) ?? new List<IdempotencyCheck>();
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
