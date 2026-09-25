using System.Globalization;

namespace Teikem.Domain.Orders;

/// <summary>Tipos de plantilla/lote de importación (columna Kind de dbo.ImportTemplate y dbo.ImportBatch).</summary>
public static class ImportKinds
{
    public const string Order = "ORDER";
}

/// <summary>Dominio de estatus ImportBatchStatus: VALIDATED (inicial, pipeline) → CONFIRMED (pipeline); DISCARDED terminal.</summary>
public static class ImportBatchStatuses
{
    public const string Domain = "ImportBatchStatus";
    public const string Validated = "VALIDATED";
    public const string Confirmed = "CONFIRMED";
    public const string Discarded = "DISCARDED";
}

/// <summary>Códigos EntityType de las entidades del importador (historial de estatus y AuditLog).</summary>
public static class ImportEntityTypes
{
    public const string ImportTemplate = "IMPORT_TEMPLATE";
    public const string ImportBatch = "IMPORT_BATCH";
}

/// <summary>
/// Catálogo de campos que una plantilla de importación de órdenes puede mapear a una posición de columna
/// (ajuste D del Lote 3). Los nombres son los de la API (camelCase) y se comparan sin distinguir mayúsculas.
/// </summary>
public static class ImportFields
{
    public const string OrderNumber = "orderNumber";
    public const string ClientInvoiceNumber = "clientInvoiceNumber";
    public const string ServiceType = "serviceType";
    public const string ConsigneeName = "consigneeName";
    public const string ConsigneeCode = "consigneeCode";
    public const string Line1 = "line1";
    public const string Line2 = "line2";
    public const string City = "city";
    public const string State = "state";
    public const string PostalCode = "postalCode";
    public const string Country = "country";
    public const string ContactPhone = "contactPhone";
    public const string PackageType = "packageType";
    public const string Pieces = "pieces";
    public const string WeightKg = "weightKg";
    public const string VolumeM3 = "volumeM3";
    public const string Description = "description";
    public const string CodAmount = "codAmount";
    public const string RequestedDate = "requestedDate";
    public const string Notes = "notes";
    public const string Reference = "reference";

    public static readonly IReadOnlyList<string> All =
    [
        OrderNumber, ClientInvoiceNumber, ServiceType, ConsigneeName, ConsigneeCode, Line1, Line2, City, State, PostalCode,
        Country, ContactPhone, PackageType, Pieces, WeightKg, VolumeM3, Description, CodAmount, RequestedDate, Notes, Reference,
    ];

    /// <summary>Nombre canónico (camelCase del catálogo) o null si el campo no existe.</summary>
    public static string? Canonical(string? field)
    {
        if (string.IsNullOrWhiteSpace(field)) return null;
        var f = field.Trim();
        return All.FirstOrDefault(a => string.Equals(a, f, StringComparison.OrdinalIgnoreCase));
    }

    public static bool IsKnown(string? field) => Canonical(field) is not null;
}

/// <summary>Columna de una plantilla: posición (1 = primera columna del archivo) y campo del catálogo.</summary>
public sealed record ImportColumn(int Position, string Field);

/// <summary>
/// Reglas puras del mapeo por posición (Lote 3, P9): validación de la plantilla (posiciones únicas &gt;= 1, campos del
/// catálogo, consignatario por nombre o código, paquete por columna o por defaults) y mapeo de una fila parseada a
/// valores por campo (celda no vacía → valor; vacía → default de la plantilla si existe; si no, el campo se omite).
/// Incluye los parseos tipados que el servicio usa fila a fila (invariantes: punto decimal, fechas ISO o M/d/yyyy).
/// </summary>
public static class ImportMapping
{
    public const int MaxPosition = 500;
    public const int MaxColumns = 100;

    public const string ColumnsRequiredMessage = "Indique al menos una columna.";
    public const string ConsigneeColumnMessage = "La plantilla debe incluir consigneeName o consigneeCode.";
    public const string PackageColumnMessage = "La plantilla debe indicar el paquete: packageType o pieces, en una columna o en defaults.";

    public static string UnknownFieldMessage(string field) => $"Campo desconocido: {field}.";
    public static string DuplicatePositionMessage(int position) => $"La posición {position} está repetida.";
    public static string DuplicateFieldMessage(string field) => $"El campo {field} está repetido.";

