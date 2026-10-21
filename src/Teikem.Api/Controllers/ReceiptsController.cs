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
public sealed class ReceiptsController(ReceiptService receipts, ReceiptGroupService groups) : ControllerBase
{
    /// <summary>
    /// Lista paginada (take ≤ 200) con filtros: warehousePublicId, status (EXPECTED, RECEIVING, DISCREPANCY, RECEIVED,
    /// RECEIVED_VARIANCE, PUTAWAY), types (ASN, BLIND, RETURN), from/to (fecha de alta en UTC, 'hasta' inclusive),
    /// productPublicIds, hasVariance, search (número, transporte, referencia del recibo o del aviso, cliente, orden de compra
    /// o proveedor) y, Lote 13, variance (SHORT, OVER, NONE; varias con O) y phase (OPEN, PENDING_PUTAWAY, DONE).
    /// includeLines = true (exportación de la lista con sus líneas): cada recibo de la página trae en lines TODAS sus líneas
    /// (las mismas de la ficha), leídas en lote para la página completa; por defecto false (lines = null).
    /// </summary>
    [HttpGet, RequirePermission(PermissionCatalog.InventoryView)]
    public Task<ReceiptPageDto> List([FromQuery] Guid? warehousePublicId, [FromQuery] string[]? status, [FromQuery] string[]? types,
        [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] Guid[]? productPublicIds, [FromQuery] bool? hasVariance,
        [FromQuery] string? search, [FromQuery] string[]? variance, [FromQuery] string? phase,
        [FromQuery] int skip = 0, [FromQuery] int take = 100, [FromQuery] bool includeLines = false, CancellationToken ct = default)
        => receipts.ListAsync(new ReceiptQuery(warehousePublicId, status is { Length: > 0 } ? status : null, types is { Length: > 0 } ? types : null,
            from, to, productPublicIds is { Length: > 0 } ? productPublicIds : null, hasVariance, search, skip, take,
            variance is { Length: > 0 } ? variance : null, phase, includeLines), ct);

    /// <summary>
    /// Resumen por almacén (2026-10-11): recibos abiertos (`openCount`) y por acomodar (`pendingPutawayCount`) de TODOS los almacenes de la compañía, también los inactivos, en una sola llamada
    /// (los mismos totales que `GET /receipts?phase=OPEN` y `?phase=PENDING_PUTAWAY` con `take=1`, almacén por almacén). Un almacén sin recibos sale con 0 y 0.
    /// </summary>
    [HttpGet("summary-by-warehouse"), RequirePermission(PermissionCatalog.InventoryView)]
    public Task<IReadOnlyList<ReceiptWarehouseSummaryDto>> SummaryByWarehouse(CancellationToken ct) => receipts.SummaryByWarehouseAsync(ct);

    // ---------------------------------------------------------------- Lote 182: recepción maestra

    /// <summary>
    /// Lote 182 (C4) — lista de recepciones maestras (varios documentos del mismo dueño que llegan juntos), de la más reciente a la más antigua. Filtros: search (número, nota, recibo, aviso u orden de compra de uno de sus
    /// documentos), status (OPEN, CONFIRMED, CANCELLED; otro valor → 400), skip y take (≤ 100).
    /// </summary>
    [HttpGet("groups"), RequirePermission(PermissionCatalog.InventoryView)]
    public Task<ReceiptGroupPageDto> Groups([FromQuery] string? search, [FromQuery] string? status, [FromQuery] int skip = 0, [FromQuery] int take = 50, CancellationToken ct = default)
        => groups.ListAsync(new ReceiptGroupQuery(search, status, skip, take), ct);

    /// <summary>Ficha de la maestra: sus recibos (uno por documento) con su avance y lo que se puede hacer.</summary>
    [HttpGet("groups/{publicId:guid}"), RequirePermission(PermissionCatalog.InventoryView)]
    public Task<ReceiptGroupDto> GetGroup(Guid publicId, CancellationToken ct) => groups.GetAsync(publicId, ct);

