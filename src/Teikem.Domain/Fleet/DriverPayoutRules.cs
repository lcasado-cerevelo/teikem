using Teikem.Domain.Common;
using Teikem.Domain.Constants;

namespace Teikem.Domain.Fleet;

/// <summary>Intento de entrega que entra al cálculo: número (1, 2, 3...) y si en ese intento se entregó.</summary>
public readonly record struct PayoutAttempt(int Number, bool Delivered);

/// <summary>Línea del pago: DELIVERY (la entrega) o ATTEMPT (un intento). Amount = 0 con nota si no hubo tarifa (R15).</summary>
public sealed record PayoutLine(string Kind, int? AttemptNumber, decimal Amount, string? Note);

/// <summary>Resultado del motor: total y líneas (primero la entrega, luego los intentos pagados por número).</summary>
public sealed record PayoutResult(decimal Total, IReadOnlyList<PayoutLine> Lines);

/// <summary>Tarifa elegida para un intento: monto, nivel del que salió (null si no hubo) y nota si no había tarifa.</summary>
public readonly record struct AttemptRatePick(decimal Amount, int? LevelUsed, string? Note);

/// <summary>
/// Lote 4 (P6) — Motor puro del pago al chofer (módulo 11A). Lo reutiliza la liquidación del Lote 9; no toca BD.
/// - PickCurrent: la fila efectivo-fechada vigente en una fecha (EffectiveTo exclusivo; empate → EffectiveFrom mayor).
/// - AttemptRate: tarifa del intento n con el fallback R16 (n mayor que el último nivel con tarifa ⇒ ese nivel).
/// - Compute: las tres fórmulas del catálogo DriverPayoutFormula.
///   · DELIVERY_PLUS_ATTEMPTS: la entrega (una vez, si hubo entrega) + cada intento, incluido el exitoso.
///   · DELIVERY_INCLUDES_FIRST: la entrega (si hubo) + los intentos desde el 2º.
///   · FAILED_REPLACES_DELIVERY: cada intento fallido paga su tarifa; el exitoso paga solo la entrega.
///   Una tarifa ausente genera una línea de 0 con la nota 'sin tarifa configurada' (R15): el vacío queda visible.
/// </summary>
public static class DriverPayoutRules
{
    /// <summary>Tope de niveles de intento por compañía (CK_DriverPayPolicy_Levels).</summary>
    public const int MaxAttemptLevels = 20;

    /// <summary>Niveles de intento por defecto cuando la compañía no tiene fila en DriverPayPolicy.</summary>
    public const int DefaultAttemptLevels = 2;

    /// <summary>Fórmula por defecto cuando la compañía no tiene fila en DriverPayPolicy.</summary>
    public const string DefaultFormula = DriverPayoutFormulas.DeliveryPlusAttempts;

    /// <summary>Nota de una línea que paga 0 porque no hay tarifa configurada (R15).</summary>
    public const string NoRateNote = "sin tarifa configurada";

    /// <summary>Tope de intentos que acepta un cálculo (defensa contra entradas absurdas en la vista previa).</summary>
    public const int MaxAttemptsPerDelivery = 50;

    public const string LineDelivery = "DELIVERY";
    public const string LineAttempt = "ATTEMPT";

    /// <summary>Fórmulas que entiende el motor, en el orden del catálogo.</summary>
    public static readonly IReadOnlyList<string> Formulas = new[]
    {
        DriverPayoutFormulas.DeliveryPlusAttempts,
        DriverPayoutFormulas.DeliveryIncludesFirst,
        DriverPayoutFormulas.FailedReplacesDelivery,
    };

    public static bool IsKnownFormula(string? code)
        => code is not null && Formulas.Contains(code.Trim().ToUpperInvariant());

    /// <summary>
    /// Fila vigente en asOf (EffectiveDated.IsCurrentOn: EffectiveFrom &lt;= asOf &lt; EffectiveTo). Las filas de longitud
    /// cero nunca son vigentes. Si hubiera más de una (no debería: UQ_*_Open), gana la de EffectiveFrom mayor.
    /// </summary>
    public static T? PickCurrent<T>(IEnumerable<T> rows, DateOnly asOf) where T : class, IEffectiveDated
        => rows.Where(r => EffectiveDated.IsCurrentOn(r, asOf))
            .OrderByDescending(r => r.EffectiveFrom)
            .FirstOrDefault();

