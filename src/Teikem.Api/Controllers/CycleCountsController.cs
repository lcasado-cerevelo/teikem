using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Teikem.Api.Auth;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;

namespace Teikem.Api.Controllers;

/// <summary>
/// Lote 6 (P6) — Conteo cíclico en modo informado (R16, R33; D22) bajo el módulo WMS_LOTSERIAL. La lista se consulta con
/// inventory.view; refrescar, RECONCILIAR y la baja requieren warehouse.count (el Operador de almacén lo tiene: reconcilia,
/// como el mock). La ficha se lee con inventory.view: completa (foto del sistema y series esperadas) con warehouse.count y a
/// ciegas sin él (Lote 8A).
/// Lote 8A — conteo a ciegas: el alta, la captura (por línea, en lote o agregando lo encontrado) y terminar requieren
/// warehouse.count.capture (quien tiene warehouse.count lo tiene implícito, PermissionCatalog.Implied); sin warehouse.count
/// la respuesta de esas acciones también llega a ciegas (isBlind = true, cantidades esperadas en null) y la reconciliación
/// queda para quien tiene warehouse.count (en la web).
/// El conteo se expone por id entero (no tiene PublicId) filtrado por tenant; sus líneas SOLO dentro de su conteo.
/// Historial de estatus: /api/v1/status/history/CYCLE_COUNT/{id}. La tarea COUNT de la cola se completa aquí (reconciliar).
/// Lote 21 — conteo por producto: crear con productPublicIds (origen PRODUCT), evidencia de captura y corrección en cada línea,
/// vista previa de la reconciliación, lista "Por revisar", cierre en bloque de los que cuadran y posición provisional.
/// </summary>
[ApiController]
[Route("api/v1/cycle-counts")]
[Authorize]
[RequireModule(ModuleKeys.WmsLotSerial)]
public sealed class CycleCountsController(CycleCountService counts, PermissionService permissions, WarehouseLayoutService layout) : ControllerBase
{
    /// <summary>
    /// Conteos activos (los 200 más recientes) con filtros: warehousePublicIds (selección múltiple, maestro L553), status (OPEN, COUNTED, RECONCILED,
    /// RECONCILED_VARIANCE), from/to (fecha de alta en días locales de la compañía, 'hasta' inclusive), binIds, productPublicIds, categoryIds (con subcategorías) y search
    /// (número del conteo, SKU o nombre de producto). Conteo a ciegas (Lote 8A): sin warehouse.count, varianceLines y
    /// netVariance llegan null.
    /// </summary>
    [HttpGet, RequirePermission(PermissionCatalog.InventoryView)]
    public async Task<IReadOnlyList<CycleCountDto>> List([FromQuery] Guid[]? warehousePublicIds, [FromQuery] string[]? status,
        [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] int[]? binIds, [FromQuery] Guid[]? productPublicIds,
        [FromQuery] int[]? categoryIds, [FromQuery] string? search, CancellationToken ct = default)
        => await counts.ListAsync(new CycleCountQuery(warehousePublicIds is { Length: > 0 } ? warehousePublicIds : null, status is { Length: > 0 } ? status : null, from, to,
            binIds is { Length: > 0 } ? binIds : null, productPublicIds is { Length: > 0 } ? productPublicIds : null,
            categoryIds is { Length: > 0 } ? categoryIds : null, search), await IsBlindAsync(ct), ct);

    /// <summary>
    /// Lote 14 (hallazgo 14) — página de conteos con el total: mismos filtros que la lista (from/to en días locales de la
    /// compañía), más zoneIds (alguna línea en esas zonas), origins (MANUAL, CHANGES, PRODUCT) y skip/take (take 1..200, por defecto
    /// 50). Cada fila trae binCount (y binCode/zoneCode si es de una posición), originCode/origin y su ventana, taskId de la
    /// tarea COUNT (para asignarla con POST /warehouse-tasks/{taskId}/assign) y assignedToName. Status acepta OPEN, COUNTED,
    /// RECONCILED y RECONCILED_VARIANCE. A ciegas sin warehouse.count (varianceLines y netVariance en null).
    /// </summary>
    [HttpGet("page"), RequirePermission(PermissionCatalog.InventoryView)]
    public async Task<CycleCountPageDto> Page([FromQuery] Guid[]? warehousePublicIds, [FromQuery] string[]? status,
        [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] int[]? binIds, [FromQuery] int[]? zoneIds,
        [FromQuery] Guid[]? productPublicIds, [FromQuery] int[]? categoryIds, [FromQuery] string[]? origins, [FromQuery] string? search,
        [FromQuery] int skip = 0, [FromQuery] int take = 50, CancellationToken ct = default)
        => await counts.ListPageAsync(new CycleCountQuery(warehousePublicIds is { Length: > 0 } ? warehousePublicIds : null,
            status is { Length: > 0 } ? status : null, from, to, binIds is { Length: > 0 } ? binIds : null,
            productPublicIds is { Length: > 0 } ? productPublicIds : null, categoryIds is { Length: > 0 } ? categoryIds : null, search,
            zoneIds is { Length: > 0 } ? zoneIds : null, origins is { Length: > 0 } ? origins : null, skip, take), await IsBlindAsync(ct), ct);

