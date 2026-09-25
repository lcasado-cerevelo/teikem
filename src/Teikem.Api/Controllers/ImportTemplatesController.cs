using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Teikem.Api.Auth;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;

namespace Teikem.Api.Controllers;

/// <summary>
/// Lote 3 (P9): plantillas de importación de órdenes por posición de columna (reutilizables; generales del tenant o de un
/// cliente). Módulo núcleo LTL_GROUND; todas las acciones exigen orders.edit. Baja lógica, nunca DELETE.
/// </summary>
[ApiController]
[Route("api/v1/import-templates")]
[Authorize]
[RequireModule(ModuleKeys.LtlGround)]
public sealed class ImportTemplatesController(ImportTemplateService templates) : ControllerBase
{
    /// <summary>Plantillas del tenant por tipo (kind=ORDER por defecto). Con clientId: las generales y las de ese cliente.</summary>
    [HttpGet, RequirePermission(PermissionCatalog.OrdersEdit)]
    public Task<IReadOnlyList<ImportTemplateDto>> List([FromQuery] string? kind, [FromQuery] Guid? clientId, [FromQuery] bool includeInactive, CancellationToken ct)
        => templates.GetListAsync(kind, clientId, includeInactive, ct);

    /// <summary>Alta: columnas por posición (únicas, &gt;= 1) y campos del catálogo; defaults por campo (p. ej. serviceType STANDARD). 400 con errores por columna.</summary>
    [HttpPost, RequirePermission(PermissionCatalog.OrdersEdit)]
    public Task<ImportTemplateDto> Create([FromBody] ImportTemplateCreateRequest req, CancellationToken ct)
        => templates.CreateAsync(req, ct);

    [HttpGet("{publicId:guid}"), RequirePermission(PermissionCatalog.OrdersEdit)]
    public Task<ImportTemplateDto> Get(Guid publicId, CancellationToken ct) => templates.GetAsync(publicId, ct);

    /// <summary>Edición parcial (columns/defaults reemplazan la lista completa).</summary>
    [HttpPatch("{publicId:guid}"), RequirePermission(PermissionCatalog.OrdersEdit)]
    public Task<ImportTemplateDto> Update(Guid publicId, [FromBody] ImportTemplatePatchRequest req, CancellationToken ct)
        => templates.UpdateAsync(publicId, req, ct);

    /// <summary>Baja lógica: sale del selector; los lotes ya validados conservan la referencia.</summary>
    [HttpPost("{publicId:guid}/deactivate"), RequirePermission(PermissionCatalog.OrdersEdit)]
    public Task<ImportTemplateDto> Deactivate(Guid publicId, CancellationToken ct) => templates.DeactivateAsync(publicId, ct);

    [HttpPost("{publicId:guid}/reactivate"), RequirePermission(PermissionCatalog.OrdersEdit)]
    public Task<ImportTemplateDto> Reactivate(Guid publicId, CancellationToken ct) => templates.ReactivateAsync(publicId, ct);
}
