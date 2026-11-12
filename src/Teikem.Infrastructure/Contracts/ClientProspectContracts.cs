namespace Teikem.Infrastructure.Contracts;

// ======================================================================================================================
// Lote 222 (P2 del pedido de vendedores) — cliente prospecto: alta mínima (clients.create_basic), aprobar / rechazar (sales.approve_client) y documentos.
// ======================================================================================================================

/// <summary>Una dirección del alta mínima: dos líneas, el pueblo (City) y el código postal. State y Country vacíos = PR.</summary>
public sealed record ClientBasicAddressRequest(string? Line1 = null, string? Line2 = null, string? City = null, string? PostalCode = null, string? State = null, string? Country = null);

/// <summary>
/// POST /clients/basic — alta mínima del cliente nuevo desde un pedido: nace PROSPECTO y avisa a quien aprueba. Obligatorios: Name (negocio), ContactName, Phone (10 dígitos) y
/// PhysicalAddress (línea 1 y pueblo). LegalName = razón social / DBA. PaymentTerm = la forma de pago del cliente (código del catálogo PaymentTerm: CHEQUE, CASH, ACH, NET15,
/// NET20, NET30, NET45, PK_BY_REP, CONSIGNMENT…). Las fotos del registro y del certificado se suben después con POST /clients/{id}/documents.
/// </summary>
public sealed record ClientBasicCreateRequest(string? Name = null, string? LegalName = null, string? ContactName = null, string? Phone = null, string? PhoneExtension = null,
    string? Phone2 = null, string? Email = null, string? MerchantRegistration = null, ClientBasicAddressRequest? PhysicalAddress = null, ClientBasicAddressRequest? PostalAddress = null,
    string? PaymentTerm = null);

/// <summary>Aprobar un prospecto (→ ACTIVO): límite de crédito (null = sin cambio; 0 o más) y término de pago (código; null = sin cambio), con comentario opcional.</summary>
public sealed record ClientApproveRequest(decimal? CreditLimit = null, string? PaymentTerm = null, string? Comment = null);

/// <summary>Rechazar un prospecto (→ SUSPENDIDO, se cancelan sus órdenes abiertas): el comentario es obligatorio.</summary>
public sealed record ClientRejectRequest(string? Comment = null);

/// <summary>El cliente como quedó y las órdenes que se cancelaron al rechazarlo (sus números).</summary>
public sealed record ClientRejectResultDto(ClientDetailDto Client, IReadOnlyList<string> CancelledOrders);

/// <summary>Un documento del cliente: tipo (MERCHANT_REGISTRATION, RESELLER_CERTIFICATE, OTHER) y su archivo (se ve por GET /files/{filePublicId}).</summary>
public sealed record ClientDocumentDto(Guid PublicId, string DocumentType, string DocumentTypeLabel, Guid FilePublicId, string FileName, string ContentType, long SizeBytes,
    string? Notes, DateTime CreatedAtUtc, string? CreatedByName);

/// <summary>Lote 222: Ajustes → Operación → Ventas. Charge null = sin cargo de envío; FreeFrom null = sin mínimo (siempre se cobra); el mínimo se mide después del IVU (fijo).</summary>
public sealed record SalesShippingSettingsDto(decimal? Charge, decimal? FreeFrom, bool Taxable, bool MeasuredAfterTax = true);

/// <summary>Cambio parcial de los ajustes de envío: null = sin cambio; Charge 0 = quitar el cargo; FreeFrom 0 = quitar el mínimo.</summary>
public sealed record SalesShippingSettingsRequest(decimal? Charge = null, decimal? FreeFrom = null, bool? Taxable = null);

/// <summary>
/// Lote 222 — panel Ventas del Pulso del día (pulse.sales + sales.view, módulo SALES_ORDERS). OrdersToday = órdenes creadas hoy (hora de la compañía, sin canceladas);
/// UrgentOpen = urgentes solicitadas o despachadas en parte; ClientsToApprove = clientes prospecto activos; AwaitingApproval = órdenes que esperan la aprobación del cliente.
/// InvoicesToQuickBooks y ConsignmentUnpaid quedan en null hasta que sus partes (QuickBooks, consignación) los llenen. Cada cifra lleva la ruta de su lista.
/// </summary>
public sealed record PulseSalesPanelDto(int OrdersToday, decimal OrdersTodayAmount, int UrgentOpen, int ClientsToApprove, int AwaitingApproval,
    int? InvoicesToQuickBooks, int? ConsignmentUnpaid, IReadOnlyDictionary<string, string> Routes,
    // Lote 238: venta neta del mes en curso (análisis de ventas) y la suma de las cuotas vigentes; null sin sales.analysis.view (MonthQuota null también si no hay cuotas)
    decimal? MonthSales = null, decimal? MonthQuota = null);
