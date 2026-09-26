using System.Globalization;

namespace Teikem.Domain.Fleet;

/// <summary>
/// Lote 4 (P1): reglas puras del vehículo (VIN, capacidades, odómetro, año del modelo y corrección manual del odómetro).
/// Sin BD: VehicleService las aplica antes de guardar y devuelve el error en 'errors' del campo (400).
/// La precisión DECIMAL (MaxWeightKg 12,3; MaxVolumeM3 12,4; CurrentOdometerKm 12,1) la valida FleetRules.DecimalError.
/// </summary>
public static class VehicleRules
{
    public const int VinMaxLength = 40;
    public const int MinModelYear = 1900;

    public const string VinTooLongMessage = "El VIN admite como máximo 40 caracteres.";
    public const string NegativeCapacityMessage = "La capacidad no puede ser negativa.";
    public const string MaxStopsMessage = "El tope de paradas debe ser mayor o igual a 1.";
    public const string NegativeOdometerMessage = "El odómetro no puede ser negativo.";

    /// <summary>VIN en mayúsculas y sin espacios; vacío → null. Más de 40 caracteres → error.</summary>
    public static (string? Vin, string? Error) NormalizeVin(string? raw)
    {
        if (raw is null) return (null, null);
        var vin = new string(raw.Where(c => !char.IsWhiteSpace(c)).ToArray()).ToUpperInvariant();
        if (vin.Length == 0) return (null, null);
        if (vin.Length > VinMaxLength) return (null, VinTooLongMessage);
        return (vin, null);
    }

    /// <summary>Capacidades: peso y volumen ≥ 0; tope de paradas ≥ 1. Devuelve (campo, mensaje) por cada error.</summary>
    public static IReadOnlyList<(string Field, string Message)> ValidateCapacities(decimal? maxWeightKg, decimal? maxVolumeM3, int? maxStops)
    {
        var errors = new List<(string, string)>();
        if (maxWeightKg is < 0) errors.Add(("maxWeightKg", NegativeCapacityMessage));
        if (maxVolumeM3 is < 0) errors.Add(("maxVolumeM3", NegativeCapacityMessage));
        if (maxStops is < 1) errors.Add(("maxStops", MaxStopsMessage));
        return errors;
    }

    public static string? ValidateOdometer(decimal? km) => km is < 0 ? NegativeOdometerMessage : null;

    /// <summary>Año del modelo entre 1900 y el año en curso + 1 (el modelo del año siguiente ya se vende).</summary>
    public static string? ValidateModelYear(int? year, int currentYear)
    {
        if (year is null) return null;
        var max = currentYear + 1;
        return year < MinModelYear || year > max ? $"El año del modelo debe estar entre {MinModelYear} y {max}." : null;
    }

    /// <summary>
    /// Corrección manual del odómetro: puede bajar un error tecleado, pero nunca por debajo de la última lectura registrada
    /// (carga de combustible activa u OT cerrada). Sin lecturas registradas → válido.
    /// </summary>
    public static string? ValidateManualOdometer(decimal newKm, (decimal Km, DateOnly Date)? lastRecorded)
    {
        if (lastRecorded is not { } last || newKm >= last.Km) return null;
        return $"El odómetro no puede ser menor que la última lectura registrada ({FormatKm(last.Km)} km el {last.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}).";
    }

    /// <summary>Kilómetros sin separador de miles y sin decimales sobrantes (6000.0 → '6000'; 6000.5 → '6000.5').</summary>
    public static string FormatKm(decimal km) => km.ToString("0.#", CultureInfo.InvariantCulture);
}
