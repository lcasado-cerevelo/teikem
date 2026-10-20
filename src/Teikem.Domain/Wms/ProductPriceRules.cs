namespace Teikem.Domain.Wms;

/// <summary>Una línea de documento (aviso de llegada u orden de compra) con el precio escrito y el costo que hoy tiene el producto.</summary>
public sealed record ProductPriceLine(int ProductId, decimal? Entered, bool Overwrite, decimal? Stored);

/// <summary>
/// Precio de compra del producto al guardar un documento. El costo del producto (<c>Product.PurchaseCost</c>) se precarga en la línea; al guardar:
/// - si el producto NO tiene costo (vacío o cero) y la línea trae uno mayor que cero, el producto lo toma (la primera línea del documento gana);
/// - si ya tiene uno distinto, solo cambia cuando la línea lo pide con «actualizar el precio del producto» (quien puede administrar inventario);
/// - si es igual, no se toca. Nunca toca capas ya congeladas.
/// </summary>
public static class ProductPriceRules
{
    public const string UpdateNeedsPermission = "Actualizar el precio del producto requiere el permiso de administrar inventario.";

    public static bool IsEmpty(decimal? cost) => cost is null || cost.Value <= 0m;

    /// <summary>¿El precio escrito difiere del guardado (ambos con valor)? Sirve para el aviso informativo.</summary>
    public static bool Differs(decimal? entered, decimal? stored) => entered is > 0m && !IsEmpty(stored) && entered.Value != stored!.Value;

    /// <summary>Nuevo costo por producto (ProductId → costo) según las líneas; vacío si nada cambia.</summary>
    public static Dictionary<int, decimal> Plan(IEnumerable<ProductPriceLine> lines)
    {
        var result = new Dictionary<int, decimal>();
        foreach (var l in lines)
        {
            if (l.Entered is not > 0m || result.ContainsKey(l.ProductId)) continue;
            if (IsEmpty(l.Stored) || (l.Overwrite && l.Entered.Value != l.Stored!.Value)) result[l.ProductId] = l.Entered.Value;
        }
        return result;
    }
}
