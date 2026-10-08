using System.Net;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Constants;
using Teikem.Domain.Identity;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// «Olvidé mi contraseña» (2026-10-07): el enlace del correo vale <see cref="LinkMinutes"/> minutos (por defecto 60). La web base del enlace sale de la
/// configuración (<c>Auth:PasswordReset:WebBaseUrl</c>), nunca de la petición, para que nadie pueda mandar a la víctima a otro sitio.
/// </summary>
public sealed class PasswordResetOptions
{
    /// <summary>Dirección de la web (https://app.teikem.com). Sin ella no se puede armar el enlace.</summary>
    public string? WebBaseUrl { get; set; }
    public int LinkMinutes { get; set; } = 60;
    /// <summary>Solo desarrollo: devolver el enlace en la respuesta para probar sin proveedor de correo.</summary>
    public bool ReturnLinkInResponse { get; set; }
}

public sealed partial class AuthService
{
    /// <summary>Cuánto vale la contraseña temporal que pone un administrador.</summary>
    public const int TempPasswordMinutes = 10;
    public const string ForgotPasswordMessage = "Si el correo está registrado, le enviamos un enlace para restablecer su contraseña.";
    public const string ResetLinkInvalidMessage = "El enlace no es válido o venció. Pida uno nuevo.";
    public const string TempPasswordSelfMessage = "No puede ponerse una contraseña temporal a sí mismo.";
    public const string BreachedPasswordMessage = "Esta contraseña aparece en brechas conocidas; elija otra.";

    private ITimeLimitedDataProtector ResetLinkProtector => dataProtection.CreateProtector("Teikem.PasswordReset").ToTimeLimitedDataProtector();

    private static void ClearTempPassword(ApplicationUser user)
    {
        user.TempPasswordExpiresUtc = null;
        user.PreviousPasswordHash = null;
        user.PreviousOnboardingRequired = null;
        user.PreviousMustChangePassword = null;
    }

    /// <summary>Si la contraseña temporal venció sin usarse, restaura la anterior (y cómo estaban los avisos de primer ingreso). Perezoso: se llama al intentar entrar.</summary>
    private async Task ExpireTempPasswordIfDueAsync(ApplicationUser user)
    {
        if (user.TempPasswordExpiresUtc is not DateTime exp || exp > DateTime.UtcNow) return;
        if (!string.IsNullOrEmpty(user.PreviousPasswordHash)) user.PasswordHash = user.PreviousPasswordHash;
        user.OnboardingRequired = user.PreviousOnboardingRequired ?? user.OnboardingRequired;
        user.MustChangePassword = user.PreviousMustChangePassword ?? user.MustChangePassword;
        ClearTempPassword(user);
        await users.UpdateAsync(user);
        await security.WriteAsync(SecurityEventTypes.PasswordChange, SecurityOutcomes.Success, user.Id, null, new { stage = "temp_expired_restored" }, default);
    }

    private async Task RevokeSessionsAsync(ApplicationUser user, CancellationToken ct)
    {
        cache.Remove(JwtTokenService.StampCacheKey(user.Id));
        var open = await db.RefreshTokens.IgnoreQueryFilters().Where(t => t.UserId == user.Id && t.RevokedAtUtc == null).ToListAsync(ct);
        foreach (var t in open) t.RevokedAtUtc = DateTime.UtcNow;
        db.SuppressAudit = true; await db.SaveChangesAsync(ct); db.SuppressAudit = false;
    }

    // ---------------- Administración: contraseña temporal ----------------

