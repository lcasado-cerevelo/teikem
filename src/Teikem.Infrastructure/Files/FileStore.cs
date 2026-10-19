using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace Teikem.Infrastructure.Files;

/// <summary>Configuración del almacén de archivos (sección «Files» de appsettings).</summary>
public sealed class FileStoreOptions
{
    public const string Section = "Files";
    /// <summary>Carpeta raíz del almacén (relativa al directorio de trabajo del API o absoluta). Fuera de la web: nunca se sirve con un enlace directo.</summary>
    public string Root { get; set; } = "App_Data/files";
    /// <summary>Llave maestra (32 bytes en base64). De ella se deriva una llave por compañía. Sin llave, el almacén no acepta archivos.</summary>
    public string? MasterKey { get; set; }
    public int MaxMegabytes { get; set; } = Teikem.Domain.Files.StoredFileRules.DefaultMaxMegabytes;

    public bool IsConfigured => TryMasterKey(out _);

    public bool TryMasterKey(out byte[] key)
    {
        key = Array.Empty<byte>();
        if (string.IsNullOrWhiteSpace(MasterKey)) return false;
        try { key = Convert.FromBase64String(MasterKey.Trim()); } catch (FormatException) { return false; }
        return key.Length == 32;
    }
}

/// <summary>
/// Lote 127 — dónde viven los archivos. La base guarda la referencia (StoredFile); esto guarda el contenido. Hoy: disco local cifrado; mañana S3/Azure Blob con la misma interfaz y nada más que cambiar.
/// </summary>
public interface IFileStore
{
    bool IsConfigured { get; }
    /// <summary>Guarda el contenido en claro de un archivo nuevo y devuelve su llave en el almacén.</summary>
    Task<string> SaveAsync(int tenantId, Guid publicId, ReadOnlyMemory<byte> content, CancellationToken ct);
    Task<byte[]> ReadAsync(int tenantId, string storageKey, CancellationToken ct);
}

/// <summary>
/// Disco local con cifrado en reposo: AES-256-GCM con una llave por compañía derivada de la maestra (HMAC-SHA256(maestra, "tenant:{id}")), así un archivo copiado del disco no sirve de nada
/// y la llave de una compañía no abre los de otra. Formato en disco: nonce (12) + etiqueta (16) + cifrado. Carpeta por compañía y mes: {tenant}/{aaaa}/{mm}/{guid}.bin.
/// </summary>
public sealed class EncryptedDiskFileStore(IOptions<FileStoreOptions> options) : IFileStore
{
    private const int NonceSize = 12, TagSize = 16;
    private readonly FileStoreOptions _o = options.Value;

    public bool IsConfigured => _o.IsConfigured;

    private string RootPath => Path.GetFullPath(_o.Root);

    private byte[] TenantKey(int tenantId)
    {
        if (!_o.TryMasterKey(out var master)) throw new InvalidOperationException(Teikem.Domain.Files.StoredFileRules.NotConfigured);
        return HMACSHA256.HashData(master, Encoding.UTF8.GetBytes($"tenant:{tenantId}"));
    }

    public async Task<string> SaveAsync(int tenantId, Guid publicId, ReadOnlyMemory<byte> content, CancellationToken ct)
    {
        var key = TenantKey(tenantId);
        var now = DateTime.UtcNow;
        var relative = Path.Combine(tenantId.ToString(), now.ToString("yyyy"), now.ToString("MM"), $"{publicId:N}.bin").Replace('\\', '/');
        var full = Path.Combine(RootPath, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var cipher = new byte[content.Length];
        var tag = new byte[TagSize];
        using (var aes = new AesGcm(key, TagSize)) aes.Encrypt(nonce, content.Span, cipher, tag);
        var blob = new byte[NonceSize + TagSize + cipher.Length];
        nonce.CopyTo(blob, 0);
        tag.CopyTo(blob, NonceSize);
        cipher.CopyTo(blob, NonceSize + TagSize);
        await File.WriteAllBytesAsync(full, blob, ct);
        return relative;
    }

    public async Task<byte[]> ReadAsync(int tenantId, string storageKey, CancellationToken ct)
    {
        var key = TenantKey(tenantId);
        var full = Path.GetFullPath(Path.Combine(RootPath, storageKey));
        if (!full.StartsWith(RootPath, StringComparison.OrdinalIgnoreCase)) throw new FileNotFoundException(storageKey);   // nunca fuera de la raíz
        var blob = await File.ReadAllBytesAsync(full, ct);
        if (blob.Length < NonceSize + TagSize) throw new InvalidDataException("Archivo del almacén dañado.");
        var plain = new byte[blob.Length - NonceSize - TagSize];
        using var aes = new AesGcm(key, TagSize);
        aes.Decrypt(blob.AsSpan(0, NonceSize), blob.AsSpan(NonceSize + TagSize), blob.AsSpan(NonceSize, TagSize), plain);
        return plain;
    }
}
