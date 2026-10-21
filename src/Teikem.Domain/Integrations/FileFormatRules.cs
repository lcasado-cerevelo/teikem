using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml;
using System.Xml.Linq;
using Teikem.Domain.Orders;

namespace Teikem.Domain.Integrations;

/// <summary>Formato del archivo que lee una integración (Lote 137, P11). AUTO decide por la extensión: .xlsx → Excel, .json → JSON, .xml → XML, lo demás → CSV.</summary>
public static class FileFormatKinds
{
    public const string Auto = "AUTO";
    public const string Csv = "CSV";
    public const string Excel = "EXCEL";
    public const string Json = "JSON";
    public const string Xml = "XML";
    public static readonly IReadOnlyList<string> All = [Auto, Csv, Excel, Json, Xml];
}

/// <summary>Una columna del mapeo por ruta: el nombre de la columna (encabezado) y de dónde sale su valor.</summary>
public sealed record FormatColumn(string Header, string Path);

/// <summary>
/// Formato del archivo de una integración. Excel: Sheet = nombre de la hoja (vacío = la primera). JSON/XML: RecordsPath = dónde están las órdenes (p. ej. <c>$.orders[*]</c>), LinesPath = las líneas dentro de
/// cada orden (p. ej. <c>@.lines[*]</c>; opcional) y Columns = el mapeo de rutas a columnas, en el orden que espera la plantilla de importación.
/// </summary>
public sealed record FileFormatConfig(string Kind = FileFormatKinds.Auto, string? Sheet = null, string? RecordsPath = null, string? LinesPath = null, IReadOnlyList<FormatColumn>? Columns = null);

/// <summary>Una ruta ya leída: la raíz (<c>$</c> documento, <c>^</c> orden, <c>@</c> registro actual) y sus pasos.</summary>
public sealed record FormatPath(char Root, IReadOnlyList<FormatPathStep> Steps);

/// <summary>Un paso de una ruta: un nombre (<c>.campo</c>), un índice (<c>[0]</c>) o todos (<c>[*]</c>).</summary>
public sealed record FormatPathStep(string? Name, int? Index, bool All);

/// <summary>Resultado de convertir un archivo estructurado a la tabla que lee la plantilla de importación.</summary>
public sealed record FormatTable(IReadOnlyList<string> Header, IReadOnlyList<IReadOnlyList<string>> Rows);

/// <summary>
/// Lote 137 (P11) — reglas puras del mapeo por ruta de archivos JSON y XML (y del formato de archivo en general). Una ruta tiene la forma <c>$.a.b[0].c</c>: <c>$</c> es el documento, <c>[*]</c> todos los
/// elementos de una lista, <c>[n]</c> uno. En XML los atributos se escriben <c>@atributo</c> y el texto de un elemento con atributos <c>#text</c>; un elemento que se repite es una lista y uno solo, un objeto
/// (<c>[*]</c> sobre un objeto lo toma a él mismo, así la misma ruta sirve con uno o con varios). Con LinesPath, cada línea es una fila y las columnas pueden leer de la línea (<c>@.cantidad</c>), de su orden
/// (<c>^.referencia</c>) o del documento (<c>$.cliente</c>). Cada orden sin líneas da una fila con las columnas de línea vacías. Entrada no confiable: el XML no admite DTD ni entidades externas, el JSON tiene
/// profundidad máxima y todo respeta los límites del CSV (2 MB, 5.000 filas).
/// </summary>
public static class FileFormatRules
{
    public const int SheetMax = 31;
    public const int MaxColumns = 60;
    public const int HeaderMax = 80;
    public const int PathMax = 200;
    public const int MaxDepth = 64;

