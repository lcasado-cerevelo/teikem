namespace Teikem.Domain.Integrations;

/// <summary>Qué crea en el sistema un documento leído. ORDER = orden de transporte (el único destino de la primera etapa); los demás destinos se registran después sin cambiar este esquema.</summary>
public static class DocumentTargets
{
    public const string Order = "ORDER";
    /// <summary>2026-10-23: aviso de llegada (ASN, LLE-#####) de un cliente dueño del inventario: lo que llega al almacén.</summary>
    public const string ArrivalNotice = "ARRIVAL_NOTICE";
    /// <summary>2026-10-23: aviso de salida (SAL-#####) de un cliente dueño del inventario: lo que sale del almacén.</summary>
    public const string ExitNotice = "EXIT_NOTICE";
    /// <summary>2026-10-23: cierre de órdenes por documento (la hoja «Ruta completada» de CMS, la factura con entregas…): NO cambia nada solo; deja una PROPUESTA que una persona confirma (modo SUGGEST) o, si el lector es AUTO, la aplica.</summary>
    public const string OrderCompletion = "ORDER_COMPLETION";
    public static readonly IReadOnlyList<string> All = [Order, ArrivalNotice, ExitNotice, OrderCompletion];
}

/// <summary>Qué hace un lector de cierre con lo que encuentra: SUGGEST (por omisión) deja una propuesta por confirmar; AUTO la aplica en el acto (solo cuando la exactitud del lector ya está medida).</summary>
/// <summary>Qué compara un chequeo de completitud: LINE_COUNT = las líneas leídas contra el total de líneas que declara el documento; SUM = la suma de una columna leída contra el total que declara.</summary>
public static class ReadCheckKinds
{
    public const string LineCount = "LINE_COUNT";
    public const string Sum = "SUM";
    /// <summary>MATCH_COUNT: el documento no declara un número sino que LISTA lo que trae (la tabla de paradas de una hoja de ruta, un resumen de líneas): se cuentan las líneas del texto que coinciden con la expresión y se comparan con lo leído.</summary>
    public const string MatchCount = "MATCH_COUNT";
    public static readonly IReadOnlyList<string> All = [LineCount, Sum, MatchCount];
}

/// <summary>Sobre qué se hace un chequeo: RECORD (por omisión: el texto y las filas de cada registro, o del documento entero si no es por registro), HEADER (lo que viene antes del primer registro, contra todas las filas) o DOCUMENT (todo el texto contra todas las filas).</summary>
public static class ReadCheckScopes
{
    public const string Record = "RECORD";
    public const string Header = "HEADER";
    public const string Document = "DOCUMENT";
    public static readonly IReadOnlyList<string> All = [Record, Header, Document];
}

/// <summary>
/// Un chequeo de completitud (2026-10-23). Pattern = expresión regular (grupo 1 si lo hay) que encuentra en el TEXTO del documento el total que declara («CASES: (\d+)», «Total Amount USD\s+([\d,.]+)»). Position = la columna leída que se cuenta o suma;
/// Scope (RECORD | HEADER | DOCUMENT; null = RECORD): sobre qué texto y qué filas se compara (útil en lectores por registro: la tabla de paradas del encabezado contra todas las órdenes leídas). MATCH_COUNT cuenta las líneas del texto que coinciden con Pattern.
/// Distinct (LINE_COUNT y MATCH_COUNT): cuenta valores distintos de esa columna (un número de orden repetido en varias líneas cuenta una vez). Tolerance = diferencia aceptada (0 en líneas; 0,01 en sumas por omisión). Name = cómo se llama en el aviso.
/// </summary>
public sealed record ReadCheck(string Kind, string Pattern, int Position = 1, bool Distinct = false, decimal? Tolerance = null, string? Name = null, string? Scope = null);

