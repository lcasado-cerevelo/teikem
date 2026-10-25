using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Teikem.Domain.Catalogs;

namespace Teikem.Domain.Integrations;

/// <summary>Cómo se compara un texto (asunto, nombre de adjunto): igual, contiene, empieza, termina, comodín (*.csv) o expresión regular.</summary>
public static class EmailMatchOperators
{
    public const string EqualTo = "EQUALS";
    public const string Contains = "CONTAINS";
    public const string StartsWith = "STARTS";
    public const string EndsWith = "ENDS";
    public const string Wildcard = "WILDCARD";
    public const string Regex = "REGEX";
    public static readonly IReadOnlyList<string> All = [EqualTo, Contains, StartsWith, EndsWith, Wildcard, Regex];
}

/// <summary>2026-10-24: cómo se conecta Teikem al buzón. IMAP = servidor, usuario y contraseña (Gmail con contraseña de aplicación, cPanel…). MICROSOFT365 = aplicación de Entra ID (sin contraseña del buzón): Username es la dirección del buzón (también compartido), TenantId y ClientId identifican la aplicación y el secreto cifrado de la integración es el secreto de la aplicación.</summary>
public static class EmailProviders
{
    public const string Imap = "IMAP";
    public const string Microsoft365 = "MICROSOFT365";
    public static readonly IReadOnlyList<string> All = [Imap, Microsoft365];
    public static bool IsMicrosoft365(string? provider) => string.Equals(provider, Microsoft365, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// 2026-10-24 — dónde se anota que un correo ya se leyó. LOG (por omisión) = en el log de Teikem (<c>IntegrationMailLog</c>): el buzón no se toca (ni se marca leído ni se mueve), se leen TODOS los correos de la carpeta
/// (leídos o no) desde <c>ReadSince</c> y cada uno se atiende una sola vez. MAILBOX = como antes: solo los NO leídos, que se marcan como leídos y se mueven a la carpeta de procesados.
/// </summary>
public static class EmailMarkModes
{
    public const string Log = "LOG";
    public const string Mailbox = "MAILBOX";
    public static readonly IReadOnlyList<string> All = [Log, Mailbox];
    public static bool IsLog(string? mode) => !string.Equals(mode, Mailbox, StringComparison.OrdinalIgnoreCase);
    /// <summary>«yyyy-MM-dd» → el inicio de ese día en UTC; null si está vacío o no es una fecha.</summary>
    public static DateTime? SinceUtc(string? readSince) => DateTime.TryParseExact(readSince?.Trim(), "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out var d) ? d : null;
}

public static class EmailAttachmentModes
{
    public const string All = "ALL";
    public const string First = "FIRST";
    public const string ByName = "BY_NAME";
    public static readonly IReadOnlyList<string> Modes = [All, First, ByName];
}

public static class EmailBodyKinds
{
    /// <summary>«Reference #: 123» → el texto que sigue a la etiqueta en esa línea.</summary>
    public const string Label = "LABEL";
    public const string Regex = "REGEX";
    /// <summary>Un bloque de renglones (una tabla) entre un marcador de inicio y uno de fin: cada renglón es una línea del archivo.</summary>
    public const string Block = "BLOCK";
    public static readonly IReadOnlyList<string> All = [Label, Regex, Block];
}

public static class EmailTransforms
{
    public const string None = "NONE";
    public const string Upper = "UPPER";
    /// <summary>Solo los dígitos (teléfonos, códigos).</summary>
    public const string Digits = "DIGITS";
    /// <summary>Quita el símbolo de moneda y las comas de miles («$1,234.50» → «1234.50»).</summary>
    public const string Money = "MONEY";
    /// <summary>Junta los espacios y tabuladores seguidos en uno solo (un OCR separa en celdas «ERD ⇥ Pick Up ⇥ 1» lo que es «ERD Pick Up 1»).</summary>
    public const string Spaces = "SPACES";
    public static readonly IReadOnlyList<string> All = [None, Upper, Digits, Money, Spaces];
}

/// <summary>Un texto y cómo compararlo (asunto o nombre de adjunto).</summary>
public sealed record EmailTextMatch(string Operator, string Value);

/// <summary>Cuál columna del renglón de un bloque va a cuál posición de columna de la plantilla.</summary>
/// <summary>Divide (2026-10-23, solo con expresión por línea): la columna vale el grupo <c>Index</c> ÷ el grupo <c>Divide</c> (p. ej. importe ÷ precio unitario = cantidad, cuando la cantidad escrita a mano no se lee).</summary>
public sealed record EmailBlockColumn(int Position, int Index, int? Divide = null);

/// <summary>
/// Un campo a leer del cuerpo del correo. Position = la columna de la plantilla de importación (1…n) a la que va el valor: el cuerpo se convierte en una fila de archivo (o varias, con un bloque) y
/// la MISMA plantilla de la integración la lee. LABEL usa Label; REGEX usa Pattern (grupo 1 si lo hay); BLOCK usa Start, End, Separator y Columns.
/// BLOCK con LineRegex (2026-10-22): cada línea del bloque que coincida con la expresión da una fila y los grupos (1, 2, 3…) van a las columnas de la plantilla (Columns.Index = número de grupo); las líneas que no
/// coinciden (continuaciones de una descripción, rayas, totales) se ignoran. Repeat: el bloque se lee cada vez que aparece (un encabezado que se repite en cada página del PDF), no solo la primera.
/// FromHeader (2026-10-23, lectores por registro): el campo se lee UNA vez de lo que viene antes del primer registro y vale para todos. AppendWith: si la posición ya tiene un valor, este se agrega a continuación con ese texto en medio (varias partes en una nota).
/// </summary>
public sealed record EmailBodyField(
    int Position, string Kind, string? Label = null, string? Pattern = null, bool Required = false, string Transform = EmailTransforms.None,
    string? Start = null, string? End = null, string? Separator = null, IReadOnlyList<EmailBlockColumn>? Columns = null,
    string? LineRegex = null, bool Repeat = false, bool FromHeader = false, string? AppendWith = null);

/// <summary>Configuración NO secreta de un conector de correo (la contraseña va aparte, cifrada). ReadPdf (2026-10-22): los PDF adjuntos se convierten en texto (con OCR si son escaneados) y se leen con los MISMOS campos a leer del cuerpo (BodyFields).</summary>
public sealed record EmailConfig(
    string Host, int Port, bool UseSsl, string Username, string Mailbox = "INBOX", string ProcessedFolder = "Procesados",
    EmailTextMatch? Subject = null, IReadOnlyList<string>? AllowedSenders = null, bool RequireAttachment = false,
    string AttachmentMode = EmailAttachmentModes.All, EmailTextMatch? AttachmentName = null, IReadOnlyList<string>? AttachmentTypes = null,
    bool ReadBody = false, IReadOnlyList<EmailBodyField>? BodyFields = null,
    bool ReadPdf = false, IReadOnlyList<DocumentReader>? Readers = null,
    string Provider = EmailProviders.Imap, string? TenantId = null, string? ClientId = null,
    string MarkMode = EmailMarkModes.Log, string? ReadSince = null);

/// <summary>Un correo ya leído (de un buzón o de un .eml): lo que las reglas necesitan.</summary>
public sealed record MailAttachmentData(string Name, string ContentType, byte[] Content);
public sealed record MailMessageData(string MessageId, string? Uid, DateTime? DateUtc, string From, string? ReplyTo, IReadOnlyDictionary<string, string> Headers, string Subject, string TextBody, IReadOnlyList<MailAttachmentData> Attachments);

/// <summary>
/// Lote 133 (P11) — reglas puras de la integración por correo, para cualquier cliente: validación de la configuración, remitente original (los correos llegan reenviados), filtros de asunto, adjuntos por nombre y
/// tipo, y lectura del cuerpo por etiqueta, expresión regular o bloque de renglones. El contenido del correo es entrada NO confiable: nunca se ejecuta nada, el HTML se reduce a texto.
/// </summary>
public static class EmailRules
{
    public const int MaxBodyFields = 40;
    public const int MaxAllowedSenders = 50;
    public const int MaxPosition = 500;
    public static readonly IReadOnlyList<string> DefaultAttachmentTypes = [".csv", ".txt"];

    public static string HostRequiredMessage => "Indique el servidor del buzón (IMAP).";
    public static string PortMessage => "El puerto debe estar entre 1 y 65535.";
    public static string UsernameRequiredMessage => "Indique el usuario del buzón.";
    public static string PasswordRequiredMessage => "Indique la contraseña del buzón.";
    public static string UnknownProviderMessage(string p) => $"Tipo de buzón desconocido: {p}. Use IMAP o MICROSOFT365.";
    public static string TenantRequiredMessage => "Indique el Tenant ID de Microsoft 365 (el identificador del directorio o el dominio, p. ej. contoso.onmicrosoft.com).";
    public static string ClientIdRequiredMessage => "Indique el Client ID (identificador de la aplicación registrada en Microsoft Entra ID).";
    public static string ClientSecretRequiredMessage => "Indique el secreto de la aplicación (Client secret).";
    public static string UnknownMarkModeMessage(string m) => $"Forma de anotar los correos leídos desconocida: {m}. Use LOG o MAILBOX.";
    public static string ReadSinceMessage => "La fecha desde la que se leen los correos debe tener el formato AAAA-MM-DD.";
    public static string MailboxRequiredMessage => "Indique la carpeta del buzón que se lee (p. ej. INBOX).";
    public static string UnknownOperatorMessage(string op) => $"Condición desconocida: {op}. Use EQUALS, CONTAINS, STARTS, ENDS, WILDCARD o REGEX.";
    public static string MatchValueMessage => "Indique el texto de la condición.";
    public static string BadRegexMessage(string pattern) => $"La expresión regular '{pattern}' no es válida.";
    public static string UnknownModeMessage(string mode) => $"Modo de adjuntos desconocido: {mode}. Use ALL, FIRST o BY_NAME.";
    public static string NameFilterRequiredMessage => "Con «por nombre» indique la condición del nombre del adjunto.";
    public static string SendersMessage => $"Máximo {MaxAllowedSenders} remitentes permitidos.";
    public static string SenderFormatMessage(string s) => $"El remitente permitido '{s}' no es un correo ni un dominio válido.";
    public static string NoBodyFieldsMessage => "Con «leer el cuerpo» (o los PDF) indique al menos un campo a leer.";
    public static string NoPdfDataMessage(string name, string sample) => $"No se encontró ningún dato en el PDF {name}. Texto leído: «{sample}»";
    public static string PdfProblemMessage(string name, string problem) => $"PDF {name}: {problem}";
    public const string PdfExtension = ".pdf";
    public static string TooManyFieldsMessage => $"Máximo {MaxBodyFields} campos a leer.";
    public static string PositionMessage => $"La columna debe estar entre 1 y {MaxPosition}.";
    public static string UnknownKindMessage(string kind) => $"Tipo de campo desconocido: {kind}. Use LABEL, REGEX o BLOCK.";
    public static string LabelRequiredMessage => "Indique la etiqueta que se busca en el correo.";
    public static string PatternRequiredMessage => "Indique la expresión regular.";
    public static string BlockMarkersMessage => "Un bloque necesita el texto de inicio y el de fin (o una expresión regular por línea).";
    public static string RepeatNeedsStartMessage => "Para repetir el bloque indique el texto de inicio (el encabezado que se repite).";
    public static string BlockColumnsMessage => "Un bloque necesita al menos una columna (posición y número de columna del renglón).";
    public static string UnknownTransformMessage(string t) => $"Transformación desconocida: {t}. Use NONE, UPPER, DIGITS, MONEY o SPACES.";
    public static string NothingToReadMessage => "Marque al menos una cosa a leer: los adjuntos o el cuerpo del correo.";
    public static string SenderNotAllowedMessage(string sender) => $"Remitente no permitido: {sender}.";
    public static string NoAttachmentMessage => "El correo no trae ningún adjunto que cumpla el criterio.";
    public static string RequiredFieldMissingMessage(int position, string what) => $"No se encontró el campo obligatorio de la columna {position} ({what}).";
    public static string NoBodyDataMessage => "No se encontró ningún dato en el cuerpo del correo.";

    // ---------------------------------------------------------------- validación

    /// <summary>Errores (campo → mensaje) de la configuración; <paramref name="hasPassword"/> = ya hay contraseña guardada o viene una nueva.</summary>
    public static IReadOnlyDictionary<string, string> Validate(EmailConfig? c, bool hasPassword)
    {
        var errors = new Dictionary<string, string>(StringComparer.Ordinal);
        if (c is null) { errors["email"] = HostRequiredMessage; return errors; }
        var provider = string.IsNullOrWhiteSpace(c.Provider) ? EmailProviders.Imap : c.Provider.Trim().ToUpperInvariant();
        if (!EmailProviders.All.Contains(provider)) errors["email.provider"] = UnknownProviderMessage(c.Provider ?? string.Empty);
        if (provider == EmailProviders.Microsoft365)
        {
            if (string.IsNullOrWhiteSpace(c.TenantId)) errors["email.tenantId"] = TenantRequiredMessage;
            if (string.IsNullOrWhiteSpace(c.ClientId)) errors["email.clientId"] = ClientIdRequiredMessage;
            if (string.IsNullOrWhiteSpace(c.Username)) errors["email.username"] = UsernameRequiredMessage;
            if (!hasPassword) errors["email.password"] = ClientSecretRequiredMessage;
        }
        else
        {
            if (string.IsNullOrWhiteSpace(c.Host)) errors["email.host"] = HostRequiredMessage;
            if (c.Port is < 1 or > 65535) errors["email.port"] = PortMessage;
            if (string.IsNullOrWhiteSpace(c.Username)) errors["email.username"] = UsernameRequiredMessage;
            if (!hasPassword) errors["email.password"] = PasswordRequiredMessage;
        }
        if (string.IsNullOrWhiteSpace(c.Mailbox)) errors["email.mailbox"] = MailboxRequiredMessage;
        var markMode = string.IsNullOrWhiteSpace(c.MarkMode) ? EmailMarkModes.Log : c.MarkMode.Trim().ToUpperInvariant();
        if (!EmailMarkModes.All.Contains(markMode)) errors["email.markMode"] = UnknownMarkModeMessage(c.MarkMode ?? string.Empty);
        if (!string.IsNullOrWhiteSpace(c.ReadSince) && EmailMarkModes.SinceUtc(c.ReadSince) is null) errors["email.readSince"] = ReadSinceMessage;
        ValidateMatch(c.Subject, "email.subject", errors);
        var senders = c.AllowedSenders ?? Array.Empty<string>();
        if (senders.Count > MaxAllowedSenders) errors["email.allowedSenders"] = SendersMessage;
        foreach (var s in senders) if (!IsSenderPattern(s)) { errors["email.allowedSenders"] = SenderFormatMessage(s); break; }
        var mode = (c.AttachmentMode ?? EmailAttachmentModes.All).ToUpperInvariant();
        if (!EmailAttachmentModes.Modes.Contains(mode)) errors["email.attachmentMode"] = UnknownModeMessage(c.AttachmentMode ?? string.Empty);
        else if (mode == EmailAttachmentModes.ByName)
        {
            if (c.AttachmentName is null) errors["email.attachmentName"] = NameFilterRequiredMessage;
            else ValidateMatch(c.AttachmentName, "email.attachmentName", errors);
        }
        var fields = c.BodyFields ?? Array.Empty<EmailBodyField>();
        if ((c.ReadBody || c.ReadPdf) && fields.Count == 0) errors["email.bodyFields"] = NoBodyFieldsMessage;
        if (fields.Count > MaxBodyFields) errors["email.bodyFields"] = TooManyFieldsMessage;
        for (var i = 0; i < fields.Count; i++) ValidateField(fields[i], $"email.bodyFields[{i}]", errors);
        foreach (var kv in DocumentReaderRules.Validate(c.Readers)) errors[kv.Key] = kv.Value;
        return errors;
    }

    internal static void ValidateMatch(EmailTextMatch? m, string key, Dictionary<string, string> errors)
    {
        if (m is null) return;
        var op = m.Operator?.Trim().ToUpperInvariant() ?? string.Empty;
        if (!EmailMatchOperators.All.Contains(op)) { errors[key] = UnknownOperatorMessage(m.Operator ?? string.Empty); return; }
        if (string.IsNullOrWhiteSpace(m.Value)) { errors[key] = MatchValueMessage; return; }
        if (op == EmailMatchOperators.Regex && !IsValidRegex(m.Value)) errors[key] = BadRegexMessage(m.Value);
    }

    internal static void ValidateBodyFields(IReadOnlyList<EmailBodyField> fields, string key, Dictionary<string, string> errors)
    {
        for (var i = 0; i < fields.Count; i++) ValidateField(fields[i], $"{key}[{i}]", errors);
    }

    private static void ValidateField(EmailBodyField f, string key, Dictionary<string, string> errors)
    {
        if (f.Position is < 1 or > MaxPosition) errors[$"{key}.position"] = PositionMessage;
        var kind = f.Kind?.Trim().ToUpperInvariant() ?? string.Empty;
        if (!EmailBodyKinds.All.Contains(kind)) { errors[$"{key}.kind"] = UnknownKindMessage(f.Kind ?? string.Empty); return; }
        if (!EmailTransforms.All.Contains((f.Transform ?? EmailTransforms.None).ToUpperInvariant())) errors[$"{key}.transform"] = UnknownTransformMessage(f.Transform ?? string.Empty);
        switch (kind)
        {
            case EmailBodyKinds.Label when string.IsNullOrWhiteSpace(f.Label): errors[$"{key}.label"] = LabelRequiredMessage; break;
            case EmailBodyKinds.Regex:
                if (string.IsNullOrWhiteSpace(f.Pattern)) errors[$"{key}.pattern"] = PatternRequiredMessage;
                else if (!IsValidRegex(f.Pattern)) errors[$"{key}.pattern"] = BadRegexMessage(f.Pattern);
                break;
            case EmailBodyKinds.Block:
                if (!string.IsNullOrWhiteSpace(f.LineRegex))
                {
                    if (!IsValidRegex(f.LineRegex)) errors[$"{key}.lineRegex"] = BadRegexMessage(f.LineRegex);
                }
                else if (string.IsNullOrWhiteSpace(f.Start) || string.IsNullOrWhiteSpace(f.End)) errors[$"{key}.start"] = BlockMarkersMessage;
                if (f.Repeat && string.IsNullOrWhiteSpace(f.Start)) errors[$"{key}.start"] = RepeatNeedsStartMessage;
                if (f.Columns is null || f.Columns.Count == 0) errors[$"{key}.columns"] = BlockColumnsMessage;
                else if (f.Columns.Any(c => c.Position is < 1 or > MaxPosition || c.Index < 1)) errors[$"{key}.columns"] = PositionMessage;
                break;
        }
    }

    internal static bool IsValidRegex(string pattern)
    {
        try { _ = new Regex(pattern, RegexOptions.None, TimeSpan.FromMilliseconds(250)); return true; }
        catch (ArgumentException) { return false; }
    }

    /// <summary>Un remitente permitido es un correo completo (a@b.com), un dominio (b.com) o «@b.com».</summary>
    public static bool IsSenderPattern(string? s)
    {
        var v = s?.Trim();
        return !string.IsNullOrEmpty(v) && Regex.IsMatch(v, @"^(@?[A-Za-z0-9.-]+\.[A-Za-z]{2,}|[^@\s]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,})$");
    }

    // ---------------------------------------------------------------- remitente original

    private static readonly Regex EmailPattern = new(@"[A-Za-z0-9._%+'-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}", RegexOptions.Compiled);
    private static readonly Regex ForwardedFrom = new(@"^\s*(?:>+\s*)?(?:De|From|Remitente|Enviado por)\s*:\s*(.+)$", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Multiline);

    /// <summary>
    /// El remitente que cuenta. Como casi siempre el correo llega REENVIADO, se busca en este orden: X-Original-Sender, Resent-From, Reply-To, el «De:» del bloque reenviado del cuerpo y, si nada,
    /// el From. Devuelve el correo en minúsculas (o null si no hay ninguno).
    /// </summary>
    public static string? OriginalSender(MailMessageData m)
    {
        foreach (var header in new[] { "X-Original-Sender", "X-Original-From", "Resent-From", "Reply-To" })
            if (m.Headers.TryGetValue(header, out var v) && FirstEmail(v) is string h) return h;
        if (m.ReplyTo is not null && FirstEmail(m.ReplyTo) is string r) return r;
        // el bloque reenviado: la PRIMERA línea «De:/From:» del cuerpo (la del mensaje original)
        var forwarded = ForwardedFrom.Match(m.TextBody ?? string.Empty);
        if (forwarded.Success && FirstEmail(forwarded.Groups[1].Value) is string f) return f;
        return FirstEmail(m.From);
    }

    private static string? FirstEmail(string? text)
    {
        var match = EmailPattern.Match(WebUtility.HtmlDecode(text ?? string.Empty));
        return match.Success ? match.Value.ToLowerInvariant() : null;
    }

    /// <summary>¿El remitente está en la lista? Lista vacía = cualquiera. Cada entrada: correo completo, dominio (b.com) o «@b.com» (incluye subdominios).</summary>
    public static bool SenderAllowed(string? sender, IReadOnlyList<string>? allowed)
    {
        if (allowed is null || allowed.Count == 0) return true;
        if (string.IsNullOrWhiteSpace(sender)) return false;
        var s = sender.Trim().ToLowerInvariant();
        var domain = s[(s.LastIndexOf('@') + 1)..];
        foreach (var raw in allowed)
        {
            var a = raw.Trim().ToLowerInvariant().TrimStart('@');
            if (a.Contains('@')) { if (a == s) return true; }
            else if (domain == a || domain.EndsWith("." + a, StringComparison.Ordinal)) return true;
        }
        return false;
    }

    // ---------------------------------------------------------------- comparar texto

    public static bool TextMatches(string? text, EmailTextMatch? m)
    {
        if (m is null) return true;
        var t = text ?? string.Empty;
        var v = m.Value ?? string.Empty;
        return (m.Operator ?? string.Empty).Trim().ToUpperInvariant() switch
        {
            EmailMatchOperators.EqualTo => PostalLocalityRules.Normalize(t) == PostalLocalityRules.Normalize(v),
            EmailMatchOperators.Contains => PostalLocalityRules.Normalize(t).Contains(PostalLocalityRules.Normalize(v), StringComparison.Ordinal),
            EmailMatchOperators.StartsWith => PostalLocalityRules.Normalize(t).StartsWith(PostalLocalityRules.Normalize(v), StringComparison.Ordinal),
            EmailMatchOperators.EndsWith => PostalLocalityRules.Normalize(t).EndsWith(PostalLocalityRules.Normalize(v), StringComparison.Ordinal),
            EmailMatchOperators.Wildcard => Regex.IsMatch(t, "^" + Regex.Escape(v).Replace("\\*", ".*").Replace("\\?", ".") + "$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250)),
            EmailMatchOperators.Regex => SafeRegex(t, v),
            _ => false,
        };
    }

    private static bool SafeRegex(string text, string pattern)
    {
        try { return Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250)); }
        catch (Exception ex) when (ex is ArgumentException or RegexMatchTimeoutException) { return false; }
    }

    // ---------------------------------------------------------------- adjuntos

    /// <summary>Los adjuntos que se leen: por tipo (extensión permitida), y según el modo todos, el primero o los que cumplan la condición del nombre. El resto se ignora.</summary>
    public static IReadOnlyList<MailAttachmentData> SelectAttachments(IReadOnlyList<MailAttachmentData> attachments, EmailConfig c)
    {
        var types = (c.AttachmentTypes is { Count: > 0 } ? c.AttachmentTypes : DefaultAttachmentTypes).Select(t => t.Trim().ToLowerInvariant().StartsWith('.') ? t.Trim().ToLowerInvariant() : "." + t.Trim().ToLowerInvariant()).ToList();
        if (c.ReadPdf && !types.Contains(PdfExtension)) types.Add(PdfExtension);   // leer PDF implica tomar los .pdf adjuntos
        var byType = attachments.Where(a => !string.IsNullOrWhiteSpace(a.Name) && types.Contains(Path.GetExtension(a.Name).ToLowerInvariant())).ToList();
        return (c.AttachmentMode ?? EmailAttachmentModes.All).ToUpperInvariant() switch
        {
            EmailAttachmentModes.First => byType.Take(1).ToList(),
            EmailAttachmentModes.ByName => byType.Where(a => TextMatches(a.Name, c.AttachmentName)).ToList(),
            _ => byType,
        };
    }

    // ---------------------------------------------------------------- texto del correo

    /// <summary>Reduce el HTML a texto: quita scripts y estilos, convierte los saltos de bloque en renglones y decodifica las entidades. Nunca se ejecuta nada.</summary>
    public static string HtmlToText(string? html)
    {
        if (string.IsNullOrEmpty(html)) return string.Empty;
        var s = Regex.Replace(html, @"<(script|style)\b[^>]*>.*?</\1>", " ", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        s = Regex.Replace(s, @"<\s*br\s*/?>|</\s*(p|div|tr|li|h[1-6]|table)\s*>", "\n", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"</\s*t[dh]\s*>", "\t", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, "<[^>]+>", string.Empty);
        s = WebUtility.HtmlDecode(s).Replace(' ', ' ').Replace("\r\n", "\n").Replace('\r', '\n');
        s = Regex.Replace(s, @"[ \f\v]+", " ");
        s = Regex.Replace(s, @" *\n *", "\n");
        return Regex.Replace(s, @"\n{3,}", "\n\n").Trim();
    }

    // ---------------------------------------------------------------- cuerpo → filas

    /// <summary>Lo leído del cuerpo: valores sueltos por posición y, si hay un bloque, un renglón de valores por línea del archivo; Missing = los obligatorios que no se encontraron.</summary>
    public sealed record BodyReading(IReadOnlyDictionary<int, string> Single, IReadOnlyList<IReadOnlyDictionary<int, string>> Lines, IReadOnlyList<string> Missing)
    {
        public bool HasData => Single.Count > 0 || Lines.Count > 0;
    }

    public static BodyReading ReadBody(string? text, IReadOnlyList<EmailBodyField> fields)
    {
        var lines = (text ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var single = new Dictionary<int, string>();
        var block = new List<IReadOnlyDictionary<int, string>>();
        var missing = new List<string>();
        foreach (var f in fields)
        {
            var kind = (f.Kind ?? string.Empty).Trim().ToUpperInvariant();
            if (kind == EmailBodyKinds.Block)
            {
                var rows = ReadBlock(lines, f);
                if (rows.Count == 0 && f.Required) missing.Add(RequiredFieldMissingMessage(f.Position, f.Start ?? "bloque"));
                if (block.Count == 0) block.AddRange(rows);
                continue;
            }
            var value = kind == EmailBodyKinds.Label ? ReadLabel(lines, f.Label) : ReadRegex(text ?? string.Empty, f.Pattern);
            if (value is null) { if (f.Required) missing.Add(RequiredFieldMissingMessage(f.Position, f.Label ?? f.Pattern ?? kind)); continue; }
            var transformed = Transform(value, f.Transform);
            single[f.Position] = f.AppendWith is not null && single.TryGetValue(f.Position, out var before) ? before + f.AppendWith + transformed : transformed;
        }
        return new BodyReading(single, block, missing);
    }

    /// <summary>
    /// Lo leído de un documento con VARIOS registros (2026-10-23): cada registro es un BodyReading; Missing junta lo que faltó en cada uno («Registro 3: …»).
    /// </summary>
    public sealed record DocumentReading(IReadOnlyList<BodyReading> Parts, IReadOnlyList<string> Missing, IReadOnlyList<string>? Texts = null, string? HeaderText = null)
    {
        public bool HasData => Parts.Any(p => p.HasData);

        /// <summary>Las filas leídas de cada registro (valores sueltos repetidos + los de su línea), por posición de columna: lo que cada chequeo de completitud compara con el texto del registro.</summary>
        public IReadOnlyList<IReadOnlyDictionary<int, string>> RowsOf(int part)
        {
            var p = Parts[part];
            return p.Lines.Count > 0 ? p.Lines.Select(l => (IReadOnlyDictionary<int, string>)Merge(p.Single, l)).ToList() : new List<IReadOnlyDictionary<int, string>> { new Dictionary<int, string>(p.Single) };
        }
    }

    public static string RecordMissingMessage(int record, string detail) => $"Registro {record}: {detail}";
    public const string NoRecordsMessage = "No se encontró ningún registro en el documento (el texto de inicio de registro no aparece).";

    /// <summary>
    /// Sin <paramref name="recordStart"/> el documento es un solo registro (como siempre). Con él, el texto se parte en cada línea que lo contiene: lo anterior al primer registro es el ENCABEZADO (sus campos FromHeader se leen de ahí,
    /// o del documento entero si no están), y cada registro se lee con los demás campos y hereda lo del encabezado.
    /// </summary>
    public static DocumentReading ReadDocument(string? text, IReadOnlyList<EmailBodyField> fields, string? recordStart)
    {
        if (string.IsNullOrWhiteSpace(recordStart))
        {
            var one = ReadBody(text, fields);
            return new DocumentReading(new[] { one }, one.Missing, new[] { text ?? string.Empty });
        }
        var lines = (text ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var marker = PostalLocalityRules.Normalize(recordStart);
        var starts = new List<int>();
        for (var i = 0; i < lines.Length; i++) if (PostalLocalityRules.Normalize(lines[i]).Contains(marker, StringComparison.Ordinal)) starts.Add(i);
        if (starts.Count == 0) return new DocumentReading(Array.Empty<BodyReading>(), new[] { NoRecordsMessage });
        var header = string.Join("\n", lines.Take(starts[0]));
        var headerFields = fields.Where(f => f.FromHeader).ToList();
        var recordFields = fields.Where(f => !f.FromHeader).ToList();
        var headerReading = ReadBody(header.Trim().Length > 0 ? header : text, headerFields);
        var parts = new List<BodyReading>();
        var texts = new List<string>();
        var missing = new List<string>(headerReading.Missing);
        for (var n = 0; n < starts.Count; n++)
        {
            var end = n + 1 < starts.Count ? starts[n + 1] : lines.Length;
            var recordText = string.Join("\n", lines.Skip(starts[n]).Take(end - starts[n]));
            texts.Add(recordText);
            var record = ReadBody(recordText, recordFields);
            missing.AddRange(record.Missing.Select(m => RecordMissingMessage(n + 1, m)));
            var single = new Dictionary<int, string>(headerReading.Single);
            foreach (var (k, v) in record.Single) single[k] = v;
            parts.Add(new BodyReading(single, record.Lines, Array.Empty<string>()));
        }
        return new DocumentReading(parts, missing, texts, header);
    }

    /// <summary>Todos los registros como un solo archivo CSV (una fila por línea de cada registro).</summary>
    public static string ToCsv(DocumentReading reading, bool withHeader, char delimiter)
    {
        if (reading.Parts.Count == 1) return ToCsv(reading.Parts[0], withHeader, delimiter);
        var rows = reading.Parts.SelectMany(p => p.Lines.Count > 0 ? p.Lines.Select(l => Merge(p.Single, l)).ToList() : new List<Dictionary<int, string>> { new(p.Single) }).ToList();
        var width = rows.SelectMany(r => r.Keys).DefaultIfEmpty(1).Max();
        var sb = new StringBuilder();
        if (withHeader) sb.AppendLine(string.Join(delimiter, Enumerable.Range(1, width).Select(i => i.ToString(CultureInfo.InvariantCulture))));
        foreach (var r in rows) sb.AppendLine(string.Join(delimiter, Enumerable.Range(1, width).Select(i => Quote(r.TryGetValue(i, out var v) ? v : string.Empty, delimiter))));
        return sb.ToString();
    }

    private static string? ReadLabel(string[] lines, string? label)
    {
        if (string.IsNullOrWhiteSpace(label)) return null;
        var wanted = PostalLocalityRules.Normalize(label).TrimEnd(':').Trim();
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            var colon = line.IndexOf(':');
            if (colon <= 0) continue;
            if (PostalLocalityRules.Normalize(line[..colon]).Trim() != wanted) continue;
            var value = FirstCell(line[(colon + 1)..]);
            if (value.Length > 0) return value;
        }
        // sin dos puntos: «Reference # 123» (la etiqueta al principio de la línea); el valor llega hasta la celda siguiente (tabulador)
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (PostalLocalityRules.Normalize(line).StartsWith(wanted + " ", StringComparison.Ordinal) || PostalLocalityRules.Normalize(line).StartsWith(wanted + "\t", StringComparison.Ordinal))
            {
                var value = FirstCell(line[wanted.Length..]);
                if (value.Length > 0) return value;
            }
        }
        // en una tabla o un PDF la etiqueta puede estar en cualquier celda del renglón («Ship Date ⇥ 10/7/26 ⇥ Order # ⇥ GRUPO0037 ⇥ Customer PO ⇥ 2214…»): el valor es lo que sigue en la celda o la celda siguiente
        foreach (var raw in lines)
        {
            if (!raw.Contains('\t')) continue;
            var cells = raw.Split('\t').Select(c => c.Trim()).ToArray();
            for (var i = 0; i < cells.Length; i++)
            {
                var norm = PostalLocalityRules.Normalize(cells[i]).TrimEnd(':').Trim();
                if (norm == wanted) { var next = cells.Skip(i + 1).FirstOrDefault(c => c.Length > 0); if (next is not null) return next; }
                else if (norm.StartsWith(wanted + " ", StringComparison.Ordinal)) { var rest = cells[i][wanted.Length..].Trim(' ', ':'); if (rest.Length > 0) return rest; }
            }
        }
        return null;
    }

    /// <summary>El valor hasta el primer tabulador (una celda), sin espacios ni dos puntos de sobra.</summary>
    private static string FirstCell(string text) => text.Trim(' ', ':', '\t').Split('\t')[0].Trim();

    private static string? ReadRegex(string text, string? pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern)) return null;
        try
        {
            var m = Regex.Match(text, pattern, RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250));
            if (!m.Success) return null;
            var v = (m.Groups.Count > 1 && m.Groups[1].Success ? m.Groups[1].Value : m.Value).Trim();
            return v.Length == 0 ? null : v;
        }
        catch (Exception ex) when (ex is ArgumentException or RegexMatchTimeoutException) { return null; }
    }

