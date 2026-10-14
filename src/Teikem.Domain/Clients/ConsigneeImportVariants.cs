using Teikem.Domain.Catalogs;
using Teikem.Domain.Orders;

namespace Teikem.Domain.Clients;

/// <summary>Lote 46 (F-B3): operadores de la condición de una variante de mapeo.</summary>
public static class ImportConditionOperators
{
    /// <summary>La celda está vacía (o la fila no llega a esa columna).</summary>
    public const string IsEmpty = "EMPTY";
    /// <summary>La celda trae algo.</summary>
    public const string NotEmpty = "NOT_EMPTY";
    /// <summary>La celda es igual al valor (sin acentos, mayúsculas ni espacios de más).</summary>
    public const string EqualTo = "EQUALS";
    /// <summary>La celda contiene el valor (mismo criterio).</summary>
    public const string Contains = "CONTAINS";

    public static readonly IReadOnlyList<string> All = [IsEmpty, NotEmpty, EqualTo, Contains];

    public static string? Canonical(string? op)
    {
        if (string.IsNullOrWhiteSpace(op)) return null;
        var o = op.Trim().ToUpperInvariant();
        return All.FirstOrDefault(a => a == o);
    }

    public static bool NeedsValue(string op) => op is EqualTo or Contains;
}

/// <summary>Condición sobre una columna del archivo (posición 1..n).</summary>
public sealed record ImportVariantCondition(int Position, string Operator, string? Value = null);

/// <summary>
/// Variante de mapeo de una plantilla de consignatarios: nombre (opcional), condiciones (TODAS deben cumplirse; sin condiciones =
/// aplica siempre) y su propio mapeo de columnas. Los valores por defecto son los de la plantilla (comunes a todas las variantes).
/// </summary>
public sealed record ImportVariant(string? Name, IReadOnlyList<ImportVariantCondition> Conditions, IReadOnlyList<ImportColumn> Columns);

/// <summary>
/// Lote 46 (F-B3): variantes de mapeo con condición por fila. Un archivo que mezcla disposiciones de columnas (p. ej. filas con
/// ZIP en la columna 5 y filas con el pueblo en la 8 y «PUERTO RICO» en la 9) se importa con una sola plantilla: a cada fila se le
/// aplica la PRIMERA variante cuyas condiciones se cumplan; si ninguna aplica, la fila se rechaza. Una plantilla sin variantes es
/// una sola variante sin condición (lo de siempre).
/// </summary>
public static class ConsigneeImportVariants
{
    public const int MaxVariants = 10, MaxConditions = 5, NameMax = 60, ValueMax = 100;

    public const string RowField = "variant";
    public const string NoVariantMatchesMessage = "Ninguna variante de la plantilla aplica a esta fila; revise las condiciones de las variantes.";
    public const string TooManyVariantsMessage = "Una plantilla admite a lo sumo 10 variantes.";
    public const string TooManyConditionsMessage = "Una variante admite a lo sumo 5 condiciones.";
    public const string ValueRequiredMessage = "La condición «igual a» o «contiene» necesita un valor.";
    public const string ColumnsInsideVariantsMessage = "Esta plantilla usa variantes: las columnas van dentro de cada variante (variants), no en columns.";
    public const string OnlyConsigneeMessage = "Las variantes de mapeo solo existen en las plantillas de consignatarios.";
    public static string UnknownOperatorMessage(string op) => $"Condición desconocida: {op}. Use EMPTY, NOT_EMPTY, EQUALS o CONTAINS.";
    public static string UnreachableMessage(int number) => $"La variante {number} no tiene condición y deja sin uso a las que le siguen; póngala al final o agréguele una condición.";
    public static string NameMaxMessage => $"Máximo {NameMax} caracteres.";
    public static string ValueMaxMessage => $"Máximo {ValueMax} caracteres.";

    /// <summary>Nombre para mostrar: el de la variante o «Variante N».</summary>
    public static string DisplayName(ImportVariant variant, int index) => string.IsNullOrWhiteSpace(variant.Name) ? $"Variante {index + 1}" : variant.Name.Trim();

