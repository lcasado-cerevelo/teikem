using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Teikem.Domain.Common;
using Teikem.Domain.Notifications;

namespace Teikem.Infrastructure.Notifications;

/// <summary>
/// Lote 51 — reglas PURAS de los avisos: mensajes exactos, validaciones, plantillas con {variables} y el calendario de reintentos.
/// Reintentos: el primer intento es inmediato; si falla se reintenta a 1, 5 y 30 minutos (3 reintentos); si el cuarto falla, queda FAILED.
/// </summary>
public static partial class NotificationRules
{
    public const int MaxNameLength = 100;
    public const int MaxSubjectLength = 200;
    public const int MaxBodyLength = 1000;
    public const int MaxRetries = 3;
    public static readonly TimeSpan[] RetryDelays = { TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30) };

    public const string RuleNotFoundMessage = "Regla de aviso no encontrada.";
    public const string NameRequiredMessage = "Escriba el nombre de la regla.";
    public const string NameTooLongMessage = "El nombre de la regla no puede pasar de 100 caracteres.";
    public const string DuplicateRuleMessage = "Ya existe una regla con ese nombre.";
    public const string NeedRecipientMessage = "La regla necesita al menos un destinatario para poder activarse.";
    public const string NeedChannelMessage = "La regla necesita al menos un canal encendido para poder activarse.";
    public const string RoleNotFoundMessage = "Rol no encontrado.";
    public const string UserNotFoundMessage = "Usuario no encontrado en esta compañía.";
    public const string NotificationNotFoundMessage = "Aviso no encontrado.";
    public const string NotMutableMessage = "Ese aviso no se puede silenciar.";
    public const string EventRequiredMessage = "Indique el evento de la regla.";
    public const string ChannelRequiredMessage = "Indique el canal.";

    public static string UnknownEventMessage(string? code) => $"Evento de aviso desconocido: '{code}'.";
    public static string UnknownChannelMessage(string? channel) => $"Canal desconocido: '{channel}'. Use EMAIL, SMS, APP o PUSH.";
    public static string NotConfiguredMessage(string channel) => $"El canal {channel} todavía no está configurado en Teikem.";
    public static string SubjectRequiredMessage(string channel) => $"El canal {channel} necesita un asunto.";
    public static string BodyRequiredMessage(string channel) => $"El canal {channel} necesita un texto.";
    public static string SubjectTooLongMessage(string channel) => $"El asunto del canal {channel} no puede pasar de 200 caracteres.";
    public static string BodyTooLongMessage(string channel) => $"El texto del canal {channel} no puede pasar de 1000 caracteres.";
    public static string UnknownVariableMessage(string name) => $"La plantilla usa una variable que el evento no tiene: {{{name}}}.";
    public static string UnknownRecipientKindMessage(string? kind) => $"Tipo de destinatario desconocido: '{kind}'. Use ROLE, USER, EMAIL, PHONE o CONTEXT.";
    public static string InvalidEmailMessage(string? v) => $"Correo no válido: '{v}'.";
    public static string InvalidPhoneMessage(string? v) => $"Teléfono no válido: '{v}'.";

    // ---- Lote 243: contenido armado al enviar (bloques, destinatarios del evento, vista previa) ----

    /// <summary>400 (campo reference): el evento no tiene proveedor de contenido, así que no se puede armar con un documento real.</summary>
    public const string ReferenceNotSupportedMessage = "Este evento no admite vista previa con un documento real.";

    /// <summary>400 (campo channels): una variable bloque ({orderDetail}) en el asunto.</summary>
    public static string BlockInSubjectMessage(string name) => $"La variable {{{name}}} es un bloque y solo puede ir en el texto, no en el asunto.";

    /// <summary>400 (campo recipients): un destinatario CONTEXT con una clave que el evento no declara.</summary>
    public static string UnknownContextRecipientMessage(string? value) => $"Este evento no tiene el destinatario '{value}'.";

    /// <summary>400 (campo audience) de la vista previa: STAFF o CUSTOMER.</summary>
    public static string UnknownAudienceMessage(string? value) => $"Destinatario de la vista previa desconocido: '{value}'. Use STAFF o CUSTOMER.";

    /// <summary>400 (campo lang) de la vista previa: es o en.</summary>
    public static string UnknownLangMessage(string? value) => $"Idioma no válido: '{value}'. Use es o en.";

    /// <summary>
    /// Registro de avisos: motivo de la fila SKIPPED cuando un destinatario del evento no dejó a nadie alcanzable en el canal y el
    /// proveedor no dio su propio motivo. EMAIL «…» no tiene correo; SMS no tiene teléfono; APP y PUSH no tiene usuario en Teikem.
    /// </summary>
    public static string ContextUnreachableMessage(string label, string channel) => channel switch
    {
        NotificationChannels.Email => $"«{label}» no tiene correo.",
        NotificationChannels.Sms => $"«{label}» no tiene teléfono.",
        _ => $"«{label}» no tiene usuario en Teikem.",
    };

    /// <summary>
    /// Etiqueta corta de un destinatario del evento para el Registro de avisos: lo que va antes del primer « (» (p. ej. «Cliente de
    /// la orden (correo principal y contactos marcados)» → «Cliente de la orden»).
    /// </summary>
    public static string ShortContextLabel(string label)
    {
        var l = (label ?? string.Empty).Trim();
        var i = l.IndexOf(" (", StringComparison.Ordinal);
        return i > 0 ? l[..i].Trim() : l;
    }

    /// <summary>Lo que se anota como destinatario en el Registro de avisos para un destinatario del evento que no resolvió: «(etiqueta)».</summary>
    public static string ContextLogRecipient(string label) => "(" + ShortContextLabel(label) + ")";

    /// <summary>
    /// Igual que <see cref="Render"/> pero cada variable bloque sale como su versión de una línea (<see cref="NotificationBlock.Text"/>):
    /// el texto de la campanita, del SMS y el texto plano del correo. Un bloque que no vino sale vacío.
    /// </summary>
    public static string RenderText(string? template, IReadOnlyDictionary<string, string> values, IReadOnlyDictionary<string, NotificationBlock>? blocks)
        => string.IsNullOrEmpty(template)
            ? string.Empty
            : VariableRegex().Replace(template, m =>
            {
                var name = m.Groups[1].Value;
                if (blocks is not null && blocks.TryGetValue(name, out var block)) return block.Text ?? string.Empty;
                return values.TryGetValue(name, out var x) ? x : string.Empty;
            });

    /// <summary>True si la plantilla usa alguna de las variables bloque indicadas.</summary>
    public static bool UsesAny(string? template, IEnumerable<string> names)
    {
        var set = names as IReadOnlySet<string> ?? names.ToHashSet(StringComparer.Ordinal);
        return VariablesOf(template).Any(set.Contains);
    }

    /// <summary>
    /// Une las variables del aviso (payload) con las que agrega el proveedor de contenido; gana el proveedor.
    /// </summary>
    public static Dictionary<string, string> Merge(IReadOnlyDictionary<string, string> payload, IReadOnlyDictionary<string, string>? extra)
    {
        var d = new Dictionary<string, string>(payload, StringComparer.Ordinal);
        if (extra is not null) foreach (var (k, v) in extra) d[k] = v ?? string.Empty;
        return d;
    }

    /// <summary>Partes de una plantilla separadas en los {bloques}: (texto, null) o (null, nombre del bloque), en orden.</summary>
    internal static IEnumerable<(string? Text, string? Block)> SplitBlocks(string template, IReadOnlySet<string> blockNames)
    {
        var last = 0;
        foreach (Match m in VariableRegex().Matches(template))
        {
            if (!blockNames.Contains(m.Groups[1].Value)) continue;
            if (m.Index > last) yield return (template[last..m.Index], null);
            yield return (null, m.Groups[1].Value);
            last = m.Index + m.Length;
        }
        if (last < template.Length) yield return (template[last..], null);
    }

    [GeneratedRegex(@"\{([A-Za-z][A-Za-z0-9_]*)\}")]
    private static partial Regex VariableRegex();

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailRegex();

    public static string? NormalizeChannel(string? channel)
    {
        var c = (channel ?? string.Empty).Trim().ToUpperInvariant();
        return NotificationChannels.All.Contains(c) ? c : null;
    }

    public static string? NormalizeKind(string? kind)
    {
        var k = (kind ?? string.Empty).Trim().ToUpperInvariant();
        return NotificationRecipientKinds.All.Contains(k) ? k : null;
    }

    public static bool IsValidEmail(string? v) => !string.IsNullOrWhiteSpace(v) && v.Trim().Length <= 200 && EmailRegex().IsMatch(v.Trim());

    /// <summary>Teléfono: solo dígitos con +, espacios, guiones y paréntesis permitidos; de 7 a 15 dígitos.</summary>
    public static bool IsValidPhone(string? v)
    {
        if (string.IsNullOrWhiteSpace(v) || v.Trim().Length > 30) return false;
        if (v.Any(ch => !(char.IsDigit(ch) || ch is '+' or ' ' or '-' or '(' or ')'))) return false;
        var digits = v.Count(char.IsDigit);
        return digits is >= 7 and <= 15;
    }

    /// <summary>Nombres de las {variables} que usa una plantilla.</summary>
    public static IReadOnlyList<string> VariablesOf(string? template)
        => string.IsNullOrEmpty(template) ? Array.Empty<string>() : VariableRegex().Matches(template).Select(m => m.Groups[1].Value).Distinct().ToList();

    /// <summary>
    /// Reemplaza cada {variable} por su valor; una variable que falta se reemplaza por vacío (el aviso sale igual, sin la pieza).
    /// html = true codifica los valores para el cuerpo de un correo.
    /// </summary>
    public static string Render(string? template, IReadOnlyDictionary<string, string> values, bool html = false)
        => string.IsNullOrEmpty(template)
            ? string.Empty
            : VariableRegex().Replace(template, m =>
            {
                var v = values.TryGetValue(m.Groups[1].Value, out var x) ? x : string.Empty;
                return html ? WebUtility.HtmlEncode(v) : v;
            });

    /// <summary>Variables del evento en JSON {"nombre":"valor"} (los valores nulos se guardan vacíos).</summary>
    public static string? SerializeValues(IReadOnlyDictionary<string, string?>? values)
        => values is null || values.Count == 0 ? null : JsonSerializer.Serialize(values.ToDictionary(kv => kv.Key, kv => kv.Value ?? string.Empty));

    public static Dictionary<string, string> ParseValues(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new Dictionary<string, string>();
        try { return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>(); }
        catch (JsonException) { return new Dictionary<string, string>(); }
    }

    /// <summary>Texto de una plantilla multilingüe para un idioma (con respaldo: es, en, cualquiera).</summary>
    public static string Pick(string? json, string? lang) => MultilingualText.Resolve(json, lang);

    /// <summary>Qué sigue tras un intento fallido (attempt = número de intentos hechos, con este incluido): reintento en X o fallo final.</summary>
    public static (bool Retry, TimeSpan Delay) AfterFailure(int attempt)
        => attempt >= 1 && attempt <= MaxRetries ? (true, RetryDelays[attempt - 1]) : (false, TimeSpan.Zero);
}
