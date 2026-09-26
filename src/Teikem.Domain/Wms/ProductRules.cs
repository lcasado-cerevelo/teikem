using System.Globalization;

namespace Teikem.Domain.Wms;

/// <summary>
/// Lote 6 (P2) — reglas puras del maestro de productos y de sus categorías (R23, R24, R26, R31, R32). No conocen EF: los
/// servicios les pasan valores ya leídos. Los mensajes son constantes o métodos estáticos porque el manual (06) y la FAQ los
/// citan tal cual (cultura invariante en los números).
/// - 'Dueño del producto' = Product.ClientId: un cliente 3PL o NULL = propio del tenant (nunca el TenantId).
/// - Costos y precios: DECIMAL(18,4), no negativos. Cantidades (mínimos): DECIMAL(16,3), no negativas.
/// - Seguimiento, unidad base y dueño son inmutables desde el primer movimiento del ledger (D25).
/// - Categorías jerárquicas sin ciclos y con a lo sumo MaxCategoryDepth niveles.
/// </summary>
public static class ProductRules
{
    public const int SkuMaxLength = 60;
    public const int NameMaxLength = 200;
    public const int BarcodeMaxLength = 60;
    public const int CategoryNameMaxLength = 150;
    /// <summary>Profundidad máxima del árbol de categorías (una categoría raíz es el nivel 1).</summary>
    public const int MaxCategoryDepth = 5;
    /// <summary>Tope de filas por página (D38).</summary>
    public const int MaxPageSize = 200;
    /// <summary>Etiqueta del dueño cuando el producto es propio del tenant (R31).</summary>
    public const string OwnLabel = "Propio";

    // ---------------------------------------------------------------- mensajes exactos

    public const string SkuRequired = "El SKU es obligatorio.";
    public const string SkuInvalid = "El SKU no admite espacios ni caracteres de control.";
    public static string SkuTooLong => $"El SKU no puede exceder {SkuMaxLength} caracteres.";
    public const string SkuImmutable = "El SKU del producto no se puede cambiar.";
    public const string NameRequired = "El nombre del producto es obligatorio.";
    public static string NameTooLong => $"El nombre no puede exceder {NameMaxLength} caracteres.";
    public static string BarcodeTooLong => $"El código de barras no puede exceder {BarcodeMaxLength} caracteres.";

    public const string SkuTaken = "Ya existe un producto con ese SKU para ese dueño.";
    public const string BarcodeTaken = "Ya existe un producto activo con ese código de barras.";
    public const string PreferredBinMismatch = "La posición preferida debe pertenecer al almacén preferido.";
    public const string PreferredWarehouseInactive = "El almacén preferido está dado de baja.";
    public const string PreferredBinInactive = "La posición preferida está inactiva.";

    public const string MoneyNegative = "El costo y el precio no pueden ser negativos.";
    public static string MoneyDecimals(string field) => $"El {field} admite como máximo 4 decimales.";
    public const string MoneyTooLarge = "El costo o el precio excede el máximo permitido.";
    public const string MeasuresNegative = "El peso y el volumen no pueden ser negativos.";
    public const string WeightInvalid = "El peso admite como máximo 3 decimales y debe ser menor que 1,000,000,000.";
    public const string VolumeInvalid = "El volumen admite como máximo 4 decimales y debe ser menor que 100,000,000.";

    public const string MinNegative = "Los mínimos no pueden ser negativos.";
    public const string MinDecimals = "Los mínimos admiten como máximo 3 decimales y deben ser menores que 10,000,000,000,000.";
    public const string PickMaxBelowMin = "El máximo de la posición de picking debe ser mayor o igual al mínimo.";
    public const string PickMinRequiresPickingBin = "El mínimo de picking requiere una posición preferida en una zona PICKING.";

    public static string Immutable(string field) => $"No se puede cambiar {field} de un producto que ya tiene movimientos.";
    /// <summary>Nombres de campo con artículo para Immutable (el manual los cita así).</summary>
    public const string FieldTracking = "el tipo de seguimiento";
    public const string FieldBaseUom = "la unidad de medida base";
    public const string FieldOwner = "el dueño";