    /// <summary>
    /// Lote 182 — abre un recibo por cada documento (de 2 a 20 avisos de llegada y/o órdenes de compra DEL MISMO dueño y almacén) bajo una recepción maestra RCM-##### en UNA transacción: cada recibo es el de
    /// siempre (REC-#####); la maestra los reúne con el mismo transporte y referencia. Documentos de otro dueño o almacén → 400; un documento que no se puede recibir → el error de siempre de ese recibo, sin efecto parcial.
    /// </summary>
    [HttpPost("groups"), RequirePermission(PermissionCatalog.WarehouseReceive)]
    public Task<ReceiptGroupDto> CreateGroup([FromBody] ReceiptGroupCreateRequest req, CancellationToken ct) => groups.CreateAsync(req, ct);

    /// <summary>Confirma juntos todos los recibos de la maestra que aún no lo están, en una transacción (si uno falla, ninguno se confirma). Todos confirmados → 422.</summary>
    [HttpPost("groups/{publicId:guid}/confirm"), RequirePermission(PermissionCatalog.WarehouseReceive)]
    public Task<ReceiptGroupDto> ConfirmGroup(Guid publicId, [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] ReceiptGroupConfirmRequest? req, CancellationToken ct)
        => groups.ConfirmAsync(publicId, req, ct);

    /// <summary>Elimina la maestra (204) y sus recibos mientras ninguno esté confirmado (422 si alguno lo está).</summary>
    [HttpDelete("groups/{publicId:guid}"), RequirePermission(PermissionCatalog.WarehouseReceive)]
    public async Task<IActionResult> DeleteGroup(Guid publicId, CancellationToken ct)
    {
        await groups.DeleteAsync(publicId, ct);
        return NoContent();
    }

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
    /// Lote 16: receivingMode (PUTAWAY | DIRECT; sin él, el del almacén) y, por línea, targetBinId o targetBinCode (posición
    /// destino). Un recibo directo no exige posición de recepción; al confirmar cada línea entra a su destino sin tareas y el
    /// recibo pasa a Acomodado en el mismo momento. Con confirm = true, sin modo y sin destinos, un almacén directo recibe con
    /// acomodo (app anterior, D9).
    /// </summary>
    [HttpPost, RequirePermission(PermissionCatalog.WarehouseReceive)]
    public Task<ReceiptDetailDto> Create([FromBody] ReceiptCreateRequest req, CancellationToken ct) => receipts.CreateAsync(req, ct);

    /// <summary>
    /// Lote 13 — edita el encabezado de un recibo abierto (confirmado → 422; rowVersion distinto → 409): type BLIND ↔ RETURN
    /// (solo sin aviso ni orden de compra), warehousePublicId (solo sin documento y sin líneas; limpia posición y muelle),
    /// stagingBinId / clearStagingBin, dockId / clearDock, carrier y reference ('' = borrar; máximo 80). null = no cambiar.
    /// Lote 16: receivingMode cambia el modo SOLO de este recibo (pasar a PUTAWAY sin posición de recepción → 422).
    /// </summary>
    [HttpPatch("{publicId:guid}"), RequirePermission(PermissionCatalog.WarehouseReceive)]
    public Task<ReceiptDetailDto> UpdateHeader(Guid publicId, [FromBody] ReceiptHeaderUpdateRequest req, CancellationToken ct)
        => receipts.UpdateHeaderAsync(publicId, req, ct);

    /// <summary>
    /// Captura de una línea (solo abiertos): cantidad recibida, lote (clearLot lo quita), series y posición de recepción.
    /// Lote 13: productPublicId (solo líneas sin línea del aviso) y expectedQty / clearExpected (solo ciegos y devoluciones).
    /// Sincroniza el estatus: RECEIVING si lo recibido cuadra con lo esperado, DISCREPANCY si no.
    /// Lote 16: targetBinId fija la posición destino (de guardado, del almacén, activa) y clearTargetBin la quita.
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
    /// Lote 16: en un recibo directo cada línea que recibe algo exige su posición destino (400 lines[i].targetBinId) y entra
    /// ahí (RECEIPT y RECEIPT_VARIANCE en esa posición) sin tareas; el recibo pasa a PUTAWAY en la misma transacción. Una
    /// línea con cruce de muelle asignado entra a la de recepción y su remanente genera la tarea hacia su destino (D11).
    /// </summary>
    [HttpPost("{publicId:guid}/confirm"), RequirePermission(PermissionCatalog.WarehouseReceive)]
    public Task<ReceiptDetailDto> Confirm(Guid publicId, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ReceiptConfirmRequest? req,
        CancellationToken ct)
        => receipts.ConfirmAsync(publicId, req, ct);