    /// <summary>
    /// Lote 14 (D2, D3, D4) — vista previa de "Conteo de lo cambiado" (warehouse.count): ventana efectiva (por defecto desde la
    /// última generación del almacén o, la primera vez, desde las 00:00 de hoy en hora de Puerto Rico), movimientos, conteos
    /// que se crearían (uno por posición), posiciones saltadas (con conteo pendiente, inactivas, vacías) y problem = el 400 que
    /// daría el alta. fromUtc &gt; toUtc o más de 31 días → 400; almacén inactivo → 422; zona de otro almacén → 404.
    /// </summary>
    [HttpGet("changes-preview"), RequirePermission(PermissionCatalog.WarehouseCount)]
    public Task<CycleCountChangesPreviewDto> ChangesPreview([FromQuery] Guid? warehousePublicId, [FromQuery] DateTime? fromUtc,
        [FromQuery] DateTime? toUtc, [FromQuery] int[]? zoneIds, [FromQuery] bool includeEmpty = true, CancellationToken ct = default)
        => counts.PreviewChangesAsync(new CycleCountFromChangesRequest(warehousePublicId, fromUtc, toUtc,
            zoneIds is { Length: > 0 } ? zoneIds : null, includeEmpty), ct);

    /// <summary>
    /// Lote 14 (D2, D3, D4) — "Conteo de lo cambiado" (warehouse.count): un conteo Pendiente por posición con movimientos en la
    /// ventana (sin los de un conteo), con todo lo que tiene y las claves que quedaron en 0 (includeEmpty), cada uno con su
    /// tarea COUNT; salta posiciones inactivas o con un conteo pendiente; tope 200. Todo o nada. 400 'filters' si no hay
    /// posiciones que contar, si todas tienen conteo pendiente o si son más de 200.
    /// </summary>
    [HttpPost("from-changes"), RequirePermission(PermissionCatalog.WarehouseCount)]
    public Task<CycleCountBatchResultDto> FromChanges([FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] CycleCountFromChangesRequest? req, CancellationToken ct)
        => counts.CreateFromChangesAsync(req, ct);

    /// <summary>
    /// Ficha en modo informado (con warehouse.count): foto (systemQty), contado, diferencia contra la foto, series esperadas
    /// y contadas, saldo actual (currentQty) con isStale, y tras reconciliar reconciledSystemQty, systemQtyChanged, adjustedQty
    /// y el movimiento de ajuste. Filtros de líneas: binIds, productPublicIds, categoryIds, onlyVariance, onlyPending, search.
    /// Lote 8A — conteo a ciegas: con solo inventory.view (sin warehouse.count) la ficha llega con isBlind = true y las
    /// cantidades esperadas de las líneas en null (systemQty, varianceQty, currentQty, reconciledSystemQty, adjustedQty;
    /// expectedSerials vacío), count.varianceLines y count.netVariance en null; onlyVariance se ignora.
    /// </summary>
    [HttpGet("{id:int}"), RequirePermission(PermissionCatalog.InventoryView)]
    public async Task<CycleCountDetailDto> Get(int id, [FromQuery] int[]? binIds, [FromQuery] Guid[]? productPublicIds,
        [FromQuery] int[]? categoryIds, [FromQuery] bool? onlyVariance, [FromQuery] bool? onlyPending, [FromQuery] string? search,
        CancellationToken ct = default)
    {
        var blind = await IsBlindAsync(ct);
        return await counts.GetAsync(id, new CycleCountLinesQuery(binIds is { Length: > 0 } ? binIds : null,
            productPublicIds is { Length: > 0 } ? productPublicIds : null, categoryIds is { Length: > 0 } ? categoryIds : null,
            onlyVariance, onlyPending, search), blind, ct);
    }

    /// <summary>
    /// Alta OPEN con número CC-#####: una línea por saldo en mano del almacén (warehousePublicId o el único activo) según
    /// zoneIds, binIds, productPublicIds y categoryIds; foto del sistema en systemQty y una tarea COUNT en la cola. Más de
    /// 1000 líneas → 400.
    /// </summary>
    [HttpPost, RequirePermission(PermissionCatalog.WarehouseCountCapture)]
    public async Task<CycleCountDetailDto> Create([FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] CycleCountCreateRequest? req, CancellationToken ct)
        => await ForCallerAsync(await counts.CreateAsync(req, ct), ct);

