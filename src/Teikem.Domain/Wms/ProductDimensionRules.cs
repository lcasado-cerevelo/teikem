using Teikem.Domain.Tenancy;

namespace Teikem.Domain.Wms;

/// <summary>Lote 230 — unidades de las medidas del producto (largo, ancho y alto). Se guardan tal como se escribieron, con su unidad.</summary>
public static class DimensionUnits
{
    public const string Inch = "IN";
    public const string Centimeter = "CM";
    public const string Foot = "FT";
    public const string Meter = "M";
    public static readonly IReadOnlyList<string> All = [Inch, Centimeter, Foot, Meter];
}

/// <summary>Lote 230 — unidades del peso del producto. El peso se guarda SIEMPRE en kg (Product.WeightKg); la unidad solo dice cómo se escribió y cómo se muestra.</summary>
public static class WeightUnits
{
    public const string Pound = "LB";
    public const string Kilogram = "KG";
    public static readonly IReadOnlyList<string> All = [Pound, Kilogram];
}

/// <summary>Sistema de medidas que se sugiere por la región de la compañía.</summary>
public static class MeasurementSystems
{
    public const string Imperial = "IMPERIAL";
    public const string Metric = "METRIC";
}

/// <summary>
/// Unidades sugeridas: DimensionUnit para lo chico (pulgadas / centímetros), LargeDimensionUnit para lo grande (pies / metros) y WeightUnit (libras / kilos).
/// </summary>
public sealed record UnitsSuggestion(string RegionCode, string System, string DimensionUnit, string LargeDimensionUnit, string WeightUnit);

/// <summary>Estado de las medidas de un producto: largo, ancho y alto en <see cref="DimensionUnit"/> y el volumen interno en m³.</summary>
public sealed record DimensionState(decimal? Length, decimal? Width, decimal? Height, string? DimensionUnit, decimal? VolumeM3);

/// <summary>Estado del peso: en kg (la verdad interna) y la unidad en que se escribió.</summary>
public sealed record WeightState(decimal? WeightKg, string? WeightUnit);

/// <summary>Cómo se muestra el volumen: en la unidad cúbica de las medidas (IN3, CM3, FT3, M3) y si salió del cálculo de las tres medidas.</summary>
public sealed record VolumeDisplay(decimal? Volume, string VolumeUnit, bool Calculated);

/// <summary>
/// Lote 230 — reglas puras de las dimensiones y el peso del producto (sin BD). Acordado con Luis (docs/ofertas-kits-fotos-diseno.md §6b y §8):
/// - Largo, ancho y alto se guardan como se escribieron, con su unidad (IN, CM, FT, M). Son de la UNIDAD BASE del producto (lo que se vende), no de la paleta.
/// - Con las TRES medidas puestas el volumen se CALCULA (convertido a m³, 4 decimales, que es la columna Product.VolumeM3) y un volumen mandado a mano se IGNORA
///   (gana el cálculo; decisión del lote: así una pantalla que reenvía el volumen viejo junto con una medida nueva no falla).
/// - Si falta cualquiera de las tres, el volumen mandado a mano se acepta y se conserva; un volumen escrito antes NO se pierde por poner (o quitar) una medida suelta:
///   solo se reemplaza cuando están las tres.
/// - 0 en una medida = quitarla (como en los mínimos). Negativa → 400.
/// - El peso se guarda en kg (Product.WeightKg, 3 decimales) y se recuerda la unidad en que se escribió (LB o KG) para mostrarlo igual (LB con 2 decimales: la vuelta
///   kg → lb recupera exactamente lo que se escribió con hasta 2 decimales).
/// - Unidad sugerida por la región de la compañía: Puerto Rico y Estados Unidos usan el sistema inglés (IN / FT / LB); cualquier otra región, el métrico (CM / M / KG).
/// </summary>
public static class ProductDimensionRules
{
    public const decimal MetersPerInch = 0.0254m;
    public const decimal MetersPerCentimeter = 0.01m;
    public const decimal MetersPerFoot = 0.3048m;
    public const decimal KgPerPound = 0.45359237m;

    /// <summary>Tope de una medida (columna DECIMAL(12,4)).</summary>
    public const decimal MaxDimension = 100_000_000m;

    // ------------------------------------------------------------------ mensajes (400)
    public const string DimensionUnitInvalid = "La unidad de las medidas debe ser IN (pulgadas), CM (centímetros), FT (pies) o M (metros).";
    public const string WeightUnitInvalid = "La unidad del peso debe ser LB (libras) o KG (kilos).";
    public const string WeightNegative = "El peso no puede ser negativo.";
    public const string WeightTooLarge = "El peso es demasiado grande.";
    public static string DimensionNegative(string label) => $"El {label} no puede ser negativo.";
    public static string DimensionInvalid(string label) => $"El {label} debe ser un número de hasta 4 decimales.";
    public const string VolumeTooLarge = "El volumen calculado con el largo, el ancho y el alto es demasiado grande.";

