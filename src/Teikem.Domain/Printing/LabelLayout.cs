using System.Text.Json;
using System.Text.Json.Serialization;

namespace Teikem.Domain.Printing;

/// <summary>Un elemento del label: si se muestra, su zona (9 posiciones), su tamaño y si se dibuja como código de barras.</summary>
public sealed record LabelElement(bool On, string Pos, string Size, bool AsBarcode = false);

/// <summary>
/// Lote 43 (F-G1) — construcción del label de envío: los 9 elementos del constructor de la maqueta (LABEL_FIELDS) con Mostrar,
/// Posición en una cuadrícula de 9 zonas (tl, tc, tr, ml, mc, mr, bl, bc, br) y Tamaño (sm, md, lg). El «Código de barras» es el
/// NÚMERO DEL PAQUETE (un label por paquete; debajo de las barras va el número legible y «Paquete i de N»). Solo el número de
/// waybill y la factura pueden mostrarse además «como código de barras». El label necesita al menos un código de barras visible para
/// poder escanearse. Se guarda como JSON canónico (orden y claves fijos, ≤ 2000 caracteres).
/// </summary>
public sealed record LabelLayout(
    LabelElement Logo,
    LabelElement Service,
    LabelElement Sender,
    LabelElement Consignee,
    LabelElement Address,
    LabelElement Barcode,
    LabelElement Waybill,
    LabelElement Invoice,
    LabelElement Cod)
{
    public const int MaxJsonLength = 2000;

    public const string LayoutRequiredMessage = "Falta la construcción del label.";
    public const string BarcodeOnlyWaybillInvoiceMessage = "Solo el número de waybill y la factura se pueden mostrar como código de barras.";
    public const string NeedsBarcodeMessage = "El label necesita al menos un código de barras para poder escanearlo.";

    /// <summary>400 «Posición desconocida en «Monto COD»: 'X'. Use tl, tc, tr, ml, mc, mr, bl, bc o br.»</summary>
    public static string UnknownPositionMessage(string element, string? value) => $"Posición desconocida en «{element}»: '{value}'. Use tl, tc, tr, ml, mc, mr, bl, bc o br.";

    /// <summary>400 «Tamaño desconocido en «Monto COD»: 'X'. Use sm, md o lg.»</summary>
    public static string UnknownSizeMessage(string element, string? value) => $"Tamaño desconocido en «{element}»: '{value}'. Use sm, md o lg.";

    public static readonly IReadOnlyList<string> Positions = new[] { "tl", "tc", "tr", "ml", "mc", "mr", "bl", "bc", "br" };
    public static readonly IReadOnlyList<string> Sizes = new[] { "sm", "md", "lg" };

    /// <summary>Claves de los elementos, en el orden del constructor (el del JSON canónico y de la tabla de la pantalla).</summary>
    public static readonly IReadOnlyList<string> Keys = new[] { "logo", "service", "sender", "consignee", "address", "barcode", "waybill", "invoice", "cod" };

    /// <summary>Nombre de cada elemento en los mensajes (el de la tabla de la pantalla).</summary>
    public static readonly IReadOnlyDictionary<string, string> ElementNames = new Dictionary<string, string>
    {
        ["logo"] = "Logo",
        ["service"] = "Nivel de servicio",
        ["sender"] = "Información del sender",
        ["consignee"] = "Consignatario",
        ["address"] = "Dirección",
        ["barcode"] = "Código de barras",
        ["waybill"] = "Número de waybill",
        ["invoice"] = "Invoice",
        ["cod"] = "Monto COD",
    };

    /// <summary>Elementos que admiten «Como código de barras».</summary>
    public static readonly IReadOnlyList<string> BarcodeCapable = new[] { "waybill", "invoice" };

    /// <summary>El label integrado (LABEL_FIELDS de la maqueta): el que aplica sin plantilla.</summary>
    public static LabelLayout BuiltIn { get; } = new(
        Logo: new(true, "tl", "md"),
        Service: new(true, "tr", "sm"),
        Sender: new(false, "tc", "sm"),
        Consignee: new(true, "ml", "md"),
        Address: new(true, "ml", "sm"),
        Barcode: new(true, "mc", "md"),
        Waybill: new(false, "bl", "sm", false),
        Invoice: new(true, "bl", "sm", false),
        Cod: new(true, "br", "md"));

    /// <summary>Elementos con su clave, en el orden canónico.</summary>
    public IEnumerable<(string Key, LabelElement Element)> Elements()
    {
        yield return ("logo", Logo);
        yield return ("service", Service);
        yield return ("sender", Sender);
        yield return ("consignee", Consignee);
        yield return ("address", Address);
        yield return ("barcode", Barcode);
        yield return ("waybill", Waybill);
        yield return ("invoice", Invoice);
        yield return ("cod", Cod);
    }

    /// <summary>¿Hay algún código de barras visible? (el elemento «Código de barras», o waybill/factura como código).</summary>
    public bool HasVisibleBarcode()
        => Barcode.On || (Waybill.On && Waybill.AsBarcode) || (Invoice.On && Invoice.AsBarcode);

    /// <summary>
    /// Valida y normaliza lo que llega del cliente: un elemento que falta (null) toma el del label integrado; posición y tamaño vacíos,
    /// los del integrado; se aceptan en mayúsculas. «Como código de barras» en un elemento que no es waybill ni factura es 400 (en
    /// «Código de barras» se ignora: siempre lo es). Devuelve (label normalizado, null) o (null, mensaje exacto de 400).
    /// </summary>
    public static (LabelLayout? Layout, string? Error) Normalize(IReadOnlyDictionary<string, LabelElement?> input)
    {
        var builtIn = BuiltIn.Elements().ToDictionary(e => e.Key, e => e.Element);
        var result = new Dictionary<string, LabelElement>(StringComparer.Ordinal);
        foreach (var key in Keys)
        {
            var def = builtIn[key];
            input.TryGetValue(key, out var raw);
            if (raw is null) { result[key] = def; continue; }
            var name = ElementNames[key];

            var pos = string.IsNullOrWhiteSpace(raw.Pos) ? def.Pos : raw.Pos.Trim().ToLowerInvariant();
            if (!Positions.Contains(pos)) return (null, UnknownPositionMessage(name, raw.Pos?.Trim()));
            var size = string.IsNullOrWhiteSpace(raw.Size) ? def.Size : raw.Size.Trim().ToLowerInvariant();
            if (!Sizes.Contains(size)) return (null, UnknownSizeMessage(name, raw.Size?.Trim()));

            var asBarcode = raw.AsBarcode;
            if (key == "barcode") asBarcode = false;
            else if (asBarcode && !BarcodeCapable.Contains(key)) return (null, BarcodeOnlyWaybillInvoiceMessage);

            result[key] = new LabelElement(raw.On, pos, size, asBarcode);
        }
        var layout = FromDictionary(result);
        if (!layout.HasVisibleBarcode()) return (null, NeedsBarcodeMessage);
        return (layout, null);
    }

    private static LabelLayout FromDictionary(IReadOnlyDictionary<string, LabelElement> d)
        => new(d["logo"], d["service"], d["sender"], d["consignee"], d["address"], d["barcode"], d["waybill"], d["invoice"], d["cod"]);

    // ---------------- JSON canónico ----------------

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    /// <summary>JSON canónico: {"logo":{"on":true,"pos":"tl","size":"md","asBarcode":false},...} con las 9 claves en orden.</summary>
    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    /// <summary>Lee el JSON guardado; si está dañado o no valida, null (quien llama usa el integrado).</summary>
    public static LabelLayout? TryParse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
            var input = new Dictionary<string, LabelElement?>(StringComparer.Ordinal);
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                var key = Keys.FirstOrDefault(k => string.Equals(k, prop.Name, StringComparison.OrdinalIgnoreCase));
                if (key is null || prop.Value.ValueKind != JsonValueKind.Object) continue;
                input[key] = prop.Value.Deserialize<LabelElement>(JsonOptions);
            }
            var (layout, error) = Normalize(input);
            return error is null ? layout : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
