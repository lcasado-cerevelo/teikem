using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Common;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Dsl;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Analytics;

public sealed record AggregateSpec(string Fn, string? Field);
public sealed record SortSpec(string Field, bool Desc);

/// <summary>Especificación ejecutable de un informe (módulo G).</summary>
public sealed class ReportSpec
{
    public required string SourceKey { get; init; }
    public IReadOnlyList<string> Secondary { get; init; } = Array.Empty<string>();
    /// <summary>Columnas: "Campo" (principal), "REL.Campo" (fuente combinada), "cf.field_key" (campo personalizado).</summary>
    public IReadOnlyList<string> Columns { get; init; } = Array.Empty<string>();
    public string? FilterJson { get; init; }
    public IReadOnlyList<string> GroupBy { get; init; } = Array.Empty<string>();
    public IReadOnlyList<AggregateSpec> Aggregates { get; init; } = Array.Empty<AggregateSpec>();
    public bool ShowTotals { get; init; }
    public IReadOnlyList<SortSpec> Sort { get; init; } = Array.Empty<SortSpec>();
    public DateTime? FromUtc { get; init; }
    public DateTime? ToUtc { get; init; }
    public int Skip { get; init; }
    public int Take { get; init; } = 500;
}

public sealed record ReportColumn(string Key, string Label, DataFieldType Type, bool IsMoney);
public sealed record ReportResult(IReadOnlyList<ReportColumn> Columns, IReadOnlyList<DataRow> Rows, DataRow? Totals, int TotalCount);
public sealed record ChartPoint(string Label, object? Key, decimal Value);

/// <summary>
/// Motor único de datos de Vistas (G), Indicadores (H) y Gráficos (I): carga la fuente (con rango de fecha),
/// combina fuentes secundarias muchos-a-uno, mezcla campos personalizados, aplica el DSL de filtros,
/// agrupa/agrega y ordena. Gráficos e Indicadores no calculan nada distinto: solo presentan distinto.
/// Evaluación en memoria por diseño en el Lote 1 (las fuentes pre-filtran por fecha); se baja a SQL cuando el volumen lo pida.
/// Lote 15 ("hoy" en hora de Puerto Rico): el rango llega en instantes UTC de medianoches LOCALES y el motor lo pasa también
/// en días locales (DataQuery.FromDay/ToDayExclusive) para las fuentes de días de calendario; agrupar por un campo de fecha
/// agrupa por el día LOCAL del instante (ITenantClock); un día de calendario (DateOnly) se agrupa tal cual.
/// </summary>
public sealed class AnalyticsEngine(IDataSourceRegistry registry, TeikemDbContext db, ITenantContext tenant, ITenantClock? clock = null)
{
    public const string CustomFieldPrefix = "cf.";

    /// <summary>Reloj de la compañía (el registrado en DI; sin él, el de Puerto Rico por defecto).</summary>
    private readonly ITenantClock _clock = clock ?? TenantClock.Default;

    /// <summary>Consulta a la fuente con el rango en instantes UTC y en días locales.</summary>
    private DataQuery RangeQuery(DateTime? fromUtc, DateTime? toUtc) => new()
    {
        FromUtc = fromUtc, ToUtc = toUtc,
        FromDay = fromUtc is DateTime f ? _clock.DayOf(f) : null,
        ToDayExclusive = toUtc is DateTime t ? _clock.DayOf(t) : null,
    };

