using System.Text.RegularExpressions;

namespace Teikem.Domain.Integrations;

public static class RemoteAuthKinds
{
    public const string Password = "PASSWORD";
    /// <summary>Llave privada (solo SFTP).</summary>
    public const string Key = "KEY";
    public static readonly IReadOnlyList<string> All = [Password, Key];
}

/// <summary>
/// Configuración NO secreta de un conector SFTP o FTP. Dir = carpeta remota donde llegan los archivos; ProcessedDir y ErrorDir = a dónde se MUEVEN (nunca se borran). HostFingerprint: SFTP exige
/// fijar la huella del servidor (SHA-256) para no hablar con un impostor. UseFtps: FTP con TLS explícito (si no, FTP sin cifrar).
/// </summary>
public sealed record RemoteConfig(
    string Host, int Port, string Username, string Dir = "/", string ProcessedDir = "procesados", string ErrorDir = "errores",
    string AuthKind = RemoteAuthKinds.Password, string? HostFingerprint = null, bool UseFtps = true,
    EmailTextMatch? FileName = null, IReadOnlyList<string>? FileTypes = null);

/// <summary>Un archivo en el servidor remoto.</summary>
public sealed record RemoteFileInfo(string Name, long Size, DateTime ModifiedUtc);

/// <summary>Lo secreto de un conector remoto (JSON cifrado en Integration.SecretEnc).</summary>
public sealed record RemoteSecret(string? Password = null, string? PrivateKey = null, string? Passphrase = null);

/// <summary>
/// Lote 134 (P11) — reglas puras de los conectores SFTP/FTP, para cualquier cliente: validación de la configuración y qué archivos se toman (por nombre y tipo, sin los que todavía se están
/// escribiendo). El servidor remoto es entrada NO confiable: nunca se ejecuta nada y los nombres se tratan como texto.
/// </summary>
public static class RemoteRules
{
    /// <summary>Un archivo modificado hace menos de esto se deja para la próxima corrida (puede estar subiéndose todavía).</summary>
    public static readonly TimeSpan SettleTime = TimeSpan.FromSeconds(10);
    public const int MaxFilesPerRun = 100;

    public static string HostRequiredMessage => "Indique el servidor (host) del conector.";
    public static string PortMessage => "El puerto debe estar entre 1 y 65535.";
    public static string UsernameRequiredMessage => "Indique el usuario del conector.";
    public static string SecretRequiredMessage(string authKind) => authKind == RemoteAuthKinds.Key ? "Indique la llave privada del conector." : "Indique la contraseña del conector.";
    public static string DirRequiredMessage => "Indique la carpeta remota donde llegan los archivos.";
    public static string DirInvalidMessage => "Las carpetas no pueden contener «..» ni saltos de línea.";
    public static string UnknownAuthMessage(string kind) => $"Autenticación desconocida: {kind}. Use PASSWORD o KEY.";
    public static string KeyOnlySftpMessage => "La llave privada solo se usa con SFTP.";
    public static string FingerprintRequiredMessage => "Falta confirmar la huella del servidor SFTP: use «Probar conexión», compare la huella con la del proveedor y péguela en el campo.";
    public static string FingerprintFormatMessage => "La huella debe ser la SHA-256 del servidor (SHA256:… en base64).";
    public static string FingerprintMismatchMessage(string seen) => $"La huella del servidor no coincide con la configurada (el servidor presenta {seen}). Si el proveedor la cambió, confírmela y actualice el campo.";
    public static string FtpPlainWarning => "Atención: FTP sin cifrar; usuario, contraseña y archivos viajan en claro. Use FTPS o SFTP si el servidor lo admite.";

