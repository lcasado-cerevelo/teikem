using System.Globalization;
using Teikem.Domain.Orders;

namespace Teikem.Domain.Clients;

/// <summary>
/// Lote 38 (F-B): catálogo de campos que una plantilla de importación de consignatarios (ImportKinds.Consignee) puede mapear
/// a una posición de columna. Nombres de la API (camelCase), comparados sin distinguir mayúsculas. El tipo de localización
/// no se mapea: todo consignatario importado es DELIVERY.
/// </summary>
public static class ConsigneeImportFields
{
    public const string Code = "code";
    public const string Name = "name";
    public const string Line1 = "line1";
    public const string Line2 = "line2";
    public const string City = "city";
    public const string State = "state";
    public const string PostalCode = "postalCode";
    public const string Country = "country";
    public const string ClientCode = "clientCode";
    public const string ClientName = "clientName";
    public const string DefaultServiceMinutes = "defaultServiceMinutes";
    public const string DefaultWindowStart = "defaultWindowStart";
    public const string DefaultWindowEnd = "defaultWindowEnd";
    public const string AccessNotes = "accessNotes";
    public const string DeliveryNotes = "deliveryNotes";
    public const string AllowDupInvoice = "allowDupInvoice";
    public const string ContactPhone = "contactPhone";
    /// <summary>Lote 45 (F-B2): persona de contacto del consignatario (Location.ContactName).</summary>
    public const string ContactName = "contactName";

    /// <summary>
    /// Lote 45 (F-B2): marca de columna «(No usar)». No es un campo del catálogo: la columna se guarda en la plantilla para que
    /// el editor la recuerde, y al importar se ignora por completo (la fila NO se salta).
    /// </summary>
    public const string Ignore = "ignore";

    public static readonly IReadOnlyList<string> All =
    [
        Code, Name, Line1, Line2, City, State, PostalCode, Country, ClientCode, ClientName,
        DefaultServiceMinutes, DefaultWindowStart, DefaultWindowEnd, AccessNotes, DeliveryNotes, AllowDupInvoice, ContactPhone, ContactName,
    ];

    /// <summary>
    /// Campos que la fila debe traer. Lote 46 (F-B3): el pueblo (city) ya no: en Puerto Rico y Estados Unidos sale del código postal
    /// con el catálogo; la plantilla debe traer city o postalCode (en columna o en defaults).
    /// </summary>
    public static readonly IReadOnlySet<string> Required = new HashSet<string>(StringComparer.Ordinal) { Name, Line1 };

    /// <summary>Campos que identifican a CADA consignatario o a su dueño: no admiten valor por defecto en la plantilla.</summary>
    public static readonly IReadOnlySet<string> NoDefault = new HashSet<string>(StringComparer.Ordinal) { Code, Name, Line1, ClientCode, ClientName };

    /// <summary>
    /// Lote 45 (F-B2): campos con formato propio (país, cliente, números, horas, sí/no): toman UNA sola columna. Los de texto
    /// (nombre, código, dirección, pueblo, ZIP, notas, teléfono, persona de contacto) pueden unir varias.
    /// </summary>
    public static readonly IReadOnlySet<string> SingleColumn = new HashSet<string>(StringComparer.Ordinal)
    {
        Country, ClientCode, ClientName, DefaultServiceMinutes, DefaultWindowStart, DefaultWindowEnd, AllowDupInvoice,
    };

    /// <summary>¿Es la marca «(No usar)»? (sin distinguir mayúsculas).</summary>
    public static bool IsIgnore(string? field) => string.Equals(field?.Trim(), Ignore, StringComparison.OrdinalIgnoreCase);

    /// <summary>Nombre canónico (camelCase del catálogo) o null si el campo no existe.</summary>
    public static string? Canonical(string? field)
    {
        if (string.IsNullOrWhiteSpace(field)) return null;
        var f = field.Trim();
        return All.FirstOrDefault(a => string.Equals(a, f, StringComparison.OrdinalIgnoreCase));
    }

