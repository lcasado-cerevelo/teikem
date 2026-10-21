using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using Teikem.Domain.Constants;

namespace Teikem.Domain.Integrations;

/// <summary>Formato del archivo (o cuerpo) que arma una integración de salida (Lote 137b).</summary>
public static class OutputFormats
{
    public const string Csv = "CSV";
    public const string Json = "JSON";
    public const string Xml = "XML";
    public static readonly IReadOnlyList<string> All = [Csv, Json, Xml];
}

/// <summary>Cuándo sale: al instante (cada orden que llega a uno de los estatus) o por horario (todas las que llegaron desde la última salida, en un solo envío).</summary>
public static class OutputTriggers
{
    public const string Event = "EVENT";
    public const string Schedule = "SCHEDULE";
    public static readonly IReadOnlyList<string> All = [Event, Schedule];
}

/// <summary>Estatus de un envío de salida: PENDING (espera su primer intento o el siguiente), SENT, UNDELIVERED (agotó los reintentos: bandeja de salidas) y DISCARDED.</summary>
public static class OutputStatuses
{
    public const string Pending = "PENDING";
    public const string Sent = "SENT";
    public const string Undelivered = "UNDELIVERED";
    public const string Discarded = "DISCARDED";
    public static readonly IReadOnlyList<string> All = [Pending, Sent, Undelivered, Discarded];
}

/// <summary>Tipo del valor de un campo de salida: en JSON los números salen como números y lo demás como texto.</summary>
public enum OutputFieldKind { Text, Number, Date }

/// <summary>Un campo de la orden que se puede poner en la salida.</summary>
public sealed record OutputField(string Code, string LabelEs, string LabelEn, OutputFieldKind Kind);

/// <summary>Una columna de la salida: cómo se llama (encabezado, o campo en JSON/XML) y qué campo de Teikem lleva.</summary>
public sealed record OutputColumn(string Header, string Field);

/// <summary>Un encabezado HTTP adicional (no secreto) de la API saliente.</summary>
public sealed record HttpHeaderItem(string Name, string Value);

/// <summary>
/// API saliente genérica (Lote 137b): a qué URL se manda lo armado, con qué método y con qué autenticación. El secreto (token, contraseña o valor del encabezado) va aparte, cifrado, y no se vuelve a mostrar.
/// AuthKind: NONE, BEARER (el secreto es el token), BASIC (AuthName = usuario, el secreto es la contraseña) o HEADER (AuthName = nombre del encabezado, el secreto es su valor).
/// </summary>
public sealed record HttpOutConfig(string Url, string Method = "POST", string AuthKind = "NONE", string? AuthName = null, IReadOnlyList<HttpHeaderItem>? Headers = null);

/// <summary>El secreto de la API saliente (se guarda cifrado).</summary>
public sealed record HttpOutSecret(string? Value);

/// <summary>
/// Qué manda una integración de salida: las órdenes del cliente que llegan a uno de los <c>Statuses</c>, cuándo (Trigger), en qué formato (Format; Delimiter solo CSV), con qué columnas y, en SFTP/FTP, con qué nombre
/// de archivo (FileName admite {date}, {time}, {id} y {ext}). Http solo en el conector HTTP.
/// </summary>
public sealed record OutputConfig(
    IReadOnlyList<string> Statuses, string Trigger = OutputTriggers.Event, string Format = OutputFormats.Csv, string? Delimiter = ",", string? FileName = null,
    IReadOnlyList<OutputColumn>? Columns = null, HttpOutConfig? Http = null);

/// <summary>
/// Lote 137b (P11) — reglas puras de la SALIDA de órdenes: catálogo de campos, validación con mensajes exactos, armado del archivo (CSV, JSON o XML), nombre del archivo y reintentos con espera creciente
/// (los mismos 1 min, 5, 30, 2 h y 12 h de los webhooks). El contenido sale de las órdenes del cliente de la integración; nada de otros clientes.
/// </summary>
public static class OutputRules
{
    public const int MaxColumns = 40;
    public const int HeaderMax = 60;
    public const int FileNameMax = 120;
    public const int MaxHeaders = 10;
    public const int HeaderValueMax = 500;
    public const int MaxOrdersPerSend = 5000;
    public const int RetentionDays = 30;
    public const string DefaultFileName = "ordenes-{date}-{time}.{ext}";

    public static readonly IReadOnlyList<string> OrderStatusCodes =
        [OrderStatuses.Submitted, OrderStatuses.Pickup, OrderStatuses.Unassigned, OrderStatuses.Assigned, OrderStatuses.InTransit, OrderStatuses.Delivered, OrderStatuses.Failed, OrderStatuses.Cancelled, OrderStatuses.InWarehouse, OrderStatuses.Returned];