    private static List<IReadOnlyDictionary<int, string>> ReadBlock(string[] lines, EmailBodyField f)
    {
        var rows = new List<IReadOnlyDictionary<int, string>>();
        var start = PostalLocalityRules.Normalize(f.Start ?? string.Empty);
        var end = PostalLocalityRules.Normalize(f.End ?? string.Empty);
        var sepText = string.IsNullOrEmpty(f.Separator) ? "\t" : f.Separator!.Replace("\\t", "\t");
        Regex? lineRx = null;
        if (!string.IsNullOrWhiteSpace(f.LineRegex))
        {
            try { lineRx = new Regex(f.LineRegex, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250)); }
            catch (ArgumentException) { return rows; }
        }
        var inside = start.Length == 0;   // sin texto de inicio (solo con expresión por línea) el bloque arranca en la primera línea
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            var norm = PostalLocalityRules.Normalize(line);
            if (!inside) { if (start.Length > 0 && norm.Contains(start, StringComparison.Ordinal)) inside = true; continue; }
            if (start.Length > 0 && f.Repeat && norm.Contains(start, StringComparison.Ordinal)) continue;   // el encabezado otra vez (página siguiente): no es un renglón
            if (end.Length > 0 && norm.Contains(end, StringComparison.Ordinal))
            {
                if (!f.Repeat) break;
                inside = false;   // este bloque terminó; se espera el próximo encabezado
                continue;
            }
            if (line.Length == 0) continue;
            var row = new Dictionary<int, string>();
            if (lineRx is not null)
            {
                Match m;
                try { m = lineRx.Match(line); } catch (RegexMatchTimeoutException) { continue; }
                if (!m.Success) continue;
                foreach (var c in f.Columns ?? Array.Empty<EmailBlockColumn>())
                {
                    if (c.Index < 1 || c.Index >= m.Groups.Count || !m.Groups[c.Index].Success || m.Groups[c.Index].Value.Trim().Length == 0) continue;
                    if (c.Divide is int dv)
                    {
                        if (dv < 1 || dv >= m.Groups.Count || !m.Groups[dv].Success) continue;
                        var top = ReadCheckRules.ParseNumber(m.Groups[c.Index].Value);
                        var bottom = ReadCheckRules.ParseNumber(m.Groups[dv].Value);
                        if (top is decimal a && bottom is decimal b && b != 0) row[c.Position] = Math.Round(a / b, 4).ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
                        continue;
                    }
                    row[c.Position] = Transform(m.Groups[c.Index].Value.Trim(), f.Transform);
                }
            }
            else
            {
                var parts = sepText == " " ? Regex.Split(line, @"\s{2,}|\t") : line.Split(sepText);
                foreach (var c in f.Columns ?? Array.Empty<EmailBlockColumn>())
                    if (c.Index >= 1 && c.Index <= parts.Length && parts[c.Index - 1].Trim().Length > 0) row[c.Position] = Transform(parts[c.Index - 1].Trim(), f.Transform);
            }
            if (row.Count > 0) rows.Add(row);
        }
        return rows;
    }

    public static string Transform(string value, string? transform) => (transform ?? EmailTransforms.None).Trim().ToUpperInvariant() switch
    {
        EmailTransforms.Upper => value.ToUpperInvariant(),
        EmailTransforms.Digits => new string(value.Where(char.IsDigit).ToArray()),
        EmailTransforms.Money => Regex.Replace(value, @"[^\d.,-]", string.Empty).Replace(",", string.Empty),
        EmailTransforms.Spaces => Regex.Replace(value, @"\s+", " ").Trim(),
        _ => value,
    };

    /// <summary>
    /// El cuerpo leído como un archivo CSV para la plantilla de la integración: una fila por línea del bloque (o una sola) con los valores sueltos repetidos; las columnas van en su posición
    /// (1…n). <paramref name="withHeader"/> agrega la fila de encabezado que la plantilla espera (los números de columna). Coma como delimitador.
    /// </summary>
    public static string ToCsv(BodyReading reading, bool withHeader, char delimiter)
    {
        var rows = reading.Lines.Count > 0 ? reading.Lines.Select(l => Merge(reading.Single, l)).ToList() : new List<Dictionary<int, string>> { new(reading.Single) };
        var width = rows.SelectMany(r => r.Keys).DefaultIfEmpty(1).Max();
        var sb = new StringBuilder();
        if (withHeader) sb.AppendLine(string.Join(delimiter, Enumerable.Range(1, width).Select(i => i.ToString(CultureInfo.InvariantCulture))));
        foreach (var r in rows) sb.AppendLine(string.Join(delimiter, Enumerable.Range(1, width).Select(i => Quote(r.TryGetValue(i, out var v) ? v : string.Empty, delimiter))));
        return sb.ToString();
    }

    private static Dictionary<int, string> Merge(IReadOnlyDictionary<int, string> single, IReadOnlyDictionary<int, string> line)
    {
        var d = new Dictionary<int, string>(single);
        foreach (var (k, v) in line) d[k] = v;
        return d;
    }

    private static string Quote(string value, char delimiter)
        => value.IndexOfAny(new[] { delimiter, '"', '\r', '\n' }) >= 0 ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
}