    public static bool IsKnown(string? field) => Canonical(field) is not null;
}

/// <summary>Consignatario del directorio que entra en la revisión de duplicados (Location ya guardada).</summary>
public sealed record ExistingConsignee(int LocationId, int? ClientId, string? Code, string Name, string Line1, bool IsActive, bool IsDeliveryOrBoth);

/// <summary>Fila del archivo para la revisión de duplicados: dueño (ClientId o null = compartida), código, nombre y línea 1.</summary>
public sealed record ConsigneeRowKey(int Row, int? OwnerClientId, string? Code, string? Name, string? Line1);

/// <summary>Hallazgo de duplicado: campo, mensaje y si es solo aviso (no bloquea la fila).</summary>
public sealed record DuplicateFinding(string Field, string Message, bool IsWarning);

/// <summary>
/// Lote 38 (F-B): reglas puras de la importación de consignatarios: validación de la plantilla, mapeo de la fila
/// (celda → default), parseos (sí/no, hora HH:mm, ventana) y reglas de duplicados (código y nombre + línea 1 por dueño).
/// Sin EF: el servicio carga el directorio y le pasa las filas.
/// </summary>
public static class ConsigneeImportMapping
{
    public const int NameMax = 200, CodeMax = 40, LineMax = 200, CityMax = 100, StateMax = 100, PostalMax = 20, NotesMax = 500, ContactNameMax = 150;

    /// <summary>Lote 45 (F-B2): columnas que un mismo campo puede unir y largo máximo del separador.</summary>
    public const int MaxParts = 5, SeparatorMax = 10;
    /// <summary>Separador que se usa entre dos columnas unidas cuando la plantilla no indica otro.</summary>
    public const string DefaultSeparator = " ";
    public const string SeparatorMaxMessage = "El separador admite a lo sumo 10 caracteres.";
    public static string TooManyPartsMessage(string field) => $"El campo {field} admite a lo sumo {MaxParts} columnas unidas.";
    public static string IgnoredColumnUsedMessage(int position) => $"La columna {position} está marcada «No usar» y también tiene un campo asignado.";

    public const string NameLine1ColumnsMessage = "La plantilla debe incluir las columnas name y line1.";
    /// <summary>Lote 46 (F-B3): antes «La plantilla debe indicar la ciudad (city) en una columna o en defaults.»; el código postal también basta.</summary>
    public const string CityColumnMessage = "La plantilla debe indicar la ciudad (city) o el código postal (postalCode), en una columna o en defaults.";
    public const string ClientCodeOrNameMessage = "Use clientCode o clientName, no ambos.";
    public const string NameRequiredMessage = "El nombre es obligatorio.";
    public const string Line1RequiredMessage = "La dirección (línea 1) es obligatoria.";
    public const string CityRequiredMessage = "La ciudad es obligatoria.";
    public const string ServiceMinutesIntegerMessage = "Los minutos de servicio deben ser un número entero.";
    public const string ServiceMinutesNegativeMessage = "Los minutos de servicio no pueden ser negativos.";
    public const string TimeFormatMessage = "La hora debe tener el formato HH:mm (24 horas).";
    public const string WindowPairMessage = "La ventana horaria requiere hora de inicio y hora de fin.";
    public const string WindowOrderMessage = "La hora de fin de la ventana debe ser posterior a la de inicio.";
    public const string AllowDupInvoiceMessage = "El valor de allowDupInvoice debe ser sí o no (también 1/0 o true/false).";
    public const string DuplicateActiveNameLineMessage = "Ya existe un consignatario activo con el mismo nombre y dirección (línea 1).";
    public const string InactiveNameLineWarning = "Hay un consignatario inactivo con el mismo nombre y dirección; considere reactivarlo.";