/// <summary>
/// Chequeos de completitud (2026-10-23): lo que la confianza del OCR NO ve (una columna o una línea que el OCR dejó de leer no baja su porcentaje) se detecta comparando lo leído con los totales del propio documento.
/// Un total que no se encuentra también es una falla: lo normal es que el documento lo traiga, y que falte puede ser una página que no llegó.
/// </summary>
public static class ReadCheckRules
{
    public const int MaxChecks = 10;
    public static string UnknownKindMessage(string kind) => $"Chequeo desconocido: {kind}. Use LINE_COUNT, SUM o MATCH_COUNT.";
    public static string UnknownScopeMessage(string scope) => $"Ámbito de chequeo desconocido: {scope}. Use RECORD, HEADER o DOCUMENT.";
    public static string MatchCountFailed(string name, int listed, int read) => $"el documento lista {listed} elemento(s) de «{name}» y se leyeron {read}";
    public static string NothingListed(string name) => $"no se encontró en el documento lo que lista «{name}»";
    public static string PatternRequiredMessage => "El chequeo necesita la expresión que encuentra el total en el documento.";
    public static string PatternInvalidMessage(string pattern) => $"La expresión del chequeo no es válida: {pattern}.";
    public static string PositionInvalidMessage => "La columna del chequeo debe estar entre 1 y 500.";
    public static string ToleranceInvalidMessage => "La tolerancia del chequeo no puede ser negativa.";
    public static string TooManyMessage => $"Máximo {MaxChecks} chequeos por lector.";
    public static string TotalNotFound(string name) => $"no se encontró el total «{name}» en el documento";
    public static string LineCountFailed(string name, decimal declared, int read) => $"el documento declara {declared:0.##} línea(s) de «{name}» y se leyeron {read}";
    public static string SumFailed(string name, decimal declared, decimal read) => $"la suma leída de «{name}» es {read:0.##} y el documento declara {declared:0.##}";
    public static string DoubtfulMessage(IEnumerable<string> failures) => $"Lectura dudosa: {string.Join("; ", failures)}. Verifique lo creado contra el documento.";

    public static IReadOnlyDictionary<string, string> Validate(IReadOnlyList<ReadCheck>? checks, string key)
    {
        var errors = new Dictionary<string, string>(StringComparer.Ordinal);
        if (checks is null || checks.Count == 0) return errors;
        if (checks.Count > MaxChecks) { errors[key] = TooManyMessage; return errors; }
        for (var i = 0; i < checks.Count; i++)
        {
            var c = checks[i];
            var k = $"{key}[{i}]";
            if (!ReadCheckKinds.All.Contains((c.Kind ?? string.Empty).Trim().ToUpperInvariant())) errors[$"{k}.kind"] = UnknownKindMessage(c.Kind ?? string.Empty);
            if (string.IsNullOrWhiteSpace(c.Pattern)) errors[$"{k}.pattern"] = PatternRequiredMessage;
            else if (!EmailRules.IsValidRegex(c.Pattern)) errors[$"{k}.pattern"] = PatternInvalidMessage(c.Pattern);
            if (!string.IsNullOrWhiteSpace(c.Scope) && !ReadCheckScopes.All.Contains(c.Scope.Trim().ToUpperInvariant())) errors[$"{k}.scope"] = UnknownScopeMessage(c.Scope);
            if (c.Position is < 1 or > 500) errors[$"{k}.position"] = PositionInvalidMessage;
            if (c.Tolerance is < 0) errors[$"{k}.tolerance"] = ToleranceInvalidMessage;
        }
        return errors;
    }

