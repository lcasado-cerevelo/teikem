using Teikem.Domain.Fleet;

namespace Teikem.Domain.Trips;

/// <summary>Parada vista por el cálculo de ETA: coordenada (si la hay), ventana y minutos de servicio.</summary>
public sealed record EtaStopInput(double? Lat, double? Lng, DateTime? WindowStartUtc, DateTime? WindowEndUtc, int ServiceMinutes);

/// <summary>Constantes del cálculo (en código en este lote): velocidad media, factor de desvío y minutos por tramo sin coordenadas.</summary>
public sealed record EtaSettings(double SpeedKmh, double DetourFactor, int DefaultLegMinutes)
{
    /// <summary>35 km/h, haversine × 1.3 y 15 minutos por tramo sin coordenadas.</summary>
    public static readonly EtaSettings Default = new(35, 1.3, 15);
}

/// <summary>Resultado por parada: llegada y salida planificadas (null sin hora de salida), tramo desde la anterior y si llega tarde.</summary>
public sealed record EtaStopResult(DateTime? ArrivalUtc, DateTime? DepartureUtc, decimal? DistanceFromPrevKm, int DurationFromPrevMin, bool Late);

/// <summary>
/// Resultado de la ruta: por parada (mismo orden que la entrada), distancia total (null si ningún tramo tiene coordenadas en
/// ambos extremos), duración total en minutos (tramos + esperas + servicio; null sin paradas) y fin planificado (salida de
/// la última parada; null sin hora de salida o sin paradas).
/// </summary>
public sealed record EtaResult(IReadOnlyList<EtaStopResult> Stops, decimal? TotalDistanceKm, int? TotalDurationMin, DateTime? EndUtc);

/// <summary>
/// Lote 5 (P0) — ETA sin motor de ruteo (DECISIÓN V8), fórmula pura y determinista:
/// - tramo con coordenadas en ambos extremos: distancia = haversine × factor de desvío (1.3), redondeada a 3 decimales
///   (DECIMAL(12,3)); minutos = max(1, techo(km / 35 × 60));
/// - primer tramo (no hay coordenada del origen) o tramo sin coordenadas: 15 minutos y distancia null;
/// - si se llega antes del inicio de la ventana se espera hasta ese inicio; salida = llegada + servicio;
/// - tardía si la llegada pasa el fin de la ventana;
/// - sin hora de salida: horas null (los tramos sí se calculan).
/// </summary>
public static class EtaCalculator
{
    private const double EarthRadiusKm = 6371.0088;

    public static EtaResult Calculate(DateTime? startUtc, IReadOnlyList<EtaStopInput>? stops, EtaSettings? settings = null)
    {
        var s = settings ?? EtaSettings.Default;
        var list = stops ?? Array.Empty<EtaStopInput>();
        if (list.Count == 0) return new EtaResult(Array.Empty<EtaStopResult>(), null, null, null);

        var results = new List<EtaStopResult>(list.Count);
        DateTime? clock = startUtc is DateTime st ? AsUtc(st) : null;
        decimal? totalKm = null;
        var totalMin = 0;
        EtaStopInput? prev = null;

        foreach (var stop in list)
        {
            decimal? legKm = null;
            int legMin;
            if (prev is not null && HasPoint(prev) && HasPoint(stop))
            {
                var km = Math.Round((decimal)(Haversine(prev.Lat!.Value, prev.Lng!.Value, stop.Lat!.Value, stop.Lng!.Value) * s.DetourFactor), 3, MidpointRounding.AwayFromZero);
                if (FleetRules.DecimalError(km, 12, 3) is not null)
                    throw new ArgumentOutOfRangeException(nameof(stops), km, "La distancia del tramo no cabe en DECIMAL(12,3).");
                legKm = km;
                legMin = Math.Max(1, (int)Math.Ceiling((double)km / s.SpeedKmh * 60.0));
                totalKm = (totalKm ?? 0m) + km;
            }
            else legMin = s.DefaultLegMinutes;

            var service = Math.Max(0, stop.ServiceMinutes);
            totalMin += legMin + service;

            DateTime? arrival = null, departure = null;
            var late = false;
            if (clock is DateTime now)
            {
                arrival = now.AddMinutes(legMin);
                if (stop.WindowStartUtc is DateTime ws && arrival.Value < AsUtc(ws))
                {
                    totalMin += (int)Math.Ceiling((AsUtc(ws) - arrival.Value).TotalMinutes);
                    arrival = AsUtc(ws);
                }
                late = stop.WindowEndUtc is DateTime we && arrival.Value > AsUtc(we);
                departure = arrival.Value.AddMinutes(service);
                clock = departure;
            }

            results.Add(new EtaStopResult(arrival, departure, legKm, legMin, late));
            prev = stop;
        }

        if (totalKm is decimal t && FleetRules.DecimalError(t, 12, 3) is not null)
            throw new ArgumentOutOfRangeException(nameof(stops), t, "La distancia total no cabe en DECIMAL(12,3).");
        return new EtaResult(results, totalKm, totalMin, clock);
    }

    /// <summary>Distancia en línea recta (km) entre dos coordenadas WGS84.</summary>
    public static double Haversine(double lat1, double lng1, double lat2, double lng2)
    {
        static double Rad(double deg) => deg * Math.PI / 180.0;
        var dLat = Rad(lat2 - lat1);
        var dLng = Rad(lng2 - lng1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                + Math.Cos(Rad(lat1)) * Math.Cos(Rad(lat2)) * Math.Sin(dLng / 2) * Math.Sin(dLng / 2);
        return 2 * EarthRadiusKm * Math.Asin(Math.Min(1.0, Math.Sqrt(a)));
    }

    private static bool HasPoint(EtaStopInput s)
        => s.Lat is double lat && s.Lng is double lng && !double.IsNaN(lat) && !double.IsNaN(lng)
           && lat is >= -90 and <= 90 && lng is >= -180 and <= 180;

    private static DateTime AsUtc(DateTime v) => v.Kind switch
    {
        DateTimeKind.Utc => v,
        DateTimeKind.Local => v.ToUniversalTime(),
        _ => DateTime.SpecifyKind(v, DateTimeKind.Utc),
    };
}
