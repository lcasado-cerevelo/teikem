namespace Teikem.Domain.Wms;

/// <summary>Lote 149 — valores de «el recibo exige documento» de la compañía (Tenant.ReceiptRequirement).</summary>
public static class ReceiptRequirements
{
    public const string Free = "FREE";
    public const string AnyDocument = "ANY_DOCUMENT";
    public const string Asn = "ASN";
    public const string PurchaseOrder = "PURCHASE_ORDER";
    public static readonly string[] All = { Free, AnyDocument, Asn, PurchaseOrder };
}

/// <summary>
/// Lote 149 — «el recibo exige documento»: cómo se aplican las ENTRADAS (la contraparte de la rotación en las salidas). FREE (por omisión) = se puede recibir a ciegas o por devolución manual como siempre;
/// ANY_DOCUMENT = hay que recibir contra un aviso de llegada o una orden de compra; ASN = solo contra un aviso de llegada de un cliente; PURCHASE_ORDER = solo contra una orden de compra (directa o por el
/// aviso nacido de ella). Los recibos de una devolución de venta (que nacen de la devolución) no se ven afectados.
/// </summary>
public static class ReceiptRequirementRules
{
    public const string Invalid = "El recibo exige documento debe ser FREE (libre), ANY_DOCUMENT (cualquier documento), ASN (aviso de llegada) o PURCHASE_ORDER (orden de compra).";

    public static string? Parse(string? value)
    {
        var v = value?.Trim().ToUpperInvariant();
        return v is not null && ReceiptRequirements.All.Contains(v) ? v : null;
    }

    public static bool Requires(string? value) => Parse(value) is string v && v != ReceiptRequirements.Free;

    /// <summary>409 de un recibo sin documento (a ciegas o devolución manual) cuando la compañía exige uno.</summary>
    public static string NoDocument(string? value) => Parse(value) switch
    {
        ReceiptRequirements.Asn => "La compañía exige recibir contra un aviso de llegada. Escoja el aviso; no se puede recibir sin documento.",
        ReceiptRequirements.PurchaseOrder => "La compañía exige recibir contra una orden de compra. Escoja la orden; no se puede recibir sin documento.",
        _ => "La compañía exige recibir contra un documento (aviso de llegada u orden de compra). Escójalo; no se puede recibir sin documento.",
    };

    public const string WrongKindPurchaseOrder = "La compañía exige recibir contra un aviso de llegada de un cliente, no contra una orden de compra.";
    public const string WrongKindAsn = "La compañía exige recibir contra una orden de compra, no contra el aviso de llegada de un cliente.";

    /// <summary>
    /// Mensaje 409 si el documento escogido no es del tipo que la compañía exige; null si sirve. <paramref name="fromPurchaseOrder"/> = el recibo va contra una orden de compra (directa o por el
    /// aviso nacido de ella); false = contra el aviso de llegada de un cliente. Sin documento (<paramref name="hasDocument"/> false) aplica <see cref="NoDocument"/>.
    /// </summary>
    public static string? Violation(string? requirement, bool hasDocument, bool fromPurchaseOrder)
    {
        var r = Parse(requirement);
        if (r is null or ReceiptRequirements.Free) return null;
        if (!hasDocument) return NoDocument(r);
        if (r == ReceiptRequirements.Asn && fromPurchaseOrder) return WrongKindPurchaseOrder;
        if (r == ReceiptRequirements.PurchaseOrder && !fromPurchaseOrder) return WrongKindAsn;
        return null;
    }
}
