using System.Globalization;
using System.Text.Json;

namespace Teikem.Domain.Tenancy;

/// <summary>Un tema predefinido de la marca (los 13 de la maqueta). Nh/Ns = tono (0-360) y saturación (0-1) del neutro.</summary>
public sealed record BrandPreset(string Id, string NameEs, string NameEn, string Flow, string Money, double Nh, double Ns);

/// <summary>Lo que se guarda en <c>Tenant.BrandingJson</c> ya validado (colores normalizados a #RRGGBB).</summary>
public sealed record BrandSettings(string Preset, bool UseCustom, string Flow, string Money, string Neutral);

/// <summary>
/// Una comprobación de la marca: <c>contrast</c> (Key = text|muted|flow|money, Mode = dark|light, Value = razón de contraste con dos
/// decimales) o <c>hue</c> (Value = distancia de matiz en grados enteros entre los dos acentos).
/// </summary>
public sealed record BrandCheck(string Type, string? Key, string? Mode, double Value, double Min, bool Pass);

/// <summary>Resultado de validar un <c>BrandingJson</c>: Code/Message vacíos si es válido; Checks = comprobaciones de color (vacío si falló antes).</summary>
public sealed record BrandValidation(bool Ok, string? Code, string? Message, IReadOnlyList<BrandCheck> Checks)
{
    public static BrandValidation Valid(IReadOnlyList<BrandCheck> checks) => new(true, null, null, checks);
    public static BrandValidation Fail(string code, string message, IReadOnlyList<BrandCheck>? checks = null) => new(false, code, message, checks ?? []);
}

/// <summary>
/// Reglas puras de la marca por compañía (Ajustes → Marca): ESPEJO EXACTO de <c>web-app/src/kernel/ui/brandTheme.ts</c> (mismos
/// 13 temas, misma derivación de la paleta a partir del tono y la saturación del neutro, mismos umbrales y mismos mensajes).
/// La paridad se asegura con los vectores compartidos <c>tests/shared/brand-vectors.json</c> que leen las pruebas xunit y vitest.
/// - Se valida el contraste WCAG contra el panel DERIVADO del propio tema, en modo oscuro y claro: texto principal ≥ 7:1, texto
///   atenuado y los dos acentos ≥ 4.5:1; y la separación de matiz entre los dos acentos ≥ 40° (son semánticos: dos corrientes).
/// - Los colores de estado (ok, warn, danger, info) NO se personalizan: se rechazan. Un tema inválido entrado por API dejaría la
///   interfaz ilegible para toda la compañía, por eso el servidor repite lo que ya hace la pantalla.
/// - El orden de las comprobaciones es parte del contrato (el primer fallo es el que se informa): tamaño → JSON → objeto → campos
///   ajenos → tipos → colores hexadecimales → tema predefinido existente → contraste → matiz.
/// Los logos NO viajan en este JSON: tienen su propio almacén (<see cref="BrandLogoRules"/>).
/// </summary>
public static class BrandingRules
{
    public const string DefaultPreset = "teikem";
    /// <summary>Tamaño máximo del JSON en caracteres.</summary>
    public const int MaxJsonChars = 4096;
    /// <summary>Separación mínima de matiz entre los dos acentos, en grados.</summary>
    public const double MinHueDistance = 40;
    /// <summary>Techo de la saturación del neutro propio (apenas por encima del 0.39 original de Teikem).</summary>
    public const double NeutralSatMax = 0.45;

    public static readonly BrandSettings DefaultSettings = new(DefaultPreset, false, "#1F6FE5", "#FF6A1A", "#2B3A5C");

    /// <summary>Los 13 temas de la maqueta; cada uno pasa todas las comprobaciones (lo prueba la suite).</summary>
    public static readonly IReadOnlyList<BrandPreset> Presets =
    [
        new("teikem", "Teikem (por defecto)", "Teikem (default)", "#1F6FE5", "#FF6A1A", 222, 0.39),
        new("marino", "Marino", "Navy", "#1F59A3", "#E08A17", 215, 0.42),
        new("acero", "Acero", "Steel", "#3D6E9C", "#D2691E", 210, 0.26),
        new("carretera", "Carretera", "Highway", "#D32F2F", "#0E8F8F", 8, 0.15),
        new("granate", "Granate", "Crimson", "#B02A45", "#0E8F8F", 345, 0.17),
        new("vino", "Vino", "Burgundy", "#9E2F4F", "#C69214", 338, 0.2),
        new("bosque", "Bosque", "Forest", "#1E8E5A", "#D98324", 160, 0.19),
        new("selva", "Selva", "Jungle", "#0F7A5A", "#C8452D", 168, 0.24),
        new("oliva", "Oliva", "Olive", "#5B7A2E", "#C2571C", 92, 0.15),
        new("turquesa", "Turquesa", "Turquoise", "#0E9AA7", "#E2622C", 190, 0.25),
        new("indigo", "Índigo", "Indigo", "#4F46E5", "#EA8C00", 250, 0.3),
        new("violeta", "Violeta", "Violet", "#7A4FD1", "#D98324", 265, 0.24),
        new("grafito", "Grafito", "Graphite", "#4E5A6E", "#C2691C", 220, 0.06),
    ];