    public static readonly IReadOnlyList<OutputField> Fields =
    [
        new("orderNumber", "Número de envío", "Shipment number", OutputFieldKind.Text),
        new("waybillNumber", "Conduce", "Waybill", OutputFieldKind.Text),
        new("clientInvoiceNumber", "Factura del cliente", "Client invoice", OutputFieldKind.Text),
        new("status", "Estatus (código)", "Status (code)", OutputFieldKind.Text),
        new("statusLabel", "Estatus (nombre)", "Status (name)", OutputFieldKind.Text),
        new("createdAt", "Fecha de creación", "Created at", OutputFieldKind.Date),
        new("promisedDate", "Fecha prometida", "Promised date", OutputFieldKind.Date),
        new("pieces", "Piezas", "Pieces", OutputFieldKind.Number),
        new("weightKg", "Peso (kg)", "Weight (kg)", OutputFieldKind.Number),
        new("codAmount", "Monto COD", "COD amount", OutputFieldKind.Number),
        new("quotedAmount", "Flete cotizado", "Quoted freight", OutputFieldKind.Number),
        new("notes", "Notas", "Notes", OutputFieldKind.Text),
        new("consigneeName", "Consignatario", "Consignee", OutputFieldKind.Text),
        new("consigneeAddress", "Dirección de entrega", "Delivery address", OutputFieldKind.Text),
        new("consigneeCity", "Pueblo de entrega", "Delivery city", OutputFieldKind.Text),
        new("consigneePostalCode", "Código postal de entrega", "Delivery postal code", OutputFieldKind.Text),
        new("deliveredAt", "Fecha y hora de entrega", "Delivered at", OutputFieldKind.Date),
        new("recipientName", "Recibido por", "Received by", OutputFieldKind.Text),
        new("deliveryNotes", "Notas de la entrega", "Delivery notes", OutputFieldKind.Text),
    ];

    public static readonly IReadOnlyList<string> HttpMethods = ["POST", "PUT"];
    public static readonly IReadOnlyList<string> HttpAuthKinds = ["NONE", "BEARER", "BASIC", "HEADER"];
    private static readonly string[] ReservedHeaders = ["authorization", "host", "content-length", "content-type", "transfer-encoding", "connection", "cookie", "user-agent"];
    private static readonly Regex NameRegex = new("^[A-Za-z_][A-Za-z0-9_\\-]*$", RegexOptions.Compiled);
    private static readonly Regex HeaderNameRegex = new("^[A-Za-z0-9!#$%&'*+\\-.^_`|~]+$", RegexOptions.Compiled);
    private static readonly Regex PlaceholderRegex = new("\\{([^{}]*)\\}", RegexOptions.Compiled);

