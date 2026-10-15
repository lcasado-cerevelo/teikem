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
    public static string UnknownRecipientKindMessage(string? kind) => $"Tipo de destinatario desconocido: '{kind}'. Use ROLE, USER, EMAIL o PHONE.";
    public static string InvalidEmailMessage(string? v) => $"Correo no válido: '{v}'.";
    public static string InvalidPhoneMessage(string? v) => $"Teléfono no válido: '{v}'.";

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