    /// <summary>
    /// Captura por línea: countedQty (NONE/LOT) o serialNumbers (SERIAL). Sin ninguno la captura se borra. Reconciliado → 422.
    /// Lote 21: la primera captura fija capturedQty/capturedBy/capturedAt; quien la capturó puede recapturar mientras el conteo
    /// está Pendiente; cualquier edición de otro usuario, o cualquiera con el conteo ya Contado, es una CORRECCIÓN (countedQty
    /// cambia, la captura original se conserva y se llena correctedBy/correctedAt; si vuelve al valor capturado se limpia).
    /// Una línea ya corregida solo la vuelve a cambiar quien la corrigió o quien tiene warehouse.count; otro → 409
    /// 'La línea ya fue corregida por el supervisor; no se puede volver a capturar.' (todo o nada).
    /// </summary>
    [HttpPut("{id:int}/lines"), RequirePermission(PermissionCatalog.WarehouseCountCapture)]
    public async Task<CycleCountDetailDto> Capture(int id, [FromBody] CountCaptureRequest req, CancellationToken ct)
        => await ForCallerAsync(await counts.CaptureAsync(id, req, ct), ct);

    /// <summary>
    /// Lote 8A — captura en lote (cola del aparato): varias líneas en una llamada, cada una por lineId o por binId +
    /// productPublicId (+ lotId o lot); la que no está en el conteo se agrega con su captura (lo encontrado). Todo o nada:
    /// errores por renglón → 400 ('lines[i].campo'); renglón repetido → 400 'La línea se repite en la solicitud.';
    /// reconciliado → 422. Respeta Idempotency-Key.
    /// </summary>
    [HttpPut("{id:int}/lines/batch"), RequirePermission(PermissionCatalog.WarehouseCountCapture)]
    public async Task<CycleCountDetailDto> CaptureBatch(int id, [FromBody] CountBatchRequest req, CancellationToken ct)
        => await ForCallerAsync(await counts.CaptureBatchAsync(id, req, ct), ct);

    /// <summary>Línea agregada a mano (lo encontrado): posición del almacén del conteo, producto, lote y captura. Repetida → 409.</summary>
    [HttpPost("{id:int}/lines"), RequirePermission(PermissionCatalog.WarehouseCountCapture)]
    public async Task<CycleCountDetailDto> AddLine(int id, [FromBody] CountAddLineRequest req, CancellationToken ct)
        => await ForCallerAsync(await counts.AddLineAsync(id, req, ct), ct);