    /// <summary>Las fallas de los chequeos de un documento (o de un registro): <paramref name="text"/> es su texto y <paramref name="rows"/> sus filas leídas por posición de columna. Vacío = todo cuadra.</summary>
    public static IReadOnlyList<string> Evaluate(IReadOnlyList<ReadCheck>? checks, string text, IReadOnlyList<IReadOnlyDictionary<int, string>> rows)
    {
        var failures = new List<string>();
        foreach (var c in checks ?? Array.Empty<ReadCheck>())
        {
            var name = string.IsNullOrWhiteSpace(c.Name) ? c.Pattern : c.Name!.Trim();
            var kind = (c.Kind ?? string.Empty).Trim().ToUpperInvariant();
            if (kind == ReadCheckKinds.MatchCount)
            {
                var listed = CountMatches(text, c.Pattern);
                if (listed == 0) { failures.Add(NothingListed(name)); continue; }
                var values = rows.Select(r => r.TryGetValue(c.Position, out var v) ? v.Trim() : string.Empty).Where(v => v.Length > 0).ToList();
                var read = c.Distinct ? values.Distinct(StringComparer.OrdinalIgnoreCase).Count() : values.Count;
                if (Math.Abs(listed - read) > (c.Tolerance ?? 0m)) failures.Add(MatchCountFailed(name, listed, read));
                continue;
            }
            var declared = FindTotal(text, c.Pattern);
            if (declared is null) { failures.Add(TotalNotFound(name)); continue; }
            if (kind == ReadCheckKinds.LineCount)
            {
                var values = rows.Select(r => r.TryGetValue(c.Position, out var v) ? v.Trim() : string.Empty).Where(v => v.Length > 0).ToList();
                var read = c.Distinct ? values.Distinct(StringComparer.OrdinalIgnoreCase).Count() : values.Count;
                if (Math.Abs(declared.Value - read) > (c.Tolerance ?? 0m)) failures.Add(LineCountFailed(name, declared.Value, read));
            }
            else if (kind == ReadCheckKinds.Sum)
            {
                var sum = rows.Select(r => r.TryGetValue(c.Position, out var v) ? ParseNumber(v) : null).Sum(n => n ?? 0m);
                if (Math.Abs(declared.Value - sum) > (c.Tolerance ?? 0.01m)) failures.Add(SumFailed(name, declared.Value, sum));
            }
        }
        return failures;
    }

    /// <summary>Cuántas líneas del texto coinciden con la expresión (por línea, sin importar mayúsculas); 0 si la expresión no es válida.</summary>
    public static int CountMatches(string text, string pattern)
    {
        try { return System.Text.RegularExpressions.Regex.Matches(text ?? string.Empty, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Multiline | System.Text.RegularExpressions.RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250)).Count; }
        catch (Exception ex) when (ex is ArgumentException or System.Text.RegularExpressions.RegexMatchTimeoutException) { return 0; }
    }

    /// <summary>El número que declara el documento: el grupo 1 de la expresión (o todo lo que coincide); la última coincidencia, porque un total va al final y un encabezado puede repetirlo.</summary>
    public static decimal? FindTotal(string text, string pattern)
    {
        try
        {
            var matches = System.Text.RegularExpressions.Regex.Matches(text ?? string.Empty, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Multiline | System.Text.RegularExpressions.RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250));
            for (var i = matches.Count - 1; i >= 0; i--)
            {
                var m = matches[i];
                var raw = m.Groups.Count > 1 && m.Groups[1].Success ? m.Groups[1].Value : m.Value;
                if (ParseNumber(raw) is decimal n) return n;
            }
            return null;
        }
        catch (Exception ex) when (ex is ArgumentException or System.Text.RegularExpressions.RegexMatchTimeoutException) { return null; }
    }

    /// <summary>«1,430», «$2,845.15»: coma de miles y punto decimal (los documentos de Puerto Rico); lo demás que no es dígito, punto o signo se ignora.</summary>
    public static decimal? ParseNumber(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var cleaned = System.Text.RegularExpressions.Regex.Replace(raw, @"[^\d.\-]", string.Empty);
        return decimal.TryParse(cleaned, System.Globalization.NumberStyles.Number | System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : null;
    }
}

public static class ReaderModes
{
    public const string Suggest = "SUGGEST";
    public const string Auto = "AUTO";
    public static readonly IReadOnlyList<string> All = [Suggest, Auto];
}