    public static string NoDefaultMessage(string field) => $"El campo {field} no admite valor por defecto.";
    public static string MaxLengthMessage(int max) => $"Máximo {max} caracteres.";
    public static string UnknownCountryMessage(string code) => $"País desconocido: {code}.";
    public static string CodeRepeatedInFileMessage(string code, int firstRow) => $"El código {code} se repite en la fila {firstRow} del archivo.";
    public static string NameLineRepeatedInFileMessage(string name, int firstRow) => $"El consignatario '{name}' con esa dirección se repite en la fila {firstRow} del archivo.";
    public static string CodeTakenMessage(string code, string existingName) => $"Ya existe un consignatario con el código '{code}' ('{existingName}').";
    public static string CodeTakenInactiveMessage(string code, string existingName) => $"Ya existe un consignatario inactivo con el código '{code}' ('{existingName}'); reactívelo en vez de importarlo.";
    public static string SharedCodeUsedByClientWarning(string code) => $"El código {code} también lo usa un consignatario de un cliente; para ese cliente seguirá resolviendo al suyo.";
    public static string SameNameOtherBranchWarning(string name) => $"El nombre '{name}' ya lo usa otro consignatario de este dueño; se creará como sucursal distinta.";

    // ---------------------------------------------------------------- plantilla

    /// <summary>
    /// Errores por campo de la definición de una plantilla CONSIGNEE (vacío = válida). Posiciones y campos con los mismos
    /// mensajes que la plantilla de órdenes; además name y line1 en columna, city en columna o defaults, clientCode y
    /// clientName excluyentes y sin default para code/name/line1/clientCode/clientName.
    /// Lote 45 (F-B2): una columna puede marcarse «No usar» (field = ignore; no puede tener además otro campo), una misma
    /// columna puede alimentar varios campos (p. ej. el código y parte del nombre) y un campo de texto puede unir hasta
    /// MaxParts columnas (los de SingleColumn siguen siendo de una sola: «El campo X está repetido.»). La misma columna dos
    /// veces en el mismo campo es «La posición N está repetida.».
    /// </summary>
    public static IReadOnlyDictionary<string, string> ValidateColumns(IReadOnlyList<ImportColumn>? columns, IReadOnlyDictionary<string, string>? defaults)
    {
        var errors = new Dictionary<string, string>(StringComparer.Ordinal);
        if (columns is null || columns.Count == 0) errors["columns"] = ImportMapping.ColumnsRequiredMessage;
        else if (columns.Count > ImportMapping.MaxColumns) errors["columns"] = $"Máximo {ImportMapping.MaxColumns} columnas.";

        var byPosition = new Dictionary<int, HashSet<string>>();
        var fields = new HashSet<string>(StringComparer.Ordinal);
        var parts = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; columns is not null && i < columns.Count; i++)
        {
            var c = columns[i];
            var isIgnore = ConsigneeImportFields.IsIgnore(c.Field);
            var canonical = isIgnore ? ConsigneeImportFields.Ignore : ConsigneeImportFields.Canonical(c.Field);

            if (c.Position < 1) errors[$"columns[{i}].position"] = "La posición debe ser 1 o mayor.";
            else if (c.Position > ImportMapping.MaxPosition) errors[$"columns[{i}].position"] = $"La posición no puede exceder {ImportMapping.MaxPosition}.";
            else if (canonical is not null)
            {
                if (!byPosition.TryGetValue(c.Position, out var atPosition)) byPosition[c.Position] = atPosition = new HashSet<string>(StringComparer.Ordinal);
                if (!atPosition.Add(canonical)) errors[$"columns[{i}].position"] = ImportMapping.DuplicatePositionMessage(c.Position);
                else if (atPosition.Count > 1 && atPosition.Contains(ConsigneeImportFields.Ignore))
                    errors[$"columns[{i}].field"] = IgnoredColumnUsedMessage(c.Position);
            }

            if (c.Separator is { Length: > SeparatorMax }) errors[$"columns[{i}].separator"] = SeparatorMaxMessage;
            if (isIgnore) continue;
            if (canonical is null) { errors[$"columns[{i}].field"] = ImportMapping.UnknownFieldMessage(c.Field?.Trim() ?? string.Empty); continue; }

            fields.Add(canonical);
            var count = parts[canonical] = parts.GetValueOrDefault(canonical) + 1;
            if (count > 1 && ConsigneeImportFields.SingleColumn.Contains(canonical)) errors.TryAdd($"columns[{i}].field", ImportMapping.DuplicateFieldMessage(canonical));
            else if (count > MaxParts) errors.TryAdd($"columns[{i}].field", TooManyPartsMessage(canonical));
        }

