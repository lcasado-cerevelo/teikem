namespace Teikem.Infrastructure.Contracts;

// Lote 98 (P10) — avisos al cliente. Records sin lógica; firma posicional fija.

/// <summary>La configuración de UN aviso de un cliente (sin fila guardada = todo apagado).</summary>
public sealed record ClientNotificationSettingDto(
    string Kind,
    bool EmailEnabled,
    bool AppEnabled,
    bool SmsEnabled,
    string? ExtraEmails,
    string? ExtraPhones);

/// <summary>Los avisos del cliente con su configuración, a cuántos usuarios del portal llegaría el correo y la hora local (0–23) a la que sale su resumen único del día.</summary>
public sealed record ClientNotificationSettingsDto(
    Guid ClientPublicId,
    string ClientName,
    int PortalRecipients,
    bool EmailConfigured,
    bool SmsConfigured,
    IReadOnlyList<ClientNotificationSettingDto> Settings,
    int DigestHour);

/// <summary>DigestHour: la hora local (0–23) del resumen del día; null = no cambiarla.</summary>
public sealed record ClientNotificationSettingsRequest(IList<ClientNotificationSettingDto>? Settings, int? DigestHour = null);

/// <summary>Un envío de un aviso por un canal. Status: PENDING, SENT, SKIPPED (no corresponde o el canal no está configurado) o FAILED (se agotaron los reintentos).</summary>
public sealed record ClientNotificationDeliveryDto(string Channel, string Recipient, string Status, string? Message, int Attempts, DateTime? SentAtUtc);

/// <summary>Un resumen que se le mandó al cliente (y que ve su portal).</summary>
public sealed record ClientNotificationDto(
    Guid PublicId,
    string Kind,
    string Subject,
    string BodyText,
    bool InPortal,
    DateTime CreatedAtUtc,
    IReadOnlyList<ClientNotificationDeliveryDto> Deliveries);
