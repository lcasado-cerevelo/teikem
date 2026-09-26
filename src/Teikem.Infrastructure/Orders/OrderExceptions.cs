using Teikem.Infrastructure.Exceptions;

namespace Teikem.Infrastructure.Orders;

/// <summary>
/// Factura repetida por consignatario (R36, DECISIÓN 5). 409 con code 'duplicate_invoice' (el consignatario no admite repetidas)
/// o 'duplicate_invoice_confirmable' (admite repetidas: el front repite el POST con confirmDuplicateInvoice=true). Los datos de la
/// orden existente viajan en Errors (clientInvoiceNumber, existingOrderNumber, existingOrderPublicId), que el middleware ya
/// serializa: el front arma el modal 'Crear de todos modos' sin parsear el título. Cuando el scope fija un cliente (portal) y la
/// orden existente es de OTRO cliente, existingOrderNumber/existingOrderPublicId van en null y no se incluyen (sin oráculo).
/// </summary>
public sealed class DuplicateInvoiceException : TeikemException
{
    public const string BlockedCode = "duplicate_invoice";
    public const string ConfirmableCode = "duplicate_invoice_confirmable";

    public DuplicateInvoiceException(string message, bool confirmable, string? existingOrderNumber, Guid? existingOrderPublicId)
        : base(message, 409, confirmable ? ConfirmableCode : BlockedCode)
    {
        Confirmable = confirmable;
        ExistingOrderNumber = existingOrderNumber;
        ExistingOrderPublicId = existingOrderPublicId;
        var errors = new Dictionary<string, string[]> { ["clientInvoiceNumber"] = new[] { message } };
        if (existingOrderNumber is not null) errors["existingOrderNumber"] = new[] { existingOrderNumber };
        if (existingOrderPublicId is Guid pid) errors["existingOrderPublicId"] = new[] { pid.ToString() };
        Errors = errors;
    }

    public bool Confirmable { get; }
    public string? ExistingOrderNumber { get; }
    public Guid? ExistingOrderPublicId { get; }
}

/// <summary>
/// Excepción INTERNA (no HTTP): un número automático (ORDER o PACKBATCH) chocó con uno tecleado por otro usuario al insertar la orden.
/// OrderService la captura fuera de la transacción, consume el valor chocado con NextAsync en autocommit y reintenta (máximo
/// NumberingRules.MaxAutoRetries). Nunca llega al middleware.
/// </summary>
public sealed class OrderNumberCollision(string kind) : Exception($"El número automático '{kind}' chocó con uno ya usado; se reintenta.")
{
    /// <summary>NumberKinds.Order o NumberKinds.PackBatch.</summary>
    public string Kind { get; } = kind;
}