    public async Task<ReportResult> RunReportAsync(ReportSpec spec, CancellationToken ct)
    {
        var source = registry.Get(spec.SourceKey);
        var rows = await source.LoadAsync(RangeQuery(spec.FromUtc, spec.ToUtc), ct);
        await MergeCustomFieldsAsync(source, rows, ct);
        var columnsMeta = new List<ReportColumn>();
        var lang = tenant.Lang;

        // Fuentes combinadas (muchos-a-uno)
        var relations = new Dictionary<string, DataRelation>(StringComparer.OrdinalIgnoreCase);
        foreach (var relKey in spec.Secondary)
        {
            var rel = source.Relations.FirstOrDefault(r => r.Key.Equals(relKey, StringComparison.OrdinalIgnoreCase))
                      ?? throw new ValidationException("secondary", $"La fuente {source.Key} no tiene la relación '{relKey}'.");
            relations[rel.Key] = rel;
            var target = registry.Get(rel.TargetSourceKey);
            var ids = rows.Select(r => RuleEvaluator.ToDecimal(r.GetValueOrDefault(rel.LocalField))).Where(v => v.HasValue).Select(v => (int)v!.Value).Distinct().ToList();
            var targetRows = await target.LoadAsync(new DataQuery { Ids = ids }, ct);
            var index = new Dictionary<string, DataRow>();
            foreach (var tr in targetRows) index[RuleEvaluator.ToText(tr.GetValueOrDefault(target.IdField))] = tr;
            foreach (var r in rows)
            {
                var fk = RuleEvaluator.ToText(r.GetValueOrDefault(rel.LocalField));
                index.TryGetValue(fk, out var joined);
                foreach (var f in target.Fields)
                    r[$"{rel.Key}.{f.Key}"] = joined?.GetValueOrDefault(f.Key);
            }
        }

        // Filtro (DSL) — mismo evaluador que la validación de campos personalizados
        var filter = RuleEvaluator.CompileFilter(spec.FilterJson);
        var filtered = rows.Where(r => filter(r)).ToList();

        // Metadatos de columnas
        IEnumerable<string> columnKeys = spec.Columns.Count > 0 ? spec.Columns : source.Fields.Select(f => f.Key);
        foreach (var key in columnKeys) columnsMeta.Add(ResolveColumn(source, key, relations, lang));

        DataRow? totals = null;
        List<DataRow> output;
        if (spec.GroupBy.Count > 0)
        {
            var groups = filtered.GroupBy(r => string.Join("\u001f", spec.GroupBy.Select(g => RuleEvaluator.ToText(r.GetValueOrDefault(g)))));
            output = new List<DataRow>();
            foreach (var g in groups)
            {
                var row = new DataRow();
                var first = g.First();
                foreach (var gk in spec.GroupBy) row[gk] = first.GetValueOrDefault(gk);
                foreach (var agg in spec.Aggregates) row[AggKey(agg)] = Aggregate(g, agg);
                row["$count"] = g.Count();
                output.Add(row);
            }
            columnsMeta = spec.GroupBy.Select(g => ResolveColumn(source, g, relations, lang))
                .Concat(spec.Aggregates.Select(a => new ReportColumn(AggKey(a), AggLabel(a, lang), DataFieldType.Number, IsMoneyField(source, a.Field, relations))))
                .ToList();
            if (spec.ShowTotals)
            {
                totals = new DataRow();
                foreach (var agg in spec.Aggregates) totals[AggKey(agg)] = Aggregate(filtered, agg);
                totals["$count"] = filtered.Count;
            }
        }
        else
        {
            output = filtered;
        }

        // Orden
        foreach (var s in Enumerable.Reverse(spec.Sort))
        {
            output = (s.Desc
                ? output.OrderByDescending(r => r.GetValueOrDefault(s.Field), ValueComparer.Instance)
                : output.OrderBy(r => r.GetValueOrDefault(s.Field), ValueComparer.Instance)).ToList();
        }

        var total = output.Count;
        var page = output.Skip(Math.Max(0, spec.Skip)).Take(Math.Clamp(spec.Take, 1, 5000)).ToList();
        return new ReportResult(columnsMeta, page, totals, total);
    }

    /// <summary>Módulo H: un solo valor agregado.</summary>
    public async Task<decimal?> EvaluateIndicatorAsync(string sourceKey, string fn, string? field, string? filterJson, DateTime? fromUtc, DateTime? toUtc, CancellationToken ct)
    {
        var source = registry.Get(sourceKey);
        var rows = await source.LoadAsync(RangeQuery(fromUtc, toUtc), ct);
        await MergeCustomFieldsAsync(source, rows, ct);
        var filter = RuleEvaluator.CompileFilter(filterJson);
        return Aggregate(rows.Where(r => filter(r)), new AggregateSpec(fn, field));
    }

