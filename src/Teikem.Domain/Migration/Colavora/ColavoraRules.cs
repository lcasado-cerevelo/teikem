using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Teikem.Domain.Constants;
using Teikem.Domain.Contacts;

namespace Teikem.Domain.Migration.Colavora;

/// <summary>
/// Migración de Colavora Courier (2026-10-18, dictado de Luis; análisis en docs/migracion/colavora/analisis.md).
/// Reglas PURAS (sin base ni servicios) que traducen lo que trae Colavora a lo que entiende Teikem: estatus, eventos,
/// razones, tipos de servicio/paquete/COD, códigos y fechas. Aparte del importador de almacén de Depot (import-legacy).
/// </summary>
public static class ColavoraRules
{
    /// <summary>Colavora guarda hora local de Puerto Rico sin zona (UTC-4 fijo, sin horario de verano).</summary>
    public static readonly TimeSpan PuertoRicoOffset = TimeSpan.FromHours(-4);

    /// <summary>Hora local de Colavora → UTC. Las fechas «1900-01-01» (vacío en Colavora) se devuelven null.</summary>
    public static DateTime? ToUtc(DateTime? local)
    {
        if (local is not { } l || l.Year < 1950) return null;
        return DateTime.SpecifyKind(l - PuertoRicoOffset, DateTimeKind.Utc);
    }

    public static DateTime ToUtc(DateTime local) => ToUtc((DateTime?)local) ?? DateTime.SpecifyKind(local, DateTimeKind.Utc);

    /// <summary>Fecha local (Puerto Rico) de un instante UTC.</summary>
    public static DateOnly LocalDate(DateTime utc) => DateOnly.FromDateTime(utc + PuertoRicoOffset);

    // ---------------------------------------------------------------- estatus de la orden (D3, D4, D4b)

    /// <summary>Días sin movimiento tras los cuales una orden Pending de Colavora se migra como Entregada (decisión D3: 5 días).</summary>
    public const int DefaultPendingCloseDays = 5;

    /// <summary>
    /// Estatus de Teikem para el estatus actual de una orden de Colavora. Los estatus «basura» (un bug de jun–jul 2026 escribió
    /// el tipo de evento en Status) devuelven null: el llamador recalcula desde el último evento de estatus.
    /// </summary>
    public static string? MapStatus(string? colavoraStatus, DateTime createdUtc, DateTime migrationUtc, int pendingCloseDays = DefaultPendingCloseDays, int? openDays = null)
    {
        var code = MapStatusCore(colavoraStatus, createdUtc, migrationUtc, openDays ?? pendingCloseDays);
        // opción de Luis (2026-10-20): con openDays, TODA orden abierta (sometida, recogida, sin asignar, asignada, en tránsito) con más días que esos se migra como Entregada
        if (openDays is int n && code is OrderStatuses.Submitted or OrderStatuses.Pickup or OrderStatuses.Unassigned or OrderStatuses.Assigned or OrderStatuses.InTransit
            && (migrationUtc - createdUtc).TotalDays > n)
            return OrderStatuses.Delivered;
        return code;
    }

    private static string? MapStatusCore(string? colavoraStatus, DateTime createdUtc, DateTime migrationUtc, int pendingCloseDays)
    {
        var s = (colavoraStatus ?? string.Empty).Trim();
        switch (s.ToUpperInvariant())
        {
            case "CREATED": return OrderStatuses.Submitted;
            case "PENDING":
                // D3: Pending con más de N días (contra la fecha de la migración) se cierra como Entregada; las recientes quedan Sometida.
                return (migrationUtc - createdUtc).TotalDays > pendingCloseDays ? OrderStatuses.Delivered : OrderStatuses.Submitted;
            case "PICKEDUP": return OrderStatuses.Pickup;
            case "INBOUND": case "UNASSIGN": case "UNASSIGNED": return OrderStatuses.Unassigned;
            case "ASSIGNED": return OrderStatuses.Assigned;
            case "OUR FOR DELIVERY": case "OUT FOR DELIVERY": case "IN TRANSIT": return OrderStatuses.InTransit;
            case "DELIVERED": case "COMPLETED": case "PARTIAL DELIVERY":
            case "DELIVERED_REOPEN": case "COMPLETED_REOPEN":
                return OrderStatuses.Delivered;   // D4 / D4b: Completed = Entregada + factura procesada; los pick up también Entregada
            case "ROUNDTRIP": return OrderStatuses.Returned;
            case "CANCELED": case "CANCELLED": return OrderStatuses.Cancelled;
            default: return null;
        }
    }

