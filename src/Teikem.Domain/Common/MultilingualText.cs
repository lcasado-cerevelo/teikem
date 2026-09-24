using System.Text.Json;

namespace Teikem.Domain.Common;

/// <summary>
/// Utilidades para el JSON multilingüe {"es":"...","en":"..."} que usan LabelJson/DescriptionJson en todo el esquema.
/// El FE nunca hardcodea etiquetas de catálogo; se resuelven en runtime según el idioma del usuario.
/// </summary>
public static class MultilingualText
{
    public const string DefaultLang = "es";
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    public static Dictionary<string, string> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json, Options)
                       ?? new Dictionary<string, string>();
            return new Dictionary<string, string>(dict, StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            // Tolerante: si alguien guardó texto plano, se usa como etiqueta única.
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [DefaultLang] = json };
        }
    }

    /// <summary>Resuelve la etiqueta para un idioma con fallback: idioma pedido → 'es' → 'en' → cualquiera → "".</summary>
    public static string Resolve(string? json, string? lang)
    {
        var dict = Parse(json);
        if (dict.Count == 0) return string.Empty;
        if (!string.IsNullOrWhiteSpace(lang))
        {
            var l = lang.Length > 2 ? lang[..2] : lang;
            if (dict.TryGetValue(l, out var v) && !string.IsNullOrEmpty(v)) return v;
        }
        if (dict.TryGetValue(DefaultLang, out var es) && !string.IsNullOrEmpty(es)) return es;
        if (dict.TryGetValue("en", out var en) && !string.IsNullOrEmpty(en)) return en;
        return dict.Values.FirstOrDefault(v => !string.IsNullOrEmpty(v)) ?? string.Empty;
    }

    public static string Build(string es, string en) =>
        JsonSerializer.Serialize(new Dictionary<string, string> { ["es"] = es, ["en"] = en });

    public static string Serialize(IDictionary<string, string> labels) => JsonSerializer.Serialize(labels);

    /// <summary>Combina el JSON base con el override del tenant (las llaves del override pisan las de la base).</summary>
    public static string Merge(string? baseJson, string? overrideJson)
    {
        if (string.IsNullOrWhiteSpace(overrideJson)) return baseJson ?? "{}";
        var b = Parse(baseJson);
        foreach (var kv in Parse(overrideJson)) b[kv.Key] = kv.Value;
        return Serialize(b);
    }
}