    /// <summary>Etiqueta por defecto del grupo que junta el resto (la de cada usuario la pasa AnalyticsService según su idioma).</summary>
    public const string OthersLabelEs = "Otras";
    public const string OthersLabelEn = "Others";
    /// <summary>Clave del punto "Otras" (la web lo distingue de una categoría real).</summary>
    public const string OthersKey = "$others";

    /// <summary>
    /// Módulo I: agrupar por un campo y agregar; barra/dona = top N por valor, línea = cronológico (últimos 30 puntos).
    /// Lote 15 (D11): en barras, dona y pastel con SUM o COUNT, si hay más de <paramref name="topN"/> grupos se muestran los
    /// topN − 1 mayores y "Otras" con la suma exacta del resto (<see cref="FoldOthers"/>): la dona no miente sobre las
    /// proporciones y la suma de los puntos es el total. Con AVG/MIN/MAX sumar el resto no tiene sentido: siguen los topN mayores.
    /// </summary>
    public async Task<IReadOnlyList<ChartPoint>> EvaluateChartAsync(string sourceKey, string groupBy, string fn, string? field, string? filterJson, string chartType, DateTime? fromUtc, DateTime? toUtc, CancellationToken ct, int topN = 8,
        string? othersLabel = null)
    {
        var source = registry.Get(sourceKey);
        var rows = await source.LoadAsync(RangeQuery(fromUtc, toUtc), ct);
        await MergeCustomFieldsAsync(source, rows, ct);
        var filter = RuleEvaluator.CompileFilter(filterJson);
        var isDateGroup = source.Fields.FirstOrDefault(f => f.Key.Equals(groupBy, StringComparison.OrdinalIgnoreCase))?.Type == DataFieldType.Date;

        var points = rows.Where(r => filter(r))
            .GroupBy(r => GroupKey(r.GetValueOrDefault(groupBy), isDateGroup, _clock.Zone))
            .Select(g => new ChartPoint(g.Key.Label, g.Key.Key, Aggregate(g, new AggregateSpec(fn, field)) ?? 0m))
            .ToList();

        var type = chartType.ToUpperInvariant();
        if (type == ChartTypes.Line) return points.OrderBy(p => p.Key, ValueComparer.Instance).TakeLast(30).ToList();
        var fold = type is ChartTypes.Bar or ChartTypes.Donut or ChartTypes.Pie
                   && fn.ToUpperInvariant() is AggregateFns.Sum or AggregateFns.Count;
        return fold
            ? FoldOthers(points, topN, othersLabel ?? OthersLabelEs)
            : points.OrderByDescending(p => p.Value).Take(topN).ToList();
    }

    /// <summary>
    /// Lote 15 (D11): los puntos de mayor a menor; con más de <paramref name="topN"/>, los topN − 1 mayores y al final un punto
    /// "Otras" (clave <see cref="OthersKey"/>) con la suma del resto. Con topN o menos puntos, sin cambios (sin "Otras").
    /// </summary>
    public static IReadOnlyList<ChartPoint> FoldOthers(IEnumerable<ChartPoint> points, int topN, string othersLabel)
    {
        var ordered = points.OrderByDescending(p => p.Value).ToList();
        if (topN < 2 || ordered.Count <= topN) return ordered.Take(Math.Max(topN, 0)).ToList();
        var result = ordered.Take(topN - 1).ToList();
        result.Add(new ChartPoint(othersLabel, OthersKey, ordered.Skip(topN - 1).Sum(p => p.Value)));
        return result;
    }