    public static IReadOnlyDictionary<string, string> Validate(RemoteConfig? c, string kind, bool hasSecret)
    {
        var errors = new Dictionary<string, string>(StringComparer.Ordinal);
        if (c is null) { errors["remote"] = HostRequiredMessage; return errors; }
        if (string.IsNullOrWhiteSpace(c.Host)) errors["remote.host"] = HostRequiredMessage;
        if (c.Port is < 1 or > 65535) errors["remote.port"] = PortMessage;
        if (string.IsNullOrWhiteSpace(c.Username)) errors["remote.username"] = UsernameRequiredMessage;
        var auth = (c.AuthKind ?? RemoteAuthKinds.Password).Trim().ToUpperInvariant();
        if (!RemoteAuthKinds.All.Contains(auth)) errors["remote.authKind"] = UnknownAuthMessage(c.AuthKind ?? string.Empty);
        else
        {
            if (auth == RemoteAuthKinds.Key && kind != IntegrationConnectorKinds.Sftp) errors["remote.authKind"] = KeyOnlySftpMessage;
            if (!hasSecret) errors["remote.secret"] = SecretRequiredMessage(auth);
        }
        if (string.IsNullOrWhiteSpace(c.Dir)) errors["remote.dir"] = DirRequiredMessage;
        foreach (var (key, value) in new[] { ("remote.dir", c.Dir), ("remote.processedDir", c.ProcessedDir), ("remote.errorDir", c.ErrorDir) })
            if (!string.IsNullOrEmpty(value) && (value.Contains("..") || value.Contains('\n') || value.Contains('\r'))) errors[key] = DirInvalidMessage;
        if (!string.IsNullOrWhiteSpace(c.HostFingerprint) && NormalizeFingerprint(c.HostFingerprint) is null) errors["remote.hostFingerprint"] = FingerprintFormatMessage;
        if (c.FileName is not null)
        {
            var op = c.FileName.Operator?.Trim().ToUpperInvariant() ?? string.Empty;
            if (!EmailMatchOperators.All.Contains(op)) errors["remote.fileName"] = EmailRules.UnknownOperatorMessage(c.FileName.Operator ?? string.Empty);
            else if (string.IsNullOrWhiteSpace(c.FileName.Value)) errors["remote.fileName"] = EmailRules.MatchValueMessage;
        }
        return errors;
    }

    /// <summary>La huella como base64 SHA-256 sin el prefijo «SHA256:» ni el relleno «=»; null si no tiene la forma de una huella SHA-256 (43 caracteres base64).</summary>
    public static string? NormalizeFingerprint(string? fingerprint)
    {
        var f = fingerprint?.Trim();
        if (string.IsNullOrEmpty(f)) return null;
        if (f.StartsWith("SHA256:", StringComparison.OrdinalIgnoreCase)) f = f[7..];
        f = f.TrimEnd('=');
        return Regex.IsMatch(f, "^[A-Za-z0-9+/]{43}$") ? f : null;
    }

    public static bool FingerprintMatches(string? configured, string? seen)
    {
        var a = NormalizeFingerprint(configured);
        var b = NormalizeFingerprint(seen);
        return a is not null && b is not null && string.Equals(a, b, StringComparison.Ordinal);
    }

    /// <summary>Los archivos que se toman: por tipo (extensión; csv y txt por omisión) y nombre, sin los que se modificaron hace menos de <see cref="SettleTime"/>; por nombre y hasta <see cref="MaxFilesPerRun"/>.</summary>
    public static IReadOnlyList<RemoteFileInfo> SelectFiles(IEnumerable<RemoteFileInfo> files, RemoteConfig c, DateTime nowUtc)
    {
        var types = (c.FileTypes is { Count: > 0 } ? c.FileTypes : EmailRules.DefaultAttachmentTypes)
            .Select(t => t.Trim().ToLowerInvariant()).Select(t => t.StartsWith('.') ? t : "." + t).ToList();
        return files
            .Where(f => !string.IsNullOrWhiteSpace(f.Name) && !f.Name.StartsWith('.') && types.Contains(Path.GetExtension(f.Name).ToLowerInvariant()))
            .Where(f => EmailRules.TextMatches(f.Name, c.FileName))
            .Where(f => nowUtc - f.ModifiedUtc >= SettleTime)
            .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
            .Take(MaxFilesPerRun)
            .ToList();
    }

    /// <summary>Ruta remota segura: junta carpeta y nombre con «/», sin dobles barras.</summary>
    public static string Combine(string dir, string name) => (string.IsNullOrEmpty(dir) || dir == "/" ? string.Empty : dir.TrimEnd('/')) + "/" + name.TrimStart('/');
}
