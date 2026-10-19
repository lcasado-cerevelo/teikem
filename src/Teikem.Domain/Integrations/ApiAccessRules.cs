using System.Security.Cryptography;
using System.Text;
using Teikem.Domain.Constants;

namespace Teikem.Domain.Integrations;

/// <summary>Códigos de los alcances (scopes) de la API de un cliente (Lote 135).</summary>
public static class ApiScopes
{
    public const string OrdersRead = "orders:read";
    public const string OrdersCreate = "orders:create";
    public const string InvoicesRead = "invoices:read";
    public const string ConsigneesRead = "consignees:read";
    public const string ConsigneesWrite = "consignees:write";
    public const string ClientRead = "client:read";
    public const string ClientWrite = "client:write";
    public const string WarehouseRead = "warehouse:read";
    public const string WarehouseWrite = "warehouse:write";
    public const string SalesRead = "sales:read";
    public const string SalesWrite = "sales:write";
    public const string PurchasingRead = "purchasing:read";
    public const string PurchasingWrite = "purchasing:write";
}

/// <summary>Una ruta de la API que un alcance abre: método HTTP y ruta con <c>{x}</c> por cada tramo variable.</summary>
public sealed record ApiRoute(string Scope, string Method, string Pattern);

/// <summary>
/// Lote 135 (P11) — la API de los clientes es una lista CERRADA de rutas: lo que la aplicación hace con sus controladores, abierto por alcances. Una llave de API solo puede llamar a las rutas de sus alcances
/// (todo lo demás responde 403, incluso con un token válido) y, si la integración tiene cliente, todo se limita a ese cliente por el filtro de datos (igual que el portal). Nunca se abre un DELETE ni una baja:
/// los consignatarios se crean y se editan, no se borran (eso se hace en la aplicación).
/// </summary>
public static class ApiAccessRules
{
    public const string ApiPrefix = "/api/v1/";
    public const string UserKind = "API";
    public const string ForbiddenCode = "api_scope";
    public static string ForbiddenMessage(string? scope) => scope is null
        ? "Esta llave de API no puede usar esta ruta: no forma parte de la API de integraciones."
        : $"Esta llave de API no tiene el alcance «{scope}» que pide esta ruta.";

    public static readonly IReadOnlyList<ApiRoute> Routes =
    [
        // transporte: órdenes
        new(ApiScopes.OrdersRead, "GET", "/api/v1/orders"), new(ApiScopes.OrdersRead, "GET", "/api/v1/orders/{id}"), new(ApiScopes.OrdersRead, "GET", "/api/v1/orders/{id}/delivery"),
        new(ApiScopes.OrdersRead, "GET", "/api/v1/files/{id}"),
        new(ApiScopes.OrdersCreate, "POST", "/api/v1/orders"),
        // facturas
        new(ApiScopes.InvoicesRead, "GET", "/api/v1/billing/invoices"), new(ApiScopes.InvoicesRead, "GET", "/api/v1/billing/invoices/{id}"),
        // consignatarios (solo los propios: el filtro por cliente); sin baja
        new(ApiScopes.ConsigneesRead, "GET", "/api/v1/locations"), new(ApiScopes.ConsigneesRead, "GET", "/api/v1/locations/{id}"),
        new(ApiScopes.ConsigneesWrite, "POST", "/api/v1/locations"), new(ApiScopes.ConsigneesWrite, "PATCH", "/api/v1/locations/{id}"),
        // el propio cliente (remitente)
        new(ApiScopes.ClientRead, "GET", "/api/v1/clients"), new(ApiScopes.ClientRead, "GET", "/api/v1/clients/{id}"), new(ApiScopes.ClientRead, "GET", "/api/v1/clients/{id}/contacts"),
        new(ApiScopes.ClientWrite, "PATCH", "/api/v1/clients/{id}/profile"), new(ApiScopes.ClientWrite, "POST", "/api/v1/clients/{id}/contacts"), new(ApiScopes.ClientWrite, "PATCH", "/api/v1/clients/{id}/contacts/{n}"),
        // almacén
        new(ApiScopes.WarehouseRead, "GET", "/api/v1/products"), new(ApiScopes.WarehouseRead, "GET", "/api/v1/products/{id}"), new(ApiScopes.WarehouseRead, "GET", "/api/v1/products/by-barcode/{code}"),
        new(ApiScopes.WarehouseRead, "GET", "/api/v1/products/{id}/lots"), new(ApiScopes.WarehouseRead, "GET", "/api/v1/products/{id}/serials"),
        new(ApiScopes.WarehouseRead, "GET", "/api/v1/inventory/balances"), new(ApiScopes.WarehouseRead, "GET", "/api/v1/inventory/transactions"), new(ApiScopes.WarehouseRead, "GET", "/api/v1/inventory/transactions/{id}"),
        new(ApiScopes.WarehouseRead, "GET", "/api/v1/receipts"), new(ApiScopes.WarehouseRead, "GET", "/api/v1/receipts/{id}"),
        new(ApiScopes.WarehouseRead, "GET", "/api/v1/manual-issues"), new(ApiScopes.WarehouseRead, "GET", "/api/v1/manual-issues/{id}"),
        new(ApiScopes.WarehouseWrite, "POST", "/api/v1/products"), new(ApiScopes.WarehouseWrite, "PATCH", "/api/v1/products/{id}"),
        new(ApiScopes.WarehouseWrite, "POST", "/api/v1/receipts"), new(ApiScopes.WarehouseWrite, "PATCH", "/api/v1/receipts/{id}"), new(ApiScopes.WarehouseWrite, "POST", "/api/v1/receipts/{id}/lines"),
        new(ApiScopes.WarehouseWrite, "POST", "/api/v1/receipts/{id}/confirm"),
        new(ApiScopes.WarehouseWrite, "POST", "/api/v1/inventory/adjustments"), new(ApiScopes.WarehouseWrite, "POST", "/api/v1/inventory/transfers"),
        new(ApiScopes.WarehouseWrite, "POST", "/api/v1/inventory/adjustments/quantity"), new(ApiScopes.WarehouseWrite, "POST", "/api/v1/inventory/transfers/in-warehouse"),
        new(ApiScopes.WarehouseWrite, "POST", "/api/v1/manual-issues"),
        // órdenes de venta y de compra
        new(ApiScopes.SalesRead, "GET", "/api/v1/sales-orders"), new(ApiScopes.SalesRead, "GET", "/api/v1/sales-orders/{id}"),
        new(ApiScopes.SalesWrite, "POST", "/api/v1/sales-orders"), new(ApiScopes.SalesWrite, "PATCH", "/api/v1/sales-orders/{id}"), new(ApiScopes.SalesWrite, "POST", "/api/v1/sales-orders/{id}/request"),
        new(ApiScopes.PurchasingRead, "GET", "/api/v1/purchase-orders"), new(ApiScopes.PurchasingRead, "GET", "/api/v1/purchase-orders/{id}"),
        new(ApiScopes.PurchasingWrite, "POST", "/api/v1/purchase-orders"), new(ApiScopes.PurchasingWrite, "PATCH", "/api/v1/purchase-orders/{id}"), new(ApiScopes.PurchasingWrite, "POST", "/api/v1/purchase-orders/{id}/send"),
    ];

