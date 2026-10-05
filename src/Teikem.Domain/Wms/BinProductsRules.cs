namespace Teikem.Domain.Wms;

/// <summary>
/// Informe "Productos por posición" (2026-10-05): una hoja por posición con los productos que tiene (SKU, nombre y código de barras),
/// para llevarla al rack y escanear desde el papel. Reemplaza a las "hojas de posición" del Lote 23 (sin control de impresión ni de
/// cambios: es solo un listado de lo que hay en cada posición en el momento de generarlo).
/// </summary>
public static class BinProductsRules
{
    /// <summary>Tope de posiciones por página (GET .../bin-products).</summary>
    public const int MaxPerPage = 200;
    /// <summary>Página por defecto.</summary>
    public const int DefaultPerPage = 50;

    /// <summary>400 en 'take' de GET .../bin-products.</summary>
    public static readonly string TakeTooLarge = $"Se pueden pedir como máximo {MaxPerPage} posiciones por consulta; use skip para pedir las siguientes.";
}