    /// <summary>
    /// Errores de la lista de variantes (vacío = válida), con llaves «variants[i]…». Cada variante valida su mapeo con
    /// ConsigneeImportMapping.ValidateColumns (los defaults son los de la plantilla; sus errores se informan una vez, sin prefijo).
    /// </summary>
    public static IReadOnlyDictionary<string, string> Validate(IReadOnlyList<ImportVariant> variants, IReadOnlyDictionary<string, string>? defaults)
    {
        var errors = new Dictionary<string, string>(StringComparer.Ordinal);
        if (variants.Count > MaxVariants) { errors["variants"] = TooManyVariantsMessage; return errors; }
        for (var i = 0; i < variants.Count; i++)
        {
            var v = variants[i];
            var prefix = $"variants[{i}]";
            if (v.Name is { } name && name.Trim().Length > NameMax) errors[$"{prefix}.name"] = NameMaxMessage;
            var conditions = v.Conditions ?? Array.Empty<ImportVariantCondition>();
            if (conditions.Count > MaxConditions) errors[$"{prefix}.conditions"] = TooManyConditionsMessage;
            for (var j = 0; j < conditions.Count && j < MaxConditions; j++)
            {
                var c = conditions[j];
                var cp = $"{prefix}.conditions[{j}]";
                if (c.Position < 1) errors[$"{cp}.position"] = "La posición debe ser 1 o mayor.";
                else if (c.Position > ImportMapping.MaxPosition) errors[$"{cp}.position"] = $"La posición no puede exceder {ImportMapping.MaxPosition}.";
                var op = ImportConditionOperators.Canonical(c.Operator);
                if (op is null) errors[$"{cp}.operator"] = UnknownOperatorMessage(c.Operator?.Trim() ?? string.Empty);
                else if (ImportConditionOperators.NeedsValue(op) && string.IsNullOrWhiteSpace(c.Value)) errors[$"{cp}.value"] = ValueRequiredMessage;
                if (c.Value is { } value && value.Trim().Length > ValueMax) errors[$"{cp}.value"] = ValueMaxMessage;
            }
            if (conditions.Count == 0 && i < variants.Count - 1) errors.TryAdd($"{prefix}.conditions", UnreachableMessage(i + 1));

            foreach (var (key, message) in ConsigneeImportMapping.ValidateColumns(v.Columns, defaults))
            {
                if (key.StartsWith("defaults.", StringComparison.Ordinal)) errors.TryAdd(key, message); // comunes: una vez
                else errors.TryAdd($"{prefix}.{key}", message);
            }
        }
        return errors;
    }

    /// <summary>¿La celda de la fila cumple la condición? Texto comparado sin acentos, mayúsculas ni espacios de más.</summary>
    public static bool Matches(IReadOnlyList<string> cells, ImportVariantCondition condition)
    {
        var index = condition.Position - 1;
        var cell = index >= 0 && index < cells.Count ? cells[index] : null;
        var empty = string.IsNullOrWhiteSpace(cell);
        switch (ImportConditionOperators.Canonical(condition.Operator))
        {
            case ImportConditionOperators.IsEmpty: return empty;
            case ImportConditionOperators.NotEmpty: return !empty;
            case ImportConditionOperators.EqualTo:
                return !empty && PostalLocalityRules.Normalize(cell) == PostalLocalityRules.Normalize(condition.Value);
            case ImportConditionOperators.Contains:
                var needle = PostalLocalityRules.Normalize(condition.Value);
                return !empty && needle.Length > 0 && PostalLocalityRules.Normalize(cell).Contains(needle, StringComparison.Ordinal);
            default: return false;
        }
    }

    /// <summary>Índice de la primera variante cuyas condiciones (todas) se cumplen; -1 si ninguna.</summary>
    public static int Select(IReadOnlyList<string> cells, IReadOnlyList<ImportVariant> variants)
    {
        for (var i = 0; i < variants.Count; i++)
            if ((variants[i].Conditions ?? Array.Empty<ImportVariantCondition>()).All(c => Matches(cells, c))) return i;
        return -1;
    }

    /// <summary>Forma guardada: nombre y valor recortados (vacío = null), operador canónico, sin valor en EMPTY / NOT_EMPTY y columnas normalizadas.</summary>
    public static ImportVariant Normalize(ImportVariant v)
        => new(
            string.IsNullOrWhiteSpace(v.Name) ? null : v.Name.Trim(),
            (v.Conditions ?? Array.Empty<ImportVariantCondition>()).Select(c =>
            {
                var op = ImportConditionOperators.Canonical(c.Operator) ?? c.Operator?.Trim().ToUpperInvariant() ?? string.Empty;
                return new ImportVariantCondition(c.Position, op,
                    ImportConditionOperators.NeedsValue(op) && !string.IsNullOrWhiteSpace(c.Value) ? c.Value.Trim() : null);
            }).ToList(),
            ConsigneeImportMapping.NormalizeColumns(v.Columns));
}
