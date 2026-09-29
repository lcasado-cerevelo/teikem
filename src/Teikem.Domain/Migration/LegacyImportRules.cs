using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Teikem.Domain.Wms;

namespace Teikem.Domain.Migration;

/// <summary>
/// Lote 10 (P1) — reglas puras de la migración desde QuickBooks Desktop y el WMS MSWM (Advance Depot y Advance Solutions).
/// No conocen EF ni el sistema de archivos: el importador les pasa celdas ya leídas. Los mensajes del reporte son métodos
/// estáticos porque el manual y la FAQ los citan tal cual (cultura invariante en los números).
/// </summary>
public static partial class LegacyImportRules
{
    /// <summary>Categoría de Depot para los ítems con Category vacía en QuickBooks.</summary>
    public const string DepotDefaultCategory = "AxisCare";
    /// <summary>Categoría de Depot para los ítems con Category = 'CARTONES'.</summary>
    public const string DepotCartonsCategory = "CARTONES";
    /// <summary>Categoría de QuickBooks Depot cuyos ítems pertenecen a Advance Solutions (no van a Depot).</summary>
    public const string SolutionsQuickBooksCategory = "SOLUTIONS";
    /// <summary>Ciudad que se usa cuando la dirección de QuickBooks no trae una línea 'Ciudad, PR 00xxx'.</summary>
    public const string NoCity = "SIN CIUDAD";
    /// <summary>Estado por defecto de las direcciones (todas son de Puerto Rico).</summary>
    public const string DefaultState = "PR";
    /// <summary>Marca de texto vacío del WMS MSWM.</summary>
    public const string WmsEmptyMark = "§";
    /// <summary>Largo máximo de un teléfono normalizado.</summary>
    public const int PhoneMaxLength = 40;
    /// <summary>Mínimo de dígitos para considerar un valor como teléfono.</summary>
    public const int PhoneMinDigits = 7;

    // ---------------------------------------------------------------- mensajes exactos del reporte

    public static string SkuNormalized(string raw, string sku) => $"SKU '{raw}' normalizado a '{sku}'.";
    public static string NameFromSku(string sku) => $"El ítem {sku} no tiene descripción; se usa el SKU como nombre.";
    public static string NegativeQuantity(string sku, decimal qty)
        => $"El ítem {sku} tiene existencia negativa ({ProductRules.FormatQty(qty)}); no se carga saldo inicial.";
    public static string UnknownWmsSku(string sku, decimal qty)
        => $"El SKU {sku} tiene {ProductRules.FormatQty(qty)} unidades en el WMS y no está en QuickBooks; se crea en la categoría por defecto.";
    public static string LocationSkipped(string id) => $"La posición {id} del WMS no tiene zona destino; se omite.";
    public static string TestRecordSkipped(string name) => $"Registro de prueba descartado: '{name}'.";
    public static string AddressNotParsed(string name) => $"Dirección de '{name}' sin línea de ciudad; se usa '{NoCity}'.";
    public static string UnknownPaymentTerm(string qb) => $"Término de pago de QuickBooks sin equivalente: '{qb}'.";
    public static string UnknownCategory(string cat) => $"Categoría de QuickBooks sin regla: '{cat}'.";

    // ---------------------------------------------------------------- SKU

    /// <summary>
    /// SKU de QuickBooks o del WMS: quita TODOS los espacios en blanco (interiores y extremos) y pasa a mayúsculas invariantes
    /// ('1040 P' → '1040P', '171-ac-426-b' → '171-AC-426-B'). Null o vacío → null.
    /// </summary>
    public static string? NormalizeSku(string? raw)
    {
        if (raw is null) return null;
        var sku = SkuKey(raw);
        return sku.Length == 0 ? null : sku;
    }

    /// <summary>
    /// ¿La normalización cambió el SKU? Compara el valor recortado con el normalizado, distinguiendo mayúsculas
    /// ('171-ac-426-b' → '171-AC-426-B' también se informa con SkuNormalized).
    /// </summary>
    public static bool SkuChanged(string? raw, string? normalized)
        => !string.Equals(raw?.Trim(), normalized, StringComparison.Ordinal);

