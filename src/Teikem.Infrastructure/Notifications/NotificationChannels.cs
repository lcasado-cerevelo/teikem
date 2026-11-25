using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Notifications;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Notifications;

/// <summary>
/// Un aviso ya armado (texto resuelto) para UN destinatario por UN canal. Attachments (Lote 243) = adjuntos del correo (solo EMAIL y
/// solo si el cuerpo usó una variable bloque); null = sin adjuntos.
/// </summary>
public sealed record NotificationDelivery(
    int TenantId,
    string CompanyName,
    string EventCode,
    long? OutboxId,
    int RuleId,
    int? UserId,
    string? Email,
    string? Phone,
    string Subject,
    string BodyText,
    string BodyHtml,
    string? LinkUrl,
    bool EventIsOptional,
    IReadOnlyList<EmailAttachment>? Attachments = null);

/// <summary>Resultado de un envío: SENT, SKIPPED (no corresponde o no está configurado; se explica) o FAILED (se reintenta).</summary>
public sealed record SendOutcome(string Status, string? Message)
{
    public static SendOutcome Sent() => new(NotificationLogStatuses.Sent, null);
    public static SendOutcome Skipped(string why) => new(NotificationLogStatuses.Skipped, why);
    public static SendOutcome Failed(string why) => new(NotificationLogStatuses.Failed, why);
}

/// <summary>
/// Lote 51 — un canal de salida. IsConfigured = false: el canal aparece apagado en la pantalla, no se puede encender en una regla y,
/// si llegara un evento, el registro queda SKIPPED con la razón. Agregar un proveedor (SMS, push) es solo registrar un sender nuevo.
/// </summary>
public interface INotificationChannelSender
{
    string Channel { get; }
    bool IsConfigured { get; }

    /// <summary>Lote 243: true si el canal entrega a un buzón local de desarrollo (Mailpit) y nunca sale a la calle.</summary>
    bool IsLocal => false;

    Task<SendOutcome> SendAsync(NotificationDelivery delivery, CancellationToken ct);
}

/// <summary>
/// Correo: usa el envío transaccional que ya existe (Brevo, o el buzón local en desarrollo). Sin proveedor configurado el canal queda
/// «no configurado». Lote 243: manda los adjuntos del aviso.
/// </summary>
public sealed class EmailChannelSender(ITransactionalEmailSender email) : INotificationChannelSender
{
    public string Channel => NotificationChannels.Email;
    public bool IsConfigured => email.IsConfigured;
    public bool IsLocal => email.IsLocalSink;

    public async Task<SendOutcome> SendAsync(NotificationDelivery d, CancellationToken ct)
    {
        if (!email.IsConfigured) return SendOutcome.Skipped(NotificationRules.NotConfiguredMessage(Channel));
        if (string.IsNullOrWhiteSpace(d.Email)) return SendOutcome.Skipped("El destinatario no tiene correo.");
        try
        {
            await email.SendAsync(new EmailMessage(d.Email.Trim(), null, d.Subject, d.BodyHtml, d.CompanyName, d.Attachments is { Count: > 0 } ? d.Attachments : null), ct);
            return SendOutcome.Sent();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            return SendOutcome.Failed(ex.Message);
        }
    }
}

/// <summary>Bandeja de la web (la campanita): escribe en UserNotification. Respeta los eventos que el usuario silenció (si son opcionales).</summary>
public sealed class AppChannelSender(TeikemDbContext db) : INotificationChannelSender
{
    public string Channel => NotificationChannels.App;
    public bool IsConfigured => true;

