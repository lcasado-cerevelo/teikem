using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 43 (F-G1) — firma de las peticiones de QZ Tray con el certificado PROPIO de la plataforma (autofirmado), para que cada
/// computadora que instaló el certificado (override.crt de QZ Tray) imprima sin el aviso «¿Permitir?».
/// - Certificado público: Printing:Qz:Certificate (texto PEM) o Printing:Qz:CertificatePath (archivo).
/// - Clave privada: Printing:Qz:PrivateKey (PEM, p. ej. variable de entorno Printing__Qz__PrivateKey) o Printing:Qz:PrivateKeyPath,
///   con Printing:Qz:PrivateKeyPassword si el PEM está cifrado. JAMÁS en el repositorio ni en una respuesta: solo sale la firma.
/// - Firma SHA512withRSA (PKCS#1 v1.5) del texto que manda QZ Tray (un hash corto de la petición; máximo 8192 caracteres).
/// - Sin certificado o sin clave: 409 con <see cref="NotConfiguredMessage"/>; la impresión sigue (QZ Tray pregunta «Permitir» o se usa
///   el PDF del navegador).
/// El servidor nunca se conecta a QZ Tray ni a una impresora: solo firma.
/// </summary>
public sealed class QzSigningService
{
    public const int MaxRequestLength = 8192;
    public const string Algorithm = "SHA512";
    public const string NotConfiguredMessage =
        "La impresión directa con QZ Tray no tiene certificado configurado en este servidor; QZ Tray pedirá «Permitir» en cada computadora (soporte: manual 12, «Certificado de QZ Tray»).";
    public const string RequestRequiredMessage = "Falta el texto a firmar.";
    public const string RequestTooLongMessage = "El texto a firmar no puede exceder 8192 caracteres.";

    private readonly Lazy<(string? Certificate, RSA? Key)> _material;

    public QzSigningService(IConfiguration config) => _material = new Lazy<(string?, RSA?)>(() => Load(config));

    /// <summary>¿Hay certificado y clave privada utilizables?</summary>
    public bool IsConfigured => _material.Value is { Certificate: not null, Key: not null };

    public QzCertificateDto GetCertificate()
    {
        var (cert, key) = _material.Value;
        if (cert is null || key is null) throw new ConflictException(NotConfiguredMessage);
        return new QzCertificateDto(cert);
    }

    public QzSignatureDto Sign(QzSignRequest? req)
    {
        var text = req?.Request;
        if (string.IsNullOrEmpty(text)) throw new ValidationException("request", RequestRequiredMessage);
        if (text.Length > MaxRequestLength) throw new ValidationException("request", RequestTooLongMessage);
        var (cert, key) = _material.Value;
        if (cert is null || key is null) throw new ConflictException(NotConfiguredMessage);
        byte[] signature;
        lock (key) signature = key.SignData(Encoding.UTF8.GetBytes(text), HashAlgorithmName.SHA512, RSASignaturePadding.Pkcs1);
        return new QzSignatureDto(Convert.ToBase64String(signature), Algorithm);
    }

    private static (string? Certificate, RSA? Key) Load(IConfiguration config)
    {
        var section = config.GetSection("Printing:Qz");
        var cert = ReadText(section["Certificate"], section["CertificatePath"]);
        var keyPem = ReadText(section["PrivateKey"], section["PrivateKeyPath"]);
        if (cert is null || keyPem is null || !cert.Contains("-----BEGIN CERTIFICATE-----", StringComparison.Ordinal)) return (null, null);
        try
        {
            var rsa = RSA.Create();
            var password = section["PrivateKeyPassword"];
            if (string.IsNullOrEmpty(password)) rsa.ImportFromPem(keyPem);
            else rsa.ImportFromEncryptedPem(keyPem, password);
            return (cert.Trim() + "\n", rsa);
        }
        catch (Exception e) when (e is CryptographicException or ArgumentException)
        {
            return (null, null);   // clave dañada o con contraseña equivocada: se trata como "sin configurar"
        }
    }

    private static string? ReadText(string? inline, string? path)
    {
        if (!string.IsNullOrWhiteSpace(inline)) return inline.Replace("\\n", "\n");
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path)) return File.ReadAllText(path);
        return null;
    }
}
