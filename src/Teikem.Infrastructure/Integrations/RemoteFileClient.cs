using System.Text;
using FluentFTP;
using Renci.SshNet;
using Renci.SshNet.Common;
using Teikem.Domain.Integrations;

namespace Teikem.Infrastructure.Integrations;

/// <summary>Una sesión abierta con un servidor remoto (SFTP o FTP): listar, bajar y mover archivos. Nunca borra.</summary>
public interface IRemoteSession : IAsyncDisposable
{
    Task<IReadOnlyList<RemoteFileInfo>> ListAsync(string dir, CancellationToken ct);
    Task<byte[]> DownloadAsync(string dir, string name, CancellationToken ct);
    /// <summary>Mueve el archivo a otra carpeta remota (se crea si falta). Si ya hay uno con ese nombre se le pone la fecha por delante.</summary>
    Task MoveAsync(string dir, string name, string toDir, CancellationToken ct);
}

/// <summary>
/// Lote 134 (P11) — cliente de servidores remotos. Interfaz para poder probar sin servidor real y para cambiar de librería sin tocar las reglas. SFTP con SSH.NET (huella SHA-256 del servidor FIJADA: sin
/// ella no se conecta) y FTP/FTPS con FluentFTP (FTPS explícito con validación de certificado).
/// </summary>
public interface IRemoteFileClient
{
    Task<IRemoteSession> OpenAsync(string kind, RemoteConfig config, RemoteSecret secret, CancellationToken ct);
    /// <summary>Prueba la conexión y devuelve la huella que presenta el servidor SFTP (para fijarla) y cuántos archivos hay en la carpeta.</summary>
    Task<RemoteTestInfo> TestAsync(string kind, RemoteConfig config, RemoteSecret secret, CancellationToken ct);
}

public sealed record RemoteTestInfo(bool Ok, string Message, string? Fingerprint, int Files);

public sealed class RemoteFileClient : IRemoteFileClient
{
    public const string ConnectionFailedMessage = "No se pudo conectar con el servidor";

    public async Task<IRemoteSession> OpenAsync(string kind, RemoteConfig c, RemoteSecret secret, CancellationToken ct)
        => kind == IntegrationConnectorKinds.Sftp ? await SftpSession.OpenAsync(c, secret, null, ct) : await FtpSession.OpenAsync(c, secret, ct);

    public async Task<RemoteTestInfo> TestAsync(string kind, RemoteConfig c, RemoteSecret secret, CancellationToken ct)
    {
        string? seen = null;
        try
        {
            await using var session = kind == IntegrationConnectorKinds.Sftp ? await SftpSession.OpenAsync(c, secret, f => seen = f, ct) : await FtpSession.OpenAsync(c, secret, ct);
            var files = await session.ListAsync(c.Dir, ct);
            var ready = RemoteRules.SelectFiles(files, c, DateTime.UtcNow);
            var newest = files.Count == 0 ? string.Empty : $" Más reciente: {files.OrderByDescending(f => f.ModifiedUtc).First().ModifiedUtc:yyyy-MM-dd HH:mm:ss} UTC.";
            return new RemoteTestInfo(true, $"Conexión correcta: {files.Count} archivo(s) en {c.Dir}; {ready.Count} listo(s) para leer (por tipo y nombre, y sin los que se modificaron hace menos de {RemoteRules.SettleTime.TotalSeconds:0} s).{newest}", seen, files.Count);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            var fingerprint = seen is null ? string.Empty : $" Huella que presenta el servidor: SHA256:{seen}";
            return new RemoteTestInfo(false, $"{ConnectionFailedMessage}: {ex.Message}.{fingerprint}", seen, 0);
        }
    }
}

