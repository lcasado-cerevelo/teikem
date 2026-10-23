using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Teikem.Domain.Constants;
using Teikem.Domain.Orders;

namespace Teikem.Domain.Trips;

/// <summary>
/// 2026-10-22 (pedido de Luis) — ubicación (latitud y longitud) de las paradas de una ruta: se busca con Google SOLO cuando la parada entra a una ruta, fuera de la petición del usuario, con un tope mensual de consultas
/// (<c>GoogleMaps:MonthlyCap</c>, a nivel de aplicación) y recordando lo ya buscado por dirección (la misma dirección no se vuelve a pagar). Reglas puras: la llave de la dirección, el mes del contador y la precisión.
/// </summary>
public static class StopGeocodeRules
{
    public const string StatusRunning = "RUNNING";
    public const string StatusOk = "OK";
    public const string StatusNotFound = "NOT_FOUND";
    public const string StatusFailed = "FAILED";

    /// <summary>Un tope por omisión por debajo del tramo gratis de Google (10.000 consultas al mes en la Geocoding API, según la consola de Google Cloud).</summary>
    public const int DefaultMonthlyCap = 9000;

    /// <summary>Una parada que falló se vuelve a intentar pasado este tiempo (error de red, de cuota o de llave).</summary>
    public static readonly TimeSpan RetryFailedAfter = TimeSpan.FromMinutes(30);

    /// <summary>Un RUNNING más viejo que esto (el API se cayó a medias) se da por fallido.</summary>
    public static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(2);

    /// <summary>Fallos seguidos que detienen el ciclo (la llave o la cuota están mal: no se sigue gastando consultas).</summary>
    public const int MaxConsecutiveFailures = 3;

    /// <summary>La dirección de la parada como se le pide a Google.</summary>
    public static string BuildAddress(string? line1, string? line2, string? city, string? state, string? postalCode) => DeliveryProofRules.BuildAddress(line1, line2, city, state, postalCode);

    /// <summary>
    /// Llave de la dirección (SHA-256 en hexadecimal de la dirección sin acentos, en mayúsculas y con los espacios y signos colapsados): la misma dirección escrita un poco distinta da la misma llave y se busca una sola vez.
    /// Vacía si la dirección no tiene nada que buscar.
    /// </summary>
    public static string AddressKey(string? line1, string? line2, string? city, string? state, string? postalCode)
    {
        var folded = Fold(string.Join(' ', new[] { line1, line2, city, state, postalCode }.Where(x => !string.IsNullOrWhiteSpace(x))));
        if (folded.Length == 0) return string.Empty;
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(folded))).ToLowerInvariant();
    }

    private static string Fold(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var ch in text.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            sb.Append(char.IsLetterOrDigit(ch) ? char.ToUpperInvariant(ch) : ' ');
        }
        return string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>El mes del contador de consultas («2026-10»), en UTC como la facturación de Google (que corta por mes del calendario).</summary>
    public static string UsageMonth(DateTime utc) => utc.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    /// <summary>La precisión que se guarda en la parada: solo una ubicación de techo (ROOFTOP) es EXACTA; lo demás se pinta como aproximado y el despachador puede poner el pin a mano.</summary>
    public static string AccuracyOf(string? confidence) => string.Equals(confidence, DeliveryProofRules.ConfidenceHigh, StringComparison.OrdinalIgnoreCase) ? GeocodeAccuracies.Exact : GeocodeAccuracies.ZipCentroid;

    /// <summary>Un tope menor o igual a cero apaga la búsqueda (no se hace ninguna consulta).</summary>
    public static bool CapAllows(int cap, int callsSoFar) => cap > 0 && callsSoFar < cap;
}