/// <summary>A qué estatus lleva un cierre: la recogida hecha (PICKUP) o la entrega hecha (DELIVERED).</summary>
public static class CompletionTargets
{
    public const string Pickup = "PICKUP";
    public const string Delivered = "DELIVERED";
    public static readonly IReadOnlyList<string> All = [Pickup, Delivered];
}

/// <summary>
/// Las columnas (posición de la columna = «posición» del campo a leer) que esperan los destinos de aviso de llegada y de salida. Un renglón = una línea del aviso; los renglones con la misma referencia, destino y fecha
/// forman UN aviso. Referencia (1), Producto = SKU del cliente (2) y Cantidad (3) son obligatorios; Destino (4, solo salida), Fecha (5: la requerida en salida, la esperada en llegada) y Lote (6, solo llegada) son opcionales.
/// </summary>
public static class NoticeColumns
{
    public const int Reference = 1;
    public const int Sku = 2;
    public const int Quantity = 3;
    public const int Destination = 4;
    public const int Date = 5;
    public const int Lot = 6;
}

/// <summary>De dónde sale lo que lee un lector: el cuerpo del correo (BODY), un PDF adjunto (PDF) o un archivo adjunto de datos, CSV/Excel/JSON/XML (FILE).</summary>
public static class ReaderSources
{
    public const string Body = "BODY";
    public const string Pdf = "PDF";
    public const string File = "FILE";
    public static readonly IReadOnlyList<string> All = [Body, Pdf, File];
}

/// <summary>Sobre qué se evalúa una condición: el nombre del adjunto, el asunto del correo o el texto del documento (cuerpo o PDF leído).</summary>
public static class ReaderConditionScopes
{
    public const string AttachmentName = "ATTACHMENT_NAME";
    public const string Subject = "SUBJECT";
    public const string Text = "TEXT";
    public static readonly IReadOnlyList<string> All = [AttachmentName, Subject, Text];
}

/// <summary>Una condición de un lector: sobre qué (Scope) y cómo se compara (los mismos operadores del asunto y del nombre de adjunto).</summary>
public sealed record ReaderCondition(string Scope, string Operator, string Value);

/// <summary>
/// Un LECTOR (2026-10-22): qué hace una integración de correo con cada documento que llega. Se aplica si TODAS sus condiciones se cumplen (sin condiciones: a todo lo de su fuente); lee con sus campos
/// (cuerpo y PDF: los «campos a leer»; archivo de datos: la plantilla) y manda lo leído a su DESTINO. Plantilla y cliente propios (si no, los de la integración): un mismo buzón puede traer documentos de
/// varios clientes y de varios tipos, y cada uno va por su camino. FirstOnly: solo el primer documento que lo cumpla.
/// Checks (2026-10-23): chequeos de COMPLETITUD contra los totales que el propio documento declara (cuántas líneas dice que trae, a cuánto suman); si no cuadran, la lectura es dudosa (ver <see cref="ReadCheckRules"/>).
/// MinConfidence (2026-10-23): la confianza mínima del OCR (0–100) de los números del documento; por debajo, la lectura es DUDOSA: se crea igual, se avisa que la verifiquen y un cierre queda por confirmar (null = 80; 0 = no se vigila).
/// WarehousePublicId (2026-10-23): el almacén de los avisos de llegada y de salida de este lector (sin él se usa el único activo de la compañía; con varios, el aviso no se crea).
/// RecordStart (2026-10-23): un documento con VARIAS órdenes (una por página o grupo de páginas, como la hoja de ruta de CMS) se parte en registros en cada línea que contiene este texto; cada registro se lee con los campos
/// del lector, y los campos «FromHeader» se leen una sola vez de lo que viene antes del primer registro (ruta, chofer, fecha del documento).
/// </summary>
public sealed record DocumentReader(
    string Name, string Source, string Target = DocumentTargets.Order,
    IReadOnlyList<ReaderCondition>? When = null, IReadOnlyList<EmailBodyField>? Fields = null,
    Guid? TemplatePublicId = null, Guid? ClientPublicId = null, bool FirstOnly = false, string? RecordStart = null, string Mode = ReaderModes.Suggest, string? ToStatus = null, Guid? WarehousePublicId = null, int? MinConfidence = null, IReadOnlyList<ReadCheck>? Checks = null);

