using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Teikem.Api.Auth;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Fleet;
using Teikem.Infrastructure.Services;

namespace Teikem.Api.Controllers;

/// <summary>
/// Lote 4 (P3) — paneles transversales de flota: "Documentos por vencer" (vehículos + licencias + certificaciones) y
/// disponibilidad para despacho (R7). Módulo CATALOG; lectura con fleet.view. Todo bajo el tenant del principal.
/// </summary>
[ApiController]
[Route("api/v1/fleet")]
[Authorize]
[RequireModule(ModuleKeys.Catalog)]
public sealed class FleetController(FleetDocumentService documents, IFleetAvailabilityService availability) : ControllerBase
{
    /// <summary>
    /// Documentos vencidos y por vencer (vencimiento ≤ hoy + withinDays, 0..365), solo el vigente de cada tipo, de dueños
    /// activos. Filtros multi-valor: docType (REGISTRATION, INSURANCE, INSPECTION, PERMIT, LICENSE, CERTIFICATION) y
    /// entity (VEHICLE, DRIVER). includeExpired=false quita los ya vencidos. Orden: vencimiento y código del dueño.
    /// </summary>
    [HttpGet("expiring-documents"), RequirePermission(PermissionCatalog.FleetView)]
    public Task<IReadOnlyList<ExpiringDocumentDto>> ExpiringDocuments([FromQuery] int? withinDays, [FromQuery] string[]? docType,
        [FromQuery] string[]? entity, [FromQuery] bool? includeExpired, CancellationToken ct)
        => documents.GetExpiringAsync(new ExpiringDocumentsQuery(withinDays ?? 30, NullIfEmpty(docType), NullIfEmpty(entity), includeExpired ?? true), ct);

    /// <summary>
    /// Disponibilidad de choferes y vehículos en una fecha (hoy UTC por defecto) con los motivos bloqueantes y los avisos.
    /// onlyAvailable=true deja solo los disponibles.
    /// </summary>
    [HttpGet("availability"), RequirePermission(PermissionCatalog.FleetView)]
    public Task<FleetAvailabilityDto> Availability([FromQuery] DateOnly? date, [FromQuery] bool onlyAvailable, CancellationToken ct)
        => availability.GetAsync(date ?? DateOnly.FromDateTime(DateTime.UtcNow), onlyAvailable, ct);

    private static string[]? NullIfEmpty(string[]? values) => values is { Length: > 0 } ? values : null;
}
