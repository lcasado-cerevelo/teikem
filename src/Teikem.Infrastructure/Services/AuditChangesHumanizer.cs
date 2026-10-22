using System.Text.Json;
using System.Text.Json.Nodes;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// 2026-10-22 (Luis) — la bitácora no debe mostrar cómo se guardan las llaves por dentro (StatusCodeId: 5 → 6): un campo que apunta a otro registro se lee con su NOMBRE («Estatus: Esperado → Recibido»,
/// «Asignado a: Ana Ruiz»). Cada campo-llave se reconoce por su nombre (`KindOf`), se rinde con una etiqueta legible (`FieldLabel`) y su valor se cambia por el nombre que da el llamador (`resolve`);
/// lo que no se puede nombrar se muestra como «(sin nombre)», nunca como el número. La llave interna `$key` no se muestra y `$reason` sale como «Motivo». Lógica pura (probada con xunit).
/// </summary>
public static class AuditChangesHumanizer
{
    public enum FkKind { None, Lookup, Status, User, Warehouse, Client, Product, Bin, Supplier, Zone, Other }

    private static readonly HashSet<string> OwnKeys = new(StringComparer.OrdinalIgnoreCase) { "$key" };

    /// <summary>¿Qué tipo de registro nombra este campo? None = no es una llave (se deja igual).</summary>
    public static FkKind KindOf(string field)
    {
        if (field.StartsWith('$')) return FkKind.None;
        if (field.EndsWith("LookupId", StringComparison.Ordinal) || field.EndsWith("LookupCodeId", StringComparison.Ordinal)) return FkKind.Lookup;
        if (field.EndsWith("StatusCodeId", StringComparison.Ordinal)) return FkKind.Status;
        if (field.EndsWith("UserId", StringComparison.Ordinal) || (field.Length > 2 && field.EndsWith("By", StringComparison.Ordinal))) return FkKind.User;
        if (field.EndsWith("WarehouseId", StringComparison.Ordinal)) return FkKind.Warehouse;
        if (field.EndsWith("ClientId", StringComparison.Ordinal)) return FkKind.Client;
        if (field.EndsWith("ProductId", StringComparison.Ordinal)) return FkKind.Product;
        if (field.EndsWith("BinId", StringComparison.Ordinal)) return FkKind.Bin;
        if (field.EndsWith("SupplierId", StringComparison.Ordinal)) return FkKind.Supplier;
        if (field.EndsWith("ZoneId", StringComparison.Ordinal)) return FkKind.Zone;
        if (field.Length > 2 && field.EndsWith("Id", StringComparison.Ordinal) && !field.Equals("PublicId", StringComparison.Ordinal)) return FkKind.Other;
        return FkKind.None;
    }

    private static readonly Dictionary<string, (string Es, string En)> Known = new(StringComparer.Ordinal)
    {
        ["StatusCodeId"] = ("Estatus", "Status"),
        ["AssignedToUserId"] = ("Asignado a", "Assigned to"),
        ["WarehouseId"] = ("Almacén", "Warehouse"),
        ["ClientId"] = ("Cliente", "Client"),
        ["ProductId"] = ("Producto", "Product"),
        ["SupplierId"] = ("Proveedor", "Supplier"),
        ["FromBinId"] = ("Posición de origen", "From bin"),
        ["ToBinId"] = ("Posición de destino", "To bin"),
        ["StagingBinId"] = ("Posición de recepción", "Receiving bin"),
        ["DefaultStagingBinId"] = ("Posición de recepción por omisión", "Default receiving bin"),
        ["CreatedBy"] = ("Creado por", "Created by"),
        ["UpdatedBy"] = ("Editado por", "Updated by"),
        ["WarehouseZoneId"] = ("Zona", "Zone"),
        ["IsActive"] = ("Activo", "Active"),
        ["IsRemoved"] = ("Eliminado", "Removed"),
        ["RemovedBy"] = ("Eliminado por", "Removed by"),
        ["IsProvisional"] = ("Provisional", "Provisional"),
        ["ProvisionalCreatedBy"] = ("Creada (provisional) por", "Provisionally created by"),
        ["Code"] = ("Código", "Code"),
        ["Name"] = ("Nombre", "Name"),
        ["Aisle"] = ("Pasillo", "Aisle"),
        ["Rack"] = ("Rack", "Rack"),
        ["Level"] = ("Nivel", "Level"),
        ["Position"] = ("Posición", "Position"),
        ["MaxWeightKg"] = ("Peso máximo (kg)", "Max weight (kg)"),
        ["MaxCapacityQty"] = ("Cupo máximo (unidades)", "Max capacity (units)"),
        ["$reason"] = ("Motivo", "Reason"),
    };

