namespace Teikem.Domain.Wms;

/// <summary>De dónde salió el costo congelado de una línea de recibo (capa de costo): la orden de compra, el precio del aviso de llegada o el costo del producto.</summary>
public static class CostSources
{
    public const string PurchaseOrder = "PO";
    public const string Asn = "ASN";
    public const string Product = "PRODUCT";
    public static readonly string[] All = { PurchaseOrder, Asn, Product };
}

/// <summary>
/// Lote 147 — capa de costo. Al confirmar un recibo cada línea congela su costo unitario (<c>ReceiptLine.UnitCost</c>) y su origen (<c>CostSource</c>); esa línea ES la capa. Prioridad:
/// costo de la línea de la orden de compra &gt; precio de la línea del aviso de llegada &gt; costo del producto (<c>Product.PurchaseCost</c>). Sin ninguno, la capa queda sin costo.
/// No hay sincronización en ningún sentido: cambiar después el costo del producto, el precio del aviso o el costo de la orden no toca una capa ya congelada.
/// </summary>
public static class CostLayerRules
{
    public const decimal MaxUnitPrice = 99_999_999m;
    public const string AsnPriceOnlyForClients = "El precio solo se captura en avisos de llegada de un cliente; en uno nacido de una orden de compra el costo es el de la orden.";
    public const string UnitPriceInvalid = "El precio unitario debe ser cero o mayor y de 4 decimales como máximo.";
    public const string UnitPriceTooLarge = "El precio unitario excede el máximo permitido.";

    public static string? ValidateUnitPrice(decimal? price)
    {
        if (price is null) return null;
        if (price.Value < 0m || decimal.Round(price.Value, 4) != price.Value) return UnitPriceInvalid;
        return price.Value > MaxUnitPrice ? UnitPriceTooLarge : null;
    }

    /// <summary>Costo y origen de la capa según la prioridad; (null, null) si no hay costo en ningún lado.</summary>
    public static (decimal? Cost, string? Source) Resolve(decimal? purchaseOrderCost, decimal? asnPrice, decimal? productCost)
    {
        if (purchaseOrderCost is not null) return (purchaseOrderCost, CostSources.PurchaseOrder);
        if (asnPrice is not null) return (asnPrice, CostSources.Asn);
        if (productCost is not null) return (productCost, CostSources.Product);
        return (null, null);
    }
}
