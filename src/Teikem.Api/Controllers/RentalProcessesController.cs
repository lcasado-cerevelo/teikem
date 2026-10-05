using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Teikem.Api.Auth;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;

namespace Teikem.Api.Controllers;

/// <summary>
/// Lote 28 (Rentas R2) — proceso configurable de los equipos devueltos (módulo RENTAL_EQUIPMENT "Rentas").
/// - rental.view: cola de procesos (/api/v1/rental-processes).
/// - rental.maintenance: avanzar a cualquier estatus habilitado, terminar (Lista, con traslado opcional) y dar de baja. Dar de baja
///   (y avanzar a SCRAPPED) exige ADEMÁS inventory.adjust, que verifica el servicio (un solo [RequirePermission] por acción).
/// El proceso se identifica por id. Historial: /api/v1/status/history/RENTAL_PROCESS/{id}.
/// </summary>
[ApiController]
[Route("api/v1/rental-processes")]
[Authorize]
[RequireModule(ModuleKeys.RentalEquipment)]
public sealed class RentalProcessesController(RentalProcessService processes) : ControllerBase
{
    /// <summary>
    /// Cola paginada (take ≤ 200): abiertos primero (los más viejos arriba), luego terminados. Filtros: status (uno o varios), open,
    /// warehousePublicId, productPublicId y search (serie, SKU, devolución o renta).
    /// </summary>
    [HttpGet, RequirePermission(PermissionCatalog.RentalView)]
    public Task<RentalProcessPageDto> List([FromQuery] string[]? status, [FromQuery] bool? open, [FromQuery] Guid? warehousePublicId,
        [FromQuery] Guid? productPublicId, [FromQuery] string? search, [FromQuery] int skip = 0, [FromQuery] int take = 100, CancellationToken ct = default)
        => processes.ListAsync(new RentalProcessQuery(status is { Length: > 0 } ? status : null, open, warehousePublicId, productPublicId, search, skip, take), ct);

    /// <summary>Pasa el proceso a otro estatus habilitado (siguiente paso, Reparación, Esperando piezas…); a SCRAPPED también pide inventory.adjust.</summary>
    [HttpPost("{id:int}/advance"), RequirePermission(PermissionCatalog.RentalMaintenance)]
    public Task<RentalProcessDto> Advance(int id, [FromBody] RentalProcessAdvanceRequest req, CancellationToken ct) => processes.AdvanceAsync(id, req, ct);

    /// <summary>Termina el proceso (Lista): traslado opcional a otra posición del almacén y el equipo vuelve a estar disponible.</summary>
    [HttpPost("{id:int}/complete"), RequirePermission(PermissionCatalog.RentalMaintenance)]
    public Task<RentalProcessDto> Complete(int id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] RentalProcessCompleteRequest? req, CancellationToken ct)
        => processes.CompleteAsync(id, req, ct);

    /// <summary>Da de baja el equipo (ajuste de salida con motivo DAMAGE; la serie queda dada de baja). Pide también inventory.adjust.</summary>
    [HttpPost("{id:int}/scrap"), RequirePermission(PermissionCatalog.RentalMaintenance)]
    public Task<RentalProcessDto> Scrap(int id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] RentalStatusRequest? req, CancellationToken ct)
        => processes.ScrapAsync(id, req, ct);
}