    public async Task<SendOutcome> SendAsync(NotificationDelivery d, CancellationToken ct)
    {
        if (d.UserId is not int userId) return SendOutcome.Skipped("La bandeja solo llega a usuarios de la compañía.");
        if (d.EventIsOptional && await db.UserNotificationPrefs.AsNoTracking().AnyAsync(p => p.UserId == userId && p.EventCode == d.EventCode && p.IsMuted, ct))
            return SendOutcome.Skipped("El usuario silenció este aviso.");
        // idempotencia: el mismo aviso de la misma regla al mismo usuario no se escribe dos veces
        if (d.OutboxId is long outbox && await db.UserNotifications.AsNoTracking().AnyAsync(n => n.NotificationOutboxId == outbox && n.NotificationRuleId == d.RuleId && n.UserId == userId, ct))
            return SendOutcome.Sent();
        db.UserNotifications.Add(new UserNotification
        {
            TenantId = d.TenantId,
            UserId = userId,
            EventCode = d.EventCode,
            Title = Cut(d.Subject, 200),
            Body = string.IsNullOrWhiteSpace(d.BodyText) ? null : Cut(d.BodyText, 1000),
            LinkUrl = Cut(d.LinkUrl, 300),
            CreatedAtUtc = DateTime.UtcNow,
            NotificationOutboxId = d.OutboxId,
            NotificationRuleId = d.OutboxId is null ? null : d.RuleId,
        });
        await db.SaveChangesAsync(ct);
        return SendOutcome.Sent();
    }

    private static string Cut(string? s, int max) => s is null ? string.Empty : s.Length <= max ? s : s[..max];
}

/// <summary>SMS sin proveedor (D16): «no configurado». Cuando se contrate uno, se reemplaza este sender por el real.</summary>
public sealed class SmsChannelSender : INotificationChannelSender
{
    public string Channel => NotificationChannels.Sms;
    public bool IsConfigured => false;
    public Task<SendOutcome> SendAsync(NotificationDelivery delivery, CancellationToken ct) => Task.FromResult(SendOutcome.Skipped(NotificationRules.NotConfiguredMessage(Channel)));
}

/// <summary>Push sin proveedor (D16): «no configurado».</summary>
public sealed class PushChannelSender : INotificationChannelSender
{
    public string Channel => NotificationChannels.Push;
    public bool IsConfigured => false;
    public Task<SendOutcome> SendAsync(NotificationDelivery delivery, CancellationToken ct) => Task.FromResult(SendOutcome.Skipped(NotificationRules.NotConfiguredMessage(Channel)));
}

/// <summary>Lo mismo que Render con html, más saltos de línea → &lt;br&gt;, para el cuerpo de un correo.</summary>
public static class NotificationHtml
{
    public static string Paragraphs(string text) => "<p>" + WebUtility.HtmlEncode(text).Replace("\r\n", "\n").Replace("\n", "<br>") + "</p>";

    /// <summary>
    /// Lote 243 — el cuerpo HTML de un correo con variables bloque. La plantilla se parte en los {bloques} que vinieron: cada tramo
    /// de texto pasa por <c>Paragraphs(Render(tramo))</c> (que codifica el HTML; los saltos de línea de la orilla, junto al bloque, se
    /// quitan y un tramo vacío no deja párrafo) y cada bloque se inserta como su Html (el proveedor ya codificó sus valores).
    /// Sin bloques (o si la plantilla no usa ninguno) devuelve EXACTAMENTE <c>Paragraphs(Render(template))</c>, lo de siempre.
    /// </summary>
    public static string Compose(string? template, IReadOnlyDictionary<string, string> values, IReadOnlyDictionary<string, NotificationBlock>? blocks)
    {
        var names = blocks is null || blocks.Count == 0 ? null : blocks.Keys.ToHashSet(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(template) || names is null || !NotificationRules.UsesAny(template, names))
            return Paragraphs(NotificationRules.Render(template, values));
        var sb = new StringBuilder();
        foreach (var (text, block) in NotificationRules.SplitBlocks(template, names))
        {
            if (block is not null) { sb.Append(blocks![block].Html); continue; }
            var t = text!.Replace("\r\n", "\n").Trim('\n');
            if (string.IsNullOrWhiteSpace(t)) continue;
            sb.Append(Paragraphs(NotificationRules.Render(t, values)));
        }
        return sb.ToString();
    }
}