    /// <summary>Clave de cruce entre fuentes: mayúsculas invariantes y sin ningún espacio en blanco.</summary>
    public static string SkuKey(string sku)
    {
        var sb = new StringBuilder(sku?.Length ?? 0);
        foreach (var c in sku ?? "")
            if (!char.IsWhiteSpace(c)) sb.Append(char.ToUpperInvariant(c));
        return sb.ToString();
    }

    // ---------------------------------------------------------------- dirección de QuickBooks

    [GeneratedRegex(@"^(.+?),?\s+(PR|P\.R\.|Puerto Rico)\s*,?\s*(\d{5})?(-\d{4})?\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CityStateRegex();

    [GeneratedRegex(@"^(.+?)\s+(\d{5})(-\d{4})?$", RegexOptions.CultureInvariant)]
    private static partial Regex CityZipRegex();

    [GeneratedRegex(@"^[\d\s.\-()/]+$", RegexOptions.CultureInvariant)]
    private static partial Regex PhoneLineRegex();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRunRegex();

    /// <summary>
    /// Descompone las líneas 'Bill to 1..5' o 'Ship to 1..5' de QuickBooks:
    /// - la primera línea no vacía es Name;
    /// - Phone es la última línea con al menos 7 dígitos formada solo por dígitos, puntos, guiones, espacios, paréntesis o '/'
    ///   (con dos teléfonos separados por '/', el primero);
    /// - la línea de ciudad es la última que cumple 'Ciudad[,] PR|P.R.|Puerto Rico[,] [00xxx[-xxxx]]' (sin distinguir
    ///   mayúsculas) o, si ninguna, la última que cumple 'Ciudad 00xxx'; da City (sin comas ni espacios finales),
    ///   State = 'PR' y PostalCode;
    /// - las líneas restantes, en orden, son Line1 y Line2 (de la tercera en adelante se concatenan en Line2 con ' / ');
    /// - sin línea de ciudad: City = 'SIN CIUDAD', State = 'PR'; sin Line1: Line1 = Name.
    /// </summary>
    public static QuickBooksAddress ParseQuickBooksAddress(IReadOnlyList<string?> lines)
    {
        var clean = (lines ?? Array.Empty<string?>())
            .Select(l => l?.Trim()).Where(l => !string.IsNullOrEmpty(l)).Select(l => l!).ToList();
        if (clean.Count == 0) return new QuickBooksAddress(null, null, null, NoCity, DefaultState, null, null);

        var name = clean[0];
        var rest = clean.Skip(1).ToList();

        // Teléfono: la última línea con forma de teléfono.
        var phoneIdx = -1;
        for (var i = rest.Count - 1; i >= 0; i--)
            if (IsPhoneLine(rest[i])) { phoneIdx = i; break; }

        // Ciudad: primero el patrón con estado; si no, 'Ciudad 00xxx'. Se toma la última línea que cumple.
        var cityIdx = -1;
        string? city = null, zip = null;
        for (var i = rest.Count - 1; i >= 0 && cityIdx < 0; i--)
        {
            if (i == phoneIdx) continue;
            var m = CityStateRegex().Match(rest[i]);
            if (m.Success) { cityIdx = i; city = m.Groups[1].Value; zip = Zip(m.Groups[3], m.Groups[4]); }
        }
        for (var i = rest.Count - 1; i >= 0 && cityIdx < 0; i--)
        {
            if (i == phoneIdx) continue;
            var m = CityZipRegex().Match(rest[i]);
            if (m.Success) { cityIdx = i; city = m.Groups[1].Value; zip = Zip(m.Groups[2], m.Groups[3]); }
        }

        string? phone = null;
        if (phoneIdx >= 0)
        {
            var raw = rest[phoneIdx];
            var slash = raw.IndexOf('/');
            phone = NormalizePhone(slash >= 0 ? raw[..slash] : raw) ?? NormalizePhone(raw);
        }

        var others = rest.Where((_, i) => i != phoneIdx && i != cityIdx).ToList();
        var line1 = others.Count > 0 ? others[0] : name;
        var line2 = others.Count > 1 ? string.Join(" / ", others.Skip(1)) : null;

        var cleanCity = city is null ? null : CleanCity(city);
        return string.IsNullOrEmpty(cleanCity)
            ? new QuickBooksAddress(name, line1, line2, NoCity, DefaultState, null, phone)
            : new QuickBooksAddress(name, line1, line2, cleanCity, DefaultState, zip, phone);
    }

    private static string? Zip(Group five, Group plusFour)
        => five.Success ? five.Value + (plusFour.Success ? plusFour.Value : "") : null;

    /// <summary>Ciudad con espacios colapsados y sin comas ni espacios finales ('TOA BAJA ' → 'TOA BAJA').</summary>
    private static string CleanCity(string city) => WhitespaceRunRegex().Replace(city, " ").Trim().TrimEnd(',').Trim();

    private static bool IsPhoneLine(string line)
        => PhoneLineRegex().IsMatch(line) && line.Count(char.IsAsciiDigit) >= PhoneMinDigits;

    // ---------------------------------------------------------------- término de pago

    private static readonly Dictionary<string, string> PaymentTermMap = new(StringComparer.Ordinal)
    {
        ["CHEQUE"] = "CHEQUE",
        ["CASH"] = "CASH",
        ["ACH"] = "ACH",
        ["COD"] = "COD",
        ["DUEONRECEIPT"] = "COD",
        ["NET15"] = "NET15",
        ["NET20"] = "NET20",
        ["NET30"] = "NET30",
        ["NET45"] = "NET45",
        ["NET60"] = "NET60",
        ["CONSIGNMENT"] = "CONSIGNMENT",
        ["PKBYREP"] = "PK_BY_REP",
    };

    /// <summary>
    /// Término de pago de QuickBooks → código interno del catálogo PaymentTerm (sin distinguir mayúsculas ni espacios).
    /// Vacío → null; desconocido → null (el importador lo informa con UnknownPaymentTerm).
    /// </summary>
    public static string? MapPaymentTerm(string? qb)
    {
        if (string.IsNullOrWhiteSpace(qb)) return null;
        return PaymentTermMap.TryGetValue(SkuKey(qb), out var code) ? code : null;
    }

    // ---------------------------------------------------------------- posiciones y zonas del WMS

    [GeneratedRegex(@"^(\d{2})-([A-Za-z])-(\d{2})$", RegexOptions.CultureInvariant)]
    private static partial Regex RackBinRegex();

    /// <summary>
    /// Id de posición del WMS → código de posición de Teikem. 'NN-L-NN' ('01-a-24') → Code '01-A-24', Aisle '01', Level 'A',
    /// Position '24'. Cualquier otro id → Code = id recortado, en mayúsculas y con los espacios interiores reemplazados por
    /// '-' ('MATTRESS PISO DEPOT' → 'MATTRESS-PISO-DEPOT'), sin partes.
    /// </summary>
    public static BinParts ParseBinCode(string locationId)
    {
        var id = (locationId ?? "").Trim();
        var m = RackBinRegex().Match(id);
        if (m.Success)
        {
            var aisle = m.Groups[1].Value;
            var level = m.Groups[2].Value.ToUpperInvariant();
            var position = m.Groups[3].Value;
            return new BinParts($"{aisle}-{level}-{position}", aisle, level, position);
        }
        return new BinParts(WhitespaceRunRegex().Replace(id, "-").ToUpperInvariant(), null, null, null);
    }

    /// <summary>
    /// Zona destino de una posición del WMS: primero por MatchLocationIds (igualdad sin distinguir mayúsculas), luego por
    /// MatchDescription (la descripción del WMS empieza con ese texto, sin distinguir mayúsculas), respetando el orden de las
    /// reglas. Null = la posición no se migra (el importador lo informa con LocationSkipped). locationType se recibe para
    /// futuras reglas por tipo; hoy no interviene.
    /// </summary>
    public static string? ResolveZone(string locationId, string? description, int locationType, IReadOnlyList<ZoneRule> rules)
    {
        var id = (locationId ?? "").Trim();
        foreach (var rule in rules)
            if (rule.MatchLocationIds is { Count: > 0 } ids
                && ids.Any(x => string.Equals(x?.Trim(), id, StringComparison.OrdinalIgnoreCase)))
                return rule.Code;

        if (IsWmsEmpty(description)) return null;
        var desc = description!.Trim();
        foreach (var rule in rules)
            if (!string.IsNullOrWhiteSpace(rule.MatchDescription)
                && desc.StartsWith(rule.MatchDescription.Trim(), StringComparison.OrdinalIgnoreCase))
                return rule.Code;
        return null;
    }

    // ---------------------------------------------------------------- productos

    /// <summary>
    /// Categoría de Depot según la Category de QuickBooks: vacía → 'AxisCare'; 'CARTONES' → 'CARTONES'; 'SOLUTIONS' → null
    /// (el ítem es de Advance Solutions); cualquier otra → null (se descarta y se informa con UnknownCategory).
    /// </summary>
    public static string? DepotCategory(string? qbCategory)
    {
        var cat = qbCategory?.Trim();
        if (string.IsNullOrEmpty(cat)) return DepotDefaultCategory;
        if (string.Equals(cat, DepotCartonsCategory, StringComparison.OrdinalIgnoreCase)) return DepotCartonsCategory;
        return null;
    }

    /// <summary>¿El tipo de ítem de QuickBooks lleva inventario? ('Inventory Part' o 'Inventory Assembly').</summary>
    public static bool IsInventoryType(string? qbType)
    {
        var t = qbType?.Trim();
        return string.Equals(t, "Inventory Part", StringComparison.OrdinalIgnoreCase)
            || string.Equals(t, "Inventory Assembly", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Número de QuickBooks en cultura invariante: '1,045' → 1045, '-208' → -208, '0.74' → 0.74. Vacío, porcentajes ('10.5%')
    /// o texto no numérico → null.
    /// </summary>
    public static decimal? ParseQuickBooksNumber(string? raw)
    {
        var s = raw?.Trim();
        if (string.IsNullOrEmpty(s) || s.Contains('%')) return null;
        s = s.Replace(",", "").Replace("$", "");
        return decimal.TryParse(s, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingWhite
                | NumberStyles.AllowTrailingWhite, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    // ---------------------------------------------------------------- contactos

    private static readonly char[] EmailSeparators = { ';', ',', ' ', '\t', '\r', '\n' };

    /// <summary>
    /// Correos de 'Main Email': separa por ';', ',' y espacios, recorta, quita vacíos, los que no tienen '@' y los duplicados
    /// (sin distinguir mayúsculas), conservando el orden (el primero será el principal).
    /// </summary>
    public static IReadOnlyList<string> SplitEmails(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return Array.Empty<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        foreach (var part in raw.Split(EmailSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            if (part.Contains('@') && seen.Add(part)) result.Add(part);
        return result;
    }

    /// <summary>
    /// Teléfono: recortado y con los espacios colapsados; null si tiene menos de 7 dígitos; se corta a 40 caracteres.
    /// </summary>
    public static string? NormalizePhone(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var phone = WhitespaceRunRegex().Replace(raw.Trim(), " ");
        if (phone.Count(char.IsAsciiDigit) < PhoneMinDigits) return null;
        return phone.Length > PhoneMaxLength ? phone[..PhoneMaxLength].TrimEnd() : phone;
    }

    // ---------------------------------------------------------------- claves

    /// <summary>
    /// Valor del campo personalizado 'Código QuickBooks' (qb_code) de un cliente: el nombre del cliente de QuickBooks recortado
    /// tal cual (el código del cliente en Teikem lo genera ClientService).
    /// </summary>
    public static string QuickBooksCodeKey(string customer) => (customer ?? "").Trim();

    /// <summary>¿Texto vacío del WMS? (null, en blanco o la marca '§').</summary>
    public static bool IsWmsEmpty(string? value)
        => string.IsNullOrWhiteSpace(value) || value.Trim() == WmsEmptyMark;
}
