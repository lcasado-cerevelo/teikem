using Teikem.Domain.Constants;

namespace Teikem.Infrastructure.Notifications;

/// <summary>Códigos de los eventos de aviso del aviso de llegada de un cliente (Lote 157, 2026-10-20). Hasta 60 caracteres.</summary>
public static class AsnEventCodes
{
    public const string Requested = "ASN_REQUESTED";
    public const string Updated = "ASN_UPDATED";
    public const string Received = "ASN_RECEIVED";
    public const string Cancelled = "ASN_CANCELLED";

    public static readonly string[] All = { Requested, Updated, Received, Cancelled };
}

/// <summary>
/// Lote 157 — los eventos de aviso del aviso de llegada (el que crea un cliente 3PL o el personal a nombre de un cliente): solicitado, modificado, recibido y cancelado. Los publica AsnService
/// (y ReceiptService al recibir) en la misma transacción de la operación. Todos opcionales (el usuario puede silenciarlos). Las reglas (a quién y por qué canal) las arma el administrador en
/// Sistema → Reglas de aviso: no se siembra ninguna. Los avisos nacidos de una orden de compra no avisan (son internos).
/// </summary>
public sealed class AsnEventProvider : INotificationEventProvider
{
    private static NotificationVariable[] Base(params NotificationVariable[] extra) => new[]
    {
        new NotificationVariable("number", "Aviso de llegada", "Arrival notice", "#12"),
        new NotificationVariable("reference", "Referencia", "Reference", "OC-778"),
        new NotificationVariable("client", "Cliente dueño", "Owner customer", "ASICS Care"),
        new NotificationVariable("warehouse", "Almacén", "Warehouse", "Almacén principal"),
        new NotificationVariable("expected", "Llegada esperada", "Expected arrival", "2026-11-02"),
    }.Concat(extra).ToArray();

    public IEnumerable<NotificationEventDef> Events { get; } = new[]
    {
        new NotificationEventDef(
            AsnEventCodes.Requested, ModuleKeys.WmsLotSerial, "Aviso de llegada solicitado", "Arrival notice requested",
            Base(new NotificationVariable("lineCount", "Líneas", "Lines", "3")),
            IsOptional: true, LinkTemplate: "/warehouse/receipts?tab=asns",
            "Aviso de llegada {number} solicitado", "Arrival notice {number} requested",
            "{client} avisó una llegada ({number}, {lineCount} líneas) para {warehouse}. Llegada esperada: {expected}. Referencia: {reference}.",
            "{client} announced an arrival ({number}, {lineCount} lines) for {warehouse}. Expected: {expected}. Reference: {reference}."),
        new NotificationEventDef(
            AsnEventCodes.Updated, ModuleKeys.WmsLotSerial, "Aviso de llegada modificado", "Arrival notice changed",
            Base(new NotificationVariable("lineCount", "Líneas", "Lines", "3")),
            IsOptional: true, LinkTemplate: "/warehouse/receipts?tab=asns",
            "Aviso de llegada {number} modificado", "Arrival notice {number} changed",
            "Se modificó el aviso de llegada {number} de {client} ({lineCount} líneas) para {warehouse}. Llegada esperada: {expected}. Referencia: {reference}.",
            "Arrival notice {number} for {client} was changed ({lineCount} lines) for {warehouse}. Expected: {expected}. Reference: {reference}."),
        new NotificationEventDef(
            AsnEventCodes.Received, ModuleKeys.WmsLotSerial, "Aviso de llegada recibido", "Arrival notice received",
            Base(),
            IsOptional: true, LinkTemplate: "/warehouse/receipts?tab=asns",
            "Aviso de llegada {number} recibido", "Arrival notice {number} received",
            "Se recibió el aviso de llegada {number} de {client} en {warehouse}. Referencia: {reference}.",
            "Arrival notice {number} for {client} was received at {warehouse}. Reference: {reference}."),
        new NotificationEventDef(
            AsnEventCodes.Cancelled, ModuleKeys.WmsLotSerial, "Aviso de llegada cancelado", "Arrival notice cancelled",
            Base(),
            IsOptional: true, LinkTemplate: "/warehouse/receipts?tab=asns",
            "Aviso de llegada {number} cancelado", "Arrival notice {number} cancelled",
            "Se canceló el aviso de llegada {number} de {client} para {warehouse}. Referencia: {reference}.",
            "Arrival notice {number} for {client} was cancelled for {warehouse}. Reference: {reference}."),
    };
}
