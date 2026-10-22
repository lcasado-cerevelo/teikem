using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Teikem.Domain.Constants;
using Teikem.Domain.Identity;
using Teikem.Domain.Security;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>Auth:TrustedBrowsers — computadoras de confianza (Lote 100). Apagado por omisión: se enciende cuando el dueño lo decide y el correo de la plataforma funciona.</summary>
public sealed class TrustedBrowserOptions
{
    /// <summary>Exigir confirmar por correo la primera vez que se entra desde una computadora nueva. Sin correo configurado nadie podría confirmar: encenderlo solo con el envío de correo andando.</summary>
    public bool Enabled { get; set; }
    public int Days { get; set; } = TrustedBrowserRules.Days;
    public int LinkHours { get; set; } = TrustedBrowserRules.LinkHours;
    /// <summary>Solo desarrollo: devolver el enlace en la respuesta para probar sin proveedor de correo.</summary>
    public bool ReturnLinkInResponse { get; set; }
}

/// <summary>Resultado de revisar el navegador en el login: <c>Trusted</c> entra; si no, <c>Sent</c> dice si salió el correo y <c>DevLink</c> (solo desarrollo) el enlace.</summary>
/// <summary>MfaRecent (portales): el código del segundo paso se escribió desde esta computadora hace menos de 30 días, así que no se vuelve a pedir.</summary>
public sealed record TrustedBrowserCheck(bool Trusted, bool Sent, string? Reason, string? DevLink, bool MfaRecent = false);

public sealed record TrustedBrowserDto(Guid PublicId, string Label, string? IpAddress, DateTime CreatedAtUtc, DateTime? LastUsedAtUtc, DateTime? ExpiresAtUtc, bool IsCurrent);

