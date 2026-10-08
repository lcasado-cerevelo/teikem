using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Constants;
using Teikem.Domain.Identity;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Primer ingreso obligatorio (pedido de Luis, 2026-09-30), para usuarios nuevos y existentes. La contraseña la pone un
/// administrador, así que el primer ingreso exige, en orden: (1) verificar el correo con un código de 6 dígitos (Brevo),
/// (2) poner una contraseña propia y (3) configurar el MFA. Hasta completarlo, el login responde <c>onboarding_required</c> con
/// un challenge token (sin tokens de acceso) y el login por PIN de la app se rechaza.
/// </summary>
public sealed class OnboardingOptions
{
    /// <summary>Apagarlo solo en ambientes de prueba automatizada (smoke): en producción siempre encendido.</summary>
    public bool Enabled { get; set; } = true;
    /// <summary>Vida del challenge token del primer ingreso (los tres pasos pueden tomar unos minutos).</summary>
    public int ChallengeMinutes { get; set; } = 30;
    /// <summary>Solo desarrollo: devolver el código del correo en la respuesta para probar sin proveedor de correo.</summary>
    public bool ReturnEmailCodeInResponse { get; set; }
}

public sealed partial class AuthService
{
    public const string OnboardingEmailFirstMessage = "Verifique primero su correo.";
    public const string OnboardingIncompleteMessage = "Complete primero la verificación del correo y el cambio de contraseña.";
    public const string OnboardingDeviceMessage = "Complete primero su primer ingreso en la web (correo, contraseña y verificación en dos pasos).";
    public const string OnboardingEmailAlreadyVerifiedMessage = "Su correo ya está verificado.";
    public const string OnboardingPasswordAlreadyChangedMessage = "Ya cambió su contraseña.";
    public const string InvalidEmailCodeMessage = "El código no es válido o venció.";
    public const string SamePasswordMessage = "La contraseña nueva debe ser distinta de la que le dieron.";
    public const string EmailNotSentMessage = "No se pudo enviar el correo. Intente de nuevo en unos minutos.";

    /// <summary>¿Le falta el primer ingreso? (con el flag apagado por configuración, nunca).</summary>
    private bool OnboardingPending(ApplicationUser user, bool hasTotp)
        => onboarding.Value.Enabled && user.OnboardingRequired && (user.EmailVerifiedUtc is null || user.MustChangePassword || !hasTotp);

    private static OnboardingStateDto OnboardingState(ApplicationUser user, bool hasTotp)
        => new(MaskEmail(user.Email), user.EmailVerifiedUtc is not null, user.MustChangePassword, hasTotp);