    /// <summary>Etiqueta de cada medida para los mensajes.</summary>
    public static string Label(string field) => field switch { "length" => "largo", "width" => "ancho", "height" => "alto", _ => field };

    // ------------------------------------------------------------------ unidades

    /// <summary>Normaliza la unidad de las medidas (mayúsculas, sin espacios; solo los cuatro códigos). Vacía = null.</summary>
    public static (string? Unit, string? Error) NormalizeDimensionUnit(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return (null, null);
        var code = value.Trim().ToUpperInvariant();
        return DimensionUnits.All.Contains(code) ? (code, null) : (null, DimensionUnitInvalid);
    }

    /// <summary>Normaliza la unidad del peso. Vacía = null.</summary>
    public static (string? Unit, string? Error) NormalizeWeightUnit(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return (null, null);
        var code = value.Trim().ToUpperInvariant();
        return WeightUnits.All.Contains(code) ? (code, null) : (null, WeightUnitInvalid);
    }

    /// <summary>Metros por cada unidad de medida.</summary>
    public static decimal MetersPer(string unit) => unit switch
    {
        DimensionUnits.Inch => MetersPerInch,
        DimensionUnits.Centimeter => MetersPerCentimeter,
        DimensionUnits.Foot => MetersPerFoot,
        DimensionUnits.Meter => 1m,
        _ => throw new ArgumentOutOfRangeException(nameof(unit), unit, DimensionUnitInvalid),
    };

    /// <summary>Unidad cúbica que corresponde a la unidad de las medidas (IN → IN3…).</summary>
    public static string CubicUnit(string? dimensionUnit) => (dimensionUnit ?? DimensionUnits.Meter) + "3";

    /// <summary>¿Están las tres medidas (mayores que cero)?</summary>
    public static bool HasAllThree(decimal? length, decimal? width, decimal? height) => length is > 0m && width is > 0m && height is > 0m;

    /// <summary>Volumen en m³ (4 decimales, como Product.VolumeM3) de las tres medidas en su unidad; null si falta alguna.</summary>
    public static decimal? VolumeM3(decimal? length, decimal? width, decimal? height, string? unit)
    {
        if (!HasAllThree(length, width, height) || unit is null) return null;
        var m = MetersPer(unit);
        return Math.Round(length!.Value * m * (width!.Value * m) * (height!.Value * m), 4, MidpointRounding.AwayFromZero);
    }

    /// <summary>Volumen exacto en la unidad cúbica de las medidas (p. ej. pulgadas cúbicas), 4 decimales; null si falta alguna.</summary>
    public static decimal? VolumeInUnit(decimal? length, decimal? width, decimal? height)
        => HasAllThree(length, width, height) ? Math.Round(length!.Value * width!.Value * height!.Value, 4, MidpointRounding.AwayFromZero) : null;

    /// <summary>Convierte un volumen en m³ a la unidad cúbica de <paramref name="unit"/> (4 decimales).</summary>
    public static decimal FromCubicMeters(decimal volumeM3, string unit)
    {
        var m = MetersPer(unit);
        return Math.Round(volumeM3 / (m * m * m), 4, MidpointRounding.AwayFromZero);
    }

    /// <summary>Peso en kg (3 decimales, como Product.WeightKg) de un peso escrito en <paramref name="unit"/>.</summary>
    public static decimal ToKg(decimal weight, string unit)
        => Math.Round(unit == WeightUnits.Pound ? weight * KgPerPound : weight, 3, MidpointRounding.AwayFromZero);

    /// <summary>El peso guardado en kg mostrado en <paramref name="unit"/> (LB con 2 decimales; KG con 3, tal como se guarda).</summary>
    public static decimal FromKg(decimal weightKg, string unit)
        => unit == WeightUnits.Pound ? Math.Round(weightKg / KgPerPound, 2, MidpointRounding.AwayFromZero) : weightKg;

    // ------------------------------------------------------------------ sugerencia por región

    /// <summary>Puerto Rico y Estados Unidos = sistema inglés (IN / FT / LB); el resto, métrico (CM / M / KG).</summary>
    public static UnitsSuggestion Suggest(string? regionCode)
    {
        var region = (regionCode ?? TenantFormatRules.DefaultRegion).Trim().ToUpperInvariant();
        return region is TenantFormatRules.PuertoRico or TenantFormatRules.UnitedStates
            ? new UnitsSuggestion(region, MeasurementSystems.Imperial, DimensionUnits.Inch, DimensionUnits.Foot, WeightUnits.Pound)
            : new UnitsSuggestion(region, MeasurementSystems.Metric, DimensionUnits.Centimeter, DimensionUnits.Meter, WeightUnits.Kilogram);
    }