/// <summary>Un documento que llegó y que los lectores evalúan: su fuente, su nombre (adjunto), el asunto del correo y su texto (si es cuerpo o PDF).</summary>
public sealed record ReaderInput(string Source, string Name, string Subject, string? Text);

public static class DocumentReaderRules
{
    public const int MaxReaders = 20;
    public const int MaxConditions = 10;
    public const int MaxNameLength = 80;

    public static string ReadersMessage => $"Máximo {MaxReaders} lectores.";
    public static string NameRequiredMessage => "Indique el nombre del lector.";
    public static string NameTooLongMessage => $"El nombre del lector admite hasta {MaxNameLength} caracteres.";
    public static string DuplicateNameMessage(string name) => $"Ya hay un lector llamado '{name}'.";
    public static string UnknownSourceMessage(string source) => $"Fuente desconocida: {source}. Use BODY, PDF o FILE.";
    public static string UnknownTargetMessage(string target) => $"Destino desconocido: {target}.";
    public static string UnknownScopeMessage(string scope) => $"Condición sobre algo desconocido: {scope}. Use ATTACHMENT_NAME, SUBJECT o TEXT.";
    public static string ConditionsMessage => $"Máximo {MaxConditions} condiciones por lector.";
    public static string ScopeNotForSourceMessage(string scope, string source) => $"La condición {scope} no aplica a un lector de {source}.";
    public static string FieldsRequiredMessage => "Un lector de cuerpo o de PDF necesita al menos un campo a leer.";
    public static string NoReaderMatchedMessage => "Ningún lector de la integración aplica a este correo.";
    public static string UnknownModeMessage(string mode) => $"Modo desconocido: {mode}. Use SUGGEST o AUTO.";
    public static string UnknownCompletionMessage(string status) => $"Estatus de cierre desconocido: {status}. Use PICKUP o DELIVERED.";

    /// <summary>La condición se evalúa sobre algo que ese tipo de documento tiene (un cuerpo no tiene nombre de adjunto, un archivo de datos no tiene texto).</summary>
    public static bool ScopeApplies(string scope, string source) => scope.ToUpperInvariant() switch
    {
        ReaderConditionScopes.AttachmentName => source != ReaderSources.Body,
        ReaderConditionScopes.Text => source != ReaderSources.File,
        ReaderConditionScopes.Subject => true,
        _ => false,
    };

