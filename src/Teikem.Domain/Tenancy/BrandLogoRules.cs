using System.Text;
using System.Xml;

namespace Teikem.Domain.Tenancy;

/// <summary>Ranuras de logo de la compañía: lockup completo y marca cuadrada, cada una con su variante para fondo oscuro.</summary>
public static class BrandLogoSlots
{
    /// <summary>Lockup completo (símbolo + nombre) para fondo CLARO.</summary>
    public const string Lockup = "lockup";
    /// <summary>Lockup completo para fondo OSCURO (variante invertida).</summary>
    public const string LockupInverted = "lockup-inverted";
    /// <summary>Marca cuadrada (barra colapsada, espacios chicos) para fondo claro.</summary>
    public const string Mark = "mark";
    /// <summary>Marca cuadrada para fondo oscuro.</summary>
    public const string MarkInverted = "mark-inverted";

    public static readonly IReadOnlyList<string> All = [Lockup, LockupInverted, Mark, MarkInverted];
    public static bool IsValid(string? slot) => slot is not null && All.Contains(slot);
}

/// <summary>Resultado de inspeccionar un archivo de logo: tipo de contenido real o el error (con el código HTTP que corresponde).</summary>
public sealed record BrandLogoInspection(string? ContentType, int StatusCode, string? Error)
{
    public bool Ok => Error is null;
    public static BrandLogoInspection Valid(string contentType) => new(contentType, 200, null);
    public static BrandLogoInspection Fail(int status, string error) => new(null, status, error);
}

/// <summary>
/// Reglas puras de los logos de la compañía (Ajustes → Marca). Formatos SVG, PNG, JPG y WebP de hasta 512 KB.
/// - El tipo se decide por el CONTENIDO (cabecera/mágicos), nunca por la extensión ni por el tipo declarado; si el tipo declarado
///   es de imagen y no coincide con el real, se rechaza (415).
/// - Un SVG se analiza como XML (sin DOCTYPE ni entidades) y se rechaza si trae contenido activo o referencias externas:
///   elementos script/foreignObject/iframe/object/embed/link/style con @import/animate/set…, atributos on*, 'javascript:' y todo
///   href/src que no sea un fragmento interno (#id) o una imagen incrustada (data:image/png|jpeg|webp|gif;base64).
/// Mensajes públicos: los usan el servicio (400/413/415), las pruebas y el manual (FAQ).
/// </summary>
public static class BrandLogoRules
{
    public const int MaxBytes = 512 * 1024;
    public static readonly IReadOnlyList<string> ContentTypes = ["image/svg+xml", "image/png", "image/jpeg", "image/webp"];

    public const string FileRequiredMessage = "Seleccione un archivo de logo.";
    public const string BadUploadMessage = "La subida del logo llegó incompleta o mal formada. Vuelva a intentarlo.";
    public const string TooLargeMessage = "El logo supera el tamaño máximo de 512 KB.";
    public const string UnsupportedMessage = "Formato no admitido: el logo debe ser SVG, PNG, JPG o WebP.";
    public const string CorruptImageMessage = "El archivo está dañado o incompleto y no se puede usar como logo.";
    public const string SvgNotXmlMessage = "El SVG no es un XML válido.";
    public const string SvgDoctypeMessage = "El SVG no se acepta: declara DOCTYPE o entidades.";
    public static string SlotNotFoundWhat => "Ranura de logo";
    public static string DeclaredMismatchMessage(string declared, string real) =>
        $"El contenido del archivo ({real}) no coincide con el tipo declarado ({declared}).";
    public static string SvgElementMessage(string name) => $"El SVG no se acepta: contiene el elemento <{name}>, que puede ejecutar código o cargar contenido externo.";
    public static string SvgAttributeMessage(string name) => $"El SVG no se acepta: el atributo '{name}' ejecuta código.";
    public static string SvgScriptUrlMessage(string name) => $"El SVG no se acepta: el atributo '{name}' contiene un enlace de script (javascript:).";
    public static string SvgExternalMessage(string name) => $"El SVG no se acepta: el atributo '{name}' apunta fuera del archivo (solo se permiten referencias internas #id).";
    public static string SvgStyleMessage => "El SVG no se acepta: una hoja de estilos importa o referencia contenido externo.";

