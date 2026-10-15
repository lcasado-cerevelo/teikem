namespace Teikem.Domain.Sales;

/// <summary>
/// Lote 57 — reglas puras de las órdenes de venta (sin EF ni servicios; SalesOrderService las traduce a 400/404/409/422). Los mensajes son públicos
/// porque el manual (capítulo 18) y la FAQ los citan tal cual.
/// </summary>
public static class SalesRules
{
    public const int MaxLines = 200;
    public const int MaxNotesLength = 500;
    public const decimal MaxQuantity = 9_999_999_999.999m;

    public const string ClientRequired = "Escoja el cliente que compra.";
    public const string ClientNotFound = "Cliente no encontrado.";
    public const string ClientInactive = "El cliente está inactivo; no se le puede vender.";
    public const string ConsigneeNotFound = "Consignatario no encontrado.";
    public const string WarehouseRequired = "Indique el almacén: la compañía tiene más de uno.";
    public const string NoActiveWarehouse = "La compañía no tiene almacenes activos.";
    public const string WarehouseNotFound = "Almacén no encontrado.";
    public const string WarehouseInactive = "El almacén está inactivo.";
    public const string LinesRequired = "Agregue al menos un producto a la orden.";
    public const string TooManyLines = "La orden admite como máximo 200 líneas.";
    public const string ProductRequired = "Escoja el producto de cada línea.";
    public const string ProductNotFound = "Producto no encontrado.";
    public const string ProductInactive = "Hay un producto inactivo en la orden.";
    public const string ProductNotOwn = "Solo se pueden vender productos propios de la compañía.";
    public const string ProductRepeated = "Un producto no puede repetirse en la orden.";
    public const string QuantityRequired = "La cantidad debe ser mayor que cero.";
    public const string QuantityDecimals = "La cantidad admite como máximo 3 decimales.";
    public const string QuantityTooLarge = "La cantidad excede el máximo permitido.";
    public const string PriceInvalid = "El precio no es válido.";
    public const string PriceDecimals = "El precio admite como máximo 4 decimales.";
    public const string LotNotFound = "Lote no encontrado.";
    public const string NotesTooLong = "Las notas no pueden pasar de 500 caracteres.";
    public const string NotFound = "Orden de venta no encontrada.";
    public const string OnlyDraftEditable = "Solo se puede editar una orden en borrador.";
    public const string OnlyDraftOrRequestedCancellable = "Solo se puede cancelar una orden en borrador o solicitada que no se haya despachado.";
    public const string AlreadyCancelled = "La orden ya está cancelada.";
    public const string PriceNeedsPermission = "Cambiar el precio exige el permiso de precios (sales.price).";
    public const string NumberTaken = "Ya existe una orden de venta con ese número; intente de nuevo.";
    public const string Concurrency = "La orden cambió al mismo tiempo. Recárguela e intente de nuevo.";

    public static string NoPrice(string sku) => $"El producto {sku} no tiene precio de venta: escriba el precio.";

    /// <summary>Cantidad válida: mayor que cero, hasta 3 decimales y dentro del máximo. Devuelve el mensaje o null.</summary>
    public static string? QuantityError(decimal? qty)
    {
        if (qty is null || qty <= 0m) return QuantityRequired;
        if (qty > MaxQuantity) return QuantityTooLarge;
        if (decimal.Round(qty.Value, 3) != qty.Value) return QuantityDecimals;
        return null;
    }

    /// <summary>Precio válido: no negativo y hasta 4 decimales. Devuelve el mensaje o null.</summary>
    public static string? PriceError(decimal? price)
    {
        if (price is null) return null;
        if (price < 0m || price > 9_999_999_999m) return PriceInvalid;
        if (decimal.Round(price.Value, 4) != price.Value) return PriceDecimals;
        return null;
    }

    /// <summary>Monto de una línea: cantidad × precio, a 4 decimales.</summary>
    public static decimal LineAmount(decimal qty, decimal unitPrice) => decimal.Round(qty * unitPrice, 4, MidpointRounding.AwayFromZero);

    /// <summary>Notas: recortadas, vacías = null, máximo 500. Devuelve (notas, error).</summary>
    public static (string? Notes, string? Error) NormalizeNotes(string? notes)
    {
        if (string.IsNullOrWhiteSpace(notes)) return (null, null);
        var n = notes.Trim();
        return n.Length > MaxNotesLength ? (null, NotesTooLong) : (n, null);
    }

    /// <summary>Una línea con faltante pendiente (marca «con backorder»): lo pendiente y la línea sin cerrar.</summary>
    public static bool IsBackordered(decimal qtyBackordered, bool isClosed) => qtyBackordered > 0m && !isClosed;
}