    // ------------------------------------------------------------------ validación y aplicación

    /// <summary>Errores de las medidas pedidas (por campo: length, width, height). 0 es válido (= quitarla).</summary>
    public static IReadOnlyList<(string Field, string Message)> ValidateDimensions(decimal? length, decimal? width, decimal? height)
    {
        var errors = new List<(string, string)>();
        foreach (var (field, value) in new[] { ("length", length), ("width", width), ("height", height) })
        {
            if (value is not decimal v) continue;
            if (v < 0m) errors.Add((field, DimensionNegative(Label(field))));
            else if (decimal.Round(v, 4, MidpointRounding.ToZero) != v || v >= MaxDimension) errors.Add((field, DimensionInvalid(Label(field))));
        }
        return errors;
    }

    /// <summary>Error del peso escrito en su unidad (negativo, o que en kg no cabe en la columna).</summary>
    public static string? ValidateWeight(decimal? weight, string unit)
    {
        if (weight is not decimal w) return null;
        if (w < 0m) return WeightNegative;
        if (w >= 1_000_000_000m || ToKg(w, unit) >= 1_000_000_000m) return WeightTooLarge;
        return null;
    }

    /// <summary>
    /// Aplica lo pedido sobre lo actual (null = sin cambio; 0 = quitar esa medida). La unidad final es la pedida, si no la que ya tenía y, si no, la sugerida
    /// (<paramref name="defaultUnit"/>); sin ninguna medida la unidad pedida se recuerda igual. Con las tres medidas el volumen se calcula (el manual se ignora);
    /// si falta alguna, gana el volumen manual pedido o se conserva el que había. Devuelve un error si el volumen calculado no cabe en la columna.
    /// </summary>
    public static (DimensionState State, string? Error) Apply(DimensionState current, decimal? length, decimal? width, decimal? height, string? unit,
        decimal? volumeM3, string defaultUnit)
    {
        static decimal? Pick(decimal? requested, decimal? current) => requested is decimal r ? (r == 0m ? null : r) : current;
        var l = Pick(length, current.Length);
        var w = Pick(width, current.Width);
        var h = Pick(height, current.Height);
        var finalUnit = unit ?? current.DimensionUnit;
        if (finalUnit is null && (l is not null || w is not null || h is not null)) finalUnit = defaultUnit;

        decimal? volume;
        if (HasAllThree(l, w, h))
        {
            volume = VolumeM3(l, w, h, finalUnit);
            if (volume >= 100_000_000m) return (current, VolumeTooLarge);
        }
        else volume = volumeM3 ?? current.VolumeM3;
        return (new DimensionState(l, w, h, finalUnit, volume), null);
    }

    /// <summary>
    /// Aplica el peso pedido: <paramref name="weight"/> (en <paramref name="unit"/> o, sin ella, la unidad que ya tenía o la sugerida) manda sobre
    /// <paramref name="weightKg"/> (la forma de siempre, en kg). Sin ninguno de los dos el peso no cambia; la unidad pedida se recuerda igual.
    /// </summary>
    public static WeightState ApplyWeight(WeightState current, decimal? weight, string? unit, decimal? weightKg, string defaultUnit)
    {
        if (weight is decimal w)
        {
            var u = unit ?? current.WeightUnit ?? defaultUnit;
            return new WeightState(ToKg(w, u), u);
        }
        if (weightKg is decimal kg) return new WeightState(kg, unit ?? current.WeightUnit ?? WeightUnits.Kilogram);
        return new WeightState(current.WeightKg, unit ?? current.WeightUnit);
    }

    /// <summary>El volumen para mostrar: calculado de las tres medidas en su unidad cúbica o, si falta alguna, el guardado en m³ convertido a esa unidad (M3 sin unidad).</summary>
    public static VolumeDisplay DisplayVolume(DimensionState s)
    {
        if (HasAllThree(s.Length, s.Width, s.Height) && s.DimensionUnit is not null)
            return new VolumeDisplay(VolumeInUnit(s.Length, s.Width, s.Height), CubicUnit(s.DimensionUnit), true);
        if (s.VolumeM3 is not decimal v) return new VolumeDisplay(null, CubicUnit(s.DimensionUnit), false);
        return new VolumeDisplay(s.DimensionUnit is null ? v : FromCubicMeters(v, s.DimensionUnit), CubicUnit(s.DimensionUnit), false);
    }
}