    public static IReadOnlyDictionary<string, string> Validate(IReadOnlyList<DocumentReader>? readers, string key = "email.readers")
    {
        var errors = new Dictionary<string, string>(StringComparer.Ordinal);
        if (readers is null || readers.Count == 0) return errors;
        if (readers.Count > MaxReaders) { errors[key] = ReadersMessage; return errors; }
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < readers.Count; i++)
        {
            var r = readers[i];
            var k = $"{key}[{i}]";
            if (string.IsNullOrWhiteSpace(r.Name)) errors[$"{k}.name"] = NameRequiredMessage;
            else if (r.Name.Trim().Length > MaxNameLength) errors[$"{k}.name"] = NameTooLongMessage;
            else if (!names.Add(r.Name.Trim())) errors[$"{k}.name"] = DuplicateNameMessage(r.Name.Trim());
            var source = (r.Source ?? string.Empty).Trim().ToUpperInvariant();
            if (!ReaderSources.All.Contains(source)) errors[$"{k}.source"] = UnknownSourceMessage(r.Source ?? string.Empty);
            if (!DocumentTargets.All.Contains((r.Target ?? string.Empty).Trim().ToUpperInvariant())) errors[$"{k}.target"] = UnknownTargetMessage(r.Target ?? string.Empty);
            if (!ReaderModes.All.Contains((r.Mode ?? string.Empty).Trim().ToUpperInvariant())) errors[$"{k}.mode"] = UnknownModeMessage(r.Mode ?? string.Empty);
            if (string.Equals(r.Target, DocumentTargets.OrderCompletion, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(r.ToStatus) && !CompletionTargets.All.Contains(r.ToStatus.Trim().ToUpperInvariant())) errors[$"{k}.toStatus"] = UnknownCompletionMessage(r.ToStatus);
            if (r.MinConfidence is < 0 or > 100) errors[$"{k}.minConfidence"] = ReadConfidenceRules.MinInvalidMessage;
            foreach (var (ek, ev) in ReadCheckRules.Validate(r.Checks, $"{k}.checks")) errors[ek] = ev;
            var when = r.When ?? Array.Empty<ReaderCondition>();
            if (when.Count > MaxConditions) errors[$"{k}.when"] = ConditionsMessage;
            for (var c = 0; c < when.Count; c++)
            {
                var cond = when[c];
                var scope = (cond.Scope ?? string.Empty).Trim().ToUpperInvariant();
                if (!ReaderConditionScopes.All.Contains(scope)) errors[$"{k}.when[{c}].scope"] = UnknownScopeMessage(cond.Scope ?? string.Empty);
                else if (ReaderSources.All.Contains(source) && !ScopeApplies(scope, source)) errors[$"{k}.when[{c}].scope"] = ScopeNotForSourceMessage(scope, source);
                EmailRules.ValidateMatch(new EmailTextMatch(cond.Operator ?? string.Empty, cond.Value ?? string.Empty), $"{k}.when[{c}]", errors);
            }
            var fields = r.Fields ?? Array.Empty<EmailBodyField>();
            if ((source is ReaderSources.Body or ReaderSources.Pdf) && fields.Count == 0) errors[$"{k}.fields"] = FieldsRequiredMessage;
            if (fields.Count > EmailRules.MaxBodyFields) errors[$"{k}.fields"] = EmailRules.TooManyFieldsMessage;
            EmailRules.ValidateBodyFields(fields, $"{k}.fields", errors);
        }
        return errors;
    }

    /// <summary>¿Aplica este lector a este documento? Misma fuente y todas las condiciones.</summary>
    public static bool Matches(DocumentReader reader, ReaderInput input)
    {
        if (!string.Equals(reader.Source, input.Source, StringComparison.OrdinalIgnoreCase)) return false;
        foreach (var c in reader.When ?? Array.Empty<ReaderCondition>())
        {
            var scope = (c.Scope ?? string.Empty).Trim().ToUpperInvariant();
            var subject = scope switch
            {
                ReaderConditionScopes.AttachmentName => input.Name,
                ReaderConditionScopes.Subject => input.Subject,
                ReaderConditionScopes.Text => input.Text,
                _ => null,
            };
            if (subject is null || !ScopeApplies(scope, input.Source)) return false;
            if (!EmailRules.TextMatches(subject, new EmailTextMatch(c.Operator ?? string.Empty, c.Value ?? string.Empty))) return false;
        }
        return true;
    }

    /// <summary>Los documentos de un correo → el lector que aplica a cada uno (el primero de la lista, en el orden configurado). Cada documento va a un solo lector; FirstOnly limita un lector a su primer documento.</summary>
    public static IReadOnlyList<(ReaderInput Input, DocumentReader Reader)> Assign(IReadOnlyList<DocumentReader> readers, IEnumerable<ReaderInput> inputs)
    {
        var result = new List<(ReaderInput, DocumentReader)>();
        var used = new HashSet<DocumentReader>();
        foreach (var input in inputs)
        {
            var reader = readers.FirstOrDefault(r => Matches(r, input) && !(r.FirstOnly && used.Contains(r)));
            if (reader is null) continue;
            used.Add(reader);
            result.Add((input, reader));
        }
        return result;
    }
}