    /// <summary>¿El estatus actual de Colavora es de los que escribió el bug (tipo de evento en vez de estatus)?</summary>
    public static bool IsGarbageStatus(string? colavoraStatus)
        => MapStatus(colavoraStatus, DateTime.UnixEpoch, DateTime.UnixEpoch) is null;

    // ---------------------------------------------------------------- eventos (D9)

    /// <summary>Qué hace un evento de Colavora en Teikem.</summary>
    public enum EventKind
    {
        /// <summary>Cambio de estatus de la orden (TargetStatus).</summary>
        Status,
        /// <summary>Entrega registrada (Delivered/Completed): estatus Entregada + intento entregado.</summary>
        Delivered,
        /// <summary>Intento fallido (comentario con razón de no entrega): estatus Intento fallido + DeliveryAttempt.</summary>
        FailedAttempt,
        /// <summary>Asignación de chofer de entrega (viaje por manifiesto, D5).</summary>
        AssignDelivery,
        /// <summary>Asignación de chofer de recogido.</summary>
        AssignPickup,
        /// <summary>Manifiesto de entrega (número = chofer + fecha).</summary>
        ManifestDelivery,
        /// <summary>Manifiesto de recogido.</summary>
        ManifestPickup,
        /// <summary>Paperwork back = factura procesada (campos del Lote 126, D8).</summary>
        PaperworkBack,
        /// <summary>Precio aplicado (Price - Item / Extra Item / COD): se suma al precio congelado (D13).</summary>
        Price,
        /// <summary>Comentario del historial sin cambio de estatus.</summary>
        Comment,
    }

    public sealed record ClassifiedEvent(EventKind Kind, string? TargetStatus, string? ReasonCode, string? DriverCode, string? DriverName, bool Reassign, decimal? Amount, string Comment);