    public const string StatusesRequiredMessage = "Escoja al menos un estatus que dispare la salida.";
    public static string StatusUnknownMessage(string s) => $"Estatus desconocido: {s}.";
    public static string TriggerMessage(string t) => $"Disparador desconocido: {t}. Use {string.Join(" o ", OutputTriggers.All)}.";
    public static string FormatMessage(string f) => $"Formato de salida desconocido: {f}. Use CSV, JSON o XML.";
    public const string DelimiterMessage = "El delimitador debe ser coma, punto y coma o tabulador.";
    public static string FileNameMaxMessage => $"El nombre del archivo no puede pasar de {FileNameMax} caracteres.";
    public const string FileNameCharsMessage = "El nombre del archivo no puede llevar carpetas ni saltos de línea.";
    public static string PlaceholderMessage(string p) => $"Marcador desconocido en el nombre del archivo: {{{p}}}. Use {{date}}, {{time}}, {{id}} o {{ext}}.";
    public const string ColumnsRequiredMessage = "Agregue al menos una columna a la salida.";
    public static string ColumnsMaxMessage => $"La salida admite hasta {MaxColumns} columnas.";
    public const string HeaderRequiredMessage = "Cada columna necesita un nombre.";
    public static string HeaderRepeatedMessage(string h) => $"Hay dos columnas llamadas «{h}».";
    public static string HeaderNameMessage(string h) => $"El nombre de la columna «{h}» debe empezar con una letra o guion bajo y llevar solo letras, números, guion y guion bajo (para JSON y XML).";
    public static string HeaderMaxMessage => $"El nombre de una columna no puede pasar de {HeaderMax} caracteres.";
    public static string FieldUnknownMessage(string f) => $"Campo desconocido: {f}.";
    public const string HttpRequiredMessage = "Indique la configuración de la API (URL, método y autenticación).";
    public static string MethodMessage(string m) => $"Método desconocido: {m}. Use POST o PUT.";
    public static string HttpAuthMessage(string a) => $"Autenticación desconocida: {a}. Use NONE, BEARER, BASIC o HEADER.";
    public const string AuthNameRequiredMessage = "Indique el usuario (BASIC) o el nombre del encabezado (HEADER) de la autenticación.";
    public const string AuthNameInvalidMessage = "El nombre del encabezado de autenticación no es válido.";
    public const string AuthSecretRequiredMessage = "Indique el token, la contraseña o el valor del encabezado de la autenticación.";
    public static string HeadersMaxMessage => $"Se admiten hasta {MaxHeaders} encabezados adicionales.";
    public static string HeaderInvalidMessage(string h) => $"El encabezado «{h}» no es válido (nombre no permitido o valor con saltos de línea o de más de {HeaderValueMax} caracteres).";
    public const string ScheduleNeedsIntervalMessage = "La salida por horario necesita un intervalo (minutos).";
    public const string OutboundNeedsClientMessage = "Una integración de salida necesita el cliente cuyas envíos se mandan.";
    public const string OutboundConnectorMessage = "Una integración de salida usa SFTP, FTP o HTTP.";
    public const string OutputRequiredMessage = "Indique qué se manda (estatus, formato y columnas).";
    public const string NotOutboundMessage = "Esta integración no es de salida.";
    public const string SizeMessage = "La salida supera el tamaño permitido (2 MB); use una salida más frecuente o menos columnas.";
    public const string NothingToSendMessage = "No hay envíos pendientes de enviar.";
    public const string NotUndeliveredMessage = "Solo se puede reintentar un envío no entregado.";
    public const string AlreadyDiscardedMessage = "El envío ya fue descartado.";
    public const string NotDiscardableMessage = "El envío ya se entregó.";

    public static OutputField? FindField(string? code) => Fields.FirstOrDefault(f => string.Equals(f.Code, code?.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>Columnas por omisión de una salida nueva: lo que casi todos piden de una entrega.</summary>
    public static IReadOnlyList<OutputColumn> DefaultColumns =>
        [new("orderNumber", "orderNumber"), new("status", "status"), new("deliveredAt", "deliveredAt"), new("recipientName", "recipientName")];

    // ---------------------------------------------------------------- validación

    /// <summary>Valida la configuración de la salida: campo → mensaje exacto (vacío = bien). <paramref name="remote"/> = SFTP/FTP (nombre de archivo); <paramref name="http"/> = conector HTTP (API saliente).</summary>
    public static Dictionary<string, string> Validate(OutputConfig? c, bool remote, bool http, bool allowInsecureUrls, bool hasSecret)
    {
        var errors = new Dictionary<string, string>();
        if (c is null) { errors["output"] = OutputRequiredMessage; return errors; }
        var statuses = (c.Statuses ?? []).Select(s => (s ?? string.Empty).Trim().ToUpperInvariant()).Where(s => s.Length > 0).Distinct().ToList();
        if (statuses.Count == 0) errors["output.statuses"] = StatusesRequiredMessage;
        else if (statuses.FirstOrDefault(s => !OrderStatusCodes.Contains(s)) is string unknown) errors["output.statuses"] = StatusUnknownMessage(unknown);
        var trigger = (c.Trigger ?? string.Empty).Trim().ToUpperInvariant();
        if (!OutputTriggers.All.Contains(trigger)) errors["output.trigger"] = TriggerMessage(c.Trigger ?? string.Empty);
        var format = (c.Format ?? string.Empty).Trim().ToUpperInvariant();
        if (!OutputFormats.All.Contains(format)) errors["output.format"] = FormatMessage(c.Format ?? string.Empty);
        else if (format == OutputFormats.Csv && DelimiterOf(c.Delimiter) is null) errors["output.delimiter"] = DelimiterMessage;

        var fileName = c.FileName?.Trim();
        if (remote && !string.IsNullOrEmpty(fileName))
        {
            if (fileName.Length > FileNameMax) errors["output.fileName"] = FileNameMaxMessage;
            else if (fileName.IndexOfAny(['/', '\\', '\r', '\n', '\0']) >= 0 || fileName.Contains("..", StringComparison.Ordinal)) errors["output.fileName"] = FileNameCharsMessage;
            else if (PlaceholderRegex.Matches(fileName).Select(m => m.Groups[1].Value).FirstOrDefault(p => !Placeholders.Contains(p)) is string bad) errors["output.fileName"] = PlaceholderMessage(bad);
        }

        var columns = c.Columns ?? [];
        if (columns.Count == 0) errors["output.columns"] = ColumnsRequiredMessage;
        else if (columns.Count > MaxColumns) errors["output.columns"] = ColumnsMaxMessage;
        else
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var col in columns)
            {
                var header = col.Header?.Trim() ?? string.Empty;
                if (header.Length == 0) { errors["output.columns"] = HeaderRequiredMessage; break; }
                if (header.Length > HeaderMax) { errors["output.columns"] = HeaderMaxMessage; break; }
                if (!seen.Add(header)) { errors["output.columns"] = HeaderRepeatedMessage(header); break; }
                if (format is OutputFormats.Json or OutputFormats.Xml && !NameRegex.IsMatch(header)) { errors["output.columns"] = HeaderNameMessage(header); break; }
                if (FindField(col.Field) is null) { errors["output.columns"] = FieldUnknownMessage(col.Field ?? string.Empty); break; }
            }
        }

        if (http) ValidateHttp(c.Http, allowInsecureUrls, hasSecret, errors);
        return errors;
    }