        var defaultFields = new HashSet<string>(StringComparer.Ordinal);
        if (defaults is not null)
        {
            foreach (var (key, _) in defaults)
            {
                var canonical = ConsigneeImportFields.Canonical(key);
                if (canonical is null) errors[$"defaults.{key}"] = ImportMapping.UnknownFieldMessage(key);
                else if (ConsigneeImportFields.NoDefault.Contains(canonical)) errors[$"defaults.{key}"] = NoDefaultMessage(canonical);
                else
                {
                    defaultFields.Add(canonical);
                    if (DefaultValueError(canonical, defaults[key]) is string message) errors[$"defaults.{key}"] = message;
                }
            }
        }

        if (columns is { Count: > 0 } && !errors.ContainsKey("columns"))
        {
            if (!fields.Contains(ConsigneeImportFields.Name) || !fields.Contains(ConsigneeImportFields.Line1))
                errors["columns"] = NameLine1ColumnsMessage;
            else if (!fields.Contains(ConsigneeImportFields.City) && !defaultFields.Contains(ConsigneeImportFields.City)
                     && !fields.Contains(ConsigneeImportFields.PostalCode) && !defaultFields.Contains(ConsigneeImportFields.PostalCode))
                errors["columns"] = CityColumnMessage;
            else if (fields.Contains(ConsigneeImportFields.ClientCode) && fields.Contains(ConsigneeImportFields.ClientName))
                errors["columns"] = ClientCodeOrNameMessage;
        }
        return errors;
    }

    /// <summary>
    /// Error del valor por defecto de un campo tipado, con el MISMO mensaje que daría la fila (null = válido o vacío): minutos
    /// de servicio, horas de la ventana, allowDupInvoice, formato del país y topes de largo. El teléfono lo revisa el servicio
    /// (ContactPointService) y la existencia del país, la validación de cada fila.
    /// </summary>
    public static string? DefaultValueError(string field, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var v = value.Trim();
        switch (field)
        {
            case ConsigneeImportFields.DefaultServiceMinutes:
                if (!ImportMapping.TryParseInt(v, out var minutes)) return ServiceMinutesIntegerMessage;
                return minutes < 0 ? ServiceMinutesNegativeMessage : null;
            case ConsigneeImportFields.DefaultWindowStart:
            case ConsigneeImportFields.DefaultWindowEnd:
                return TryParseTime(v, out _) ? null : TimeFormatMessage;
            case ConsigneeImportFields.AllowDupInvoice:
                return TryParseBool(v, out _) ? null : AllowDupInvoiceMessage;
            case ConsigneeImportFields.Country:
                return Teikem.Domain.Common.CountryCode.IsValid(v.ToUpperInvariant()) ? null : Teikem.Domain.Common.CountryCode.InvalidMessage;
            case ConsigneeImportFields.Line2: return v.Length > LineMax ? MaxLengthMessage(LineMax) : null;
            case ConsigneeImportFields.City: return v.Length > CityMax ? MaxLengthMessage(CityMax) : null;
            case ConsigneeImportFields.State: return v.Length > StateMax ? MaxLengthMessage(StateMax) : null;
            case ConsigneeImportFields.PostalCode: return v.Length > PostalMax ? MaxLengthMessage(PostalMax) : null;
            case ConsigneeImportFields.AccessNotes:
            case ConsigneeImportFields.DeliveryNotes:
                return v.Length > NotesMax ? MaxLengthMessage(NotesMax) : null;
            case ConsigneeImportFields.ContactName: return v.Length > ContactNameMax ? MaxLengthMessage(ContactNameMax) : null;
            default:
                return null;
        }
    }

    /// <summary>
    /// Lote 45 (F-B2): columnas como se guardan en la plantilla. Nombre canónico del campo («ignore» para «No usar»); un campo
    /// de UNA columna queda sin Part ni Separator; uno que une varias queda con Part 1..n (orden pedido, si no por posición) y
    /// el separador de cada parte después de la primera (la primera nunca lleva). Orden: posición y luego parte.
    /// </summary>
    public static IReadOnlyList<ImportColumn> NormalizeColumns(IReadOnlyList<ImportColumn> columns)
    {
        ArgumentNullException.ThrowIfNull(columns);
        var withField = columns.Select((c, i) => (Column: c, Index: i,
            Field: ConsigneeImportFields.IsIgnore(c.Field) ? ConsigneeImportFields.Ignore : ConsigneeImportFields.Canonical(c.Field) ?? c.Field.Trim())).ToList();
        var result = new List<ImportColumn>(columns.Count);
        foreach (var group in withField.GroupBy(x => x.Field, StringComparer.Ordinal))
        {
            var ordered = group.OrderBy(x => x.Column.Part ?? int.MaxValue).ThenBy(x => x.Column.Position).ThenBy(x => x.Index).ToList();
            if (ordered.Count == 1 || group.Key == ConsigneeImportFields.Ignore)
            {
                result.AddRange(ordered.Select(x => new ImportColumn(x.Column.Position, group.Key)));
                continue;
            }
            for (var p = 0; p < ordered.Count; p++)
                result.Add(new ImportColumn(ordered[p].Column.Position, group.Key, p + 1, p == 0 ? null : ordered[p].Column.Separator));
        }
        return result.OrderBy(c => c.Position).ThenBy(c => c.Part ?? 0).ThenBy(c => c.Field, StringComparer.Ordinal).ToList();
    }

    private static readonly System.Text.RegularExpressions.Regex PhoneNumber =
        new(@"(?<!\d)(?:\+?1[\s.\-]?)?\(?(\d{3})\)?[\s.\-]*(\d{3})[\s.\-]*(\d{4})(?!\d)", System.Text.RegularExpressions.RegexOptions.Compiled);

    public static string MultiplePhonesWarning(string first) => $"La celda del teléfono trae más de un número; se tomó el primero ({first}).";

    /// <summary>
    /// Lote 45 (F-B2): teléfono de la celda en los formatos que traen los archivos reales (787-555-0101, 787 555-0101, 7875550101,
    /// (787)555-0101, +1 787…): se guardan los 10 dígitos (como el alta manual). Si la celda trae DOS o más números se toma el primero
    /// y Multiple = true (la fila lleva el aviso). Sin un número de 10 dígitos reconocible, el texto queda igual (lo revisa la regla
    /// de ContactPoint y, si no sirve, la fila da «Teléfono inválido.»).
    /// </summary>
    public static (string Value, bool Multiple) NormalizePhone(string raw)
    {
        var text = raw?.Trim() ?? string.Empty;
        var matches = PhoneNumber.Matches(text);
        if (matches.Count == 0) return (text, false);
        var m = matches[0];
        return (m.Groups[1].Value + m.Groups[2].Value + m.Groups[3].Value, matches.Count > 1);
    }

    /// <summary>
    /// Lote 45 (F-B2): ZIP de Puerto Rico o Estados Unidos que perdió los ceros de la izquierda (Excel lo guardó como número:
    /// 00901 → 901) se rellena a 5 dígitos. Solo toca 3 o 4 dígitos sin nada más; cualquier otro valor queda igual.
    /// </summary>
    public static string NormalizePostalCode(string value, string? country)
    {
        var c = country?.Trim().ToUpperInvariant();
        if (c is not ("PR" or "US")) return value;
        var v = value.Trim();
        return v.Length is 3 or 4 && v.All(char.IsAsciiDigit) ? v.PadLeft(5, '0') : v;
    }

    /// <summary>
    /// Defaults con llaves canónicas del catálogo de consignatarios y valores recortados (las llaves desconocidas ya las
    /// rechazó ValidateColumns; aquí se ignoran). Conserva allowDupInvoice, defaultServiceMinutes, ventana, notas, etc.
    /// </summary>
    public static IReadOnlyDictionary<string, string> NormalizeDefaults(IReadOnlyDictionary<string, string>? defaults)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (defaults is null) return result;
        foreach (var (key, value) in defaults)
        {
            var canonical = ConsigneeImportFields.Canonical(key);
            if (canonical is null || string.IsNullOrWhiteSpace(value)) continue;
            result[canonical] = value.Trim();
        }
        return result;
    }

    /// <summary>
    /// Valores por campo de una fila (misma regla que ImportMapping.Map: la celda no vacía gana; si no, el default).
    /// Lote 45 (F-B2): las columnas «No usar» se ignoran (la fila sigue); un campo con varias columnas une las celdas no vacías
    /// en el orden de Part (luego posición), con el separador de cada parte (un espacio si no trae); si todas vienen vacías, el
    /// campo cae en su default.
    /// </summary>
    public static IReadOnlyDictionary<string, string> Map(IReadOnlyList<string> cells, IReadOnlyList<ImportColumn> columns, IReadOnlyDictionary<string, string>? defaults)
    {
        ArgumentNullException.ThrowIfNull(cells);
        ArgumentNullException.ThrowIfNull(columns);
        var normalizedDefaults = NormalizeDefaults(defaults);
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var groups = new Dictionary<string, List<(ImportColumn Column, int Index)>>(StringComparer.Ordinal);
        for (var i = 0; i < columns.Count; i++)
        {
            var column = columns[i];
            if (ConsigneeImportFields.IsIgnore(column.Field)) continue;
            var field = ConsigneeImportFields.Canonical(column.Field)
                        ?? throw new ArgumentException(ImportMapping.UnknownFieldMessage(column.Field), nameof(columns));
            if (!groups.TryGetValue(field, out var list)) groups[field] = list = new List<(ImportColumn, int)>();
            list.Add((column, i));
        }
        foreach (var (field, list) in groups)
        {
            var joined = new System.Text.StringBuilder();
            foreach (var (column, _) in list.OrderBy(x => x.Column.Part ?? int.MaxValue).ThenBy(x => x.Column.Position).ThenBy(x => x.Index))
            {
                var index = column.Position - 1;
                var raw = index >= 0 && index < cells.Count ? cells[index] : null;
                if (string.IsNullOrWhiteSpace(raw)) continue;
                if (joined.Length > 0) joined.Append(column.Separator ?? DefaultSeparator);
                joined.Append(raw.Trim());
            }
            if (joined.Length > 0) values[field] = joined.ToString();
        }
        foreach (var (field, value) in normalizedDefaults)
            if (!values.ContainsKey(field)) values[field] = value;
        return values;
    }

    // ---------------------------------------------------------------- parseos

    private static readonly HashSet<string> TrueWords = new(StringComparer.OrdinalIgnoreCase) { "sí", "si", "s", "1", "true", "yes", "y" };
    private static readonly HashSet<string> FalseWords = new(StringComparer.OrdinalIgnoreCase) { "no", "n", "0", "false" };

    /// <summary>sí/si/no, s/n, 1/0, true/false, yes/no, y/n (sin distinguir mayúsculas).</summary>
    public static bool TryParseBool(string? text, out bool value)
    {
        var t = text?.Trim().Normalize();
        value = false;
        if (string.IsNullOrEmpty(t)) return false;
        if (TrueWords.Contains(t)) { value = true; return true; }
        return FalseWords.Contains(t);
    }

    private static readonly string[] TimeFormats = ["HH:mm", "H:mm", "HH:mm:ss", "H:mm:ss"];

    /// <summary>Hora de 24 horas HH:mm o HH:mm:ss (también H:mm).</summary>
    public static bool TryParseTime(string? text, out TimeOnly value)
    {
        var t = text?.Trim();
        if (string.IsNullOrEmpty(t)) { value = default; return false; }
        return TimeOnly.TryParseExact(t, TimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out value);
    }

    /// <summary>
    /// Ventana horaria (mismos mensajes que la ficha del consignatario): ninguna → válida; solo una → error en la que falta;
    /// fin &lt;= inicio → error en defaultWindowEnd.
    /// </summary>
    public static (string Field, string Message)? ValidateWindow(TimeOnly? start, TimeOnly? end)
    {
        if (start is null && end is null) return null;
        if (start is null) return (ConsigneeImportFields.DefaultWindowStart, WindowPairMessage);
        if (end is null) return (ConsigneeImportFields.DefaultWindowEnd, WindowPairMessage);
        if (start.Value >= end.Value) return (ConsigneeImportFields.DefaultWindowEnd, WindowOrderMessage);
        return null;
    }

    // ---------------------------------------------------------------- duplicados

    private static string? CodeKey(string? code) => string.IsNullOrWhiteSpace(code) ? null : code.Trim().ToUpperInvariant();

    /// <summary>
    /// Duplicados dentro del archivo, por dueño (mismo ClientId o ambas compartidas): código repetido (sin distinguir
    /// mayúsculas) → error en code; nombre + línea 1 normalizados (ConsigneeMatch.Key) repetidos → error en name. El mensaje
    /// nombra la PRIMERA fila donde aparece. Filas de otro dueño no chocan.
    /// </summary>
    public static IReadOnlyDictionary<int, IReadOnlyList<DuplicateFinding>> FindDuplicatesInFile(IEnumerable<ConsigneeRowKey> rows)
    {
        var codes = new Dictionary<(int? Owner, string Code), int>();
        var keys = new Dictionary<(int? Owner, string Key), int>();
        var result = new Dictionary<int, IReadOnlyList<DuplicateFinding>>();
        foreach (var r in rows)
        {
            var findings = new List<DuplicateFinding>();
            if (CodeKey(r.Code) is string code)
            {
                if (codes.TryGetValue((r.OwnerClientId, code), out var first))
                    findings.Add(new DuplicateFinding(ConsigneeImportFields.Code, CodeRepeatedInFileMessage(r.Code!.Trim(), first), false));
                else codes[(r.OwnerClientId, code)] = r.Row;
            }
            if (ConsigneeMatch.Key(r.Name, r.Line1) is string key)
            {
                if (keys.TryGetValue((r.OwnerClientId, key), out var first))
                    findings.Add(new DuplicateFinding(ConsigneeImportFields.Name, NameLineRepeatedInFileMessage(r.Name!.Trim(), first), false));
                else keys[(r.OwnerClientId, key)] = r.Row;
            }
            if (findings.Count > 0) result[r.Row] = findings;
        }
        return result;
    }

    /// <summary>Índice del directorio para revisar miles de filas sin recorrer todos los consignatarios por fila.</summary>
    public static ConsigneeDirectory BuildDirectory(IEnumerable<ExistingConsignee> existing)
    {
        var d = new ConsigneeDirectory();
        d.AddRange(existing);
        return d;
    }
}

