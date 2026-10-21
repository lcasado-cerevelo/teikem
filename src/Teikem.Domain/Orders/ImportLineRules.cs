using Teikem.Domain.Catalogs;
using Teikem.Domain.Clients;

namespace Teikem.Domain.Orders;

/// <summary>Clase de una línea de un archivo con VARIAS líneas por orden (Lote 131): pieza física, descripción de lo que se envía o factura.</summary>
public static class ImportLineKinds
{
    public const string Piece = "PIECE";
    public const string Description = "DESCRIPTION";
    public const string Invoice = "INVOICE";
}

/// <summary>
/// Regla de una plantilla de órdenes con varias líneas por orden: si la celda de la columna <c>Position</c> cumple la condición
/// (<c>Operator</c>: EMPTY, NOT_EMPTY, EQUALS, CONTAINS o IN —lista de valores separados por «|»—), la línea es de la clase <c>Kind</c>
/// (DESCRIPTION o FACTURA). Varias reglas = cualquiera de ellas; la primera que se cumple manda.
/// </summary>
public sealed record ImportLineRule(string Kind, int Position, string Operator, string? Value = null);

/// <summary>
/// Lote 131 (dictado de Luis, 2026-10-19; caso AxisCare, para CUALQUIER cliente): el mapeo declara lo que NO es pieza (descripciones y facturas)
/// y todo lo demás es pieza. Reglas puras (sin EF): validación de las reglas guardadas en la plantilla y clasificación de cada línea.
/// </summary>
public static class ImportLineRules
{
    public const string InOperator = "IN";
    public const int ValueMax = 200;
    public const int MaxRules = 20;

    public const string NeedsOrderNumberMessage = "Un archivo con varias líneas por envío necesita la columna orderNumber en la plantilla.";
    public const string RulesNeedMultiLineMessage = "Las reglas de línea solo se usan en una plantilla con varias líneas por envío.";
    public const string MissingOrderNumberMessage = "La línea no trae número de envío; en un archivo con varias líneas por envío todas lo necesitan.";
    public const string NoPieceMessage = "El envío no trae ninguna pieza (todas sus líneas son descripción o factura).";
    public static string UnknownKindMessage(string kind) => $"Clase de línea desconocida: {kind}. Use DESCRIPTION o INVOICE.";
    public static string UnknownOperatorMessage(string op) => $"Condición desconocida: {op}. Use EMPTY, NOT_EMPTY, EQUALS, CONTAINS o IN.";
    public static string ValueRequiredMessage(string op) => $"La condición {op} necesita un valor.";
    public static string TooManyRulesMessage => $"Máximo {MaxRules} reglas.";

    public static string? CanonicalKind(string? kind)
    {
        var k = kind?.Trim().ToUpperInvariant();
        return k is ImportLineKinds.Description or ImportLineKinds.Invoice ? k : null;
    }

    public static string? CanonicalOperator(string? op)
    {
        var o = op?.Trim().ToUpperInvariant();
        return o == InOperator ? InOperator : ImportConditionOperators.Canonical(o);
    }

    /// <summary>Errores de las reglas (clave «rules[i].campo» → mensaje); vacío si están bien.</summary>
    public static IReadOnlyDictionary<string, string> Validate(IReadOnlyList<ImportLineRule>? rules)
    {
        var errors = new Dictionary<string, string>(StringComparer.Ordinal);
        if (rules is null) return errors;
        if (rules.Count > MaxRules) errors["rules"] = TooManyRulesMessage;
        for (var i = 0; i < rules.Count; i++)
        {
            var r = rules[i];
            if (CanonicalKind(r.Kind) is null) errors[$"rules[{i}].kind"] = UnknownKindMessage(r.Kind);
            if (r.Position is < 1 or > ImportMapping.MaxPosition) errors[$"rules[{i}].position"] = $"La posición debe estar entre 1 y {ImportMapping.MaxPosition}.";
            var op = CanonicalOperator(r.Operator);
            if (op is null) errors[$"rules[{i}].operator"] = UnknownOperatorMessage(r.Operator);
            else if (op is not (ImportConditionOperators.IsEmpty or ImportConditionOperators.NotEmpty))
            {
                if (string.IsNullOrWhiteSpace(r.Value)) errors[$"rules[{i}].value"] = ValueRequiredMessage(op);
                else if (r.Value.Trim().Length > ValueMax) errors[$"rules[{i}].value"] = $"Máximo {ValueMax} caracteres.";
            }
        }
        return errors;
    }

    /// <summary>Forma guardada: clase y operador canónicos, valor recortado y sin valor en EMPTY / NOT_EMPTY.</summary>
    public static ImportLineRule Normalize(ImportLineRule r)
    {
        var op = CanonicalOperator(r.Operator) ?? r.Operator?.Trim().ToUpperInvariant() ?? string.Empty;
        var noValue = op is ImportConditionOperators.IsEmpty or ImportConditionOperators.NotEmpty;
        return new ImportLineRule(CanonicalKind(r.Kind) ?? r.Kind?.Trim().ToUpperInvariant() ?? string.Empty, r.Position, op, noValue || string.IsNullOrWhiteSpace(r.Value) ? null : r.Value.Trim());
    }

    /// <summary>¿La celda cumple la condición de la regla? Texto comparado sin acentos, mayúsculas ni espacios de más.</summary>
    public static bool Matches(IReadOnlyList<string> cells, ImportLineRule rule)
    {
        if (CanonicalOperator(rule.Operator) != InOperator)
            return ConsigneeImportVariants.Matches(cells, new ImportVariantCondition(rule.Position, rule.Operator, rule.Value));
        var index = rule.Position - 1;
        var cell = index >= 0 && index < cells.Count ? cells[index] : null;
        if (string.IsNullOrWhiteSpace(cell)) return false;
        var normalized = PostalLocalityRules.Normalize(cell);
        return (rule.Value ?? string.Empty).Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(v => PostalLocalityRules.Normalize(v) == normalized);
    }

    /// <summary>Clase de la línea: la de la primera regla que se cumple; sin regla que aplique, PIECE (todo lo que no es descripción ni factura es pieza).</summary>
    public static string Classify(IReadOnlyList<string> cells, IReadOnlyList<ImportLineRule>? rules)
    {
        foreach (var r in rules ?? Array.Empty<ImportLineRule>())
            if (Matches(cells, r)) return CanonicalKind(r.Kind) ?? ImportLineKinds.Piece;
        return ImportLineKinds.Piece;
    }
}