    private static void ValidateHttp(HttpOutConfig? h, bool allowInsecure, bool hasSecret, Dictionary<string, string> errors)
    {
        if (h is null) { errors["output.http"] = HttpRequiredMessage; return; }
        var urlError = WebhookRules.ValidateUrl(h.Url, allowInsecure);
        if (urlError is not null) { errors["output.http.url"] = urlError; return; }
        var method = (h.Method ?? string.Empty).Trim().ToUpperInvariant();
        if (!HttpMethods.Contains(method)) { errors["output.http.method"] = MethodMessage(h.Method ?? string.Empty); return; }
        var auth = (h.AuthKind ?? "NONE").Trim().ToUpperInvariant();
        if (!HttpAuthKinds.Contains(auth)) { errors["output.http.authKind"] = HttpAuthMessage(h.AuthKind ?? string.Empty); return; }
        if (auth is "BASIC" or "HEADER" && string.IsNullOrWhiteSpace(h.AuthName)) { errors["output.http.authName"] = AuthNameRequiredMessage; return; }
        if (auth == "HEADER" && (!HeaderNameRegex.IsMatch(h.AuthName!.Trim()) || ReservedHeaders.Contains(h.AuthName.Trim().ToLowerInvariant()))) { errors["output.http.authName"] = AuthNameInvalidMessage; return; }
        if (auth != "NONE" && !hasSecret) { errors["output.http.secret"] = AuthSecretRequiredMessage; return; }
        var headers = h.Headers ?? [];
        if (headers.Count > MaxHeaders) { errors["output.http.headers"] = HeadersMaxMessage; return; }
        foreach (var header in headers)
        {
            var name = header.Name?.Trim() ?? string.Empty;
            var value = header.Value ?? string.Empty;
            if (!HeaderNameRegex.IsMatch(name) || ReservedHeaders.Contains(name.ToLowerInvariant()) || value.Length > HeaderValueMax || value.IndexOfAny(['\r', '\n']) >= 0) { errors["output.http.headers"] = HeaderInvalidMessage(name); return; }
        }
    }

    private static readonly string[] Placeholders = ["date", "time", "id", "ext"];

    /// <summary>El delimitador del CSV: coma, punto y coma o tabulador (acepta «\t» y «tab»); null si no es uno de ellos.</summary>
    public static char? DelimiterOf(string? delimiter)
    {
        var d = delimiter ?? ",";
        return d switch { "," => ',', ";" => ';', "\t" or "\\t" or "tab" or "TAB" => '\t', _ => null };
    }

    public static string ExtensionOf(string format) => format.ToUpperInvariant() switch { OutputFormats.Json => "json", OutputFormats.Xml => "xml", _ => "csv" };

    public static string ContentTypeOf(string format) => format.ToUpperInvariant() switch
    {
        OutputFormats.Json => "application/json",
        OutputFormats.Xml => "application/xml",
        _ => "text/csv; charset=utf-8",
    };