    /// <summary>
    /// Un administrador pone una contraseña temporal a otro usuario de su compañía (se la da en persona). Vale <see cref="TempPasswordMinutes"/> minutos:
    /// si nadie entra con ella en ese tiempo, vuelve la anterior; si entra, el sistema le obliga a poner la suya. Cierra las sesiones abiertas del usuario.
    /// </summary>
    public async Task<TemporaryPasswordDto> AdminSetTemporaryPasswordAsync(int userId, TemporaryPasswordRequest? req, CancellationToken ct)
    {
        var tenantId = ((TenantContext)tenant).RequireTenantId();
        if (!await db.UserTenants.AnyAsync(m => m.UserId == userId && m.TenantId == tenantId, ct)) throw new NotFoundException("Usuario", userId);
        if (userId == tenant.UserId) throw new ConflictException(TempPasswordSelfMessage);
        var user = await users.FindByIdAsync(userId.ToString()) ?? throw new NotFoundException("Usuario", userId);
        var kind = user.UserKindLookupId is null ? UserKinds.Internal : (await lookups.GetAsync(user.UserKindLookupId.Value, ct))?.InternalCode ?? UserKinds.Internal;
        if (kind == UserKinds.Portal) throw new NotFoundException("Usuario", userId);

        var pwd = string.IsNullOrWhiteSpace(req?.Password) ? UserAdminService.GenerateTemporaryPassword() : req!.Password!;
        if (await breachChecker.IsBreachedAsync(pwd, ct)) throw new ValidationException("password", BreachedPasswordMessage);

        await ExpireTempPasswordIfDueAsync(user);
        // Una segunda temporal encima de una vigente conserva la contraseña de ANTES de la primera (no la temporal).
        var pending = user.TempPasswordExpiresUtc is not null;
        var prevHash = pending ? user.PreviousPasswordHash : user.PasswordHash;
        var prevOnb = pending ? user.PreviousOnboardingRequired : user.OnboardingRequired;
        var prevMust = pending ? user.PreviousMustChangePassword : user.MustChangePassword;

        var token = await users.GeneratePasswordResetTokenAsync(user);
        var result = await users.ResetPasswordAsync(user, token, pwd);
        if (!result.Succeeded)
        {
            await security.WriteAsync(SecurityEventTypes.PasswordChange, SecurityOutcomes.Failure, user.Id, tenantId, new { stage = "admin_temp", errors = result.Errors.Select(e => e.Code) }, ct);
            throw new ValidationException("password", string.Join(" ", result.Errors.Select(e => e.Description)));
        }
        user = await users.FindByIdAsync(userId.ToString()) ?? throw new NotFoundException("Usuario", userId);
        var expires = DateTime.UtcNow.AddMinutes(TempPasswordMinutes);
        user.PreviousPasswordHash = prevHash;
        user.PreviousOnboardingRequired = prevOnb;
        user.PreviousMustChangePassword = prevMust;
        user.TempPasswordExpiresUtc = expires;
        // Entra por el flujo de primer ingreso: obliga a poner una contraseña propia antes de dar tokens.
        user.OnboardingRequired = true;
        user.MustChangePassword = true;
        await users.UpdateAsync(user);
        await RevokeSessionsAsync(user, ct);
        await security.WriteAsync(SecurityEventTypes.PasswordChange, SecurityOutcomes.Success, user.Id, tenantId, new { stage = "admin_temp", by = tenant.UserId, minutes = TempPasswordMinutes }, ct);
        return new TemporaryPasswordDto(pwd, expires, TempPasswordMinutes);
    }

    // ---------------- Login: olvidé mi contraseña ----------------