    /// <summary>Etiqueta legible de un campo: la conocida, o el nombre sin el sufijo Id/LookupId y con palabras separadas.</summary>
    public static string FieldLabel(string field, string lang)
    {
        if (Known.TryGetValue(field, out var k)) return lang == "en" ? k.En : k.Es;
        var s = field;
        foreach (var suffix in new[] { "LookupCodeId", "LookupId", "StatusCodeId", "Id" })
            if (s.Length > suffix.Length && s.EndsWith(suffix, StringComparison.Ordinal)) { s = s[..^suffix.Length]; break; }
        var words = new System.Text.StringBuilder();
        for (var i = 0; i < s.Length; i++)
        {
            if (i > 0 && char.IsUpper(s[i]) && !char.IsUpper(s[i - 1])) words.Append(' ');
            words.Append(s[i]);
        }
        return words.ToString();
    }

    /// <summary>Los (tipo, id) de campos-llave que trae un detalle, para resolverlos todos juntos.</summary>
    public static IEnumerable<(FkKind Kind, int Id)> Keys(string? json)
    {
        if (JsonNode.Parse(SafeJson(json)) is not JsonObject obj) yield break;
        foreach (var (name, value) in obj)
        {
            var kind = KindOf(name);
            if (kind == FkKind.None || kind == FkKind.Other) continue;
            foreach (var n in Values(value)) if (n is JsonValue jv && jv.TryGetValue<int>(out var id)) yield return (kind, id);
        }
    }

    private static IEnumerable<JsonNode?> Values(JsonNode? v)
    {
        if (v is JsonObject o && (o.ContainsKey("from") || o.ContainsKey("to"))) { yield return o["from"]; yield return o["to"]; }
        else yield return v;
    }

    private static string SafeJson(string? json) => string.IsNullOrWhiteSpace(json) ? "null" : json;

    /// <summary>Devuelve el detalle con los campos-llave en su forma legible. Un detalle que no es un objeto JSON se devuelve igual.</summary>
    public static string? Humanize(string? json, string lang, Func<FkKind, int, string?> resolve)
    {
        if (string.IsNullOrWhiteSpace(json)) return json;
        JsonNode? parsed;
        try { parsed = JsonNode.Parse(json); } catch (JsonException) { return json; }
        if (parsed is not JsonObject obj) return json;

        var result = new JsonObject();
        foreach (var (name, value) in obj)
        {
            if (OwnKeys.Contains(name)) continue;
            var kind = KindOf(name);
            if (kind == FkKind.None && name != "$reason" && !Known.ContainsKey(name)) { result[name] = value?.DeepClone(); continue; }
            var label = FieldLabel(name, lang);
            if (name == "$reason") { result[label] = value?.DeepClone(); continue; }
            JsonNode? Map(JsonNode? n)
            {
                if (n is null) return null;
                if (n is JsonValue jv && jv.TryGetValue<int>(out var id)) return JsonValue.Create(kind == FkKind.Other ? NoName(lang) : resolve(kind, id) ?? NoName(lang));
                return n.DeepClone();
            }
            if (value is JsonObject o && (o.ContainsKey("from") || o.ContainsKey("to")))
            {
                var ft = new JsonObject();
                if (o.ContainsKey("from")) ft["from"] = Map(o["from"]);
                if (o.ContainsKey("to")) ft["to"] = Map(o["to"]);
                result[label] = ft;
            }
            else result[label] = Map(value);
        }
        return result.ToJsonString();
    }

    private static string NoName(string lang) => lang == "en" ? "(no name)" : "(sin nombre)";
}