    private static readonly HashSet<string> BlockedElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "script", "foreignObject", "iframe", "frame", "frameset", "object", "embed", "applet", "link", "meta", "base",
        "audio", "video", "canvas", "set", "animate", "handler", "listener",
    };

    private static readonly string[] AllowedDataImagePrefixes =
        ["data:image/png;base64,", "data:image/jpeg;base64,", "data:image/jpg;base64,", "data:image/webp;base64,", "data:image/gif;base64,"];

    /// <summary>Nombre de archivo sugerido para descargar el logo de una ranura según su tipo.</summary>
    public static string Extension(string contentType) => contentType switch
    {
        "image/svg+xml" => "svg",
        "image/png" => "png",
        "image/jpeg" => "jpg",
        _ => "webp",
    };

    /// <summary>Inspecciona el archivo (tamaño, tipo real por cabecera y, si es SVG, contenido activo). <paramref name="declaredContentType"/> es opcional.</summary>
    public static BrandLogoInspection Inspect(byte[] data, string? declaredContentType = null)
    {
        if (data.Length == 0) return BrandLogoInspection.Fail(400, FileRequiredMessage);
        if (data.Length > MaxBytes) return BrandLogoInspection.Fail(413, TooLargeMessage);

        var (real, corrupt) = Sniff(data);
        if (corrupt) return BrandLogoInspection.Fail(400, CorruptImageMessage);
        var isSvgCandidate = real is null && LooksLikeText(data);
        if (real is null && !isSvgCandidate) return BrandLogoInspection.Fail(415, UnsupportedMessage);

        if (real is null)
        {
            var svg = InspectSvg(data);
            if (!svg.Ok) return svg;
            real = "image/svg+xml";
        }

        var declared = NormalizeDeclared(declaredContentType);
        if (declared is not null && ContentTypes.Contains(declared) && declared != real)
            return BrandLogoInspection.Fail(415, DeclaredMismatchMessage(declared, real));
        return BrandLogoInspection.Valid(real);
    }

    private static string? NormalizeDeclared(string? ct)
    {
        if (string.IsNullOrWhiteSpace(ct)) return null;
        var t = ct.Split(';')[0].Trim().ToLowerInvariant();
        return t == "image/jpg" ? "image/jpeg" : t;
    }

    // ------------------------------------------------------------------ binarios (mágicos)

    private static (string? ContentType, bool Corrupt) Sniff(byte[] d)
    {
        if (StartsWith(d, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]))
        {
            // primer fragmento IHDR de 13 bytes con ancho y alto > 0 y fragmento IEND al final
            var ihdr = d.Length >= 33 && d[8] == 0 && d[9] == 0 && d[10] == 0 && d[11] == 13 && d[12] == 'I' && d[13] == 'H' && d[14] == 'D' && d[15] == 'R';
            if (!ihdr) return (null, true);
            var w = (d[16] << 24) | (d[17] << 16) | (d[18] << 8) | d[19];
            var h = (d[20] << 24) | (d[21] << 16) | (d[22] << 8) | d[23];
            var iend = d.Length >= 12 && d[^8] == 'I' && d[^7] == 'E' && d[^6] == 'N' && d[^5] == 'D';
            return w > 0 && h > 0 && iend ? ("image/png", false) : (null, true);
        }
        if (StartsWith(d, [0xFF, 0xD8, 0xFF]))
        {
            // termina con el marcador EOI (se toleran bytes de relleno al final)
            var end = d.Length;
            while (end > 2 && d[end - 1] == 0) end--;
            return d.Length >= 4 && d[end - 2] == 0xFF && d[end - 1] == 0xD9 ? ("image/jpeg", false) : (null, true);
        }
        if (d.Length >= 12 && StartsWith(d, "RIFF"u8) && d[8] == 'W' && d[9] == 'E' && d[10] == 'B' && d[11] == 'P')
        {
            var riffSize = d[4] | (d[5] << 8) | (d[6] << 16) | (d[7] << 24);
            var chunkOk = d.Length >= 16 && d[12] == 'V' && d[13] == 'P' && d[14] == '8' && (d[15] == ' ' || d[15] == 'L' || d[15] == 'X');
            return chunkOk && riffSize >= 4 && riffSize + 8L <= d.Length ? ("image/webp", false) : (null, true);
        }
        return (null, false);
    }

    private static bool StartsWith(byte[] d, ReadOnlySpan<byte> prefix) => d.AsSpan().StartsWith(prefix);

    /// <summary>¿Empieza como texto XML (tras un BOM UTF-8 y espacios, un '<')?</summary>
    private static bool LooksLikeText(byte[] d)
    {
        var i = d.Length >= 3 && d[0] == 0xEF && d[1] == 0xBB && d[2] == 0xBF ? 3 : 0;
        while (i < d.Length && d[i] is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n') i++;
        return i < d.Length && d[i] == (byte)'<';
    }

    // ------------------------------------------------------------------ SVG

    private static BrandLogoInspection InspectSvg(byte[] data)
    {
        string text;
        try { text = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(data); }
        catch (DecoderFallbackException) { return BrandLogoInspection.Fail(415, UnsupportedMessage); }
        if (text.Length > 0 && text[0] == '﻿') text = text[1..];

        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,   // sin DOCTYPE ni entidades (billion laughs, XXE)
            XmlResolver = null,
            IgnoreComments = true,
            MaxCharactersFromEntities = 0,
        };
        try
        {
            using var reader = XmlReader.Create(new StringReader(text), settings);
            var rootSeen = false;
            var inStyle = 0;
            var depth = 0;
            while (reader.Read())
            {
                switch (reader.NodeType)
                {
                    case XmlNodeType.Element:
                        depth++;
                        if (!rootSeen)
                        {
                            rootSeen = true;
                            if (reader.LocalName != "svg" || reader.NamespaceURI != "http://www.w3.org/2000/svg")
                                return BrandLogoInspection.Fail(415, UnsupportedMessage);
                        }
                        if (BlockedElements.Contains(reader.LocalName)) return BrandLogoInspection.Fail(400, SvgElementMessage(reader.LocalName));
                        var bad = CheckAttributes(reader);
                        if (bad is not null) return bad;
                        if (reader.LocalName.Equals("style", StringComparison.OrdinalIgnoreCase) && !reader.IsEmptyElement) inStyle++;
                        if (reader.IsEmptyElement) depth--;
                        break;
                    case XmlNodeType.EndElement:
                        depth--;
                        if (reader.LocalName.Equals("style", StringComparison.OrdinalIgnoreCase) && inStyle > 0) inStyle--;
                        break;
                    case XmlNodeType.Text or XmlNodeType.CDATA:
                        if (inStyle > 0 && StyleIsUnsafe(reader.Value)) return BrandLogoInspection.Fail(400, SvgStyleMessage);
                        break;
                    case XmlNodeType.DocumentType:
                        return BrandLogoInspection.Fail(400, SvgDoctypeMessage);
                }
            }
            if (!rootSeen) return BrandLogoInspection.Fail(415, UnsupportedMessage);
            return BrandLogoInspection.Valid("image/svg+xml");
        }
        catch (XmlException ex) when (ex.Message.Contains("DTD", StringComparison.OrdinalIgnoreCase) || ex.Message.Contains("DOCTYPE", StringComparison.OrdinalIgnoreCase))
        {
            return BrandLogoInspection.Fail(400, SvgDoctypeMessage);
        }
        catch (XmlException)
        {
            return BrandLogoInspection.Fail(400, SvgNotXmlMessage);
        }
    }

    private static BrandLogoInspection? CheckAttributes(XmlReader reader)
    {
        if (!reader.HasAttributes) return null;
        for (var ok = reader.MoveToFirstAttribute(); ok; ok = reader.MoveToNextAttribute())
        {
            var name = reader.LocalName;
            var qualified = reader.Name;
            var value = reader.Value;
            var isNs = reader.Prefix == "xmlns" || qualified == "xmlns";
            if (isNs) continue;
            if (name.StartsWith("on", StringComparison.OrdinalIgnoreCase)) { reader.MoveToElement(); return BrandLogoInspection.Fail(400, SvgAttributeMessage(qualified)); }
            if (HasScriptUrl(value)) { reader.MoveToElement(); return BrandLogoInspection.Fail(400, SvgScriptUrlMessage(qualified)); }
            if (name.Equals("href", StringComparison.OrdinalIgnoreCase) || name.Equals("src", StringComparison.OrdinalIgnoreCase))
            {
                var v = value.Trim();
                var internalRef = v.StartsWith('#');
                var dataImage = AllowedDataImagePrefixes.Any(p => v.StartsWith(p, StringComparison.OrdinalIgnoreCase));
                if (!internalRef && !dataImage) { reader.MoveToElement(); return BrandLogoInspection.Fail(400, SvgExternalMessage(qualified)); }
            }
            if (name.Equals("style", StringComparison.OrdinalIgnoreCase) && StyleIsUnsafe(value)) { reader.MoveToElement(); return BrandLogoInspection.Fail(400, SvgStyleMessage); }
        }
        reader.MoveToElement();
        return null;
    }

    /// <summary>'javascript:' o 'vbscript:' aunque lo disfracen con espacios, saltos de línea o caracteres de control.</summary>
    private static bool HasScriptUrl(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (var ch in value) if (!char.IsWhiteSpace(ch) && !char.IsControl(ch)) sb.Append(char.ToLowerInvariant(ch));
        var s = sb.ToString();
        return s.Contains("javascript:", StringComparison.Ordinal) || s.Contains("vbscript:", StringComparison.Ordinal);
    }

    /// <summary>CSS que importa o referencia contenido externo (@import, url(...) que no sea #id ni imagen incrustada) o ejecuta (expression, behavior).</summary>
    private static bool StyleIsUnsafe(string css)
    {
        var s = new string(css.Where(c => !char.IsWhiteSpace(c) && !char.IsControl(c)).ToArray()).ToLowerInvariant();
        if (s.Contains("@import", StringComparison.Ordinal) || s.Contains("expression(", StringComparison.Ordinal)
            || s.Contains("behavior:", StringComparison.Ordinal) || s.Contains("-moz-binding", StringComparison.Ordinal)
            || s.Contains("javascript:", StringComparison.Ordinal) || s.Contains("vbscript:", StringComparison.Ordinal)) return true;
        var i = 0;
        while ((i = s.IndexOf("url(", i, StringComparison.Ordinal)) >= 0)
        {
            i += 4;
            var rest = s[i..].TrimStart('\'', '"');
            if (!rest.StartsWith('#') && !AllowedDataImagePrefixes.Any(p => rest.StartsWith(p, StringComparison.Ordinal))) return true;
        }
        return false;
    }
}
