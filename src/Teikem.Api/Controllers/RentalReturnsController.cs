using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Teikem.Api.Auth;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;

namespace Teikem.Api.Controllers;

/// <summary>
/// Lote 28 (Rentas R2) — devoluciones de renta DRN-##### (módulo RENTAL_EQUIPMENT "Rentas").
/// - rental.return: registrar la devolución de una renta En renta (POST /api/v1/rentals/{publicId}/returns): equipos por número de
///   serie, motivo, condición, posición de destino y si pasan por proceso.
/// - rental.view: lista (/api/v1/rental-returns) y ficha (/api/v1/rental-returns/{publicId}).
/// Historial y auditoría: tipo RENTAL_RETURN; el cambio de la renta a Devuelta queda en /status/history/RENTAL/{id}.
/// </summary>
[ApiController]
[Route("api/v1")]
[Authorize]
[RequireModule(ModuleKeys.RentalEquipment)]
public sealed class RentalReturnsController(RentalReturnService returns) : ControllerBase
{
    /// <summary>
    /// Registra la devolución (total o parcial, anticipada o al término): por cada serie, TRANSFER desde EN-RENTA a la posición
    /// destino; con proceso queda reservada y En proceso; sin proceso, Disponible. Si vuelven todos los equipos la renta pasa a Devuelta.
    /// </summary>
    [HttpPost("rentals/{publicId:guid}/returns"), RequirePermission(PermissionCatalog.RentalReturn)]
    public Task<RentalReturnDto> Create(Guid publicId, [FromBody] RentalReturnCreateRequest req, CancellationToken ct) => returns.CreateAsync(publicId, req, ct);

    /// <summary>
    /// Lista paginada (take ≤ 200), más recientes primero. Filtros: rentalPublicId, clientPublicId, reason (END_OF_CONTRACT,
    /// EARLY_DAMAGE, EARLY_CLIENT, OTHER), from/to (fecha de devolución), early (antes del recogido) y search (número, renta o serie).
    /// </summary>
    [HttpGet("rental-returns"), RequirePermission(PermissionCatalog.RentalView)]
    public Task<RentalReturnPageDto> List([FromQuery] Guid? rentalPublicId, [FromQuery] Guid? clientPublicId, [FromQuery] string[]? reason,
        [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] bool? early, [FromQuery] string? search, [FromQuery] int skip = 0,
        [FromQuery] int take = 100, CancellationToken ct = default)
        => returns.ListAsync(new RentalReturnQuery(rentalPublicId, clientPublicId, reason is { Length: > 0 } ? reason : null, from, to, early, search, skip, take), ct);

    [HttpGet("rental-returns/{publicId:guid}"), RequirePermission(PermissionCatalog.RentalView)]
    public Task<RentalReturnDto> Get(Guid publicId, CancellationToken ct) => returns.GetAsync(publicId, ct);
}