    /// <summary>Manda al correo un enlace de un solo uso. La respuesta es la misma exista o no el correo (sin enumeración de usuarios).</summary>
    public async Task<ForgotPasswordResultDto> RequestPasswordResetAsync(ForgotPasswordRequest req, CancellationToken ct)
    {
        var opts = passwordReset?.Value ?? new PasswordResetOptions();
        var address = req.Email?.Trim();
        var user = string.IsNullOrWhiteSpace(address) ? null : await users.FindByEmailAsync(address);
        if (user is null || !user.IsActive || string.IsNullOrWhiteSpace(user.Email))
        {
            await security.WriteAsync(SecurityEventTypes.PasswordChange, SecurityOutcomes.Failure, null, null, new { stage = "forgot_unknown", email = address }, ct);
            return new ForgotPasswordResultDto(ForgotPasswordMessage, null);
        }
        var kind = user.UserKindLookupId is null ? UserKinds.Internal : (await lookups.GetAsync(user.UserKindLookupId.Value, ct))?.InternalCode ?? UserKinds.Internal;
        if (kind == UserKinds.Portal) return new ForgotPasswordResultDto(ForgotPasswordMessage, null);

        var raw = await users.GeneratePasswordResetTokenAsync(user);
        var minutes = Math.Max(5, opts.LinkMinutes);
        var protectedToken = ResetLinkProtector.Protect(raw, TimeSpan.FromMinutes(minutes));
        var link = $"{(opts.WebBaseUrl ?? string.Empty).TrimEnd('/')}/reset-password?email={Uri.EscapeDataString(user.Email)}&token={Uri.EscapeDataString(protectedToken)}";
        var html = $"""
            <div style="font-family:Arial,Helvetica,sans-serif;max-width:480px">
              <h2 style="color:#0B2C66">Restablecer su contraseña</h2>
              <p>Hola{(string.IsNullOrWhiteSpace(user.FullName) ? "" : " " + WebUtility.HtmlEncode(user.FullName))}:</p>
              <p>Pidieron restablecer la contraseña de su cuenta de Teikem. Use este enlace para elegir una nueva:</p>
              <p><a href="{WebUtility.HtmlEncode(link)}" style="display:inline-block;background:#1F6FE5;color:#fff;padding:12px 20px;border-radius:6px;text-decoration:none">Elegir nueva contraseña</a></p>
              <p>El enlace vale {minutes} minutos y se puede usar una sola vez. Si usted no lo pidió, ignore este correo: su contraseña no cambia.</p>
            </div>
            """;
        if (!string.IsNullOrWhiteSpace(opts.WebBaseUrl))
        {
            try { await email.SendAsync(user.Email, user.FullName, "Restablecer su contraseña de Teikem", html, ct, await CompanyNameForEmailAsync(user, null, ct)); }
            catch (Exception ex) when (ex is not OperationCanceledException) { /* misma respuesta: el log del proveedor registra el fallo */ }
        }
        await security.WriteAsync(SecurityEventTypes.PasswordChange, SecurityOutcomes.Success, user.Id, null, new { stage = "forgot_sent", provider = email.IsConfigured }, ct);
        return new ForgotPasswordResultDto(ForgotPasswordMessage, opts.ReturnLinkInResponse ? link : null);
    }

    /// <summary>Pone la contraseña nueva con el enlace del correo. Cierra todas las sesiones y levanta el bloqueo por intentos fallidos.</summary>
    public async Task ResetPasswordWithLinkAsync(ResetPasswordRequest req, CancellationToken ct)
    {
        var invalid = new ValidationException("token", ResetLinkInvalidMessage);
        var address = req.Email?.Trim();
        var user = string.IsNullOrWhiteSpace(address) ? null : await users.FindByEmailAsync(address);
        if (user is null || !user.IsActive || string.IsNullOrWhiteSpace(req.Token)) throw invalid;
        string raw;
        try { raw = ResetLinkProtector.Unprotect(req.Token); }
        catch (Exception ex) when (ex is not OperationCanceledException) { throw invalid; }

        var pwd = req.NewPassword ?? string.Empty;
        if (await breachChecker.IsBreachedAsync(pwd, ct)) throw new ValidationException("newPassword", BreachedPasswordMessage);
        var result = await users.ResetPasswordAsync(user, raw, pwd);
        if (!result.Succeeded)
        {
            if (result.Errors.Any(e => e.Code == "InvalidToken")) throw invalid;
            await security.WriteAsync(SecurityEventTypes.PasswordChange, SecurityOutcomes.Failure, user.Id, null, new { stage = "reset_link", errors = result.Errors.Select(e => e.Code) }, ct);
            throw new ValidationException("newPassword", string.Join(" ", result.Errors.Select(e => e.Description)));
        }
        user = await users.FindByIdAsync(user.Id.ToString()) ?? throw invalid;
        user.MustChangePassword = false;
        ClearTempPassword(user);
        // Recibir el enlace en su correo demuestra que es suyo.
        if (user.EmailVerifiedUtc is null) { user.EmailVerifiedUtc = DateTime.UtcNow; user.EmailConfirmed = true; }
        await users.UpdateAsync(user);
        await users.ResetAccessFailedCountAsync(user);
        await users.SetLockoutEndDateAsync(user, null);
        await RevokeSessionsAsync(user, ct);
        await security.WriteAsync(SecurityEventTypes.PasswordChange, SecurityOutcomes.Success, user.Id, null, new { stage = "reset_link" }, ct);
    }
}
