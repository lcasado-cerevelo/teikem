using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Teikem.Api.Auth;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;

namespace Teikem.Api.Controllers;

/// <summary>
/// Lote 246 (Lote F del plan de Solutions) — QuickBooks Desktop de Órdenes de compra (módulo PURCHASING; todo con purchasing.quickbooks). Archivo IIF con TRNS PURCHORD
/// (una, varias o todas las por pasar); exportar NO marca: después se responde «Sí, ya están» (confirm), «No pasó» (not-passed) o «Todavía no» (nada). Marcar y quitar la
/// marca a mano para corregir. Ninguna acción cambia el estatus de la orden. La cuenta de órdenes de compra se configura en GET/PATCH /api/v1/sales-orders/quickbooks/settings.
/// El formato PURCHORD todavía no se ha probado contra un QuickBooks Desktop real.
/// </summary>
[ApiController]
[Route("api/v1/purchase-orders/quickbooks")]
[Authorize]
[RequireModule(ModuleKeys.Purchasing)]
public sealed class PurchaseOrderQuickBooksController(QuickBooksService quickBooks) : ControllerBase
{
    /// <summary>
    /// Todas las órdenes de compra por pasar (activas, que no vinieron de QuickBooks, enviadas, recibidas en parte o recibidas, sin la marca), con proveedores y productos
    /// nuevos y sus datos, el aviso «N de estas ya salieron el (fecha)» y avisos no bloqueantes (nombres largos, términos, redondeo, moneda, caracteres).
    /// </summary>
    [HttpGet, RequirePermission(PermissionCatalog.PurchasingQuickBooks)]
    public Task<PurchaseQuickBooksPendingDto> Pending(CancellationToken ct) => quickBooks.GetPurchasePendingAsync(ct);

    /// <summary>
    /// Genera el archivo .iif (Windows-1252, en base64) con las órdenes indicadas (purchaseOrderIds, hasta 1000) o todas las por pasar (all = true) y, si includeNew (por omisión
    /// sí), sus proveedores y productos nuevos. Anota la fecha del archivo en cada orden, pero no las marca. 400 sin selección o con una inexistente; 409 ya en QuickBooks;
    /// 422 vino de QuickBooks, estatus que no se pasa o nada por pasar.
    /// </summary>
    [HttpPost("export"), RequirePermission(PermissionCatalog.PurchasingQuickBooks)]
    public Task<PurchaseQuickBooksExportDto> Export([FromBody] PurchaseQuickBooksExportRequest? req, CancellationToken ct)
        => quickBooks.ExportPurchaseOrdersAsync(req, ct);

    /// <summary>«Sí, ya están»: marca como en QuickBooks las órdenes del archivo que se importaron. Una que nunca salió en un archivo = 422.</summary>
    [HttpPost("confirm"), RequirePermission(PermissionCatalog.PurchasingQuickBooks)]
    public Task<PurchaseQuickBooksMarkResultDto> Confirm([FromBody] PurchaseQuickBooksSelectionRequest? req, CancellationToken ct)
        => quickBooks.ConfirmPurchaseOrdersAsync(req, ct);

    /// <summary>«No pasó»: las indicadas pierden la fecha del archivo y vuelven a por pasar. Marcada = 409; vino de QuickBooks = 422; nunca exportada = sin cambio.</summary>
    [HttpPost("not-passed"), RequirePermission(PermissionCatalog.PurchasingQuickBooks)]
    public Task<PurchaseQuickBooksNotPassedResultDto> NotPassed([FromBody] PurchaseQuickBooksSelectionRequest? req, CancellationToken ct)
        => quickBooks.NotPassedPurchaseOrdersAsync(req, ct);

    /// <summary>Marcar a mano como en QuickBooks: purchaseOrderIds o all = true (todas las por pasar). Las ya marcadas no cambian.</summary>
    [HttpPost("mark"), RequirePermission(PermissionCatalog.PurchasingQuickBooks)]
    public Task<PurchaseQuickBooksMarkResultDto> Mark([FromBody] PurchaseQuickBooksSelectionRequest? req, CancellationToken ct)
        => quickBooks.MarkPurchaseOrdersAsync(req, ct);

    /// <summary>Quitar la marca a las indicadas: vuelven a «exportada, por confirmar» o a por pasar. Para corregir una marca puesta por error.</summary>
    [HttpPost("unmark"), RequirePermission(PermissionCatalog.PurchasingQuickBooks)]
    public Task<PurchaseQuickBooksMarkResultDto> Unmark([FromBody] PurchaseQuickBooksSelectionRequest? req, CancellationToken ct)
        => quickBooks.UnmarkPurchaseOrdersAsync(req, ct);
}
