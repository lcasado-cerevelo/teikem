namespace Teikem.Domain.Trips;

/// <summary>
/// Instantánea de la ruta tomada en la FASE 1 de la optimización (dentro de la transacción #1): el Trip, su versión vigente
/// de Route (null si aún no tenía) y el RowVersion del Trip en ese momento. La FASE 3 la compara con lo vigente para decidir
/// si el resultado del motor todavía aplica (<see cref="RouteEditRules.IsStale"/>).
/// </summary>
public sealed record OptimizationSnapshot(int TripId, int? RouteId, byte[] TripRowVersion);

/// <summary>
/// Lote 5 (P3) — reglas puras de la edición de una ruta: pin manual por parada (validación de coordenadas), detección de
/// una ruta que cambió mientras el optimizador calculaba (optimización en tres fases) y los mensajes del optimizador. Los
/// mensajes son constantes públicas porque el manual y la FAQ los citan tal cual.
/// </summary>
public static class RouteEditRules
{
    // ---------------- Mensajes ----------------

    public const string LocationRequiredMessage = "Indique la latitud y la longitud.";
    public const string LatitudeRangeMessage = "La latitud debe estar entre -90 y 90.";
    public const string LongitudeRangeMessage = "La longitud debe estar entre -180 y 180.";

    /// <summary>409: la ruta vigente o su RowVersion cambiaron entre la FASE 1 y la FASE 3 (la corrida queda en ERROR).</summary>
    public const string StaleMessage = "La ruta cambió mientras se optimizaba; vuelva a optimizar.";

    /// <summary>409: el motor lanzó una excepción, agotó el tiempo o devolvió un resultado inválido (la corrida queda en ERROR).</summary>
    public const string EngineErrorMessage = "El optimizador no pudo calcular la ruta; la corrida quedó registrada con error.";

    /// <summary>422: la ruta vigente no tiene paradas (no se crea corrida).</summary>
    public const string NoStopsMessage = "La ruta no tiene paradas que optimizar.";

    /// <summary>ErrorMessage de la corrida descartada porque la ruta cambió durante el cálculo.</summary>
    public const string StaleRunErrorMessage = "Descartada: la ruta cambió durante el cálculo.";

    /// <summary>ErrorMessage de la corrida cuando el motor excede <see cref="EngineTimeout"/>.</summary>
    public const string EngineTimeoutRunErrorMessage = "El optimizador excedió el tiempo máximo de 60 segundos.";

    /// <summary>ErrorMessage de la corrida cuando el cliente canceló la solicitud mientras el motor calculaba.</summary>
    public const string CancelledRunErrorMessage = "Cancelada: la solicitud se interrumpió durante el cálculo.";

    /// <summary>Prefijo del ErrorMessage cuando el resultado del motor no pasa RouteOptimizationRules.ValidateResult.</summary>
    public const string InvalidResultRunErrorPrefix = "Resultado inválido del optimizador: ";

    /// <summary>Largo máximo del ErrorMessage que se guarda en la corrida.</summary>
    public const int RunErrorMaxLength = 4000;

    /// <summary>Tiempo máximo que se espera al motor (FASE 2, fuera de transacción).</summary>
    public static readonly TimeSpan EngineTimeout = TimeSpan.FromSeconds(60);

    /// <summary>Comentario del historial ROUTE de la versión creada por una corrida: 'Optimización #{runId}'.</summary>
    public static string OptimizationComment(int runId) => $"Optimización #{runId}";

    /// <summary>Comentario del historial TRIP al pasar de DRAFT a PLANNED por optimizar: 'Ruta optimizada (versión n)'.</summary>
    public static string TripOptimizedComment(int version) => $"Ruta optimizada (versión {version})";

    // ---------------- Pin manual ----------------

    /// <summary>
    /// Valida la coordenada del pin manual. Devuelve (campo, mensaje) del primer error o null si es válida. NaN e infinitos
    /// se tratan como fuera de rango.
    /// </summary>
    public static (string Field, string Message)? ValidateLocation(double? lat, double? lng)
    {
        if (lat is null || lng is null) return (lat is null ? "lat" : "lng", LocationRequiredMessage);
        if (double.IsNaN(lat.Value) || lat.Value < -90 || lat.Value > 90) return ("lat", LatitudeRangeMessage);
        if (double.IsNaN(lng.Value) || lng.Value < -180 || lng.Value > 180) return ("lng", LongitudeRangeMessage);
        return null;
    }

    // ---------------- Optimización en tres fases ----------------

    /// <summary>
    /// ¿El resultado del motor ya no aplica? true si la ruta dejó de ser editable, si la versión vigente de Route no es la
    /// de la instantánea o si el RowVersion del Trip cambió (cualquier mutación de la ruta lo cambia). Un RowVersion null
    /// equivale a vacío.
    /// </summary>
    public static bool IsStale(OptimizationSnapshot snapshot, int? currentRouteId, byte[]? currentRowVersion, bool isEditable)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!isEditable) return true;
        if (snapshot.RouteId != currentRouteId) return true;
        // Sin RowVersion (proveedor sin rowversion, p. ej. InMemory en pruebas) cuenta como arreglo vacío en ambos lados.
        var before = snapshot.TripRowVersion ?? Array.Empty<byte>();
        var now = currentRowVersion ?? Array.Empty<byte>();
        return !before.AsSpan().SequenceEqual(now);
    }

    /// <summary>Recorta el ErrorMessage de una corrida a <see cref="RunErrorMaxLength"/> caracteres (nunca null ni vacío).</summary>
    public static string TrimRunError(string? message)
    {
        var m = string.IsNullOrWhiteSpace(message) ? EngineErrorMessage : message.Trim();
        return m.Length <= RunErrorMaxLength ? m : m[..RunErrorMaxLength];
    }
}
