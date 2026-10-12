using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Teikem.Api.Auth;
using Teikem.Domain.Constants;
using Teikem.Domain.Orders;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;

namespace Teikem.Api.Controllers;

/// <summary>
/// Lote 3 (P9): plantillas de importación de órdenes por posición de columna (reutilizables; generales del tenant o de un
/// cliente). Módulo núcleo LTL_GROUND; todas las acciones exigen orders.edit. Baja lógica, nunca DELETE.
/// Lote 38: solo atiende Kind ORDER: kind=CONSIGNEE responde 400 'Tipo de plantilla desconocido: CONSIGNEE.' y una plantilla
/// de consignatarios pedida aquí responde 404 (esas viven en /api/v1/locations/import-templates).
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
        => templates.GetListAsync(ImportKinds.Order, kind, clientId, includeInactive, ct);

    /// <summary>Alta: columnas por posición (únicas, &gt;= 1) y campos del catálogo; defaults por campo (p. ej. serviceType STANDARD). 400 con errores por columna.</summary>
    [HttpPost, RequirePermission(PermissionCatalog.OrdersEdit)]
    public Task<ImportTemplateDto> Create([FromBody] ImportTemplateCreateRequest req, CancellationToken ct)
        => templates.CreateAsync(req, ImportKinds.Order, ct);

    [HttpGet("{publicId:guid}"), RequirePermission(PermissionCatalog.OrdersEdit)]
    public Task<ImportTemplateDto> Get(Guid publicId, CancellationToken ct) => templates.GetAsync(publicId, ImportKinds.Order, ct);

    /// <summary>Edición parcial (columns/defaults reemplazan la lista completa).</summary>
    [HttpPatch("{publicId:guid}"), RequirePermission(PermissionCatalog.OrdersEdit)]
    public Task<ImportTemplateDto> Update(Guid publicId, [FromBody] ImportTemplatePatchRequest req, CancellationToken ct)
        => templates.UpdateAsync(publicId, req, ImportKinds.Order, ct);

    /// <summary>Baja lógica: sale del selector; los lotes ya validados conservan la referencia.</summary>
    [HttpPost("{publicId:guid}/deactivate"), RequirePermission(PermissionCatalog.OrdersEdit)]
    public Task<ImportTemplateDto> Deactivate(Guid publicId, CancellationToken ct) => templates.DeactivateAsync(publicId, ImportKinds.Order, ct);

    [HttpPost("{publicId:guid}/reactivate"), RequirePermission(PermissionCatalog.OrdersEdit)]
    public Task<ImportTemplateDto> Reactivate(Guid publicId, CancellationToken ct) => templates.ReactivateAsync(publicId, ImportKinds.Order, ct);
}
