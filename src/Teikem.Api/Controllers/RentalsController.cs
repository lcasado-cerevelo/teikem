using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Teikem.Api.Auth;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;

namespace Teikem.Api.Controllers;

/// <summary>
/// Lote 27 (Rentas R1) — rentas de equipos propios con número de serie (submódulo de Almacén, módulo RENTAL_EQUIPMENT "Rentas").
/// - rental.view: lista (status, clientPublicId, dueWithinDays, overdue, search), ficha y bitácora de extensiones.
/// - rental.manage: alta (Borrador, REN-#####), edición, equipos (agregar, quitar), tarifa por equipo, programar (reserva las
///   series), despachar (TRANSFER a EN-RENTA, serie ON_RENT) y cancelar (solo Borrador o Programada).
/// - rental.extend: extender la fecha de recogido (D4, sin aprobación de un segundo usuario).
/// La renta se expone por PublicId y sus equipos por id SOLO bajo su renta. Historial: /api/v1/status/history/RENTAL/{id}.
/// Devoluciones y procesos llegan en el bloque R2.
/// </summary>
[ApiController]
[Route("api/v1/rentals")]
[Authorize]
[RequireModule(ModuleKeys.RentalEquipment)]
public sealed class RentalsController(RentalService rentals) : ControllerBase
{
    /// <summary>
    /// Lista paginada (take ≤ 200), más recientes primero; con dueWithinDays u overdue, por fecha de recogido. Filtros: status
    /// (DRAFT, SCHEDULED, ON_RENT, RETURNED, CANCELLED), clientPublicId, dueWithinDays (abiertas que vencen de hoy a hoy + N),
    /// overdue (abiertas con el recogido antes de hoy) y search (número, contrato, cliente, localidad o serie).
    /// </summary>
    [HttpGet, RequirePermission(PermissionCatalog.RentalView)]
    public Task<RentalPageDto> List([FromQuery] string[]? status, [FromQuery] Guid? clientPublicId, [FromQuery] int? dueWithinDays,
        [FromQuery] bool overdue, [FromQuery] string? search, [FromQuery] int skip = 0, [FromQuery] int take = 100, CancellationToken ct = default)
        => rentals.ListAsync(new RentalQuery(status is { Length: > 0 } ? status : null, clientPublicId, dueWithinDays, overdue, search, skip, take), ct);

    [HttpGet("{publicId:guid}"), RequirePermission(PermissionCatalog.RentalView)]
    public Task<RentalDto> Get(Guid publicId, CancellationToken ct) => rentals.GetAsync(publicId, ct);

    /// <summary>Bitácora de extensiones (fecha anterior y nueva, motivo, quién y cuándo, tarifas nuevas).</summary>
    [HttpGet("{publicId:guid}/extensions"), RequirePermission(PermissionCatalog.RentalView)]
    public Task<IReadOnlyList<RentalExtensionDto>> Extensions(Guid publicId, CancellationToken ct) => rentals.ListExtensionsAsync(publicId, ct);

    /// <summary>Alta en Borrador: cliente activo, localidad del cliente, fechas, almacén de origen (o el único activo) y equipos opcionales.</summary>
    [HttpPost, RequirePermission(PermissionCatalog.RentalManage)]
    public Task<RentalDto> Create([FromBody] RentalCreateRequest req, CancellationToken ct) => rentals.CreateAsync(req, ct);

    /// <summary>Edición (Borrador o Programada): localidad, contacto, almacén (sin equipos), fechas (sin extensiones), contrato, transporte y notas.</summary>
    [HttpPatch("{publicId:guid}"), RequirePermission(PermissionCatalog.RentalManage)]
    public Task<RentalDto> Update(Guid publicId, [FromBody] RentalPatchRequest req, CancellationToken ct) => rentals.UpdateAsync(publicId, req, ct);

    /// <summary>Agrega equipos por serie (Borrador o Programada; en Programada los reserva).</summary>
    [HttpPost("{publicId:guid}/lines"), RequirePermission(PermissionCatalog.RentalManage)]
    public Task<RentalDto> AddLines(Guid publicId, [FromBody] RentalLinesAddRequest req, CancellationToken ct) => rentals.AddLinesAsync(publicId, req, ct);

    /// <summary>Quita un equipo (Borrador o Programada; en Programada libera su reserva). Devuelve la ficha.</summary>
    [HttpDelete("{publicId:guid}/lines/{lineId:int}"), RequirePermission(PermissionCatalog.RentalManage)]
    public Task<RentalDto> RemoveLine(Guid publicId, int lineId, CancellationToken ct) => rentals.RemoveLineAsync(publicId, lineId, ct);

    /// <summary>Tarifa vigente del equipo (antes del despacho): frecuencia, monto ≥ 0 y moneda. Solo se guarda (D3).</summary>
    [HttpPut("{publicId:guid}/lines/{lineId:int}/rate"), RequirePermission(PermissionCatalog.RentalManage)]
    public Task<RentalDto> SetLineRate(Guid publicId, int lineId, [FromBody] RentalLineRateRequest req, CancellationToken ct)
        => rentals.SetLineRateAsync(publicId, lineId, req, ct);

    /// <summary>Borrador → Programada: reserva las series (el disponible baja).</summary>
    [HttpPost("{publicId:guid}/schedule"), RequirePermission(PermissionCatalog.RentalManage)]
    public Task<RentalDto> Schedule(Guid publicId, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] RentalStatusRequest? req, CancellationToken ct)
        => rentals.ScheduleAsync(publicId, req, ct);

    /// <summary>Programada → En renta: los equipos pasan a la posición EN-RENTA (siguen en mano, reservados; serie ON_RENT).</summary>
    [HttpPost("{publicId:guid}/dispatch"), RequirePermission(PermissionCatalog.RentalManage)]
    public Task<RentalDto> Dispatch(Guid publicId, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] RentalStatusRequest? req, CancellationToken ct)
        => rentals.DispatchAsync(publicId, req, ct);

    /// <summary>Cancela (solo Borrador o Programada; libera las reservas). Después del despacho la renta termina con una devolución.</summary>
    [HttpPost("{publicId:guid}/cancel"), RequirePermission(PermissionCatalog.RentalManage)]
    public Task<RentalDto> Cancel(Guid publicId, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] RentalStatusRequest? req, CancellationToken ct)
        => rentals.CancelAsync(publicId, req, ct);

    /// <summary>Extiende la fecha de recogido (Programada o En renta) con motivo y, si cambian, tarifas nuevas por equipo.</summary>
    [HttpPost("{publicId:guid}/extensions"), RequirePermission(PermissionCatalog.RentalExtend)]
    public Task<RentalDto> Extend(Guid publicId, [FromBody] RentalExtendRequest req, CancellationToken ct) => rentals.ExtendAsync(publicId, req, ct);
}