    /// <summary>Claves que la marca nunca acepta (comparación sin distinguir mayúsculas): los colores de estado.</summary>
    public static readonly IReadOnlyList<string> StatusKeys = ["ok", "warn", "danger", "info", "status", "statuscolors"];

    private static readonly string[] TopKeys = ["preset", "useCustom", "custom"];
    private static readonly string[] CustomKeys = ["flow", "money", "neutral"];

    // ------------------------------------------------------------------ mensajes (los usan el servicio, las pruebas y el manual)
    public static string TooLargeMessage => $"La marca es demasiado grande (máximo {MaxJsonChars} caracteres); los logos se suben aparte.";
    public const string MalformedMessage = "La marca no es un JSON válido.";
    public const string NotObjectMessage = "La marca debe ser un objeto JSON.";
    public static string UnknownFieldMessage(string path) => $"La marca trae un campo desconocido: '{Clip(path)}'.";
    public static string StatusColorMessage(string path) => $"Los colores de estado (ok, warn, danger, info) no se pueden personalizar: '{Clip(path)}'.";
    public static string BadTypeMessage(string path) => $"El campo '{path}' tiene un tipo inválido.";
    public static string BadHexMessage(string path) => $"El color '{path}' no es hexadecimal (use #RGB o #RRGGBB).";
    public static string UnknownPresetMessage(string id) => $"El tema predefinido '{Clip(id)}' no existe.";
    public static string ContrastMessage(BrandCheck c) =>
        $"El contraste {ContrastLabel(c.Key!)} en modo {(c.Mode == "dark" ? "oscuro" : "claro")} es {Num(c.Value)}:1; el mínimo es {Num(c.Min)}:1.";
    public static string HueMessage(BrandCheck c) =>
        $"Los colores de operación y de dinero son demasiado parecidos: {Num(c.Value)}° de separación y el mínimo es {Num(c.Min)}°.";

    private static string ContrastLabel(string key) => key switch
    {
        "text" => "del texto",
        "muted" => "del texto atenuado",
        "flow" => "del color de operación",
        _ => "del color de dinero",
    };

    private static string Num(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);
    private static string Clip(string s) => s.Length > 40 ? s[..40] + "…" : s;

    public static BrandPreset? PresetById(string? id) => Presets.FirstOrDefault(p => p.Id == id);

    // ------------------------------------------------------------------ validación del JSON

    /// <summary>
    /// Valida el <c>BrandingJson</c> que llega a <c>PUT /tenant/settings</c>. Vacío = quitar la marca (válido, sin comprobaciones).
    /// Los campos que faltan toman el valor por defecto (tema Teikem, sin colores propios).
    /// </summary>
    public static BrandValidation Validate(string json)
    {
        if (json.Length == 0) return BrandValidation.Valid([]);
        if (json.Length > MaxJsonChars) return BrandValidation.Fail("tooLarge", TooLargeMessage);
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch (JsonException) { return BrandValidation.Fail("malformed", MalformedMessage); }
        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return BrandValidation.Fail("notObject", NotObjectMessage);
            var foreign = ForeignKey(root, TopKeys, "");
            if (foreign is not null) return foreign;

            var hasPreset = root.TryGetProperty("preset", out var presetEl);
            if (hasPreset && presetEl.ValueKind != JsonValueKind.String) return BrandValidation.Fail("badType", BadTypeMessage("preset"));
            var hasUse = root.TryGetProperty("useCustom", out var useEl);
            if (hasUse && useEl.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return BrandValidation.Fail("badType", BadTypeMessage("useCustom"));

            string flow = DefaultSettings.Flow, money = DefaultSettings.Money, neutral = DefaultSettings.Neutral;
            if (root.TryGetProperty("custom", out var customEl))
            {
                if (customEl.ValueKind != JsonValueKind.Object) return BrandValidation.Fail("badType", BadTypeMessage("custom"));
                var inner = ForeignKey(customEl, CustomKeys, "custom.");
                if (inner is not null) return inner;
                foreach (var k in CustomKeys)
                {
                    if (!customEl.TryGetProperty(k, out var v)) continue;
                    if (v.ValueKind != JsonValueKind.String) return BrandValidation.Fail("badType", BadTypeMessage("custom." + k));
                    var text = v.GetString()!;
                    if (!IsValidHex(text)) return BrandValidation.Fail("badHex", BadHexMessage("custom." + k));
                    var hex = NormalizeHex(text);
                    if (k == "flow") flow = hex; else if (k == "money") money = hex; else neutral = hex;
                }
            }

            var preset = hasPreset ? presetEl.GetString()! : DefaultPreset;
            if (PresetById(preset) is null) return BrandValidation.Fail("unknownPreset", UnknownPresetMessage(preset));
            var settings = new BrandSettings(preset, hasUse && useEl.ValueKind == JsonValueKind.True, flow, money, neutral);

            var checks = Checks(settings);
            foreach (var c in checks)
            {
                if (c.Pass) continue;
                return c.Type == "contrast"
                    ? BrandValidation.Fail("contrast", ContrastMessage(c), checks)
                    : BrandValidation.Fail("hue", HueMessage(c), checks);
            }
            return BrandValidation.Valid(checks);
        }
    }

