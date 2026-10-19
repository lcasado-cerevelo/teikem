using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Teikem.Infrastructure.Files;

namespace Teikem.Infrastructure.Security;

/// <summary>
/// Lote 133 (P11) — cifra los secretos de los conectores (contraseña del buzón, llave SFTP…). Una llave por compañía y por propósito, derivada de la llave maestra (Files:MasterKey) con HMAC-SHA256,
/// y AES-256-GCM con nonce aleatorio. Los secretos nunca viajan al navegador, nunca van a los registros y se muestran una sola vez al guardarlos.
/// </summary>
public interface ISecretProtector
{
    bool IsConfigured { get; }
    byte[] Protect(int tenantId, string purpose, string plain);
    string Unprotect(int tenantId, string purpose, byte[] blob);
}

public sealed class SecretProtector(IOptions<FileStoreOptions> options) : ISecretProtector
{
    private const int NonceSize = 12, TagSize = 16;
    public const string NotConfiguredMessage = "No se pueden guardar contraseñas de conectores: falta la llave maestra del servidor (Files:MasterKey).";
    public const string CorruptMessage = "El secreto guardado no se pudo leer; vuelva a escribir la contraseña de la integración.";

    public bool IsConfigured => options.Value.IsConfigured;

    private byte[] KeyOf(int tenantId, string purpose)
    {
        if (!options.Value.TryMasterKey(out var master)) throw new InvalidOperationException(NotConfiguredMessage);
        return HMACSHA256.HashData(master, Encoding.UTF8.GetBytes($"secret:{tenantId}:{purpose}"));
    }

    public byte[] Protect(int tenantId, string purpose, string plain)
    {
        var key = KeyOf(tenantId, purpose);
        var data = Encoding.UTF8.GetBytes(plain);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var cipher = new byte[data.Length];
        var tag = new byte[TagSize];
        using (var aes = new AesGcm(key, TagSize)) aes.Encrypt(nonce, data, cipher, tag);
        var blob = new byte[NonceSize + TagSize + cipher.Length];
        nonce.CopyTo(blob, 0);
        tag.CopyTo(blob, NonceSize);
        cipher.CopyTo(blob, NonceSize + TagSize);
        return blob;
    }

    public string Unprotect(int tenantId, string purpose, byte[] blob)
    {
        if (blob.Length < NonceSize + TagSize) throw new InvalidDataException(CorruptMessage);
        var key = KeyOf(tenantId, purpose);
        var plain = new byte[blob.Length - NonceSize - TagSize];
        try
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(blob.AsSpan(0, NonceSize), blob.AsSpan(NonceSize + TagSize), blob.AsSpan(NonceSize, TagSize), plain);
        }
        catch (CryptographicException) { throw new InvalidDataException(CorruptMessage); }
        return Encoding.UTF8.GetString(plain);
    }
}
