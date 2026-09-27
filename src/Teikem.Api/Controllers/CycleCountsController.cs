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
/// inventory.view; la ficha (que muestra la foto del sistema y las series esperadas), el alta, la captura, terminar,
/// refrescar, RECONCILIAR y la baja requieren warehouse.count (el Operador de almacén lo tiene: reconcilia, como el mock).
/// El conteo se expone por id entero (no tiene PublicId) filtrado por tenant; sus líneas SOLO dentro de su conteo.
/// Historial de estatus: /api/v1/status/history/CYCLE_COUNT/{id}. La tarea COUNT de la cola se completa aquí (reconciliar).
/// </summary>
[ApiController]
[Route("api/v1/cycle-counts")]
[Authorize]
[RequireModule(ModuleKeys.WmsLotSerial)]
public sealed class CycleCountsController(CycleCountService counts) : ControllerBase
{
    /// <summary>
    /// Conteos activos (los 200 más recientes) con filtros: warehousePublicIds (selección múltiple, maestro L553), status (OPEN, COUNTED, RECONCILED), from/to
    /// (fecha de alta en UTC, 'hasta' inclusive), binIds, productPublicIds, categoryIds (con subcategorías) y search
    /// (número del conteo, SKU o nombre de producto).
    /// </summary>
    [HttpGet, RequirePermission(PermissionCatalog.InventoryView)]
    public Task<IReadOnlyList<CycleCountDto>> List([FromQuery] Guid[]? warehousePublicIds, [FromQuery] string[]? status,
        [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] int[]? binIds, [FromQuery] Guid[]? productPublicIds,
        [FromQuery] int[]? categoryIds, [FromQuery] string? search, CancellationToken ct = default)
        => counts.ListAsync(new CycleCountQuery(warehousePublicIds is { Length: > 0 } ? warehousePublicIds : null, status is { Length: > 0 } ? status : null, from, to,
            binIds is { Length: > 0 } ? binIds : null, productPublicIds is { Length: > 0 } ? productPublicIds : null,
            categoryIds is { Length: > 0 } ? categoryIds : null, search), ct);

    /// <summary>
    /// Ficha en modo informado: foto (systemQty), contado, diferencia contra la foto, series esperadas y contadas, saldo
    /// actual (currentQty) con isStale, y tras reconciliar reconciledSystemQty, systemQtyChanged, adjustedQty y el
    /// movimiento de ajuste. Filtros de líneas: binIds, productPublicIds, categoryIds, onlyVariance, onlyPending, search.
    /// </summary>
    [HttpGet("{id:int}"), RequirePermission(PermissionCatalog.WarehouseCount)]
    public Task<CycleCountDetailDto> Get(int id, [FromQuery] int[]? binIds, [FromQuery] Guid[]? productPublicIds,
        [FromQuery] int[]? categoryIds, [FromQuery] bool? onlyVariance, [FromQuery] bool? onlyPending, [FromQuery] string? search,
        CancellationToken ct = default)
        => counts.GetAsync(id, new CycleCountLinesQuery(binIds is { Length: > 0 } ? binIds : null,
            productPublicIds is { Length: > 0 } ? productPublicIds : null, categoryIds is { Length: > 0 } ? categoryIds : null,
            onlyVariance, onlyPending, search), ct);

    /// <summary>
    /// Alta OPEN con número CC-#####: una línea por saldo en mano del almacén (warehousePublicId o el único activo) según
    /// zoneIds, binIds, productPublicIds y categoryIds; foto del sistema en systemQty y una tarea COUNT en la cola. Más de
    /// 1000 líneas → 400.
    /// </summary>
    [HttpPost, RequirePermission(PermissionCatalog.WarehouseCount)]
    public Task<CycleCountDetailDto> Create([FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] CycleCountCreateRequest? req, CancellationToken ct)
        => counts.CreateAsync(req, ct);

    /// <summary>Captura por línea: countedQty (NONE/LOT) o serialNumbers (SERIAL). Sin ninguno la captura se borra. Reconciliado → 422.</summary>
    [HttpPut("{id:int}/lines"), RequirePermission(PermissionCatalog.WarehouseCount)]
    public Task<CycleCountDetailDto> Capture(int id, [FromBody] CountCaptureRequest req, CancellationToken ct)
        => counts.CaptureAsync(id, req, ct);

    /// <summary>Línea agregada a mano (lo encontrado): posición del almacén del conteo, producto, lote y captura. Repetida → 409.</summary>
    [HttpPost("{id:int}/lines"), RequirePermission(PermissionCatalog.WarehouseCount)]
    public Task<CycleCountDetailDto> AddLine(int id, [FromBody] CountAddLineRequest req, CancellationToken ct)
        => counts.AddLineAsync(id, req, ct);

    /// <summary>Terminar de contar: OPEN → COUNTED con todas las líneas capturadas (si no, 422 'Faltan {n} línea(s) por contar.').</summary>
    [HttpPost("{id:int}/finish"), RequirePermission(PermissionCatalog.WarehouseCount)]
    public Task<CycleCountDetailDto> Finish(int id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] CountReconcileRequest? req, CancellationToken ct)
        => counts.FinishAsync(id, req, ct);

    /// <summary>Refrescar (opcional): las líneas con foto vieja toman el saldo actual y pierden su captura para recontarlas.</summary>
    [HttpPost("{id:int}/refresh"), RequirePermission(PermissionCatalog.WarehouseCount)]
    public Task<CycleCountDetailDto> Refresh(int id, CancellationToken ct) => counts.RefreshAsync(id, ct);

    /// <summary>
    /// Reconciliar (D22): ajusta CONTADO − SALDO ACTUAL por línea (ADJUSTMENT COUNT_VARIANCE; en serie bajas, altas y
    /// TRANSFER), marca systemQtyChanged si el saldo se movió desde la foto, pasa a RECONCILED y cierra la tarea COUNT.
    /// Contado menor que lo reservado → 409 sin cambios; segunda reconciliación → 422.
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
}