    /// <summary>Primer campo de <paramref name="obj"/> que no es de la marca (o es un color de estado); null si todos son conocidos.</summary>
    private static BrandValidation? ForeignKey(JsonElement obj, string[] known, string prefix)
    {
        foreach (var p in obj.EnumerateObject())
        {
            if (StatusKeys.Contains(p.Name.ToLowerInvariant())) return BrandValidation.Fail("statusColor", StatusColorMessage(prefix + p.Name));
            if (!known.Contains(p.Name)) return BrandValidation.Fail("unknownField", UnknownFieldMessage(prefix + p.Name));
        }
        return null;
    }

    // ------------------------------------------------------------------ comprobaciones

    /// <summary>Colores efectivos: los del tema, o los propios con el neutro acotado (tono y saturación).</summary>
    public static (string Flow, string Money, double Nh, double Ns) Colors(BrandSettings b)
    {
        if (!b.UseCustom)
        {
            var p = PresetById(b.Preset) ?? Presets[0];
            return (p.Flow, p.Money, p.Nh, p.Ns);
        }
        var (h, s, _) = HslOf(IsValidHex(b.Neutral) ? b.Neutral : DefaultSettings.Neutral);
        return (NormalizeHex(b.Flow), NormalizeHex(b.Money), h, Math.Min(NeutralSatMax, s));
    }

    /// <summary>
    /// Contraste de texto, texto atenuado y los dos acentos contra el panel (derivado del propio tema) en los DOS modos —oscuro y
    /// luego claro; en cada uno text, muted, flow, money— y la separación de matiz entre los acentos.
    /// </summary>
    public static IReadOnlyList<BrandCheck> Checks(BrandSettings b)
    {
        var c = Colors(b);
        var list = new List<BrandCheck>();
        foreach (var mode in new[] { "dark", "light" })
        {
            var surf = DeriveSurfaces(c.Nh, c.Ns, mode);
            var panel = surf["--panel"];
            (string Key, string Color, double Min)[] rows =
            [
                ("text", surf["--text"], 7),
                ("muted", surf["--muted"], 4.5),
                ("flow", AccentV2(c.Flow, mode), 4.5),
                ("money", AccentV2(c.Money, mode), 4.5),
            ];
            foreach (var (key, color, min) in rows)
            {
                var ratio = JsRound(ContrastRatio(color, panel) * 100) / 100;
                list.Add(new BrandCheck("contrast", key, mode, ratio, min, ratio >= min));
            }
        }
        var distance = JsRound(HueDistance(c.Flow, c.Money));
        list.Add(new BrandCheck("hue", null, null, distance, MinHueDistance, distance >= MinHueDistance));
        return list;
    }

    // ------------------------------------------------------------------ color (misma aritmética que brandTheme.ts)

    /// <summary>Math.round de JavaScript (el .5 sube), no el redondeo bancario de .NET.</summary>
    private static double JsRound(double v) => Math.Floor(v + 0.5);

    public static bool IsValidHex(string? h)
    {
        if (h is null) return false;
        var s = h.StartsWith('#') ? h[1..] : h;
        return (s.Length == 3 || s.Length == 6) && s.All(Uri.IsHexDigit);
    }

    /// <summary>'#abc' / 'abc' / '#aabbcc' → '#AABBCC'.</summary>
    public static string NormalizeHex(string h)
    {
        var s = h.Trim().Replace("#", "", StringComparison.Ordinal);
        if (s.Length == 3) s = string.Concat(s.Select(ch => new string(ch, 2)));
        return "#" + s.ToUpperInvariant();
    }

    private static (int R, int G, int B) HexToRgb(string hex)
    {
        var n = Convert.ToInt32(NormalizeHex(hex)[1..], 16);
        return ((n >> 16) & 255, (n >> 8) & 255, n & 255);
    }

