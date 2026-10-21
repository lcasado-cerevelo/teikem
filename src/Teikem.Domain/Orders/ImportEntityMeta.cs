using System.Globalization;

namespace Teikem.Domain.Orders;

/// <summary>Un campo que una plantilla puede mapear: obligatorio (marca visual en el editor) y si admite valor por defecto.</summary>
public sealed record ImportFieldSpec(string Field, bool Required, bool DefaultAllowed);

/// <summary>
/// Regla de grupo de una plantilla: al menos uno de <see cref="Fields"/> tiene que estar en una columna (o, con <see cref="AllowDefault"/>, en los valores por defecto). Si no, el error
/// de «columns» es <see cref="Message"/>.
/// </summary>
public sealed record ImportRequirement(IReadOnlyList<string> Fields, bool AllowDefault, string Message);

/// <summary>
/// 2026-10-21 — Metadatos puros de una entidad importable por plantilla (motor genérico, dictado de Luis 2026-10-19): sus campos, qué no admite valor por defecto, las reglas de grupo y el
/// mapeo de una fila (celda no vacía → valor; vacía → valor por defecto si hay). Cada entidad nueva (productos, proveedores…) declara su meta en <see cref="ImportEntities"/>; el servicio de
/// plantillas (validar y guardar la definición) y el importador genérico leen de aquí. La parte que toca la base de datos (revisar y crear filas) vive en Infrastructure.
/// </summary>
public sealed class ImportEntityMeta(string kind, IReadOnlyList<ImportFieldSpec> fields, IReadOnlyList<ImportRequirement> requirements)
{
    public string Kind { get; } = kind;
    public IReadOnlyList<ImportFieldSpec> Fields { get; } = fields;
    public IReadOnlyList<ImportRequirement> Requirements { get; } = requirements;

    public const string ColumnsRequiredMessage = "Indique al menos una columna.";
    public static string UnknownFieldMessage(string field) => $"Campo desconocido: {field}.";
    public static string DuplicatePositionMessage(int position) => $"La posición {position} está repetida.";
    public static string DuplicateFieldMessage(string field) => $"El campo {field} está repetido.";
    public static string DefaultNotAllowedMessage(string field) => $"El campo {field} no admite valor por defecto.";

    public string? Canonical(string? field)
        => string.IsNullOrWhiteSpace(field) ? null : Fields.FirstOrDefault(f => string.Equals(f.Field, field.Trim(), StringComparison.OrdinalIgnoreCase))?.Field;

    private bool DefaultAllowed(string canonical) => Fields.First(f => f.Field == canonical).DefaultAllowed;

    /// <summary>Errores por campo de la definición (vacío = válida). Las llaves siguen la forma columns[i].position / columns[i].field / defaults.campo / columns.</summary>
    public IReadOnlyDictionary<string, string> ValidateColumns(IReadOnlyList<ImportColumn>? columns, IReadOnlyDictionary<string, string>? defaults)
    {
        var errors = new Dictionary<string, string>(StringComparer.Ordinal);
        if (columns is null || columns.Count == 0) errors["columns"] = ColumnsRequiredMessage;
        else if (columns.Count > ImportMapping.MaxColumns) errors["columns"] = $"Máximo {ImportMapping.MaxColumns} columnas.";

        var positions = new HashSet<int>();
        var chosen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; columns is not null && i < columns.Count; i++)
        {
            var c = columns[i];
            if (c.Position < 1) errors[$"columns[{i}].position"] = "La posición debe ser 1 o mayor.";
            else if (c.Position > ImportMapping.MaxPosition) errors[$"columns[{i}].position"] = $"La posición no puede exceder {ImportMapping.MaxPosition}.";
            else if (!positions.Add(c.Position)) errors[$"columns[{i}].position"] = DuplicatePositionMessage(c.Position);

            var canonical = Canonical(c.Field);
            if (canonical is null) errors[$"columns[{i}].field"] = UnknownFieldMessage(c.Field?.Trim() ?? string.Empty);
            else if (!chosen.Add(canonical)) errors[$"columns[{i}].field"] = DuplicateFieldMessage(canonical);
        }

        var defaultFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, _) in defaults ?? new Dictionary<string, string>())
        {
            var canonical = Canonical(key);
            if (canonical is null) errors[$"defaults.{key}"] = UnknownFieldMessage(key);
            else if (!DefaultAllowed(canonical)) errors[$"defaults.{key}"] = DefaultNotAllowedMessage(canonical);
            else defaultFields.Add(canonical);
        }

        if (columns is { Count: > 0 } && !errors.ContainsKey("columns"))
            foreach (var r in Requirements)
                if (!r.Fields.Any(f => chosen.Contains(f) || (r.AllowDefault && defaultFields.Contains(f)))) { errors["columns"] = r.Message; break; }
        return errors;
    }

    /// <summary>Defaults con el nombre canónico del campo, sin los no admitidos ni los vacíos.</summary>
    public IReadOnlyDictionary<string, string> NormalizeDefaults(IReadOnlyDictionary<string, string>? defaults)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in defaults ?? new Dictionary<string, string>())
        {
            var canonical = Canonical(key);
            if (canonical is null || !DefaultAllowed(canonical) || string.IsNullOrWhiteSpace(value)) continue;
            result[canonical] = value.Trim();
        }
        return result;
    }

    /// <summary>Valores por campo de una fila: la celda de la columna, o el valor por defecto de la plantilla si la celda viene vacía.</summary>
    public Dictionary<string, string> MapRow(IReadOnlyList<string> cells, IReadOnlyList<ImportColumn> columns, IReadOnlyDictionary<string, string> defaults)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var c in columns)
        {
            var field = Canonical(c.Field);
            if (field is null) continue;
            var text = c.Position >= 1 && c.Position <= cells.Count ? cells[c.Position - 1].Trim() : string.Empty;
            if (text.Length > 0) values[field] = text;
        }
        foreach (var (field, value) in defaults)
            if (!values.ContainsKey(field)) values[field] = value;
        return values;
    }

    /// <summary>Número con punto o coma decimal (sin separador de miles); vacío → null; no numérico → false.</summary>
    public static bool TryDecimal(string? text, out decimal? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(text)) return true;
        var t = text.Trim().Replace("$", string.Empty).Replace(" ", string.Empty);
        if (t.Contains(',') && !t.Contains('.')) t = t.Replace(',', '.');
        if (!decimal.TryParse(t, NumberStyles.Number, CultureInfo.InvariantCulture, out var d)) return false;
        value = d;
        return true;
    }
}

/// <summary>Registro de las entidades importables por plantilla además de ORDER y CONSIGNEE (que tienen su propio importador). Agregar una entidad = declarar aquí su meta.</summary>
public static class ImportEntities
{
    private static readonly Dictionary<string, ImportEntityMeta> All = new(StringComparer.OrdinalIgnoreCase);

    static ImportEntities()
    {
        Add(Wms.ProductImportFields.Meta);
        Add(Wms.SupplierImportFields.Meta);
        Add(Wms.WarehouseImportFields.Meta);
        Add(Wms.BinImportFields.Meta);
    }

    private static void Add(ImportEntityMeta meta) => All[meta.Kind] = meta;

    public static ImportEntityMeta? TryGet(string? kind) => kind is not null && All.TryGetValue(kind.Trim(), out var meta) ? meta : null;
    public static IReadOnlyCollection<string> Kinds => All.Keys;
}
