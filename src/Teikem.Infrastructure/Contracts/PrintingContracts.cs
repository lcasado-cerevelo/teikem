namespace Teikem.Infrastructure.Contracts;

// ======================================================================================================================
// Lote 43 (F-G1) — impresoras y plantillas de label. La impresora es un PERFIL de impresión de la compañía (uso, tamaño, girar,
// predeterminada). El servidor nunca se conecta a una impresora: el navegador imprime DIRECTO con QZ Tray (instalado en cada
// computadora, QZ_TRAY) o, de respaldo, con el PDF y el diálogo del navegador (BROWSER). La configuración de cada estación (este
// navegador) vive en el navegador y el servidor no la recibe (D-FG2).
// ======================================================================================================================

/// <summary>
/// Impresora de la compañía. Purpose: LABEL | DOCUMENT; ConnectionType: BROWSER | QZ_TRAY; SystemName: nombre exacto de la impresora en
/// el sistema de la computadora (QZ Tray imprime por él; null = Name); MediaSize: 4X6 | 4X4 | 4X2 (labels) o LETTER
/// (documentos). RotateMedia = «Girar 90°» (la página sale con /Rotate 90). IsDefault = predeterminada de la compañía para su uso.
/// RowVersion en base64 para el PATCH con concurrencia optimista.
/// </summary>
public sealed record PrinterDto(
    Guid PublicId,
    string Name,
    string? Model,
    string Purpose,
    string ConnectionType,
    string MediaSize,
    bool RotateMedia,
    string? Notes,
    bool IsDefault,
    bool IsActive,
    string RowVersion,
    string? SystemName);

/// <summary>
/// Alta (POST) o edición (PATCH) de una impresora. En el alta: Name y Purpose obligatorios; ConnectionType vacío = BROWSER; MediaSize
/// vacío = 4X6 (labels) o LETTER (documentos). En el PATCH null = sin cambio; "" en Model/Notes los quita; Purpose no se cambia (400).
/// RowVersion opcional (409 si el registro cambió).
/// </summary>
public sealed record PrinterRequest(
    string? Name = null,
    string? Model = null,
    string? Purpose = null,
    string? ConnectionType = null,
    string? MediaSize = null,
    bool? RotateMedia = null,
    string? Notes = null,
    string? RowVersion = null,
    string? SystemName = null);

/// <summary>Elemento del label: Mostrar, Posición (tl, tc, tr, ml, mc, mr, bl, bc, br), Tamaño (sm, md, lg) y «Como código de barras».</summary>
public sealed record LabelElementDto(bool On, string Pos, string Size, bool AsBarcode = false);

/// <summary>
/// Construcción del label: los 9 elementos del constructor de la maqueta (el código de barras = número del paquete). En una petición, un elemento null toma el del label integrado.
/// </summary>
public sealed record LabelLayoutDto(
    LabelElementDto? Logo,
    LabelElementDto? Service,
    LabelElementDto? Sender,
    LabelElementDto? Consignee,
    LabelElementDto? Address,
    LabelElementDto? Barcode,
    LabelElementDto? Waybill,
    LabelElementDto? Invoice,
    LabelElementDto? Cod);

/// <summary>Plantilla de label (diseño independiente del tamaño de la etiqueta). IsDefault = predeterminada de la compañía.</summary>
public sealed record LabelTemplateDto(
    Guid PublicId,
    string Name,
    LabelLayoutDto Layout,
    bool IsDefault,
    bool IsActive,
    string RowVersion,
    // PIECE (label de cada pieza) | INVOICE (label de la factura: un código de barras con el número, uno por envío)
    string Kind = "PIECE");

/// <summary>Alta (POST: Name y Layout obligatorios) o edición (PATCH: null = sin cambio) de una plantilla. RowVersion opcional.</summary>
public sealed record LabelTemplateRequest(string? Name = null, LabelLayoutDto? Layout = null, string? RowVersion = null, string? Kind = null);

/// <summary>Impresora activa para el selector de la estación (sin notas).</summary>
public sealed record PrinterOptionDto(
    Guid PublicId,
    string Name,
    string? Model,
    string Purpose,
    string ConnectionType,
    string MediaSize,
    bool RotateMedia,
    bool IsDefault,
    string? SystemName);

/// <summary>
/// Lo que necesita una estación para imprimir: impresoras y plantillas ACTIVAS de la compañía (con su predeterminada marcada) y el
/// label integrado (el que aplica si no hay plantilla). Orden de resolución en el navegador: estación → compañía → integrado.
/// PrintLabelsDirect = ajuste de la compañía «Imprimir labels directo a la impresora» (los labels de las órdenes salen solos al
/// guardarlas, sin diálogo). QzSigningConfigured = el servidor tiene certificado y clave para firmar las peticiones de QZ Tray (si no,
/// QZ Tray pregunta «¿Permitir?» en cada computadora).
/// </summary>
public sealed record PrintStationOptionsDto(
    IReadOnlyList<PrinterOptionDto> Printers,
    IReadOnlyList<LabelTemplateDto> Templates,
    LabelLayoutDto BuiltInLayout,
    bool PrintLabelsDirect,
    bool QzSigningConfigured,
    // el label integrado de la FACTURA (el que aplica si la compañía no tiene plantilla de factura)
    LabelLayoutDto? BuiltInInvoiceLayout = null);

/// <summary>Ajustes de impresión de la compañía (printers.manage): «Imprimir labels directo a la impresora».</summary>
public sealed record PrintingSettingsDto(bool PrintLabelsDirect);

/// <summary>Cambio de los ajustes de impresión de la compañía (null = sin cambio).</summary>
public sealed record PrintingSettingsRequest(bool? PrintLabelsDirect = null);

/// <summary>Certificado público (PEM) con el que QZ Tray reconoce a este servidor (override.crt en cada computadora).</summary>
public sealed record QzCertificateDto(string Certificate);

/// <summary>Texto que QZ Tray pide firmar (un hash corto de la petición).</summary>
public sealed record QzSignRequest(string? Request);

/// <summary>Firma SHA512withRSA en base64 del texto pedido.</summary>
public sealed record QzSignatureDto(string Signature, string Algorithm);