    /// <summary>El nombre del archivo: el patrón (o el de omisión) con {date} aaaammdd, {time} hhmmss, {id} el número del envío y {ext} la extensión del formato.</summary>
    public static string ExpandFileName(string? pattern, string format, DateTime nowUtc, long id)
    {
        var p = string.IsNullOrWhiteSpace(pattern) ? DefaultFileName : pattern.Trim();
        return PlaceholderRegex.Replace(p, m => m.Groups[1].Value switch
        {
            "date" => nowUtc.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
            "time" => nowUtc.ToString("HHmmss", CultureInfo.InvariantCulture),
            "id" => id.ToString(CultureInfo.InvariantCulture),
            "ext" => ExtensionOf(format),
            _ => m.Value,
        });
    }

    // ---------------------------------------------------------------- armar la salida

    /// <summary>Arma el contenido de la salida con las columnas configuradas. Cada orden es un diccionario campo → valor (texto, número, fecha o nulo).</summary>
    public static string Render(OutputConfig c, IReadOnlyList<IReadOnlyDictionary<string, object?>> orders)
    {
        var columns = (c.Columns ?? []).Select(col => (Header: col.Header.Trim(), Field: FindField(col.Field)!)).ToList();
        return (c.Format ?? OutputFormats.Csv).Trim().ToUpperInvariant() switch
        {
            OutputFormats.Json => RenderJson(columns, orders),
            OutputFormats.Xml => RenderXml(columns, orders),
            _ => RenderCsv(columns, orders, DelimiterOf(c.Delimiter) ?? ','),
        };
    }

    /// <summary>El texto invariante de un valor (para CSV y XML): fechas en ISO 8601 UTC, números sin cultura, nulo vacío.</summary>
    public static string TextOf(object? v) => v switch
    {
        null => string.Empty,
        DateTime d => DateTime.SpecifyKind(d, DateTimeKind.Utc).ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
        decimal m => m.ToString(CultureInfo.InvariantCulture),
        double x => x.ToString(CultureInfo.InvariantCulture),
        int i => i.ToString(CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => v.ToString() ?? string.Empty,
    };

    private static string RenderCsv(List<(string Header, OutputField Field)> columns, IReadOnlyList<IReadOnlyDictionary<string, object?>> orders, char delimiter)
    {
        var sb = new StringBuilder();
        sb.Append(string.Join(delimiter, columns.Select(c => Quote(c.Header, delimiter)))).Append("\r\n");
        foreach (var o in orders)
            sb.Append(string.Join(delimiter, columns.Select(c => Quote(TextOf(o.GetValueOrDefault(c.Field.Code)), delimiter)))).Append("\r\n");
        return sb.ToString();
    }

    private static string Quote(string value, char delimiter)
        => value.IndexOfAny([delimiter, '"', '\r', '\n']) >= 0 ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;

    private static string RenderJson(List<(string Header, OutputField Field)> columns, IReadOnlyList<IReadOnlyDictionary<string, object?>> orders)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartObject();
            w.WriteStartArray("orders");
            foreach (var o in orders)
            {
                w.WriteStartObject();
                foreach (var (header, field) in columns)
                {
                    var v = o.GetValueOrDefault(field.Code);
                    switch (v)
                    {
                        case null: w.WriteNull(header); break;
                        case decimal m: w.WriteNumber(header, m); break;
                        case int i: w.WriteNumber(header, i); break;
                        case double x: w.WriteNumber(header, x); break;
                        default: w.WriteString(header, TextOf(v)); break;
                    }
                }
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private static string RenderXml(List<(string Header, OutputField Field)> columns, IReadOnlyList<IReadOnlyDictionary<string, object?>> orders)
    {
        using var ms = new MemoryStream();
        using (var w = XmlWriter.Create(ms, new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = true, CloseOutput = false }))
        {
            w.WriteStartDocument();
            w.WriteStartElement("orders");
            foreach (var o in orders)
            {
                w.WriteStartElement("order");
                foreach (var (header, field) in columns) w.WriteElementString(header, XmlSafe(TextOf(o.GetValueOrDefault(field.Code))));
                w.WriteEndElement();
            }
            w.WriteEndElement();
            w.WriteEndDocument();
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    /// <summary>Quita los caracteres de control que XML 1.0 no admite (un texto con ellos haría fallar el armado).</summary>
    private static string XmlSafe(string s) => new(s.Where(ch => ch is '\t' or '\n' or '\r' || ch >= 0x20 && ch != 0xFFFE && ch != 0xFFFF).ToArray());

    // ---------------------------------------------------------------- reintentos (los mismos de los webhooks)

    public static DateTime? NextAttemptUtc(int attemptsMade, DateTime nowUtc) => WebhookRules.NextAttemptUtc(attemptsMade, nowUtc);
    public static int MaxAttempts => WebhookRules.MaxAttempts;
}
