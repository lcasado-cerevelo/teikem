using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Teikem.Api.Auth;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;

namespace Teikem.Api.Controllers;

/// <summary>
/// Lote 245 (Lote B del plan de rutas, cadenas y precios) — «Análisis de ventas › Por cadena» (módulo Órdenes de venta), con sales.analysis.view o
/// sales.analysis.manage. Mismo prefijo que <see cref="SalesAnalysisController"/> sin tocarlo (by-chain no choca con sus rutas). Es una vista más: «Por ruta» sigue
/// siendo la de por omisión. El TenantId sale de la sesión; una cadena de otra compañía da 404. Meses «AAAA-MM».
/// </summary>
[ApiController]
[Route("api/v1/sales-analysis")]
[Authorize]
[RequireModule(ModuleKeys.SalesOrders)]
public sealed class SalesAnalysisChainController(ClientChainAnalysisService analysis) : ControllerBase
{
    private const string View = PermissionCatalog.SalesAnalysisView + "|" + PermissionCatalog.SalesAnalysisManage;

    /// <summary>
    /// Vista «Por cadena»: month (por omisión el último mes completo) contra compareMonth (por omisión el anterior). Filas por cadena (activas, e inactivas con venta)
    /// con su desglose por cliente, «Sin cadena», totales y conciliación. Con chainPublicId: solo esa cadena (sin «Sin cadena»; la conciliación es del mes completo).
    /// </summary>
    [HttpGet("by-chain"), RequirePermission(View)]
    [ProducesResponseType(typeof(ClientChainSalesDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<ClientChainSalesDto> ByChain([FromQuery] string? month, [FromQuery] string? compareMonth, [FromQuery] Guid? chainPublicId, CancellationToken ct)
        => analysis.GetSalesByChainAsync(month, compareMonth, chainPublicId, ct);

    /// <summary>
    /// Venta neta por mes de los clientes de una cadena (chainPublicId, obligatorio) de fromMonth a toMonth (por omisión los 12 meses que terminan en el último mes
    /// completo; a lo sumo 36) y sus 5 clientes principales con su participación.
    /// </summary>
    [HttpGet("by-chain/series"), RequirePermission(View)]
    [ProducesResponseType(typeof(ClientChainSeriesDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<ClientChainSeriesDto> Series([FromQuery] Guid? chainPublicId, [FromQuery] string? fromMonth, [FromQuery] string? toMonth, CancellationToken ct)
        => analysis.GetChainSeriesAsync(chainPublicId, fromMonth, toMonth, ct);
}