    /// <summary>Terminar de contar: OPEN → COUNTED con todas las líneas capturadas (si no, 422 'Faltan {n} línea(s) por contar.').</summary>
    [HttpPost("{id:int}/finish"), RequirePermission(PermissionCatalog.WarehouseCountCapture)]
    public async Task<CycleCountDetailDto> Finish(int id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] CountReconcileRequest? req, CancellationToken ct)
        => await ForCallerAsync(await counts.FinishAsync(id, req, ct), ct);

    /// <summary>
    /// Lote 21 — vista previa de reconciliar (warehouse.count), SIN escribir: por línea la posición, zona, producto, lote,
    /// existencia ACTUAL, reservado, contado (con quién contó y quién corrigió), ajuste que se asentaría, saldo resultante y el
    /// error que daría (contado menor que lo reservado); en serie el plan (bajas, altas, traslados). Totales: líneas, pendientes
    /// (dato, no error), con diferencia, movimientos, con error y matches (cuadra: se puede cerrar en bloque). Misma regla que
    /// POST .../reconcile. Reconciliado → 422; otro tenant → 404.
    /// </summary>
    [HttpGet("{id:int}/reconcile-preview"), RequirePermission(PermissionCatalog.WarehouseCount)]
    public Task<ReconcilePreviewDto> ReconcilePreview(int id, CancellationToken ct) => counts.PreviewReconcileAsync(id, ct);

    /// <summary>
    /// Lote 21 — lista "Por revisar" (warehouse.count): conteos Contados (con includeOpen también los Pendientes con todas sus
    /// líneas capturadas), más recientes primero, con quién contó, primer producto y cuántos más, posiciones, líneas, cuántas
    /// difieren contra la existencia actual, correcciones y matches. Filtros: warehousePublicId, countedByUserId, search
    /// (número, SKU o nombre), skip y take (1..200, por defecto 50). Calculada en lotes, sin una consulta por conteo.
    /// </summary>
    [HttpGet("review"), RequirePermission(PermissionCatalog.WarehouseCount)]
    public Task<CycleCountReviewPageDto> Review([FromQuery] Guid? warehousePublicId, [FromQuery] int? countedByUserId,
        [FromQuery] string? search, [FromQuery] bool includeOpen = false, [FromQuery] int skip = 0, [FromQuery] int take = 50, CancellationToken ct = default)
        => counts.ReviewAsync(new CycleCountReviewQuery(warehousePublicId, countedByUserId, search, includeOpen, skip, take), ct);

    /// <summary>
    /// Lote 21 — cierra en bloque los conteos que CUADRAN (warehouse.count): { warehousePublicId?, ids?, comment?, includeOpen? }.
    /// Cuadra = contra la existencia ACTUAL no asentaría ningún movimiento ni tiene errores; esos terminan en Concordancia, cada
    /// uno en su propia transacción. Responde { examined, closed[], skipped[], truncated }; cada omitido trae reasonCode
    /// (WouldPost, Errors, Pending, Stale, NotCounted, AlreadyReconciled, NotFound, NoLines, Failed), reason y count. Hasta 200
    /// conteos por llamada (más ids → 400).
    /// </summary>
    [HttpPost("reconcile-matching"), RequirePermission(PermissionCatalog.WarehouseCount)]
    public Task<CycleCountReconcileMatchingResultDto> ReconcileMatching(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] CountReconcileMatchingRequest? req, CancellationToken ct)
        => counts.ReconcileMatchingAsync(req, ct);

    /// <summary>
    /// Lote 21 — posición provisional (warehouse.count.capture): quien cuenta halló producto donde el sistema no tenía nada y la
    /// posición no existe. { zoneId, code } (o aisle/rack/level/position para componer el código) en una zona del almacén del
    /// conteo; reutiliza las validaciones del alta de posición (código repetido en el almacén → 409; zona inactiva → 422; zona
    /// de otro almacén → 404; conteo reconciliado → 422). La posición nace isProvisional y se usa de inmediato como línea nueva
    /// (PUT .../lines/batch con binId); el supervisor la confirma en POST /warehouses/{publicId}/bins/{binId}/confirm-provisional.
    /// </summary>
    [HttpPost("{id:int}/bins"), RequirePermission(PermissionCatalog.WarehouseCountCapture)]
    public Task<WarehouseBinDto> CreateProvisionalBin(int id, [FromBody] CountProvisionalBinRequest req, CancellationToken ct)
        => layout.CreateProvisionalBinAsync(id, new WarehouseBinRequest(req?.ZoneId, req?.Code, req?.Aisle, req?.Rack, req?.Level, req?.Position), ct);

    /// <summary>Refrescar (opcional): las líneas con foto vieja toman el saldo actual y pierden su captura para recontarlas.</summary>
    [HttpPost("{id:int}/refresh"), RequirePermission(PermissionCatalog.WarehouseCount)]
    public Task<CycleCountDetailDto> Refresh(int id, CancellationToken ct) => counts.RefreshAsync(id, ct);

    /// <summary>
    /// Reconciliar (D22) — "Confirmar conteo y ajustar" (Lote 14, D8): desde Pendiente o Contado, en un paso. Ajusta
    /// CONTADO − SALDO ACTUAL por línea (ADJUSTMENT COUNT_VARIANCE; en serie bajas, altas y TRANSFER), marca systemQtyChanged
    /// si el saldo se movió desde la foto, pasa a RECONCILED_VARIANCE 'Diferencia' (si asentó algún movimiento) o RECONCILED
    /// 'Concordancia' y cierra la tarea COUNT. Contado menor que lo reservado → 409 sin cambios; segunda reconciliación → 422.
    /// </summary>
    [HttpPost("{id:int}/reconcile"), RequirePermission(PermissionCatalog.WarehouseCount)]
    public Task<CycleCountDetailDto> Reconcile(int id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] CountReconcileRequest? req, CancellationToken ct)
        => counts.ReconcileAsync(id, req, ct);

    /// <summary>Baja de un conteo OPEN (204); su tarea COUNT se cancela. Terminado o reconciliado → 422.</summary>
    [HttpDelete("{id:int}"), RequirePermission(PermissionCatalog.WarehouseCount)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await counts.DeleteAsync(id, ct);
        return NoContent();
    }

    /// <summary>Conteo a ciegas para quien consulta: sin warehouse.count (Lote 8A).</summary>
    private async Task<bool> IsBlindAsync(CancellationToken ct) => !await permissions.HasPermissionAsync(PermissionCatalog.WarehouseCount, ct);

    /// <summary>La respuesta de alta, captura y terminar llega a ciegas a quien no tiene warehouse.count (no filtra lo esperado).</summary>
    private async Task<CycleCountDetailDto> ForCallerAsync(CycleCountDetailDto detail, CancellationToken ct)
        => await IsBlindAsync(ct) ? CycleCountService.Blind(detail) : detail;
}