/// <summary>SFTP con SSH.NET. Las llamadas son síncronas en la librería: se corren fuera del hilo de la petición.</summary>
internal sealed class SftpSession(SftpClient client) : IRemoteSession
{
    public static Task<IRemoteSession> OpenAsync(RemoteConfig c, RemoteSecret secret, Action<string>? onFingerprint, CancellationToken ct) => Task.Run<IRemoteSession>(() =>
    {
        AuthenticationMethod auth;
        if ((c.AuthKind ?? RemoteAuthKinds.Password).ToUpperInvariant() == RemoteAuthKinds.Key)
        {
            using var key = new MemoryStream(Encoding.UTF8.GetBytes(secret.PrivateKey ?? string.Empty));
            auth = new PrivateKeyAuthenticationMethod(c.Username, new PrivateKeyFile(key, string.IsNullOrEmpty(secret.Passphrase) ? null : secret.Passphrase));
        }
        else auth = new PasswordAuthenticationMethod(c.Username, secret.Password ?? string.Empty);
        var client = new SftpClient(new ConnectionInfo(c.Host, c.Port, c.Username, auth) { Timeout = TimeSpan.FromSeconds(30) });
        string? mismatch = null;
        client.HostKeyReceived += (_, e) =>
        {
            onFingerprint?.Invoke(e.FingerPrintSHA256.TrimEnd('='));
            // sin huella fijada o distinta: NO se confía (así nunca se habla con un impostor)
            e.CanTrust = RemoteRules.FingerprintMatches(c.HostFingerprint, e.FingerPrintSHA256);
            if (!e.CanTrust) mismatch = e.FingerPrintSHA256.TrimEnd('=');
        };
        try { client.Connect(); }
        catch (SshConnectionException) when (mismatch is not null)
        {
            client.Dispose();
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(c.HostFingerprint) ? RemoteRules.FingerprintRequiredMessage : RemoteRules.FingerprintMismatchMessage("SHA256:" + mismatch));
        }
        catch { client.Dispose(); throw; }
        return new SftpSession(client);
    }, ct);

    public Task<IReadOnlyList<RemoteFileInfo>> ListAsync(string dir, CancellationToken ct) => Task.Run<IReadOnlyList<RemoteFileInfo>>(
        () => client.ListDirectory(dir).Where(f => f.IsRegularFile).Select(f => new RemoteFileInfo(f.Name, f.Length, f.LastWriteTimeUtc)).ToList(), ct);

    public Task<byte[]> DownloadAsync(string dir, string name, CancellationToken ct) => Task.Run(() =>
    {
        using var ms = new MemoryStream();
        client.DownloadFile(RemoteRules.Combine(dir, name), ms);
        return ms.ToArray();
    }, ct);

    public Task MoveAsync(string dir, string name, string toDir, CancellationToken ct) => Task.Run(() =>
    {
        var target = string.IsNullOrWhiteSpace(toDir) ? null : (toDir.StartsWith('/') ? toDir : RemoteRules.Combine(dir, toDir));
        if (target is null) return;
        if (!client.Exists(target)) client.CreateDirectory(target);
        var to = RemoteRules.Combine(target, name);
        if (client.Exists(to)) to = RemoteRules.Combine(target, $"{DateTime.UtcNow:yyyyMMddHHmmss}-{name}");
        client.RenameFile(RemoteRules.Combine(dir, name), to);
    }, ct);

    public ValueTask DisposeAsync()
    {
        try { if (client.IsConnected) client.Disconnect(); } catch (Exception) { /* cerrar nunca debe fallar */ }
        client.Dispose();
        return ValueTask.CompletedTask;
    }
}

/// <summary>FTP y FTPS (TLS explícito) con FluentFTP.</summary>
internal sealed class FtpSession(AsyncFtpClient client) : IRemoteSession
{
    public static async Task<IRemoteSession> OpenAsync(RemoteConfig c, RemoteSecret secret, CancellationToken ct)
    {
        var client = new AsyncFtpClient(c.Host, c.Username, secret.Password ?? string.Empty, c.Port);
        client.Config.EncryptionMode = c.UseFtps ? FtpEncryptionMode.Explicit : FtpEncryptionMode.None;
        client.Config.ValidateAnyCertificate = false;
        client.Config.ConnectTimeout = 30_000;
        client.Config.ReadTimeout = 30_000;
        try { await client.Connect(ct); }
        catch { client.Dispose(); throw; }
        return new FtpSession(client);
    }

    public async Task<IReadOnlyList<RemoteFileInfo>> ListAsync(string dir, CancellationToken ct)
    {
        var result = new List<RemoteFileInfo>();
        foreach (var i in (await client.GetListing(dir, ct)).Where(i => i.Type == FtpObjectType.File).Take(200))
        {
            // el LISTado solo trae minutos y la hora del servidor (sin zona): MDTM da la hora UTC exacta; si el servidor no lo admite se usa la del listado
            var modified = i.Modified.Kind == DateTimeKind.Utc ? i.Modified : i.Modified.ToUniversalTime();
            try
            {
                var exact = await client.GetModifiedTime(RemoteRules.Combine(dir, i.Name), ct);
                if (exact > DateTime.MinValue) modified = exact.Kind == DateTimeKind.Utc ? exact : DateTime.SpecifyKind(exact, DateTimeKind.Utc);
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { /* sin MDTM: se queda la hora del listado */ }
            result.Add(new RemoteFileInfo(i.Name, i.Size, modified));
        }
        return result;
    }

    public async Task<byte[]> DownloadAsync(string dir, string name, CancellationToken ct)
    {
        var bytes = await client.DownloadBytes(RemoteRules.Combine(dir, name), ct);
        return bytes ?? throw new IOException($"No se pudo bajar {name}.");
    }

    public async Task MoveAsync(string dir, string name, string toDir, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(toDir)) return;
        var target = toDir.StartsWith('/') ? toDir : RemoteRules.Combine(dir, toDir);
        if (!await client.DirectoryExists(target, ct)) await client.CreateDirectory(target, ct);
        var to = RemoteRules.Combine(target, name);
        if (await client.FileExists(to, ct)) to = RemoteRules.Combine(target, $"{DateTime.UtcNow:yyyyMMddHHmmss}-{name}");
        await client.MoveFile(RemoteRules.Combine(dir, name), to, FtpRemoteExists.Overwrite, ct);
    }

    public async ValueTask DisposeAsync()
    {
        try { await client.Disconnect(); } catch (Exception) { /* cerrar nunca debe fallar */ }
        client.Dispose();
    }
}