    public static string DeactivateWithStock(string sku, decimal qty)
        => $"El producto {sku} tiene inventario en mano ({FormatQty(qty)}); no se puede desactivar.";
    public static string DeactivateOpenDocs(string sku)
        => $"El producto {sku} está en recibos abiertos o tareas pendientes; ciérrelos antes de desactivarlo.";

    public const string CategoryNameRequired = "El nombre de la categoría es obligatorio.";
    public static string CategoryNameTooLong => $"El nombre de la categoría no puede exceder {CategoryNameMaxLength} caracteres.";
    public const string CategoryCycle = "Una categoría no puede ser su propia ascendente.";
    public static string CategoryDepth => $"Las categorías admiten como máximo {MaxCategoryDepth} niveles.";
    public const string CategoryNameTaken = "Ya existe una categoría con ese nombre en ese nivel.";
    public const string CategoryHasProducts = "La categoría tiene productos activos.";
    public const string CategoryHasChildren = "La categoría tiene subcategorías activas; desactívelas primero.";
    public const string CategoryParentInactive = "La categoría ascendente está inactiva.";
    public const string CategoryInactive = "La categoría está inactiva.";

    // ---------------------------------------------------------------- normalización

    /// <summary>
    /// SKU: recortado y en mayúsculas invariantes (el índice de SQL no distingue mayúsculas). Vacío → SkuRequired; más largo
    /// que SkuMaxLength → SkuTooLong; con espacios internos o caracteres de control → SkuInvalid.
    /// </summary>
    public static (string? Sku, string? Error) NormalizeSku(string? raw)
    {
        var sku = raw?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(sku)) return (null, SkuRequired);
        if (sku.Length > SkuMaxLength) return (null, SkuTooLong);
        if (sku.Any(c => char.IsWhiteSpace(c) || char.IsControl(c))) return (null, SkuInvalid);
        return (sku, null);
    }

    /// <summary>Nombre obligatorio, recortado, hasta NameMaxLength.</summary>
    public static (string? Name, string? Error) NormalizeName(string? raw)
    {
        var name = raw?.Trim();
        if (string.IsNullOrEmpty(name)) return (null, NameRequired);
        if (name.Length > NameMaxLength) return (null, NameTooLong);
        return (name, null);
    }

    /// <summary>Código de barras opcional: recortado; vacío → null (sin código); más largo que BarcodeMaxLength → error.</summary>
    public static (string? Barcode, string? Error) NormalizeBarcode(string? raw)
    {
        var code = raw?.Trim();
        if (string.IsNullOrEmpty(code)) return (null, null);
        if (code.Length > BarcodeMaxLength) return (null, BarcodeTooLong);
        return (code, null);
    }

    // ---------------------------------------------------------------- números

    /// <summary>
    /// Costo o precio (DECIMAL(18,4)): null es válido; negativo → MoneyNegative; más de 4 decimales → MoneyDecimals(campo)
    /// ('El costo admite como máximo 4 decimales.'); fuera de rango → MoneyTooLarge.
    /// </summary>
    public static string? Money(decimal? value, string field)
    {
        if (value is not decimal v) return null;
        if (v < 0) return MoneyNegative;
        if (decimal.Round(v, 4, MidpointRounding.ToZero) != v) return MoneyDecimals(field);
        if (v >= 100_000_000_000_000m) return MoneyTooLarge;
        return null;
    }

    /// <summary>Peso (DECIMAL(12,3)) y volumen (DECIMAL(12,4)): no negativos y dentro de la precisión. Devuelve (campo, mensaje).</summary>
    public static IReadOnlyList<(string Field, string Message)> ValidateMeasures(decimal? weightKg, decimal? volumeM3)
    {
        var errors = new List<(string, string)>();
        if (weightKg is decimal w)
        {
            if (w < 0) errors.Add(("weightKg", MeasuresNegative));
            else if (decimal.Round(w, 3, MidpointRounding.ToZero) != w || w >= 1_000_000_000m) errors.Add(("weightKg", WeightInvalid));
        }
        if (volumeM3 is decimal v)
        {
            if (v < 0) errors.Add(("volumeM3", MeasuresNegative));
            else if (decimal.Round(v, 4, MidpointRounding.ToZero) != v || v >= 100_000_000m) errors.Add(("volumeM3", VolumeInvalid));
        }
        return errors;
    }

    /// <summary>
    /// Mínimos (DECIMAL(16,3), CK_Product_Numbers): no negativos, a lo sumo 3 decimales; MaxPickQty ≥ MinPickQty cuando ambos
    /// existen; MinPickQty exige una posición preferida en una zona PICKING (el reabasto la usa como destino).
    /// Recibe el estado FINAL del producto (en un PATCH, lo actual combinado con lo pedido). Devuelve (campo, mensaje).
    /// </summary>
    public static IReadOnlyList<(string Field, string Message)> ValidateMinimums(decimal? minQty, decimal? minPickQty, decimal? maxPickQty,
        bool preferredBinIsPicking)
    {
        var errors = new List<(string, string)>();
        void Check(decimal? value, string field)
        {
            if (value is not decimal v) return;
            if (v < 0) errors.Add((field, MinNegative));
            else if (decimal.Round(v, 3, MidpointRounding.ToZero) != v || v >= 10_000_000_000_000m) errors.Add((field, MinDecimals));
        }
        Check(minQty, "minQty");
        Check(minPickQty, "minPickQty");
        Check(maxPickQty, "maxPickQty");
        if (errors.Count > 0) return errors;

        if (minPickQty is decimal min && maxPickQty is decimal max && max < min) errors.Add(("maxPickQty", PickMaxBelowMin));
        if (minPickQty.HasValue && !preferredBinIsPicking) errors.Add(("minPickQty", PickMinRequiresPickingBin));
        return errors;
    }

    /// <summary>Disponible = en mano − reservado (se calcula en código: QtyAvailable es computada en SQL y InMemory no la calcula).</summary>
    public static decimal Available(decimal onHand, decimal reserved) => onHand - reserved;

    /// <summary>Bajo mínimo (R32): hay MinQty, el producto está activo y su disponible total es menor que el mínimo.</summary>
    public static bool IsBelowMin(decimal? minQty, decimal available, bool isActive)
        => isActive && minQty is decimal min && available < min;

    /// <summary>Valor de una cantidad a un costo/precio: Round4 AwayFromZero; null si no hay costo/precio.</summary>
    public static decimal? Value(decimal qty, decimal? unitAmount)
        => unitAmount is decimal u ? Math.Round(qty * u, 4, MidpointRounding.AwayFromZero) : null;

    /// <summary>Días al vencimiento del lote (negativo si ya venció; null sin fecha).</summary>
    public static int? DaysToExpiry(DateOnly? expiry, DateOnly today) => expiry is DateOnly e ? e.DayNumber - today.DayNumber : null;

    /// <summary>Etiqueta del dueño (R31): nombre del cliente o 'Propio'.</summary>
    public static string OwnerLabel(string? clientName) => string.IsNullOrWhiteSpace(clientName) ? OwnLabel : clientName;

    /// <summary>Cantidad en cultura invariante, sin ceros sobrantes ('12.5', '3', '-0.125').</summary>
    public static string FormatQty(decimal qty) => qty.ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>
    /// ¿Qué campos inmutables cambia la edición? Solo importa si el producto ya tiene movimientos (D25). Devuelve el nombre del
    /// primero que cambia (para Immutable) o null.
    /// </summary>
    public static string? ImmutableChange(bool hasMovements, bool trackingChanges, bool uomChanges, bool ownerChanges)
    {
        if (!hasMovements) return null;
        if (trackingChanges) return FieldTracking;
        if (uomChanges) return FieldBaseUom;
        if (ownerChanges) return FieldOwner;
        return null;
    }

    // ---------------------------------------------------------------- categorías

    /// <summary>Nombre de categoría obligatorio, recortado, hasta CategoryNameMaxLength.</summary>
    public static (string? Name, string? Error) NormalizeCategoryName(string? raw)
    {
        var name = raw?.Trim();
        if (string.IsNullOrEmpty(name)) return (null, CategoryNameRequired);
        if (name.Length > CategoryNameMaxLength) return (null, CategoryNameTooLong);
        return (name, null);
    }

    /// <summary>
    /// ¿Asignar <paramref name="newParentId"/> como ascendente de <paramref name="categoryId"/> crea un ciclo? Sí si el padre es
    /// la propia categoría o si, subiendo desde el padre, se llega a ella. Un mapa corrupto (ciclo previo) también cuenta.
    /// </summary>
    public static bool CreatesCycle(int categoryId, int? newParentId, IReadOnlyDictionary<int, int?> parents)
    {
        var seen = new HashSet<int>();
        for (var current = newParentId; current is int c; current = parents.TryGetValue(c, out var p) ? p : null)
        {
            if (c == categoryId) return true;
            if (!seen.Add(c)) return true;
        }
        return false;
    }

    /// <summary>Nivel de una categoría (raíz = 1) según la cadena de ascendentes del mapa.</summary>
    public static int Level(int? parentId, IReadOnlyDictionary<int, int?> parents)
    {
        var level = 1;
        var seen = new HashSet<int>();
        for (var current = parentId; current is int c && seen.Add(c); current = parents.TryGetValue(c, out var p) ? p : null)
            level++;
        return level;
    }

    /// <summary>Altura del subárbol de una categoría (sin hijas = 1).</summary>
    public static int SubtreeHeight(int categoryId, IReadOnlyDictionary<int, int?> parents)
    {
        var children = parents.Where(kv => kv.Value is not null).GroupBy(kv => kv.Value!.Value)
            .ToDictionary(g => g.Key, g => g.Select(kv => kv.Key).ToList());
        var seen = new HashSet<int>();
        int Height(int id)
        {
            if (!seen.Add(id) || !children.TryGetValue(id, out var kids) || kids.Count == 0) return 1;
            return 1 + kids.Max(Height);
        }
        return Height(categoryId);
    }

    /// <summary>
    /// ¿Excede la profundidad máxima colgar <paramref name="categoryId"/> (con su subárbol) de <paramref name="newParentId"/>?
    /// Para una categoría nueva, categoryId = null (subárbol de altura 1).
    /// </summary>
    public static bool ExceedsDepth(int? categoryId, int? newParentId, IReadOnlyDictionary<int, int?> parents)
    {
        var height = categoryId is int id ? SubtreeHeight(id, parents) : 1;
        return Level(newParentId, parents) + height - 1 > MaxCategoryDepth;
    }

    /// <summary>Ruta legible 'Raíz / Hija / Nieta' de una categoría (nombres por id; un id sin nombre se omite).</summary>
    public static string CategoryPath(int categoryId, IReadOnlyDictionary<int, int?> parents, IReadOnlyDictionary<int, string> names)
    {
        var chain = new List<string>();
        var seen = new HashSet<int>();
        for (int? current = categoryId; current is int c && seen.Add(c); current = parents.TryGetValue(c, out var p) ? p : null)
            if (names.TryGetValue(c, out var n)) chain.Add(n);
        chain.Reverse();
        return string.Join(" / ", chain);
    }

    /// <summary>La categoría y todas sus descendientes (para filtrar 'categoría con subcategorías').</summary>
    public static IReadOnlySet<int> WithDescendants(IEnumerable<int> categoryIds, IReadOnlyDictionary<int, int?> parents)
    {
        var children = parents.Where(kv => kv.Value is not null).GroupBy(kv => kv.Value!.Value)
            .ToDictionary(g => g.Key, g => g.Select(kv => kv.Key).ToList());
        var result = new HashSet<int>();
        var stack = new Stack<int>(categoryIds ?? Array.Empty<int>());
        while (stack.Count > 0)
        {
            var id = stack.Pop();
            if (!result.Add(id)) continue;
            if (children.TryGetValue(id, out var kids)) foreach (var k in kids) stack.Push(k);
        }
        return result;
    }
}
