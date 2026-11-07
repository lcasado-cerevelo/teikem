using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Teikem.Api.Auth;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;

namespace Teikem.Api.Controllers;

/// <summary>
/// Lote 225 (P5 del pedido de vendedores) — pestaña QuickBooks de Órdenes de venta (módulo SALES_ORDERS). QuickBooks Desktop por archivo IIF. Una factura de venta emitida
/// tiene una sola marca «en QuickBooks» (sí/no): ver con sales.quickbooks.view (la lista y el histórico también con mark o export); marcar a mano (una, varias o todas) con sales.quickbooks.mark; exportar el archivo y confirmar
/// «sí, ya están» con sales.quickbooks.export. Exportar saca SIEMPRE todas las por pasar y no las marca.
/// </summary>
[ApiController]
[Route("api/v1/sales-orders/quickbooks")]
[Authorize]
[RequireModule(ModuleKeys.SalesOrders)]
public sealed class QuickBooksController(QuickBooksService quickBooks) : ControllerBase
{
    /// <summary>La lista y el histórico los ven los tres permisos de QuickBooks (quien marca o exporta necesita verlas).</summary>
    private const string AnyQuickBooks = PermissionCatalog.SalesQuickBooksView + "|" + PermissionCatalog.SalesQuickBooksMark + "|" + PermissionCatalog.SalesQuickBooksExport;

    /// <summary>
    /// Todas las facturas por pasar (sin paginar: «Exportar» las lleva todas), con «cliente nuevo» / «producto nuevo» y sus datos («ver datos»), el aviso «N de estas ya salieron
    /// el (fecha)» y avisos de lo que QuickBooks rechazaría (nombres largos, términos sin nombre en QuickBooks).
    /// </summary>
    [HttpGet, RequirePermission(AnyQuickBooks)]
    public Task<QuickBooksPendingDto> Pending(CancellationToken ct) => quickBooks.GetPendingAsync(ct);

    /// <summary>
    /// Genera el archivo .iif (Windows-1252, en base64) con TODAS las facturas por pasar y, si includeNew (por omisión sí), sus clientes y productos nuevos. Anota la fecha de este
    /// archivo en cada factura, pero no las marca: la respuesta trae la lista para «¿Ya importaste estas N?». Nada por pasar = 422.
    /// </summary>
    [HttpPost("export"), RequirePermission(PermissionCatalog.SalesQuickBooksExport)]
    public Task<QuickBooksExportDto> Export([FromBody] QuickBooksExportRequest? req, CancellationToken ct) => quickBooks.ExportAsync(req, ct);

    /// <summary>«Sí, ya están»: marca como en QuickBooks las facturas del archivo que se importaron (las desmarcadas saldrán en el próximo). Una que nunca salió en un archivo = 422.</summary>
    [HttpPost("confirm"), RequirePermission(PermissionCatalog.SalesQuickBooksExport)]
    public Task<QuickBooksMarkResultDto> Confirm([FromBody] QuickBooksConfirmRequest? req, CancellationToken ct) => quickBooks.ConfirmAsync(req, ct);

    /// <summary>Marcar a mano como en QuickBooks: invoiceIds (PublicId) o all = true (todas las por pasar). Las ya marcadas no cambian.</summary>
    [HttpPost("mark"), RequirePermission(PermissionCatalog.SalesQuickBooksMark)]
    public Task<QuickBooksMarkResultDto> Mark([FromBody] QuickBooksMarkRequest? req, CancellationToken ct) => quickBooks.MarkAsync(req, ct);

    /// <summary>Quitar la marca a las indicadas (invoiceIds): vuelven a «por pasar». Para corregir una marca puesta por error.</summary>
    [HttpPost("unmark"), RequirePermission(PermissionCatalog.SalesQuickBooksMark)]
    public Task<QuickBooksMarkResultDto> Unmark([FromBody] QuickBooksMarkRequest? req, CancellationToken ct) => quickBooks.UnmarkAsync(req, ct);

    /// <summary>Histórico: las ya pasadas con quién y cuándo (la más reciente primero). from/to = día de la marca (hora de la compañía); q = factura, cliente o número del cliente.</summary>
    [HttpGet("history"), RequirePermission(AnyQuickBooks)]
    public Task<PagedResult<QuickBooksHistoryItemDto>> History([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] string? q, [FromQuery] int skip,
        [FromQuery] int take, CancellationToken ct) => quickBooks.GetHistoryAsync(from, to, q, skip, take, ct);

    /// <summary>Ajustes → Operación → QuickBooks: cuentas (por cobrar, ventas, costo, inventario, impuesto), ítems de IVU y de envío, y términos de pago → nombre en QuickBooks.</summary>
    [HttpGet("settings"), RequirePermission(PermissionCatalog.SalesQuickBooksView + "|" + PermissionCatalog.AdminTenant)]
    public Task<QuickBooksSettingsDto> Settings(CancellationToken ct) => quickBooks.GetSettingsAsync(ct);

    /// <summary>Cambia los ajustes (null = sin cambio; "" = valor por omisión). 400 con el mensaje exacto si un nombre es demasiado largo o un término no existe.</summary>
    [HttpPatch("settings"), RequirePermission(PermissionCatalog.AdminTenant)]
    public Task<QuickBooksSettingsDto> UpdateSettings([FromBody] QuickBooksSettingsRequest? req, CancellationToken ct) => quickBooks.UpdateSettingsAsync(req, ct);
}