    /// <summary>Errores por campo de la definición de una plantilla (vacío = válida). Las llaves siguen la forma columns[i].position / defaults.campo.</summary>
    public static IReadOnlyDictionary<string, string> ValidateColumns(IReadOnlyList<ImportColumn>? columns, IReadOnlyDictionary<string, string>? defaults)
    {
        var errors = new Dictionary<string, string>(StringComparer.Ordinal);
        if (columns is null || columns.Count == 0)
        {
            errors["columns"] = ColumnsRequiredMessage;
        }
        else if (columns.Count > MaxColumns)
        {
            errors["columns"] = $"Máximo {MaxColumns} columnas.";
        }

        var positions = new HashSet<int>();
        var fields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; columns is not null && i < columns.Count; i++)
        {
            var c = columns[i];
            if (c.Position < 1) errors[$"columns[{i}].position"] = "La posición debe ser 1 o mayor.";
            else if (c.Position > MaxPosition) errors[$"columns[{i}].position"] = $"La posición no puede exceder {MaxPosition}.";
            else if (!positions.Add(c.Position)) errors[$"columns[{i}].position"] = DuplicatePositionMessage(c.Position);

            var canonical = ImportFields.Canonical(c.Field);
            if (canonical is null) errors[$"columns[{i}].field"] = UnknownFieldMessage(c.Field?.Trim() ?? string.Empty);
            else if (!fields.Add(canonical)) errors[$"columns[{i}].field"] = DuplicateFieldMessage(canonical);
        }

        var defaultFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (defaults is not null)
        {
            foreach (var (key, _) in defaults)
            {
                var canonical = ImportFields.Canonical(key);
                if (canonical is null) errors[$"defaults.{key}"] = UnknownFieldMessage(key);
                else defaultFields.Add(canonical);
            }
        }

        if (columns is { Count: > 0 } && !errors.ContainsKey("columns"))
        {
            if (!fields.Contains(ImportFields.ConsigneeName) && !fields.Contains(ImportFields.ConsigneeCode))
                errors["columns"] = ConsigneeColumnMessage;
            else if (!fields.Contains(ImportFields.PackageType) && !fields.Contains(ImportFields.Pieces)
                     && !defaultFields.Contains(ImportFields.PackageType) && !defaultFields.Contains(ImportFields.Pieces))
                errors["columns"] = PackageColumnMessage;
        }
        return errors;
    }

    /// <summary>Defaults con llaves canónicas y valores recortados (las llaves desconocidas ya las rechazó ValidateColumns; aquí se ignoran).</summary>
    public static IReadOnlyDictionary<string, string> NormalizeDefaults(IReadOnlyDictionary<string, string>? defaults)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (defaults is null) return result;
        foreach (var (key, value) in defaults)
        {
            var canonical = ImportFields.Canonical(key);
            if (canonical is null || string.IsNullOrWhiteSpace(value)) continue;
            result[canonical] = value.Trim();
        }
        return result;
    }

    /// <summary>
    /// Valores por campo de una fila: la celda de cada posición mapeada (recortada) si no está vacía; si está vacía o la
    /// fila no llega a esa posición, el default de la plantilla si existe; si no, el campo se omite. Los defaults de
    /// campos sin columna también se aplican. Las llaves son canónicas.
    /// </summary>
    public static IReadOnlyDictionary<string, string> Map(IReadOnlyList<string> cells, IReadOnlyList<ImportColumn> columns, IReadOnlyDictionary<string, string>? defaults)
    {
        ArgumentNullException.ThrowIfNull(cells);
        ArgumentNullException.ThrowIfNull(columns);
        var normalizedDefaults = NormalizeDefaults(defaults);
        var values = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var column in columns)
        {
            var field = ImportFields.Canonical(column.Field) ?? throw new ArgumentException(UnknownFieldMessage(column.Field), nameof(columns));
            var index = column.Position - 1;
            var raw = index >= 0 && index < cells.Count ? cells[index] : null;
            var value = string.IsNullOrWhiteSpace(raw) ? null : raw.Trim();
            if (value is not null) values[field] = value;
        }
        foreach (var (field, value) in normalizedDefaults)
            if (!values.ContainsKey(field)) values[field] = value;

        return values;
    }

    // ---------------------------------------------------------------- parseos tipados (invariantes)

    public static bool TryParseInt(string? text, out int value)
        => int.TryParse(text?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

    public static bool TryParseDecimal(string? text, out decimal value)
        => decimal.TryParse(text?.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out value);

    private static readonly string[] DateFormats =
    [
        "yyyy-MM-dd", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-ddTHH:mm", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm",
        "M/d/yyyy", "MM/dd/yyyy", "M/d/yyyy HH:mm", "MM/dd/yyyy HH:mm",
    ];

    /// <summary>Fecha ISO (yyyy-MM-dd, con hora opcional) o M/d/yyyy; sin conversión de zona horaria.</summary>
    public static bool TryParseDate(string? text, out DateTime value)
    {
        var t = text?.Trim();
        if (string.IsNullOrEmpty(t)) { value = default; return false; }
        if (DateTime.TryParseExact(t, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out value)) return true;
        return DateTime.TryParse(t, CultureInfo.InvariantCulture, DateTimeStyles.None, out value);
    }
}
