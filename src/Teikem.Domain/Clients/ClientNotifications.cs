using Teikem.Domain.Common;

namespace Teikem.Domain.Clients;

/// <summary>Constantes de los avisos al cliente (Lote 98, P10).</summary>
public static class ClientNotificationCodes
{
    /// <summary>EntityType de la bitácora de la configuración de avisos de un cliente (sembrado en la sección 2026-10-17 (ag) de update.sql).</summary>
    public const string SettingEntityType = "CLIENT_NOTIFICATION_SETTING";
}

/// <summary>
/// Qué avisa Teikem a un cliente (el sender, quien envía; NO al consignatario). Todos son RESÚMENES, no el detalle de cada orden. PICKED_UP, OUT_FOR_DELIVERY, DELIVERED (transporte) y WH_RECEIVED, WH_DISPATCHED
/// (almacén: aviso de llegada recibido, aviso de salida despachado) entran en UN SOLO resumen al día por cliente, a la hora que el cliente escoge (Client.NoticeDigestHour); si no hubo nada, no se manda nada.
/// COD_PROCESSED (cuando una remesa de COD se remite) e INVOICE_ISSUED (cuando se emite una factura) salen aparte, uno por documento.
/// </summary>
public static class ClientNotificationKinds
{
    public const string PickedUp = "PICKED_UP";
    public const string OutForDelivery = "OUT_FOR_DELIVERY";
    public const string Delivered = "DELIVERED";
    public const string CodProcessed = "COD_PROCESSED";
    public const string InvoiceIssued = "INVOICE_ISSUED";
    /// <summary>Almacén: un aviso de llegada del cliente se recibió (RefId = el aviso).</summary>
    public const string WhReceived = "WH_RECEIVED";
    /// <summary>Almacén: un aviso de salida del cliente se despachó, total o parcialmente (RefId = el aviso).</summary>
    public const string WhDispatched = "WH_DISPATCHED";
    /// <summary>Clase del resumen único del día (no se configura: junta los avisos de <see cref="DailyKinds"/> que el cliente tenga encendidos).</summary>
    public const string Daily = "DAILY";
    public static readonly string[] All = { PickedUp, OutForDelivery, Delivered, WhReceived, WhDispatched, CodProcessed, InvoiceIssued };
    /// <summary>Los avisos que entran en el resumen del día.</summary>
    public static readonly string[] DailyKinds = { PickedUp, OutForDelivery, Delivered, WhReceived, WhDispatched };
    public static bool IsDaily(string? k) => k is not null && DailyKinds.Contains(k.Trim().ToUpperInvariant());
    public static bool IsKnown(string? k) => k is not null && All.Contains(k.Trim().ToUpperInvariant());
}

/// <summary>Canales de un aviso al cliente: correo, la bandeja del portal (APP) y SMS (se guarda la preferencia; no sale hasta contratar el proveedor).</summary>
public static class ClientNotificationChannels
{
    public const string Email = "EMAIL";
    public const string App = "APP";
    public const string Sms = "SMS";
}

public static class ClientNotificationDeliveryStatuses
{
    public const string Pending = "PENDING";
    public const string Sent = "SENT";
    public const string Skipped = "SKIPPED";
    public const string Failed = "FAILED";
}

/// <summary>
/// Configuración de un aviso de un cliente (la decide la COMPAÑÍA, por cliente y por aviso): qué canales se encienden y a quién más se manda (correos y teléfonos sueltos, además de los usuarios del portal
/// del cliente). Sin fila, el aviso está apagado: un cliente no recibe nada hasta que la compañía lo encienda.
/// </summary>
[AuditEntity(ClientNotificationCodes.SettingEntityType)]
public class ClientNotificationSetting : ITenantScoped
{
    public int ClientNotificationSettingId { get; set; }
    public int TenantId { get; set; }
    public int ClientId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public bool EmailEnabled { get; set; }
    public bool AppEnabled { get; set; }
    public bool SmsEnabled { get; set; }
    /// <summary>Correos extra separados por coma (además de los usuarios activos del portal del cliente).</summary>
    public string? ExtraEmails { get; set; }
    /// <summary>Teléfonos extra para SMS separados por coma.</summary>
    public string? ExtraPhones { get; set; }
    [NotAudited] public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public int? UpdatedBy { get; set; }

    public bool AnyChannel => EmailEnabled || AppEnabled || SmsEnabled;
}

/// <summary>Un hecho que puede avisarse (se anota solo si el cliente tiene ese aviso encendido): una orden recogida, en ruta o entregada; una remesa COD remitida; una factura emitida. El resumen lo arma el trabajo.</summary>
public class ClientNotificationEvent : ITenantScoped
{
    public long ClientNotificationEventId { get; set; }
    public int TenantId { get; set; }
    public int ClientId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public int? TransportOrderId { get; set; }
    public int? TripId { get; set; }
    /// <summary>La remesa o la factura de un aviso de COD o de factura.</summary>
    public int? RefId { get; set; }
    public int Pieces { get; set; }
    public DateTime OccurredAtUtc { get; set; } = DateTime.UtcNow;
    /// <summary>El aviso (resumen) en que quedó incluido; nulo = todavía sin resumir.</summary>
    public long? NotificationId { get; set; }
}

