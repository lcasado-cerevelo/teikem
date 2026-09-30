using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Teikem.Api.Auth;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;

namespace Teikem.Api.Controllers;

/// <summary>
/// Lote 6 (P1) — Almacenes y ubicaciones (R2, R3, R6) bajo el módulo WMS_LOTSERIAL. Lectura (GET) con inventory.view; todo lo
/// demás (alta, edición, baja, zonas, posiciones, muelles, estatus de muelle y des/reactivación) con warehouse.manage.
/// El almacén se expone por PublicId; zona, posición y muelle (sin TenantId) por id entero SOLO bajo su almacén: un id de
/// otro almacén u otro tenant es 404. Historial de estatus: /api/v1/status/history/WAREHOUSE/{id} y WAREHOUSE_DOCK/{id}.
/// </summary>
[ApiController]
[Route("api/v1/warehouses")]
[Authorize]
[RequireModule(ModuleKeys.WmsLotSerial)]
public sealed class WarehousesController(WarehouseService warehouses, WarehouseLayoutService layout) : ControllerBase
{
    // ---------------------------------------------------------------- almacenes

    /// <summary>Almacenes del tenant (por defecto solo activos) con conteos de zonas, posiciones, muelles y existencia.</summary>
    [HttpGet, RequirePermission(PermissionCatalog.InventoryView)]
    public Task<IReadOnlyList<WarehouseDto>> List([FromQuery] bool includeInactive, CancellationToken ct)
        => warehouses.ListAsync(includeInactive, ct);

    /// <summary>Alta: código único e inmutable; país por defecto 'PR'; nace ACTIVE con historial.</summary>
    [HttpPost, RequirePermission(PermissionCatalog.WarehouseManage)]
    public Task<WarehouseDetailDto> Create([FromBody] WarehouseCreateRequest req, CancellationToken ct) => warehouses.CreateAsync(req, ct);

    [HttpGet("{publicId:guid}"), RequirePermission(PermissionCatalog.InventoryView)]
    public Task<WarehouseDetailDto> Get(Guid publicId, CancellationToken ct) => warehouses.GetAsync(publicId, ct);

    /// <summary>Edición en línea: null = sin cambio, "" = quitar; rowVersion opcional (409 si cambió). code → 400.</summary>
    [HttpPatch("{publicId:guid}"), RequirePermission(PermissionCatalog.WarehouseManage)]
    public Task<WarehouseDetailDto> Update(Guid publicId, [FromBody] WarehousePatchRequest req, CancellationToken ct)
        => warehouses.UpdateAsync(publicId, req, ct);