    public static string KindMessage(string kind) => $"Formato desconocido: {kind}. Use {string.Join(", ", FileFormatKinds.All)}.";
    public static string SheetMessage => $"El nombre de la hoja no puede pasar de {SheetMax} caracteres.";
    public const string RecordsRequiredMessage = "Indique la ruta de los envíos (por ejemplo $.orders[*]).";
    public const string ColumnsRequiredMessage = "Agregue al menos una columna al mapeo.";
    public static string ColumnsMaxMessage => $"El mapeo admite hasta {MaxColumns} columnas.";
    public const string HeaderRequiredMessage = "Cada columna necesita un nombre.";
    public static string HeaderMaxMessage => $"El nombre de una columna no puede pasar de {HeaderMax} caracteres.";
    public static string HeaderRepeatedMessage(string h) => $"Hay dos columnas llamadas «{h}».";
    public static string PathInvalidMessage(string p) => $"La ruta «{p}» no es válida. Use el formato $.campo.otro[*] (también @.campo o ^.campo).";
    public const string RecordsRootMessage = "La ruta de los envíos debe empezar con $ (por ejemplo $.orders[*]).";
    public const string LinesRootMessage = "La ruta de las líneas debe ser relativa al envío: empiece con @ (por ejemplo @.lines[*]).";
    public static string OrderRootMessage(string p) => $"La ruta «{p}» usa ^ (el envío), que solo se puede cuando hay una ruta de líneas.";
    public const string NotStructuredMessage = "Esta integración no tiene un formato JSON o XML con mapeo: configure «Formato del archivo».";
    public static string StructuredWithoutMappingMessage(string kind) => $"El archivo es {kind}, pero la integración no tiene el mapeo de rutas: configure «Formato del archivo».";
    public static string UnreadableMessage(string kind, string detail) => $"No se pudo leer el archivo {kind}: {detail}";
    public const string DtdMessage = "El archivo XML trae una declaración DTD, que no se admite.";
    public static string NoRecordsMessage(string path) => $"La ruta de los envíos «{path}» no encontró ningún registro en el archivo.";
    public const string TooManyRowsMessage = CsvParser.LimitMessage;
    public const string FormatNotForApiMessage = "Una integración de tipo API no tiene formato de archivo.";

    // ---------------------------------------------------------------- qué formato es

    /// <summary>Resuelve el formato de un archivo: el de la configuración, o (AUTO) el que dicen la extensión y la firma.</summary>
    public static string Resolve(FileFormatConfig? config, string fileName, ReadOnlySpan<byte> bytes)
    {
        var kind = (config?.Kind ?? FileFormatKinds.Auto).Trim().ToUpperInvariant();
        if (kind != FileFormatKinds.Auto) return kind;
        var ext = Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant();
        if (ext == ".xlsx" || (bytes.Length >= 4 && bytes[0] == 0x50 && bytes[1] == 0x4B && bytes[2] == 0x03 && bytes[3] == 0x04)) return FileFormatKinds.Excel;
        if (ext == ".json") return FileFormatKinds.Json;
        if (ext == ".xml") return FileFormatKinds.Xml;
        return FileFormatKinds.Csv;
    }

    /// <summary>Las extensiones que lee una integración de carpeta según su formato.</summary>
    public static IReadOnlyList<string> ExtensionsOf(FileFormatConfig? config) => (config?.Kind ?? FileFormatKinds.Auto).Trim().ToUpperInvariant() switch
    {
        FileFormatKinds.Excel => [".xlsx"],
        FileFormatKinds.Json => [".json"],
        FileFormatKinds.Xml => [".xml"],
        FileFormatKinds.Csv => [".csv", ".txt"],
        _ => [".csv", ".txt", ".xlsx", ".json", ".xml"],
    };

    // ---------------------------------------------------------------- validación

