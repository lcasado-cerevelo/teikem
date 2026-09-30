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
    /// Lista paginada (take ≤ 200) con filtros: warehousePublicId, status (EXPECTED, RECEIVING, DISCREPANCY, RECEIVED,
    /// RECEIVED_VARIANCE, PUTAWAY), types (ASN, BLIND, RETURN), from/to (fecha de alta en UTC, 'hasta' inclusive),
    /// productPublicIds, hasVariance, search (número, transporte, referencia del recibo o del aviso, cliente, orden de compra
    /// o proveedor) y, Lote 13, variance (SHORT, OVER, NONE; varias con O) y phase (OPEN, PENDING_PUTAWAY, DONE).
    /// </summary>
    [HttpGet, RequirePermission(PermissionCatalog.InventoryView)]
    public Task<ReceiptPageDto> List([FromQuery] Guid? warehousePublicId, [FromQuery] string[]? status, [FromQuery] string[]? types,
        [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] Guid[]? productPublicIds, [FromQuery] bool? hasVariance,
        [FromQuery] string? search, [FromQuery] string[]? variance, [FromQuery] string? phase,
        [FromQuery] int skip = 0, [FromQuery] int take = 100, CancellationToken ct = default)
        => receipts.ListAsync(new ReceiptQuery(warehousePublicId, status is { Length: > 0 } ? status : null, types is { Length: > 0 } ? types : null,
            from, to, productPublicIds is { Length: > 0 } ? productPublicIds : null, hasVariance, search, skip, take,
            variance is { Length: > 0 } ? variance : null, phase), ct);

    /// <summary>Ficha del recibo: encabezado, líneas (esperado, recibido, diferencia, lote, series, costo de PO, cruce de muelle) y sus PUTAWAY.</summary>
    [HttpGet("{publicId:guid}"), RequirePermission(PermissionCatalog.InventoryView)]
    public Task<ReceiptDetailDto> Get(Guid publicId, CancellationToken ct) => receipts.GetAsync(publicId, ct);

    /// <summary>
    /// Alta con número REC-#####: contra un aviso de cliente (asnId), contra una orden de compra (purchaseOrderPublicId;
    /// + purchasing.receive y módulo PURCHASING), ciega (type BLIND, por defecto) o de devolución (RETURN). Lote 13: nace
    /// abierto — contra aviso u orden de compra en RECEIVING (o DISCREPANCY si lo capturado difiere de lo esperado); ciego o
    /// devolución sin líneas en EXPECTED (solo el encabezado; las líneas se agregan después). carrier y reference (≤ 80) van
    /// en el encabezado; stagingBinId queda como posición de recepción por defecto.
    /// Lo recibido arranca igual a lo esperado (R8). Posición de recepción: stagingBinId o la primera de una zona STAGING.
    /// Lote 8A (cola del aparato): con confirm = true crea, captura las líneas completas (lote y series incluidos) y confirma
    /// en UNA transacción (lines obligatorias en ciegos y devoluciones); devuelve el recibo ya confirmado (RECEIVED o
    /// RECEIVED_VARIANCE, o PUTAWAY si no quedó nada por acomodar) con los mismos mensajes que el flujo por pasos. Respeta
    /// Idempotency-Key (el reintento devuelve el mismo REC). Contra aviso u orden de compra, lines se aplica sobre las líneas
    /// del documento (lo escaneado manda; lo no mencionado queda en 0; un producto fuera del documento entra como línea
    /// extra); sin lines se recibe lo esperado.
    /// </summary>
    [HttpPost, RequirePermission(PermissionCatalog.WarehouseReceive)]
    public Task<ReceiptDetailDto> Create([FromBody] ReceiptCreateRequest req, CancellationToken ct) => receipts.CreateAsync(req, ct);

    /// <summary>
    /// Lote 13 — edita el encabezado de un recibo abierto (confirmado → 422; rowVersion distinto → 409): type BLIND ↔ RETURN
    /// (solo sin aviso ni orden de compra), warehousePublicId (solo sin documento y sin líneas; limpia posición y muelle),
    /// stagingBinId / clearStagingBin, dockId / clearDock, carrier y reference ('' = borrar; máximo 80). null = no cambiar.
    /// </summary>
    [HttpPatch("{publicId:guid}"), RequirePermission(PermissionCatalog.WarehouseReceive)]
    public Task<ReceiptDetailDto> UpdateHeader(Guid publicId, [FromBody] ReceiptHeaderUpdateRequest req, CancellationToken ct)
        => receipts.UpdateHeaderAsync(publicId, req, ct);

    /// <summary>
    /// Captura de una línea (solo abiertos): cantidad recibida, lote (clearLot lo quita), series y posición de recepción.
    /// Lote 13: productPublicId (solo líneas sin línea del aviso) y expectedQty / clearExpected (solo ciegos y devoluciones).
    /// Sincroniza el estatus: RECEIVING si lo recibido cuadra con lo esperado, DISCREPANCY si no.
    /// </summary>
    [HttpPut("{publicId:guid}/lines/{lineId:int}"), RequirePermission(PermissionCatalog.WarehouseReceive)]
    public Task<ReceiptDetailDto> UpdateLine(Guid publicId, int lineId, [FromBody] ReceiptLineUpdateRequest req, CancellationToken ct)
        => receipts.UpdateLineAsync(publicId, lineId, req, ct);

    /// <summary>
    /// Línea extra (solo abiertos, hasta 200 líneas). En un recibo de aviso de cliente el producto debe ser de ese cliente; de
    /// PO, propio. Lote 13: expectedQty solo en ciegos y devoluciones; sincroniza el estatus (EXPECTED → RECEIVING/DISCREPANCY).
    /// </summary>
    [HttpPost("{publicId:guid}/lines"), RequirePermission(PermissionCatalog.WarehouseReceive)]
    public Task<ReceiptDetailDto> AddLine(Guid publicId, [FromBody] ReceiptLineRequest req, CancellationToken ct)
        => receipts.AddLineAsync(publicId, req, ct);

    /// <summary>Quita una línea extra (solo abiertos). Las líneas del aviso no se quitan (409); con cruce de muelle asignado → 409.</summary>
    [HttpDelete("{publicId:guid}/lines/{lineId:int}"), RequirePermission(PermissionCatalog.WarehouseReceive)]
    public Task<ReceiptDetailDto> RemoveLine(Guid publicId, int lineId, CancellationToken ct)
        => receipts.RemoveLineAsync(publicId, lineId, ct);

    /// <summary>
    /// Confirma el recibo completo (D5): RECEIPT por lo esperado + ADJUSTMENT RECEIPT_VARIANCE por la diferencia (D4), PO a
    /// PARTIAL/RECEIVED, aviso a RECEIVED, reparto de cruce de muelle y PUTAWAY por el remanente. Segunda confirmación → 422.
    /// Lote 13: destino RECEIVED, o RECEIVED_VARIANCE si alguna línea tiene diferencia (en ciegos y devoluciones la
    /// diferencia solo marca el estatus: en el Kárdex entra lo recibido).
    /// </summary>
    [HttpPost("{publicId:guid}/confirm"), RequirePermission(PermissionCatalog.WarehouseReceive)]
    public Task<ReceiptDetailDto> Confirm(Guid publicId, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ReceiptConfirmRequest? req,
        CancellationToken ct)
        => receipts.ConfirmAsync(publicId, req, ct);

    /// <summary>Baja de un recibo abierto (EXPECTED, RECEIVING o DISCREPANCY) sin cruce de muelle (204). Su aviso de PO se cancela; el de cliente queda pendiente.</summary>
    [HttpDelete("{publicId:guid}"), RequirePermission(PermissionCatalog.WarehouseReceive)]
    public async Task<IActionResult> Delete(Guid publicId, CancellationToken ct)
    {
        await receipts.DeleteAsync(publicId, ct);
        return NoContent();
    }
}