    /// <summary>
    /// Tarifa del intento n. Si n supera el nivel más alto con tarifa, se usa ese nivel (fallback R16). Un nivel intermedio
    /// sin tarifa, o la ausencia total de tarifas de intento, paga 0 con la nota 'sin tarifa configurada'.
    /// </summary>
    public static AttemptRatePick AttemptRate(IReadOnlyDictionary<int, decimal> ratesByLevel, int n)
    {
        if (n < 1) throw new ArgumentOutOfRangeException(nameof(n), "El número de intento debe ser mayor o igual a 1.");
        if (ratesByLevel.Count == 0) return new AttemptRatePick(0m, null, NoRateNote);
        var highest = ratesByLevel.Keys.Max();
        if (n > highest) return new AttemptRatePick(ratesByLevel[highest], highest, null);
        return ratesByLevel.TryGetValue(n, out var rate)
            ? new AttemptRatePick(rate, n, null)
            : new AttemptRatePick(0m, null, NoRateNote);
    }

    /// <summary>
    /// Valida la secuencia de intentos: al menos uno, números entre 1 y el tope, sin repetir, y a lo sumo una entrega,
    /// que debe ser el último intento. Devuelve el mensaje de error o null.
    /// </summary>
    public static string? ValidateAttempts(IReadOnlyCollection<PayoutAttempt>? attempts)
    {
        if (attempts is null || attempts.Count == 0) return "Indique al menos un intento.";
        if (attempts.Count > MaxAttemptsPerDelivery) return $"Se admiten como máximo {MaxAttemptsPerDelivery} intentos.";
        if (attempts.Any(a => a.Number < 1 || a.Number > MaxAttemptsPerDelivery))
            return $"El número de intento debe estar entre 1 y {MaxAttemptsPerDelivery}.";
        if (attempts.Select(a => a.Number).Distinct().Count() != attempts.Count) return "Hay números de intento repetidos.";
        var delivered = attempts.Where(a => a.Delivered).ToList();
        if (delivered.Count > 1) return "Solo un intento puede ser la entrega.";
        if (delivered.Count == 1 && delivered[0].Number != attempts.Max(a => a.Number))
            return "La entrega debe ser el último intento.";
        return null;
    }

    /// <summary>¿Existe el nivel de intento n entre los niveles configurados (1..levels)? Devuelve el mensaje de error o null.</summary>
    public static string? ValidateAttemptNumber(int n, int levels)
        => n >= 1 && n <= levels ? null : $"El intento {n} no existe; los niveles configurados van de 1 a {levels}.";

    /// <summary>
    /// Calcula el pago de una entrega (con sus intentos) según la fórmula. deliveryRate null = sin tarifa de entrega
    /// (línea de 0 con nota). Las entradas inválidas (fórmula desconocida, intentos inválidos) lanzan ArgumentException.
    /// </summary>
    public static PayoutResult Compute(string formula, decimal? deliveryRate, IReadOnlyDictionary<int, decimal> attemptRates,
        IEnumerable<PayoutAttempt> attempts)
    {
        var code = formula?.Trim().ToUpperInvariant();
        if (!IsKnownFormula(code)) throw new ArgumentException($"Fórmula de pago desconocida: '{formula}'.", nameof(formula));
        var list = attempts.OrderBy(a => a.Number).ToList();
        var error = ValidateAttempts(list);
        if (error is not null) throw new ArgumentException(error, nameof(attempts));

        var success = list.Where(a => a.Delivered).Select(a => (PayoutAttempt?)a).FirstOrDefault();
        var lines = new List<PayoutLine>();

        // La entrega se paga una sola vez, en las tres fórmulas, si hubo entrega (primera línea).
        if (success is { } s)
            lines.Add(new PayoutLine(LineDelivery, s.Number, deliveryRate ?? 0m, deliveryRate is null ? NoRateNote : null));

        foreach (var a in list)
        {
            var paysAttempt = code switch
            {
                DriverPayoutFormulas.DeliveryPlusAttempts => true,
                DriverPayoutFormulas.DeliveryIncludesFirst => a.Number >= 2,
                DriverPayoutFormulas.FailedReplacesDelivery => !a.Delivered,
                _ => false,
            };
            if (paysAttempt)
            {
                var pick = AttemptRate(attemptRates, a.Number);
                lines.Add(new PayoutLine(LineAttempt, a.Number, pick.Amount, pick.Note));
            }
        }

        return new PayoutResult(lines.Sum(l => l.Amount), lines);
    }
}
