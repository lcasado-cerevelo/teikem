using Teikem.Domain.Clients;
using Teikem.Domain.Constants;

namespace Teikem.Domain.Orders
{

/// <summary>
/// Preguntas de numeración de la ficha del cliente (Lote 2): quién asigna el número de orden y el de factura
/// (dos preguntas independientes) y los patrones propios (NULL = patrón por defecto del sistema).
/// </summary>
public sealed record NumberingSettings(
    bool ClientAssignsOrderNumber,
    bool ClientAssignsInvoiceNumber,
    string? OrderNumberFormat,
    string? InvoiceNumberFormat,
    string? PackageNumberFormat)
{
    public static readonly NumberingSettings Default = new(false, false, null, null, null);

    public static NumberingSettings FromClient(Client client)
    {
        ArgumentNullException.ThrowIfNull(client);
        return new NumberingSettings(
            client.ClientAssignsOrderNumber,
            client.ClientAssignsInvoiceNumber,
            client.OrderNumberFormat,
            client.InvoiceNumberFormat,
            client.PackageNumberFormat);
    }
}

/// <summary>
/// Reglas puras (sin EF) de los cuatro identificadores de una orden: número de orden, número de factura del cliente,
/// número de empaque y número de paquete por línea. Decide qué patrón aplica, quién puede teclear cada número,
/// en qué ámbito corre el contador y cómo reaccionar a una colisión del índice único.
/// Documento: L237/L240 (consecutivo interno del cliente), L243/L832 (empaque siempre Teikem, EMP-#####).
/// </summary>
public static class NumberingRules
{
    /// <summary>Patrón fijo del número de empaque: nunca se configura por cliente ni por tenant.</summary>
    public const string PackBatchPattern = "EMP-#####";

    /// <summary>Longitud máxima de un número tecleado (misma que las columnas NVARCHAR(40)).</summary>
    public const int MaxTypedLength = NumberFormat.MaxLength;

    /// <summary>Reintentos ante colisión de un número automático con uno tecleado por alguien más.</summary>
    public const int MaxAutoRetries = 5;

    public const string OrderAssignedByTeikemMessage = "El número de orden lo asigna Teikem para este cliente; déjelo en blanco.";
    public const string InvoiceAssignedByTeikemMessage = "El número de factura lo asigna Teikem para este cliente; déjelo en blanco.";
    public const string PackBatchAlwaysTeikemMessage = "El número de empaque siempre lo genera Teikem.";

    /// <summary>Nombres de los índices únicos de dbo.TransportOrder cuya violación puede reintentarse.</summary>
    public const string OrderNumberIndex = "UX_Order_Number";
    public const string PackBatchIndex = "UX_Order_PackBatch";

    /// <summary>
    /// Orden fijo en que todo llamador dibuja los números dentro de la transacción del alta. Como cada NextAsync toma
    /// un bloqueo de fila que dura hasta el commit, adquirirlos siempre en el mismo orden evita interbloqueos entre
    /// altas simultáneas.
    /// </summary>
    public static readonly IReadOnlyList<string> DrawOrder =
        [NumberKinds.Order, NumberKinds.Invoice, NumberKinds.PackBatch, NumberKinds.Package];

    /// <summary>Patrón efectivo: el del cliente si lo tiene, si no el default del sistema; PACKBATCH siempre EMP-#####.</summary>
    public static string EffectivePattern(string kind, NumberingSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return kind switch
        {
            NumberKinds.Order => Coalesce(settings.OrderNumberFormat, NumberFormat.Defaults.Order),
            NumberKinds.Invoice => Coalesce(settings.InvoiceNumberFormat, NumberFormat.Defaults.Invoice),
            NumberKinds.Package => Coalesce(settings.PackageNumberFormat, NumberFormat.Defaults.Package),
            NumberKinds.PackBatch => PackBatchPattern,
            _ => throw UnknownKind(kind),
        };
    }

    /// <summary>
    /// Ámbito del contador: ORDER, INVOICE y PACKAGE se cuentan por cliente (consecutivo interno del cliente; el índice
    /// UX_Order_Number también es por cliente, así que dos clientes con el patrón por defecto no chocan);
    /// PACKBATCH es uno por tenant (ClientId NULL).
    /// </summary>
    public static int? ScopeClientId(string kind, int clientId) => kind switch
    {
        NumberKinds.Order or NumberKinds.Invoice or NumberKinds.Package => clientId,
        NumberKinds.PackBatch => null,
        _ => throw UnknownKind(kind),
    };

