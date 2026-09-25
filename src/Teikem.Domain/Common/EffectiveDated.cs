namespace Teikem.Domain.Common;

/// <summary>
/// Fila con vigencia por fechas (principio #8: historial efectivo-fechado). EffectiveFrom es inclusivo y EffectiveTo
/// es exclusivo (NULL = abierta). Editar = cerrar la fila (EffectiveTo = fecha nueva) y abrir otra; nunca se sobrescribe
/// el valor. Dos ediciones el mismo día dejan una fila de longitud cero que nunca está vigente pero queda como historial.
/// </summary>
public interface IEffectiveDated
{
    DateOnly EffectiveFrom { get; set; }
    DateOnly? EffectiveTo { get; set; }
}

/// <summary>
/// Reglas puras de vigencia (sin EF). Los errores de fecha se lanzan como ArgumentException; el servicio los traduce a 400.
/// </summary>
public static class EffectiveDated
{
    /// <summary>¿La fila está vigente en la fecha? EffectiveFrom &lt;= fecha &lt; EffectiveTo (abierta si EffectiveTo es NULL).</summary>
    public static bool IsCurrentOn(IEffectiveDated row, DateOnly date)
        => row.EffectiveFrom <= date && (row.EffectiveTo is null || row.EffectiveTo.Value > date);

    /// <summary>Cierra la fila en la fecha dada (exclusiva). La fecha no puede ser anterior al inicio de vigencia.</summary>
    public static void Close(IEffectiveDated row, DateOnly date)
    {
        if (date < row.EffectiveFrom)
            throw new ArgumentException($"La fecha de cierre ({date:yyyy-MM-dd}) no puede ser anterior al inicio de vigencia ({row.EffectiveFrom:yyyy-MM-dd}).", nameof(date));
        row.EffectiveTo = date;
    }

    /// <summary>
    /// ¿La fila sigue viva en 'date' o después? (EffectiveTo NULL o posterior a la fecha). Es la guarda de duplicado por
    /// intervalo: una fila nueva que nace abierta en 'date' choca con toda fila existente que no haya terminado antes de 'date'.
    /// </summary>
    public static bool IsLiveOnOrAfter(IEffectiveDated row, DateOnly date)
        => row.EffectiveTo is null || row.EffectiveTo.Value > date;

    /// <summary>
    /// Cierre en cascada (p. ej. los tramos de un componente que se cierra): fija EffectiveTo = max(date, EffectiveFrom),
    /// de modo que una fila nacida después de la fecha de cierre queda de longitud cero y nunca con EffectiveTo &lt; EffectiveFrom
    /// (CK_RateTier_Dates). Una fila ya cerrada no se toca.
    /// </summary>
    public static void CloseNotBefore(IEffectiveDated row, DateOnly date)
    {
        if (row.EffectiveTo is not null) return;
        row.EffectiveTo = date < row.EffectiveFrom ? row.EffectiveFrom : date;
    }

    /// <summary>
    /// Valida que una versión nueva pueda abrirse en newFrom sobre la fila abierta: newFrom &gt;= EffectiveFrom.
    /// El mismo día está permitido (la fila anterior queda de longitud cero, como historial).
    /// </summary>
    public static void ValidateNewVersion(IEffectiveDated open, DateOnly newFrom)
    {
        if (newFrom < open.EffectiveFrom)
            throw new ArgumentException($"La nueva vigencia ({newFrom:yyyy-MM-dd}) no puede ser anterior al inicio de la fila actual ({open.EffectiveFrom:yyyy-MM-dd}).", nameof(newFrom));
    }
}