    /// <summary>Baja definitiva (INACTIVE terminal): solo vacío y sin documentos abiertos (409 con Errors por tipo).</summary>
    [HttpPost("{publicId:guid}/deactivate"), RequirePermission(PermissionCatalog.WarehouseManage)]
    public Task<WarehouseDetailDto> Deactivate(Guid publicId,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] WarehouseDeactivateRequest? req, CancellationToken ct)
        => warehouses.DeactivateAsync(publicId, req, ct);

    // ---------------------------------------------------------------- zonas

    [HttpGet("{publicId:guid}/zones"), RequirePermission(PermissionCatalog.InventoryView)]
    public Task<IReadOnlyList<WarehouseZoneDto>> Zones(Guid publicId, [FromQuery] bool includeInactive, CancellationToken ct)
        => layout.ListZonesAsync(publicId, includeInactive, ct);

    [HttpPost("{publicId:guid}/zones"), RequirePermission(PermissionCatalog.WarehouseManage)]
    public Task<WarehouseZoneDto> CreateZone(Guid publicId, [FromBody] WarehouseZoneRequest req, CancellationToken ct)
        => layout.CreateZoneAsync(publicId, req, ct);

    [HttpPatch("{publicId:guid}/zones/{zoneId:int}"), RequirePermission(PermissionCatalog.WarehouseManage)]
    public Task<WarehouseZoneDto> UpdateZone(Guid publicId, int zoneId, [FromBody] WarehouseZonePatchRequest req, CancellationToken ct)
        => layout.UpdateZoneAsync(publicId, zoneId, req, ct);

    [HttpPost("{publicId:guid}/zones/{zoneId:int}/deactivate"), RequirePermission(PermissionCatalog.WarehouseManage)]
    public Task<WarehouseZoneDto> DeactivateZone(Guid publicId, int zoneId, CancellationToken ct)
        => layout.SetZoneActiveAsync(publicId, zoneId, false, ct);

    [HttpPost("{publicId:guid}/zones/{zoneId:int}/reactivate"), RequirePermission(PermissionCatalog.WarehouseManage)]
    public Task<WarehouseZoneDto> ReactivateZone(Guid publicId, int zoneId, CancellationToken ct)
        => layout.SetZoneActiveAsync(publicId, zoneId, true, ct);

    // ---------------------------------------------------------------- posiciones

    /// <summary>
    /// Posiciones del almacén, paginadas en el servidor ({ total, skip, take, items }; take ≤ 200, por defecto 100), por código.
    /// Filtros: ?search (contiene en código, zona, pasillo, rack, nivel o posición), ?zoneId (404 si no es del almacén),
    /// ?zoneIds (varias), ?aisle, ?rack, ?level, ?position (contiene), ?productPublicIds (con existencia de esos productos),
    /// ?occupancy (EMPTY, PARTIAL, FULL, NO_CAPACITY; varias, 400 si otro), ?binIds, ?includeInactive, ?onlyWithStock.
    /// </summary>
    [HttpGet("{publicId:guid}/bins"), RequirePermission(PermissionCatalog.InventoryView)]
    public Task<WarehouseBinPageDto> Bins(Guid publicId, [FromQuery] int? zoneId, [FromQuery] string? search,
        [FromQuery] bool includeInactive, [FromQuery] bool onlyWithStock, [FromQuery] int[]? zoneIds, [FromQuery] string? aisle,
        [FromQuery] string? rack, [FromQuery] string? level, [FromQuery] string? position, [FromQuery] Guid[]? productPublicIds,
        [FromQuery] string[]? occupancy, [FromQuery] int[]? binIds, CancellationToken ct, [FromQuery] int skip = 0, [FromQuery] int take = 100)
        => layout.ListBinsAsync(publicId, new WarehouseBinQuery(zoneId, search, includeInactive, onlyWithStock, NullIfEmpty(zoneIds), aisle, rack,
            level, position, NullIfEmpty(productPublicIds), NullIfEmpty(occupancy), NullIfEmpty(binIds), skip, take), ct);

    private static T[]? NullIfEmpty<T>(T[]? values) => values is { Length: > 0 } ? values : null;

    /// <summary>Alta de posición: código explícito o compuesto pasillo-rack-nivel-posición, único por almacén (409).</summary>
    [HttpPost("{publicId:guid}/bins"), RequirePermission(PermissionCatalog.WarehouseManage)]
    public Task<WarehouseBinDto> CreateBin(Guid publicId, [FromBody] WarehouseBinRequest req, CancellationToken ct)
        => layout.CreateBinAsync(publicId, req, ct);

    /// <summary>
    /// Lote 11 — cupo máximo en bloque: { maxCapacityQty } o { clear: true } (exactamente uno) sobre TODAS las posiciones que
    /// cumplen los filtros (zoneIds, aisle, rack, level, position, search, binIds, includeInactive, onlyWithoutCapacity), en una
    /// transacción. Sin filtros de posiciones exige allBins: true. Devuelve { matched, changed }. Para saber de antemano cuántas
    /// se afectarán: GET .../bins?take=1 con los mismos filtros (Total).
    /// </summary>
    [HttpPost("{publicId:guid}/bins/capacity"), RequirePermission(PermissionCatalog.WarehouseManage)]
    public Task<WarehouseBinCapacityResultDto> SetBinsCapacity(Guid publicId, [FromBody] WarehouseBinCapacityRequest req, CancellationToken ct)
        => layout.SetBinsCapacityAsync(publicId, req, ct);

    [HttpPatch("{publicId:guid}/bins/{binId:int}"), RequirePermission(PermissionCatalog.WarehouseManage)]
    public Task<WarehouseBinDto> UpdateBin(Guid publicId, int binId, [FromBody] WarehouseBinPatchRequest req, CancellationToken ct)
        => layout.UpdateBinAsync(publicId, binId, req, ct);

    /// <summary>Baja de posición: 409 con inventario (en mano o reservado) o con tareas abiertas que la usan.</summary>
    [HttpPost("{publicId:guid}/bins/{binId:int}/deactivate"), RequirePermission(PermissionCatalog.WarehouseManage)]
    public Task<WarehouseBinDto> DeactivateBin(Guid publicId, int binId, CancellationToken ct)
        => layout.SetBinActiveAsync(publicId, binId, false, ct);

    [HttpPost("{publicId:guid}/bins/{binId:int}/reactivate"), RequirePermission(PermissionCatalog.WarehouseManage)]
    public Task<WarehouseBinDto> ReactivateBin(Guid publicId, int binId, CancellationToken ct)
        => layout.SetBinActiveAsync(publicId, binId, true, ct);

    // ---------------------------------------------------------------- muelles

    [HttpGet("{publicId:guid}/docks"), RequirePermission(PermissionCatalog.InventoryView)]
    public Task<IReadOnlyList<WarehouseDockDto>> Docks(Guid publicId, [FromQuery] bool includeInactive, CancellationToken ct)
        => layout.ListDocksAsync(publicId, includeInactive, ct);

    /// <summary>Alta de muelle (INBOUND, OUTBOUND o BOTH); nace FREE con historial WAREHOUSE_DOCK.</summary>
    [HttpPost("{publicId:guid}/docks"), RequirePermission(PermissionCatalog.WarehouseManage)]
    public Task<WarehouseDockDto> CreateDock(Guid publicId, [FromBody] WarehouseDockRequest req, CancellationToken ct)
        => layout.CreateDockAsync(publicId, req, ct);

    [HttpPatch("{publicId:guid}/docks/{dockId:int}"), RequirePermission(PermissionCatalog.WarehouseManage)]
    public Task<WarehouseDockDto> UpdateDock(Guid publicId, int dockId, [FromBody] WarehouseDockPatchRequest req, CancellationToken ct)
        => layout.UpdateDockAsync(publicId, dockId, req, ct);

    /// <summary>Estatus manual del muelle (FREE, OCCUPIED o MAINTENANCE) vía StatusService, con comentario.</summary>
    [HttpPost("{publicId:guid}/docks/{dockId:int}/status"), RequirePermission(PermissionCatalog.WarehouseManage)]
    public Task<WarehouseDockDto> SetDockStatus(Guid publicId, int dockId, [FromBody] WarehouseDockStatusRequest req, CancellationToken ct)
        => layout.SetDockStatusAsync(publicId, dockId, req, ct);

    /// <summary>Baja de muelle: 409 con citas SCHEDULED/ARRIVED.</summary>
    [HttpPost("{publicId:guid}/docks/{dockId:int}/deactivate"), RequirePermission(PermissionCatalog.WarehouseManage)]
    public Task<WarehouseDockDto> DeactivateDock(Guid publicId, int dockId, CancellationToken ct)
        => layout.SetDockActiveAsync(publicId, dockId, false, ct);

    [HttpPost("{publicId:guid}/docks/{dockId:int}/reactivate"), RequirePermission(PermissionCatalog.WarehouseManage)]
    public Task<WarehouseDockDto> ReactivateDock(Guid publicId, int dockId, CancellationToken ct)
        => layout.SetDockActiveAsync(publicId, dockId, true, ct);
}
