using System.Globalization;

namespace Teikem.Domain.Clients;

/// <summary>
/// Lote 248 — constantes de los avisos de vencimiento (documentos del cliente y, desde la fuente SPECIAL_PRICE, precios especiales). Los ajustes de cada compañía
/// viven en dbo.Tenant (DocExpiryThresholdDays, DocExpiryDailyAfterExpiry, DocExpiryNotifyClient); aquí no hay entidad.
/// </summary>
public static class DocumentExpiryCodes
{
    /// <summary>Umbrales por omisión (días antes de vencer).</summary>
    public const string DefaultThresholds = "30,15,5";
    public const int MaxThresholds = 10;
    public const int MinThreshold = -365;
    public const int MaxThreshold = 365;
    /// <summary>Días después del último umbral (≤ 0) en que todavía se recupera un aviso que no salió porque el trabajo no corrió.</summary>
    public const int RecoveryDays = 7;
    /// <summary>Tope de avisos nuevos por corrida y por compañía; lo demás queda para la corrida siguiente.</summary>
    public const int MaxNoticesPerRun = 500;
    /// <summary>Largo máximo de la clave de no repetición (NotificationOutbox.DedupKey).</summary>
    public const int MaxDedupKeyLength = 100;
    /// <summary>Prefijo de la clave de no repetición de un documento del cliente.</summary>
    public const string ClientDocumentPrefix = "CD";
    /// <summary>Prefijo de la clave de no repetición de un precio especial (Lote 247).</summary>
    public const string SpecialPricePrefix = "SP";
}

/// <summary>Lote 248 — qué aviso toca hoy: si es de vencido, el umbral cruzado (null en el aviso diario) y la marca que va en la clave («T15», «T0», «T-7» o «D20261231»).</summary>
public sealed record ExpiryNoticeDecision(bool Expired, int? Threshold, string Marker);

/// <summary>Lote 248 — reglas puras de los umbrales y de la decisión del aviso de vencimiento. Mensajes exactos (todos 400).</summary>
public static class ExpiryNoticeRules
{
    public const string ThresholdOutOfRange = "Cada umbral es un número entero de días entre -365 y 365 (positivo = antes de vencer, 0 = el día, negativo = después).";
    public const string ThresholdRepeated = "Hay un umbral repetido.";
    public const string TooManyThresholds = "Se pueden poner hasta 10 umbrales.";

    /// <summary>Los umbrales por omisión, de mayor a menor (30, 15, 5).</summary>
    public static IReadOnlyList<int> DefaultThresholds { get; } = new[] { 30, 15, 5 };

    /// <summary>
    /// Valida una lista de umbrales y la devuelve normalizada de mayor a menor. Null = lista vacía (sin umbrales: no se avisa). Orden de los errores: cuántos, rango, repetidos.
    /// </summary>
    public static (IReadOnlyList<int> Thresholds, string? Error) Validate(IReadOnlyList<int>? values)
    {
        var list = values ?? Array.Empty<int>();
        if (list.Count > DocumentExpiryCodes.MaxThresholds) return (Array.Empty<int>(), TooManyThresholds);
        if (list.Any(t => t < DocumentExpiryCodes.MinThreshold || t > DocumentExpiryCodes.MaxThreshold)) return (Array.Empty<int>(), ThresholdOutOfRange);
        if (list.Distinct().Count() != list.Count) return (Array.Empty<int>(), ThresholdRepeated);
        return (list.OrderByDescending(t => t).ToArray(), null);
    }

    /// <summary>Lee la columna Tenant.DocExpiryThresholdDays («30,15,5»): las entradas vacías se saltan; un valor que no es entero da el error de rango. Luego <see cref="Validate"/>.</summary>
    public static (IReadOnlyList<int> Thresholds, string? Error) ParseThresholds(string? csv)
    {
        var values = new List<int>();
        foreach (var raw in (csv ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!int.TryParse(raw, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var t)) return (Array.Empty<int>(), ThresholdOutOfRange);
            values.Add(t);
        }
        return Validate(values);
    }

    /// <summary>Los umbrales de la columna o, si está rota, los de omisión (para lo que corre sin usuario: el trabajo, el aviso al vender).</summary>
    public static IReadOnlyList<int> ThresholdsOrDefault(string? csv)
    {
        var (list, error) = ParseThresholds(csv);
        return error is null ? list : DefaultThresholds;
    }

