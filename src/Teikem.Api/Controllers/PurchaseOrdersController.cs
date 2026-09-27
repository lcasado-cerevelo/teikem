using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Teikem.Api.Auth;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;

namespace Teikem.Api.Controllers;

/// <summary>
/// Lote 6 (P8) — órdenes de compra y faltantes (módulo PURCHASING; pantalla 'Ajustes de inventario', R14).
/// - purchasing.view: lista, ficha, órdenes con faltante y líneas en faltante.
/// - purchasing.manage: alta (DRAFT, PO-#####), edición (capacidad EDIT_PURCHASE_ORDER; 422 fuera de DRAFT por defecto),
///   enviar, cancelar (DRAFT/SENT/PARTIAL, con comentario en el historial) y eliminar (sin recepciones).
/// - inventory.adjust: resolver un faltante por línea (CLOSE, REORDER — además purchasing.manage —, MANUAL_ADJUSTMENT —
///   además el módulo WMS_LOTSERIAL —).
/// La orden se expone por PublicId; sus líneas por id entero SOLO bajo su orden. Historial:
/// /api/v1/status/history/PURCHASE_ORDER/{id}. Recibir contra la orden se hace en /api/v1/receipts.
/// </summary>
[ApiController]
[Route("api/v1/purchase-orders")]
[Authorize]
[RequireModule(ModuleKeys.Purchasing)]
public sealed class PurchaseOrdersController(PurchaseOrderService purchaseOrders, PurchaseShortageService shortages) : ControllerBase
{
    /// <summary>
    /// Lista paginada (take ≤ 200), más recientes primero. Filtros: status (DRAFT, SENT, PARTIAL, RECEIVED, CANCELLED),
    /// supplierId, warehousePublicId, from/to (fecha de la orden) y search (número, proveedor, notas, SKU o producto).
    /// </summary>
    [HttpGet, RequirePermission(PermissionCatalog.PurchasingView)]
    public Task<PurchaseOrderPageDto> List([FromQuery] string[]? status, [FromQuery] int? supplierId, [FromQuery] Guid? warehousePublicId,
        [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] string? search, [FromQuery] int skip = 0, [FromQuery] int take = 100,
        CancellationToken ct = default)
        => purchaseOrders.ListAsync(new PurchaseOrderQuery(status is { Length: > 0 } ? status : null, supplierId, warehousePublicId, from, to,
            search, skip, take), ct);

    /// <summary>Órdenes activas no canceladas con recibo confirmado y cantidades pendientes (faltante y su costo).</summary>
    [HttpGet("shortages"), RequirePermission(PermissionCatalog.PurchasingView)]
    public Task<IReadOnlyList<PoShortageSummaryDto>> Shortages(CancellationToken ct) => shortages.ListWithShortageAsync(ct);

    [HttpGet("{publicId:guid}"), RequirePermission(PermissionCatalog.PurchasingView)]
    public Task<PurchaseOrderDto> Get(Guid publicId, CancellationToken ct) => purchaseOrders.GetAsync(publicId, ct);

    /// <summary>Líneas con faltante pendiente o con resoluciones (bitácora: acción, cantidad, motivo, reorden, movimiento).</summary>
    [HttpGet("{publicId:guid}/shortage-lines"), RequirePermission(PermissionCatalog.PurchasingView)]
    public Task<IReadOnlyList<ShortageLineDto>> ShortageLines(Guid publicId, CancellationToken ct) => shortages.LinesAsync(publicId, ct);

    /// <summary>Alta en DRAFT: proveedor activo, almacén (o el único activo), solo productos propios sin repetir, costo por línea.</summary>
    [HttpPost, RequirePermission(PermissionCatalog.PurchasingManage)]
    public Task<PurchaseOrderDto> Create([FromBody] PurchaseOrderCreateRequest req, CancellationToken ct) => purchaseOrders.CreateAsync(req, ct);

    /// <summary>Edición (fecha esperada, notas y reemplazo de líneas) bajo la capacidad EDIT_PURCHASE_ORDER del estatus actual.</summary>
    [HttpPatch("{publicId:guid}"), RequirePermission(PermissionCatalog.PurchasingManage)]
    public Task<PurchaseOrderDto> Update(Guid publicId, [FromBody] PurchaseOrderPatchRequest req, CancellationToken ct)
        => purchaseOrders.UpdateAsync(publicId, req, ct);

    /// <summary>DRAFT → SENT.</summary>
    [HttpPost("{publicId:guid}/send"), RequirePermission(PermissionCatalog.PurchasingManage)]
    public Task<PurchaseOrderDto> Send(Guid publicId, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] PurchaseOrderStatusRequest? req,
        CancellationToken ct)
        => purchaseOrders.SendAsync(publicId, req, ct);

    /// <summary>Cancela desde DRAFT, SENT o PARTIAL con comentario en el historial; RECEIVED → 422; recibo abierto → 409.</summary>
    [HttpPost("{publicId:guid}/cancel"), RequirePermission(PermissionCatalog.PurchasingManage)]
    public Task<PurchaseOrderDto> Cancel(Guid publicId, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] PurchaseOrderStatusRequest? req,
        CancellationToken ct)
        => purchaseOrders.CancelAsync(publicId, req, ct);

    /// <summary>Baja lógica (204): DRAFT, SENT o CANCELLED sin recepciones ni recibo abierto; si no, 409.</summary>
    [HttpDelete("{publicId:guid}"), RequirePermission(PermissionCatalog.PurchasingManage)]
    public async Task<IActionResult> Delete(Guid publicId, CancellationToken ct)
    {
        await purchaseOrders.DeleteAsync(publicId, ct);
        return NoContent();
    }

    /// <summary>
    /// Resuelve el faltante de una línea: CLOSE o REORDER (el pendiente completo) o MANUAL_ADJUSTMENT (parcial, con
    /// posición, motivo PO_SHORTAGE/FOUND y lote o series). Sin pendiente → 409; orden cancelada o sin recepciones → 422.
    /// </summary>
    [HttpPost("{publicId:guid}/lines/{lineId:int}/resolve"), RequirePermission(PermissionCatalog.InventoryAdjust)]
    public Task<ShortageResolveResultDto> Resolve(Guid publicId, int lineId, [FromBody] ShortageResolveRequest req, CancellationToken ct)
        => shortages.ResolveAsync(publicId, lineId, req, ct);
}