    /// <summary>
    /// ¿Puede el cliente teclear este número? ORDER/INVOICE según las dos preguntas de su ficha; PACKAGE siempre
    /// (por línea); PACKBATCH nunca.
    /// </summary>
    public static bool ClientAssigns(string kind, NumberingSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return kind switch
        {
            NumberKinds.Order => settings.ClientAssignsOrderNumber,
            NumberKinds.Invoice => settings.ClientAssignsInvoiceNumber,
            NumberKinds.Package => true,
            NumberKinds.PackBatch => false,
            _ => throw UnknownKind(kind),
        };
    }

    /// <summary>Vacío o espacios → null (automático); si no, Trim sin alterar mayúsculas; más de 40 caracteres → ArgumentException.</summary>
    public static string? NormalizeTyped(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        if (trimmed.Length > MaxTypedLength)
            throw new ArgumentException($"El número no puede exceder {MaxTypedLength} caracteres.");
        return trimmed;
    }

    /// <summary>
    /// Normaliza el valor tecleado y verifica que ese tipo de número pueda teclearse para el cliente. Devuelve el valor
    /// normalizado (null = lo genera Teikem) o lanza ArgumentException con el mensaje exacto para el usuario; el servicio
    /// lo traduce a ValidationException 400 con el campo correspondiente.
    /// </summary>
    public static string? EnsureTypedAllowed(string kind, NumberingSettings settings, string? typed)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var value = NormalizeTyped(typed);
        if (value is null)
        {
            _ = ClientAssigns(kind, settings); // valida el kind aunque no haya valor
            return null;
        }
        if (ClientAssigns(kind, settings)) return value;
        throw new ArgumentException(kind switch
        {
            NumberKinds.Order => OrderAssignedByTeikemMessage,
            NumberKinds.Invoice => InvoiceAssignedByTeikemMessage,
            NumberKinds.PackBatch => PackBatchAlwaysTeikemMessage,
            _ => throw UnknownKind(kind),
        });
    }

    /// <summary>Resuelve el patrón con el consecutivo (delegado a NumberFormat.Resolve: relleno con ceros, sin truncar).</summary>
    public static string Resolve(string pattern, long seq) => NumberFormat.Resolve(pattern, seq);

    /// <summary>
    /// Mensaje cuando el número automático, resuelto con el patrón del cliente, no cabe en las columnas NVARCHAR(40):
    /// Resolve nunca trunca (un consecutivo con más dígitos que '#' se antepone completo), así que un patrón largo termina
    /// desbordando. El servicio lo traduce a 409 y revierte la transacción del alta (con los consecutivos dibujados).
    /// </summary>
    public static string GeneratedTooLongMessage(string kind) =>
        $"El número {KindLabel(kind)} generado con el patrón del cliente excede {MaxTypedLength} caracteres; acorte el patrón en la ficha del cliente.";

    /// <summary>Resolve + comprobación de longitud: más de 40 caracteres → ArgumentException con GeneratedTooLongMessage(kind).</summary>
    public static string ResolveChecked(string kind, string pattern, long seq)
    {
        var result = Resolve(pattern, seq);
        if (result.Length > MaxTypedLength) throw new ArgumentException(GeneratedTooLongMessage(kind));
        return result;
    }

    private static string KindLabel(string kind) => kind switch
    {
        NumberKinds.Order => "de orden",
        NumberKinds.Invoice => "de factura",
        NumberKinds.Package => "de paquete",
        NumberKinds.PackBatch => "de empaque",
        _ => throw UnknownKind(kind),
    };

    /// <summary>
    /// Ante una violación de índice único al insertar la orden: si chocó UX_Order_Number y el número de orden fue
    /// automático → NumberKinds.Order (se salta el valor chocado y se reintenta); si chocó UX_Order_PackBatch y el
    /// empaque fue automático → NumberKinds.PackBatch; en cualquier otro caso null (un número tecleado que choca es
    /// 409 directo).
    /// </summary>
    public static string? CollidedKind(string? sqlMessage, bool orderAuto, bool packBatchAuto)
    {
        if (string.IsNullOrEmpty(sqlMessage)) return null;
        if (orderAuto && sqlMessage.Contains(OrderNumberIndex, StringComparison.OrdinalIgnoreCase)) return NumberKinds.Order;
        if (packBatchAuto && sqlMessage.Contains(PackBatchIndex, StringComparison.OrdinalIgnoreCase)) return NumberKinds.PackBatch;
        return null;
    }

    public static bool IsKnownKind(string? kind) => kind is NumberKinds.Order or NumberKinds.Invoice or NumberKinds.Package or NumberKinds.PackBatch;

    private static string Coalesce(string? pattern, string fallback) => string.IsNullOrWhiteSpace(pattern) ? fallback : pattern.Trim();

    private static ArgumentOutOfRangeException UnknownKind(string kind)
        => new(nameof(kind), kind, "Tipo de número desconocido; use ORDER, INVOICE, PACKAGE o PACKBATCH.");
}
}
