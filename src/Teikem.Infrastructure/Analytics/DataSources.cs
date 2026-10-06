using Teikem.Domain.Common;
using Teikem.Domain.Constants;

namespace Teikem.Infrastructure.Analytics;

public enum DataFieldType { Text, Number, Date, Bool }

/// <summary>Campo nativo de una fuente de datos, con etiqueta multilingüe y tipo (para comparar y formatear).</summary>
public sealed record DataField(string Key, string LabelEs, string LabelEn, DataFieldType Type, bool IsMoney = false);

/// <summary>Relación muchos-a-uno conocida hacia otra fuente (ej. orden → su cliente). Nunca uno-a-muchos.</summary>
public sealed record DataRelation(string Key, string TargetSourceKey, string LocalField, string LabelEs, string LabelEn);

public sealed class DataQuery
{
    /// <summary>Desde (inclusivo), como instante UTC: para las fuentes cuyo campo de fecha es un instante (CreatedAtUtc…).</summary>
    public DateTime? FromUtc { get; init; }
    /// <summary>Hasta (EXCLUSIVO), como instante UTC.</summary>
    public DateTime? ToUtc { get; init; }
    /// <summary>
    /// Lote 15: el mismo rango en días LOCALES de la compañía (desde inclusivo, hasta EXCLUSIVO) para las fuentes cuyo campo de
    /// fecha es un día de calendario (Trip.PlanDate, Contract.StartDate). Lo llena el motor con ITenantClock; NULL = derivarlo
    /// de FromUtc/ToUtc (compatibilidad con quien arma la consulta a mano).
    /// </summary>
    public DateOnly? FromDay { get; init; }
    public DateOnly? ToDayExclusive { get; init; }
    /// <summary>Ids concretos a cargar (para resolver relaciones); NULL = todos.</summary>
    public IReadOnlyCollection<int>? Ids { get; init; }
}

public sealed class DataRow : Dictionary<string, object?>
{
    public DataRow() : base(StringComparer.OrdinalIgnoreCase) { }
}

/// <summary>
/// Fuente de datos registrable. Cada módulo de negocio registra las suyas (Órdenes, Clientes, Productos, ...);
/// el Lote 1 trae AUDIT_LOG, SECURITY_EVENT y USER para probar el motor de vistas/indicadores/gráficos.
/// </summary>
public interface IDataSource
{
    string Key { get; }
    string LabelEs { get; }
    string LabelEn { get; }
    /// <summary>Código EntityType cuando la fuente representa una entidad con campos personalizados (módulo F).</summary>
    string? EntityTypeCode { get; }
    /// <summary>Campo de fecha de actividad (NULL = la fuente es un "estado actual", sin rango de fecha aplicable).</summary>
    string? DateField { get; }
    /// <summary>Campo identificador (destino de relaciones muchos-a-uno).</summary>
    string IdField { get; }
    /// <summary>Módulo de negocio por defecto para indicadores nuevos (heurística; el usuario lo puede cambiar).</summary>
    string DefaultBusinessModule { get; }
    IReadOnlyList<DataField> Fields { get; }
    IReadOnlyList<DataRelation> Relations { get; }
    /// <summary>
    /// Lote 29 (Rentas R3): módulo de la compañía (ModuleKeys) que debe estar encendido para leer la fuente, además del permiso de
    /// su EntityType y del módulo de negocio de cada definición. null (por defecto) = sin requisito propio. Lo usan las fuentes de
    /// un submódulo que se apaga aparte de su módulo de negocio (Rentas dentro de Almacén): con el módulo apagado la fuente, sus
    /// vistas, indicadores y gráficos no se listan ni se leen (404), igual que una fuente sin permiso.
    /// </summary>
    string? TenantModule => null;
    Task<List<DataRow>> LoadAsync(DataQuery query, CancellationToken ct);
}

public interface IDataSourceRegistry
{
    IReadOnlyList<IDataSource> All { get; }
    IDataSource Get(string key);
    bool TryGet(string key, out IDataSource source);
}

public sealed class DataSourceRegistry(IEnumerable<IDataSource> sources) : IDataSourceRegistry
{
    private readonly Dictionary<string, IDataSource> _map = sources.ToDictionary(s => s.Key, s => s, StringComparer.OrdinalIgnoreCase);
    public IReadOnlyList<IDataSource> All => _map.Values.OrderBy(s => s.Key).ToList();
    public IDataSource Get(string key) => TryGet(key, out var s) ? s : throw new Exceptions.NotFoundException("Fuente de datos", key);
    public bool TryGet(string key, out IDataSource source) => _map.TryGetValue(key ?? string.Empty, out source!);
}

/// <summary>
/// Resuelve el rango de fecha de un indicador, gráfico o vista (LAST7 por defecto). Lote 15 (decisión del dueño: "hoy" es en
/// hora de Puerto Rico en todo lo que dependa del día): los días se calculan en la zona de la compañía con el punto único del
/// Lote 14 (ITenantClock) y se convierten a instantes UTC para la consulta: desde = 00:00 local del primer día (inclusivo),
/// hasta = 00:00 local del día siguiente al último (EXCLUSIVO).
/// - LAST7 = hoy y los 6 días anteriores; LAST30 = hoy y los 29 anteriores; THIS_MONTH = del día 1 del mes local a hoy;
///   CUSTOM = [Desde, Hasta] como días locales (cualquiera de los dos puede faltar); ALL = sin rango.
/// </summary>
public static class DateRangeResolver
{
    /// <summary>Días locales [desde, hasta] (ambos inclusivos) del modo, con "hoy" dado.</summary>
    public static (DateOnly? From, DateOnly? To) LocalDays(string? mode, DateOnly? from, DateOnly? to, DateOnly today)
        => (mode ?? DateRangeModes.Last7).ToUpperInvariant() switch
        {
            DateRangeModes.All => (null, null),
            DateRangeModes.Last30 => (today.AddDays(-29), today),
            DateRangeModes.ThisMonth => (new DateOnly(today.Year, today.Month, 1), today),
            DateRangeModes.Custom => (from, to),
            _ => (today.AddDays(-6), today),
        };

    /// <summary>Rango en instantes UTC de los días locales del modo en la zona indicada (pura: para pruebas de bordes).</summary>
    public static (DateTime? FromUtc, DateTime? ToUtc) Resolve(string? mode, DateOnly? from, DateOnly? to, DateTime nowUtc, TimeZoneInfo zone)
    {
        var (f, t) = LocalDays(mode, from, to, LocalDay.Today(nowUtc, zone));
        return LocalDay.UtcRange(f, t, zone);
    }

    /// <summary>Rango en instantes UTC con el reloj de la compañía (hoy local y su zona).</summary>
    public static (DateTime? FromUtc, DateTime? ToUtc) Resolve(string? mode, DateOnly? from, DateOnly? to, Abstractions.ITenantClock clock)
    {
        var (f, t) = LocalDays(mode, from, to, clock.Today);
        return clock.UtcRange(f, t);
    }
}