/// <summary>
/// Lote 100 (decidido con Luis, 2026-10-17): «si se trata de loguear de otra computadora, tiene que volver a recibir un email de confirmación». El navegador manda una marca secreta
/// (la crea y guarda la web); la primera vez que no se reconoce se registra pendiente y se manda un correo con un enlace; al confirmarlo, el navegador queda de confianza 30 días sin uso.
/// El enlace confirma la marca del navegador que intentó entrar aunque se abra desde otro aparato (el teléfono, por ejemplo).
/// </summary>
public sealed class TrustedBrowserService(TeikemDbContext db, ITenantContext tenant, IOptions<TrustedBrowserOptions> options, IOptions<PasswordResetOptions> web,
    ITransactionalEmailSender email, ISecurityEventWriter security, ILookupCache? lookups = null, Microsoft.Extensions.Configuration.IConfiguration? config = null)
{
    public bool Enabled => options.Value.Enabled;

    public static string Hash(string secret) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret))).ToLowerInvariant();

    private static string NewSecret() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();

    // ---------------------------------------------------------------- login

    public async Task<TrustedBrowserCheck> CheckAsync(ApplicationUser user, string? browserToken, string? companyName, CancellationToken ct, bool doNotTrust = false)
    {
        if (!TrustedBrowserRules.IsValidToken(browserToken))
            return new TrustedBrowserCheck(false, false, "Este cliente no identifica la computadora; entre desde la web.", null);
        var now = DateTime.UtcNow;
        var hash = Hash(browserToken!);
        // la fila de esta marca (el índice es único por usuario y marca): puede estar quitada o consumida («No confiar»); en ese caso se reutiliza como pendiente
        var row = await db.TrustedBrowsers.FirstOrDefaultAsync(b => b.UserId == user.Id && b.TokenHash == hash, ct);
        if (row is { RevokedAtUtc: not null })
        {
            row.RevokedAtUtc = null;
            row.ConfirmedAtUtc = null;
            row.ExpiresAtUtc = null;
            row.MfaVerifiedAtUtc = null;
            row.EmailSentAtUtc = null;
            row.SingleUse = false;
        }
        if (row is not null && row.IsTrusted(now))
        {
            if (doNotTrust) row.SingleUse = true;   // «No confiar en esta computadora»: este ingreso entra, luego se quita (ConsumeIfSingleUseAsync)
            row.LastUsedAtUtc = now;
            row.ExpiresAtUtc = now.AddDays(options.Value.Days);   // la ventana corre: 30 días SIN USO
            db.SuppressAudit = true;
            try { await db.SaveChangesAsync(ct); } finally { db.SuppressAudit = false; }
            return new TrustedBrowserCheck(true, false, null, null, !row.SingleUse && row.MfaVerifiedAtUtc is DateTime mv && mv > now.AddDays(-TrustedBrowserRules.MfaDays));
        }

        // no se reconoce: se pide confirmar por correo (sin saturar el correo del usuario)
        if (row is not null && row.EmailSentAtUtc is DateTime last && last > now.AddMinutes(-TrustedBrowserRules.ResendMinutes))
            return new TrustedBrowserCheck(false, true, null, null);
        var sentToday = await db.TrustedBrowsers.CountAsync(b => b.UserId == user.Id && b.EmailSentAtUtc > now.AddHours(-24), ct);
        if (sentToday >= TrustedBrowserRules.MaxEmailsPerDay)
            return new TrustedBrowserCheck(false, false, "Ya se enviaron varios correos de confirmación hoy; revise su correo o intente más tarde.", null);

        var secret = NewSecret();
        row ??= db.TrustedBrowsers.Add(new TrustedBrowser { PublicId = Guid.NewGuid(), UserId = user.Id, TokenHash = hash, CreatedAtUtc = now }).Entity;
        row.ConfirmHash = Hash(secret);
        row.LinkExpiresAtUtc = now.AddHours(options.Value.LinkHours);
        row.EmailSentAtUtc = now;
        row.UserAgent = Trunc(tenant.UserAgent, 400);
        row.IpAddress = Trunc(tenant.IpAddress, 64);
        row.Label = TrustedBrowserRules.Describe(tenant.UserAgent);
        row.ConfirmedAtUtc = null;
        row.ExpiresAtUtc = null;
        row.MfaVerifiedAtUtc = null;
        row.SingleUse = doNotTrust;
        db.SuppressAudit = true;
        try { await db.SaveChangesAsync(ct); } finally { db.SuppressAudit = false; }
        await security.WriteAsync(SecurityEventTypes.Login, SecurityOutcomes.Blocked, user.Id, null, new { stage = "browser_pending", browser = row.Label, ip = row.IpAddress }, ct);

        // 2026-10-22: el usuario de portal (cliente o consignatario) confirma la computadora en el PORTAL (Portal:WebBaseUrl), no en la web del equipo; sin esa dirección, la de la web.
        var portalUrl = config?["Portal:WebBaseUrl"];
        var isPortal = !string.IsNullOrWhiteSpace(portalUrl) && lookups is not null && user.UserKindLookupId is int kindId && (await lookups.GetAsync(kindId, ct))?.InternalCode == UserKinds.Portal;
        var baseUrl = (isPortal ? portalUrl : web.Value.WebBaseUrl ?? (options.Value.ReturnLinkInResponse ? "http://localhost:5173" : null))?.TrimEnd('/');
        if (string.IsNullOrWhiteSpace(baseUrl)) return new TrustedBrowserCheck(false, false, "No se pudo armar el enlace de confirmación (falta la dirección de la web).", null);
        var link = $"{baseUrl}/confirm-browser?token={secret}";
        var sent = false;
        if (email.IsConfigured && !string.IsNullOrWhiteSpace(user.Email))
        {
            try
            {
                await email.SendAsync(user.Email, user.FullName, "Confirme esta computadora para entrar", ConfirmationHtml(user, row, link, options.Value.LinkHours), ct, companyName);
                sent = true;
            }
            catch (Exception)
            {
                sent = false;   // el correo falló: el usuario puede volver a intentar entrar (se reenvía pasados unos minutos)
            }
        }
        return new TrustedBrowserCheck(false, sent, sent ? null : "No se pudo enviar el correo de confirmación.", options.Value.ReturnLinkInResponse ? link : null);
    }

    private static string ConfirmationHtml(ApplicationUser user, TrustedBrowser row, string link, int linkHours)
    {
        static string enc(string s) => WebUtility.HtmlEncode(s);
        return $"<p>Hola {enc(user.FullName ?? user.Email ?? "")},</p>"
            + "<p>Alguien entró con su contraseña desde una computadora que no reconocemos:</p>"
            + $"<ul><li>Navegador: <b>{enc(row.Label)}</b></li><li>Dirección: {enc(row.IpAddress ?? "desconocida")}</li><li>Fecha: {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC</li></ul>"
            + $"<p><b>Si fue usted</b>, confirme esta computadora: <a href=\"{enc(link)}\">Confirmar esta computadora</a>. El enlace vale {linkHours} horas y se usa una sola vez. Después vuelva a la pantalla de entrada e inicie sesión otra vez; esta computadora quedará guardada por {TrustedBrowserRules.Days} días sin uso.</p>"
            + "<p><b>Si no fue usted</b>, no haga clic: ignore este correo y cambie su contraseña, porque alguien la conoce.</p>";
    }

    /// <summary>Anota que desde esta computadora (ya de confianza) se escribió el código del segundo paso: los portales no lo repiten por 30 días. Sin marca o sin fila no hace nada.</summary>
    public async Task MarkMfaVerifiedAsync(int userId, string? browserToken, CancellationToken ct)
    {
        if (!TrustedBrowserRules.IsValidToken(browserToken)) return;
        var hash = Hash(browserToken!);
        var row = await db.TrustedBrowsers.FirstOrDefaultAsync(b => b.UserId == userId && b.TokenHash == hash && b.RevokedAtUtc == null && b.ConfirmedAtUtc != null, ct);
        if (row is null) return;
        row.MfaVerifiedAtUtc = DateTime.UtcNow;
        db.SuppressAudit = true;
        try { await db.SaveChangesAsync(ct); } finally { db.SuppressAudit = false; }
    }

    /// <summary>Si la computadora era «no confiar» (un solo ingreso), se quita ahora que el ingreso terminó (después de emitir la sesión).</summary>
    public async Task ConsumeIfSingleUseAsync(int userId, string? browserToken, CancellationToken ct)
    {
        if (!TrustedBrowserRules.IsValidToken(browserToken)) return;
        var hash = Hash(browserToken!);
        var row = await db.TrustedBrowsers.FirstOrDefaultAsync(b => b.UserId == userId && b.TokenHash == hash && b.RevokedAtUtc == null && b.SingleUse, ct);
        if (row is null) return;
        row.RevokedAtUtc = DateTime.UtcNow;
        db.SuppressAudit = true;
        try { await db.SaveChangesAsync(ct); } finally { db.SuppressAudit = false; }
        await security.WriteAsync(SecurityEventTypes.Login, SecurityOutcomes.Success, userId, null, new { stage = "browser_single_use_consumed", browser = row.Label }, ct);
    }

    // ---------------------------------------------------------------- confirmar (anónimo, con el enlace)

    public async Task ConfirmAsync(string? secret, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(secret) || secret.Length > 200) throw new ValidationException(TrustedBrowserRules.LinkInvalidMessage);
        var hash = Hash(secret.Trim());
        var now = DateTime.UtcNow;
        var row = await db.TrustedBrowsers.FirstOrDefaultAsync(b => b.ConfirmHash == hash && b.RevokedAtUtc == null && b.LinkExpiresAtUtc > now, ct)
            ?? throw new ValidationException(TrustedBrowserRules.LinkInvalidMessage);
        row.ConfirmHash = null;
        row.LinkExpiresAtUtc = null;
        row.ConfirmedAtUtc = now;
        row.LastUsedAtUtc = now;
        row.ExpiresAtUtc = now.AddDays(options.Value.Days);
        db.SuppressAudit = true;
        try { await db.SaveChangesAsync(ct); } finally { db.SuppressAudit = false; }
        await security.WriteAsync(SecurityEventTypes.Login, SecurityOutcomes.Success, row.UserId, null, new { stage = "browser_confirmed", browser = row.Label }, ct);
    }

    // ---------------------------------------------------------------- mis computadoras

    public async Task<IReadOnlyList<TrustedBrowserDto>> ListMineAsync(string? currentToken, CancellationToken ct)
    {
        var uid = tenant.UserId ?? throw new UnauthorizedException();
        var now = DateTime.UtcNow;
        var current = TrustedBrowserRules.IsValidToken(currentToken) ? Hash(currentToken!) : null;
        var rows = await db.TrustedBrowsers.AsNoTracking().Where(b => b.UserId == uid && b.RevokedAtUtc == null && b.ConfirmedAtUtc != null && b.ExpiresAtUtc > now)
            .OrderByDescending(b => b.LastUsedAtUtc).ToListAsync(ct);
        return rows.Select(b => new TrustedBrowserDto(b.PublicId, b.Label, b.IpAddress, b.CreatedAtUtc, b.LastUsedAtUtc, b.ExpiresAtUtc, current is not null && b.TokenHash == current)).ToList();
    }

    public async Task RevokeMineAsync(Guid publicId, CancellationToken ct)
    {
        var uid = tenant.UserId ?? throw new UnauthorizedException();
        var row = await db.TrustedBrowsers.FirstOrDefaultAsync(b => b.PublicId == publicId && b.UserId == uid && b.RevokedAtUtc == null, ct)
            ?? throw new NotFoundException(TrustedBrowserRules.NotFoundMessage, feminine: true);
        row.RevokedAtUtc = DateTime.UtcNow;
        db.SuppressAudit = true;
        try { await db.SaveChangesAsync(ct); } finally { db.SuppressAudit = false; }
        await security.WriteAsync(SecurityEventTypes.Login, SecurityOutcomes.Success, uid, tenant.TenantId, new { stage = "browser_revoked", browser = row.Label }, ct);
    }

    /// <summary>Quita la confianza de TODAS las computadoras del usuario (al cambiar o restablecer la contraseña).</summary>
    public async Task RevokeAllAsync(int userId, CancellationToken ct)
    {
        var open = await db.TrustedBrowsers.Where(b => b.UserId == userId && b.RevokedAtUtc == null).ToListAsync(ct);
        if (open.Count == 0) return;
        var now = DateTime.UtcNow;
        foreach (var b in open) b.RevokedAtUtc = now;
        db.SuppressAudit = true;
        try { await db.SaveChangesAsync(ct); } finally { db.SuppressAudit = false; }
        await security.WriteAsync(SecurityEventTypes.Login, SecurityOutcomes.Success, userId, null, new { stage = "browsers_revoked_all", count = open.Count }, ct);
    }

    private static string? Trunc(string? s, int max) => s is null ? null : s.Length <= max ? s : s[..max];
}