/// <summary>El resumen que se le manda (y que queda en la bandeja del portal): un aviso por cliente y grupo de eventos.</summary>
public class ClientNotification : ITenantScoped
{
    public long ClientNotificationId { get; set; }
    [NotAudited] public Guid PublicId { get; set; }
    public int TenantId { get; set; }
    public int ClientId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string BodyText { get; set; } = string.Empty;
    /// <summary>Cifras del resumen: {"orders":N,"pieces":P,"cod":X,"trip":"CODIGO", ...}.</summary>
    public string? SummaryJson { get; set; }
    /// <summary>Visible en la bandeja del portal del cliente (el canal APP estaba encendido).</summary>
    public bool InPortal { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>Un envío de un aviso por un canal a un destinatario, con su resultado y reintentos.</summary>
public class ClientNotificationDelivery : ITenantScoped
{
    public long ClientNotificationDeliveryId { get; set; }
    public int TenantId { get; set; }
    public long ClientNotificationId { get; set; }
    public string Channel { get; set; } = ClientNotificationChannels.Email;
    public string Recipient { get; set; } = string.Empty;
    public string Status { get; set; } = ClientNotificationDeliveryStatuses.Pending;
    public string? Message { get; set; }
    public int Attempts { get; set; }
    public DateTime? NextAttemptUtc { get; set; }
    public DateTime? SentAtUtc { get; set; }
}

public static class ClientNotificationRules
{
    public const int MaxListLength = 400;
    public const int MaxRecipients = 10;
    /// <summary>Minutos sin eventos nuevos de un grupo antes de resumirlo (para que una ruta que sigue entregando salga en un solo aviso).</summary>
    public const int SettleMinutes = 3;
    public const int MaxAttempts = 3;
    /// <summary>Hora local (0–23) a la que sale el resumen del día de un cliente que no escogió otra.</summary>
    public const int DefaultDigestHour = 18;
    /// <summary>Cuántos números de documento o de ruta se nombran en una línea del resumen antes de poner «y N más».</summary>
    public const int MaxNamedInSummary = 8;
    public const string DigestHourInvalid = "La hora del resumen debe estar entre 0 y 23.";

    public const string KindInvalid = "El aviso no existe.";
    public const string ClientInvalid = "El cliente no existe.";
    public const string ExtraEmailsTooLong = "Los correos extra no pueden pasar de 400 caracteres.";
    public const string ExtraPhonesTooLong = "Los teléfonos extra no pueden pasar de 400 caracteres.";
    public const string TooManyRecipients = "Máximo 10 correos y 10 teléfonos extra por aviso.";
    public static string InvalidEmail(string v) => $"El correo '{v}' no es válido.";
    public static string InvalidPhone(string v) => $"El teléfono '{v}' no es válido.";
    public const string DuplicateKind = "Cada aviso solo puede venir una vez.";
    public const string Concurrency = "La configuración de avisos cambió mientras se guardaba. Revise y vuelva a intentar.";

    private static readonly char[] Separators = { ',', ';', '\n', '\r' };

    /// <summary>Correos válidos y distintos de una lista separada por coma o punto y coma; el primero mal escrito lanza FormatException con el mensaje exacto.</summary>
    public static IReadOnlyList<string> ParseEmails(string? text)
    {
        var list = new List<string>();
        foreach (var raw in (text ?? string.Empty).Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var ok = raw.Length <= 200 && raw.Contains('@') && !raw.Contains(' ') && raw.IndexOf('@') > 0 && raw.IndexOf('@') == raw.LastIndexOf('@') && raw[(raw.IndexOf('@') + 1)..].Contains('.') && !raw.EndsWith('.');
            if (!ok) throw new FormatException(InvalidEmail(raw));
            if (!list.Contains(raw, StringComparer.OrdinalIgnoreCase)) list.Add(raw);
        }
        if (list.Count > MaxRecipients) throw new FormatException(TooManyRecipients);
        return list;
    }

    /// <summary>Teléfonos (7 a 15 dígitos; se admiten +, espacios, guiones y paréntesis) distintos de una lista separada por coma.</summary>
    public static IReadOnlyList<string> ParsePhones(string? text)
    {
        var list = new List<string>();
        foreach (var raw in (text ?? string.Empty).Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var digits = raw.Count(char.IsDigit);
            if (raw.Length > 30 || digits < 7 || digits > 15 || raw.Any(c => !(char.IsDigit(c) || c is '+' or ' ' or '-' or '(' or ')'))) throw new FormatException(InvalidPhone(raw));
            if (!list.Contains(raw)) list.Add(raw);
        }
        if (list.Count > MaxRecipients) throw new FormatException(TooManyRecipients);
        return list;
    }
}

/// <summary>
/// Lote 119 (P10) — preferencia de una persona del portal sobre UN aviso: la COMPAÑÍA decide qué avisos recibe el cliente y por qué canal (<see cref="ClientNotificationSetting"/>, el techo) y la persona solo puede APAGAR lo
/// que no quiere recibir (correo y/o bandeja del portal). Sin fila, recibe todo lo que la compañía encendió. El SMS no es por persona (va a los teléfonos que la compañía anote).
/// </summary>
public class PortalNoticePref : ITenantScoped
{
    public int PortalNoticePrefId { get; set; }
    public int TenantId { get; set; }
    public int PortalUserId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public bool EmailEnabled { get; set; } = true;
    public bool AppEnabled { get; set; } = true;
    [NotAudited] public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