    private static string RgbToHex(double r, double g, double b)
    {
        static string C(double v) => ((int)Math.Max(0, Math.Min(255, JsRound(v)))).ToString("x2", CultureInfo.InvariantCulture);
        return ("#" + C(r) + C(g) + C(b)).ToUpperInvariant();
    }

    private static (double H, double S, double L) HslOf(string hex)
    {
        var (ri, gi, bi) = HexToRgb(hex);
        double r = ri / 255.0, g = gi / 255.0, b = bi / 255.0;
        var mx = Math.Max(r, Math.Max(g, b));
        var mn = Math.Min(r, Math.Min(g, b));
        double h = 0, s = 0;
        var l = (mx + mn) / 2;
        if (mx != mn)
        {
            var d = mx - mn;
            s = l > 0.5 ? d / (2 - mx - mn) : d / (mx + mn);
            h = mx == r ? (g - b) / d + (g < b ? 6 : 0) : mx == g ? (b - r) / d + 2 : (r - g) / d + 4;
            h *= 60;
        }
        return (h, s, l);
    }

    private static string HslToHex(double h, double s, double l)
    {
        h = ((h % 360) + 360) % 360;
        s = Math.Max(0, Math.Min(1, s));
        l = Math.Max(0, Math.Min(1, l));
        var c = (1 - Math.Abs(2 * l - 1)) * s;
        var x = c * (1 - Math.Abs((h / 60 % 2) - 1));
        var m = l - c / 2;
        double r = 0, g = 0, b = 0;
        if (h < 60) { r = c; g = x; }
        else if (h < 120) { r = x; g = c; }
        else if (h < 180) { g = c; b = x; }
        else if (h < 240) { g = x; b = c; }
        else if (h < 300) { r = x; b = c; }
        else { r = c; b = x; }
        return RgbToHex((r + m) * 255, (g + m) * 255, (b + m) * 255);
    }

    private static double RelLum(string hex)
    {
        var (r, g, b) = HexToRgb(hex);
        static double F(int v8)
        {
            var v = v8 / 255.0;
            return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * F(r) + 0.7152 * F(g) + 0.0722 * F(b);
    }

    private static double ContrastRatio(string a, string b)
    {
        double l1 = RelLum(a), l2 = RelLum(b);
        return (Math.Max(l1, l2) + 0.05) / (Math.Min(l1, l2) + 0.05);
    }

    private static double HueDistance(string a, string b)
    {
        var d = Math.Abs(HslOf(a).H - HslOf(b).H) % 360;
        return d > 180 ? 360 - d : d;
    }

    /// <summary>Variante «-2» del acento: en oscuro aclara (lee sobre fondo oscuro); en claro oscurece.</summary>
    private static string AccentV2(string baseHex, string mode)
    {
        var (h, s, l) = HslOf(baseHex);
        return mode == "light"
            ? HslToHex(h, Math.Min(1, s + 0.08), Math.Max(0.2, l - 0.18))
            : HslToHex(h, Math.Min(1, s * 0.92), Math.Min(0.82, l + 0.2));
    }

    // Luminosidades de las superficies medidas sobre la paleta original de Teikem (tono 222, saturación 39 %).
    private static readonly Dictionary<string, Dictionary<string, double>> SurfL = new()
    {
        ["dark"] = new() { ["bg2"] = 0.067, ["bg"] = 0.088, ["panel"] = 0.129, ["panel2"] = 0.153, ["line2"] = 0.18, ["raise"] = 0.194, ["line"] = 0.231, ["faint"] = 0.429, ["muted"] = 0.614, ["text"] = 0.935, ["appTop"] = 0.15 },
        ["light"] = new() { ["text"] = 0.153, ["muted"] = 0.429, ["faint"] = 0.614, ["line"] = 0.904, ["raise"] = 0.925, ["line2"] = 0.937, ["bg"] = 0.955, ["panel2"] = 0.965, ["bg2"] = 0.986, ["panel"] = 1, ["appTop"] = 1 },
    };
    private const double MutedSat = 0.44;

    /// <summary>Solo las superficies que entran en las comprobaciones (panel, texto y texto atenuado) a partir del tono y la saturación del neutro.</summary>
    private static Dictionary<string, string> DeriveSurfaces(double nh, double ns, string mode)
    {
        var l = SurfL[mode];
        string C(double lum, double? sat = null) => HslToHex(nh, sat ?? ns, lum);
        return new()
        {
            ["--panel"] = mode == "light" ? "#FFFFFF" : C(l["panel"]),
            ["--text"] = C(l["text"]),
            ["--muted"] = C(l["muted"], ns * MutedSat),
        };
    }
}
