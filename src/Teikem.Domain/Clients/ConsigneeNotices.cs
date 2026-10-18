using Teikem.Domain.Common;
using Teikem.Domain.Constants;

namespace Teikem.Domain.Clients;

/// <summary>Canales de un aviso al consignatario: correo y texto (la preferencia de texto se guarda; no sale hasta contratar el proveedor).</summary>
public static class ConsigneeNoticeChannels
{
    public const string Email = "EMAIL";
    public const string Sms = "SMS";
    public static string? Normalize(string? raw)
    {
        var c = raw?.Trim().ToUpperInvariant();
        return c is Email or Sms ? c : null;
    }
}

public static class ConsigneeNoticeKinds
{
    public const string Status = "STATUS";
    public const string Consent = "CONSENT";
}

public static class ConsigneeNoticeStatuses
{
    public const string Pending = "PENDING";
    public const string Sent = "SENT";
    public const string Skipped = "SKIPPED";
    public const string Failed = "FAILED";
}

public static class ConsigneeNoticeSources
{
    public const string Client = "CLIENT";
    public const string Consignee = "CONSIGNEE";
}

/// <summary>
/// Lote 116 — avisos al consignatario (dictado por Luis; docs/p12). El CLIENTE (no la compañía) prende el aviso y marca, por consignatario, si acepta correo y/o texto; al marcarlo sale una advertencia (política de privacidad), queda en la
/// bitácora y se le notifica al consignatario por el medio marcado. El enlace con TOKEN propio sale cuando la orden sale «En ruta» y se sigue avisando de intento fallido, otra salida y los estatus finales; nunca de los movimientos dentro del
/// almacén (Recogida, Sin asignar, Asignada) ni de los estatus brincados. Toda comunicación trae la opción de darse de baja.
/// </summary>
public static class ConsigneeNoticeRules
{
    /// <summary>Los estatus que se avisan: En tránsito (sale En ruta), Intento fallido y los finales.</summary>
    public static readonly string[] NoticeStatuses =
    {
        OrderStatuses.InTransit, OrderStatuses.Failed, OrderStatuses.Delivered, OrderStatuses.Cancelled, OrderStatuses.Returned, OrderStatuses.InWarehouse,
    };

    public static bool ShouldNotify(string? statusCode) => NoticeStatuses.Contains(statusCode ?? string.Empty, StringComparer.OrdinalIgnoreCase);

    public const int MaxAttempts = 4;
    public static readonly TimeSpan[] RetryDelays = { TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30) };

    public const string WarningRequiredMessage = "Para marcar que el consignatario recibe avisos debe aceptar la advertencia de privacidad.";
    public const string NotEnabledMessage = "Primero prenda el aviso al consignatario.";
    public const string InvalidChannelMessage = "Indique el medio del aviso: correo o texto.";
    public const string NoEmailMessage = "El consignatario no tiene un correo registrado: agréguelo antes de marcarlo.";
    public const string NoPhoneMessage = "El consignatario no tiene un teléfono registrado: agréguelo antes de marcarlo.";
    public const string InvalidLangMessage = "Indique el idioma de los avisos: español (es) o inglés (en).";
    public const string ConsigneeNotFoundMessage = "Consignatario";
    public const string UnsubscribeNotFoundMessage = "El enlace de baja no es válido.";
    public const string TrackLinkNotFoundMessage = "No encontramos ese enlace de seguimiento.";

    /// <summary>La advertencia que se le muestra al cliente antes de marcar (parte de la política de privacidad).</summary>
    public static string Warning(string lang) => lang == "en"
        ? "By continuing you confirm that the consignee agreed to receive notices about their shipments through this medium. The consignee will be told that you marked them, every message will include a way to unsubscribe, and this change will be recorded."
        : "Si desea continuar, confirma que el consignatario aceptó recibir avisos sobre sus envíos por este medio. Se le notificará al consignatario que usted lo marcó, cada mensaje traerá la opción de darse de baja y este cambio quedará registrado en la bitácora.";

    public static string Subject(string status, string company, string lang)
    {
        var en = lang == "en";
        var what = status switch
        {
            OrderStatuses.InTransit => en ? "is on its way" : "va en camino",
            OrderStatuses.Failed => en ? "had a failed delivery attempt" : "tuvo un intento de entrega fallido",
            OrderStatuses.Delivered => en ? "was delivered" : "fue entregado",
            OrderStatuses.Cancelled => en ? "was cancelled" : "fue cancelado",
            OrderStatuses.Returned => en ? "was returned" : "fue devuelto",
            OrderStatuses.InWarehouse => en ? "is at our warehouse" : "está en nuestro almacén",
            _ => en ? "has an update" : "tiene una novedad",
        };
        return en ? $"{company}: your package {what}" : $"{company}: su paquete {what}";
    }

    public static string ConsentSubject(string client, string lang) => lang == "en" ? $"{client} will send you notices about your shipments" : $"{client} le enviará avisos sobre sus envíos";
}

/// <summary>Bitácora de las marcas del cliente («este consignatario acepta avisos por correo/texto»): quién, cuándo, con qué contacto y que aceptó la advertencia. También la baja del propio consignatario (Source CONSIGNEE).</summary>
public class ConsigneeNoticeConsent : ITenantScoped
{
    public int ConsigneeNoticeConsentId { get; set; }
    public int TenantId { get; set; }
    public int ClientId { get; set; }
    public int LocationId { get; set; }
    public string Channel { get; set; } = string.Empty;
    public bool Accepted { get; set; }
    public string Contact { get; set; } = string.Empty;
    public bool WarningAccepted { get; set; }
    public string Source { get; set; } = ConsigneeNoticeSources.Client;
    public int? ChangedBy { get; set; }
    public DateTime ChangedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>Un aviso al consignatario en cola: de alta (CONSENT) o de estatus (STATUS) de una orden. El Token es el del enlace de baja.</summary>
public class ConsigneeNotice : ITenantScoped
{
    public int ConsigneeNoticeId { get; set; }
    public int TenantId { get; set; }
    public int ClientId { get; set; }
    public int LocationId { get; set; }
    public int? TransportOrderId { get; set; }
    public string Kind { get; set; } = ConsigneeNoticeKinds.Status;
    public string Channel { get; set; } = ConsigneeNoticeChannels.Email;
    public string Contact { get; set; } = string.Empty;
    public string? StatusCode { get; set; }
    public string Lang { get; set; } = "es";
    public string Token { get; set; } = string.Empty;
    public string Status { get; set; } = ConsigneeNoticeStatuses.Pending;
    public int Attempts { get; set; }
    public DateTime? NextAttemptUtc { get; set; }
    public DateTime? SentAtUtc { get; set; }
    public string? Message { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>Un medio de contacto que se dio de baja de los avisos de la compañía (no se le manda nada más).</summary>
public class ConsigneeUnsubscribe : ITenantScoped
{
    public int ConsigneeUnsubscribeId { get; set; }
    public int TenantId { get; set; }
    public string Channel { get; set; } = string.Empty;
    public string Contact { get; set; } = string.Empty;
    public DateTime AtUtc { get; set; } = DateTime.UtcNow;
}