    /// <summary>j***@empresa.com</summary>
    public static string MaskEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return string.Empty;
        var at = email.IndexOf('@');
        if (at <= 0) return email;
        return email[..1] + "***" + email[at..];
    }

    private (string Token, DateTime ExpiresAtUtc) OnboardingChallenge(ApplicationUser user, int tenantId, string? deviceInfo)
        => jwt.CreateMfaChallengeToken(user, tenantId, deviceInfo, TimeSpan.FromMinutes(onboarding.Value.ChallengeMinutes));

    private async Task<ApplicationUser> OnboardingUserAsync(int userId)
        => await users.FindByIdAsync(userId.ToString()) is { IsActive: true } u ? u : throw new UnauthorizedException();

    /// <summary>Qué le falta del primer ingreso (con el challenge token).</summary>
    public async Task<OnboardingStateDto> GetOnboardingAsync(int userId, CancellationToken ct)
    {
        var user = await OnboardingUserAsync(userId);
        return OnboardingState(user, await ConfirmedTotpAsync(user.Id, ct) is not null);
    }

    /// <summary>Paso 1a: manda al correo un código de 6 dígitos (el proveedor de tokens de correo de Identity: vale unos minutos).</summary>
    public async Task<OnboardingEmailSentDto> SendOnboardingEmailCodeAsync(int userId, int tenantId, CancellationToken ct)
    {
        var user = await OnboardingUserAsync(userId);
        if (user.EmailVerifiedUtc is not null) throw new ConflictException(OnboardingEmailAlreadyVerifiedMessage);
        var code = await users.GenerateTwoFactorTokenAsync(user, TokenOptions.DefaultEmailProvider);
        var html = $"""
            <div style="font-family:Arial,Helvetica,sans-serif;max-width:480px">
              <h2 style="color:#0B2C66">Verifique su correo</h2>
              <p>Hola{(string.IsNullOrWhiteSpace(user.FullName) ? "" : " " + WebUtility.HtmlEncode(user.FullName))}:</p>
              <p>Su código de verificación de Teikem es:</p>
              <p style="font-size:28px;font-weight:bold;letter-spacing:6px">{code}</p>
              <p>Vence en unos minutos. Si usted no está entrando a Teikem, ignore este correo.</p>
            </div>
            """;
        try
        {
            await email.SendAsync(user.Email!, user.FullName, "Su código de verificación de Teikem", html, ct, await CompanyNameForEmailAsync(user, tenantId, ct));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new ConflictException(EmailNotSentMessage);
        }
        await security.WriteAsync(SecurityEventTypes.Login, SecurityOutcomes.Success, user.Id, tenantId, new { stage = "onboarding_email_sent", provider = email.IsConfigured }, ct);
        return new OnboardingEmailSentDto(MaskEmail(user.Email), email.IsConfigured, onboarding.Value.ReturnEmailCodeInResponse ? code : null);
    }

    /// <summary>Paso 1b: confirma el código. Un código equivocado cuenta un intento (bloqueo de la cuenta como el login).</summary>
    public async Task<OnboardingStepDto> VerifyOnboardingEmailAsync(int userId, int tenantId, OnboardingCodeRequest req, CancellationToken ct)
    {
        var user = await OnboardingUserAsync(userId);
        var hasTotp = await ConfirmedTotpAsync(user.Id, ct) is not null;
        if (user.EmailVerifiedUtc is not null) return new OnboardingStepDto(OnboardingState(user, hasTotp), null);
        var code = (req.Code ?? string.Empty).Trim();
        if (await users.IsLockedOutAsync(user) || code.Length == 0
            || !await users.VerifyTwoFactorTokenAsync(user, TokenOptions.DefaultEmailProvider, code))
        {
            await users.AccessFailedAsync(user);
            await security.WriteAsync(SecurityEventTypes.Login, SecurityOutcomes.Failure, user.Id, tenantId, new { stage = "onboarding_email" }, ct);
            throw new ValidationException("code", InvalidEmailCodeMessage);
        }
        await users.ResetAccessFailedCountAsync(user);
        user.EmailVerifiedUtc = DateTime.UtcNow;
        user.EmailConfirmed = true;
        await users.UpdateAsync(user);
        await security.WriteAsync(SecurityEventTypes.Login, SecurityOutcomes.Success, user.Id, tenantId, new { stage = "onboarding_email_verified" }, ct);
        return new OnboardingStepDto(OnboardingState(user, hasTotp), null);
    }

    /// <summary>
    /// Paso 2: la contraseña propia (reemplaza la que dio el administrador). Exige el correo verificado. Rota el sello de
    /// seguridad, así que devuelve un challenge token nuevo para seguir con el MFA.
    /// </summary>
    public async Task<OnboardingStepDto> SetOnboardingPasswordAsync(int userId, int tenantId, string? deviceInfo, OnboardingPasswordRequest req, CancellationToken ct)
    {
        var user = await OnboardingUserAsync(userId);
        if (user.EmailVerifiedUtc is null) throw new ConflictException(OnboardingEmailFirstMessage);
        if (!user.MustChangePassword) throw new ConflictException(OnboardingPasswordAlreadyChangedMessage);
        var pwd = req.NewPassword ?? string.Empty;
        if (await users.CheckPasswordAsync(user, pwd)) throw new ValidationException("newPassword", SamePasswordMessage);
        if (await breachChecker.IsBreachedAsync(pwd, ct)) throw new ValidationException("newPassword", "Esta contraseña aparece en brechas conocidas; elija otra.");
        var resetToken = await users.GeneratePasswordResetTokenAsync(user);
        var result = await users.ResetPasswordAsync(user, resetToken, pwd);
        if (!result.Succeeded)
        {
            await security.WriteAsync(SecurityEventTypes.PasswordChange, SecurityOutcomes.Failure, user.Id, tenantId, new { stage = "onboarding", errors = result.Errors.Select(e => e.Code) }, ct);
            throw new ValidationException("newPassword", string.Join(" ", result.Errors.Select(e => e.Description)));
        }
        user = await OnboardingUserAsync(userId);
        user.MustChangePassword = false;
        ClearTempPassword(user); // puso la suya: la temporal del administrador ya no se puede restaurar
        await users.UpdateAsync(user);
        // El sello rotó: se quita el que el API tiene en caché (60 s) para que el challenge nuevo, firmado con el sello nuevo, se
        // acepte ya en la siguiente petición (antes, "Comenzar configuración" fallaba la primera vez).
        cache.Remove(JwtTokenService.StampCacheKey(user.Id));
        // Cambio de contraseña: se cierran las sesiones que hubiera (Identity ya rotó el SecurityStamp).
        var open = await db.RefreshTokens.IgnoreQueryFilters().Where(t => t.UserId == user.Id && t.RevokedAtUtc == null).ToListAsync(ct);
        foreach (var t in open) t.RevokedAtUtc = DateTime.UtcNow;
        db.SuppressAudit = true; await db.SaveChangesAsync(ct); db.SuppressAudit = false;
        await security.WriteAsync(SecurityEventTypes.PasswordChange, SecurityOutcomes.Success, user.Id, tenantId, new { stage = "onboarding" }, ct);
        var hasTotp = await ConfirmedTotpAsync(user.Id, ct) is not null;
        return new OnboardingStepDto(OnboardingState(user, hasTotp), OnboardingChallenge(user, tenantId, deviceInfo).Token);
    }
}