    /// <summary>Texto para guardar en Tenant.DocExpiryThresholdDays (normalizado de mayor a menor, separado por coma, sin espacios).</summary>
    public static string Format(IEnumerable<int> thresholds)
        => string.Join(",", thresholds.Distinct().OrderByDescending(t => t).Select(t => t.ToString(CultureInfo.InvariantCulture)));

    /// <summary>Días de anticipación de «por vencer»: el mayor umbral positivo, o 0 si no hay.</summary>
    public static int WarnDays(IEnumerable<int> thresholds) => thresholds.Where(t => t > 0).DefaultIfEmpty(0).Max();

    /// <summary>
    /// Cuántos días hacia atrás (desde hoy) hay que mirar «hasta» ya pasados: null = sin límite (aviso diario encendido); con algún umbral ≤ 0, |el menor| + 7 (la
    /// recuperación); si todos son positivos, 0.
    /// </summary>
    public static int? LookbackDays(IEnumerable<int> thresholds, bool dailyAfterExpiry)
    {
        if (dailyAfterExpiry) return null;
        var nonPositive = thresholds.Where(t => t <= 0).ToList();
        return nonPositive.Count == 0 ? 0 : Math.Abs(nonPositive.Min()) + DocumentExpiryCodes.RecoveryDays;
    }

    /// <summary>
    /// ¿Qué aviso toca hoy? Con d = «hasta» − hoy:
    /// - d ≥ 0: el umbral más cercano ya cruzado (el menor t ≥ 0 con d ≤ t), marca «T{t}», aviso de POR VENCER. Sin umbral cruzado, nada.
    /// - d &lt; 0 y <paramref name="allowExpired"/> = false: nada (la fuente no avisa de lo vencido).
    /// - d &lt; 0 con el aviso diario: marca «D{yyyyMMdd de hoy}», aviso de VENCIDO (uno por día).
    /// - d &lt; 0 sin el diario: el menor t ≤ 0 con d ≤ t, marca «T{t}», aviso de VENCIDO; fuera de la ventana de <see cref="LookbackDays"/>, nada. Así se recupera el
    ///   aviso del día 0 si el trabajo no corrió ese día, y un documento histórico no avisa.
    /// Solo se avisa el umbral más cercano; la clave de no repetición (<see cref="DedupKey"/>) evita repetirlo.
    /// </summary>
    public static ExpiryNoticeDecision? Decide(DateOnly validTo, DateOnly today, IReadOnlyList<int> thresholds, bool dailyAfterExpiry, bool allowExpired = true)
    {
        var d = validTo.DayNumber - today.DayNumber;
        if (d >= 0)
        {
            var crossed = thresholds.Where(t => t >= 0 && d <= t).ToList();
            if (crossed.Count == 0) return null;
            var t0 = crossed.Min();
            return new ExpiryNoticeDecision(false, t0, Marker(t0));
        }
        if (!allowExpired) return null;
        if (dailyAfterExpiry) return new ExpiryNoticeDecision(true, null, "D" + today.ToString("yyyyMMdd", CultureInfo.InvariantCulture));
        if (LookbackDays(thresholds, false) is int back && d < -back) return null;
        var past = thresholds.Where(t => t <= 0 && d <= t).ToList();
        if (past.Count == 0) return null;
        var t1 = past.Min();
        return new ExpiryNoticeDecision(true, t1, Marker(t1));
    }

    private static string Marker(int threshold) => "T" + threshold.ToString(CultureInfo.InvariantCulture);

    /// <summary>Clave de no repetición: «{prefijo}:{id}:{yyyyMMdd del hasta}:{marca}», p. ej. «CD:123:20261231:T15». Pasar de 100 caracteres es un error de programación.</summary>
    public static string DedupKey(string prefix, int id, DateOnly validTo, string marker)
    {
        var key = string.Create(CultureInfo.InvariantCulture, $"{prefix}:{id}:{validTo:yyyyMMdd}:{marker}");
        if (key.Length > DocumentExpiryCodes.MaxDedupKeyLength) throw new ArgumentException($"La clave de no repetición pasa de {DocumentExpiryCodes.MaxDedupKeyLength} caracteres: {key}");
        return key;
    }
}
