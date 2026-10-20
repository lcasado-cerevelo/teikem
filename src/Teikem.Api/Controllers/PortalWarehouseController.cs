using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Teikem.Api.Auth;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;

namespace Teikem.Api.Controllers;

/// <summary>
/// Lote 153 — portal del cliente de ALMACÉN (solo lectura): sus existencias, sus productos, sus movimientos y el valor de su inventario. Módulos CLIENT_PORTAL y WMS_LOTSERIAL de la compañía; la sesión del portal
/// (claims tid + cid) limita todo a su cliente. Una sesión del staff no entra (<c>PortalScopeMiddleware</c>) y el consignatario con acceso propio tampoco.
/// </summary>
[ApiController]
[Route("api/v1/portal/warehouse")]
[Authorize]
[RequireModule(ModuleKeys.ClientPortal)]
public sealed class PortalWarehouseController(PortalWarehouseService warehouse) : ControllerBase
{
    /// <summary>Mis existencias por producto, lote y almacén (sin posiciones). Filtros: buscador (SKU, nombre o código de barras) y almacén; máximo 100 por página.</summary>
    [HttpGet("stock"), RequireModule(ModuleKeys.WmsLotSerial)]
    public Task<PortalStockPageDto> Stock([FromQuery] string? search, [FromQuery] Guid? warehousePublicId, [FromQuery] int skip, [FromQuery] int take, CancellationToken ct)
        => warehouse.StockAsync(search, warehousePublicId, skip, take, ct);

    /// <summary>Mis productos con lo que hay en total. Filtros: buscador (SKU, nombre, código de barras o marca) e inactivos; máximo 100 por página.</summary>
    [HttpGet("products"), RequireModule(ModuleKeys.WmsLotSerial)]
    public Task<PortalProductPageDto> Products([FromQuery] string? search, [FromQuery] bool includeInactive, [FromQuery] int skip, [FromQuery] int take, CancellationToken ct)
        => warehouse.ProductsAsync(search, includeInactive, skip, take, ct);

    /// <summary>Mis movimientos de inventario (entradas, salidas, ajustes), los más nuevos primero. Filtros: desde y hasta (días de la compañía, inclusive) y buscador.</summary>
    [HttpGet("movements"), RequireModule(ModuleKeys.WmsLotSerial)]
    public Task<PortalMovementPageDto> Movements([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] string? search, [FromQuery] int skip, [FromQuery] int take, CancellationToken ct)
        => warehouse.MovementsAsync(from, to, search, skip, take, ct);

    /// <summary>El valor de mi inventario (valuación de la compañía: costo promedio o por capas), por producto y con totales; base del seguro. 403 si mi acceso no incluye ver el valor (solo lectura).</summary>
    [HttpGet("valuation"), RequireModule(ModuleKeys.WmsLotSerial)]
    public Task<ValuationReportDto> Valuation([FromQuery] string? search, [FromQuery] Guid? warehousePublicId, [FromQuery] int skip, [FromQuery] int take, CancellationToken ct)
        => warehouse.ValuationAsync(search, warehousePublicId, skip, take, ct);
}