/// <summary>
/// Lote 38 (F-B): directorio de consignatarios indexado por dueño para las reglas de duplicados contra la base.
/// - Código (sin distinguir mayúsculas, todos los tipos): fila de cliente contra los suyos y los compartidos; fila compartida
///   solo contra los compartidos. Activo → error; solo inactivo → error que pide reactivarlo. Fila compartida cuyo código usa
///   un consignatario activo de un cliente → aviso.
/// - Nombre + línea 1 (ConsigneeMatch.Key; mismo dueño exacto; DELIVERY/BOTH): activo → error; inactivo → aviso de reactivar;
///   mismo nombre normalizado con otra línea 1 (activo) → aviso de sucursal distinta.
/// </summary>
public sealed class ConsigneeDirectory
{
    private readonly HashSet<int> _seen = new();
    private readonly Dictionary<(int? Owner, string Code), List<ExistingConsignee>> _byCode = new();
    private readonly HashSet<string> _clientCodes = new(StringComparer.Ordinal);
    private readonly Dictionary<(int? Owner, string Key), List<ExistingConsignee>> _byKey = new();
    private readonly Dictionary<(int? Owner, string Name), List<ExistingConsignee>> _byName = new();

    public int Count => _seen.Count;

    /// <summary>Agrega consignatarios (los ya vistos por LocationId se ignoran).</summary>
    public void AddRange(IEnumerable<ExistingConsignee> items)
    {
        foreach (var e in items)
        {
            if (!_seen.Add(e.LocationId)) continue;
            if (!string.IsNullOrWhiteSpace(e.Code))
            {
                var code = e.Code.Trim().ToUpperInvariant();
                Bucket(_byCode, (e.ClientId, code)).Add(e);
                if (e.ClientId is not null && e.IsActive) _clientCodes.Add(code);
            }
            if (!e.IsDeliveryOrBoth) continue;
            if (ConsigneeMatch.Key(e.Name, e.Line1) is string key) Bucket(_byKey, (e.ClientId, key)).Add(e);
            if (ConsigneeMatch.Normalize(e.Name) is string name) Bucket(_byName, (e.ClientId, name)).Add(e);
        }
    }