    private static readonly Regex DriverRx = new(@"^(?<re>Re-)?Assign (?:Delivery|PickUp) Driver:\s*(?<code>[^\s-]+)\s*-\s*(?<name>.*)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ReasonRx = new(@"^\s*(?<code>[0-9]{1,3})\s*-\s*(?<rest>.*)$", RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex EditStatusRx = new(@"Status CHANGED FROM\s+(?<from>\S+)\s+TO\s+(?<to>\S+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Razones de Colavora que son un intento fallido (D9): la orden pasa a Intento fallido y queda el DeliveryAttempt.</summary>
    public static readonly IReadOnlySet<string> FailedReasonCodes = new HashSet<string>(StringComparer.Ordinal)
    {
        "306", "307", "315", "335", "340", "360", // Advance Logistics
        "04", "05", "06", "07", "08", "09", "10", "11", // De La Paz
    };

    /// <summary>Razones que, aunque vengan en un evento Delivered, NO son una entrega (devolución, recogido, error).</summary>
    public static readonly IReadOnlySet<string> NonDeliveryReasonCodes = new HashSet<string>(StringComparer.Ordinal) { "104", "314", "330", "360" };

    /// <summary>Clasifica un evento de Shipments_Events por su EventType y el texto del evento.</summary>
    public static ClassifiedEvent Classify(string? eventType, string? eventText, decimal codAmount = 0)
    {
        var type = (eventType ?? string.Empty).Trim();
        var text = (eventText ?? string.Empty).Trim();
        var comment = text.Length == 0 ? type : text;
        var t = type.ToUpperInvariant();

        if (t.EndsWith("_DEL", StringComparison.Ordinal) || t.Contains("_DEL_", StringComparison.Ordinal))
            return new(EventKind.Comment, null, null, null, null, false, null, $"{type}: {comment}");

        switch (t)
        {
            case "CREATED": case "IMPORT":
                return new(EventKind.Status, OrderStatuses.Submitted, null, null, null, false, null, comment);
            case "PENDING":
                return new(EventKind.Comment, null, null, null, null, false, null, comment);
            case "PICKUP": case "PICKEDUP":
                return new(EventKind.Status, OrderStatuses.Pickup, ReasonOf(text), null, null, false, null, comment);
            case "INBOUND": case "INBOUD": case "UNASSIGNED": case "UNASSIGN":
                return new(EventKind.Status, OrderStatuses.Unassigned, null, null, null, false, null, comment);
            case "ASSIGNED":
                return new(EventKind.Status, OrderStatuses.Assigned, null, null, null, false, null, comment);
            case "ASSIGNDL":
            {
                var m = DriverRx.Match(text);
                return new(EventKind.AssignDelivery, OrderStatuses.Assigned, null, m.Success ? m.Groups["code"].Value : null,
                    m.Success ? CleanName(m.Groups["name"].Value) : null, m.Success && m.Groups["re"].Success, null, comment);
            }
            case "ASSIGNPK":
            {
                var m = DriverRx.Match(text);
                return new(EventKind.AssignPickup, null, null, m.Success ? m.Groups["code"].Value : null,
                    m.Success ? CleanName(m.Groups["name"].Value) : null, m.Success && m.Groups["re"].Success, null, comment);
            }
            case "MANIFEST-DL": return new(EventKind.ManifestDelivery, null, null, null, null, false, null, comment);
            case "MANIFEST-PK": return new(EventKind.ManifestPickup, null, null, null, null, false, null, comment);
            case "OUTBOUND": case "IN TRANSIT":
                return new(EventKind.Status, OrderStatuses.InTransit, null, null, null, false, null, comment);
            case "DELIVERED": case "COMPLETED":
            {
                var reason = ReasonOf(text);
                if (reason is not null && FailedReasonCodes.Contains(reason))
                    return new(EventKind.FailedAttempt, OrderStatuses.Failed, reason, null, null, false, null, comment);
                if (reason is not null && NonDeliveryReasonCodes.Contains(reason))
                    return new(EventKind.Status, reason == "104" ? OrderStatuses.Returned : OrderStatuses.Unassigned, reason, null, null, false, null, comment);
                if (t == "COMPLETED" && reason is "13" or "370")
                    return new(EventKind.PaperworkBack, null, reason, null, null, false, null, comment);
                return new(EventKind.Delivered, OrderStatuses.Delivered, reason, null, null, false, null, comment);
            }
            case "DELIVERED_REOPEN": case "COMPLETED_REOPEN":
                return new(EventKind.Status, OrderStatuses.Unassigned, null, null, null, false, null, comment);
            case "ROUNDTRIP":
                return new(EventKind.Status, OrderStatuses.Returned, ReasonOf(text), null, null, false, null, comment);
            case "CANCELED": case "CANCELLED":
                return new(EventKind.Status, OrderStatuses.Cancelled, null, null, null, false, null, comment);
            case "PWBACK": case "PWBACKAD":
                return new(EventKind.PaperworkBack, null, null, null, null, false, null, $"{type} {comment}".Trim());
            case "COMMENT": case "BOL COMMENT":
            {
                var reason = ReasonOf(text);
                if (reason is not null && FailedReasonCodes.Contains(reason))
                    return new(EventKind.FailedAttempt, OrderStatuses.Failed, reason, null, null, false, null, comment);
                return new(EventKind.Comment, null, reason, null, null, false, null, comment);
            }
            case "EDIT":
            {
                var m = EditStatusRx.Match(text);
                if (m.Success && MapStatus(m.Groups["to"].Value, DateTime.UnixEpoch, DateTime.UnixEpoch) is { } to && to != OrderStatuses.Delivered)
                    return new(EventKind.Status, to, null, null, null, false, null, comment);
                return new(EventKind.Comment, null, null, null, null, false, null, comment);
            }
            default:
                if (t.StartsWith("PRICE", StringComparison.Ordinal))
                    return new(EventKind.Price, null, null, null, null, false, ParseAmount(text), $"{type}: {comment}");
                if (t == "COD")
                    return new(EventKind.Comment, null, null, null, null, false, codAmount > 0 ? codAmount : null, $"COD: {comment}");
                return new(EventKind.Comment, null, null, null, null, false, null, type.Length == 0 ? comment : $"{type}: {comment}");
        }
    }

    /// <summary>Código de razón al inicio del texto («312 - DELIVERED by: …» → 312; «03 - Orden Rechazada» → 03).</summary>
    public static string? ReasonOf(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var m = ReasonRx.Match(text);
        return m.Success ? m.Groups["code"].Value : null;
    }

    /// <summary>Monto de un evento Price («1.00», «12.5»); null si no es número.</summary>
    public static decimal? ParseAmount(string? text)
        => decimal.TryParse((text ?? string.Empty).Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var v) ? v : null;

    /// <summary>Texto del evento Delivered: «312 - DELIVERED by: Carrier Axis (correo) Comment: lo que dijo» → quién y el comentario.</summary>
    public static (string? By, string? Comment) ParseDeliveredText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return (null, null);
        var by = Regex.Match(text, @"by:\s*(?<by>.*?)\s*(?:Comment:|$)", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        var cm = Regex.Match(text, @"Comment:\s*(?<c>.*)$", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        var who = by.Success ? by.Groups["by"].Value.Trim() : null;
        var comment = cm.Success ? cm.Groups["c"].Value.Trim() : null;
        return (string.IsNullOrEmpty(who) ? null : who, string.IsNullOrEmpty(comment) ? null : comment);
    }

    /// <summary>Nombres con acentos rotos en los eventos (VillafaÃ±e): se intenta leerlos como UTF-8 mal decodificado.</summary>
    public static string CleanName(string? name)
    {
        var n = (name ?? string.Empty).Trim();
        if (n.Contains('Ã') || n.Contains('Â'))
        {
            try
            {
                var bytes = Encoding.Latin1.GetBytes(n);
                var fixedName = Encoding.UTF8.GetString(bytes);
                if (!fixedName.Contains('�')) n = fixedName;
            }
            catch (EncoderFallbackException) { }
        }
        return Regex.Replace(n, @"\s{2,}", " ");
    }

    /// <summary>Manifiesto de Colavora = código de chofer + aaaammdd («83720261017» → chofer 837, 2026-10-17; «80120240121-Pk» → 801 recogido).</summary>
    public static (string DriverCode, DateOnly Date)? ParseManifest(string? manifest)
    {
        if (string.IsNullOrWhiteSpace(manifest)) return null;
        var m = manifest.Trim();
        var dash = m.IndexOf('-');
        if (dash > 0) m = m[..dash];
        if (m.Length < 9) return null;
        var datePart = m[^8..];
        if (!DateOnly.TryParseExact(datePart, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) return null;
        var code = m[..^8].TrimStart('0');
        return (code.Length == 0 ? "0" : code, date);
    }

    /// <summary>Código del viaje (Trip) de Teikem para un manifiesto de Colavora: único por compañía.</summary>
    public static string TripCode(string manifest) => "M-" + manifest.Trim();

    // ---------------------------------------------------------------- catálogos (D14: por compañía)

    /// <summary>Código interno de catálogo a partir de un nombre («NEXT DAY ISLAND» → NEXT_DAY_ISLAND; «5 GAL CONTAINER» → CONTAINER_5_GAL).</summary>
    public static string ToCode(string? name, int maxLength = 40)
    {
        var s = RemoveDiacritics((name ?? string.Empty).Trim().ToUpperInvariant());
        s = Regex.Replace(s, "[^A-Z0-9]+", "_").Trim('_');
        if (s.Length > 0 && char.IsDigit(s[0])) s = "N" + s;
        if (s.Length == 0) s = "SIN_NOMBRE";
        return s.Length > maxLength ? s[..maxLength].TrimEnd('_') : s;
    }

    private static string RemoveDiacritics(string text)
    {
        var norm = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(norm.Length);
        foreach (var c in norm)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    /// <summary>Tipos de servicio de Colavora que ya existen globales en Teikem (se reutilizan; el resto se crea por compañía).</summary>
    public static string? GlobalServiceType(string? colavoraType) => (colavoraType ?? string.Empty).Trim().ToUpperInvariant() switch
    {
        "ND" => "STANDARD",
        "SD" => "SAMEDAY",
        _ => null,
    };

    /// <summary>Abreviaturas de servicio de Colavora que son «pick up» (recojo y traigo), para la marca del tipo de servicio.</summary>
    public static bool IsPickupServiceType(string? colavoraType, bool isForPickupFlag)
        => isForPickupFlag || (colavoraType ?? string.Empty).Trim().ToUpperInvariant() is "PK" or "PI" or "PV";

    /// <summary>Tipos de paquete de Colavora que ya existen globales en Teikem.</summary>
    public static string? GlobalPackageType(string? colavoraType) => (colavoraType ?? string.Empty).Trim().ToUpperInvariant() switch
    {
        "BX" => "BOX",
        "EN" => "ENVELOPE",
        "PL" => "PALLET",
        _ => null,
    };

    /// <summary>¿La pieza es la factura del cliente (tipo de paquete Invoice) y no una pieza física (D8)?</summary>
    public static bool IsInvoicePackageType(string? colavoraType) => string.Equals((colavoraType ?? string.Empty).Trim(), "IN", StringComparison.OrdinalIgnoreCase);

    /// <summary>Tipo de COD de la orden (texto libre en Colavora) → código de CodType de Teikem. Los que no existen se crean por compañía.</summary>
    public static string MapCodType(string? text, decimal? amount)
    {
        var t = (text ?? string.Empty).Trim().ToUpperInvariant();
        if (t.Length == 0 || t == "NONE") return amount is > 0 ? "CASH" : "NONE";
        return t switch
        {
            "CASH" or "CASH ONLY" => "CASH",
            "CHECK" => "CHECK",
            "CASH OR CHECK" or "CASH/CHECK" => "CASH_OR_CHECK",
            "CREDIT CARD" => "CREDIT_CARD",
            "AUTHORIZED" => "AUTHORIZED",
            "MONEY ORDER" => "MONEY_ORDER",
            "ATH MOVIL" or "ATH MÓVIL" => "ATH_MOVIL",
            "MIXED PAYMENT" => "MIXED",
            _ => ToCode(t),
        };
    }

    // ---------------------------------------------------------------- clientes, consignatarios y piezas

    /// <summary>Código de cliente de Teikem (máximo 10, mayúsculas) a partir del Sender de Colavora.</summary>
    public static string ClientCode(string? sender)
    {
        var s = Regex.Replace((sender ?? string.Empty).Trim().ToUpperInvariant(), @"\s+", "");
        if (s.Length == 0) s = "SIN_CODIGO";
        return s.Length > 10 ? s[..10] : s;
    }

    /// <summary>Clientes de prueba de Colavora que no se migran (D5 del análisis): copias -DEV / -PCK, SAMPLE y la compañía demo.</summary>
    public static bool IsTestSender(string? sender, string? senderName, IEnumerable<string>? patterns = null)
    {
        var s = (sender ?? string.Empty).Trim();
        var n = (senderName ?? string.Empty).Trim();
        var pats = patterns?.ToList() ?? new List<string> { "-DEV", "-PCK", "SAMPLE:" };
        return pats.Any(p => s.Contains(p, StringComparison.OrdinalIgnoreCase) || n.Contains(p, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Primer texto no vacío (direcciones de Colavora: Address1 a veces vacío y la dirección en Address2).</summary>
    public static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim() ?? string.Empty;

    /// <summary>Recorta a la longitud máxima (los textos de Colavora traen espacios al final).</summary>
    public static string? Trim(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var v = Regex.Replace(value.Trim(), @"\s{2,}", " ");
        return v.Length > max ? v[..max].TrimEnd() : v;
    }

    /// <summary>ZIP de 5 dígitos (Colavora trae «00949», «949», «00949-1234»); null si no hay dígitos.</summary>
    public static string? NormalizeZip(string? zip)
    {
        if (string.IsNullOrWhiteSpace(zip)) return null;
        var digits = new string(zip.TakeWhile(c => c != '-').Where(char.IsDigit).ToArray());
        if (digits.Length == 0) return null;
        return digits.Length >= 5 ? digits[..5] : digits.PadLeft(5, '0');
    }

    /// <summary>Estado: Colavora trae «PR», «P.R.», ZIPs y vacíos. Todo lo que no sea un estado de dos letras queda PR.</summary>
    public static string NormalizeState(string? state)
    {
        var s = Regex.Replace((state ?? string.Empty).Trim().ToUpperInvariant(), "[^A-Z]", "");
        return s.Length == 2 ? s : "PR";
    }

    /// <summary>Número de pieza de Teikem (≤ 40) para una pieza de Colavora: su código de barras o, sin él, waybill-secuencia.</summary>
    public static string PieceNumber(string? barcode, string waybill, int sequence)
    {
        var b = (barcode ?? string.Empty).Trim();
        if (b.Length == 0) b = $"{waybill}-{sequence.ToString(CultureInfo.InvariantCulture)}";
        return b.Length > 40 ? b[..40] : b;
    }

    /// <summary>Número de pieza alterno cuando el de Colavora ya existe en la compañía (los códigos de barra se repiten entre órdenes).</summary>
    public static string PieceNumberAlternate(string number, int colavoraPieceId)
    {
        var suffix = "~" + colavoraPieceId.ToString(CultureInfo.InvariantCulture);
        var baseLen = Math.Min(number.Length, 40 - suffix.Length);
        return number[..baseLen] + suffix;
    }

    /// <summary>Descripción de la línea de carga: el comentario de la pieza de Colavora (AxisCare: el equipo) o el tipo de paquete.</summary>
    public static string LineDescription(string? pieceComment, string packageTypeName)
        => Trim(pieceComment, 250) ?? Trim(packageTypeName, 250) ?? "Pieza";

    // ---------------------------------------------------------------- usuarios (D11)

    /// <summary>SecGroup 4 de colavora_courier_login.Users = chofer.</summary>
    public static bool IsDriver(int? secGroup) => secGroup == 4;

    /// <summary>Código de empleado del chofer: el username sin el dominio («812», «2006@delapaz» → 2006, «kenneth»).</summary>
    public static string DriverCode(string? username)
    {
        var u = (username ?? string.Empty).Trim();
        var at = u.IndexOf('@');
        if (at > 0) u = u[..at];
        u = u.ToUpperInvariant();
        return u.Length > 20 ? u[..20] : u;
    }

    /// <summary>Teléfonos leídos de un campo de Colavora: cada número separado (prefijo + solo dígitos), la extensión del primero y lo que no se pudo leer.</summary>
    public sealed record ColavoraPhones(IReadOnlyList<PhoneParts> Numbers, string? Extension, IReadOnlyList<string> Warnings);

    private static readonly Regex PhoneExtension = new(@"\s*(?:\bext(?:ension|ensión)?\.?|\bx|#)\s*(\d{1,6})\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Teléfono(s) de Colavora → número(s) solo con dígitos y prefijo (dictado de Luis, 2026-10-20). El campo es texto libre: «(787) 555-1234»,
    /// «787-555-1234 ext 12», varios números separados por «/» o «;», 7 dígitos sin código de área, y basura («PR», «9.99999E+11», «(787»).
    /// Lo que se puede leer se devuelve; lo que no, va en <c>Warnings</c> (nunca se guarda texto suelto como teléfono). <paramref name="defaultCountry"/> = prefijo de la compañía.
    /// </summary>
    public static ColavoraPhones ParsePhones(string? raw, string defaultCountry = "1")
    {
        var numbers = new List<PhoneParts>();
        var warnings = new List<string>();
        var text = (raw ?? string.Empty).Trim();
        if (text.Length == 0) return new ColavoraPhones(numbers, null, warnings);
        if (Regex.IsMatch(text, @"\d[eE][+-]?\d"))
        {
            warnings.Add($"Teléfono dañado (notación científica de Excel): «{Trim(text, 60)}».");
            return new ColavoraPhones(numbers, null, warnings);
        }
        string? extension = null;
        var em = PhoneExtension.Match(text);
        if (em.Success) { extension = em.Groups[1].Value; text = text[..em.Index]; }
        foreach (var segment in Regex.Split(text, @"\s*[/;]\s*"))
        {
            var seg = segment.Trim().TrimEnd('-', '.', ',', ' ');
            if (seg.Length == 0) continue;
            try
            {
                var p = PhoneRules.Parse(seg, null, defaultCountry);
                if (!numbers.Contains(p)) numbers.Add(p);
            }
            catch (FormatException) { warnings.Add($"No se pudo leer el teléfono «{Trim(seg, 60)}»."); }
        }
        if (numbers.Count == 0) extension = null;
        return new ColavoraPhones(numbers, extension, warnings);
    }

    /// <summary>
    /// Código del chofer sin ceros a la izquierda: Colavora lo escribe «003» en el usuario y a veces «3» en los eventos y manifiestos;
    /// los dos son el mismo chofer. «000» queda «0».
    /// </summary>
    public static string DriverCodeAlias(string? code)
    {
        var c = (code ?? string.Empty).Trim().TrimStart('0').ToUpperInvariant();
        return c.Length == 0 ? "0" : c;
    }

    /// <summary>¿El username de Colavora es un correo real (al que se le puede mandar la invitación)?</summary>
    public static bool LooksLikeEmail(string? username)
    {
        var u = (username ?? string.Empty).Trim();
        var at = u.IndexOf('@');
        return at > 0 && at < u.Length - 1 && u.IndexOf('.', at) > at && !u.Contains(' ');
    }

    /// <summary>Nombre completo del usuario («Pedro» + «Hernandez»; sin nombre, el username).</summary>
    public static string FullName(string? first, string? last, string? username)
    {
        var n = Regex.Replace($"{first} {last}".Trim(), @"\s{2,}", " ");
        return n.Length > 0 ? n : (username ?? "Usuario").Trim();
    }

    /// <summary>Rol de Teikem para un usuario de staff de Colavora: SecAdmin → TenantAdmin; el resto Dispatcher (operador).</summary>
    public static string StaffRole(bool? secAdmin) => secAdmin == true ? "TenantAdmin" : "Dispatcher";
}
