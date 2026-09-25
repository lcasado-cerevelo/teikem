namespace Teikem.Infrastructure.Clients;

/// <summary>
/// Reglas puras de los tramos de "pieza extra" (Lote 2, P4). Sin EF: se prueban con xunit.
/// - Un tramo va de FromUnit a ToUnit (ToUnit null = abierto hacia arriba) en número de piezas.
/// - La pieza 1 nunca es extra: FromUnit >= 2.
/// - Los tramos abiertos de un mismo componente no pueden traslaparse (R27). SQL Server no tiene EXCLUDE,
///   así que la validación vive aquí; el CHECK MaxValue >= MinValue es la segunda barrera del rango.
/// </summary>
public static class RateTierRules
{
    /// <summary>Primera pieza que puede pagar como extra (la pieza 1 va incluida en la tarifa por servicio).</summary>
    public const int MinFromUnit = 2;

    /// <summary>Rango de un tramo (existente o candidato) para el chequeo de traslape.</summary>
    public sealed record TierRange(int Id, int FromUnit, int? ToUnit);

    /// <summary>FromUnit >= 2 y ToUnit vacío o mayor o igual que FromUnit.</summary>
    public static bool IsValidRange(int fromUnit, int? toUnit)
        => fromUnit >= MinFromUnit && (toUnit is null || toUnit.Value >= fromUnit);

    /// <summary>
    /// Devuelve el primer tramo existente que se traslapa con el candidato, o null si no hay traslape.
    /// Traslape (inclusivo): nuevo.Desde &lt;= existente.Hasta &amp;&amp; nuevo.Hasta &gt;= existente.Desde, con Hasta null = infinito.
    /// excludeId permite editar un tramo sin compararlo consigo mismo.
    /// </summary>
    public static TierRange? FindOverlap(TierRange candidate, IEnumerable<TierRange> existing, int? excludeId = null)
    {
        foreach (var e in existing)
        {
            if (excludeId.HasValue && e.Id == excludeId.Value) continue;
            if (Overlaps(candidate, e)) return e;
        }
        return null;
    }

    public static bool Overlaps(TierRange a, TierRange b)
    {
        var aTo = a.ToUnit ?? int.MaxValue;
        var bTo = b.ToUnit ?? int.MaxValue;
        return a.FromUnit <= bTo && aTo >= b.FromUnit;
    }

    /// <summary>¿La pieza número 'unit' cae dentro del tramo?</summary>
    public static bool Contains(TierRange t, int unit) => unit >= t.FromUnit && (t.ToUnit is null || unit <= t.ToUnit.Value);

    /// <summary>Texto legible del rango para mensajes: "2–5" o "6+".</summary>
    public static string Describe(int fromUnit, int? toUnit) => toUnit is null ? $"{fromUnit}+" : $"{fromUnit}–{toUnit}";
}
