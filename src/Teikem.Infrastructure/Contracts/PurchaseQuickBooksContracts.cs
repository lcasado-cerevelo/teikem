namespace Teikem.Infrastructure.Contracts;

// Lote 246 (Lote F del plan de Solutions) — QuickBooks Desktop de compras: órdenes de compra por pasar, archivo IIF (TRNS PURCHORD), «Sí, ya están», «No pasó»,
// marca a mano y quitar la marca (permiso purchasing.quickbooks, módulo PURCHASING). Exportar NO marca.

/// <summary>
/// Una orden de compra por pasar (o que salió en el archivo). QuickBooksVendor = el nombre del proveedor como sale en el archivo (limpio: sin comillas dobles, tabuladores ni
/// dos puntos). Total = el de Teikem (Σ cantidad × costo); FileTotal = el del archivo (cada línea redondeada a centavos, lo que asienta QuickBooks). Lines = cuántas líneas
/// lleva. ExportedAtUtc / ExportedByName = la última vez que salió en un archivo y quién lo sacó (NULL = nunca). NewSupplier / NewProductSkus = lo que QuickBooks todavía
/// no tiene.
/// </summary>
public sealed record PurchaseQuickBooksOrderDto(Guid PublicId, string Number, DateOnly OrderDate, DateOnly? ExpectedDate, string StatusCode, string Status, int SupplierId,
    string SupplierName, string QuickBooksVendor, string OriginCode, decimal Total, decimal FileTotal, int Lines, DateTime? ExportedAtUtc, string? ExportedByName,
    bool NewSupplier, IReadOnlyList<string> NewProductSkus);

/// <summary>
/// Los datos de un proveedor nuevo para QuickBooks (lo que lleva el bloque !VEND, por si la oficina prefiere teclearlo). QuickBooksName = como sale en el archivo;
/// Phone = con prefijo internacional; PaymentTerm = código del término en Teikem y QuickBooksTerms = su nombre en QuickBooks (NULL = sin nombre: sale sin término).
/// </summary>
public sealed record PurchaseQuickBooksNewSupplierDto(int SupplierId, string Name, string QuickBooksName, string? ContactName, string? Phone, string? Email,
    string? PaymentTerm, string? QuickBooksTerms);

/// <summary>
/// La ventana QuickBooks de Órdenes de compra: TODAS las por pasar (activas, que no vinieron de QuickBooks, enviadas, recibidas en parte o recibidas, sin la marca), el aviso
/// «N de estas ya salieron el (fecha)» (AlreadyExportedCount y la fecha más reciente), los proveedores y productos nuevos con sus datos, los avisos no bloqueantes (nombres
/// largos, términos sin nombre, redondeo, moneda, caracteres que QuickBooks no admite) y la cuenta de órdenes de compra que usará el archivo.
/// </summary>
public sealed record PurchaseQuickBooksPendingDto(int Count, decimal Total, int AlreadyExportedCount, DateTime? LastExportedAtUtc,
    IReadOnlyList<PurchaseQuickBooksOrderDto> Orders, IReadOnlyList<PurchaseQuickBooksNewSupplierDto> NewSuppliers, IReadOnlyList<QuickBooksNewProductDto> NewProducts,
    IReadOnlyList<string> Warnings, string PurchaseOrderAccount);

/// <summary>
/// Exportar: las órdenes indicadas (PurchaseOrderIds = PublicId, hasta 1000) o, con All = true, todas las por pasar. IncludeNew = el archivo lleva también los proveedores
/// (!VEND) y productos (!INVITEM) nuevos de esas órdenes (si no, solo las órdenes y la oficina los entra a mano).
/// </summary>
public sealed record PurchaseQuickBooksExportRequest(IReadOnlyList<Guid>? PurchaseOrderIds = null, bool All = false, bool IncludeNew = true);

/// <summary>Confirmar, «No pasó», marcar o quitar la marca: las órdenes indicadas (PurchaseOrderIds = PublicId, hasta 1000). All = true solo lo admite «marcar» (todas las por pasar).</summary>
public sealed record PurchaseQuickBooksSelectionRequest(IReadOnlyList<Guid>? PurchaseOrderIds = null, bool All = false);

/// <summary>
/// El archivo IIF de compras (Windows-1252, en base64) y la lista para «¿Ya las importaste?». AlreadyExportedCount / LastExportedAtUtc = cuántas de ellas ya habían salido
/// antes y cuándo (el aviso contra duplicados). Exportar NO las marca.
/// </summary>
public sealed record PurchaseQuickBooksExportDto(string FileName, string ContentType, string ContentBase64, DateTime ExportedAtUtc, int OrderCount, decimal Total,
    int AlreadyExportedCount, DateTime? LastExportedAtUtc, bool IncludedNew, int NewSupplierCount, int NewProductCount, IReadOnlyList<string> Warnings,
    IReadOnlyList<PurchaseQuickBooksOrderDto> Orders);

/// <summary>Cuántas quedaron marcadas (o desmarcadas), cuántas ya lo estaban (o no lo estaban), y cuántos proveedores y productos quedaron como ya mandados.</summary>
public sealed record PurchaseQuickBooksMarkResultDto(int Marked, int AlreadyMarked, int SuppliersSent, int ProductsSent, DateTime AtUtc);

/// <summary>«No pasó»: cuántas volvieron a por pasar (se borró la fecha del archivo) y cuántas quedaron igual (nunca habían salido en un archivo).</summary>
public sealed record PurchaseQuickBooksNotPassedResultDto(int Reset, int Unchanged, DateTime AtUtc);
