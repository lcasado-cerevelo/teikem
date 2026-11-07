namespace Teikem.Domain.Sales;

/// <summary>Lote 222 — cómo se reautenticó el vendedor al enviar el pedido (la «firma» del vendedor).</summary>
public static class SalesSubmitAuth
{
    public const string Biometric = "BIOMETRIC";
    public const string Password = "PASSWORD";
    public static readonly string[] All = { Biometric, Password };
}

/// <summary>Lote 222 — el cargo de envío en la factura: una línea de servicio (no de inventario) con el tipo de cargo SHIPPING.</summary>
public static class SalesShippingCodes
{
    public const string ChargeType = "SHIPPING";

    public static string InvoiceDescription(string orderNumber) => $"{orderNumber} — Cargo de envío";
}

/// <summary>Lote 222 — reglas puras y mensajes de los datos nuevos de la orden (urgente, autorización, prospecto). El manual y la FAQ los citan tal cual.</summary>
public static class SalesOrderExtraRules
{
    public const int MaxAuthorizedByLength = 150;

    public const string AuthorizedByTooLong = "El nombre de quien autoriza no puede pasar de 150 caracteres.";
    public const string SignatureRequired = "Este cliente exige el nombre y la firma de quien autoriza el pedido.";
    public const string SignatureNotFound = "Firma";
    public const string SubmitAuthInvalid = "La reautenticación debe ser BIOMETRIC o PASSWORD.";
    public const string AwaitingClientApproval = "La orden espera la aprobación del cliente nuevo: no se puede despachar hasta que lo aprueben.";
    public const string ClientSuspended = "El cliente está suspendido: no se le puede solicitar una orden.";

    /// <summary>Nombre de quien autoriza: recortado, vacío = null, hasta 150. Devuelve (nombre, error).</summary>
    public static (string? Name, string? Error) NormalizeAuthorizedBy(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return (null, null);
        var n = name.Trim();
        return n.Length > MaxAuthorizedByLength ? (null, AuthorizedByTooLong) : (n, null);
    }

    /// <summary>¿Falta la autorización que exige el cliente? (nombre y firma dibujada, solo si el cliente tiene «Exige la firma de quien autoriza el pedido»).</summary>
    public static bool MissingRequiredSignature(bool clientRequires, string? authorizedByName, int? signatureFileId)
        => clientRequires && (string.IsNullOrWhiteSpace(authorizedByName) || signatureFileId is null);

    /// <summary>BIOMETRIC / PASSWORD; vacío = null. Devuelve (valor, error).</summary>
    public static (string? Value, string? Error) ParseSubmitAuth(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return (null, null);
        var v = value.Trim().ToUpperInvariant();
        return SalesSubmitAuth.All.Contains(v) ? (v, null) : (null, SubmitAuthInvalid);
    }

    /// <summary>Total de la orden: productos + su IVU + el envío + el IVU del envío.</summary>
    public static decimal Total(decimal subtotal, decimal taxAmount, decimal shippingCharge, decimal shippingTax) => subtotal + taxAmount + shippingCharge + shippingTax;
}