    /// <summary>
    /// Lote 174 (C2) — confirma una TANDA: asienta lo recibido y aún no asentado de las líneas que pueden entrar (sin serie, sin daño declarado y sin
    /// cruce de muelle), crea su acomodo y deja el recibo abierto. Recibo directo o sin unidades nuevas → 422. La confirmación final cierra el resto.
    /// </summary>
    [HttpPost("{publicId:guid}/confirm-batch"), RequirePermission(PermissionCatalog.WarehouseReceive)]
    public Task<ReceiptDetailDto> ConfirmBatch(Guid publicId, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ReceiptConfirmRequest? req,
        CancellationToken ct)
        => receipts.ConfirmBatchAsync(publicId, req, ct);

    /// <summary>
    /// Lote 175 — «Recibir una parte» (cola del aparato): contra un aviso o una orden de compra suma lo capturado al recibo abierto del documento (o lo
    /// crea) y confirma una tanda; el recibo sigue abierto. ClientKey hace el reintento idempotente. Sin series ni daño (400). La confirmación final
    /// es POST /receipts con confirm = true: si el documento ya tiene tandas, cierra ese recibo.
    /// </summary>
    [HttpPost("part"), RequirePermission(PermissionCatalog.WarehouseReceive)]
    public Task<ReceiptDetailDto> ReceivePart([FromBody] ReceiptPartRequest req, CancellationToken ct)
        => receipts.ReceivePartAsync(req, ct);

    /// <summary>
    /// Lote 16 — posiciones destino sugeridas para una línea (take 1..10; por defecto 5): el acomodo dirigido (en devoluciones
    /// la cuarentena primero) con cupo, existencia, lo ya destinado por otras líneas del recibo, espacio libre y fits. Las que
    /// caben primero; las que exceden el cupo al final (fits = false). Línea de otro recibo → 404.
    /// </summary>
    [HttpGet("{publicId:guid}/lines/{lineId:int}/target-suggestions"), RequirePermission(PermissionCatalog.InventoryView)]
    public Task<IReadOnlyList<ReceiptTargetSuggestionDto>> TargetSuggestions(Guid publicId, int lineId, [FromQuery] int? take, CancellationToken ct)
        => receipts.SuggestTargetsAsync(publicId, lineId, take, ct);

    /// <summary>
    /// Lote 16 — "Usar posiciones sugeridas" (solo abiertos; rowVersion distinto → 409): a cada línea sin posición destino que
    /// recibe algo le asigna la primera sugerida donde cabe (en orden de línea, acumulando). Devuelve el recibo, cuántas se
    /// asignaron y cuántas quedaron sin sugerencia. Nada se llena solo: solo al pedirlo (D3).
    /// </summary>
    [HttpPost("{publicId:guid}/targets/suggest"), RequirePermission(PermissionCatalog.WarehouseReceive)]
    public Task<ReceiptApplySuggestionsResultDto> ApplySuggestedTargets(Guid publicId,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ReceiptApplySuggestionsRequest? req, CancellationToken ct)
        => receipts.ApplySuggestedTargetsAsync(publicId, req, ct);

    /// <summary>Baja de un recibo abierto (EXPECTED, RECEIVING o DISCREPANCY) sin cruce de muelle (204). Su aviso de PO se cancela; el de cliente queda pendiente.</summary>
    [HttpDelete("{publicId:guid}"), RequirePermission(PermissionCatalog.WarehouseReceive)]
    public async Task<IActionResult> Delete(Guid publicId, CancellationToken ct)
    {
        await receipts.DeleteAsync(publicId, ct);
        return NoContent();
    }
}