    /// <summary>Valida la configuración: devuelve campo → mensaje exacto (vacío = bien). Los campos son «format», «format.sheet», «format.recordsPath», «format.linesPath» y «format.columns».</summary>
    public static Dictionary<string, string> Validate(FileFormatConfig? c)
    {
        var errors = new Dictionary<string, string>();
        if (c is null) return errors;
        var kind = (c.Kind ?? FileFormatKinds.Auto).Trim().ToUpperInvariant();
        if (!FileFormatKinds.All.Contains(kind)) { errors["format"] = KindMessage(c.Kind ?? string.Empty); return errors; }
        if ((c.Sheet?.Trim().Length ?? 0) > SheetMax) errors["format.sheet"] = SheetMessage;
        var structured = kind is FileFormatKinds.Json or FileFormatKinds.Xml;
        var hasMapping = !string.IsNullOrWhiteSpace(c.RecordsPath) || !string.IsNullOrWhiteSpace(c.LinesPath) || (c.Columns?.Count ?? 0) > 0;
        if (!structured && !(kind == FileFormatKinds.Auto && hasMapping)) return errors;   // CSV y Excel no llevan mapeo (en AUTO solo se valida si lo trae)

        var records = c.RecordsPath?.Trim();
        if (string.IsNullOrEmpty(records)) errors["format.recordsPath"] = RecordsRequiredMessage;
        else if (records.Length > PathMax || ParsePath(records) is null) errors["format.recordsPath"] = PathInvalidMessage(records);
        else if (records[0] != '$') errors["format.recordsPath"] = RecordsRootMessage;

        var lines = c.LinesPath?.Trim();
        if (!string.IsNullOrEmpty(lines))
        {
            if (lines.Length > PathMax || ParsePath(lines) is null) errors["format.linesPath"] = PathInvalidMessage(lines);
            else if (lines[0] != '@') errors["format.linesPath"] = LinesRootMessage;
        }

        var columns = c.Columns ?? [];
        if (columns.Count == 0) errors["format.columns"] = ColumnsRequiredMessage;
        else if (columns.Count > MaxColumns) errors["format.columns"] = ColumnsMaxMessage;
        else
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var col in columns)
            {
                var header = col.Header?.Trim() ?? string.Empty;
                var path = col.Path?.Trim() ?? string.Empty;
                if (header.Length == 0) { errors["format.columns"] = HeaderRequiredMessage; break; }
                if (header.Length > HeaderMax) { errors["format.columns"] = HeaderMaxMessage; break; }
                if (!seen.Add(header)) { errors["format.columns"] = HeaderRepeatedMessage(header); break; }
                if (path.Length > PathMax || ParsePath(path) is null) { errors["format.columns"] = PathInvalidMessage(path); break; }
                if (path[0] == '^' && string.IsNullOrEmpty(lines)) { errors["format.columns"] = OrderRootMessage(path); break; }
            }
        }
        return errors;
    }

    // ---------------------------------------------------------------- rutas

    /// <summary>Lee una ruta; null si no es válida.</summary>
    public static FormatPath? ParsePath(string? path)
    {
        var p = path?.Trim();
        if (string.IsNullOrEmpty(p) || p[0] is not ('$' or '@' or '^')) return null;
        var steps = new List<FormatPathStep>();
        var i = 1;
        while (i < p.Length)
        {
            if (p[i] == '.')
            {
                var start = ++i;
                while (i < p.Length && IsNameChar(p[i])) i++;
                if (i == start) return null;
                steps.Add(new FormatPathStep(p[start..i], null, false));
            }
            else if (p[i] == '[')
            {
                var end = p.IndexOf(']', i);
                if (end < 0) return null;
                var inner = p[(i + 1)..end];
                if (inner == "*") steps.Add(new FormatPathStep(null, null, true));
                else if (inner.Length is > 0 and <= 6 && inner.All(char.IsAsciiDigit)) steps.Add(new FormatPathStep(null, int.Parse(inner, CultureInfo.InvariantCulture), false));
                else return null;
                i = end + 1;
            }
            else return null;
        }
        return new FormatPath(p[0], steps);
    }

    private static bool IsNameChar(char c) => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or ':' or '@' or '#';

    /// <summary>Evalúa los pasos de una ruta desde un nodo: todos los nodos a los que llega (vacío si no hay).</summary>
    public static List<JsonNode?> Evaluate(JsonNode? start, FormatPath path)
    {
        var current = new List<JsonNode?> { start };
        foreach (var step in path.Steps)
        {
            var next = new List<JsonNode?>();
            foreach (var node in current)
            {
                if (step.Name is not null)
                {
                    if (node is JsonObject o && o.TryGetPropertyValue(step.Name, out var v)) next.Add(v);
                }
                else if (step.All)
                {
                    if (node is JsonArray a) next.AddRange(a);
                    else if (node is not null) next.Add(node);   // un solo elemento XML/JSON se trata como lista de uno
                }
                else if (step.Index is int n)
                {
                    if (node is JsonArray a) { if (n < a.Count) next.Add(a[n]); }
                    else if (n == 0 && node is not null) next.Add(node);
                }
            }
            current = next;
        }
        return current;
    }

    /// <summary>El texto de un valor: cadena tal cual, número y booleano en su forma invariante; nulo, objeto o lista dan vacío.</summary>
    public static string ScalarText(JsonNode? node) => node switch
    {
        null => string.Empty,
        JsonValue v when v.TryGetValue<string>(out var s) => s,
        JsonValue v => v.ToJsonString().Trim('"'),
        _ => string.Empty,
    };

    // ---------------------------------------------------------------- JSON y XML → tabla

    /// <summary>Convierte un archivo JSON o XML a la tabla (encabezado y filas) que lee la plantilla de importación. Devuelve el mensaje exacto del problema en <paramref name="error"/>.</summary>
    public static FormatTable? ToTable(byte[] bytes, string kind, FileFormatConfig config, out string? error, int maxRows = CsvParser.MaxRows)
    {
        error = null;
        var upper = kind.ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(config.RecordsPath) && string.IsNullOrWhiteSpace(config.LinesPath) && (config.Columns?.Count ?? 0) == 0) { error = StructuredWithoutMappingMessage(upper); return null; }
        var problems = Validate(config with { Kind = upper });
        if (problems.Count > 0) { error = problems.Values.First(); return null; }
        if (bytes.Length > CsvParser.MaxBytes) { error = CsvParser.LimitMessage; return null; }

        JsonNode? root;
        try { root = upper == FileFormatKinds.Json ? ParseJson(bytes) : ParseXml(bytes); }
        catch (XmlException ex) when (ex.Message.Contains("DTD", StringComparison.OrdinalIgnoreCase)) { error = DtdMessage; return null; }
        catch (Exception ex) when (ex is JsonException or XmlException or InvalidOperationException) { error = UnreadableMessage(upper, FirstLine(ex.Message)); return null; }

        var recordsPath = ParsePath(config.RecordsPath)!;
        var linesPath = string.IsNullOrWhiteSpace(config.LinesPath) ? null : ParsePath(config.LinesPath);
        var columns = config.Columns!.Select(c => (Header: c.Header.Trim(), Path: ParsePath(c.Path)!)).ToList();
        var orders = Evaluate(root, recordsPath).Where(n => n is not null).ToList();
        if (orders.Count == 0) { error = NoRecordsMessage(config.RecordsPath!.Trim()); return null; }

        var rows = new List<IReadOnlyList<string>>();
        foreach (var order in orders)
        {
            var lines = linesPath is null ? [null] : Evaluate(order, linesPath);
            if (lines.Count == 0) lines = [null];
            foreach (var line in lines)
            {
                if (rows.Count >= maxRows) { error = TooManyRowsMessage; return null; }
                rows.Add(columns.Select(c => ScalarText(Evaluate(StartOf(c.Path.Root, root, order, line, linesPath is not null), c.Path).FirstOrDefault())).ToList());
            }
        }
        return new FormatTable(columns.Select(c => c.Header).ToList(), rows);
    }

    private static JsonNode? StartOf(char rootSymbol, JsonNode? root, JsonNode? order, JsonNode? line, bool hasLines)
        => rootSymbol switch { '$' => root, '^' => order, _ => hasLines ? line : order };

    private static JsonNode? ParseJson(byte[] bytes)
        => JsonNode.Parse(bytes, new JsonNodeOptions { PropertyNameCaseInsensitive = false }, new JsonDocumentOptions { MaxDepth = MaxDepth, AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });

    private static JsonNode? ParseXml(byte[] bytes)
    {
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersFromEntities = 0, MaxCharactersInDocument = CsvParser.MaxBytes * 4L, IgnoreComments = true, IgnoreProcessingInstructions = true };
        using var stream = new MemoryStream(bytes);
        using var reader = XmlReader.Create(stream, settings);
        var doc = XDocument.Load(reader);
        return new JsonObject { [doc.Root!.Name.LocalName] = ElementToNode(doc.Root, 0) };
    }

    private static JsonNode? ElementToNode(XElement element, int depth)
    {
        if (depth > MaxDepth) throw new InvalidOperationException("El XML tiene demasiados niveles.");
        var attributes = element.Attributes().Where(a => !a.IsNamespaceDeclaration).ToList();
        var children = element.Elements().ToList();
        var text = string.Concat(element.Nodes().OfType<XText>().Select(t => t.Value)).Trim();
        if (attributes.Count == 0 && children.Count == 0) return JsonValue.Create(text);
        var obj = new JsonObject();
        foreach (var a in attributes) obj["@" + a.Name.LocalName] = JsonValue.Create(a.Value);
        foreach (var group in children.GroupBy(c => c.Name.LocalName))
        {
            var items = group.ToList();
            obj[group.Key] = items.Count == 1 ? ElementToNode(items[0], depth + 1) : new JsonArray(items.Select(i => ElementToNode(i, depth + 1)).ToArray());
        }
        if (text.Length > 0) obj["#text"] = JsonValue.Create(text);
        return obj;
    }

    private static string FirstLine(string message) => message.Split('\n')[0].Trim();

    // ---------------------------------------------------------------- tabla → CSV

    /// <summary>Arma el texto CSV que lee el importador: encabezado (si la plantilla lo trae) y filas, con comillas donde hace falta.</summary>
    public static string ToCsv(IReadOnlyList<string>? header, IEnumerable<IReadOnlyList<string>> rows, char delimiter)
    {
        var sb = new StringBuilder();
        if (header is not null) sb.Append(string.Join(delimiter, header.Select(h => Quote(h, delimiter)))).Append('\n');
        foreach (var r in rows) sb.Append(string.Join(delimiter, r.Select(c => Quote(c, delimiter)))).Append('\n');
        return sb.ToString();
    }

    private static string Quote(string value, char delimiter)
        => value.IndexOfAny([delimiter, '"', '\r', '\n']) >= 0 ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
}
