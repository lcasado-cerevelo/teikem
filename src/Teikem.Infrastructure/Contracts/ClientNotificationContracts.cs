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

/// <summary>Los cinco avisos del cliente con su configuración y a cuántos usuarios del portal llegaría el correo.</summary>
public sealed record ClientNotificationSettingsDto(
    Guid ClientPublicId,
    string ClientName,
    int PortalRecipients,
    bool EmailConfigured,
    bool SmsConfigured,
    IReadOnlyList<ClientNotificationSettingDto> Settings);

public sealed record ClientNotificationSettingsRequest(IList<ClientNotificationSettingDto>? Settings);

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
