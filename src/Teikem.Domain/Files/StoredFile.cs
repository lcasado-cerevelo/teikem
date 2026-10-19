using Teikem.Domain.Catalogs;
using Teikem.Domain.Common;

namespace Teikem.Domain.Files;

/// <summary>Constantes del almacén de archivos (Lote 127).</summary>
public static class StoredFileCodes
{
    /// <summary>EntityType de la bitácora del archivo (sembrado en la sección (bg) de update.sql).</summary>
    public const string EntityType = "STORED_FILE";

    /// <summary>Clases de archivo: la imagen de la firma, la foto de la entrega, la foto de un daño, un documento (PDF u otro).</summary>
    public const string KindSignature = "SIGNATURE";
    public const string KindDeliveryPhoto = "DELIVERY_PHOTO";
    public const string KindDamagePhoto = "DAMAGE_PHOTO";
    public const string KindDocument = "DOCUMENT";
    public static readonly string[] Kinds = { KindSignature, KindDeliveryPhoto, KindDamagePhoto, KindDocument };
}

/// <summary>
/// Lote 127 (dictado de Luis, 2026-10-18) — un archivo guardado por Teikem (la firma del que recibió, las fotos de la entrega o de un daño, un documento): en la base solo la REFERENCIA (de quién es, qué es,
/// nombre, tipo, tamaño, hash y la llave en el almacén); el contenido vive en el almacén de archivos (IFileStore), cifrado en reposo con una llave por compañía. Pertenece a una entidad
/// (OwnerEntity + OwnerId, el patrón polimórfico de ContactPoint): verlo exige el permiso de lectura del dueño y subirlo o quitarlo el de escritura. Se quita en lógico (IsActive = 0); el contenido no se borra.
/// Nunca se sirve con un enlace al disco: siempre por el API con permiso.
/// </summary>
[AuditEntity(StoredFileCodes.EntityType)]
public class StoredFile : ITenantScoped
{
    public int StoredFileId { get; set; }
    [NotAudited] public Guid PublicId { get; set; }
    public int TenantId { get; set; }
    public int OwnerEntityLookupId { get; set; }
    public int OwnerId { get; set; }
    public string Kind { get; set; } = StoredFileCodes.KindDocument;
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    /// <summary>SHA-256 del contenido en claro (hex, 64), para detectar duplicados y comprobar integridad.</summary>
    public string Sha256 { get; set; } = string.Empty;
    /// <summary>Dónde está en el almacén (ruta relativa a la raíz del almacén); no tiene sentido fuera de Teikem.</summary>
    [NotAudited] public string StorageKey { get; set; } = string.Empty;
    public bool IsEncrypted { get; set; } = true;
    public int? UploadedBy { get; set; }
    public bool IsActive { get; set; } = true;
    [NotAudited] public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public LookupCode? OwnerEntity { get; set; }
}

/// <summary>Reglas puras del almacén: qué se admite y los mensajes exactos.</summary>
public static class StoredFileRules
{
    public const int MaxFileNameLength = 200;
    public const int DefaultMaxMegabytes = 10;

    public const string FileRequired = "Adjunte un archivo.";
    public const string KindInvalid = "La clase del archivo debe ser SIGNATURE, DELIVERY_PHOTO, DAMAGE_PHOTO o DOCUMENT.";
    public const string TypeNotAllowed = "Solo se admiten imágenes (JPG, PNG, WEBP, GIF) y PDF.";
    public const string ContentMismatch = "El contenido del archivo no corresponde a su tipo.";
    public const string NotConfigured = "El almacén de archivos no está configurado (Files:MasterKey); avise a soporte.";
    public static string TooLarge(int maxMb) => $"El archivo no puede pasar de {maxMb} MB.";

    /// <summary>Tipos admitidos y su extensión.</summary>
    public static readonly IReadOnlyDictionary<string, string> AllowedTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["image/jpeg"] = ".jpg", ["image/png"] = ".png", ["image/webp"] = ".webp", ["image/gif"] = ".gif", ["application/pdf"] = ".pdf",
    };

    public static bool IsKind(string? kind) => kind is not null && StoredFileCodes.Kinds.Contains(kind.Trim().ToUpperInvariant());

    public static string NormalizeKind(string? kind) => (kind ?? string.Empty).Trim().ToUpperInvariant();

    /// <summary>El tipo real del contenido por sus primeros bytes (JPEG, PNG, WEBP, GIF o PDF); null si no es ninguno. No se confía en lo que dice el cliente.</summary>
    public static string? SniffContentType(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF) return "image/jpeg";
        if (bytes.Length >= 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47) return "image/png";
        if (bytes.Length >= 12 && bytes[0] == (byte)'R' && bytes[1] == (byte)'I' && bytes[2] == (byte)'F' && bytes[3] == (byte)'F' && bytes[8] == (byte)'W' && bytes[9] == (byte)'E' && bytes[10] == (byte)'B' && bytes[11] == (byte)'P') return "image/webp";
        if (bytes.Length >= 6 && bytes[0] == (byte)'G' && bytes[1] == (byte)'I' && bytes[2] == (byte)'F' && bytes[3] == (byte)'8') return "image/gif";
        if (bytes.Length >= 5 && bytes[0] == (byte)'%' && bytes[1] == (byte)'P' && bytes[2] == (byte)'D' && bytes[3] == (byte)'F' && bytes[4] == (byte)'-') return "application/pdf";
        return null;
    }

    /// <summary>Un nombre de archivo seguro: sin carpetas ni caracteres raros, recortado, con la extensión del tipo real.</summary>
    public static string SafeFileName(string? name, string contentType)
    {
        var ext = AllowedTypes.TryGetValue(contentType, out var e) ? e : string.Empty;
        var raw = Path.GetFileNameWithoutExtension((name ?? string.Empty).Replace('\\', '/').Split('/').Last()).Trim();
        var cleaned = new string(raw.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.' or ' ').ToArray()).Trim();
        if (cleaned.Length == 0) cleaned = "archivo";
        if (cleaned.Length > MaxFileNameLength - ext.Length) cleaned = cleaned[..(MaxFileNameLength - ext.Length)];
        return cleaned + ext;
    }

    public static string Sha256Hex(ReadOnlySpan<byte> bytes) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();
}