    private static List<ExistingConsignee> Bucket<TKey>(Dictionary<TKey, List<ExistingConsignee>> map, TKey key) where TKey : notnull
    {
        if (!map.TryGetValue(key, out var list)) map[key] = list = new List<ExistingConsignee>();
        return list;
    }

    /// <summary>Errores (IsWarning=false) y avisos de la fila contra el directorio.</summary>
    public IReadOnlyList<DuplicateFinding> Evaluate(ConsigneeRowKey row)
    {
        var findings = new List<DuplicateFinding>();

        // Código
        if (!string.IsNullOrWhiteSpace(row.Code))
        {
            var typed = row.Code.Trim();
            var code = typed.ToUpperInvariant();
            var matches = new List<ExistingConsignee>();
            if (_byCode.TryGetValue((row.OwnerClientId, code), out var own)) matches.AddRange(own);
            if (row.OwnerClientId is not null && _byCode.TryGetValue((null, code), out var shared)) matches.AddRange(shared);
            var active = matches.Where(m => m.IsActive).OrderBy(m => m.LocationId).FirstOrDefault();
            var inactive = matches.Where(m => !m.IsActive).OrderBy(m => m.LocationId).FirstOrDefault();
            if (active is not null)
                findings.Add(new DuplicateFinding(ConsigneeImportFields.Code, ConsigneeImportMapping.CodeTakenMessage(typed, active.Name), false));
            else if (inactive is not null)
                findings.Add(new DuplicateFinding(ConsigneeImportFields.Code, ConsigneeImportMapping.CodeTakenInactiveMessage(typed, inactive.Name), false));
            else if (row.OwnerClientId is null && _clientCodes.Contains(code))
                findings.Add(new DuplicateFinding(ConsigneeImportFields.Code, ConsigneeImportMapping.SharedCodeUsedByClientWarning(typed), true));
        }

        // Nombre + línea 1
        if (ConsigneeMatch.Key(row.Name, row.Line1) is string key)
        {
            var same = _byKey.TryGetValue((row.OwnerClientId, key), out var list) ? list : null;
            if (same is not null && same.Any(m => m.IsActive))
                findings.Add(new DuplicateFinding(ConsigneeImportFields.Name, ConsigneeImportMapping.DuplicateActiveNameLineMessage, false));
            else if (same is not null && same.Count > 0)
                findings.Add(new DuplicateFinding(ConsigneeImportFields.Name, ConsigneeImportMapping.InactiveNameLineWarning, true));
            else if (ConsigneeMatch.Normalize(row.Name) is string name
                     && _byName.TryGetValue((row.OwnerClientId, name), out var sameName) && sameName.Any(m => m.IsActive))
                findings.Add(new DuplicateFinding(ConsigneeImportFields.Name, ConsigneeImportMapping.SameNameOtherBranchWarning(row.Name!.Trim()), true));
        }
        return findings;
    }
}
