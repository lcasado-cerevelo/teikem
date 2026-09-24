using Teikem.Domain.Constants;

namespace Teikem.Infrastructure.Analytics;

public enum DataFieldType { Text, Number, Date, Bool }

/// <summary>Campo nativo de una fuente de datos, con etiqueta multilingüe y tipo (para comparar y formatear).</summary>
public sealed record DataField(string Key, string LabelEs, string LabelEn, DataFieldType Type, bool IsMoney = false);

/// <summary>Relación muchos-a-uno conocida hacia otra fuente (ej. orden → su cliente). Nunca uno-a-muchos.</summary>
public sealed record DataRelation(string Key, string TargetSourceKey, string LocalField, string LabelEs, string LabelEn);

public sealed class DataQuery
{
    public DateTime? FromUtc { get; init; }
    public DateTime? ToUtc { get; init; }
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

/// <summary>Resuelve el rango de fecha de un indicador/gráfico (LAST7 default) a instantes UTC.</summary>
public static class DateRangeResolver
{
    public static (DateTime? FromUtc, DateTime? ToUtc) Resolve(string? mode, DateOnly? from, DateOnly? to, DateTime? nowUtc = null)
    {
        var now = nowUtc ?? DateTime.UtcNow;
        var today = now.Date;
        switch ((mode ?? DateRangeModes.Last7).ToUpperInvariant())
        {
            case DateRangeModes.All: return ((DateTime?)null, (DateTime?)null);
            case DateRangeModes.Last30: return (today.AddDays(-29), today.AddDays(1));
            case DateRangeModes.ThisMonth: return (new DateTime(today.Year, today.Month, 1), today.AddDays(1));
            case DateRangeModes.Custom:
                return (from?.ToDateTime(TimeOnly.MinValue), to?.ToDateTime(TimeOnly.MinValue).AddDays(1));
            default: return (today.AddDays(-6), today.AddDays(1));
        }
    }
}
