using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Teikem.Domain.Orders;

namespace Teikem.Infrastructure.Geocoding;

/// <summary>
/// Opciones de Google Maps (<c>GoogleMaps</c>). La llave NUNCA va en el repositorio: se pone con la variable de entorno <c>GoogleMaps__ApiKey</c>. Apagado por omisión (Enabled=false): sin llave no se busca ubicación y las
/// entregas se registran igual, sin ubicación.
/// </summary>
public sealed class GoogleMapsOptions
{
    public const string Section = "GoogleMaps";
    public bool Enabled { get; set; }
    public string? ApiKey { get; set; }
    /// <summary>Confianza mínima aceptada: LOW (acepta todo), MEDIUM o HIGH.</summary>
    public string MinConfidence { get; set; } = DeliveryProofRules.ConfidenceLow;
    /// <summary>Tiempo máximo de la única consulta (dictado de Luis: 30 s).</summary>
    public int TimeoutSeconds { get; set; } = 30;
    public int PollSeconds { get; set; } = 5;

    /// <summary>Se puede buscar solo si está encendido y hay llave de verdad (no un marcador).</summary>
    public bool IsUsable => Enabled && !string.IsNullOrWhiteSpace(ApiKey) && !ApiKey.StartsWith('<') && !ApiKey.Contains("use env", StringComparison.OrdinalIgnoreCase);
}

/// <summary>Resultado de la única consulta a Google: OK con coordenadas y confianza, NOT_FOUND (no la encontró, poca precisión o fuera de Puerto Rico) o FAILED (error, tiempo o cuota).</summary>
public sealed record GeocodeOutcome(string Status, decimal? Lat, decimal? Lng, string? Confidence)
{
    public static GeocodeOutcome NotFound => new(DeliveryProofCodes.GeoNotFound, null, null, null);
    public static GeocodeOutcome Failed => new(DeliveryProofCodes.GeoFailed, null, null, null);
}

/// <summary>
/// Geocodificación con la Geocoding API de Google (Lote 118), tomada de RentComparable: <c>GET maps/api/geocode/json?address=…&amp;components=country:PR&amp;key=…</c>. UN SOLO INTENTO con tiempo máximo (30 s): si falla o
/// se tarda, el resultado es FAILED y nadie reintenta. Nunca registra la dirección completa ni la llave (el mensaje de una excepción HTTP puede traer la URL con la llave: solo se anota el tipo de la excepción).
/// </summary>
public sealed class GoogleGeocoder(HttpClient http, IOptions<GoogleMapsOptions> options, ILogger<GoogleGeocoder> logger)
{
    private const string Endpoint = "https://maps.googleapis.com/maps/api/geocode/json";

    public bool IsUsable => options.Value.IsUsable;

    public async Task<GeocodeOutcome> GeocodeAsync(string address, CancellationToken ct)
    {
        var o = options.Value;
        if (!o.IsUsable || string.IsNullOrWhiteSpace(address)) return GeocodeOutcome.Failed;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(o.TimeoutSeconds, 1, 120)));
        try
        {
            var url = $"{Endpoint}?address={Uri.EscapeDataString(address)}&components=country:PR&key={Uri.EscapeDataString(o.ApiKey!)}";
            using var response = await http.GetAsync(url, timeout.Token);
            var body = await response.Content.ReadAsStringAsync(timeout.Token);
            return Interpret(DeliveryProofRules.ParseGeocode(body), o.MinConfidence, response.IsSuccessStatusCode);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning("Google tardó más de {Seconds} s en ubicar una dirección; se deja sin ubicación.", o.TimeoutSeconds);
            return GeocodeOutcome.Failed;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            logger.LogWarning("Google no respondió ({Kind}); se deja sin ubicación.", ex.GetType().Name);
            return GeocodeOutcome.Failed;
        }
    }

    /// <summary>La respuesta de Google convertida en resultado (pública y pura para las pruebas).</summary>
    public static GeocodeOutcome Interpret(DeliveryProofRules.GeocodeAnswer answer, string? minConfidence, bool httpOk = true)
    {
        if (!httpOk) return GeocodeOutcome.Failed;
        if (answer.Status == "ZERO_RESULTS") return GeocodeOutcome.NotFound;
        if (answer.Status != "OK" || answer.Lat is not double lat || answer.Lng is not double lng) return GeocodeOutcome.Failed;   // REQUEST_DENIED, OVER_QUERY_LIMIT, respuesta inválida…
        var confidence = DeliveryProofRules.ConfidenceOf(answer.LocationType);
        if (!DeliveryProofRules.MeetsMinimum(confidence, minConfidence) || !DeliveryProofRules.InPuertoRico(lat, lng)) return GeocodeOutcome.NotFound;
        return new GeocodeOutcome(DeliveryProofCodes.GeoOk, DeliveryProofRules.Round6(lat), DeliveryProofRules.Round6(lng), confidence);
    }
}
