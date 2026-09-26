using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Teikem.Api.Auth;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;

namespace Teikem.Api.Controllers;

/// <summary>
/// Lote 6 (P4) — Recepción (R7, R8, R9, R18) bajo el módulo WMS_LOTSERIAL. Lectura con inventory.view; alta, captura de
/// líneas, confirmación y baja con warehouse.receive. Recibir contra una orden de compra exige ADEMÁS purchasing.receive y
/// el módulo PURCHASING (lo verifica el servicio: 403 con PERMISSION_DENIED o module_disabled).
/// El recibo se expone por PublicId; sus líneas (sin TenantId) por id entero SOLO bajo su recibo: una línea de otro recibo
/// o de otro tenant es 404. Historial de estatus: /api/v1/status/history/RECEIPT/{id}.
/// </summary>
[ApiController]
[Route("api/v1/receipts")]
[Authorize]
[RequireModule(ModuleKeys.WmsLotSerial)]
public sealed class ReceiptsController(ReceiptService receipts) : ControllerBase
{
    /// <summary>
    /// Lista paginada (take ≤ 200) con filtros: warehousePublicId, status (OPEN, RECEIVED, PUTAWAY), types (ASN, BLIND,
    /// RETURN), from/to (fecha de alta en UTC, 'hasta' inclusive), productPublicIds, hasVariance y search (número, referencia
    /// del aviso, cliente, orden de compra o proveedor).
    /// </summary>
    [HttpGet, RequirePermission(PermissionCatalog.InventoryView)]
    public Task<ReceiptPageDto> List([FromQuery] Guid? warehousePublicId, [FromQuery] string[]? status, [FromQuery] string[]? types,
        [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] Guid[]? productPublicIds, [FromQuery] bool? hasVariance,
        [FromQuery] string? search, [FromQuery] int skip = 0, [FromQuery] int take = 100, CancellationToken ct = default)
        => receipts.ListAsync(new ReceiptQuery(warehousePublicId, status is { Length: > 0 } ? status : null, types is { Length: > 0 } ? types : null,
            from, to, productPublicIds is { Length: > 0 } ? productPublicIds : null, hasVariance, search, skip, take), ct);

    /// <summary>Ficha del recibo: encabezado, líneas (esperado, recibido, diferencia, lote, series, costo de PO, cruce de muelle) y sus PUTAWAY.</summary>
    [HttpGet("{publicId:guid}"), RequirePermission(PermissionCatalog.InventoryView)]
    public Task<ReceiptDetailDto> Get(Guid publicId, CancellationToken ct) => receipts.GetAsync(publicId, ct);

    /// <summary>
    /// Alta OPEN con número REC-#####: contra un aviso de cliente (asnId), contra una orden de compra (purchaseOrderPublicId;
    /// + purchasing.receive y módulo PURCHASING), ciega (type BLIND, por defecto) o de devolución (RETURN) con sus líneas.
    /// Lo recibido arranca igual a lo esperado (R8). Posición de recepción: stagingBinId o la primera de una zona STAGING.
    /// </summary>
    [HttpPost, RequirePermission(PermissionCatalog.WarehouseReceive)]
    public Task<ReceiptDetailDto> Create([FromBody] ReceiptCreateRequest req, CancellationToken ct) => receipts.CreateAsync(req, ct);

    /// <summary>Captura de una línea (solo OPEN): cantidad recibida, lote (clearLot lo quita), series y posición de recepción.</summary>
    [HttpPut("{publicId:guid}/lines/{lineId:int}"), RequirePermission(PermissionCatalog.WarehouseReceive)]
    public Task<ReceiptDetailDto> UpdateLine(Guid publicId, int lineId, [FromBody] ReceiptLineUpdateRequest req, CancellationToken ct)
        => receipts.UpdateLineAsync(publicId, lineId, req, ct);

    /// <summary>Línea extra (solo OPEN, hasta 200 líneas). En un recibo de aviso de cliente el producto debe ser de ese cliente; de PO, propio.</summary>
    [HttpPost("{publicId:guid}/lines"), RequirePermission(PermissionCatalog.WarehouseReceive)]
    public Task<ReceiptDetailDto> AddLine(Guid publicId, [FromBody] ReceiptLineRequest req, CancellationToken ct)
        => receipts.AddLineAsync(publicId, req, ct);

    /// <summary>Quita una línea extra (solo OPEN). Las líneas del aviso no se quitan (409); con cruce de muelle asignado → 409.</summary>
    [HttpDelete("{publicId:guid}/lines/{lineId:int}"), RequirePermission(PermissionCatalog.WarehouseReceive)]
    public Task<ReceiptDetailDto> RemoveLine(Guid publicId, int lineId, CancellationToken ct)
        => receipts.RemoveLineAsync(publicId, lineId, ct);

    /// <summary>
    /// Confirma el recibo completo (D5): RECEIPT por lo esperado + ADJUSTMENT RECEIPT_VARIANCE por la diferencia (D4), PO a
    /// PARTIAL/RECEIVED, aviso a RECEIVED, reparto de cruce de muelle y PUTAWAY por el remanente. Segunda confirmación → 422.
    /// </summary>
    [HttpPost("{publicId:guid}/confirm"), RequirePermission(PermissionCatalog.WarehouseReceive)]
    public Task<ReceiptDetailDto> Confirm(Guid publicId, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ReceiptConfirmRequest? req,
        CancellationToken ct)
        => receipts.ConfirmAsync(publicId, req, ct);

    /// <summary>Baja de un recibo OPEN sin cruce de muelle (204). Su aviso de PO se cancela; el de cliente queda pendiente.</summary>
    [HttpDelete("{publicId:guid}"), RequirePermission(PermissionCatalog.WarehouseReceive)]
    public async Task<IActionResult> Delete(Guid publicId, CancellationToken ct)
    {
        await receipts.DeleteAsync(publicId, ct);
        return NoContent();
    }
}
