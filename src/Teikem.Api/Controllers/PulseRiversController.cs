using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Teikem.Api.Auth;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;

namespace Teikem.Api.Controllers;

/// <summary>
/// Lote 88 (P12) — los dos ríos del Pulso. Son datos de las órdenes (orders.view, módulo LTL_GROUND) y del COD (cod.view, módulo COD); el panel del Pulso pide además pulse.orders y pulse.cod. Fotos de
/// ahora: cada nodo cuenta lo que hay en ese punto del flujo (ver PulseRiverService).
/// </summary>
[ApiController]
[Route("api/v1/pulse/rivers")]
[Authorize]
public sealed class PulseRiversController(PulseRiverService rivers, PulseRadarService radar) : ControllerBase
{
    /// <summary>«Paquetes en la calle»: Entrada, En almacén, Por despachar (sin chofer), En ruta y Entregado hoy.</summary>
    [HttpGet("orders"), RequireModule(ModuleKeys.LtlGround), RequirePermission(PermissionCatalog.OrdersView)]
    public Task<PulseRiverDto> Orders(CancellationToken ct) => rivers.OrdersAsync(ct);

    /// <summary>«Dinero COD»: Por cobrar, Cobrado, Cuadrado y Remesa lista.</summary>
    [HttpGet("cod"), RequireModule(ModuleKeys.Cod), RequirePermission(PermissionCatalog.CodView)]
    public Task<PulseRiverDto> Cod(CancellationToken ct) => rivers.CodAsync(ct);

    /// <summary>Lote 89: Radar de órdenes abiertas — cuántas hay y los grupos con más órdenes, por pueblo (TOWN) o por cliente (CLIENT). Se busca una orden con el listado de Órdenes.</summary>
    [HttpGet("/api/v1/pulse/radar"), RequireModule(ModuleKeys.LtlGround), RequirePermission(PermissionCatalog.OrdersView)]
    public Task<PulseRadarDto> Radar([FromQuery] string? groupBy, [FromQuery] int take, CancellationToken ct) => radar.RadarAsync(groupBy, take, ct);
}
