using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Teikem.Api.Auth;
using Teikem.Domain.Constants;
using Teikem.Domain.Orders;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;

namespace Teikem.Api.Controllers;

/// <summary>
/// Lote 38 (F-B): plantillas de importación de consignatarios por posición de columna (Kind CONSIGNEE; generales del tenant
/// o de un cliente). Módulo CATALOG; leer exige locations.read y escribir locations.create. Una plantilla de órdenes pedida
/// aquí responde 404 y un kind distinto de CONSIGNEE en el cuerpo, 400. Baja lógica, nunca DELETE.
/// </summary>
[ApiController]
[Route("api/v1/locations/import-templates")]
[Authorize]
[RequireModule(ModuleKeys.Catalog)]
public sealed class ConsigneeImportTemplatesController(ImportTemplateService templates) : ControllerBase
{
    /// <summary>Plantillas de consignatarios. Con clientId: las generales y las de ese cliente.</summary>
    [HttpGet, RequirePermission(PermissionCatalog.LocationsRead)]
    public Task<IReadOnlyList<ImportTemplateDto>> List([FromQuery] Guid? clientId, [FromQuery] bool includeInactive, CancellationToken ct)
        => templates.GetListAsync(ImportKinds.Consignee, null, clientId, includeInactive, ct);

    [HttpGet("{publicId:guid}"), RequirePermission(PermissionCatalog.LocationsRead)]
    public Task<ImportTemplateDto> Get(Guid publicId, CancellationToken ct) => templates.GetAsync(publicId, ImportKinds.Consignee, ct);

    /// <summary>Alta: name y line1 en columna, city en columna o defaults; clientCode o clientName (no ambos). 400 con errores por columna.</summary>
    [HttpPost, RequirePermission(PermissionCatalog.LocationsCreate)]
    public Task<ImportTemplateDto> Create([FromBody] ImportTemplateCreateRequest req, CancellationToken ct)
        => templates.CreateAsync(req, ImportKinds.Consignee, ct);

    /// <summary>Edición parcial (columns/defaults reemplazan la lista completa).</summary>
    [HttpPatch("{publicId:guid}"), RequirePermission(PermissionCatalog.LocationsCreate)]
    public Task<ImportTemplateDto> Update(Guid publicId, [FromBody] ImportTemplatePatchRequest req, CancellationToken ct)
        => templates.UpdateAsync(publicId, req, ImportKinds.Consignee, ct);

    /// <summary>Baja lógica: sale del selector; los lotes ya validados conservan la referencia.</summary>
    [HttpPost("{publicId:guid}/deactivate"), RequirePermission(PermissionCatalog.LocationsCreate)]
    public Task<ImportTemplateDto> Deactivate(Guid publicId, CancellationToken ct) => templates.DeactivateAsync(publicId, ImportKinds.Consignee, ct);

    [HttpPost("{publicId:guid}/reactivate"), RequirePermission(PermissionCatalog.LocationsCreate)]
    public Task<ImportTemplateDto> Reactivate(Guid publicId, CancellationToken ct) => templates.ReactivateAsync(publicId, ImportKinds.Consignee, ct);
}
