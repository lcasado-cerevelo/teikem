namespace Teikem.Infrastructure.Contracts;

// Lote 225 (P5 del pedido de vendedores) — QuickBooks Desktop: facturas por pasar, archivo IIF, marca «en QuickBooks», histórico y ajustes.

/// <summary>
/// Una factura por pasar a QuickBooks. QuickBooksCustomer = el nombre con que sale en el archivo, ya limpio como lo escribe el archivo (el «Código QuickBooks» de la migración
/// o el nombre del negocio; sin comillas dobles, tabuladores ni dos puntos).
/// ExportedAtUtc = la última vez que salió en un archivo (NULL = nunca). NewClient / NewProductSkus = lo que QuickBooks todavía no tiene (los datos van en la lista de arriba).
/// </summary>
public sealed record QuickBooksPendingInvoiceDto(Guid PublicId, string Number, DateOnly? IssueDate, DateOnly? DueDate, string Status, Guid ClientPublicId, string ClientCode,
    string ClientName, string QuickBooksCustomer, decimal Subtotal, decimal TaxAmount, decimal Total, IReadOnlyList<string> OrderNumbers, DateTime? ExportedAtUtc,
    bool NewClient, IReadOnlyList<string> NewProductSkus);

/// <summary>Los datos de un cliente nuevo para QuickBooks («ver datos»): lo que lleva el archivo, por si la oficina prefiere teclearlo (QuickBooksName tal como sale en el archivo).</summary>
public sealed record QuickBooksNewClientDto(Guid PublicId, string Code, string QuickBooksName, string? CompanyName, IReadOnlyList<string> BillTo, IReadOnlyList<string> ShipTo,
    string? Phone1, string? Phone2, string? Email, string? Contact, string? PaymentTerm, string? QuickBooksTerms, string? MerchantRegistration, decimal? CreditLimit);

/// <summary>Los datos de un producto nuevo para QuickBooks: nombre del ítem = SKU (QuickBooksName = como sale en el archivo; los dos puntos pasan a guion). Un kit no sale: salen sus componentes.</summary>
public sealed record QuickBooksNewProductDto(Guid PublicId, string Sku, string QuickBooksName, string Name, decimal? Price, decimal? Cost, bool Taxable);

/// <summary>
/// La pestaña QuickBooks: todas las facturas por pasar (no se pagina: «Exportar» las lleva todas), el aviso «N de estas ya salieron el (fecha)» (AlreadyExportedCount y la
/// fecha más reciente), los clientes y productos nuevos con sus datos, y avisos de lo que QuickBooks rechazaría (nombres largos, términos sin nombre).
/// </summary>
public sealed record QuickBooksPendingDto(int Count, decimal Total, int AlreadyExportedCount, DateTime? LastExportedAtUtc, IReadOnlyList<QuickBooksPendingInvoiceDto> Invoices,
    IReadOnlyList<QuickBooksNewClientDto> NewClients, IReadOnlyList<QuickBooksNewProductDto> NewProducts, IReadOnlyList<string> Warnings);

/// <summary>IncludeNew = el archivo lleva también los clientes y productos nuevos de esas facturas (si no, solo las facturas y la oficina los entra a mano).</summary>
public sealed record QuickBooksExportRequest(bool IncludeNew = true);

/// <summary>
/// El archivo IIF (Windows-1252, en base64) con TODAS las facturas por pasar, y la lista para «¿Ya importaste estas N en QuickBooks?». AlreadyExportedCount / LastExportedAtUtc =
/// cuántas de ellas ya habían salido antes y cuándo (el aviso contra duplicados). Exportar NO las marca.
/// </summary>
public sealed record QuickBooksExportDto(string FileName, string ContentType, string ContentBase64, DateTime ExportedAtUtc, int InvoiceCount, decimal Total,
    int AlreadyExportedCount, DateTime? LastExportedAtUtc, bool IncludedNew, int NewClientCount, int NewProductCount, IReadOnlyList<QuickBooksPendingInvoiceDto> Invoices);

/// <summary>Marcar o quitar la marca: las facturas indicadas (InvoiceIds = PublicId) o, con All = true, todas las por pasar.</summary>
public sealed record QuickBooksMarkRequest(IReadOnlyList<Guid>? InvoiceIds = null, bool All = false);

/// <summary>«Sí, ya están»: las facturas del archivo que sí se importaron (las desmarcadas no se mandan y saldrán en el próximo).</summary>
public sealed record QuickBooksConfirmRequest(IReadOnlyList<Guid>? InvoiceIds = null);

/// <summary>Cuántas quedaron marcadas (o desmarcadas), cuántas ya lo estaban, y cuántos clientes y productos quedaron como ya mandados.</summary>
public sealed record QuickBooksMarkResultDto(int Marked, int AlreadyMarked, int ClientsSent, int ProductsSent, DateTime AtUtc);

/// <summary>Histórico: una factura ya pasada, con quién y cuándo la marcó y la última vez que salió en un archivo.</summary>
public sealed record QuickBooksHistoryItemDto(Guid PublicId, string Number, DateOnly? IssueDate, string ClientCode, string ClientName, decimal Total, DateTime QuickBooksAtUtc,
    int? QuickBooksBy, string? QuickBooksByName, DateTime? ExportedAtUtc);

/// <summary>Un término de pago de Teikem y su nombre en QuickBooks (NULL = sin nombre: la factura sale sin término).</summary>
public sealed record QuickBooksTermMapDto(string Code, string Label, string? QuickBooksName);

/// <summary>Ajustes → Operación → QuickBooks, con los valores por omisión ya aplicados.</summary>
public sealed record QuickBooksSettingsDto(string ReceivableAccount, string SalesAccount, string CostAccount, string InventoryAccount, string TaxAccount, string TaxItem,
    string ShippingItem, IReadOnlyList<QuickBooksTermMapDto> Terms);

/// <summary>Cambiar los ajustes: null = sin cambio; "" = volver al valor por omisión. Terms: código → nombre en QuickBooks ("" o null quita ese término del mapa); los que no vienen no cambian.</summary>
public sealed record QuickBooksSettingsRequest(string? ReceivableAccount = null, string? SalesAccount = null, string? CostAccount = null, string? InventoryAccount = null,
    string? TaxAccount = null, string? TaxItem = null, string? ShippingItem = null, IReadOnlyDictionary<string, string?>? Terms = null);
