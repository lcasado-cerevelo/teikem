namespace Teikem.Infrastructure.Contracts;

// Lote 103 — «¿Cómo va mi orden?». La respuesta pública NUNCA lleva nombres, direcciones, teléfonos, montos ni el número de orden del cliente: solo el avance.

/// <summary>Una etapa de la barra de progreso: el estatus (clave y texto con el nombre que le puso la compañía), si ya pasó (Done) y si es la actual (Current).</summary>
public sealed record PublicTrackStepDto(string Key, string Label, bool Done, bool Current);

/// <summary>
/// El avance de un paquete: la barra tiene tantas etapas como estatus tiene el camino normal de las órdenes de la compañía (Steps; Total = cuántas). Step = la etapa actual (1 a Total; 0 si la orden fue cancelada).
/// State: IN_PROGRESS, COMPLETED (entregada), ALERT (detenida: en espera, parcial, fallida…; la barra se queda donde iba) o CANCELLED. StatusLabel = el estatus actual con su texto.
/// CompanyName es el nombre de la compañía (público, el de la marca), no datos de personas.
/// </summary>
public sealed record PublicTrackDto(string Waybill, string CompanyName, int Step, int Total, string State, string StatusLabel, IReadOnlyList<PublicTrackStepDto> Steps, DateTime? LastUpdateUtc);

/// <summary>Ajuste de la compañía: identificador de la dirección pública (null = apagado) y si está encendido.</summary>
public sealed record TrackingSettingsDto(string? Slug, bool Enabled);

public sealed record TrackingSettingsRequest(string? Slug);