    /// <summary>Rutas que cualquier llave puede usar: sus propios datos y los catálogos que necesita para armar una solicitud (tipos de servicio, de paquete…).</summary>
    private static readonly string[] AlwaysAllowed = ["GET /api/v1/api-auth/me", "GET /api/v1/catalogs/{domain}", "GET /api/v1/catalogs"];

    private static string Norm(string? path) => (path ?? string.Empty).Split('?')[0].TrimEnd('/').ToLowerInvariant();

    private static bool Matches(string pattern, string path)
    {
        var p = pattern.TrimEnd('/').ToLowerInvariant().Split('/');
        var q = path.Split('/');
        if (p.Length != q.Length) return false;
        for (var i = 0; i < p.Length; i++)
            if (!(p[i].StartsWith('{') && p[i].EndsWith('}') && q[i].Length > 0) && p[i] != q[i]) return false;
        return true;
    }

    /// <summary>Los alcances que abren esta ruta (vacío = la ruta no es de la API de integraciones). <paramref name="always"/> = cualquier llave puede usarla.</summary>
    public static IReadOnlyList<string> ScopesFor(string method, string? path, out bool always)
    {
        var m = (method ?? string.Empty).ToUpperInvariant();
        var p = Norm(path);
        always = AlwaysAllowed.Any(a => a.StartsWith(m + " ", StringComparison.Ordinal) && Matches(a[(m.Length + 1)..], p));
        return Routes.Where(r => r.Method == m && Matches(r.Pattern, p)).Select(r => r.Scope).Distinct().ToList();
    }

    /// <summary>¿Puede una llave con estos alcances llamar a esta ruta? Si no, el alcance que haría falta (null = la ruta no es de la API).</summary>
    public static bool IsAllowed(string method, string? path, IReadOnlyCollection<string> scopes, out string? neededScope)
    {
        var needed = ScopesFor(method, path, out var always);
        neededScope = needed.FirstOrDefault();
        if (always) return true;
        return needed.Any(s => scopes.Contains(s, StringComparer.OrdinalIgnoreCase));
    }
}

/// <summary>Llaves de API: identificador público, secreto de alta entropía (se muestra una sola vez y se guarda solo su hash SHA-256) y comparación en tiempo constante.</summary>
public static class ApiKeyRules
{
    public const string KeyPrefix = "tk_";
    public const string SecretPrefix = "tks_";
    public const int DefaultRateLimit = 60;
    public const int MinRateLimit = 1;
    public const int MaxRateLimit = 6000;
    public const int MaxKeysPerIntegration = 10;
    public const int LabelMax = 80;

    public static string RateLimitMessage => $"El límite de uso debe estar entre {MinRateLimit} y {MaxRateLimit} solicitudes por minuto.";
    public const string LabelRequiredMessage = "Indique un nombre para la llave.";
    public static string LabelMaxMessage => $"Máximo {LabelMax} caracteres.";
    public static string TooManyKeysMessage => $"Una integración puede tener hasta {MaxKeysPerIntegration} llaves activas; revoque alguna.";
    public const string NotAnApiIntegrationMessage = "Solo las integraciones de tipo API tienen llaves.";
    public const string InvalidCredentialsMessage = "Credenciales inválidas.";
    public const string RevokedMessage = "La llave ya está revocada.";
    public static string RateLimitedMessage(int seconds) => $"Superó el límite de uso de la llave; vuelva a intentar en {seconds} s.";

    public static string NewKeyId() => KeyPrefix + Convert.ToHexString(RandomNumberGenerator.GetBytes(10)).ToLowerInvariant();

    public static string NewSecret() => SecretPrefix + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static string HashSecret(string secret) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret))).ToLowerInvariant();

    public static bool VerifySecret(string? secret, string? hash)
    {
        if (string.IsNullOrEmpty(secret) || string.IsNullOrEmpty(hash)) return false;
        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(HashSecret(secret)), Encoding.UTF8.GetBytes(hash));
    }

    public static string Last4(string secret) => secret.Length <= 4 ? secret : secret[^4..];

    public static bool IsValidRateLimit(int limit) => limit is >= MinRateLimit and <= MaxRateLimit;
}
