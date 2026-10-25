using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MailKit.Security;
using MimeKit;
using Teikem.Domain.Integrations;

namespace Teikem.Infrastructure.Integrations;

/// <summary>Un correo pendiente del buzón: los datos que las reglas necesitan y la forma de marcarlo como procesado.</summary>
public sealed record MailboxMessage(MailMessageData Data, string Key);

/// <summary>
/// Lote 133 (P11) — el buzón de una integración de correo. Interfaz para poder probar sin servidor real (un lector de prueba con correos de muestra) y para cambiar de protocolo sin tocar las reglas.
/// El filtro del asunto corre en el servidor de correo: nunca se baja todo el buzón; solo los NO leídos, hasta <c>maxMessages</c> por corrida.
/// </summary>
public interface IMailboxReader
{
    Task<IReadOnlyList<MailboxMessage>> FetchAsync(EmailConfig config, string password, int maxMessages, CancellationToken ct);
    /// <summary>Marca como leídos y mueve a la carpeta de procesados (se crea si falta) los correos ya atendidos. Nunca borra.</summary>
    Task MarkProcessedAsync(EmailConfig config, string password, IReadOnlyList<string> keys, CancellationToken ct);
    /// <summary>Prueba la conexión (servidor, usuario, contraseña y carpeta). Devuelve cuántos correos sin leer hay.</summary>
    Task<int> TestAsync(EmailConfig config, string password, CancellationToken ct);

    /// <summary>
    /// «Ya lo leí» en el log de Teikem (el buzón no se toca): las claves de los correos de la carpeta (leídos o no) recibidos desde <paramref name="sinceUtc"/>, del más viejo al más nuevo, que el log todavía NO tiene,
    /// hasta <paramref name="need"/>. Se recorre el buzón por páginas y por cada una se pregunta al log (<paramref name="alreadyLogged"/> devuelve de esas claves las ya atendidas): se corta en cuanto hay las que hacen falta.
    /// El filtro del asunto «contiene» se aplica aquí; el resto lo comprueba el ciclo de la corrida.
    /// </summary>
    Task<IReadOnlyList<string>> ListNewKeysAsync(EmailConfig config, string password, DateTime? sinceUtc, int need, Func<IReadOnlyList<string>, CancellationToken, Task<ISet<string>>> alreadyLogged, CancellationToken ct);

    /// <summary>Baja los correos completos de esas claves (sin marcarlos ni moverlos).</summary>
    Task<IReadOnlyList<MailboxMessage>> GetAsync(EmailConfig config, string password, IReadOnlyList<string> keys, CancellationToken ct);
}

/// <summary>Convierte un correo MIME (de un buzón o de un archivo .eml) en los datos que usan las reglas. El HTML se reduce a texto; nada se ejecuta.</summary>
public static class MailMessageMapper
{
    private static readonly string[] HeaderNames = ["X-Original-Sender", "X-Original-From", "Resent-From", "Reply-To"];

    public static MailMessageData Map(MimeMessage m, string? uid = null)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in HeaderNames) if (m.Headers[name] is string v && !string.IsNullOrWhiteSpace(v)) headers[name] = v;
        var text = m.TextBody;
        if (string.IsNullOrWhiteSpace(text)) text = EmailRules.HtmlToText(m.HtmlBody);
        var attachments = new List<MailAttachmentData>();
        foreach (var part in m.Attachments)
        {
            using var ms = new MemoryStream();
            if (part is MimePart mp) mp.Content?.DecodeTo(ms);
            else if (part is MessagePart mep) mep.Message.WriteTo(ms);
            var name = (part as MimePart)?.FileName ?? part.ContentDisposition?.FileName ?? part.ContentType.Name ?? string.Empty;
            attachments.Add(new MailAttachmentData(name, part.ContentType.MimeType, ms.ToArray()));
        }
        var id = string.IsNullOrWhiteSpace(m.MessageId) ? $"sin-id-{Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes((m.Subject ?? "") + m.Date.ToString("O"))))[..16]}" : m.MessageId.Trim();
        return new MailMessageData(id, uid, m.Date == DateTimeOffset.MinValue ? null : m.Date.UtcDateTime, m.From.Mailboxes.FirstOrDefault()?.Address ?? m.From.ToString(),
            m.ReplyTo.Mailboxes.FirstOrDefault()?.Address, headers, m.Subject ?? string.Empty, text ?? string.Empty, attachments);
    }

    /// <summary>Lee un archivo .eml completo (para probar la configuración subiéndolo a la integración, igual que si hubiera llegado).</summary>
    public static MailMessageData FromEml(byte[] bytes)
    {
        using var ms = new MemoryStream(bytes);
        return Map(MimeMessage.Load(ms));
    }
}

/// <summary>IMAP con MailKit.</summary>
public sealed class ImapMailboxReader : IMailboxReader
{
    public const string ConnectionFailedMessage = "No se pudo conectar con el buzón";

