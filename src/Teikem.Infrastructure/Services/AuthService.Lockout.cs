using Microsoft.AspNetCore.Identity;
using Teikem.Domain.Identity;
using Teikem.Infrastructure.Abstractions;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 100: espera que CRECE con los bloqueos repetidos de una cuenta (decidido con Luis, 2026-10-17). Cada 5 intentos fallidos seguidos bloquean la cuenta:
/// el primer bloqueo dura 1 minuto, el segundo 5, el tercero 15 y del cuarto en adelante 60. Si pasan 24 horas sin otro bloqueo se empieza de nuevo. Nunca es
/// para siempre, así nadie puede dejar fuera a un usuario real a propósito. Lo usan por igual el login, el segundo paso (MFA), la reautenticación y el cambio de contraseña.
/// </summary>
public static class LockoutPolicy
{
    public static readonly IReadOnlyList<int> Minutes = new[] { 1, 5, 15, 60 };
    public const int ResetAfterHours = 24;

    /// <summary>Nivel que corresponde al próximo bloqueo: el guardado, o 0 si el último bloqueo fue hace más de 24 horas (o nunca).</summary>
    public static int LevelFor(int storedLevel, DateTime? lastLockoutUtc, DateTime nowUtc)
        => lastLockoutUtc is DateTime at && at > nowUtc.AddHours(-ResetAfterHours) ? Math.Max(0, storedLevel) : 0;

    public static int MinutesFor(int level) => Minutes[Math.Min(Math.Max(0, level), Minutes.Count - 1)];
}

public sealed partial class AuthService
{
    private static string? _dummyHash;

    /// <summary>
    /// Sin enumeración de usuarios por el tiempo de respuesta: cuando el usuario no existe, está inactivo o bloqueado también se hace una verificación de contraseña
    /// (mismo costo que una real) antes de responder lo mismo que una contraseña equivocada.
    /// </summary>
    private void BurnPasswordCheck(string? password)
    {
        var dummy = new ApplicationUser { Id = 0, UserName = "x" };
        _dummyHash ??= users.PasswordHasher.HashPassword(dummy, "Teikem-sin-usuario-2026");
        users.PasswordHasher.VerifyHashedPassword(dummy, _dummyHash, password ?? string.Empty);
    }

    /// <summary>
    /// Cuenta un intento fallido. Si con él la cuenta queda bloqueada, fija la espera según el nivel de bloqueos repetidos (<see cref="LockoutPolicy"/>), avisa por correo al
    /// dueño de la cuenta y devuelve los minutos; si no, null.
    /// </summary>
    private async Task<int?> RegisterFailureAsync(ApplicationUser user, CancellationToken ct)
    {
        await users.AccessFailedAsync(user);
        if (!await users.IsLockedOutAsync(user)) return null;
        var now = DateTime.UtcNow;
        var level = LockoutPolicy.LevelFor(user.LockoutLevel, user.LockoutLevelAtUtc, now);
        var minutes = LockoutPolicy.MinutesFor(level);
        user.LockoutLevel = level + 1;
        user.LockoutLevelAtUtc = now;
        user.LockoutEnd = now.AddMinutes(minutes);
        await users.UpdateAsync(user);
        await NotifyLockoutAsync(user, minutes, ct);
        return minutes;
    }

    /// <summary>Aviso al dueño de la cuenta: se bloqueó unos minutos por intentos fallidos. Un fallo del correo nunca impide responder.</summary>
    private async Task NotifyLockoutAsync(ApplicationUser user, int minutes, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(user.Email) || !email.IsConfigured) return;
        try
        {
            var when = minutes == 1 ? "1 minuto" : $"{minutes} minutos";
            var html = $"<p>Hola {System.Net.WebUtility.HtmlEncode(user.FullName ?? user.Email)},</p>"
                + $"<p>Su cuenta se bloqueó por <b>{when}</b> porque hubo varios intentos fallidos de entrar con su contraseña"
                + (string.IsNullOrWhiteSpace(tenant.IpAddress) ? "" : $" (desde la dirección {System.Net.WebUtility.HtmlEncode(tenant.IpAddress)})") + ".</p>"
                + "<p>Si fue usted, espere ese tiempo y vuelva a intentar. Si no fue usted, cambie su contraseña en cuanto pueda: alguien puede estar probando contraseñas en su cuenta.</p>";
            await email.SendAsync(user.Email, user.FullName, "Su cuenta se bloqueó temporalmente", html, ct, await CompanyNameForEmailAsync(user, null, ct));
        }
        catch (Exception)
        {
            // el correo es un aviso: no se rompe el login por él
        }
    }
}