    /// <summary>
    /// Clave de agrupación. Fecha: un instante (DateTime, de la base = UTC) se agrupa por su día LOCAL en la zona de la compañía
    /// (Lote 15); un día de calendario (DateOnly) tal cual. Etiqueta yyyy-MM-dd y clave = medianoche del día (para ordenar).
    /// </summary>
    public static (string Label, object? Key) GroupKey(object? v, bool isDate, TimeZoneInfo zone)
    {
        if (isDate)
        {
            DateOnly? day = v switch
            {
                DateOnly d => d,
                DateTime dt => LocalDay.DayOf(dt, zone),
                DateTimeOffset dto => LocalDay.DayOf(dto.UtcDateTime, zone),
                _ => RuleEvaluator.ToDate(v) is DateTime parsed ? DateOnly.FromDateTime(parsed) : null,
            };
            return day is DateOnly x ? (x.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), x.ToDateTime(TimeOnly.MinValue)) : ("—", null);
        }
        var text = RuleEvaluator.ToText(v);
        return (string.IsNullOrEmpty(text) ? "—" : text, v);
    }

    private static string AggKey(AggregateSpec a) => $"{a.Fn.ToLowerInvariant()}_{a.Field ?? "rows"}";

    private static string AggLabel(AggregateSpec a, string lang)
    {
        var fn = a.Fn.ToUpperInvariant() switch
        {
            AggregateFns.Sum => lang == "en" ? "Sum" : "Suma",
            AggregateFns.Avg => lang == "en" ? "Average" : "Promedio",
            AggregateFns.Min => lang == "en" ? "Min" : "Mínimo",
            AggregateFns.Max => lang == "en" ? "Max" : "Máximo",
            _ => lang == "en" ? "Count" : "Cantidad",
        };
        return a.Field is null ? fn : $"{fn} ({a.Field})";
    }

    private static bool IsMoneyField(IDataSource source, string? field, IReadOnlyDictionary<string, DataRelation> relations)
        => field is not null && source.Fields.Any(f => f.Key.Equals(field, StringComparison.OrdinalIgnoreCase) && f.IsMoney);

    public static decimal? Aggregate(IEnumerable<DataRow> rows, AggregateSpec agg)
    {
        var fn = agg.Fn.ToUpperInvariant();
        if (fn == AggregateFns.Count) return agg.Field is null ? rows.Count() : rows.Count(r => r.GetValueOrDefault(agg.Field) is not null);
        if (agg.Field is null) throw new ValidationException("field", $"La función {fn} requiere un campo.");
        var values = rows.Select(r => RuleEvaluator.ToDecimal(r.GetValueOrDefault(agg.Field))).Where(v => v.HasValue).Select(v => v!.Value).ToList();
        if (values.Count == 0) return fn == AggregateFns.Sum ? 0m : null;
        return fn switch
        {
            AggregateFns.Sum => values.Sum(),
            AggregateFns.Avg => Math.Round(values.Average(), 4),
            AggregateFns.Min => values.Min(),
            AggregateFns.Max => values.Max(),
            _ => throw new ValidationException("fn", $"Función de agregación desconocida: {fn}."),
        };
    }

    private ReportColumn ResolveColumn(IDataSource source, string key, IReadOnlyDictionary<string, DataRelation> relations, string lang)
    {
        if (key.StartsWith(CustomFieldPrefix, StringComparison.OrdinalIgnoreCase))
            return new ReportColumn(key, key[CustomFieldPrefix.Length..], DataFieldType.Text, false);
        var dot = key.IndexOf('.');
        if (dot > 0 && relations.TryGetValue(key[..dot], out var rel))
        {
            var target = registry.Get(rel.TargetSourceKey);
            var tf = target.Fields.FirstOrDefault(f => f.Key.Equals(key[(dot + 1)..], StringComparison.OrdinalIgnoreCase));
            var relLabel = lang == "en" ? rel.LabelEn : rel.LabelEs;
            return tf is null
                ? new ReportColumn(key, key, DataFieldType.Text, false)
                : new ReportColumn(key, $"{relLabel} · {(lang == "en" ? tf.LabelEn : tf.LabelEs)}", tf.Type, tf.IsMoney);
        }
        var f0 = source.Fields.FirstOrDefault(f => f.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
        return f0 is null
            ? new ReportColumn(key, key, DataFieldType.Text, false)
            : new ReportColumn(f0.Key, lang == "en" ? f0.LabelEn : f0.LabelEs, f0.Type, f0.IsMoney);
    }

    /// <summary>Módulo F ↔ G: agrega columnas cf.{FieldKey} con el valor tipado de cada registro.</summary>
    private async Task MergeCustomFieldsAsync(IDataSource source, List<DataRow> rows, CancellationToken ct)
    {
        if (source.EntityTypeCode is null || rows.Count == 0) return;
        var defs = await db.CustomFieldDefinitions.AsNoTracking()
            .Where(d => d.IsActive && d.EntityType!.InternalCode == source.EntityTypeCode)
            .ToListAsync(ct);
        if (defs.Count == 0) return;
        var ids = rows.Select(r => RuleEvaluator.ToDecimal(r.GetValueOrDefault(source.IdField))).Where(v => v.HasValue).Select(v => (int)v!.Value).ToHashSet();
        var defIds = defs.Select(d => d.CustomFieldDefinitionId).ToList();
        var values = await db.CustomFieldValues.AsNoTracking()
            .Where(v => defIds.Contains(v.CustomFieldDefinitionId) && ids.Contains(v.EntityId))
            .ToListAsync(ct);
        var byEntity = values.ToLookup(v => v.EntityId);
        var defById = defs.ToDictionary(d => d.CustomFieldDefinitionId);
        foreach (var r in rows)
        {
            var id = (int?)RuleEvaluator.ToDecimal(r.GetValueOrDefault(source.IdField));
            foreach (var d in defs) r[CustomFieldPrefix + d.FieldKey] = null;
            if (id is null) continue;
            foreach (var v in byEntity[id.Value])
            {
                var d = defById[v.CustomFieldDefinitionId];
                r[CustomFieldPrefix + d.FieldKey] = (object?)v.ValueNumber ?? v.ValueDate ?? (object?)v.ValueBool ?? v.ValueText;
            }
        }
    }

    // ---- Parseo de los JSON guardados en ReportDefinition ----
    public static IReadOnlyList<string> ParseStringList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<string>();
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.ValueKind == JsonValueKind.Array
            ? doc.RootElement.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!).ToList()
            : Array.Empty<string>();
    }

    public static (IReadOnlyList<string> By, IReadOnlyList<AggregateSpec> Aggregates, bool Totals) ParseGroup(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return (Array.Empty<string>(), Array.Empty<AggregateSpec>(), false);
        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement;
        var by = r.TryGetProperty("by", out var b) && b.ValueKind == JsonValueKind.Array ? b.EnumerateArray().Select(e => e.GetString()!).ToList() : new List<string>();
        var aggs = new List<AggregateSpec>();
        if (r.TryGetProperty("aggregates", out var a) && a.ValueKind == JsonValueKind.Array)
            foreach (var e in a.EnumerateArray())
                aggs.Add(new AggregateSpec(e.TryGetProperty("fn", out var fn) ? fn.GetString() ?? "COUNT" : "COUNT",
                    e.TryGetProperty("field", out var f) && f.ValueKind == JsonValueKind.String ? f.GetString() : null));
        var totals = r.TryGetProperty("totals", out var t) && t.ValueKind == JsonValueKind.True;
        return (by, aggs, totals);
    }

    public static IReadOnlyList<SortSpec> ParseSort(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<SortSpec>();
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return Array.Empty<SortSpec>();
        return doc.RootElement.EnumerateArray()
            .Select(e => new SortSpec(e.GetProperty("field").GetString()!, e.TryGetProperty("dir", out var d) && string.Equals(d.GetString(), "desc", StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }

    private sealed class ValueComparer : IComparer<object?>
    {
        public static readonly ValueComparer Instance = new();
        public int Compare(object? x, object? y)
        {
            if (x is null && y is null) return 0;
            if (x is null) return -1;
            if (y is null) return 1;
            return RuleEvaluator.Compare(x, y) ?? string.Compare(RuleEvaluator.ToText(x), RuleEvaluator.ToText(y), StringComparison.OrdinalIgnoreCase);
        }
    }
}
