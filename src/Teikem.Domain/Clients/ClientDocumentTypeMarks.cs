using System.Text.Json;

namespace Teikem.Domain.Clients;

/// <summary>
/// Lote 248 — marcas de un tipo de documento del cliente (catálogo LookupCode «ClientDocumentType»), con el mismo patrón que <c>ServiceTypeMarks</c>:
/// - «lleva vigencia» (<see cref="RequiresValidTo"/>): el documento exige la fecha «hasta» y sale en «Documentos por vencer»;
/// - «es de impuestos» (<see cref="IsTax"/>): el documento puede respaldar una exención de IVU.
/// Viven en el ExtraJson del tipo: <c>{"requiresValidTo":true,"isTax":true}</c>; el ajuste de la compañía (LookupCodeOverride.CustomExtraJson) pisa lo que diga el
/// valor global. Sin marcas: ni lleva vigencia ni es de impuestos. Un JSON roto se ignora (se quedan las marcas que había).
/// </summary>
public sealed record ClientDocumentTypeMarks(bool RequiresValidTo, bool IsTax)
{
    public const string RequiresValidToKey = "requiresValidTo";
    public const string IsTaxKey = "isTax";

    public static readonly ClientDocumentTypeMarks Default = new(false, false);

    public static ClientDocumentTypeMarks Parse(string? json) => Default.Apply(json);

    /// <summary>El valor global (ExtraJson) y, encima, el ajuste de la compañía (CustomExtraJson).</summary>
    public static ClientDocumentTypeMarks Resolve(string? extraJson, string? customExtraJson) => Parse(extraJson).Apply(customExtraJson);

    /// <summary>Aplica encima las marcas que traiga ese JSON (las que falten se quedan como estaban); un JSON roto o que no es objeto se ignora.</summary>
    public ClientDocumentTypeMarks Apply(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return this;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return this;
            var requiresValidTo = RequiresValidTo;
            var isTax = IsTax;
            foreach (var p in doc.RootElement.EnumerateObject())
            {
                if (p.Value.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) continue;
                if (p.Name.Equals(RequiresValidToKey, StringComparison.OrdinalIgnoreCase)) requiresValidTo = p.Value.GetBoolean();
                else if (p.Name.Equals(IsTaxKey, StringComparison.OrdinalIgnoreCase)) isTax = p.Value.GetBoolean();
            }
            return new ClientDocumentTypeMarks(requiresValidTo, isTax);
        }
        catch (JsonException) { return this; }
    }

    /// <summary>
    /// Escribe estas marcas dentro de un JSON existente (p. ej. el CustomExtraJson de la compañía) sin perder las demás llaves; un JSON roto o vacío se reemplaza
    /// por uno nuevo con solo las dos marcas.
    /// </summary>
    public string WriteTo(string? json)
    {
        var map = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(json))
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                    foreach (var p in doc.RootElement.EnumerateObject())
                        if (!p.Name.Equals(RequiresValidToKey, StringComparison.OrdinalIgnoreCase) && !p.Name.Equals(IsTaxKey, StringComparison.OrdinalIgnoreCase))
                            map[p.Name] = p.Value.Clone();
            }
            catch (JsonException) { map.Clear(); }
        }
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartObject();
            foreach (var (k, v) in map) { w.WritePropertyName(k); v.WriteTo(w); }
            w.WriteBoolean(RequiresValidToKey, RequiresValidTo);
            w.WriteBoolean(IsTaxKey, IsTax);
            w.WriteEndObject();
        }
        return System.Text.Encoding.UTF8.GetString(ms.ToArray());
    }
}
