using System.Globalization;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Primitives;
using Teikem.Domain.Common;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>Un filtro por un campo personalizado (Lote 130): Value (texto, lista, sí/no, igualdad) o From/To (número y fecha, ambos inclusivos).</summary>
public sealed record CustomFieldFilter(string Key, string? Value = null, string? From = null, string? To = null);

/// <summary>
/// Lote 130 (dictado de Luis, 2026-10-18) — los campos personalizados con «Mostrar en la búsqueda» (<c>ShowInList</c>) ofrecen un filtro en la lista de su entidad, del tipo del campo. En la consulta
/// viajan como <c>cf.{clave}</c> (texto, lista, sí/no) o <c>cf.{clave}.from</c> / <c>cf.{clave}.to</c> (número y fecha). Un filtro por un campo que no existe o que no está habilitado para la búsqueda es 400.
/// Entre filtros distintos la fila debe cumplir todos.
/// </summary>
public static class CustomFieldFilters
{
    public const string Prefix = "cf.";

    /// <summary>Los filtros que trae la dirección: cf.clave, cf.clave.from y cf.clave.to (la clave en minúsculas).</summary>
    public static IReadOnlyList<CustomFieldFilter> FromQuery(IEnumerable<KeyValuePair<string, StringValues>> query)
    {
        var map = new Dictionary<string, (string? Value, string? From, string? To)>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, values) in query)
        {
            if (!name.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)) continue;
            var text = values.ToString().Trim();
            if (text.Length == 0) continue;
            var rest = name[Prefix.Length..];
            var kind = 0;   // 0 valor, 1 desde, 2 hasta
            if (rest.EndsWith(".from", StringComparison.OrdinalIgnoreCase)) { rest = rest[..^5]; kind = 1; }
            else if (rest.EndsWith(".to", StringComparison.OrdinalIgnoreCase)) { rest = rest[..^3]; kind = 2; }
            var key = rest.Trim().ToLowerInvariant();
            if (key.Length == 0) continue;
            var cur = map.GetValueOrDefault(key);
            map[key] = kind switch { 1 => (cur.Value, text, cur.To), 2 => (cur.Value, cur.From, text), _ => (text, cur.From, cur.To) };
        }
        return map.Select(kv => new CustomFieldFilter(kv.Key, kv.Value.Value, kv.Value.From, kv.Value.To)).ToList();
    }

    public static string NotEnabledMessage(string label) => $"El campo personalizado '{label}' no está habilitado para la búsqueda.";
    public static string UnknownMessage(string key) => $"El campo personalizado '{key}' no existe.";
    public static string InvalidValueMessage(string label) => $"El valor del filtro '{label}' no es válido.";

    /// <summary>
    /// Aplica los filtros a una consulta de la entidad <paramref name="entityTypeCode"/> (el id de cada fila sale de <paramref name="id"/>): deja solo las filas cuyo valor del campo cumple el filtro.
    /// Sin filtros devuelve la misma consulta.
    /// </summary>
    public static async Task<IQueryable<T>> ApplyCustomFieldsAsync<T>(this IQueryable<T> query, TeikemDbContext db, string entityTypeCode, IReadOnlyList<CustomFieldFilter>? filters,
        Expression<Func<T, int>> id, CancellationToken ct)
    {
        if (filters is null || filters.Count == 0) return query;
        var defs = await db.CustomFieldDefinitions.AsNoTracking().Include(d => d.DataType)
            .Where(d => d.IsActive && d.EntityType!.InternalCode == entityTypeCode).ToListAsync(ct);
        foreach (var f in filters)
        {
            var def = defs.FirstOrDefault(d => string.Equals(d.FieldKey, f.Key, StringComparison.OrdinalIgnoreCase)) ?? throw new ValidationException(Prefix + f.Key, UnknownMessage(f.Key));
            var label = MultilingualText.Resolve(def.LabelJson, "es");
            if (!def.ShowInList) throw new ValidationException(Prefix + f.Key, NotEnabledMessage(label));
            var matching = MatchingIds(db, def.CustomFieldDefinitionId, def.DataType!.InternalCode, f, label);
            var contains = Expression.Call(typeof(Queryable), nameof(Queryable.Contains), new[] { typeof(int) }, matching.Expression, id.Body);
            query = query.Where(Expression.Lambda<Func<T, bool>>(contains, id.Parameters));
        }
        return query;
    }

    private static IQueryable<int> MatchingIds(TeikemDbContext db, int definitionId, string dataType, CustomFieldFilter f, string label)
    {
        var values = db.CustomFieldValues.AsNoTracking().Where(v => v.CustomFieldDefinitionId == definitionId);
        switch (dataType)
        {
            case CustomFieldDataTypes.Number:
            {
                var (from, to, exact) = (Dec(f.From, label), Dec(f.To, label), Dec(f.Value, label));
                if (from is null && to is null && exact is null) throw new ValidationException(Prefix + f.Key, InvalidValueMessage(label));
                if (exact is decimal e) values = values.Where(v => v.ValueNumber == e);
                if (from is decimal a) values = values.Where(v => v.ValueNumber >= a);
                if (to is decimal b) values = values.Where(v => v.ValueNumber <= b);
                break;
            }
            case CustomFieldDataTypes.Date:
            case CustomFieldDataTypes.DateTime:
            {
                var (from, to, exact) = (Day(f.From, label), Day(f.To, label), Day(f.Value, label));
                if (from is null && to is null && exact is null) throw new ValidationException(Prefix + f.Key, InvalidValueMessage(label));
                if (exact is DateTime x) { var next = x.AddDays(1); values = values.Where(v => v.ValueDate >= x && v.ValueDate < next); }
                if (from is DateTime a) values = values.Where(v => v.ValueDate >= a);
                if (to is DateTime b) { var next = b.AddDays(1); values = values.Where(v => v.ValueDate < next); }
                break;
            }
            case CustomFieldDataTypes.Bool:
            {
                var raw = (f.Value ?? string.Empty).Trim().ToLowerInvariant();
                if (raw is not ("true" or "false")) throw new ValidationException(Prefix + f.Key, InvalidValueMessage(label));
                var want = raw == "true";
                values = values.Where(v => v.ValueBool == want);
                break;
            }
            case CustomFieldDataTypes.MultiSelect:
            {
                var token = "," + (f.Value ?? string.Empty).Trim() + ",";
                if (token.Length <= 2) throw new ValidationException(Prefix + f.Key, InvalidValueMessage(label));
                values = values.Where(v => v.ValueText != null && ("," + v.ValueText + ",").Contains(token));
                break;
            }
            case CustomFieldDataTypes.Select:
            case CustomFieldDataTypes.LookupRef:
            {
                var want = (f.Value ?? string.Empty).Trim();
                if (want.Length == 0) throw new ValidationException(Prefix + f.Key, InvalidValueMessage(label));
                values = values.Where(v => v.ValueText == want);
                break;
            }
            default:
            {
                var text = (f.Value ?? string.Empty).Trim();
                if (text.Length == 0) throw new ValidationException(Prefix + f.Key, InvalidValueMessage(label));
                values = values.Where(v => v.ValueText != null && v.ValueText.Contains(text));
                break;
            }
        }
        return values.Select(v => v.EntityId);
    }

    private static decimal? Dec(string? raw, string label)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        return decimal.TryParse(raw.Trim().Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : throw new ValidationException("cf", InvalidValueMessage(label));
    }

    private static DateTime? Day(string? raw, string label)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        return DateTime.TryParse(raw.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var d) ? d.Date : throw new ValidationException("cf", InvalidValueMessage(label));
    }
}
