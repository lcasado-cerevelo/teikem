namespace Teikem.Infrastructure.Abstractions;

/// <summary>
/// Redirección de TODO el correo saliente en un ambiente de pruebas (2026-10-19, decidido por Luis para test): con <c>Email:RedirectTo</c> puesto,
/// ningún correo llega a su destinatario real (clientes, consignatarios, staff); todos van a esa dirección, con el destinatario original en el
/// asunto («[TEST → quien@era.com] …») y en una línea al inicio del cuerpo. Producción y staging no la llevan y envían igual que siempre.
/// </summary>
public sealed class EmailRedirectOptions
{
    public const string Section = "Email";

    /// <summary>Buzón que recibe todo (p. ej. teikem@cerevelo.com). Vacío = sin redirección.</summary>
    public string? RedirectTo { get; set; }

    /// <summary>
    /// true: usa dirección «más» por destinatario original (teikem+cliente_suempresa.com@cerevelo.com), que Google Workspace entrega en el mismo
    /// buzón y permite filtrar por destinatario. false: todo a RedirectTo tal cual.
    /// </summary>
    public bool PlusAddressing { get; set; } = true;

    /// <summary>Dominios cuyos destinatarios SÍ reciben de verdad (p. ej. "cerevelo.com": los códigos de MFA del equipo llegan a cada quien).</summary>
    public string[] ExceptDomains { get; set; } = [];

    /// <summary>Etiqueta del ambiente que va en el asunto (Hosting:EnvironmentLabel cuando existe; si no, «TEST»).</summary>
    public string? Label { get; set; }
}

/// <summary>Resultado de aplicar la redirección a un correo.</summary>
public sealed record RedirectedEmail(string ToEmail, string? ToName, string Subject, string HtmlContent, bool Redirected);

public static class EmailRedirectRules
{
    public static bool IsEnabled(EmailRedirectOptions? o) => o is not null && !string.IsNullOrWhiteSpace(o.RedirectTo) && o.RedirectTo.Contains('@');

    /// <summary>Aplica la redirección (lógica pura; la prueba unitaria la cubre). Sin redirección devuelve el correo tal cual.</summary>
    public static RedirectedEmail Apply(EmailRedirectOptions? o, string toEmail, string? toName, string subject, string htmlContent)
    {
        if (!IsEnabled(o)) return new RedirectedEmail(toEmail, toName, subject, htmlContent, false);
        var original = (toEmail ?? string.Empty).Trim();
        var domain = original.Contains('@') ? original[(original.LastIndexOf('@') + 1)..] : string.Empty;
        if (o!.ExceptDomains.Any(d => !string.IsNullOrWhiteSpace(d) && string.Equals(d.Trim().TrimStart('@'), domain, StringComparison.OrdinalIgnoreCase)))
            return new RedirectedEmail(original, toName, subject, htmlContent, false);

        var label = string.IsNullOrWhiteSpace(o.Label) ? "TEST" : o.Label.Trim().ToUpperInvariant();
        var target = o.RedirectTo!.Trim();
        if (o.PlusAddressing)
        {
            var at = target.LastIndexOf('@');
            // la parte local admite letras, dígitos, punto, guion y guion bajo; lo demás (incluida la @ del original) se vuelve «_»
            var tag = new string(original.Select(c => char.IsLetterOrDigit(c) || c is '.' or '-' or '_' ? c : '_').ToArray()).Trim('.');
            if (tag.Length > 0) target = $"{target[..at]}+{tag}{target[at..]}";
        }
        var who = string.IsNullOrWhiteSpace(toName) || string.Equals(toName, original, StringComparison.OrdinalIgnoreCase) ? original : $"{toName} <{original}>";
        var newSubject = $"[{label} → {original}] {subject}";
        var banner = $"<div style=\"background:#DC2626;color:#fff;padding:8px 12px;font-family:sans-serif;font-size:13px\"><strong>{label}</strong> · Ambiente de pruebas: este correo iba para <strong>{System.Net.WebUtility.HtmlEncode(who)}</strong> y se redirigió aquí; el destinatario real no lo recibió.</div>";
        return new RedirectedEmail(target, string.IsNullOrWhiteSpace(toName) ? null : toName, newSubject, banner + htmlContent, true);
    }
}
