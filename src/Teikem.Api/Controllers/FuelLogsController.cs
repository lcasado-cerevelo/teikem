using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Teikem.Api.Auth;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;

namespace Teikem.Api.Controllers;

/// <summary>
/// Lote 4 (P5) — bitácora de combustible (panel del módulo Flota). Vive bajo el módulo CATALOG; lectura con fleet.view y
/// escritura con fleet.maintenance. km/L y costo/km se calculan al leer sobre la serie activa completa de cada vehículo.
/// Las cargas no tienen PublicId: se exponen con su id entero (FuelLog lleva TenantId: una carga de otro tenant es 404).
/// Contactos y campos personalizados: FUEL_LOG (Lote 1).
/// </summary>
[ApiController]
[Route("api/v1/fuel-logs")]
[Authorize]
[RequireModule(ModuleKeys.Catalog)]
public sealed class FuelLogsController(FuelLogService fuel) : ControllerBase
{
    /// <summary>Lista paginada (más recientes primero) con resumen por vehículo. Rango: fromUtc inclusivo, toUtc exclusivo.</summary>
    [HttpGet, RequirePermission(PermissionCatalog.FleetView)]
    public Task<FuelLogPageDto> List([FromQuery] Guid? vehiclePublicId, [FromQuery] Guid? driverPublicId, [FromQuery] DateTime? fromUtc,
        [FromQuery] DateTime? toUtc, [FromQuery] bool includeInactive, CancellationToken ct,
        [FromQuery] int skip = 0, [FromQuery] int take = FuelLogService.DefaultTake)
        => fuel.ListAsync(new FuelLogQuery(vehiclePublicId, driverPublicId, fromUtc, toUtc, includeInactive, skip, take), ct);

    /// <summary>Alta de una carga: sube el odómetro del vehículo si la lectura es mayor (nunca lo baja).</summary>
    [HttpPost, RequirePermission(PermissionCatalog.FleetMaintenance)]
    public Task<FuelLogDto> Create([FromBody] FuelLogCreateRequest req, CancellationToken ct) => fuel.CreateAsync(req, ct);

    /// <summary>Corrección: null = sin cambio; clearOdometer/clearDriver quitan el valor. El vehículo no se cambia.</summary>
    [HttpPatch("{id:int}"), RequirePermission(PermissionCatalog.FleetMaintenance)]
    public Task<FuelLogDto> Update(int id, [FromBody] FuelLogPatchRequest req, CancellationToken ct) => fuel.UpdateAsync(id, req, ct);

    /// <summary>Baja lógica (IsActive = 0): deja de contar en la eficiencia; el odómetro del vehículo no baja. Nunca DELETE.</summary>
    [HttpPost("{id:int}/deactivate"), RequirePermission(PermissionCatalog.FleetMaintenance)]
    public Task<FuelLogDto> Deactivate(int id, CancellationToken ct) => fuel.DeactivateAsync(id, ct);
}