    private static async Task<ImapClient> ConnectAsync(EmailConfig c, string password, CancellationToken ct)
    {
        var client = new ImapClient { Timeout = 30_000 };
        try
        {
            await client.ConnectAsync(c.Host, c.Port, c.UseSsl ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTlsWhenAvailable, ct);
            await client.AuthenticateAsync(c.Username, password, ct);
            return client;
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    public async Task<IReadOnlyList<MailboxMessage>> FetchAsync(EmailConfig c, string password, int maxMessages, CancellationToken ct)
    {
        using var client = await ConnectAsync(c, password, ct);
        var inbox = await client.GetFolderAsync(c.Mailbox, ct);
        await inbox.OpenAsync(FolderAccess.ReadOnly, ct);
        var query = SearchQuery.NotSeen;
        // el filtro del asunto va al servidor cuando es «contiene» (lo demás se vuelve a comprobar aquí)
        if (c.Subject is { } s && string.Equals(s.Operator, EmailMatchOperators.Contains, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(s.Value))
            query = query.And(SearchQuery.SubjectContains(s.Value));
        var uids = (await inbox.SearchAsync(query, ct)).Take(Math.Clamp(maxMessages, 1, 200)).ToList();
        var result = new List<MailboxMessage>();
        foreach (var uid in uids)
        {
            var message = await inbox.GetMessageAsync(uid, ct);
            result.Add(new MailboxMessage(MailMessageMapper.Map(message, uid.Id.ToString()), uid.Id.ToString()));
        }
        await client.DisconnectAsync(true, ct);
        return result;
    }

    public async Task MarkProcessedAsync(EmailConfig c, string password, IReadOnlyList<string> keys, CancellationToken ct)
    {
        if (keys.Count == 0) return;
        using var client = await ConnectAsync(c, password, ct);
        var inbox = await client.GetFolderAsync(c.Mailbox, ct);
        await inbox.OpenAsync(FolderAccess.ReadWrite, ct);
        var list = keys.Select(k => new UniqueId(uint.Parse(k))).ToList();
        await inbox.AddFlagsAsync(list, MessageFlags.Seen, true, ct);
        if (!string.IsNullOrWhiteSpace(c.ProcessedFolder))
        {
            IMailFolder target;
            try { target = await client.GetFolderAsync(c.ProcessedFolder, ct); }
            catch (FolderNotFoundException)
            {
                var root = client.GetFolder(client.PersonalNamespaces[0]);
                target = await root.CreateAsync(c.ProcessedFolder, true, ct);
            }
            await inbox.MoveToAsync(list, target, ct);
        }
        await client.DisconnectAsync(true, ct);
    }

    public async Task<int> TestAsync(EmailConfig c, string password, CancellationToken ct)
    {
        using var client = await ConnectAsync(c, password, ct);
        var inbox = await client.GetFolderAsync(c.Mailbox, ct);
        await inbox.OpenAsync(FolderAccess.ReadOnly, ct);
        var unseen = (await inbox.SearchAsync(SearchQuery.NotSeen, ct)).Count;
        await client.DisconnectAsync(true, ct);
        return unseen;
    }

    private const int KeyPage = 500;

    public async Task<IReadOnlyList<string>> ListNewKeysAsync(EmailConfig c, string password, DateTime? sinceUtc, int need, Func<IReadOnlyList<string>, CancellationToken, Task<ISet<string>>> alreadyLogged, CancellationToken ct)
    {
        using var client = await ConnectAsync(c, password, ct);
        var inbox = await client.GetFolderAsync(c.Mailbox, ct);
        await inbox.OpenAsync(FolderAccess.ReadOnly, ct);
        var query = SearchQuery.All;
        if (sinceUtc is { } since) query = query.And(SearchQuery.DeliveredAfter(since.Date.AddDays(-1)));   // el servidor compara por día: se pide un día de más y abajo se descartan los que no tocan
        if (c.Subject is { } s && string.Equals(s.Operator, EmailMatchOperators.Contains, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(s.Value))
            query = query.And(SearchQuery.SubjectContains(s.Value));
        var uids = (await inbox.SearchAsync(query, ct)).OrderBy(u => u.Id).ToList();
        var keys = uids.Select(u => $"{inbox.UidValidity}:{u.Id}").ToList();
        var result = new List<string>();
        for (var i = 0; i < keys.Count && result.Count < need; i += KeyPage)
        {
            var page = keys.Skip(i).Take(KeyPage).ToList();
            var done = await alreadyLogged(page, ct);
            foreach (var k in page) { if (!done.Contains(k)) { result.Add(k); if (result.Count >= need) break; } }
        }
        await client.DisconnectAsync(true, ct);
        return result;
    }

    public async Task<IReadOnlyList<MailboxMessage>> GetAsync(EmailConfig c, string password, IReadOnlyList<string> keys, CancellationToken ct)
    {
        if (keys.Count == 0) return Array.Empty<MailboxMessage>();
        using var client = await ConnectAsync(c, password, ct);
        var inbox = await client.GetFolderAsync(c.Mailbox, ct);
        await inbox.OpenAsync(FolderAccess.ReadOnly, ct);
        var result = new List<MailboxMessage>();
        foreach (var key in keys)
        {
            var parts = key.Split(':');
            if (parts.Length != 2 || !uint.TryParse(parts[0], out var validity) || !uint.TryParse(parts[1], out var id) || validity != inbox.UidValidity) continue;   // el buzón se reconstruyó: esa clave ya no existe
            var message = await inbox.GetMessageAsync(new UniqueId(id), ct);
            result.Add(new MailboxMessage(MailMessageMapper.Map(message, id.ToString()), key));
        }
        await client.DisconnectAsync(true, ct);
        return result;
    }
}
