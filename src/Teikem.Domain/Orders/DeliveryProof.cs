using System.Globalization;
using System.Text.Json;
using Teikem.Domain.Common;

namespace Teikem.Domain.Orders;

/// <summary>Constantes del comprobante de entrega (Lote 118).</summary>
public static class DeliveryProofCodes
{
    /// <summary>EntityType de la bitácora del comprobante (sembrado en la sección 2026-10-18 (bb) de update.sql).</summary>
    public const string EntityType = "DELIVERY_PROOF";

    /// <summary>Quién lo registró: el personal desde la web, o (a futuro) la app del chofer.</summary>
    public const string SourceStaffWeb = "STAFF_WEB";
    public const string SourceDriverApp = "DRIVER_APP";

    /// <summary>Quién firmó: el usuario que registra (la firma automática del personal) o el que recibió (la app del chofer).</summary>
    public const string SignatureUser = "USER";
    public const string SignatureRecipient = "RECIPIENT";

    /// <summary>Estado de la ubicación: NONE (no se intentó: sin llave o sin dirección), FROM_STOP (la parada ya tenía coordenadas), PENDING/RUNNING (en cola / en curso), OK, NOT_FOUND, FAILED (error, tiempo o cuota).</summary>
    public const string GeoNone = "NONE", GeoFromStop = "FROM_STOP", GeoPending = "PENDING", GeoRunning = "RUNNING", GeoOk = "OK", GeoNotFound = "NOT_FOUND", GeoFailed = "FAILED";
}

/// <summary>
/// Comprobante de entrega de una orden (Lote 118, dictado de Luis 2026-10-18): lo que queda registrado cuando se entrega. Lo registra el personal desde la web con el permiso orders.deliver; la firma es AUTOMÁTICA y
/// a nombre del usuario que lo registra (SignatureKind USER), nunca del consignatario: el comprobante lo dice. A lo sumo uno vigente por orden. La ubicación se busca aparte (Google, un solo intento, asíncrono, 30 s):
/// si no se logra, el comprobante queda igual, sin ubicación.
/// </summary>
[AuditEntity(DeliveryProofCodes.EntityType)]
public class DeliveryProof : ITenantScoped
{
    public int DeliveryProofId { get; set; }
    [NotAudited] public Guid PublicId { get; set; }
    public int TenantId { get; set; }
    public int TransportOrderId { get; set; }
    public DateTime DeliveredAtUtc { get; set; }
    public string RecipientName { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public string Source { get; set; } = DeliveryProofCodes.SourceStaffWeb;
    public int RecordedBy { get; set; }
    public int SignedByUserId { get; set; }
    public string SignatureKind { get; set; } = DeliveryProofCodes.SignatureUser;
    public string GeoStatus { get; set; } = DeliveryProofCodes.GeoNone;
    public string? GeoAccuracy { get; set; }
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }
    [NotAudited] public DateTime? GeoCheckedAtUtc { get; set; }
    public bool IsActive { get; set; } = true;
    [NotAudited] public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>Reglas puras de registrar una entrega y de buscar su ubicación.</summary>
public static class DeliveryProofRules
{
    public const int MaxRecipientLength = 150;
    public const int MaxNotesLength = 500;

    public const string RecipientRequired = "Indique quién recibió la entrega.";
    public const string RecipientTooLong = "El nombre de quien recibió no puede pasar de 150 caracteres.";
    public const string NotesTooLong = "La nota no puede pasar de 500 caracteres.";
    public const string FutureDelivery = "La fecha y hora de la entrega no pueden ser futuras.";
    public const string AlreadyRegisteredFormat = "El envío {0} ya tiene registrada su entrega.";
    public const string FinalFormat = "El envío {0} está en un estatus final ({1}): no se registra su entrega.";
    public const string PickUpFormat = "El envío {0} es Pick up: termina «En almacén» y no se entrega.";
    public const string CommentFormat = "Entrega registrada desde la web por {0}.";
    /// <summary>Lote 127: adjuntar firma o fotos exige que la entrega ya esté registrada.</summary>
    public const string NoProofFormat = "El envío {0} no tiene registrada su entrega; registre la entrega antes de adjuntar archivos.";

    /// <summary>Tolerancia con el reloj del navegador: una entrega "de hace un instante" que llega unos minutos adelantada se acepta.</summary>
    public static readonly TimeSpan FutureTolerance = TimeSpan.FromMinutes(5);

    // ---- ubicación (Google Geocoding), tomada de RentComparable ----
    public const string ConfidenceHigh = "HIGH", ConfidenceMedium = "MEDIUM", ConfidenceLow = "LOW";

    /// <summary>ROOFTOP = alta; RANGE_INTERPOLATED y GEOMETRIC_CENTER = media; APPROXIMATE (u otro) = baja.</summary>
    public static string ConfidenceOf(string? locationType) => locationType?.Trim().ToUpperInvariant() switch
    {
        "ROOFTOP" => ConfidenceHigh,
        "RANGE_INTERPOLATED" or "GEOMETRIC_CENTER" => ConfidenceMedium,
        _ => ConfidenceLow,
    };

    private static int Rank(string? c) => c switch { ConfidenceHigh => 3, ConfidenceMedium => 2, _ => 1 };

    /// <summary>¿La confianza llega al mínimo configurado (LOW acepta todo)?</summary>
    public static bool MeetsMinimum(string confidence, string? minimum) => Rank(confidence) >= Rank(string.IsNullOrWhiteSpace(minimum) ? ConfidenceLow : minimum.Trim().ToUpperInvariant());

    /// <summary>Las coordenadas deben caer dentro de Puerto Rico (lat 17.8 a 18.7, lng −68.2 a −65.1): lo demás es una dirección mal encontrada.</summary>
    public static bool InPuertoRico(double lat, double lng) => lat is >= 17.8 and <= 18.7 && lng is >= -68.2 and <= -65.1;

    /// <summary>La dirección a buscar: calle, segunda línea, pueblo, estado, ZIP y «Puerto Rico», sin las partes vacías.</summary>
    public static string BuildAddress(string? line1, string? line2, string? city, string? state, string? postalCode)
        => string.Join(", ", new[] { line1, line2, city, state, postalCode, "Puerto Rico" }.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!.Trim()));

    /// <summary>Lo que se necesita de la respuesta de Google: <c>status</c>, y de <c>results[0]</c> la latitud, la longitud y el <c>location_type</c>.</summary>
    public sealed record GeocodeAnswer(string Status, double? Lat, double? Lng, string? LocationType);

    public static GeocodeAnswer ParseGeocode(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new GeocodeAnswer("INVALID_RESPONSE", null, null, null);
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var status = root.TryGetProperty("status", out var st) ? st.GetString() ?? "UNKNOWN" : "UNKNOWN";
            if (status != "OK" || !root.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array || results.GetArrayLength() == 0) return new GeocodeAnswer(status, null, null, null);
            var geometry = results[0].GetProperty("geometry");
            var location = geometry.GetProperty("location");
            var lat = location.GetProperty("lat").GetDouble();
            var lng = location.GetProperty("lng").GetDouble();
            var type = geometry.TryGetProperty("location_type", out var lt) ? lt.GetString() : null;
            return new GeocodeAnswer(status, lat, lng, type);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            return new GeocodeAnswer("INVALID_RESPONSE", null, null, null);
        }
    }

    /// <summary>Coordenadas con 6 decimales (como las guarda la tabla).</summary>
    public static decimal Round6(double v) => Math.Round((decimal)v, 6, MidpointRounding.AwayFromZero);

    public static string Invariant(double v) => v.ToString("0.######", CultureInfo.InvariantCulture);
}
