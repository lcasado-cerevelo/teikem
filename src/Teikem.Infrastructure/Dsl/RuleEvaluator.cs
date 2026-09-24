using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Teikem.Infrastructure.Dsl;

/// <summary>
/// Evaluador único del DSL de reglas (filtros de informes/indicadores/gráficos y validación de campos personalizados).
/// Un solo motor para validar y para filtrar. Compara con tipo: números como números, fechas como fechas
/// (corrige el bug del mock que comparaba ISO con parseFloat), texto sin distinguir mayúsculas.
///
/// Filtro (JSON):
///   { "and": [ {...}, {...} ] } | { "or": [...] } | { "not": {...} }
///   { "field": "Status", "op": "eq", "value": "DELIVERED" }
///   [ {...}, {...} ]  → equivale a "and" (forma simplificada del mock)
/// Operadores: eq, ne, gt, gte, lt, lte, contains, startsWith, endsWith, in, between, isNull, notNull, isTrue, isFalse
///
/// Validación (JSON): { "regex": "...", "min": 0, "max": 100, "minLength": 1, "maxLength": 50 }
/// </summary>
public static class RuleEvaluator
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    public static Func<IReadOnlyDictionary<string, object?>, bool> CompileFilter(string? filterJson)
    {
        if (string.IsNullOrWhiteSpace(filterJson)) return _ => true;
        using var doc = JsonDocument.Parse(filterJson);
        var node = FilterNode.Parse(doc.RootElement);
        return node is null ? _ => true : node.Evaluate;
    }

    public static bool Matches(IReadOnlyDictionary<string, object?> row, string? filterJson) => CompileFilter(filterJson)(row);

    /// <summary>Devuelve los campos referenciados por el filtro (para validar contra la fuente de datos).</summary>
    public static IReadOnlyCollection<string> ReferencedFields(string? filterJson)
    {
        if (string.IsNullOrWhiteSpace(filterJson)) return Array.Empty<string>();
        using var doc = JsonDocument.Parse(filterJson);
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        FilterNode.Parse(doc.RootElement)?.CollectFields(set);
        return set;
    }

    // ------------------------------------------------------------------ Validación

    public sealed record ValidationSpec(string? Regex, decimal? Min, decimal? Max, int? MinLength, int? MaxLength);

    public static ValidationSpec? ParseValidation(string? validationJson)
    {
        if (string.IsNullOrWhiteSpace(validationJson)) return null;
        using var doc = JsonDocument.Parse(validationJson);
        var r = doc.RootElement;
        if (r.ValueKind != JsonValueKind.Object) return null;
        return new ValidationSpec(
            GetString(r, "regex") ?? GetString(r, "pattern"),
            GetDecimal(r, "min"), GetDecimal(r, "max"),
            (int?)GetDecimal(r, "minLength"), (int?)GetDecimal(r, "maxLength"));
    }

    /// <summary>Valida un valor contra la spec; devuelve la lista de mensajes (vacía = válido).</summary>
    public static IReadOnlyList<string> Validate(object? value, string? validationJson)
    {
        var spec = ParseValidation(validationJson);
        var errors = new List<string>();
        if (spec is null || value is null) return errors;

        if (value is string s)
        {
            if (spec.MinLength is int minL && s.Length < minL) errors.Add($"Longitud mínima {minL}.");
            if (spec.MaxLength is int maxL && s.Length > maxL) errors.Add($"Longitud máxima {maxL}.");
            if (!string.IsNullOrEmpty(spec.Regex))
            {
                try { if (!Regex.IsMatch(s, spec.Regex, RegexOptions.None, TimeSpan.FromMilliseconds(200))) errors.Add("No cumple el formato requerido."); }
                catch (ArgumentException) { errors.Add("Expresión regular inválida en la definición del campo."); }
                catch (RegexMatchTimeoutException) { errors.Add("Validación de formato demasiado costosa."); }
            }
        }
        var num = ToDecimal(value);
        if (num is decimal d)
        {
            if (spec.Min is decimal min && d < min) errors.Add($"Valor mínimo {min}.");
            if (spec.Max is decimal max && d > max) errors.Add($"Valor máximo {max}.");
        }
        if (value is DateTime dt)
        {
            // min/max para fechas se expresan como días relativos a hoy (negativo = pasado)
            if (spec.Min is decimal dmin && dt < DateTime.UtcNow.Date.AddDays((double)dmin)) errors.Add("Fecha anterior a la mínima permitida.");
            if (spec.Max is decimal dmax && dt > DateTime.UtcNow.Date.AddDays((double)dmax)) errors.Add("Fecha posterior a la máxima permitida.");
        }
        return errors;
    }

    // ------------------------------------------------------------------ Comparación tipada

    public static int? Compare(object? left, object? right)
    {
        if (left is null || right is null) return null;
        var ln = ToDecimal(left); var rn = ToDecimal(right);
        if (ln.HasValue && rn.HasValue) return ln.Value.CompareTo(rn.Value);
        var ld = ToDate(left); var rd = ToDate(right);
        if (ld.HasValue && rd.HasValue) return ld.Value.CompareTo(rd.Value);
        if (left is bool lb && ToBool(right) is bool rb) return lb.CompareTo(rb);
        return string.Compare(ToText(left), ToText(right), StringComparison.OrdinalIgnoreCase);
    }

    public static bool AreEqual(object? left, object? right)
    {
        if (left is null && right is null) return true;
        if (left is null || right is null) return false;
        var ln = ToDecimal(left); var rn = ToDecimal(right);
        if (ln.HasValue && rn.HasValue) return ln.Value == rn.Value;
        var ld = ToDate(left); var rd = ToDate(right);
        if (ld.HasValue && rd.HasValue) return ld.Value == rd.Value;
        if (ToBool(left) is bool lb && ToBool(right) is bool rb) return lb == rb;
        return string.Equals(ToText(left), ToText(right), StringComparison.OrdinalIgnoreCase);
    }

    public static decimal? ToDecimal(object? v) => v switch
    {
        null => null,
        decimal d => d,
        int i => i, long l => l, short s => s, byte b => b,
        double db => (decimal)db, float f => (decimal)f,
        bool => null,
        DateTime or DateOnly or DateTimeOffset => null,
        JsonElement je when je.ValueKind == JsonValueKind.Number => je.GetDecimal(),
        JsonElement je when je.ValueKind == JsonValueKind.String => decimal.TryParse(je.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var x) && !LooksLikeDate(je.GetString()) ? x : null,
        string s => decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var x) && !LooksLikeDate(s) ? x : null,
        _ => null,
    };

    public static DateTime? ToDate(object? v) => v switch
    {
        null => null,
        DateTime dt => dt,
        DateOnly d => d.ToDateTime(TimeOnly.MinValue),
        DateTimeOffset dto => dto.UtcDateTime,
        JsonElement je when je.ValueKind == JsonValueKind.String => ParseDate(je.GetString()),
        string s => ParseDate(s),
        _ => null,
    };

    public static bool? ToBool(object? v) => v switch
    {
        null => null,
        bool b => b,
        JsonElement je when je.ValueKind is JsonValueKind.True => true,
        JsonElement je when je.ValueKind is JsonValueKind.False => false,
        JsonElement je when je.ValueKind is JsonValueKind.String => ToBool(je.GetString()),
        string s when bool.TryParse(s, out var b) => b,
        string s when s is "1" or "0" => s == "1",
        int i => i != 0,
        _ => null,
    };

    public static string ToText(object? v) => v switch
    {
        null => string.Empty,
        JsonElement je => je.ValueKind == JsonValueKind.String ? je.GetString() ?? "" : je.ToString(),
        DateTime dt => dt.ToString("O"),
        DateOnly d => d.ToString("yyyy-MM-dd"),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => v.ToString() ?? string.Empty,
    };

    private static bool LooksLikeDate(string? s) => s is { Length: >= 8 } && s.Count(c => c == '-') >= 2;

    private static DateTime? ParseDate(string? s)
    {
        if (string.IsNullOrWhiteSpace(s) || !LooksLikeDate(s)) return null;
        return DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var dt) ? dt : null;
    }

    private static string? GetString(JsonElement e, string name)
        => e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    private static decimal? GetDecimal(JsonElement e, string name)
        => e.TryGetProperty(name, out var p) ? ToDecimal(p) : null;

    // ------------------------------------------------------------------ Árbol de filtro

    private abstract class FilterNode
    {
        public abstract bool Evaluate(IReadOnlyDictionary<string, object?> row);
        public abstract void CollectFields(ISet<string> fields);

        public static FilterNode? Parse(JsonElement e)
        {
            switch (e.ValueKind)
            {
                case JsonValueKind.Array:
                    return new Logical("and", e.EnumerateArray().Select(Parse).Where(n => n is not null).ToList()!);
                case JsonValueKind.Object:
                    foreach (var op in new[] { "and", "or" })
                        if (e.TryGetProperty(op, out var arr) && arr.ValueKind == JsonValueKind.Array)
                            return new Logical(op, arr.EnumerateArray().Select(Parse).Where(n => n is not null).ToList()!);
                    if (e.TryGetProperty("not", out var inner)) return new Not(Parse(inner));
                    if (e.TryGetProperty("field", out var f) && f.ValueKind == JsonValueKind.String)
                    {
                        var opName = e.TryGetProperty("op", out var o) && o.ValueKind == JsonValueKind.String ? o.GetString()! : "eq";
                        object? value = e.TryGetProperty("value", out var v) ? v.Clone() : null;
                        return new Condition(f.GetString()!, opName, value);
                    }
                    return null;
                default:
                    return null;
            }
        }
    }

    private sealed class Logical(string op, List<FilterNode> children) : FilterNode
    {
        public override bool Evaluate(IReadOnlyDictionary<string, object?> row)
            => op.Equals("or", StringComparison.OrdinalIgnoreCase) ? children.Any(c => c.Evaluate(row)) : children.All(c => c.Evaluate(row));
        public override void CollectFields(ISet<string> fields) { foreach (var c in children) c.CollectFields(fields); }
    }

    private sealed class Not(FilterNode? inner) : FilterNode
    {
        public override bool Evaluate(IReadOnlyDictionary<string, object?> row) => inner is null || !inner.Evaluate(row);
        public override void CollectFields(ISet<string> fields) => inner?.CollectFields(fields);
    }

    private sealed class Condition(string field, string op, object? value) : FilterNode
    {
        public override void CollectFields(ISet<string> fields) => fields.Add(field);

        public override bool Evaluate(IReadOnlyDictionary<string, object?> row)
        {
            row.TryGetValue(field, out var actual);
            switch (op.ToLowerInvariant())
            {
                case "eq": return AreEqual(actual, value);
                case "ne": case "neq": return !AreEqual(actual, value);
                case "gt": return Compare(actual, value) > 0;
                case "gte": case "ge": return Compare(actual, value) >= 0;
                case "lt": return Compare(actual, value) < 0;
                case "lte": case "le": return Compare(actual, value) <= 0;
                case "contains": return actual is not null && ToText(actual).Contains(ToText(value), StringComparison.OrdinalIgnoreCase);
                case "startswith": return actual is not null && ToText(actual).StartsWith(ToText(value), StringComparison.OrdinalIgnoreCase);
                case "endswith": return actual is not null && ToText(actual).EndsWith(ToText(value), StringComparison.OrdinalIgnoreCase);
                case "isnull": case "empty": return actual is null || (actual is string s && s.Length == 0);
                case "notnull": case "notempty": return actual is not null && !(actual is string s2 && s2.Length == 0);
                case "istrue": return ToBool(actual) == true;
                case "isfalse": return ToBool(actual) == false;
                case "in": return Items(value).Any(item => AreEqual(actual, item));
                case "notin": return !Items(value).Any(item => AreEqual(actual, item));
                case "between":
                {
                    var items = Items(value).ToList();
                    if (items.Count != 2) return false;
                    return Compare(actual, items[0]) >= 0 && Compare(actual, items[1]) <= 0;
                }
                default: throw new InvalidOperationException($"Operador de filtro desconocido: '{op}'.");
            }
        }

        private static IEnumerable<object?> Items(object? v) => v switch
        {
            JsonElement je when je.ValueKind == JsonValueKind.Array => je.EnumerateArray().Select(x => (object?)x.Clone()),
            IEnumerable<object?> en => en,
            null => Array.Empty<object?>(),
            _ => new[] { v },
        };
    }
}
