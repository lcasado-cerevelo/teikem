using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Teikem.Api.Auth;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;

namespace Teikem.Api.Controllers;

/// <summary>
/// 2026-10-11 — Despacho manual (DMA-#####): salida de inventario SIN entrega, con motivo obligatorio (catálogo
/// ManualIssueReason) y nota libre, para la web y el aparato. Controlador delgado sobre PickBatchService (misma salida que la
/// recolección: FEFO, series, un solo dueño, ISSUE, 409 insufficient_stock sin efecto parcial). Módulo WMS_LOTSERIAL como
/// /pick-batches. Crear, eliminar y leer los motivos con warehouse.issue; lista y ficha con inventory.view. Respeta
/// Idempotency-Key (middleware). El documento es una PICK_BATCH: su historial de estatus es /api/v1/status/history/PICK_BATCH/{id}
/// y también aparece en GET /api/v1/pick-batches (kind=MANUAL o ALL).
/// </summary>
[ApiController]
[Route("api/v1/manual-issues")]
[Authorize]
[RequireModule(ModuleKeys.WmsLotSerial)]
public sealed class ManualIssuesController(PickBatchService batches, LookupService catalogs) : ControllerBase
{
    /// <summary>Lista paginada de despachos manuales: mismos filtros que GET /api/v1/pick-batches con kind=MANUAL fijo.</summary>
    [HttpGet, RequirePermission(PermissionCatalog.InventoryView)]
    public Task<PickBatchPageDto> List([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] Guid[]? productPublicIds,
        [FromQuery] string[]? status, [FromQuery] string? search, [FromQuery] bool includeDeleted = false, [FromQuery] int skip = 0,
        [FromQuery] int take = 100, CancellationToken ct = default)
        => batches.ListAsync(new PickBatchQuery(from, to, productPublicIds is { Length: > 0 } ? productPublicIds : null,
            status is { Length: > 0 } ? status : null, null, null, search, includeDeleted, skip, take, "MANUAL"), ct);

    /// <summary>Motivos activos y habilitados para la compañía (etiqueta en el idioma del usuario, override aplicado).</summary>
    [HttpGet("reasons"), RequirePermission(PermissionCatalog.WarehouseIssue)]
    public Task<IReadOnlyList<LookupValueDto>> Reasons(CancellationToken ct)
        => catalogs.GetValuesAsync(LookupDomains.ManualIssueReason, false, ct);

    /// <summary>Ficha (también de uno eliminado). Un PublicId de una recolección EMP → 404 'Despacho manual no encontrado.'.</summary>
    [HttpGet("{publicId:guid}"), RequirePermission(PermissionCatalog.InventoryView)]
    public Task<PickBatchDto> Get(Guid publicId, CancellationToken ct) => batches.GetManualIssueAsync(publicId, ct);

    /// <summary>
    /// Despacha (DMA-#####, COLLECTED): productos activos de un solo dueño, posición y lote por FEFO o explícitos, series
    /// escaneadas; motivo obligatorio y nota ≤ 500. Saca el inventario con un ISSUE por porción (nota del Kárdex
    /// 'DMA-00012 · motivo'). Inventario insuficiente → 409 insufficient_stock sin efecto parcial (el número no se consume).
    /// </summary>
    [HttpPost, RequirePermission(PermissionCatalog.WarehouseIssue)]
    public Task<PickBatchDto> Create([FromBody] ManualIssueCreateRequest req, CancellationToken ct)
        => batches.ManualIssueAsync(req, ct);

    /// <summary>Elimina (204): restaura el inventario a la posición original y pasa a CANCELLED. Una recolección EMP → 404.</summary>
    [HttpDelete("{publicId:guid}"), RequirePermission(PermissionCatalog.WarehouseIssue)]
    public async Task<IActionResult> Delete(Guid publicId, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] PickBatchDeleteRequest? req,
        CancellationToken ct)
    {
        await batches.DeleteManualIssueAsync(publicId, req, ct);
        return NoContent();
    }
}
