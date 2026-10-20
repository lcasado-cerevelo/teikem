using System.Globalization;

namespace Teikem.Domain.ExitNotices;

/// <summary>Reglas PURAS del aviso de salida (Lote 143): mensajes exactos, validaciones de captura y el estatus que corresponde según las líneas. Sin base de datos.</summary>
public static class ExitNoticeRules
{
    public const int MaxLines = 200;
    public const int MaxReferenceLength = 80;
    public const int MaxDestinationLength = 200;
    public const int MaxNotesLength = 500;
    public const decimal MaxQuantity = 99_999_999m;

    public const string NotFound = "Aviso de salida no encontrado.";
    public const string ClientRequired = "Escoja el cliente dueño de la mercancía.";
    public const string ClientNotFound = "Cliente no encontrado.";
    public const string ClientInactive = "El cliente está inactivo; no se le puede hacer un aviso de salida.";
    public const string WarehouseRequired = "Indique el almacén: la compañía tiene más de uno.";
    public const string NoActiveWarehouse = "La compañía no tiene almacenes activos.";
    public const string WarehouseNotFound = "Almacén no encontrado.";
    public const string WarehouseInactive = "El almacén está inactivo.";
    public const string LinesRequired = "Agregue al menos un producto al aviso de salida.";
    public const string TooManyLines = "El aviso de salida admite como máximo 200 líneas.";
    public const string ProductRequired = "Escoja el producto de cada línea.";
    public const string ProductNotFound = "Producto no encontrado.";
    public const string ProductInactive = "Hay un producto inactivo en el aviso de salida.";
    public const string ProductRepeated = "Un producto no puede repetirse en el aviso de salida.";
    public const string SerialNotSupported = "Los productos con número de serie todavía no se pueden pedir con un aviso de salida.";
    public const string KitNotSupported = "Los kits todavía no se pueden pedir con un aviso de salida.";
    public const string LotNotFound = "Lote no encontrado.";
    public const string QuantityRequired = "La cantidad debe ser mayor que cero.";
    public const string QuantityDecimals = "La cantidad admite como máximo 3 decimales.";
    public const string QuantityTooLarge = "La cantidad excede el máximo permitido.";
    public const string ReferenceTooLong = "La referencia no puede pasar de 80 caracteres.";
    public const string DestinationTooLong = "El destino no puede pasar de 200 caracteres.";
    public const string NotesTooLong = "Las notas no pueden pasar de 500 caracteres.";
    public const string NumberTaken = "Ya existe un aviso de salida con ese número; intente de nuevo.";
    public const string Concurrency = "El aviso de salida cambió al mismo tiempo. Recárguelo e intente de nuevo.";

    public const string OnlyRequestedModifiable = "Solo se pueden modificar las líneas de un aviso de salida solicitado.";
    public const string LineClosed = "La línea está cerrada.";
    public const string LineAlreadyShipped = "A esa línea ya se le despachó producto: no se puede modificar.";
    public const string OnlyRequestedCancellable = "Solo se puede cancelar un aviso de salida solicitado que no se haya despachado.";
    public const string AlreadyCancelled = "El aviso de salida ya está cancelado.";
    public const string NothingToFulfill = "No hay inventario disponible para reservar el pendiente de esa línea.";
    public const string NoBackorder = "Esa línea no tiene pendiente.";
    public const string NotOpenForBackorder = "Solo se atiende el pendiente de un aviso de salida solicitado o despachado parcial.";

    public const string DispatchLinesRequired = "Indique qué líneas se despachan.";
    public const string DispatchLineRequired = "Indique la línea que se despacha.";
    public const string DispatchLineRepeated = "Una línea no puede repetirse en el despacho.";
    public const string NotDispatchable = "Solo se puede despachar un aviso de salida solicitado o despachado parcial.";
    public const string LotWithoutBin = "Si indica el lote de donde sale, indique también la posición.";

    public static string OwnerMismatch(string sku) => $"El producto {sku} no pertenece al cliente del aviso de salida.";
    public static string LotNotTracked(string sku) => $"El producto {sku} no se controla por lote: no indique lote.";
    public static string DispatchMoreThanReserved(string sku, decimal reserved) => $"No se puede despachar más de lo reservado de {sku} ({Num(reserved)}). Reserve primero el pendiente.";
    public static string BinNotPickable(string code) => $"La posición {code} no se puede usar: está inactiva o es de cuarentena, cruce de muelle o renta.";
    public static string IncreaseShortage(string sku, decimal extra, decimal available) => $"No hay inventario suficiente de {sku} para subir {Num(extra)}: disponible {Num(available)}.";

    public static string Num(decimal v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>Cantidad válida: mayor que cero, hasta 3 decimales y bajo el máximo. null = el mensaje de «requerida».</summary>
    public static string? QuantityError(decimal? qty)
    {
        if (qty is null || qty <= 0m) return QuantityRequired;
        if (qty >= MaxQuantity) return QuantityTooLarge;
        if (decimal.Round(qty.Value, 3) != qty.Value) return QuantityDecimals;
        return null;
    }

    /// <summary>Texto opcional recortado; vacío = null; pasado del máximo = el mensaje de error.</summary>
    public static (string? Value, string? Error) NormalizeText(string? value, int max, string tooLong)
    {
        var v = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        return v is not null && v.Length > max ? (null, tooLong) : (v, null);
    }

    /// <summary>Una línea con pendiente que todavía espera inventario (no cerrada con faltante).</summary>
    public static bool IsBackordered(decimal qtyBackordered, bool isClosed) => qtyBackordered > 0m && !isClosed;

    /// <summary>
    /// Estatus que corresponde según las líneas (solo desde REQUESTED o PARTIAL): terminada = sin reserva ni pendiente. Todas terminadas: SHIPPED si se despachó algo y
    /// CANCELLED si nada (se cerraron todas con faltante). Con algo despachado y líneas pendientes desde REQUESTED: PARTIAL. null = no cambia.
    /// </summary>
    public static string? NextStatus(string current, bool anyShipped, bool allDone)
    {
        if (current is not (ExitNoticeStatuses.Requested or ExitNoticeStatuses.Partial)) return null;
        if (allDone) return anyShipped ? ExitNoticeStatuses.Shipped : ExitNoticeStatuses.Cancelled;
        if (anyShipped && current == ExitNoticeStatuses.Requested) return ExitNoticeStatuses.Partial;
        return null;
    }
}
