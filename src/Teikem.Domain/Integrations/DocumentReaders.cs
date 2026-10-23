namespace Teikem.Domain.Integrations;

/// <summary>Qué crea en el sistema un documento leído. ORDER = orden de transporte (el único destino de la primera etapa); los demás destinos se registran después sin cambiar este esquema.</summary>
public static class DocumentTargets
{
    public const string Order = "ORDER";
    public static readonly IReadOnlyList<string> All = [Order];
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
/// </summary>
public sealed record DocumentReader(
    string Name, string Source, string Target = DocumentTargets.Order,
    IReadOnlyList<ReaderCondition>? When = null, IReadOnlyList<EmailBodyField>? Fields = null,
    Guid? TemplatePublicId = null, Guid? ClientPublicId = null, bool FirstOnly = false);

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
