namespace Teikem.Domain.Security;

/// <summary>
/// Lote 8A — reglas puras del PIN con el que el usuario entra en los aparatos de almacén (UserPin):
/// - 4 a 6 dígitos ASCII (se ignoran espacios al inicio y al final): 'El PIN debe tener de 4 a 6 dígitos.'.
/// - Ni todos iguales (1111) ni secuencias consecutivas ascendentes o descendentes (1234, 98765): 'El PIN no puede ser una
///   secuencia trivial.'.
/// - 5 intentos fallidos seguidos → bloqueo de 15 minutos ('PIN bloqueado por 15 minutos.', HTTP 423); al bloquear, el
///   contador vuelve a cero; un acierto también lo reinicia y quita el bloqueo (RegisterSuccess).
/// El hash se calcula con el mismo PasswordHasher de Identity que las contraseñas (PinService); aquí no hay criptografía.
/// </summary>
public static class PinRules
{
    public const int MinLength = 4;
    public const int MaxLength = 6;
    public const int MaxFailedAttempts = 5;
    public static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(15);

    public const string FormatMessage = "El PIN debe tener de 4 a 6 dígitos.";
    public const string TrivialMessage = "El PIN no puede ser una secuencia trivial.";
    public const string LockedMessage = "PIN bloqueado por 15 minutos.";

    /// <summary>Mensaje de error del PIN, o null si es válido.</summary>
    public static string? Validate(string? pin)
    {
        var p = pin?.Trim() ?? string.Empty;
        if (p.Length is < MinLength or > MaxLength || !p.All(char.IsAsciiDigit)) return FormatMessage;
        return IsTrivial(p) ? TrivialMessage : null;
    }

    /// <summary>Todos los dígitos iguales o una secuencia consecutiva (paso +1 o −1, sin dar la vuelta de 9 a 0).</summary>
    public static bool IsTrivial(string digits)
    {
        if (digits.Length < 2) return true;
        if (digits.All(c => c == digits[0])) return true;
        return HasStep(digits, 1) || HasStep(digits, -1);
    }

    private static bool HasStep(string p, int step)
    {
        for (var i = 1; i < p.Length; i++)
            if (p[i] - p[i - 1] != step) return false;
        return true;
    }

    /// <summary>¿Sigue bloqueado a esta hora?</summary>
    public static bool IsLocked(DateTime? lockedUntilUtc, DateTime nowUtc) => lockedUntilUtc is DateTime l && l > nowUtc;

    /// <summary>
    /// Un intento fallido más: devuelve el contador y el bloqueo nuevos. Al llegar a 5 se bloquea 15 minutos desde ahora y el
    /// contador vuelve a cero (tras el bloqueo hay otros 5 intentos).
    /// </summary>
    public static (int FailedCount, DateTime? LockedUntilUtc) RegisterFailure(int failedCount, DateTime nowUtc)
    {
        var next = Math.Max(0, failedCount) + 1;
        return next >= MaxFailedAttempts ? (0, nowUtc.Add(LockDuration)) : (next, null);
    }

    /// <summary>Un acierto (PIN correcto fuera de bloqueo): contador a cero y sin bloqueo.</summary>
    public static (int FailedCount, DateTime? LockedUntilUtc) RegisterSuccess() => (0, null);
}
